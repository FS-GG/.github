#!/usr/bin/env bash
# Fixture for scripts/check-closing-keywords.py — the closing-keyword gate (.github#643, #683).
#
# The gate exists because GitHub closes an issue a PR body says it does NOT close: the parser scans
# for `close|fix|resolve` + a ref and never reads the word "not". So the FAILURE LEGS are the point
# of this fixture. A gate that cannot say NO is the #266 defect it was written to close, and a
# fixture that only ever feeds it clean input would never notice.
#
# TWO LEGS ARE REAL BODIES THAT REALLY CLOSED AN ISSUE, and they are the spine of this file:
#
#   leg 1 — the sentence PR #640 shipped in prose.  It closed #422.
#   leg 9 — the body PR #681 shipped in BACKTICKS.  It closed #422 AGAIN — and #681 is the PR that
#           shipped this very gate, whose guidance told workers that backticks were the remedy.
#
# Between them they pin both halves of the bug: the markdown parser skips code, the commit parser
# does not, and the commit parser is the one that closes the issue. Legs 9-10c hold that line. If any
# of them goes green, the gate is back to certifying the merge that re-closed #422.
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

# ---------------------------------------------------------------------------------------------
# CODE IS NOT A DEFENCE (#683). These four legs were INVERTED by #683 — they used to assert that a
# keyword in code is green, and that assertion was the bug.
#
# The reasoning that produced them was sound about the wrong parser. The MARKDOWN parser does skip
# code, and `closingIssuesReferences` really is empty for a backticked ref. But the thing that CLOSES
# the issue on a squash merge is the COMMIT MESSAGE, and a commit message is PLAIN TEXT: backticks,
# fences and indentation are ordinary characters in it. So every one of these bodies closes an issue,
# and the gate used to certify all four of them green.
#
# If any of these goes green again, the fail-open is back — and it is the one that has already
# happened twice.
# ---------------------------------------------------------------------------------------------

# 9. THE SECOND REAL INSTANCE, and the loudest leg in this file. This is PR #681's body — the PR that
#    shipped the first version of this gate. It wrote its dangerous examples as code, exactly as its
#    own guidance told workers to. `closingIssuesReferences` said #643 and only #643; the squash
#    commit closed #422 for the SECOND time, and #422'"'"'s CLOSED_EVENT names the commit as the closer.
#
#    The gate that let this through was green, correct about markdown, and wrong about what happened.
gate_on 'PR #640 said, in as many words, `It does not close #422`. On merge, GitHub `closed #422`;
the board then stamped it Done.

Closes #643.'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q '#422'; then
  ok "the real #681 body is a FINDING, and it names #422 — a backticked keyword still closes"
else
  bad "#681's backticked 'closed #422' must exit 1 and name #422 (got rc=$RC)" "$OUT"
fi

# 9a. A FENCED block. The form a body uses to quote the bug it is fixing — and the form that reads as
#     most obviously safe, because markdown really does ignore it. The commit parser does not.
gate_on 'The bug, quoted:

```
It does not close #422.
```

Refs #416.'
[ "$RC" = 1 ] && ok "a negated keyword inside a FENCE is a FINDING — the commit message is plain text" \
              || bad "a fenced keyword must exit 1 (got rc=$RC)" "$OUT"

# 10. An INLINE code span — which is precisely how the old recipe told workers to write it. The
#     remedy was the bug.
gate_on 'Never write `does not close #422` — GitHub does not read the word "not".'
[ "$RC" = 1 ] && ok "a negated keyword in an INLINE SPAN is a FINDING — the old remedy was the bug" \
              || bad "an inline-code keyword must exit 1 (got rc=$RC)" "$OUT"

# 10a. An INDENTED block — a quoted log, or the gate's own output pasted back in.
gate_on 'The first draft reported:

    check-closing-keywords: OK
    on merge this PR will close: #422, #123
    ...which was not what anyone wanted.

