# UTEL transport fault coverage

This source review answers [the transport-coverage evaluation on #4638](https://github.com/FS-GG/.github/issues/4638#issuecomment-6470054902). It maps [OpenTelemetry's resilience scenarios](https://opentelemetry.io/docs/collector/resiliency/) to existing UTEL behavior. It selects no exporter migration, additional completion prerequisite or runtime operation.

The inspected source is `.github` commit `6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46`. Tests below are maintained executable assertions, not new test results or installed fault qualification. OpenTelemetry defaults do not describe UTEL defaults.

## Preserve each delivery stage

Persisted producer intent, a locally retained immutable batch, a remotely durable receipt and applied/corrected facts are separate stages. The client verifies workspace, producer, stream, batch and digest before accepting a receipt. A bare HTTP 202 is insufficient. Receiver acceptance creates a durable processing obligation; it does not establish applied facts, browser visibility or complete attribution.

Transport retry preserves the original batch identity and bytes. It does not authorize replaying an uncertain workload. A run generation is not a persistent producer association; see [the canonical association distinction](https://github.com/FS-GG/.github/issues/4705#issuecomment-6344445603).

## Source coverage and limits

| Scenario | Existing assertions | Limit |
| --- | --- | --- |
| Outage beyond retry budget | [Client](https://github.com/FS-GG/.github/blob/6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46/src/FS.GG.Telemetry.Client/RemoteClient.fs) bounds requests to five attempts with ten-second per-attempt cancellation. [Spool tests][spool] retain unacknowledged bytes and uncertain correction predecessors. | Those separate controls do not establish an integrated outage across the complete budget followed by installed recovery. |
| Full queue | [Producer][producer] refuses a new entry at 128 ready files or 8 MiB. Spool tests preserve pending intent. [Receiver tests][receipts] admit exact retries at capacity and refuse new obligations. | Queue capacity is distinct from physical disk exhaustion; not every count/byte combination is asserted. |
| Producer interruption | Spool tests inject failures before/after intent, enqueue and cursor persistence, then recover identical bytes. Source disappearance preserves an explicit gap. | Hook exceptions are not an actual killed producer or power failure. |
| Receiver interruption | Receipt tests exit a real child at file/index/application barriers and recover the same identity. [TLS tests][remote] retain receipts across host restart. | Test envelopes and hardware do not qualify installed producer termination or failed media. |
| Full or failed disk | Persistence uses file flush, rename and directory synchronization. Tests cover adjacent exceptions, retained intent and storage-unavailable outcomes. | Direct ENOSPC/EIO, failing flush/fsync and SQLite FULL/IOERR were not found in the inspected telemetry tests. WAL alone is not failed-media proof. |
| Duplicate or lost acknowledgement | TLS tests drop an accepted response and recover through same-identity retry. Receipt tests retain deduplication, changed-content conflict and expiry identity. | No installed lost-acknowledgement case is accepted by the normal receipt evidence. |
| Misconfiguration | Client/host tests refuse malformed configuration and unsafe credential paths. Spool tests retain bytes on unsupported/rejected receivers without downgrade. | A credential reference or offline capture does not establish current authenticated receiver readiness. |
| Correction replay | Spool and [passive HTTP tests][passive] cover correction order, ownership and projected revision while attribution remains Unknown. [CI correction tests][store] separately preserve immutable bytes across replay/restart. | The CI path cannot establish every passive correction/restart case or genuine installed correction. |

[producer]: https://github.com/FS-GG/.github/blob/6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46/src/FS.GG.Coord.Cli/WorkspaceTelemetryApplication.fs
[spool]: https://github.com/FS-GG/.github/blob/6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46/tests/FS.GG.Coord.Cli.Tests/WorkspaceTelemetryApplicationTests.fs
[receipts]: https://github.com/FS-GG/.github/blob/6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46/tests/FS.GG.Coord.Cli.Tests/TelemetryReceiptTests.fs
[remote]: https://github.com/FS-GG/.github/blob/6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46/tests/FS.GG.Telemetry.Tests/RemoteTelemetryTests.fs
[passive]: https://github.com/FS-GG/.github/blob/6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46/tests/FS.GG.Telemetry.Tests/HostPassiveReceiptTests.fs
[store]: https://github.com/FS-GG/.github/blob/6aee8ee58c8d1dfdc6cdd06476a5ee0963f12a46/tests/FS.GG.Coord.Cli.Tests/TelemetryStoreApplicationTests.fs

## One possible focused test

A future scoped fixture could make an actual SQLite commit return SQLITE_FULL at the existing durable-obligation/application boundary. It should verify no applied receipt or partial projection, retained exact transport identity/bytes, then recovery after removing only the synthetic fault. This differs from another exception hook. It would not prove failed media, power loss or producer ENOSPC. No test or additional required journey is selected by this report.

## Installed acceptance remains separate

[4705](https://github.com/FS-GG/.github/issues/4705#issuecomment-6344445603) accepts one genuine 25-event batch applied by Host 0.8.0/uv14, matching receipt/fact identities and untouched neighboring receipts. It retains historical batch 2 pending, two producer gaps and Unknown programme/activity attribution; client credential copies were deleted.

[4707](https://github.com/FS-GG/.github/issues/4707#issuecomment-6396226736) accepts its compatible cutover, configuration, 48 canonical joins, 12 browser assertions and runtime retirement. Neither child establishes all installed interruption, recovery, correction, attribution, retention/rollback or assessment/adoption promises. Current source is not asserted equal to those historical installed payloads.

Root retains the remaining selection and current custody reconciliation on #4638/#4640. All original failed/Unknown effects and 439 inherited plus 46 later holds remain. No new send, drain, credential delivery, cleanup or benefit claim follows from this review.
