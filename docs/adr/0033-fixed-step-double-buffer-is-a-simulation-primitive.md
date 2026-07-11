# ADR-0033: The fixed-step double buffer is a simulation primitive, owned by `FS.GG.Game.Core`

- **Status:** Accepted
- **Date:** 2026-07-11
- **Affects:** `.github` (this ADR + the ADR-0022 amendment), **FS.GG.Game** (owner of the primitive), **FS.GG.Rendering** (donor — already executed the reclassification), and **every product repo** (the loop doctrine, §3)
- **Amends:** [ADR-0022](0022-extract-fs-gg-game-as-an-sdd-driven-component.md) §Decision 1 — the `FS.GG.Game.Core` inventory. ADR-0022's sim/render cut stands; this ADR closes what it left unsaid.

## Context

ADR-0022 P5 moved the deterministic sim primitives out of `FS.GG.UI.Canvas` and down into
`FS.GG.Game.Core`, the new bottom layer. Its §Decision 1 inventory names **`fixed-step`**.

It never mentions `Loop`, `StepState`, or the double buffer.

That silence had a consequence. `FixedStep.drain` — the accumulator — moved **down**. The
double-buffered loop **built on top of** that accumulator stayed **up**, in Canvas. One accumulator,
split across two repos, because nobody wrote down which side of the cut it fell on.

The classification that kept it upstream was never an ADR. It was a comment in
`src/Canvas/Canvas.Lib.fsproj`:

> Canvas now carries only the render-adjacent surfaces: pure Elements, **the render Loop**, and the
> Persistence request vocabulary.

An implementation-time judgement, recorded in a build file and ratified nowhere.

### It does not survive inspection

`Loop.advance` contains no rendering. It is `FixedStep.drain` plus a fold that retains the previous
world. `StepState.Previous` is the world one tick ago — **simulation** state. Only `alpha` is
render-adjacent, and `alpha` is `Accumulator / dt`: arithmetic whose *consumer* is the renderer,
exactly as the consumer of `FixedStep.drain` is the renderer. A primitive is not render-owned because
a renderer reads its output; by that test the whole simulation is render-owned.

### The split was not academic — it shipped as a bug

Two implementations of one accumulator, in two repos, **diverged on the thing that matters**:

