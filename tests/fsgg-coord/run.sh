#!/usr/bin/env bash
# Fixture for scripts/fsgg-coord — the GraphQL-budget-thrifty Coordination-board client.
#
# Proves the three consumption levers actually fire, with NO network: a PATH-shim `gh` stub serves
# canned GraphQL/REST responses and COUNTS calls, so the test can assert the caching — not just that
# commands exit 0. The claims under test (docs/coordination/graphql-budget.md):
#   1. bootstrap introspects the board map ONCE; later reads (board / field-id / option-id) add
#      ZERO GraphQL calls (the primary-budget win).
#   2. item-id resolves via issue->projectItems and picks the RIGHT board (ignores other projects),
#      then caches — a repeat lookup adds ZERO GraphQL calls.
#   3. set-field auto-routes by dataType: SINGLE_SELECT->--single-select-option-id, DATE->--date,
#      TEXT->--text, resolving every id from cache (no per-write introspection).
#   4. issues fetches over REST with an ETag; an unchanged repeat gets 304 and is served from cache.
#   5. bad field / option names fail loudly (non-zero) with the available names.
#   6. ready/next scan the whole board in a paginated loop (2 pages -> 2 calls), filter client-side
#      (repo / status / phase; Done excluded by default), and `next` prefers Ready over Backlog.
#   7. `Blocked by` is canonicalised to issue refs on write (prose refused, zero GraphQL spent) and
#      honoured on read: `next` skips an item whose blockers are open / unverifiable.
#   8. lint asserts the epic invariants — no childless `[epic]`, none Done over an open child, none
#      with more children than the scan can see, none whose body declares an unlinked child — and
#      exits non-zero.
#   9. epic_rollup flips a parent only when every child is board-Done AND issue-CLOSED, and REFUSES
#      outright when the epic's body declares a child the sub-issue graph does not contain.
#  10. `child` creates that sub-issue edge — by REST id, via `-F` (a `-f` string 422s), idempotently.
#
# Self-contained: a throwaway cache + stub under a temp dir, no network, no other repos. Mirrors
# tests/skill-union/run.sh (FS-GG/.github#111) in shape so the two fixtures read the same way.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
COORD="$HERE/../../scripts/fsgg-coord"      # always invoked as `bash "$COORD"`

WORK="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-coord-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

FIXTURES="$WORK/fixtures"; STUB="$WORK/bin"; STORE="$WORK/store"; export GH_LOG="$WORK/gh.log"
export GH_GRAPHQL_COUNT="$WORK/graphql.count" GH_REST_COUNT="$WORK/rest.count"
mkdir -p "$FIXTURES" "$STUB" "$STORE"
: >"$GH_LOG"; : >"$GH_GRAPHQL_COUNT"; : >"$GH_REST_COUNT"
# The comment store's id sequence. GitHub issues comment ids from ONE server-side sequence, and the
# claim lock's correctness rests on exactly that: monotonic ids are the total order every racer sees.
echo 900 >"$STORE/nextid"

# Run fsgg-coord against the stub + an isolated cache. FSGG_COORD_DEBUG surfaces the 304/cache path.
export FSGG_COORD_CACHE="$WORK/cache"
run() { PATH="$STUB:$PATH" FSGG_COORD_DEBUG=1 bash "$COORD" "$@"; }

# #480: a worker command's default scope is the repo you are STANDING IN, so the fixture has to be
# able to stand somewhere. `run_at <dir>` runs the coord from a throwaway checkout whose `origin`
# names a repo (or from a plain directory, which is no checkout at all).
run_at() { local d="$1"; shift; ( cd "$d" && PATH="$STUB:$PATH" FSGG_COORD_DEBUG=1 bash "$COORD" "$@" ); }
mkcheckout() {  # mkcheckout <name> [origin-url] -> prints the path
  local dir="$WORK/co-$1"; mkdir -p "$dir"
  git -C "$dir" init -q >/dev/null 2>&1
  [ -n "${2:-}" ] && git -C "$dir" remote add origin "$2"
  printf '%s' "$dir"
}
CO_SDD="$(mkcheckout sdd    https://github.com/FS-GG/FS.GG.SDD.git)"
CO_TPL="$(mkcheckout tpl    git@github.com:FS-GG/FS.GG.Templates.git)"   # the ssh form must parse too
NOGIT="$(mkcheckout nogit)"                                              # a dir with no remote at all

pass=0; failcount=0
ok()   { echo "PASS  $1"; pass=$((pass+1)); }
bad()  { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# assert_eq <name> <expected> <actual>
assert_eq() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "expected='$2' actual='$3'"; fi; }
# assert_contains <name> <needle> <haystack>
assert_contains() { case "$3" in *"$2"*) ok "$1" ;; *) bad "$1" "needle='$2' not in: $3" ;; esac; }
# The inverse. A message this fabric DELETED on purpose (a guess dressed as a diagnosis) needs an
# assertion that it STAYS deleted — otherwise "we removed the misleading sentence" is a claim nothing
# checks, and the next refactor quietly puts it back.
refute_contains() { case "$3" in *"$2"*) bad "$1" "needle='$2' SHOULD NOT be in: $3" ;; *) ok "$1" ;; esac; }
# assert_fails <name> <cmd...>  — expects non-zero exit
assert_fails() { local n="$1"; shift; if "$@" >/dev/null 2>&1; then bad "$n" "expected non-zero exit"; else ok "$n"; fi; }

gcount() { wc -l <"$GH_GRAPHQL_COUNT" | tr -d ' '; }
rcount() { wc -l <"$GH_REST_COUNT"    | tr -d ' '; }

# ---- canned responses --------------------------------------------------------------------------
cat >"$FIXTURES/projects.json" <<'JSON'
{"data":{"organization":{"projectsV2":{"nodes":[
  {"number":7,"title":"Other","id":"PVT_other"},
  {"number":12,"title":"Coordination","id":"PVT_coord"}
]}},"rateLimit":{"cost":1,"remaining":4999}}}
JSON

