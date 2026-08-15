# M6 compatibility-retirement readiness (superseded historical gate)

> Historical record: this gate correctly did **not** pass. On 2026-08-15 the owner explicitly
> superseded the elapsed-time condition; no weekly period is reinterpreted or fabricated. The replacement
> is the comprehensive, exact-SHA cutover acceptance in
> [`evidence/2026-08-15-m6-cutover-acceptance.json`](evidence/2026-08-15-m6-cutover-acceptance.json),
> validated by `scripts/m6-cutover-acceptance.py`. This document remains evidence of the earlier blocked
> state and has no current authorization role.

M6 is not ready. M3, M4, and M5 are milestone delivery windows, not three weekly operating
measurement periods. Each lasted less than one day, and each has zero issue creations and zero issue
closures in its exact window. The roadmap requires creation to stay *below* closure; `0 < 0` is false.
No evidence was invented for the remaining measures after that blocking result.

The machine-readable census is
[`evidence/2026-08-14-m6-retirement-readiness.json`](evidence/2026-08-14-m6-retirement-readiness.json).
It was intentionally rejected by the now-retired calendar validator. Its exact bytes remain bound by
the replacement evidence rather than being rewritten into a pass:

```sh
sha256sum docs/reports/evidence/2026-08-14-m6-retirement-readiness.json
python3 scripts/m6-cutover-acceptance.py \
  docs/reports/evidence/2026-08-15-m6-cutover-acceptance.json
```

The validator requires exactly seven-day consecutive periods, issue closures greater than creations,
fewer than 10% statement-only repair commits, zero intent reversals, zero partial-success reads, zero
ambiguous release states, an explicit coherent/resumable/no-release-owed disposition, declining policy
surface, slower generated-evidence growth, reproducible verification, and an empty same-class census.
Missing and null values fail closed.
Schema-valid positive evidence still cannot pass offline: the acceptance invocation adds
`--live-github`, which resolves the source commit, re-derives each period's issue counts, and requires
the union of the fixed successor searches to equal the fully classified candidate census. The test-only
snapshot harness imports the pure validators directly and cannot emit the production CLI acceptance.
Every non-GitHub measure must also equal a repository-relative observation artifact bound by SHA-256
and carrying the source, measured-at instant, period boundaries, and structured reproduction argv;
realpath containment rejects symlink escape. Arbitrary verification prose cannot authorize retirement.
The production PASS path remains disabled while the separately reviewed canonical collector is under
critique. `scripts/coordination-health-collector.py` owns the fixed UTC windows and independently derives
GitHub counts, schema-v3 critique repair rounds, machine lifecycle/read observations, saga manifest and
stable-channel release coherence, reviewed exact-SHA policy inventories, and tree-byte deltas across all
implementation surfaces. The live reconciliation workflow retains a digest-bearing shadow artifact for
each admitted run; the collector requires one successful complete observation on every UTC day and counts
unexpected lifecycle differences. A successful typed-boundary reconciliation is also the machine basis for
zero partial-success reads. It refuses a non-current authenticated `main`, incomplete/capped GitHub Search,
missing release/feed receipts, and missing daily observations; it writes content-addressed raw evidence and
accepts no caller-controlled period, count, or verdict. This preparatory change can still block, never
authorize.

## Live successor disposition

Two input defects were objectively resolved but remained open. `.github#2580` was closed only after
the dual-feed fixture passed 28/28 and the live gate observed all 11 package-bearing contracts coherent
on both feeds. `.github#2586` was closed only after the lifecycle reducer suite passed 24/24 and the
focused CLI precedence test passed 1/1.

The refreshed fixed-query census removes closed #2106, #2409, #2561, #2582, and #2587. Compatibility
retirement remains blocked by `.github#2569`: `Board.fs`/`Reads.fs` still carry the private GraphQL
compatibility shims it names. Their continued presence is evidence against retirement, not permission
to delete them early.

## Historical continuation that was superseded

Because the roadmap says these measures are weekly, the first possible new three-period run begins
2026-08-17T00:00:00Z and ends 2026-09-07T00:00:00Z. That date is not a forecast or automatic approval.
After the third full week, refresh the GitHub census and every raw measure, make the open successor list
empty through real resolution or evidenced non-applicability, and rerun the validator. Only a pass
with `--live-github` *after the canonical collector and production acceptance switch land under review*
authorizes removal of the reducer rollback switch, GraphQL shims, v1 decision readers, pre-saga release
paths, or the recoverable TRX history.

The owner decision does not convert short milestone windows into weekly measurements. It replaces that
criterion with named comprehensive tests, required red mutations, immutable evidence verification,
new-only live smoke, coherent release adoption, exact-main binding, and an empty successor census.
