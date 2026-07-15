#!/usr/bin/env bash
# case: repo scope and lint
# tier: full
# covers: lint ready next reap
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="13-repo-scope-and-lint"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #480: a worker command defaults to THE REPO YOU ARE STANDING IN -----------------------------
#
# `take`/`batch`/`next`/`who` initialised `repo=""`, and every board read treats that as THE WHOLE
# ORG. The documented contract was the opposite ("Default: the repo you are standing in"), so a bare
# `take` in the `.github` checkout claimed FS.GG.Game#141 — walking past four schedulable `.github`
# items — and printed a worktree command against `.github`'s `origin/main`, which would have built a
# worktree of the WRONG REPOSITORY. A dispatcher reporting work over a scope it never examined.
#
# Scope is resolved from the GIT REMOTE, not `gh repo view`: no API call, so it cannot burn budget and
# cannot mistake an exhausted one for "you are not in a checkout" (#430).
assert_contains "next: bare, from an FS.GG.SDD checkout -> that repo's queue (Backlog #127) [#480]" \
  "FS.GG.SDD#127" "$(run_at "$CO_SDD" next 2>/dev/null)"
assert_eq "next: bare, from an FS.GG.SDD checkout does NOT reach into Templates [#480]" \
  "false" "$(run_at "$CO_SDD" next 2>/dev/null | grep -q 'FS.GG.Templates' && echo true || echo false)"
# (`batch`'s own scoping is asserted in the #312 section below, which is the board set where it has
# real Paths-carrying candidates in more than one repo — the only place the scope can be seen to bite.)
#
# An explicit --repo is the caller SPELLING OUT the scope. The checkout is a fallback, never evidence
# that overrides it — the same precedence verify-paths states.
assert_contains "scope: an explicit --repo still wins over the checkout [#480]" \
  "FS.GG.Templates#99" "$(run_at "$CO_SDD" next --repo templates 2>/dev/null)"

# `take` ACTS — it claims, and prints a worktree command against THIS checkout's origin/main. With no
# detectable repo it must refuse, not quietly widen to the org: widening is what handed a `.github`
# worker another repo's item plus an isolation command that would silently succeed at the wrong thing.
take_nogit="$(run_at "$NOGIT" take 2>&1 || true)"
assert_contains "take: outside a checkout REFUSES rather than scanning the whole org [#480]" \
  "--repo required" "$take_nogit"

# THE REGRESSION GUARD. `ready` and `lint` are org-wide RECONCILERS: /check-board runs a BARE
# `ready --all --json` and `lint --json` to reconcile the WHOLE board. Defaulting them to the checkout
# would silently shrink the reconciler to one repo — trading this scope bug for a strictly worse one,
# in the very tool that exists to catch scope bugs. They must stay org-wide even from inside a checkout.
assert_eq "ready: bare from a checkout stays ORG-WIDE (/check-board depends on it) [#480]" \
  "true" "$(run_at "$CO_SDD" ready --all --json 2>/dev/null \
             | jq -r '[.[].repo] | unique | length > 1')"

# `reap --apply` is the one command here that DESTROYS another worker's state — it deletes their claim
# marker and unassigns them. So it is the worst possible place to keep an org-wide default: a janitor
# run from `.github` would collect claims in five repos it was never pointed at. It scopes like its
# siblings. (Asserted on the DRY RUN — the point is which claims it considers, not that it deletes.)
assert_eq "reap: bare, from a checkout considers only THAT repo's claims [#480]" \
  "true" "$(as_at "$CO_SDD" janitor-x reap 2>&1 \
             | grep -qE 'FS\.GG\.(Templates|Governance|Rendering|Audio|Game)#' && echo false || echo true)"
assert_contains "next: unknown repo reports no startable item (stderr)" \
  "no startable item" "$(run next --repo nope 2>&1 >/dev/null)"

