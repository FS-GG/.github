# ADR-0022: Extract FS.GG.Game as an SDD-driven component

- **Status:** Accepted
- **Date:** 2026-07-06
- **Affects:** **.github** (this ADR + roster row + registry contracts + architecture map 5→6 + skill-ownership migration), **FS.GG.Rendering** (donor — Canvas + Scene majors, frozen `--profile game`), **FS-GG/FS.GG.Game** (new repo), **FS.GG.SDD** (dogfood lifecycle host — defines the provider-less "dev-repo" provenance shape)
- **Amended by:** [ADR-0033](0033-fixed-step-double-buffer-is-a-simulation-primitive.md) — §Decision 1's `FS.GG.Game.Core` inventory says `fixed-step`, and is **silent on the double-buffered loop built on it**. That silence left `Loop`/`StepState` upstream in Canvas while the accumulator they are built on moved down, splitting one accumulator across two repos; the two copies then diverged on non-finite input. ADR-0033 ratifies the buffer as a **simulation** primitive owned by `FS.GG.Game.Core`. The cut below is otherwise unchanged.

## Context

Game logic is render-independent, but today it lives *inside* the render core:
the four `FS.GG.UI.Canvas.*` sim primitives (`Pathfinding` A*/BFS, `SpatialGrid`,
`Rng` SplitMix64, `FixedStep`), the `Scene.Geometry` collision module, the `game`
template profile, four `game`-gated skills (`fs-gg-game-core` / `-audio` /
`-persistence` / `-model-swap`), and a large, still-growing design corpus (6
game-logic library designs + 15 game TestSpecs in this repo). A profile flag is
the wrong container for a subsystem this size, and each addition currently accretes
game logic into `FS.GG.UI.*` packages — the exact ownership smell this record removes.

The one rule that keeps the platform honest — **the dependency direction is one-way;
Rendering depends on no other FS-GG component** (`architecture.md` §2) — must survive
the split. So must house style: `.fsi`-as-sole-surface with committed baselines, pure
cores / I/O at the edge, `net10.0` + FSharp.Core `10.1.301`, locked restore,
deterministic builds, `Json-is-contract / Plain+Rich-are-projections`.

Two edges carry the real risk. Both were pre-resolved:

- **The cut line and a possible forced major on Scene.** Settled by the P0 usage
  audit (`docs/reports/2026-07-06-p0-scene-geometry-cut-line-audit.md`). The audit
  corrected the framing: `Scene.Geometry` is *not* a `Vec2`/`AABB` type — it is a
  public module of six pure functions over the shared, render-core `Rect`/`Point`
  (defined in `src/Scene/Types.fsi`, used by 42 `src` files across 8 subsystems).
  `Rect`/`Point` cannot move; the `Geometry` functions are de-facto game-only (sole
  in-`src` consumer is `Canvas/SpatialGrid.fs`, itself a moving primitive; Controls
  hand-rolls its own internal geometry).
- **Dogfooding `fsgg-sdd` on a real component reverses a standing expectation**
  (Spec-Kit-per-repo, `project-split-decision.md`) and immediately exercises the
  provider-less-provenance gap: a repo `init`'d by hand has no `fs-gg-ui-template`
  pin for `doctor` / `upgrade` / `scaffold-provenance` to anchor to.

## Decision

1. **Create `FS-GG/FS.GG.Game`** as the platform's sixth repository and its new
   **bottom layer**:
   - `FS.GG.Game.Core` — packable, **FSharp.Core-only** sim core (RNG, fixed-step,
     collision, pathfinding, grids/spatial partitioning, FOV/LOS, ECS/state model).
     Reaches up to **nothing**. Sibling to Rendering at the bottom of the graph.
     *(Amended by [ADR-0033](0033-fixed-step-double-buffer-is-a-simulation-primitive.md):
     "fixed-step" includes **the double-buffered fixed-step loop built on it** —
     `StepState` / `Loop.init` / `advance` / `alpha`. Reading it as the bare accumulator is
     what left the buffer in `FS.GG.UI.Canvas`.)*
   - `FS.GG.Game.Render` — packable adapter, depends on `Game.Core` + `FS.GG.UI.Scene`,
     mapping sim state onto `Scene` (drawable projection). Reaches **up** to Rendering
     (allowed — downstream → upstream).
   - `FS.GG.Game.Template` *(later)* — the Pong starter + game skills move here; **not**
     wired to a scaffold provider in this epic (§4).
   - `FS.GG.Game` *(optional BOM)* — a metapackage pinning the members at one exact
     version, mirroring the `FS.GG.UI` BOM. Ship-or-defer per open decision (§ below).

