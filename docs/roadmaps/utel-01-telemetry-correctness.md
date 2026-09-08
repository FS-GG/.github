# UTEL-01 — Routine telemetry correctness and public-evidence safety

Backlink: [Unified Development Roadmap §9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)

## Outcome

Make the existing advisory routine observer preserve native delivery truth, join the exact producer identity,
separate evidence quality questions, and refuse private or unbounded repository evidence. Telemetry loss remains
non-blocking and cannot certify routine efficiency.

## Delivered window

- [x] Keep `fsgg.routine-delivery/v1` and regress the real `summarize` plus `dataclasses.asdict` producer shape
  across ready, refused, delivered, ambiguous-readback and indeterminate outcomes.
- [x] Publish `fsgg.routine-unit-economics/2`; consume `expectedHead` and `observedHead`, preserve independently
  known native delivery, support legacy-only `head` without claiming a proved join, and invalidate conflicts or
  expected/observed mismatches.
- [x] Report `recordValidity`, `joinIntegrity`, `populationCoverage`, and `qualification` independently. A supplied
  whole-unit usage record may produce arithmetic, but absent independent population collection keeps coverage
  unknown and qualification not evaluated.
- [x] Validate bounded usage/economics types, units, non-negative counts, reasoning/output consistency,
  reconciliation and completeness using fixed diagnostic codes.
- [x] Add 1 MiB input and 64 KiB output bounds, allowlisted public input fields/types, unsafe-path refusal before
  reads, symlink/alias-safe atomic output, a candidate/index-blob Git guard, forced-staging fixtures and CI wiring.
- [x] Observe one real matching merged PR with the producer, then run the observer without private usage and retain
  delivered identity with insufficient qualification. [PR #3347 observation](https://github.com/FS-GG/.github/issues/3347#issuecomment-5582965362)
  binds source `857685615a068acbd3eccfd5236da106231a499d` to merge
  `d26cab2412d08002090b12621b1dce8dd7ab817c`: delivery and the head join match, while usage is missing,
  population coverage stays unknown, and qualification remains not evaluated.

## Boundaries and workspace impact

Automatic collection, child lineage, population reconciliation, historical backfill, PostgreSQL/object deployment,
dashboards and counters are deferred. There is no database schema or protected publication in this feature.

Existing and freshly created workspaces are unchanged: this repository-owned diagnostic tooling is not scaffolded,
published, or enabled in generated receivers. The first behavior change is source merge for callers that explicitly
invoke these tools. Producer publication and receiver adoption do not apply; upgrade handling remains separate.
