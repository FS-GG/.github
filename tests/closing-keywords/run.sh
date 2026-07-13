#!/usr/bin/env bash
# Fixture for scripts/check-closing-keywords.py — the negated-closing-keyword gate (.github#643).
#
# The gate exists because GitHub closes an issue a PR body says it does NOT close: the parser scans
# for `close|fix|resolve` + a ref and never reads the word "not". So the FAILURE LEGS are the point
# of this fixture. A gate that cannot say NO is the #266 defect it was written to close, and a
# fixture that only ever feeds it clean input would never notice.
#
# Leg 1 is the REAL INSTANCE, byte-for-byte: the sentence PR #640 actually shipped, which actually
# closed #422. Leg 2 is the body #640 was actually remediated TO. Between them they pin the gate to
# the bug it was written for and to the fix the recipe prescribes — not to strings invented here.
#
# Every leg asserts the EXIT CODE (the gate's contract). No leg greps the gate's prose for a verdict.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-closing-keywords.py"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# usage: gate_on <body>   -> sets $RC and $OUT
gate_on() {
  set +e
  OUT="$(printf '%s' "$1" | python3 "$GATE" 2>&1)"
  RC=$?
  set -e
}

echo "closing-keywords fixture"

# ---------------------------------------------------------------------------------------------
# FAILURE LEGS — each must fire. If any of these goes green the gate is decorative.
# ---------------------------------------------------------------------------------------------

# 1. THE REAL INSTANCE. This is the sentence PR #640 shipped; GitHub closed #422 on merge, the board
#    stamped it Done, and three acceptance criteria went unmet. If the gate cannot catch THIS, it
#    catches nothing that has ever actually happened here.
gate_on '## What this does NOT do

**It does not close #422.** A producer PR that breaks the mirror still cannot be failed *as a PR*.'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q '#422'; then
  ok "the real #640 sentence is a FINDING, and it names #422"
else
  bad "'does not close #422' must exit 1 and name #422 (got rc=$RC)" "$OUT"
fi

# 2. The other forms #643 enumerates. Each is a real way a worker writes "I am not closing this".
for body in \
  'This does not fix #123.' \
  'I could not fix #123 in this PR.' \
  'Unlike #300, this does not close #123.' \
  "It doesn't close #123." \
  'This will not resolve #123.' \
  'We cannot close #123 until the mirror lands.' \
  'This no longer closes #123.' \
  'It never closes #123.'
do
  gate_on "$body"
  if [ "$RC" = 1 ]; then
    ok "negated form is a FINDING: $body"
  else
    bad "negated form must exit 1: $body (got rc=$RC)" "$OUT"
  fi
done

# 2b. THE CASE THAT KILLED THE FIRST DRAFT OF THIS GATE, and the reason it does not merely detect
#     negations. None of these carries a negator; every one of them closes an issue.
#
#     The first draft flagged only negated refs. Run against the body of the very PR that introduced
#     it, it reported "OK — will close #422, #123, #643": the body NARRATED the bug ("GitHub closed
#     #422") and QUOTED an example ("this closes #123"), and both bind. It would have passed the PR
#     that fixes negation while silently re-closing #422 — the exact issue whose wrongful closure IS
#     the bug. If any leg here goes green, that hole is back.
gate_on 'On merge, GitHub closed #422; the board then stamped it Done.'
[ "$RC" = 1 ] && ok "NARRATED past tense ('GitHub closed #422') is a FINDING — no negator needed" \
              || bad "narrated 'GitHub closed #422' must exit 1 (got rc=$RC)" "$OUT"

gate_on 'A loose window would fire on "Nothing was skipped and this closes #123".'
[ "$RC" = 1 ] && ok "a QUOTED example ('this closes #123') is a FINDING — GitHub binds it anyway" \
              || bad "quoted 'this closes #123' must exit 1 (got rc=$RC)" "$OUT"

gate_on 'A follow-up will resolve #123 once the mirror lands.'
[ "$RC" = 1 ] && ok "a DEFERRAL ('a follow-up will resolve #123') is a FINDING" \
              || bad "deferral 'will resolve #123' must exit 1 (got rc=$RC)" "$OUT"

gate_on 'The log said: fixes #123, which is not what we wanted.'
[ "$RC" = 1 ] && ok "a keyword copied out of a LOG line is a FINDING" \
              || bad "'The log said: fixes #123' must exit 1 (got rc=$RC)" "$OUT"

# 3. The keyword vocabulary is GitHub's, not a subset. A gate that knows `close` but not `resolved`
#    is a gate with a hole exactly where somebody's habit lives.
for kw in close closes closed fix fixes fixed resolve resolves resolved; do
  gate_on "This does not $kw #123."
  [ "$RC" = 1 ] && ok "negated keyword '$kw' is a FINDING" \
                || bad "negated keyword '$kw' must exit 1 (got rc=$RC)" "$OUT"
done

