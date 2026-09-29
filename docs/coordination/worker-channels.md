# Worker channel directory

This is the permanent lookup for active worker-to-worker coordination channels.
Read it from protected `.github/main` at the start of a handoff. The channel
holds messages; this file holds its address and owner. Update an address here
through a protected PR when a workstream changes channels. Do not copy progress
reports into this directory.

| Workstream | Shared channel | Owners | Canonical work |
|---|---|---|---|
| Unified Roadmap, installed telemetry and the subitem pipeline | [`MAILBOX.md` on `mailbox/work-main-unified-20260929`](https://github.com/FS-GG/.github/blob/mailbox/work-main-unified-20260929/MAILBOX.md) | Root/programme integrator and replacement work-main/SystemAdmin | [Unified Development Roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md); [#3613](https://github.com/FS-GG/.github/issues/3613); [#3612](https://github.com/FS-GG/.github/issues/3612) for Pages refresh |

For a workstream without a row, use the [cross-repo issue protocol](README.md#requests-and-responses--cross-repo-issues): open or reply in the target repository's issue. Add a row here when workers agree to use a persistent shared channel.

The user selected the replacement work-main channel on 2026-09-29 because Main
is unavailable. Prior messages remain in the
[old mailbox](https://github.com/FS-GG/.github/blob/mailbox/plover-61db-standalone-telemetry/MAILBOX.md);
new requests and replies belong in the replacement channel.

When using the shared channel, fetch the remote branch before reading or
appending. Reply in its `MAILBOX.md` thread, preserve prior entries, and push a
normal fast-forward commit. If the branch advanced, fetch and replay the reply
against the new head before pushing. Check the remote commit after a push; a
local draft or watcher detection alone does not prove that the other owner read
the message. Keep private telemetry, credentials, and private identifiers out
of this public branch.
