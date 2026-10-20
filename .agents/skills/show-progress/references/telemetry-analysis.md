# Telemetry, token efficiency and process pathologies

Use telemetry to explain the cost of useful delivery and find practical improvements.
Keep the main report compact; add detail when requested or when it changes a decision.

## Read existing evidence

Select the report's items, time window and cutoff before comparing totals. Prefer an existing
canonical item-detail or efficiency snapshot from the configured, authorized telemetry source.
Use its supported read-only summary/query route when a fresh read is needed. Start from the
[local telemetry reference](https://github.com/FS-GG/.github/blob/main/docs/reference/local-telemetry-store.md)
and [measurement contract](https://github.com/FS-GG/.github/blob/main/contracts/process-efficiency/README.md);
verify the selected installed caller's capabilities. Source implementation does not prove capture,
installation or coverage. Do not activate, drain, reconcile, migrate or backfill a store for a report,
launch an analyst, or change producer configuration.

State the observed population and missing portion: selected items, expected versus observed
invocations, root/child/follow-up coverage, attribution gaps, and open or abandoned work. Separate
event/source freshness, ingestion freshness and publication freshness; a fresh dashboard render
does not establish fresh capture. Reuse existing assessments with their evidence and limitations.
Do not present proposed or uncalibrated assessment behavior as an operational measurement.

When canonical data is unavailable, an existing bounded local diagnostic may support explicitly
limited observations. The maintained `scripts/v2-progress/collect-local.py` route reads cumulative
`token_count` diagnostics; it does not establish complete native usage or receiver readiness.
Never sum cumulative samples as requests, bridge resets or missing baselines, infer missing modern
counter formats as zero, or scrape raw transcripts to invent purpose allocations. Keep private
stores, rollout content, assignments and identities private; saved/public reports use approved
aggregates and public-safe evidence links.

## Give a useful token breakdown

Use a small table such as `Dimension | Observed usage | Coverage / interpretation`.
Include only dimensions actually supported by the selected evidence:

- Input, cached input, output and reasoning tokens. Preserve the provider's accounting basis:
  cached input and reasoning may be subsets, so do not add them again to totals. Deduplicate
  native invocation/turn identities and corrections; keep incompatible accounting bases separate.
- Root coordination, child implementation and follow-up usage; observed model and effort when
  available. Requested settings are not observed settings. A role is not proof of work purpose.
- Implementation, assurance/review, coordination, context recovery and repair when canonical
  attribution supports them. Keep mixed/unclassified and shared unallocated amounts visible.
  Do not divide tokens by elapsed time or charge shared work to every child.
- Tokens per accepted Delivery for a matched, explicitly scoped population and acceptance stage.
  Show the numerator, denominator, window, exclusions and open/abandoned exposure. Partial
  observed cost is not a complete whole-item ratio. Zero accepted deliveries means the ratio
  is not applicable; retain the cost. PR count alone is not an acceptance denominator.

Compare equivalent work, accounting, coverage and acceptance stages. Keep quality, accepted
outcomes and elapsed/wait time beside cost: fewer tokens or a high cache hit rate alone do not
establish efficiency. Currency requires a documented price version or actual billing provenance;
token share is not dollar share. If no defensible baseline or complete accounting exists, say
that improvement is unmeasured and describe the available observations.

## Diagnose pathologies and improvements

Look for evidence of repeated unchanged context reads, oversized tool output, handoff/context
reconstruction, model-driven polling, duplicate proof assembly, repeated refused or failed
attempts, late integration repair, and coordination cost growing without accepted outcomes.
Also consider avoidable serialization and supplier delays when timestamps and dependencies
support them. Token counters alone cannot establish any of these causes.

For each material finding, connect **observed pattern → delivery impact → supported cause or
hypothesis → feasible improvement → owner/next action**. Quantify frequency or attributed cost
only where measured; do not assign the same cost to multiple overlapping cause labels.
Preserve competing explanations. A retry is not automatically flaky, changed-input validation
is not duplicate work, and necessary review is not waste. An avoidability claim needs an
alternative permitted by the requirements at that time; changing a requirement is a proposal.

Prefer simplifications supported by evidence: narrower retrieval and bounded output, reuse of
valid evidence, deterministic operation observation, clearer handoffs, earlier integration,
or independent work during a known wait. Explain the expected benefit and how existing ordinary
work could reveal it; do not invent savings, remove required validation, add a reporting ledger,
or make new measurement a delivery gate. Retain existing owned findings and their dispositions
through the canonical process referenced by the skill. If no material pathology is established,
say so with the coverage limit rather than manufacturing a problem.