cat >"$FIXTURES/fields.json" <<'JSON'
{"data":{"organization":{"projectV2":{"fields":{"nodes":[
  {"__typename":"ProjectV2SingleSelectField","id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_review","name":"In review"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_done","name":"Done"}]},
  {"__typename":"ProjectV2SingleSelectField","id":"PVTSSF_phase","name":"Phase","dataType":"SINGLE_SELECT","options":[{"id":"opt_p1","name":"P1 Rendering"},{"id":"opt_p2","name":"P2 SDD"}]},
  {"__typename":"ProjectV2Field","id":"PVTF_target","name":"Target","dataType":"DATE"},
  {"__typename":"ProjectV2Field","id":"PVTF_contract","name":"Contract","dataType":"TEXT"},
  {"__typename":"ProjectV2Field","id":"PVTF_blockedby","name":"Blocked by","dataType":"TEXT"}
]}}},"rateLimit":{"cost":1,"remaining":4998}}}
JSON

# Two project items: one on the WRONG board (number 7) and one on Coordination (12). The helper
# must pick PVTI_coord123 by matching the cached board number, not just take nodes[0].
cat >"$FIXTURES/item.json" <<'JSON'
{"data":{"repository":{"issue":{"id":"I_abc","title":"demo",
  "projectItems":{"nodes":[
    {"id":"PVTI_wrongboard","project":{"number":7,"title":"Other"}},
    {"id":"PVTI_coord123","project":{"number":12,"title":"Coordination"}}
  ]}}},"rateLimit":{"cost":1,"remaining":4997}}}
JSON

cat >"$FIXTURES/issues.json" <<'JSON'
[{"number":42,"title":"[cross-repo] demo","labels":[{"name":"cross-repo"}]}]
JSON

# Board items for ready/next, in TWO pages so the pagination loop is exercised. Page 1 says
# hasNextPage:true + an endCursor; the second GraphQL call (carrying cursor=CUR1) serves page 2.
# Per item only status/phase/blockedBy (via fieldValueByName) + the issue's own content — the thrifty
# shape. `blockedBy` covers every state board_annotate must resolve, all from the scan itself:
#   #200 -> an OPEN board item (#127) + a bare `#201` self-repo ref (OPEN)  => blocked
#   #201 -> a CLOSED board item (#8)                                        => NOT blocked
#   #202 -> a real ref that is not ON the board                             => UNKNOWN => blocked
#   #203 -> legacy pre-gate prose                                           => UNPARSEABLE => blocked
#   #189 -> the literal placeholder "-" ("nothing blocks this")           => NOT blocked
#   #54  -> a Done card carrying a legacy resolution log (must not crash the annotate pass)
cat >"$FIXTURES/board-items-p1.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":true,"endCursor":"CUR1"},
  "nodes":[
    {"status":{"name":"Ready"},"phase":{"name":"P4 Templates"},"blockedBy":null,"content":{"__typename":"Issue","number":99,"title":"Re-mirror minimumFsggSdd","url":"https://github.com/FS-GG/FS.GG.Templates/issues/99","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}},
    {"status":{"name":"Done"},"phase":null,"blockedBy":{"text":"RESOLVED: FS-GG/FS.GG.SDD#8 shipped @d80a8ae"},"content":{"__typename":"Issue","number":54,"title":"Dependency Dashboard","url":"https://github.com/FS-GG/.github/issues/54","state":"OPEN","repository":{"nameWithOwner":"FS-GG/.github"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4990}}
JSON
cat >"$FIXTURES/board-items-p2.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Backlog"},"phase":{"name":"P2 SDD"},"blockedBy":null,"content":{"__typename":"Issue","number":127,"title":"TD1 SDD epic","url":"https://github.com/FS-GG/FS.GG.SDD/issues/127","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Backlog"},"phase":null,"blockedBy":null,"content":{"__typename":"DraftIssue","title":"a draft idea"}},
    {"status":{"name":"Done"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":8,"title":"Ship FS.GG.Contracts","url":"https://github.com/FS-GG/FS.GG.SDD/issues/8","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P1 Rendering"},"blockedBy":{"text":"-"},"content":{"__typename":"Issue","number":189,"title":"Placeholder dash means no blocker","url":"https://github.com/FS-GG/FS.GG.Audio/issues/189","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P1 Rendering"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#127, #201"},"content":{"__typename":"Issue","number":200,"title":"Blocked on an open item","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/200","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P1 Rendering"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#8"},"content":{"__typename":"Issue","number":201,"title":"Blocker already closed","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/201","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P3 Governance"},"blockedBy":{"text":"FS-GG/FS.GG.Other#999"},"content":{"__typename":"Issue","number":202,"title":"Blocker is not on the board","url":"https://github.com/FS-GG/FS.GG.Governance/issues/202","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P3 Governance"},"blockedBy":{"text":"RESOLVED: shipped last week"},"content":{"__typename":"Issue","number":203,"title":"Legacy prose in the field","url":"https://github.com/FS-GG/FS.GG.Governance/issues/203","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P1 Rendering"},"blockedBy":null,"content":{"__typename":"Issue","number":502,"title":"CLOSED, but the column still says Ready (#520)","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/502","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/.github#449"},"content":{"__typename":"Issue","number":350,"title":"Blocked by a MERGED pull request (#476)","url":"https://github.com/FS-GG/FS.GG.Templates/issues/350","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P7 Audio"},"blockedBy":null,"content":{"__typename":"Issue","number":31,"title":"Every Paths: token is unmatchable","url":"https://github.com/FS-GG/FS.GG.Audio/issues/31","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}},
    {"status":{"name":"Done"},"phase":null,"blockedBy":null,"content":{"__typename":"PullRequest","number":449,"title":"the ADR that resolves SDD#350","url":"https://github.com/FS-GG/.github/pull/449","state":"MERGED","repository":{"nameWithOwner":"FS-GG/.github"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4989}}
JSON

# Board pages for `lint` — same items connection, but carrying each epic's sub-issues. Covers every
# invariant plus two negatives (a clean epic, a childless NON-epic) so the checks cannot pass by
# firing on everything.
#   #400 [epic], OPEN, zero children                    -> EPIC-NO-CHILDREN
#   #401 [epic], board Done, child #403 still OPEN      -> EPIC-DONE-OPEN-CHILD
#   #404 [epic], totalCount 150 but 2 nodes visible     -> EPIC-CHILDREN-TRUNCATED *only*: its body
#                                                          declares #499, but a truncated child list
#                                                          cannot tell "unlinked" from "unseen", so
#                                                          EPIC-UNLINKED-CHILD must NOT also fire
#   #405 non-epic, Status Done but issue OPEN           -> DONE-STATUS-OPEN-ISSUE (note, not an error)
#   #406 [epic], board Done, every child CLOSED         -> clean (body declares exactly its one child)
#   #407 non-epic, zero children                        -> clean (the check is epic-scoped)
#   #408 [epic], CLOSED, zero children                  -> clean (the check is live-work-scoped)
#   #409 [epic], body declares #414, never linked       -> EPIC-UNLINKED-CHILD (and #415, named only
#                                                          in prose, declares nothing)
cat >"$FIXTURES/lint-p1.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":true,"endCursor":"LCUR1"},
  "nodes":[
    {"status":{"name":"Ready"},"content":{"__typename":"Issue","number":31,"title":"Every Paths: token is unmatchable (#496 reopened)","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.Audio/issues/31","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"},"body":"Reserve every lockfile.\n\nPaths: **/packages.lock.json","subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Backlog"},"content":{"__typename":"Issue","number":400,"title":"[sdd] [epic] Gap A: orphan","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/400","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"body":"An epic. It has no touch-set, and it SAYS so.\n\nPaths: none\n","subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Done"},"content":{"__typename":"Issue","number":401,"title":"[epic] Done over an open child","state":"CLOSED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/401","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":2,"nodes":[
      {"number":402,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}},
      {"number":403,"state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}},
    {"status":{"name":"Backlog"},"content":{"__typename":"DraftIssue","title":"a draft idea"}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4970}}
JSON
cat >"$FIXTURES/lint-p2.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"In progress"},"content":{"__typename":"Issue","number":404,"title":"[epic] Too many children to see","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/404","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"},"body":"- [ ] #499 — invisible to the scan, but only because the child list is truncated\n","subIssues":{"totalCount":150,"nodes":[
      {"number":410,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}},
      {"number":411,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}]}}},
    {"status":{"name":"Done"},"content":{"__typename":"Issue","number":405,"title":"A merged PR that left its issue open","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.Templates/issues/405","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"},"subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Done"},"content":{"__typename":"Issue","number":406,"title":"[epic] Properly finished","state":"CLOSED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/406","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"body":"- [x] #412 — landed\n","subIssues":{"totalCount":1,"nodes":[
      {"number":412,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}},
    {"status":{"name":"Ready"},"content":{"__typename":"Issue","number":407,"title":"An ordinary card, no children","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/407","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"body":"Paths: src/Foo\n","subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Done"},"content":{"__typename":"Issue","number":408,"title":"[epic] Finished, and it never grew children","state":"CLOSED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/408","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Ready"},"content":{"__typename":"Issue","number":420,"title":"Real work, and nobody can pick it up","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/420","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"body":"A perfectly good item that forgot its touch-set.\n","subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Backlog"},"content":{"__typename":"Issue","number":421,"title":"Its only Paths: line is FENCED, so the scheduler cannot see it","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/421","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"body":"Example of the syntax:\n\n```\nPaths: src/Foo\n```\n","subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Backlog"},"content":{"__typename":"Issue","number":422,"title":"A decision item that declares its touch-set-lessness","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/422","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"body":"Paths: none\n","subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"In progress"},"content":{"__typename":"Issue","number":423,"title":"Already claimed — not a scheduling candidate","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/423","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Backlog"},"content":{"__typename":"Issue","number":424,"title":"Closed, so nobody needs to pick it up","state":"CLOSED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/424","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"In progress"},"content":{"__typename":"Issue","number":409,"title":"[epic] A child was filed, and only mentioned","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/409","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"body":"## Children\n\n- [x] (a) #413 — linked, landed\n- [ ] (b) #414 — filed while working (a); a comment on this epic is not a link\n- [x] (c) preview shipped — DONE (PR #418)\n\nSee also #415, which is prose and declares nothing.\n","subIssues":{"totalCount":1,"nodes":[
      {"number":413,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4969}}
JSON
# EPIC-UNLINKED-CHILD (and epic_rollup) re-resolve an otherwise-unlinked body ref over REST to skip
# the ones that are PULL REQUESTS — a PR can never be a sub-issue, so citing it is not a missing
# child (FS-GG/.github#346). These fixtures are the probe targets, and must exist before the FIRST
# `run lint` since #409's body now cites PR #418 alongside genuine unlinked issue #414. A `.pull_request`
# key is GitHub's own discriminator; state:"closed" keeps them out of the open-issue candidate lists.
mkpr()    { jq -n --argjson n "$1" --arg r "${2:-FS-GG/FS.GG.SDD}" \
  '{number:$n, state:"closed", repo:$r, pull_request:{url:("https://github.com/"+$r+"/pull/"+($n|tostring))},
    html_url:("https://github.com/"+$r+"/pull/"+($n|tostring))}' >"$STORE/issue-$1.json"; }
mkissue() { jq -n --argjson n "$1" --arg s "${3:-open}" --arg r "${2:-FS-GG/FS.GG.SDD}" \
  '{number:$n, state:$s, repo:$r, html_url:("https://github.com/"+$r+"/issues/"+($n|tostring))}' >"$STORE/issue-$1.json"; }
mkpr    418            # #409's (c): a PR the graph can never hold
mkissue 414            # #409's (b): a genuine unlinked issue, and it must still fire

# `done --flip` + epic_rollup. Two chains:
#   #42 -> epic #300: children #42 (CLOSED, board Done) and #43 (OPEN, board Done).  Must HOLD.
#          Board Status alone would say 2/2 Done — the bug that flipped FS-GG/.github#235.
#   #44 -> epic #301: children #44 and #45, both CLOSED and board Done.              Must FLIP.
cat >"$FIXTURES/done-42.json" <<'JSON'
{"data":{"repository":{"issue":{"number":42,"title":"child of an unfinished epic","url":"https://github.com/FS-GG/FS.GG.SDD/issues/42","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[{"number":7,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/7","merged":true,"mergedAt":"2026-07-01T10:00:00Z","mergeCommit":{"abbreviatedOid":"abc1234"},
      "closingIssuesReferences":{"nodes":[{"number":42,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":{"number":300}}}},"rateLimit":{"cost":1,"remaining":4968}}
JSON
cat >"$FIXTURES/rollup-42.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":{
  "number":300,"url":"https://github.com/FS-GG/FS.GG.SDD/issues/300","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},
  "body":"## Children\n\n- [x] #42 — landed\n- [ ] #43 — still open\n",
  "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
  "subIssues":{"totalCount":2,"nodes":[
    {"number":42,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}},
    {"number":43,"state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}}
  ]}}}}},"rateLimit":{"cost":1,"remaining":4967}}
JSON
cat >"$FIXTURES/done-44.json" <<'JSON'
{"data":{"repository":{"issue":{"number":44,"title":"last child of a finished epic","url":"https://github.com/FS-GG/FS.GG.SDD/issues/44","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[{"number":9,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/9","merged":true,"mergedAt":"2026-07-02T10:00:00Z","mergeCommit":{"abbreviatedOid":"def5678"},
      "closingIssuesReferences":{"nodes":[{"number":44,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":{"number":301}}}},"rateLimit":{"cost":1,"remaining":4966}}
JSON
cat >"$FIXTURES/rollup-44.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":{
  "number":301,"url":"https://github.com/FS-GG/FS.GG.SDD/issues/301","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},
  "body":"Children:\n\n- [x] #44 — landed\n- [x] (b) FS-GG/FS.GG.SDD#45 — landed (cf. #999, not a child)\n",
  "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
  "subIssues":{"totalCount":2,"nodes":[
    {"number":44,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}},
    {"number":45,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}}
  ]}}}}},"rateLimit":{"cost":1,"remaining":4965}}
JSON
# #46 -> epic #302: the ONLY linked child (#46) is CLOSED + board-Done, so the graph alone says
# "1/1 children done — flip". But the epic's BODY declares a second child, #47, that was never
# linked as a sub-issue. That is FS-GG/.github#325: rollup must refuse, not stamp a green epic over
# a child it cannot see. #999 appears as a trailing ref on a declaration line and is NOT a child.
cat >"$FIXTURES/done-46.json" <<'JSON'
{"data":{"repository":{"issue":{"number":46,"title":"the only LINKED child of an epic with an unlinked one","url":"https://github.com/FS-GG/FS.GG.SDD/issues/46","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[{"number":11,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/11","merged":true,"mergedAt":"2026-07-03T10:00:00Z","mergeCommit":{"abbreviatedOid":"9ab0cde"},
      "closingIssuesReferences":{"nodes":[{"number":46,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":{"number":302}}}},"rateLimit":{"cost":1,"remaining":4963}}
JSON
cat >"$FIXTURES/rollup-46.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":{
  "number":302,"url":"https://github.com/FS-GG/FS.GG.SDD/issues/302","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},
  "body":"## Children\n\n- [x] (a) #46 — landed (cf. #999, a mention, not a child)\n- [ ] (b) #47 — filed while working (a), never linked\n",
  "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
  "subIssues":{"totalCount":1,"nodes":[
    {"number":46,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}}
  ]}}}}},"rateLimit":{"cost":1,"remaining":4962}}
JSON
# #48 -> epic #303: THREE unlinked children, declared with `+` and `*` bullets. GitHub renders a task
# list for all three of `-`/`*`/`+`, so a matcher that knows only `-` would read this epic as
# declaring nothing and wave the rollup through — a gate failing open on a formatting choice. Also
# pins the join: `paste -sd', '` cycles its delimiters and would render "a,b c".
cat >"$FIXTURES/done-48.json" <<'JSON'
{"data":{"repository":{"issue":{"number":48,"title":"child of an epic written with + bullets","url":"https://github.com/FS-GG/FS.GG.SDD/issues/48","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[{"number":13,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/13","merged":true,"mergedAt":"2026-07-04T10:00:00Z","mergeCommit":{"abbreviatedOid":"1234abc"},
      "closingIssuesReferences":{"nodes":[{"number":48,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":{"number":303}}}},"rateLimit":{"cost":1,"remaining":4961}}
JSON
cat >"$FIXTURES/rollup-48.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":{
  "number":303,"url":"https://github.com/FS-GG/FS.GG.SDD/issues/303","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},
  "body":"+ [x] #48 — landed\n+ [ ] #49 — never linked\n* [ ] #50 — never linked\n+ [ ] FS-GG/FS.GG.Rendering#51 — a cross-repo child, never linked\n",
  "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
  "subIssues":{"totalCount":1,"nodes":[
    {"number":48,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}}
  ]}}}}},"rateLimit":{"cost":1,"remaining":4960}}
JSON
# #52 -> epic #55: the graph's one child (#52) is CLOSED + board-Done, so the rollup would flip. The
# body also cites `PR #920` on a task-list line — a PR, which can NEVER be a sub-issue. The old code
# read that as an unlinked child and refused forever; the fix re-resolves it, sees a PR, drops it,
# and the epic rolls up (FS-GG/.github#346). Contrast #302, whose unlinked ref is a real ISSUE.
cat >"$FIXTURES/done-52.json" <<'JSON'
{"data":{"repository":{"issue":{"number":52,"title":"child of an epic that also cites the PR that closed it","url":"https://github.com/FS-GG/FS.GG.SDD/issues/52","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[{"number":19,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/19","merged":true,"mergedAt":"2026-07-05T10:00:00Z","mergeCommit":{"abbreviatedOid":"cafe123"},
      "closingIssuesReferences":{"nodes":[{"number":52,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":{"number":55}}}},"rateLimit":{"cost":1,"remaining":4959}}
JSON
cat >"$FIXTURES/rollup-52.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":{
  "number":55,"url":"https://github.com/FS-GG/FS.GG.SDD/issues/55","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},
  "body":"## Children\n\n- [x] #52 — landed\n- [x] preview shipped — DONE (PR #920)\n",
  "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
  "subIssues":{"totalCount":1,"nodes":[
    {"number":52,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}}
  ]}}}}},"rateLimit":{"cost":1,"remaining":4958}}
JSON
cat >"$FIXTURES/rollup-none.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":null}}},"rateLimit":{"cost":1,"remaining":4964}}
JSON

# ------------------------------------------------------------------------------------------------
# THE DONE-STAMP'S THREE HOLES (#558, #543, #583). Fixtures here; assertions further down.
#
# #165: THE CLOSING KEYWORD WAS IN THE COMMIT SUBJECT (#558 / #543 leg 1). `gh pr create --fill` —
#   which pnext-item §5 prescribes — maps the commit SUBJECT to the PR TITLE. GitHub populates
#   `closingIssuesReferences` from the PR BODY only, and only while the PR is open. So a worker writing
#   the near-universal `gate: reconstruct the scene edge (closes #165)` gets: PR merged ✓, issue closed
#   ✓ (by the squash commit — GitHub honoured the keyword), CI green ✓, board Done ✓ ... and a
#   PERMANENTLY RED STAMP. Editing the merged PR's body does not backfill the link; the window shut at
#   merge. The item is genuinely done and genuinely unstampable, and the recipe forbids the only other
#   route ("faking it is how the board starts lying"). A red that fires reproducibly on correct,
#   merged, green work is the fastest way to teach every worker that red stamps are noise.
#   GitHub's OWN record of the closing act — the CLOSED_EVENT's `closer` — names PR 175. Stamp DONE.
cat >"$FIXTURES/done-165.json" <<'JSON'
{"data":{"repository":{"issue":{"number":165,"title":"closed by a squash commit whose SUBJECT carried the keyword","url":"https://github.com/FS-GG/FS.GG.SDD/issues/165","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":175,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/175","merged":true,"mergedAt":"2026-07-11T10:00:00Z","mergeCommit":{"abbreviatedOid":"5e5a17c"},
      "closingIssuesReferences":{"nodes":[]}}
  ]},
  "timelineItems":{"nodes":[{"closer":{"__typename":"PullRequest","number":175}}]},
  "subIssues":{"totalCount":0,"nodes":[]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4954}}
JSON
# #166: same, but GitHub recorded the closer as the COMMIT (the squash), not the PR. The commit's
#   associated PR is 176. Same verdict — GitHub still says that is what closed it.
cat >"$FIXTURES/done-166.json" <<'JSON'
{"data":{"repository":{"issue":{"number":166,"title":"closed by a COMMIT; the PR is reachable only through it","url":"https://github.com/FS-GG/FS.GG.SDD/issues/166","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":176,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/176","merged":true,"mergedAt":"2026-07-11T11:00:00Z","mergeCommit":{"abbreviatedOid":"77c0de1"},
      "closingIssuesReferences":{"nodes":[]}}
  ]},
  "timelineItems":{"nodes":[{"closer":{"__typename":"Commit","oid":"77c0de1","associatedPullRequests":{"nodes":[{"number":176}]}}}]},
  "subIssues":{"totalCount":0,"nodes":[]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4953}}
JSON
# #96: `--pr` USED TO SKIP PROVENANCE ENTIRELY (#543 leg 2) — it selected by NUMBER alone. So the
#   documented escape hatch from #558 was a SOUNDNESS HOLE that reintroduced #342: point it at any
#   merged PR that merely MENTIONS the issue and the stamp went green. PR 97 closes #70, not #96, and
#   no CLOSED_EVENT names it. `done #96 --pr 97` must REFUSE. `--pr` overrides WHICH PR, never WHETHER.
cat >"$FIXTURES/done-96.json" <<'JSON'
{"data":{"repository":{"issue":{"number":96,"title":"a merged PR mentions it; --pr must not launder that into a stamp","url":"https://github.com/FS-GG/FS.GG.SDD/issues/96","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":97,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/97","merged":true,"mergedAt":"2026-07-11T12:00:00Z","mergeCommit":{"abbreviatedOid":"deadfa1"},
      "closingIssuesReferences":{"nodes":[{"number":70,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}
  ]},
  "timelineItems":{"nodes":[]},
  "subIssues":{"totalCount":0,"nodes":[]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4952}}
JSON
# #507: DONE OVER AN OPEN CHILD (#583) — the #322 failure, in the command written to prevent it.
#   `epic_rollup` reads the sub-issue graph of the item's PARENT and never asks the same question of
#   the item in hand. A worker following pnext-item §4 (split off what you cannot land, `child`-link
#   it) therefore closes the parent over the split-out acceptance criterion, with a green ✓✓ actively
#   saying otherwise. The more faithfully a worker splits their work, the more reliably it fires.
#   Must REFUSE, and name the open child.
cat >"$FIXTURES/done-507.json" <<'JSON'
{"data":{"repository":{"issue":{"number":507,"title":"two of three criteria landed; the third was split out and child-linked","url":"https://github.com/FS-GG/FS.GG.SDD/issues/507","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":508,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/508","merged":true,"mergedAt":"2026-07-11T13:00:00Z","mergeCommit":{"abbreviatedOid":"c0ffee1"},
      "closingIssuesReferences":{"nodes":[{"number":507,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}
  ]},
  "timelineItems":{"nodes":[]},
  "subIssues":{"totalCount":1,"nodes":[
    {"number":585,"state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}
  ]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4951}}
JSON
# #509: the child list is TRUNCATED — 3 declared, 1 visible. "No open children" is then a statement
#   about a set we already know is incomplete. An unverifiable subject must not report green (#266).
cat >"$FIXTURES/done-509.json" <<'JSON'
{"data":{"repository":{"issue":{"number":509,"title":"more sub-issues than the query could see","url":"https://github.com/FS-GG/FS.GG.SDD/issues/509","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":510,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/510","merged":true,"mergedAt":"2026-07-11T14:00:00Z","mergeCommit":{"abbreviatedOid":"badca11"},
      "closingIssuesReferences":{"nodes":[{"number":509,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}
  ]},
  "timelineItems":{"nodes":[]},
  "subIssues":{"totalCount":3,"nodes":[
    {"number":511,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}
  ]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4950}}
JSON
# PR provenance (FS-GG/.github#342). `closedByPullRequestsReferences` also lists PRs that merely
# MENTION the issue (our "Filed, not fixed: #N" convention), lowest-number-first — so the old code's
# "first merged PR" stamped the wrong commit, or stamped green off an unrelated merge. These probe
# the no-`--pr` auto-selection (the `--pr` tests above exercise the explicit-override branch, which
# is unchanged). All three sit on the board as Done already, so a stamp turns only on PR selection.
#
# #84: real closer #92 (merged LATER), plus an earlier, lower-numbered PR #85 that only MENTIONS #84
#      in prose while actually closing #74 — the exact live case in the issue. Must stamp #92.
cat >"$FIXTURES/done-84.json" <<'JSON'
{"data":{"repository":{"issue":{"number":84,"title":"closed by its own PR, mentioned by an earlier one","url":"https://github.com/FS-GG/FS.GG.SDD/issues/84","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":85,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/85","merged":true,"mergedAt":"2026-07-10T13:41:56Z","mergeCommit":{"abbreviatedOid":"410843e"},
      "closingIssuesReferences":{"nodes":[{"number":74,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}},
    {"number":92,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/92","merged":true,"mergedAt":"2026-07-10T14:09:15Z","mergeCommit":{"abbreviatedOid":"09c836e"},
      "closingIssuesReferences":{"nodes":[{"number":84,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}
  ]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"Done"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4957}}
JSON
# #86: a merged PR #87 MENTIONS #86 but closes #70 — no PR closes #86. Must REFUSE, even though the
#      board says Done: a mention is not authorship (#342's second failure — green with no closer).
cat >"$FIXTURES/done-86.json" <<'JSON'
{"data":{"repository":{"issue":{"number":86,"title":"only mentioned by a merged PR, never closed by one","url":"https://github.com/FS-GG/FS.GG.SDD/issues/86","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":87,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/87","merged":true,"mergedAt":"2026-07-10T15:00:00Z","mergeCommit":{"abbreviatedOid":"beef777"},
      "closingIssuesReferences":{"nodes":[{"number":70,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}
  ]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"Done"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4956}}
JSON
# #88: reopened + re-closed, so TWO merged PRs both close it. The lower-numbered #89 merged EARLIER;
#      "latest merge" must win over "lowest number". Must stamp #95.
cat >"$FIXTURES/done-88.json" <<'JSON'
{"data":{"repository":{"issue":{"number":88,"title":"reopened and re-closed by two PRs","url":"https://github.com/FS-GG/FS.GG.SDD/issues/88","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[
    {"number":89,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/89","merged":true,"mergedAt":"2026-07-10T09:00:00Z","mergeCommit":{"abbreviatedOid":"1111aaa"},
      "closingIssuesReferences":{"nodes":[{"number":88,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}},
    {"number":95,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/95","merged":true,"mergedAt":"2026-07-10T11:00:00Z","mergeCommit":{"abbreviatedOid":"2222bbb"},
      "closingIssuesReferences":{"nodes":[{"number":88,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}
  ]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"Done"}}]},
  "parent":null}}},"rateLimit":{"cost":1,"remaining":4955}}
JSON

# ---- gh stub ------------------------------------------------------------------------------------
cat >"$STUB/gh" <<STUB
#!/usr/bin/env bash
set -euo pipefail
sub="\${1:-}"; sub2="\${2:-}"
args=("\$@")

if [ "\$sub" = "api" ] && [ "\$sub2" = "graphql" ]; then
  echo g >>"\$GH_GRAPHQL_COUNT"
  # GH_RATELIMIT=1: the GraphQL budget is EXHAUSTED — every query AND every mutation 403s, exactly as
  # it does on the real API, and in the real wording the client has to recognise (#418). REST keeps
  # working, which is the whole point: it is a different budget, and the claim lock lives on it.
  if [ -n "\${GH_RATELIMIT:-}" ]; then
    echo "gh: GraphQL: API rate limit exceeded for user ID 12345. (HTTP 403)" >&2; exit 1
  fi
  q=""; num=""
  for a in "\$@"; do case "\$a" in query=*) q="\${a#query=}";; num=*) num="\${a#num=}";; esac; done
  hascur=""; for a in "\$@"; do case "\$a" in cursor=*) hascur=1;; esac; done
  # Order matters: the done + rollup queries both select projectItems, and lint shares the
  # items(first:...) connection with the ready/next scan. Discriminate on the narrower marker first.
  if   printf '%s' "\$q" | grep -q 'projectsV2';                      then cat "$FIXTURES/projects.json"
  elif printf '%s' "\$q" | grep -q 'closedByPullRequestsReferences';  then cat "$FIXTURES/done-\$num.json"
  elif printf '%s' "\$q" | grep -q 'pullRequest' && printf '%s' "\$q" | grep -q 'closingIssuesReferences'; then
    # \`verify-paths\`' last-resort "which issue does this PR close?" query. MUST stay below the
    # \`closedByPullRequestsReferences\` arm above, whose query selects this connection too.
    # No fixture = the PR closes nothing, which is the pre-existing SKIP leg (PR 9).
    if [ -f "$FIXTURES/pr-closes-\$num.json" ]; then cat "$FIXTURES/pr-closes-\$num.json"
    else echo '{"data":{},"rateLimit":{"cost":1,"remaining":4999}}'; fi
  elif printf '%s' "\$q" | grep -q 'items(first' && printf '%s' "\$q" | grep -q 'subIssues'; then
    if [ -n "\$hascur" ]; then cat "$FIXTURES/lint-p2.json"; else cat "$FIXTURES/lint-p1.json"; fi
  elif printf '%s' "\$q" | grep -q 'items(first';      then
    # GH_FAIL_BOARD=1 makes the whole-board scan unreachable (a 401), modelling the rate-limit /
    # bad-credentials case #344 is about: the read cannot happen, so the client must fail CLOSED
    # (non-zero) rather than render an empty board as a confident, itemised answer.
    [ -n "\${GH_FAIL_BOARD:-}" ] && { echo "gh: Bad credentials (HTTP 401)" >&2; exit 1; }
    # GH_BOARD_SET=<name> serves fixtures/board-<name>.json instead of the default two-page board, so
    # the ADR-0027 tests get their own board without perturbing the existing count assertions. An
    # unknown name is a FIXTURE BUG, not a silent fallback to the default board.
    if [ -n "\${GH_BOARD_SET:-}" ]; then
      [ -f "$FIXTURES/board-\${GH_BOARD_SET}.json" ] \
        || { echo "gh stub: no board fixture '\$GH_BOARD_SET'" >&2; exit 5; }
      cat "$FIXTURES/board-\${GH_BOARD_SET}.json"
    elif [ -n "\$hascur" ]; then cat "$FIXTURES/board-items-p2.json"; else cat "$FIXTURES/board-items-p1.json"; fi
  elif printf '%s' "\$q" | grep -q 'subIssues';        then
    if [ -f "$FIXTURES/rollup-\$num.json" ]; then cat "$FIXTURES/rollup-\$num.json"
    else cat "$FIXTURES/rollup-none.json"; fi
  elif printf '%s' "\$q" | grep -q 'projectV2(number'; then cat "$FIXTURES/fields.json"
  elif printf '%s' "\$q" | grep -q 'projectItems' && printf '%s' "\$q" | grep -q 'fieldValueByName'; then
    # \`release\` reads the item's CURRENT Status before deciding whether to reset it (#331). Keyed on
    # the issue number, so one fixture serves items sitting in different columns:
    #   \$STORE/itemstatus-<num>   the Status name; an EMPTY file means on-board with no Status set
    #   GH_FAIL_ITEM_STATUS=<num>  the read fails (a 502) — release must not guess a Status
    #   GH_OFFBOARD_ITEM=<num>     the issue is on no board at all
    # Default \`In progress\`: that is what \`claim\` leaves behind, so the pre-existing release tests
    # keep exercising the reset path.
    if [ "\$num" = "\${GH_FAIL_ITEM_STATUS:-}" ]; then echo "gh: HTTP 502 Bad Gateway" >&2; exit 1; fi
    if [ "\$num" = "\${GH_OFFBOARD_ITEM:-}" ]; then
      jq -cn '{data:{repository:{issue:{projectItems:{nodes:[]}}}},rateLimit:{cost:1,remaining:4996}}'
      exit 0
    fi
    if [ -f "$STORE/itemstatus-\$num" ]; then st="\$(cat "$STORE/itemstatus-\$num")"; else st="In progress"; fi
    # A wrong-board node rides along, so the client is seen to select by project number, not by luck.
    jq -cn --arg st "\$st" '{data:{repository:{issue:{projectItems:{nodes:[
        {project:{number:7},  status:{name:"Wrong board"}},
        {project:{number:12}, status:(if \$st == "" then null else {name:\$st} end)}
      ]}}}},rateLimit:{cost:1,remaining:4996}}'
    exit 0
  elif printf '%s' "\$q" | grep -q 'projectItems';     then
    # GH_OFFBOARD_ITEM=<num> must answer the SAME way here as it does for the Status read above, or
    # the fixture would have item-id and item-status disagreeing about whether <num> is on the board.
    if [ "\$num" = "\${GH_OFFBOARD_ITEM:-}" ]; then
      jq -cn '{data:{repository:{issue:{id:"I_offboard",title:"offboard",projectItems:{nodes:[]}}}},rateLimit:{cost:1,remaining:4997}}'
    else cat "$FIXTURES/item.json"; fi
  else echo '{"data":{},"rateLimit":{"cost":1,"remaining":4999}}'; fi
  exit 0
fi

if [ "\$sub" = "api" ] && [ "\$sub2" = "rate_limit" ]; then
  expr=""; n=\${#args[@]}
  for ((i=0;i<n;i++)); do [ "\${args[i]}" = "--jq" ] && expr="\${args[i+1]}"; done
  # Under GH_RATELIMIT the meter reads what the 403s imply — 0 GraphQL, REST untouched. `reset` sits
  # in the future so \`rate_reset_in\` renders a real countdown instead of clamping to 0m00s.
  if [ -n "\${GH_RATELIMIT:-}" ]; then
    rlreset=\$(( \$(date -u +%s) + 900 ))
    payload='{"resources":{"graphql":{"remaining":0,"limit":5000,"reset":'\$rlreset'},"core":{"remaining":4700,"limit":5000,"reset":'\$rlreset'}}}'
  else
    payload='{"resources":{"graphql":{"remaining":4321,"limit":5000,"reset":1751630400},"core":{"remaining":4990,"limit":5000,"reset":1751630400}}}'
  fi
  if [ -n "\$expr" ]; then printf '%s' "\$payload" | jq -r "\$expr"; else printf '%s' "\$payload"; fi
  exit 0
fi

if [ "\$sub" = "api" ]; then
  echo r >>"\$GH_REST_COUNT"
  method=""; path=""; inm=""; jqexpr=""; body=""; include=""; hasfield=""; paginate=""
  subid_f=""; subid_F=""
  n=\${#args[@]}
  for ((i=1;i<n;i++)); do
    case "\${args[i]}" in
      -X)        method="\${args[i+1]}" ;;
      --include)  include=1 ;;
      --paginate) paginate=1 ;;
      --jq)      jqexpr="\${args[i+1]}" ;;
      -H)        h="\${args[i+1]}"; case "\$h" in "If-None-Match: "*) inm="\${h#If-None-Match: }";; esac ;;
      -f)        hasfield=1; kv="\${args[i+1]}"; case "\$kv" in body=*) body="\${kv#body=}";; sub_issue_id=*) subid_f="\${kv#sub_issue_id=}";; esac ;;
      -F)        hasfield=1; kv="\${args[i+1]}"; case "\$kv" in body=@*) body="\$(cat "\${kv#body=@}")";; sub_issue_id=*) subid_F="\${kv#sub_issue_id=}";; esac ;;
      user)      [ -z "\$path" ] && path="user" ;;
      repos/*)   path="\${args[i]}" ;;
    esac
  done
  # Real \`gh api\` infers POST when fields are supplied and no method is given. A stub that defaults
  # to GET would silently serve a comment LIST where the client expects the created comment's id.
  [ -n "\$method" ] || { [ -n "\$hasfield" ] && method="POST" || method="GET"; }
  emit() { if [ -n "\$jqexpr" ]; then jq -r "\$jqexpr"; else cat; fi; }
  now="\$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  # --- the repo is part of the SUBJECT, and every issue read must PROVE it (#494) ------------------
  # The store is keyed by issue NUMBER, but every fixture records the repo it belongs to. Serving a
  # read addressed to some OTHER repo out of that fixture is precisely the confusion #479 was: the
  # stub would answer for a subject the client never asked for, and no payload could ever betray it —
  # so the harness would be exactly as repo-blind as the bug, and the whole class untestable.
  # Two distinct misses, and they are NOT the same failure:
  #   - no fixture with that number at all  -> exit 4, a loud STUB bug (a test forgot to seed).
  #   - a fixture that lives in ANOTHER repo -> 404, exactly as GitHub answers it. That is a CLIENT
  #     bug, and it must reach the client as the unreachable subject it really is (#266) — never as
  #     a silent fallback to the same-numbered issue next door.
  # Every issue-side arm below routes through this and logs '<verb> <owner>/<repo> <n>', so a test can
  # assert the subject from the REQUEST — the way #479 had to for the two pulls/ arms, because the
  # payload cannot carry the answer.
  #
  # The store is keyed two ways, and the REPO-QUALIFIED key wins:
  #   issue-<owner>__<repo>-<n>.json   a genuine per-repo issue. Seed these (seed_issue_in) when a
  #                                    test needs the SAME number to exist in two repos as two
  #                                    DIFFERENT issues — the shape #479 straddles, and the shape a
  #                                    number-keyed store cannot represent at all.
  #   issue-<n>.json                   the unqualified default, still answering for exactly ONE repo:
  #                                    the one its own .repo names. It is a default, NOT a fallback —
  #                                    a read from any other repo gets the 404 below, never this file.
  #
  # Sets JF (the issue fixture), CF (its comments) and KEY (the store key both are built from — the
  # suffix every other per-subject file must also carry, or two repos would share one). Call it
  # DIRECTLY, never in a \$( ): an exit inside a command substitution kills only the subshell, and the
  # stub would sail on with an empty JF.
  issue_guard() {  # \$1=<owner>/<repo>  \$2=<num> -> sets JF, CF, KEY, or exits (404 / stub bug)
    local home
    KEY="\${1//\//__}-\$2"
    JF="$STORE/issue-\$KEY.json"; CF="$STORE/comments-\$KEY.json"
    [ -f "\$JF" ] && return 0
    KEY="\$2"; JF="$STORE/issue-\$2.json"; CF="$STORE/comments-\$2.json"
    [ -f "\$JF" ] || { echo "gh stub: no issue fixture \$1#\$2" >&2; exit 4; }
    home="\$(jq -r '.repo // empty' "\$JF")"
    if [ -n "\$home" ] && [ "\$home" != "\$1" ]; then
      printf 'issue-404 %s %s (lives in %s)\n' "\$1" "\$2" "\$home" >>"\$GH_LOG"
      echo "gh: Not Found (HTTP 404)" >&2; exit 1
    fi
  }

  # An injection var names a SUBJECT, and a subject is a repo and a number — not a number. The bare
  # form (GH_VANISH_ISSUE=70) is the legacy one and still fires on #70 in whatever repo has it, which
  # was unambiguous only while a number could name just one issue. Now that the store can hold SDD#70
  # and Rendering#70 at once, a test that needs to aim at exactly one of them writes the qualified
  # form (GH_VANISH_ISSUE=FS-GG/FS.GG.SDD#70). Accept both (#494).
  injected() {  # \$1=<env value>  \$2=<owner>/<repo>  \$3=<num> -> 0 if THIS subject is the target
    [ -n "\$1" ] || return 1
    [ "\$1" = "\$3" ] || [ "\$1" = "\$2#\$3" ]
  }

  if [ "\$path" = "user" ]; then printf '{"login":"EHotwagner"}' | emit; exit 0; fi

  # --- assignees: the REST form of "assign @me" (#418) --------------------------------------------
  # \`gh issue edit --add-assignee\` costs 4 GraphQL points; this endpoint costs 0 of them. The stub
  # logs the two directions so the fixture can assert the client took the REST road and not the old one.
  if [[ "\$path" =~ ^repos/([^/]+)/([^/]+)/issues/([0-9]+)/assignees\$ ]]; then
    anwo="\${BASH_REMATCH[1]}/\${BASH_REMATCH[2]}"; anum="\${BASH_REMATCH[3]}"
    issue_guard "\$anwo" "\$anum"
    printf 'assignee-%s %s %s\n' "\$(printf '%s' "\$method" | tr 'A-Z' 'a-z')" "\$anwo" "\$anum" >>"\$GH_LOG"
    printf '{"number":%s}' "\$anum" | emit; exit 0
  fi

  # --- sub-issues: the native child edge `child` writes and `epic_rollup` reads -------------------
  # A real mutable store, so `child` can be observed to be idempotent rather than merely exit 0. The
  # stub also reproduces the API's two traps: the endpoint keys on the child's REST **id** (not its
  # number), and it 422s when sub_issue_id arrives as a JSON string — i.e. via \`gh api -f\`. A stub
  # that accepted -f would let the client regress to the form the API rejects.
  if [[ "\$path" =~ ^repos/([^/]+)/([^/]+)/issues/([0-9]+)/sub_issues\$ ]]; then
    snwo="\${BASH_REMATCH[1]}/\${BASH_REMATCH[2]}"; snum="\${BASH_REMATCH[3]}"
    issue_guard "\$snwo" "\$snum"
    printf 'sub-issue-%s %s %s\n' "\$(printf '%s' "\$method" | tr 'A-Z' 'a-z')" "\$snwo" "\$snum" >>"\$GH_LOG"
    sf="\${JF/\/issue-/\/subissues-}"     # parallel to the issue key, so it is repo-qualified too
    [ -f "\$sf" ] || echo '[]' >"\$sf"
    # GH_FAIL_SUBISSUES_GET=<n>: the existing-links read for <n> fails. `child` must not read that as
    # "the edge is absent" and POST anyway — an unreachable subject is not an absent one (#266/#320).
    if [ "\$method" = "GET" ] && injected "\${GH_FAIL_SUBISSUES_GET:-}" "\$snwo" "\$snum"; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    if [ "\$method" = "POST" ]; then
      # GH_FORCE_SUBISSUE_POST_FAIL=1: the link POST fails the way the real API does. `child` must
      # relay THIS text, not a guessed cause.
      if [ -n "\${GH_FORCE_SUBISSUE_POST_FAIL:-}" ]; then
        echo 'gh: Validation Failed (HTTP 422): sub_issue_id is not valid' >&2; exit 1
      fi
      if [ -n "\$subid_f" ]; then
        echo 'gh: Validation Failed (HTTP 422): sub_issue_id must be an integer' >&2; exit 1
      fi
      [ -n "\$subid_F" ] || { echo "gh: sub_issue_id required" >&2; exit 1; }
      printf 'sub-issue-add %s %s -F sub_issue_id=%s\n' "\$snwo" "\$snum" "\$subid_F" >>"\$GH_LOG"
      jq --argjson id "\$subid_F" 'if any(.[]; .id == \$id) then . else . + [{"id":\$id,"number":(\$id - 1000)}] end' \
        "\$sf" >"\$sf.t" && mv "\$sf.t" "\$sf"
      cat "\$sf" | emit; exit 0
    fi
    cat "\$sf" | emit; exit 0
  fi

  # --- issue comments: a REAL mutable store, so the claim CAS can actually be raced -------------
  if [[ "\$path" =~ ^repos/([^/]+)/([^/]+)/issues/([0-9]+)/comments ]]; then
    cnwo="\${BASH_REMATCH[1]}/\${BASH_REMATCH[2]}"; cnum="\${BASH_REMATCH[3]}"
    issue_guard "\$cnwo" "\$cnum"
    cf="\$CF"
    [ -f "\$cf" ] || echo '[]' >"\$cf"
    [ "\$method" = "GET" ] && printf 'comment-list %s %s\n' "\$cnwo" "\$cnum" >>"\$GH_LOG"

    # GH_FAIL_READ_ISSUE=<n>: reads of <n>'s comments fail once a marker has been POSTed there.
    # Models a transient gh failure (rate limit / 5xx) landing on the CAS re-read, i.e. after our
    # marker exists but before we know whether we won it.
    if [ "\$method" = "GET" ] && injected "\${GH_FAIL_READ_ISSUE:-}" "\$cnwo" "\$cnum" && [ -f "$STORE/posted-\$KEY" ]; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    # GH_VANISH_ISSUE=<n>: our marker is GONE by the time the CAS re-reads (a peer's --force/reap
    # collected it, or the read lagged the write). The re-read sees NO live marker at all, so the
    # claimant cannot show it holds the lock. It must treat that as a loss, not a win.
    if [ "\$method" = "GET" ] && injected "\${GH_VANISH_ISSUE:-}" "\$cnwo" "\$cnum" && [ -f "$STORE/posted-\$KEY" ]; then
      jq 'map(select(.body | test("^<!--\\\\s*fsgg:claim") | not))' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
    fi
    # GH_REAP_RACE=<n>: the holder heartbeats between reap's snapshot read and its delete. Every read
    # after the first returns a freshly-renewed marker.
    if [ "\$method" = "GET" ] && injected "\${GH_REAP_RACE:-}" "\$cnwo" "\$cnum"; then
      rc="\$(cat "$STORE/readcount-\$KEY" 2>/dev/null || echo 0)"
      echo \$((rc + 1)) >"$STORE/readcount-\$KEY"
      if [ "\$rc" -ge 1 ]; then
        jq --arg ts "\$now" 'map(.updated_at = \$ts)' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      fi
    fi

    if [ "\$method" = "POST" ]; then
      touch "$STORE/posted-\$KEY"
      # GH_RACE_INJECT=<worker>: a rival worker's marker lands BETWEEN our read and our re-read,
      # taking a LOWER comment id. This is the exact interleaving the CAS exists to resolve.
      if [ -n "\${GH_RACE_INJECT:-}" ] && injected "\${GH_RACE_ISSUE:-}" "\$cnwo" "\$cnum"; then
        rid="\$(cat "$STORE/nextid")"; echo \$((rid + 1)) >"$STORE/nextid"
        jq --argjson id "\$rid" --arg w "\$GH_RACE_INJECT" --arg ts "\$now" \
          '. + [{id:\$id, body:("<!-- fsgg:claim worker=" + \$w + " lease=120 -->\nrival"),
                 user:{login:"EHotwagner"}, created_at:\$ts, updated_at:\$ts}]' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      fi
      id="\$(cat "$STORE/nextid")"; echo \$((id + 1)) >"$STORE/nextid"
      jq --argjson id "\$id" --arg b "\$body" --arg ts "\$now" \
        '. + [{id:\$id, body:\$b, user:{login:"EHotwagner"}, created_at:\$ts, updated_at:\$ts}]' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      printf 'comment-post %s %s %s\n' "\$cnwo" "\$cnum" "\$id" >>"\$GH_LOG"
      jq -n --argjson id "\$id" '{id:\$id}' | emit; exit 0
    fi
    emit <"\$cf"; exit 0
  fi

  # --- a single comment by id: PATCH (heartbeat) / DELETE (release, back-off, reap) -------------
  # GH_FAIL_DELETE=<id>: the DELETE of <id> fails with a 500 (transient). Models "the marker survives".
  # A DELETE of an id that is NOT in the store 404s, exactly as GitHub does — the collector's benign
  # "somebody already removed it" case, which must not read as a hard failure.
  if [[ "\$path" =~ ^repos/([^/]+)/([^/]+)/issues/comments/([0-9]+) ]]; then
    mnwo="\${BASH_REMATCH[1]}/\${BASH_REMATCH[2]}"; cid="\${BASH_REMATCH[3]}"
    if [ "\$method" = "DELETE" ] && [ "\$cid" = "\${GH_FAIL_DELETE:-}" ]; then
      printf 'comment-delete-failed %s\n' "\$cid" >>"\$GH_LOG"
      echo "gh: HTTP 500 Internal Server Error" >&2; exit 1
    fi
    # GH_DELETE_404=<id>: a rival collector's DELETE of <id> landed first. Ours 404s, and the comment
    # really is gone afterwards — "already collected", which is success for a garbage collector.
    if [ "\$method" = "DELETE" ] && [ "\$cid" = "\${GH_DELETE_404:-}" ]; then
      for cf in "$STORE"/comments-*.json; do
        [ -f "\$cf" ] || continue
        jq --argjson id "\$cid" 'map(select(.id != \$id))' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      done
      printf 'comment-delete-404 %s\n' "\$cid" >>"\$GH_LOG"
      echo "gh: Not Found (HTTP 404)" >&2; exit 1
    fi
    found=""
    for cf in "$STORE"/comments-*.json; do
      [ -f "\$cf" ] || continue
      jq -e --argjson id "\$cid" 'any(.[]; .id == \$id)' "\$cf" >/dev/null 2>&1 || continue
      found=1
      # A comment id is globally unique, but it is still only reachable through the repo that OWNS
      # its issue. Marker PATCH/DELETE is how `heartbeat` and `release` touch the lock, so a
      # cross-repo slip here would renew or collect a marker on the wrong repo's item (#494).
      mjf="\${cf/\/comments-/\/issue-}"
      mhome="\$(jq -r '.repo // empty' "\$mjf" 2>/dev/null || true)"
      if [ -n "\$mhome" ] && [ "\$mhome" != "\$mnwo" ]; then
        printf 'comment-404 %s %s (lives in %s)\n' "\$mnwo" "\$cid" "\$mhome" >>"\$GH_LOG"
        echo "gh: Not Found (HTTP 404)" >&2; exit 1
      fi
      if [ "\$method" = "DELETE" ]; then
        printf 'comment-delete %s %s\n' "\$mnwo" "\$cid" >>"\$GH_LOG"
        jq --argjson id "\$cid" 'map(select(.id != \$id))' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      else
        printf 'comment-patch %s %s\n' "\$mnwo" "\$cid" >>"\$GH_LOG"
        jq --argjson id "\$cid" --arg b "\$body" --arg ts "\$now" \
          'map(if .id == \$id then .body = \$b | .updated_at = \$ts else . end)' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      fi
      break
    done
    if [ -z "\$found" ]; then
      printf 'comment-%s-404 %s\n' "\$(printf '%s' "\$method" | tr 'A-Z' 'a-z')" "\$cid" >>"\$GH_LOG"
      echo "gh: Not Found (HTTP 404)" >&2; exit 1
    fi
    echo '{}' | emit; exit 0
  fi

  # Both PR reads LOG THE REPO they were asked for. The fixtures are keyed by PR number alone — which
  # is to say the stub is exactly as repo-blind as the bug in .github#479 was, and would happily serve
  # \`FS.GG.Audio/pulls/48\` from \`.github\`'s PR 48. So the repo cannot be asserted from the payload;
  # it has to be asserted from the REQUEST. That is what this log line is for.
  # THE LIVENESS PROBE (#581): `pr_alive` lists OPEN pull requests and looks for a head branch
  # `item/<n>-*`. GH_LIVE_PR="<num>:<pr>" puts an open PR on item <num>. This is the ONLY signal that
  # can tell "the worker died" from "the build took longer than the lease" — and getting that wrong
  # reaped live, uncommitted work twice, once from the worker who was fixing the issue about it.
  if [[ "\$path" =~ ^repos/([^/]+/[^/]+)/pulls\?state=open ]]; then
    printf 'pulls-list %s\n' "\${BASH_REMATCH[1]}" >>"\$GH_LOG"
    if [ -n "\${GH_LIVE_PR:-}" ]; then
      livenum="\${GH_LIVE_PR%%:*}"; livepr="\${GH_LIVE_PR##*:}"
      printf '[{"number":%s,"head":{"ref":"item/%s-live-work"}}]\n' "\$livepr" "\$livenum" | emit
    else
      echo '[]' | emit
    fi
    exit 0
  fi
  if [[ "\$path" =~ ^repos/([^/]+/[^/]+)/pulls/([0-9]+)/files ]]; then
    printf 'pr-files %s %s\n' "\${BASH_REMATCH[1]}" "\${BASH_REMATCH[2]}" >>"\$GH_LOG"
    emit <"$FIXTURES/pr-files-\${BASH_REMATCH[2]}.json"; exit 0
  fi
  # GH_FAIL_PR_GET=<n>: the head-ref read for PR <n> fails. `verify-paths` resolves which issue a PR
  # implements from its branch name here, and an empty answer would read as "the branch is not
  # item/<n>-…" — i.e. a SKIP verdict invented from an unanswered query (.github#322).
  if [[ "\$path" =~ ^repos/([^/]+/[^/]+)/pulls/([0-9]+)$ ]]; then
    printf 'pr-get %s %s\n' "\${BASH_REMATCH[1]}" "\${BASH_REMATCH[2]}" >>"\$GH_LOG"
    if [ "\${BASH_REMATCH[2]}" = "\${GH_FAIL_PR_GET:-}" ]; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    emit <"$FIXTURES/pr-\${BASH_REMATCH[2]}.json"; exit 0
  fi

  # --- a single issue: GET (title/body/Paths) or PATCH (widen rewrites the body) ----------------
  # GH_FAIL_ISSUE_GET=<n>: the body read for <n> fails. `paths_of` reads the touch-set here, and an
  # empty answer would read as "declared nothing" — i.e. disjoint from everything.
  if [[ "\$path" =~ ^repos/([^/]+)/([^/]+)/issues/([0-9]+)$ ]]; then
    inwo="\${BASH_REMATCH[1]}/\${BASH_REMATCH[2]}"; inum="\${BASH_REMATCH[3]}"
    if [ "\$method" = "GET" ] && injected "\${GH_FAIL_ISSUE_GET:-}" "\$inwo" "\$inum"; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    issue_guard "\$inwo" "\$inum"
    if [ "\$method" = "PATCH" ]; then
      printf 'issue-patch %s %s\n' "\$inwo" "\$inum" >>"\$GH_LOG"
      jq --arg b "\$body" '.body = \$b' "\$JF" >"\$JF.t" && mv "\$JF.t" "\$JF"
    else
      printf 'issue-get %s %s\n' "\$inwo" "\$inum" >>"\$GH_LOG"
    fi
    emit <"\$JF"; exit 0
  fi

  # GH_ISSUE_LIST_MALFORMED=1: the claim-candidate read comes back as bytes that are NOT JSON, and
  # gh EXITS 0 (#461). This is the failure the lock's fail-open lived in — a truncated page, a proxy
  # error body, a transient 5xx rendered as text. It must NOT be confused with an empty list, which
  # is a legitimate "nobody holds anything". Serving this arm BEFORE the store arm so the malformed
  # response wins for any issue-list read.
  if [ -n "\${GH_ISSUE_LIST_MALFORMED:-}" ] && [[ "\$path" =~ ^repos/[^/]+/[^/]+/issues\? ]]; then
    printf '<html><body>502 Bad Gateway</body></html>\n'
    exit 0
  fi

  # --- the issue LIST, served LIVE from the comment store -----------------------------------------
  # GH_ISSUES_FROM_STORE=1: build the list from the seeded issues, stamping each one's REAL comment
  # count. open_claim_candidates prunes on "comments > 0", so a static fixture would report 0
  # comments for an issue a test had just claimed — and the blind spot under test would "pass" for
  # the wrong reason. No ETag is served: the real client reads this list directly, via --paginate,
  # precisely so that no cache can hide a live marker behind a stale "comments: 0".
  if [ -n "\${GH_ISSUES_FROM_STORE:-}" ] && [[ "\$path" =~ ^repos/([^/]+)/([^/]+)/issues\? ]]; then
    lo="\${BASH_REMATCH[1]}"; lr="\${BASH_REMATCH[2]}"
    # Record HOW the lock's candidate list was fetched: it must paginate (no 100-issue lock limit)
    # and must not send a conditional request (no cache may hide a live marker).
    printf 'issue-list %s paginate=%s inm=%s\n' "\$lo/\$lr" "\${paginate:-0}" "\${inm:-none}" >>"\$GH_LOG"
    # Accumulate over a FILE, not through argv (#497). The real API happily serves >128 KiB of issue
    # bodies; a stub that re-passes its own accumulator as \`--argjson\` cannot, because a single
    # argument is capped at MAX_ARG_STRLEN. That cap is the very bug under test, so a stub carrying
    # it would E2BIG before the client ever ran — and the defect would be structurally untestable
    # (cf. #494). \`printf\` is a bash BUILTIN, so piping \$out onward is not an exec and not capped.
    lst="\$(mktemp)"
    for jf in "$STORE"/issue-*.json; do
      [ -f "\$jf" ] || continue
      # The comment count must come from THIS fixture's own comment file — derive it from the issue
      # file's name, which carries the repo qualification when there is one. Recomposing the path
      # from \`.number\` alone would hand a repo-qualified issue the OTHER repo's comment count (#494).
      cf="\${jf/\/issue-/\/comments-}"
      cc=0; [ -f "\$cf" ] && cc="\$(jq 'length' "\$cf")"
      jq -c --argjson cc "\$cc" '. + {comments: \$cc}' "\$jf" >>"\$lst"
    done
    out="\$(jq -c -s '.' "\$lst")"; rm -f "\$lst"
    [ -n "\$include" ] && { printf 'HTTP/2.0 200 OK\r\n'; printf '\r\n'; }
    printf '%s' "\$out" \
      | jq -c --arg nwo "\$lo/\$lr" 'map(select(.repo == \$nwo) | select(.state == "open"))' \
      | emit
    exit 0
  fi

  # --- the issue LIST (the ETag-revalidated `issues` command) -----------------------------------
  etag='"issues-etag-v1"'
  if [ -n "\$inm" ] && [ "\$inm" = "\$etag" ]; then
    echo "gh: HTTP 304 Not Modified" >&2; exit 1
  fi
  if [ -n "\$include" ]; then
    printf 'HTTP/2.0 200 OK\r\n'; printf 'ETag: %s\r\n' "\$etag"; printf '\r\n'
  fi
  # Through emit, so a --jq passed to \`gh api\` is honoured here as the real thing honours it.
  # open_claim_candidates projects the list server-side; a raw cat would hand it a nested array.
  emit <"$FIXTURES/issues.json"
  exit 0
fi

if [ "\$sub" = "project" ] && [ "\$sub2" = "item-add" ]; then
  # `add` (#587) — the verb whose ABSENCE is why every recipe reached past the client. It must go
  # through the same budget classification as every other board write, so the stub speaks the
  # rate-limit here too.
  [ -n "\${GH_RATELIMIT:-}" ] && { echo "gh: API rate limit already exceeded (HTTP 403)" >&2; exit 1; }
  printf 'item-add %s\n' "\$*" >>"\$GH_LOG"
  echo '{"id":"PVTI_newlyadded"}'
  exit 0
fi

if [ "\$sub" = "project" ] && [ "\$sub2" = "item-edit" ]; then
  # GH_FAIL_ITEM_EDIT=1: the board write fails — a Projects v2 5xx, or an item with no board entry
  # to edit. The MARKER is the lock, so nothing that holds it may unwind on this; but nothing may
  # report a board mutation it did not perform either.
  [ -n "\${GH_FAIL_ITEM_EDIT:-}" ] && { echo "gh: HTTP 502 Bad Gateway" >&2; exit 1; }
  # Projects v2 is GraphQL, so an exhausted budget takes the board WRITES with it — the half of #418
  # that actually causes the drift. \`gh project\` phrases it slightly differently from
  # \`gh api graphql\`; the client must recognise BOTH, so the stub speaks this one here.
  [ -n "\${GH_RATELIMIT:-}" ] && { echo "gh: API rate limit already exceeded (HTTP 403)" >&2; exit 1; }
  # Real \`gh project item-edit\` requires a non-empty --id and errors without one. Model that, so a
  # caller that swallowed its item resolution (empty --id) cannot silently read as a successful write
  # — the exact shape of #344 one layer down: set_field's \`item="\$(item_id …)"\` swallows item_id's
  # \`die\`, and only the malformed edit failing turns that back into a caught failure.
  idv=""; ne=\${#args[@]}
  for ((i=0;i<ne;i++)); do [ "\${args[i]}" = "--id" ] && idv="\${args[i+1]:-}"; done
  [ -n "\$idv" ] || { echo "gh: the '--id' flag is required" >&2; exit 1; }
  printf 'item-edit %s\n' "\$*" >>"\$GH_LOG"; exit 0
fi

if [ "\$sub" = "issue" ] && [ "\$sub2" = "edit" ]; then
  printf 'issue-edit %s\n' "\$*" >>"\$GH_LOG"; exit 0
fi

if [ "\$sub" = "repo" ] && [ "\$sub2" = "view" ]; then
  printf 'FS-GG/FS.GG.SDD\n'; exit 0
fi

echo "gh stub: unhandled: \$*" >&2; exit 3
STUB
chmod +x "$STUB/gh"

# ================================================================================================
echo "fsgg-coord fixture — cache='$FSGG_COORD_CACHE'"

# (1) bootstrap once; later reads add zero GraphQL calls.
run bootstrap >/dev/null
after_bootstrap="$(gcount)"
assert_eq "bootstrap: 2 GraphQL calls (projects + fields)" "2" "$after_bootstrap"

board="$(run board)"
assert_eq "board: number cached"        "12"           "$(jq -r '.number' <<<"$board")"
assert_eq "board: node id cached"       "PVT_coord"    "$(jq -r '.id' <<<"$board")"
assert_eq "board: Phase is SINGLE_SELECT" "SINGLE_SELECT" "$(jq -r '.fields.Phase.dataType' <<<"$board")"
assert_eq "board: Phase option id cached" "opt_p2"     "$(jq -r '.fields.Phase.options["P2 SDD"]' <<<"$board")"
assert_eq "board: Target is DATE"       "DATE"         "$(jq -r '.fields.Target.dataType' <<<"$board")"

assert_eq "field-id Phase (from cache)"  "PVTSSF_phase" "$(run field-id Phase)"
assert_eq "option-id Phase 'P2 SDD'"     "opt_p2"       "$(run option-id Phase 'P2 SDD')"
assert_eq "board/field-id/option-id add ZERO GraphQL calls" "$after_bootstrap" "$(gcount)"

# (2) item-id: narrow resolve, picks the right board, then caches.
before_item="$(gcount)"
assert_eq "item-id resolves the Coordination item" "PVTI_coord123" "$(run item-id 'FS.GG.SDD#42')"
assert_eq "item-id: exactly one GraphQL call" "$((before_item + 1))" "$(gcount)"
assert_eq "item-id again is served from cache (zero calls)" "$((before_item + 1))" "$(run item-id 'FS.GG.SDD#42' >/dev/null; gcount)"
assert_eq "item-id accepts owner/repo#n form" "PVTI_coord123" "$(run item-id 'FS-GG/FS.GG.SDD#42')"
assert_eq "item-id accepts a full URL"        "PVTI_coord123" "$(run item-id 'https://github.com/FS-GG/FS.GG.SDD/issues/42')"

# (3) set-field auto-routes by dataType, resolving all ids from cache.
run set-field 'FS.GG.SDD#42' Phase 'P2 SDD' >/dev/null
run set-field 'FS.GG.SDD#42' Target '2026-08-01' >/dev/null
run set-field 'FS.GG.SDD#42' Contract 'fs-gg-ui-template' >/dev/null
edits="$(cat "$GH_LOG")"
assert_contains "set-field SINGLE_SELECT -> option id" "--single-select-option-id opt_p2" "$edits"
assert_contains "set-field passes the resolved field id" "--field-id PVTSSF_phase" "$edits"
assert_contains "set-field passes the project + item ids" "--project-id PVT_coord --field-id PVTSSF_phase" "$edits"
assert_contains "set-field targets the resolved item"  "--id PVTI_coord123"        "$edits"
assert_contains "set-field DATE -> --date"             "--date 2026-08-01"          "$edits"
assert_contains "set-field TEXT -> --text"             "--text fs-gg-ui-template"   "$edits"
: >"$GH_LOG"
run set-field 'FS.GG.SDD#42' Contract '' >/dev/null
assert_contains "set-field: an empty value clears ANY field (not just Blocked by)" "--clear" "$(cat "$GH_LOG")"

# (4) issues over REST with ETag; unchanged repeat -> 304 served from cache.
body1="$(run issues FS.GG.SDD --label cross-repo 2>/dev/null)"
assert_contains "issues: first call returns the list" '"number":42' "$body1"
rest_after_first="$(rcount)"
err2="$(run issues FS.GG.SDD --label cross-repo 2>&1 >/dev/null)"
assert_contains "issues: repeat revalidates and 304s to cache" "304 Not Modified — served from cache" "$err2"
assert_eq "issues: the 304 path used the cached body" '42' "$(run issues FS.GG.SDD --label cross-repo --jq '.[].number' 2>/dev/null)"

# (5) bad names fail loudly.
assert_fails "field-id on an unknown field fails"  run field-id Bogus
assert_fails "option-id on an unknown option fails" run option-id Phase 'P9 Nope'

write_issue() {  # write_issue <key> <num> <title> <body> <owner/repo> <state>
  jq -n --argjson n "$2" --arg t "$3" --arg b "$4" --arg r "$5" --arg s "$6" \
    '{id:($n + 1000), number:$n, title:$t, body:$b, assignees:[], state:$s, repo:$r,
      html_url:("https://github.com/" + $r + "/issues/" + ($n|tostring))}' >"$STORE/issue-$1.json"
  echo '[]' >"$STORE/comments-$1.json"
}
declared_body() {  # <paths-or-empty> -> a body that declares them (or declares nothing)
  local b="Some description."
  [ -n "$1" ] && b="$b

Paths: $1"
  printf '%s' "$b"
}
seed_issue() {      # seed_issue <num> <title> <paths-or-empty> [owner/repo]
  write_issue "$1" "$1" "$2" "$(declared_body "$3")" "${4:-FS-GG/FS.GG.SDD}" open
}
# A raw-body seeder. `seed_issue` can only produce a well-formed declaration; #277 is about bodies
# that merely LOOK like they declare one, so those tests need to write the body verbatim.
seed_issue_raw() {  # seed_issue_raw <num> <title> <body> [owner/repo]
  write_issue "$1" "$1" "$2" "$3" "${4:-FS-GG/FS.GG.SDD}" open
}
# Seed a REPO-QUALIFIED issue: the same number, in another repo, as a genuinely different issue with
# its own body and its own touch-set (#494). `state` defaults to open; pass `closed` for a fixture that
# exists only to be READ (a `paths_of` subject), so it stays out of every open-issue candidate scan.
seed_issue_in() {   # seed_issue_in <owner/repo> <num> <title> <paths-or-empty> [state]
  write_issue "${1//\//__}-$2" "$2" "$3" "$(declared_body "$4")" "$1" "${5:-open}"
}

# #485 leg (a): `next` no longer has its own idea of "startable" — it IS `batch`, which means it now
# demands a declared touch-set like everything else that schedules. The board rows below therefore need
# real issues behind them, with real `Paths:`. That is not fixture bookkeeping, it IS the fix: `next`
# used to recommend items `batch` refuses, to the one audience that cannot check — a human asking "what
# should I do next" — and it did so live, recommending an item that overlapped a live claim.
#
# #200 and #201 are deliberately DISJOINT: the blocker tests below must measure blocker handling, not
# a touch-set collision.
seed_issue_in FS-GG/FS.GG.Templates   99 "Re-mirror minimumFsggSdd"  "src/Templates/**"
seed_issue_in FS-GG/FS.GG.SDD        127 "TD1 SDD epic"              "src/Sdd/**"
seed_issue_in FS-GG/FS.GG.Audio      189 "Placeholder dash"          "src/Audio/Dash.fs"
# The #520/#476/#496 residue subjects. They are on the board from the FIRST scan, so their bodies must
# exist from the first scan too: `body_of` fails CLOSED (correctly), so an unseeded board item kills
# every `batch` call in the file — which is exactly how the first cut of these fixtures broke `next`.
seed_issue_in FS-GG/FS.GG.Rendering  502 "closed but still Ready"    "src/Closed/**"
seed_issue_in FS-GG/FS.GG.Templates  350 "blocked by a merged PR"    "src/Tpl350/**"
seed_issue_in FS-GG/FS.GG.Audio       31 "unmatchable tokens"        "**/packages.lock.json"
seed_issue_in FS-GG/FS.GG.Rendering  200 "Blocked on an open item"   "src/Render/Blocked.fs"
seed_issue_in FS-GG/FS.GG.Rendering  201 "Blocker already closed"    "src/Render/Clear.fs"
seed_issue_in FS-GG/FS.GG.Governance 202 "Blocker not on the board"  "src/Gov/Off.fs"

# (6) ready/next: thrifty whole-board read, paginated, client-side filtered.
before_ready="$(gcount)"
ready_all="$(run ready --json 2>/dev/null)"
assert_eq "ready: paginates in exactly 2 GraphQL calls" "$((before_ready + 2))" "$(gcount)"
# 11 of 14 now: the #520/#476/#496 residue fixtures added three non-Done items (a CLOSED-but-Ready
# issue, an item blocked by a MERGED PR, and one whose every touch-set token is unmatchable) plus a
# Done PR. `ready` is a TRUTH read — it shows what is on the board, including items the SCHEDULER
# will refuse. That distinction is the point of #520: the board column is a projection, the issue is
# the work, and /check-board is what reconciles them.
assert_eq "ready: excludes Done by default (11 of 14 items)" "11" "$(jq 'length' <<<"$ready_all")"
assert_contains "ready: keeps the Ready item"   '99'  "$(jq -c '[.[].number]' <<<"$ready_all")"
assert_contains "ready: keeps a Backlog item"   '127' "$(jq -c '[.[].number]' <<<"$ready_all")"
assert_eq "ready: drops the Done item (#54)"    "false" "$(jq 'any(.[]; .number==54)' <<<"$ready_all")"
assert_eq "ready --repo .github: only #54 exists there and it is Done -> empty" \
  "0" "$(run ready --repo .github --json 2>/dev/null | jq 'length')"
assert_eq "ready --status Done: widens past 'not Done' -> #54" \
  "54" "$(run ready --status Done --json 2>/dev/null | jq -r '.[0].number')"
assert_eq "ready --phase 'P2': substring-matches the phase" \
  "127" "$(run ready --phase P2 --json 2>/dev/null | jq -r '.[0].number')"

# Ready-before-Backlog is a cross-repo preference (Templates#99 Ready vs SDD#127 Backlog), so it can
# only be asserted over the WHOLE board. Since #480 a bare `next` scopes to the checkout, and the
# fixture runs from inside `.github` — which would scope this to `.github` and assert nothing. Run it
# from a directory that is not a checkout, where the org-wide fallback still applies. The scoping
# itself is asserted below, deliberately, instead of riding on this test's cwd by accident.
assert_contains "next (no checkout -> org-wide): picks the Ready item first" \
  "FS.GG.Templates#99" "$(run_at "$NOGIT" next 2>/dev/null)"
assert_contains "next --repo FS.GG.SDD: no Ready -> falls back to Backlog #127" \
  "FS.GG.SDD#127" "$(run next --repo FS.GG.SDD 2>/dev/null)"
assert_eq "ready --repo templates: registry short-id resolves to FS.GG.Templates (#99)" \
  "99" "$(run ready --repo templates --all --json 2>/dev/null | jq -r '.[0].number')"
# The regression FS-GG/.github#381 fixed: `game`/`audio` are the two newest rostered repos, and
# resolve_repo hard-coded only the four ORIGINAL short-ids — so `--repo audio` fell through to the
# literal token `audio`, matched no `FS.GG.Audio` board item, and returned an empty queue. #189 is
# the Ready Audio item on the default board; before the fix this assertion sees an empty list.
assert_eq "ready --repo audio: registry short-id resolves to FS.GG.Audio (#189) [#381]" \
  "189" "$(run ready --repo audio --json 2>/dev/null | jq -r '.[0].number')"
assert_contains "next --repo sdd: short-id resolves to FS.GG.SDD (Backlog #127)" \
  "FS.GG.SDD#127" "$(run next --repo sdd 2>/dev/null)"

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


# ---- NO-TOUCH-SET (#496): the rule whose absence let lint be GREEN over a DEAD queue ------------
# Every other lint rule validates the epic roll-up graph. None asked the only question a worker has:
# CAN ANYONE PICK THIS UP? A Ready/Backlog item with no `Paths:` is refused by batch/take — correctly
# — so it sits on the board looking like work and is invisible to every worker who asks for work.
# Nine had accumulated in `.github` while lint said `0 error(s)`.
#
# The NEGATIVES matter more than the positives here. A rule that fires on every touch-set-less item
# would fire on every epic, and a gate that is always red is a gate nobody reads — which is how the
# original state (always green) and the naive fix (always red) fail in exactly the same way.
nts() { jq -r "[.[] | select(.code==\"NO-TOUCH-SET\") | .id | sub(\"^[^/]+/\";\"\")] | sort | join(\",\")" <<<"$lint_json"; }
assert_eq "lint: NO-TOUCH-SET fires on EXACTLY the unschedulable items" \
  "FS.GG.SDD#420,FS.GG.SDD#421" "$(nts)"

# #420 — Ready, real work, forgot its touch-set. The whole point.
assert_contains "lint: NO-TOUCH-SET says nobody can ever pick it up" "no worker can ever pick it up" \
  "$(jq -r '.[] | select(.code=="NO-TOUCH-SET" and (.id|test("420"))) | .detail' <<<"$lint_json")"
assert_contains "lint: NO-TOUCH-SET offers the sentinel by name" "Paths: none" \
  "$(jq -r '.[] | select(.code=="NO-TOUCH-SET" and (.id|test("420"))) | .detail' <<<"$lint_json")"

# #421 — its ONLY Paths: line is FENCED. The scheduler cannot see it, so the item declares nothing.
# A checker that did not track fences would call it healthy while `take` kept refusing it — the gate
# failing open on the one state it exists to catch. Same fence rule, both sides (#277).
assert_contains "lint: a FENCED-only Paths: line is no declaration at all (fails closed)" \
  "FS.GG.SDD#421" "$(nts)"

# --- the negatives ---
# #400 is an epic carrying `Paths: none`. It is unschedulable and that is CORRECT — the sentinel is
# what makes the absence deliberate. If it fired here, every epic on the board would be an error.
case "$(nts)" in *400*) bad "lint: NO-TOUCH-SET must NOT fire on an epic that declares 'Paths: none'" "$(nts)" ;;
                 *)     ok  "lint: 'Paths: none' suppresses NO-TOUCH-SET — the sentinel is the whole point" ;; esac
case "$(nts)" in *422*) bad "lint: NO-TOUCH-SET must NOT fire on a decision item declaring 'Paths: none'" "$(nts)" ;;
                 *)     ok  "lint: a decision item declaring 'Paths: none' is clean" ;; esac
# #407 declares a real touch-set.
case "$(nts)" in *407*) bad "lint: NO-TOUCH-SET must NOT fire on an item with a real Paths: line" "$(nts)" ;;
                 *)     ok  "lint: an item with a real touch-set is clean" ;; esac
# #423 is In progress — already claimed, never a scheduling candidate. Firing here would red the board
# for every item somebody is actively working.
case "$(nts)" in *423*) bad "lint: NO-TOUCH-SET must NOT fire on an In progress item" "$(nts)" ;;
                 *)     ok  "lint: NO-TOUCH-SET is scoped to Ready/Backlog — not items in flight" ;; esac
