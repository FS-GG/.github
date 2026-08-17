# ADR-0075: Executor identity is a capability the process holds, not an attribute of it — a per-receiver operation lock takes the item CAS on a third subject, and one exported rule orders both

- **Status:** Accepted
- **Date:** 2026-08-16
- **Affects:** `.github` (the coordination engine: `Options.fs`, `Client.fs`, `Reads.fs`/`Reads.fsi`), and every roster repository that receives a fenced operation — sdd, rendering, governance, templates, game, audio, **net**
- **Amends:** [ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) §1 — the identity model gains a third identity, and worker id is named as the one that may never decide an authorization
- **Extends:** [ADR-0041](0041-the-chore-lock-is-the-item-cas-on-another-subject.md) — the item CAS is taken on a third subject, unchanged again, by the same argument that admitted the second
- **Decides:** [.github#2312](https://github.com/FS-GG/.github/issues/2312), slice 2 of [.github#1858](https://github.com/FS-GG/.github/issues/1858)

## Context

On 2026-07-28 two executors ran `.github#1853` to completion concurrently **under one claim marker**, and
six repositories received pull requests from an unlocked executor — `FS.GG.Net#58` among them. The lock did
not fail. It reported exactly one holder, and it was right: both executors were the same worker, on the same
claim, in the same session, and every GitHub-visible fact about them was identical.

[ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md) fixed the previous version of this problem by
moving the lock off the *account* and onto the **worker**, because N agents shared one account and the
assignee lock was a no-op. The 2026-07-28 incident is that same shape one level down: N executors shared one
worker id, so a worker-keyed lock is a no-op *between them*. ADR-0027 §1's reasoning is not wrong; its
identity model is simply not deep enough to separate two contexts of one session, and
`twinSession` — the predicate built for exactly this — is structurally incapable of it, because both
contexts genuinely carry the same session.

The governing design is
[the GitHub-native executor fencing design](../reports/2026-08-04-github-native-executor-fencing-design.md),
landed for `.github#1858` in [#2213](https://github.com/FS-GG/.github/pull/2213). §11.3 of that document
records that two of its decisions are ADR-shaped and assigns them to the slice that makes them rather than
to the design itself. This is that record.

Two facts constrain the answer, and both are measured rather than assumed:

1. **The identity fix is unroutable.** [#1938](https://github.com/FS-GG/.github/issues/1938) decided that
   the *harness* owns durable executor identity, and no repository in this org's roster owns the harness
   runtime. A design that waits for a durable per-executor id waits indefinitely.
2. **Location independence is an invariant.** The coordination state and the claim CAS live on GitHub, and
   `fsgg-coord` is effectively stateless between invocations. A machine-local registry of live executors
   would make correctness depend on which PC stayed online, and would help only the runtime that wrote it.

So the fence must work **without ever comparing identities**, and must reconstruct its entire authorization
state from GitHub alone.

## Decision

### **§1 — Executor identity is a capability the process holds, not an attribute of it.** *(amends ADR-0027 §1)*

ADR-0027's model has two identities. This record makes it three, distinguished by their **issuer** rather
than by their spelling:

| identity | value | issued by | what it is for | what it must never do |
|---|---|---|---|---|
| **worker id** | `snipe-383c` | minted locally (`whoami --mint`) | human-readable addressing (`say --to`), provenance in `who` | decide **any** authorization |
| **claim generation** | the winning `fsgg:claim` comment id | **GitHub** | which *tenancy* of an item an effect belongs to | identify *who* is acting |
| **executor identity** | the **grant's** comment id | **GitHub** | the only identity a fence trusts | outlive its own scope |

The inversion is the whole decision: two contexts of one session, one worker id, one machine, can each
*obtain* a grant; GitHub issues them two different comment ids; exactly one is the winner. **Neither had to
know the other existed**, which is precisely why this works where identity comparison cannot.

This does not retract ADR-0027 §1. Worker-keying remains right for what it decides — addressing, provenance,
and the ordinary one-worker-per-item case. What this record adds is the rule that **a worker id may not be
an input to an authorization decision**, which ADR-0027 never had to say because it had no third identity to
say it against.

### **§2 — The per-receiver operation lock is the item CAS on a third subject.** *(extends ADR-0041)*

ADR-0041 found that `Writes.claim` *"is **already a general comment-order CAS over an arbitrary issue ref.**
It is not item-specific; it is *item-configured*, by its caller, through a callback"*, and applied it to a
per-repo chore lock. This record applies the identical finding a third time, to a per-receiver
**operation-lock** issue: closed, unlocked, off-board, one per repository.

**The CAS write path gains no code, no prefix, no field and no parameter.** A caller supplies a lock ref and
stub callbacks and is done. Mutual exclusion is answered by the **subject** — one lock issue per receiver —
which is ADR-0041's own argument (*"`fsgg:claim` disambiguates markers **on the same issue**. A dedicated
issue disambiguates **by subject**"*), so no key needs to appear in the marker at all.

Three consequences follow, and the third is the one that keeps this honest:

- A dedicated **operation** lock is not the chore lock. Sharing one issue would make a chore drain and a
  dispatch operation serialise against each other — two independent questions answered in one colour.
- The **operation key** (`Operation.OpKey`, slice 1) is deliberately *not* written into the marker, and
  `pathRepo=` is deliberately not reused to smuggle it. The opkey answers **idempotence**, a different
  question asked at a different time; overloading a parsed field with a second meaning is the drift class
  this design refuses everywhere else.
- **Absent ref ⇒ refuse.** A fence that cannot find its lock must refuse and never proceed. This is a
  requirement, not commentary.

**The roster is eight, and the eighth row is part of this decision rather than a detail of its
implementation.** `Options.choreLockNumbers` lists seven repositories and omits `FS.GG.Net` — the repository
the incident actually reached. A per-receiver table built the same way would have inherited that hole in the
worst possible place, so completeness is **derived from `registry/repos.yml` by a test** (ADR-0058: derive,
don't restate) rather than reviewed by eye. Onboarding a ninth repository reds that test until the lock
exists.

### **§3 — Merges are fenced by a lease-free election, not by this lock.**

The dispatch lock is verified *synchronously*, by the broker, inside the window it holds the lease. The merge
gate is a **queued CI job**, and a lease-based lock is verifiable only by a reader running inside the lease.
Worse, releasing the lock *deletes the marker*, so a correctly-behaving executor that released on schedule
would be read as unauthorized — a fail-*always* shape.

So merges use an **append-only election**: no lease, no release, no eviction, keyed on the opkey, whose
winner is the **lowest-id** election marker bearing it. This asserts a historical fact that is stable
forever, so any job, on any runner, at any later time, computes the same answer.

**This is not a second CAS**, and the line matters: it has no lease, no release, no eviction, no twin
detection and no column to restore. What it *does* share with the CAS is the ordering rule — and that rule
may not be written twice.

### **§4 — "Lowest id wins" is one exported function, and every consumer reaches it.**

The rule was written **four** times, and three of them decided locks: `reserver`'s lapsed-claim fallback,
`who`'s Held/Stale classification, **`reap`'s** stale-lock candidate, and **`adopt`'s** expired-claim
selection. The module exported no lease-free ordering function at all, so every caller that needed one wrote
it again — [#485](https://github.com/FS-GG/.github/issues/485)'s defect (*one question computed in five
places and agreeing in none*) reappearing under the two highest-stakes lock paths in the engine.

`Reads.lowestId` is now that function, and all four sites call it. `winner` is `lowestId` composed with the
staleness filter, which states their relationship as one rule with one parameter rather than two rules that
happen to agree today.

**These are not drop-in `reserver` calls**, and this record says so because the mistake is available and
dangerous: `reserver` returns the *live* winner when one exists, while `reap` and `adopt` act **only** when
none does. Substituting it would hand `reap` a live holder — breaking a lock its owner is still standing in.

### **§5 — The lock's production caller is a CLI verb pair, and a lock with no caller is not a fence.** *(added on the 2026-08-17 reopen)*

This section is written **after** the rest of this record, because the rest of it was true and still left the
mechanism unreachable. `d1632c4e` landed §2's `Options.opLockRef` and `Client.OpLock.acquire` exactly as
decided here — eight rows, the CAS unchanged, the fail-closed refusal — and landed them callable from
**nothing but their own unit tests**. `OpLock.release` was one step worse: defined in `Client.fs`, omitted
from `Client.fsi`, and therefore private, so no caller of any kind could have reached it. The board analyst
measured it (`grep -rn "OpLock\.acquire" src/ --include=*.fs` → 0; the same expression over `tests/` → 4)
and reopened the row on 2026-08-17 for that alone.

The consequence was not stylistic, and naming it precisely is the point. `fsgg-dispatch-broker.yml` refuses
a dispatch unless the presented `grant` **is the live CAS winner** on the receiver's operation-lock issue.
With no writer, no `fsgg:claim` marker could ever appear on such an issue, so that refusal was unreachable
**by construction** — the fence was not "off", it was a shape with no way in. Every test written against the
lock passed throughout, because each supplied the subject the production path never would; that is
[#266](https://github.com/FS-GG/.github/issues/266)'s admitted mechanism (*"the subject is missing or empty,
and absence reads as pass"*) reached from the other side.

**The decision: the grant is taken and dropped by two CLI verbs, `op-lock acquire` and `op-lock release`,
and `acquire` emits the whole authorization tuple rather than only the grant.**

- **A verb, because the executor is not the engine.** §5.2's broker is a workflow, and what stands between a
  board worker and it is a shell composing `gh workflow run`. There is no existing verb whose job this is —
  the engine has never dispatched — and the chore lock's precedent (taken inside `next`) does not transfer,
  because a dispatch is not a side effect of some other question being asked.
- **Two invocations, because acquire and release are necessarily two processes.** §4.1 requires the lock to
  be *"taken immediately before a dispatch and released after it"*, and the dispatch happens between them,
  in CI. A `Writes.Held` is an in-process capability with no public constructor — deliberately — so the
  release path re-establishes it through `Writes.verifyHeld`, the same door `release <ref>` uses for an
  ordinary item claim, which is also what keeps `claim`'s twin and impersonation predicates in the path.
  Selecting the marker by `Reads.lowestId` instead would delete whichever marker happened to be *first*,
  which under a lapsed foreign marker is another worker's, and under a shared worker id is
  [#550](https://github.com/FS-GG/.github/issues/550).
- **The whole tuple, because the broker recomputes the key.** It refuses on an `opkey` mismatch, and the
  opkey is a SHA-256 no operator can compute by hand. A verb emitting only the grant would be structurally
  unusable: the caller would still have to derive the key some second way, and a second way to compute a key
  is exactly how two answers come to disagree. Both come out of one `Operation.compose` call (slice 1),
  which makes their agreement a property rather than a convention.
- **The key is composed before the lock is taken.** Cheap questions first is the broker's own ordering, and
  here it protects more than a round trip: composing after acquiring would leave a live marker on a receiver
  for a dispatch that was never going to be authorized, stalling it for the whole ten-minute lease on a typo.
- **A contended receiver exits `6`, not `1`.** `HeldByAnother` is the fence *working*, and its remedy is to
  back off and retry; every other refusal names a fact somebody must change first, on which a retry is a
  loop. Collapsing them would tell a caller to stop retrying a busy receiver and to retry an unrostered one
  for ever.
- **`FSGG_COORD_OP_LOCKS` is read, and is a different variable from `FSGG_COORD_CHORE_LOCKS`.** §2's
  `extra` roster parameter otherwise had no production reader at all — the same reader-without-writer shape,
  one level down. Two subjects get two variables, or a vendored tenant repointing one lock silently repoints
  the other.

**What this section does not claim.** The broker is still driven by hand: nothing in the engine invokes
`gh workflow run`, and this row does not decide that it should. What is now true, and was not, is that a
grant *can be obtained*, so the broker's authorization path has an input it can be given and its refusals
can be reached. Wiring an automatic dispatch caller is a separate decision that belongs with whichever slice
owns the executor's own loop.

## Consequences

- **The engine's CAS is untouched.** `Writes.claim`/`claimScoped` gained nothing; this is composition at the
  call site. The CAS's own tests pass unmodified, which is the check that this claim is true rather than
  merely asserted.
- **Eight `[op-lock]` issues now exist**, one per roster repository, created closed and unlocked, off the
  board, carrying no labels. They are infrastructure, not work. Each names `Options.opLockRef` as the place
  its number is embedded, and that pairing is the whole coherence contract: change one, change the other.
  Reopening one puts a fake item in front of every worker on that board; **locking** one silently disables
  the lock, because a locked conversation refuses comments and the marker *is* a comment.
- **The lock ref is embedded beside the roster, not read from YAML**, inheriting ADR-0042's reasoning
  unchanged: the engine has no YAML reader deliberately, because the shim ships to receivers as a
  `kind: client` kit item *without* the roster. Growth is a code edit, gated by the completeness test.
- **`#516`'s one-item-per-worker refusal is not tripped** by an executor taking a grant while holding its
  item: that check scans *board* items, and the lock issue is off-board by construction. This is verified
  mechanically — acquiring the lock bills the GraphQL meter, which is the only route to Projects v2, zero
  times.
- **A generation change invalidates every authorization written under the old one.** That is correct — a
  re-claimed item is a new tenancy — but it means a `--force` steal or a `reap` mid-review requires the
  successor to re-authorize. This is authoring, not retry.
- **What this does NOT establish**, stated because the temptation to overclaim is real: it guarantees **at
  most one effect per `(item, generation, receiver)`**. It does *not* establish that the electee is the
  *rightful* claim holder — two contexts sharing one worker id and one live claim are both, to every
  GitHub-visible fact, that holder, and whichever wins is *an* authorized executor. Deduplication under a
  generation is the property; authentication of the holder is not, and remains `#1938`'s unroutable
  boundary.
- **The engine grew two verbs, `op-lock acquire` and `op-lock release`**, and the growth is not free: a new
  verb costs a row in `Options.commandName`, `renderSupport` and the write-surface table, a row in the shim's
  `BOARD_WRITES` partition (both halves classify as the single token `op-lock`, for `room open`'s reason),
  and a row in each of the four hand-kept inventories that exist precisely so a verb cannot be added
  silently. Every one of those refused the build until it was written, which is what those inventories are
  for; the cost is the evidence they work.
- **A source-level reachability assertion now guards the mechanism**, because that is the defect class this
  row shipped and it is invisible to behavioural tests: `OpLockDispatchVerbTests` fails if
  `Client.OpLock.acquire` or `OpLock.release` has no production call site, and carries a CONTROL leg proving
  the same instrument returns empty for an absent symbol and non-empty for a consumed one. A green suite over
  an unreachable subject is what this record's own §5 is about.
- **Six further slices are written against this.** The election's *writer* (slice 3), the merge gate (slice
  4), the broker (slice 5), receiver-side validation (slice 6), the reproduction (slice 7) and the arming
  sequence (slice 8) all depend on the lock identity and ordering rule fixed here.

## Alternatives considered

1. **A second CAS for grant markers, under a prefix of their own.** Rejected on ADR-0041's Option B and
   #485: one rule computed in two places agrees at first and drifts later. It is also unimplementable as
   drafted — `markerBody` hardcodes the claim prefix with `worker=` first, and a body that misses `markerRe`
   is classified `NotAMarker` and dropped, so a differently-prefixed grant would be neither writable by
   `Writes.claim` nor visible to `Reads.winner`.
2. **Parameterising the existing CAS's prefix.** Rejected on ADR-0041's Option A: it refactors the org's most
   safety-critical function to obtain a generality it already has, and would parameterise *off* protections
   (stale collection, twin detection) that a lock issue positively wants.
3. **Using the dispatch lock for merges too**, which the first two drafts of the design did implicitly.
   Rejected on the two measured grounds in §3 — a queued verifier cannot read inside a lease, and a
   receiver-scoped lock would red an unrelated pull request for a different item.
4. **Fixing the identity instead, with a durable per-executor id.** Not rejected on merit — it is
   [#1938](https://github.com/FS-GG/.github/issues/1938)'s decided direction — but **unroutable**, because no
   repository owns the harness runtime. This record is deliberately orthogonal: because it never compares
   identities, it neither competes with #1938 nor waits for it, and #1938 landing later would add a warning
   this design does not have without invalidating anything here.
5. **A per-receiver coordination room ([ADR-0051](0051-coordination-rooms-derive-from-referencing-items.md))
   as the channel.** Rejected as the *mechanism*: a room carries no lock and no lease, so it stays advisory —
   and the incident's finding is precisely that both executors cooperated in good faith over a channel and
   duplicated anyway. Rooms remain useful *alongside* this; they are not a substitute for a lock.
6. **GitHub Actions `concurrency` as the deduplicator.** Rejected on a verified semantic: the default cancels
   the existing *pending* run in favour of the newly queued one, so the policy is last-writer-wins. That is
   mutual exclusion, not idempotence, and it silently prefers the later duplicate.
7. **Leaving the four ordering copies in place** and adding the new function beside them. This was a
   genuinely separable clause with a real argument for it — it edits a safety-critical read path for a
   property no *observed* defect has yet violated, which is ADR-0041's own Option A objection in another
   file. It was rejected because the election would otherwise have made a **fifth** copy, and because
   `reserver`'s doc comment already *named* `who` as making the same choice while `who` did not call it —
   the drift had already started.
