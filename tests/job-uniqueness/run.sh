#!/usr/bin/env bash
# Fixture for scripts/check-job-uniqueness.py (.github#2354 AC-2, filed as .github#2374, epic #266).
#
# Offline, and it needs no stub: the gate is static and local, reading only git and the working tree.
#
# Every negative leg asserts the REASON, not merely a non-zero exit — tests/feed-coherence/run.sh:10
# names the trap this defends against: a "must fail" test whose non-zero exit came from a path guard
# rather than from the thing under test (the #266 vacuous-failure defect, recurring).
#
# THE HEADLINE LEG is the outage itself, reconstructed from this repo's REAL PR #2371: two workflows
# that share a bare job id `reconcile` at the merge-base become two DIFFERENT names when one gets a
# `name:` override — the gate must not complain about the fix landing. Then reverting that override —
# the exact regression AC-3 names — must be caught.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-job-uniqueness.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/job-uniqueness-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1
export GIT_AUTHOR_NAME=fixture GIT_AUTHOR_EMAIL=fixture@fs-gg.invalid
export GIT_COMMITTER_NAME=fixture GIT_COMMITTER_EMAIL=fixture@fs-gg.invalid

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# repo <dir> — a throwaway git repo with a `.github/workflows` dir. Prints the dir.
repo() {
  local d="$1"; mkdir -p "$d/.github/workflows"
  git -C "$d" init -q -b main
  echo "$d"
}
# commit_base <dir> — snapshot the current tree as the base, and print the base sha.
commit_base() {
  git -C "$1" add -A && git -C "$1" commit -qm base && git -C "$1" rev-parse HEAD
}

