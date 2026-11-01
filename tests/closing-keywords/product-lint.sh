#!/usr/bin/env bash
# Fixture for the PRODUCT-REPO closing-keyword lint (.github#4730): the `closing-keywords` job of
# .github/workflows/kit-materialize.yml, which every coordination-kit receiver already calls.
#
# Two halves, both offline:
#
#   A. THE RULE, as a matrix. scripts/check-closing-keywords.py already treated every closing keyword
#      next to an issue reference as a finding unless it sat on a declaration line, whether negated or
#      not. This half PINS that for each of the nine GitHub keywords x { plain, negated, contraction,
#      cross-repo, URL }, and pins what must NOT fire (`Refs`, a verb with no reference, a word that is
#      not a GitHub keyword). Origin: FS.GG.Game#731 said "does not close" #730 and closed it.
#
#   B. THE JOB. The real `run:` block of the real job is extracted from kit-materialize.yml and executed
#      against a stub `gh`, so what is tested is the shipped shell, not a copy. It proves: the body is read
#      live (a re-run after an edit judges the edit), the event payload is the fallback, a finding
#      fails the job and names the phrase, `Refs #N` passes, an unclassified exit fails closed, body text
#      cannot open a workflow command, and the `if:` / checkout / env shape cannot drift.
#
# Every leg asserts an exit code or a structural fact. No leg greps prose for a verdict, except where the
# leg's whole point is that the message names the phrase (A-names, B-annotation).
#
# DECISION, CODE SPANS AND FENCES (documented in the script docstring, pinned here): a closing keyword
# next to an issue reference inside an inline code span, a fenced block or an indented block is STILL a
# finding. The markdown parser skips code, but the squash commit message is plain text and binds it
# (.github#683, FS-GG/.github#422 was closed twice that way). Code is not a defence.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-closing-keywords.py"
WF="$REPO_ROOT/.github/workflows/kit-materialize.yml"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# rule_on <body> -> $RC, $OUT.  The body goes through a FILE, as the job does.
rule_on() {
  printf '%s' "$1" > "$WORK/body.md"
  set +e
  OUT="$(python3 -I "$GATE" --body "$WORK/body.md" 2>&1)"
  RC=$?
  set -e
}

expect() { # expect <rc> <label> <body>
  rule_on "$3"
  if [ "$RC" = "$1" ]; then ok "$2"; else bad "$2 (wanted rc=$1, got rc=$RC)" "$OUT"; fi
}

echo "closing-keywords product-lint fixture"

# =============================================================================================
# A. THE RULE
# =============================================================================================

KEYWORDS="close closes closed fix fixes fixed resolve resolves resolved"

for kw in $KEYWORDS; do
  KW_UP="$(printf '%s' "$kw" | tr '[:lower:]' '[:upper:]')"
  expect 1 "A1  plain, in a sentence: '$kw #730'"                 "This PR $kw #730 in passing."
  expect 1 "A2  negated: 'does not $kw #730'"                      "This PR does not $kw #730."
  expect 1 "A3  contraction: 'won't $kw #730'"                     "It won't $kw #730."
  expect 1 "A4  cross-repo ref: 'not $kw FS-GG/FS.GG.Game#730'"    "This is not going to $kw FS-GG/FS.GG.Game#730."
  expect 1 "A5  URL ref: 'does not $kw <issue url>'"               "This does not $kw https://github.com/FS-GG/FS.GG.Game/issues/730 yet."
  expect 1 "A6  GH-N ref: 'does not $kw GH-730'"                   "This does not $kw GH-730 yet."
  expect 1 "A7  upper case: 'DOES NOT $KW_UP #730'"                "THIS DOES NOT $KW_UP #730."
  expect 1 "A8  colon form in a sentence: '$kw: #730 later'"       "Not ready to $kw: #730 later."
  expect 0 "A9  OPT-IN: '$kw #730.' on a line of its own"          "Summary line.

$kw #730."
done

