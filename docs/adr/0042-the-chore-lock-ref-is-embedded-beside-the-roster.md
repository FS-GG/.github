# ADR-0042: The chore-lock ref is embedded beside the roster — `repos.yml` is unreadable exactly where the queue has to work

- **Status:** Accepted (2026-07-17)
- **Date:** 2026-07-17
- **Affects:** `.github` (the chore queue: `Options.fs`, `Chore.fs`), and every repo where chores are offered — sdd, rendering, governance, templates, game, audio
- **Amends:** [ADR-0041](0041-the-chore-lock-is-the-item-cas-on-another-subject.md) §"The lock issue is CLOSED" — the configuration clause only. ADR-0041's substrate decision is untouched.
- **Decides:** [#1026](https://github.com/FS-GG/.github/issues/1026). Unblocks [#733](https://github.com/FS-GG/.github/issues/733) (the wiring), which this record does **not** do.

## Context

ADR-0041 decided the chore-lock substrate — a chore takes `Writes.claim`, unchanged, on a dedicated closed
per-repo lock issue — and that decision stands. This record amends one sentence of it:

> **Its number is recorded per-repo in `registry/repos.yml`.** Absent ⇒ `offer` refuses.

**The engine cannot read `registry/repos.yml`. It has no YAML reader at all, and the reason it has none is
the reason it must not have one.** The roster is an embedded `match` in `Options.fs`, and `Options.fs:433`
states why in as many words:

> The roster map is EMBEDDED rather than read from `registry/repos.yml` because the shim ships as a
> `kind: client` kit item WITHOUT the roster (case 13 §6c / #381).

So ADR-0041 specified a configuration source that is **absent exactly where the queue has to work**. A
`repos.yml` reader would resolve in `.github` and nowhere else, and `offer` would refuse in all six receivers
forever.

That is not a cost — it is the mechanism inverting. #733's whole argument for the chore queue is that it
*"amortises maintenance across a fleet that is already calling the tool constantly."* A substrate readable
only in the authority repo amortises across the one repo that already has a human running `/check-board`.

This was not an oversight in ADR-0041's reasoning. Its analysis of `Writes.claim` is correct and #873's
premise really was wrong. It is a gap between the substrate it chose and the artifact that has to load it —
and `Chore.fsi` has been citing a source that does not exist ever since, which is why #733 had no legal
first step rather than merely a hard one.

## Decision

**The chore-lock ref is embedded beside the roster, in `Options.fs`, and keyed on OWNER as well as repo.**

```fsharp
val choreLockRef: owner: string -> repo: string -> FS.GG.Coord.Types.Ref option
```

It resolves the repo through `resolveRepo`, so every documented `--repo` spelling names one lock.

**`None` is the fail-closed answer, and ADR-0041's contract is unchanged by this record:** absent ⇒ `offer`
refuses. A chore queue that cannot find its lock offers nothing and never broadcasts — condition 1 fails
closed, like every other "could not look" in this engine (#266, #421).

**Keyed on the owner because the owner is configurable** (`FSGG_COORD_OWNER`, default `FS-GG`) and the
embedded numbers are FS-GG's issues. Keyed on the repo alone, a caller under any other owner would be handed
`<their-owner>/.github#1033` — a well-formed ref naming an unrelated issue, i.e. a lock that protects nothing
while reporting that it does. An owner the map does not know has **no** lock rather than a wrong one.

**`.github`'s lock is [#1033](https://github.com/FS-GG/.github/issues/1033)** — closed (so it never appears
in an `--state open` read, never lands on the board, and cannot be mistaken for work), never locked (the
marker is a comment, and a locked conversation refuses comments).

**All seven repos now have a lock** ([#1087](https://github.com/FS-GG/.github/issues/1087)). #733 wired
`offer` and left the receivers `None` because their lock issues did not exist yet; #1087 created the six
(`FS.GG.SDD#518`, `FS.GG.Rendering#878`, `FS.GG.Governance#268`, `FS.GG.Templates#252`, `FS.GG.Game#406`,
`FS.GG.Audio#183`), each a closed `[chore-lock]` issue naming this file as where its number is embedded,
and added their arms to `choreLockRef`. The queue drains in every repo now, not `.github` alone. A repo the
map does not know is still `None` — the fail-closed default is unchanged, it just has six fewer members.

## Consequences

- **Growth is a code edit here, not a data change.** This is the price, and it is ADR-0019's rule inverted:
  adding a repo's lock is one arm in `choreLockRef` plus the closed issue it names, in the same PR. The
  roster already pays exactly this price for exactly this reason, so the queue is no worse off than
  `--repo` is.
- **The embedded map is a hardcoded per-repo list in the parser, and that shape has gone stale three times**
  (#381, #446, #962). The mitigation is not vigilance: an absent arm yields `None`, and `None` shuts `offer`.
  A stale map makes the queue *quieter*, never wrong — which is the opposite of how those three failed
  (an empty answer reported as a full one).
- **`offer` refused in all six receivers until #1087 landed their locks.** That was the honest state while
  it held — a chore queue that offers without a lock is the failure mode, not a smaller version of the
  feature — and #1087 closed it by creating the six lock issues and rostering their numbers, so the queue
  now conscripts a caller in every repo rather than only the one that happened to have a lock issue first.
- **No new dependency.** ADR-0040 C4 says this port must not spend surface, and a YAML reader is surface.
- **No REST spend.** The lock ref costs a `match`, not a search — see Alternatives.

## Alternatives considered

**B. Give the engine a YAML reader.** ADR-0041 works as written, in `.github`. Rejected: a new dependency in
a port ADR-0040 C4 says must not spend surface, and it is absent exactly where receivers run — `offer` would
refuse there forever, which inverts the mechanism (above). This is the option the ADR implied and it is the
one the artifact cannot support.

**C. Derive the ref by convention** (a well-known title, searched at acquire). No configuration anywhere.
Rejected on ADR-0041's own Consequences: a chore queue draining REST *"would be a helping mechanism that
stops the fleet."* REST is where the claim lock lives (ADR-0034 §3) and it is the budget that dies first —
measured at 0/5,000 twice on 2026-07-16 (#894, #907) while GraphQL stayed healthy. A search per acquire
spends the budget the lock itself needs.

**D. Pass it in** (`--chore-lock` / `FSGG_CHORE_LOCK`). No reader, no embed, and it matches ADR-0041's
`Absent ⇒ offer refuses` semantics exactly. Rejected as the weaker form of the same trade: it pushes
configuration onto every caller, and a skill that forgets the flag silently gets no queue — a helping
mechanism that quietly stops helping, with nothing to notice it. The embed needs no caller discipline and is
wrong in the same direction (quiet) when it is wrong at all. `FSGG_CHORE_LOCK` remains available as a future
override if a receiver ever needs one before its arm exists; nothing here forecloses it.

**A′. Embed the ref keyed on repo alone.** The obvious spelling of the chosen option. Rejected during
implementation: the owner is configurable, so this hands a foreign owner a real ref to an unrelated issue.
See Decision.
