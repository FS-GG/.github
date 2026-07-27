#!/usr/bin/env bash
# Fixture for scripts/check-ignored-author-coherence.py — the preset's gitIgnoredAuthors and the
# workflows' push identity are ONE fact, and this proves the gate can say NO about it (.github#1538).
#
# THE FAILURE LEGS ARE THE POINT. The defect this gate closes is invisible by construction: the
# workflows keep pushing, Renovate keeps parking, and the only symptom is a bump PR that never
# opens. So a gate that merely passes on the fixed tree proves nothing at all — it is exactly what
# was there before (nothing) with a green tick on top. Every wrong-value shape below was DRIVEN
# THROUGH RENOVATE 43.281.1's REAL isBranchModified while this gate was written, against a fixture
# branch carrying one App-authored commit, with no persisted repository cache:
#
#   gitIgnoredAuthors = [the exact email]      -> isBranchModified = false   RELEASED
#   gitIgnoredAuthors = [the bot LOGIN]        -> true   PARKED
#   gitIgnoredAuthors = [a RE-CASED email]     -> true   PARKED
#   gitIgnoredAuthors = []                     -> true   PARKED
#
# and every one of the last three is a config `renovate-config-validator` calls VALID. That is why
# the schema run in this gate's workflow is necessary and NOT sufficient, and why legs 4-9 exist.
#
# THE VACUOUS-PASS LEGS (5, 6, 7, 8, 15, 17, 18) are #1538 criterion 2: an absent key, an empty
# list, an unreadable preset and a tree with no subject are each states in which "every configured
# identity is declared" is TRUE and the org is entirely broken. Each must be a NO VERDICT (3).
set -u

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-ignored-author-coherence.py"
PY="${PYTHON:-python3}"

EMAIL='297630107+${{ steps.app-token.outputs.app-slug }}[bot]@users.noreply.github.com'
CANON='297630107+fs-gg-cross-repo-dispatch[bot]@users.noreply.github.com'
LOGIN='fs-gg-cross-repo-dispatch[bot]'
RECASED='297630107+FS-GG-Cross-Repo-Dispatch[bot]@users.noreply.github.com'

fails=0
ok() { printf '  ok   — %s\n' "$1"; }
bad() { printf '  FAIL — %s\n     %s\n' "$1" "${2-}"; fails=$((fails + 1)); }

# A tree with exactly the shape the gate reads: a preset, and one pushing workflow.
mktree() {
  local d
  d="$(mktemp -d)"
  mkdir -p "$d/.github/workflows"
  printf '{\n  "%s": [\n    "%s"\n  ]\n}\n' "gitIgnoredAuthors" "$CANON" >"$d/default.json"
  cat >"$d/.github/workflows/kit-materialize.yml" <<YML
name: kit-materialize
jobs:
  materialize:
    runs-on: ubuntu-latest
    steps:
      - name: Commit and push
        run: |
          set -euo pipefail
          git config user.name  "\${{ steps.app-token.outputs.app-slug }}[bot]"
          git config user.email "$EMAIL"
          git push
YML
  printf '%s' "$d"
}

# Replace the preset's whole gitIgnoredAuthors value with the literal given.
preset() { printf '{\n  "gitIgnoredAuthors": %s\n}\n' "$2" >"$1/default.json"; }

# Add one more pushing workflow, with the identity line given.
pusher() {
  local d="$1" name="$2" line="$3"
  cat >"$d/.github/workflows/$name.yml" <<YML
name: $name
jobs:
  push:
    runs-on: ubuntu-latest
    steps:
      - run: |
          set -euo pipefail
          $line
          git push
YML
}

run_gate() { "$PY" "$GATE" --root "$1" >/dev/null 2>&1; printf '%s' "$?"; }
gate_says() { "$PY" "$GATE" --root "$1" 2>&1; }

expect() {
  local tree="$1" want="$2" what="$3" rc
  rc="$(run_gate "$tree")"
  if [ "$rc" = "$want" ]; then ok "$what"; else bad "$what" "expected exit $want, got $rc"; fi
}

printf 'ignored-author-coherence fixture\n'

# --------------------------------------------------------------------------------------------
# 1. THE SHIPPED TREE IS GREEN — and it is checked FIRST, so a gate that only ever passes on
#    synthetic repos while the real preset has drifted is not a state this suite can reach.
rc="$(run_gate "$ROOT")"
[ "$rc" = "0" ] && ok "the SHIPPED tree is green" || bad "the shipped tree must be green" "exit $rc"

