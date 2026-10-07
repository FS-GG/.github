# Synthetic owner/restart fixtures

All populations, candidate/attempt identities, timestamps and placeholder packet hashes here are **synthetic**. They describe replay controls, not actual dispatch records, termination, native acceptance or economic evidence. Expectations describe selected properties; they are not canonical golden outputs.

The existing `context-delta.fsx` consumer loads all six typed inputs and calls the actual `contextDelta` and `frontier` functions. It checks complete reservations, missing-base and missed-revision resynchronization, unreadable owner retention, full capacity, one-slot same-owner continuation and superseded-return fencing. Run `python3 tests/work-programme/context-owner-restart-fixtures/structural-check.py` for duplicate-key, bounds, lineage and intended input-population checks. Shape checks alone do not establish that these F# assertions pass.

`python3 tests/work-programme/context-owner-restart-fixtures/watcher-regression.py` runs three synthetic watcher controls with eight assertions against the repository's existing watcher. No native queries or process-control calls occur. A fresh watch recovers current terminal facts after a dropped notification, with a new watch-local revision1. This number must never be substituted for owner-return revisions or historical notification identity.

Qualification: run `dotnet fsi --exec tests/work-programme/context-delta.fsx` and the separate legacy `tests/work-programme/acceptance.fsx` under admitted resources. Python regression success does not qualify the F# consumer. These fixtures do not implement a new observer/executor, prove actual interruption recovery, or close V2-CTX-01.4.

A separate real consumer window must use genuine packet-bound owner/base returns, preserve unknown operations and the complete reservation inventory, recover a missed watcher notification via fresh exact-head observations, and retain the same owner through repair. Root alone admits effects/integration. Native usage, controlled comparison and whole-family economics remain separate.
