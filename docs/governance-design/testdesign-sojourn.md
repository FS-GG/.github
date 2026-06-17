---
title: "Test design: governance adapters for Sojourn"
category: Governance design
categoryindex: 7
index: 9
description: A worked test design applying the governance system to Sojourn — a deterministic Rust 4X game — with multiple gameplay-invariant and software-development adapters that reify the project's existing ad-hoc rules, plus a deeper pass of complex gameplay-design rules (tech-web graph folds, seed-sweep balance probes, and cross-domain composites).
---

# Test design: governance adapters for Sojourn

This is a worked example and a deliberate stress test of the
[adapter model](adapters.md). [Sojourn](https://github.com/nuklearwanze/Sojourn)
is a hard-science, no-combat 4X strategy game (settling the Solar System,
2026–2126) with a **deterministic Rust simulation core**. It is about as far from
an F# design system as a project can be — a different language, a different
domain, a different team — which makes it the ideal *second adopter* against which
to test the [adoption bar](principles.md): can the kernel govern a domain that
shares none of the design-system's vocabulary?

The answer this design argues: yes, and almost for free, because **Sojourn already
enforces its invariants — piecemeal.** The adapters do not add new rules. They
reify what Sojourn already checks ad hoc into one [reified algebra](rule-edsl.md)
that is tiered ([who decides](kernel.md)), severity-graded
([what blocks](routing-and-modes.md)), explainable, and light by default.

This is a proposal / test design. Nothing here is built, and per the dependency
direction it would be an external F# tool that *inspects* Sojourn's files (RON
data, Rust source, Cargo metadata, git diffs); Sojourn would take no dependency on
it.

## What Sojourn already enforces (the raw material)

These are observed in the repository and its `CLAUDE.md`:

- **`source` provenance** — every number in `data/**` carries a `source:` field,
  "validated in CI" (e.g. `data/vehicle/classes.ron`).
- **Analytic invariants** — `data/*/validation.ron` files encode conservation,
  ISRU break-even, learning monotonicity, `P50 < P80`, power-margin additivity,
  shielding attenuation, etc., with tolerances.
- **Determinism discipline** — the FA-01 float policy routes *all* transcendentals
  through `libm` (no platform `std` math); randomness flows through the kernel's
  seeded streams (`rand_core` trait, no `thread_rng`); saves use `postcard`;
  `blake3` pins data content.
- **Crate decoupling (FA-04 C1)** — gameplay slices depend on `sojourn-core`
  only, never on each other or the UI; "the audit greps `sojourn-core` only."
- **Replay scenarios** — `scenarios/*.ron` (incl. `smoke_decade.ron`) driven by
  `sojourn-harness`.
- **Pillars P1–P6** — declared "non-negotiable" in the README, plus the six
  conserved currencies (delta-v, mass, power, crew-time, budget, political
  capital).
- **Spec Kit** — the repo already uses `.specify` / `speckit` / `specs`
  (features FA-01…FA-11).

Each bullet becomes an adapter or a rule below.

## The adapter set

Eight single-domain adapters, each mapping to real crates/data and existing
checks. Gameplay-invariant adapters first, then software-development adapters.

| Adapter | Pillar / feature | Inspects | Tier mix |
| --- | --- | --- | --- |
| Conservation & physics | P2, validation.ron | `data/*/validation.ron`, `sojourn-astro`, `-vehicle`, `-economy` | Det + Agent + Human |
| Plausibility & provenance | P1, P6 | `data/**/*.ron` `source:`, Sojournal, `blake3` pins | Det + Agent + Human |
| Faction fairness | P5 | `data/polity`, `data/econ` per-faction; physics tables | Det + Agent |
| Research process | P3 | `data/tech`, `data/research` graphs | Det + Agent |
| Determinism | FA-01, harness | `scenarios/*.ron`, `sojourn-core`/slice source, `sojourn-harness` | Det + Agent |
| Crate architecture & surface | FA-04 C1 | `Cargo.toml` graph, slice query APIs, `postcard` DTOs, `deny.toml` | Det + Agent + Human |
| Data-dense presentation | P4, FR-UI-1505 | `sojourn-ui`, `sojourn-ui-desktop`, theme config | Det + Agent |
| Spec Kit lifecycle | existing | `.specify`, `specs/FA-*`, `tasks.md` | reused as-is |
| Playtest / balance | P3, P5, harness | seed sweeps over `scenarios/*.ron` via `sojourn-harness` | Det + Agent + Human |