# The shipped tree must also actually HAVE a subject — a green over zero identities would be the
# vacuous pass, and the count is the one line a reader uses to decide whether the gate looked.
out="$(gate_says "$ROOT")"
case "$out" in
  *"0 configured push identit"*) bad "the shipped tree must have subjects" "$out" ;;
  *"configured push identit"*) ok "the shipped tree reports its subject count: ${out#*OK — }" ;;
  *) bad "the shipped tree's summary must name a subject count" "$out" ;;
esac

# 2. A MINIMAL COHERENT TREE IS GREEN.
t="$(mktree)"
expect "$t" 0 "a coherent preset + workflow is green"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# 3. THE WORKFLOW'S EMAIL DRIFTS. #1538 criterion 4, mutation one: the bot USER id is retyped. The
#    address still looks entirely plausible and Renovate matches NOTHING.
t="$(mktree)"
sed -i 's/297630107+/1234567+/' "$t/.github/workflows/kit-materialize.yml"
expect "$t" 1 "a workflow email whose bot-user id drifted is RED"
out="$(gate_says "$t")"
case "$out" in
  *"kit-materialize.yml:"[0-9]*"1234567+"*) ok "the finding names the file, the LINE and the VALUE" ;;
  *) bad "the finding must name file, line and value" "$out" ;;
esac
rm -rf "$t"

# 4. THE PRESET IS CHANGED TO THE BOT LOGIN. #1538 criterion 4, mutation two — and the one that
#    matters most, because it is the plausible wrong fix: it reads like the identity, it validates
#    clean, and (MEASURED) it releases nothing.
t="$(mktree)"
preset "$t" "[\"$LOGIN\"]"
expect "$t" 1 "the bot LOGIN in place of the email is RED"
rm -rf "$t"

# 5. THE PRESET ENTRY IS RE-CASED. Set.delete is case-SENSITIVE; measured PARKED.
t="$(mktree)"
preset "$t" "[\"$RECASED\"]"
expect "$t" 1 "a re-cased preset entry is RED (the match is exact)"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# THE VACUOUS-PASS FAMILY. Each of these is a tree in which "every configured identity is declared"
# is TRUE and every Renovate branch in the org is parked. NO VERDICT (3), never green.

# 6. THE KEY IS GONE. #1538 criterion 4, mutation three.
t="$(mktree)"
printf '{\n  "extends": ["config:recommended"]\n}\n' >"$t/default.json"
expect "$t" 3 "a preset with NO gitIgnoredAuthors key is a NO VERDICT, not a pass"
rm -rf "$t"

# 7. THE KEY IS AN EMPTY LIST. Renovate reads this identically to an absent key
#    (`config.ignoredAuthors = gitIgnoredAuthors ?? []`), so the gate must too.
t="$(mktree)"
preset "$t" "[]"
expect "$t" 3 "an EMPTY gitIgnoredAuthors is a NO VERDICT"
rm -rf "$t"

# 8. THE PRESET WILL NOT PARSE.
t="$(mktree)"
printf '{ "gitIgnoredAuthors": [ \n' >"$t/default.json"
expect "$t" 3 "an unparsable preset is a NO VERDICT"
rm -rf "$t"

# 9. THE PRESET IS NOT THERE AT ALL. A failed read is not a clean tree.
t="$(mktree)"
rm -f "$t/default.json"
expect "$t" 3 "a MISSING preset is a NO VERDICT"
rm -rf "$t"

# 10. THE KEY HOLDS A NON-STRING.
t="$(mktree)"
preset "$t" "[null]"
expect "$t" 3 "a non-string gitIgnoredAuthors entry is a NO VERDICT"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# THE SUBJECT IS DERIVED, NOT A LIST OF WORKFLOW NAMES. #1538 was filed naming TWO workflows; the
# repo has FOUR. A fifth must be graded the day it is written, with no edit to the gate.

# 11. A THIRD PUSHING WORKFLOW, COPY-PASTED AND EDITED. The exact hazard #1538 names.
t="$(mktree)"
pusher "$t" "new-autofix" "git config user.email \"9999+other-app[bot]@users.noreply.github.com\""
expect "$t" 1 "a NEW workflow pushing under an undeclared identity is RED"
out="$(gate_says "$t")"
case "$out" in
  *new-autofix.yml*) ok "the finding names the NEW workflow, which no list here mentions" ;;
  *) bad "the finding must name the new workflow" "$out" ;;