Neither was intended.'
[ "$RC" = 1 ] && ok "an INDENTED code block is a FINDING — indentation is not a defence either" \
              || bad "an indented code block must exit 1 (got rc=$RC)" "$OUT"

# 10b. A LIST CONTINUATION — indented four spaces, but ordinary prose to markdown. It fired before
#      #683 and it fires after, for a different reason: it is now scanned because EVERYTHING is
#      scanned. Kept because it pins the raw scan's floor — if this ever goes green, the raw scan has
#      stopped happening at all.
gate_on '- the mirror lands first

    and then this closes #123

- the second item'
[ "$RC" = 1 ] && ok "a LIST CONTINUATION still FIRES (it always did, and now everything does)" \
              || bad "a list continuation must exit 1 (got rc=$RC)" "$OUT"

# 10c. THE OTHER DIRECTION — #616, and the check that dropping strip_code entirely would have LOST.
#      A declaration inside a fence is bound by the COMMIT parser (the issue closes) but skipped by
#      the MARKDOWN parser (the PR records no link). The issue closes with nothing on the PR saying
#      so. The gate must model both parsers, not swap one blindness for the other.
gate_on 'Summary of the change.

```
Closes #643.
```'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q 'no link'; then
  ok "a DECLARATION inside code is a FINDING — it closes, but the PR records no link (#616)"
else
  bad "a fenced declaration must exit 1 and warn about the missing link (got rc=$RC)" "$OUT"
fi

# ---------------------------------------------------------------------------------------------
# THE REMEDIES THAT SURVIVE A SQUASH. The gate tells authors to do these, so they must be green —
# a gate that rejects the fix it prescribes teaches workers to ignore it.
# ---------------------------------------------------------------------------------------------

# 10d. REWORD THE VERB. A verb outside GitHub's keyword list binds nothing, in code or out of it.
gate_on 'This does NOT complete #422, and it supersedes #416. A follow-up addresses #295.'
[ "$RC" = 0 ] && ok "REWORDING the verb is green ('does NOT complete' / 'supersedes' / 'addresses')" \
              || bad "the reworded remedy must exit 0 (got rc=$RC)" "$OUT"

# 10e. BREAK THE ADJACENCY. Quoting the number without its `#` leaves nothing for the keyword to bind
#      to — the only way to narrate this bug verbatim without re-committing it.
gate_on 'PR #640 said it does not close 422 — and on merge, GitHub closed 422 anyway.'
[ "$RC" = 0 ] && ok "BREAKING THE ADJACENCY is green (the number quoted without its '#')" \
              || bad "a keyword with no bindable ref must exit 0 (got rc=$RC)" "$OUT"

# 10f. ...but a NEWLINE does NOT break the adjacency, and this leg exists because a draft of this very
#      change said it did. The separator between keyword and ref is `\s+`, and a newline is
#      whitespace — so `closes` ending one line and `#123` opening the next binds exactly as if they
#      were side by side, for GitHub and for this gate alike.
#
#      Had the bad advice shipped, the gate would have REJECTED a body it had just told the author to
#      write — the "gate rejects the fix it prescribes" failure leg 6 guards in the other direction.
#      Review caught it; this leg is what stops it coming back.
gate_on 'The body said it does not close
#422, and GitHub closed it anyway.'
[ "$RC" = 1 ] && ok "a NEWLINE does NOT break the binding — keyword/ref across lines still FIRES" \
              || bad "keyword+ref across a newline must exit 1 (got rc=$RC)" "$OUT"

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
# THE COMMIT MESSAGES ARE THE SUBJECT THAT CLOSES (#684). The body was only ever a PROXY for them —
# exact on a single-commit branch, which is all pnext-item §5 produces, and WRONG the moment a branch
# carries two. These legs are the hole: every one of them is GREEN to a body-only gate.
# ---------------------------------------------------------------------------------------------

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# usage: gate_on_subjects <commits> <body>   -> sets $RC and $OUT
gate_on_subjects() {
  printf '%s' "$1" > "$TMP/commits.txt"
  printf '%s' "$2" > "$TMP/body.md"
  set +e
  OUT="$(python3 "$GATE" --commits "$TMP/commits.txt" --body "$TMP/body.md" 2>&1)"
  RC=$?
  set -e
}