# 4. The ref forms GitHub binds. `owner/repo#n` is how a cross-repo body reads, and it is exactly the
#    shape a `[cross-repo]` finding gets written in.
for ref in '#123' 'GH-123' 'FS-GG/.github#123' 'https://github.com/FS-GG/.github/issues/123'; do
  gate_on "This does not close $ref."
  [ "$RC" = 1 ] && ok "negated ref form is a FINDING: $ref" \
                || bad "negated ref form must exit 1: $ref (got rc=$RC)" "$OUT"
done

# 5. A colon between keyword and ref. `Closes: #10` is a form people write, and GitHub honours it —
#    so a NEGATED one must still be caught. BOTH spacings: requiring whitespace after the colon made
#    `close:#123` invisible and the gate reported GREEN on a body that closes an issue — a fail-OPEN,
#    the one direction this gate may never take.
gate_on 'This does not close: #123.'
[ "$RC" = 1 ] && ok "negated 'close: #N' is a FINDING" \
              || bad "negated 'close: #N' must exit 1 (got rc=$RC)" "$OUT"

gate_on 'This does not close:#123.'
[ "$RC" = 1 ] && ok "negated 'close:#N' (colon, NO space) is a FINDING — not a hole" \
              || bad "negated 'close:#N' must exit 1 (got rc=$RC)" "$OUT"

# 5b. ...and the same spacing must still be accepted as a DECLARATION, or the fix for the hole above
#     would turn a legitimate `Closes:#7` line into a false finding.
gate_on 'Closes:#7'
if [ "$RC" = 0 ] && printf '%s' "$OUT" | grep -q 'will close: #7'; then
  ok "'Closes:#7' (colon, no space) is a valid declaration"
else
  bad "'Closes:#7' must exit 0 and report #7 (got rc=$RC)" "$OUT"
fi

# ---------------------------------------------------------------------------------------------
# NEGATIVE LEGS — a gate that cries wolf gets ignored, and then it protects nothing.
# ---------------------------------------------------------------------------------------------

# 6. THE REMEDIATED #640 BODY — the fix the recipe prescribes, and what #640 now actually says. If
#    this goes red, the gate is telling workers to write something it will then reject.
gate_on '**This does NOT complete issue #422.** A producer PR that breaks the mirror still cannot be
failed *as a PR*, and landing a correct dual-homed edit still opens a window.

Refs #422, #416, #295.'
[ "$RC" = 0 ] && ok "the remediated #640 body is GREEN ('does NOT complete' + 'Refs')" \
              || bad "the remediated #640 body must exit 0 (got rc=$RC)" "$OUT"

# 7. An ordinary affirmative close. The overwhelmingly common body, and the gate must be silent on
#    it — AND must report what it will close, which is the line an author most needs to see.
gate_on 'gate: reconstruct the scaffold scene edge

Closes #165.'
if [ "$RC" = 0 ] && printf '%s' "$OUT" | grep -q 'will close: #165'; then
  ok "an affirmative 'Closes #165' is green, and the gate SAYS it will close #165"
else
  bad "affirmative close must exit 0 and report the ref (got rc=$RC)" "$OUT"
fi

# 8. The declaration forms a worker actually writes. All are green, and all must REPORT what they
#    close — the declared set is the gate's most useful output, and a body may legitimately close
#    several issues.
gate_on 'Closes: #1, closes #2.'
if [ "$RC" = 0 ] && printf '%s' "$OUT" | grep -q '#1, #2'; then
  ok "'Closes: #1, closes #2.' — keyword repeated — is green and reports BOTH"
else
  bad "'Closes: #1, closes #2.' must exit 0 and report both refs (got rc=$RC)" "$OUT"
fi

# 8a. ...and the form that LOOKS like it declares two and closes ONE. GitHub binds a keyword to the
#     one ref that follows it, so `Closes #1, #2` closes #1 and silently drops #2. Blessing this
#     would make the gate complicit in the under-closing half of the same bug (#558's family): the
#     author declares two, the board hears one, and nothing anywhere mentions the other.
gate_on 'Closes #1, #2.'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q '#2 will NOT be closed'; then
  ok "'Closes #1, #2.' is a FINDING — #2 is bound to nothing and would be silently dropped"
else
  bad "'Closes #1, #2.' must exit 1 and name #2 as dropped (got rc=$RC)" "$OUT"
fi

gate_on 'Summary of the change.

- Closes #7
- Fixes FS-GG/.github#8'
[ "$RC" = 0 ] && ok "bulleted declarations ('- Closes #7') are green" \
              || bad "bulleted declarations must exit 0 (got rc=$RC)" "$OUT"

# 8b. The MESSAGE is what teaches, so pin its two shapes. A negated ref must be told that GitHub
#     cannot read "not"; an undeclared-but-affirmative one must be told it is simply not a
#     declaration. Same verdict, different lesson — and the negated author is the one most sure
#     they are safe, so their finding must name the negator back to them.
gate_on 'This does not close #422.'
printf '%s' "$OUT" | grep -q 'does NOT read the word "not"' \
  && ok "a NEGATED finding names the negation back to the author" \
  || bad "the negated finding must mention the word \"not\"" "$OUT"