All rule code below is **illustrative**: probes are thin readers over Sojourn's
existing formats, and the algebra is the [reified `Check`](rule-edsl.md)
(`Atom`/`All`/`Any`/`Not`/`Implies` + `Opaque`), applicative and CE-free.

## Gameplay-invariant adapters

### Conservation & physics (P2)

The laws the deterministic sim must never violate. Most are *already* analytic
cases in `validation.ron`; the adapter runs them as deterministic checks and adds
judgement tiers for new derivations.

```fsharp
let validationCases dir =
    probe "validation-cases" [ ref "data" (dir + "/validation.ron") ] [ LiteralArg dir ]
          (fun _ -> (* run the analytic cases + tolerances; Met iff all pass *) Met)

let conservationEconomy =
    rule "p2-econ-conservation" Deterministic P2 (Atom (validationCases "data/econ")) |> blocking

// a newly derived physics formula is a judgement call, not a lint
let newDerivationSound =
    rule "p2-derivation-plausible" AgentReviewed P2
        (Opaque ("derivation-physically-sound", fun _ -> Unknown "judgement"))
    |> asking "Is this derived relation dimensionally and physically sound? Cite the law."
```

- **Deterministic:** every `data/*/validation.ron` case passes; dimensional
  analysis on changed constants; six-currency transactions balance.
- **AgentReviewed:** a new derived relation is physically sound; a maneuver/
  trajectory is plausible.
- **HumanOnly:** relax a conservation tolerance (a pillar-touching decision).

### Plausibility & provenance (P1, P6)

```fsharp
let everyNumberSourced =
    probe "source-tags" [ ref "data" "data/**" ] []
          (fun _ -> (* every numeric field in data/**.ron has a source: sibling *) Met)

let noBannedConcepts =
    probe "banned-concepts" [ ref "data" "data/**"; ref "design" "design/**" ] []
          (fun _ -> (* scan for reactionless / FTL / warp / "top speed" / invented scarcity *) Met)

let plausibilityFloor =
    rule "p1-provenance" Deterministic P1
        (All [ Atom everyNumberSourced; Atom noBannedConcepts; Atom (dataPinMatches "blake3") ])
    |> blocking

let sourceIsCredible =
    rule "p1-source-credible" AgentReviewed P1
        (Opaque ("cited-source-real", fun _ -> Unknown "judgement"))
    |> asking "Is the cited source flown hardware, a funded program, or peer-reviewed? Quote it."
```

- **Deterministic:** `source:` present on every number; banned-concept scan;
  `blake3` data pin matches; Sojournal references resolve.
- **AgentReviewed:** the cited source is actually credible and supports the
  number.
- **HumanOnly:** admit a brand-new technology to the canon.

### Faction fairness (P5)

The asymmetry-from-economics-never-physics pillar becomes a deterministic
*containment* check plus a judgement on balance.

- **Deterministic:** per-faction data (`data/polity`, `data/econ`) only touches
  economic / political / research-bonus fields — **never** astro/vehicle physics
  constants. (All ten factions resolve to one physical reality.)
- **AgentReviewed:** is this asymmetry "fair but different" rather than strictly
  stronger?

### Research process (P3)

- **Deterministic:** the `data/tech` web is well-formed; every engineering node
  carries the required progression (basic science → insight → engineering → test
  campaign → flight heritage); no "purchase shortcut" edge skips stages;
  dead-end / overrun params present.