# 14. THE ACCEPTANCE CRITERION, and the leg this whole change exists for. A three-commit branch whose
#     FIRST commit says `fixes #999`. The PR body is built by pnext-item §5 from `git log -1` — the
#     LAST commit only — so #999 appears nowhere in it. On merge the squash message concatenates all
#     three, GitHub reads `fixes #999`, and closes an issue this PR never mentioned.
COMMITS_HOLE='wip: first cut

This fixes #999 as a side effect.

refactor: extract the parser

third commit subject

Closes #684.'
BODY_CLEAN='Closes #684.'

gate_on_subjects "$COMMITS_HOLE" "$BODY_CLEAN"
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q '#999'; then
  ok "a keyword in a NON-FINAL commit is a FINDING, and the gate names #999"
else
  bad "a keyword in a non-final commit must exit 1 and name #999 (got rc=$RC)" "$OUT"
fi

# 14a. ...and the finding must say WHICH text closes it. An author who is told only "you close #999"
#      will go and edit the PR body, which is not the text that closes it, and the gate will still be
#      red. The body they can edit; the commit message they must rewrite the branch to change.
printf '%s' "$OUT" | grep -qi 'COMMIT MESSAGES' \
  && ok "the finding names the COMMIT MESSAGES as the offending subject, not the body" \
  || bad "a commit-message finding must name which subject it came from" "$OUT"

# 14b. THE PROOF THAT THIS WAS A HOLE. The very same branch, audited the way the gate did before
#      #684 — body only — is GREEN. This leg is what the fix is FOR, and if it ever starts failing,
#      the gate has begun reading the body as though it were the commit messages, which it is not.
set +e
OUT="$(printf '%s' "$BODY_CLEAN" | python3 "$GATE" 2>&1)"; RC=$?
set -e
[ "$RC" = 0 ] && ok "the SAME branch is green to a body-only scan — the #684 hole, pinned" \
              || bad "the body-only scan of a clean body must still be green (got rc=$RC)" "$OUT"

# 15. CODE IS NOT A DEFENCE, AND IN A COMMIT MESSAGE THERE IS NOT EVEN A MARKDOWN PARSER TO SKIP IT.
#     A commit message is never rendered: a fence in one is four backtick characters and nothing more.
#     So a fenced keyword here is not "a declaration that will not link" (#616's shape, which is what
#     it means in a BODY) — it is a plain, binding, undeclared close.
gate_on_subjects 'docs: quote the bug

The old recipe told workers to write it as code:

```
closes #422
```

Closes #684.' "$BODY_CLEAN"
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q '#422'; then
  ok "a FENCED keyword in a commit message is a FINDING — there is no markdown parser there"
else
  bad "a fenced keyword in a commit message must exit 1 and name #422 (got rc=$RC)" "$OUT"
fi

# 16. THE GREEN LEG. A well-formed multi-commit branch: exactly one declaration, on a line of its
#     own, in the commit that carries it. A gate that cannot pass this rejects every honest branch,
#     and an author who cannot get green by doing the right thing will find a way around the gate.
gate_on_subjects 'gate: read the text that actually closes

The body was a proxy for the squash message. Refs #683.

tests: pin the multi-commit leg

Closes #684.' 'Closes #684.'
if [ "$RC" = 0 ] && printf '%s' "$OUT" | grep -q 'will close: #684'; then
  ok "an honest multi-commit branch is GREEN, and the gate says it will close #684"
else
  bad "an honest multi-commit branch must exit 0 and report #684 (got rc=$RC)" "$OUT"
fi

# 16a. ...and it must report what EACH subject declares. The two texts are edited independently, and
#      showing them side by side is how an author sees them drift apart — which is all #684 ever was.
printf '%s' "$OUT" | grep -q 'PR body' \
  && ok "the green report names both subjects and what each declares" \
  || bad "with both subjects, the report must name each one" "$OUT"

