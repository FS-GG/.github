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

echo "recipe-landable fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::recipe-landable fixture FAILED"; exit 1; }
echo "recipe-landable fixture — OK"
