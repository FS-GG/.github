# Standalone telemetry H3 Main evidence

Date: 2026-09-10. Status: accepted Main evidence; H3 complete.

This report records accepted Main results for the H3 receiver, producer, recovery,
browser-scope, credential-rotation, missing-ingestion, fact-replay, updater, and
stopped-development-container independence work. It contains no credential,
private key, authorization header, or raw private telemetry. It does not change
the H3 exit criteria or any operator procedure.

## Main deployment and accepted history

FS.GG.Telemetry.Host 0.1.1 is running on Main under the dedicated rootless Podman
account `fsgg-telemetry-podman` (UID 954). Its durable storage was identified as
ext4 on `/dev/nvme0n1p2`. The live producer scope is workspace
`main-fsharp-dev`, producer `fsharp-dev-main`, stream `coordination`.
The rootless user service is configured for automatic startup, but this report
does not treat that configuration or a process restart as proof of an OS reboot.

The reference applied outcome is batch `h3-client-activation-proof-v1`, digest
`61ac7f3101aa7f32e9f0fd98361dd135658060370c54979977c7caf77a1e2399`.
The synthetic source payload is retained inside `fsharp-dev` at
`/home/developer/.config/fs-gg/telemetry-main/h3-client-proof.json`; the separate
live client result is
`/home/developer/.config/fs-gg/telemetry-main/live-proof-result.json`. These are
nonsecret evidence files. Producer credentials and the private client bundle are
outside this report.

A disposable client used a fresh home, workspace association, and spool with the
same producer identity. It recovered the applied outcome after recreation,
reported no pending batch, and resubmission returned the same applied receipt.
These observations establish receipt-level replay; they do not independently
measure the stored fact count. An independent lookup observed at
`2026-09-10T12:58:57.546Z`, after the Main receiver restart, returned the same
reference receipt. The accepted
SystemAdmin client corrections are merges
`2e59f51075c5535fc3346d631e8da6a386434191` and
`c3eb26e7c357870b06e43ee5b7ec3d00f9d3b27a`.

