# StarCraft II unit symbology library — architecture & design

- **Date:** 2026-07-05
- **Owner:** `.github` (cross-repo coordination); **implementation home:** a new
  showcase product (`FS.GG.Symbology.StarCraft2`) generated from the `game`/`sample-pack`
  profile, built **on top of** the shipped `fs-gg-symbology` capability — it adds **data +
  a mapping + gates**, not a new library primitive.
- **Status:** Design proposal (pre-ADR). Seeds a Rendering showcase item + a `.github`
  coordination row; does **not** change a contract. The pure `Symbology`/`Symbology.Render`
  surface is consumed **as-is**. The showcase is scaffolded on the **`governed` profile** and
  takes a **hard, blocking dependency** on `FS.GG.Governance` for its gates.
- **Scope:** A curated, **complete** `Sc2UnitStats -> Token` symbology for the three
  StarCraft II: *Legacy of the Void* rosters (Liquipedia unit statistics), plus an
  **agent-driven render→eyeball→tweak harness** and a set of **governance gates** (coverage,
  channel-completeness, legibility, fidelity, determinism) that make "every unit is
  represented, and the required information is present" a *checked* property rather than a
  hope.
- **Language/target:** F# on `net10.0`, functional-first, headless-renderable, byte-deterministic.
- **Related:** [`fs-gg-symbology` SKILL](https://github.com/FS-GG/FS.GG.Rendering/blob/main/template/product-skills/fs-gg-symbology/SKILL.md) ·
  skill registry rows `fs-gg-symbology` / `fs-gg-scene` / `fs-gg-skiaviewer`
  ([`registry/skills.yml`](../../registry/skills.yml)) ·
  [`architecture.md`](../architecture.md) §4.1 (`FS.GG.UI.Symbology` + `Symbology.Render`) ·
  the [game-audio library design](https://github.com/FS-GG/FS.GG.Game/blob/main/docs/reports/2026-07-05-game-audio-library-architecture.md) and
  [game-logic overview](https://github.com/FS-GG/FS.GG.Game/blob/main/docs/reports/2026-07-05-game-logic-skills-design-overview.md) (relocated to FS.GG.Game, ADR-0022; sibling
  "library + skill + gates" proposals) · source data:
  [Liquipedia — Unit Statistics (LotV)](https://liquipedia.net/starcraft2/Unit_Statistics_(Legacy_of_the_Void)).

---

## 1. TL;DR — the recommendation

> Do **not** build a new rendering library. `fs-gg-symbology` already ships the whole
> engine: a pure `'stats -> Token` **ChannelMap**, three interchangeable **grammars**
> (Token / Badge / Ring), a headless fail-loud `Render.toPng`, and a pure **legibility
> linter** (`Legibility.score`). What is missing is the *content and the discipline*:
> a **complete, curated SC2 roster as data**, a **hand-tuned `mapSc2Unit` ChannelMap**
> that turns Liquipedia's rich stat vocabulary into a legible-yet-informative glyph, and a
> **gate set** that fails CI when a unit is unrepresented, a required stat is not encoded,
> the board lints non-`Clean`, or the numbers drifted from source.
>
> Ship it as a small showcase product, **`FS.GG.Symbology.StarCraft2`**, scaffolded on the
> **`governed` profile**, with three parts: **(a)** `roster/*.yml` — the three-race unit
> corpus, content-addressed to Liquipedia; **(b)** `Sc2Symbology.fs` — the editable
> `Sc2UnitStats -> Token` mapping (the *only* thing the design loop tweaks); **(c)** a
> **governance gate-set** under `.fsgg/` (`governance.yml`/`policy.yml`/`capabilities.yml`/
> `tooling.yml`) authored to `FS.GG.Governance`'s schemas and enforced as a **hard,
> always-blocking dependency** — every check is `Severity = Blocking` under profile
> **`Release`** (the tightest), evaluated at a **blocking boundary everywhere**
> (`--mode verify` locally, `--mode gate` at merge), so a red gate returns **exit 2
> (`GovernedBlocking`)** with no advisory rung to slip past. An **agent** drives the loop:
> read roster → author/adjust `mapSc2Unit` → render the per-race boards → *read the PNGs
> back* and run the linter → run the governance route → tweak the mapping → repeat until the
> gate exits 0.

**Why this shape:** it preserves the FS-GG invariant that the mapping is *data you edit*
and the grammar/engine is *fixed library* — the exact discipline `fs-gg-symbology` is built
around ("the grammar is fixed; the mapping is yours to edit"). It reuses the legibility
linter as the mechanical backstop instead of inventing a second aesthetics oracle, and it
keeps determinism and headless rendering intact. It authors the five gates as a **real
`FS.GG.Governance` gate-set** and wires them **blocking** at the merge boundary — the
product *requires* governance to ship, exercising the kernel's `Deterministic`/`Blocking`
axes and the `governed` profile end-to-end.

> **Boundary note (deliberate).** The org's framework rule is that *no framework component
> depends on Governance* and governance is *advisory by default* ([`architecture.md`](../architecture.md)
> §66, §103–110) — that rule protects the four **framework** repos (Rendering/SDD/Templates)
> from being blocked by an experimental platform. This showcase is **not** a framework repo;
> it is exactly the kind of downstream **product** Governance is meant to "earn adoption
> from." Making the dependency **hard and blocking** is a supported *product-level* choice —
> the `governed` profile + the `fs-gg-governance` overlay exist precisely to hard-wire it —
> and it changes nothing about the framework boundary.

---

## 2. What exists today, and the gap this fills

The `fs-gg-symbology` capability is **already the right library** — and it is already aimed
squarely at this exact problem. From the shipped skill:

- **`'stats -> Token` is the contract.** A `Token` carries pre-attentive channels —
  `Faction` (`Ally|Enemy|Neutral|Custom`), `Klass` (`Mobile|Heavy|Scout`), `Sigil`
  (a vector shape family), `Threat : float`, `Health : float`, `Speed : int`,
  `Shield : bool`, `Heading : float` — plus an opt-in `Label`/`AutoLabel` inspection channel.
  You build from `Symbology.defaultToken` and override only the fields your game encodes.
- **Three grammars, one mapping.** `Grammar = Token | Badge | Ring` re-*draws* the same
  ChannelMap: a heading-rotated silhouette, a screen-aligned framed emblem, or a radial
  gauge. Switching grammar never touches the mapping, so the linter's verdict is
  grammar-independent.
- **Headless, fail-loud render.** `Render.toPng` rasterises a `gallery`/`filmstrip` board and
  **refuses to emit on a non-passing verdict**, so a critique never reasons over a blank image.
- **A pure legibility linter.** `Legibility.score roster` returns a `Verdict` +
  `Findings` naming the overloaded/out-of-domain `Channel`, capacity-vs-used, and the
  offending unit indices — the deterministic backstop for "is this still readable?".
- **A stated loop.** *render → read the PNG back → critique at target size → tweak the
  mapping ONLY → repeat*, with timestamped board PNG + mapping snapshot recorded under
  `readiness/`.

So the engine, the determinism story, the critique loop, and the mechanical linter **already
exist and are correct**. What is missing — and what this document designs — is three things:

1. **Content.** A *complete, faithful* StarCraft II roster (three races, ~20 units each) as
   structured data, tied to the Liquipedia source, that the mapping consumes.
2. **A tuned, opinionated `mapSc2Unit`** that resolves the genuine tension between SC2's
   large stat vocabulary (cost, supply, HP, shields, armor, six+ attributes, ground/air DPS,
   range, speed, sight, cargo) and the Token's deliberately *small* pre-attentive channel
   budget — deciding what pops out, what is inspection-detail, and what is dropped.
3. **Governance gates** that make "*all units are presented*" and "*this and that info must
   be present*" a **checked, CI-enforced** property — the piece the user explicitly asked for.

This is the same pattern as the sibling designs: **the primitive exists (symbology / audio
edge / grid); the deliverable is the curated content + the discipline + the gates around it.**

---

## 3. Design principles (inherited from FS-GG + fs-gg-symbology)

| # | Principle | What it means here |
|---|---|---|
| P1 | **The mapping is data; the engine is fixed** | Only `mapSc2Unit` (a pure `Sc2UnitStats -> Token`) is authored/tuned. We never fork `Symbology`/`Symbology.Render`. The unit of change in every loop iteration is the ChannelMap, never the grammar. |
| P2 | **Roster is the source of truth** | The full unit corpus lives as data (`roster/{terran,protoss,zerg}.yml`), content-addressed to Liquipedia. Coverage/fidelity gates read *this*, not the code. |
| P3 | **Legible beats complete** | SC2 has far more stats than the Token has pre-attentive channels. We *deliberately* route only the battle-relevant few to pop-out channels, push identity/economy detail to the `Label`/`AutoLabel` inspection channel, and **drop** the rest to tooltips. The linter enforces we don't overload. |
| P4 | **Determinism under test** | Same roster + same mapping ⇒ byte-identical boards, across runs and OS/arch. Boards render headless; no device, no wall-clock, no hash-order nondeterminism. |
| P5 | **Governance is a hard, always-blocking dependency** | The gates are a real `FS.GG.Governance` gate-set (`.fsgg/` schemas), every check `Blocking` under profile `Release`, evaluated at a blocking boundary both locally (`--mode verify`) and at merge (`--mode gate`). The product *cannot ship or even iterate green* past a red gate (exit 2) — there is no advisory rung. A product-level opt-in, legitimate for a downstream showcase and distinct from the framework repos' no-Governance-dependency rule. |
| P6 | **Aesthetics is a first-class, checked axis** | "Aesthetically pleasant" is not left to vibes: race-coherent palettes + sigil families are specified, the linter is the mechanical floor, and the agent's *eye* pass (reading the PNG) is the ceiling — both gate the loop. |

---

## 4. Intake — the StarCraft II stat schema (the source data)

The Liquipedia *Unit Statistics (Legacy of the Void)* tables cover **Terran, Protoss, and
Zerg** (~20 units each). Per-unit fields, grouped:

| Group | Fields | Notes |
|---|---|---|
| **Economy** | `Supply`, `Minerals`, `Gas`, `BuildTime` | Supply is fractional (Zergling 0.5). |
| **Physical** | `Hp`, `Shields` (Protoss only), `Armor`, `Size` (collision Ø), `Cargo`, `Sight`, `Speed` | Shields are Protoss-only ⇒ a race-conditional field. |
| **Attributes** | flags: `Light`, `Armored`, `Biological`, `Mechanical`, `Massive`, `Psionic` (multi-valued) | Drives bonus-damage matchups; a unit can hold several. |
| **Combat** | `GroundDamage`, `AirDamage`, `BonusDamage(vs type)`, `GroundDps`, `AirDps`, `BonusDps`, `AttackCooldown`, `Range` | Melee = range ~0; some units are ground-only, air-only, or both; casters may be attackless. |

**Anchor examples (verbatim from source):**

| Unit | Race | Supply | Min/Gas | HP (+Shd) | Armor | Speed | Dmg | DPS | Range | Attributes |
|---|---|---|---|---|---|---|---|---|---|---|
| **Marine** | Terran | 1 | 50/0 | 45 | 0 | 3.15 | 6 | 9.8 | 5 | Light, Biological |
| **Adept** | Protoss | 2 | 100/25 | 70 (+70 Shd) | 1 | 3.5 | 10 | 6.2 | 4 | Light, Biological |
| **Zergling** | Zerg | 0.5 | 25/0 | 35 | 0 | 4.13 | 5 | 10 | melee | Light, Biological |

This schema is the `Sc2UnitStats` record. The roster files are the **complete** enumeration
of it; the mapping is a total function over them (P2 → the coverage gate).

---

## 5. The core design — channel assignment (`Sc2UnitStats -> Token`)

This is the heart of the work: the Token has a **small, deliberately-capped** pre-attentive
budget, and SC2 has a **large** stat vocabulary. The design decision is *what earns a
pop-out channel*, *what is inspection-detail*, and *what is dropped*. Three tiers:

### 5.1 Tier A — pre-attentive channels (read at a glance, across a board)

| SC2 stat(s) | → Token channel | Encoding rationale |
|---|---|---|
| **Race** (T/P/Z) | `Faction` (`Custom` hue per race) + palette | The single most important "which side / what am I looking at" read; saturated hue is the strongest pop-out. Terran steel-blue, Protoss gold-teal, Zerg violet-carapace. |
| **Role archetype** (worker / core-army / siege / caster / air / detector) | `Klass` (`Mobile|Heavy|Scout`) **+** `Sigil` (shape family) | Class drives frame weight; the sigil shape says *what kind of thing* (a fang for melee, a bolt for ranged, a ring for area/siege, etc.). Derived, not a raw stat. |
| **Combat power** (max of ground/air DPS, normalised) | `Threat : float [0,1]` | The "how dangerous" gauge. `min 1.0 (maxDps / DPS_CEIL)` with a documented ceiling (~40 DPS) so a Marine and a Battlecruiser separate. |
| **Effective durability** (`Hp (+ Shields)` vs a race-scaled max) | `Health : float [0,1]` | Health arc / bar sweep. Shields fold into the numerator; the max is per-role so a Zergling isn't permanently "near-dead" next to an Ultralisk. |
| **Movement speed** | `Speed : int 0..4` (pips) | `speed / SPEED_STEP` clamped to 4 pips; separates a Zealot from a Phoenix at a glance. |
| **Has shields** (Protoss) / **is Massive** | `Shield : bool` | Reused as a "defensive/heavy" flag — Protoss units light it; on Terran/Zerg it can mark `Massive`. One boolean, documented per race. |
| **Facing** (only in animated/board views that carry it) | `Heading : float` | Token grammar rotates the silhouette; Badge/Ring show a discrete heading pip. Static in a gallery. |

### 5.2 Tier B — inspection-detail (the opt-in `Label` / `AutoLabel` channel)

The Token's label channel exists precisely for "the sigil can't disambiguate identity."
Use **`AutoLabel`** (projected from the Token's *own* encoded channels — `FactionCode`,
`KlassCode`, `HealthTier`, `ThreatTier`, `SpeedPips`, `ShieldFlag`) for a compact state
readout, and an explicit **`Label`** (rich/laid) for the economy line that has no pop-out
channel:

- **Callsign** — the unit name / a 3-letter code (`MAR`, `ADP`, `ZGL`).
- **Economy readout** — `min/gas · supply · range` (e.g. `50/0 · 1 · R5`), the numbers a
  designer scans but that must *not* compete with the battle-read encodings.
- Keep to the grammar budget (**Token ≤ 3 lines, Badge ≤ 2, Ring ≤ 2**); never colour a
  label run to impersonate the faction/state encodings (skill caveat).

### 5.3 Tier C — deliberately dropped (tooltip / data-sheet only)

`BuildTime`, `Sight`, `Cargo`, `Size`, `AttackCooldown`, per-type `BonusDamage`, the full
`Attributes` set, and the ground-vs-air *split* of DPS are **not** given a glyph channel.
Encoding them would blow the legibility budget (P3). They remain in the roster data and the
generated **data-sheet** (a side table the board links to), not on the sigil. *Which* stats
sit in Tier C is itself a reviewed design decision the doc records — and a gate (5.4) asserts
the Tier-A/B set is actually populated.

### 5.4 The mapping, concretely

```fsharp
open FS.GG.UI.Scene
open FS.GG.UI.Symbology

// INTAKE — one record per Liquipedia column (roster/*.yml deserialises to this)
type Race = Terran | Protoss | Zerg
type Sc2UnitStats =
    { Name: string; Code: string; Race: Race; Role: string
      Supply: float; Minerals: int; Gas: int
      Hp: int; Shields: int; Armor: int; Speed: float; Range: float
      GroundDps: float; AirDps: float; Attributes: string list; Massive: bool }

// tuning constants (documented, reviewed — the levers of the aesthetic)
let DPS_CEIL = 40.0
let SPEED_STEP = 1.0
let hpMaxFor role = match role with "siege" | "capital" -> 500.0 | "core" -> 200.0 | _ -> 120.0

let raceFaction = function Terran -> Custom 0 | Protoss -> Custom 1 | Zerg -> Custom 2
let roleKlass  = function "worker" -> Scout | "siege" | "capital" -> Heavy | _ -> Mobile
let roleSigil  = function
    | "melee" -> Fang | "ranged" -> Bolt | "siege" -> Ring | "caster" -> Star | _ -> Bolt

// MAP — the ONLY thing the design loop edits
let mapSc2Unit (u: Sc2UnitStats) : Token =
    let maxDps = max u.GroundDps u.AirDps
    { Symbology.defaultToken with
        R        = 28.0
        Faction  = raceFaction u.Race
        Klass    = roleKlass u.Role
        Sigil    = roleSigil u.Role
        Threat   = min 1.0 (maxDps / DPS_CEIL)
        Health   = float (u.Hp + u.Shields) / hpMaxFor u.Role
        Speed    = int (min 4.0 (u.Speed / SPEED_STEP))
        Shield   = u.Shields > 0 || u.Massive
        AutoLabel = Some(Symbology.autoLabel [ FactionCode; ThreatTier; SpeedPips ])
        Label    = Some(Symbology.plainLabel (sprintf "%s\n%d/%d · %g" u.Code u.Minerals u.Gas u.Supply)) }
```

The `render → read PNG → tweak these constants and cases → repeat` loop is exactly the
`fs-gg-symbology` loop, now driven against a real, complete roster and clamped by gates.

---

## 6. Aesthetics — race identity systems ("informative *and* pleasant")

"Aesthetically pleasant" is specified, not left to taste:

- **Race palettes (faction hue + accent).** Terran = **steel-blue / amber warning**;
  Protoss = **gold / teal-plasma**; Zerg = **violet-carapace / bio-orange**. Each is a
  saturated primary (faction pop-out) + one accent for state, chosen for **WCAG-legible
  separation** at the on-board size and for the colour-blind-safe ordering the design system
  already vends. These are the *only* saturated hues; state/inspection uses value, not new hues.
- **Sigil families per role, coherent within a race.** Melee=fang, ranged=bolt, siege=ring,
  caster=star, air gets an elevated/winged variant. A race reads as a *family* because the
  frame weight (`Klass`) and palette are shared; the sigil says the role.
- **Grammar per view (one mapping, three drawings):**
  - **Badge** — the default for a **full-roster insignia wall** (upright, framed, dense);
    the "here is every Terran unit" board.
  - **Ring** — the **stat-forward comparison** view (radial gauges make Threat/Health/Speed
    read continuously); the "compare these three units" board.
  - **Token** — the **in-motion / heading** view for a hypothetical live battlefield.
- **Boards produced:** `gallery` per race (roster wall), a cross-race `filmstrip`
  (archetype-by-archetype comparison), and a `Ring` comparison of the "power" units. Every
  board is a timestamped artifact under `readiness/symbology/`.

---

## 7. Library shape

```text
FS.GG.Symbology.StarCraft2/                (showcase product; profile: governed)
├─ roster/                                 (P2 — the corpus, source of truth)
│   ├─ terran.yml   protoss.yml   zerg.yml (one entry per unit; every Liquipedia column)
│   └─ SOURCES.yml                         (per-unit { source-url, retrieved, sha256 } → fidelity gate)
├─ src/
│   ├─ Sc2Roster.fs        (yml → Sc2UnitStats; the intake schema §4)
│   ├─ Sc2Symbology.fs     (mapSc2Unit — the ONLY design-loop surface, §5)
│   ├─ Sc2Palette.fs       (race palettes + sigil families, §6)
│   └─ Sc2Boards.fs        (per-race galleries, cross-race filmstrip, Ring comparison)
├─ gates/                  (the check executables G1–G5 invoke; exit-code per check)
├─ .fsgg/                  (§8 — the FS.GG.Governance gate-set: governance/policy/
│                           capabilities/tooling.yml; applied by the fs-gg-governance overlay)
├─ readiness/symbology/    (timestamped board PNG + mapping snapshot per iteration)
└─ docs/data-sheet.md      (generated Tier-C stat tables the boards link to)
```

- **Depends *down* only** for rendering: `FS.GG.UI.Scene`, `FS.GG.UI.Symbology`,
  `FS.GG.UI.Symbology.Render`, `FS.GG.UI.SkiaViewer` (render host). Nothing depends *up* into it.
- **Depends *hard* on Governance** for shipping: `FS.GG.Governance.Cli` (`fsgg-governance`) +
  the `FS.GG.Governance.ReferenceGateSet` baseline. The merge boundary *requires* a green
  `route --mode gate` (P5). This is the one deliberate upward dependency, legitimate because
  this is a downstream product, not a framework repo (§8 boundary note).
- **Consumes the product skills** `fs-gg-symbology` (+ `fs-gg-scene`, `fs-gg-skiaviewer`, and
  `fs-gg-testing` — the last materialises under the `governed` profile) — a *consumer* of
  those registry rows, introducing **no new contract**.

---

## 8. Governance gates — "every unit represented, required info present"

The user's explicit ask, wired as a **hard dependency**. The five checks are authored as a
real `FS.GG.Governance` **gate-set** in the product's `.fsgg/`
(`governance.yml`/`policy.yml`/`capabilities.yml`/`tooling.yml`), modelled directly on the
`fs-gg-governance` overlay's populated set. Each check is placed on the kernel's two
orthogonal axes:

- **`CheckTier`** — `Deterministic` (a reified, byte-checkable predicate) vs `AgentReviewed`
  (the eye critique) vs `HumanOnly`. `Deterministic` is *structurally* required for the
  mechanical gates (the kernel refuses `Deterministic` for opaque checks).
- **`Severity`** — **all `Blocking`, everywhere**. On the enforcement mode ladder
  (`Sandbox < Inner < Focused < Verify < Gate < Release`) the loop deliberately evaluates at
  the **lowest blocking rung, `Verify`, even locally** (the structurally-advisory
  `Sandbox`/`Inner` rungs are *not* used), so the agent's local verdict is the *same hard
  verdict* the merge boundary produces at `Gate`/`Release`. There is no advisory rung to
  iterate past: a red check returns **exit 2 (`GovernedBlocking`)** at every stage. Profile
  **`Release`** — the tightest maturity floor, so no gate can ever be relaxed to advisory.
  The merge boundary still recomputes from scratch and ignores any local escape hatch.

| # | Gate | `CheckTier` | Asserts | Fails when | Reuses |
|---|---|---|---|---|---|
| **G1 — Coverage** | Deterministic | Every unit in `roster/*.yml` produces a Token; every race board contains exactly its roster. | `mapSc2Unit` is total over the roster; `board.units.Length == roster.Length` per race. | — |
| **G2 — Channel-completeness** | Deterministic | Each unit encodes the **required** pop-out channels: `Faction`, `Klass`, `Threat`, `Health`, `Speed` **non-default**; `Label`/`AutoLabel` present. | A Token leaves a *required* channel at `defaultToken` (e.g. Threat 0 for a combat unit, no label). | `defaultToken` diff + per-role required-set policy. |
| **G3 — Legibility** | Deterministic | Each race board (and the cross-race filmstrip) lints `Clean`. | `Legibility.score (roster |> List.map mapSc2Unit)` returns `Warning`/`Error`. | **The existing linter**, surfaced as a governance check via `tooling.yml`. |
| **G4 — Fidelity** | Deterministic | Encoded stats trace back to source; no hand-edited numbers. | A roster row's `sha256` ≠ its `SOURCES.yml` entry, or a value is out of domain (negative HP, DPS > ceiling×N). | The `sha256`/`SOURCES.yml` manifest pattern from the audio `LICENSES.yml` gate. |
| **G5 — Determinism** | Deterministic | Re-rendering an unchanged mapping is byte-identical. | Two runs of the board diverge (hash-order / wall-clock / float nondeterminism leaked in). | `Render.toPng` + golden PNG hash in a cross-platform matrix. |
| **G6 — Aesthetic sign-off** *(opt.)* | AgentReviewed | The eye critique (§9) records a pass on crowding/contrast/race-separation. | The agent's read-the-PNG-back verdict is negative and no override is recorded. | The kernel's `AgentReviewed` tier — a first-class, provenance-tracked non-deterministic check. |

**How it wires (`tooling.yml` → `capabilities.yml` → `policy.yml` → `governance.yml`).**
Each gate is a `tooling.yml` command (e.g. `dotnet run --project gates -- coverage`) whose
exit code the kernel consumes; `capabilities.yml` declares the check reified + its tier;
`policy.yml` sets severity `Blocking` and the maturity floor; `governance.yml` binds the set
to profile `Release`. The `.fsgg/` tree is applied by the `fs-gg-governance` overlay
*after* scaffold (so it is not flagged writing into an SDD-owned tree), matching
[`architecture.md`](../architecture.md) §4.3–4.4. The `FS.GG.Governance.ReferenceGateSet`
content package (version-derived from the four `schemaVersion`s) is the versioned baseline
this set diffs against.

**Why gates and not just review:** the request — *"all units must be presented, this and
that info must be present"* — is a **completeness invariant over a growing corpus**. Review
catches it once; a *blocking* gate catches it every time a unit is added or the mapping is
retuned, and — because it is `Deterministic + Blocking` at the merge boundary — it *cannot be
merged around*. G1+G2 encode *presence*, G3 *legibility*, G4 *truth*, G5 *reproducibility*,
G6 *taste* — together the machine-checkable definition of "the roster is faithfully and
legibly represented." Each check emits a **named finding with full provenance** (which unit,
which channel, which value), so a red gate is an actionable TWEAK trigger, never a mystery.

The gates compose with the render's own fail-loud contract: `Render.toPng` already refuses to
emit on a non-passing legibility verdict, so **G3 is enforced twice** — at render time and in
the governance route — and a critique never reasons over a blank image.

---

## 9. The agent-driven design loop

The library exists to let an **agent** rapidly design and iterate. The loop is the
`fs-gg-symbology` render→eyeball→tweak loop, made autonomous and **gate-terminated**:

```text
  ┌─────────────────────────────────────────────────────────────────────────┐
  │  1. READ roster/*.yml           (the complete SC2 corpus, P2)             │
  │  2. AUTHOR / EDIT  mapSc2Unit    (Tier-A/B channel assignment, §5)        │ ◄─┐
  │  3. RENDER boards  Render.toPng  (per-race gallery + filmstrip)           │   │
  │  4. CRITIQUE — two checks:                                                │   │
  │       (a) EYE  — read the PNGs back (multimodal): crowding,               │   │ tweak
  │                  contrast, race separation, label collisions              │   │ the
  │       (b) LINT — Legibility.score → Verdict/Findings (mechanical)         │   │ MAPPING
  │  5. ROUTE fsgg-governance route --mode verify  (G1–G6, profile Release,   │   │ ONLY
  │           blocking — same hard verdict the merge boundary's gate returns) │   │
  │  6. if exit 2 (any Blocking gate red) OR eye-critique finds a problem ────┼───┘
  │     else (exit 0) → SNAPSHOT board PNG + mapping under readiness/ → DONE   │
  └─────────────────────────────────────────────────────────────────────────┘
```

- **What the agent tweaks is only the mapping** (`mapSc2Unit` + the tuning constants/palette),
  never the grammar or the library — the invariant that keeps the loop safe and the linter's
  verdict meaningful.
- **The gate blocks everywhere — no advisory rung.** The agent runs the route locally at
  `--mode verify` (a *blocking* boundary) under profile `Release`, so the loop sees the exact
  hard verdict the merge boundary produces at `--mode gate`; it never iterates against a
  softer local check. "Repeat until green" means *repeat until exit 0* — the same bar that
  gates the ship. One gate-set, one verdict, recomputed from scratch at the boundary.
- **The eye pass is real work the agent is uniquely good at:** `Render.toPng` returns a PNG
  path; the agent *reads the image back* and critiques crowding/contrast/collisions the linter
  structurally cannot see. The linter is the floor; the eye is the ceiling.
- **Skills that power it:** `fs-gg-symbology` (drives the loop + owns the linter),
  `fs-gg-scene` (primitives), `fs-gg-skiaviewer` (headless render host). No new skill is
  *required*; an optional thin `fs-gg-sc2-roster` product-skill could encode the Tier-A/B/C
  policy and the gate list so a fresh agent picks up the conventions without re-deriving them.
- **Evidence:** every accepted iteration writes a timestamped board PNG + mapping snapshot
  under `readiness/symbology/` (the skill's Evidence rule) — a visual changelog of the design.

---

## 10. Effort analysis — what's achievable at reasonable cost

Phased; each phase independently shippable. Sizes rough (S ≈ days, M ≈ 1–2 wks).

| Phase | Deliverable | Effort | Notes / risk |
|---|---|---|---|
| **P0 — Corpus + intake** | `roster/*.yml` for one race (Terran) + `Sc2Roster.fs` + `SOURCES.yml`. | **S** | Data entry from Liquipedia; the schema §4 is fixed. Low risk, immediately useful. |
| **P1 — First mapping + boards** | `mapSc2Unit` (§5), `Sc2Palette`, per-race gallery + Ring comparison, first `readiness/` snapshot. | **S–M** | The core design tension (Tier A/B/C) is resolved here via the loop. Reuses the whole engine. |
| **P2 — Governance gate-set** | `.fsgg/` gate-set (`governance/policy/capabilities/tooling.yml`) + the `gates/` check executables; applied via the `fs-gg-governance` overlay; `route --mode gate` blocking in CI; golden board hashes in a cross-platform matrix. | **M** | Hard dependency on `fsgg-governance` + `ReferenceGateSet`. G3 is `Legibility.score` reused; G4 is the sha256 manifest; G1/G2 small pure checks. Risk is gate-set schema authoring, not the checks. The explicit ask. |
| **P3 — All three races + filmstrip** | Complete Protoss + Zerg rosters; cross-race archetype filmstrip; generated data-sheet for Tier-C. | **M** | Data entry + a few race-conditional mapping cases (shields, massive). Coverage gate turns green when complete. |
| **P4 — Agent loop + optional skill** | Documented autonomous loop (§9); optional `fs-gg-sc2-roster` product-skill encoding the policy + gate list. | **S–M** | Mostly prose + a thin skill; proves the "rapidly design and iterate" claim end-to-end. |

**Bottom line:** a *complete, gated, aesthetically-coherent* three-race SC2 symbology —
every unit represented, required info present, byte-deterministic, lint-`Clean` — is
**P0–P2 for one race ≈ 1–2 weeks** and **all three races + agent loop ≈ 4–6 weeks**,
because the rendering engine, the linter, the grammars, and the loop already exist. This
project is **content + a mapping + five gates**, not a library build.

---

## 11. Risks & open questions

| # | Risk / question | Mitigation / proposal |
|---|---|---|
| R1 | **Stat vocabulary > channel budget** — SC2 has more stats than the Token has pop-out channels. | The explicit Tier A/B/C split (§5) + G2/G3 gates. Legibility beats completeness (P3); Tier-C lives in the data-sheet, not the sigil. |
| R2 | **"Aesthetically pleasant" is subjective.** | Specify it (palettes + sigil families §6), floor it with the linter (G3), and ceiling it with the agent's eye pass. Not left to vibes. |
| R3 | **Data drift / errors** vs the live Liquipedia numbers. | G4 fidelity gate: per-unit `sha256` in `SOURCES.yml`; a changed number fails CI until re-verified against source. Balance patches are an explicit, dated roster bump. |
| R4 | **Race-conditional fields** (shields Protoss-only; melee = ~0 range; attackless casters). | Encode as documented cases in `mapSc2Unit` (shields → `Shield`/Health numerator; caster → Threat 0 is *allowed* for that role in G2's per-role required-set). |
| R5 | **Faction channel is `Ally/Enemy/Neutral`, not 3 races.** | Use `Custom n` per race with the race palette; the linter treats `Custom` hues as distinct factions — verify separation holds in the eye pass and G3. |
| R6 | **Scope creep toward a full SC2 data app.** | Hard boundary: this is a *symbology showcase* (glyphs + gates + loop), not a balance tool. Tier-C stays in a static data-sheet; no simulation, no live game data. |
| R7 | **IP / attribution** for SC2 names + Liquipedia data. | Names/stats are factual; cite Liquipedia (CC-BY-SA) in `SOURCES.yml`; ship no Blizzard art — every glyph is our own abstract vector. Keep it a research/showcase artifact. |
| R8 | **Hard Governance dependency couples the showcase to Governance's release cadence** — a `schemaVersion` bump or `ReferenceGateSet` version change can break the gate-set or block the build. | Accepted, eyes-open: it is the point of the exercise (prove the kernel end-to-end). Pin the `fsgg-governance` + `ReferenceGateSet` versions via CPM; track the edge in `registry/dependencies.yml`; the Coordination row sequences overlay-version bumps. If Governance churn becomes painful, the fallback is the *advisory* posture the other showcases use — a config change (severity `Advisory`), not a rewrite. |

---

## 12. Cross-repo placement & next steps

- **Implementation home:** a new **showcase product** `FS.GG.Symbology.StarCraft2`,
  scaffolded on the **`governed` profile** so it materialises `fs-gg-symbology` + `fs-gg-scene`
  + `fs-gg-skiaviewer` + `fs-gg-testing` **and** applies the `fs-gg-governance` overlay. It
  depends *down* onto the published `FS.GG.UI.Symbology(.Render)` packages and *up* onto
  `FS.GG.Governance` (the deliberate hard dependency), and **introduces no new contract** —
  the pure symbology surface is consumed verbatim.
- **No contract touchpoint.** Unlike the audio design (which proposed *additive*
  `AudioEffect` variants), this project changes **nothing** in the `fs-gg-symbology`
  capability surface. It is a pure consumer. If the loop reveals a genuinely missing Token
  channel, *that* would be a Rendering surface-bump + `.github` registry reconcile — but the
  design goal is explicitly to fit within the existing channel budget (P3).
- **Governance stance.** The gates are a **hard, blocking `FS.GG.Governance` gate-set** (P5,
  §8): the product *requires* a green `fsgg-governance route --mode gate` to merge/ship. This
  is a *product-level* opt-in and does **not** contradict `architecture.md`'s framework rule
  (§66, §103–110) — that rule keeps the four **framework** repos free of a Governance
  dependency; a downstream showcase adopting it hard is exactly the adoption Governance is
  meant to earn. The gate-set is authored to Governance's schemas and diffs against the
  versioned `FS.GG.Governance.ReferenceGateSet`.
- **Decision record:** promote **two** decisions to ADRs — (a) the Tier-A/B/C
  channel-assignment policy (Rendering-local), and (b) **the hard Governance dependency for a
  showcase product** (a cross-repo `.github` ADR, since it is a deliberate, reusable exception
  to the "products may stay governance-free" default and should be citable by future
  showcases).
- **Coordination:** file a Rendering showcase item ("SC2 unit symbology + governed gate-set")
  with the P0–P4 children above, and a `.github` **Coordination** row so the Governance
  dependency (overlay version + `ReferenceGateSet` pin coherence) is sequenced and kept
  coherent. Keep `registry/skills.yml` unchanged (pure consumer) and
  `registry/dependencies.yml` coherent — the product now carries a **real edge onto
  `fsgg-governance` + the reference gate set**, which the pin-coherence check must track.
- **Immediate next step:** P0 — enter the Terran roster from Liquipedia into `roster/terran.yml`
  with `SOURCES.yml` hashes, then run one turn of the §9 loop to produce the first gated
  Terran Badge wall under `readiness/symbology/`.

---

## 13. Sources

Source data — [Liquipedia: Unit Statistics (Legacy of the Void)](https://liquipedia.net/starcraft2/Unit_Statistics_(Legacy_of_the_Void)) (CC-BY-SA).
Capability — [`fs-gg-symbology` SKILL](https://github.com/FS-GG/FS.GG.Rendering/blob/main/template/product-skills/fs-gg-symbology/SKILL.md)
(`Token`/`ChannelMap`, grammars `Token|Badge|Ring`, `Legibility.score`, `Render.toPng`, the render→eyeball→tweak loop) ·
skill registry rows `fs-gg-symbology` / `fs-gg-scene` / `fs-gg-skiaviewer` ([`registry/skills.yml`](../../registry/skills.yml)).
Framework — [`architecture.md`](../architecture.md) §4.1 (`FS.GG.UI.Symbology` + `Symbology.Render`, headless degrade-and-disclose),
§4.3 (the Governance kernel: `CheckTier`/`Severity` axes, the `Sandbox…Release` mode ladder, `route --mode gate` exit 2, `FS.GG.Governance.ReferenceGateSet`),
§4.4 (the `fs-gg-governance` overlay applied after scaffold), §66/§103–110 (the framework-only no-Governance-dependency boundary this product deliberately opts out of) ·
[`rendering-project.md`](../rendering-project.md) (governance boundary; release identity belongs to the repo).
Sibling designs (same "library + skill + gates" shape) —
[game-audio library architecture](https://github.com/FS-GG/FS.GG.Game/blob/main/docs/reports/2026-07-05-game-audio-library-architecture.md) (the `LICENSES.yml` sha256 gate pattern reused as G4) ·
[game-logic skills overview](https://github.com/FS-GG/FS.GG.Game/blob/main/docs/reports/2026-07-05-game-logic-skills-design-overview.md) (relocated to FS.GG.Game, ADR-0022; determinism-as-tested-property, pure-function discipline).