esac
rm -rf "$t"

# 12. THE ENV-VAR ROUTE IS ALSO THE SUBJECT. Renovate reads %ae AND %ce; a gate that only knew
#     `git config` could be sidestepped by one line, silently.
t="$(mktree)"
pusher "$t" "env-pusher" "export GIT_AUTHOR_EMAIL=nobody@example.invalid"
expect "$t" 1 "GIT_AUTHOR_EMAIL set to an undeclared identity is RED"
rm -rf "$t"

t="$(mktree)"
pusher "$t" "env-pusher" "export GIT_COMMITTER_EMAIL=$CANON"
expect "$t" 0 "GIT_COMMITTER_EMAIL set to the DECLARED identity is green"
rm -rf "$t"

# 13. THE `git -c` PER-INVOCATION SPELLING IS ALSO THE SUBJECT.
t="$(mktree)"
pusher "$t" "dash-c" "git -c user.email=nobody@example.invalid commit --allow-empty -m x"
expect "$t" 1 "\`git -c user.email=\` set to an undeclared identity is RED"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# A MENTION IS NOT A USE (#683) — and this is not hypothetical: all four real subject workflows
# carry long comment blocks about this exact address, and default.json quotes the spelling.

# 14. A COMMENT IS NOT AN ASSIGNMENT.
t="$(mktree)"
pusher "$t" "commented" "# git config user.email \"nobody@example.invalid\"  <- do not do this"
expect "$t" 0 "a COMMENT naming a wrong address is not a finding"
rm -rf "$t"

# 15. ...BUT A TREE WHOSE ONLY OCCURRENCE IS A COMMENT HAS NO SUBJECT AT ALL. This is the leg that
#     stops leg 14's exemption from becoming a hole: the gate must not go green over a tree it
#     graded nothing in.
t="$(mktree)"
rm -f "$t/.github/workflows/kit-materialize.yml"
pusher "$t" "commented" "# git config user.email \"nobody@example.invalid\""
expect "$t" 3 "a tree whose ONLY occurrence is a comment is a NO VERDICT (nothing graded)"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# WHAT THE GATE REFUSES TO READ. "I could not look" is never "I looked, and it is fine" (#266).

# 16. THE ADDRESS HIDDEN BEHIND A VARIABLE. Not a finding — the workflow may be perfectly correct —
#     but not a pass either, because the gate cannot tell.
t="$(mktree)"
pusher "$t" "opaque" "git config user.email \"\$BOT_EMAIL\""
expect "$t" 3 "an address hidden behind a variable is a NO VERDICT, not a green"
rm -rf "$t"

# 17. A PLACEHOLDER MAY NOT SWALLOW THE ADDRESS'S STRUCTURE. `${{ … }}@users.noreply.github.com`
#     has a literal `@`, so it is graded — and it must NOT match, because the slug class excludes
#     `+`. Without that narrowness the gate would pass anything ending in the right domain.
t="$(mktree)"
pusher "$t" "wide" "git config user.email \"\${{ steps.x.outputs.y }}@users.noreply.github.com\""
expect "$t" 1 "a placeholder cannot swallow the \`+\` and match by accident"
rm -rf "$t"

# 18. `git commit --author` IS REFUSED RATHER THAN HALF-READ.
t="$(mktree)"
pusher "$t" "authored" "git commit --author=\"x <$CANON>\" --allow-empty -m x"
expect "$t" 3 "\`git commit --author\` is REFUSED, not silently skipped"
rm -rf "$t"

# 19. A LINE THAT TOUCHES THE SUBJECT AND IS NOT ANY KNOWN SPELLING IS REFUSED, NOT SKIPPED.
t="$(mktree)"
pusher "$t" "weird" "eval \"git config user.email \$SOMETHING\""
expect "$t" 3 "an identity set through \`eval\` is REFUSED, not skipped"
rm -rf "$t"

# 20. AN UNTERMINATED QUOTE IS A NO VERDICT. The gate tokenises with shlex; a line it cannot
#     tokenise is a line it has not read, and a line it has not read is never a pass.
t="$(mktree)"
pusher "$t" "unbalanced" "git config user.email \"$CANON"
expect "$t" 3 "a line that will not tokenise is a NO VERDICT"
rm -rf "$t"

