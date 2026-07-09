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
#      with more children than the scan can see — and exits non-zero.
#   9. epic_rollup flips a parent only when every child is board-Done AND issue-CLOSED.
#
# Self-contained: a throwaway cache + stub under a temp dir, no network, no other repos. Mirrors
# tests/skill-union/run.sh (FS-GG/.github#111) in shape so the two fixtures read the same way.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
COORD="$HERE/../../scripts/fsgg-coord"      # always invoked as `bash "$COORD"`

WORK="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-coord-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

FIXTURES="$WORK/fixtures"; STUB="$WORK/bin"; export GH_LOG="$WORK/gh.log"
export GH_GRAPHQL_COUNT="$WORK/graphql.count" GH_REST_COUNT="$WORK/rest.count"
mkdir -p "$FIXTURES" "$STUB"
: >"$GH_LOG"; : >"$GH_GRAPHQL_COUNT"; : >"$GH_REST_COUNT"

# Run fsgg-coord against the stub + an isolated cache. FSGG_COORD_DEBUG surfaces the 304/cache path.
export FSGG_COORD_CACHE="$WORK/cache"
run() { PATH="$STUB:$PATH" FSGG_COORD_DEBUG=1 bash "$COORD" "$@"; }

pass=0; failcount=0
ok()   { echo "PASS  $1"; pass=$((pass+1)); }
bad()  { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# assert_eq <name> <expected> <actual>
assert_eq() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "expected='$2' actual='$3'"; fi; }
# assert_contains <name> <needle> <haystack>
assert_contains() { case "$3" in *"$2"*) ok "$1" ;; *) bad "$1" "needle='$2' not in: $3" ;; esac; }
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
  {"__typename":"ProjectV2SingleSelectField","id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_done","name":"Done"}]},
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
    {"status":{"name":"Ready"},"phase":{"name":"P3 Governance"},"blockedBy":{"text":"RESOLVED: shipped last week"},"content":{"__typename":"Issue","number":203,"title":"Legacy prose in the field","url":"https://github.com/FS-GG/FS.GG.Governance/issues/203","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4989}}
JSON

# Board pages for `lint` — same items connection, but carrying each epic's sub-issues. Covers every
# invariant plus two negatives (a clean epic, a childless NON-epic) so the checks cannot pass by
# firing on everything.
#   #400 [epic], zero children                          -> EPIC-NO-CHILDREN
#   #401 [epic], board Done, child #403 still OPEN      -> EPIC-DONE-OPEN-CHILD
#   #404 [epic], totalCount 150 but 2 nodes visible     -> EPIC-CHILDREN-TRUNCATED
#   #405 non-epic, Status Done but issue OPEN           -> DONE-STATUS-OPEN-ISSUE (note, not an error)
#   #406 [epic], board Done, every child CLOSED         -> clean
#   #407 non-epic, zero children                        -> clean (the check is epic-scoped)
cat >"$FIXTURES/lint-p1.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":true,"endCursor":"LCUR1"},
  "nodes":[
    {"status":{"name":"Backlog"},"content":{"__typename":"Issue","number":400,"title":"[sdd] [epic] Gap A: orphan","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/400","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":0,"nodes":[]}}},
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
    {"status":{"name":"In progress"},"content":{"__typename":"Issue","number":404,"title":"[epic] Too many children to see","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/404","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"},"subIssues":{"totalCount":150,"nodes":[
      {"number":410,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}},
      {"number":411,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}]}}},
    {"status":{"name":"Done"},"content":{"__typename":"Issue","number":405,"title":"A merged PR that left its issue open","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.Templates/issues/405","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"},"subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Done"},"content":{"__typename":"Issue","number":406,"title":"[epic] Properly finished","state":"CLOSED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/406","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":1,"nodes":[
      {"number":412,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}},
    {"status":{"name":"Ready"},"content":{"__typename":"Issue","number":407,"title":"An ordinary card, no children","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/407","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":0,"nodes":[]}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4969}}
JSON

