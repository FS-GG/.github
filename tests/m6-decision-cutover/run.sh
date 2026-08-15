#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

core_out="$(mktemp)"
cli_out="$(mktemp)"
trap 'rm -f "$core_out" "$cli_out"' EXIT

dotnet test tests/FS.GG.Coord.Core.Tests/FS.GG.Coord.Core.Tests.fsproj -c Release --no-restore \
  --filter 'FullyQualifiedName~StructuredDecisionTests' >"$core_out"
dotnet test tests/FS.GG.Coord.Cli.Tests/FS.GG.Coord.Cli.Tests.fsproj -c Release --no-restore \
  --filter 'FullyQualifiedName~DeliveryRouteCliTests|FullyQualifiedName~DeliveryApplicationTests|FullyQualifiedName~ReviewApplicationTests' >"$cli_out"

grep -q 'Failed:     0' "$core_out"
grep -q 'Failed:     0' "$cli_out"

required_tests=(
  'M4 route authorization is bound'
  'M4 append requires the exact previous digest'
  'M6 effective route is derived only'
  'M4 narrative body edits cannot affect'
  'm6_structured_chain_drives_effective_state'
  'm6_v1_prose_chain_is_inert'
  'm6_ledger_refuses_gaps_stale_links_subjects_and_critics'
  'm6_generic_critic_and_wrong_acceptance_links_fail'
  'm6_escalation_requires_typed_repair_phase'
  'm6_typed_escalation_and_repair_drive_state'
  'm6_missing_malformed_and_misplaced_audit_fails'
  'm6_moved_head_parses_only_new_live_generation'
  'historical_prose_cannot_authorize'
  'typed_acceptance_reaches_accept'
  'M6 typed human park authorizes Blocked without prose authority'
)
for name in "${required_tests[@]}"; do
  grep -Fq "$name" tests/FS.GG.Coord.Core.Tests/StructuredDecisionTests.fs tests/FS.GG.Coord.Core.Tests/LifecycleProjectionTests.fs tests/FS.GG.Coord.Cli.Tests/ReviewApplicationTests.fs
done

grep -Fq 'LifecycleProjection.isHumanPark watermark.Intent' src/FS.GG.Coord.Cli/Client.fs

retired_symbols='DeliveryRouteApplication\.decode([^S]|$)|RouteReadClassification|classifyRoute|toLegacyReceipt|projectStructuredReview|normalizeStructuredReviews|EvidenceClassification|FSGG_COORD_LIFECYCLE_PROJECTION'
if grep -Enr --include='*.fs' --include='*.fsi' "$retired_symbols" src; then
  echo 'retired decision/lifecycle authority remains in production source' >&2
  exit 1
fi

printf '%s\n' \
  'decision-cutover coverage map: 15 named inversions present' \
  'route: digest/tamper/append/body-inert/effective/record-show-claim' \
  'review: parse/state/backlinks/escalation/repair/diff-audit/head-move/critic succession/landability' \
  'legacy: prose and v1 route evidence cannot authorize; retired production symbols absent'
