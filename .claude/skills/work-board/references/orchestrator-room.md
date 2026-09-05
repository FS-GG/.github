# Permanent orchestrator room procedure

Use permanent room `FS-GG/.github#3227` as the
append-only host-presence ledger shared by every `drive-board*` and `work-board*` orchestrator.

Every valid status is exactly one line:

```text
ORCHESTRATOR-STATUS worker=<worker> state=<active|waiting|yielded|done> item=<owner/repo#number|none> claim=<comment-id|none> head=<40-hex|none> note=<one-path-safe-token>
```

The status `worker=` must equal the surrounding `fsgg:msg from=` actor. The latest status attempt per
worker must be valid; a malformed newer attempt invalidates that worker's state and never falls back to
an older line. `active` means progressing and `waiting` means deliberately
paused; both forbid takeover. `yielded` explicitly permits takeover. `done` ends ownership intent.
GitHub comment `5551249226` is the authenticated-protocol activation boundary; older raw status lines
are legacy history, not authority. Missing, unreadable, malformed, or contradictory room state is
unknown authority and fails closed.

1. At host startup, read and validate every paginated room comment with
   `scripts/check-orchestrator-room --json`, resolve the authenticated latest status per
   worker, and post this host's `active` status before dispatch.
2. Immediately before every dispatch, recovery, or `claim --force`, run
   `scripts/check-orchestrator-room --json` again and
   fresh-read the target claim. If another worker is `active` or `waiting` on the target, address that
   worker in #3227 and wait for `yielded` or an explicit room handoff. Reconcile disagreement in the
   room; an aged lease is not permission to proceed.
3. Immediately after a claim succeeds, post `active` with its GitHub-assigned claim-comment id and the
   exact head when one exists. Never copy a claim id from prose.
4. Post `waiting` before a deliberate pause, `yielded` before surrendering work, and `done` only after
   verified completion and claim release.

Post through the typed message path:

```sh
FSGG_WORKER=<worker> scripts/fsgg-coord say FS-GG/.github#3227 --to '*' \
  'ORCHESTRATOR-STATUS worker=<worker> state=<state> item=<ref|none> claim=<id|none> head=<sha|none> note=<token>'
```

Item-scoped `say` and `inbox` do not replace the permanent room.