# (6c) resolve_repo covers the WHOLE roster, not just the four original repos (FS-GG/.github#381).
# resolve_repo maps a `--repo` short-id to the repo name board items carry. It hard-coded the four
# framework short-ids and let `game`/`audio` fall through to the literal token, so `--repo game`
# matched nothing and reported an empty queue — the silent-drift class the roster (registry/repos.yml)
# exists to kill. The map is EMBEDDED in the client (it is a mirrored `kind: client` kit item shipped
# WITHOUT the roster, so it cannot read repos.yml at run time), so this guard reads the roster HERE —
# it is present in the .github checkout where CI runs — and proves every rostered short-id resolves.
# The board is GENERATED from repos.yml (one Ready item per rostered repo) so it can never drift from
# the roster: a repo added to repos.yml but not to resolve_repo's map lands as a red check here, with
# `--repo <that-id>` selecting nothing, rather than as a silent empty queue in production.
#
# Enumerate the roster STRAIGHT from repos.yml — EVERY repo, regardless of `receives` — via the same
# yq-or-python ladder repos.sh uses. `repos.sh list` can only filter by a capability (`--receives`),
# and no single capability is guaranteed to cover the whole roster: `coordination-kit` excludes the
# authority `.github` (it is the SOURCE), and even `labels` being universal is a convention no gate
# enforces — so keying the guard on any one cap would let a repo that skips it slip the check. A
# direct parse has no such blind spot: a repo in repos.yml is a repo the guard tests.
REG="$HERE/../../registry/repos.yml"
roster2json() {
  if command -v yq >/dev/null 2>&1; then yq -o=json '.repos' "$REG"
  else python3 -c 'import sys,yaml,json; json.dump(yaml.safe_load(open(sys.argv[1]))["repos"], sys.stdout)' "$REG"; fi
}
roster_pairs="$(roster2json | jq -r '.[] | [.id, .full] | @tsv')"     # id<TAB>FS-GG/<repo>, roster order
roster_fulls="$(printf '%s\n' "$roster_pairs" | cut -f2)"
jq -n --arg fulls "$roster_fulls" '
  { data:{organization:{projectV2:{items:{
      pageInfo:{hasNextPage:false,endCursor:null},
      nodes:[ ($fulls|split("\n")|map(select(length>0))) | to_entries[]
        | { status:{name:"Ready"}, phase:{name:"P1 Rendering"}, blockedBy:null,
            content:{ __typename:"Issue", number:(7001+.key), title:("roster probe "+.value),
              url:("https://github.com/"+.value+"/issues/"+((7001+.key)|tostring)),
              state:"OPEN", repository:{nameWithOwner:.value} } } ]
  }}}}, rateLimit:{cost:1,remaining:4000} }' >"$FIXTURES/board-roster.json"
# `done <<<`, not a pipe: a `while` on the right of `|` runs in a subshell, and its assert_eq
# increments to pass/failcount would be lost. A here-string keeps the loop in this shell.
while IFS=$'\t' read -r rid rfull; do
  [ -n "$rid" ] || continue
  got="$(GH_BOARD_SET=roster run ready --repo "$rid" --jq '[.[].repo]|unique|join(",")' 2>/dev/null)"
  assert_eq "resolve_repo: --repo $rid selects only $rfull (#381)" "$rfull" "$got"
  # ...and `issues` is held to the SAME roster guard (#446). It was the one repo-taking command that
  # never called resolve_repo — it took the bare name verbatim, so `issues game` asked GitHub for
  # `repos/FS-GG/game` and got a 404 while `--repo game` worked everywhere else. That is worse than it
  # sounds: `issues` is the command both skills advertise as THE way to read issues without spending
  # GraphQL, so the natural recovery from its 404 is `gh issue list` — 2 points a call, the exact
  # budget the command exists to save (#418). Assert the RESOLVED REST path, per rostered short-id.
  : >"$GH_LOG"
  GH_ISSUES_FROM_STORE=1 run issues "$rid" --jq '.[].number' >/dev/null 2>&1 || true
  assert_contains "issues: short-id $rid reads $rfull over REST, not '$rid' (#446)" \
    "issue-list $rfull" "$(cat "$GH_LOG")"
done <<< "$roster_pairs"
# The 404 as reported: the bare short-id must never reach GitHub unresolved.
: >"$GH_LOG"
GH_ISSUES_FROM_STORE=1 run issues game --jq '.[].number' >/dev/null 2>&1 || true
case "$(cat "$GH_LOG")" in
  *"issue-list FS-GG/game"*) bad "#446: 'issues game' must not request repos/FS-GG/game (404)" "$(cat "$GH_LOG")" ;;
  *)                         ok  "#446: 'issues game' must not request repos/FS-GG/game (404)" ;;