# #424 is CLOSED. Nobody needs to pick it up.
case "$(nts)" in *424*) bad "lint: NO-TOUCH-SET must NOT fire on a CLOSED issue" "$(nts)" ;;
                 *)     ok  "lint: NO-TOUCH-SET does not fire on a closed issue" ;; esac

# EPIC-UNLINKED-CHILD: the epic's body declares a child the sub-issue graph does not contain, so
# rollup cannot see it (FS-GG/.github#325). #409 declares #414; only #413 is linked.
assert_eq "lint: EPIC-UNLINKED-CHILD names the declared-but-unlinked child" "FS.GG.SDD#414" \
  "$(run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' \
     | sed 's/.*graph, so rollup cannot see them: //')"
assert_eq "lint: EPIC-UNLINKED-CHILD fires on exactly the one epic that has one" "FS-GG/FS.GG.SDD#409" \
  "$(run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .id')"
# #415 is named in the epic's prose, not in a task-list line. A mention is not a declaration.
assert_eq "lint: a bare prose mention does not count as a declared child" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(.detail|test("415"))] | length')"
# #404's body declares #499, but its child list is TRUNCATED — "unlinked" is unknowable there, and a
# gate that guesses is the very defect this rule exists to prevent (FS-GG/.github#266).
assert_eq "lint: a truncated epic yields no unlinked-child verdict" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(.code=="EPIC-UNLINKED-CHILD" and .id=="FS-GG/FS.GG.Rendering#404")] | length')"
# #409's body ALSO cites `PR #418` on a task-list line. A PR can never be a sub-issue, so it is not
# an unlinked child — the fix re-resolves the ref, sees a pull request, and drops it (FS-GG/.github#346).
# #414 (a real issue) survives, so the finding still fires and still names ONLY #414, never #418.
assert_eq "lint: a body-declared PR ref is NOT reported as an unlinked child (#346)" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(.detail|test("#418"))] | length')"
assert_eq "lint: a genuine unlinked ISSUE still fires alongside a skipped PR ref" "FS.GG.SDD#414" \
  "$(run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' \
     | sed 's/.*graph, so rollup cannot see them: //')"
# The internal scratch field used to carry the refs for the PR probe must not leak into the schema.
assert_eq "lint: the PR-probe scratch field is not exposed in --json output" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(has("unlinked"))] | length')"
# Fail closed (FS-GG/.github#266): a ref the probe cannot resolve is KEPT, never silently dropped —
# "I could not check" is not "it is a PR". Force the #414 lookup to 502; the finding must survive.
assert_eq "lint: an unresolvable unlinked ref is kept, not dropped (fail closed, #266)" "FS.GG.SDD#414" \
  "$(GH_FAIL_ISSUE_GET=414 run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' \
     | sed 's/.*graph, so rollup cannot see them: //')"

# --repo scopes the scan. FS.GG.Templates holds only #405 — a NOTE — so lint passes, and --strict fails.
assert_eq "lint --repo templates: notes alone do not fail" "0" \
  "$(run lint --repo templates >/dev/null 2>&1; echo $?)"
assert_eq "lint --repo templates --strict: a note becomes fatal" "1" \
  "$(run lint --repo templates --strict >/dev/null 2>&1; echo $?)"
assert_eq "lint --repo rendering: only #404's finding is in scope" "EPIC-CHILDREN-TRUNCATED" \
  "$(run lint --repo rendering --json 2>/dev/null | jq -r '.[].code' | sort -u | tr '\n' ' ' | sed 's/ $//')"

# (9) epic_rollup counts a child finished only when the board says Done AND the issue is CLOSED.
#     Board-Done alone flipped an epic over a still-open child on the live board (FS-GG/.github#235).
: >"$GH_LOG"
hold="$(run done 'FS.GG.SDD#42' --pr 7 --flip 2>/dev/null)"
assert_contains "done --flip: the child itself stamps DONE"      "FSGG-DONE   FS.GG.SDD#42" "$hold"
assert_contains "rollup: HOLDS when a child is board-Done but still OPEN" \
  "1/2 children Done+closed — holding" "$hold"
# Exactly one Status write: the child. The epic must NOT be flipped.
assert_eq "rollup: holding writes Status once (the child), never the epic" "1" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# ================================================================================================
# THE DONE-STAMP'S THREE HOLES (#558, #543, #583)
# ================================================================================================
#
# (1) #558 / #543 leg 1 — THE STAMP MUST NOT GO RED ON CORRECT, MERGED, GREEN WORK.
# The recipe's own `gh pr create --fill` routes a closing keyword in the commit SUBJECT to the PR
# TITLE, where `closingIssuesReferences` never looks. The squash commit still closes the issue, so
# everything succeeds and the stamp goes red — PERMANENTLY, because the link cannot be created after
# the merge. GitHub's own CLOSED_EVENT `closer` is the record of what actually closed it.
: >"$GH_LOG"
subj_rc=0
subj="$(run done 'FS.GG.SDD#165' --flip 2>&1)" || subj_rc=$?
assert_contains "#558: a keyword in the commit SUBJECT still earns the stamp (GitHub's own closer)" \
  "FSGG-DONE   FS.GG.SDD#165" "$subj"
assert_eq "#558: ...and exits 0 — the work IS done, and a red stamp on done work is how red becomes noise" \
  "0" "$subj_rc"
# The closer can be the COMMIT rather than the PR (a squash). Same record, same verdict.
commit_closer="$(run done 'FS.GG.SDD#166' --flip 2>&1 || true)"
assert_contains "#558: a COMMIT closer resolves through to its PR" \
  "FSGG-DONE   FS.GG.SDD#166" "$commit_closer"

# (2) #543 leg 2 — `--pr` MUST NOT LAUNDER A MENTION INTO A STAMP.
# It used to select by NUMBER alone, skipping the provenance check #342 added — so the documented
# escape hatch from the bug above was a SOUNDNESS HOLE: point it at any merged PR that merely mentions
# the issue and the stamp went green. PR 97 closes #70, not #96.
mention_rc=0
mention="$(run done 'FS.GG.SDD#96' --pr 97 --flip 2>&1)" || mention_rc=$?
assert_contains "#543: --pr must REFUSE a PR that only MENTIONS the issue (#342, through the --pr hole)" \
  "FSGG-NOT-DONE   FS.GG.SDD#96" "$mention"
assert_eq "#543: ...with a non-zero exit" "1" "$mention_rc"
case "$mention" in
  *"FSGG-DONE"*) bad "#543: --pr must not be an override of PROVENANCE" "$mention" ;;
  *) ok "#543: --pr overrides WHICH PR, never WHETHER it closed the issue" ;;
esac

# (3) #583 — DONE OVER AN OPEN CHILD. The #322 failure, in the command written to prevent it.
# `epic_rollup` asks this of the item's PARENT and never of the item in hand, so a worker who follows
# pnext-item §4 (split off what you cannot land, `child`-link it) closes the parent over the split-out
# criterion — with a green ✓✓ actively saying otherwise.
: >"$GH_LOG"
kid_rc=0
kid="$(run done 'FS.GG.SDD#507' --flip 2>&1)" || kid_rc=$?
assert_contains "#583: REFUSES to stamp an item that has an OPEN sub-issue" \
  "FSGG-NOT-DONE   FS.GG.SDD#507" "$kid"
assert_contains "#583: ...and NAMES the open child, so the worker can act on it" \
  "FS.GG.SDD#585" "$kid"
assert_eq "#583: ...with a non-zero exit" "1" "$kid_rc"
# THE ONE THAT MATTERS: it must not have written Done to the board on the way to refusing.
assert_eq "#583: ...and writes NO board Status — a green stamp over unfinished work is a board that lies" \
  "0" "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# ...and a child list we could not see WHOLE makes "no open children" a claim about a set already
# known to be incomplete. An unverifiable subject must not report green (#266).
trunc_rc=0
trunc="$(run done 'FS.GG.SDD#509' --flip 2>&1)" || trunc_rc=$?
assert_contains "#583: a TRUNCATED child list refuses rather than reporting green (#266)" \
  "only 1 visible" "$trunc"
assert_eq "#583: ...with a non-zero exit" "1" "$trunc_rc"

: >"$GH_LOG"
flip="$(run done 'FS.GG.SDD#44' --pr 9 --flip 2>/dev/null)"
assert_contains "rollup: FLIPS when every child is Done AND closed" "FSGG-DONE   FS.GG.SDD#301 (epic)" "$flip"
assert_contains "rollup: the stamp says Done + closed" "all 2 children Done + closed" "$flip"
# Two Status writes: the child, then the epic it completed.
assert_eq "rollup: flipping writes Status twice (child, then epic)" "2" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"
# A body declaration is not a hint the rollup may ignore. Epic #302's graph says 1/1 children done —
# it would flip — but the body declares #47, never linked. "All children Done" is then a claim about
# a set we already know is short, so it must not report green (FS-GG/.github#325, #266's rule).
: >"$GH_LOG"
unlinked="$(run done 'FS.GG.SDD#46' --pr 11 --flip 2>/dev/null)"
assert_contains "done --flip: the child still stamps DONE"        "FSGG-DONE   FS.GG.SDD#46" "$unlinked"
assert_contains "rollup: REFUSES when the body declares an unlinked child" \
  "body declares 1 child(ren) the sub-issue graph does not contain" "$unlinked"
assert_contains "rollup: names the unlinked child"                "FS-GG/FS.GG.SDD#47" "$unlinked"
assert_contains "rollup: points at the verb that fixes it"        "fsgg-coord child" "$unlinked"
# #999 is a trailing mention on a declaration line, not a second child.
assert_eq "rollup: a trailing mention on a declaration line is not a child" "0" \
  "$(printf '%s' "$unlinked" | grep -c '999' || true)"
# The child's Status is written; the epic's is NOT. A refusal that still flipped would be the bug.
assert_eq "rollup: refusing writes Status once (the child), never the epic" "1" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# `+` and `*` are task-list bullets too. A matcher that knows only `-` reads epic #303 as declaring
# nothing and rolls it up — the gate failing OPEN on a formatting choice.
bullets="$(run done 'FS.GG.SDD#48' --pr 13 --flip 2>/dev/null)"
assert_contains "rollup: '+' and '*' task-list bullets declare children too" \
  "body declares 3 child(ren)" "$bullets"
# ...and the three are joined with ", " — `paste -sd', '` would CYCLE the delimiters ("a,b c").
# `unique` sorts the refs, so the cross-repo one leads; the point of the assertion is the separator.
assert_contains "rollup: multiple unlinked children join on ', ' (no delimiter cycling)" \
  "FS-GG/FS.GG.Rendering#51, FS-GG/FS.GG.SDD#49, FS-GG/FS.GG.SDD#50" "$bullets"
assert_eq "rollup: a '+' epic is never flipped" "0" \
  "$(printf '%s' "$bullets" | grep -c 'FS.GG.SDD#303 (epic)' || true)"

# The mirror image of #302: epic #55's graph is complete (its one child #52 is Done + closed), and
# its body's only extra ref is `PR #920` on a task-list line. A PR can never be a sub-issue, so it is
# not a missing child — the rollup re-resolves it, drops it, and FLIPS, where before it refused
# forever (FS-GG/.github#346). `mkpr` seeds the probe target as a pull request.
: >"$GH_LOG"
mkpr 920
flippr="$(run done 'FS.GG.SDD#52' --pr 19 --flip 2>/dev/null)"
assert_contains "rollup: FLIPS over a body-cited PR ref (a PR is not an unlinked child, #346)" \
  "FSGG-DONE   FS.GG.SDD#55 (epic)" "$flippr"
case "$flippr" in
  *"does not contain"*) bad "rollup: a body-cited PR must not read as an unlinked child" "$flippr" ;;
  *) ok "rollup: a body-cited PR does not block the rollup" ;;
esac
# Two Status writes: the child, then the epic the PR-cite no longer wedges.
assert_eq "rollup: flipping over a PR ref writes Status twice (child, then epic)" "2" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# (10) PR provenance (FS-GG/.github#342): with no `--pr`, `done` stamps the PR that actually CLOSED
#      the issue — the latest-merged among true closers — and refuses a mere prose mention, where the
#      old code stamped the first (lowest-numbered) merged reference of any kind.
prov84="$(run done 'FS.GG.SDD#84' 2>/dev/null || true)"
assert_contains "done: stamps the PR that CLOSED the issue, not an earlier prose mention" \
  "merged PR #92 @ 09c836e" "$prov84"
assert_eq "done: the mentioning PR #85 (which closed #74) is never stamped" "0" \
  "$(printf '%s' "$prov84" | grep -c '#85\|410843e' || true)"

refuse86="$(run done 'FS.GG.SDD#86' 2>/dev/null || true)"
assert_contains "done: REFUSES when a merged PR only mentions the issue (no closer)" \
  "no merged PR closes this issue" "$refuse86"
assert_contains "done: the refusal is a red NOT-DONE stamp" "FSGG-NOT-DONE   FS.GG.SDD#86" "$refuse86"
assert_eq "done: a board-Done issue with only a mention still fails (green needs a real closer)" "1" \
  "$(run done 'FS.GG.SDD#86' >/dev/null 2>&1; echo $?)"

prov88="$(run done 'FS.GG.SDD#88' 2>/dev/null || true)"
assert_contains "done: among two closers, the LATEST-merged wins (not the lowest-numbered)" \
  "merged PR #95 @ 2222bbb" "$prov88"
assert_eq "done: the earlier-merged, lower-numbered closer #89 is not stamped" "0" \
  "$(printf '%s' "$prov88" | grep -c '#89\|1111aaa' || true)"

# ---- `child`: the only thing that actually creates the edge rollup reads ------------------------
# The epic #302 and the child #47 the rollup above just refused to roll up over — written here
# (`seed_issue` is defined further down, with the ADR-0027 fixtures) so the fix reads next to the
# failure it repairs. `id` is NOT the number: the endpoint keys on the REST id.
for n in 302 47 49; do
  jq -n --argjson n "$n" '{id:($n + 1000), number:$n, title:"fixture", body:"", assignees:[],
    state:"open", repo:"FS-GG/FS.GG.SDD"}' >"$STORE/issue-$n.json"
  echo '[]' >"$STORE/comments-$n.json"
done
: >"$GH_LOG"
linked="$(run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' 2>/dev/null)"
assert_contains "child: links the issue as a sub-issue" "linked FS.GG.SDD#47 as a sub-issue of FS.GG.SDD#302" "$linked"
# The endpoint keys on the REST id (1047), never the issue number (47).
assert_contains "child: POSTs the child's REST id, not its number" "sub_issue_id=1047" "$(cat "$GH_LOG")"
assert_contains "child: sends it with -F, the typed form" "-F sub_issue_id=1047" "$(cat "$GH_LOG")"
# ...and that success is not vacuous: the stub really does 422 the `-f` string form, so `child`
# passing above means it used `-F`. A failure leg that cannot fire is the defect this epic is about
# (#266) — SDD#299's `/tmp` fixture is the same shape of mistake.
assert_fails "child: the stub's -f leg is real — a string sub_issue_id 422s" \
  env PATH="$STUB:$PATH" gh api "repos/FS-GG/FS.GG.SDD/issues/302/sub_issues" -f sub_issue_id=1047
# Re-linking is success, not a 422 — a worker re-running its close-out must not have to check first.
again="$(run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' 2>/dev/null)"
assert_contains "child: is idempotent" "already a sub-issue" "$again"
assert_fails "child: refuses a missing argument" run child 'FS.GG.SDD#302'

# An unreachable existing-links read must FAIL CLOSED. Swallowing it would make "I could not check"
# indistinguishable from "the edge is absent": `child` would POST, collect a 422, and blame the
# token. That is #320's defect exactly — reappearing inside the fix for its own epic.
: >"$GH_LOG"
unreachable="$(GH_FAIL_SUBISSUES_GET=302 run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' 2>&1 || true)"
assert_contains "child: an unreachable sub-issue read is refused, not guessed" \
  "refusing to guess whether" "$unreachable"
assert_eq "child: ...and it exits non-zero" "1" \
  "$(GH_FAIL_SUBISSUES_GET=302 run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' >/dev/null 2>&1; echo $?)"
assert_eq "child: ...and POSTs nothing while it cannot tell" "0" \
  "$(grep -c 'sub-issue-add' "$GH_LOG" || true)"
# A failing POST surfaces gh's own diagnosis (422 vs 403), not a guessed cause.
assert_contains "child: a failed link reports the API's error, not a guess" "422" \
  "$(GH_FORCE_SUBISSUE_POST_FAIL=1 run child 'FS.GG.SDD#302' 'FS.GG.SDD#49' 2>&1 || true)"

# budget reads both meters.
bud="$(run budget)"
assert_contains "budget: reports graphql meter" "graphql" "$bud"
assert_contains "budget: reports remaining"     "remaining" "$bud"

# ================================================================================================
# ADR-0027 — parallel intra-repo work: worker identity, the comment-order claim lock, the
# schedulable batch, the worker channel, and the touch-set drift check.
#
# The regression this whole section exists for: under ADR-0021 the lock was the issue ASSIGNEE, and
# N agents authenticating as ONE GitHub account all resolve `@me` to the same login — so a second
# worker's claim on a held item sailed through and both worked it. The lock is now keyed on a WORKER
# ID and resolved by comment-order CAS. `claim: a second worker on the SAME account is refused`
# below is the assertion that would have caught the original bug.
# ================================================================================================
echo "--- ADR-0027: parallel intra-repo work ---"

# The parallel-work board: three items In progress (held / stale / unclaimed) and five Ready.
cat >"$FIXTURES/board-pw.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":42,"title":"Audio mixer","url":"https://github.com/FS-GG/FS.GG.SDD/issues/42","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":43,"title":"Legacy port","url":"https://github.com/FS-GG/FS.GG.SDD/issues/43","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":60,"title":"Nobody claimed me","url":"https://github.com/FS-GG/FS.GG.SDD/issues/60","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":70,"title":"Scene graph","url":"https://github.com/FS-GG/FS.GG.SDD/issues/70","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":71,"title":"Mixer tweak","url":"https://github.com/FS-GG/FS.GG.SDD/issues/71","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":72,"title":"No touch-set declared","url":"https://github.com/FS-GG/FS.GG.SDD/issues/72","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":73,"title":"Scene subtree","url":"https://github.com/FS-GG/FS.GG.SDD/issues/73","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":74,"title":"ADR housekeeping","url":"https://github.com/FS-GG/FS.GG.SDD/issues/74","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4980}}
JSON

# The #440 board: ZERO Ready items, one perfectly startable Backlog item (#520), and one Backlog item
# that declares no touch-set (#521). This is the shape `.github` was actually in when `take` reported
# "no schedulable item — every candidate is blocked, claimed, overlapping, or undeclared" over a queue
# that had startable work in it.
cat >"$FIXTURES/board-bl.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Backlog"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":520,"title":"Startable, but merely Backlog","url":"https://github.com/FS-GG/FS.GG.SDD/issues/520","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Backlog"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":521,"title":"Backlog, and undeclared","url":"https://github.com/FS-GG/FS.GG.SDD/issues/521","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Done"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":522,"title":"Finished work","url":"https://github.com/FS-GG/FS.GG.SDD/issues/522","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4980}}
JSON

# ONE writer for every issue fixture. Only the store KEY varies, and that is the whole of #494:
#   <n>                    the unqualified default. Answers for exactly ONE repo — the one its own
#                          .repo names — and 404s for any other. A default, never a fallback.
#   <owner>__<repo>-<n>    repo-qualified, so the SAME number can exist in two repos as two different
#                          issues. A number-keyed store cannot represent that at all, which is why a
#                          cross-repo defect on the issue side used to be untestable.
# `id` is deliberately NOT the number: the sub-issues endpoint keys on the REST id, and a fixture
# where the two coincide would let `child` pass while POSTing the wrong field (.github#325).
seed_issue 42 "Audio mixer"          "src/Audio/**, tests/Audio/**"
seed_issue 43 "Legacy port"          "src/Legacy/**"
seed_issue 60 "Nobody claimed me"    "src/Orphan/**"
seed_issue 70 "Scene graph"          "src/Scene/**, tests/Scene/**"
seed_issue 71 "Mixer tweak"          "src/Audio/Mixer/**"
seed_issue 72 "No touch-set declared" ""
seed_issue 73 "Scene subtree"        "src/Scene/Sub/**"
seed_issue 74 "ADR housekeeping"     "docs/adr/**"
# Rendering#70 is a REAL, different issue that happens to share SDD#70's number — the shape the #479
# verify-paths tests below straddle, and which the store could not hold until #494. Its touch-set is
# deliberately IDENTICAL to SDD#70's, so the payload stays innocent and the repo can only be caught in
# the REQUEST. (#494's own tests, further down, use a divergent one to catch it in the payload too.)
seed_issue_in FS-GG/FS.GG.Rendering 70 "Scene graph (Rendering)" "src/Scene/**, tests/Scene/**"

# Pre-existing claims: #42 held by finch-a3f (fresh), #43 by ghost-000 (lease long expired),
# #60 In progress with NO marker at all — the state the FS-GG/.github incident was found in.
fresh_ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
stale_ts="$(date -u -d '-5 hours' +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -v-5H +%Y-%m-%dT%H:%M:%SZ)"
jq -n --arg ts "$fresh_ts" '[{id:801, body:"<!-- fsgg:claim worker=finch-a3f lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-42.json"
jq -n --arg ts "$stale_ts" '[{id:802, body:"<!-- fsgg:claim worker=ghost-000 lease=120 -->\ndead",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-43.json"