# 21. THE QUOTING AND CHAINING A REGEX WOULD HAVE MISREAD. `shlex` strips the quotes around the KEY
#     as well as the value and stops at the `&&`, so this is the declared identity and not a
#     finding. A regex cutting `(?P<value>.+)$` out of the line would have reported
#     `"…" && echo done` as the address — a confident finding about a correct line.
t="$(mktree)"
pusher "$t" "chained" "git config \"user.email\" \"$CANON\" && echo done"
expect "$t" 0 "a quoted key and a chained \`&&\` are READ, not misreported"
rm -rf "$t"

# 22. A TRAILING SHELL COMMENT IS NOT PART OF THE ADDRESS. Same class as leg 21 and the reason the
#     value is tokenised rather than sliced.
t="$(mktree)"
pusher "$t" "trailing" "git config user.email \"$CANON\"  # the org App"
expect "$t" 0 "a trailing shell comment is not read as part of the address"
rm -rf "$t"

# 23. A STEP `name:` IS NOT SHELL. This is the structural half of #683: the gate walks the YAML node
#     tree and reads only `run:` scalars and `env:` mappings, so a step named after the thing it
#     configures is invisible to it. A whole-file regex would have refused this workflow — and a
#     gate that reds a step title is one that gets disabled.
t="$(mktree)"
cat >"$t/.github/workflows/named.yml" <<'YML'
name: named
jobs:
  push:
    runs-on: ubuntu-latest
    steps:
      - name: Configure git user.email for the bot
        run: |
          git push
YML
expect "$t" 0 "a step NAME mentioning user.email is not an assignment"
rm -rf "$t"

# 24. `git log --author=` IS A FILTER, NOT AN ASSIGNMENT. The `--author` screen exists so the
#     `git commit --author` refusal can fire at all; it must not drag in the query form, or the
#     gate would be demanding a change to a question it has no part in.
t="$(mktree)"
pusher "$t" "querying" "git log --author=someone@example.invalid --oneline"
expect "$t" 0 "\`git log --author=\` is not an identity assignment"
rm -rf "$t"

# 25. THE DECIDABLE READ IS EXEMPT. `git config user.email` with nothing after it PRINTS the value
#     and cannot set one, so refusing it would be the gate demanding a change to a line that
#     configures nothing — and the first thing a wrongly-red gate teaches is that it is noise
#     (#698). The exemption is drawn where the TOKENS settle it, which is why leg 19's `eval` and
#     leg 26's `echo` are still refused: those are not decidable, they merely look harmless.
t="$(mktree)"
pusher "$t" "reading" "git config user.email"
expect "$t" 0 "the value-less READ form is not an assignment"
rm -rf "$t"

# 26. ...AND AN `echo` THAT INTERPOLATES THE KEY IS STILL REFUSED, deliberately. It could be a
#     write behind a substitution and nothing in the line says otherwise. This leg exists so the
#     boundary of leg 25 is a decision on the record rather than an accident of implementation.
t="$(mktree)"
pusher "$t" "echoing" "echo \"identity: \$(git config user.email)\""
expect "$t" 3 "an \`echo\` interpolating the key is REFUSED, not assumed benign"
rm -rf "$t"

# 27. AN `env:` KEY IS AN ASSIGNMENT WITH NO SHELL AT ALL, and is graded from the YAML mapping.
t="$(mktree)"
cat >"$t/.github/workflows/env-block.yml" <<'YML'
name: env-block
jobs:
  push:
    runs-on: ubuntu-latest
    env:
      GIT_COMMITTER_EMAIL: nobody@example.invalid
    steps:
      - run: git push
YML
expect "$t" 1 "an \`env:\` GIT_COMMITTER_EMAIL naming an undeclared identity is RED"
rm -rf "$t"