# The incident, verbatim in shape: FS.GG.Game#731.
expect 1 "A10 the Game#731 sentence (a negated close of #730)" \
  "This PR does not close #730. The adapters still need the bindings."

# Declaration forms: the documented opt-in, repeated keyword for each issue.
expect 0 "A11 OPT-IN: two issues, keyword repeated"   "Closes #1, closes #2"
expect 0 "A12 OPT-IN: bullet and colon"               "- Fixes: FS-GG/.github#4730."
expect 1 "A13 a bare second ref is bound to nothing, so the declaration is a finding" "Closes #1, #2"

# What must PASS.
expect 0 "A20 Refs links without closing"                       "Refs #4730"
expect 0 "A21 Refs, cross-repo"                                 "Refs FS-GG/.github#4730 and part of #731."
expect 0 "A22 empty body"                                       ""
expect 0 "A23 'close the file' is not a reference"              "We close the file handle early."
expect 0 "A24 'fix the build' is not a reference"               "Fix the build; see #730 for why."
expect 0 "A25 'resolve the conflict' then a non-adjacent ref"   "Resolve the merge conflict in the lockfile (context: #730)."
expect 0 "A26 'will close the issue #730' is not adjacent, so GitHub binds nothing" "This will close the issue #730 eventually."

# Words that are NOT GitHub keywords bind nothing, so they must not fire. 'not resolving #N' is the
# phrase the task statement named; GitHub's list is the nine above and has no '-ing' form.
expect 0 "A30 'not resolving #730' is not a GitHub keyword"     "This is not resolving #730."
expect 0 "A31 'closing #730' is not a GitHub keyword"           "Closing #730 is deferred."
expect 0 "A32 'fixing #730' is not a GitHub keyword"            "We are fixing #730 next."

# Line endings: the web editor stores CRLF.
expect 1 "A40 CRLF body, negated"                               $'Refs #730\r\n\r\nThis does not close #730.\r\n'
expect 0 "A41 CRLF body, Refs only"                             $'Refs #730\r\n'
expect 0 "A42 CRLF body, declaration line"                      $'Closes #730.\r\n'

# CODE. Decision: code is not a defence. Each of these closes on merge because the squash message is
# plain text, so each must be a finding.
expect 1 "A50 inline code span"            'The text `does not close #730` is quoted here.'
expect 1 "A51 fenced block"                $'Example:\n\n```\nthis does not close #730\n```\n'
expect 1 "A52 fenced block, tilde fence"   $'Example:\n\n~~~\nthis does not close #730\n~~~\n'
expect 1 "A53 declaration-shaped line INSIDE a fence is a quotation, not an opt-in" $'```\nCloses #730.\n```\n'
expect 1 "A54 indented code block"         $'Example:\n\n    this does not close #730\n'
expect 1 "A55 unclosed fence swallows the rest, and the keyword inside it is still a finding" $'```\nthis does not close #730\n'
expect 0 "A56 a fence holding no keyword-plus-reference passes"  $'```\nrefs #730\n```\n'
expect 0 "A57 quoting the number without its hash breaks the adjacency" 'Not closing: it does not close 730.'

# The finding names the phrase and the issue, and the remedy.
rule_on "This PR does not close #730."
if [ "$RC" = 1 ] && grep -q 'not close #730' <<<"$OUT" && grep -q 'Refs #422' <<<"$OUT"; then
  ok "A-names  the finding names the offending phrase 'not close #730' and the Refs remedy"
else
  bad "A-names  the finding must name 'not close #730' and the remedy (rc=$RC)" "$OUT"
fi