# Run against the parallel-work board, as a named worker (rule 1: --worker wins everything).
# GH_ISSUES_FROM_STORE: `active_claims` finds claims by MARKER, so it lists open issues per repo —
# that list has to reflect claims the tests post as they post them (see the stub's list route).
pw() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
as() { local w="$1"; shift
       PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" --worker "$w" "$@"; }
# ...the same, but STANDING somewhere (#480): a worker command's default scope is its checkout, so a
# test about `take`'s behaviour has to say where the worker is standing, not inherit the fixture's cwd.
as_at() { local d="$1" w="$2"; shift 2
       ( cd "$d" && PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" --worker "$w" "$@" ); }
# Count markers exactly as the lock parses them — ANCHORED at the body start. An unanchored grep
# would count a claim marker quoted inside a free-form message as a real lock, which is the forgery
# the anchoring exists to prevent, and would make the forgery test pass for the wrong reason.
claims_on() { jq -r '[.[] | select(.body | test("^<!--\\s*fsgg:claim\\s")) | .id] | join(",")' "$STORE/comments-$1.json"; }
workers_on() { jq -r '[.[] | select(.body | test("^<!--\\s*fsgg:claim\\s")) | (.body | capture("worker=(?<w>[^ ]+)") | .w)] | sort | join(",")' "$STORE/comments-$1.json"; }

# ---- worker identity ---------------------------------------------------------------------------
assert_eq "whoami: --worker wins everything" "heron-b71" \
  "$(as heron-b71 whoami | awk '/^worker/{print $2}')"
assert_eq "whoami: reports which rule derived the id" "--worker flag" \
  "$(as heron-b71 whoami | sed -n 's/^source  //p')"
assert_eq "whoami: \$FSGG_WORKER is honoured" "wren-c22" \
  "$(PATH="$STUB:$PATH" FSGG_WORKER=wren-c22 bash "$COORD" whoami | awk '/^worker/{print $2}')"

# ---- harness session ids (rule 4) ---------------------------------------------------------------
# The CI runner exports no harness vars and the developer's shell may export several, so every case
# below states its own environment explicitly. `env -u` strips whatever the outer harness set.
#
# It also runs from "$WORK", which is NOT a git checkout. Identity rule 3 (the worktree name) OUTRANKS
# the session id, so a developer running this fixture from inside a linked worktree — which ADR-0021
# tells them to work in — would otherwise see every rule-4/5 assertion below fail on correct code.
# CI only passed because it happens to check out the primary worktree. The rule under test is
# "no harness, no worktree", so the fixture must supply "no worktree" rather than inherit it.
hless() { PATH="$STUB:$PATH" env -u CLAUDE_CODE_SESSION_ID -u OPENCODE_SESSION_ID \
            -u FSGG_AGENT_SESSION_ID -u FSGG_WORKER "$@" \
            bash -c 'cd "$1" && exec bash "$2" whoami' _ "$WORK" "$COORD" 2>&1; }

# A session id is NOT an identity where the harness shares it across subagents. Claude Code does
# (anthropics/claude-code#7881) — so it names the worker only as a fallback, and it WARNS.
cc="$(hless CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064)"
assert_contains "whoami: a claude-code session id names the worker (rule 4)" \
  "source  claude-code session id (309bd638-8a1c-42b7-952b-898efb8d1064)" "$cc"
assert_contains "whoami: ...and reports the harness"          "harness claude-code" "$cc"
assert_contains "whoami: ...flagging that subagents share it" "(shared by subagents)" "$cc"
assert_contains "whoami: ...and WARNS, because a shared id cannot lock" \
  "every subagent of this claude-code session shares this session id" "$cc"

# Deterministic: the same session must always name the same worker (no persistence, no drift).
cc2="$(hless CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064)"
assert_eq "whoami: the session-derived name is deterministic" \
  "$(awk '/^worker/{print $2}' <<<"$cc")" "$(awk '/^worker/{print $2}' <<<"$cc2")"
assert_eq "whoami: a DIFFERENT session gets a different name" "false" \
  "$([ "$(hless CLAUDE_CODE_SESSION_ID=aaaa | awk '/^worker/{print $2}')" \
     = "$(hless CLAUDE_CODE_SESSION_ID=bbbb | awk '/^worker/{print $2}')" ] && echo true || echo false)"
assert_contains "whoami: the derived name is memorable, not a UUID" "-" \
  "$(hless CLAUDE_CODE_SESSION_ID=aaaa | awk '/^worker/{print $2}')"

# OpenCode spawns subagents as CHILD sessions with their own ids, so there its session id IS
# per-worker — and must not warn. The cardinality is a property of the harness, not of the name.
oc="$(hless OPENCODE_SESSION_ID=ses_abc123)"
assert_contains "whoami: an opencode session id is per-worker"  "(per-worker)" "$oc"
assert_eq "whoami: ...so it does NOT warn" "0" "$(grep -c 'WARNING' <<<"$oc" || true)"

# An unknown harness may declare itself; absent that, we assume its session is shared (fail safe).
unk="$(hless FSGG_AGENT_SESSION_ID=zz-99)"
assert_contains "whoami: an unknown harness's session id still works" "session zz-99" "$unk"
assert_contains "whoami: ...but is assumed shared until proven otherwise" "shared by subagents" "$unk"

# Rule 5, the last resort: no harness, no worktree -> a per-checkout name, and a different warning.
none="$(hless)"
assert_contains "whoami: with no harness, falls back to a per-checkout name" \
  "generated, persisted per-checkout" "$none"
assert_contains "whoami: ...and says why THAT id may not be unique" \
  "every worker sharing this checkout gets this same id" "$none"

# Precedence: an explicit id always beats a session id, and never warns.
expl="$(PATH="$STUB:$PATH" CLAUDE_CODE_SESSION_ID=309bd638 FSGG_WORKER=wren-c22 bash "$COORD" whoami 2>&1)"
assert_contains "whoami: \$FSGG_WORKER beats a harness session id" "source  \$FSGG_WORKER" "$expl"
assert_eq "whoami: an explicit id never warns" "0" "$(grep -c 'WARNING' <<<"$expl" || true)"
assert_contains "whoami: ...but the session is still recorded as provenance" "harness claude-code" "$expl"

# ---- the lock: THE regression. Same account, different worker -> refused. -----------------------
: >"$GH_LOG"
if as heron-b71 claim 'FS.GG.SDD#42' >/dev/null 2>&1; then
  bad "claim: a second worker on the SAME account is refused (ADR-0021 regression)" \
      "heron-b71 claimed an item already held by finch-a3f"
else
  ok "claim: a second worker on the SAME account is refused (ADR-0021 regression)"
fi
assert_contains "claim: refusal names the holding WORKER, not the login" "held by worker 'finch-a3f'" \
  "$(as heron-b71 claim 'FS.GG.SDD#42' 2>&1 || true)"
assert_eq "claim: a refused claim leaves exactly one marker" "finch-a3f" "$(workers_on 42)"

# The holder re-claiming is a heartbeat, not a second marker (so `take` retries stay idempotent).
assert_contains "claim: the holder re-claiming renews its lease" "lease renewed" \
  "$(as finch-a3f claim 'FS.GG.SDD#42' 2>/dev/null)"
assert_eq "claim: re-claiming does not add a second marker" "801" "$(claims_on 42)"

# An uncontended claim on a free item: marker posted, assignee set, Status flipped, branch printed.
: >"$GH_LOG"
out70="$(as heron-b71 claim 'FS.GG.SDD#70' 2>/dev/null)"
assert_contains "claim: uncontended claim succeeds"       "claimed FS.GG.SDD#70 by worker heron-b71" "$out70"
assert_contains "claim: prints the isolation worktree"    "git worktree add ../FS.GG.SDD-70 -b item/70-scene-graph" "$out70"
# .github#319: the base ref is load-bearing, and the assertion above cannot see it — that needle is a
# PREFIX of the base-less command, so it passed happily for as long as the bug existed. `git worktree
# add -b <new>` with no commit-ish branches from the shared checkout's HEAD, routinely another worker's
# unmerged branch since N workers pass through that checkout, and the item's PR then carries that
# branch's commits too. The base ref therefore gets its own assertion: check what FOLLOWS the branch
# name, not that the branch name appears.
isolate70="$(printf '%s\n' "$out70" | grep 'git worktree add' || true)"
base70="${isolate70##* }"   # the trailing commit-ish; on the base-less command this is the branch name
if [ "$base70" = "origin/main" ]; then
  ok "claim: the isolation worktree names an explicit base ref"
else
  bad "claim: the isolation worktree names an explicit base ref" \
    "expected a commit-ish after '-b <branch>', got trailing token '$base70' — that branches off the shared checkout's HEAD: $isolate70"
fi
assert_contains "claim: prints the attribution trailer"   'FSGG-Worker: heron-b71' "$out70"
assert_contains "claim: flips the board to In progress"   "board: In progress" "$out70"
# The assignee is set over REST, not `gh issue edit` (#418): same courtesy to human readers, 4 fewer
# GraphQL points per claim (and 4 more per release) — on a budget every worker shares. A regression to
# `gh issue edit` would be invisible except here.
assert_contains "claim: assigns @me for the humans — over REST" \
  "assignee-post FS-GG/FS.GG.SDD 70" "$(cat "$GH_LOG")"
assert_eq "claim: does NOT spend GraphQL on the assignee (no 'gh issue edit')" \
  "0" "$(grep -c '^issue-edit' "$GH_LOG" || true)"
assert_eq "claim: exactly one marker on the item" "heron-b71" "$(workers_on 70)"

# PROVENANCE. The marker records WHICH agent transcript claimed the item — the question #255 could
# only answer with mtimes and `ps`. `worker=` must stay the first key, or parse_claims stops matching.
: >"$STORE/comments-71.json"; echo '[]' >"$STORE/comments-71.json"
PATH="$STUB:$PATH" GH_BOARD_SET=pw CLAUDE_CODE_SESSION_ID=sess-1234 \
  bash "$COORD" --worker heron-b71 claim 'FS.GG.SDD#71' >/dev/null 2>&1
marker71="$(jq -r '.[] | select(.body | test("^<!--\\s*fsgg:claim")) | .body' "$STORE/comments-71.json")"
assert_contains "claim: the marker records the harness"  "harness=claude-code" "$marker71"
assert_contains "claim: the marker records the session"  "session=sess-1234"   "$marker71"
assert_contains "claim: the human line names the agent"  "session \`sess-1234\`" "$marker71"
assert_eq "claim: provenance keys do NOT break the worker= capture the lock parses" "heron-b71" \
  "$(workers_on 71)"
assert_contains "claim: worker= is still the FIRST key after the marker" \
  "fsgg:claim worker=heron-b71 lease=120 harness=claude-code" "$marker71"
# ...and a marker carrying provenance is still a lock: a second worker is refused.
assert_fails "claim: a provenance-carrying marker still excludes another worker" \
  as wren-c22 claim 'FS.GG.SDD#71'
# Leave #71 free for the scheduler assertions further down.
PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" --worker heron-b71 release 'FS.GG.SDD#71' >/dev/null 2>&1

# ---- the CAS: a rival marker lands with a LOWER id between our post and our re-read -------------
# The loser must delete its own marker and exit non-zero, leaving the winner's marker alone.
: >"$GH_LOG"
race_out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_RACE_INJECT=rook-d19 GH_RACE_ISSUE=74 \
  bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#74' 2>&1 || true)"
assert_contains "claim CAS: the loser knows it lost, and to whom" "lost the claim race on FS.GG.SDD#74 to worker 'rook-d19'" "$race_out"
assert_contains "claim CAS: the loser backs off cleanly"          "backed off cleanly" "$race_out"
assert_eq "claim CAS: exactly ONE marker survives the race"       "rook-d19" "$(workers_on 74)"
assert_contains "claim CAS: the loser deleted its OWN marker"     "comment-delete" "$(cat "$GH_LOG")"
# The race winner really holds #74 — hand it back, or every scheduler assertion below inherits it.
as rook-d19 release 'FS.GG.SDD#74' >/dev/null 2>&1
assert_eq "claim CAS: the winner's claim is a real lock it must release" "" "$(workers_on 74)"

# ---- heartbeat / release -----------------------------------------------------------------------
: >"$GH_LOG"
assert_contains "heartbeat: renews the holder's lease" "renewed FS.GG.SDD#70" "$(as heron-b71 heartbeat 'FS.GG.SDD#70' 2>/dev/null)"
assert_contains "heartbeat: patches the marker in place" "comment-patch" "$(cat "$GH_LOG")"
assert_fails "heartbeat: a non-holder cannot renew" as wren-c22 heartbeat 'FS.GG.SDD#70'

assert_fails "release: a non-holder cannot release another worker's claim" as wren-c22 release 'FS.GG.SDD#70'
assert_eq "release: the refused release left the marker intact" "heron-b71" "$(workers_on 70)"
assert_contains "release: --force releases another worker's claim" "released FS.GG.SDD#70" \
  "$(as wren-c22 release 'FS.GG.SDD#70' --force 2>/dev/null)"
assert_eq "release: the marker is gone" "" "$(workers_on 70)"

# ---- release: "drop the lease" is not "this item is startable" (#331) ---------------------------
# `release` used to force Status=Ready unconditionally, so the documented blocked-item sequence
# (`set-field Status Blocked` then `release`) had its Blocked silently reverted on the very next
# line — leaving a board row whose Status contradicted its own `Blocked by`. It must now reset the
# `In progress` that `claim` set, and ONLY that.
#
# Every case below clears GH_LOG *after* the claim: `claim` writes Status=In progress itself, so a
# log still holding that write would let a "release wrote no Status" assertion pass for free.
for n in 340 341 342 343 344 346 347; do seed_issue "$n" "release case $n" "src/Rel$n/**"; done
edits_to_status() { grep -c 'PVTSSF_status' "$GH_LOG" 2>/dev/null || true; }
# rel <VAR=value> <args...> — `as pika-r01`, plus one stub knob for the Status read.
#
# These scenarios walk ONE worker through item after item to exercise release/`Paths:` semantics, and
# a few deliberately do not release in between — so they claim with `--force`, which is the sanctioned
# way to say "yes, I mean to hold more than one" (#516). The rule itself — at most one item per worker,
# because a claim RESERVES A TOUCH-SET and a second unattended one locks files nobody is editing — is
# asserted in its own section below, with its own failure leg. `--force` on an unheld item steals
# nothing; it only opts out of the #516 guard.
rel() { local e="$1"; shift
        PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
          env "$e" bash "$COORD" --worker pika-r01 "$@"; }

# (a) the ordinary path is unchanged: the lease drops and the item returns to the pool.
as pika-r01 claim 'FS.GG.SDD#340' --force >/dev/null 2>&1
printf 'In progress' >"$STORE/itemstatus-340"; : >"$GH_LOG"
assert_contains "release: resets the 'In progress' that claim set" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#340' 2>/dev/null)"
assert_contains "release: ...with a real board write" "opt_ready" "$(cat "$GH_LOG")"
assert_eq "release: ...and the marker is gone" "" "$(workers_on 340)"

# (b) THE REGRESSION. A deliberately-set Blocked survives its own release.
as pika-r01 claim 'FS.GG.SDD#341' --force >/dev/null 2>&1
printf 'Blocked' >"$STORE/itemstatus-341"; : >"$GH_LOG"
rel341="$(as pika-r01 release 'FS.GG.SDD#341' 2>/dev/null)"
assert_contains "release: PRESERVES a deliberately-set Blocked" "board: Blocked (preserved" "$rel341"
assert_eq "release: ...writing no Status at all, rather than a matching one" "0" "$(edits_to_status)"
assert_eq "release: ...and the lease is still dropped" "" "$(workers_on 341)"

# (c) `--status S` is the caller stating the end state, and still wins over the preserve rule.
as pika-r01 claim 'FS.GG.SDD#342' --force >/dev/null 2>&1
printf 'Blocked' >"$STORE/itemstatus-342"; : >"$GH_LOG"
assert_contains "release --status: an explicit end state overrides preserve" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#342' --status Ready 2>/dev/null)"
assert_contains "release --status: ...and is actually written" "opt_ready" "$(cat "$GH_LOG")"

# (d) preserve is not Blocked-specific: release never downgrades a terminal column either.
as pika-r01 claim 'FS.GG.SDD#343' --force >/dev/null 2>&1
printf 'Done' >"$STORE/itemstatus-343"; : >"$GH_LOG"
assert_contains "release: never downgrades a Done item to Ready" "board: Done (preserved" \
  "$(as pika-r01 release 'FS.GG.SDD#343' 2>/dev/null)"
assert_eq "release: ...Done is left untouched" "0" "$(edits_to_status)"

# (e) A Status we could not READ is not a Status we may overwrite — forcing Ready over an unknown
#     column is the same fail-open, one turn in. The lease still drops: the marker is the lock.
as pika-r01 claim 'FS.GG.SDD#344' --force >/dev/null 2>&1
: >"$GH_LOG"
rel344="$(rel GH_FAIL_ITEM_STATUS=344 release 'FS.GG.SDD#344' 2>&1)"
assert_contains "release: an unreadable Status is not overwritten" "Status unchanged" "$rel344"
assert_contains "release: ...and says so, naming the repair" "set-field" "$rel344"
assert_eq "release: ...no Status write on an unreadable column" "0" "$(edits_to_status)"
assert_eq "release: ...but the lease IS dropped" "" "$(workers_on 344)"

# (f) On the board, but no Status set: nothing was chosen, so there is nothing to preserve.
as pika-r01 claim 'FS.GG.SDD#346' --force >/dev/null 2>&1
: >"$STORE/itemstatus-346"; : >"$GH_LOG"
assert_contains "release: an item with no Status set returns to the pool" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#346' 2>/dev/null)"

# (g) An off-board item has no column to reset, and release must not pretend it did.
as pika-r01 claim 'FS.GG.SDD#347' --force >/dev/null 2>&1
: >"$GH_LOG"
rel347="$(rel GH_OFFBOARD_ITEM=347 release 'FS.GG.SDD#347' 2>/dev/null)"
assert_contains "release: an off-board item reports no board, not a phantom write" "not on board" "$rel347"
assert_eq "release: ...and writes no Status" "0" "$(edits_to_status)"
assert_eq "release: ...and the lock is released, board or no board" "" "$(workers_on 347)"

# ---- who: what is actually running, without spelunking through worktrees ------------------------
who="$(pw who --repo sdd 2>/dev/null)"
assert_contains "who: names the worker holding each item" "finch-a3f" "$who"
assert_contains "who: flags a claim past its lease as STALE" "STALE" "$who"
assert_contains "who: flags In-progress work with NO marker as UNCLAIMED" "UNCLAIMED" "$who"
whoerr="$(pw who --repo sdd 2>&1 >/dev/null)"
assert_contains "who: warns that someone is working outside the protocol" \
  "In progress with NO claim marker" "$whoerr"
assert_eq "who --json: the unclaimed item has a null worker" "null" \
  "$(pw who --repo sdd --json 2>/dev/null | jq -r '.[] | select(.number==60) | .worker')"

# ---- reap: collect a dead worker's claim -------------------------------------------------------
assert_contains "reap: dry-run reports, does not release" "would reap  FS.GG.SDD#43  worker ghost-000" \
  "$(pw reap --repo sdd 2>/dev/null)"
assert_eq "reap: dry-run left the marker in place" "ghost-000" "$(workers_on 43)"
: >"$GH_LOG"
reaped="$(as wren-c22 reap --repo sdd --apply 2>/dev/null)"
assert_contains "reap --apply: releases the expired claim" "reaped  FS.GG.SDD#43  worker ghost-000" "$reaped"
assert_contains "reap --apply: returns the item to the pool" "board: Ready" "$reaped"
assert_eq "reap --apply: the stale marker is gone" "" "$(workers_on 43)"
assert_eq "reap --apply: it TELLS the reaped worker (a message, not a silent steal)" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=ghost-000"))] | length' "$STORE/comments-43.json")"

# `reap` is the OTHER way a claim goes away, so it owes the board the same answer as `release` and
# used to force Ready just as hard (#331). A worker whose lease expires on an item it had marked
# Blocked must not have that column reset on its way out — the reaper is collecting a LEASE, and it
# knows nothing about whether the item became startable. Seeded after the block above so the stale
# marker cannot perturb the reap tests that precede it.
seed_issue 348 "reaped while blocked" "src/Rel348/**"
jq -n --arg ts "$stale_ts" '[{id:848, body:"<!-- fsgg:claim worker=ghost-348 lease=120 -->\ndead",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-348.json"
printf 'Blocked' >"$STORE/itemstatus-348"; : >"$GH_LOG"
reaped348="$(as wren-c22 reap --repo sdd --apply 2>/dev/null)"
assert_contains "reap --apply: collects the expired claim" "reaped  FS.GG.SDD#348" "$reaped348"
assert_contains "reap --apply: PRESERVES a deliberately-set Blocked" "board: Blocked (preserved)" "$reaped348"
assert_eq "reap --apply: ...writing no Status at all" "0" "$(edits_to_status)"
assert_eq "reap --apply: ...but the lease IS collected" "" "$(workers_on 348)"

# ---- #481: undoing a claim RESTORES the column it overwrote; it does not guess `Ready` -----------
# #331 taught release/reap to leave a deliberately-chosen column alone, and to reset only the
# `In progress` a claim had written. Resetting it to `Ready` was a faithful undo — while `claim` was
# reachable ONLY from `Ready`. #440 gave `take` a Ready-else-Backlog fallback, and from that commit on
# every undo path (`release`, `take`'s lost-race retry, `reap`) PROMOTED a Backlog item it touched:
# a slow, invisible drain of the untriaged column into the one humans read as scheduled work — made
# self-reinforcing by the very fallback that caused it.
#
# The pre-claim column is knowable at exactly one instant — before the claim overwrites it — so the
# claim records it in its own marker, which is created, inherited, stolen and destroyed with the
# lease. Everything below is about that record surviving long enough to be used.
bodies_on() { jq -r '.[].body' "$STORE/comments-$1.json"; }
mark_stale() {  # mark_stale <num> <marker-id> <worker> [prev-key]
  jq -n --arg ts "$stale_ts" --argjson id "$2" --arg b "<!-- fsgg:claim worker=$3 lease=120${4:+ prev=$4} -->
dead" '[{id:$id, body:$b, user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' \
    >"$STORE/comments-$1.json"
}
for n in 350 351 352 353 354 355 356 357 358; do seed_issue "$n" "restore case $n" "src/Rst$n/**"; done

# (a) THE DEFECT. `take` claims a Backlog item; `release` must not hand it back as Ready.
printf 'Backlog' >"$STORE/itemstatus-350"
as pika-r01 claim 'FS.GG.SDD#350' --force >/dev/null 2>&1
assert_contains "#481: the claim RECORDS the column it overwrote, in its own marker" \
  "prev=Backlog" "$(bodies_on 350)"
printf 'In progress' >"$STORE/itemstatus-350"; : >"$GH_LOG"   # ...which is what the claim wrote
rel350="$(as pika-r01 release 'FS.GG.SDD#350' 2>/dev/null)"
assert_contains "#481: release RESTORES Backlog instead of promoting it to Ready" "board: Backlog" "$rel350"
assert_contains "#481: ...and says it restored a column, rather than reset one" "restored" "$rel350"
assert_contains "#481: ...with a real board write, not just a claim about one" "opt_backlog" "$(cat "$GH_LOG")"
assert_eq "#481: ...and the lease is still dropped" "" "$(workers_on 350)"

# (b) Marker keys are SPACE-separated; Status names are not. `In review` must survive the round trip
#     — an encoding that loses at the space would silently truncate the restore target to `In`.
printf 'In review' >"$STORE/itemstatus-351"
as pika-r01 claim 'FS.GG.SDD#351' --force >/dev/null 2>&1
assert_contains "#481: a Status with a space is percent-encoded into the marker" \
  "prev=In%20review" "$(bodies_on 351)"
printf 'In progress' >"$STORE/itemstatus-351"; : >"$GH_LOG"
assert_contains "#481: ...and decodes back to the real column name on release" "board: In review" \
  "$(as pika-r01 release 'FS.GG.SDD#351' 2>/dev/null)"
assert_contains "#481: ...resolving the real option id, so the write is not a no-op" "opt_review" "$(cat "$GH_LOG")"

# (c) A heartbeat REWRITES the whole marker body. Anything it does not carry forward is destroyed —
#     so a claim that beats for two hours would forget the column long before it released it.
printf 'Backlog' >"$STORE/itemstatus-352"
as pika-r01 claim 'FS.GG.SDD#352' --force >/dev/null 2>&1
printf 'In progress' >"$STORE/itemstatus-352"
as pika-r01 heartbeat 'FS.GG.SDD#352' >/dev/null 2>&1
assert_contains "#481: a heartbeat rewrites the marker and CARRIES the recorded column" \
  "prev=Backlog" "$(bodies_on 352)"
: >"$GH_LOG"
assert_contains "#481: ...so a long-running claim still restores Backlog" "board: Backlog" \
  "$(as pika-r01 release 'FS.GG.SDD#352' 2>/dev/null)"

# (d) `reap` is the other way a claim goes away, so it owes the board the same answer — and the dead
#     worker's marker is the only thing that still knows what its claim overwrote.
mark_stale 353 853 ghost-353 Backlog
printf 'In progress' >"$STORE/itemstatus-353"; : >"$GH_LOG"
reaped353="$(as wren-c22 reap --repo sdd --apply 2>/dev/null)"
assert_contains "#481: reap RESTORES the column the dead claim overwrote" "board: Backlog" "$reaped353"
assert_eq "#481: ...and still collects the lease" "" "$(workers_on 353)"

# (e) THE SUBTLE ONE. Re-claiming over an expired lease: `claim` GARBAGE-COLLECTS the dead marker
#     before running the CAS. Read the board after that and it says `In progress` — the DEAD claim's
#     footprint — so the new claim would conclude nothing was overwritten and record nothing, and the
#     Backlog would be lost at the reap-and-reclaim rather than at the release. The record has to be
#     inherited from the marker being collected, which means reading it BEFORE the GC deletes it.
mark_stale 354 854 ghost-354 Backlog
printf 'In progress' >"$STORE/itemstatus-354"
as pika-r01 claim 'FS.GG.SDD#354' --force >/dev/null 2>&1
assert_contains "#481: a re-claim over a dead lease INHERITS the column that claim overwrote" \
  "prev=Backlog" "$(bodies_on 354)"
: >"$GH_LOG"
assert_contains "#481: ...so Backlog survives a reap-and-reclaim, not merely a clean release" \
  "board: Backlog" "$(as pika-r01 release 'FS.GG.SDD#354' 2>/dev/null)"

# (f) A --force steal evicts the holder and claims over the same already-overwritten column. Same
#     inheritance, or the thief releases the item into the wrong queue.
printf 'Backlog' >"$STORE/itemstatus-355"
as pika-r01 claim 'FS.GG.SDD#355' --force >/dev/null 2>&1
printf 'In progress' >"$STORE/itemstatus-355"
as wren-c22 claim 'FS.GG.SDD#355' --force >/dev/null 2>&1
assert_contains "#481: a --force steal inherits the column the EVICTED claim overwrote" \
  "prev=Backlog" "$(bodies_on 355)"
: >"$GH_LOG"
assert_contains "#481: ...so the thief's release restores Backlog too" "board: Backlog" \
  "$(as wren-c22 release 'FS.GG.SDD#355' 2>/dev/null)"

# (g) BACKWARD COMPATIBILITY. Markers minted before this change carry no record, and there are live
#     ones on the board right now. They must keep releasing to `Ready` — the old behaviour, now
#     scoped to the one case where there is genuinely nothing to restore.
mark_stale 356 856 pika-r01           # a marker with no prev= key at all
printf 'In progress' >"$STORE/itemstatus-356"; : >"$GH_LOG"
assert_contains "#481: a marker minted BEFORE this change still falls back to Ready" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#356' 2>/dev/null)"

# (h) A claim that somehow recorded `In progress` recorded its own footprint, not a column anybody
#     chose. Restoring it would leave the item looking claimed with no claim on it.
mark_stale 357 857 pika-r01 'In%20progress'
printf 'In progress' >"$STORE/itemstatus-357"; : >"$GH_LOG"
assert_contains "#481: a recorded 'In progress' is a footprint, not a column — not restored" \
  "board: Ready" "$(as pika-r01 release 'FS.GG.SDD#357' 2>/dev/null)"

# (h2) An UNREADABLE column writes nothing (#331) — but we still know what the claim overwrote, and
#      the repair we hand the operator must say THAT. Telling them to set `Ready` over a Backlog item
#      would reintroduce the promotion in the advice, on the one path that can still prove it wrong.
seed_issue 361 "unreadable, but the marker remembers" "src/Rst361/**"
printf 'Backlog' >"$STORE/itemstatus-361"
as pika-r01 claim 'FS.GG.SDD#361' --force >/dev/null 2>&1
: >"$GH_LOG"
rel361="$(rel GH_FAIL_ITEM_STATUS=361 release 'FS.GG.SDD#361' 2>&1)"
assert_contains "#481: an unreadable column still writes nothing (#331 holds)" "Status unchanged" "$rel361"
assert_contains "#481: ...but the repair names the column the claim OVERWROTE, not 'Ready'" \
  "set-field FS.GG.SDD#361 Status Backlog" "$rel361"
assert_eq "#481: ...and no Status is written on an unreadable column" "0" "$(edits_to_status)"

# (i) #331 SURVIVES: a column set deliberately DURING the lease still beats the recorded one. The
#     record answers "what did the claim overwrite", never "what should this item be now".
printf 'Backlog' >"$STORE/itemstatus-358"
as pika-r01 claim 'FS.GG.SDD#358' --force >/dev/null 2>&1
printf 'Blocked' >"$STORE/itemstatus-358"; : >"$GH_LOG"
rel358="$(as pika-r01 release 'FS.GG.SDD#358' 2>/dev/null)"
assert_contains "#481: a column set DURING the lease still wins over the recorded one (#331 holds)" \
  "board: Blocked (preserved" "$rel358"
assert_eq "#481: ...writing no Status at all, rather than a matching one" "0" "$(edits_to_status)"

# (j) WHAT IT COSTS. Reading the pre-claim column is a GraphQL read, and `claim` is the hottest path
#     on the scarcest resource in this org: every worker runs `take` -> `claim` every round, against
#     ONE 5,000 pt/hr budget shared by the whole ACCOUNT — which this repo has already exhausted to
#     the point of taking the board down (#418). So the cost is pinned, not asserted: #481 buys the
#     restore with exactly ONE narrow item read (the `set_field` write is the other call). The failure
#     this guards is a later change reaching for a board SCAN here — seven points, times N workers,
#     times every round — which would be invisible until the budget was gone.
seed_issue 359 "what a claim costs" "src/Rst359/**"
printf 'Backlog' >"$STORE/itemstatus-359"; : >"$GH_GRAPHQL_COUNT"
as pika-r01 claim 'FS.GG.SDD#359' --force >/dev/null 2>&1
assert_eq "#418: a claim spends 2 GraphQL reads — the item lookup, plus #481's pre-claim column" \
  "2" "$(gcount)"
# ...and RELEASE is unchanged. It still makes the ONE read #331 needs (is this column one somebody
# chose during the lease?), and the restore target rides in on the marker for free — so restoring a
# column costs strictly less than re-deriving it would have.
printf 'In progress' >"$STORE/itemstatus-359"; : >"$GH_GRAPHQL_COUNT"
as pika-r01 release 'FS.GG.SDD#359' >/dev/null 2>&1
assert_eq "#418: a release still spends exactly 1 — the marker carries the column, adding NO read" \
  "1" "$(gcount)"

# ---- overlap --active: a candidate against everything in flight --------------------------------
assert_contains "overlap --active: disjoint candidate clears every live claim" "DISJOINT" \
  "$(pw overlap 'FS.GG.SDD#73' --active 2>/dev/null || true)"
ov="$(pw overlap 'FS.GG.SDD#71' --active 2>&1 || true)"
assert_contains "overlap --active: names the colliding item AND its worker" "OVERLAP — FS.GG.SDD#71 ⇄ FS.GG.SDD#42 (worker finch-a3f" "$ov"
assert_contains "overlap --active: shows the conflicting subtrees" "src/Audio/Mixer  ⇄  src/Audio" "$ov"
assert_fails "overlap --active: exits non-zero on a collision" pw overlap 'FS.GG.SDD#71' --active

# ---- batch: the scheduler ----------------------------------------------------------------------
# #42 (in flight) owns src/Audio, so #71 is unschedulable. #72 declares nothing. #73 overlaps #70,
# which was chosen first. That leaves #70 and #74 — a disjoint pair, safe to hand to two workers.
batch_json="$(pw batch --repo sdd --json 2>/dev/null)"
assert_eq "batch: picks a maximal DISJOINT set" '["FS.GG.SDD#70","FS.GG.SDD#74"]' "$(jq -c '.' <<<"$batch_json")"
batch_err="$(pw batch --repo sdd 2>&1 >/dev/null)"
assert_contains "batch: says why it skipped in-flight overlap"   "#71 — overlaps in-flight work" "$batch_err"
assert_contains "batch: says why it skipped an undeclared item"  "#72 — no 'Paths:' declared" "$batch_err"
assert_contains "batch: says why it skipped a batch-mate clash"  "#73 — overlaps batch member FS.GG.SDD#70" "$batch_err"
assert_eq "batch -n 1: honours the requested width" '["FS.GG.SDD#70"]' \
  "$(pw batch --repo sdd -n 1 --json 2>/dev/null | jq -c '.')"

# ---- take: pick + claim in one step -------------------------------------------------------------
took="$(as smew-f31 take --repo sdd 2>/dev/null)"
assert_contains "take: claims the first schedulable item" "claimed FS.GG.SDD#70 by worker smew-f31" "$took"
assert_eq "take: the claim marker is really there" "smew-f31" "$(workers_on 70)"
# With #70 now held by smew, a second worker takes the next disjoint item rather than idling.
took2="$(as brant-g07 take --repo sdd 2>/dev/null)"
assert_contains "take: a second worker gets a DIFFERENT, disjoint item" "claimed FS.GG.SDD#74 by worker brant-g07" "$took2"

# ---- say / inbox: the channel -------------------------------------------------------------------
as finch-a3f say 'FS.GG.SDD#42' --to smew-f31 'I own src/Audio until Friday.' >/dev/null 2>&1
as finch-a3f say 'FS.GG.SDD#42' 'Broadcast to whoever is here.' >/dev/null 2>&1
inbox="$(as smew-f31 inbox --repo sdd 2>/dev/null)"
assert_contains "inbox: delivers a message addressed to this worker" "I own src/Audio until Friday." "$inbox"
assert_contains "inbox: delivers a broadcast (to=*)"                 "Broadcast to whoever is here." "$inbox"
assert_contains "inbox: says which item the message rode in on"      "FS.GG.SDD#42" "$inbox"
assert_eq "inbox: the cursor advanced -> nothing new on a second read" "no new messages for worker smew-f31." \
  "$(as smew-f31 inbox --repo sdd 2>/dev/null)"
assert_eq "inbox: a worker does not see its OWN messages" "no new messages for worker finch-a3f." \
  "$(as finch-a3f inbox --repo sdd 2>/dev/null)"
as finch-a3f say 'FS.GG.SDD#42' --to smew-f31 'One more.' >/dev/null 2>&1
assert_contains "inbox --peek: shows new mail" "One more." "$(as smew-f31 inbox --repo sdd --peek 2>/dev/null)"
assert_contains "inbox --peek: does NOT advance the cursor" "One more." "$(as smew-f31 inbox --repo sdd 2>/dev/null)"

# ---- widen: re-declare mid-flight, and TELL whoever it now collides with ------------------------
widen="$(as brant-g07 widen 'FS.GG.SDD#74' --paths 'docs/adr/**, src/Audio/**' 2>&1 || true)"
assert_contains "widen: rewrites the declared touch-set"          "widened FS.GG.SDD#74 → Paths: docs/adr/**, src/Audio/**" "$widen"
assert_contains "widen: re-checks against in-flight claims"        "now collides with FS.GG.SDD#42 (worker finch-a3f)" "$widen"
assert_contains "widen: notifies the worker it collided with"      "notified worker finch-a3f on FS.GG.SDD#42" "$widen"
assert_eq "widen: the notification is a real message on THEIR item" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=finch-a3f"))] | length' "$STORE/comments-42.json")"
assert_contains "widen: the new touch-set persisted to the issue body" "Paths: docs/adr/**, src/Audio/**" \
  "$(jq -r '.body' "$STORE/issue-74.json")"
assert_eq "widen: it replaced the Paths line, it did not append a second one" "1" \
  "$(jq -r '.body' "$STORE/issue-74.json" | grep -c '^Paths:')"
# On a DIFFERENT item, so the assertions above are not perturbed by a second notification.
assert_fails "widen: a collision exits non-zero" as brant-g07 widen 'FS.GG.SDD#73' --paths 'src/Audio/**'

# ---- verify-paths: did the PR stay inside the declared touch-set? ------------------------------
cat >"$FIXTURES/pr-7.json" <<'JSON'
{"head":{"ref":"item/70-scene-graph"},"number":7}
JSON
cat >"$FIXTURES/pr-files-7.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"},{"filename":"tests/Scene/GraphTests.fs"}]
JSON
cat >"$FIXTURES/pr-8.json" <<'JSON'
{"head":{"ref":"item/70-scene-graph"},"number":8}
JSON
cat >"$FIXTURES/pr-files-8.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"},{"filename":"src/Audio/Mixer.fs"},{"filename":"README.md"}]
JSON
assert_contains "verify-paths: a PR inside its touch-set is OK" "FSGG-PATHS OK" \
  "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.SDD 2>/dev/null)"
drift="$(pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD 2>&1 || true)"
assert_contains "verify-paths: drift is reported with a count" "FSGG-PATHS DRIFT — PR #8 touches 2 file(s) outside" "$drift"
assert_contains "verify-paths: names the offending files"      "src/Audio/Mixer.fs" "$drift"
assert_contains "verify-paths: does not flag files inside the touch-set" "src/Scene/Graph.fs" \
  "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.SDD 2>/dev/null; echo 'src/Scene/Graph.fs')"
assert_contains "verify-paths: points at the remedy" "fsgg-coord widen" "$drift"
assert_fails "verify-paths: drift exits non-zero by default" pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD
assert_contains "verify-paths --warn: reports drift but exits 0 (the advisory CI gate)" "FSGG-PATHS DRIFT" \
  "$(pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD --warn >/dev/null 2>&1 \
  && ok "verify-paths --warn: exit 0" || bad "verify-paths --warn: exit 0" "non-zero exit under --warn"

# A PR with nothing to verify against is SKIP, never a silent OK — CI must not stamp "stays inside
# its touch-set" on a PR that never declared one.
cat >"$FIXTURES/pr-9.json" <<'JSON'
{"head":{"ref":"chore/no-linked-issue"},"number":9}
JSON
cat >"$FIXTURES/pr-files-9.json" <<'JSON'
[{"filename":"README.md"}]
JSON
cat >"$FIXTURES/pr-10.json" <<'JSON'
{"head":{"ref":"item/72-no-touch-set-declared"},"number":10}
JSON
cat >"$FIXTURES/pr-files-10.json" <<'JSON'
[{"filename":"src/Whatever.fs"}]
JSON
skip9="$(pw verify-paths --pr 9 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
assert_contains "verify-paths --warn: an unlinked PR is SKIP, not OK" "FSGG-PATHS SKIP" "$skip9"
case "$skip9" in *"FSGG-PATHS OK"*) bad "verify-paths --warn: SKIP is not mistaken for OK" "OK leaked into a SKIP verdict" ;; *) ok "verify-paths --warn: SKIP is not mistaken for OK" ;; esac
assert_contains "verify-paths --warn: an undeclared touch-set is SKIP" "declares no 'Paths:' touch-set" \
  "$(pw verify-paths --pr 10 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
assert_fails "verify-paths: an unlinked PR fails without --warn" pw verify-paths --pr 9 --repo FS-GG/FS.GG.SDD

# `SKIP` must mean "I asked, and there is nothing to check" — never "I could not ask" (.github#322,
# child (j) of #266). When the head-ref read 502s and the GraphQL fallback then answers healthily
# that the PR closes no issue — the normal case, since ADR-0021's convention is the branch name, not
# a `Closes:` line — an unreachable subject used to be indistinguishable from an unlinked PR: SKIP,
# rc=0, gate green, sticky comment deleted. The touch-set went unchecked and nothing said so.
#
# `--warn` downgrades the *drift verdict* to advisory. It does not license inventing a verdict from
# an unanswered query, so this fails closed under `--warn` too — which is exactly the rc the
# touch-set-drift gate keeps rather than `|| true`-ing away.
vp502() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
            verify-paths --pr 7 --repo FS-GG/FS.GG.SDD "$@" 2>&1; }
out502="$(vp502 --warn || true)"
case "$out502" in
  *"FSGG-PATHS"*) bad "verify-paths: an unreachable head ref reaches NO verdict" "invented a verdict: $out502" ;;
  *) ok "verify-paths: an unreachable head ref reaches NO verdict" ;;
esac
assert_contains "verify-paths: ...and says the head ref could not be read" "cannot read PR #7's head ref" "$out502"
assert_contains "verify-paths: ...and refuses to guess rather than skipping" "refusing to guess" "$out502"
assert_fails "verify-paths: an unreachable head ref fails closed under --warn" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --warn
assert_fails "verify-paths: an unreachable head ref fails closed by default" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD
# An explicit --issue needs no head ref, so it must not be dragged down by the failing lookup: the
# fix must guard the CALL, not the whole resolution step.
assert_contains "verify-paths: --issue bypasses the head-ref read entirely" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.SDD#70' 2>&1)"

# ---- the repo boundary: a verdict may only be printed on ONE repo's PR vs THAT repo's issue (#479) --
# `--pr` used to resolve against the repo you were STANDING IN while `--issue` carried a repo of its
# own. Nothing compared them. So `verify-paths --pr 48 --issue 'FS.GG.Audio#42'` run from `.github`
# read `.github`'s PR 48 — a closed, unrelated PR — diffed it against Audio's touch-set, and printed
# `FSGG-PATHS DRIFT`, naming a file that was in neither the PR nor the repo. The inverse is worse: a
# genuinely drifting PR in another repo reports `FSGG-PATHS OK` whenever the same-numbered PR in the
# CWD happens to be clean — the guard passing, with confidence, on a subject it never looked at. And
# it is load-bearing: `.github/workflows/touch-set-drift.yml` greps this very output for its verdict.
#
# These tests are the mismatched leg #266 asks for. Note what makes them possible: the stub logs the
# REPO of each PR read, because the payload cannot betray the bug — the fixtures are keyed by number
# alone, so the stub is precisely as repo-blind as the code was. The subject has to be asserted from
# the request, or the test would pass on the wrong repo exactly the way the tool did.
no_verdict() { # <name> <output> — the ONLY safe outcome across a boundary is no OK/DRIFT at all
  case "$2" in
    *"FSGG-PATHS OK"*|*"FSGG-PATHS DRIFT"*) bad "$1" "printed a verdict across a repo boundary: $2" ;;
    *) ok "$1" ;;
  esac
}
mism="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
          verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70' 2>&1 || true)"
no_verdict "verify-paths: --repo and --issue in different repos reach NO verdict" "$mism"
assert_contains "verify-paths: ...and names BOTH repos it was asked to straddle" "FS-GG/FS.GG.Rendering" "$mism"
assert_contains "verify-paths: ...and says the touch-set was not checked" "touch-set was NOT checked" "$mism"
assert_fails "verify-paths: a cross-repo mismatch fails by default" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70'
# --warn downgrades a VERDICT to advisory. It does not license a verdict on the wrong subject, so the
# mismatch still fails closed under it — the rc the touch-set-drift gate keeps rather than `|| true`-ing.
assert_fails "verify-paths: a cross-repo mismatch fails closed under --warn too" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70' --warn
no_verdict "verify-paths: ...and still reaches no verdict under --warn" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70' --warn 2>&1 || true)"

# With no --repo, the ISSUE decides — not the checkout. `gh repo view` (the stub) says FS-GG/FS.GG.SDD,
# so the old code would have read PR 7 from SDD; the acceptance criterion is that it reads it from
# Rendering, the repo the caller actually named. Assert on the REQUEST, not the verdict: the fixtures
# would serve an identical, healthy `FSGG-PATHS OK` either way — which is the whole trap.
: >"$GH_LOG"
PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
  verify-paths --pr 7 --issue 'FS-GG/FS.GG.Rendering#70' >/dev/null 2>&1 || true
assert_contains "verify-paths: --issue decides the repo when --repo is absent" \
  "pr-files FS-GG/FS.GG.Rendering 7" "$(cat "$GH_LOG")"
# Scoped to the PR-READ lines, which is what this assertion is actually about. Grepping the whole log
# for the substring would go red the day any unrelated, legitimate SDD read joined this flow — and
# since #494 every issue read logs its repo too, so that log is no longer a proxy for "the PR's repo".
case "$(grep -E '^pr-(get|files) ' "$GH_LOG" || true)" in
  *"FS.GG.SDD"*) bad "verify-paths: ...and the CHECKOUT's repo is never consulted for the PR" \
                     "read the PR from the checkout's repo, not the issue's: $(cat "$GH_LOG")" ;;
  *)             ok  "verify-paths: ...and the CHECKOUT's repo is never consulted for the PR" ;;
esac

# ---- the ISSUE side of the boundary: the harness must be able to tell the repos apart (#494) -------
# Everything above asserts the repo of a PR read, because #479 had to teach the stub to log it. Every
# ISSUE read stayed blind: the store was keyed by number alone, so `paths_of` reading FS.GG.SDD#70 and
# FS.GG.Rendering#70 got the SAME fixture back, and a cross-repo defect on the issue side could not be
# written down as a failing test at all. Not merely untested — UNTESTABLE, which is how a class of bug
# survives a suite that looks thorough (#266's thesis, one level down).
#
# So the store is now repo-QUALIFIED, and these two are different issues that happen to share a number.
# Their touch-sets DIVERGE deliberately: PR 7 touches src/Scene/**, which SDD#494 covers and
# Rendering#494 does not. One PR, one number, two repos — and therefore two different honest verdicts.
# A stub that cannot tell the repos apart cannot produce both, which is precisely the point.
# Seeded CLOSED: these two exist only to be READ as touch-sets. An open issue joins every subsequent
# open-issue candidate scan in its repo, and a fixture that quietly changes what OTHER tests see is
# the wrong way to pay for this one.
seed_issue_in FS-GG/FS.GG.SDD       494 "Scene work (SDD)"        "src/Scene/**, tests/Scene/**" closed
seed_issue_in FS-GG/FS.GG.Rendering 494 "Audio work (Rendering)"  "src/Audio/**"                 closed

assert_contains "#494: SDD's #494 declares the PR's files — OK" "FSGG-PATHS OK" \
  "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.SDD#494' 2>&1 || true)"
# The payload betrays the repo now: same PR, same number, other repo, opposite verdict. Under the old
# number-keyed store BOTH of these read SDD's body and BOTH printed OK — the fixture passing green on
# a subject it never looked at, exactly the way #479 did.
assert_contains "#494: ...and Rendering's #494 — same number, other repo — does NOT: DRIFT" \
  "FSGG-PATHS DRIFT" "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.Rendering --issue 'FS-GG/FS.GG.Rendering#494' 2>&1 || true)"

# Acceptance 1: a test can assert WHICH REPO an issue read was addressed to — from the request.
: >"$GH_LOG"
pw verify-paths --pr 7 --repo FS-GG/FS.GG.Rendering --issue 'FS-GG/FS.GG.Rendering#494' >/dev/null 2>&1 || true
assert_contains "#494: an issue read names the repo it was addressed to" \
  "issue-get FS-GG/FS.GG.Rendering 494" "$(cat "$GH_LOG")"
assert_eq "#494: ...and the same-numbered issue next door is never read" \
  "0" "$(grep -c 'issue-get FS-GG/FS.GG.SDD 494' "$GH_LOG" || true)"

# Requirement 2: a lookup that misses must never fall back to a same-numbered issue in another repo.
# #74 exists ONLY in SDD (touch-set docs/adr/**). Asked for as Rendering#74, the honest answer is 404 —
# not SDD's body. A stub that served it would let a repo-confused `paths_of` come back CONFIDENT and
# WRONG, with a touch-set it had no business seeing.
raw74="$(env PATH="$STUB:$PATH" gh api repos/FS-GG/FS.GG.Rendering/issues/74 2>&1 || true)"
case "$raw74" in
  *"docs/adr"*) bad "#494: a wrong-repo read is never served the same-numbered issue next door" \
                    "served SDD's #74 body to a Rendering read: $raw74" ;;
  *"404"*)      ok  "#494: a wrong-repo read is never served the same-numbered issue next door" ;;
  *)            bad "#494: a wrong-repo read is never served the same-numbered issue next door" \
                    "expected a 404, got: $raw74" ;;
esac
# ...and the OTHER miss stays a loud stub bug (exit 4), because a test that forgot to seed its subject
# is not the same failure as a client that asked the wrong repo, and must not be quietly rendered as one.
rawmiss="$(env PATH="$STUB:$PATH" gh api repos/FS-GG/FS.GG.SDD/issues/6553 2>&1 || true)"
assert_contains "#494: an unseeded issue is still a loud STUB bug, not a 404" "no issue fixture" "$rawmiss"

# A failure injection names a SUBJECT, and a subject is a repo AND a number. Now that SDD#494 and
# Rendering#494 both exist, `GH_FAIL_ISSUE_GET=494` can no longer say WHICH one it means — and a stub
# that let one number arm an injection in two repos would be conflating them again, one layer below
# the bug this item is about. The qualified form aims.
inj_sdd="$(env PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET='FS-GG/FS.GG.Rendering#494' \
             gh api repos/FS-GG/FS.GG.SDD/issues/494 --jq '.title' 2>&1 || true)"
inj_rnd="$(env PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET='FS-GG/FS.GG.Rendering#494' \
             gh api repos/FS-GG/FS.GG.Rendering/issues/494 --jq '.title' 2>&1 || true)"
assert_contains "#494: a qualified injection fires on the repo it names" "502" "$inj_rnd"
assert_contains "#494: ...and leaves the same-numbered issue next door alone" "Scene work (SDD)" "$inj_sdd"
# ...and the bare form still means "that number, in whatever repo has it" — the legacy spelling every
# injection above this line uses, and which must keep working.
assert_contains "#494: the bare injection form still fires regardless of repo" "502" \
  "$(env PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET=494 gh api repos/FS-GG/FS.GG.SDD/issues/494 2>&1 || true)"

# Acceptance 2: a deliberately-introduced cross-repo confusion in `paths_of` turns this fixture RED.
# This is the assertion that proves the leg is COVERED rather than merely coverable — a test suite can
# be repo-aware everywhere and still never exercise the confusion. The mutant clamps `paths_of`'s repo
# argument to FS.GG.SDD, i.e. reintroduces #479 on the issue side, and nothing else.
mutant="$WORK/fsgg-coord-repo-blind"
awk '{ print }
     /^paths_of\(\)/ { print "  set -- \"$1\" FS.GG.SDD \"$3\"   # MUTANT (#494): repo-blind paths_of" }' \
  "$COORD" >"$mutant"
assert_eq "#494: the mutant really is repo-blind (it clamps paths_of to one repo)" \
  "1" "$(grep -c 'MUTANT (#494)' "$mutant" || true)"
: >"$GH_LOG"
mut494="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$mutant" \
            verify-paths --pr 7 --repo FS-GG/FS.GG.Rendering --issue 'FS-GG/FS.GG.Rendering#494' 2>&1 || true)"
# ...and it flipped for the RIGHT reason. A mutant that merely errored its way to a different verdict
# would prove nothing; the request log has to show it actually read the OTHER repo's issue.
assert_contains "#494: ...and the mutant is caught reading the WRONG repo's issue" \
  "issue-get FS-GG/FS.GG.SDD 494" "$(cat "$GH_LOG")"
# The honest verdict on Rendering#494 is DRIFT (above). The repo-blind mutant reads SDD's touch-set
# instead and reports OK — confidently green, on a subject it never looked at. THAT is the flip a test
# must be able to see, and could not before this change.
case "$mut494" in
  *"FSGG-PATHS OK"*)
    ok "#494: a repo-blind paths_of flips DRIFT -> OK, so the fixture goes RED" ;;
  *)
    bad "#494: a repo-blind paths_of flips DRIFT -> OK, so the fixture goes RED" \
        "the mutant's verdict did NOT change — the cross-repo leg is still not exercised: $mut494" ;;
esac

# The refusal must not overshoot into a false alarm on refs that AGREE. Repo names are case-insensitive
# on GitHub, and `--repo` takes a registry short-id everywhere else in this tool — both must still pass.
assert_contains "verify-paths: a registry short-id --repo agrees with the issue's repo" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo sdd --issue 'FS-GG/FS.GG.SDD#70' 2>&1)"
assert_contains "verify-paths: a differently-cased --repo is not a conflict" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/fs.gg.sdd --issue 'FS-GG/FS.GG.SDD#70' 2>&1)"
assert_contains "verify-paths: a bare-repo --issue (owner defaults) is not a conflict" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS.GG.SDD#70' 2>&1)"

