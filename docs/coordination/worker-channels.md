# Worker channel directory

This is the permanent lookup for active worker-to-worker coordination channels.
Read it from protected `.github/main` at the start of a handoff. The channel
holds messages; this file holds its address and owner. Update an address here
through a protected PR when a workstream changes channels. Do not copy progress
reports into this directory.

| Workstream | Shared channel | Owners | Canonical work |
|---|---|---|---|
| Token telemetry and the subitem pipeline | [`MAILBOX.md` on `mailbox/plover-61db-standalone-telemetry`](https://github.com/FS-GG/.github/blob/mailbox/plover-61db-standalone-telemetry/MAILBOX.md) | fsdev and Main/SystemAdmin | [#3613](https://github.com/FS-GG/.github/issues/3613); [#3612](https://github.com/FS-GG/.github/issues/3612) for Pages refresh |

For a workstream without a row, use the [cross-repo issue protocol](README.md#requests-and-responses--cross-repo-issues): open or reply in the target repository's issue. Add a row here when workers agree to use a persistent shared channel.

When using the telemetry channel, fetch the remote branch before reading or
appending. Reply in its `MAILBOX.md` thread, preserve prior entries, and push a
normal fast-forward commit. If the branch advanced, fetch and replay the reply
against the new head before pushing. Check the remote commit after a push; a
local draft or watcher detection alone does not prove that the other owner read
the message. Keep private telemetry, credentials, and private identifiers out
of this public branch.
