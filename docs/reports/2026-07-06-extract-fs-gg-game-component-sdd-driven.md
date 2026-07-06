# Extract FS.GG.Game as an SDD-driven component — implementation plan

- **Date:** 2026-07-06
- **Owner:** `.github` (cross-repo coordination); **implementation home:** new `FS-GG/FS.GG.Game` + `FS.GG.Rendering` (the donor).
- **Status:** In execution. The decision of record is **[ADR-0022](../adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md)** (Proposed; merged to `main` via [#220](https://github.com/FS-GG/.github/pull/220), 2026-07-06); this document is the executable plan behind it.
- **Progress (2026-07-06):** **P0 ✅ · P1 ✅ · P2 → next.** Epic **[#213](https://github.com/FS-GG/.github/issues/213)** on the Coordination board; phases P0–P5 = children [#214–#219](https://github.com/FS-GG/.github/issues/213). P0/P1 stamped Done (earned done-stamp, PR #220 merged). **Next gated action: create `FS-GG/FS.GG.Game`** (P2 [#216](https://github.com/FS-GG/.github/issues/216), runbook in the issue).
- **Scope decisions locked (2026-07-06):**
  1. **Full component** — both the render-independent sim primitives *and* the game starter/skills leave Rendering for a new `FS.GG.Game` repo.
  2. **SDD as the new repo's own dev lifecycle** (the dogfood) — `fsgg-sdd` drives `FS.GG.Game`'s `charter → ship` process, **coexisting** with Spec Kit's `specs/NNN-*` history, not replacing it.
  3. **Consumer scaffolding deferred** — no `game` scaffold provider in this epic. Rendering's `dotnet new fs-gg-ui --profile game` is **frozen** as-is; a consumer provider is a named sequel epic.
- **Why:** Game logic is render-independent, but today it lives *inside* the render core — `FS.GG.UI.Canvas.Pathfinding` / `.SpatialGrid` / `FS.GG.UI.Scene.Geometry`, the `game` template profile, four `game`-gated skills, plus a large and still-growing design corpus (6 game-logic library designs + 15 game TestSpecs in this repo). A profile flag is the wrong container for a subsystem this size, and each addition currently accretes game logic into UI packages. Extraction restores the render core to *rendering* and gives the game subsystem a home that can grow and ship on its own cadence.

---

## 1. Executive summary

`FS.GG.Game` becomes the platform's sixth repository and its new **bottom layer**: a pure, BCL-only simulation core (`FS.GG.Game.Core`) that depends on nothing, plus a thin `FS.GG.Game.Render` adapter that maps sim state onto `FS.GG.UI.Scene`. The **one-way dependency rule** is preserved — `Game.Core` reaches up to nothing; `Game.Render` reaches up to Rendering; Rendering still reaches up to nothing. A headless game sim builds and tests with zero Skia.

The extraction is sequenced as a publish-before-flip epic on the Coordination board (mirroring how every cross-repo change in this org lands). Two edges carry all the real risk:

- **The primitive cut line hides a forced SemVer major** on `FS.GG.UI.Canvas`, and **`FS.GG.UI.Scene.Geometry` is ambiguous** — it may be shared render geometry, not game-only. That decision must be settled by a usage audit *before* the ADR is finalized (§4).
- **Dogfooding `fsgg-sdd` on a real component reverses a standing decision** (Spec-Kit-per-repo) and **will immediately exercise the "provider-less provenance" gap** — a repo `init`'d by hand has no `fs-gg-ui-template` pin for `doctor`/`upgrade`/`scaffold-provenance` to anchor to. That is a forcing function, not a blocker (§5).

Everything else is well-trodden org machinery: a roster row (ADR-0019), registry contract rows + a coherence gate (ADR-0007/§5 of `architecture.md`), a skill-ownership migration in `skills.yml`, and an `architecture.md` reconcile from "five repositories" to six.

---

## 2. Target architecture

```text
                       FS-GG/.github  (coordination — registry, roster, ADRs)
                                        │
   ┌───────────────┬────────────────────┼───────────────────┬──────────────────┐
   │               │                    │                    │                  │
┌──┴────────┐ ┌────┴──────┐   ┌─────────┴────────┐   ┌───────┴───────┐   ┌──────┴───────┐
│FS.GG.SDD  │ │FS.GG.Gov. │   │ FS.GG.Rendering  │   │ FS.GG.Templates│  │  FS.GG.Game  │  ← NEW
│lifecycle  │ │inference  │   │ UI framework     │   │ scaffold comp. │  │  simulation  │
│+Contracts │ │kernel     │   │ FS.GG.UI.*       │   │                │  │  FS.GG.Game.*│
└───────────┘ └───────────┘   └──────────────────┘   └────────────────┘  └──────────────┘

Dependency edges (downstream → upstream):
  FS.GG.Game.Render ──▶ FS.GG.UI.Scene      (adapter reaches UP to Rendering — allowed)
  FS.GG.Game.Core   ──▶ (nothing)           (new BOTTOM layer, BCL-only, sibling to Rendering)
  Rendering         ──▶ (nothing)           (unchanged — still reaches up to nothing)
```

### 2.1 Package layout of `FS.GG.Game`

| Project | Kind | Depends on | Public surface (`.fsi`) |
|---|---|---|---|
| `FS.GG.Game.Core` | packable lib | FSharp.Core only | sim vocabulary — RNG, fixed-step, collision, pathfinding, grids/spatial partitioning, FOV/LOS, ECS/state model |
| `FS.GG.Game.Render` | packable lib | `Game.Core` + `FS.GG.UI.Scene` | `sim-state → Scene` adapters (drawable projection of sim entities) |
| `FS.GG.Game.Template` *(later)* | `dotnet new` pkg | — | the Pong starter + game skills (moves here; **not** wired to a provider in this epic) |
| `FS.GG.Game` *(optional BOM)* | metapackage | — | pins `Game.Core`/`.Render` at one exact version (mirrors the `FS.GG.UI` BOM) |

House style carries over verbatim: `.fsi` as sole public surface with committed surface baselines, pure cores / I-O at the edge, `net10.0` + FSharp.Core `10.1.301`, central package management with locked restore, deterministic builds, warnings-as-errors, `Json-is-contract / Plain+Rich-are-projections`. The game-logic design corpus already commits to *integer logic / float presentation* and *byte-identical determinism as a tested property* (see the `2026-07-05-game-logic-*` reports) — those become `Game.Core`'s core invariants.

---

## 3. What moves, and from where

| Asset | Today | Destination | Break class |
|---|---|---|---|
| `FS.GG.UI.Canvas.Pathfinding` (A*/BFS) | `FS.GG.UI.Canvas` | `FS.GG.Game.Core` | **ApiCompat major on Canvas** |
| `FS.GG.UI.Canvas.SpatialGrid` | `FS.GG.UI.Canvas` | `FS.GG.Game.Core` | **ApiCompat major on Canvas** |
| `FS.GG.UI.Canvas.Rng` (SplitMix64) | `FS.GG.UI.Canvas` | `FS.GG.Game.Core` | **ApiCompat major on Canvas** |
| `FS.GG.UI.Canvas.FixedStep` | `FS.GG.UI.Canvas` | `FS.GG.Game.Core` | **ApiCompat major on Canvas** |
| `FS.GG.UI.Scene.Geometry` (Vec2/AABB) | `FS.GG.UI.Scene` | **UNDECIDED — see §4** | possible major on Scene |
| collision / grids / line-drawing / visibility fragments (#132) | Rendering `template/` fragments | `FS.GG.Game.Template` (or shared) | content move |
| `fs-gg-game-core`, `fs-gg-audio`, `fs-gg-persistence`, `fs-gg-model-swap` skills | owner `fs-gg-rendering` in `skills.yml` | owner `fs-gg-game` | **skill-registry ownership migration** |
| the `game` TestSpecs (15) + game-logic design reports (6) | `.github/docs/` | stay in `.github` (design corpus) or move to `FS.GG.Game/docs/` | doc relocation (decide in ADR) |

The four `Canvas.*` primitives are unambiguously game-sim and move cleanly (at the cost of the Canvas major). **`Scene.Geometry` is the exception** and is treated separately.

---

## 4. Edge #1 — the cut line and the forced major

### 4.1 The Canvas major is real and expected

Removing public types from `FS.GG.UI.Canvas` trips the SDK Package Validation / ApiCompat gate, which **forces a SemVer major** on that package against the published-feed baseline; the registry version ranges then enforce it downstream. This is a normal, gated move in this org — do it as a **clean move + major bump coordinated as a `contract-change`**, *not* as `[<Obsolete>]` re-export aliases. Leaving game names in the `FS.GG.UI.*` namespace as forwarders re-creates exactly the ownership smell the extraction exists to remove.

### 4.2 `Scene.Geometry` — **AUDITED & SETTLED (P0, 2026-07-06)**

> Full audit: `2026-07-06-p0-scene-geometry-cut-line-audit.md`. Summary below; that report is the record.

The audit corrected this section's premise. **`Scene.Geometry` is not a `Vec2`/`AABB` type** — it is a public module of six pure functions (`intersects`, `contains`, `containsPoint`, `center`, `ofCenter`, `sweptIntersects`) over the shared Scene `Rect`/`Point`. There is no `Vec2` or `AABB` type in the Scene package (`Vec2` is a template-fragment/product type; `AABB` is an algorithm comment). So the A/B/C options — built on a shared `Vec2`/`AABB` type — do not apply.

What the live source shows:

- `Rect`/`Point` (in `src/Scene/Types.fsi`) are **irreducibly render-core** — 42 `src` files across 8 subsystems. They stay in Scene.
- The `Geometry` **functions** are **de-facto game-only**: the sole in-`src` consumer is `Canvas/SpatialGrid.fs` (itself a moving primitive); the rest are the game collision fragment and game-flavored tests. Controls hand-rolls its own `ChartGeometry`/`WidgetGeometry` and never calls `Geometry.*`.

**Decision — Option D (owner-confirmed):** `Rect`/`Point` stay in Scene; the `Geometry` collision module **moves to `Game.Core`**, reimplemented over `Game.Core`'s own BCL-only primitives (no dependency on Scene, no layering inversion). Scene loses its public `Geometry` module → **Scene takes an ApiCompat major.**

- **`FS.GG.Math` leaf: NO** (open decision #2 closed) — nothing is shared beneath both Scene and `Game.Core`.
- **Blast radius: two majors (Canvas + Scene), no new package.** The Scene major is from removing a game-only module, not from birthing a math leaf.

---

## 5. Edge #2 — SDD as the dev lifecycle (the dogfood)

### 5.1 What it concretely looks like

`FS.GG.Game` is `init`'d with SDD and developed through it:

```sh
cd FS.GG.Game
fsgg-sdd init                 # writes .fsgg/ (project.yml/sdd.yml/agents.yml/constitution.md)
fsgg-sdd charter              # → specify → clarify → checklist → plan → tasks → analyze
#   ... you implement (no `implement` command — SDD brackets, it does not author) ...
fsgg-sdd evidence             # work/<id>/evidence.yml records that it happened
fsgg-sdd verify → ship        # readiness/<id>/*, merge-boundary stages
```

Optionally the **light governance overlay** is dropped in (the four `.fsgg/*.yml` slots) so the repo also dogfoods advisory gates — governance only inspects, never a build dependency.

### 5.2 The two things the ADR must settle

1. **Coexist, don't replace.** House style says *every* repo keeps `specs/NNN-*` and standard Spec Kit; `project-split-decision.md` deliberately kept SDD's scope narrow. `fsgg-sdd` *brackets* implementation (no `implement` command; it wraps evidence *around* your code), so it **complements** Spec Kit authoring rather than replacing it. ADR-0022 records: `FS.GG.Game` runs `.fsgg/` lifecycle **alongside** `specs/` history. Setting this expectation explicitly prevents the drift of "is SDD our spec tool now?"
2. **This surfaces the provider-less provenance gap, by design.** A hand-`init`'d dev repo has **no** `fs-gg-ui-template` pin, so `scaffold-provenance` / `doctor` / `upgrade` — all built around the coherent-set template pin — have nothing to anchor to. `FS.GG.Game` becomes the real workload that forces `fsgg-sdd` to define a coherent **no-template lifecycle** (a "provider: none / dev-repo" provenance shape). Budget this as an explicit SDD work item inside the epic, not a surprise mid-flight. It is the concrete pay-down of the gap named in the prior architecture discussion.

---

## 6. Consumer deferral — freeze `--profile game`

Because the consumer `game` scaffold provider is **out of scope**, the existing `dotnet new fs-gg-ui --profile game` must not lose its starter:

- **Decision: freeze.** Rendering keeps the current `game` profile exactly as-is, pinned; `FS.GG.Game` develops the future starter (`FS.GG.Game.Template`) in parallel; consumer migration is a **named sequel epic**.
- **Explicitly rejected: re-source.** Having Rendering's `game` profile pull the starter/skills live from `FS.GG.Game` is ~half the provider work we deferred — it contradicts the scope choice and is deferred with the rest.

Consequence: for the duration of this epic there are **two** copies of the game starter (Rendering's frozen profile + the nascent `FS.GG.Game.Template`). That is an accepted, temporary N-copies cost, tracked as a coherence row so it can't be forgotten, and retired by the sequel provider epic.

---

## 7. Registry / roster / skill / coherence onboarding

Everything a "new component" costs in this org's machinery:

- **ADR-0022** — "Extract FS.GG.Game as a component; develop it with SDD." Records the split rationale, the dependency direction, the §4 Geometry decision, and the Spec-Kit-coexists-with-SDD call. Per the system-overview obligation, the same PR reconciles `docs/architecture.md` (five → six repositories).
- **`registry/repos.yml`** (ADR-0019) — add the roster row `{ id: game, full: FS-GG/FS.GG.Game, role: framework, receives: [labels, coordination-kit] }`; bump `updated:`, prepend a `registry/repos.CHANGELOG.md` entry, run `scripts/repos.sh` (shell+jq, self-contained). Slice-2 `coordination-sync` then distributes the kit (the two coordination skills + `fsgg-coord` client) into the new repo; the coherence gate asserts the bytes.
- **`registry/dependencies.yml`** — new repo entry + contract rows for `FS.GG.Game.Core` / `.Render` (owner, surface = the package, consumers), the **Canvas major-bump edge**, the `Game.Render → FS.GG.UI.Scene` dependency edge, and a `coherence:` row for the extraction (`coherent: false` until published-and-flipped). Human-project into `docs/registry/compatibility.md` in the same PR (the review of 2026-07-02 found the projection is convention-maintained — do not skip it). Version derivation for the new packages follows ADR-0007.
- **`registry/skills.yml`** — migrate `fs-gg-game-core` / `-audio` / `-persistence` / `-model-swap` ownership `fs-gg-rendering → fs-gg-game`, keep/adjust `materializes-when` (they are `profile in [game, sample-pack]` today), and re-point the skill-union gate + canonical `sha256`s to the new source paths.
- **Labels & CI fabric** — the roster row grants `labels`; the reusable gates (`contract-coherence`, lockfile-sync, dispatch-sender, skill-union-assert) apply once the repo is created and the org-shared build config is synced from `dist/dotnet/` via `sync-build-config.sh`.
- **Coordination epic** — ✅ **open (2026-07-06):** epic [#213](https://github.com/FS-GG/.github/issues/213) with phase children [#214–#219](https://github.com/FS-GG/.github/issues/213) as sub-issues on the Coordination board (Projects v2 #1), fields + `Blocked by` chain set, publish-before-flip.

---

## 8. Phased implementation sequence

Publish-before-flip throughout (FR-007): publish the artifact, verify it live on the feed, *then* flip the registry `coherent:` flag.

| Phase | Deliverable | Repos touched | Exit condition |
|---|---|---|---|
| **P0 — Decide** ✅ | §4 usage audit of `Scene.Geometry`; settle the cut line. | Rendering (read) | **DONE 2026-07-06:** Option D — `Rect`/`Point` stay in Scene, `Geometry` module moves to `Game.Core`; Scene takes a major; **no `FS.GG.Math` leaf**. Record: `2026-07-06-p0-scene-geometry-cut-line-audit.md`. |
| **P1 — Record** ✅ | ADR-0022 + Coordination epic + `architecture.md` reconcile (5→6) + roster row. | `.github` | **DONE 2026-07-06 (PR #220, epic #213):** ADR-0022 merged to `main`; epic #213 + children #214–#219 open; `game` roster row landed (`repos.sh` green, contract-coherence + projection gates green). |
| **P2 — Stand up** ⏳ next | Create `FS-GG/FS.GG.Game`; sync build config; `FS.GG.Game.Core` = the clean-move primitives (`Rng`/`FixedStep`/`Pathfinding` are Scene-independent; `SpatialGrid` + the ported `Geometry` need Game.Core's own `Point`/`Rect` vocabulary — decided in P3). Canvas **and Scene** major bumps prepared in Rendering (not yet released). **No `FS.GG.Math` leaf** (P0). Runbook: #216. | `.github`, Rendering, Game | `Game.Core` builds/tests headless; surface baseline committed; `game` added to board `Repo Scope`. |
| **P3 — Dogfood** | `fsgg-sdd init` the repo; run first feature (`charter→ship`) through `.fsgg/`; surface & close the no-template provenance shape in SDD. | Game, SDD | One feature shipped via SDD; provenance gap has a defined dev-repo shape. |
| **P4 — Adapter + content** | `FS.GG.Game.Render` (Scene adapter); move starter fragments + game skills into `FS.GG.Game.Template`; migrate `skills.yml` ownership. Freeze Rendering `--profile game`. | Game, Rendering, `.github` | Render adapter emits Scene; skill-union gate green with new owner. |
| **P5 — Publish** | Release `Game.Core`/`.Render` to org feed (+ nuget.org dual-publish per ADR-0012/0013); flip the extraction `coherent:` row; release Rendering's Canvas **and Scene** majors; update registry ranges + `compatibility.md`. | Game, Rendering, `.github` | Packages live on both feeds; `coherent: true`; ApiCompat gate green on the new majors. |
| **P6 — Sequel (out of scope here)** | `game` scaffold provider (Templates descriptor + `FS.GG.Game.Template` provider + composition lane); retire the frozen `--profile game`. | Templates, Game, Rendering | *Named future epic — not this plan.* |

---

## 9. Risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `Scene.Geometry` is shared render geometry; wrong cut breaks layering or forces a second major | **High** | P0 usage audit is a hard gate on the ADR; prefer Option C (`FS.GG.Math` leaf) if shared. |
| Canvas major ripples to every downstream consumer of the removed primitives | Medium | Registry ranges + ApiCompat gate already enforce the major; the game-starter is the main consumer and moves with the primitives. |
| Dogfooding SDD exposes an unbounded provenance/`doctor` rework | Medium | Scope the SDD work to a **defined** "dev-repo / provider: none" provenance shape in P3; time-box; do not gold-plate. |
| SDD-vs-Spec-Kit role confusion | Medium | ADR-0022 states *coexist, not replace*, and names the no-`implement` bracket boundary explicitly. |
| Two game-starter copies during the freeze (§6) drift | Low/Med | Track as a `coherence:` row; the sequel provider epic retires it; no consumer contract change in between. |
| Skill-union gate red during ownership migration | Low | Migrate `skills.yml` owner + `sha256` + source path atomically in P4; run the union assert in the same PR. |
| New coherent-set axis adds standing maintenance | Low | Accepted cost of a component; it inherits the existing reusable gates rather than bespoke CI. |

---

## 10. Open decisions (must close before/inside ADR-0022)

1. ~~**`Scene.Geometry` home**~~ — **CLOSED (P0, 2026-07-06): Option D.** `Rect`/`Point` stay in Scene; the `Geometry` module moves to `Game.Core` (reimplemented BCL-only). Scene takes an ApiCompat major.
2. ~~**`FS.GG.Math` leaf**~~ — **CLOSED (P0, 2026-07-06): NO.** Nothing is shared beneath both Scene and `Game.Core`.
3. **Design-corpus location** — do the 15 game TestSpecs + 6 game-logic design reports stay in `.github/docs/` (coordination-owned) or relocate to `FS.GG.Game/docs/`? Recommend: **relocate the game-logic library designs to `FS.GG.Game`**, keep the TestSpecs where cross-repo tests reference them.
4. **Governance dogfood** — does `FS.GG.Game` adopt the light governance overlay in P3, or SDD-only first? Recommend SDD-only in P3, governance as a fast-follow.
5. **BOM** — ship the `FS.GG.Game` metapackage now or defer until there are ≥3 members.

---

## 11. Appendix — ADR-0022 skeleton

```md
# ADR-0022 — Extract FS.GG.Game as an SDD-driven component

## Status
Proposed (2026-07-06)

## Context
- Game logic is render-independent but lives inside the render core (Canvas.*/Scene.Geometry),
  the `game` template profile, and four `game`-gated skills; the subsystem (15 TestSpecs +
  6 design reports) has outgrown a profile flag.
- The one-way dependency rule (Rendering reaches up to nothing) must survive the split.

## Decision
1. Create FS-GG/FS.GG.Game: `FS.GG.Game.Core` (BCL-only, new bottom layer) + `FS.GG.Game.Render`
   (adapter, depends up on FS.GG.UI.Scene).
2. `Scene.Geometry`: Option D (per P0 audit) — `Rect`/`Point` stay in Scene; the `Geometry`
   module moves to `Game.Core` reimplemented BCL-only. Scene takes a major. No `FS.GG.Math` leaf.
3. Develop FS.GG.Game with `fsgg-sdd` as its lifecycle, COEXISTING with Spec Kit `specs/`.
4. Defer the consumer `game` scaffold provider; FREEZE `dotnet new fs-gg-ui --profile game`.

## Consequences
- Forced SemVer major on FS.GG.UI.Canvas AND FS.GG.UI.Scene (Option D removes the game-only
  Geometry module from Scene). No FS.GG.Math leaf.
- New coherent-set axis: roster row, registry contract rows + coherence gate, skill-ownership
  migration, architecture.md 5→6.
- SDD must define a provider-less "dev-repo" provenance shape (pays down the known gap).
- Two game-starter copies during the freeze (tracked); retired by the sequel provider epic.
```

### Registry row sketches (illustrative — finalize against live versions in P2/P5)

```yaml
# registry/repos.yml
- { id: game, full: FS-GG/FS.GG.Game, role: framework, receives: [labels, coordination-kit] }

# registry/dependencies.yml (contracts:)
- name: game-sim-core          # owner: FS.GG.Game — FS.GG.Game.Core package (BCL-only sim)
- name: game-scene-adapter     # owner: FS.GG.Game — FS.GG.Game.Render → FS.GG.UI.Scene
# dependencies: FS.GG.Game.Render → FS.GG.UI.Scene (downstream → upstream)
# coherence: game-extraction (coherent: false until P5 publish-and-flip)
```

---

## 12. Where to start

**P0 ✅ and P1 ✅ are done** (2026-07-06): the `Scene.Geometry` cut line is settled (Option D — touches *two* packages, Canvas + Scene; no `FS.GG.Math` leaf), ADR-0022 is merged, and the Coordination epic (#213) is open with all phases sequenced.

**P2 is the live front (#216).** Its one gated action is creating the `FS-GG/FS.GG.Game` org repo — everything upstream (audit, ADR, roster, epic) is complete, and the extraction is turnkey (~640 LOC, three Scene-independent primitives move clean; the runbook is on #216). The primitive move (P2) and the SDD dogfood (P3) are the two milestones that prove the design; the consumer provider (P6) is deliberately a separate future epic.