# TITLES (the coordinator's .github#4963 evidence). Under COMMIT_OR_PR_TITLE the title becomes the squash
# commit subject, so a title is checked with the same grammar and the same negation handling, with NO
# opt-in (a declaration line belongs in the body) and no code exemption (a title is plain text).
title_on() {
  set +e
  OUT="$(python3 -I "$GATE" --title "$1" 2>&1)"
  RC=$?
  set -e
}
title_expect() { # title_expect <rc> <label> <title>
  title_on "$3"
  if [ "$RC" = "$1" ]; then ok "$2"; else bad "$2 (wanted rc=$1, got rc=$RC)" "$OUT"; fi
}
for kw in $KEYWORDS; do
  title_expect 1 "T1  title '$kw #4730' fails"                          "docs: $kw #4730 documentation items"
  title_expect 1 "T2  negated title 'do not $kw #4730' fails"           "docs: do not $kw #4730"
  title_expect 1 "T3  cross-repo title '$kw FS-GG/.github#4730' fails"  "docs: $kw FS-GG/.github#4730"
  title_expect 1 "T4  a title that is nothing but '$kw #4730.' has NO opt-in and fails" "$kw #4730."
done
title_expect 1 "T5  the .github#4963 title shape: 'docs: close #3010 documentation items'" "docs: close #3010 documentation items"
title_expect 0 "T6  CAUSAL CONTROL: the same title with (Refs #N) passes"                  "docs: documentation items (Refs #3010)"
title_expect 0 "T7  'Refs #N' title passes"                                                "Refs #3010: documentation items"
title_expect 0 "T8  a title with a closing verb and no reference passes"                    "docs: close the loop on the kit"
title_expect 0 "T9  non-keywords in a title pass"                                           "docs: resolving #3010 later"
title_expect 1 "T10 a code span in a title is not a defence (plain text)"                   'docs: `close #3010` items'
title_rc_empty() { set +e; python3 -I "$GATE" --title "" >/dev/null 2>&1; RC=$?; set -e; }
title_rc_empty
[ "$RC" = 3 ] && ok "T11 an EMPTY title is NO VERDICT (rc=3), never a pass" || bad "T11 empty title must be rc=3 (got $RC)"
printf 'Refs #3010' > "$WORK/refs-body.md"
set +e; python3 -I "$GATE" --title "docs: close #3010 items" --body "$WORK/refs-body.md" >/dev/null 2>&1; RC=$?; set -e
[ "$RC" = 1 ] && ok "T12 a clean body does not excuse a closing title (body Refs #3010, title closes it)" || bad "T12 (rc=$RC)"
# CR in a title: the echoed excerpt must be inert, and detection must still see through it.
title_on $'docs: close #3010 items\r::error::forged'
if [ "$RC" = 1 ] && ! grep -q $'\r' <<<"$OUT" && ! grep -q '^::' <<<"$OUT" && ! grep -q '^ *::error::forged' <<<"$OUT"; then
  ok "T14 a CR plus ::error:: in a title stays inert in the rule's output (no CR byte, no line starts a command)"
else bad "T14 (rc=$RC)" "$(od -c <<<"$OUT")"; fi
title_on $'docs: close\r#3010 items'
[ "$RC" = 1 ] && ok "T15 a CR between keyword and reference still binds, so stripping it from the echo never changes a verdict" || bad "T15 (rc=$RC)" "$OUT"
body_on_cr=$'Refs #1\nthis does not close #730\r::error::forged\n'
rule_on "$body_on_cr"
if [ "$RC" = 1 ] && ! grep -q $'\r' <<<"$OUT" && ! grep -q '^::' <<<"$OUT"; then
  ok "T16 a lone CR inside a BODY file reads as a line break (Python text mode), so it cannot reach the log either"
else bad "T16 (rc=$RC)" "$(od -c <<<"$OUT")"; fi

title_on "docs: close #3010 items"
if grep -q 'PR TITLE' <<<"$OUT" && grep -q 'close #3010' <<<"$OUT"; then ok "T13 the finding names the title and the phrase"; else bad "T13" "$OUT"; fi

# =============================================================================================
# B. THE JOB
# =============================================================================================

META="$WORK/meta.json"
RUNSH="$WORK/lint-step.sh"

