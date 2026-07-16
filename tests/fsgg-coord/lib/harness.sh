#!/usr/bin/env bash
# Shared harness for the fsgg-coord fixture — the world every case file runs against.
#
# SOURCED, never executed. `. lib/harness.sh` leaves you with: a throwaway temp world (fixtures +
# a PATH-shim `gh` stub that COUNTS calls + an isolated cache), the assertion helpers, the issue
# seeders, and the ADR-0027 parallel-work world (board-pw, the base issues, the pre-existing
# claims, and the `pw`/`as` worker wrappers). It is the former run.sh prelude, lifted verbatim so
# each case gets the SAME world the monolith built once — the difference is that it is now built
# per case, which is what lets the cases run in parallel and in isolation.
#
# A case file is:
#     . "$(dirname "${BASH_SOURCE[0]}")/../lib/harness.sh"
#     ...seed what THIS case needs, assert...
#     harness_report
#
# THE CACHE IS WARM BY DEFAULT. The monolith bootstrapped once at the top, so every assertion after
# it saw a warm board map; the count-delta assertions (`+1 GraphQL`) are written against that. A case
# that means to test the COLD path sets HARNESS_COLD=1 before sourcing.
set -euo pipefail

# Resolve against THIS file, not $0 — the caller lives in cases/.
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COORD="$HERE/../../scripts/fsgg-coord-bash" # always invoked as `bash "$COORD"`
# ADR-0040 D.2: `scripts/fsgg-coord` is now the ~40-line shim (it execs the engine). This corpus drives
# the ~7,000-line BASH monolith — preserved verbatim at `scripts/fsgg-coord-bash` — because the shadow /
# differential cases (50-shadow-engine, 51-fs-flip) compare bash against the engine, and the escape-hatch
# cases hold bash exact. Both live until D.4 deletes bash; this harness points at the file they test.

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

  # #448: set-field --batch emits ONE document of ALIASED field mutations. It is counted as exactly one
  # GraphQL call by the counter at the top of this arm — which IS the acceptance assertion. This must
  # be discriminated FIRST: it is a mutation, and none of the query markers below apply to it.
  #
  # GH_BATCH_FAIL_ALIAS=<alias> makes that alias fail, and the stub then models what the REAL API does:
  # GraphQL executes mutations in a document SERIALLY, so every alias BEFORE the failure has ALREADY
  # BEEN APPLIED, and every alias after it never ran. Both of the latter come back null, and the body
  # arrives as HTTP 200 carrying data AND errors, with errors[].path naming the alias. A client that
  # reads that as success reports a half-written board as a clean one.
  if printf '%s' "\$q" | grep -q 'ProjectV2ItemFieldValue'; then
    printf 'batch-mutation %s\n' "\$q" >>"\$GH_LOG"
    als=\$(printf '%s' "\$q" | grep -oE 'f[0-9]+:' | tr -d ':')
    data=""; errs=""; hit=""
    for a in \$als; do
      if [ "\$a" = "\${GH_BATCH_FAIL_ALIAS:-}" ]; then
        hit=1
        errs="\${errs}\${errs:+,}{\"type\":\"FORBIDDEN\",\"path\":[\"\$a\"],\"message\":\"stub: \$a rejected\"}"
        data="\${data}\${data:+,}\"\$a\":null"
      elif [ -n "\$hit" ]; then
        data="\${data}\${data:+,}\"\$a\":null"
      else
        data="\${data}\${data:+,}\"\$a\":{\"projectV2Item\":{\"id\":\"PVTI_coord123\"}}"
      fi
    done
    if [ -n "\$errs" ]; then
      printf '{"data":{%s},"errors":[%s]}\n' "\$data" "\$errs"; exit 1
    fi
    printf '{"data":{%s}}\n' "\$data"; exit 0
  fi

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
  # Under GH_RATELIMIT the meter reads what the 403s imply — 0 GraphQL, REST untouched. \`reset\` sits
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
  subid_f=""; subid_F=""; slurp=""
  n=\${#args[@]}
  for ((i=1;i<n;i++)); do
    case "\${args[i]}" in
      -X)        method="\${args[i+1]}" ;;
      --include)  include=1 ;;
      --paginate) paginate=1 ;;
      --slurp)   slurp=1 ;;
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
  # \`--paginate --slurp\` does NOT hand back the page payload — it hands back an ARRAY OF PAGES, one
  # element per response (#697). A stub that ignored the flag and cat'd the fixture would serve
  # \`{"check_runs":[…]}\` where real gh serves \`[{"check_runs":[…]}]\`, and every client expression
  # written against the real shape would come back empty HERE and work in production — or, far worse,
  # the reverse. \`--slurp\` and \`--jq\` are mutually exclusive in real gh, so they are here too.
  emit() {
    if [ -n "\$slurp" ]; then jq -s '.'
    elif [ -n "\$jqexpr" ]; then jq -r "\$jqexpr"
    else cat; fi
  }
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

  # --- sub-issues: the native child edge \`child\` writes and \`epic_rollup\` reads -------------------
  # A real mutable store, so \`child\` can be observed to be idempotent rather than merely exit 0. The
  # stub also reproduces the API's two traps: the endpoint keys on the child's REST **id** (not its
  # number), and it 422s when sub_issue_id arrives as a JSON string — i.e. via \`gh api -f\`. A stub
  # that accepted -f would let the client regress to the form the API rejects.
  if [[ "\$path" =~ ^repos/([^/]+)/([^/]+)/issues/([0-9]+)/sub_issues\$ ]]; then
    snwo="\${BASH_REMATCH[1]}/\${BASH_REMATCH[2]}"; snum="\${BASH_REMATCH[3]}"
    issue_guard "\$snwo" "\$snum"
    printf 'sub-issue-%s %s %s\n' "\$(printf '%s' "\$method" | tr 'A-Z' 'a-z')" "\$snwo" "\$snum" >>"\$GH_LOG"
    sf="\${JF/\/issue-/\/subissues-}"     # parallel to the issue key, so it is repo-qualified too
    [ -f "\$sf" ] || echo '[]' >"\$sf"
    # GH_FAIL_SUBISSUES_GET=<n>: the existing-links read for <n> fails. \`child\` must not read that as
    # "the edge is absent" and POST anyway — an unreachable subject is not an absent one (#266/#320).
    if [ "\$method" = "GET" ] && injected "\${GH_FAIL_SUBISSUES_GET:-}" "\$snwo" "\$snum"; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    if [ "\$method" = "POST" ]; then
      # GH_FORCE_SUBISSUE_POST_FAIL=1: the link POST fails the way the real API does. \`child\` must
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
      # its issue. Marker PATCH/DELETE is how \`heartbeat\` and \`release\` touch the lock, so a
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
  # THE LIVENESS PROBE (#581): \`pr_alive\` lists OPEN pull requests and looks for a head branch
  # \`item/<n>-*\`. GH_LIVE_PR="<num>:<pr>" puts an open PR on item <num>. This is the ONLY signal that
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
  # GH_FAIL_PR_GET=<n>: the head-ref read for PR <n> fails. \`verify-paths\` resolves which issue a PR
  # implements from its branch name here, and an empty answer would read as "the branch is not
  # item/<n>-…" — i.e. a SKIP verdict invented from an unanswered query (.github#322).
  if [[ "\$path" =~ ^repos/([^/]+/[^/]+)/pulls/([0-9]+)$ ]]; then
    printf 'pr-get %s %s\n' "\${BASH_REMATCH[1]}" "\${BASH_REMATCH[2]}" >>"\$GH_LOG"
    if [ "\${BASH_REMATCH[2]}" = "\${GH_FAIL_PR_GET:-}" ]; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    # GH_PR_LAZY=<n>: PR <n> reports \`mergeable: null\` on its FIRST read and its real value only on
    # the SECOND (#697). This is not a quirk of the stub — it is what GitHub does: mergeability is
    # computed lazily, on demand, so the first read of an untested PR is always null. A client that
    # believed that first read would call a CONFLICTED PR "unknown", or worse, landable. Observed
    # live while #697 was being worked: PR #692 read null, then resolved to \`dirty\`.
    if [ "\${BASH_REMATCH[2]}" = "\${GH_PR_LAZY:-}" ] && [ ! -f "$STORE/lazy-\${BASH_REMATCH[2]}" ]; then
      : >"$STORE/lazy-\${BASH_REMATCH[2]}"
      jq '.mergeable = null' <"$FIXTURES/pr-\${BASH_REMATCH[2]}.json" | emit; exit 0
    fi
    emit <"$FIXTURES/pr-\${BASH_REMATCH[2]}.json"; exit 0
  fi
  # THE CHECK-RUNS OF A HEAD SHA (#697). \`pr_landable\` reads this to tell FINISHED work from work
  # that merely exists. Keyed by SHA, exactly as the real endpoint is — so a fixture cannot answer for
  # a commit the client never asked about. --slurp'd by the caller, so this serves ONE page shaped as
  # the real payload: {"check_runs": [...]}. A missing fixture is a hard 404, not an empty list: "CI
  # never ran" and "I could not ask" must never look alike (#606).
  if [[ "\$path" =~ ^repos/([^/]+/[^/]+)/commits/([^/]+)/check-runs ]]; then
    printf 'check-runs %s %s\n' "\${BASH_REMATCH[1]}" "\${BASH_REMATCH[2]}" >>"\$GH_LOG"
    # The check-runs grow WITH their runs (see GH_RUNS_GROW below): a new workflow run brings new
    # check-runs with it. This read only READS the generation — it must not advance it, or one poll's
    # two reads would straddle two generations, a state GitHub never produces.
    if [ "\${BASH_REMATCH[2]}" = "\${GH_RUNS_GROW:-}" ] \
       && [ -f "$STORE/gen-\${BASH_REMATCH[2]}" ] \
       && [ "\$(cat "$STORE/gen-\${BASH_REMATCH[2]}")" -gt 1 ] \
       && [ -f "$FIXTURES/checks-\${BASH_REMATCH[2]}.grown.json" ]; then
      emit <"$FIXTURES/checks-\${BASH_REMATCH[2]}.grown.json"; exit 0
    fi
    [ -f "$FIXTURES/checks-\${BASH_REMATCH[2]}.json" ] \
      || { echo "gh stub: no check-runs fixture for sha \${BASH_REMATCH[2]}" >&2; exit 4; }
    emit <"$FIXTURES/checks-\${BASH_REMATCH[2]}.json"; exit 0
  fi

  # THE WORKFLOW RUNS OF A HEAD SHA (#720). \`pr_landable\` scores THESE, not check-runs, because
  # SUPERSESSION is a property of the RUN — \`concurrency: cancel-in-progress\` replaces a run, and
  # nothing on a check-run says so. Same contract as the block above: keyed by SHA, ONE page shaped
  # like the real payload ({"workflow_runs": [...]}), and a MISSING fixture is a hard 404 rather than
  # an empty list, so "CI never ran" can never be confused with "I could not ask" (#606).
  if [[ "\$path" =~ ^repos/([^/]+/[^/]+)/actions/runs\?head_sha=([^\&]+) ]]; then
    printf 'wf-runs %s %s\n' "\${BASH_REMATCH[1]}" "\${BASH_REMATCH[2]}" >>"\$GH_LOG"
    # GH_RUNS_GROW=<sha>: the run set GROWS between POLLS, which is what GitHub actually does — it
    # schedules a PR's workflows over 20-60s, so an early poll sees a SUBSET. Poll 1 gets the small
    # set; poll 2+ gets \`*.grown.json\`.
    #
    # THE GENERATION ADVANCES PER POLL, NOT PER READ, and that distinction is the whole leg. One poll
    # of \`pr_landable\` makes TWO reads (workflow runs, then check-runs). A marker that flipped between
    # them would serve the SMALL runs and the GROWN checks inside a single poll — a state GitHub never
    # produces — and the PR would go red on poll 1 from the check-runs alone. The stability path would
    # never be exercised, and the leg would pass while testing nothing. (It did, first time round: the
    # naive-waiter mutation went uncaught.) So: the runs read INCREMENTS the generation, and the
    # check-runs read only READS it.
    if [ "\${BASH_REMATCH[2]}" = "\${GH_RUNS_GROW:-}" ]; then
      gen="$STORE/gen-\${BASH_REMATCH[2]}"
      n=\$(( \$( [ -f "\$gen" ] && cat "\$gen" || echo 0 ) + 1 )); echo "\$n" >"\$gen"
      if [ "\$n" -gt 1 ]; then emit <"$FIXTURES/runs-\${BASH_REMATCH[2]}.grown.json"; exit 0; fi
    fi
    [ -f "$FIXTURES/runs-\${BASH_REMATCH[2]}.json" ] \
      || { echo "gh stub: no workflow-runs fixture for sha \${BASH_REMATCH[2]}" >&2; exit 4; }
    emit <"$FIXTURES/runs-\${BASH_REMATCH[2]}.json"; exit 0
  fi

  # --- a single issue: GET (title/body/Paths) or PATCH (widen rewrites the body) ----------------
  # GH_FAIL_ISSUE_GET=<n>: the body read for <n> fails. \`paths_of\` reads the touch-set here, and an
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

  # --- the issue LIST (the ETag-revalidated \`issues\` command) -----------------------------------
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
  # \`add\` (#587) — the verb whose ABSENCE is why every recipe reached past the client. It must go
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
  # LOGGED, so a test can assert this GraphQL call was never made (#430): the repo now comes off the
  # git remote, and "we did not spend the budget" is a claim about the CALL, not about the verdict.
  printf 'repo-view\n' >>"\$GH_LOG"
  #   GH_FAIL_REPO_VIEW=rate  the GraphQL budget is gone — the steady state of an N-worker fan-out.
  #   GH_FAIL_REPO_VIEW=other some other gh failure (auth, network, no default remote).
  #   GH_FAIL_REPO_VIEW=empty gh SUCCEEDS and names nothing — the one case that really is "no checkout".
  #   GH_FAIL_REPO_VIEW=rate2 a rate limit that is NOT gh's last word — it must still classify.
  case "\${GH_FAIL_REPO_VIEW:-}" in
    rate)  echo "GraphQL: API rate limit already exceeded for user ID 1645484." >&2; exit 1 ;;
    rate2) printf 'GraphQL: API rate limit already exceeded for user ID 1645484.\ngh: try again later.\n' >&2; exit 1 ;;
    other) echo "gh: could not determine current repository" >&2; exit 1 ;;
    empty) exit 0 ;;
  esac
  printf 'FS-GG/FS.GG.SDD\n'; exit 0
