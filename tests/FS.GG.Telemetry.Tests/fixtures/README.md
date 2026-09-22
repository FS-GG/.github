# Host 0.1.2 schema 9 backup fixture

`host-0.1.2-schema9-backup.zip` was produced with `Operations.runWithAssessment`
from the published `FS.GG.Telemetry.Host` 0.1.2 NuGet package. Its disposable
workspace is `proof-workspace`; it contains no credentials or private telemetry.
The package SHA-256 was
`af53e6868492e12e00522811ef42c4bedb087d190625230fe3fdeb53e515aaec`.
The archive contains the exact backup-set manifest, workspace manifest, and
SQLite database emitted by Host 0.1.2. The SQLite `PRAGMA user_version` is 9.

The test restores this archive through the current Host entry point into a
fresh state root, checks schema 10, and verifies that the source backup bytes
still match their pre-restore digest. The test supplies an approved local-store
assessment because CI may run on an overlay filesystem.