python3 - "$WF" "$RUNSH" "$META" <<'PY'
import json, sys, yaml
wf = yaml.safe_load(open(sys.argv[1]))
job = wf["jobs"]["closing-keywords"]
steps = job["steps"]
lint = [s for s in steps if "check-closing-keywords.py" in s.get("run", "")]
assert len(lint) == 1, "exactly one step runs the rule"
open(sys.argv[2], "w").write(lint[0]["run"])
fetch = steps[0]
json.dump({
    "name": job.get("name"),
    "if": job.get("if"),
    "timeout": job.get("timeout-minutes"),
    "has_needs": "needs" in job,
    "has_paths": "paths" in job,
    "fetch_with": fetch.get("with", {}),
    "fetch_uses": fetch.get("uses"),
    "lint_env_keys": sorted(lint[0].get("env", {})),
    "lint_env": lint[0].get("env", {}),
    "any_secret_ref": any("secrets." in json.dumps(s) for s in steps),
    "jobs": sorted(wf["jobs"]),
    "step_count": len(steps),
    "run_has_expr": "${{" in lint[0]["run"],
}, open(sys.argv[3], "w"))
PY

m() { python3 -c "import json;print(json.load(open('$META'))$1)"; }

[ "$(m "['name']")" = "closing-keywords" ] \
  && ok "B1  the job's check name is 'closing-keywords' (context: materialize / closing-keywords)" \
  || bad "B1  the job name changed: $(m "['name']")"

python3 - "$META" <<'PY' && ok "B2  the job runs on every pull_request and pull_request_target, never on dispatch, with no narrowing (#1508)" || bad "B2  the job's if: is wrong or narrows"
import json, sys
d = json.load(open(sys.argv[1]))
expr = d["if"]
assert d["name"] and not d["has_needs"] and not d["has_paths"]
def ev(name):
    py = expr.replace("github.event_name", repr(name)).replace("||", " or ").replace("&&", " and ")
    return eval(py)
assert ev("pull_request") is True and ev("pull_request_target") is True
assert ev("workflow_dispatch") is False
# No head/base/author/label/path term may appear.
for forbidden in ("head", "base", "author", "label", "paths", "actor", "draft"):
    assert forbidden not in expr, forbidden
PY

python3 - "$META" <<'PY' && ok "B3  the rule is fetched at this workflow's own commit, as a DIRECTORY, with no credential persisted" || bad "B3  the rule fetch is not pinned to job_workflow_sha / not a directory fetch"
import json, sys
d = json.load(open(sys.argv[1]))
w = d["fetch_with"]
assert d["fetch_uses"].startswith("actions/checkout@")
assert w["repository"] == "FS-GG/.github"
assert w["ref"] == "${{ github.job_workflow_sha }}", w["ref"]
assert w["sparse-checkout"].strip() == "/scripts/", w["sparse-checkout"]
assert w["sparse-checkout-cone-mode"] is False
assert w["persist-credentials"] is False
PY

python3 - "$META" <<'PY' && ok "B4  untrusted values reach the shell only through env:, no secret is read, no receiver tree is checked out" || bad "B4  the lint step interpolates an expression into run:, reads a secret or checks out the receiver"
import json, sys
d = json.load(open(sys.argv[1]))
assert d["lint_env_keys"] == ["EVENT_BODY", "EVENT_TITLE", "GH_TOKEN", "PR_NUMBER", "REPO"], d["lint_env_keys"]
assert d["lint_env"]["GH_TOKEN"] == "${{ github.token }}"
assert d["run_has_expr"] is False
assert d["any_secret_ref"] is False
assert d["step_count"] == 2          # fetch the rule, run the rule: nothing else, in particular no receiver checkout
assert d["timeout"] and int(d["timeout"]) <= 10
PY

# --- run the real step -----------------------------------------------------------------------
# The stub `gh` answers `pulls/N` with a JSON document built from LIVE_TITLE and the live body, and
# `pulls/N/commits` with the subjects in STUB_COMMITS; mode `fail` makes every call exit 1.
mkstub() {
  mkdir -p "$WORK/bin"
  cat > "$WORK/bin/gh" <<'STUB'
#!/usr/bin/env bash
if [ "${STUB_GH_MODE:-ok}" = fail ]; then echo "gh: HTTP 502" >&2; exit 1; fi
case "$*" in
  *"/commits"*) cat "$STUB_COMMITS" ;;
  *) cat "$STUB_GH_JSON" ;;