# `done --flip` + epic_rollup. Two chains:
#   #42 -> epic #300: children #42 (CLOSED, board Done) and #43 (OPEN, board Done).  Must HOLD.
#          Board Status alone would say 2/2 Done — the bug that flipped FS-GG/.github#235.
#   #44 -> epic #301: children #44 and #45, both CLOSED and board Done.              Must FLIP.
cat >"$FIXTURES/done-42.json" <<'JSON'
{"data":{"repository":{"issue":{"number":42,"title":"child of an unfinished epic","url":"https://github.com/FS-GG/FS.GG.SDD/issues/42","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[{"number":7,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/7","merged":true,"mergedAt":"2026-07-01T10:00:00Z","mergeCommit":{"abbreviatedOid":"abc1234"}}]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":{"number":300}}}},"rateLimit":{"cost":1,"remaining":4968}}
JSON
cat >"$FIXTURES/rollup-42.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":{
  "number":300,"url":"https://github.com/FS-GG/FS.GG.SDD/issues/300","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},
  "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
  "subIssues":{"totalCount":2,"nodes":[
    {"number":42,"state":"CLOSED","projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}},
    {"number":43,"state":"OPEN","projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}}
  ]}}}}},"rateLimit":{"cost":1,"remaining":4967}}
JSON
cat >"$FIXTURES/done-44.json" <<'JSON'
{"data":{"repository":{"issue":{"number":44,"title":"last child of a finished epic","url":"https://github.com/FS-GG/FS.GG.SDD/issues/44","state":"CLOSED",
  "closedByPullRequestsReferences":{"nodes":[{"number":9,"url":"https://github.com/FS-GG/FS.GG.SDD/pull/9","merged":true,"mergedAt":"2026-07-02T10:00:00Z","mergeCommit":{"abbreviatedOid":"def5678"}}]},
  "projectItems":{"nodes":[{"project":{"number":12,"title":"Coordination"},"status":{"name":"In progress"}}]},
  "parent":{"number":301}}}},"rateLimit":{"cost":1,"remaining":4966}}
JSON
cat >"$FIXTURES/rollup-44.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":{
  "number":301,"url":"https://github.com/FS-GG/FS.GG.SDD/issues/301","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},
  "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
  "subIssues":{"totalCount":2,"nodes":[
    {"number":44,"state":"CLOSED","projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}},
    {"number":45,"state":"CLOSED","projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"Done"}}]}}
  ]}}}}},"rateLimit":{"cost":1,"remaining":4965}}
JSON
cat >"$FIXTURES/rollup-none.json" <<'JSON'
{"data":{"repository":{"issue":{"parent":null}}},"rateLimit":{"cost":1,"remaining":4964}}
JSON

# ---- gh stub ------------------------------------------------------------------------------------
cat >"$STUB/gh" <<STUB
#!/usr/bin/env bash
set -euo pipefail
sub="\${1:-}"; sub2="\${2:-}"
args=("\$@")

if [ "\$sub" = "api" ] && [ "\$sub2" = "graphql" ]; then
  echo g >>"\$GH_GRAPHQL_COUNT"
  q=""; num=""
  for a in "\$@"; do case "\$a" in query=*) q="\${a#query=}";; num=*) num="\${a#num=}";; esac; done
  hascur=""; for a in "\$@"; do case "\$a" in cursor=*) hascur=1;; esac; done
  # Order matters: the done + rollup queries both select projectItems, and lint shares the
  # items(first:...) connection with the ready/next scan. Discriminate on the narrower marker first.
  if   printf '%s' "\$q" | grep -q 'projectsV2';                      then cat "$FIXTURES/projects.json"
  elif printf '%s' "\$q" | grep -q 'closedByPullRequestsReferences';  then cat "$FIXTURES/done-\$num.json"
  elif printf '%s' "\$q" | grep -q 'items(first' && printf '%s' "\$q" | grep -q 'subIssues'; then
    if [ -n "\$hascur" ]; then cat "$FIXTURES/lint-p2.json"; else cat "$FIXTURES/lint-p1.json"; fi
  elif printf '%s' "\$q" | grep -q 'items(first';      then
    if [ -n "\$hascur" ]; then cat "$FIXTURES/board-items-p2.json"; else cat "$FIXTURES/board-items-p1.json"; fi
  elif printf '%s' "\$q" | grep -q 'subIssues';        then
    if [ -f "$FIXTURES/rollup-\$num.json" ]; then cat "$FIXTURES/rollup-\$num.json"
    else cat "$FIXTURES/rollup-none.json"; fi
  elif printf '%s' "\$q" | grep -q 'projectV2(number'; then cat "$FIXTURES/fields.json"
  elif printf '%s' "\$q" | grep -q 'projectItems';     then cat "$FIXTURES/item.json"
  else echo '{"data":{},"rateLimit":{"cost":1,"remaining":4999}}'; fi
  exit 0