expect() {  # expect <name> <want-rc> <needle> <root> <base>
  local name="$1" want="$2" needle="$3" root="$4" base="$5"
  local out rc=0
  out="$(python3 "$TOOL" --root "$root" --base "$base" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

expect_ctx() {  # expect_ctx <name> <want-rc> <needle> <root> <base> -- <--required-context ...>
  local name="$1" want="$2" needle="$3" root="$4" base="$5"; shift 5
  local out rc=0
  out="$(python3 "$TOOL" --root "$root" --base "$base" "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

job() {  # job <file> <job-id> [name-line]
  { echo "name: w"; echo "on: { pull_request: }"; echo "jobs:"; echo "  $2:"
    [ -n "${3:-}" ] && echo "    name: $3"
    echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } > "$1"
}
two_jobs() {  # two_jobs <file> <job-id-a> <job-id-b>
  { echo "name: w"; echo "on: { pull_request: }"; echo "jobs:"
    echo "  $2:"; echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"
    echo "  $3:"; echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } > "$1"
}

# =============================================================================================
# 1. THE OUTAGE, RECONSTRUCTED FROM PR #2371 — two workflows sharing a bare `reconcile` job id.
# =============================================================================================
R="$(repo "$WORK/real")"
job "$R/.github/workflows/architecture-map.yml" reconcile
job "$R/.github/workflows/coord-board-reconcile.yml" reconcile
BASE="$(commit_base "$R")"

expect "two workflows sharing a bare job id, UNCHANGED, is already ambiguous — not a NEW regression" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$R" "$BASE"

# THE FIX: give one of them a disambiguating name:. Must not be flagged — shrinking ambiguity is not
# a regression, it is the repair PR #2371 made.
job "$R/.github/workflows/architecture-map.yml" reconcile "architecture-map reconcile"
expect "disambiguating ONE of two colliding jobs is not a finding — that is the #2371 repair" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$R" "$BASE"

# THE REGRESSION: revert it. Now `reconcile` and `architecture-map reconcile` — HEAD keeps only ONE
# producer for the reverted-away name, but this checks the FORWARD direction against a base that
# ALREADY had it disambiguated (the shape #2371's own state would face if reverted).
B2="$(commit_base "$R")"
job "$R/.github/workflows/architecture-map.yml" reconcile   # drop the name: override
expect "REGRESSION #2354: reverting a disambiguating name: is caught" \
  1 "the check-run name 'reconcile' is now produced by 2 top-level jobs" "$R" "$B2"
expect "REGRESSION: and it names BOTH producing files" \
  1 "architecture-map.yml:reconcile" "$R" "$B2"
expect "REGRESSION: and it explains the hazard and cites the precedent" \
  1 ".github#2354 defect" "$R" "$B2"

# =============================================================================================
# 2. ADDING A FOURTH JOB WITH AN EXISTING (PREVIOUSLY UNIQUE) NAME — AC-3's other example.
# =============================================================================================
RU="$(repo "$WORK/unique-then-fourth")"
job "$RU/.github/workflows/only.yml" reconcile
BU="$(commit_base "$RU")"
expect "a name with exactly one producer is not ambiguous" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$RU" "$BU"

job "$RU/.github/workflows/second.yml" reconcile
expect "REGRESSION: a SECOND job claiming a previously-unique name is caught" \
  1 "the check-run name 'reconcile' is now produced by 2 top-level jobs" "$RU" "$BU"

# =============================================================================================
# 3. THE GATE MUST NOT CRY WOLF ON THE REPO'S OWN ACCEPTED CONVENTION.
# =============================================================================================
# An ALREADY-shared name (2+ producers at base) picking up a THIRD producer is not a new regression:
# the ambiguity existed before this PR, and this PR did not create it.
RF="$(repo "$WORK/already-shared-grows")"
job "$RF/.github/workflows/a.yml" fixture
job "$RF/.github/workflows/b.yml" fixture
BF="$(commit_base "$RF")"
job "$RF/.github/workflows/c.yml" fixture
expect "a name ALREADY shared by 2+ jobs picking up a third is not flagged (the repo's own 'fixture' shape)" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$RF" "$BF"

# An unrelated edit to a workflow that is NOT part of any collision must stay green.
RN="$(repo "$WORK/unrelated-edit")"
job "$RN/.github/workflows/a.yml" fixture
job "$RN/.github/workflows/b.yml" fixture
job "$RN/.github/workflows/solo.yml" build
BN="$(commit_base "$RN")"
{ echo "name: w"; echo "on: { pull_request: }"; echo "jobs:"; echo "  build:"
  echo "    runs-on: ubuntu-latest"; echo "    timeout-minutes: 30"
  echo "    steps: [{ run: 'echo changed' }]"; } > "$RN/.github/workflows/solo.yml"
expect "editing an UNRELATED job's body, with pre-existing duplicates elsewhere, stays green" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$RN" "$BN"

# A job renamed via job-level name: from one unique name to another unique name is not a collision.
RR="$(repo "$WORK/rename-unique")"
job "$RR/.github/workflows/a.yml" solo "Solo A"
BR="$(commit_base "$RR")"
job "$RR/.github/workflows/a.yml" solo "Solo A renamed"
expect "renaming a job's own unique name to another unique name is not flagged" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$RR" "$BR"

# =============================================================================================
# 4. JOBS WITH `uses:` (reusable-workflow callers) ARE OUT OF SCOPE — that is #549's gate.
# =============================================================================================
RC="$(repo "$WORK/uses-jobs")"
{ echo "name: w"; echo "on: { pull_request: }"; echo "jobs:"
  echo "  caller-a:"; echo "    uses: ./.github/workflows/callee.yml"
  echo "  caller-b:"; echo "    uses: ./.github/workflows/callee.yml"; } > "$RC/.github/workflows/x.yml"
job "$RC/.github/workflows/dummy.yml" placeholder
BC="$(commit_base "$RC")"
expect "jobs with uses: are excluded even when their job ids collide — that is #549's subject" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$RC" "$BC"

# =============================================================================================
# 5. A MATRIX JOB IS ONE PRODUCER, NOT MANY — it does not itself create a collision.
# =============================================================================================
RM="$(repo "$WORK/matrix-job")"
job "$RM/.github/workflows/m.yml" verify
BM="$(commit_base "$RM")"
{ echo "name: w"; echo "on: { pull_request: }"; echo "jobs:"; echo "  verify:"
  echo "    runs-on: ubuntu-latest"; echo "    strategy:"; echo "      matrix:"
  echo "        os: [ubuntu-latest, windows-latest]"
  echo "    steps: [{ run: 'true' }]"; } > "$RM/.github/workflows/m.yml"
expect "a job GAINING a strategy.matrix is still one producer, not a collision" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$RM" "$BM"

# =============================================================================================
# 6. `--required-context` (.github#2447): an ABSOLUTE tripwire for an explicit, small set of names.
# =============================================================================================
# An already-ambiguous name, UNCHANGED between base and head, is not flagged by default (leg 1's
# first assertion already proves that). But if the CALLER marks that same name as a required
# context, it must be flagged — unconditionally, not as a base-vs-head regression, because a
# required context that is already ambiguous is a live exposure every day it stays that way.
RQ="$(repo "$WORK/required-context")"
job "$RQ/.github/workflows/architecture-map.yml" reconcile
job "$RQ/.github/workflows/coord-board-reconcile.yml" reconcile
BRQ="$(commit_base "$RQ")"

expect_ctx "an already-ambiguous name, unchanged, stays green WITHOUT --required-context" \
  0 "ok: no top-level job's check-run name became newly ambiguous" "$RQ" "$BRQ"

expect_ctx "the SAME already-ambiguous name, unchanged, is flagged WITH --required-context naming it" \
  1 "the REQUIRED status check 'reconcile' is produced by 2 top-level jobs at HEAD" \
  "$RQ" "$BRQ" --required-context reconcile
expect_ctx "...and it names both producing files" \
  1 "architecture-map.yml:reconcile" "$RQ" "$BRQ" --required-context reconcile
expect_ctx "...and it says this is LIVE today, not merely a future risk" \
  1 "LIVE, today" "$RQ" "$BRQ" --required-context reconcile

expect_ctx "--required-context naming an UNAMBIGUOUS name stays green" \
  0 "ok: no top-level job's check-run name became newly ambiguous" \
  "$RQ" "$BRQ" --required-context some-other-context

expect_ctx "--required-context naming a name absent from HEAD entirely stays green (no KeyError)" \
  0 "ok: no top-level job's check-run name became newly ambiguous" \
  "$RQ" "$BRQ" --required-context this-name-does-not-exist-anywhere

# A NEW regression (leg 1's shape) is still reported the same way even with an unrelated
# --required-context passed alongside it — the two finding classes are independent.
RQ2="$(repo "$WORK/required-context-and-regression")"
job "$RQ2/.github/workflows/only.yml" drift
BRQ2="$(commit_base "$RQ2")"
job "$RQ2/.github/workflows/second.yml" drift
expect_ctx "a NEW regression is still caught with an unrelated --required-context also passed" \
  1 "the check-run name 'drift' is now produced by 2 top-level jobs" \
  "$RQ2" "$BRQ2" --required-context reconcile

# =============================================================================================
# 7. Fail closed. "I could not check" is never green, and never a finding either (#266/#320).
# =============================================================================================
RE="$(repo "$WORK/empty")"
echo "no workflows here" > "$RE/README.md"   # something to commit — an empty tree cannot be
BE="$(commit_base "$RE")"
expect "no workflow at all, at either base or head, is exit 3 — never a vacuous green" \
  3 "" "$RE" "$BE"

RBad="$(repo "$WORK/unparsable")"
job "$RBad/.github/workflows/c.yml" job1
BBad="$(commit_base "$RBad")"
printf 'name: c\non: { pull_request: }\njobs:\n  j:\n   - broken: [\n' > "$RBad/.github/workflows/c.yml"
expect "an unparsable workflow at HEAD is exit 3 — not a finding about a name" \
  3 "not parsable as YAML" "$RBad" "$BBad"

RNoBase="$(repo "$WORK/no-base")"
job "$RNoBase/.github/workflows/c.yml" job1
commit_base "$RNoBase" >/dev/null
expect "an unreadable base ref is exit 3 — never a green verdict over an uncompared tree" \
  3 "" "$RNoBase" "0000000000000000000000000000000000000000"

RNoJobs="$(repo "$WORK/no-jobs")"
job "$RNoJobs/.github/workflows/c.yml" job1
BNJ="$(commit_base "$RNoJobs")"
printf 'name: c\non: { pull_request: }\n' > "$RNoJobs/.github/workflows/c.yml"
expect "a workflow with no jobs: at HEAD is exit 3 — it cannot run" \
  3 "declares no \`jobs:\` mapping" "$RNoJobs" "$BNJ"

# =============================================================================================
# 8. The gate's own shipped surface.
# =============================================================================================
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
if python3 - "$REPO_ROOT/.github/workflows/job-uniqueness-coherence.yml" <<'PY'
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
perms = d.get("permissions")
assert isinstance(perms, dict) and perms.get("contents") == "read", f"top-level permissions: {perms}"
body = "".join(str(s.get("run", "")) for j in d["jobs"].values() for s in j.get("steps", []))
assert "check-job-uniqueness.py" in body, "the gate workflow never runs the gate"
assert "tests/job-uniqueness/run.sh" in body, "the gate workflow never runs this fixture"
assert "merge-base" in body, "the gate must compare against the merge-base, not the base tip (#375)"
# .github#2447: the shipped workflow must actually pass the live-required plain top-level contexts
# through, or the new tripwire above is dead code nobody wires up.
assert "--required-context" in body, "the gate workflow never passes --required-context (.github#2447)"
for expected in ("projection", "roster-closure", "drift", "reconcile"):
    assert expected in body, f"the gate workflow never names {expected!r} as a --required-context"
assert "--required-context contract-coherence" not in body, (
    "the nested contract-coherence / coherence context must NOT be passed as --required-context — "
    "it can never match a top-level producer, and would be silently vacuous (see the workflow's own "
    "header comment)"
)
for jid, j in d["jobs"].items():
    assert isinstance(j.get("timeout-minutes"), int), f"job {jid} does not bound itself (#541)"
PY
then ok "the shipped job-uniqueness-coherence.yml declares no invalid scope, bounds its jobs, diffs the merge-base, wires --required-context, and runs both the gate and this fixture"
else bad "the shipped job-uniqueness-coherence.yml is not the shape this fixture asserts"
fi

echo
echo "job-uniqueness fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::job-uniqueness fixture FAILED"; exit 1; }
echo "job-uniqueness fixture — OK"