esac
STUB
  chmod +x "$WORK/bin/gh"
}
mkstub

# Defaults a leg may override before calling job_on.
LIVE_TITLE="docs: tidy the kit (Refs #4730)"
EVENT_TITLE="docs: tidy the kit (Refs #4730)"
COMMIT_SUBJECTS="tidy the kit"

# job_on <live body> <event body> [gh mode] [pr number] [hub dir]  -> $RC $OUT, $SUMMARY (file)
job_on() {
  local live="$1" event="$2" mode="${3:-ok}" prn="${4:-4730}" hub="${5:-$REPO_ROOT}"
  local t; t="$(mktemp -d "$WORK/job.XXXX")"
  mkdir -p "$t/ws" "$t/tmp"
  ln -s "$hub" "$t/ws/fsgg-hub"
  python3 -I -c 'import json,sys; json.dump({"body": sys.argv[1], "title": sys.argv[2]}, open(sys.argv[3], "w"))' \
    "$live" "$LIVE_TITLE" "$t/live.json"
  printf '%s\n' "$COMMIT_SUBJECTS" > "$t/commits.txt"
  : > "$t/summary.md"
  set +e
  OUT="$( cd "$t/ws" && env -i PATH="$WORK/bin:$PATH" HOME="$t" \
      RUNNER_TEMP="$t/tmp" GITHUB_STEP_SUMMARY="$t/summary.md" \
      STUB_GH_MODE="$mode" STUB_GH_JSON="$t/live.json" STUB_COMMITS="$t/commits.txt" \
      EVENT_BODY="$event" EVENT_TITLE="$EVENT_TITLE" PR_NUMBER="$prn" REPO=FS-GG/FS.GG.Game GH_TOKEN=stub \
      bash "$RUNSH" 2>&1 )"
  RC=$?
  set -e
  SUMMARY="$t/summary.md"
}

job_on 'Refs #730' 'Refs #730'
if [ "$RC" = 0 ] && grep -q 'closing-keywords: OK' "$SUMMARY"; then
  ok "B5  a 'Refs #N' body passes (so closingIssuesReferences stays empty)"
else bad "B5  a Refs-only body must pass (rc=$RC)" "$OUT"; fi

job_on 'This PR does not close #730.' 'Refs #730'
if [ "$RC" = 1 ] \
   && grep -q '^::error title=closing-keywords: closing keyword next to an issue reference::.*not close #730' <<<"$OUT" \
   && grep -q 'not close #730' "$SUMMARY"; then
  ok "B6  a live body with a negated close FAILS the job, and the annotation and summary name 'not close #730'"
else bad "B6  the live body's finding must fail the job and name the phrase (rc=$RC)" "$OUT"; fi

job_on 'Refs #730' 'This PR does not close #730.'
if [ "$RC" = 0 ]; then
  ok "B7  RE-RUN AFTER AN EDIT: the live body is clean while the replayed payload is stale, so the job passes"
else bad "B7  a re-run after fixing the body must judge the live body (rc=$RC)" "$OUT"; fi

job_on '-' 'This PR does not close #730.' fail
if [ "$RC" = 1 ] && grep -q '^::warning title=closing-keywords: live title and body unreadable' <<<"$OUT" \
   && grep -q 'source: event' <<<"$OUT"; then
  ok "B8  live read fails, the event-time body is the fallback, it is judged, and the log says so"
else bad "B8  the event body must be the fallback (rc=$RC)" "$OUT"; fi

job_on '-' 'Refs #730' fail
[ "$RC" = 0 ] && ok "B9  live read fails and the event body is clean: pass" || bad "B9  (rc=$RC)" "$OUT"

job_on '' 'This PR does not close #730.'
[ "$RC" = 0 ] && ok "B10 a live body that is empty (the author cleared it) is clean, whatever the stale payload says" || bad "B10 (rc=$RC)" "$OUT"

