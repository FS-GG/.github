#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

core_out="$(mktemp)"
cli_out="$(mktemp)"
artifacts="$(mktemp -d)"
trap 'rm -f "$core_out" "$cli_out"; rm -rf "$artifacts"' EXIT

dotnet build tests/FS.GG.Coord.Core.Tests/FS.GG.Coord.Core.Tests.fsproj -c Release -p:RestoreLockedMode=true --artifacts-path "$artifacts" >/dev/null
dotnet build tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj -c Release -p:RestoreLockedMode=true --artifacts-path "$artifacts" >/dev/null
# DeliveryApplicationTests deliberately locates the shared cross-language corpus by walking upward
# from its assembly. Preserve that non-vacuous subject inside the redirected artifact tree.
mkdir -p "$artifacts/tests/delivery-leading-line"
cp tests/delivery-leading-line/corpus.json "$artifacts/tests/delivery-leading-line/corpus.json"

dotnet test tests/FS.GG.Coord.Core.Tests/FS.GG.Coord.Core.Tests.fsproj -c Release --no-build \
  --artifacts-path "$artifacts" --filter 'FullyQualifiedName~StructuredDecisionTests' >"$core_out"
dotnet test tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj -c Release --no-build \
  --artifacts-path "$artifacts" --filter 'FullyQualifiedName~DeliveryRouteCliTests|FullyQualifiedName~DeliveryApplicationTests|FullyQualifiedName~ReviewApplicationTests' >"$cli_out"

grep -q 'Failed:     0' "$core_out"
grep -q 'Failed:     0' "$cli_out"
grep -Eq 'Total:[[:space:]]+[1-9][0-9]*' "$core_out"
grep -Eq 'Total:[[:space:]]+[1-9][0-9]*' "$cli_out"

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
  grep -Fq "$name" tests/FS.GG.Coord.Core.Tests/StructuredDecisionTests.fs tests/FS.GG.Coord.Core.Tests/LifecycleProjectionTests.fs tests/FS.GG.Coord.Cli.Lifecycle.Tests/ReviewApplicationTests.fs
done

grep -Fq 'LifecycleProjection.isHumanPark watermark.Intent' src/FS.GG.Coord.Cli/Client.fs

# Lifecycle owns the live dispatch table itself. A command arm in Client.run, or a registration that
# points back through legacyHandler/runClient, would preserve the monolith while making inventory tests
# look green. Pin both sides of the cutover against the production composition subject.
if grep -Eq '^[[:space:]]*\|[[:space:]]*(DeliveryCmd|ReviewCmd|Landable|DoneCmd|VerifyPaths|RouteCmd)[[:space:]]*->' src/FS.GG.Coord.Cli/Client.fs; then
  echo 'Lifecycle dispatch arm remains in Client.fs' >&2
  exit 1
fi
lifecycle_composition="$(sed -n '/let private lifecycleProgramRegistrations =/,/let private lifecycleHandlers =/p' src/FS.GG.Coord.Cli/Program.fs)"
if grep -Eq 'legacyHandler|runClient' <<<"$lifecycle_composition"; then
  echo 'Lifecycle production registrations bounce through the legacy Client dispatcher' >&2
  exit 1
fi
for handler in delivery review deliveryRouteCmd landable doneCmd verifyPaths followupAudit; do
  grep -Fq "LiveHandlers.$handler" <<<"$lifecycle_composition"
done

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