- **AgentReviewed:** a deliberate leapfrog is justified and plausible.

## Software-development adapters

### Determinism (FA-01 + harness) — the crown jewel

Bridges gameplay and software: the core promise of the game *is* a deterministic
simulation, so its highest-value invariant is reproducibility.

```fsharp
let replayStable scenario =
    probe "replay-stable" [ ref "scenario" scenario; ref "crate" "sojourn-harness" ] [ LiteralArg scenario ]
          (fun _ -> (* drive scenario twice via harness; compare blake3 state hash *) Met)

let noNondeterministicApi =
    probe "no-nondet-api" [ ref "crate" "sojourn-core" ] []
          (fun _ -> (* grep sim crates: no SystemTime/Instant, no thread_rng/rand::random,
                       no HashMap/HashSet iteration, no std f64 transcendentals (use libm) *) Met)

let determinismCore =
    rule "determinism-core" Deterministic FloatPolicy
        (All [ Atom noNondeterministicApi
               Atom (replayStable "scenarios/smoke_decade.ron") ])
    |> blocking

let subtleNondeterminism =
    rule "determinism-subtle" AgentReviewed FloatPolicy
        (Opaque ("float-order-stable", fun _ -> Unknown "judgement"))
    |> asking "Could this change reorder floating-point accumulation or iteration and break replay?"
```

- **Deterministic:** every `scenarios/*.ron` replays to an identical state hash;
  banned-nondeterminism API scan; seeded-stream discipline; toolchain pin
  unchanged.
- **AgentReviewed:** does new code introduce a subtle ordering nondeterminism.

### Crate architecture & surface (FA-04 C1)

Reifies the dependency audit Sojourn already runs by hand.

```fsharp
let sliceDecoupling =
    probe "slice-decoupling" [ ref "cargo" "workspace" ] []
          (fun _ -> (* cargo metadata: gameplay slices -> sojourn-core only,
                       never each other, never a UI crate; UI is the lone composition root *) Met)

let architecture =
    rule "fa04-decoupling" Deterministic Decoupling (Atom sliceDecoupling) |> blocking

let newThirdPartyMathDep =
    rule "no-new-math-dep" HumanOnly Decoupling
        (Opaque ("third-party-math-dep", fun _ -> Unknown "decision"))
// Sojourn's standing rule is "no new third-party math/physics deps" — a human call
```

- **Deterministic:** slice decoupling holds; slice query-API surface is semver-
  stable; `postcard` save schema stays backward-compatible; `deny.toml` /
  `clippy.toml` clean.
- **AgentReviewed:** is a new (non-math) dependency justified.
- **HumanOnly:** add a third-party math/physics dependency (against the standing
  rule); change the save format.

### Data-dense presentation (P4, FR-UI-1505)

- **Deterministic:** UI session state is ephemeral and never written into the
  deterministic save (FR-UI-1505 — the UI crate emits no save DTO); the UI theme
  config carries no plausibility-bearing numbers; `sojourn-ui` is headless-
  testable (no GUI dep leaks into the view-model).
- **AgentReviewed:** does a screen honour the data-dense, information-first
  convention (`gameplay/UI-UX-CONVENTIONS.md`).

### Spec Kit lifecycle

Reused unchanged from [Spec Kit in the system](speckit-in-the-system.md): Sojourn
already has `.specify` / `specs/FA-*` / `tasks.md`, so the lifecycle adapter
attaches with no new work.

## Deeper gameplay rules (a second pass)

The eight adapters above mostly *reify hygiene Sojourn already runs* — provenance,
conservation, determinism, decoupling. This section pushes further, into rules that
govern the **shape of the game** rather than the correctness of a file. They
exercise capabilities of the [reified algebra](rule-edsl.md) the first pass leaves
idle: **graph folds** over the tech web, **deeper `Implies` chains** spanning four
or five domains, the **`HumanOnly` tier** on a genuinely ethical call, and a new
kind of probe that the deterministic core makes almost free — the **seed sweep**.