2. **`Scene.Geometry`: Option D** (per the P0 audit). `Rect`/`Point` **stay in Scene**
   as render-core primitives. The `Geometry` collision module **moves to `Game.Core`**,
   reimplemented over `Game.Core`'s own BCL-only primitives — so `Game.Core` does not
   reference Scene and the BCL-only-bottom property holds. Scene loses its public
   `Geometry` module. **No `FS.GG.Math` leaf is born** — nothing is shared beneath both
   Scene and `Game.Core`.

3. **Develop `FS.GG.Game` with `fsgg-sdd` as its lifecycle, COEXISTING with Spec Kit.**
   The repo runs the `.fsgg/` lifecycle (`charter → … → ship`) *alongside* a standard
   `specs/NNN-*` history — it does not replace it. `fsgg-sdd` *brackets* implementation
   (there is no `implement` command; it wraps evidence *around* your code — see
   `architecture.md` §4.2), so it complements Spec Kit authoring rather than supplanting
   it. This is the deliberate dogfood: `FS.GG.Game` is the first repo whose own dev
   lifecycle is SDD.

4. **Defer the consumer `game` scaffold provider; FREEZE `dotnet new fs-gg-ui --profile
   game`.** Rendering keeps its current `game` profile exactly as-is, pinned; `FS.GG.Game`
   develops the future starter in parallel; consumer migration is a **named sequel epic**.
   Re-sourcing Rendering's profile live from `FS.GG.Game` is explicitly rejected (≈half
   the deferred provider work). Accepted, tracked cost: two game-starter copies during the
   freeze.

## Consequences

- **Forced SemVer major on `FS.GG.UI.Canvas` AND `FS.GG.UI.Scene`.** Canvas loses the
  four sim primitives; Scene loses the game-only `Geometry` module. Both trip the SDK
  Package Validation / ApiCompat gate against the published-feed baseline, and the
  registry version ranges enforce the majors downstream. This is a **clean move + major
  bump coordinated as a `contract-change`**, *not* `[<Obsolete>]` re-export aliases —
  leaving game names in `FS.GG.UI.*` re-creates the ownership smell the extraction removes.
  **No `FS.GG.Math` leaf** (the P0 audit closed that).
- **A new coherent-set axis and full "new component" onboarding.** Roster row
  (`registry/repos.yml`, ADR-0019); registry contract rows for `game-sim-core` /
  `game-scene-adapter`, the Canvas + Scene major-bump edges, the `Game.Render →
  FS.GG.UI.Scene` dependency edge, and a `coherence:` row (`coherent: false` until
  publish-and-flip); skill-ownership migration in `registry/skills.yml`
  (`fs-gg-rendering → fs-gg-game` for the four game skills, with re-pointed source paths +
  `sha256`s + union gate); and this ADR reconciles `docs/architecture.md` from five to six
  repositories (the system-overview obligation).
- **SDD must define a provider-less "dev-repo" provenance shape.** A hand-`init`'d dev
  repo has no `fs-gg-ui-template` pin, so `scaffold-provenance` / `doctor` / `upgrade`
  have nothing to anchor to. `FS.GG.Game` is the forcing workload that makes `fsgg-sdd`
  define a coherent no-template lifecycle (a `provider: none` / dev-repo provenance). This
  is a **scoped, time-boxed** SDD work item inside the epic — pays down a known gap; do
  not gold-plate.
- **SDD-vs-Spec-Kit role stays explicit.** Decision 3 records *coexist, not replace* and
  names the no-`implement` bracket boundary, heading off "is SDD our spec tool now?" drift.
- **Two game-starter copies during the freeze** (Rendering's frozen profile + the nascent
  `FS.GG.Game.Template`). Accepted, temporary N-copies cost, tracked as a `coherence:` row;
  retired by the sequel provider epic (no consumer contract change in between).
- **Sequencing is publish-before-flip** (FR-007), board-sequenced on Coordination: publish
  `Game.Core`/`.Render` (org feed + nuget.org dual-publish, ADR-0012/0013), verify live,
  *then* flip `coherent:` and release Rendering's Canvas + Scene majors. Phased plan:
  `docs/reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md`.

### Open items carried into the epic (not blocking this ADR)

- **Design-corpus location** — relocate the 6 game-logic library designs to `FS.GG.Game`.
  The companion recommendation to *keep the 15 game TestSpecs in `.github`* was **superseded
  by [ADR-0029](0029-game-owns-the-testspec-corpus.md)**: no code references the specs (the
  "cross-repo tests reference them" premise did not hold) and the `.github`-canonical layout
  produced the ADR-0024 audio-API drift ([.github#393](https://github.com/FS-GG/.github/pull/393)),
  so FS.GG.Game now owns the whole corpus and `.github` keeps pointer stubs.
- **Governance dogfood** — SDD-only in P3; the light governance overlay as a fast-follow.
- **BOM** — ship the `FS.GG.Game` metapackage now or defer until ≥3 members.
