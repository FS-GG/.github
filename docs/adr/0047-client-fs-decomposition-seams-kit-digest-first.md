# ADR-0047: The Client.fs decomposition seams — extract the kit-digest advisory first

- **Status:** Accepted
- **Date:** 2026-07-18
- **Affects:** FS-GG/.github (`FS.GG.Coord.Cli` — the `Client.fs` module and the new `KitDigest` module); nothing user-facing changes, so no other repo is touched
- **Decides:** [#1164](https://github.com/FS-GG/.github/issues/1164), the separate engine-ADR line [#1158](https://github.com/FS-GG/.github/issues/1158) split out of the coherence-gate review.

## Context

`src/FS.GG.Coord.Cli/Client.fs` is **4,557 lines** and its own `.fsi`-fronted siblings cite it by name as
the hazard they exist to avoid: [ADR-0041](0041-the-chore-lock-is-the-item-cas-on-another-subject.md)'s
`Chores.fsi` says the offer path was pulled out because "`Client.fs` is 3,700 lines and the org's most
contended file ([#979](https://github.com/FS-GG/.github/issues/979) calls it 'a collision magnet'); an
argument put there is an argument the next editor re-litigates by accident." It has since grown another
~850 lines. The file is handed out by `take` far more than any other in the tree, and two workers on two
items routinely collide in it — the exact failure [ADR-0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md)'s
touch-sets exist to prevent, defeated by one file being the touch-set of half the board.

The org already has the remedy and has used it twice: `Chores` (ADR-0041) and `Followups` are both
`.fsi`-fronted modules carved out of `Client`, each carrying a correctness argument that now lives where
it can be read once rather than re-derived per edit. What was missing was a **record of which seams
remain**, so that the carving is a plan rather than an opportunistic series of cuts that stop wherever a
worker's patience runs out.

The #1158 review named two seams inside `Client.fs` that are cohesive, `.fsi`-shaped, and independently
extractable:

1. **The kit-digest / git-subprocess advisory** (~200 lines, [#469](https://github.com/FS-GG/.github/issues/469)/[#563](https://github.com/FS-GG/.github/issues/563)/[#588](https://github.com/FS-GG/.github/issues/588)/[#509](https://github.com/FS-GG/.github/issues/509)):
   two stderr advisories — "is `registry/repos.lock` stale?" and "will the touch-set you just claimed owe
   a relock?" — plus the file/tree IO under them (`git rev-parse`, SHA-256 of a kit source, the two-root
   divergence walk). The pure comparisons already live in `Core.Kit`; this is only the IO edge.
2. **The four inline `Utf8JsonWriter` renderers** → a Snapshot-style module. These hand-write wire JSON
   inside command handlers, the shape [Snapshot](../../src/FS.GG.Coord.Cli/Snapshot.fs) already models
   for its own writers.

The first is the lowest-risk cut: it is advisory-only (no verb's exit code depends on it), it has exactly
two entry points, and its only coupling to `Client` proper is that one function took a `Client.Context`
when all it read was `ctx.Transport`.

## Decision

**Extract the kit-digest advisory into `FS.GG.Coord.Cli.KitDigest`** — `KitDigest.fs` + `KitDigest.fsi`,
compiled between `Chores` and `Client` — as the first Client.fs decomposition seam. Its `.fsi` exposes
exactly three names: `digestWarn` (the tree-observed staleness advisory), `declaredWarn` (the
claim-time relock advisory), and `kitRoot` (the git/kit-root resolver, which `verify-paths` also needs
for its generated-paths subtraction). The digest, the root-walk, and the lock IO stay private behind the
signature.

**`declaredWarn` takes an `IGitHubTransport`, not a `Client.Context`.** `Context` is defined inside
`Client.fs`, so a module compiled before it cannot name the type — and need not: `ctx.Transport` was the
only field the advisory ever read. The call sites pass `ctx.Transport`. This is the reusable move for
every later seam: a helper that took `Context` for one of its fields is decoupled by taking that field.

**The JSON-renderer seam is named here and deferred to its own follow-up**, not folded into this cut. It
carries no such single-field coupling and touches four handlers; bundling it would make this PR two
stories in the one file a reviewer can least afford to read twice.

**The principle, stated so the next cut inherits it:** a block inside `Client.fs` that carries a
correctness argument of its own, has a small stable surface, and can be reached only through a handful of
named entry points is a module with an `.fsi`, not a region of the magnet. This is ADR-0041's reasoning
generalised from the chore lock to the file as a whole.

## Consequences

- **`Client.fs` shrinks from 4,557 to 4,330 lines** (~227), and the argument for *why* the two advisories
  are advisory-and-not-a-gate — the [#266](https://github.com/FS-GG/.github/issues/266) reasoning about a
  read you could not make — now lives in `KitDigest.fsi` where it is read once, not in a comment block a
  `Client.fs` editor scrolls past.
- **The advisories gain a testable surface.** They were `private` to `Client` and unreachable by any test;
  `digestWarn`/`declaredWarn`/`kitRoot` are now the module's public API. No test is added in this cut (the
  extraction is behaviour-preserving and the existing 1,003 tests pass unchanged), but the seam that made
  them untestable is gone.
- **Behaviour is identical.** The four call sites (`claim` ×2 via `declaredWarn`, `verify-paths` and the
  claim path via `digestWarn`, plus `verify-paths`'s `kitRoot`) call the same code through the module
  boundary. This is a move, not a rewrite.
- **The remaining seam is on the record.** The JSON-renderer extraction is a filed follow-up, so the
  decomposition continues as a plan rather than stalling as an unfinished intention.
- **No wire, CLI, or exit-code surface changes**, so no consumer and no other repo is affected; the cut is
  invisible outside the engine's own source layout.

## Alternatives considered

- **Leave it in `Client.fs`.** This is the status quo the collision data already indicts (#979); every
  edit to the magnet is a rebase waiting to happen, and the advisory's own #266 argument keeps being
  re-litigated in place. Rejected.
- **Extract all the named seams at once.** One PR carving both the advisory and the four JSON renderers
  would be large and unreviewable in exactly the file where an unreviewable diff is most dangerous.
  Rejected in favour of the lowest-risk cut first, the rest filed.
- **Expose `Client.Context` earlier instead of decoupling.** Moving `Context` to a pre-`Client` module to
  keep `declaredWarn`'s original signature would drag the whole command layer's central type forward for
  one field of one advisory. Passing the `Transport` is smaller and is the pattern later seams reuse.
  Rejected.