# Every code-and-edge shape also fails through the job, not just through the rule.
job_on $'```\nthis does not close #730\n```' 'Refs #730'
[ "$RC" = 1 ] && ok "B11 a fenced negated close fails through the job" || bad "B11 (rc=$RC)" "$OUT"

job_on 'x' 'x' ok 'abc'
[ "$RC" = 1 ] && grep -q 'no pull request number' <<<"$OUT" \
  && ok "B12 a missing or non-numeric PR number is a failure, never a pass" || bad "B12 (rc=$RC)" "$OUT"

# Workflow-command injection: no line of body text may start a workflow command.
job_on $'::error::forged\n::stop-commands::x\n::add-mask::secret\nThis does not close #730.' 'x'
leaked="$(printf '%s\n' "$OUT" | grep -E '^::' | grep -v -E '^::(error|warning) title=closing-keywords' || true)"
if [ "$RC" = 1 ] && [ -z "$leaked" ]; then
  ok "B13 body text cannot open a workflow command: every echoed body line is prefixed"
else bad "B13 a body line reached the log as a workflow command (rc=$RC)" "$leaked"; fi

# Fail closed on codes the job does not classify, using a stub rule.
mkdir -p "$WORK/hub3/scripts" "$WORK/hub7/scripts"
printf '#!/usr/bin/env python3\nimport sys\nsys.exit(3)\n' > "$WORK/hub3/scripts/check-closing-keywords.py"
printf '#!/usr/bin/env python3\nimport sys\nsys.exit(7)\n' > "$WORK/hub7/scripts/check-closing-keywords.py"
job_on 'Refs #1' 'Refs #1' ok 4730 "$WORK/hub3"
[ "$RC" = 1 ] && grep -q 'no verdict (permanent)' <<<"$OUT" && ok "B14 rule exit 3 (no verdict) fails the job" || bad "B14 (rc=$RC)" "$OUT"
job_on 'Refs #1' 'Refs #1' ok 4730 "$WORK/hub7"
[ "$RC" = 1 ] && grep -q 'inconclusive' <<<"$OUT" && ok "B15 an unclassified rule exit fails the job" || bad "B15 (rc=$RC)" "$OUT"

# The rule file missing (a sparse fetch that brought nothing) must not read as a pass.
mkdir -p "$WORK/hub0/scripts"
job_on 'Refs #1' 'Refs #1' ok 4730 "$WORK/hub0"
[ "$RC" = 1 ] && ok "B16 the rule absent from the fetch fails the job instead of passing" || bad "B16 (rc=$RC)" "$OUT"

# --- TITLE through the job ---------------------------------------------------------------
LIVE_TITLE="docs: close #3010 documentation items"; EVENT_TITLE="docs: documentation items (Refs #3010)"
job_on 'Refs #3010' 'Refs #3010'
if [ "$RC" = 1 ] && grep -q '^::error title=closing-keywords: closing keyword next to an issue reference::title: `close #3010`' <<<"$OUT" \
   && grep -q 'close #3010' "$SUMMARY"; then
  ok "B17 a closing LIVE title fails the job although the body is clean, and the annotation names the phrase"
else bad "B17 (rc=$RC)" "$OUT"; fi

LIVE_TITLE="docs: documentation items (Refs #3010)"; EVENT_TITLE="docs: close #3010 documentation items"
job_on 'Refs #3010' 'Refs #3010'
[ "$RC" = 0 ] && ok "B18 RETITLE then re-run: the live title is clean while the replayed payload title is stale, so it passes" || bad "B18 (rc=$RC)" "$OUT"

LIVE_TITLE="-"; EVENT_TITLE="docs: close #3010 documentation items"
job_on '-' 'Refs #3010' fail
[ "$RC" = 1 ] && grep -q 'source: event' <<<"$OUT" && ok "B19 live read fails: the event title is the fallback, as a pair with the event body, and is judged" || bad "B19 (rc=$RC)" "$OUT"

