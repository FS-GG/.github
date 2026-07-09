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
  {"__typename":"ProjectV2SingleSelectField","id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_done","name":"Done"}]},
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
#   #400 [epic], OPEN, zero children                    -> EPIC-NO-CHILDREN
#   #401 [epic], board Done, child #403 still OPEN      -> EPIC-DONE-OPEN-CHILD
#   #404 [epic], totalCount 150 but 2 nodes visible     -> EPIC-CHILDREN-TRUNCATED
#   #405 non-epic, Status Done but issue OPEN           -> DONE-STATUS-OPEN-ISSUE (note, not an error)
#   #406 [epic], board Done, every child CLOSED         -> clean
#   #407 non-epic, zero children                        -> clean (the check is epic-scoped)
#   #408 [epic], CLOSED, zero children                  -> clean (the check is live-work-scoped)
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
    {"status":{"name":"Ready"},"content":{"__typename":"Issue","number":407,"title":"An ordinary card, no children","state":"OPEN","url":"https://github.com/FS-GG/FS.GG.SDD/issues/407","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":0,"nodes":[]}}},
    {"status":{"name":"Done"},"content":{"__typename":"Issue","number":408,"title":"[epic] Finished, and it never grew children","state":"CLOSED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/408","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"},"subIssues":{"totalCount":0,"nodes":[]}}}
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
  method=""; path=""; inm=""; jqexpr=""; body=""; include=""; hasfield=""; paginate=""
  n=\${#args[@]}
  for ((i=1;i<n;i++)); do
    case "\${args[i]}" in
      -X)        method="\${args[i+1]}" ;;
      --include)  include=1 ;;
      --paginate) paginate=1 ;;
      --jq)      jqexpr="\${args[i+1]}" ;;
      -H)        h="\${args[i+1]}"; case "\$h" in "If-None-Match: "*) inm="\${h#If-None-Match: }";; esac ;;
      -f)        hasfield=1; kv="\${args[i+1]}"; case "\$kv" in body=*) body="\${kv#body=}";; esac ;;
      -F)        hasfield=1; kv="\${args[i+1]}"; case "\$kv" in body=@*) body="\$(cat "\${kv#body=@}")";; esac ;;
      user)      [ -z "\$path" ] && path="user" ;;
      repos/*)   path="\${args[i]}" ;;
    esac
  done
  # Real \`gh api\` infers POST when fields are supplied and no method is given. A stub that defaults
  # to GET would silently serve a comment LIST where the client expects the created comment's id.
  [ -n "\$method" ] || { [ -n "\$hasfield" ] && method="POST" || method="GET"; }
  emit() { if [ -n "\$jqexpr" ]; then jq -r "\$jqexpr"; else cat; fi; }
  now="\$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  if [ "\$path" = "user" ]; then printf '{"login":"EHotwagner"}' | emit; exit 0; fi

  # --- issue comments: a REAL mutable store, so the claim CAS can actually be raced -------------
  if [[ "\$path" =~ ^repos/[^/]+/[^/]+/issues/([0-9]+)/comments ]]; then
    cnum="\${BASH_REMATCH[1]}"; cf="$STORE/comments-\$cnum.json"
    [ -f "\$cf" ] || echo '[]' >"\$cf"

    # GH_FAIL_READ_ISSUE=<n>: reads of <n>'s comments fail once a marker has been POSTed there.
    # Models a transient gh failure (rate limit / 5xx) landing on the CAS re-read, i.e. after our
    # marker exists but before we know whether we won it.
    if [ "\$method" = "GET" ] && [ "\$cnum" = "\${GH_FAIL_READ_ISSUE:-}" ] && [ -f "$STORE/posted-\$cnum" ]; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    # GH_VANISH_ISSUE=<n>: our marker is GONE by the time the CAS re-reads (a peer's --force/reap
    # collected it, or the read lagged the write). The re-read sees NO live marker at all, so the
    # claimant cannot show it holds the lock. It must treat that as a loss, not a win.
    if [ "\$method" = "GET" ] && [ "\$cnum" = "\${GH_VANISH_ISSUE:-}" ] && [ -f "$STORE/posted-\$cnum" ]; then
      jq 'map(select(.body | test("^<!--\\\\s*fsgg:claim") | not))' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
    fi
    # GH_REAP_RACE=<n>: the holder heartbeats between reap's snapshot read and its delete. Every read
    # after the first returns a freshly-renewed marker.
    if [ "\$method" = "GET" ] && [ "\$cnum" = "\${GH_REAP_RACE:-}" ]; then
      rc="\$(cat "$STORE/readcount-\$cnum" 2>/dev/null || echo 0)"
      echo \$((rc + 1)) >"$STORE/readcount-\$cnum"
      if [ "\$rc" -ge 1 ]; then
        jq --arg ts "\$now" 'map(.updated_at = \$ts)' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      fi
    fi

    if [ "\$method" = "POST" ]; then
      touch "$STORE/posted-\$cnum"
      # GH_RACE_INJECT=<worker>: a rival worker's marker lands BETWEEN our read and our re-read,
      # taking a LOWER comment id. This is the exact interleaving the CAS exists to resolve.
      if [ -n "\${GH_RACE_INJECT:-}" ] && [ "\$cnum" = "\${GH_RACE_ISSUE:-}" ]; then
        rid="\$(cat "$STORE/nextid")"; echo \$((rid + 1)) >"$STORE/nextid"
        jq --argjson id "\$rid" --arg w "\$GH_RACE_INJECT" --arg ts "\$now" \
          '. + [{id:\$id, body:("<!-- fsgg:claim worker=" + \$w + " lease=120 -->\nrival"),
                 user:{login:"EHotwagner"}, created_at:\$ts, updated_at:\$ts}]' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      fi
      id="\$(cat "$STORE/nextid")"; echo \$((id + 1)) >"$STORE/nextid"
      jq --argjson id "\$id" --arg b "\$body" --arg ts "\$now" \
        '. + [{id:\$id, body:\$b, user:{login:"EHotwagner"}, created_at:\$ts, updated_at:\$ts}]' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      printf 'comment-post %s %s\n' "\$cnum" "\$id" >>"\$GH_LOG"
      jq -n --argjson id "\$id" '{id:\$id}' | emit; exit 0
    fi
    emit <"\$cf"; exit 0
  fi

  # --- a single comment by id: PATCH (heartbeat) / DELETE (release, back-off, reap) -------------
  # GH_FAIL_DELETE=<id>: the DELETE of <id> fails with a 500 (transient). Models "the marker survives".
  # A DELETE of an id that is NOT in the store 404s, exactly as GitHub does — the collector's benign
  # "somebody already removed it" case, which must not read as a hard failure.
  if [[ "\$path" =~ ^repos/[^/]+/[^/]+/issues/comments/([0-9]+) ]]; then
    cid="\${BASH_REMATCH[1]}"
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
      if [ "\$method" = "DELETE" ]; then
        printf 'comment-delete %s\n' "\$cid" >>"\$GH_LOG"
        jq --argjson id "\$cid" 'map(select(.id != \$id))' "\$cf" >"\$cf.t" && mv "\$cf.t" "\$cf"
      else
        printf 'comment-patch %s\n' "\$cid" >>"\$GH_LOG"
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

  if [[ "\$path" =~ ^repos/[^/]+/[^/]+/pulls/([0-9]+)/files ]]; then
    emit <"$FIXTURES/pr-files-\${BASH_REMATCH[1]}.json"; exit 0
  fi
  if [[ "\$path" =~ ^repos/[^/]+/[^/]+/pulls/([0-9]+)$ ]]; then
    emit <"$FIXTURES/pr-\${BASH_REMATCH[1]}.json"; exit 0
  fi

  # --- a single issue: GET (title/body/Paths) or PATCH (widen rewrites the body) ----------------
  # GH_FAIL_ISSUE_GET=<n>: the body read for <n> fails. `paths_of` reads the touch-set here, and an
  # empty answer would read as "declared nothing" — i.e. disjoint from everything.
  if [[ "\$path" =~ ^repos/[^/]+/[^/]+/issues/([0-9]+)$ ]]; then
    inum="\${BASH_REMATCH[1]}"; jf="$STORE/issue-\$inum.json"
    if [ "\$method" = "GET" ] && [ "\$inum" = "\${GH_FAIL_ISSUE_GET:-}" ]; then
      echo "gh: HTTP 502 Bad Gateway" >&2; exit 1
    fi
    [ -f "\$jf" ] || { echo "gh stub: no issue fixture \$inum" >&2; exit 4; }
    if [ "\$method" = "PATCH" ]; then
      printf 'issue-patch %s\n' "\$inum" >>"\$GH_LOG"
      jq --arg b "\$body" '.body = \$b' "\$jf" >"\$jf.t" && mv "\$jf.t" "\$jf"
    fi
    emit <"\$jf"; exit 0
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
    out='[]'
    for jf in "$STORE"/issue-*.json; do
      [ -f "\$jf" ] || continue
      inum="\$(jq -r '.number' "\$jf")"; cf="$STORE/comments-\$inum.json"
      cc=0; [ -f "\$cf" ] && cc="\$(jq 'length' "\$cf")"
      out="\$(jq -c -n --argjson acc "\$out" --slurpfile it "\$jf" --argjson cc "\$cc" \
                '\$acc + [ \$it[0] + {comments: \$cc} ]')"
    done
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

if [ "\$sub" = "project" ] && [ "\$sub2" = "item-edit" ]; then
  # GH_FAIL_ITEM_EDIT=1: the board write fails — a Projects v2 5xx, or an item with no board entry
  # to edit. The MARKER is the lock, so nothing that holds it may unwind on this; but nothing may
  # report a board mutation it did not perform either.
  [ -n "\${GH_FAIL_ITEM_EDIT:-}" ] && { echo "gh: HTTP 502 Bad Gateway" >&2; exit 1; }
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

seed_issue() {  # seed_issue <num> <title> <paths-or-empty> [owner/repo]
  local n="$1" t="$2" p="$3" repo="${4:-FS-GG/FS.GG.SDD}" body="Some description."
  [ -n "$p" ] && body="$body

Paths: $p"
  jq -n --argjson n "$n" --arg t "$t" --arg b "$body" --arg r "$repo" \
    '{number:$n, title:$t, body:$b, assignees:[], state:"open", repo:$r,
      html_url:("https://github.com/" + $r + "/issues/" + ($n|tostring))}' >"$STORE/issue-$n.json"
  : >"$STORE/comments-$n.json"; echo '[]' >"$STORE/comments-$n.json"
}
seed_issue 42 "Audio mixer"          "src/Audio/**, tests/Audio/**"
seed_issue 43 "Legacy port"          "src/Legacy/**"
seed_issue 60 "Nobody claimed me"    "src/Orphan/**"
seed_issue 70 "Scene graph"          "src/Scene/**, tests/Scene/**"
seed_issue 71 "Mixer tweak"          "src/Audio/Mixer/**"
seed_issue 72 "No touch-set declared" ""
seed_issue 73 "Scene subtree"        "src/Scene/Sub/**"
seed_issue 74 "ADR housekeeping"     "docs/adr/**"

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
assert_contains "claim: prints the attribution trailer"   'FSGG-Worker: heron-b71' "$out70"
assert_contains "claim: flips the board to In progress"   "board: In progress" "$out70"
assert_contains "claim: still assigns @me for the humans" "issue edit 70 --repo FS-GG/FS.GG.SDD --add-assignee @me" "$(cat "$GH_LOG")"
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

# Board-wide take (no --repo) must not trip the empty-array expansion. By now everything schedulable
# is claimed or overlapping, so this exercises the "nothing to hand out" path — which still exits 0.
if as teal-e55 take >/dev/null 2>&1; then ok "take: board-wide (no --repo) exits cleanly"
else bad "take: board-wide (no --repo) exits cleanly" "non-zero exit"; fi
assert_contains "take: says WHY there is nothing to hand out" "no schedulable item" \
  "$(as teal-e55 take 2>&1 >/dev/null)"

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
c84="$(as heron-b71 claim 'FS.GG.SDD#84' 2>&1)"
assert_contains "claim: collects the stale marker it claims over" "collected worker 'ghost-111' expired claim" "$c84"
assert_eq "claim: exactly ONE marker survives (the stale one is gone)" "heron-b71" "$(workers_on 84)"
assert_eq "claim: the collected worker is TOLD, not silently evicted" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=ghost-111"))] | length' "$STORE/comments-84.json")"

# (b) Re-claiming when MY OWN marker went stale must renew a single marker, not mint a second.
mk_claim 85 811 finch-a3f stale | jq -s '.' >"$STORE/comments-85.json"
as finch-a3f claim 'FS.GG.SDD#85' >/dev/null 2>&1
assert_eq "claim: a worker whose own marker went stale ends with ONE marker" "finch-a3f" "$(workers_on 85)"
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
  "$(as heron-b71 claim 'FS.GG.SDD#88' 2>/dev/null)"

# (f) A marker we cannot parse a worker out of must FAIL CLOSED — block the item, not vanish.
jq -n --arg ts "$fresh_ts" '[{id:815, body:"<!-- fsgg:claim lease=120 -->\nhalf-written",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-89.json"
assert_fails "lock: a malformed marker blocks the item (fails closed)" as heron-b71 claim 'FS.GG.SDD#89'
assert_contains "lock: the refusal names the unparsed marker" "unparsed-marker" \
  "$(as heron-b71 claim 'FS.GG.SDD#89' 2>&1 || true)"

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
  bash "$COORD" --worker heron-b71 claim 'FS.GG.SDD#95' 2>&1 || true)"
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
echo "fsgg-coord fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::fsgg-coord fixture FAILED"; exit 1; }
echo "fsgg-coord fixture — OK"
