#!/usr/bin/env bash
# Fixture for scripts/coord-board-archive.py (.github#2420).
#
# This gate ARCHIVES ROWS OFF THE SHARED BOARD, so the property that matters is not "does it archive"
# — it is "does it REFUSE to archive everything it must refuse". Every guard therefore gets a negative
# case that proves the guard can say NO, and each is written so that DELETING the guard turns the leg
# red rather than leaving it quietly green.
#
# The planner is a pure function precisely so this fixture needs no board, no token and no network:
# the guards are the whole risk surface, and they are exercised directly rather than through a mock
# that could agree with itself.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SCRIPT="$ROOT/scripts/coord-board-archive.py"

pass=0
fail=0
ok() { echo "PASS  $1"; pass=$((pass + 1)); }
bad() { echo "FAIL  $1"; printf '%s\n' "${2:-}" | sed 's/^/    | /'; fail=$((fail + 1)); }

[ -f "$SCRIPT" ] || { echo "FAIL  the script under test is missing: $SCRIPT"; exit 1; }

run_case() {
  # run_case <name> <python-expression-returning-(archived_refs, skip_reasons)>
  python3 - "$SCRIPT" <<'PY'
import datetime as dt, importlib.util, json, sys

spec = importlib.util.spec_from_file_location("cba", sys.argv[1])
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

NOW = dt.datetime(2026, 8, 12, tzinfo=dt.timezone.utc)
OLD = "2026-01-01T00:00:00Z"     # comfortably outside any retention window
RECENT = "2026-08-10T00:00:00Z"  # two days ago

def row(**kw):
    base = dict(itemId="PVTI_x", status="Done", state="CLOSED", closedAt=OLD,
                number=1, repo="FS-GG/.github", blockedBy=None)
    base.update(kw)
    return base

def plan(rows, days=30):
    a, s = mod.plan(rows, NOW, days, "FS-GG/.github")
    return [(r["repo"], r["number"]) for r in a], [why for _, why in s]

results = {}

# 1. The happy path must actually archive, or every negative below is vacuous.
arch, _ = plan([row(number=100)])
results["archives a long-closed Done row"] = (arch == [("FS-GG/.github", 100)], arch)

# 2. GUARD 1a — a live row is never archived, whatever else is true of it.
arch, _ = plan([row(number=101, status="Ready"), row(number=102, status="In progress"),
                row(number=103, status="Blocked"), row(number=104, status="In review")])
results["never archives a non-Done row"] = (arch == [], arch)

# 3. GUARD 1b — `Done` over an OPEN issue is drift `lint` must keep reporting, not something to hide.
arch, _ = plan([row(number=105, state="OPEN")])
results["never archives Done-over-OPEN (the drift lint reports)"] = (arch == [], arch)

# 4. GUARD 2 — inside the retention window, a finished row stays visible.
arch, _ = plan([row(number=106, closedAt=RECENT)])
results["never archives inside the retention window"] = (arch == [], arch)

# ...and the window is a real boundary, not a constant: widen it and the same row survives, narrow it
# and it goes. A guard that ignored --retention-days would pass one of these and fail the other.
arch_wide, _ = plan([row(number=107, closedAt=RECENT)], days=30)
arch_narrow, _ = plan([row(number=107, closedAt=RECENT)], days=1)
results["retention-days actually moves the boundary"] = (
    arch_wide == [] and arch_narrow == [("FS-GG/.github", 107)], (arch_wide, arch_narrow))

# 5. GUARD 3 — a row a LIVE row still names as its blocker is protected. Off-board, that edge would
#    render BlockerUnknown (Protocol.fs:406): "closed, so clear" would become "cannot tell".
rows = [row(number=200), row(number=201, status="Ready", closedAt=None, state="OPEN",
                             blockedBy="FS-GG/.github#200")]
arch, _ = plan(rows)
results["never archives a live row's blocker (qualified ref)"] = (arch == [], arch)

# ...including a BARE `#200`, which is how the board writes a same-repo edge.
rows = [row(number=202), row(number=203, status="Ready", closedAt=None, state="OPEN",
                             blockedBy="blocked on #202 landing first")]
arch, _ = plan(rows)
results["never archives a live row's blocker (bare ref)"] = (arch == [], arch)

# ...but a blocker named only by an ALREADY-DONE row is not protective: a finished row cannot strand
# anything, and treating it as protective would pin the whole historical graph on-board forever.
rows = [row(number=204), row(number=205, blockedBy="FS-GG/.github#204")]
arch, _ = plan(rows)
results["a Done row's blocker edge does NOT protect"] = (
    sorted(arch) == [("FS-GG/.github", 204), ("FS-GG/.github", 205)], arch)

# 6. GUARD 4 — unreadable is never archived. "I could not look" is not "I looked" (#266).
arch, _ = plan([row(number=None), row(repo=None, number=300), row(number=301, status=None),
                row(number=302, state=None), row(number=303, closedAt=None),
                row(number=304, closedAt="not-a-date")])
results["never archives an unreadable row"] = (arch == [], arch)

# 7. Every skip is REASONED. A silent skip and a silent archive must not look alike to an operator.
_, reasons = plan([row(number=400, status="Ready"), row(number=401, state="OPEN"),
                   row(number=402, closedAt=RECENT), row(number=403, closedAt=None)])
results["every skip carries a reason"] = (all(r and r.strip() for r in reasons), reasons)

print(json.dumps({k: [bool(v[0]), repr(v[1])] for k, v in results.items()}))
PY
}

OUT="$(run_case)" || { echo "FAIL  the planner raised"; exit 1; }

while IFS= read -r line; do
  name="${line%%$'\t'*}"
  rest="${line#*$'\t'}"
  verdict="${rest%%$'\t'*}"
  detail="${rest#*$'\t'}"
  if [ "$verdict" = "True" ]; then ok "$name"; else bad "$name" "$detail"; fi
done < <(printf '%s' "$OUT" | python3 -c '
import json,sys
for k,(v,d) in json.load(sys.stdin).items():
    print(f"{k}\t{v}\t{d}")
')

echo
echo "coord-board-archive fixture: $pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