# The boundary from the OTHER side, which the report did not name: GitHub lets a PR close an issue in
# a DIFFERENT repo, so the closing-reference fallback can hand back a cross-repo ref all by itself —
# no `--issue` flag involved, and straight back over the line the check above refuses to cross. PR 11's
# branch is not `item/<n>-…`, so resolution falls through to that query.
cat >"$FIXTURES/pr-11.json" <<'JSON'
{"head":{"ref":"chore/closes-another-repo"},"number":11}
JSON
cat >"$FIXTURES/pr-files-11.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"}]
JSON
cat >"$FIXTURES/pr-closes-11.json" <<'JSON'
{"data":{"repository":{"pullRequest":{"closingIssuesReferences":{"nodes":[
  {"number":70,"repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}]}}}},
 "rateLimit":{"cost":1,"remaining":4999}}
JSON
xrepo="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
           verify-paths --pr 11 --repo FS-GG/FS.GG.SDD --warn 2>&1 || true)"
no_verdict "verify-paths: a PR closing another repo's issue reaches NO verdict" "$xrepo"
assert_contains "verify-paths: ...and is SKIP under --warn, with the reason" "FSGG-PATHS SKIP" "$xrepo"
assert_contains "verify-paths: ...naming the other repo"                     "FS-GG/FS.GG.Rendering#70" "$xrepo"
assert_fails "verify-paths: a PR closing another repo's issue fails without --warn" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" verify-paths --pr 11 --repo FS-GG/FS.GG.SDD
# ...and the pre-existing SKIP leg still SKIPs: PR 9 closes nothing at all, which is a different thing
# from closing something elsewhere, and the new arm in the stub must not have swallowed it.
assert_contains "verify-paths: a PR that closes NOTHING is still the unlinked SKIP" \
  "closes no issue" "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
                         verify-paths --pr 9 --repo FS-GG/FS.GG.SDD --warn 2>&1)"

# The "nothing to hand out" path must not trip the empty-array expansion, and still exits 0. Every
# claim this fixture has made by now is in FS.GG.SDD, so that is the queue where everything schedulable
# is claimed or overlapping — stand there.
#
# This used to be a BARE take, and its name said "board-wide (no --repo)". That mode was the #480
# defect, not a feature: with no --repo the scan reached across the whole org, so a bare `take` in the
# `.github` checkout claimed FS.GG.Game#141 and printed a worktree command against `.github`'s
# origin/main. A `take` now always has a scope — the checkout, or an explicit --repo — so the test
# says where it is standing instead of relying on the absence of a scope.
if as_at "$CO_SDD" teal-e55 take >/dev/null 2>&1; then ok "take: the empty queue exits cleanly [#480]"
else bad "take: the empty queue exits cleanly [#480]" "non-zero exit"; fi
# It must say why PER ITEM, in `batch`'s own words. This assertion used to accept the fixed sentence
# "no schedulable item — every candidate is blocked, claimed, overlapping, or undeclared", which named
# four causes without observing any of them (#440) — so the test's own name was the thing it failed to
# check. The reason now has to be one `batch` actually found.
take_empty="$(as_at "$CO_SDD" teal-e55 take 2>&1 >/dev/null)"
assert_contains "take: says WHY there is nothing to hand out" "passed over:" "$take_empty"
assert_contains "take: ...naming a real, observed reason rather than a guessed list" \
  "already claimed by worker" "$take_empty"

# ================================================================================================
# The lock's hard cases. Each of these is an interleaving in which two workers could end up believing
# they hold one item — the failure the whole protocol exists to prevent.
# ================================================================================================
echo "--- ADR-0027: lock invariants under adversarial interleavings ---"

seed_issue 84 "Stale holder, new claimant" "src/A/**"
seed_issue 85 "Stale holder is me"         "src/B/**"
seed_issue 86 "Expired worker, live holder" "src/C/**"
seed_issue 87 "Expired worker, no holder"   "src/D/**"
seed_issue 88 "Forged marker in a message"  "src/E/**"
seed_issue 89 "Malformed marker"            "src/F/**"
seed_issue 90 "Read fails after post"       "src/G/**"

mk_claim() {  # mk_claim <issue> <id> <worker> <fresh|stale>
  local ts="$fresh_ts"; [ "$4" = "stale" ] && ts="$stale_ts"
  jq -n --argjson id "$2" --arg w "$3" --arg ts "$ts" \
    '{id:$id, body:("<!-- fsgg:claim worker=" + $w + " lease=120 -->\nheld"),
      user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}'
}

# (a) A stale marker must be COLLECTED by the next claimant, never merely ignored. An ignored marker
#     is what `heartbeat` later resurrects underneath the new holder — two live markers, one item.
mk_claim 84 810 ghost-111 stale | jq -s '.' >"$STORE/comments-84.json"
: >"$GH_LOG"
c84="$(as heron-b71 claim 'FS.GG.SDD#84' --force 2>&1)"
assert_contains "claim: collects the stale marker it claims over" "collected worker 'ghost-111' expired claim" "$c84"
assert_eq "claim: exactly ONE marker survives (the stale one is gone)" "heron-b71" "$(workers_on 84)"
assert_eq "claim: the collected worker is TOLD, not silently evicted" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=ghost-111"))] | length' "$STORE/comments-84.json")"

# (b) Re-claiming when MY OWN marker went stale must renew a single marker, not mint a second.
# Its OWN worker id: `finch-a3f` holds #42 live for the tests further down, and a worker may hold
# only one item (#516). The scenario is about renewing YOUR OWN stale marker, not about holding two.
mk_claim 85 811 otter-b55 stale | jq -s '.' >"$STORE/comments-85.json"
as otter-b55 claim 'FS.GG.SDD#85' >/dev/null 2>&1
assert_eq "claim: a worker whose own marker went stale ends with ONE marker" "otter-b55" "$(workers_on 85)"
assert_eq "claim: ...and exactly one, not two" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:claim"))] | length' "$STORE/comments-85.json")"

# (c) THE RESURRECTION BUG. A worker whose lease expired must NOT be able to heartbeat its marker back
#     to life once another worker legitimately holds the item. It must be told to stop.
jq -s '.' <(mk_claim 86 812 ghost-222 stale) <(mk_claim 86 813 heron-b71 fresh) >"$STORE/comments-86.json"
: >"$GH_LOG"
hb86="$(as ghost-222 heartbeat 'FS.GG.SDD#86' 2>&1 || true)"
assert_fails "heartbeat: an expired worker cannot resurrect its claim under a new holder" \
  as ghost-222 heartbeat 'FS.GG.SDD#86'
assert_contains "heartbeat: it names the worker that now holds the item" "worker 'heron-b71' does" "$hb86"
assert_contains "heartbeat: it tells the loser to STOP working"          "STOP working it" "$hb86"
assert_eq "heartbeat: the refused renew patched NOTHING" "0" "$(grep -c 'comment-patch' "$GH_LOG" || true)"

# (d) An expired lease is refused even when nobody else took the item — the promise lapsed; re-claim.
mk_claim 87 814 ghost-333 stale | jq -s '.' >"$STORE/comments-87.json"
hb87="$(as ghost-333 heartbeat 'FS.GG.SDD#87' 2>&1 || true)"
assert_fails "heartbeat: an expired lease cannot be renewed in place" as ghost-333 heartbeat 'FS.GG.SDD#87'
assert_contains "heartbeat: it says the lease EXPIRED and points at re-claiming" "EXPIRED" "$hb87"
assert_contains "heartbeat: ...and names the remedy" "fsgg-coord claim" "$hb87"

# (e) Marker forgery. A message body is free-form text; quoting a claim marker inside one must not
#     forge a lock. The marker is only a marker at the START of a comment body.
as wren-c22 say 'FS.GG.SDD#88' 'Careful with <!-- fsgg:claim worker=ghost-666 lease=120 --> in prose.' >/dev/null 2>&1
assert_eq "lock: a claim marker quoted inside a message does NOT hold the item" "" "$(workers_on 88)"
assert_contains "lock: ...so the item is still claimable" "claimed FS.GG.SDD#88" \
  "$(as vole-c88 claim 'FS.GG.SDD#88' 2>/dev/null)"

# (f) A marker we cannot parse a worker out of must FAIL CLOSED — block the item, not vanish.
jq -n --arg ts "$fresh_ts" '[{id:815, body:"<!-- fsgg:claim lease=120 -->\nhalf-written",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-89.json"
assert_fails "lock: a malformed marker blocks the item (fails closed)" as vole-c89 claim 'FS.GG.SDD#89'
assert_contains "lock: the refusal names the unparsed marker" "unparsed-marker" \
  "$(as vole-c89 claim 'FS.GG.SDD#89' 2>&1 || true)"

# (g) A transient read failure on the CAS re-read must not orphan the marker we just posted. An
#     orphaned live marker blocks every other worker for a full lease while nobody works the item.
: >"$STORE/comments-90.json"; echo '[]' >"$STORE/comments-90.json"
cas90="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_READ_ISSUE=90 \
  bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#90' 2>&1 || true)"
assert_contains "claim: a failed CAS re-read removes our own marker" "removed our marker" "$cas90"
assert_contains "claim: ...and says nothing was claimed" "nothing was claimed" "$cas90"
assert_eq "claim: no orphaned marker survives a failed re-read" "" "$(workers_on 90)"

# (h) reap must re-verify freshness immediately before deleting. A holder that heartbeats between the
#     scan and the delete keeps its lock — otherwise `reap` itself causes the double-hold.
cat >"$FIXTURES/board-pw2.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":91,"title":"Slow but alive","url":"https://github.com/FS-GG/FS.GG.SDD/issues/91","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4979}}
JSON
seed_issue 91 "Slow but alive" "src/H/**"
mk_claim 91 816 finch-a3f stale | jq -s '.' >"$STORE/comments-91.json"
reap91="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw2 GH_REAP_RACE=91 \
  bash "$COORD" --worker wren-c22 reap --repo sdd --apply 2>&1 || true)"
assert_contains "reap: a claim renewed between the scan and the delete is SKIPPED" \
  "renewed since the scan" "$reap91"
assert_eq "reap: ...and its marker survives" "finch-a3f" "$(workers_on 91)"

# (i) THE FAIL-OPEN. If the CAS re-read shows NO live marker, our own marker is missing — a peer's
#     --force/reap collected it, or the read lagged our write. We cannot demonstrate we hold the lock,
#     so we must NOT announce that we do. "We cannot tell" is a loss. Guarding only the
#     `winner != us` case (and skipping the empty case) let a worker claim while holding nothing,
#     leaving the item free for the next claimant — two workers, one item.
seed_issue 92 "Our marker vanishes mid-CAS" "src/I/**"
echo '[]' >"$STORE/comments-92.json"
cas92="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_VANISH_ISSUE=92 \
  bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#92' 2>&1 || true)"
assert_fails "claim: an empty CAS re-read is a LOSS, not a win" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_VANISH_ISSUE=92 bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#92'
assert_contains "claim: it says the marker vanished" "marker vanished" "$cas92"
case "$cas92" in *"claimed FS.GG.SDD#92"*) bad "claim: must not announce a lock it cannot show" "$cas92" ;;
                 *) ok "claim: must not announce a lock it cannot show" ;; esac

# (j) A marker bearing OUR id is not proof it is ours: rules 4/5 hand one id to several workers, and
#     the re-claim path skips the CAS entirely. It must warn there, not only on the fresh-claim path.
#     Drive it through rule 4 (a shared claude-code session id), since that is the id that can collide.
seed_issue 93 "Re-claim under a shared id" "src/J/**"
sess93=309bd638-8a1c-42b7-952b-898efb8d1064
# Runs from "$WORK" (not a checkout) so the SHARED SESSION ID names the worker — rule 3 (the worktree
# name) would otherwise win and this test would never exercise the shared-id path it exists for.
shared93() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  env -u FSGG_WORKER -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
      CLAUDE_CODE_SESSION_ID="$sess93" \
      bash -c 'cd "$1" && shift && exec bash "$@"' _ "$WORK" "$COORD" "$@"; }
wid93="$(shared93 whoami 2>/dev/null | awk '/^worker/{print $2}')"
mk_claim 93 817 "$wid93" fresh | jq -s '.' >"$STORE/comments-93.json"
reclaim93="$(shared93 claim 'FS.GG.SDD#93' 2>&1 || true)"
assert_contains "claim: the re-claim path renews rather than duplicating" "lease renewed" "$reclaim93"
assert_eq "claim: ...and still exactly one marker" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:claim"))] | length' "$STORE/comments-93.json")"
assert_contains "claim: the re-claim path WARNS that it never ran the CAS" "adopted ITS lock" "$reclaim93"
assert_contains "claim: ...and names the shared-id hazard" "may not be unique to this worker" "$reclaim93"

# (k) `paths_of` must FAIL CLOSED. An empty touch-set reads as "disjoint from everything", so a failed
#     body read would let the scheduler hand out work overlapping a held item. `claims_of` already
#     refuses to guess the lock state; the touch-set is the other half of the same guarantee.
#     The tell is WHICH diagnosis comes out: a failed read must not be reported as "declared nothing".
seed_issue 94 "Touch-set read fails" "src/K/**"
paths94="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_ISSUE_GET=94 \
  bash "$COORD" overlap 'FS.GG.SDD#94' 'FS.GG.SDD#84' 2>&1 || true)"
assert_contains "paths_of: a failed body read refuses to schedule against an unknown touch-set" \
  "refusing to schedule" "$paths94"
case "$paths94" in
  *"no 'Paths:' touch-set declared"*)
    bad "paths_of: a failed read must not be diagnosed as 'the issue declared nothing'" "$paths94" ;;
  *) ok "paths_of: a failed read must not be diagnosed as 'the issue declared nothing'" ;;
esac

# (l) Two claimants collecting the SAME expired marker: the loser's DELETE 404s because the winner
#     already removed it. "Already gone" is the goal state of a collector, so the loser must still
#     claim — not die "refusing to claim over a marker that is still there" about a marker that is
#     demonstrably not there. GH_DELETE_404 models the winner's delete landing first: the marker is
#     present for our read and our re-verify, and gone by the time our DELETE arrives.
seed_issue 95 "Concurrent GC of one stale marker" "src/L/**"
mk_claim 95 818 ghost-444 stale | jq -s '.' >"$STORE/comments-95.json"
gc95="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_DELETE_404=818 \
  bash "$COORD" --worker heron-b71 claim 'FS.GG.SDD#95' --force 2>&1 || true)"
assert_contains "claim: a 404 collecting an already-gone marker is not fatal" "claimed FS.GG.SDD#95" "$gc95"
assert_eq "delete_comment: 'already gone' leaves exactly the new holder" "heron-b71" "$(workers_on 95)"

# (m) `reap` must DELETE before it notifies. Notifying first means a failed delete tells the worker to
#     stop while its marker still holds the item for a full lease — released to its owner, held
#     against everyone else, and nothing clears it.
cat >"$FIXTURES/board-pw3.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":96,"title":"Delete fails","url":"https://github.com/FS-GG/FS.GG.SDD/issues/96","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4979}}
JSON
seed_issue 96 "Delete fails during reap" "src/M/**"
mk_claim 96 819 ghost-555 stale | jq -s '.' >"$STORE/comments-96.json"
: >"$GH_LOG"
reap96="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw3 GH_FAIL_DELETE=819 \
  bash "$COORD" --worker wren-c22 reap --repo sdd --apply 2>&1 || true)"
assert_contains "reap: a failed delete is reported, not swallowed" "FAILED" "$reap96"
assert_eq "reap: a failed delete leaves the marker in place" "ghost-555" "$(workers_on 96)"
assert_eq "reap: ...and does NOT tell the worker it was released" "0" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=ghost-555"))] | length' "$STORE/comments-96.json")"

# (n) `say --to` must normalize to a worker id. Ids are slug()'d at creation and `inbox` matches `.to`
#     by exact string, so an unslugged target posts a message its recipient can never see.
seed_issue 97 "Addressed message" "src/N/**"
echo '[]' >"$STORE/comments-97.json"
say97="$(as wren-c22 say 'FS.GG.SDD#97' --to 'Heron-B71' 'the impl is yours' 2>&1)"
assert_contains "say: a mis-cased --to is normalized to the worker id" "normalized from 'Heron-B71'" "$say97"
assert_eq "say: ...and the marker addresses the slug, so inbox can match it" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=heron-b71"))] | length' "$STORE/comments-97.json")"
assert_eq "say: '*' stays the literal broadcast target" "1" \
  "$(as wren-c22 say 'FS.GG.SDD#97' 'anyone home' >/dev/null 2>&1; jq '[.[] | select(.body | test("to=\\*"))] | length' "$STORE/comments-97.json")"

# `--help` must not silently truncate when a subcommand is added (usage is marker-delimited now).
help="$(pw --help 2>/dev/null)"
for c in whoami claim heartbeat release who reap take batch overlap widen say inbox verify-paths; do
  case "$help" in *"fsgg-coord $c"*) : ;; *) bad "usage: documents '$c'" "missing from --help"; continue ;; esac
done
ok "usage: --help documents every parallel-work subcommand"

# ================================================================================================
# THE MARKER IS THE LOCK: active_claims may not be seeded from the board column (FS-GG/.github#257)
# ================================================================================================
# `claim` writes `Status: In progress` strictly best-effort — a Projects v2 5xx is swallowed, and an
# item that was never added to the board has no column to write. Deriving "what is running" from the
# column therefore LOST exactly the claims it most needed to see. Every state below is one the old
# column-seeded code reported as "nothing is running":
#   #211 — a board item whose Status flip failed: held, but the column still says Ready.
#   #215 — a live claim on an item that is not on the board at all.
#   #216 — a DEAD worker's claim on an off-board item: reap could never collect it, so the lease
#          stopped being self-healing and the item stayed locked forever.
# And the consequence the issue itself misjudged as "scheduling is nonetheless safe": because an
# off-board claim never reached `active_claims`, its touch-set was never RESERVED, so `batch` would
# hand out #213 — which overlaps the very subtree #215's holder is working. That is a scheduling
# correctness bug, not merely an observability one.
#
# Fixtures live in FS.GG.Rendering so the arm-B scan (open issues, per repo) sees only these and not
# the pile of FS.GG.SDD issues the earlier sub-sections left in the store.
echo "--- .github#257: in-flight work is defined by the marker, not the column ---"

cat >"$FIXTURES/board-blind.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":210,"title":"In progress, no marker","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/210","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":211,"title":"Held, but the Status flip failed","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/211","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":212,"title":"Free and disjoint","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/212","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":213,"title":"Overlaps an OFF-BOARD claim","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/213","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4978}}
JSON

RND=FS-GG/FS.GG.Rendering
seed_issue 210 "In progress, no marker"            "src/Orphan2/**" "$RND"   # arm A only (0 comments)
seed_issue 211 "Held, but the Status flip failed"  "src/Flip/**"    "$RND"
seed_issue 212 "Free and disjoint"                 "src/Clean/**"   "$RND"
seed_issue 213 "Overlaps an OFF-BOARD claim"       "src/Off/Sub/**" "$RND"
seed_issue 215 "Off-board, held"                   "src/Off/**"     "$RND"   # never added to the board
seed_issue 216 "Off-board, holder died"            "src/Dead/**"    "$RND"
seed_issue 217 "Just a chatty issue"               "src/Chatty/**"  "$RND"

mk_claim 211 830 wren-c22   fresh | jq -s '.' >"$STORE/comments-211.json"
mk_claim 215 831 puffin-h11 fresh | jq -s '.' >"$STORE/comments-215.json"
mk_claim 216 832 ghost-222  stale | jq -s '.' >"$STORE/comments-216.json"
# #217 has comments but NO marker. It is arm-B's candidate prune under test: a chatty open issue is
# NOT in-flight work, and only the board's own `In progress` may license an `unclaimed` verdict.
jq -n --arg ts "$fresh_ts" '[{id:833, body:"just a normal comment, no marker here",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-217.json"

bl()   { PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
blas() { local w="$1"; shift
         PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 bash "$COORD" --worker "$w" "$@"; }

# ---- who: every live marker, wherever the board thinks the item is ------------------------------
blind_json="$(bl who --repo rendering --json 2>/dev/null)"
assert_eq "who: reports exactly the in-flight items — no more, no less" "[210,211,215,216]" \
  "$(jq -c '[.[].number] | sort' <<<"$blind_json")"
assert_eq "who: a claim whose board Status flip FAILED is held, not invisible" "held" \
  "$(jq -r '.[] | select(.number==211) | .state' <<<"$blind_json")"
assert_eq "who: ...and names its worker" "wren-c22" \
  "$(jq -r '.[] | select(.number==211) | .worker' <<<"$blind_json")"
assert_eq "who: a claim on an item that is NOT ON THE BOARD is held" "held" \
  "$(jq -r '.[] | select(.number==215) | .state' <<<"$blind_json")"
assert_eq "who: ...and carries its touch-set, read from the issue body" '["src/Off"]' \
  "$(jq -c '.[] | select(.number==215) | .paths' <<<"$blind_json")"
assert_eq "who: an off-board claim past its lease is STALE" "stale" \
  "$(jq -r '.[] | select(.number==216) | .state' <<<"$blind_json")"
assert_eq "who: In progress with no marker is still UNCLAIMED (only the column can say so)" "unclaimed" \
  "$(jq -r '.[] | select(.number==210) | .state' <<<"$blind_json")"
assert_eq "who: a markerless item's touch-set still resolves (arm-A body read)" '["src/Orphan2"]' \
  "$(jq -c '.[] | select(.number==210) | .paths' <<<"$blind_json")"
assert_eq "who: a chatty open issue with no marker is NOT in-flight work" "" \
  "$(jq -r '.[] | select(.number==217) | .number // empty' <<<"$blind_json")"

# ---- reap: a dead worker's off-board claim is collectable, so the lease self-heals --------------
assert_contains "reap: finds an expired claim the board never knew about" \
  "would reap  FS.GG.Rendering#216  worker ghost-222" "$(bl reap --repo rendering 2>/dev/null)"

# ---- batch: an off-board claim RESERVES its touch-set ------------------------------------------
# #211 is claimed (Status says Ready — the lock disagrees). #213 overlaps src/Off, which off-board
# #215 is holding. Only #212 is genuinely free.
blind_batch="$(bl batch --repo rendering --json 2>/dev/null)"
assert_eq "batch: schedules only the item no live marker touches" '["FS.GG.Rendering#212"]' \
  "$(jq -c '.' <<<"$blind_batch")"
blind_err="$(bl batch --repo rendering 2>&1 >/dev/null)"
assert_contains "batch: skips a Ready item that a marker actually holds" \
  "#211 — already claimed by worker wren-c22" "$blind_err"
assert_contains "batch: refuses to schedule over an OFF-BOARD claim's touch-set" \
  "#213 — overlaps in-flight work" "$blind_err"

# ---- #428: a skip reason must name the HOLDER and the LEASE, not just the obstacle ---------------
# The touch-set is a lock, and a lock has an owner and an expiry. Reporting only the collision tells a
# worker they are blocked and withholds both facts every remedy needs: WHO to talk to, and WHETHER the
# wait is worth it. `wren-c22`/`puffin-h11` hold FRESH claims here, so the window is the full lease.
#
# The exact MINUTE is deliberately not asserted: these claims age in real time as the fixture runs, so
# a `~120m` needle passes on a fast machine and reds on a slow one. Assert the shape (a window exists,
# and it is a countdown rather than an expiry); the EXPIRED case below pins the other branch exactly.
assert_contains "#428: a claimed item names the lease window, not just the holder" \
  "#211 — already claimed by worker wren-c22 (lease frees in ~" "$blind_err"
assert_contains "#428: an OVERLAP names the worker holding the colliding paths" \
  "#213 — overlaps in-flight work held by puffin-h11 on FS.GG.Rendering#215" "$blind_err"
assert_contains "#428: ...and its lease window" \
  "held by puffin-h11 on FS.GG.Rendering#215 (lease frees in ~" "$blind_err"
assert_contains "#428: ...and still shows WHICH paths collided" \
  "src/Off/Sub  ⇄  src/Off" "$blind_err"

# ================================================================================================
# #428: a STARVED queue is BUSY, not empty — say so, name the holders, and give a lease to wait on
# ================================================================================================
# The chokepoint this is filed against: in a repo where one file is the touch-set of nearly every
# item, ONE claim serialises the whole queue. `batch` correctly hands out nothing — and "nothing
# schedulable" reads exactly like an empty backlog, so the worker goes home from a repo with four
# items in it. The per-item reasons say why each item is out; only a LEASE says whether to wait.
#
# #223 (held, fresh) reserves src/Starve/**; #222 is claimed outright; #216's holder is DEAD, and its
# touch-set is still reserved — a claim may only be broken by `reap`, never by scheduling over it.
echo "--- #428: a starved queue names its holders and its leases ---"

cat >"$FIXTURES/board-starved.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":221,"title":"Overlaps a live claim","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/221","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":222,"title":"Claimed outright","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/222","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":224,"title":"Overlaps a DEAD holder's claim","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/224","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":225,"title":"Overlaps a MARKERLESS In progress item","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/225","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":226,"title":"In progress, outside the protocol","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/226","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4978}}
JSON

seed_issue 221 "Overlaps a live claim"          "src/Starve/Sub/**" "$RND"
seed_issue 222 "Claimed outright"               "src/Solo/**"       "$RND"
seed_issue 223 "Holds src/Starve"               "src/Starve/**"     "$RND"
seed_issue 224 "Overlaps a DEAD holder's claim" "src/Dead/Sub/**"   "$RND"
seed_issue 225 "Overlaps a MARKERLESS item"     "src/Ghostly/Sub/**" "$RND"
seed_issue 226 "In progress, outside protocol"  "src/Ghostly/**"    "$RND"   # NO comments = no marker

mk_claim 222 840 kite-z01 fresh | jq -s '.' >"$STORE/comments-222.json"
mk_claim 223 841 tern-y99 fresh | jq -s '.' >"$STORE/comments-223.json"
# (#216 already carries ghost-222's STALE marker on src/Dead/**, seeded above.)

sv() { PATH="$STUB:$PATH" GH_BOARD_SET=starved GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }

starved_json="$(sv batch --repo rendering --json 2>/dev/null)"
assert_eq "#428: a starved queue schedules NOTHING (the lock still holds)" "[]" \
  "$(jq -c '.' <<<"$starved_json")"

starved_err="$(sv batch --repo rendering 2>&1 >/dev/null)"
assert_contains "#428: the starved queue is called BUSY, not empty" \
  "3 item(s) are QUEUED BEHIND LIVE CLAIMS held by:" "$starved_err"
assert_contains "#428: ...and it names every holder, so the worker knows who to talk to" \
  "held by: ghost-222, kite-z01, tern-y99" "$starved_err"
assert_contains "#428: ...and gives a lease to decide against" \
  "soonest: lease EXPIRED — reapable" "$starved_err"
assert_contains "#428: ...and says plainly that this is not an empty backlog" \
  "this queue is BUSY, not empty" "$starved_err"

# A DEAD holder is the one blocker a worker can clear themselves — so it must not read as a wait.
assert_contains "#428: an EXPIRED lease is a reap, never a wait" \
  "#224 — overlaps in-flight work held by ghost-222 on FS.GG.Rendering#216 (lease EXPIRED — reapable)" \
  "$starved_err"
assert_contains "#428: ...and the starved-queue advice says how to collect it" \
  "1 of those lease(s) have EXPIRED — collect them: fsgg-coord reap --repo FS.GG.Rendering --apply" "$starved_err"

# A MARKERLESS `In progress` item reserves its touch-set too — `active_claims` is right to, since
# something is evidently editing those files. But there is no worker to name, no lease to wait out and
# nobody to `say` to, so it must NOT be dressed up as a holder. "held by — (lease unknown)" would
# invite a worker to wait for a marker that is never coming, and would put "—" in the holder list.
assert_contains "#428: a markerless In progress item is not reported as a HOLDER" \
  "#225 — overlaps FS.GG.Rendering#226, which the board says is In progress with NO claim marker" \
  "$starved_err"
assert_contains "#428: ...and it says there is no lease to wait out" \
  "there is no lease to wait out; see: fsgg-coord who" "$starved_err"
refute_contains "#428: ...and an unnameable reserver never appears as a holder named '—'" \
  "held by —" "$starved_err"
# It reserves, so it must not be scheduled over — but it is NOT a lease, so it must not inflate the
# queued-behind-claims count either. Three items are queued behind real claims; #225 is not one.
assert_contains "#428: ...and it is not counted as a lease the worker can wait out" \
  "3 item(s) are QUEUED BEHIND LIVE CLAIMS held by: ghost-222, kite-z01, tern-y99" "$starved_err"
assert_eq "#428: ...and the markerless item's files are still RESERVED (never scheduled over)" "[]" \
  "$(jq -c '.' <<<"$starved_json")"

# The advice must not fire when the worker actually GOT something — that queue is not starved, and a
# "this queue is BUSY" banner on a successful schedule is noise that trains workers to skip stderr.
refute_contains "#428: a queue that DID hand out work prints no starved-queue banner" \
  "QUEUED BEHIND LIVE CLAIMS" "$blind_err"

# ---- inbox: messages ride off-board claims too --------------------------------------------------
blas puffin-h11 say 'FS.GG.Rendering#215' --to hoopoe-i22 'I hold src/Off — stay out.' >/dev/null 2>&1
assert_contains "inbox: delivers a message posted on an off-board claim" "I hold src/Off — stay out." \
  "$(blas hoopoe-i22 inbox --repo rendering 2>/dev/null)"

# ---- HOW the candidate list is fetched: the lock may not be capped, nor read from a cache --------
# `issues` (the ETag'd command) asks for ONE page of 100. Had the candidate scan reused it, a live
# claim on a repo's 101st open issue would be invisible — and `batch` would hand its touch-set away.
# A conditional request is equally forbidden: a 304 serving a pre-claim `comments: 0` hides a marker.
: >"$GH_LOG"
bl who --repo rendering >/dev/null 2>&1
assert_contains "who: the open-issue scan PAGINATES (a lock has no 100-issue limit)" \
  "issue-list FS-GG/FS.GG.Rendering paginate=1" "$(cat "$GH_LOG")"
assert_contains "who: ...and is never a conditional request (no cache may hide a live marker)" \
  "inm=none" "$(cat "$GH_LOG")"

# ---- reap: an off-board claim has no board entry to reset, and reap must not pretend it did ------
reap215="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 GH_FAIL_ITEM_EDIT=1 \
  bash "$COORD" --worker wren-c22 reap --repo rendering --apply 2>/dev/null)"
assert_contains "reap --apply: still collects the claim when the board write fails" \
  "reaped  FS.GG.Rendering#216  worker ghost-222" "$reap215"
assert_contains "reap --apply: ...and does NOT claim a board reset it never performed" \
  "not on board (marker cleared; nothing to reset)" "$reap215"
assert_eq "reap --apply: the marker is gone — the lock released, board or no board" "" "$(workers_on 216)"

# ================================================================================================
# THE CLAIM'S LIFETIME IS THE WORK'S LIFETIME (#581, #533, #516)
# ================================================================================================
# Three symptoms, one missing concept. A claim marker should live exactly as long as the work does,
# and it failed at BOTH ENDS and in the middle:
#
#   #581  the WORK outlives the CLAIM — lease expiry is treated as proof of abandonment
#   #533  the CLAIM outlives the WORK — `done --flip` never dropped the marker
#   #516  one worker, N claims  — the CAS protects the ITEM; nobody protected the WORKER
#
# ---- #581: an expired lease is EVIDENCE of abandonment. It is not PROOF. ------------------------
# The false positive is systematic, not incidental: WORK THAT TAKES LONGER THAN THE LEASE. And the
# protocol's own remedy — heartbeat — is what a busy worker forgets precisely when the work is long.
# `take --repo rendering` handed out FS.GG.Rendering#429 with PR #433 OPEN on `item/429-*`, because a
# loaded box stretched one build past 120 minutes. Then it happened AGAIN, to the worker fixing #485:
# the claim lapsed mid-test-cycle and was reaped with the work uncommitted. It survived only because
# the reaping worker chose to preserve the worktree as a WIP commit. That is generosity, not a lock.
#
# An open PR on `item/<n>-*` is the worktree protocol's OWN artifact, and it is server-side proof.

# Seed our OWN stale claim: the reap test above legitimately collected #216's, and a test that
# depends on another test's leftovers is a test that passes for the wrong reason.
jq -n --arg ts "$stale_ts" '[{id:880, body:"<!-- fsgg:claim worker=ghost-222 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-216.json"
assert_eq "#581: precondition — the expired claim is back" "ghost-222" "$(workers_on 216)"

# reap must REFUSE to destroy work that is demonstrably alive.
reap_live="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="216:433" \
  bash "$COORD" --worker wren-c22 reap --repo rendering --apply 2>&1 || true)"
assert_contains "#581: reap REFUSES a claim whose PR is open — the lease lapsed, the WORK did not" \
  "REFUSING" "$reap_live"
assert_contains "#581: ...and names the PR, so the refusal is checkable" "#433" "$reap_live"
# THE ONE THAT MATTERS: the marker must still be there. A refusal that deleted anyway is the bug.
assert_eq "#581: ...and the claim SURVIVES — this is the leg that reaped live work twice" \
  "ghost-222" "$(workers_on 216)"

# ...and with NO open PR it still reaps. Without this the assertion above is satisfied by a reap that
# simply stopped working — the #436 shape, guarding the mechanism that stops live work being destroyed.
reap_dead="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 GH_FAIL_ITEM_EDIT=1 \
  bash "$COORD" --worker wren-c22 reap --repo rendering --apply 2>/dev/null || true)"
assert_contains "#581: an expired claim with NO open PR is still reaped (the negative control)" \
  "reaped  FS.GG.Rendering#216" "$reap_dead"
assert_eq "#581: ...and its marker is gone" "" "$(workers_on 216)"

# `who` must SAY it. `STALE` and `STALE (PR #433 OPEN)` are not the same fact, and `who` is what a
# human reads immediately before deciding to reap. Its own subject, in its own repo — a test that
# leans on another test's leftovers passes for the wrong reason.
seed_issue 890 "Long build, lapsed lease" 'src/Long890/**'
jq -n --arg ts "$stale_ts" '[{id:890, body:"<!-- fsgg:claim worker=shrike-a91 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-890.json"
who_live="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="890:433" \
  bash "$COORD" who --repo sdd --json 2>/dev/null || true)"
assert_eq "#581: who carries the proof of life on the STALE row" "#433 item/890-live-work" \
  "$(jq -r '.[] | select(.number == 890) | .livePr // ""' <<<"$who_live")"
who_txt="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="890:433" \
  bash "$COORD" who --repo sdd 2>/dev/null || true)"
assert_contains "#581: ...and the human-facing row says STALE (#433 OPEN), not a bare STALE" \
  "STALE (#433 OPEN)" "$who_txt"
# The negative control: with no open PR it is a bare STALE, and a reaper may collect it.
who_dead="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" who --repo sdd 2>/dev/null || true)"
case "$who_dead" in
  *"STALE (#"*) bad "#581: with no open PR the row must be a BARE stale" "$who_dead" ;;
  *)            ok  "#581: with no open PR the row must be a BARE stale" ;;
esac

# ---- #533: a COMPLETED item must not keep its lock. ---------------------------------------------
# `done --flip` verified the merge, set Status, rolled up the epic — and never touched the marker.
# `release` was the only path that dropped it, and `release` REWRITES Status, so running it on an item
# you just stamped Done clobbers the stamp you just earned. So on the SUCCESS path there was no action,
# in the tool or the recipe, that dropped the lock. It stayed live for the rest of the 120m lease, and
# a live marker's `Paths:` keep reserving its touch-set.
#
# It bites hardest exactly where the protocol is working: the items most likely to overlap a
# just-finished item are its own FOLLOW-UP findings — the ones §4 tells you to file BECAUSE you were
# standing in those files. The recipe reliably produced an item its own author had locked out.
jq -n --arg ts "$fresh_ts" '[{id:860, body:"<!-- fsgg:claim worker=vole-533 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-42.json"
assert_eq "#533: precondition — the finished item is claimed" "vole-533" "$(workers_on 42)"
: >"$GH_LOG"
run_worker_done="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker vole-533 done 'FS.GG.SDD#42' --pr 7 --flip 2>&1 || true)"
assert_contains "#533: the stamp is still earned" "FSGG-DONE   FS.GG.SDD#42" "$run_worker_done"
assert_eq "#533: ...and `done --flip` DROPS the claim — a finished item must not reserve its files" \
  "" "$(workers_on 42)"

# It must only drop OUR OWN marker. Deleting another worker's claim is `reap`'s job, and it is
# destructive — `done` may not do it silently just because the item happens to be finished.
jq -n --arg ts "$fresh_ts" '[{id:861, body:"<!-- fsgg:claim worker=other-999 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-42.json"
other_done="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker vole-533 done 'FS.GG.SDD#42' --pr 7 --flip 2>&1 || true)"
assert_eq "#533: ...but it must NOT delete a claim that is not ours" "other-999" "$(workers_on 42)"
assert_contains "#533: ...it says so instead, and points at reap" "still holds its claim" "$other_done"
: >"$STORE/comments-42.json"; echo '[]' >"$STORE/comments-42.json"

# ---- #516: at most ONE item per worker. ---------------------------------------------------------
# The CAS is keyed on the ITEM, so it guarantees at most one worker per item. NOTHING guaranteed the
# converse — and the cost model assumes it. This is #419/ADR-0027 turned inside out: that family is N
# workers colliding on ONE id; this is one id holding N items, and "give every worker its own id" does
# nothing for it. A claim RESERVES A TOUCH-SET, so the second, unattended claim is a live lock on files
# nobody is editing — and in this repo `scripts/fsgg-coord` is exactly the contended path (#428).
#
# The worker who found it found it BY DOING IT: two `take`s, both succeeded, neither said a word.
seed_issue 870 "First item"  'src/A870/**'
seed_issue 871 "Second item" 'src/B871/**'
jq -n --arg ts "$fresh_ts" '[{id:870, body:"<!-- fsgg:claim worker=godwit-b49 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-870.json"
c516_rc=0
c516="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker godwit-b49 claim 'FS.GG.SDD#871' 2>&1)" || c516_rc=$?
assert_eq "#516: a worker who already holds an item cannot silently claim a second" "1" "$c516_rc"
assert_contains "#516: ...and the refusal NAMES the item they hold" "FS.GG.SDD#870" "$c516"
assert_contains "#516: ...and says why it is not merely untidy (the touch-set stays reserved)" \
  "reserves a touch-set" "$c516"
assert_eq "#516: ...and the second item is NOT claimed" "" "$(workers_on 871)"
# --force is the deliberate override. A rule with no escape hatch gets worked around, not obeyed.
PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker godwit-b49 claim 'FS.GG.SDD#871' --force >/dev/null 2>&1 || true
assert_eq "#516: ...but --force still holds two, deliberately" "godwit-b49" "$(workers_on 871)"
# A DIFFERENT worker is of course unaffected — the rule is one item per WORKER, not one per repo.
: >"$STORE/comments-871.json"; echo '[]' >"$STORE/comments-871.json"
PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker stoat-c71 claim 'FS.GG.SDD#871' >/dev/null 2>&1 || true
assert_eq "#516: a DIFFERENT worker claims freely — the rule is one item per worker, not per repo" \
  "stoat-c71" "$(workers_on 871)"

# ================================================================================================
# FS-GG/.github#273: an unmatchable touch-set token must never read as DISJOINT.
#
# The docs promised "globs"; the matcher implements exact paths + subtree containment. A token that
# keeps a wildcard after normalization (`**/x`, `src/*/x`) therefore matches NO file — and a token
# that matches nothing CONFLICTS WITH NOTHING. So the failure was OPEN: `overlap` printed DISJOINT,
# `batch` handed two workers items whose real files overlapped completely, and `widen` reported the
# cleanest possible answer for the worst possible reason. Every assertion below is one of the doors
# that fail-open walked through.
# ================================================================================================
echo "--- .github#273: an unmatchable touch-set is refused, never cleared ---"

seed_issue 300 "Leading globstar"  '**/packages.lock.json'
seed_issue 301 "Real lockfiles"    'src/Engine/packages.lock.json'
seed_issue 302 "Interior wildcard" 'src/*/lock.json'
seed_issue 303 "Honest scene"      'src/Scene/**'
seed_issue 304 "Honest audio"      'src/Audio/**'

# (1) THE BUG. #300 declares every lockfile via `**/`; #301 names one of those very files. Under the
#     old matcher `**/packages.lock.json` was a directory literally named `**`, so these two read as
#     DISJOINT and both workers were told to go ahead. It must now refuse instead — and, above all,
#     it must never say DISJOINT.
ov300="$(pw overlap 'FS.GG.SDD#300' 'FS.GG.SDD#301' 2>&1 || true)"
assert_contains "overlap: an unmatchable leading '**/' is named, not cleared" \
  "unmatchable touch-set token(s): **/packages.lock.json" "$ov300"
assert_contains "overlap: ...and the supported grammar is quoted" "There is no glob matcher" "$ov300"
case "$ov300" in *"DISJOINT —"*) bad "overlap: #300 vs #301 is NEVER reported DISJOINT" "the #273 fail-open: $ov300" ;;
                 *) ok "overlap: #300 vs #301 is NEVER reported DISJOINT" ;; esac