The accepted operator reports are retained in the private SystemAdmin mailbox:
[restored-receipt result](https://github.com/EHotwagner/SystemAdmin/blob/40fbf0607f6ef644879beebf7f998d0c5e46de1e/Mailbox/from-main/20260910T142608Z-corrected-restored-receipt.md)
and [credential-rotation result](https://github.com/EHotwagner/SystemAdmin/blob/6d9b5951566a07c3930bf522b83017b50010fa80/Mailbox/from-main/20260910T144116Z-producer-rotation.md).
The source correction is [SystemAdmin PR #17](https://github.com/EHotwagner/SystemAdmin/pull/17).

## Missing ingestion remains visible

The installed `fs.gg.coord.cli` 0.88.0 client was exercised with a fresh home,
private configuration, private spool, synthetic producer identity, and an
intentionally unreachable local endpoint. The failed submission and a later
drain retry both reported `unacknowledged-lossy`. The client retained one
owner-only ready envelope on the development container's overlay filesystem,
created no outcome, and reported `pending: 1`,
`pendingCensus: bounded-ready-files`, and `unacknowledgedLossy: true`.

During a readback from `2026-09-10T15:49:51.535921678Z` through
`2026-09-10T15:49:52.004140830Z`, authenticated Host health independently
reported `ready`. The live `main-fsharp-dev` association remained configured
with `pending: 0` and `unacknowledgedLossy: false`. A separate root readback at
`2026-09-10T15:23:02.918693Z` observed the same ready Host and the same distinct
pending/lossy disposable state.

The redacted fixture evidence is retained at
`/tmp/fsgg-h3-ingestion-gap-fixture/evidence.json`; the independent readback is
`/tmp/fsgg-h3-ingestion-gap-fixture/independent-readback.json`. The retained
ready envelope remains under that fixture's `spool` directory and its
`outcomes` directory remains absent. This establishes visible missing or
unacknowledged ingestion independently of Host service health. This gap fixture
alone does not establish fact-level replay or deduplication. The aggregate
workspace status also does not count acknowledged rejected outcomes; those are
retained as private outcome detail after the ready envelope is removed.

## Fact-level replay

Main replayed the canonical retained envelope once against the correct batch
route at `2026-09-10T15:50:36.714638Z`. The duplicate POST returned HTTP 200
with the exact existing applied receipt and digest. Read-only SQLite
transactions before and after the POST found exactly one unchanged fact, one
unchanged namespaced ingest batch, and one unchanged applied transport receipt.
A final receipt lookup returned HTTP 200 with that same applied receipt, and
authenticated health remained ready.

The probe packet marked its own result refused only because it incorrectly
required HTTP 202. Verification against released Host 0.1.1 source commit
`431d69d38d71da3b2c293bee8cc05448795ea38f` confirmed that an existing receipt
with the same digest correctly returns HTTP 200; HTTP 202 is the new-receipt
admission branch. The accepted Main
[replay report](https://github.com/EHotwagner/SystemAdmin/blob/eb7f3f3b307173f81d093e012d39fd8d41c40c89/Mailbox/from-main/20260910T154851Z-h3-replay-correct-batch-route.md)
retains the complete comparison at
`/home/eugen/.local/share/fs-gg/telemetry-main/h3-correct-route-replay-4qawvoe9`.
The earlier wrong-route attempt remains unknown and is not reclassified by this
successful evidence.

## Restart, backup, and restored readback

The Main operator quiesced the receiver, created coherent backup
`h3-20260910T130354Z`, and restored it separately as
`h3-restore-20260910T130354Z`. The restored database SHA-256 matched the backup.
The selected filesystem remained ext4. The interval from stop request to live
service restart was approximately 2.38 seconds. That interval is not receipt
recovery time, an RTO measurement, or power-loss evidence. Authenticated health
after restart returned HTTP 200 with `{"status":"ready"}`.

The first receipt-only rehearsal from SystemAdmin PR #14 was refused by Host
0.1.1 preflight because its candidate configured no browser principal. It
created no rehearsal container. Its failed private configuration and environment
were preserved. The corrected source in SystemAdmin PR #17, merge
`1b005c9df4d7e5fc5624b836dca9d81a58003b04`, uses a distinct rehearsal and one
temporary browser principal scoped only to `main-fsharp-dev`.

The corrected installed probe completed in 3.094 seconds and recovered the exact
reference receipt from the restored state. It verified authenticated producer
readiness, browser login and allowed-workspace readback, denial outside the
browser scope, and producer/browser credential separation. The live receiver
and `fsharp-dev` retained their identities and start times. The rehearsal
`fsgg-telemetry-h3-browser-23b7f4f6eb82c24f` was retained stopped, the temporary
plaintext browser key was absent, and the failed PR #14 artifacts plus pristine
restored configuration remained unchanged. The 3.094 seconds is probe duration,
not recovery time or RTO.

The native Main evidence is retained at:

- `/home/eugen/.local/share/fs-gg/telemetry-main/restored-receipt-pr17-we3erfe3/probe-result.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/restored-receipt-pr17-we3erfe3/run.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/restored-receipt-pr17-we3erfe3/stdout.txt`
- `/home/eugen/.local/share/fs-gg/telemetry-main/restored-receipt-pr17-we3erfe3/stderr.txt` (empty)

## Credential rotation

The accepted PR #17 coordinator rotated the single producer credential once,
without an overlap interval, in 3.452 seconds. The old credential received HTTP
401. The new credential reached authenticated readiness and recovered the same
reference applied receipt. The client bundle was updated in place as container
user `developer`; `fsharp-dev` was not restarted. It retained StartedAt
`2026-09-09T20:53:56.139915248+02:00` across the rehearsal and rotation.

Post-rotation client health at `2026-09-10T14:47:23.273547Z` returned HTTP 200
and `{"status":"ready"}`. A separate development-container health check also
returned ready. The updater units were absent/inactive at the time of this
rotation, and no other Host maintenance overlapped it. The 3.452 seconds is
coordinator duration, not RTO.

The native Main rotation evidence is retained at:

- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/rotation-result.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/run.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/final-health.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/stdout.txt`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/stderr.txt` (empty)

## Updater installation and activation

The accepted [SystemAdmin PR #21](https://github.com/EHotwagner/SystemAdmin/pull/21),
merge `0883ef0cdec1257dc31df00f022787ed2f5875b9`, supplied the updater installed
for the dedicated rootless account. The exact Main
[installation report](https://github.com/EHotwagner/SystemAdmin/blob/9512041/Mailbox/from-main/20260910T154417Z-updater-release-root-mode.md)
records a successful initial result of `current` at Host version 0.1.1 and an
independent applied readback of `h3-client-activation-proof-v1` with Host health
ready.

The timer is enabled and active/waiting. Its effective recurring interval is
five minutes with up to 30 seconds of randomized delay; it is persistent and
also has a two-minute boot delay. The installation did not restart the Host or
`fsharp-dev`: both retained their container identity, start time, and image.
The incumbent publisher timer also remained enabled and active. Root-private
native installation evidence is retained at
`/root/fs-gg-telemetry-updater-install.lmMKD5`.

## Stopped-development-container independence

The Main operator completed the remaining H3 acceptance action with a
hash-bound recovery guard and the UID-954 telemetry maintenance lock. Before
the stop, all twelve project roots and the worker mailbox were clean including
untracked files. The only running development container was `fsharp-dev`, exact
container ID
`8e28412c46970d087c84c88197b27ab0ce40b74241559fd02e5ff989c77dc1f0`.
The other development containers retained their prior non-running states.

Main stopped `fsharp-dev` at `2026-09-10T17:30:38Z`. While it was stopped,
strict-TLS reads made entirely from Main reported Host readiness and recovered
the exact applied receipt `h3-client-activation-proof-v1`, digest
`61ac7f3101aa7f32e9f0fd98361dd135658060370c54979977c7caf77a1e2399`,
bound to workspace `main-fsharp-dev`, producer `fsharp-dev-main`, and stream
`coordination`. Private history revision
`e56281e0d1758a644dfc751b67916c5254d1086e41480287230e8c2983279a7e`
contained 22 applied receipts, zero pending batches, and exactly one fact for
`h3-client-activation-proof`.

The operator restarted the same container ID at `2026-09-10T17:30:38Z`. The
container client and Main Host both returned ready, and the receipt, digest,
history revision, applied and pending counts, and fact count were unchanged.
The telemetry Host retained its original process lifetime, and the incumbent
publisher timer remained enabled and active. Normal recovery succeeded, after
which the transient recovery guard and maintenance-lock units were disarmed.
The separate orchestration PostgreSQL service was not touched.

The private Main report is
`/home/eugen/.local/share/fs-gg/telemetry-main/h3-stop-proof-8e28412c-20260910T1717Z/report.md`,
SHA-256
`b7d8bdac1de9c342579f38be6a5c821b8208f98e93e743838ab9018390c30ae6`.

## H3 exit assessment

The evidence supports these parts of the H3 exit:

- the selected Main receiver, TLS route, producer identity, rootless service
  account, and durable ext4 placement operate together;
- the isolated restored-state rehearsal verifies scoped browser readback and
  credential separation; this is not a live operator browser-session check;
- a disposable producer can reconnect with its configured identity and recover
  the same accepted receipt after resubmission;
- receiver process/container restart, coherent backup and separate restore retain
  the accepted reference receipt;
- credential rotation denies the old credential, preserves the accepted receipt,
  and updates the existing development container without restarting it;
- a retained unacknowledged disposable batch is visibly pending and potentially
  lossy while the Main Host independently reports ready;
- replaying the exact existing envelope returns its applied receipt while the
  fact, ingest-batch, and transport-receipt counts and selected fields remain
  unchanged;
- the installed updater reports the current immutable version, preserves the
  retained receipt, and waits on its configured recurring timer without
  restarting the Host or development container;
- stopping every running development container leaves Main ready with the exact
  applied receipt and unchanged private-history counts, after which restarting
  the same container restores client health without duplication; and
- the tested operator guide and source corrections are delivered in SystemAdmin.

H3 is accepted. Main served the retained receipt and history independently while
all development containers were stopped, and the same development container
then recovered without changing the receipt or counts. The current record does
not demonstrate Main OS reboot, physical power-loss recovery, cross-version
rollback, or measured RPO/RTO. The roadmap phrase “host restart” is supported
only for a telemetry Host service/container restart; it must not be read as an
OS reboot claim.

Publisher preparation and cutover are separate P1 work. The private-repository
publisher input failure and its correction do not weaken the H3 receiver evidence
and are intentionally excluded from this assessment. P1 staging has passed, but
legacy history has not been migrated and no publication cutover is claimed.
