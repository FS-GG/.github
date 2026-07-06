# Extract FS.GG.Game as an SDD-driven component — implementation plan

- **Date:** 2026-07-06
- **Owner:** `.github` (cross-repo coordination); **implementation home:** new `FS-GG/FS.GG.Game` + `FS.GG.Rendering` (the donor).
- **Status:** In execution. The decision of record is **[ADR-0022](../adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md)** (Proposed; merged to `main` via [#220](https://github.com/FS-GG/.github/pull/220), 2026-07-06); this document is the executable plan behind it.
- **Progress (2026-07-06):** **P0 ✅ · P1 ✅ · P2 ✅ · P3 ✅ · P4 ✅ · P5 ✅ · P6 → sequel (out of scope).** Epic **[#213](https://github.com/FS-GG/.github/issues/213)** on the Coordination board; phases P0–P5 = children [#214–#219](https://github.com/FS-GG/.github/issues/213). **The extraction is complete: P5 published-and-flipped.** **P5 (#219):** (a) **`FS.GG.Game.Core` + `FS.GG.Game.Render` 0.1.0-preview.1 published** to the org feed **and** nuget.org — FS.GG.Game grew its first `release.yml` (org-feed + nuget.org OIDC dual-publish, ADR-0012/0013) and cut `v0.1.0-preview.1` ([FS.GG.Game#5](https://github.com/FS-GG/FS.GG.Game/pull/5), release run 28803118900, both packages "Your package was pushed" on both feeds). (b) The **Canvas + Scene SemVer majors executed** — the sim primitives (`Rng`/`FixedStep`/`Pathfinding`/`SpatialGrid`) + the `Geometry` module physically removed from `FS.GG.UI.Canvas`/`.Scene`, the frozen `--profile game` fragments **re-homed** to consume `FS.GG.Game.Core` (on a new `$(FsGgGameVersion)` axis), baselines/tests/api-surface/SKILL.md reconciled, and released as the `FS.GG.UI` coherent set **0.2.0-preview.1** ([FS.GG.Rendering#155](https://github.com/FS-GG/FS.GG.Rendering/pull/155), tag triple `v`/`fs-gg-ui/v`/`fs-gg-ui-template/v` `0.2.0-preview.1`, release run 28807190647; ApiCompat gate green on the removals, the generated-product template-instantiation gate built a real `--profile game` product green). (c) The **registry flipped** — `game-extraction` **coherent:true**, new contracts `game-sim-core`/`game-scene-adapter`, the `rendering→game` + `game→rendering` edges, and the `fs-gg-ui-template` 0.2.0 bump ([.github#228](https://github.com/FS-GG/.github/pull/228); `fsgg-sdd registry validate` valid, projection + architecture.md reconciled). Publish-before-flip (FR-007) throughout. **`game-starter-two-copies` stays `coherent:false`** — retired at **P6** (the consumer `game` scaffold provider), the one remaining sequel. **Earlier — P4 (#218 closed — board Done):** the **render edge** + **skill-ownership migration** landed. (a) **`FS.GG.Game.Render`** — a pure Scene adapter projecting `Game.Core` primitives (`Point`/`Rect`/`Cell`, pathfinding routes) onto `FS.GG.UI.Scene` drawables ([FS.GG.Game#3](https://github.com/FS-GG/FS.GG.Game/pull/3), SDD feature 002; consumes `FS.GG.UI.Scene@0.1.64-preview.1` from nuget.org — no org-feed credential; the `Game.Render → FS.GG.UI.Scene` edge is established; Game.Render.Tests 12/12, whole solution still headless). (b) The **4 game product skills** (`fs-gg-game-core`/`-audio`/`-persistence`/`-model-swap`) migrated `owner: fs-gg-rendering → fs-gg-game` **byte-identically** — FS.GG.Game grew a producer skill-manifest ([FS.GG.Game#4](https://github.com/FS-GG/FS.GG.Game/pull/4), SDD feature 003) that the registry reconciles from ([.github#225](https://github.com/FS-GG/.github/pull/225); registry = manifest = bytes, and the move corrected two digests drifted since 0.1.67). (c) **`--profile game` frozen** — Rendering unchanged; the accepted two-copies cost tracked as the new `game-starter-two-copies` coherence row ([.github#226](https://github.com/FS-GG/.github/pull/226)), retired at P6. **Scoped out of P4 (deferred to P5/P6, by design):** the game **fragments** (collision/grids/line-drawing/visibility) re-home — they hard-depend on `FS.GG.UI.Scene`/`Canvas` and are part of the frozen profile, so they move with the physical Canvas/Scene cut (P5) / the provider (P6), not the skill-union exit. **Next: P5 — publish** (#219): release `Game.Core`/`.Render` to the org feed + nuget.org, execute the staged Canvas + Scene majors, flip `game-extraction` coherent:true.
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
| **P2 — Stand up** ✅ | Create `FS-GG/FS.GG.Game`; sync build config; `FS.GG.Game.Core` = the clean-move primitives (`Rng`/`FixedStep`/`Pathfinding` Scene-independent; `SpatialGrid` + the ported `Geometry` over Game.Core's own BCL-only `Point`/`Rect`). Canvas **and Scene** major bumps **staged, not executed** in Rendering. **No `FS.GG.Math` leaf** (P0). Runbook: #216. | `.github`, Rendering, Game | **DONE 2026-07-06 (#216 closed, PR [#222](https://github.com/FS-GG/.github/pull/222)):** repo created public; `Game.Core` builds/tests **headless** (71/71 Expecto, zero Skia/Scene); surface baseline committed (11 types); CI wired + green (`gate` = locked-restore+build+test+surface-drift+build-config-drift, `coordination-coherence`, `lockfile-sync`); coordination kit + labels distributed; `game` on board `Repo Scope` (#216 set); registry `game-extraction` coherence row (`coherent:false`) + `architecture.md` reconciled. **Deferred by design:** the Canvas/Scene removal is *staged intent only* — executing it now breaks Rendering (the frozen `--profile game` collision fragment `open`s `FS.GG.UI.Scene.Geometry`; `Package.Tests` assert those surfaces), so the physical cut lands in P4/P5. |
| **P3 — Dogfood** ✅ | `fsgg-sdd init` the repo; run first feature (`charter→ship`) through `.fsgg/`; surface & close the no-template provenance shape in SDD. | Game, SDD | **DONE 2026-07-06 (#217 closed):** SDD **feature 085** ([SDD#158](https://github.com/FS-GG/FS.GG.SDD/pull/158)) — `init` writes a provider-less dev-repo `scaffold-provenance.json` (`outcome: devRepoInit`, no template pin, schema v1 additive); `doctor`/`upgrade` engage (`ProviderName=None`, `coherentByAbsence`, seeded-artifact reconcile). Released in **`fsgg-sdd` 0.8.0** (org feed + nuget.org). Dogfood [Game#2](https://github.com/FS-GG/FS.GG.Game/pull/2): FS.GG.Game `init`'d (dev-repo provenance, `doctor` coherent) + `Rng.nextBool` driven `charter→ship` (75/75). **Findings:** `init` blocks on an existing `.gitignore`; the seeded `readiness/*/` collides with committed `readiness/surface-baselines/`; the org-managed `.config/dotnet-tools.json` has no per-repo slot to pin the dogfood tool — all reconciled/deferred, tracked on #217. |
| **P4 — Adapter + content** ✅ | `FS.GG.Game.Render` (Scene adapter); move game skills into `FS.GG.Game.Template`; migrate `skills.yml` ownership. Freeze Rendering `--profile game`. | Game, Rendering, `.github` | **DONE 2026-07-06 (#218 closed):** `FS.GG.Game.Render` adapter emits Scene ([Game#3](https://github.com/FS-GG/FS.GG.Game/pull/3), edge established, 12/12 headless); 4 game skills migrated `owner rendering → game` byte-identically via a new Game producer manifest ([Game#4](https://github.com/FS-GG/FS.GG.Game/pull/4)) reconciled into `skills.yml` ([.github#225](https://github.com/FS-GG/.github/pull/225), registry = manifest = bytes, 2 drifted digests corrected); `--profile game` frozen, two-copies tracked as `game-starter-two-copies` ([.github#226](https://github.com/FS-GG/.github/pull/226)). **Fragments re-home deferred to P5/P6** (they `open FS.GG.UI.Scene`/`Canvas`; move with the physical cut/provider). No Rendering code change. |
| **P5 — Publish** ✅ | Release `Game.Core`/`.Render` to org feed (+ nuget.org dual-publish per ADR-0012/0013); flip the extraction `coherent:` row; release Rendering's Canvas **and Scene** majors; update registry ranges + `compatibility.md`. | Game, Rendering, `.github` | **DONE 2026-07-06 (#219):** FS.GG.Game.Core/.Render `0.1.0-preview.1` live on both feeds ([Game#5](https://github.com/FS-GG/FS.GG.Game/pull/5) + `v0.1.0-preview.1`, run 28803118900); Canvas+Scene majors released as `FS.GG.UI 0.2.0-preview.1` ([Rendering#155](https://github.com/FS-GG/FS.GG.Rendering/pull/155) + tag triple, run 28807190647; ApiCompat green, generated-product gate green, fragments re-homed to Game.Core); registry flipped `game-extraction` **coherent:true** + new contracts/edges ([.github#228](https://github.com/FS-GG/.github/pull/228), validator valid). Publish-before-flip throughout. |
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

**P0 ✅, P1 ✅, P2 ✅, and P3 ✅ are done** (2026-07-06): the `Scene.Geometry` cut line is settled (Option D — touches *two* packages, Canvas + Scene; no `FS.GG.Math` leaf), ADR-0022 is merged, the Coordination epic (#213) is open with all phases sequenced, **[FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) is stood up and live** (`FS.GG.Game.Core` builds/tests headless, CI green, coherence row merged — #216, PR #222), and the **`fsgg-sdd` dogfood shipped** — the provider-less **"dev-repo" provenance shape** is defined, merged, and released.

**P3 done (#217): the `fsgg-sdd` dogfood.** SDD **feature 085** ([SDD#158](https://github.com/FS-GG/FS.GG.SDD/pull/158)) closed Edge #2 (§5): `fsgg-sdd init` now writes a provider-less `.fsgg/scaffold-provenance.json` (`outcome: devRepoInit`, no template pin, schema v1 additive), so `doctor`/`upgrade` anchor to a hand-`init`'d repo (`ProviderName=None`, `coherentByAbsence`, seeded-artifact reconcile) instead of "nothing to reconcile". Released in **`fsgg-sdd` 0.8.0** (org feed + nuget.org). The dogfood landed as [Game#2](https://github.com/FS-GG/FS.GG.Game/pull/2): FS.GG.Game `init`'d against 0.8.0 (`doctor` coherent, provider-less) and a first feature — `Rng.nextBool` — driven `charter→ship` (Game.Core.Tests 75/75). The primitive move (P2) and this SDD dogfood (P3) are the two milestones that prove the design; the consumer provider (P6) is deliberately a separate future epic.

**P4 done (#218): adapter + content.** `FS.GG.Game.Render` (the Scene adapter) landed ([Game#3](https://github.com/FS-GG/FS.GG.Game/pull/3)); the 4 game skills migrated `owner rendering → game` byte-identically via a new Game producer manifest ([Game#4](https://github.com/FS-GG/FS.GG.Game/pull/4)) reconciled into `skills.yml` ([.github#225](https://github.com/FS-GG/.github/pull/225)); `--profile game` frozen with the two-copies cost tracked as `game-starter-two-copies` ([.github#226](https://github.com/FS-GG/.github/pull/226)). The starter **fragments** re-home was deferred to P5/P6 by design (they `open FS.GG.UI.Scene`/`Canvas` and move with the physical cut / the provider).

**P5 done (#219): publish-and-flip — the extraction is complete.** `FS.GG.Game.Core` + `.Render` `0.1.0-preview.1` published to the org feed + nuget.org (FS.GG.Game grew its first `release.yml` with OIDC dual-publish, cut `v0.1.0-preview.1`; [Game#5](https://github.com/FS-GG/FS.GG.Game/pull/5), run 28803118900). The staged Canvas + Scene SemVer majors executed in Rendering — the moved surfaces physically removed, the frozen `--profile game` fragments **re-homed** to consume `FS.GG.Game.Core` (new `$(FsGgGameVersion)` axis), baselines/tests/api-surface/SKILL.md reconciled, released as `FS.GG.UI 0.2.0-preview.1` ([Rendering#155](https://github.com/FS-GG/FS.GG.Rendering/pull/155), tag triple, run 28807190647; ApiCompat green on the removals, the generated-product template-instantiation gate built a real `--profile game` product green). The registry finalized the `game-sim-core`/`game-scene-adapter` contracts + the `rendering→game`/`game→rendering` edges + the `fs-gg-ui-template` 0.2.0 bump and flipped `game-extraction` **coherent:true** ([.github#228](https://github.com/FS-GG/.github/pull/228)) — publish-before-flip (FR-007) throughout. **Only P6 remains** (the sequel: the consumer `game` scaffold provider that retires the frozen `--profile game` + the `game-starter-two-copies` cost) — a named future epic, not this plan.

### P2 landing notes (for P4/P5 and future work)

- **The Canvas + Scene majors are *staged intent*, not executed.** Confirmed by live audit: removing `Geometry`/`SpatialGrid`/the primitives from Rendering *now* breaks Rendering's own build — the frozen `dotnet new fs-gg-ui --profile game` collision fragment does `open FS.GG.UI.Scene` + `Geometry.*`, and `Package.Tests` (`Feature240GameCoreSkillTests`, `Feature250CollisionSafeVec2Tests`) assert those surfaces. So the physical cut + surface-baseline refresh + test re-homing land in **P4** (skill-ownership migration + `--profile game` freeze) → **P5** (publish), per the §6 freeze.
- **Per-package `contracts:` rows deferred to P4/P5.** `Game.Core`/`.Render` aren't published and nothing cross-repo consumes them yet, so the only registry delta at P2 is the `game-extraction` coherence row (`coherent:false`). `game-sim-core`/`game-scene-adapter` + the `Game.Render → FS.GG.UI.Scene` edge finalize against live versions when the packages publish (ADR-0007).
- **CI note — hermetic FSharp.Core.** The `gate` locked-restore first tripped `NU1403`: the CI runner resolves FSharp.Core `10.1.301` from the .NET SDK's `FSharp/library-packs/` offline fallback (an MSBuild-level `RestoreAdditionalProjectFallbackFolders` a `nuget.config` `fallbackPackageFolders` clear cannot reach), recording a different content hash than a local nuget.org restore. Fixed by pinning both `packages.lock.json` to the SDK/CI hash (byte-identical to the value `FS.GG.Rendering`'s lockfile carries — the org-canonical value, since sibling lockfiles are generated on-runner). FSharp.Core is the only package the SDK ships in library-packs, so it is the only diverging hash.
- **Manual/admin follow-ups (owner).** `FS.GG.Game` added to the `fs-gg-cross-repo-dispatch` + `renovate` App installations (done 2026-07-06). `lockfile-sync.yml` additionally needs the org secrets `FSGG_DISPATCH_APP_ID` / `FSGG_DISPATCH_APP_PRIVATE_KEY` visible to the repo (verified naturally on the first `renovate/*` PR). **nuget.org** dual-publish (Trusted Publishing / OIDC policy on the `Paradigma11` account) is a **P5** step — nothing to do until the packages publish.