rc300=0; pw overlap 'FS.GG.SDD#300' 'FS.GG.SDD#301' >/dev/null 2>&1 || rc300=$?
assert_eq "overlap: an unmatchable token exits 2 (undeclared), not 0" "2" "$rc300"

# An interior wildcard is the same defect wearing a different hat, and it is refused on EITHER side.
ov302="$(pw overlap 'FS.GG.SDD#303' 'FS.GG.SDD#302' 2>&1 || true)"
assert_contains "overlap: an interior '*' is refused from the right-hand side too" \
  "unmatchable touch-set token(s): src/*/lock.json" "$ov302"

# (2) The shapes the matcher DOES honour must keep working — a fail-closed gate that rejects the
#     legal grammar is just a differently-broken scheduler.
ov303="$(pw overlap 'FS.GG.SDD#303' 'FS.GG.SDD#304' 2>&1 || true)"
assert_contains "overlap: a trailing '/**' pair is still DISJOINT" "DISJOINT —" "$ov303"
assert_contains "overlap: an exact path still conflicts with its own subtree" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#301' 'FS.GG.SDD#301' 2>&1 || true)"

# (3) `claim` refuses the lock on an item that declares nothing the matcher can reserve. Holding it
#     would make our files invisible to every other worker's overlap check.
cl300="$(as smew-f31 claim 'FS.GG.SDD#300' 2>&1 || true)"
assert_contains "claim: refuses an item whose touch-set can never match a file" \
  "can never match a file: **/packages.lock.json" "$cl300"
assert_eq "claim: ...and takes no marker while refusing" "" "$(workers_on 300)"
# An item with NO `Paths:` declares nothing and reserves nothing — it stays claimable, as before.
as teal-e55 claim 'FS.GG.SDD#72' >/dev/null 2>&1 || true
assert_eq "claim: an item with no 'Paths:' at all is still claimable (unchanged)" "teal-e55" "$(workers_on 72)"

# (4) `widen` must reject BEFORE it PATCHes, so a refused widen leaves the old declaration — which is
#     still reserving real files — intact on the issue body.
body303_before="$(jq -r '.body' "$STORE/issue-303.json")"
wd303="$(as brant-g07 widen 'FS.GG.SDD#303' --paths 'src/Scene/**, **/*.lock.json' 2>&1 || true)"
assert_contains "widen: refuses to write an unmatchable touch-set" "can never match a file" "$wd303"
assert_eq "widen: ...and the issue body is untouched (rejected before the PATCH)" \
  "$body303_before" "$(jq -r '.body' "$STORE/issue-303.json")"
case "$wd303" in *"DISJOINT —"*) bad "widen: an unmatchable widen never reports DISJOINT" "cleared: $wd303" ;;
                 *) ok "widen: an unmatchable widen never reports DISJOINT" ;; esac

# (5) The scheduler. A CANDIDATE with an unmatchable token is passed over with its reason (exactly as
#     an undeclared one is) — it must never fall through to `conflicts_between`, which would clear it
#     against every item on the board.
cat >"$FIXTURES/board-pw4.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":300,"title":"Leading globstar","url":"https://github.com/FS-GG/FS.GG.SDD/issues/300","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":301,"title":"Real lockfiles","url":"https://github.com/FS-GG/FS.GG.SDD/issues/301","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4978}}
JSON
pw4() { PATH="$STUB:$PATH" GH_BOARD_SET=pw4 GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
b4_out="$(pw4 batch --repo sdd --json 2>/dev/null)"
b4_err="$(pw4 batch --repo sdd 2>&1 >/dev/null || true)"
assert_eq "batch: schedules the honest item, and only it" '["FS.GG.SDD#301"]' "$(jq -c '.' <<<"$b4_out")"
assert_contains "batch: says WHY it passed over the unmatchable candidate" \
  "#300 — unmatchable 'Paths:' token(s): **/packages.lock.json" "$b4_err"

# (6) …and an IN-FLIGHT claim with an unmatchable token reserves NOTHING, so every candidate would
#     clear it. We cannot see that holder's real surface, so we schedule nothing at all — the same
#     refusal `paths_of` makes when it cannot read a touch-set.
mk_claim 300 840 ghost-666 fresh | jq -s '.' >"$STORE/comments-300.json"
b4h="$(pw4 batch --repo sdd 2>&1 || true)"
assert_contains "batch: REFUSES to schedule against a held item that reserves nothing" \
  "refusing to schedule against an unknown touch-set" "$b4h"
assert_contains "batch: ...and names the held item" "FS.GG.SDD#300" "$b4h"
assert_fails "batch: ...and exits non-zero rather than handing out work" pw4 batch --repo sdd
: >"$STORE/comments-300.json"; echo '[]' >"$STORE/comments-300.json"

# (7) `verify-paths` gets a fourth verdict. Under a broken touch-set every changed file is "outside"
#     it — technically true, and useless. Name the broken token instead of burying it under a file list.
cat >"$FIXTURES/pr-30.json" <<'JSON'
{"head":{"ref":"item/300-leading-globstar"},"number":30}
JSON
cat >"$FIXTURES/pr-files-30.json" <<'JSON'
[{"filename":"src/Engine/packages.lock.json"}]
JSON
vp30="$(pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD 2>&1 || true)"
assert_contains "verify-paths: an unmatchable touch-set is INVALID, not DRIFT" "FSGG-PATHS INVALID" "$vp30"
assert_contains "verify-paths: ...and names the token that matches nothing" "**/packages.lock.json" "$vp30"
case "$vp30" in *"FSGG-PATHS DRIFT"*) bad "verify-paths: INVALID is not dressed up as DRIFT" "$vp30" ;;
                *) ok "verify-paths: INVALID is not dressed up as DRIFT" ;; esac
assert_fails "verify-paths: an unmatchable touch-set exits non-zero" pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD
assert_contains "verify-paths --warn: reports INVALID but exits 0 (the advisory CI gate)" "FSGG-PATHS INVALID" \
  "$(pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD --warn >/dev/null 2>&1 \
  && ok "verify-paths --warn: INVALID exits 0" || bad "verify-paths --warn: INVALID exits 0" "non-zero exit under --warn"

# (8) The guard must not reintroduce the fail-open INSIDE ITSELF. `paths_of` fails closed by calling
#     `die` when the body read fails — but `die` is an `exit`, and an `exit` inside `$( )` kills only
#     the substitution subshell. Had `claim` passed `"$(paths_of …)"` as an ARGUMENT, it would print
#     "refusing to schedule against an unknown touch-set" and then claim the item anyway, exit 0.
seed_issue 305 "Body read explodes" 'src/Fine/**'
rc305=0
PATH="$STUB:$PATH" GH_ISSUES_FROM_STORE=1 GH_FAIL_ISSUE_GET=305 \
  bash "$COORD" --worker smew-f31 claim 'FS.GG.SDD#305' >/dev/null 2>&1 || rc305=$?
assert_eq "claim: a failed touch-set read FAILS CLOSED (the die is not swallowed by \$( ))" "1" "$rc305"
assert_eq "claim: ...and no marker was taken on the item it could not read" "" "$(workers_on 305)"

# (9) `widen` re-checks against in-flight claims. A LEGACY holder whose own tokens are unmatchable
#     reserves nothing and clears everything — so widen must refuse to answer rather than report the
#     cleanest possible DISJOINT. (`overlap --active` and `batch` guard this; widen did not.)
seed_issue 306 "Widener"        'src/Widen/**'
seed_issue 307 "Legacy holder"  '**/packages.lock.json'
mk_claim 307 841 ghost-777 fresh | jq -s '.' >"$STORE/comments-307.json"
wd306="$(as brant-g07 widen 'FS.GG.SDD#306' --paths 'src/Widen/**, src/More/**' 2>&1 || true)"
assert_contains "widen: refuses to re-check against a holder whose touch-set reserves nothing" \
  "in-flight claim(s) declare unmatchable touch-set token(s): FS.GG.SDD#307" "$wd306"
case "$wd306" in *"DISJOINT —"*) bad "widen: never clears against an unmatchable in-flight claim" "cleared: $wd306" ;;
                 *) ok "widen: never clears against an unmatchable in-flight claim" ;; esac
# The widen itself still LANDED — that is how a bad declaration gets fixed, including on a held item.
assert_contains "widen: ...but the re-declaration itself still landed" "Paths: src/Widen/**, src/More/**" \
  "$(jq -r '.body' "$STORE/issue-306.json")"

# Widening is precisely how a bad declaration gets repaired — including on an item you already hold.
# The screen must never leave a worker stuck: `#307` is claimed AND declares the unmatchable token,
# and it must still be able to widen out of it. (This passes for a reason independent of the `$self`
# exclusion — widen PATCHes before it re-checks, so by then #307's own paths are already the new,
# valid ones. The exclusion guards the read-after-write case where the re-read still serves the old
# body; that lag is not reproducible against this stub, so it is defence, not a tested path.)
wd307="$(as ghost-777 widen 'FS.GG.SDD#307' --paths 'src/Engine/packages.lock.json' 2>&1 || true)"
assert_contains "widen: an item held with a bad touch-set can widen out of it (the PATCH lands)" \
  "widened FS.GG.SDD#307 → Paths: src/Engine/packages.lock.json" "$wd307"
assert_contains "widen: ...and the repaired declaration persisted" "Paths: src/Engine/packages.lock.json" \
  "$(jq -r '.body' "$STORE/issue-307.json")"
: >"$STORE/comments-307.json"; echo '[]' >"$STORE/comments-307.json"

# (10) The CI gate must classify INVALID before its `else`, or the verdict lands in `skip` — which
#      DELETES the sticky comment and passes green, burying the finding. Assert the workflow's own
#      branch order, the one thing the fixture can check about it offline.
wf="$HERE/../../.github/workflows/touch-set-drift.yml"
assert_contains "touch-set-drift.yml: INVALID is classified, not absorbed by the else" \
  "verdict=invalid" "$(cat "$wf")"
assert_contains "touch-set-drift.yml: ...and rendered by its own comment branch, never the ✅ one" \
  'elif [ "$VERDICT" = "invalid" ]' "$(cat "$wf")"
inv_line="$(grep -n 'FSGG-PATHS INVALID' "$wf" | head -1 | cut -d: -f1)"
else_line="$(grep -n 'verdict=skip' "$wf" | head -1 | cut -d: -f1)"
if [ "$inv_line" -lt "$else_line" ]; then ok "touch-set-drift.yml: INVALID is tested BEFORE the skip fallback"
else bad "touch-set-drift.yml: INVALID is tested BEFORE the skip fallback" "INVALID at $inv_line, skip at $else_line"; fi

# (11) The IN-FLIGHT-claim guards, on the two commands that consult `active_claims`. These are the
#      `claims_with_unmatchable_paths` (jq) path, NOT the per-candidate `invalid_paths` (grep) path
#      exercised in (5)/(6) — a mutation that neuters the shared jq filter must turn these red, or the
#      two guards standing between a legacy holder and a double-booking are untested.
#      #308 is a legacy holder: claimed, in flight, declaring a token that reserves nothing.
seed_issue 308 "Legacy in-flight" '**/*.lock.json'
mk_claim 308 842 ghost-888 fresh | jq -s '.' >"$STORE/comments-308.json"

ov_act="$(pw overlap 'FS.GG.SDD#303' --active 2>&1 || true)"
assert_contains "overlap --active: refuses against an in-flight claim that reserves nothing" \
  "in-flight claim(s) declare unmatchable touch-set token(s): FS.GG.SDD#308" "$ov_act"
case "$ov_act" in *"DISJOINT —"*) bad "overlap --active: never clears against an unmatchable claim" "cleared: $ov_act" ;;
                  *) ok "overlap --active: never clears against an unmatchable claim" ;; esac
rc_act=0; pw overlap 'FS.GG.SDD#303' --active >/dev/null 2>&1 || rc_act=$?
assert_eq "overlap --active: ...and exits 2, not 0" "2" "$rc_act"

b_act="$(pw batch --repo sdd 2>&1 || true)"
assert_contains "batch: refuses when an IN-FLIGHT claim (not a candidate) reserves nothing" \
  "refusing to schedule against an unknown touch-set" "$b_act"
assert_contains "batch: ...and names that in-flight claim" "FS.GG.SDD#308" "$b_act"
assert_fails "batch: ...and hands out nothing" pw batch --repo sdd
: >"$STORE/comments-308.json"; echo '[]' >"$STORE/comments-308.json"

# ================================================================================================
# FS-GG/.github#277: a quoted `Paths:` line is not a declaration.
#
# #273's token was UNMATCHABLE — it reserved nothing, and once named it could be refused. This one is
# FABRICATED: every token is well-formed, so `invalid_paths` sees nothing wrong, and the item reserves
# the WRONG files with complete confidence. `paths_from_body` grepped the whole body, fences included,
# so an issue that quoted a `Paths:` line — in a repro, in a suggested `widen` — acquired it; and
# because every match was unioned, a real declaration plus a quoted one reserved both.
# ================================================================================================
echo "--- .github#277: a fenced or quoted 'Paths:' line is not a declaration ---"

# The #261 shape: the ONLY `Paths:` line is fenced, quoting another repo. Every token is valid, so the
# #273 guard clears it. It must now declare NOTHING — unschedulable beats mis-scheduled.
seed_issue_raw 310 "Quotes another repo" 'Worker A declared, in FS.GG.Rendering#186:

```
Paths: nuget.config, Directory.Build.local.props
```

The fix here lives in `scripts/fsgg-coord`.'
seed_issue 311 "Real coord work" 'scripts/fsgg-coord'

ov310="$(pw overlap 'FS.GG.SDD#310' 'FS.GG.SDD#311' 2>&1 || true)"
assert_contains "paths: an issue quoting a valid-token 'Paths:' line reserves nothing" \
  "no 'Paths:' touch-set declared" "$ov310"
case "$ov310" in *"DISJOINT —"*) bad "overlap: a fabricated touch-set is NEVER reported DISJOINT" "the #277 fail-open: $ov310" ;;
                 *) ok "overlap: a fabricated touch-set is NEVER reported DISJOINT" ;; esac
rc310=0; pw overlap 'FS.GG.SDD#310' 'FS.GG.SDD#311' >/dev/null 2>&1 || rc310=$?
assert_eq "overlap: a fenced-only declaration exits 2 (undeclared), not 0" "2" "$rc310"

# The union hazard: a real declaration must not be widened by a quoted one. #312 works on Scene only;
# it quotes Audio. It must conflict with Scene and stay DISJOINT from Audio.
seed_issue_raw 312 "Real + quoted" 'Paths: src/Scene/**

Compare with the audio item, which declares:

```
Paths: src/Audio/**
```'
assert_contains "paths: a body with a real and a quoted declaration takes the real one" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#312' 'FS.GG.SDD#70' 2>&1 || true)"
assert_contains "paths: ...and does NOT union in the quoted one" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#312' 'FS.GG.SDD#42' 2>&1 || true)"

# A quote that ESCAPES the strip (bare, at column 0) is where #277's suggested `head -1` and the
# union part ways — and where `head -1` would have reintroduced ADR-0021's own bug. Taking the first
# line reserves the QUOTE and DROPS the real declaration under it: two workers, told DISJOINT, both
# editing src/Scene. The union over-reserves instead, which is loud and costs only parallelism.
seed_issue_raw 313 "Bare quote above the real line" 'Paths: src/Audio/**   (quoting the other issue)