| | non-finite input |
|---|---|
| `FS.GG.Game.Core.FixedStep.drain` | hardened and documented — a `NaN` runs no steps and never enters the accumulator |
| `FS.GG.UI.Canvas.Loop.advance` | propagated `NaN` into `Accumulator` and **froze the simulation permanently** ([Rendering#266](https://github.com/FS-GG/FS.GG.Rendering/issues/266)) |

**The hardened one was not the one products used.** That is the whole argument for the
reclassification, delivered as a defect. Duplicating a primitive across a boundary does not merely
cost maintenance — it decides, silently, which copy gets the fix.

### What has already happened

This ADR **ratifies work that is complete**; it does not propose new work. Recording it as a proposal
would be a lie about the state of the org, which is the failure [ADR-0032](0032-the-lock-hash-must-not-depend-on-the-machine.md)
had to undo in [ADR-0031](0031-republished-package-is-a-named-failure.md).

- **FS.GG.Game#44 / #61** — `Loop` lands in `FS.GG.Game.Core`, built on `FixedStep.drain`. One
  accumulator, hardened, in the repo that owns it.
- **[Rendering ADR-0104](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/product/decisions/0104-canvas-loop-is-a-simulation-primitive.md)**
  (Rendering#269) — accepted the reclassification, deprecated `Canvas.Loop`/`StepState` by doc comment,
  then by `[<Obsolete>]` once `FS.GG.Game.Core` `0.3.0` shipped a reachable replacement, migrated the
  samples, and **removed** both at the framework `0.6.0` major (Rendering#319 / #355 / #371). The swap
  was measured, not argued: both samples' seeded evidence fingerprints were byte-identical across it.
- **Registry** — `game-sim-core` carried `Loop` at `0.3.0` and is now `0.4.0`, published.

So the primitive is owned, the duplicate is gone, and consumers have migrated.

### Why an org ADR, then — the part that is still broken

**The decision was executed correctly and recorded in the wrong place.**

Rendering ADR-0104 is a **repo-local** ADR, in the **donor** repo, and it does two things that are not
Rendering's to do: it settles what **`FS.GG.Game.Core`** owns, and it lays a loop doctrine on **every
product repo**. This README says the rule plainly — *"ADRs for decisions that span more than one FS-GG
repo. Per-repo decisions live in that repo."* The sim/render boundary is the most cross-repo thing in
the platform; ADR-0022 drew it, and only an org ADR can redraw it.

The practical cost is not hypothetical. Until this ADR, a reader who consulted **the authority** —
ADR-0022, the record that drew the cut — found it **still silent on `Loop`**. The answer existed only
in a `.fsproj` comment and in a *different repo's* local decision log. That is the same shape as the
defect this ADR corrects: the real classification living somewhere nobody thinks to look. Rendering
ADR-0104 is a sound execution record, and it should have had an org ADR to cite. This is that ADR,
arriving late.

## Decision

**1. The fixed-step double buffer is a SIMULATION primitive, owned by `FS.GG.Game.Core`.**
`StepState` (`Current` / `Previous` / `Accumulator`) and `Loop.init` / `advance` / `alpha` are
simulation surface. This supersedes the "render Loop" classification in `Canvas.Lib.fsproj`, and
**amends ADR-0022 §Decision 1**: the `FS.GG.Game.Core` inventory reads *fixed-step **and the
double-buffered fixed-step loop built on it***, not `fixed-step` alone.

**2. One accumulator in the org.** The double buffer is built **on** `FixedStep.drain` — not on a
second copy of it. A second implementation of the accumulator is a **defect**, not a convenience: the
divergence in §Context is what a second copy costs, and it is paid by whichever product happened to
reference the wrong one.

**3. The double-buffered fixed-step loop is the DEFAULT** for any product with a continuously-moving
simulation. Stepping the world any other way **MUST record why in the spec.**

> *Interpolate when the world moves between ticks. Buffer when you interpolate.*

The sanctioned departures, and why each is one:

| departure | why it is legitimate |
|---|---|
| discrete-grid games (Snake, Tetris) | nothing to show between `Previous` and `Current` — carry one world and a step timer |
| headless replay that only fingerprints `Current` | never interpolates, so it may carry no `Previous` at all |
| rollback netcode | *widens* the buffer to a ring of N worlds — more buffering, not less |
| turn-based | the world does not move between ticks |

**Continuous motion with a single buffer is a defect, not a departure.**

**4. Three things stay non-negotiable regardless of buffering**, because they are the three ways to
lose replay: **never feed `alpha` back into the simulation**, **never step with a variable `dt`**, and
**never read a wall clock below the effect interpreter**.

**5. A cross-repo boundary is decided in an ORG ADR.** A repo-local ADR may **execute** a boundary
decision and must **cite** the org ADR that made it. A repo-local ADR that *originates* one — however
correct, and ADR-0104 is correct — leaves the authority silent and the answer discoverable only by
someone who already knows where to look.

## Consequences

- **No code changes, in any repo.** Everything §1–§4 ratifies has shipped: `Loop` is in `Game.Core`,
  `Canvas.Loop`/`StepState` are removed at framework `0.6.0`, the samples are migrated, and
  `game-sim-core` is published at `0.4.0`. This ADR is a **record**, and its cost is the record.
- **ADR-0022 is amended in place** with a pointer here, so the next reader of the authority is no
  longer sent to a donor repo's `.fsproj` comment. A gap that survives in the artifacts is one the
  next reader inherits (the ADR-0032 lesson, applied to a silence instead of a misdiagnosis).
- **The doctrine now binds the repos a Rendering-local ADR never could** — FS.GG.Audio,
  FS.GG.Templates, FS.GG.Game itself, and every product scaffolded from them. It was already written
  into `FS.GG.Game`'s `Loop.fsi` and the `fs-gg-game-core` skill; §3 is where it becomes org policy
  rather than one module's doc comment.
- **Rendering ADR-0104 stands** as the execution record, unamended. It decided correctly; it simply
  had no org ADR to cite. Its `Canvas.Lib.fsproj` correction already points at itself and remains
  accurate.
- **`docs/architecture.md` needs no reconcile.** Its `FS.GG.Game.Core` line mirrors ADR-0022's
  inventory (*"RNG, fixed-step, …"*) — it is silent on the buffer, not wrong about it, and §1's
  amendment reaches it through ADR-0022. The system's shape (six repos, Game.Core at the bottom) is
  unchanged by this ADR.
- **Late ratification is cheap; a misplaced record is not.** The decision was right and is executed —
  the only thing this ADR buys is that the *next* boundary question gets answered by the org record
  instead of by whichever repo notices it first.

## References

- [#313](https://github.com/FS-GG/.github/issues/313) — this item; the acceptance criterion of
  FS.GG.Game#44 that could not be discharged in-repo (`FS.GG.Game` has no `docs/adr/`, and ADR-0022
  lives here).
- [ADR-0022](0022-extract-fs-gg-game-as-an-sdd-driven-component.md) — the sim/render cut this amends.
- [Rendering ADR-0104](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/product/decisions/0104-canvas-loop-is-a-simulation-primitive.md)
  — the repo-local execution record (Rendering#269).
- FS.GG.Game [#44](https://github.com/FS-GG/FS.GG.Game/issues/44) / [#61](https://github.com/FS-GG/FS.GG.Game/issues/61) — `Loop` lands in `Game.Core`.
- Rendering [#266](https://github.com/FS-GG/FS.GG.Rendering/issues/266) (the `NaN` freeze in the upstream copy),
  [#319](https://github.com/FS-GG/FS.GG.Rendering/issues/319) / [#355](https://github.com/FS-GG/FS.GG.Rendering/issues/355) (the `0.6.0` retirement).
- Registry: `game-sim-core` (`registry/dependencies.yml`) — `0.3.0` carried `Loop`; now `0.4.0`.
