#!/usr/bin/env bash
# Fixture for scripts/check-reconcile-concurrency-scope.py (.github#2361 round 2).
#
# Offline, and it needs no stub: the gate is static, string-only — it reads one committed workflow
# file and resolves its `concurrency.group` template against a fixed, in-script event set. No API,
# no network, no live GitHub scheduler (which is the whole reason this had to become a committed,
# CI-run check rather than staying a worker's scratchpad script: the behaviour it protects can only
# ever be exercised live, but the LOGIC it protects is pure string resolution and belongs in CI).
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: a "must fail" test whose non-zero exit came from a path guard rather than the thing
# under test would pass against a gate broken in a completely different way.
#
# TWO regression legs, because this gate protects a TWO-SIDED invariant and either half regressing is
# a real defect this repo has already shipped once:
#   - the ORIGINAL .github#2361 defect: a flat, repo-wide group (`coord-board-reconcile-${{
#     github.repository }}`, no isolation at all) — every marker-comment burst evicts a real run.
#   - the ROUND-2 defect this same item's independent review caught: a group keyed by the triggering
#     issue/PR number for EVERY event, which fixes the eviction but lets genuinely concurrent
#     board-writing runs happen — the fan-out `--worker coord-board-reconcile`'s fixed id was
#     reasoned to be safe from, silently removed.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-reconcile-concurrency-scope.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/reconcile-concurrency-scope-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# expect <name> <want-rc> <needle> <root> — the rc AND the reason must both match.
expect() {
  local name="$1" want="$2" needle="$3" root="$4"
  local out rc=0
  out="$(python3 "$TOOL" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# root <dir> — a synthetic working tree carrying only the one workflow this gate reads.
root() { mkdir -p "$1/.github/workflows"; echo "$1"; }

REAL="$REPO_ROOT/.github/workflows/coord-board-reconcile.yml"

# =============================================================================================
# 1. REGRESSION — the real, shipped file passes.
# =============================================================================================
expect "REGRESSION: the real coord-board-reconcile.yml is fan-out-safe" 0 "OK —" "$REPO_ROOT"

# =============================================================================================
# 2. GATE-INVERSION, defect A — the ORIGINAL .github#2361 shape: flat, repo-wide group.
# =============================================================================================
RA="$(root "$WORK/defect-a-flat")"
python3 - "$REAL" "$RA/.github/workflows/coord-board-reconcile.yml" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
fixed = re.sub(
    r"group: >-\n.*\n.*\n",
    "group: coord-board-reconcile-${{ github.repository }}\n",
    text,
)
assert fixed != text, "could not locate the concurrency.group block — fix the fixture"
open(dst, "w", encoding="utf-8").write(fixed)
PY
expect "GATE-INVERSION A: reverting to the flat pre-#2361 group is caught" \
  1 "reproducing the original .github#2361 defect" "$RA"

# =============================================================================================
# 3. GATE-INVERSION, defect B — the ROUND-2 shape: keyed by issue/PR number for EVERY event, no
#    predicate mirroring at all. This is the fix's OWN first cut, which the independent critic
#    caught: it solves the eviction but reopens the "no fan-out for a shared id" invariant.
# =============================================================================================
RB="$(root "$WORK/defect-b-naive-fanout")"
python3 - "$REAL" "$RB/.github/workflows/coord-board-reconcile.yml" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
fixed = re.sub(
    r"group: >-\n.*\n.*\n",
    "group: >-\n"
    "    coord-board-reconcile-${{ github.repository }}-${{ github.event.pull_request.number ||\n"
    "    github.event.issue.number || 'periodic' }}\n",
    text,
)
assert fixed != text, "could not locate the concurrency.group block — fix the fixture"
open(dst, "w", encoding="utf-8").write(fixed)
PY
expect "GATE-INVERSION B: fanning out EVERY event by issue/PR number is caught" \
  1 "real board-writing runs could now run concurrently" "$RB"

# =============================================================================================
# 4. Predicate drift: the group's skip-branch stops mirroring the job's OWN `if:` (a different
#    marker prefix on one side only) — two independently-editable facts allowed to disagree.
# =============================================================================================
RC="$(root "$WORK/defect-c-drift")"
python3 - "$REAL" "$RC/.github/workflows/coord-board-reconcile.yml" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
# Drift ONLY the group's copy of the marker prefix, leaving the job's `if:` untouched.
fixed = text.replace(
    "!startsWith(github.event.comment.body, '<!-- fsgg:')) && 'main'",
    "!startsWith(github.event.comment.body, '<!-- other:')) && 'main'",
)
assert fixed != text, "could not locate the group's marker-prefix copy — fix the fixture"
open(dst, "w", encoding="utf-8").write(fixed)
PY
expect "predicate drift (group's marker prefix no longer matches the job's if:) is caught" \
  1 "no longer mirrors the job's own skip predicate" "$RC"

# =============================================================================================
# 5. Fail closed. "Nothing to check" is never green (epic #266).
# =============================================================================================
REMPTY="$(root "$WORK/empty")"
expect "no coord-board-reconcile.yml at all is exit 3, never a vacuous green" 3 "does not exist" "$REMPTY"

RBAD="$(root "$WORK/unparsable")"
printf 'name: w\non: { push: }\njobs:\n  reconcile:\n   - broken: [\n' > "$RBAD/.github/workflows/coord-board-reconcile.yml"
expect "an unparsable workflow is exit 3" 3 "would not parse" "$RBAD"

RNOJOB="$(root "$WORK/nojob")"
printf 'name: w\non: { push: }\njobs:\n  other:\n    runs-on: ubuntu-latest\n    steps: []\n' \
  > "$RNOJOB/.github/workflows/coord-board-reconcile.yml"
expect "no \`reconcile\` job at all is exit 3" 3 "no \`reconcile\` job" "$RNOJOB"

RNOCONC="$(root "$WORK/noconcurrency")"
python3 - "$REAL" "$RNOCONC/.github/workflows/coord-board-reconcile.yml" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
fixed = re.sub(r"concurrency:\n  group: >-\n.*\n.*\n  cancel-in-progress: false\n\n", "", text)
assert fixed != text, "could not strip the concurrency block — fix the fixture"
open(dst, "w", encoding="utf-8").write(fixed)
PY
expect "no \`concurrency.group\` at all is exit 3" 3 "no \`concurrency.group\` string" "$RNOCONC"

# =============================================================================================
# 6. The gate is STATIC — prove it, rather than asserting it in a comment. Its exit-code contract
#    omits 2 ("retryable") on the strength of this claim.
# =============================================================================================
if python3 - "$TOOL" <<'PY'
import ast, sys
tree = ast.parse(open(sys.argv[1], encoding="utf-8").read())
banned = {"urllib", "http", "socket", "requests", "subprocess", "ssl", "ftplib", "telnetlib"}
for node in ast.walk(tree):
    if isinstance(node, ast.Import):
        names = [a.name for a in node.names]
    elif isinstance(node, ast.ImportFrom):
        names = [node.module or ""]
    else:
        continue
    for n in names:
        assert n.split(".")[0] not in banned, f"the gate imports {n} — it is not static"
PY
then ok "the gate imports no transport (urllib/http/socket/requests/subprocess) — exit 2 is a verdict it can never mean"
else bad "the gate imports a transport module — it can reach the network, and its exit-code contract is a lie"
fi

# =============================================================================================
# 7. The gate's own shipped surface (once wired into CI).
# =============================================================================================
COHERENCE_WF="$REPO_ROOT/.github/workflows/reconcile-concurrency-scope-coherence.yml"
if [ -f "$COHERENCE_WF" ] && python3 - "$COHERENCE_WF" <<'PY'
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
perms = d.get("permissions")
assert isinstance(perms, dict) and perms.get("contents") == "read", f"top-level permissions: {perms}"
body = "".join(str(s.get("run", "")) for j in d["jobs"].values() for s in j.get("steps", []))
assert "check-reconcile-concurrency-scope.py" in body, "the gate workflow never runs the gate"
assert "tests/reconcile-concurrency-scope/run.sh" in body, "the gate workflow never runs this fixture"
for jid, j in d["jobs"].items():
    assert isinstance(j.get("timeout-minutes"), int), f"job {jid} does not bound itself"
PY
then ok "the shipped reconcile-concurrency-scope-coherence.yml declares contents: read, bounds its own jobs, and runs both the gate and this fixture"
else bad "the shipped reconcile-concurrency-scope-coherence.yml is missing or not the shape this fixture asserts"
fi

echo
echo "reconcile-concurrency-scope fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::reconcile-concurrency-scope fixture FAILED"; exit 1; }
echo "reconcile-concurrency-scope fixture — OK"