### The seed-sweep probe (and a ninth adapter)

Because the core is bit-deterministic from `seed + decision script`, and
`sojourn-harness` already drives scenarios, a probe can *run the simulation across a
seed sweep and assert a distributional property* — and stay **Deterministic**,
because the run is reproducible. The judgement is not in the computation; it is in
the **acceptance band**. That split lands on the tiers exactly:

> The sweep is `Deterministic`. The band it must fall inside — "is this *fair*?",
> "is this *fun-shaped*?" — is `HumanOnly`. The diagnosis of *why* a sweep drifted
> out of band is `AgentReviewed`.

That is its own adapter — **Playtest / balance** — and it is the one addition that
turns governance-of-the-codebase into governance-of-the-*design*.

```fsharp
let seedSweep scenario metric n =
    probe "seed-sweep" [ ref "scenario" scenario; ref "crate" "sojourn-harness" ]
          [ LiteralArg scenario; NumberArg (float n) ]
          (fun _ -> (* drive `scenario` across n seeds; reduce to the `metric` distribution;
                       Met iff distribution ∈ the declared band, else Unmet w/ the outlier seed *) Met)
```

### Currency economy — "nothing is free" as contract

The six conserved currencies (funds, delta-v, mass, crew-time, ops-capacity,
political capital) are a stated pillar but only the *physical* ones conserve in
`validation.ron` today. Make the design contract checkable, and watch the soft
currencies — reputation, political capital — which are the likely leak.

```fsharp
// every player-issuable action must debit >= 2 of the six currencies
let noFreeLunch =
    probe "no-free-lunch" [ ref "data" "data/econ/actions.ron" ] []
          (fun _ -> (* each action's cost vector touches >= 2 currencies *) Met)

let currencyClosure =
    rule "p2-currency-closure" Deterministic P2
        (All [ Atom noFreeLunch
               Atom (everyCurrencyHasDeclaredSource "funds")
               Atom (everyCurrencyHasDeclaredSource "political") ])
    |> blocking

// a commodity's value must not fall as its delta-v distance from a sink rises
let deltaVAddress =
    rule "p2-deltav-address" Deterministic P2
        (Atom (priceMonotoneInDeltaV "data/econ/commodities.ron"))
    |> advisory
```

### Research curve — the tech web as a governed graph

```fsharp
// UL -> EngineeringProgram -> Technology must be a DAG, every tech reachable from UL=0
let techWebWellFormed =
    probe "tech-web-dag" [ ref "data" "data/tech/web.ron"; ref "data" "data/research/domains.ron" ] []
          (fun _ -> (* topo-sort; Met iff acyclic AND every tech reachable by some
                       path that does not require itself *) Met)

let researchGraph =
    rule "p3-tech-web" Deterministic P3 (Atom techWebWellFormed) |> blocking

// the README's own promise: ~1 breakthrough / 8-15 yr in a heavily-invested domain
let breakthroughCadence =
    rule "p3-breakthrough-cadence" Deterministic P3
        (Atom (seedSweep "scenarios/deep_invest.ron" "mean_breakthrough_interval_years" 200))
    |> advisory   // the band [8.0, 15.0] is the human dial; the sweep itself is mechanical

// a failure must teach: no pure-loss outcome
let failureTeaches =
    rule "p3-failure-teaches" Deterministic P3
        (Atom (everyFailureInjectsUnderstanding "data/research")) |> blocking

// over-investing to leapfrog must cost strictly more basic science than the stage-by-stage path
let leapfrogCost =
    rule "p3-leapfrog-cost" AgentReviewed P3
        (Opaque ("leapfrog-not-dominant", fun _ -> Unknown "judgement"))
    |> asking "Does the leapfrog path cost strictly more basic science than going stage-by-stage?"
```

### Faction asymmetry — "asymmetric but fair", measured