esac
# owner/repo and a literal repo name still work — resolve_repo's own fall-through, unchanged.
: >"$GH_LOG"
GH_ISSUES_FROM_STORE=1 run issues FS-GG/FS.GG.Game --jq '.[].number' >/dev/null 2>&1 || true
assert_contains "issues: an explicit owner/repo is passed through untouched (#446)" \
  "issue-list FS-GG/FS.GG.Game" "$(cat "$GH_LOG")"

# (6b) blocked-awareness: `next` skips items whose blockers are still open / unverifiable, and the
#      whole thing resolves against the SAME scan — no extra GraphQL per blocker.
before_blocked="$(gcount)"
blocked_json="$(run ready --all --json 2>/dev/null)"
assert_eq "blocked-awareness costs ZERO extra GraphQL calls" \
  "$((before_blocked + 2))" "$(gcount)"
assert_eq "blockers: an OPEN board item still blocks (#200)" \
  "true"  "$(jq -r '.[] | select(.number==200) | .blocked' <<<"$blocked_json")"
assert_eq "blockers: a bare #n ref resolves to the item's own repo (#200 -> Rendering#201)" \
  "FS-GG/FS.GG.Rendering#201" \
  "$(jq -r '.[] | select(.number==200) | .blockers[] | select(.ref|endswith("#201")) | .ref' <<<"$blocked_json")"
assert_eq "blockers: a CLOSED blocker does not block (#201)" \
  "false" "$(jq -r '.[] | select(.number==201) | .blocked' <<<"$blocked_json")"
assert_eq "blockers: a CLOSED blocker is still reported, with its state (#201)" \
  "CLOSED" "$(jq -r '.[] | select(.number==201) | .blockers[0].state' <<<"$blocked_json")"
assert_eq "blockers: a ref that is not on the board is UNKNOWN, and blocks (#202)" \
  "UNKNOWN true" \
  "$(jq -r '.[] | select(.number==202) | "\(.blockers[0].state) \(.blocked)"' <<<"$blocked_json")"
assert_eq "blockers: legacy prose is UNPARSEABLE, and blocks (#203)" \
  "UNPARSEABLE true" \
  "$(jq -r '.[] | select(.number==203) | "\(.blockers[0].state) \(.blocked)"' <<<"$blocked_json")"
assert_eq "blockers: an item with no Blocked by is never blocked (#99)" \
  "false" "$(jq -r '.[] | select(.number==99) | .blocked' <<<"$blocked_json")"
assert_eq "blockers: a draft item (no repo/number) survives the annotate pass" \
  "false" "$(jq -r '.[] | select(.type=="DraftIssue") | .blocked' <<<"$blocked_json")"
assert_eq "blockers: the placeholder '-' reads as NO blocker, not UNPARSEABLE (#189)" \
  "0 false" \
  "$(jq -r '.[] | select(.number==189) | "\(.blockers|length) \(.blocked)"' <<<"$blocked_json")"
assert_contains "next: a placeholder-'-' item is startable, not skipped (#189)" \
  "FS.GG.Audio#189" "$(run next --repo FS.GG.Audio 2>/dev/null)"
assert_fails "Blocked by: set-field REFUSES to write a placeholder" \
  run set-field 'FS.GG.SDD#42' 'Blocked by' '-'
assert_contains "Blocked by: the placeholder refusal points at clearing the field" \
  "'Blocked by' ''" "$(run set-field 'FS.GG.SDD#42' 'Blocked by' 'none' 2>&1 || true)"

assert_contains "next --repo rendering: skips #200, picks the item whose blocker closed (#201)" \
  "FS.GG.Rendering#201" "$(run next --repo rendering 2>/dev/null)"
skipnote="$(run next --repo rendering 2>&1 >/dev/null)"
# Same information, in `batch`'s words rather than `next`'s own (#485 leg a): the reason now comes from
# the check that ACTUALLY passed the item over. Two commands describing one rejection in two
# hand-written sentences is how they drifted into disagreeing about what "startable" means.
assert_contains "next: says WHICH item it skipped"      "FS.GG.Rendering#200 — blocked by" "$skipnote"
assert_contains "next: names the open blocker + state"  "FS.GG.SDD#127 (open)"         "$skipnote"
assert_contains "next: names the bare-ref blocker too"  "FS.GG.Rendering#201 (open)"   "$skipnote"

