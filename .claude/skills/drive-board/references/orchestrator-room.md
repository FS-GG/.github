# Permanent orchestrator room procedure

Use permanent room [FS-GG/.github#3227](https://github.com/FS-GG/.github/issues/3227) as the
append-only host-presence ledger shared by every `drive-board*` and `work-board*` orchestrator.

Every valid status is exactly one line:

```text
ORCHESTRATOR-STATUS worker=<worker> state=<active|waiting|yielded|done> item=<owner/repo#number|none> claim=<comment-id|none> head=<40-hex|none> note=<one-path-safe-token>
```

The latest valid line per worker wins. `active` means progressing and `waiting` means deliberately
paused; both forbid takeover. `yielded` explicitly permits takeover. `done` ends ownership intent.
Missing, unreadable, malformed, or contradictory room state is unknown authority and fails closed.

1. At host startup, read every paginated room comment with
   `gh api repos/FS-GG/.github/issues/3227/comments --paginate`, resolve the latest valid status per
   worker, and post this host's `active` status before dispatch.
2. Immediately before every dispatch, recovery, or `claim --force`, read the complete room again and
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
