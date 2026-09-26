# ADR-0084: Semantic reuse may advance delivery without cancelling coherent validation

- **Status:** Accepted
- **Date:** 2026-09-08; scoped-execution amendment 2026-09-26
- **Decision owners:** FS-GG accountable programme owner; QSEL-01
- **Affects:** routine delivery, roadmap and board drivers, coherent candidate validation

## Context

Routine candidates often change documentation, projections, or generated bindings without changing the
semantic subject covered by an earlier complete qualification. Waiting for a duplicate full run makes cheap
work expensive. Treating path names or a cache hit as proof is unsafe, while cancelling the full run removes
the independent signal that can expose an incomplete classifier.

## Decision

Every candidate receives a cheap content-and-semantic identity classification with exactly one disposition:
`current`, `reused`, `deferred`, or `failed`. Reuse is valid only when the producer's content-addressed receipt
binds the exact candidate, an independently empty semantic delta, matching behavioral, compiled-contract,
toolchain/profile, verification-bound, formal-corpus, and harness identities, any required generated-binding
correspondence, and authentic complete unexpired prior evidence. Unknown, malformed, stale, or mismatched
inputs cannot become reuse.

The cheap classification runs first. After an accepted classification, the candidate's coherent execution
obligation starts. By default this is the full run. A narrowly scoped run is permitted only for a ready pull
request with a validated exact-head `reused` disposition, authentic complete prior **full** evidence, a
complete candidate-to-base diff, unchanged protected formal and tool identities, and changed paths confined
to the producer's audited nonauthority allowlist. The producer records the selected scope and its evidence
in a versioned plan and aggregate receipt. Unknown paths, missing or malformed evidence, and policy changes
select the full run or fail closed; they never silently select scoped execution.

The first scoped profile retains the base formal fragment and every existing nonformal partition, including
the complete unit and architecture suites. It omits only unrelated formal scenarios and records them as
outside this candidate's scope, never as passing fragments. Main, nightly, merge-group, dispatch, release,
and comprehensive acceptance boundaries retain full execution. A scoped aggregate cannot become a full
reuse donor or satisfy a full recovery obligation. The full canonical qualification receipt keeps its
complete-inventory validation unchanged.

For a validated exact-head `reused` disposition, native delivery and the selected coherent run start
concurrently. Independent obligations shard and execute concurrently where their isolation contract permits,
and coherent candidate runs may also overlap. `current`, or the absence of valid reuse, must await a
successful exact-head **full** coherent run. `deferred` and `failed` do not authorize delivery. Reuse never
cancels the selected coherent run.

Native exact-head merge and readback remain the delivery boundary. A pending coherent run after merge remains
`pending`, not `disputed`, and does not rewrite delivered source state. A later coherent failure changes that
candidate's validation state to `disputed` and blocks dependent acceptance, activation, publication, or reuse
until repaired; it does not erase native merge history. A late pass closes the pending validation normally.

The nightly producer recovery performs an oldest-first, capacity-bounded scan only of explicitly pending
candidate identities. It does not infer work from arbitrary open PRs, cancel active candidates, or create an
unbounded retry service.

Routine remains the default delivery process. Only a recorded explicit human instruction selecting heavyweight
process for named scope changes that route. Reuse, formal work, protected paths, or a disputed result do not by
themselves create heavyweight ceremony; their substantive checks and protected effects remain fail-closed.

## Consequences

- Valid semantic reuse shortens the merge path while the recorded coherent obligation continues running.
- A scoped candidate has less independent formal detection than a full candidate. Audited source boundaries,
  authentic full prior evidence, the retained base fragment, and periodic full boundaries limit that loss;
  they do not make the two profiles equivalent.
- A classifier bug or late failure can produce a visible disputed state instead of silently certifying
  dependent adoption.
- Delivery, coherent validation, and later activation remain separate facts with distinct failure handling.
- The producer owns classification, sharding, aggregate receipts, and bounded recovery. Consumers validate and
  apply those receipts rather than reimplementing semantic comparison.

## Compatibility and rollout

The Coordination producer contract lands and is read back before consumers adopt it. Consumer helpers retain
the existing no-selection behavior: without a valid reuse receipt they wait for a successful current coherent
full run. The scoped profile remains disabled until its versioned plan, selected-inventory aggregation,
full-donor refusal, recovery distinction, and adversarial fixtures pass together. No branch-protection,
publication, service, or heavyweight-process setting changes in this decision.