fi

if [ "\$sub" = "api" ] && [ "\$sub2" = "rate_limit" ]; then
  expr=""; n=\${#args[@]}
  for ((i=0;i<n;i++)); do [ "\${args[i]}" = "--jq" ] && expr="\${args[i+1]}"; done
  payload='{"resources":{"graphql":{"remaining":4321,"limit":5000,"reset":1751630400},"core":{"remaining":4990,"limit":5000,"reset":1751630400}}}'
  if [ -n "\$expr" ]; then printf '%s' "\$payload" | jq -r "\$expr"; else printf '%s' "\$payload"; fi
  exit 0
fi

if [ "\$sub" = "api" ]; then
  echo r >>"\$GH_REST_COUNT"
  inm=""; path=""; n=\${#args[@]}
  for ((i=1;i<n;i++)); do
    case "\${args[i]}" in
      -H) h="\${args[i+1]}"; case "\$h" in "If-None-Match: "*) inm="\${h#If-None-Match: }";; esac ;;
      repos/*) path="\${args[i]}" ;;
    esac
  done
  etag='"issues-etag-v1"'
  if [ -n "\$inm" ] && [ "\$inm" = "\$etag" ]; then
    echo "gh: HTTP 304 Not Modified" >&2; exit 1
  fi
  printf 'HTTP/2.0 200 OK\r\n'
  printf 'ETag: %s\r\n' "\$etag"
  printf '\r\n'
  cat "$FIXTURES/issues.json"
  exit 0
fi

if [ "\$sub" = "project" ] && [ "\$sub2" = "item-edit" ]; then
  printf 'item-edit %s\n' "\$*" >>"\$GH_LOG"; exit 0
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

# (6) ready/next: thrifty whole-board read, paginated, client-side filtered.
before_ready="$(gcount)"
ready_all="$(run ready --json 2>/dev/null)"
assert_eq "ready: paginates in exactly 2 GraphQL calls" "$((before_ready + 2))" "$(gcount)"
assert_eq "ready: excludes Done by default (8 of 10 items)" "8" "$(jq 'length' <<<"$ready_all")"
assert_contains "ready: keeps the Ready item"   '99'  "$(jq -c '[.[].number]' <<<"$ready_all")"
assert_contains "ready: keeps a Backlog item"   '127' "$(jq -c '[.[].number]' <<<"$ready_all")"
assert_eq "ready: drops the Done item (#54)"    "false" "$(jq 'any(.[]; .number==54)' <<<"$ready_all")"
assert_eq "ready --repo .github: only #54 exists there and it is Done -> empty" \
  "0" "$(run ready --repo .github --json 2>/dev/null | jq 'length')"
assert_eq "ready --status Done: widens past 'not Done' -> #54" \
  "54" "$(run ready --status Done --json 2>/dev/null | jq -r '.[0].number')"
assert_eq "ready --phase 'P2': substring-matches the phase" \
  "127" "$(run ready --phase P2 --json 2>/dev/null | jq -r '.[0].number')"

assert_contains "next: picks the Ready item first" "FS.GG.Templates#99" "$(run next 2>/dev/null)"
assert_contains "next --repo FS.GG.SDD: no Ready -> falls back to Backlog #127" \
  "FS.GG.SDD#127" "$(run next --repo FS.GG.SDD 2>/dev/null)"
assert_eq "ready --repo templates: registry short-id resolves to FS.GG.Templates (#99)" \
  "99" "$(run ready --repo templates --all --json 2>/dev/null | jq -r '.[0].number')"
assert_contains "next --repo sdd: short-id resolves to FS.GG.SDD (Backlog #127)" \
  "FS.GG.SDD#127" "$(run next --repo sdd 2>/dev/null)"
assert_contains "next: unknown repo reports no startable item (stderr)" \
  "no startable item" "$(run next --repo nope 2>&1 >/dev/null)"

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
assert_contains "next: says WHICH item it skipped"      "skipping FS.GG.Rendering#200" "$skipnote"
assert_contains "next: names the open blocker + state"  "FS.GG.SDD#127 (open)"         "$skipnote"
assert_contains "next: names the bare-ref blocker too"  "FS.GG.Rendering#201 (open)"   "$skipnote"

gov="$(run next --repo governance 2>&1 >/dev/null)"
assert_contains "next: unknown-state blocker is reported as such" "(unknown)"     "$gov"
assert_contains "next: legacy prose is reported as unparseable"   "(unparseable)" "$gov"
assert_contains "next: all candidates blocked -> says so, not a bare 'nothing to do'" \
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
assert_eq "lint: a childless [epic] is EPIC-NO-CHILDREN (#400)"        "EPIC-NO-CHILDREN "     "$(codes '#400')"
assert_eq "lint: Done over an open child (#401)"                       "EPIC-DONE-OPEN-CHILD " "$(codes '#401')"
assert_eq "lint: >100 children is EPIC-CHILDREN-TRUNCATED (#404)"      "EPIC-CHILDREN-TRUNCATED " "$(codes '#404')"
assert_eq "lint: Done status on an open issue is a NOTE (#405)"        "DONE-STATUS-OPEN-ISSUE " "$(codes '#405')"
assert_eq "lint: a properly finished epic is clean (#406)"             ""                      "$(codes '#406')"
assert_eq "lint: a childless NON-epic is clean — the check is epic-scoped (#407)" "" "$(codes '#407')"
assert_contains "lint: EPIC-DONE-OPEN-CHILD names the open child" "#403" \
  "$(jq -r '.[] | select(.code=="EPIC-DONE-OPEN-CHILD") | .detail' <<<"$lint_json")"
assert_eq "lint: severities — 3 errors, 1 note" "3 1" \
  "$(jq -r '"\([.[]|select(.severity=="error")]|length) \([.[]|select(.severity=="note")]|length)"' <<<"$lint_json")"

assert_fails "lint: exits non-zero when an invariant is broken" run lint
assert_contains "lint: text output is greppable" "FSGG-LINT ERROR  EPIC-NO-CHILDREN" "$(run lint 2>/dev/null || true)"
assert_contains "lint: prints an error/note tally on stderr" "3 error(s), 1 note(s)" \
  "$(run lint 2>&1 >/dev/null || true)"

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

: >"$GH_LOG"
flip="$(run done 'FS.GG.SDD#44' --pr 9 --flip 2>/dev/null)"
assert_contains "rollup: FLIPS when every child is Done AND closed" "FSGG-DONE   FS.GG.SDD#301 (epic)" "$flip"
assert_contains "rollup: the stamp says Done + closed" "all 2 children Done + closed" "$flip"
# Two Status writes: the child, then the epic it completed.
assert_eq "rollup: flipping writes Status twice (child, then epic)" "2" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# budget reads both meters.
bud="$(run budget)"
assert_contains "budget: reports graphql meter" "graphql" "$bud"
assert_contains "budget: reports remaining"     "remaining" "$bud"

# ================================================================================================
echo "fsgg-coord fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::fsgg-coord fixture FAILED"; exit 1; }
echo "fsgg-coord fixture — OK"
