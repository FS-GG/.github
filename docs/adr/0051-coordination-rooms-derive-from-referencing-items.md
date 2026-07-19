# ADR-0051: A coordination room is an item-referencing rendezvous whose roster and lifecycle derive from the items it references

- **Status:** Accepted
- **Date:** 2026-07-19
- **Affects:** all FS-GG repos (amends [ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) §5; `.github` owns the protocol)
- **Fixes:** [FS-GG/.github#1204](https://github.com/FS-GG/.github/issues/1204)
- **Amends:** [ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) §5 — the worker channel gains a
  second, derived subject. `inbox`'s subject set widens from *"items I claim"* to *"items I claim ∪
  rooms those items reference"*. The per-item channel of §5 is unchanged and remains the default; a
  room is the shape for the tail §5 does not serve.

## Context

The worker channel ([ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) §5: `say`/`inbox`,
`fsgg:msg` behind a per-worker cursor) **rides the claimed item a message concerns**. That is the right
shape for a one-line hand-off between two workers who already share an item ("you take the interface, I
take the impl") — the conversation sits next to the work, and GitHub notifies for free, the same reason
[ADR-0001](0001-cross-repo-coordination-via-issues.md) chose issues over a file mailbox.

It is the **wrong** shape for the tail: a stale or deadlocked cluster that needs fast, high-volume,
N-party negotiation. Three properties of the per-item channel invert exactly there:

1. **No shared place.** The channel rides *one* claimed item. Two workers blocked on *each other's*
   items — the canonical deadlock — share no item, so they have no channel. An idle/between-items
   worker has no address at all.
2. **Poll latency at the worst moment.** `inbox` is a pull behind a cursor; a head-down worker sees a
   `say` only when it next polls.
3. **The escape hatches are the slowest paths.** A stale claim frees only when its lease expires
   (default 120m) and `reap` (a dry run by default) is driven — the coarsest, slowest remedy, firing
   exactly when the situation wants the fastest.

The model *prevents* contention structurally (disjoint touch-sets via `batch`, blockers before
scheduling per [ADR-0038](0038-the-corpus-is-the-cut-over-gate.md) §2, lanes) and bets high-bandwidth
negotiation is rare. The bet holds in the common case and inverts in the tail. #1204 proposed a "room"
to serve that tail without a new store, daemon, roster, or board schema.

## Decision

A **room** is an on-demand rendezvous for a contended cluster whose lifecycle, roster, and channel all
**derive from the items it references** — nothing is separately managed.

1. **A room is an issue that references its items via a soft body line, not a sub-issue parent.** Each
   coordinated item carries a `Rooms: #R` line — the same declaration surface as `Paths:`/`Blocked by:`
   ([ADR-0045](0045-machine-readable-sentinels-for-human-block-and-chore.md),
   [ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) §7). It is **not** a GitHub sub-issue
   child of the room: an issue has one parent, and a contended item is usually *already* parented to its
   feature-epic — a hard room-parent would steal it.
2. **Not every epic is a room.** A room is spawned only for a contended cluster — items that overlap on
   touch-sets or sit in a block knot, the set `batch`/`take` already compute (reserved touch-sets,
   `BlockedBy`). `room open --over N,M` creates the issue and writes the back-reference onto each item;
   a human who sees a knot opens one the same way. Most epics stay pure roll-up tracking — a room on
   every epic would be noise.
3. **Membership is derived — there is no `join`.** A worker is in room #R iff it holds a claim on an
   item that references #R. This is the **only code delta**: `inbox`'s subject set widens from *"items I
   claim"* to *"items I claim ∪ rooms those items reference."*
4. **Lifecycle is derived — the room dies with its work.** It closes on roll-up when every
   *currently-referenced* item is done (the epic-roll-up close `check-board` already computes).
   "Currently-referenced" is load-bearing: a follow-up that inherits the contention adds its own
   `Rooms:` line and keeps the room alive — the room's life follows the **contention**, not any one
   item. No manual close, no room-lease, no litter.
5. **The channel is unchanged.** `say` already takes any `Ref` and deliberately does not require `Held`;
   `messages` already reads any `owner/repo#number`. Pointing them at a room-issue is free — the
   `fsgg:msg` format, the anchored-marker rule, and the per-worker cursor all stand.
6. **The outcome must land on the items.** A room is *talk*; a resolution is a *touch-set mutation*.
   "You take the interface, I take the impl" is not resolved until it is a `widen` on the real items
   ([ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) §7). The room records the agreement;
   `widen` enforces it. A room must never become where a decision is *made* but not *recorded* in the
   load-bearing touch-sets.

Why it works: the channel is slow today because a head-down worker polls rarely. But an agent that has
deliberately entered a room to hash out a blocker is a **hot-poller by definition** — it is doing
nothing else — so poll-latency stops binding exactly when negotiation is live. And the invitation rides
the worker's **own item** (its `Rooms:` line), so its normal check surfaces it. First-contact is
bounded; thereafter latency is gone. No push needed.

## Consequences

- **One real code delta:** `inbox`'s subject enumeration (decision §3) widens to include the rooms its
  claimed items reference. Everything else is reuse — `say`/`messages` are already subject-general,
  `Rooms:` is another declared body line in the [ADR-0045](0045-machine-readable-sentinels-for-human-block-and-chore.md)/[ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md)
  §7 family, and roll-up-close already exists. No new marker, store, daemon, or board schema.
- **No litter, no roster management** — both derive from the item link (decision §3–4).
- **The `inbox` subject-set cost** is one extra derived hop per joined room (one comments-read),
  bounded the way [ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) §Consequences bounds
  `who`/`inbox`.
- **`architecture-map: unaffected`** — no new repo, boundary, coherent-set axis, or contract. This ADR
  adds a coordination-protocol concept, not a system-shape change, so `docs/architecture.md` is not
  reconciled.
- **Deliberately unsolved here:** the *stale* half (a dead worker's claim) — a shorter/adaptive lease
  plus auto-`reap` is that fix, and it is separable; and *push* — the room stays poll-based, the
  hot-poller property (above) is what makes that acceptable. Both are orthogonal follow-ups, not
  conflated into this record.

## Alternatives considered

- **A hard sub-issue parent (the room owns its items as GitHub children).** Rejected: an issue has one
  parent, and a contended item is usually already parented to its feature-epic. A room-parent would
  steal that parentage and break the epic roll-up. The soft `Rooms:` body line (decision §1) references
  without owning.
- **A standalone room object — a new store/marker/roster managed on its own.** Rejected: it reintroduces
  exactly the litter and lifecycle-management this shape avoids (manual close, room-leases, orphan
  rooms). Deriving roster and lifecycle from the item links (decision §3–4) means nothing to garbage
  collect.
- **Push (webhooks/presence) instead of poll.** Deferred, not rejected: it is a larger, orthogonal step
  — the sibling of [ADR-0001](0001-cross-repo-coordination-via-issues.md) §4's deferred git-only
  mailbox. The hot-poller property makes poll acceptable for the room's live-negotiation case, so push
  is not on this ADR's critical path.
- **Broadcast `say --to *`.** Insufficient: `*` reaches every worker on every claim, not the contended
  cluster, and it still rides a single item so it does not give the deadlocked pair a shared place. A
  room scopes the conversation to exactly the items in the knot.

<!-- Implementation sequencing (the `inbox` subject-set delta, the `Rooms:` grammar and precedence, and
`room open`) lives on the Coordination board as its own item, per ADR-0001 and the house rule that an
ADR carries effects, not assignments. Filed as the follow-up to #1204. -->
