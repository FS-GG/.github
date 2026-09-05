# Permanent orchestrator room

Room [FS-GG/.github#3227](https://github.com/FS-GG/.github/issues/3227) is the durable, off-board
coordination channel for every `drive-board*` and `work-board*` host. It remains open permanently. An
item claim says who owns an item mutation; this ledger separately says what each orchestrator is doing
and whether takeover is safe.

## Closed status vocabulary

Every status is one line inside an append-only `fsgg-coord say` comment:

```text
ORCHESTRATOR-STATUS worker=<worker> state=<active|waiting|yielded|done> item=<owner/repo#number|none> claim=<comment-id|none> head=<40-hex|none> note=<one-path-safe-token>
```

- `active`: the orchestrator is progressing the item. Takeover is forbidden.
- `waiting`: the item is deliberately paused at a live handoff, check, review, publication, or external
  boundary. Takeover is forbidden even if the item lease ages.
- `yielded`: the orchestrator has stopped and explicitly permits another host to take over.
- `done`: the orchestrator has no remaining ownership intent for the item.

The latest syntactically valid status per worker wins. Status is append-only; never edit or delete an
older line. A missing or unreadable room ledger is unknown authority, not permission to take over.

## Required procedure

1. At orchestrator startup, read the complete paginated room ledger over REST:

   ```sh
   gh api repos/FS-GG/.github/issues/3227/comments --paginate
   ```

   Resolve the latest valid line per worker. Post this host's `active` line before dispatching work.
2. Before dispatch, recovery, or `claim --force`, read the room again and fresh-read the target claim.
   If another worker's latest state is `active` or `waiting` on the target, do not force-claim. Address
   that worker in #3227 and wait for `yielded` or an explicit handoff. If room and claim disagree, fail
   closed and reconcile them in the room.
3. Immediately after a claim succeeds, post a fresh `active` line carrying the GitHub claim-comment id
   and exact head when one exists. Do not copy a claim id from prose.
4. Before a deliberate pause, post `waiting`; before surrendering work, post `yielded`; after verified
   completion and claim release, post `done`. These transitions are part of the host workflow, not an
   optional status report.

Post through the typed message path:

```sh
FSGG_WORKER=<worker> scripts/fsgg-coord say FS-GG/.github#3227 --to '*' \
  'ORCHESTRATOR-STATUS worker=<worker> state=<state> item=<ref|none> claim=<id|none> head=<sha|none> note=<token>'
```

Item-scoped `say`/`inbox` remains useful for implementers, but it cannot replace this room: an
orchestrator may not hold the item it is supervising, and an inbox cursor can consume a status before
another host begins.