# 20. A FINDING IS NOT SWALLOWED BY A REFUSAL ELSEWHERE. One unreadable line must not turn a real
#     drift into a no-verdict — a reader would repair the wrong thing.
t="$(mktree)"
sed -i 's/297630107+/1234567+/' "$t/.github/workflows/kit-materialize.yml"
pusher "$t" "opaque" "git config user.email \"\$BOT_EMAIL\""
expect "$t" 1 "a real drift still reports as a FINDING when another line is unreadable"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# 21. AN UNUSED PRESET ENTRY IS REPORTED, NEVER GRADED. default.json is ORG-SHARED: an entry may
#     name an identity that pushes from a workflow in a repository this gate cannot read, so
#     redding it would be this repo asserting something about a tree it has not looked at.
t="$(mktree)"
preset "$t" "[\"$CANON\", \"someone-else[bot]@users.noreply.github.com\"]"
expect "$t" 0 "an UNUSED declared entry is not a finding"
out="$(gate_says "$t")"
case "$out" in
  *"unused here"*someone-else*) ok "the unused entry is REPORTED in the summary" ;;
  *) bad "an unused entry must be named in the summary" "$out" ;;
esac
rm -rf "$t"

# 22. NO WORKFLOWS AT ALL.
t="$(mktree)"
rm -rf "$t/.github/workflows"
expect "$t" 3 "a tree with no workflows is a NO VERDICT"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# THE SCHEMA HALF (#1538 criterion 3), WITH ITS OWN NEGATIVE CONTROL.
#
# A validator that ran but asserted nothing would be worse than no validator, so this never trusts
# an exit 0 without first proving the same binary reds a config it should. And when the binary is
# absent the legs are SKIPPED LOUDLY and counted as neither pass nor fail — a tooling failure
# reported as a passing check is the thing this whole suite exists to prevent.
VALIDATOR="${RENOVATE_CONFIG_VALIDATOR:-}"
if [ -z "$VALIDATOR" ] && command -v renovate-config-validator >/dev/null 2>&1; then
  VALIDATOR="$(command -v renovate-config-validator)"
fi

if [ -z "$VALIDATOR" ]; then
  printf '  SKIP — renovate-config-validator not available; the schema legs did NOT run.\n'
  printf '         (Set RENOVATE_CONFIG_VALIDATOR=<path>. ignored-author-coherence.yml does.)\n'
else
  v="$(mktemp -d)"

  # 23. THE SHIPPED PRESET VALIDATES.
  if "$VALIDATOR" --no-global "$ROOT/default.json" >/dev/null 2>&1; then
    ok "the shipped default.json passes renovate-config-validator"
  else
    bad "the shipped default.json must validate" "$("$VALIDATOR" --no-global "$ROOT/default.json" 2>&1)"
  fi

  # 24. NEGATIVE CONTROL: the same binary reds an invented option. Without this leg, a validator
  #     that silently no-opped would read exactly like a clean preset.
  "$PY" - "$ROOT/default.json" "$v/invalid.json" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1], encoding="utf-8"))
doc["gitIgnoredAuthorz"] = doc.pop("gitIgnoredAuthors", [])
json.dump(doc, open(sys.argv[2], "w", encoding="utf-8"), indent=2)
PY
  if "$VALIDATOR" --no-global "$v/invalid.json" >/dev/null 2>&1; then
    bad "the validator must RED an invented option" "it exited 0 — it is asserting nothing"
  else
    ok "negative control: the validator REDS an invented option (\`gitIgnoredAuthorz\`)"
  fi

  # 25. THE VALIDATOR'S BLIND SPOT, ASSERTED RATHER THAN ASSUMED. This is why the coherence gate
  #     above exists and why leg 24's green must never be read as "the preset is correct": the bot
  #     LOGIN in place of the email parks every branch in the org and validates perfectly clean.
  #     If a future renovate ever DID catch this, this leg reds and the docstring gets rewritten —
  #     which is the right outcome either way.
  "$PY" - "$ROOT/default.json" "$v/login.json" "$LOGIN" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1], encoding="utf-8"))
doc["gitIgnoredAuthors"] = [sys.argv[3]]
json.dump(doc, open(sys.argv[2], "w", encoding="utf-8"), indent=2)
PY
  if "$VALIDATOR" --no-global "$v/login.json" >/dev/null 2>&1; then
    ok "recorded: the validator is BLIND to the bot-login value (so the coherence gate is required)"
  else
    bad "the validator's blind spot has changed" "it now reds the login form — update the docstring"
  fi

  rm -rf "$v"
fi

printf '\n'
if [ "$fails" -eq 0 ]; then
  printf 'ignored-author-coherence fixture: OK\n'
  exit 0
fi
printf 'ignored-author-coherence fixture: %d FAILURE(S)\n' "$fails"
exit 1
