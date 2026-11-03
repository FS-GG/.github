# Owner continuation v1

This is the implemented wire interface for an existing accountable owner, extending canonical
coordination rather than adding a scheduler or writer. Source qualification, publication and named
caller/instruction adoption are separate; this reference establishes no live adoption.

## Select the qualified caller

The owning Delivery records the named qualification/adoption tuple: exact executable/source identity,
instruction revision, organization binding/enrollment and scope. Select that profile's physically verified
absolute caller per invocation through the existing explicit-bin tier. An unrelated PATH or stale source
build must not select it; other services' callers and the global resolver remain unchanged.

Invocation template; replace private-path operands using the adopted tuple:

```sh
FSGG_COORD_ENGINE_BIN="${QUALIFIED_CALLER:?select the adopted caller}" scripts/fsgg-coord board-v2 inspect --binding-file PRIVATE_BINDING --report-file NEW_PRIVATE_REPORT --owner-continuation-stdin < PRIVATE_OWNER_INPUT
```

The opt-in is organization inspection only, mutually exclusive with `--integrator-stdin` and invalid
for `refresh`. Use a new private report; `--previous-report-file` may supply display history. Product
interfaces remain separate. Ordinary isolated local checks already need no Root runtime approval under
[ADR-0096](../adr/0096-proportionate-verification-and-unrecoverable-holds.md); an unavailable successor
cannot create another approval gate.

## Closed input

The [decoder](../../src/FS.GG.Coord.Cli/BoardV2Application.fs) requires all fields below, including nullable
keys. Unknown/duplicate properties and duplicate slice/fact identities refuse. Input is bounded to
262,144 UTF-8 bytes, depth 16, 64 nonempty requests and 16 caller facts per request.

| Object | Exact fields |
|---|---|
| Root | `schema`, `integrator`, `repositoryCheckout`, `draft`, `requests` |
| Request | `sliceId`, `attempt`, `owner`, `revision`, `scopeDigest`, `action`, `original`, `envelope`, `facts` |
| Fact | `id`, `predicate`, `state`, `value`, `detail`, `support`, `observedAt`, `validUntil`, `evidence`, `supersedes`, `scopeDigest`, `artifactRevision` |

`schema` is `fsgg.coord.owner-continuation-input/1`. `integrator` is the existing closed
`fsgg.coord.board-v2-integrator-observations/2` document for binding5, or `/1` for binding4;
use its [existing contract and fixtures](../../tests/coord-board-v2-cli-join/INSPECT.md) and
`parseIntegratorObservations`, not a new planning record. Identity joins must match its slice.
Request `scopeDigest` may be null to derive that identity; every fact still requires the exact digest.

`repositoryCheckout` is null or the qualified local checkout path used for registered read-only
preflight. `draft` is null or exactly `{title, bodyFile}`: proposed PR title and private body-file path.
Neither executes creation. `original` is null or the retained original reference; `envelope` is null
or exactly `{nodeId, updatedAt, bodySha256}`.

Caller predicates are `source-ready`, `verification-passed`, `effect-clear`, `isolated`, `resource-share`,
`native-rollup`, `changes-required`, `independent-review`. A caller cannot supply native provenance;
attesting `native-rollup` does not prove CI. `known` uses Boolean `value` and empty `detail`; `unknown`
uses null and one reason; `conflict` uses null and nonempty reasons. Freshness requires current evidence,
valid intervals and at most five minutes' age. `artifactRevision` is null or an exact lowercase
40-hex source SHA (`Fact.ArtifactRevision`); source-ready/verification evidence must bind the preflight-observed artifact before
draft creation. Supersession must name retained, same-subject/predicate/issuer evidence without cycles.

Support is exactly `{kind, supplier}` for `reported`, or `kind: "attestation"` with `supplier`, `role`,
`model`, `effort`, `method`, `contributors`, `contributorsComplete`, `limitations`. Null model/effort
remain Unknown. Independent review requires role `reviewer`, complete contributors and a supplier distinct from the
owner and every declared contributor.