Paths: src/Scene/**'
assert_contains "paths: a bare quote ABOVE the declaration never drops it (no under-reserve)" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#313' 'FS.GG.SDD#70' 2>&1 || true)"
assert_contains "paths: ...and the over-reserved quote is reported as a real OVERLAP, not hidden" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#313' 'FS.GG.SDD#42' 2>&1 || true)"

# The other two block syntaxes markdown gives an author for quoting a line.
seed_issue_raw 314 "Tilde fence" '~~~
Paths: src/Audio/**
~~~

Paths: src/Scene/**'
assert_contains "paths: a '~~~' fence hides a quoted declaration too" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#314' 'FS.GG.SDD#42' 2>&1 || true)"
seed_issue_raw 315 "Indented block" 'Reproduction:

    Paths: src/Audio/**

Paths: src/Scene/**'
assert_contains "paths: a 4-space indented block is a code block, not a declaration" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#315' 'FS.GG.SDD#42' 2>&1 || true)"
# ...but the legal shapes keep working: a fenced block that quotes nothing, and a list-indented line.
seed_issue_raw 316 "Indented <4" '  Paths: src/Scene/**'
assert_contains "paths: a line indented 3 spaces or fewer is still a declaration" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#316' 'FS.GG.SDD#70' 2>&1 || true)"

# `widen` WRITES the line `paths_from_body` READS. If the reader skips fenced lines and the writer
# still patches the first `Paths:` anywhere, widen rewrites the quote inside the fence, the real
# declaration stands, and the tool reports a widen that changed nothing it will ever read.
as brant-g07 widen 'FS.GG.SDD#312' --paths 'src/Scene/**, src/Legacy/**' >/dev/null 2>&1 || true
body312="$(jq -r '.body' "$STORE/issue-312.json")"
assert_contains "widen: patches the REAL declaration, not the quoted one" \
  "Paths: src/Scene/**, src/Legacy/**" "$body312"
assert_contains "widen: ...and leaves the fenced quote untouched" "Paths: src/Audio/**" "$body312"
assert_contains "widen: ...and the reader now sees the widened set" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#312' 'FS.GG.SDD#43' 2>&1 || true)"

# The writer's other half: when the body's ONLY `Paths:` line is fenced, there is no declaration to
# patch — widen must APPEND one. Before the fix it overwrote the quote and the item still declared
# nothing, so a worker who "fixed" their touch-set stayed unschedulable with no idea why.
as brant-g07 widen 'FS.GG.SDD#310' --paths 'scripts/fsgg-coord' >/dev/null 2>&1 || true
body310="$(jq -r '.body' "$STORE/issue-310.json")"
assert_contains "widen: appends a declaration when the only 'Paths:' line is fenced" \
  "Paths: nuget.config, Directory.Build.local.props" "$body310"
assert_contains "widen: ...and the appended line is what the reader picks up" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#310' 'FS.GG.SDD#311' 2>&1 || true)"

# Because the reader UNIONS the surviving declarations, `widen` must replace the first and DROP the
# rest — otherwise a leftover line keeps reserving its old tokens and widen did not widen to what it
# printed. This is also how a body that accumulated a stray bare quotation gets repaired.
seed_issue_raw 320 "Two bare declarations" 'Paths: src/Audio/**

Paths: src/Scene/**'
as brant-g07 widen 'FS.GG.SDD#320' --paths 'src/Legacy/**' >/dev/null 2>&1 || true
body320="$(jq -r '.body' "$STORE/issue-320.json")"
assert_eq "widen: collapses duplicate declarations to exactly one" "1" \
  "$(printf '%s\n' "$body320" | grep -cE '^ {0,3}[Pp]aths:' || true)"
assert_contains "widen: ...and the survivor is the widened set" "Paths: src/Legacy/**" "$body320"
assert_contains "widen: ...so the reader no longer sees the dropped tokens" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#320' 'FS.GG.SDD#42' 2>&1 || true)"
assert_contains "widen: ...and does see the widened one" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#320' 'FS.GG.SDD#43' 2>&1 || true)"

# An UNCLOSED fence swallows the rest of the body, so there is no declaration to patch and widen
# APPENDS — straight into the still-open fence, where the reader will never see it. widen would then
# print a confident `widened …` over an item that still declares nothing, on precisely the body a
# worker runs widen to repair. The fence is closed before the declaration is appended.
seed_issue_raw 318 "Unclosed fence" 'Here is the repro:

```
Paths: src/Audio/**'
as brant-g07 widen 'FS.GG.SDD#318' --paths 'src/Scene/**' >/dev/null 2>&1 || true
assert_contains "widen: closes an unterminated fence before appending the declaration" \
  "OVERLAP" "$(pw overlap 'FS.GG.SDD#318' 'FS.GG.SDD#70' 2>&1 || true)"
assert_contains "widen: ...and the appended declaration does not reserve the quoted set" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#318' 'FS.GG.SDD#42' 2>&1 || true)"

# `repl` reaches awk through the environment, not `-v`, which applies escape processing to its value.
# Under `-v` a token holding a backslash was PATCHed as something other than the string
# `die_on_invalid_paths` validated and widen echoed back — `src/a\tb` became a real tab, which the
# reader's `tr` then split into two tokens. The body must store exactly what was announced.
seed_issue_raw 319 "Backslash token" 'Paths: src/Old'
as brant-g07 widen 'FS.GG.SDD#319' --paths 'src/a\tb' >/dev/null 2>&1 || true
body319="$(jq -r '.body' "$STORE/issue-319.json")"
assert_contains "widen: a backslash in a token is stored verbatim (no awk -v escape processing)" \
  'Paths: src/a\tb' "$body319"
assert_eq "widen: ...and the stored line holds no literal tab" "0" \
  "$(printf '%s' "$body319" | grep -cP '\t' || true)"

# The scheduler: a fenced-only body is passed over exactly as an undeclared one is — with its reason.
cat >"$FIXTURES/board-pw5.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":317,"title":"Fenced only","url":"https://github.com/FS-GG/FS.GG.SDD/issues/317","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":311,"title":"Real coord work","url":"https://github.com/FS-GG/FS.GG.SDD/issues/311","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4977}}
JSON
seed_issue_raw 317 "Fenced only" 'Repro:

```
Paths: scripts/fsgg-coord
```'
pw5() { PATH="$STUB:$PATH" GH_BOARD_SET=pw5 GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
b5_out="$(pw5 batch --repo sdd --json 2>/dev/null)"
b5_err="$(pw5 batch --repo sdd 2>&1 >/dev/null || true)"
assert_eq "batch: schedules the honestly-declared item, and only it" '["FS.GG.SDD#311"]' "$(jq -c '.' <<<"$b5_out")"
assert_contains "batch: says WHY it passed over the fenced-only candidate" \
  "#317 — no 'Paths:' declared (cannot schedule" "$b5_err"
# ...and calls it an OMISSION, not a design decision (#496). A fenced-only declaration is invisible to
# the scheduler, so this item really does declare nothing — and the worker reading this list is the
# person who can fix it. The sentinel is offered by name, so the fix is a copy-paste either way.
assert_contains "batch: names the fenced-only candidate as an OMISSION, and offers the sentinel" \
  "this is an OMISSION" "$b5_err"
# The fail-open in one line: #317's quote names the very file #311 declares. Had the quote been read,
# batch would have seen an OVERLAP it could not schedule — instead it must see no declaration at all.
case "$b5_out" in *317*) bad "batch: never schedules an item on a fabricated touch-set" "$b5_out" ;;
                  *) ok "batch: never schedules an item on a fabricated touch-set" ;; esac

# ---- #353: widen / overlap --active scope their touch-set check to ONE repo ---------------------
# `Paths:` tokens are repo-relative. `batch` compares only within a repo (`active_claims "$repo"`),
# but `widen` and `overlap --active` used to call `active_claims` with NO repo — so an item's tokens
# were compared against EVERY repo's live claims. `scripts/fsgg-coord` in one repo then "collided"
# with `scripts/fsgg-coord` in another, two strings that name files in two different repositories.
# The fail is not the dangerous direction (it never hands two workers one file), but it stops a
# worker who has nothing to stop for and posts a false "sequence behind me" notice onto a stranger's
# item — and it is INCOHERENT with the scheduler, which would happily run the pair in parallel.
#
# The fixture: #401 (SDD) is the item we probe. #402 (Rendering, in flight) declares the SAME bare
# token `scripts/fsgg-coord` — a phantom across the repo boundary. #403 (SDD, in flight) declares
# `src/Scene/**` — a REAL same-repo neighbour, the positive control that proves scoping did not
# simply blind the check.
cat >"$FIXTURES/board-xrepo.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":401,"title":"SDD widen target","url":"https://github.com/FS-GG/FS.GG.SDD/issues/401","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":402,"title":"Rendering bystander","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/402","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":403,"title":"SDD sibling","url":"https://github.com/FS-GG/FS.GG.SDD/issues/403","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4975}}
JSON
seed_issue 401 "SDD widen target"    "scripts/fsgg-coord"  "FS-GG/FS.GG.SDD"
seed_issue 402 "Rendering bystander" "scripts/fsgg-coord"  "FS-GG/FS.GG.Rendering"
seed_issue 403 "SDD sibling"         "src/Scene/**"        "FS-GG/FS.GG.SDD"
# Live markers on the two in-flight items (a claim marker IS a comment; the store is the source).
jq -n --arg ts "$fresh_ts" '[{id:8402, body:"<!-- fsgg:claim worker=render-x1 lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-402.json"
jq -n --arg ts "$fresh_ts" '[{id:8403, body:"<!-- fsgg:claim worker=sdd-sib lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-403.json"
px()  { PATH="$STUB:$PATH" GH_BOARD_SET=xrepo GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
asx() { local w="$1"; shift
        PATH="$STUB:$PATH" GH_BOARD_SET=xrepo GH_ISSUES_FROM_STORE=1 bash "$COORD" --worker "$w" "$@"; }

# overlap --active: the cross-repo namesake is NOT a collision.
ovx="$(px overlap 'FS.GG.SDD#401' --active 2>&1 || true)"
assert_contains "overlap --active: a same-named path in ANOTHER repo is not a collision (#353)" "DISJOINT" "$ovx"
case "$ovx" in *OVERLAP*|*Rendering#402*)
    bad "overlap --active: never invents a cross-repo overlap (#353)" "$ovx" ;;
  *) ok "overlap --active: never invents a cross-repo overlap (#353)" ;; esac
if px overlap 'FS.GG.SDD#401' --active >/dev/null 2>&1
  then ok  "overlap --active: exits 0 when the only namesake claim is in another repo (#353)"
  else bad "overlap --active: exits 0 when the only namesake claim is in another repo (#353)" "exited non-zero"; fi

# Pairwise `overlap a b` closes the same trap. #401 (SDD) and #402 (Rendering) BOTH declare the bare
# token `scripts/fsgg-coord` right now — the phantom-collision setup. Repo-relative tokens in two
# different repos can never name the same file, so this is DISJOINT, not the OVERLAP `conflicts_between`
# would report if handed the two token lists raw (#353). (Run before the widen tests below mutate #401.)
pov="$(px overlap 'FS.GG.SDD#401' 'FS-GG/FS.GG.Rendering#402' 2>&1 || true)"
assert_contains "overlap a b: different repos are DISJOINT even on a same-named token (#353)" \
  "different repos" "$pov"
case "$pov" in *OVERLAP*) bad "overlap a b: never invents a cross-repo overlap (#353)" "$pov" ;;
               *) ok "overlap a b: never invents a cross-repo overlap (#353)" ;; esac
if px overlap 'FS.GG.SDD#401' 'FS-GG/FS.GG.Rendering#402' >/dev/null 2>&1
  then ok  "overlap a b: exits 0 for a cross-repo pair (#353)"
  else bad "overlap a b: exits 0 for a cross-repo pair (#353)" "exited non-zero"; fi

# widen: the cross-repo namesake is NOT a collision, and its holder is NOT pestered.
before402="$(jq 'length' "$STORE/comments-402.json")"
wx="$(asx kite-t01 widen 'FS.GG.SDD#401' --paths 'scripts/fsgg-coord' 2>&1 || true)"
assert_contains "widen: a same-named path in ANOTHER repo is not a collision (#353)" "DISJOINT" "$wx"
case "$wx" in *OVERLAP*|*Rendering#402*)
    bad "widen: never invents a cross-repo overlap (#353)" "$wx" ;;
  *) ok "widen: never invents a cross-repo overlap (#353)" ;; esac
assert_eq "widen: leaves the innocent cross-repo bystander uncommented (#353)" \
  "$before402" "$(jq 'length' "$STORE/comments-402.json")"
if asx kite-t01 widen 'FS.GG.SDD#401' --paths 'scripts/fsgg-coord' >/dev/null 2>&1
  then ok  "widen: exits 0 when the only namesake claim is in another repo (#353)"
  else bad "widen: exits 0 when the only namesake claim is in another repo (#353)" "exited non-zero"; fi

# Positive control: a REAL same-repo overlap is still caught — scoping narrowed the set, not the test.
wc353="$(asx kite-t01 widen 'FS.GG.SDD#401' --paths 'src/Scene/**' 2>&1 || true)"
assert_contains "widen: a genuine SAME-repo overlap is STILL detected (#353)" \
  "now collides with FS.GG.SDD#403" "$wc353"
assert_contains "widen: ...and still notifies the same-repo worker (#353)" \
  "notified worker sdd-sib on FS.GG.SDD#403" "$wc353"
assert_fails "widen: a real same-repo collision still exits non-zero (#353)" \
  asx kite-t01 widen 'FS.GG.SDD#401' --paths 'src/Scene/**'

# ...and a genuine SAME-repo pairwise overlap is still caught (Test C left both declaring src/Scene/**).
assert_contains "overlap a b: a real same-repo overlap is STILL detected (#353)" "OVERLAP" \
  "$(px overlap 'FS.GG.SDD#401' 'FS.GG.SDD#403' 2>&1 || true)"
assert_fails "overlap a b: a real same-repo overlap still exits non-zero (#353)" \
  px overlap 'FS.GG.SDD#401' 'FS.GG.SDD#403'

# ---- #312: batch/take qualify their touch-set comparison by repo, even when scheduling ALL repos --
# #353 fixed the single-item comparers (`widen`, `overlap --active`) by SCOPING their input to one
# repo. `batch` cannot do that: with no `--repo` it schedules across the whole board, so its
# reservation set legitimately mixes repos. It used to flatten every live claim's tokens into one bare
# list and hand it to `conflicts_between` — which cannot see a repo — so `src/Physics/**` held in one
# repo phantom-collided with `src/Physics/**` Ready in ANOTHER, and the scheduler passed over an item
# nothing was actually holding (#312). The fix tags each reserved token with its owning repo and
# compares only within a repo.
#
# The fixture is built on repos with EMPTY stores (Templates/Governance/Audio/Game) so unscoped
# `active_claims` sees only these planted claims — not the polluted SDD/Rendering stores earlier tests
# left behind. The token `src/Physics/**` is unique to this block for the same reason.
#   #420 Templates  Ready        src/Physics/**          — candidate; only a CROSS-repo namesake holds it
#   #421 Governance Ready        src/Physics/**          — candidate in a THIRD repo, SAME bare token
#   #422 Audio      Ready        src/Physics/Solver.fs   — REAL same-repo overlap of the in-flight #424
#   #425 Templates  Ready        src/Physics/Gun.fs      — REAL same-repo overlap of the batch-mate #420
#   #423 Game       In progress  src/Physics/**          — cross-repo phantom (its own repo, no candidate)
#   #424 Audio      In progress  src/Physics/**          — the genuine same-repo neighbour #422 clashes with
cat >"$FIXTURES/board-xbatch.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":420,"title":"Templates A","url":"https://github.com/FS-GG/FS.GG.Templates/issues/420","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":421,"title":"Governance B","url":"https://github.com/FS-GG/FS.GG.Governance/issues/421","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":422,"title":"Audio control","url":"https://github.com/FS-GG/FS.GG.Audio/issues/422","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":425,"title":"Templates mate","url":"https://github.com/FS-GG/FS.GG.Templates/issues/425","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":423,"title":"Game phantom","url":"https://github.com/FS-GG/FS.GG.Game/issues/423","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Game"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":424,"title":"Audio neighbour","url":"https://github.com/FS-GG/FS.GG.Audio/issues/424","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4970}}
JSON
seed_issue 420 "Templates A"      "src/Physics/**"        "FS-GG/FS.GG.Templates"
seed_issue 421 "Governance B"     "src/Physics/**"        "FS-GG/FS.GG.Governance"
seed_issue 422 "Audio control"    "src/Physics/Solver.fs" "FS-GG/FS.GG.Audio"
seed_issue 425 "Templates mate"   "src/Physics/Gun.fs"    "FS-GG/FS.GG.Templates"
seed_issue 423 "Game phantom"     "src/Physics/**"        "FS-GG/FS.GG.Game"
seed_issue 424 "Audio neighbour"  "src/Physics/**"        "FS-GG/FS.GG.Audio"
# Live markers on the two in-flight items (a claim marker IS a comment; the store is the source).
jq -n --arg ts "$fresh_ts" '[{id:8423, body:"<!-- fsgg:claim worker=game-x1 lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-423.json"
jq -n --arg ts "$fresh_ts" '[{id:8424, body:"<!-- fsgg:claim worker=audio-n1 lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-424.json"
# Runs from a directory that is NO CHECKOUT, deliberately. Since #480 a bare `batch` scopes to the repo
# you are standing in, and this section is about the ONE case that can only be seen across repos: a
# phantom cross-repo overlap. Standing nowhere is how you legitimately ask for the whole board — and it
# keeps this test measuring `conflicts_between`, not the cwd of whoever ran the fixture.
pb()    { ( cd "$NOGIT" && PATH="$STUB:$PATH" GH_BOARD_SET=xbatch GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@" ); }
# ...and the same stub, STANDING in a Templates checkout, to prove the scope actually bites here.
pb_tpl() { ( cd "$CO_TPL" && PATH="$STUB:$PATH" GH_BOARD_SET=xbatch GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@" ); }

# The whole board, no --repo: the cross-repo phantom (#423) and same-repo neighbour (#424) are BOTH
# in flight, but only #422/#425 (real same-repo overlaps) may be dropped. #420 clears the phantom;
# #421 rides alongside #420 though both declare the same bare token in different repos.
xb_json="$(pb batch --json 2>/dev/null)"
assert_eq "batch: schedules cross-repo candidates sharing only a repo-relative token (#312)" \
  '["FS.GG.Templates#420","FS.GG.Governance#421"]' "$(jq -c '.' <<<"$xb_json")"
xb_err="$(pb batch 2>&1 >/dev/null || true)"
# The two REAL same-repo overlaps are still caught — scoping narrowed the comparison, not the check.
assert_contains "batch: a genuine same-repo IN-FLIGHT overlap is still caught (#312)" \
  "#422 — overlaps in-flight work" "$xb_err"
assert_contains "batch: a genuine same-repo BATCH-MATE overlap is still caught (#312)" \
  "#425 — overlaps batch member FS.GG.Templates#420" "$xb_err"

# #480, where it bites: the SAME board, but STANDING in the Templates checkout. Org-wide it schedules
# FS.GG.Templates#420 AND FS.GG.Governance#421 — a worker in the Templates tree must be offered only
# #420. The Governance item is real work, it is simply not work you can do from this checkout, and
# handing it over comes with a worktree command built against the wrong repository's origin/main.
assert_eq "batch: standing in Templates, the Governance candidate is NOT offered [#480]" \
  '["FS.GG.Templates#420"]' "$(pb_tpl batch --json 2>/dev/null | jq -c '.')"
# The ssh remote form resolves identically — a worker who cloned over ssh is not a different worker.
# ($CO_TPL's origin is git@github.com:FS-GG/FS.GG.Templates.git, so this leg also proves that parse.)
assert_eq "batch: ...and the ssh remote form resolves to the same scope [#480]" \
  "1" "$(pb_tpl batch --json 2>/dev/null | jq 'length')"
# ...and neither cross-repo candidate is ever passed over for a phantom.
case "$xb_err" in
  *"#420 — overlaps"*|*"#421 — overlaps"*)
    bad "batch: never drops a candidate for a cross-repo phantom (#312)" "$xb_err" ;;
  *) ok "batch: never drops a candidate for a cross-repo phantom (#312)" ;;
esac

# ---- #344: an unreadable board must FAIL CLOSED, not render as a confident empty answer ----------
# `gql` dies inside a `$( )`, and `exit` there only unwinds the subshell; `set -e` does not carry the
# substitution's non-zero across a bare `out="$(...)"` assignment. So every scheduler read used to get
# an empty payload and carry on — `take`/`next`/`ready`/`batch` reported an empty, itemised board and
# exited 0 when the board could not be read at all. `die` now signals the top-level shell, so a failed
# read halts the whole command at any nesting depth. GH_FAIL_BOARD makes the board scan a 401.
#
# THE SCAN CACHE (#418) DOES NOT WEAKEN THIS, and the two rules have to be said together:
#   * a failed READ is never rescued by the cache — the read dies, the command dies. That is what the
#     loop below asserts, with the cache cold (FSGG_COORD_SCAN_TTL_SEC=0), which is the only state in
#     which a read actually happens;
#   * a cache HIT is not a read. `next`/`take` served from a <90s scan never touch the network, so
#     there is no unreachable board for them to fail closed ON. That is the deal the cache makes, and
#     it is safe precisely because the claim CAS — REST markers, never cached — is what grants the
#     item. A stale schedule costs a lost race and a retry; it cannot cost a double claim.
# The old fixture passed this loop with a cache warmed by earlier tests, which asserted neither rule.
rm -f "$FSGG_COORD_CACHE"/scan-*.json
for spec in "ready" "next" "batch" "take --repo .github"; do
  if FSGG_COORD_SCAN_TTL_SEC=0 GH_FAIL_BOARD=1 run $spec >/dev/null 2>&1; then
    bad "#344: '$spec' fails closed (non-zero) when the board is unreachable"
  else
    ok  "#344: '$spec' fails closed (non-zero) when the board is unreachable"
  fi
done
# ...and the cache must not be a back door around it: a MISS + a 401 is still a hard failure, even
# with caching switched fully on. (A stale file cannot mask it either — this one is empty.)
rm -f "$FSGG_COORD_CACHE"/scan-*.json
assert_fails "#418: a cache MISS + an unreachable board still fails closed (the cache never rescues a failed read)" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_BOARD=1 FSGG_COORD_SCAN_TTL_SEC=90 \
    bash "$COORD" next --repo FS.GG.SDD
assert_eq "#418: ...and a failed scan is NOT written to the cache" "no" \
  "$(ls "$FSGG_COORD_CACHE"/scan-*.json >/dev/null 2>&1 && echo yes || echo no)"
# The headline regression: `take` must not turn "I could not read the board" into the confident claim
# "every candidate is blocked, claimed, overlapping, or undeclared." (Cache off: `take` is a SCHEDULING
# read, so a warm cache would legitimately answer without a read at all — see the two rules above. The
# regression under test is what happens when it DOES read and the read fails.)
take_unreadable="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_FAIL_BOARD=1 run take --repo .github 2>&1 || true)"
case "$take_unreadable" in
  *"every candidate is"*) bad "#344: take does NOT assert an empty queue on an unreadable board" "got: $take_unreadable" ;;
  *)                      ok  "#344: take does NOT assert an empty queue on an unreadable board" ;;
esac
# ...and the failure is surfaced, not swallowed: `batch` (the 'see why' target) names the read error.
assert_contains "#344: batch surfaces the read failure instead of an empty result" \
  "GraphQL call failed" "$(GH_FAIL_BOARD=1 run batch --repo .github 2>&1 || true)"
# The DELIBERATE exceptions stay soft under the now-fatal `die`. (1) A best-effort board write whose
# subject is off-board must NOT unwind a claim whose lock is already held — the marker is the lock.
# Pure signal-die would abort the whole claim after the marker was posted; `SOFT_DIE=1` on the
# `( … set_field … )` subshell keeps that `die` local, so the claim completes and reports "not on
# board". This is the path the earlier tests missed (they seed off-board markers directly rather than
# driving `claim`), so it is asserted here explicitly.
seed_issue 779 "An item that is not on the board" "src/Off344/**"
# env-via-`env`, like `rel`: a shell prefix on the `as` FUNCTION would not export to the inner `bash`.
claim_off="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  env GH_OFFBOARD_ITEM=779 bash "$COORD" --worker osprey-344 claim 'FS.GG.SDD#779' 2>&1 || true)"
assert_contains "#344: an off-board claim still lands (best-effort flip's die stays local)" \
  "not on board" "$claim_off"
assert_contains "#344: ...and it reports the claim rather than a fatal abort" \
  "claimed FS.GG.SDD#779 by worker osprey-344" "$claim_off"
assert_eq "#344: ...and the marker (the lock) was actually posted" "yes" \
  "$([ -f "$STORE/comments-779.json" ] && [ -n "$(claims_on 779)" ] && echo yes || echo no)"
# (2) release/reap must still drop a lease when the board is unreadable (item_status marks its read
# SOFT_DIE=1) — proven above at "release: an unreadable Status is not overwritten". So "unreachable"
# and "readable, and empty" no longer share an exit code (#266), and neither strands a claim.

# ================================================================================================
# #418 — THE SHARED BUDGET. N workers, one GitHub account, one 5,000-pt/hr GraphQL budget. The bug
# this section pins down is not "we ran out of points"; it is what running out USED to do quietly:
# `claim` took the lock over REST, could not write `Status: In progress` because GraphQL was gone,
# swallowed the 403, and printed "not on board". The board then said `Backlog` while a worker held
# the item, and `next` hid it from every other worker — the CLAIM-STATUS-LAG drift /check-board
# reconciles. So the fixture asserts the three properties that close it, with the budget stubbed OUT
# (GH_RATELIMIT=1) rather than reasoned about:
#   (1) exhaustion is RECOGNISED and NAMED — exit EX_RATE (75), not a mystery 1;
#   (2) the claim still LANDS (REST lock) and the board write is QUEUED, not lost, and says so;
#   (3) `flush` replays the queue once the budget returns — and the board write actually happens.
# Plus the lever that keeps the budget from running out at all: (4) the shared scan cache, which is
# what turns N workers looping `next` into ONE board scan per TTL window.

seed_issue 810 "Rate-limited claim" "src/Rl810/**"

# (1)+(2) claim under an exhausted budget.
: >"$GH_LOG"; rl_before_rest="$(rcount)"
claim_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
  env GH_RATELIMIT=1 bash "$COORD" --worker vole-418 claim 'FS.GG.SDD#810' 2>&1 || true)"
assert_contains "#418: a rate-limited claim still LANDS — the lock is REST, not GraphQL" \
  "claimed FS.GG.SDD#810 by worker vole-418" "$claim_rl"
assert_contains "#418: ...and the board write is reported DEFERRED, not silently dropped" \
  "DEFERRED" "$claim_rl"
assert_contains "#418: ...and names the condition (exhausted budget), NOT 'not on board'" \
  "GraphQL budget exhausted" "$claim_rl"
# The regression that mattered: the old code printed "not on board" for a 403. If that string comes
# back for a rate-limited claim, the drift is back with it.
case "$claim_rl" in *"not on board"*) bad "#418: a rate-limit must NOT be misreported as 'not on board'" "$claim_rl" ;;
                    *) ok "#418: a rate-limit must NOT be misreported as 'not on board'" ;; esac
assert_eq "#418: the marker (the lock) was posted despite the exhausted budget" "yes" \
  "$([ -n "$(claims_on 810)" ] && echo yes || echo no)"
# Count THIS item's entry, not the queue's depth: a transient 502 earlier in the fixture legitimately
# queues a write too (any write the board refuses is queued — only an off-board item is dropped).
assert_eq "#418: the deferred Status write is QUEUED" "1" \
  "$(grep -c '"ref":"FS.GG.SDD#810","field":"Status"' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"
assert_eq "#418: ...and no board mutation was reported as done" "0" \
  "$(grep -c '^item-edit' "$GH_LOG" || true)"

# #421 (the #266 class, folded in here because it is the same defect and the same lines): a lookup
# that FAILED must never be reported as a lookup that found nothing. With the budget exhausted,
# `item-id`/`set-field` used to print "issue … is not on board 'Coordination' — add it first: gh
# project item-add …" for an issue that WAS on the board — a confident absence, complete with a
# remediation that would have created a DUPLICATE item. The read failing and the item being absent are
# different facts, and only the second may be reported.
itemid_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 bash "$COORD" item-id 'FS.GG.SDD#42' --refresh 2>&1 || true)"
case "$itemid_rl" in
  *"not on board"*|*"item-add"*)
    bad "#421: a rate-limited item-id must NOT be reported as 'not on board'" "$itemid_rl" ;;
  *) ok "#421: a rate-limited item-id must NOT be reported as 'not on board'" ;;
esac
assert_contains "#421: ...it names the real cause (the exhausted budget)" "GraphQL budget EXHAUSTED" "$itemid_rl"
rc_itemid=0
PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 bash "$COORD" item-id 'FS.GG.SDD#42' --refresh >/dev/null 2>&1 || rc_itemid=$?
assert_eq "#421: ...and exits EX_RATE (75), not the off-board code (3)" "75" "$rc_itemid"



# (1) the exit code a worker loop backs off on — 75 (EX_TEMPFAIL), from `take`, not a generic 1.
rc_take=0
PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
  env GH_RATELIMIT=1 bash "$COORD" --worker vole-418 take --repo FS.GG.SDD >/dev/null 2>&1 || rc_take=$?
assert_eq "#418: an exhausted budget exits EX_RATE (75), the back-off signal" "75" "$rc_take"

# (3) flush replays the queue once the budget is back — and the write REALLY lands on the board.
#
# THE DRAIN MUST BE OBSERVED, NOT INFERRED FROM AN ABSENCE (#436). This assertion used to read the
# queue depth as `wc -l <pending.jsonl 2>/dev/null || echo 0` and compare it to 0 — which is green
# when the queue was drained AND green when the file never existed, never was written, or lived at a
# different path. `flush` UNLINKS the file when it empties it, so the absent case is the one that
# actually occurs: the assertion could not fail. That is epic #266's own shape — a coherence check
# that reports green on a missing subject — guarding the mechanism that stops an exhausted budget
# from silently dropping a board write. So: depth is ABSENT or a number (never conflated), the queue
# is proven NON-EMPTY first, and the flush is made to account for exactly what was in it.
pending_depth() {   # ABSENT | <count> — the two facts the old `|| echo 0` collapsed into one
  local f="$FSGG_COORD_CACHE/pending.jsonl"
  if [ -f "$f" ]; then wc -l <"$f" | tr -d ' '; else echo ABSENT; fi
}
depth_before="$(pending_depth)"
case "$depth_before" in
  ABSENT|0) bad "#418: the queue holds the deferred write(s) BEFORE flush" \
              "depth=$depth_before — nothing was queued, so the drain below would prove nothing" ;;
  *)        ok  "#418: the queue holds the deferred write(s) BEFORE flush ($depth_before queued)" ;;
esac
: >"$GH_LOG"
flush_out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" flush 2>&1 || true)"
assert_contains "#418: flush replays the queued board write" "written" "$flush_out"
assert_contains "#418: ...as a real Status mutation" "PVTSSF_status" "$(cat "$GH_LOG")"
# Every queued write is accounted for — not just "at least one was written".
assert_contains "#418: ...and accounts for EVERY queued write" "$depth_before written" "$flush_out"
# ...and the drained queue is UNLINKED. Distinguishing this from "0 lines" is the whole point: only
# one of the two can be produced by a flush that ran, and it is this one.
assert_eq "#418: ...leaving the queue drained and UNLINKED (not merely unreadable)" \
  "ABSENT" "$(pending_depth)"

# ================================================================================================
# #510 — THE PROMISE THAT WAS ONLY TRUE FOR `claim`. Every one of the assertions above is about
# `claim`, and `claim` was the ONLY command that called defer_write. Every OTHER board write —
# `set-field` (which the recipes drive several times in a row when filing a finding), `done --flip`,
# `release`, `reap` — fell through to the shared EX_RATE handler, which prints:
#
#     "... Board WRITES are queued: see `fsgg-coord flush`."
#
# and queued NOTHING. The write was gone, and the message told the worker it was safe — so the worker
# did the correct thing, trusted it, carried on, and the finding they had just filed landed on the
# board with no Status, no Repo Scope and no Phase. `flush` then reported "nothing pending" and
# CONFIRMED the lie. (Family: epic #416 — a surface that runs, reports success, and does nothing.)
#
# There is now exactly ONE board write (`board_write`) and it queues. These assertions are the
# reproduction from the issue, verbatim: two `set-field` calls on an exhausted budget, then `flush`.

seed_issue 811 "Rate-limited set-field" "src/Rl811/**"
rm -f "$FSGG_COORD_CACHE/pending.jsonl"

sf_rl_rc=0
sf_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 \
  bash "$COORD" set-field 'FS.GG.SDD#811' Contract 'fs-gg-ui-template' 2>&1)" || sf_rl_rc=$?
assert_eq "#510: a rate-limited set-field exits EX_RATE (75), the back-off signal" "75" "$sf_rl_rc"
assert_contains "#510: ...and SAYS the write is queued" "QUEUED" "$sf_rl"
# THE BUG, in one assertion: the message promised a queue and the queue was empty.
assert_eq "#510: ...and the write is ACTUALLY in the queue — the promise is kept, not printed" "1" \
  "$(grep -c '"ref":"FS.GG.SDD#811","field":"Contract"' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"

# The recipe drives several in a row (cross-repo-coordination + pnext-item §4 both do). Every one of
# them must survive, not just the first — the worker filing a finding sets Status, Repo Scope, Phase.
PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 \
  bash "$COORD" set-field 'FS.GG.SDD#811' Status Backlog >/dev/null 2>&1 || true
assert_eq "#510: a SECOND recipe-driven write queues too (a finding sets 3 fields, not 1)" "2" \
  "$(pending_depth)"

# ...but ONLY a transient failure is queued. A REFUSED write — an unknown field, an unknown option, a
# `Blocked by` that is not a ref — must NEVER be queued: replaying it could not succeed on the tenth
# attempt either, and the queue would carry it forever while swallowing the refusal that says why.
# This is the trap the first cut of the fix fell into, so it is pinned here.
refused_rc=0
refused="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw \
  bash "$COORD" set-field 'FS.GG.SDD#811' 'No Such Field' x 2>&1)" || refused_rc=$?
assert_eq "#510: a REFUSED write is not EX_RATE — it is a plain error" "1" "$refused_rc"
# Assert the REFUSED write's absence from the queue directly, not via the queue's depth: this call
# runs with the budget restored, so `autoflush` legitimately drains the two writes queued above
# first. Depth would then be green for the wrong reason — the #436 shape, and the very trap this
# fixture already calls out one section up.
assert_eq "#510: ...and is NOT queued (replaying it would never succeed)" "0" \
  "$(grep -c 'No Such Field' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"
assert_contains "#510: ...and the refusal REACHES the worker, naming the fields that do exist" \
  "no field named" "$refused"
# The diagnostic itself used to die: the jq filter was written `join(\", \")` inside a $( ) — the
# backslashes survive, jq gets a syntax error, and the message that explains the refusal IS the
# refusal. A diagnostic that cannot render is a diagnostic that does not exist.
case "$refused" in *"jq: error"*) bad "#510: the refusal message must RENDER, not throw a jq syntax error" "$refused" ;;
                   *)             ok "#510: the refusal message must RENDER, not throw a jq syntax error" ;; esac

# `done --flip` is the other silent dropper: it flipped Status and, on an exhausted budget, said
# "board: Done" over a write that never landed. The stamp stays GREEN (the work IS merged and done —
# a red stamp on correct work is how a red stamp becomes noise, #558), but the board note must not
# claim Done, and the exit must be EX_RATE so a loop backs off and flushes.
done_rl_rc=0
done_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 env GH_RATELIMIT=1 \
  bash "$COORD" done 'FS.GG.SDD#801' --flip 2>&1)" || done_rl_rc=$?
case "$done_rl" in
  *"board: Done"*) bad "#510: done --flip must NOT report 'board: Done' over a write it only queued" "$done_rl" ;;
  *)               ok  "#510: done --flip must NOT report 'board: Done' over a write it only queued" ;;
esac
assert_eq "#510: ...it exits EX_RATE (75) so the loop backs off and flushes" "75" "$done_rl_rc"

# `release` restores the column the claim overwrote (#481). On an exhausted budget it used to report
# "not on board" — #418's exact misdiagnosis, still live here — and DROP the restore, stranding the
# item in the column the claim overwrote with no claim on it. It now names the real cause.
#
# NOTE the leg this does NOT cover: under exhaustion `release` fails at the *read* of the current
# Status, before it ever reaches a write, so what it reports is "could not read it" and the restore is
# still not queued. That is a real residue — the WRITE path is fixed here, the READ path is a separate
# defect — and it is honest rather than false, which is the bar this issue sets. Do not read the
# passing assertion below as "release under exhaustion restores the column"; it does not.
rel_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 env GH_RATELIMIT=1 \
  bash "$COORD" --worker vole-418 release 'FS.GG.SDD#810' 2>&1 || true)"
case "$rel_rl" in
  *"not on board"*) bad "#510: a rate-limited release must NOT be misreported as 'not on board'" "$rel_rl" ;;
  *)                ok  "#510: a rate-limited release must NOT be misreported as 'not on board'" ;;
esac
# Whatever it says, it must not ASSERT a board column it did not write.
case "$rel_rl" in *"board: Ready"*|*"board: Backlog"*)
    bad "#510: release must not claim a restore it never performed" "$rel_rl" ;;
  *) ok "#510: release must not claim a restore it never performed" ;; esac

rm -f "$FSGG_COORD_CACHE/pending.jsonl"

# (4) THE SCAN CACHE — the lever that stops the budget running out in the first place. Two `next`
# calls inside the TTL must cost ONE board scan, because the second serves the first's scan from the
# shared (user-level, therefore cross-worker) cache. This is the assertion that would have caught the
# original burn: five workers looping `next` were paying five full scans a round.
rm -f "$FSGG_COORD_CACHE"/scan-*.json
before_scan="$(gcount)"
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" next --repo FS.GG.SDD >/dev/null 2>&1 || true
mid_scan="$(gcount)"
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" next --repo FS.GG.SDD >/dev/null 2>&1 || true
after_scan="$(gcount)"
assert_eq "#418: the first 'next' pays for the board scan" "1" "$((mid_scan - before_scan))"
assert_eq "#418: a second 'next' inside the TTL spends ZERO GraphQL (the shared scan cache)" \
  "0" "$((after_scan - mid_scan))"

# ...and --fresh must still be able to buy the truth. A cache you cannot bypass is a cache you cannot
# trust — `take`'s retry-after-a-lost-race depends on this.
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" next --repo FS.GG.SDD --fresh >/dev/null 2>&1 || true
assert_eq "#418: --fresh bypasses the cache and rescans" "1" "$(( $(gcount) - after_scan ))"
# The line the TTL is drawn on: RECONCILING reads never serve a cached board. `ready` is what
# /check-board snapshots, and a reconciler that reports yesterday's drift is worse than none.
before_ready_fresh="$(gcount)"
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" ready --repo FS.GG.SDD >/dev/null 2>&1 || true
assert_eq "#418: 'ready' (a TRUTH read) always scans fresh — it never serves the cache" \
  "1" "$(( $(gcount) - before_ready_fresh ))"

# ================================================================================================
# #587 — `add`: THE VERB WHOSE ABSENCE MADE THE MONOPOLY UNENFORCEABLE
# ================================================================================================
# Every recipe said `gh project item-add`, and so did this tool's own "not on board" message — so a
# worker COULD NOT put an item on the board without reaching past the client, onto a GraphQL budget
# the whole fleet shares and nothing else meters, caches, or queues against. A rule you cannot obey
# is not a rule, it is a reprimand: the monopoly needed this verb before it could need a gate.
#
# NOTE the placement: these run AFTER the #418 block, not inside it. `add` calls `autoflush` like
# every other board-writing command, so running it mid-#418 drains the very queue that section is
# about to assert on — which is how the first cut of these tests broke four assertions above.

# The tool must no longer PRESCRIBE the bypass it exists to replace.
offboard_msg="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_OFFBOARD_ITEM=777 bash "$COORD" item-id 'FS.GG.SDD#777' --refresh 2>&1 || true)"
case "$offboard_msg" in
  *"gh project item-add"*) bad "#587: the tool must not prescribe the bypass it is meant to replace" "$offboard_msg" ;;
  *) ok "#587: the tool must not prescribe the bypass it is meant to replace" ;;
esac
assert_contains "#587: ...it names its own verb instead" "fsgg-coord add" "$offboard_msg"

# An issue NOT on the board is added, and the new item id is printed.
: >"$GH_LOG"
add_out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_OFFBOARD_ITEM=777 bash "$COORD" add 'FS.GG.SDD#777' 2>&1 || true)"
assert_contains "#587: add puts an off-board issue ON the board" "added FS.GG.SDD#777" "$add_out"
assert_eq "#587: ...with exactly one item-add call" "1" "$(grep -c '^item-add' "$GH_LOG" || true)"

# IDEMPOTENT. N parallel workers running the file-a-finding recipe would otherwise each create a
# board item for the same issue — #464's shape, one layer down.
: >"$GH_LOG"
again="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" add 'FS.GG.SDD#42' 2>&1 || true)"
assert_contains "#587: add is IDEMPOTENT — an item already on the board is not added twice" \
  "already on board" "$again"
assert_eq "#587: ...and spends no item-add" "0" "$(grep -c '^item-add' "$GH_LOG" || true)"

# A FAILED LOOKUP IS NOT AN ABSENCE (#421, one verb along). On an exhausted budget the "is it already
# there?" read cannot run — and adding on a read that FAILED is exactly how you create a duplicate
# board item. It must REFUSE, not add.
: >"$GH_LOG"
add_rl_rc=0
PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 bash "$COORD" add 'FS.GG.SDD#820' >/dev/null 2>&1 || add_rl_rc=$?
assert_eq "#587: a rate-limited add REFUSES rather than adding on a failed lookup (#421)" \
  "0" "$(grep -c '^item-add' "$GH_LOG" || true)"
assert_eq "#587: ...and exits EX_RATE (75), the back-off signal" "75" "$add_rl_rc"

# ================================================================================================
# #520 / #485 RESIDUE — THE SURVIVING DISAGREEMENTS
# ================================================================================================
# #485's fix landed (`batch` is THE predicate; `next` and `take` delegate to it). But consolidating
# five copies into one only helps if the one is RIGHT, and three disagreements outlived the merge.

# ---- 1. A CLOSED ISSUE IS NOT SCHEDULABLE (#520). The sixth disagreement. ------------------------
# Candidate selection read the board `Status` column and NOTHING ELSE. `board_items` has carried
# `.state` all along; nothing ever read it. So an issue whose column was never flipped to `Done`
# stayed a candidate FOREVER — FS.GG.Rendering#502 was handed to a worker two hours after it was
# closed as completed, and #481's restore-the-column logic then promoted it Backlog -> Ready on
# release, re-arming it for the next one. `lint` always knew (`select(.state == "OPEN")`); the
# scheduler simply never asked.
b520="$(run batch --repo FS.GG.Rendering --json 2>/dev/null || true)"
case "$b520" in
  *502*) bad "#520: a CLOSED issue must NOT be schedulable, however the board column reads" "$b520" ;;
  *)     ok  "#520: a CLOSED issue must NOT be schedulable, however the board column reads" ;;
esac
# ...and it must SAY so, not drop it silently. A queue that shrinks without explanation is #440.
b520r="$(run batch --repo FS.GG.Rendering 2>&1 || true)"
assert_contains "#520: ...and the reason names the issue state, not a mystery" \
  "the issue is closed" "$b520r"
# The board column is a PROJECTION; the issue is the WORK. `ready` must still SHOW it — only the
# SCHEDULER refuses to hand it out — because /check-board is what reconciles the column.
r520="$(run ready --repo FS.GG.Rendering 2>/dev/null || true)"
assert_contains "#520: ...but `ready` still SHOWS it, so /check-board can reconcile the column" \
  "502" "$r520"

# ---- 2. A MERGED BLOCKER IS RESOLVED — IN EVERY SURFACE, NOT JUST `.blocked` (#476). -------------
# #476 taught `board_annotate` that a `Blocked by` naming a PR clears on CLOSED *or* MERGED. Two
# copies of the pre-#476 rule survived: `board_table`'s BLOCKED BY column and `take`'s "a BLOCKED
# queue, not an empty one" diagnostic. So `.blocked` said false while the display and the diagnostic
# both named a MERGED pull request as the reason — sending a worker to go and look at FINISHED work.
# There is ONE definition now (JQ_BLOCKER_RULE) and every site consumes it.
r476="$(run ready --repo FS.GG.Templates 2>/dev/null || true)"
merged_row="$(printf '%s\n' "$r476" | grep '350' || true)"
case "$merged_row" in
  *"#449"*) bad "#476: a MERGED blocker must not be listed as still blocking (board_table)" "$merged_row" ;;
  *)        ok  "#476: a MERGED blocker must not be listed as still blocking (board_table)" ;;
esac
# ...and the item is genuinely schedulable, not merely displayed as unblocked.
b476="$(run batch --repo FS.GG.Templates --json 2>/dev/null || true)"
assert_contains "#476: ...and the item it blocked is startable again" "350" "$b476"

# ---- 3. `lint` WAS GREEN OVER AN ITEM `batch` WILL NEVER SCHEDULE (#496, reopened). --------------
# `touchset_decl` asked only whether a `Paths:` line EXISTED — it never applied the grammar. So an
# item whose every token is unmatchable (`**/packages.lock.json`: a leading `**/` matches nothing,
# and a token that matches nothing CONFLICTS with nothing) was skipped forever by `batch` while the
# one surface whose job is board health reported `0 error(s)`. That is #496's own defect, reopened
# for the unmatchable case, and it is why the rule is now shared rather than re-spelled.
lint31="$(run lint --repo FS.GG.Audio --json 2>/dev/null || true)"
assert_contains "#496: lint goes RED on an item whose every touch-set token is unmatchable" \
  "BAD-TOUCH-SET" "$lint31"
assert_contains "#496: ...and names the token, and the grammar" "packages.lock.json" "$lint31"
# The two deaths are DIFFERENT deaths, and must not be conflated: one declared nothing, one declared
# something unusable. #496's whole point was making the distinction machine-readable.
assert_eq "#496: ...and it is NOT reported as NO-TOUCH-SET — a declaration was made, it is just dead" \
  "0" "$(printf '%s' "$lint31" | jq -r '[.[] | select(.id | test("#31")) | select(.code == "NO-TOUCH-SET")] | length' 2>/dev/null || echo 0)"
# ...and a WELL-FORMED touch-set stays green. Without this the assertion above is satisfied by a lint
# that simply reds everything.
lint_ok="$(run lint --repo FS.GG.Templates --json 2>/dev/null || true)"
case "$lint_ok" in
  *BAD-TOUCH-SET*) bad "#496: a well-formed touch-set must NOT be flagged" "$lint_ok" ;;
  *)               ok  "#496: a well-formed touch-set must NOT be flagged" ;;
esac

# ================================================================================================
# #440 — A FULL QUEUE MUST NOT READ AS AN EMPTY ONE. `next` picks "the first Ready, else the first
# Backlog"; `take` stopped at Ready. So on a board with zero Ready items and startable Backlog ones —
# which is what `.github` looked like — `take` reported:
#
#   no schedulable item — every candidate is blocked, claimed, overlapping, or undeclared.
#
# Every clause of that was false, and the true reason (the item is in Backlog) was not among them. A
# worker that believes the message idles in front of work it could have taken. The two commands a
# worker treats as "what do I do next" must agree about what EXISTS, and neither may name a cause it
# did not observe.
bl() { PATH="$STUB:$PATH" GH_BOARD_SET=bl GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
         bash "$COORD" --worker crake-440 "$@"; }
seed_issue 520 "Startable, but merely Backlog" "src/Bl520/**"
seed_issue 521 "Backlog, and undeclared" ""

take440="$(bl take --repo FS.GG.SDD 2>&1 || true)"
assert_contains "#440: take falls back to Backlog when no Ready item exists" \
  "claimed FS.GG.SDD#520" "$take440"
assert_contains "#440: ...and SAYS the pick came from Backlog (it is not a Ready item)" \
  "from Backlog" "$take440"
assert_eq "#440: ...and the claim marker really landed" "crake-440" "$(workers_on 520)"
# The undeclared Backlog item is still refused — the fallback widens WHICH statuses are reachable,
# not what counts as schedulable. An item with no touch-set can still not be scheduled.
case "$take440" in
  *"#521"*) bad "#440: an undeclared item must NOT become schedulable via the Backlog fallback" "$take440" ;;
  *)        ok  "#440: an undeclared item must NOT become schedulable via the Backlog fallback" ;;
esac

# Now the empty case: with #520 claimed, only the undeclared #521 remains. `take` must report the
# reason BATCH observed, per item — not recite a fixed list of causes.
take440b="$(bl take --repo FS.GG.SDD 2>&1 || true)"
assert_contains "#440: an empty queue names the REAL per-item reason" \
  "#521 — no 'Paths:' declared" "$take440b"
case "$take440b" in
  *"every candidate is blocked, claimed, overlapping, or undeclared"*)
    bad "#440: take must not recite a guessed list of causes" "$take440b" ;;
  *) ok "#440: take must not recite a guessed list of causes" ;;
esac
# ...and `batch --json` must EMIT those reasons on stderr, or `take` has nothing true to relay. This
# is the assertion that keeps the two halves of the fix from drifting apart.
berr440="$(bl batch --repo FS.GG.SDD --include-backlog -n 1 --json 2>&1 >/dev/null || true)"
assert_contains "#440: batch --json still reports 'passed over' reasons on stderr" \
  "no 'Paths:' declared" "$berr440"

# ---- #461: the claim scan FAILS CLOSED — an unreadable lock is never an empty lock --------------
# The fail-open lived on the LOCK ITSELF. If the claim-candidate read came back as bytes that are not
# JSON (a truncated page, a proxy error body, a 5xx rendered as text — `gh` EXITS 0), then `$cand` was
# the empty string, `jq 'length'` printed nothing AND EXITED 0 (so `set -euo pipefail` never fired),
# `n` was "", `[ 0 -lt "" ]` errored with `integer expected`, the loop body never ran, and
# `active_claims` returned `[]` — a failed read wearing an empty set's clothes.
#
# THESE ARE FAILURE-LEG ASSERTIONS, and they are the whole point (#266): a happy-path test passes
# against the BROKEN code and proves nothing. Each one below asserts a REFUSAL — that the command
# declines to act on a lock it could not read, rather than proceeding as if nothing were claimed.
# Seed a REAL live claim first, so "nothing is claimed" is a provably WRONG answer, not a vacuous one.
mal() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_ISSUE_LIST_MALFORMED=1 \
          bash "$COORD" --worker "$1" "${@:2}"; }

# `who` must not answer "nothing is in flight" off a scan that did not succeed.
rc_who461=0
who461="$(mal kite-461 who --repo FS.GG.SDD 2>&1)" || rc_who461=$?
case "$who461" in
  *"nothing is in flight"*) bad "#461: who must NOT report 'nothing is in flight' off a failed scan" "$who461" ;;
  *) ok "#461: who must NOT report 'nothing is in flight' off a failed scan" ;;
esac
[ "$rc_who461" -ne 0 ] \
  && ok "#461: ...it exits NON-ZERO (fails closed)" \
  || bad "#461: ...it exits NON-ZERO (fails closed)" "rc=$rc_who461"
assert_contains "#461: ...and NAMES the unreadable read" "malformed" "$who461"

# `take` must not schedule an item off a claim set it could not read — that is the double-book.
rc_take461=0
take461="$(mal kite-461 take --repo FS.GG.SDD 2>&1)" || rc_take461=$?
[ "$rc_take461" -ne 0 ] \
  && ok "#461: take REFUSES to schedule off an unreadable claim set" \
  || bad "#461: take REFUSES to schedule off an unreadable claim set" "rc=0: $take461"
case "$take461" in
  *"claimed "*) bad "#461: ...and claims NOTHING (no double-book)" "$take461" ;;
  *) ok "#461: ...and claims NOTHING (no double-book)" ;;
esac

# `widen` is the guard a worker TRUSTS before editing shared files. A confident DISJOINT off a failed
# scan sends two workers into the same paths. It must refuse instead.
rc_widen461=0
widen461="$(mal kite-461 widen 'FS.GG.SDD#74' --paths 'src/Audio/**' 2>&1)" || rc_widen461=$?
case "$widen461" in
  *DISJOINT*) bad "#461: widen must NEVER report DISJOINT off a failed claim scan" "$widen461" ;;
  *) ok "#461: widen must NEVER report DISJOINT off a failed claim scan" ;;
esac
[ "$rc_widen461" -ne 0 ] \
  && ok "#461: ...and exits non-zero rather than blessing the touch-set" \
  || bad "#461: ...and exits non-zero rather than blessing the touch-set" "rc=0"

# The GUARD MUST NOT FIRE ON A LEGITIMATE EMPTY SET. `n=0` (a real, successful scan that found no
# claims) is a valid answer and must still be reported as "nothing is in flight" — otherwise the fix
# is just a different fail-closed bug, refusing to work on a healthy, idle repo.
who461ok="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 bash "$COORD" \
              --worker kite-461 who --repo FS.GG.Rendering 2>&1 || true)"
case "$who461ok" in
  *"nothing is in flight"*|*WORKER*) ok "#461: a SUCCESSFUL scan with no claims still reports an empty set" ;;
  *) bad "#461: a SUCCESSFUL scan with no claims still reports an empty set" "$who461ok" ;;
esac

# ---- #469 / #563 / #588: the kit-digest obligation is OBSERVED, not inferred from a declaration ---
#
# `repos.lock` pins a content digest of every kit source (ADR-0019, #527). Editing one invalidates it
# and reds `main` — and the obligation was invisible, because `verify-paths` only asks "did the PR stay
# INSIDE what you declared", never "was your declaration SUFFICIENT for what you touched" (#465/#469).
#
# The FIRST fix asked a question it could not answer: "is `registry/repos.yml` in your touch-set?" —
# and called the obligation MET if it was. #527 then moved the digests out of the authored `repos.yml`
# into the generated `repos.lock`, and the warning did not move with it. So:
#
#   * it FAILED OPEN — declare `repos.yml` and the warning went silent while `repos.lock` was still
#     stale and `main` was still red. Mute exactly where it was needed (#563; epic #266's shape).
#   * its ADVICE BROKE #309 — it told you to reserve `repos.yml` (the three-worker deadlock #527 was
#     merged to REMOVE, #428) and to run `repos.sh digest`, which still exists and now writes nothing.
#
# The old fixture asserted that fail-open AS A FEATURE ("declaring registry/repos.yml must SILENCE the
# warning"). It is gone. A DECLARATION is not the obligation; a MATCHING DIGEST is — so the tool now
# recomputes the digest and looks, and these assertions stand a tree up and make it genuinely stale.

KITROOT="$WORK/kitroot"
mkdir -p "$KITROOT/.claude/skills/pnext-item" "$KITROOT/.agents/skills/pnext-item" \
         "$KITROOT/scripts" "$KITROOT/registry"
kit_seed() {   # (re)write the tree and relock it, so the lock is HONEST before each scenario
  printf 'skill body v1\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
  cp "$KITROOT/.claude/skills/pnext-item/SKILL.md" "$KITROOT/.agents/skills/pnext-item/SKILL.md"
  printf '#!/usr/bin/env bash\n# client v1\n' >"$KITROOT/scripts/fsgg-coord"
  {
    printf '# registry/repos.lock — GENERATED.\n'
    printf '%s  .claude/skills/pnext-item\n' "$(sha256sum "$KITROOT/.claude/skills/pnext-item/SKILL.md" | cut -d' ' -f1)"
    printf '%s  scripts/fsgg-coord\n'        "$(sha256sum "$KITROOT/scripts/fsgg-coord" | cut -d' ' -f1)"
  } >"$KITROOT/registry/repos.lock"
}
kd() { FSGG_KIT_ROOT="$KITROOT" PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
         bash "$COORD" --worker kite-469 "$@"; }

# THE NEGATIVE CONTROL FIRST, because it is the one that can silently rot: a tree whose lock MATCHES
# must produce NO warning. If this ever goes green by accident (a broken root, an unreadable lock),
# every positive assertion below is vacuous.
kit_seed
w_clean="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord' 2>&1 || true)"
case "$w_clean" in
  *"KIT DIGEST"*) bad "#563: a lock that MATCHES must not warn — the obligation is met" "$w_clean" ;;
  *) ok "#563: a lock that MATCHES must not warn — the obligation is met" ;;
esac

# (1) A STALE CLIENT digest is observed and named — regardless of what the touch-set declares.
kit_seed; printf '# client v2 — edited\n' >>"$KITROOT/scripts/fsgg-coord"
w469="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord, tests/fsgg-coord/run.sh' 2>&1 || true)"
assert_contains "#469: widen NAMES a kit source whose digest is now STALE" "KIT DIGEST" "$w469"
assert_contains "#469: ...and prints the CURRENT regenerate command" "repos.sh relock" "$w469"
assert_contains "#469: ...and says which gate will red main" "repos-registry-selftest" "$w469"
# The post-#527 rule, in the advice itself: repos.lock is generated + CI-gated, so it must NOT be
# reserved. Telling a worker to declare it is telling them to re-create #428.
assert_contains "#469: ...and says NOT to reserve the generated lock (#309/#527)" \
  "do NOT reserve it" "$w469"
case "$w469" in
  *"repos.sh digest"*) bad "#588: the advice must not name \`repos.sh digest\` — it writes nothing now" "$w469" ;;
  *) ok "#588: the advice must not name \`repos.sh digest\` — it writes nothing now" ;;
esac
# ...and it still widens. Advisory, never fatal: `repos-registry-selftest` is the authority.
assert_contains "#469: ...while STILL widening (advisory, not fatal)" "widened FS.GG.SDD#74" "$w469"

# (2) THE FAIL-OPEN, PINNED. Declaring `registry/repos.yml` used to SILENCE this. It must not: the
#     lock is still stale, and main is still red. This is the assertion #563 exists for.
w_yml="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord, registry/repos.yml' 2>&1 || true)"
assert_contains "#563: declaring registry/repos.yml must NOT silence a genuinely stale lock" \
  "KIT DIGEST" "$w_yml"

# (3) A STALE SKILL digest is observed too — the coupling is not client-specific.
kit_seed; printf 'skill body v2\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
cp "$KITROOT/.claude/skills/pnext-item/SKILL.md" "$KITROOT/.agents/skills/pnext-item/SKILL.md"
w469s="$(kd widen 'FS.GG.SDD#74' --paths '.claude/skills/pnext-item/**' 2>&1 || true)"
assert_contains "#469: a SKILL source is content-addressed too, and is named" \
  ".claude/skills/pnext-item" "$w469s"

# (4) SKILL ROOTS — the byte-identical union (ADR-0011/0014) is OBSERVED, not inferred. Edit one root
#     and not the other: the `roots` gate reds main, and the tool must say so. Previously this hung off
#     the digest gap's early return, so declaring `repos.yml` suppressed BOTH obligations at once.
kit_seed; printf 'skill body v2 — one root only\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
w_roots="$(kd widen 'FS.GG.SDD#74' --paths '.claude/skills/pnext-item/**' 2>&1 || true)"
assert_contains "#563: diverged skill roots are NAMED" "SKILL ROOTS" "$w_roots"
assert_contains "#563: ...with the mirror command that fixes it" ".agents/skills/pnext-item" "$w_roots"
# ...and a CLIENT kit has no mirror, so a client-only staleness must NOT nag about roots.
kit_seed; printf '# client v2\n' >>"$KITROOT/scripts/fsgg-coord"
w_client="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord' 2>&1 || true)"
case "$w_client" in
  *"SKILL ROOTS"*) bad "#469: a CLIENT kit must NOT be told to mirror skill roots" "$w_client" ;;
  *) ok "#469: a CLIENT kit must NOT be told to mirror skill roots" ;;
esac

# (5) No lock to read — a RECEIVER repo mirrors the kit but not the registry. Stay silent rather than
#     nagging every worker in every downstream repo about a file they do not have.
w469r="$(FSGG_KIT_ROOT="$WORK/no-such-root" PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
           bash "$COORD" --worker kite-469 widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord' 2>&1 || true)"
case "$w469r" in
  *"KIT DIGEST"*) bad "#469: no lock to read -> silent (receiver repos have no registry)" "$w469r" ;;
  *) ok "#469: no lock to read -> silent (receiver repos have no registry)" ;;
esac
kit_seed

# ================================================================================================
# THE CLAIM SCAN MUST NOT TRAVEL THROUGH `argv` (FS-GG/.github#497)
# ================================================================================================
# `active_claims` reads each candidate's full BODY on purpose — arm B carries it so a touch-set costs
# zero extra reads. It then used to pass that whole set back through the jq COMMAND LINE, both to
# accumulate arm B repo-by-repo and to merge the two arms. On Linux a SINGLE argument is capped at
# MAX_ARG_STRLEN (128 KiB) — independently of the far larger total ARG_MAX — so past that, `execve`
# returns E2BIG, jq never runs, the `$( )` yields the EMPTY STRING, and the next loop iteration feeds
# it back as `--argjson acc ''` ("invalid JSON text").
#
# This is not a corner case: the org crossed 128 KiB of open-issue bodies in July 2026 and EVERY
# claim-aware read — who, reap, batch, take, inbox, widen, overlap --active — died at once. `take`
# could not schedule, so no worker could pick up work through the protocol at all. It failed CLOSED
# (#461's guard refused to report the empty claim set as "nobody holds anything"), so it was a loud
# outage rather than a double-claim — but an outage that no amount of waiting would clear.
#
# The fixture therefore serves a candidate set BIGGER THAN THE CAP and asserts the scan still reads
# it. The size assertion below is load-bearing: if a later edit shrinks these bodies under 128 KiB,
# the test would still pass while no longer exercising the bug, which is worse than not having it.
echo "--- .github#497: a claim scan larger than MAX_ARG_STRLEN is still readable ---"

ARG_CAP=131072                      # MAX_ARG_STRLEN: the per-argument ceiling, not ARG_MAX
seed_fat_issue() {                  # <num> <body-bytes> — an open, chatty issue with a BIG body
  local n="$1" bytes="$2" repo="FS-GG/FS.GG.Audio" filler body
  # Each body stays well under GitHub's 65,536-CHARACTER cap, so it is the ACCUMULATED set — not any
  # one issue — that breaches MAX_ARG_STRLEN here. That is the outage this section pins. (A single
  # body CAN breach it on its own once the characters are multi-byte: 65,536 CJK chars is ~196 KB.
  # A different defect, on a per-item argv path this fix does not touch — filed as #507.)
  filler="$(head -c "$bytes" /dev/zero | tr '\0' 'x')"
  body="Paths: src/Fat$n/**

$filler"
  jq -n --argjson n "$n" --arg t "fat body $n" --arg b "$body" --arg r "$repo" \
    '{id:($n + 1000), number:$n, title:$t, body:$b, assignees:[], state:"open", repo:$r,
      html_url:("https://github.com/" + $r + "/issues/" + ($n|tostring))}' >"$STORE/issue-$n.json"
  echo '[]' >"$STORE/comments-$n.json"
}
fat() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }

for n in 530 531 532; do seed_fat_issue "$n" 50000; done
# `open_claim_candidates` prunes `comments == 0` — a claim marker IS a comment, so a silent issue can
# hold no lock. These must therefore be CHATTY to enter the candidate set (and carry their bodies in
# with them): #530 gets a real claim marker, #531/#532 merely get talked at, exactly as a live board
# looks. Route both through the tool so the comment schema cannot drift from the real one.
fat --worker kite-497 claim 'FS-GG/FS.GG.Audio#530' >/dev/null 2>&1 || true
for n in 531 532; do
  fat --worker kite-497 say "FS-GG/FS.GG.Audio#$n" --to '*' 'chatter, no marker' >/dev/null 2>&1 || true
done
# Assert the SEED landed before asserting anything about the scan. Without this, a regression in
# `claim` shows up below as `expected='kite-497' actual=''` — which reads as a broken claim SCAN and
# sends the next reader into the wrong function entirely.
assert_eq "#497: (fixture) the marker seeded onto the fat issue" "kite-497" "$(workers_on 530)"

# The fixture really is over the cap — otherwise everything below is vacuous.
fatbytes="$(jq -c -s '[.[] | {number, title, url: .html_url, body}]' \
              "$STORE"/issue-530.json "$STORE"/issue-531.json "$STORE"/issue-532.json | wc -c)"
if [ "$fatbytes" -gt "$ARG_CAP" ]; then
  ok "#497: the fixture candidate set really exceeds MAX_ARG_STRLEN ($fatbytes > $ARG_CAP bytes)"
else
  bad "#497: the fixture candidate set must EXCEED $ARG_CAP bytes or it tests nothing" "$fatbytes"
fi

# The scan reads it. Pre-fix this died with `Argument list too long` / `invalid JSON text passed to
# --argjson`, and #461's guard turned that into a hard `cannot read the claim set`.
fatwho="$(fat who --repo FS-GG/FS.GG.Audio --json 2>&1 || true)"
case "$fatwho" in
  *"cannot read the claim set"*|*"Argument list too long"*|*"--argjson"*)
    bad "#497: a claim set over the arg cap must still be READ, not die" "$fatwho" ;;
  *) ok "#497: a claim set over the arg cap must still be READ, not die" ;;