gov="$(run next --repo governance 2>&1 >/dev/null)"
assert_contains "next: unknown-state blocker is reported as such" "(unknown)"     "$gov"
assert_contains "next: legacy prose is reported as unparseable"   "(unparseable)" "$gov"
# The old summary line is deliberately GONE (#485 leg a). `next` no longer decides why it found
# nothing; blockedness is now only ONE of the reasons an item can be passed over (undeclared touch-set,
# unmatchable token, live claim, batch-mate overlap). A fixed closing sentence naming one cause it never
# checked is precisely the defect #440 removed from `take` — a guess dressed as a diagnosis.
assert_contains "next: all candidates passed over -> the REAL per-item reason, not a guessed summary" \
  "passed over:" "$gov"
refute_contains "next: ...and never the fixed sentence #440 deleted from take (#485 leg a)" \
  "every candidate was blocked" "$gov"
assert_eq "next: all candidates blocked -> prints no item on stdout" \
  "" "$(run next --repo governance 2>/dev/null)"
assert_contains "next --ignore-blocked: restores the old behaviour (#202)" \
  "FS.GG.Governance#202" "$(run next --repo governance --ignore-blocked 2>/dev/null)"
assert_eq "next --ignore-blocked: emits no skip notes" \
  "0" "$(run next --repo governance --ignore-blocked 2>&1 >/dev/null | grep -c 'skipping' || true)"

assert_contains "ready: the table gains a BLOCKED BY column" "BLOCKED BY" \
  "$(run ready 2>/dev/null | head -1)"
# Row-scoped, so the assertion cannot pass on an em dash / ref borrowed from a neighbouring row.
row200="$(run ready --repo rendering 2>/dev/null | grep '#200')"
row201="$(run ready --repo rendering 2>/dev/null | grep '#201  ')"
assert_contains "ready: #200's row lists both open blockers" "FS.GG.Rendering#201, FS.GG.SDD#127" "$row200"
assert_contains "ready: #201's row shows no blocker — its only one is CLOSED" "  —  " "$row201"

# (7) `Blocked by` is canonicalized to owner/repo#n on write, and prose is refused before any
#     GraphQL is spent. The field is TEXT (Projects v2 has no typed dependency field), so nothing
#     but this gate stops it drifting back into a resolution log.
: >"$GH_LOG"
run set-field 'FS.GG.SDD#42' 'Blocked by' 'FS-GG/FS.GG.SDD#8' >/dev/null
assert_contains "Blocked by: a full ref passes through" "--text FS-GG/FS.GG.SDD#8" "$(cat "$GH_LOG")"

: >"$GH_LOG"
run set-field 'FS.GG.SDD#42' 'Blocked by' '#33' >/dev/null
assert_contains "Blocked by: bare #n adopts the blocked item's repo" \
  "--text FS-GG/FS.GG.SDD#33" "$(cat "$GH_LOG")"

: >"$GH_LOG"
run set-field 'FS.GG.SDD#42' 'Blocked by' 'FS.GG.Rendering#33 , https://github.com/FS-GG/FS.GG.Templates/issues/8' >/dev/null
assert_contains "Blocked by: a list canonicalizes every form" \
  "--text FS-GG/FS.GG.Rendering#33, FS-GG/FS.GG.Templates#8" "$(cat "$GH_LOG")"

: >"$GH_LOG"
run set-field 'FS.GG.SDD#42' 'Blocked by' '#8, FS-GG/FS.GG.SDD#8' >/dev/null
assert_contains "Blocked by: de-dupes refs that canonicalize alike" \
  "--text FS-GG/FS.GG.SDD#8" "$(cat "$GH_LOG")"

# An empty value must clear via `--clear`. Real `gh` treats `--text ''` as "no changes to make",
# so asserting on `--text` here would pass the stub and silently no-op against the live board.
: >"$GH_LOG"
run set-field 'FS.GG.SDD#42' 'Blocked by' '' >/dev/null
assert_contains "Blocked by: empty clears the field via --clear" "--clear" "$(cat "$GH_LOG")"
assert_eq "Blocked by: clearing does NOT pass --text" "0" \
  "$(grep -c -- '--text' "$GH_LOG" || true)"

assert_fails "Blocked by: rejects a delivery log" \
  run set-field 'FS.GG.SDD#42' 'Blocked by' 'RESOLVED: #8 closed, shipped @d80a8ae'
