# Standalone telemetry H3 Main evidence

Date: 2026-09-10. Status: partial Main evidence; H3 remains open.

This report records accepted Main results for the H3 receiver, producer, recovery,
browser-scope, and credential-rotation work. It contains no credential, private
key, authorization header, or raw private telemetry. It does not change the H3
exit criteria or any operator procedure.

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
and `{"status":"ready"}`. A separate development-container health check also returned ready.
The updater units were absent/inactive and no other Host maintenance overlapped
the rotation. The 3.452 seconds is coordinator duration, not RTO.

The native Main rotation evidence is retained at:

- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/rotation-result.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/run.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/final-health.json`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/stdout.txt`
- `/home/eugen/.local/share/fs-gg/telemetry-main/producer-rotation-pr17-psryyfez/stderr.txt` (empty)

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
  and updates the existing development container without restarting it; and
- the tested operator guide and source corrections are delivered in SystemAdmin.

H3 remains unchecked. The roadmap explicitly requires stopping all development
containers and confirming that Main still serves durable history; that independent
check is deferred until active source workers have saved their work. The current
record also does not yet demonstrate Main OS reboot, physical power-loss recovery,
or measured RPO/RTO. The roadmap phrase “host restart” is therefore supported
only for a telemetry Host service/container restart; it must not be read as an OS
reboot claim. Explicit evidence that missing or unacknowledged ingestion remains
visible independently of service/timer health should also be reconciled before
closing H3.

Publisher preparation and cutover are separate P1 work. The private-repository
publisher input failure and its correction do not weaken the H3 receiver evidence
and are intentionally excluded from this assessment.
