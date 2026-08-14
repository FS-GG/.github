# M6 compatibility-retirement readiness

M6 is not ready. M3, M4, and M5 are milestone delivery windows, not three weekly operating
measurement periods. Each lasted less than one day, and each has zero issue creations and zero issue
closures in its exact window. The roadmap requires creation to stay *below* closure; `0 < 0` is false.
No evidence was invented for the remaining measures after that blocking result.

The machine-readable census is
[`evidence/2026-08-14-m6-retirement-readiness.json`](evidence/2026-08-14-m6-retirement-readiness.json).
It is intentionally rejected by:

```sh
python3 scripts/coordination-retirement-readiness.py \
  docs/reports/evidence/2026-08-14-m6-retirement-readiness.json
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
GitHub counts, first-parent repair classifications, incident events, release coherence, exact-SHA policy
inventories, and tree-byte deltas. It refuses a non-current authenticated `main`, writes content-addressed
raw observations, and accepts no caller-controlled period, count, or verdict. This preparatory change can
still block, never authorize.

## Live successor disposition

Two input defects were objectively resolved but remained open. `.github#2580` was closed only after
the dual-feed fixture passed 28/28 and the live gate observed all 11 package-bearing contracts coherent
on both feeds. `.github#2586` was closed only after the lifecycle reducer suite passed 24/24 and the
focused CLI precedence test passed 1/1.

The refreshed fixed-query census removes closed #2106, #2409, #2561, #2582, and #2587. Compatibility
retirement remains blocked by `.github#2569`: `Board.fs`/`Reads.fs` still carry the private GraphQL
compatibility shims it names. Their continued presence is evidence against retirement, not permission
to delete them early.

## Earliest honest continuation

Because the roadmap says these measures are weekly, the first possible new three-period run begins
2026-08-17T00:00:00Z and ends 2026-09-07T00:00:00Z. That date is not a forecast or automatic approval.
After the third full week, refresh the GitHub census and every raw measure, make the open successor list
empty through real resolution or evidenced non-applicability, and rerun the validator. Only a pass
with `--live-github` *after the canonical collector and production acceptance switch land under review*
authorizes removal of the reducer rollback switch, GraphQL shims, v1 decision readers, pre-saga release
paths, or the recoverable TRX history.

The user-authorized Chainsaw cut bypasses unavailable kit-source SDD/feedback/cycle machinery only.
It does not convert short milestone windows into weekly measurements, fabricate feedback, weaken release
security, or permit irreversible evidence deletion before the gate passes.
