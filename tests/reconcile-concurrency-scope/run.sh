#!/usr/bin/env bash
# Fixture for scripts/check-reconcile-concurrency-scope.py (.github#2361, round 3).
#
# Offline, and it needs no stub: the gate is static, string-only — it reads one committed workflow
# file and checks its `concurrency` block against three literal facts (no per-event branching in
# `group`, `queue: max`, `cancel-in-progress: false`). No API, no network, no live GitHub scheduler.
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: a "must fail" test whose non-zero exit came from a path guard rather than the thing
# under test would pass against a gate broken in a completely different way.
#
# THREE regression legs, one per design this single item actually shipped and had superseded, in
# order — each one is a real defect this repo carried, however briefly, and each must stay caught:
#   A. pre-.github#2361: a flat group, GitHub's implicit `queue: single` default — the ORIGINAL
#      defect (22/37 issue_comment runs cancelled; PR #2383 silently blocked).
#   B. round 1: the group keyed by triggering issue/PR number, still `queue: single` — fixed the
#      eviction, reopened the double-write hole (`github.event...` branching in the group).
#   C. round 2: the group's isolation narrowed to mirror the job's marker-comment `if:` predicate,
#      still `queue: single` — closed the double-write hole, left genuine-vs-genuine eviction (and
#      still `github.event...` branching, just narrower) open. This is the design an earlier
#      independent-review round explicitly asked be encoded as its own failing case.
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

# job_stub <dir> — a minimal but valid `reconcile` job, so only the concurrency block varies.
job_stub() {
  { echo "name: w"
    echo "on: { workflow_dispatch: }"
    echo "jobs:"
    echo "  reconcile:"
    echo "    if: >-"
    echo "      github.event_name != 'issue_comment' ||"
    echo "      !startsWith(github.event.comment.body, '<!-- fsgg:')"
    echo "    runs-on: ubuntu-latest"
    echo "    timeout-minutes: 5"
    echo "    steps: [{ run: 'true' }]"; }
}

REAL="$REPO_ROOT/.github/workflows/coord-board-reconcile.yml"

# =============================================================================================
# 1. REGRESSION — the real, shipped file passes.
# =============================================================================================
expect "REGRESSION: the real coord-board-reconcile.yml is fan-out-safe" 0 "OK —" "$REPO_ROOT"

# =============================================================================================
# 2. GATE-INVERSION A — the ORIGINAL .github#2361 shape: flat group, implicit queue: single.
# =============================================================================================
RA="$(root "$WORK/defect-a-original")"
{ job_stub
  echo "concurrency:"
  echo "  group: coord-board-reconcile-\${{ github.repository }}"
  echo "  cancel-in-progress: false"; } > "$RA/.github/workflows/coord-board-reconcile.yml"
expect "GATE-INVERSION A: the original pre-#2361 shape (no queue: max) is caught" \
  1 "inherits GitHub's \`single\` default" "$RA"

# =============================================================================================
# 3. GATE-INVERSION B — round 1: group keyed by issue/PR number, still queue: single.
# =============================================================================================
RB="$(root "$WORK/defect-b-round1")"
{ job_stub
  echo "concurrency:"
  echo "  group: >-"
  echo "    coord-board-reconcile-\${{ github.repository }}-\${{ github.event.pull_request.number ||"
  echo "    github.event.issue.number || 'periodic' }}"
  echo "  cancel-in-progress: false"; } > "$RB/.github/workflows/coord-board-reconcile.yml"
expect "GATE-INVERSION B: round 1's per-issue/PR fan-out (double-write hole) is caught" \
  1 "the group varies by triggering event" "$RB"

# =============================================================================================
# 4. GATE-INVERSION C — round 2: group mirrors the job's marker `if:`, still queue: single. The
#    design an independent-review round explicitly required be encoded as its own failing case.
# =============================================================================================
RC="$(root "$WORK/defect-c-round2")"
{ job_stub
  echo "concurrency:"
  echo "  group: >-"
  echo "    coord-board-reconcile-\${{ github.repository }}-\${{ (github.event_name != 'issue_comment' ||"
  echo "    !startsWith(github.event.comment.body, '<!-- fsgg:')) && 'main' || github.event.issue.number }}"
  echo "  cancel-in-progress: false"; } > "$RC/.github/workflows/coord-board-reconcile.yml"
expect "GATE-INVERSION C: round 2's predicate-mirrored fan-out (genuine-vs-genuine eviction still open) is caught" \
  1 "the group varies by triggering event" "$RC"

# =============================================================================================
# 5. queue present but wrong, and cancel-in-progress: true (GitHub's own rejected combination).
# =============================================================================================
RQ="$(root "$WORK/queue-single-explicit")"
{ job_stub
  echo "concurrency:"
  echo "  group: coord-board-reconcile-\${{ github.repository }}"
  echo "  cancel-in-progress: false"
  echo "  queue: single"; } > "$RQ/.github/workflows/coord-board-reconcile.yml"
expect "an explicit \`queue: single\` is caught, same as an absent queue key" \
  1 "inherits GitHub's \`single\` default" "$RQ"

RCIP="$(root "$WORK/cancel-in-progress-true")"
{ job_stub
  echo "concurrency:"
  echo "  group: coord-board-reconcile-\${{ github.repository }}"
  echo "  cancel-in-progress: true"
  echo "  queue: max"; } > "$RCIP/.github/workflows/coord-board-reconcile.yml"
expect "\`cancel-in-progress: true\` (GitHub rejects this paired with queue: max) is caught" \
  1 "GitHub rejects \`queue: max\` paired with" "$RCIP"

# =============================================================================================
# 6. The fix must actually satisfy the gate: the file as this PR ships it, standalone.
# =============================================================================================
RFIXED="$(root "$WORK/fixed")"
{ job_stub
  echo "concurrency:"
  echo "  group: coord-board-reconcile-\${{ github.repository }}"
  echo "  cancel-in-progress: false"
  echo "  queue: max"; } > "$RFIXED/.github/workflows/coord-board-reconcile.yml"
expect "the shipped shape (flat group, queue: max, cancel-in-progress: false) passes" 0 "OK —" "$RFIXED"

# =============================================================================================
# 7. Fail closed. "Nothing to check" is never green (epic #266).
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
job_stub > "$RNOCONC/.github/workflows/coord-board-reconcile.yml"
expect "no \`concurrency\` block at all is exit 3" 3 "no \`concurrency\` block" "$RNOCONC"

RNOGROUP="$(root "$WORK/nogroup")"
{ job_stub
  echo "concurrency:"
  echo "  cancel-in-progress: false"
  echo "  queue: max"; } > "$RNOGROUP/.github/workflows/coord-board-reconcile.yml"
expect "a \`concurrency\` block with no \`group\` is exit 3" 3 "not a non-empty string" "$RNOGROUP"

# =============================================================================================
# 8. The gate is STATIC — prove it, rather than asserting it in a comment. Its exit-code contract
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
# 9. The gate's own shipped surface.
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