This **non-runnable schema fragment** illustrates only the closed reported-support object:

```json
{"kind":"reported","supplier":"synthetic-investigator"}
```

A full input requires live joins. The executed synthetic builder and rejection/CLI controls are in
[OwnerContinuationTests](../../tests/coord-board-v2-cli-join/OwnerContinuationTests.fs); no full example
has been improvised or executed here.

## Same-owner progression and authority

Use `action: "next"` for inferred progression:
**Implement → VerifyIsolated → OpenDraft → CI plus Review → Deliver → acceptance Await**.
Missing or conflicting facts hold the affected stage. The explicit actions `implement`, `verify-isolated`,
`review`, `deliver` remain diagnostic choices; `open-draft` is inferred, not a permitted input action.
Draft/merge advice requires the existing registered preflight. The actual qualified helpers perform
creation with draft=true, validation, merge and independent readback; advice grants none of these effects.
A source merge awaits complete acceptance rather than closing the Delivery or Outcome.

[Native envelope reads](../../src/FS.GG.Coord.GitHub/OwnerContinuationReads.fs) authenticate a Root-authored
comment and its editor after edits against canonical Root policy, on the native parent or observed owning
plan. The body is `<!-- fsgg:owner-continuation-envelope/v1 -->` followed by one closed
`fsgg.coord.owner-envelope/1` JSON object. It binds principal, invocation, owner/slice/attempt, source/head,
write domains, original run/attempt, issued/expiry times, immutable inputs and the covered steps
`observe-ci`, `merge-pr`. Supplied login/admin claims cannot replace these native joins. See the executed
[envelope builder](../../tests/coord-board-v2-cli-join/OwnerEnvelopeTests.fs).

Verified original success can advance the covered sequence without per-step Root approval; expired or
changed authority cannot. Mutable native clocks remain freshness joins; immutable source/body/state and
plan operands supply semantic identity. Dependency coverage binds identities, states and edge population
only: dependency bodies are unobserved. Obtain separate evidence when their content matters.

## Output and recovery

`fsgg.coord.board-v2-owner-inspection/1` contains one `ownerContinuation` path, not the legacy selection
payload; `changedOwnerContinuation` filters display changes. `authorityScopes` supplies immutable envelope
operands with explicit gaps; `contextViews` points to the existing bounded context-view command.

The [pure fold and encoder](../../src/FS.GG.Coord.Core/OwnerContinuation.fs) emit
`schema: fsgg.coord.owner-continuation/1` with `policyRevision`, `id`/`semanticDigest`, `inputDigest`,
`subject`, `owner`, `sliceId`,
`attempt`, `revision`, `scopeDigest`, `kind`, `action`, `original`, `condition`, `reasonCodes`, `determiningFacts`,
`nextEvent`, `grantRef`, `requiredRefresh`, `artifactRevision`, and `dispatchAuthority: false`.
Clock-only fresh refreshes can change inputDigest without new attention; changed source meaning,
artifact/original, decision or reasons changes semantic identity. History is display-only, never current facts.

Preserve `original` on **every** recommendation: ContinueOwner, ObserveOriginal, Await and RequestJudgment.
Resume its existing [observe-operation handler/cursor](../../src/FS.GG.Coord.Cli/OperationObservationApplication.fs).
All attention kinds remain meaningful: completed, failed, pull-request-closed, head-changed, run-changed,
population-changed, material-change, unknown, deadline, budget-exhausted, interrupted, observation-failure.
No failed return, missing history or expired clock authorizes resend, replacement identity or renewed budget.

The [user override](https://github.com/FS-GG/.github/issues/5281#issuecomment-6611681098) removes calendar-pilot
waiting, not technical qualification, actual adoption or necessary real recovery/rollback. The
[bounded historical replay](../../reports/coordination-engine-historical-replay-2026-11-03.json) retained
four usable cases, one unavailable and one attempt control, with five expected-decision matches including
the control. It has zero positive-advancement and real scoped-Await coverage; it establishes no live
operation, executor recovery or throughput/cost benefit.
