#!/usr/bin/env bash
# Fixture for scripts/coord-board-archive.py (.github#2420, count bound .github#3052).
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
import contextlib, datetime as dt, importlib.util, io, json, sys

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

def plan(rows, days=30, max_visible=100):
    a, s = mod.plan(rows, NOW, days, max_visible)
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

# 5b. GUARD 3, CROSS-REPO — the round-1 review finding, as its own permanent leg.
#     `Blockers.canonToken` (Blockers.fs:126-149) resolves a bare `#n` against the REFERRING ROW'S OWN
#     repo. The first version of this script resolved it against one global default, so on a genuinely
#     multi-repo board `FS-GG/FS.GG.SDD#9 blocked by #8` protected `FS-GG/.github#8` and left the REAL
#     `FS-GG/FS.GG.SDD#8` archivable. The fixture never varied `repo` across rows, so it sailed through.
rows = [row(number=8, repo="FS-GG/FS.GG.SDD"),
        row(number=9, repo="FS-GG/FS.GG.SDD", status="Ready", state="OPEN", closedAt=None,
            blockedBy="#8")]
arch, _ = plan(rows)
results["a bare #n resolves against the REFERRING row's repo, not a global default"] = (arch == [], arch)

#     ...and the same bare `#8` must NOT protect a same-numbered row in a DIFFERENT repo, or the guard
#     becomes an indiscriminate number-blocker that pins unrelated rows forever.
rows = [row(number=8, repo="FS-GG/.github"),
        row(number=9, repo="FS-GG/FS.GG.SDD", status="Ready", state="OPEN", closedAt=None,
            blockedBy="#8")]
arch, _ = plan(rows)
results["a bare #n does NOT protect the same number in another repo"] = (
    arch == [("FS-GG/.github", 8)], arch)

# 5c. The other two canonical forms the engine accepts, which the first version did not parse at all.
rows = [row(number=10, repo="FS-GG/FS.GG.SDD"),
        row(number=11, repo="FS-GG/.github", status="Ready", state="OPEN", closedAt=None,
            blockedBy="https://github.com/FS-GG/FS.GG.SDD/issues/10")]
arch, _ = plan(rows)
results["a URL issue ref protects"] = (arch == [], arch)

rows = [row(number=12, repo="FS-GG/FS.GG.SDD"),
        row(number=13, repo="FS-GG/.github", status="Ready", state="OPEN", closedAt=None,
            blockedBy="FS.GG.SDD#12")]
arch, _ = plan(rows)
results["a repo#n ref protects, owner defaulting to the referring row's"] = (arch == [], arch)

# 5d. FAIL-CLOSED — a bare `#n` on a row whose own repo is unreadable cannot be resolved to one
#     repository, so it protects that number EVERYWHERE rather than being guessed into one.
rows = [row(number=14, repo="FS-GG/FS.GG.SDD"), row(number=14, repo="FS-GG/.github"),
        row(number=15, repo=None, status="Ready", state="OPEN", closedAt=None, blockedBy="#14")]
arch, _ = plan(rows)
results["an unresolvable bare #n protects that number in EVERY repo"] = (arch == [], arch)

# 3b. GUARD 1b, PULL REQUESTS — `MERGED` is a PR's terminal state and GraphQL reports it instead of
#     `CLOSED`. Round-2 review observation: 8 of 36 post-sweep rows were merged PRs that could never
#     be archived — a permanent, growing floor. Accepting it is SAFER than what is already allowed: a
#     merged PR cannot be reopened, while a closed issue can.
arch, _ = plan([row(number=108, state="MERGED")])
results["archives a long-merged PR row"] = (arch == [("FS-GG/.github", 108)], arch)

#     ...but a merged PR is still subject to every other guard — retention here.
arch, _ = plan([row(number=109, state="MERGED", closedAt=RECENT)])
results["a MERGED row still obeys the retention window"] = (arch == [], arch)

#     ...and no OTHER state is terminal, however plausible it looks.
arch, _ = plan([row(number=110, state="OPEN"), row(number=111, state="DRAFT"),
                row(number=112, state="")])
results["no state other than CLOSED/MERGED is terminal"] = (arch == [], arch)

# 6. GUARD 4 — unreadable is never archived. "I could not look" is not "I looked" (#266).
arch, _ = plan([row(number=None), row(repo=None, number=300), row(number=301, status=None),
                row(number=302, state=None), row(number=303, closedAt=None),
                row(number=304, closedAt="not-a-date")])