esac
assert_eq "#497: ...and the claim inside that oversized set is reported, with its holder" \
  "kite-497" "$(printf '%s' "$fatwho" | jq -r '.[] | select(.number==530) | .worker' 2>/dev/null)"
# The scan stays HONEST at size: the two chatty-but-markerless issues are not in-flight work, and a
# body big enough to break the plumbing must not become a claim. Scoped to the fat fixtures — Audio
# also holds the #422/#424 overlap section's board item, which `who` reports for its own good reason.
assert_eq "#497: ...and chatty markerless issues in that set are still not claims" "[530]" \
  "$(printf '%s' "$fatwho" | jq -c '[.[] | select(.number >= 530 and .number <= 532) | .number] | sort' 2>/dev/null)"

# ---- .github#419: an id the agent INVENTS is an id the agent SHARES ------------------------------
# ADR-0027 moved the lock off the shared GitHub account and onto a worker id. #419 is that same bug
# one level down: `whoami` warned "set FSGG_WORKER explicitly", agents obliged, and eight live workers
# drew from four of the twenty words — two of them independently picking the suffix `7c2`. A lock
# whose key two workers can both hold is not a lock. Two defences, tested here:
#   1. mint, don't invent — an executable remedy, so no literal is there to copy; and
#   2. `claim` refuses a marker carrying our id but a DIFFERENT session, instead of adopting it.
echo "--- .github#419: colliding worker ids ---"

# 1. The remedy is a command, and its stdout is EXACTLY one eval-able line — commentary must go to
#    stderr or `eval "$(… --mint)"` executes the prose.
mint_out="$(pw whoami --mint 2>/dev/null)"
assert_eq "#419: --mint prints exactly one line on stdout" "1" "$(printf '%s\n' "$mint_out" | wc -l | tr -d ' ')"
assert_contains "#419: ...and it is an eval-able export" "export FSGG_WORKER=" "$mint_out"
# The whole point: the id must be UNIQUE per call. `$RANDOM` alone is seeded from pid+time, so agents
# a harness fans out in one second drew the same word — hence both halves now come from /dev/urandom.
m1="$(pw whoami --mint 2>/dev/null)"; m2="$(pw whoami --mint 2>/dev/null)"; m3="$(pw whoami --mint 2>/dev/null)"
assert_eq "#419: successive mints do NOT collide" "3" \
  "$(printf '%s\n%s\n%s\n' "$m1" "$m2" "$m3" | sort -u | wc -l | tr -d ' ')"
# eval-ing it must actually name the worker — the ritual §0 now tells workers to run.
assert_eq "#419: the minted id is the one eval takes effect as" \
  "$(sed 's/^export FSGG_WORKER=//' <<<"$m1")" \
  "$(eval "$m1"; PATH="$STUB:$PATH" bash "$COORD" whoami 2>/dev/null | awk '/^worker/{print $2}')"

# 2. The warning must point at the COMMAND and name no id — a literal is what agents pattern-match on.
shared_warn="$(PATH="$STUB:$PATH" env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID -u FSGG_WORKER \
  CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064 \
  bash -c 'cd "$1" && exec bash "$2" whoami' _ "$WORK" "$COORD" 2>&1 >/dev/null)"
assert_contains "#419: the shared-id warning points at the MINT command" "whoami --mint" "$shared_warn"
assert_contains "#419: ...and tells the worker not to invent one" "do NOT invent" "$shared_warn"
assert_eq "#419: ...and offers NO literal id to copy (the old 'finch-a3f' attractor)" "0" \
  "$(grep -cE '(finch|heron|wren)-[0-9a-f]{3}' <<<"$shared_warn" || true)"

# 3. THE REGRESSION. A live marker with OUR worker id but a DIFFERENT session is a twin, not us.
#    Before #419 this landed in `mine` and the heartbeat path renewed it — "held (lease renewed)" —
#    silently putting two workers on one item. It must now refuse.
twin_ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
jq -n --arg ts "$twin_ts" '[{id:819, body:"<!-- fsgg:claim worker=heron-7c2 lease=120 harness=claude-code session=79b9e347 -->\ntheirs",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-74.json"
twin() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
           env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
           CLAUDE_CODE_SESSION_ID=ed60050b FSGG_WORKER=heron-7c2 bash "$COORD" "$@"; }
if twin claim 'FS.GG.SDD#74' >/dev/null 2>&1; then
  bad "#419: claim REFUSES a marker with our id but another session" \
      "session ed60050b adopted session 79b9e347's lock on #74 — two workers, one item"
else
  ok "#419: claim REFUSES a marker with our id but another session"
fi
twin_err="$(twin claim 'FS.GG.SDD#74' 2>&1 || true)"
assert_contains "#419: ...naming it as two workers sharing one id" "two workers share one id" "$twin_err"
assert_contains "#419: ...and reporting the OTHER session"        "79b9e347" "$twin_err"
assert_contains "#419: ...and offering the mint as the way out"   "whoami --mint" "$twin_err"
assert_eq "#419: ...and the twin's marker is left intact"  "819" "$(claims_on 74)"
assert_eq "#419: ...and NO second marker was posted"       "heron-7c2" "$(workers_on 74)"

# --force steals another WORKER's item. This is not a contested item, it is a broken identity — and
# the fix for a broken identity is a new identity. Forcing here deletes a marker our twin is working
# behind, so the refusal must survive --force.
if twin claim 'FS.GG.SDD#74' --force >/dev/null 2>&1; then
  bad "#419: --force does NOT override the twin refusal" "--force let a twin steal its own id's lock"
else
  ok "#419: --force does NOT override the twin refusal"
fi
assert_eq "#419: ...so --force left the twin's marker alone" "819" "$(claims_on 74)"

# 4. Back-compat, and the boundary of the rule. We may only conclude "twin" when BOTH sessions are
#    known. A marker with no `session=` (a human, a harness that exports none, any pre-#419 marker) is
#    genuinely indistinguishable from ours — so it keeps the old behaviour (ours, warned about) rather
#    than failing closed on old data and locking workers out of items they really do hold. #42's
#    fixture marker is exactly that: worker=finch-a3f, no session.
#    The marker is seeded HERE rather than reusing #42's: an earlier test re-claims #42, and that
#    heartbeat rewrites its marker with whatever session the AMBIENT shell exports — so on a developer's
#    machine (which exports CLAUDE_CODE_SESSION_ID) #42's marker is not sessionless by the time we get
#    here, and this assertion would fail on correct code while passing in CI. Same trap `hless` above
#    documents; state the environment explicitly rather than inheriting it.
jq -n --arg ts "$twin_ts" '[{id:820, body:"<!-- fsgg:claim worker=dunlin-9f1 lease=120 -->\nsessionless, as a human or a pre-#419 marker is",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-71.json"
assert_contains "#419: a SESSIONLESS marker with our id is still ours (heartbeat, not refusal)" \
  "lease renewed" "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
     env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
     CLAUDE_CODE_SESSION_ID=ed60050b FSGG_WORKER=dunlin-9f1 bash "$COORD" claim 'FS.GG.SDD#71' 2>/dev/null)"
# ...and the same session re-claiming its OWN marker is a heartbeat, not a twin. Without this, a
# worker could never renew its own claim — the refusal would fire on itself.
same() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
           env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
           CLAUDE_CODE_SESSION_ID=79b9e347 FSGG_WORKER=heron-7c2 bash "$COORD" "$@"; }
assert_contains "#419: the SAME session re-claiming its own marker is a heartbeat" \
  "lease renewed" "$(same claim 'FS.GG.SDD#74' 2>/dev/null)"

# ---- #488: "nothing schedulable" must be COUNTED, never inferred from an empty stderr -----------
#
# `take`'s else-branch asserted the strongest available negative — "no open board item at all — this
# is an empty queue, not a blocked one" — on the sole evidence that `batch` wrote nothing to STDERR.
# An empty stderr is not an empty board. It was #440's defect reintroduced inside #440's own fix, and
# it pointed the worker AWAY from the true cause: it does not merely omit "blocked", it rules it out.
#
# TWO distinct ways a queue starves while looking empty, and they fail differently:
#   A. every candidate is BLOCKED. `batch` used to drop blocked items BEFORE the skip-reason loop, so
#      they left no trace at all — the one state most likely to starve a queue was the one state that
#      said nothing. `batch` now reports it as a reason, which is where a reason belongs.
#   B. every open item is in some NON-STARTABLE column (In review here; In progress in real life).
#      These are never Ready/Backlog, so they are never candidates and `batch` has nothing to skip —
#      stderr is legitimately empty. This is the case ONLY `take`'s own count can catch, and the one
#      the old code got exactly backwards. (`In review` rather than `In progress` on purpose: the
#      latter makes `active_claims` go read the item's claim markers, which needs an issue fixture
#      this board does not have. Same branch, no unrelated dependency.)
# FSGG_COORD_SCAN_TTL_SEC=0 on every leg below: `take` reads through the SHARED 90s scan cache
# (CACHED=1), which is keyed on the BOARD, not on GH_BOARD_SET — so a cache warmed by an earlier
# test's default board would be served here and the swap silently ignored. Turning the cache off is
# what makes these legs actually see board-starved. (Found the hard way: every leg failed because
# `take` was scheduling against a board that had none of these items.)
cat >"$FIXTURES/board-starved.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":{"name":"P7 Audio"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#999"},"content":{"__typename":"Issue","number":301,"title":"Blocked on an open, unverifiable blocker","url":"https://github.com/FS-GG/FS.GG.Audio/issues/301","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}},
    {"status":{"name":"In review"},"phase":{"name":"P3 Governance"},"blockedBy":null,"content":{"__typename":"Issue","number":302,"title":"Open, but in review — not startable, not blocked","url":"https://github.com/FS-GG/FS.GG.Governance/issues/302","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Done"},"phase":{"name":"P3 Governance"},"blockedBy":null,"content":{"__typename":"Issue","number":303,"title":"Finished, and must not be counted as open","url":"https://github.com/FS-GG/FS.GG.Governance/issues/303","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P4 Templates"},"blockedBy":null,"content":{"__typename":"Issue","number":304,"title":"A real, startable item in ANOTHER repo","url":"https://github.com/FS-GG/FS.GG.Templates/issues/304","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4988}}
JSON

# A. BLOCKED queue — `batch` must now NAME the blocker instead of dropping the item in silence.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved run take --repo audio 2>&1 || true)"
assert_contains "#488 A: a blocked candidate is reported, not silently dropped" "blocked by" "$out"
assert_contains "#488 A: ...and it names the blocker"                           "FS.GG.SDD#999" "$out"
case "$out" in
  *"empty queue"*) bad "#488 A: a BLOCKED queue must not be called empty" "$out" ;;
  *)               ok  "#488 A: a BLOCKED queue is not reported as 'an empty queue, not a blocked one'" ;;
esac

# B. IN PROGRESS queue — stderr is legitimately empty, so only a COUNT can tell empty from starved.
# This is the exact sentence #488 was filed about, and the exact board that falsifies it.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved run take --repo governance 2>&1 || true)"
case "$out" in
  *"empty queue"*) bad "#488 B: an open non-startable item must not read as 'no open board item at all'" "$out" ;;
  *)               ok  "#488 B: a queue whose items are all non-startable is not called empty" ;;
esac
assert_contains "#488 B: it COUNTS the open items instead of guessing" "1 open board item" "$out"
assert_contains "#488 B: ...and says what state they are in"           "In review"          "$out"
# Done must not be counted as open — otherwise every finished board reads as "blocked".
case "$out" in
  *"2 open board item"*) bad "#488 B: a Done item was counted as open" "$out" ;;
  *)                     ok  "#488 B: a Done item is not counted as open" ;;
esac

# C. GENUINELY empty — the message it was always meant for must still fire, or the fix has merely
# swapped one false sentence for another.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved run take --repo sdd 2>&1 || true)"
assert_contains "#488 C: a genuinely empty queue still says so" "empty queue, not a blocked one" "$out"

# D. UNREADABLE — "I could not look" and "I looked, and it is empty" must not produce the same
# sentence (#266's ratified rule). A failed count may never render as an empty queue.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved GH_FAIL_BOARD=1 run take --repo governance 2>&1 || true)"
case "$out" in
  *"empty queue"*) bad "#488 D: an UNREADABLE board must not read as an empty queue" "$out" ;;
  *)               ok  "#488 D: an unreadable board is a no-verdict, never 'an empty queue'" ;;
esac

# ================================================================================================
# =================================================================================================
# #485 — "is this item startable?" was computed in five places and agreed in none.
#
# Four legs, asserted together because they were ONE bug: every command that answers "what can I
# start?" had its own idea of the answer, and no two agreed.
# =================================================================================================
echo "--- #485: one predicate for 'startable' ---"

# A board whose blockers are PULL REQUESTS — the case the old code could never resolve, because a PR is
# never a board item, so it could only ever be UNKNOWN, and UNKNOWN blocks. Forever.
cat >"$FIXTURES/board-blk.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#701"},"content":{"__typename":"Issue","number":700,"title":"blocked by a MERGED pr","url":"https://github.com/FS-GG/FS.GG.SDD/issues/700","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#703"},"content":{"__typename":"Issue","number":702,"title":"blocked by an OPEN pr","url":"https://github.com/FS-GG/FS.GG.SDD/issues/702","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#705"},"content":{"__typename":"Issue","number":704,"title":"blocked by a pr closed UNMERGED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/704","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":null,"content":{"__typename":"Issue","number":706,"title":"declares its touch-set in markdown","url":"https://github.com/FS-GG/FS.GG.SDD/issues/706","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#709"},"content":{"__typename":"Issue","number":708,"title":"the board says its blocker is closed","url":"https://github.com/FS-GG/FS.GG.SDD/issues/708","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Done"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":709,"title":"STALE on the board: says CLOSED, is open","url":"https://github.com/FS-GG/FS.GG.SDD/issues/709","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":null,"content":{"__typename":"Issue","number":710,"title":"declares NOTHING at all","url":"https://github.com/FS-GG/FS.GG.SDD/issues/710","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":null,"content":{"__typename":"Issue","number":712,"title":"declares a token no matcher can honour","url":"https://github.com/FS-GG/FS.GG.SDD/issues/712","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
]}}}},"rateLimit":{"cost":1,"remaining":4999}}
JSON

# The same fail-open, ALONE on a board, so `take` has no choice but to reach for it (leg f).
cat >"$FIXTURES/board-blkf.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#709"},"content":{"__typename":"Issue","number":708,"title":"the board says its blocker is closed","url":"https://github.com/FS-GG/FS.GG.SDD/issues/708","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Done"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":709,"title":"STALE on the board: says CLOSED, is open","url":"https://github.com/FS-GG/FS.GG.SDD/issues/709","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
]}}}},"rateLimit":{"cost":1,"remaining":4999}}
JSON

mkpr_state() {   # <num> <state> <merged_at|""> — a PR exactly as REST /issues/<n> serves one
  jq -n --argjson n "$1" --arg s "$2" --arg m "$3" --arg r "FS-GG/FS.GG.SDD" \
    '{number:$n, state:$s, repo:$r,
      pull_request:{ merged_at: (if $m == "" then null else $m end) },
      html_url:("https://github.com/"+$r+"/pull/"+($n|tostring))}' >"$STORE/issue-$1.json"
  echo '[]' >"$STORE/comments-$1.json"
}
mkpr_state 701 closed "2026-07-11T10:00:00Z"    # MERGED           -> resolved
mkpr_state 703 open   ""                        # OPEN             -> still blocks
mkpr_state 705 closed ""                        # closed UNMERGED  -> resolved (abandoned)

seed_issue_in FS-GG/FS.GG.SDD 700 "merged-pr blocker"   "src/S700.fs"
seed_issue_in FS-GG/FS.GG.SDD 702 "open-pr blocker"     "src/S702.fs"
seed_issue_in FS-GG/FS.GG.SDD 704 "closed-pr blocker"   "src/S704.fs"
seed_issue_in FS-GG/FS.GG.SDD 708 "stale-board blocker" "src/S708.fs"
seed_issue_in FS-GG/FS.GG.SDD 710 "declares nothing"    ""
# leg (b): the touch-set written the way a human writes one — in markdown.
seed_issue_raw 706 "markdown paths" 'Some description.

Paths: `src/S706.fs`, `tests/S706/**`' FS-GG/FS.GG.SDD
# ...and one that is genuinely broken: a LEADING `**/` matches no file, ever. Stripping backticks must
# not launder this into acceptance — an unmatchable token reserves nothing, so it stays refused (#273).
seed_issue_raw 712 "unmatchable" 'Paths: **/everything.fs' FS-GG/FS.GG.SDD
# The board says #709 is CLOSED. The SERVER says it is open. That disagreement IS leg (f).
mkissue 709 FS-GG/FS.GG.SDD open

blk() { PATH="$STUB:$PATH" GH_BOARD_SET=blk GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
# TTL=0 wherever a board SET matters: the scan cache is keyed by the board, and GH_BOARD_SET is a
# stub-side fiction the cache knows nothing about — a CACHED read would happily serve an earlier set.
blkf() { PATH="$STUB:$PATH" GH_BOARD_SET=blkf GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 bash "$COORD" "$@"; }
blkjson="$(blk batch --repo sdd -n 9 --json 2>/dev/null)"

# ---- leg (e): MERGED != CLOSED, and a PR is NEVER on the board (was #476) -------------------------
# `blocked: any(.state != "CLOSED")` is exactly right for an Issue (OPEN|CLOSED) and silently wrong for
# a PullRequest (OPEN|CLOSED|**MERGED**). The gate opened when the blocking work was ABANDONED, and
# shut forever once it was FINISHED. Live on a critical path: FS.GG.SDD#350 blocked by .github#449.
assert_eq "485(e): a MERGED pr no longer blocks forever — #700 is startable" \
  "true" "$(jq -r 'any(.[]; . == "FS.GG.SDD#700")' <<<"$blkjson")"
assert_eq "485(e): an OPEN pr still blocks — #702 is NOT offered" \
  "false" "$(jq -r 'any(.[]; . == "FS.GG.SDD#702")' <<<"$blkjson")"
assert_eq "485(e): a pr closed UNMERGED resolves too — #704 is startable" \
  "true" "$(jq -r 'any(.[]; . == "FS.GG.SDD#704")' <<<"$blkjson")"
# A bare `batch` reads the board twice (directly, and again inside active_claims) — that is the
# pre-existing baseline. Resolving three off-board PR blockers must add NOTHING to it: they are answered
# over REST, which is a different budget from the one every worker shares and this loop drains (#418).
: >"$GH_GRAPHQL_COUNT"; blk batch --repo sdd -n 9 --json >/dev/null 2>&1 || true
assert_eq "485(e): off-board blockers cost ZERO extra GraphQL — resolved over REST (#418)" \
  "2" "$(gcount)"

# ---- leg (b): a touch-set written in markdown is still a touch-set (was #435) ---------------------
# ``Paths: `src/x/**` `` normalized to tokens with the BACKTICKS ATTACHED, and the trailing-glob strip
# is anchored at end-of-string — so `/\*\*$` never matched, the `**` survived, `invalid_paths` flagged
# it, and the item was refused. A grammar-legal declaration rejected for its markdown: the item vanished
# from `batch` while `take` reported an empty queue. FS.GG.Audio#29 and #31 were both filed this way.
assert_eq "485(b): a backticked 'Paths:' line is a valid declaration — #706 is startable" \
  "true" "$(jq -r 'any(.[]; . == "FS.GG.SDD#706")' <<<"$blkjson")"
assert_contains "485(b): ...and its tokens are the paths, without the markdown" \
  "src/S706.fs" "$(blk overlap 'FS.GG.SDD#706' 'FS.GG.SDD#706' 2>&1 || true)"
assert_contains "485(b): a REAL unmatchable token is still refused, and now NAMES the grammar" \
  "no glob matcher" "$(blk batch --repo sdd 2>&1 >/dev/null || true)"

# ---- leg (a): `next` no longer recommends what `batch` refuses (was #431) -------------------------
nx="$(blk next --repo sdd 2>/dev/null)"
refute_contains "485(a): next does NOT recommend an item with no declared touch-set (#710)" \
  "FS.GG.SDD#710" "$nx"
# `-n 1` stops at the first pick, so `next` only reports on what it examined. The REASON is `batch`'s,
# in `batch`'s words — ask the full scheduling pass for it.
assert_contains "485(a): ...and the reason is the refusing check's own words, not next's guess" \
  "#710 — no 'Paths:' declared" "$(blk batch --repo sdd 2>&1 >/dev/null || true)"
# The agreement IS the fix: what `next` recommends must be what `batch` would schedule.
assert_eq "485(a): next's pick is one batch would schedule — one predicate, not two" \
  "true" "$(nxid="$(printf '%s' "$nx" | sed -n 's/^→ \([^ ]*\).*/\1/p')"; \
            jq -r --arg id "$nxid" 'any(.[]; . == $id)' <<<"$blkjson")"

# ---- leg (f): the scheduler's last word — ask the SERVER, not the cached flag (was #343) ----------
# #708's blocker is #709. The BOARD says #709 is CLOSED (a stale `content.state` — the shape #343's
# unpinned fail-open is suspected to take), so `blocked` is FALSE and `batch` offers it. The SERVER says
# #709 is OPEN. `take` must refuse rather than point a worker at work another item still owns.
assert_eq "485(f): the stale board really does offer #708 (the fail-open, reproduced)" \
  "true" "$(blkf batch --repo sdd --json 2>/dev/null | jq -r 'any(.[]; . == "FS.GG.SDD#708")')"
takeout="$(PATH="$STUB:$PATH" GH_BOARD_SET=blkf GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
             bash "$COORD" --worker hawk-f01 take --repo sdd 2>&1 >/dev/null || true)"
assert_contains "485(f): take REFUSES an item whose blocker is open on the server" \
  "refusing to claim" "$takeout"
assert_contains "485(f): ...and names the blocker it caught the board lying about" \
  "FS-GG/FS.GG.SDD#709" "$takeout"
# `grep -c` prints 0 AND exits 1 on no-match, so `|| echo 0` would print it twice.
assert_eq "485(f): ...and claims nothing — no marker is posted" \
  "0" "$(grep -c 'comment-post FS-GG/FS.GG.SDD 708' "$GH_LOG" || true)"

# ==================================================================================================
# THE SHADOW (ADR-0034 Phase 2) — and the only three things it is allowed to do.
# ==================================================================================================
# The typed F# engine runs beside bash on every scheduling call, bash's answer is returned, and the
# disagreement is logged. Three properties, and every one of them is load-bearing:
#
#   1. IT MAY NOT CHANGE THE ANSWER. Byte-identical stdout and exit code, shadow on or off. If this
#      assertion ever fails, the shadow has stopped being an observer and become a participant — and
#      it will have done so on a tool that hands work to a live fleet.
#   2. A MISSING ENGINE IS A SKIP, NOT AN ERROR. This script is byte-copied into six repos with no
#      .NET on the coordination path until Phase 3. The shadow must not be able to red their CI.
#   3. ...AND THEREFORE IT MUST NOT BE ABLE TO GO SILENTLY VACUOUS. (1) and (2) have just specified a
#      component designed to do nothing when it goes wrong — which is epic #266's shape exactly. So
#      the log must record that it RAN and WHAT it compared, and `divergence` must refuse to call an
#      empty log green. A shadow nobody can prove ran is not evidence, and "zero divergence" would be
#      the most reassuring possible way to say "we never looked".
export FSGG_COORD_DIVERGENCE_LOG="$WORK/divergence.jsonl"
: >"$FSGG_COORD_DIVERGENCE_LOG"

# The engine, if this checkout has one built. CI builds it first and sets FSGG_SHADOW_REQUIRE_ENGINE=1,
# which turns "no engine" from a skip into a FAILURE — because a fixture that quietly skips the half of
# itself that does the actual comparing is the very thing this section exists to forbid.
ENGINE="${FSGG_COORD_ENGINE_BIN:-$HERE/../../src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"
if [ ! -x "$ENGINE" ]; then
  if [ -n "${FSGG_SHADOW_REQUIRE_ENGINE:-}" ]; then
    bad "shadow: the engine must be built before this fixture runs" "not executable: $ENGINE"
  else
    echo "SKIP  shadow: no engine at $ENGINE (build: dotnet build src/FS.GG.Coord.Cli -c Release)"
  fi
fi

# ---- 0. THE DEFAULT IS `auto`, AND THAT IS WHAT MAKES THE CLOCK START ------------------------------
# `--engine=bash` as the default was faithful to the roadmap ("defaulting off") and produced a harness
# that never runs: nothing in the fleet sets FSGG_COORD_ENGINE, so nothing ever compares, the log stays
# empty forever, and "zero divergence across the live fleet for three consecutive days" can never be
# met — because the clock never starts. An observer nobody switches on is a decoration.
#
# So: shadow WHEREVER AN ENGINE EXISTS, bash everywhere else. The presence of the engine IS the switch,
# which is the honest gate — a repo that has not got one cannot be broken by a thing that is not there.
: >"$FSGG_COORD_DIVERGENCE_LOG"
auto_noeng="$(PATH="$STUB:$PATH" FSGG_COORD_ENGINE_BIN=/nonexistent \
                bash "$COORD" batch --repo rendering --json 2>/dev/null)" && auto_rc=0 || auto_rc=$?
assert_eq "shadow: with NO engine, the default is plain bash — a receiver is untouched" \
  "0" "$(wc -c <"$FSGG_COORD_DIVERGENCE_LOG" | tr -d ' ')"
assert_eq "shadow: ...and it decided normally" "0" "$auto_rc"

# ---- 3. an empty log is NOT green. It is no-verdict, and no-verdict is non-zero. -------------------
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq 'shadow: `divergence` over an EMPTY log exits 3 (no-verdict), never 0' "3" "$rc"
assert_contains "shadow: ...and says so — an empty log is zero EVIDENCE, not zero divergence" \
  "zero EVIDENCE" "$(run divergence 2>&1 >/dev/null || true)"

# ---- 2. a missing engine is a logged SKIP, and bash is untouched -----------------------------------
: >"$FSGG_COORD_DIVERGENCE_LOG"
plain="$(run batch --repo rendering --json 2>/dev/null)" && plain_rc=0 || plain_rc=$?
noeng="$(FSGG_COORD_ENGINE_BIN=/nonexistent PATH="$STUB:$PATH" FSGG_COORD_ENGINE=shadow \
           bash "$COORD" batch --repo rendering --json 2>/dev/null)" && noeng_rc=0 || noeng_rc=$?
assert_eq "shadow: a MISSING engine does not change bash's answer"    "$plain" "$noeng"
assert_eq "shadow: ...nor its exit code (a receiver's CI must not red)" "$plain_rc" "$noeng_rc"
assert_eq "shadow: ...and the skip is RECORDED, not silent" \
  "false" "$(jq -s -r '.[-1].ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
assert_contains "shadow: ...naming the cause, so a silent no-op is impossible to mistake for agreement" \
  "no engine" "$(jq -s -r '.[-1].reason' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo "")"

if [ -x "$ENGINE" ]; then
  # ---- 1. THE ANSWER IS BASH'S. Byte-identical, shadow on or off. --------------------------------
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  shadowed="$(FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_COORD_ENGINE=shadow run batch --repo rendering --json 2>/dev/null)" \
    && sh_rc=0 || sh_rc=$?
  assert_eq "shadow: the shadowed answer IS bash's answer, byte for byte" "$plain" "$shadowed"
  assert_eq "shadow: ...and the exit code is bash's too" "$plain_rc" "$sh_rc"

  # ---- 3. NON-VACUITY. It ran, and it compared something. ----------------------------------------
  assert_eq "shadow: the run is RECORDED as having happened" \
    "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  compared="$(jq -s -r '[.[] | select(.ran) | .compared] | add // 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo 0)"
  if [ "${compared:-0}" -gt 0 ]; then
    ok "shadow: it compared $compared item-verdict(s) — the comparison is not vacuous"
  else
    bad "shadow: it compared NOTHING" "a shadow that compares zero items reports zero divergence, which is indistinguishable from success"
  fi

  # ---- THE OBSERVER MAY NOT KILL THE CALLER --------------------------------------------------------
  # The sharpest edge in this whole design, and it took a review to see it.
  #
  # The shadow reads markers for the candidates bash SHORT-CIRCUITS (a blocked one never has its lock
  # read). Those reads go through `claims_of`, which is documented "or DIE" and means it — a lock
  # guessed from a failed read is the one thing a lock may never be. But `die` is `kill -s TERM $$`: it
  # takes down the TOP-LEVEL shell, and NO `|| true` can catch a signal.
  #
  # So one transient 5xx on a blocked candidate's comments would have aborted the entire tool. A worker
  # running `--engine shadow take` would get a hard failure and NO ITEM — on a run bash alone completes
  # without noticing. The observer would have changed the answer, which is the one thing it exists not
  # to do. Rendering#200 is blocked, so it is swept and never otherwise read; failing its comment read
  # reproduces exactly that.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  : >"$STORE/posted-FS-GG__FS.GG.Rendering-200"     # arms GH_FAIL_READ_ISSUE for that subject
  killed="$(FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_COORD_ENGINE=shadow \
              GH_FAIL_READ_ISSUE='FS-GG/FS.GG.Rendering#200' \
              run batch --repo rendering --json 2>/dev/null)" && killed_rc=0 || killed_rc=$?
  rm -f "$STORE/posted-FS-GG__FS.GG.Rendering-200"
  assert_eq "shadow: a DYING read inside the observer does not kill the tool (exit code is bash's)" \
    "$plain_rc" "$killed_rc"
  assert_eq "shadow: ...and bash's answer survives it intact" "$plain" "$killed"
  assert_eq "shadow: ...and the unobservable candidate is COUNTED, not silently dropped" \
    "true" "$(jq -s -r '([.[] | select(.ran) | .unobserved // 0] | add // 0) > 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"

  # ---- `--ignore-blocked` MUST NOT MANUFACTURE OUTCOME DIVERGENCES ------------------------------
  # The flag is a DIAGNOSTIC ("what WOULD be startable if the blockers cleared") and it relaxes the
  # blocker filter and nothing else. The shadow's premise is that both engines decide from the same
  # observations — so it must hand the engine the rule bash ENFORCED, not the rule bash knows.
  #
  # Before this was fixed, the snapshot still carried the blockers. The engine dutifully returned
  # `blocked-by` for every candidate bash had deliberately let through, and each one was logged as an
  # OUTCOME divergence — the RELEASE-BLOCKING class — while both engines were behaving exactly as
  # designed. A diagnostic flag would have poisoned the one signal that has to stay trustworthy, and
  # `--engine=fs` would have been held back by a disagreement that never existed.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_COORD_ENGINE=shadow \
    run batch --repo rendering --include-backlog --ignore-blocked --json >/dev/null 2>&1 || true
  assert_eq "shadow: --ignore-blocked reports ZERO outcome divergences (the engine is told what bash ENFORCED)" \
    "0" "$(jq -s -r '[.[] | select(.ran) | .outcome] | add // 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  assert_eq "shadow: ...and it still actually compared the candidates, rather than dodging them" \
    "true" "$(jq -s -r '([.[] | select(.ran) | .compared] | add // 0) > 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"

  # The engine must also be reachable through PATH alone — that is how Phase 3's shim will find it,
  # and a resolution path nothing exercises is a resolution path that does not work.
  cp "$ENGINE" "$STUB/fsgg-coord-engine" 2>/dev/null || true
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  viapath="$(FSGG_COORD_ENGINE=shadow run batch --repo rendering --json 2>/dev/null || true)"
  assert_eq "shadow: the engine resolves off PATH (the shape the Phase 3 shim will use)" "$plain" "$viapath"
  assert_eq "shadow: ...and that run is recorded as RAN, not skipped" \
    "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"

  # AND WITH NO ENV VAR AT ALL. This is the assertion the whole phase turns on: an engine on PATH and
  # nobody opting in to anything must still produce evidence, or the three-day clock never starts.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  autoout="$(run batch --repo rendering --json 2>/dev/null || true)"
  assert_eq "shadow: an engine on PATH shadows BY DEFAULT — no env var, no flag, no ceremony" \
    "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  assert_eq "shadow: ...and it STILL returns bash's answer, byte for byte" "$plain" "$autoout"

  # ...and `--engine bash` remains the escape hatch: never shadow, whatever is on PATH.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  offout="$(run --engine bash batch --repo rendering --json 2>/dev/null || true)"
  assert_eq "shadow: --engine bash refuses to shadow even with an engine right there" \
    "0" "$(wc -c <"$FSGG_COORD_DIVERGENCE_LOG" | tr -d ' ')"
  assert_eq "shadow: ...and answers identically" "$plain" "$offout"
  rm -f "$STUB/fsgg-coord-engine"
fi

# ---- the classifier: OUTCOME and REASON are not the same news, and must not be summed -------------
# Fed by hand, because the point is the CLASSIFICATION, not the engines. An outcome divergence means
# the two disagree about whether an item may be HANDED OUT — that is how two workers end up in one
# file, and it blocks the flip. A reason divergence means they agree it is unschedulable and name a
# different fact; at Phase 2 that is EXPECTED (they check in a different order) and it is a decision
# to record, not a bug. Summing them would bury the first in the second, and "zero divergence for
# three days" would never go green for something that was never wrong.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"compared":4,"extraReads":2,"outcome":1,"reason":1,"unpaired":0,"divergences":[{"id":"FS.GG.SDD#1","class":"outcome","bash":"startable","engine":"held-by"},{"id":"FS.GG.SDD#2","class":"reason","bash":"blocked-by","engine":"held-by"}]}
{"ts":"2026-07-12T10:01:00Z","mode":"shadow","ran":false,"reason":"no engine on PATH"}
JSONL
# `|| true` is REQUIRED, and the reason is the contract under test: `divergence` exits 1 when the
# engines disagree about what may be handed out. Under `set -euo pipefail` an unguarded capture of a
# non-zero command kills the fixture — which is exactly what it did, silently truncating this whole
# section on the first run.
d="$(run divergence --json 2>/dev/null || true)"
assert_eq "shadow: OUTCOME divergences are counted apart"  "1" "$(jq -r '.outcome' <<<"$d")"
assert_eq "shadow: REASON divergences are counted apart"   "1" "$(jq -r '.reason'  <<<"$d")"
assert_eq "shadow: SKIPPED runs are counted, and are not agreement" "1" "$(jq -r '.skipped' <<<"$d")"
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: an OUTCOME divergence exits 1 — the flip is BLOCKED" "1" "$rc"

# ...and with the outcome divergence gone, a reason divergence alone must NOT block the flip.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"compared":4,"extraReads":0,"outcome":0,"reason":3,"unpaired":0,"divergences":[{"id":"FS.GG.SDD#2","class":"reason","bash":"blocked-by","engine":"held-by"}]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: reason divergences alone exit 0 — they are a decision, not a defect" "0" "$rc"

# ---- A RUN THAT COMPARED NOTHING IS NOT AGREEMENT. ------------------------------------------------
# Found by running the shadow ONCE against the real board. The queue happened to hold no Ready item, so
# the shadow ran, compared ZERO verdicts, and `divergence` printed `green on OUTCOME` — a gate reporting
# green over a subject it never read, which is epic #266 EXACTLY, rebuilt inside the tool whose whole
# purpose is to retire it. It survived precisely as long as it took to run against real data once.
#
# `ran` is not the bar. `compared` is. A run over an empty candidate set agrees with every engine ever
# written, because it decided nothing — and three days of an empty queue is not three days of agreement.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"compared":0,"extraReads":0,"outcome":0,"reason":0,"unpaired":0,"divergences":[]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: a run that compared ZERO verdicts is no-verdict (exit 3), NOT green" "3" "$rc"
assert_contains "shadow: ...and says why — an empty queue agrees with everything" \
  "compared ZERO" "$(run divergence 2>&1 >/dev/null || true)"

# ---- THE OTHER TWO WAYS TO DISAGREE, both of which used to score as AGREEMENT ---------------------
# `outcome` counts items on which the two engines ruled DIFFERENTLY. It therefore cannot see either of
# the states below, because in both of them the engine produced no per-item ruling to differ WITH — so
# `outcome` was 0, and a green sailed out of a run in which the engines could not have agreed less.

# (a) THE ENGINE REFUSED THE BATCH. An in-flight reservation whose touch-set is unmatchable reserves
#     NOTHING while occupying files (#273), so the engine refuses to schedule at all. `decisions` is
#     empty. If bash proceeded, that is the sharpest disagreement available — not a quiet one.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"engineVerdict":"red","compared":0,"extraReads":0,"unobserved":0,"outcome":0,"reason":0,"unpaired":6,"divergences":[]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: an engine that REFUSED the batch is never green (it agreed to nothing)" "3" "$rc"

# (b) AN ITEM ONLY ONE ENGINE RULED ON. Under `-n` each engine stops at its own cap, so a different
#     EVALUATED SET is a divergence in the fold even when every shared verdict matches. `compared` now
#     counts PAIRS, not the union — the union is what let this read as a large, confident comparison.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"engineVerdict":"green","compared":5,"extraReads":0,"unobserved":0,"outcome":0,"reason":0,"unpaired":2,"divergences":[{"id":"FS.GG.SDD#9","class":"bash-only","bash":"startable","engine":null}]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: an item ruled on by ONE engine only is RED — the folds evaluated different sets" "1" "$rc"
assert_contains "shadow: ...and the report says what was NOT compared, not just what was" \
  "NOT compared" "$(run divergence 2>/dev/null || true)"

echo "fsgg-coord fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::fsgg-coord fixture FAILED"; exit 1; }
echo "fsgg-coord fixture — OK"