LIVE_TITLE="--help"; EVENT_TITLE="x"
job_on 'Refs #1' 'Refs #1'
[ "$RC" = 0 ] && ok "B20 a title that looks like an option is passed as data" || bad "B20 (rc=$RC)" "$OUT"

# Commit subjects: WARNING ONLY.
LIVE_TITLE="docs: tidy (Refs #4730)"; EVENT_TITLE="$LIVE_TITLE"; COMMIT_SUBJECTS="docs: close #4730 items"
job_on 'Refs #4730' 'Refs #4730'
if [ "$RC" = 0 ] && grep -q '^::warning title=closing-keywords: a commit subject has a closing keyword' <<<"$OUT"; then
  ok "B21 a closing commit subject WARNS and does not fail the job"
else bad "B21 (rc=$RC)" "$OUT"; fi
COMMIT_SUBJECTS="tidy the kit"
job_on 'Refs #4730' 'Refs #4730'
if [ "$RC" = 0 ] && ! grep -q 'a commit subject has' <<<"$OUT"; then ok "B22 a clean commit subject warns nothing"; else bad "B22 (rc=$RC)" "$OUT"; fi

LIVE_TITLE=$'docs: close #3010 items\r::error::forged\r::stop-commands::x'; EVENT_TITLE="$LIVE_TITLE"; COMMIT_SUBJECTS="tidy the kit"
job_on 'Refs #3010' 'Refs #3010'
leaked="$(printf '%s\n' "$OUT" | grep -E '^::' | grep -v -E '^::(error|warning) title=closing-keywords' || true)"
if [ "$RC" = 1 ] && [ -z "$leaked" ] && ! grep -q $'\r' <<<"$OUT"; then
  ok "B23 a title carrying CR plus ::error:: fails the job and stays inert in the log (no CR, no forged command)"
else bad "B23 (rc=$RC)" "$leaked"; fi

# =============================================================================================
# C. THE CALLABLE DOOR (.github#5195): closing-keywords-receiver.yml
# =============================================================================================
# A repository that is not a coordination-kit receiver cannot call kit-materialize.yml (it needs the App
# secrets, a kit pin and a receiver project), so the same job is callable on its own. There is ONE lint:
# the receiver workflow's job must be the kit job, step for step, so every B leg above covers both.

RWF="$REPO_ROOT/.github/workflows/closing-keywords-receiver.yml"

python3 - "$WF" "$RWF" <<'PY' && ok "C1  the receiver workflow's closing-keywords job is identical to the kit job except its check name (one lint, two doors)" || bad "C1  the receiver workflow's job differs from the kit job: fix both or neither"
import sys, yaml
kit = yaml.safe_load(open(sys.argv[1]))["jobs"]["closing-keywords"]
rec = yaml.safe_load(open(sys.argv[2]))["jobs"]["closing-keywords"]
assert rec.pop("name") == "title-and-body", "the receiver job's check name is its own (check-job-uniqueness.py)"
assert kit.pop("name") == "closing-keywords"
assert kit == rec, "job bodies differ"
PY

python3 - "$RWF" <<'PY' && ok "C2  the receiver workflow is workflow_call only, holds exactly the two read scopes, has one job and reads no secret" || bad "C2  the receiver workflow's trigger, permissions, job count or secret use is wrong"
import json, sys, yaml
wf = yaml.safe_load(open(sys.argv[1]))
on = wf.get(True, wf.get("on"))            # PyYAML reads the key `on` as boolean True
assert list(on) == ["workflow_call"], on
assert on["workflow_call"] in (None, {}), "no inputs and no secrets: a caller passes nothing"
assert wf["permissions"] == {"contents": "read", "pull-requests": "read"}, wf["permissions"]
assert list(wf["jobs"]) == ["closing-keywords"]
assert "secrets." not in json.dumps(wf)
assert "concurrency" not in json.dumps(wf["jobs"]), "a job-level concurrency block is refused (check-pr-concurrency.py)"
PY

echo
echo "closing-keywords product-lint: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
