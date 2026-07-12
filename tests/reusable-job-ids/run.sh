#!/usr/bin/env bash
# Fixture for scripts/check-reusable-job-ids.py (.github#549, epic #266).
#
# Offline, and it needs no stub: the gate is static and local. It reads the working tree and git, and
# nothing else — which is the entire reason it exists. The check #549 ASKED for (read each repo's
# branch protection) cannot run in GitHub Actions at all: `administration: read` is not a valid
# GITHUB_TOKEN `permissions:` scope, and the org's dispatch App does not hold it either (.github#463).
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: the .github#266 vacuous-failure defect was a "must fail" test whose non-zero exit
# came from a path guard rather than from the thing under test.
#
# THE HEADLINE LEG is the outage itself, on the real file: this repo's actual lock-range-coherence.yml
# with its `lock-ranges:` job renamed — the exact commit that would stop every PR in FS.GG.Audio from
# merging. It is committed to a throwaway git repo so the gate sees a real base→head diff, which is
# what it will see on a real PR.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-reusable-job-ids.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/reusable-job-ids-fixture.XXXXXX")"
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

callee() {  # callee <file> <job-id> [name-line]
  { echo "name: c"; echo "on:"; echo "  workflow_call:"; echo "jobs:"; echo "  $2:"
    [ -n "${3:-}" ] && echo "    name: $3"
    echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } > "$1"
}

# =============================================================================================
# 1. THE OUTAGE — this repo's REAL lock-range-coherence.yml, with its job renamed.
# =============================================================================================
R="$(repo "$WORK/real")"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$R/.github/workflows/"
BASE="$(commit_base "$R")"

# Sanity: unchanged, the gate is green. A gate that reds on a no-op is a gate nobody keeps.
expect "the REAL lock-range-coherence.yml, unchanged, is green" \
  0 "ok: every context name published" "$R" "$BASE"

# Now the rename that deadlocks FS.GG.Audio.
python3 - "$R/.github/workflows/lock-range-coherence.yml" <<'PY'
import re, sys
p = sys.argv[1]
text = open(p, encoding="utf-8").read()
renamed = re.sub(r"^  lock-ranges:$", "  check-lock-ranges:", text, flags=re.M)
assert renamed != text, "lock-range-coherence.yml has no `lock-ranges:` job — fix the fixture"
open(p, "w", encoding="utf-8").write(renamed)
PY
expect "REGRESSION #549: renaming the REAL callee's job id is caught, on the PR that does it" \
  1 "no longer publishes the context name 'lock-ranges'" "$R" "$BASE"
expect "REGRESSION: and it names the receiver that requires it TODAY" \
  1 "FS.GG.Audio requires \`lock-ranges / lock-ranges\` today" "$R" "$BASE"
expect "REGRESSION: and it says what happens — every PR there hangs, with no commit in that repo" \
  1 "waiting for status to be reported" "$R" "$BASE"
expect "REGRESSION: and it shows what the workflow publishes NOW" \
  1 "it now publishes 'check-lock-ranges'" "$R" "$BASE"

# =============================================================================================
# 2. All four ways to break a caller. The job ID is not the only API surface.
# =============================================================================================
# (a) A job's `name:` overrides its id as the published context — renaming THAT breaks callers too.
RN="$(repo "$WORK/rename-name")"
callee "$RN/.github/workflows/c.yml" job1 "Lock ranges"
BN="$(commit_base "$RN")"
callee "$RN/.github/workflows/c.yml" job1 "Lock-range check"
expect "renaming a job's \`name:\` (not its id) is equally breaking, and is caught" \
  1 "no longer publishes the context name 'Lock ranges'" "$RN" "$BN"

# ...and the mirror: ADDING a `name:` to a job that had none also changes the published context.
RA="$(repo "$WORK/add-name")"
callee "$RA/.github/workflows/c.yml" job1
BA="$(commit_base "$RA")"
callee "$RA/.github/workflows/c.yml" job1 "Now Named"
expect "ADDING a \`name:\` to a bare job changes its published context, and is caught" \
  1 "no longer publishes the context name 'job1'" "$RA" "$BA"

# (b) Deleting a job.
RD="$(repo "$WORK/delete-job")"
{ echo "name: c"; echo "on:"; echo "  workflow_call:"; echo "jobs:"
  echo "  keep:"; echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"
  echo "  drop:"; echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } \
  > "$RD/.github/workflows/c.yml"
BD="$(commit_base "$RD")"
callee "$RD/.github/workflows/c.yml" keep
expect "DELETING a job in a reusable workflow is caught" \
  1 "no longer publishes the context name 'drop'" "$RD" "$BD"

# (c) Deleting the whole workflow.
RW="$(repo "$WORK/delete-wf")"
callee "$RW/.github/workflows/c.yml" job1
BW="$(commit_base "$RW")"
rm "$RW/.github/workflows/c.yml"
expect "DELETING the whole reusable workflow is caught" \
  1 "the workflow, or that job, was removed entirely" "$RW" "$BW"

# (d) Removing `on: workflow_call` — it stops being callable, so every name it published is gone.
RC="$(repo "$WORK/decallee")"
callee "$RC/.github/workflows/c.yml" job1
BC="$(commit_base "$RC")"
{ echo "name: c"; echo "on: { push: }"; echo "jobs:"; echo "  job1:"
  echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } > "$RC/.github/workflows/c.yml"
expect "removing \`on: workflow_call\` is caught — it published names, and now publishes none" \
  1 "no longer publishes the context name 'job1'" "$RC" "$BC"

