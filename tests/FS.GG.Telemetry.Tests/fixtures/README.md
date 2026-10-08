# Host 0.1.2 schema 9 backup fixture

`host-0.1.2-schema9-backup.zip` was produced with `Operations.runWithAssessment`
from the published `FS.GG.Telemetry.Host` 0.1.2 NuGet package. Its disposable
workspace is `proof-workspace`; it contains no credentials or private telemetry.
The package SHA-256 was
`af53e6868492e12e00522811ef42c4bedb087d190625230fe3fdeb53e515aaec`.
The archive contains the exact backup-set manifest, workspace manifest, and
SQLite database emitted by Host 0.1.2. Before backup, the published 0.1.2
store enrolled `proof-producer`, submitted `proof-applied` and drained it to
`applied`. The SQLite `PRAGMA user_version` is 9, with one retained ingest
batch, transport receipt and producer enrollment.

The archive is immutable historical evidence and a current rejection fixture. The current Host accepts schema14
only: its test refuses this authentic schema9 backup without creating the target or changing source bytes.
Historical schema9→10 restore acceptance remains history; it is no current compatibility promise. Tests inject
an approved disposable local-store assessment because CI may run on an overlay filesystem. This archive does
not qualify current13 maintenance, pending producer-state preservation or original-operation recovery.
