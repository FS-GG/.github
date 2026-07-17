#!/usr/bin/env bash
# Fixture for scripts/check-recipe-followup.py — the "a recipe NAMES `fsgg-coord followup`, it does not
# HAND-ROLL the queue file" rule (.github#1073, the remaining half of #1063).
#
# The rule exists because #1061's ten lines of queue shell were wrong four ways on first writing, and a
# hand review caught them: nothing executes a recipe, so nothing tests one (#724, one verb over). This
# fixture is what stops the GATE itself going the same way — a gate with no test is exactly epic #266.
#
# It drives the REAL script against throwaway recipe/workflow trees, and — crucially — against the REPO'S
# OWN recipes, so a fixture that passes on toy input cannot hide a rule that no longer fires on the real
# thing.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-recipe-followup.py"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "recipe-followup fixture — gate='$GATE'"

[ -f "$GATE" ] || { bad "the gate exists" "missing $GATE"; echo "::error::recipe-followup FAILED"; exit 1; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/recipe-followup-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# run_on <markdown> -> rc; combined output in $OUT. Builds a throwaway repo whose layout the gate
# recognises (scripts/ + .claude/skills/x/SKILL.md) and runs the REAL gate inside it.
run_on() {
  local md="$1" root rc=0
  root="$(mktemp -d "$WORK/repo-XXXXXX")"
  mkdir -p "$root/scripts" "$root/.claude/skills/x" "$root/.agents/skills" "$root/docs/coordination"
  cp "$GATE" "$root/scripts/"
  printf '%s\n' "$md" > "$root/.claude/skills/x/SKILL.md"
  OUT="$(python3 "$root/scripts/check-recipe-followup.py" 2>&1)" || rc=$?
  return "$rc"
}

# --- the shape the rule mandates: NAME the verb ---------------------------------------------------
rc=0; run_on '# ok
```sh
scripts/fsgg-coord followup add FS-GG/game#5
next="$(scripts/fsgg-coord followup pop)"; rc=$?
```' || rc=$?
[ "$rc" -eq 0 ] && ok "a recipe that CALLS \`fsgg-coord followup\` passes" \
                || bad "the sanctioned form must pass" "rc=$rc"$'\n'"$OUT"

# --- THE RULE: a hand-rolled append to the queue file is refused -----------------------------------
rc=0; run_on '# bad
```sh
echo "FS-GG/game#5" >> "${FSGG_FOLLOWUPS:-$HOME/.fsgg-followups-$FSGG_WORKER}"
```' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'follow-up queue'; then
  ok "a hand-rolled append to the queue file is refused (#1073)"
else
  bad "a recipe writing the raw queue file must be refused" "rc=$rc"$'\n'"$OUT"
fi

# --- ...and a hand-rolled pop (grep/sed on the file) is refused too, via the same path signal ------
rc=0; run_on '# bad
```sh
q="$HOME/.fsgg-followups-$FSGG_WORKER"
next="$(grep -m1 . "$q")"; sed -i "0,/./{/./d}" "$q"
```' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'follow-up queue'; then
  ok "a hand-rolled pop that names the queue file is refused"
else
  bad "a recipe grep/sed-ing the raw queue file must be refused" "rc=$rc"$'\n'"$OUT"
fi

# --- #1162: the fence may be INDENTED. A ```sh fence nested under a list item carries leading
#     whitespace, and anchoring at column 0 left every such fence unscanned. An indented hand-rolled
#     queue write must be refused just like a flush-left one.
rc=0; run_on '# bad — a list-nested, indented fence
1. Record the follow-up.

   ```sh
   echo "FS-GG/game#5" >> "$HOME/.fsgg-followups-$FSGG_WORKER"
   ```' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'follow-up queue'; then
  ok "#1162: an INDENTED (list-nested) fence hand-rolling the queue is refused"
else
  bad "#1162: an indented fence must be scanned, not skipped" "rc=$rc"$'\n'"$OUT"
fi

# --- the refusal must NAME the remedy. A gate that says "no" without "do this instead" gets worked
#     around, and the workaround is another copy.
rc=0; run_on '# bad
```sh
cat "$HOME/.fsgg-followups-$FSGG_WORKER"
```' || rc=$?
if printf '%s' "$OUT" | grep -q 'fsgg-coord followup'; then
  ok "the refusal names the verb to call instead"
else
  bad "a refusal that does not name the remedy just invites a workaround" "$OUT"
fi

# --- PROSE IS NOT CODE. The docs must be able to DESCRIBE the retired idiom — in code spans and
#     tables — or the lesson cannot be written down. Only ```sh fences are scanned.
rc=0; run_on '# prose
The old idiom was `echo ... >> "${FSGG_FOLLOWUPS:-$HOME/.fsgg-followups-$FSGG_WORKER}"`, wrong four ways.

| step | old shell | now |
|---|---|---|
| pop | `sed -i` on `.fsgg-followups-<worker>` | `followup pop` |

```sh
scripts/fsgg-coord followup pop
```' || rc=$?
[ "$rc" -eq 0 ] && ok "PROSE may describe the old queue file — only \`\`\`sh fences are scanned" \
                || bad "the gate must not fire on prose, or the lesson cannot be written down" "rc=$rc"$'\n'"$OUT"

# --- the escape hatch exists, is EXPLICIT, and carries a reason ------------------------------------
rc=0; run_on '# exempt
<!-- followup-exempt: showing the raw file for a doc about the on-disk format itself -->
```sh
cat "$HOME/.fsgg-followups-$FSGG_WORKER"
```' || rc=$?
[ "$rc" -eq 0 ] && ok "an explicitly-exempted fence is allowed (and must state a reason)" \
                || bad "the exemption must work, or the gate gets deleted the first time it is inconvenient" "rc=$rc"$'\n'"$OUT"

# --- AN EMPTY SUBJECT IS A FINDING, NOT A PASS (#266, this gate's own lesson turned on itself) -----
root="$(mktemp -d "$WORK/empty-XXXXXX")"; mkdir -p "$root/scripts"; cp "$GATE" "$root/scripts/"
rc=0; OUT="$(python3 "$root/scripts/check-recipe-followup.py" 2>&1)" || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -qi 'NO recipes'; then
  ok "scanning ZERO recipes is a FAILURE, not a clean pass (#266)"
else
  bad "a gate that scans nothing and reports success is the bug it exists to catch" "rc=$rc"$'\n'"$OUT"
fi

# --- WORKFLOWS, TOO. A workflow's shell lives in `run:` blocks; a hand-rolled queue there is refused,
#     and its COMMENTS are prose the YAML parser drops.
run_wf() {
  local wf="$1" root rc=0
  root="$(mktemp -d "$WORK/repo-XXXXXX")"
  mkdir -p "$root/scripts" "$root/.claude/skills/x" "$root/.github/workflows"
  cp "$GATE" "$root/scripts/"
  printf '# a recipe that does nothing\n' > "$root/.claude/skills/x/SKILL.md"
  printf '%s\n' "$wf" > "$root/.github/workflows/w.yml"
  OUT="$(python3 "$root/scripts/check-recipe-followup.py" 2>&1)" || rc=$?
  return "$rc"
}

rc=0; run_wf 'name: w
on: [push]
jobs:
  q:
    runs-on: ubuntu-latest
    steps:
      - name: Drain the queue
        run: |
          echo "FS-GG/game#5" >> "$HOME/.fsgg-followups-$FSGG_WORKER"' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q 'follow-up queue'; then
  ok "a workflow that hand-rolls the queue in a run: block is REFUSED"
else
  bad "the rule must reach a workflow run: block" "rc=$rc"$'\n'"$OUT"
fi

# A workflow COMMENT naming the file is prose, and passes — the rule reads run:, not the file.
rc=0; run_wf 'name: w
# The old shell wrote $HOME/.fsgg-followups-<worker>; the verb owns the path now.
on: [push]
jobs:
  q:
    runs-on: ubuntu-latest
    steps:
      - name: Pop
        run: scripts/fsgg-coord followup pop' || rc=$?
[ "$rc" -eq 0 ] && ok "a workflow COMMENT naming the queue file is prose, and passes" \
                || bad "the rule fired on a comment — it must scan run: blocks, not raw text" "rc=$rc"$'\n'"$OUT"

# An UNPARSEABLE workflow is a finding, not a pass (#266).
rc=0; run_wf 'name: w
on: [push]
jobs: [this is not
   valid: yaml: at all' || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -qi 'could not be parsed'; then
  ok "an UNPARSEABLE workflow is a finding, not a pass (#266)"
else
  bad "a workflow the gate could not read must not report as clean" "rc=$rc"$'\n'"$OUT"
fi

# --- AND IT MUST STILL FIRE ON THE REAL REPO. Everything above runs on toy input; this leg stops the
#     rule passing forever on fixtures while the real recipes drift out from under it.
rc=0; OUT="$(python3 "$GATE" 2>&1)" || rc=$?
if [ "$rc" -eq 0 ]; then
  ok "the repo's OWN recipes pass the rule — none of them hand-rolls the queue"
else
  bad "a recipe in THIS repo hand-rolls the follow-up queue" "$OUT"
fi
# ...and it must have actually looked at something — `OK — 0 recipe(s)` would satisfy the leg above
# while checking nothing.
n="$(printf '%s' "$OUT" | sed -n 's/.*OK — \([0-9]*\) recipe(s).*/\1/p')"
if [ -n "$n" ] && [ "$n" -gt 0 ]; then
  ok "...and it scanned $n real recipe(s), so that pass is not vacuous"
else
  bad "the real-repo run must report how many recipes it scanned, and it must be > 0" "$OUT"
fi
w="$(printf '%s' "$OUT" | sed -n 's/.*and \([0-9]*\) workflow(s).*/\1/p')"
if [ -n "$w" ] && [ "$w" -gt 0 ]; then
  ok "...and $w real workflow(s), so the workflow root is not pointed at nothing"
else
  bad "the real-repo run must report how many workflows it scanned, and it must be > 0" "$OUT"
fi

echo "recipe-followup fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::recipe-followup fixture FAILED"; exit 1; }
echo "recipe-followup fixture — OK"