# =============================================================================================
# 3. The gate must not cry wolf. A gate that reds on safe changes is a gate somebody deletes.
# =============================================================================================
RAdd="$(repo "$WORK/add-job")"
callee "$RAdd/.github/workflows/c.yml" job1
BAdd="$(commit_base "$RAdd")"
{ echo "name: c"; echo "on:"; echo "  workflow_call:"; echo "jobs:"
  echo "  job1:"; echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"
  echo "  job2:"; echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } \
  > "$RAdd/.github/workflows/c.yml"
expect "ADDING a job is always safe — no caller can require a context that did not exist" \
  0 "ok: every context name published" "$RAdd" "$BAdd"

RNew="$(repo "$WORK/new-wf")"
callee "$RNew/.github/workflows/c.yml" job1
BNew="$(commit_base "$RNew")"
callee "$RNew/.github/workflows/brand-new.yml" fresh
expect "a BRAND-NEW reusable workflow published nothing before, so it cannot break a caller" \
  0 "ok: every context name published" "$RNew" "$BNew"

RNonC="$(repo "$WORK/non-callee")"
callee "$RNonC/.github/workflows/c.yml" job1
{ echo "name: local"; echo "on: { pull_request: }"; echo "jobs:"; echo "  whatever:"
  echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } \
  > "$RNonC/.github/workflows/local.yml"
BNonC="$(commit_base "$RNonC")"
{ echo "name: local"; echo "on: { pull_request: }"; echo "jobs:"; echo "  renamed-freely:"
  echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } \
  > "$RNonC/.github/workflows/local.yml"
expect "renaming a job in a NON-reusable workflow is nobody's business but ours — not flagged" \
  0 "ok: every context name published" "$RNonC" "$BNonC"

RBody="$(repo "$WORK/body-only")"
callee "$RBody/.github/workflows/c.yml" job1
BBody="$(commit_base "$RBody")"
{ echo "name: c"; echo "on:"; echo "  workflow_call:"; echo "jobs:"; echo "  job1:"
  echo "    runs-on: ubuntu-latest"; echo "    timeout-minutes: 10"
  echo "    steps: [{ run: 'echo changed' }]"; } > "$RBody/.github/workflows/c.yml"
expect "editing a reusable job's BODY (steps, timeout) changes no context — not flagged" \
  0 "ok: every context name published" "$RBody" "$BBody"

# =============================================================================================
# 4. Fail closed. "I could not check" is never green, and never a finding either (#266/#320).
# =============================================================================================
RNoCallee="$(repo "$WORK/no-callee")"
{ echo "name: local"; echo "on: { push: }"; echo "jobs:"; echo "  j:"
  echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } \
  > "$RNoCallee/.github/workflows/local.yml"
BNo="$(commit_base "$RNoCallee")"
expect "a base with NO reusable workflow at all is exit 3 — this repo always has callees" \
  3 "compared nothing" "$RNoCallee" "$BNo"

RBad="$(repo "$WORK/unparsable")"
callee "$RBad/.github/workflows/c.yml" job1
BBad="$(commit_base "$RBad")"
printf 'name: c\non:\n  workflow_call:\njobs:\n  j:\n   - broken: [\n' > "$RBad/.github/workflows/c.yml"
expect "an unparsable workflow is exit 3 — not a finding about a renamed job" \
  3 "not parsable as YAML" "$RBad" "$BBad"

RNoBase="$(repo "$WORK/no-base")"
callee "$RNoBase/.github/workflows/c.yml" job1
commit_base "$RNoBase" >/dev/null
expect "an unreadable base ref is exit 3 — never a green verdict over an uncompared tree" \
  3 "no verdict" "$RNoBase" "0000000000000000000000000000000000000000"

# A reusable workflow with NO jobs publishes nothing and cannot run — that is a broken file, not a
# clean audit.
RNoJobs="$(repo "$WORK/no-jobs")"
callee "$RNoJobs/.github/workflows/c.yml" job1
BNJ="$(commit_base "$RNoJobs")"
printf 'name: c\non:\n  workflow_call:\n' > "$RNoJobs/.github/workflows/c.yml"
expect "a reusable workflow with no \`jobs:\` is exit 3 — it publishes nothing and cannot run" \
  3 "publishes nothing" "$RNoJobs" "$BNJ"

# =============================================================================================
# 5. The gate's own shipped surface.
# =============================================================================================
if python3 - "$REPO_ROOT/.github/workflows/reusable-job-id-coherence.yml" <<'PY'
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
perms = d.get("permissions")
assert isinstance(perms, dict) and perms.get("contents") == "read", f"top-level permissions: {perms}"
# `administration` is NOT a valid GITHUB_TOKEN scope — declaring it is a startup_failure, which is
# how #549's first attempt died. Nothing in this repo may reintroduce it.
assert "administration" not in perms, "administration: is not a valid workflow permissions scope"
body = "".join(str(s.get("run", "")) for j in d["jobs"].values() for s in j.get("steps", []))
assert "check-reusable-job-ids.py" in body, "the gate workflow never runs the gate"
assert "tests/reusable-job-ids/run.sh" in body, "the gate workflow never runs this fixture"
assert "merge-base" in body, "the gate must compare against the merge-base, not the base tip (#375)"
for jid, j in d["jobs"].items():
    assert isinstance(j.get("timeout-minutes"), int), f"job {jid} does not bound itself (#541)"
PY
then ok "the shipped reusable-job-id-coherence.yml declares no invalid scope, bounds its jobs, diffs the merge-base, and runs both the gate and this fixture"
else bad "the shipped reusable-job-id-coherence.yml is not the shape this fixture asserts"
fi

echo
echo "reusable-job-ids fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::reusable-job-ids fixture FAILED"; exit 1; }
echo "reusable-job-ids fixture — OK"