fi

echo "gh stub: unhandled: \$*" >&2; exit 3
STUB
chmod +x "$STUB/gh"

# ---- issue seeders (the store's only writers) ----------------------------------------------
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
# #203 (legacy prose in `Blocked by`) had NO issue fixture, and for years nothing noticed: bash
# short-circuits blocked candidates BEFORE it reads any body, so it never asked for this one. Only the
# SHADOW reads it — to hand the item to the engine — and a failed read there is a counted `unobserved`,
# withheld from the comparison. So the engine was never shown the one item in this fixture whose blocker
# is PROSE, and the prose-blocker path went unexercised on the very board built to exercise it.
seed_issue_in FS-GG/FS.GG.Governance 203 "Legacy prose in the field"  "src/Gov/Legacy.fs"

# ---- the ADR-0027 parallel-work world -----------------------------------------------------
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

mk_claim() {  # mk_claim <issue> <id> <worker> <fresh|stale>
  local ts="$fresh_ts"; [ "$4" = "stale" ] && ts="$stale_ts"
  jq -n --argjson id "$2" --arg w "$3" --arg ts "$ts" \
    '{id:$id, body:("<!-- fsgg:claim worker=" + $w + " lease=120 -->\nheld"),
      user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}'
}

# ---- the cache, and the tally --------------------------------------------------------------------
# Warm the board map the way the monolith's opening bootstrap did, so a case's `+N GraphQL` delta
# means what it meant there. `item-id` for #42 is warmed too: `set-field --batch` resolves it, and
# a cold resolve would silently add a call to every count assertion downstream of it.
warm_cache() { run bootstrap >/dev/null 2>&1; run item-id 'FS.GG.SDD#42' >/dev/null 2>&1; }
[ -n "${HARNESS_COLD:-}" ] || warm_cache

