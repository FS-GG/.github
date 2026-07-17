#!/usr/bin/env bash
# Fixture for scripts/check-recipe-landable.py — the "a recipe NAMES the command, it does not EMBED
# the gate" rule (.github#724).
#
# The rule exists because the merge-gate rollup was wrong FOUR times in four copies (#547, #606, #698,
# #710, #720), and every fix edited a copy: nothing executes a recipe, so nothing tests one. This
# fixture is what stops the GATE going the same way — a gate with no test is the exact thing epic #266
# is about, and it would be absurd for this one of all gates to be untested.
#
# It drives the REAL script against throwaway recipe trees, and — crucially — against the REPO'S OWN
# recipes, so a fixture that passes on toy input cannot hide a rule that no longer fires on the real
# thing.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-recipe-landable.py"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "recipe-landable fixture — gate='$GATE'"

[ -f "$GATE" ] || { bad "the gate exists" "missing $GATE"; echo "::error::recipe-landable FAILED"; exit 1; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/recipe-landable-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# run_on <markdown> -> rc; combined output in $OUT.
# Builds a throwaway repo whose layout the gate recognises (scripts/ + .claude/skills/x/SKILL.md) and
# runs the REAL gate inside it. The gate locates its targets relative to its OWN path, so it has to be
# copied in — which also means this exercises the real path resolution, not a stubbed one.
run_on() {
  local md="$1" root rc=0
  root="$(mktemp -d "$WORK/repo-XXXXXX")"
  mkdir -p "$root/scripts" "$root/.claude/skills/x" "$root/.agents/skills" "$root/docs/coordination"
  cp "$GATE" "$root/scripts/"
  printf '%s\n' "$md" > "$root/.claude/skills/x/SKILL.md"
  OUT="$(python3 "$root/scripts/check-recipe-landable.py" 2>&1)" || rc=$?
  return "$rc"
}

# --- the shape the rule mandates: NAME the command ------------------------------------------------
rc=0; run_on '# ok
```sh
scripts/fsgg-coord landable <pr> --wait || exit 1
gh api -X PUT repos/FS-GG/x/pulls/1/merge -f merge_method=squash
```' || rc=$?
[ "$rc" -eq 0 ] && ok "a recipe that CALLS \`fsgg-coord landable\` passes" \
                || bad "the sanctioned form must pass" "rc=$rc"$'\n'"$OUT"

# --- THE RULE: a hand-rolled workflow-runs rollup is refused ---------------------------------------
rc=0; run_on '# bad
```sh
SHA=$(gh api repos/FS-GG/x/pulls/1 --jq .head.sha)
gh api "repos/FS-GG/x/actions/runs?head_sha=$SHA" --paginate --slurp | jq "[.[].workflow_runs[]]"
```' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'workflow runs'; then
  ok "a hand-rolled WORKFLOW-RUNS gate is refused (#724)"
else
  bad "a recipe reading actions/runs must be refused" "rc=$rc"$'\n'"$OUT"
fi

# --- ...and so is a hand-rolled check-runs rollup, which is the OTHER half the copies kept getting
#     wrong. Both endpoints, or the rule only bans half the disease.
rc=0; run_on '# bad
```sh
gh api "repos/FS-GG/x/commits/$SHA/check-runs" --paginate --slurp | jq "[.[].check_runs[]]"
```' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'check runs'; then
  ok "a hand-rolled CHECK-RUNS gate is refused (#724)"
else
  bad "a recipe reading check-runs must be refused" "rc=$rc"$'\n'"$OUT"
fi

# --- #1162: the fence may be INDENTED. A ```sh fence nested under a list item carries leading
#     whitespace, and anchoring the fence at column 0 left every such fence UNSCANNED — a hand-rolled
#     gate could hide in one. An indented check-runs rollup must be refused just like a flush-left one.
rc=0; run_on '# bad — a list-nested, indented fence
1. First, find the head SHA.

   ```sh
   gh api "repos/FS-GG/x/commits/$SHA/check-runs" --paginate --slurp | jq "[.[].check_runs[]]"
   ```' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'check runs'; then
  ok "#1162: an INDENTED (list-nested) fence hand-rolling check-runs is refused"
else
  bad "#1162: an indented fence must be scanned, not skipped" "rc=$rc"$'\n'"$OUT"
fi

# --- #1162: the actions/runs matcher must catch the `-f head_sha=` FORM-FIELD shape, which carries no
#     literal `?`. The old `actions/runs\?` required the `?` and missed this call entirely.
rc=0; run_on '# bad — form field, no query string
```sh
gh api repos/FS-GG/x/actions/runs -f head_sha=$SHA --paginate --slurp | jq "[.[].workflow_runs[]]"
```' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'workflow runs'; then
  ok "#1162: actions/runs filtered by \`-f head_sha=\` (no \`?\`) is refused"
else
  bad "#1162: the form-field shape of actions/runs must be caught" "rc=$rc"$'\n'"$OUT"
fi

# --- #1162: ...but the SINGLE-run object read (`actions/runs/<id>`) is legitimate — the #721 remedy
#     reads `referenced_workflows` off ONE run — and must NOT be flagged. The matcher excludes it.
rc=0; run_on '# ok — a single run object, not the runs collection
```sh
gh api repos/FS-GG/x/actions/runs/$RUN_ID --jq ".referenced_workflows[].sha"
```' || rc=$?
[ "$rc" -eq 0 ] && ok "#1162: a single-run read (\`actions/runs/<id>\`) is not a rollup and passes (#721)" \
                || bad "#1162: the matcher must not flag the legitimate single-run read" "rc=$rc"$'\n'"$OUT"

# --- the refusal must NAME the remedy. A gate that says "no" without saying "do this instead" gets
#     worked around, and the workaround is another copy.
rc=0; run_on '# bad
```sh
gh api "repos/FS-GG/x/actions/runs?head_sha=abc" --paginate
```' || rc=$?
if printf '%s' "$OUT" | grep -q 'fsgg-coord landable'; then
  ok "the refusal names the command to call instead"
else
  bad "a refusal that does not name the remedy just invites a workaround" "$OUT"
fi

# --- PROSE IS NOT CODE. The docs must be able to DESCRIBE the bug — in code spans, in tables, in the
#     lesson itself — or the rule cannot be written down at all, and the next worker rediscovers it
#     from scratch. Only ```sh fences are scanned. This is the leg that keeps the gate USABLE.
rc=0; run_on '# prose
The old gate read `repos/FS-GG/x/actions/runs?head_sha=$SHA` and called green work red.

| endpoint | why it was wrong |
|---|---|
| `commits/<sha>/check-runs` | a superseded run reads as cancelled |

```sh
scripts/fsgg-coord landable <pr> --wait
```' || rc=$?
[ "$rc" -eq 0 ] && ok "PROSE may describe the endpoints — only \`\`\`sh fences are scanned" \
                || bad "the gate must not fire on prose, or the lesson cannot be written down" "rc=$rc"$'\n'"$OUT"

# --- a non-sh fence is not a recipe either (yaml, json, text) --------------------------------------
rc=0; run_on '# yaml
```yaml
on: {pull_request: {paths: ["x"]}}   # commits/<sha>/check-runs is named here, harmlessly
```' || rc=$?
[ "$rc" -eq 0 ] && ok "a non-sh fence is not a hand-rolled gate" \
                || bad "only sh/bash fences are recipes" "rc=$rc"$'\n'"$OUT"

# --- the escape hatch exists, is EXPLICIT, and carries a reason ------------------------------------
# There is no legitimate use today. It exists so that a future need is a DELIBERATE, reviewed act with
# a stated reason, rather than a reason to delete the gate — which is how gates actually die.
rc=0; run_on '# exempt
<!-- landable-exempt: demonstrating the raw API for a doc about the API itself -->
```sh
gh api "repos/FS-GG/x/actions/runs?head_sha=$SHA" --paginate
```' || rc=$?
[ "$rc" -eq 0 ] && ok "an explicitly-exempted fence is allowed (and must state a reason)" \
                || bad "the exemption must work, or the gate gets deleted the first time it is inconvenient" "rc=$rc"$'\n'"$OUT"

# --- AN EMPTY SUBJECT IS A FINDING, NOT A PASS (#266, this gate's own lesson turned on itself) -----
# If the gate is ever pointed at a tree with no recipes — a moved directory, a renamed root — it must
# go RED. A gate that scans nothing and reports success is the entire family of bugs this rule is part
# of, and it would be the funniest possible way for this one to die.
root="$(mktemp -d "$WORK/empty-XXXXXX")"; mkdir -p "$root/scripts"; cp "$GATE" "$root/scripts/"
rc=0; OUT="$(python3 "$root/scripts/check-recipe-landable.py" 2>&1)" || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -qi 'NO recipes'; then
  ok "scanning ZERO recipes is a FAILURE, not a clean pass (#266)"
else
  bad "a gate that scans nothing and reports success is the bug it exists to catch" "rc=$rc"$'\n'"$OUT"
fi

# --- WORKFLOWS, TOO (#737) -------------------------------------------------------------------------
# The last hand-rolled copy of the gate was not a recipe — it was `skill-registry-autofix.yml`, an
# auto-merge bot. #737 converted it to call `landable`, so the root goes in and a SIXTH copy is now
# unwritable in a workflow as well.
#
# A workflow is scanned by PARSING (`jobs.*.steps[*].run`), not by fence, and these legs are the reason:
# this repo's workflows are more comment than code, and they discuss `actions/runs` at length and
# correctly. A whole-file regex would fire on the very prose that explains the rule.
run_wf() {
  local wf="$1" root rc=0
  root="$(mktemp -d "$WORK/repo-XXXXXX")"
  mkdir -p "$root/scripts" "$root/.claude/skills/x" "$root/.github/workflows"
  cp "$GATE" "$root/scripts/"
  printf '# a recipe that does nothing\n' > "$root/.claude/skills/x/SKILL.md"
  printf '%s\n' "$wf" > "$root/.github/workflows/w.yml"
  OUT="$(python3 "$root/scripts/check-recipe-landable.py" 2>&1)" || rc=$?
  return "$rc"
}

# THE RULE, in a workflow: a hand-rolled rollup in a `run:` block is refused.
rc=0; run_wf 'name: w
on: [push]
jobs:
  gate:
    runs-on: ubuntu-latest
    steps:
      - name: Wait for the checks
        run: |
          gh api "repos/FS-GG/x/actions/runs?head_sha=$SHA" --paginate --slurp > runs.json' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'workflow runs'; then
  ok "#737: a workflow that hand-rolls the rollup in a run: block is REFUSED"
else
  bad "#737: the last copy lived in a workflow — the rule must reach one" "rc=$rc"$'\n'"$OUT"
fi

# ...and the check-runs half.
rc=0; run_wf 'name: w
on: [push]
jobs:
  gate:
    runs-on: ubuntu-latest
    steps:
      - run: gh api "repos/FS-GG/x/commits/$SHA/check-runs" --paginate --slurp > c.json' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'check runs'; then
  ok "#737: a workflow reading check-runs itself is REFUSED"
else
  bad "#737: a workflow must not read check-runs itself" "rc=$rc"$'\n'"$OUT"
fi

# THE FALSE-POSITIVE THIS DESIGN EXISTS TO AVOID. A workflow COMMENT naming the endpoints is prose —
# `skill-registry-autofix.yml`'s permissions block explains at length why `actions/runs` needs
# `Actions: read`, and it must keep being able to. The YAML parser drops comments, which is exactly the
# prose/code line the fence rule draws by hand. A gate that fired here would fire on correct behaviour,
# and one that does that is one people learn to skip (#498).
rc=0; run_wf 'name: w
# The merge gate needs actions/runs?head_sha= to tell a superseded check run from a red one, and
# repos/x/commits/y/check-runs is what it scores. Both are prose here, and must stay legal.
on: [push]
jobs:
  gate:
    runs-on: ubuntu-latest
    # actions/runs? in a job comment, too
    steps:
      - name: Gate
        # ...and a step comment mentioning /check-runs
        run: scripts/fsgg-coord landable "$PR" --wait --require registry-coherence' || rc=$?
if [ "$rc" -eq 0 ]; then
  ok "#737: a workflow COMMENT naming the endpoints is prose, and passes — the rule reads run:, not the file"
else
  bad "#737: the rule fired on a comment — it must scan run: blocks, not raw text" "rc=$rc"$'\n'"$OUT"
fi

# The sanctioned form, in a workflow.
rc=0; run_wf 'name: w
on: [push]
jobs:
  gate:
    runs-on: ubuntu-latest
    steps:
      - run: |
          scripts/fsgg-coord landable "$PR" --repo "$GITHUB_REPOSITORY" --wait --require registry-coherence --sha "$SHA"' || rc=$?
[ "$rc" -eq 0 ] && ok "#737: a workflow that CALLS the tool passes" \
                || bad "#737: the sanctioned form must pass in a workflow too" "rc=$rc"$'\n'"$OUT"

# THE ESCAPE HATCH, IN THE SYNTAX OF THE THING BEING SCANNED. A recipe's hatch is an HTML comment before
# the fence; a `run:` block is SHELL, where `<!--` is not a comment but a syntax error — so a workflow's
# hatch is a `#` comment. This leg exists because the first cut of #737 reused the markdown regex here,
# which made the hatch UNWRITABLE in a workflow: the only way past the gate would have been to delete it,
# which is the exact opposite of why the hatch exists.
rc=0; run_wf 'name: w
on: [push]
jobs:
  gate:
    runs-on: ubuntu-latest
    steps:
      - name: A deliberate, reasoned exception
        run: |
          # landable-exempt: this step audits historical runs; it is not a merge gate
          gh api "repos/FS-GG/x/actions/runs?head_sha=$SHA" --paginate' || rc=$?
[ "$rc" -eq 0 ] && ok "#737: an explicitly-exempted run: block is allowed, in SHELL comment syntax" \
                || bad "#737: the hatch must be WRITABLE in a workflow, or the gate gets deleted instead" "rc=$rc"$'\n'"$OUT"

# ...and the hatch must still be a DELIBERATE act: an unexempted step next to an exempted one is refused,
# so the escape is per-step and cannot be smuggled in file-wide.
rc=0; run_wf 'name: w
on: [push]
jobs:
  gate:
    runs-on: ubuntu-latest
    steps:
      - name: Exempted
        run: |
          # landable-exempt: audit only
          gh api "repos/FS-GG/x/actions/runs?head_sha=$SHA"
      - name: NOT exempted
        run: gh api "repos/FS-GG/x/commits/$SHA/check-runs" --paginate' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'NOT exempted'; then
  ok "#737: the hatch is PER-STEP — an unexempted step beside an exempted one is still refused"
else
  bad "#737: one exempted step must not exempt the whole workflow" "rc=$rc"$'\n'"$OUT"
fi

# An UNPARSEABLE workflow is a finding, not a pass — this gate's own lesson (#266). A file we could not
# read is one whose gate we could not check, and calling that clean is the fail-open shape.
rc=0; run_wf 'name: w
on: [push]
jobs: [this is not
   valid: yaml: at all' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -qi 'could not be parsed'; then
  ok "#737: an UNPARSEABLE workflow is a finding, not a pass (#266)"
else
  bad "#737: a workflow the gate could not read must not report as clean" "rc=$rc"$'\n'"$OUT"
fi

# --- AND IT MUST STILL FIRE ON THE REAL REPO. Everything above runs on toy input; this leg is what
#     stops the rule passing forever on fixtures while the real recipes drift out from under it.
rc=0; OUT="$(python3 "$GATE" 2>&1)" || rc=$?
if [ "$rc" -eq 0 ]; then
  ok "the repo's OWN recipes pass the rule — none of them hand-rolls the gate"
else
  bad "a recipe in THIS repo hand-rolls the merge gate" "$OUT"
fi
# ...and it must have actually looked at something. `OK — 0 recipe(s) scanned` would satisfy the leg
# above while checking nothing at all.
n="$(printf '%s' "$OUT" | sed -n 's/.*OK — \([0-9]*\) recipe(s).*/\1/p')"
if [ -n "$n" ] && [ "$n" -gt 0 ]; then
  ok "...and it scanned $n real recipe(s), so that pass is not vacuous"
else
  bad "the real-repo run must report how many recipes it scanned, and it must be > 0" "$OUT"
fi
# ...and the same for the workflows (#737). `and 0 workflow(s) scanned` would pass the leg above while
# leaving the root this item added pointed at nothing — the #266 shape, in the fix for it.
w="$(printf '%s' "$OUT" | sed -n 's/.*and \([0-9]*\) workflow(s).*/\1/p')"
if [ -n "$w" ] && [ "$w" -gt 0 ]; then
  ok "...and $w real workflow(s), so the #737 root is not pointed at nothing"
else
  bad "the real-repo run must report how many workflows it scanned, and it must be > 0" "$OUT"
fi

echo "recipe-landable fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::recipe-landable fixture FAILED"; exit 1; }
echo "recipe-landable fixture — OK"