assert_fails "Blocked by: rejects the inverted 'blocks X' edge" \
  run set-field 'FS.GG.SDD#42' 'Blocked by' 'blocks FS.GG.Governance#14'
assert_fails "Blocked by: rejects prose trailing a valid ref" \
  run set-field 'FS.GG.SDD#42' 'Blocked by' 'FS-GG/FS.GG.SDD#8 (republish vehicle)'
assert_contains "Blocked by: the refusal names Status as the right home for 'is blocked'" \
  "set-field <issue> Status Blocked" \
  "$(run set-field 'FS.GG.SDD#42' 'Blocked by' 'not a ref' 2>&1 || true)"

# A refused write must cost ZERO GraphQL — validation precedes item resolution.
before_reject="$(gcount)"
run set-field 'FS.GG.Rendering#77' 'Blocked by' 'nonsense prose' >/dev/null 2>&1 || true
assert_eq "Blocked by: a refused write spends no GraphQL" "$before_reject" "$(gcount)"

# Other TEXT fields keep taking free-form text — the gate is scoped to `Blocked by`.
: >"$GH_LOG"
run set-field 'FS.GG.SDD#42' Contract 'fs-gg-ui-template (0.3.1, preview)' >/dev/null
assert_contains "Contract: still accepts free-form text" \
  "--text fs-gg-ui-template (0.3.1, preview)" "$(cat "$GH_LOG")"

# (8) lint: the board's epic invariants. An `[epic]` (title convention — Projects v2 issue types are
#     unset on this board) must have children, must not be Done over an open child, and must not have
#     more children than the scan can see.
before_lint="$(gcount)"
lint_json="$(run lint --json 2>/dev/null || true)"
assert_eq "lint: paginates the board in exactly 2 GraphQL calls" "$((before_lint + 2))" "$(gcount)"
codes() { jq -r --arg id "$1" '.[] | select(.id|endswith($id)) | .code' <<<"$lint_json" | sort | tr '\n' ' '; }
assert_eq "lint: a childless OPEN [epic] is EPIC-NO-CHILDREN (#400)"   "EPIC-NO-CHILDREN "     "$(codes '#400')"
# #401 is itself CLOSED. Liveness scopes EPIC-NO-CHILDREN only — a closed epic over an open child is
# the sharpest form of THIS bug (the .github#235 case), so it must still be reported.
assert_eq "lint: Done over an open child, on a CLOSED epic (#401)"     "EPIC-DONE-OPEN-CHILD " "$(codes '#401')"
assert_eq "lint: >100 children is EPIC-CHILDREN-TRUNCATED (#404)"      "EPIC-CHILDREN-TRUNCATED " "$(codes '#404')"
assert_eq "lint: Done status on an open issue is a NOTE (#405)"        "DONE-STATUS-OPEN-ISSUE " "$(codes '#405')"
assert_eq "lint: a properly finished epic is clean (#406)"             ""                      "$(codes '#406')"
assert_eq "lint: a childless NON-epic is clean — the check is epic-scoped (#407)" "" "$(codes '#407')"
# A CLOSED childless epic is finished work, not an orphan: rollup is how an epic reaches Done, and
# `next` hands out only open Ready/Backlog cards. Linting it can never be acted on.
assert_eq "lint: a childless CLOSED [epic] is clean — the check is live-work-scoped (#408)" "" "$(codes '#408')"
assert_contains "lint: EPIC-DONE-OPEN-CHILD names the open child" "#403" \
  "$(jq -r '.[] | select(.code=="EPIC-DONE-OPEN-CHILD") | .detail' <<<"$lint_json")"
assert_eq "lint: severities — 7 errors, 1 note (2 NO-TOUCH-SET + 1 BAD-TOUCH-SET, #496)" "7 1" \
  "$(jq -r '"\([.[]|select(.severity=="error")]|length) \([.[]|select(.severity=="note")]|length)"' <<<"$lint_json")"

assert_fails "lint: exits non-zero when an invariant is broken" run lint
assert_contains "lint: text output is greppable" "FSGG-LINT ERROR  EPIC-NO-CHILDREN" "$(run lint 2>/dev/null || true)"
assert_contains "lint: prints an error/note tally on stderr" "7 error(s), 1 note(s)" \
  "$(run lint 2>&1 >/dev/null || true)"



harness_report