results["never archives an unreadable row"] = (arch == [], arch)

# 7. Every skip is REASONED. A silent skip and a silent archive must not look alike to an operator.
_, reasons = plan([row(number=400, status="Ready"), row(number=401, state="OPEN"),
                   row(number=402, closedAt=RECENT), row(number=403, closedAt=None)])
results["every skip carries a reason"] = (all(r and r.strip() for r in reasons), reasons)

# 8. BELOW THE BOUND, retention remains the policy: count pressure must not make a fresh row leave.
arch, _ = plan([row(number=500, closedAt=RECENT)], max_visible=2)
results["below the visible-row bound, ordinary retention is unchanged"] = (arch == [], arch)

# 9. ABOVE THE BOUND, the oldest otherwise-safe recent rows leave first. Input connection order is
#    deliberately scrambled: GitHub does not promise an order, and selection must not inherit one.
rows = [row(number=503, closedAt="2026-08-11T03:00:00Z"),
        row(number=501, closedAt="2026-08-11T01:00:00Z"),
        row(number=504, closedAt="2026-08-11T04:00:00Z"),
        row(number=502, closedAt="2026-08-11T02:00:00Z")]
arch, _ = plan(rows, max_visible=2)
results["over the bound archives the oldest safe rows first"] = (
    arch == [("FS-GG/.github", 501), ("FS-GG/.github", 502)], arch)

# ...and reversing the entire scan cannot change the selected set or its deterministic order.
arch_reversed, _ = plan(list(reversed(rows)), max_visible=2)
results["pressure selection is independent of GraphQL connection order"] = (
    arch_reversed == arch, (arch, arch_reversed))

# 10. The bound never weakens an absolute guard. With 103 visible rows but only one safe terminal
#     candidate, the planner archives that one and honestly leaves 102 rather than hiding live rows.
rows = [row(number=600, closedAt=RECENT)] + [
    row(number=601 + i, status="Ready", state="OPEN", closedAt=None) for i in range(102)
]
arch, _ = plan(rows, max_visible=100)
results["insufficient safe candidates leave the board above target"] = (
    arch == [("FS-GG/.github", 600)] and len(rows) - len(arch) == 102,
    (arch, len(rows) - len(arch)))

# 11. The measured live shape: 374 recent safe rows require exactly 274 archives to reach 100.
rows = [row(number=1000 + i, itemId=f"PVTI_{i:03d}",
            closedAt=f"2026-08-{10 + (i % 2):02d}T{(i % 24):02d}:00:00Z") for i in range(374)]
arch, _ = plan(rows, max_visible=100)
results["a 374-row safe fixture reaches exactly 100 visible rows"] = (
    len(arch) == 274 and len(rows) - len(arch) == 100,
    (len(arch), len(rows) - len(arch)))

# 12. The numeric policy is validated rather than allowing a zero/negative typo to sweep everything.
try:
    mod.plan([row(number=700)], NOW, 30, 0)
except ValueError as exc:
    invalid_refused = "positive" in str(exc)
else:
    invalid_refused = False
results["a non-positive visible-row bound is refused"] = (invalid_refused, invalid_refused)

# 13. The operator-facing report must distinguish "target reached" from "safety left it above".
report_rows = [row(number=800, itemId="PVTI_safe", closedAt="2026-08-28T00:00:00Z")] + [
    row(number=801 + i, itemId=f"PVTI_live_{i}", status="Ready", state="OPEN", closedAt=None)
    for i in range(102)
]
def fake_graphql(*args):
    if args == ("meter",):
        return {"remaining": 5000}
    if args[0] == "archive-scan":
        return {"items": report_rows, "pages": 2, "spent": 2}
    raise AssertionError(args)

mod.coord_graphql = fake_graphql
saved_argv = sys.argv
sys.argv = [str(sys.argv[1]), "--project", "PVT_fixture", "--max-visible", "100"]
stdout = io.StringIO()
try:
    with contextlib.redirect_stdout(stdout):
        report_rc = mod.main()
finally:
    sys.argv = saved_argv
report = stdout.getvalue()
results["reports when absolute guards prevent reaching the bound"] = (
    report_rc == 0 and "keep 102" in report and "BOUND NOT REACHED" in report and
    "2 row(s) remain above the target" in report,
    report)

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