The first pass only checks the *negative* (faction data never touches physics
constants). Add the positive: asymmetry must be sourced, and balance must measure.

```fsharp
// every faction bonus/penalty must cite real-world heritage (P5 x P1)
let asymmetryIsSourced =
    rule "p5-asymmetry-sourced" AgentReviewed Cross
        (forEachFactionBonus ==> hasCredibleSource)
    |> asking "Does this faction asymmetry cite flown hardware / a funded program (e.g. Roscosmos NTP heritage)?"

// across a seed sweep, no faction's score distribution leaves the fairness band
let factionFairnessBand =
    rule "p5-fairness-band" Deterministic P5
        (Atom (seedSweep "scenarios/all_factions.ron" "score_spread_by_faction" 200))
    |> blocking   // the band is a HumanOnly dial; the measurement is reproducible

// every faction has at least one non-failure path at default difficulty
let noUnplayableFaction =
    rule "p5-no-dead-faction" AgentReviewed P5
        (Atom (seedSweep "scenarios/all_factions.ron" "min_faction_survival_rate" 200))
    |> asking "Is any faction's survival rate ~0 at default difficulty (e.g. Astrolith's revenue-or-bankrupt)?"

// no single propulsion architecture should win every Grand Goal
let noDominantArchitecture =
    rule "p5-strategy-diversity" AgentReviewed P5
        (Atom (seedSweep "scenarios/grand_goals.ron" "winning_architecture_by_goal" 200))
    |> asking "Does one of NTP / NEP / reusable-chemical win all four Grand Goals across the sweep?"
```

### Mission coherence — feasibility, not just dimensions

```fsharp
// no contract may be physically impossible with any researchable vehicle inside its window
let contractsFeasible =
    rule "p2-contracts-feasible" AgentReviewed Cross
        (contractOffered ==> existsResearchableVehicleWithin)   // astro x vehicle x economy
    |> asking "Is there a researchable vehicle that closes this contract's delta-v inside its window?"

// a contract's quoted delta-v must be >= the n-body authoritative cost, not the cheaper patched-conic
let authorityBudget =
    rule "p2-authority-budget" Deterministic P2
        (Atom (quotedDeltaVGteNBody "data/econ/contracts.ron")) |> blocking
```

### Astrobiology — where the Human tier earns its keep

```fsharp
// confidence may only rise through the staged ladder; no stage skipped to "confirmed"
let detectionStaging =
    rule "p8-detection-staging" Deterministic P1
        (Atom (stagingMonotone "data/astro/biosignatures.ron")) // orbital -> in-situ -> microscopy -> sample return
    |> blocking

// every positive-detection path must model a competing abiotic explanation (educational honesty)
let abioticCompetitor =
    rule "p8-false-positive-honesty" AgentReviewed P1
        (positiveDetectionPath ==> hasModeledAbioticExplanation)
    |> asking "Does this detection path model a competing abiotic explanation?"

// detected life is a science object, never an actor (v1.0 scope)
let lifeNeverActs =
    rule "p9-life-not-actor" Deterministic P1
        (Not (anyEventWhere "subject" "detected_life")) |> blocking

// entering a Special Region requires sterility OR a wired-up science/reputation penalty (astro x polity)
let planetaryProtection =
    rule "p1-cospar-teeth" Deterministic Cross
        (entersSpecialRegion ==> (isSterileLander .| modelsScienceRepPenalty)) |> blocking

// admitting a new candidate-life world / editing the seeded ground-truth prior is reserved for humans
let newLifeWorld =
    rule "p8-new-life-world" HumanOnly P1
        (Opaque ("admit-life-world", fun _ -> Unknown "decision"))
```

### Educational honesty — the Sojournal as a governed surface