# Every case ends here. The runner parses this line to tally the suite.
harness_report() {
  echo "${CASE_NAME:-case} — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
  [ "$failcount" -eq 0 ] || { echo "::error::${CASE_NAME:-case} FAILED"; exit 1; }
}

# ---- helpers the monolith defined mid-file, hoisted so any case that inherited them still has them --
# These were defined inside one section and used from several others — the coupling that made the
# monolith un-splittable. They are VERBATIM; only their home moved.
edits_to_status() { grep -c 'PVTSSF_status' "$GH_LOG" 2>/dev/null || true; }
rel() { local e="$1"; shift
        PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
          env "$e" bash "$COORD" --worker pika-r01 "$@"; }

no_verdict() { # <name> <output> — the ONLY safe outcome across a boundary is no OK/DRIFT at all
  case "$2" in
    *"FSGG-PATHS OK"*|*"FSGG-PATHS DRIFT"*) bad "$1" "printed a verdict across a repo boundary: $2" ;;
    *) ok "$1" ;;
  esac
}

# The .github#257 off-board world: in-flight work is defined by the MARKER, not the column. It was
# built inline by the ADR-0027 section and then read by five later ones (`who`/`reap`/`batch`/#428/
# #461's negative control), so it is a function now — a case pays for it only if it needs it.
# (Body unindented: the JSON heredocs below need their terminator at column 0.)
seed_offboard_world() {
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

}
