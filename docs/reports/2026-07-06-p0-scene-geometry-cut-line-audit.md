# P0 — `Scene.Geometry` cut-line usage audit (gates ADR-0022)

- **Date:** 2026-07-06
- **Phase:** P0 of the FS.GG.Game extraction epic (see `2026-07-06-extract-fs-gg-game-component-sdd-driven.md`, §4 / §12).
- **Purpose:** Settle Edge #1 — the primitive cut line and whether a `FS.GG.Math` leaf is born — *before* ADR-0022 is authored. This is the hard gate on the ADR.
- **Method:** Static audit of the live `FS-GG/FS.GG.Rendering` working tree (`/home/developer/projects/FS.GG.Rendering`).
- **Outcome:** Cut line fixed. `FS.GG.Math` leaf = **NO**. Scene takes a forced major (Option D, owner-confirmed 2026-07-06).

---

## 1. Headline: the plan's premise was inaccurate

The plan describes the ambiguous asset as **`FS.GG.UI.Scene.Geometry (Vec2/AABB)`**. The live source does not match:

- `Scene.Geometry` is a **public module of six pure functions** — `intersects`, `contains`, `containsPoint`, `center`, `ofCenter`, `sweptIntersects` — operating over the shared Scene `Rect`/`Point`. Source: `src/Scene/Geometry.fsi`.
- There is **no `Vec2` type and no `AABB` type in the Scene package.** `AABB` occurs exactly once in all of `src`, as an algorithm comment inside `Geometry.fs:39` ("Swept AABB via Minkowski expansion").
- `Vec2` exists only as a **template-fragment / generated-product type** — `template/fragments/vec2/src/Product/Vec2.fs`, `template/base/src/Product/{Model,View,LayoutEvidence}.fs`, and tests. It is scaffold-generated game product code, not part of the shipped Scene package.

**Consequence:** the A/B/C options in the plan (§4.2) were built on a shared `Vec2`/`AABB` type that does not exist. The decision has to be re-framed against what is actually there.

---

## 2. What is actually there

### 2.1 `Rect`/`Point` are irreducibly render-core

`Rect`/`Point` are defined in `src/Scene/Types.fsi` and consumed across **42 `src` files in 8 subsystems**:

| Subsystem | Files using `Rect`/`Point` |
|---|---|
| Controls | 17 |
| Scene | 10 |
| Canvas | 5 |
| Testing | 4 |
| SkiaViewer | 3 |
| Layout | 1 |
| Symbology | 1 |
| Controls.Elmish | 1 |

These are the shared scene vocabulary the whole framework builds on. **They cannot move** — relocating them (or a math leaf beneath them) forces a Scene major that ripples through every Control. This is what kills any option that pulls the primitive types out from under Scene.

### 2.2 The `Geometry` functions are de-facto game-only

Repo-wide consumers of `Geometry.<fn>`, excluding the definition and specs:

- **`src`:** exactly one — `src/Canvas/SpatialGrid.fs:101` (`Geometry.containsPoint`). SpatialGrid is itself one of the four Canvas primitives scheduled to move to `Game.Core`.
- **template:** `template/fragments/collision/src/Product/Collision.fs` (`intersects`, `center`) — the game collision fragment.
- **tests:** `tests/Scene.Tests/GeometryTests.fs`, `tests/Canvas.Tests/{SpatialGridTests,CollisionHelperTests}.fs` — game-flavored (bullet-vs-wall swept collision, spatial-grid queries).

No Controls, Layout, Symbology, or SkiaViewer file calls `Geometry.*`. Controls hand-rolls its **own** internal geometry (`src/Controls/Internal/ChartGeometry.fs`, `WidgetGeometry.fs`) and does not route through `Scene.Geometry`. Once SpatialGrid moves with the other primitives, `Scene.Geometry` has **zero remaining render consumers**.

---

## 3. Decision

### 3.1 Cut line — **Option D** (owner-confirmed 2026-07-06)

> `Rect`/`Point` **stay in Scene** (render-core). The `Geometry` collision module **moves to `Game.Core`**, reimplemented over `Game.Core`'s own BCL-only sim primitives (the `Vec2`/`AABB`/integer-logic types the game-logic design corpus already specifies). `Game.Core` reaches up to **nothing** — BCL-only bottom preserved, no layering inversion. Scene **loses its public `Geometry` module → Scene takes an ApiCompat major.**

Rationale: it removes the last game-flavored surface (`sweptIntersects` — projectile tunneling) from the render public API, satisfying §4.1's "no game names in the `FS.GG.UI.*` namespace." The cheaper alternative — keeping `Geometry` in Scene as generic rect helpers — was rejected because it leaves that game smell in the render contract, which is the exact ownership problem the extraction exists to remove.

### 3.2 `FS.GG.Math` leaf — **NO**

Open decision #2 resolves to **no leaf**. Option C's shared BCL geometry/math leaf was predicated on a `Vec2`/`AABB` type shared between Scene and Game.Core. No such shared type exists: `Rect`/`Point` stay under Scene, and `Game.Core` brings its own primitives. Nothing needs to sit below both, so no fourth package is born.

### 3.3 Blast radius — **two majors, no new package**

| Package | Change | Why |
|---|---|---|
| `FS.GG.UI.Canvas` | **major** | removes `Pathfinding`, `SpatialGrid`, `Rng`, `FixedStep` (the four sim primitives) |
| `FS.GG.UI.Scene` | **major** | removes the public `Geometry` collision module |

The plan's headline risk ("does this touch one package or two?") resolves to **two** — but the Scene major comes from removing a game-only module, *not* from birthing a math leaf as §4.2 Option C assumed.

---

## 4. Follow-on notes for P2 (implementation)

- `Game.Core` reimplements the six `Geometry` functions over its own primitives; it does **not** reference `FS.GG.UI.Scene`. Port `tests/Scene.Tests/GeometryTests.fs` alongside.
- The Scene surface baseline must drop `module Geometry` (currently at `template/base/docs/api-surface/Scene/Scene.fsi:579`). Test `Feature240GameCoreSkillTests` asserts the packed Scene surface *carries* `module Geometry` — that assertion (and `Feature250CollisionSafeVec2Tests`) must be re-homed to the `Game.Core` surface, not left red against Scene.
- The `template/fragments/collision` and `template/fragments/vec2` fragments feed the frozen `--profile game` starter (§6); they stay put during the freeze and are retired by the sequel provider epic.

---

## 5. Effect on the plan's open decisions (§10)

- **#1 (`Scene.Geometry` home):** closed → **Option D** (module moves to `Game.Core`; `Rect`/`Point` stay in Scene).
- **#2 (`FS.GG.Math` leaf):** closed → **NO**.

P0 exit condition ("Cut line fixed; `FS.GG.Math` leaf decided yes/no") is **met.** Next: author ADR-0022 from §11 with decision-2 filled in as Option D, and open the Coordination epic (P1).