```fsharp
// every mechanic consuming a sourced constant must have a Sojournal entry citing the SAME source
let mechanicExplained =
    rule "p8-sojournal-coverage" Deterministic P1 (Atom everyMechanicHasSojournalEntry) |> advisory

// a Sojournal claim may not contradict its own citation
let sojournalConsistent =
    rule "p8-sojournal-consistent" AgentReviewed P1
        (Opaque ("claim-matches-cite", fun _ -> Unknown "judgement"))
    |> asking "Does this Sojournal claim contradict the source it cites?"
```

### Difficulty meta-invariant

```fsharp
// difficulty alters harshness, never physics
let difficultyNotPhysics =
    rule "p2-difficulty-not-physics" Deterministic P2
        (Not (difficultyTable |> touchesPhysicsConstant)) |> blocking
```

### The flagship composites

The highest-value rules bind four-plus domains in one explicit `Implies` — the
pattern [theory](theory-and-composition.md) names as most valuable. These extend
the simpler `new-propulsion` rule in [Composition](#composition):

```fsharp
// admitting new propulsion: sourced AND conserves AND climbs the TRL ladder AND
// faction-neutral physics AND explained AND reachable by some feasible contract.
let newPropulsionAdmission =
    rule "new-propulsion-admission" AgentReviewed Cross
        (   addsEngineeringNode "propulsion"
        ==> All [ hasCredibleSource                 // Plausibility
                  rocketEquationHolds               // Conservation
                  followsResearchStages             // Research
                  physicsIsFactionNeutral           // Fairness
                  hasSojournalEntry                 // Educational honesty
                  existsResearchableVehicleWithin ]) // Mission coherence
    |> blocking

// the Homestead Grand Goal, restated as an invariant: a "survivable" settlement
// must close every budget simultaneously over the 5-year resupply embargo.
let homesteadSurvivable =
    rule "homestead-survivable" AgentReviewed Cross
        (   settlementTemplate
        ==> All [ closesMassBudget; closesPowerThermal
                  closesLifeSupport; closesIsruOverEmbargo ]) // base x crew x economy x astro
    |> blocking
```

`homesteadSurvivable` is the Homestead win condition and a governance rule at once
— which is the whole thesis: the design *is* the contract.

### Why these are "more complex"

The first eight adapters are per-artifact predicates. This pass adds four shapes the
algebra supports but the first pass leaves idle:

- **Graph folds** — `techWebWellFormed` and `detectionStaging` are reachability /
  topology over a DAG, not field checks.
- **Distributional probes** — the seed sweep reduces hundreds of deterministic runs
  to a single band test, with the band as the only human input.
- **Deep `Implies` chains** — `newPropulsionAdmission` spans five domains in one
  named, centrally-reviewable combinator.
- **The `HumanOnly` tier on a real decision** — `newLifeWorld` is the genuinely
  ethical call the tier exists for.

None of this needs a kernel change: they are new facts, artifacts, and probes. The
adoption bar holds even as the rules get materially harder.

## Composition

The eight adapters compose at one root via a coproduct (see
[adapters](adapters.md) and [theory](theory-and-composition.md)):

```fsharp
type SojournFact =
    | Conservation   of ConservationFact
    | Plausibility   of PlausibilityFact
    | Fairness       of FairnessFact
    | ResearchProc   of ResearchFact
    | Determinism    of DeterminismFact
    | Architecture   of ArchitectureFact
    | Presentation   of PresentationFact
    | SpecKit        of SpecKitFact
    | Balance        of BalanceFact
```

The valuable rules are cross-domain — expressed as explicit, deterministic
combinators at the root, never ad-hoc glue:

```fsharp
// A physics-constant change must conserve, stay deterministic, and stay sourced.
let physicsChangeDisciplined =
    rule "physics-change" AgentReviewed Cross
        (   (touches "data/astro/**" .| touches "data/vehicle/**")
        ==> All [ conservationPasses                       // Conservation domain
                  replayStable "scenarios/smoke_decade.ron" // Determinism domain
                  everyChangedNumberSourced ])             // Plausibility domain
    |> blocking

// A new propulsion tech must be sourced, conserve, and have no purchase shortcut.
let newPropulsionDisciplined =
    rule "new-propulsion" AgentReviewed Cross
        (   addsEngineeringNode "propulsion"
        ==> All [ hasCredibleSource; rocketEquationHolds; followsResearchStages ])
    |> blocking

// A faction-data change must not reach into physics tables.
let factionStaysEconomic =
    rule "faction-containment" Deterministic Cross
        (touches "data/polity/**" ==> Not (touches "data/astro/**" .| touches "data/vehicle/**"))
    |> blocking
```

## Light by default, for this repo

The dial (the [constitution](speckit-in-the-system.md)) for Sojourn writes itself:
the **pillars P1–P6, the FA-01 float policy, and the FA-04 decoupling are the
fences**. Everything else is advisory.

- Editing `design/*.md` or `gameplay/*.md` (the spec sources) — advisory; no
  machinery. Thinking is not contract.
- Editing `data/**` — advisory provenance/conservation reports in the inner loop.
- **The merge fence** (`Gate` mode, recomputed from base) blocks on: determinism
  replay (`smoke_decade.ron`), `validation.ron` conservation, `source:` presence,
  the dependency-direction audit, and the `blake3` data pin.
- `Sandbox` mode lets a designer prototype a wild propulsion idea with zero
  friction; it simply cannot reach `master`, because the fence recomputes
  independently.

## What this test validates about the design

- **The adoption bar is met.** A Rust game with no shared vocabulary adopts the
  kernel by supplying facts, artifacts, and probes — nothing else. Inference,
  tiers, severity, modes, rendering, and explanation are all reused. This is the
  second unrelated domain the [adoption bar](principles.md) requires.
- **Generality is cross-language.** The governance kernel is F#; the artifacts are
  RON, Rust, and Cargo metadata. The `ArtifactRef` abstraction and probe-as-thin-
  reader boundary hold across the language gap.
- **Reification pays off immediately.** Sojourn's invariants exist but are
  scattered across CI scripts, `validation.ron`, and a hand-run grep. Reifying
  them yields one explainable surface ("this merge is blocked because
  `smoke_decade.ron` replay diverged at tick 41,217") and a single dial instead of
  many independent gates.
- **The `CheckTier` split fits a science game perfectly.** "Is the number
  sourced?" is mechanical; "is the source credible?" is agent judgement; "admit a
  new technology to the canon?" is human. P1 alone exercises all three tiers.

## Status and honest caveats

- Proposal / test design only; no code.
- Governance would be an **external** F# tool inspecting Sojourn from outside; it
  does **not** replace Sojourn's CI, and Sojourn keeps building, testing, and
  releasing if the tool is removed (the dependency direction in
  [principles](principles.md)).
- Several probes (`replayStable`, `validationCases`) wrap work Sojourn's
  `sojourn-harness` and CI already do — the adapter orchestrates and explains
  them; it does not reimplement the physics.
- The cross-domain rules are the one place coupling concentrates; per
  [theory](theory-and-composition.md) they are kept small, named, and
  deterministic.

## References

- Sojourn repository — <https://github.com/nuklearwanze/Sojourn>
- Sojourn README (pillars P1–P6, six currencies, factions) —
  <https://github.com/nuklearwanze/Sojourn/blob/master/README.md>
- Example sourced data file —
  <https://github.com/nuklearwanze/Sojourn/blob/master/data/vehicle/classes.ron>
- Replay scenarios — <https://github.com/nuklearwanze/Sojourn/tree/master/scenarios>
- Design and gameplay specs —
  <https://github.com/nuklearwanze/Sojourn/tree/master/design>,
  <https://github.com/nuklearwanze/Sojourn/tree/master/gameplay>
- Governance design: [adapters](adapters.md), [rule eDSL](rule-edsl.md),
  [theory and composition](theory-and-composition.md),
  [Spec Kit in the system](speckit-in-the-system.md).