# 17. AN EMPTY COMMIT-MESSAGE SUBJECT IS NO VERDICT, NOT A PASS. Every PR has at least one commit and
#     every commit has a message, so empty does not mean "clean" — it means the rev range or the
#     clone depth is broken. This is the fail-open that would reopen #684 SILENTLY: the gate would go
#     green having read no commit at all, which is #266's signature exactly.
gate_on_subjects '' "$BODY_CLEAN"
[ "$RC" = 3 ] && ok "an EMPTY --commits file is rc=3 (no verdict), NOT green" \
              || bad "an empty --commits file must exit 3 — reading nothing is not a pass (got rc=$RC)" "$OUT"

# 17a. Whitespace is not a commit message either. `git log` on an empty range emits newlines, so this
#      is the shape the broken-wiring case ACTUALLY takes on a runner — not a zero-byte file.
gate_on_subjects '

' "$BODY_CLEAN"
[ "$RC" = 3 ] && ok "a WHITESPACE-only --commits file is rc=3 — the real shape of an empty rev range" \
              || bad "a whitespace-only --commits file must exit 3 (got rc=$RC)" "$OUT"

# 18. A missing --commits file. The workflow writes it with `git log`; this is "git log failed and
#     nobody noticed", and it must land in the same no-verdict bucket as a missing body.
#
#     The --body file is written HERE rather than inherited from whichever leg ran last. A missing
#     body is ALSO rc=3, so a leg that let its body go missing would still pass — green for a reason
#     that has nothing to do with the --commits file it claims to be testing. A fixture whose legs
#     can pass for the wrong reason is the fail-open this whole file exists to keep out.
printf '%s' "$BODY_CLEAN" > "$TMP/body.md"
set +e
OUT="$(python3 "$GATE" --commits "$HERE/does-not-exist.txt" --body "$TMP/body.md" 2>&1)"; RC=$?
set -e
[ "$RC" = 3 ] && ok "a missing --commits file is rc=3 (no verdict), NOT green" \
              || bad "a missing --commits file must exit 3 (got rc=$RC)" "$OUT"

# 19. THE BODY IS STILL READ. #684 adds a subject; it does not replace one. The body is what builds
#     `closingIssuesReferences` — the PR's visible link — so #616 and #558 still live there, and a
#     finding in the body must still fire when the commit messages are spotless.
gate_on_subjects 'tests: pin the multi-commit leg

Closes #684.' 'This does not close #422.

Closes #684.'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q '#422'; then
  ok "a body finding still fires when the COMMITS are clean — the body scan was not dropped"
else
  bad "the body must still be audited alongside the commits (got rc=$RC)" "$OUT"
fi

# 20. BOTH SUBJECTS CLOSE, so "will close" is the UNION of them. The body is the editable text, so
#     declaring there and nowhere else is the normal thing to do — and GitHub honours the body's
#     `closingIssuesReferences` on merge (#558), so that issue IS closed. Reporting only the commit
#     messages told such an author their PR "closes no issue", one line above printing that the body
#     declared it: two adjacent contradictory sentences, and an under-report of what the merge does.
gate_on_subjects 'gate: read the text that closes

Some prose, and a bare reference. Refs #683.' 'Closes #684.'
if [ "$RC" = 0 ] && printf '%s' "$OUT" | grep -q 'will close: #684'; then
  ok "a declaration in the BODY ALONE still reports 'will close: #684' — the union, not just commits"
else
  bad "a body-only declaration must still be reported as closing #684 (got rc=$RC)" "$OUT"
fi
printf '%s' "$OUT" | grep -q 'closes no issue' \
  && bad "the gate said 'closes no issue' while the body declares #684 — self-contradicting" "$OUT" \
  || ok "...and it does NOT also claim the PR closes nothing"

# ---------------------------------------------------------------------------------------------
# The subjects of this gate are a PR BODY and a branch's COMMIT MESSAGES. There is deliberately no leg that runs it
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
