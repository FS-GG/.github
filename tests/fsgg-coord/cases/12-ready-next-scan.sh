#!/usr/bin/env bash
# case: ready next scan
# tier: smoke
# covers: ready next
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="12-ready-next-scan"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"


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


harness_report