gate_on 'On merge, GitHub closed #422.'
printf '%s' "$OUT" | grep -q 'not a declaration' \
  && ok "an UNDECLARED finding says it is not a declaration (no bogus negation claim)" \
  || bad "the undeclared finding must say 'not a declaration'" "$OUT"

# 9. CODE IS NOT PROSE. GitHub does not link inside a fence, so neither may the gate — a body that
#    QUOTES the bug (as #643's own issue body does, and as this repo's docs now do) must not be a
#    finding. Without this, the skill that documents the rule would fail the gate that enforces it.
gate_on 'The bug, quoted:

```
It does not close #422.
```

Refs #422.'
[ "$RC" = 0 ] && ok "a negated keyword inside a fence does NOT fire (GitHub ignores code)" \
              || bad "a fenced negated keyword must exit 0 (got rc=$RC)" "$OUT"

# 10. ...and the same for an inline code span, which is how the rule gets written in a sentence.
gate_on 'Never write `does not close #422` — GitHub does not read the word "not".'
[ "$RC" = 0 ] && ok "a negated keyword in an inline code span does NOT fire" \
              || bad "an inline-code negated keyword must exit 0 (got rc=$RC)" "$OUT"

# 10a. ...and an INDENTED code block, which GitHub also honours as code. A body that quotes a log or
#      a gate's own output this way has done nothing wrong, and must not be failed for it.
#
#      THE BLOCK IS MULTI-LINE ON PURPOSE. The first cut of the indent rule keyed off "the previous
#      line was blank", which blanked the block's FIRST line and scanned every line after it — green
#      on a one-line fixture, and a false finding on the two-line block a real body actually contains.
gate_on 'The first draft reported:

    check-closing-keywords: OK
    on merge this PR will close: #422, #123
    ...which was not what anyone wanted.

Neither was intended.'
[ "$RC" = 0 ] && ok "a MULTI-LINE indented code block does NOT fire (every line of it is code)" \
              || bad "a multi-line indented code block must exit 0 (got rc=$RC)" "$OUT"

# 10b. THE FAIL-OPEN THIS GATE MUST NEVER TAKE. A list item'"'"'s continuation is indented four spaces
#      and is ORDINARY PROSE — GitHub parses it, and closes the issue. If leg 10a'"'"'s indent rule ever
#      grows careless enough to blank this, the gate goes green on a real, silent close. That is the
#      #266 fail-open, inside the gate written to close one.
gate_on '- the mirror lands first

    and then this closes #123

- the second item'
[ "$RC" = 1 ] && ok "a LIST CONTINUATION is prose, not code — an undeclared close in it still FIRES" \
              || bad "a list continuation must NOT be treated as code (got rc=$RC)" "$OUT"

# 11. A body with no reference at all.
gate_on 'A tidy-up with no issue behind it.'
if [ "$RC" = 0 ] && printf '%s' "$OUT" | grep -q 'closes no issue'; then
  ok "a body with no closing ref is green, and says so"
else
  bad "a body with no closing ref must exit 0 (got rc=$RC)" "$OUT"
fi

# 12. A bare mention with no verb — the OTHER remedy the recipe prescribes.
gate_on 'Refs #422. Follow-up to #416.'
[ "$RC" = 0 ] && ok "a bare 'Refs #422' closes nothing and is green" \
              || bad "'Refs #422' must exit 0 (got rc=$RC)" "$OUT"

# ---------------------------------------------------------------------------------------------
# NO-VERDICT LEG — "I could not check" must never read as "I checked, and it's fine" (#266).
# ---------------------------------------------------------------------------------------------

# 13. A --body file that does not exist. The workflow writes that file from the event payload, so
#     this is the shape of "the payload was empty and nobody noticed".
set +e
OUT="$(python3 "$GATE" --body "$HERE/does-not-exist.md" 2>&1)"; RC=$?
set -e
[ "$RC" = 3 ] && ok "a missing --body file is rc=3 (no verdict), NOT green" \
              || bad "a missing --body file must exit 3 (got rc=$RC)" "$OUT"

# ---------------------------------------------------------------------------------------------
# The subject of this gate is a PR BODY, and nothing else. There is deliberately no leg that runs it
# over this repo's docs: a markdown file in the tree is not parsed by GitHub for closing keywords, so
# a doc that narrates `closed #422` in prose closes nothing and is not a defect. Asserting green over
# the docs would be asserting a property the gate does not have, and the day someone wrote an honest
# sentence in one it would fail for a reason that is not a bug.
#
# What grounds this fixture instead is that its two loudest legs are REAL BODIES — the one #640
# shipped (leg 1) and the one it was remediated to (leg 6) — plus leg 2b, which is the live body that
# broke this gate's own first draft. Those are not strings invented here.
# ---------------------------------------------------------------------------------------------

echo
echo "closing-keywords: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
