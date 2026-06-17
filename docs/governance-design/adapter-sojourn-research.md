---
title: "Adapter proposal: Sojourn's research system"
category: Governance design
categoryindex: 7
index: 10
description: A deep-dive governance adapter for Sojourn's two-track research system (Understanding Levels + TRL maturation, seeding, breakthroughs, the science tide, reliability), showing complex reified rules — graph folds, property laws, determinism ordering, and seed-sweep balance bands — and what the adapter buys over the status quo of scattered Rust tests, loader checks, and implicit clamps.
---

# Adapter proposal: Sojourn's research system

This is a focused companion to the
[Sojourn test design](testdesign-sojourn.md). That document sketched a *research
process* adapter in four lines. Sojourn's research system turns out to be the
single richest invariant surface in the game — two coupled stochastic tracks, a
graph, a diffusion model, and a reliability law, all of which must stay
deterministic. It is worth a full adapter on its own, and it is the best case in
the project for showing what the [reified algebra](rule-edsl.md) buys over the
status quo.

The argument in one line: **the research system's invariants already exist, but
each is enforced by a *different mechanism* — a Rust unit test here, a loader
validation there, an implicit `clamp` in a function, a one-off CI sweep, a comment,
or nothing at all. The adapter does not invent rules; it lifts all of them onto one
tiered, explainable, hashable surface, and in doing so makes expressible the
cross-domain and distributional rules that no single Rust test can state.**

## 1. The research system, in depth

Observed in `crates/sojourn-research/` and `data/research` + `data/tech`. Two
tracks, plus four subsystems that couple them.

### Track A — Science (Understanding Levels)

`domains.rs`. Each faction accumulates a continuous **Understanding Level (UL)** in
`[0, 100]` across the **A1–A17 knowledge domains** (`UlMap` keyed by
`(faction, domain)`; `WorldUlMap` per domain). UL grows via `grow_domain()`:

```
delta = rp × ul_per_rp × dr_factor × synergy × catchup
```

- **Diminishing returns** — `dr_factor()` is `1.0` below a configurable knee, then
  decays toward `0` as UL → 100.
- **Synergy** — a multiplier `≥ 1` from coupled domains' ULs.
- **Catch-up** — factions below world UL gain a discount scaling with
  `(world − current)`, *capped so catch-up alone can never push past the World UL.*
- **Effective UL** — `effective_ul = max(ground − tacit_penalty, 0)`; gates and
  queries read effective UL, never ground. `first_unmet_floor()` gates nodes.
- `inject()` bypasses cost entirely — missions raise UL directly (FR-RESP-104).

### Track B — Engineering (TRL maturation)

`tree.rs` + `programs.rs` + `reliability.rs`. A **`TechNode`** carries
`start_trl`, `ul_floors: Vec<(DomainId, f64)>`, `tech_prereqs`, a `trl_steps`
ladder, a `ReliabilityCurve`, and `derivative_of`. Each **`TrlStep`** has a target
`trl` (2→9), an abstract `cost` (Design-Effort-days), a funding-incompressible
`min_duration_days` floor, an optional `facility` capability assertion, and an
S-curve steepness `scurve_k`.

A **`Program`** advances a tech: it completes a step only when **S-curve progress
reaches `1.0` AND elapsed time exceeds `min_duration_days`** (schedule cannot be
compressed below the safety floor). At each step boundary a **test campaign** rolls
a probabilistic outcome:

- **success** → TRL advances;
- **failure** → rework (progress resets to `0.6`), an Understanding "lesson" is
  injected into the domain, `risk_index` rises, `fail_streak` increments;
- **3+ failures on a seeded path** → **dead-end confirmed**.

Schedules are reported at **P50/P80** (P80 = P50 + a 2σ overrun margin).
**Reliability** is a scalar per-use success probability in `[0, ceiling]`:

```
reliability = trl_term(0 below TRL 6, (trl−6)/3 above)
            + heritage_term (1 − exp(−flight_units × heritage_weight))
            + ul_margin_term + trait_bonus      (then clamped to [0, ceiling])
```

It is **monotone non-decreasing in TRL, flight_units, ul_margin, and trait_bonus**,
and heritage saturates but never decreases. `derivative_start_trl()` grants a
heritage TRL bump (≥3 units → +2, 1–2 → +1, capped at TRL 7).

### The four coupling subsystems

- **Seeding** (`seeding.rs`) — at world creation, dead ends and per-domain
  breakthrough thresholds are seeded from the `research/seed` RNG stream. Seeding is
  **constructive**: for every capability category it keeps *one* path fully viable;
  all others may have dead ends rolled at each TRL step with probability
  `dead_end_rate`. Breakthrough thresholds get `0.5–1.5×` jitter.
  `every_category_reachable()` proves the constructive guarantee; a CI sweep checks
  it post-seed.
- **Breakthroughs** (`breakthrough.rs`) — `InsightMap` accrues **insight pressure**
  per `(faction, domain)`, **basic-science-weighted** (weight `1.0` basic vs `0.1`
  applied — "breakthroughs are basic-science-weighted (R7)"). Crossing the seeded
  threshold fires a breakthrough and **resets insight to zero**.
- **The science tide** (`tide.rs`, FR-RESP-401/402, R9) — **World UL** rises by a
  `tide_baseline_per_day`, publishers "double-pump" their domain, and the leading
  faction pulls world UL up (weighted by `tide_aggregate_weight`). A damping factor
  (`0.01 × dt.clamp(0.01, 1.0)`) prevents explosive convergence; world UL caps at
  `100`. This is the anti-runaway / keep-rivals-relevant mechanism.
- **Allocation** (`rp_de.rs`, FR-RESP-103) — factions split RP/DE across domains and
  programs by normalised non-negative weights; `fraction()` guards divide-by-zero.

### The determinism contract

`module.rs` registers the `"research"` module on a **daily cadence** and fixes the
intra-step order for **warp-invariance (G1)**:

> domains → RP/DE → programs → campaigns → tide → insight → personnel/aging

driven by elapsed `dt_days`, not tick counts. Exactly two named RNG streams are
used — `research/seed` and `research/test` — both via the kernel's seeded
`rand_core` (the bit-shift uniform `(next_u64() >> 11) / 2^53`). State lives in
`ResearchSlice` with a **content-hash pin** of the research data. Tests already
assert TRL maturation, P80 > P50, facility gating, below-floor rejection, and
heritage monotonicity.

## 2. Why this is adapter-worthy: the enforcement is scattered

Every invariant below is real and (mostly) already honoured. The point is *where it
lives today* versus what one reified surface gives.

| Invariant | Enforced today by | Gap |
| --- | --- | --- |
| Constructive reachability (every category has a viable path) | `every_category_reachable()` + a one-off CI sweep | a bespoke script, not a contract; invisible at review time |
| Reliability monotone in TRL/heritage/margin | one Rust unit test on sample points | not a *law*; a refactor can break monotonicity between the tested points |
| Tech-web referential integrity, ≥2 paths/category, no-combat | the RON loader (FR-RESP-901) | language-locked in Rust; not explainable or hashable as a rule |
| Catch-up never exceeds World UL; UL clamps `[0,100]` | implicit `min`/`clamp` inside `domains.rs` | an implementation detail, not a stated, testable guarantee |
| P80 > P50; min-duration incompressible; TRL ≥ 6 to fly | scattered unit tests | three separate tests, no single "maturation law" surface |
| Determinism step order (G1); only two RNG streams | a comment + replay tests | the ordering rule is a comment, not a checked artifact |
| Breakthrough cadence ≈ 1 / 8–15 yr | a README claim | **unenforced** — nothing checks the design's own promise |
| Basic-science weighting (R7) | a constant in `breakthrough.rs` | the *classification* (which domain is basic) is unchecked |
| Research bonuses never touch physics | nothing (convention) | **unenforced cross-domain** — a Rust test in `-research` cannot see `-astro` |
| Provenance on every research/tech number | CI `source:` grep | global grep, not tied to the rule that needs it |

The bottom four rows are the tell: a unit test inside `sojourn-research` *cannot*
express "no faction research bonus reaches an astro physics constant" (that spans
two crates), nor "breakthroughs arrive every 8–15 years" (that is a distribution
over many seeded runs), nor "this domain's basic/applied tag is plausible" (that is
judgement). The adapter is the only place these become first-class.

## 3. The adapter

Per the [adapter interface](adapters.md): a fact domain, an artifact mapping,
probes, a rule catalog, and fences. Everything else is the reused kernel.

### Facts and artifacts

```fsharp
type ResearchFact =
    | TechWeb      of TechWebFact        // shape of the graph
    | Maturation   of MaturationFact     // TRL / reliability / schedule laws
    | Stochastic   of StochasticFact     // seeding / breakthrough / dead-end
    | Diffusion    of DiffusionFact      // the tide / World UL
    | Insight      of InsightFact         // basic-science weighting
    | ResearchProv of ProvFact            // provenance on research data
    | Coupling     of CouplingFact        // cross-domain (physics neutrality, TRL-to-fly)

// stable handles onto Sojourn's real files, mapped to the kernel's ArtifactRef
type ResearchArtifact =
    | Domains            // data/research/domains.ron     (A1–A17, basic flags, synergy pairs)
    | ResearchParams     // data/research/params.ron      (rates, knees, thresholds, tide baselines)
    | Traits             // data/research/traits.ron
    | TechTree           // data/tech/tech-tree.ron       (TechNode / TrlStep / prereqs)
    | CapabilityCats     // data/tech/capability-categories.ron
    | Src of string      // crates/sojourn-research/src/*.rs
    | Scenario of string // scenarios/*.ron  (replay)

let artifact = function
    | Domains        -> ref "data" "data/research/domains.ron"
    | ResearchParams -> ref "data" "data/research/params.ron"
    | Traits         -> ref "data" "data/research/traits.ron"
    | TechTree       -> ref "data" "data/tech/tech-tree.ron"
    | CapabilityCats -> ref "data" "data/tech/capability-categories.ron"
    | Src f          -> ref "crate" ("crates/sojourn-research/src/" + f)
    | Scenario s     -> ref "scenario" s
```

Probes are thin readers over those formats; the algebra is the reified
`Check` (`Atom`/`All`/`Any`/`Not`/`Implies` + `Opaque`), applicative and CE-free.

### A. Tech-web as a governed graph (Deterministic)

Reifies the FR-RESP-901 loader *and* the seeding reachability sweep into one
contract — graph folds, the shape the first-pass adapters never used.

```fsharp
let referentialIntegrity =
    probe "tech-web-refs" [ artifact TechTree; artifact Domains ] []
          (fun _ -> (* every tech_prereq, ul_floor domain, derivative_of resolves *) Met)

let twoPathsPerCategory =
    probe "category-redundancy" [ artifact CapabilityCats; artifact TechTree ] []
          (fun _ -> (* every CapabilityCategory has >= 2 candidate paths *) Met)

let noCombatCategory =
    probe "no-combat-cat" [ artifact CapabilityCats ] []
          (fun _ -> (* Principle IX: no weapons/combat capability category *) Met)

// the deep one: the stage ladder is complete and has no purchase shortcut.
let ladderComplete =
    probe "trl-ladder-complete" [ artifact TechTree ] []
          (fun _ -> (* each TechNode's trl_steps cover start_trl..9 with no skipped band;
                       a leapfrog edge exists ONLY where PrereqKind = UlSatisfiable;
                       no Product prereq is satisfiable by spending alone *) Met)

// constructive reachability AFTER seeding — was a bespoke CI sweep
let constructiveReachability =
    probe "category-reachable" [ artifact CapabilityCats; artifact (Src "seeding.rs") ] []
          (fun _ -> (* post-seed: every category retains >=1 path with no dead end at any TRL step *) Met)

let techWebWellFormed =
    rule "resp-web-wellformed" Deterministic Resp901
        (All [ Atom referentialIntegrity; Atom twoPathsPerCategory
               Atom noCombatCategory;     Atom ladderComplete
               Atom constructiveReachability ])
    |> blocking
```

### B. Maturation laws (Deterministic, property-based)

A unit test checks reliability at sample points; a *law* checks the shape. The
reified rule is the law, evaluated as a property over the curve.

```fsharp
let reliabilityMonotone =
    probe "reliability-monotone" [ artifact (Src "reliability.rs"); artifact TechTree ] []
          (fun _ -> (* reliability() non-decreasing in trl, flight_units, ul_margin, trait_bonus;
                       == 0 for trl < 6; result in [0, ceiling] *) Met)

let scheduleHonest =
    probe "p80-gt-p50" [ artifact (Src "programs.rs") ] []
          (fun _ -> (* every estimate() has P80 >= P50; min_duration floor never compressed *) Met)

let flyOnlyAtTrl6 =
    probe "trl6-to-fly" [ artifact (Src "reliability.rs") ] []
          (fun _ -> (* a tech yields nonzero reliability only at TRL >= 6 *) Met)

let derivativeBumpCapped =
    probe "derivative-cap" [ artifact (Src "reliability.rs") ] []
          (fun _ -> (* derivative_start_trl: +2 / +1 / 0, capped at TRL 7 *) Met)

let maturationLaws =
    rule "resp-maturation-laws" Deterministic RespMature
        (All [ Atom reliabilityMonotone; Atom scheduleHonest
               Atom flyOnlyAtTrl6;       Atom derivativeBumpCapped ])
    |> blocking
```

### C. Determinism & ordering (Deterministic + Agent) — the crown invariant

```fsharp
let stepOrderFixed =
    probe "g1-step-order" [ artifact (Src "module.rs") ] []
          (fun _ -> (* step_world order == domains -> RP/DE -> programs -> campaigns
                       -> tide -> insight -> personnel; driven by dt_days, not ticks *) Met)

let rngStreamsNamed =
    probe "rng-streams" [ artifact (Src "module.rs"); artifact (Src "seeding.rs") ] []
          (fun _ -> (* only ctx.rng("research/seed") and ctx.rng("research/test");
                       no thread_rng / rand::random; map iteration is BTreeMap-ordered *) Met)

let researchReplayStable =
    probe "replay-stable" [ artifact (Scenario "scenarios/smoke_decade.ron"); ref "crate" "sojourn-harness" ]
          [ LiteralArg "scenarios/smoke_decade.ron" ]
          (fun _ -> (* drive twice; compare blake3 state hash of research/slice *) Met)

let determinism =
    rule "resp-determinism" Deterministic G1
        (All [ Atom stepOrderFixed; Atom rngStreamsNamed; Atom researchReplayStable ])
    |> blocking

let subtleOrdering =
    rule "resp-determinism-subtle" AgentReviewed G1
        (Opaque ("float-or-iter-order", fun _ -> Unknown "judgement"))
    |> asking "Could this change reorder float accumulation or map iteration and break research replay?"
```

### D. Stochastic-within-bounds (Deterministic sweep + Human band)

The seed sweep — a distribution over the deterministic core — is the probe class
the first-pass adapters never used. The computation is reproducible; only the
*band* is a human dial.

```fsharp
let seedSweep scenario metric n =
    probe "seed-sweep" [ ref "scenario" scenario; ref "crate" "sojourn-harness" ]
          [ LiteralArg scenario; NumberArg (float n) ]
          (fun _ -> (* drive across n seeds; reduce to `metric`; Met iff distribution ∈ band *) Met)

let seedingBounded =
    rule "resp-seeding-bounded" Deterministic RespSeed
        (Atom (probe "seed-params-bounded" [ artifact ResearchParams; artifact (Src "seeding.rs") ] []
                     (fun _ -> (* dead_end_rate within params bounds; breakthrough jitter in [0.5, 1.5] *) Met)))
    |> blocking

// the design's own promise — currently checked by nobody
let breakthroughCadence =
    rule "resp-breakthrough-cadence" Deterministic RespInsight
        (Atom (seedSweep "scenarios/deep_invest.ron" "mean_breakthrough_interval_years" 200))
    |> advisory   // band [8.0, 15.0] yr is HumanOnly; the sweep is mechanical
```

### E. Diffusion / anti-runaway (Deterministic + sweep)

```fsharp
let catchupCapped =
    rule "resp-catchup-cap" Deterministic RespCatchup
        (Atom (probe "catchup-le-world" [ artifact (Src "domains.rs") ] []
                     (fun _ -> (* catch-up term can never push ground UL past World UL *) Met)))
    |> blocking

let tideStable =
    rule "resp-tide-stable" Deterministic RespTide
        (Atom (probe "tide-bounded" [ artifact (Src "tide.rs"); artifact ResearchParams ] []
                     (fun _ -> (* world UL caps at 100; damped convergence; publish double-pump bounded *) Met)))
    |> blocking

let noPermanentLockout =
    rule "resp-no-lockout" AgentReviewed RespTide
        (Atom (seedSweep "scenarios/two_leaders.ron" "max_domain_lead_duration_years" 200))
    |> asking "Can any faction permanently monopolise a domain — i.e. does the tide ever fail to let rivals catch up?"
```

### F. Insight semantics (Deterministic + Agent)

```fsharp
let basicWeighting =
    rule "resp-basic-weighting" Deterministic RespInsight
        (Atom (probe "r7-weighting" [ artifact (Src "breakthrough.rs"); artifact Domains ] []
                     (fun _ -> (* applied domains accrue <= 0.1x basic; insight resets on fire *) Met)))
    |> blocking

let classificationSound =
    rule "resp-domain-class" AgentReviewed P1
        (Opaque ("basic-vs-applied", fun _ -> Unknown "judgement"))
    |> asking "Is each A1–A17 domain's basic-science vs applied classification scientifically defensible?"
```

### G. Provenance & plausibility (Deterministic + Agent + Human)

```fsharp
let researchSourced =
    rule "resp-provenance" Deterministic P1
        (All [ Atom (probe "sourced" [ artifact Domains; artifact ResearchParams
                                       artifact Traits;  artifact TechTree; artifact CapabilityCats ] []
                           (fun _ -> (* every number carries a non-empty source: *) Met))
               Atom (dataPinMatches "blake3") ])   // the ResearchSlice content-hash pin
    |> blocking

let sourceCredible =
    rule "resp-source-credible" AgentReviewed P1
        (Opaque ("cited-source-real", fun _ -> Unknown "judgement"))
    |> asking "Is the cited source flown hardware, a funded program, or peer-reviewed? Quote it."

let admitTechOrDomain =
    rule "resp-admit-canon" HumanOnly P1
        (Opaque ("admit-tech-or-domain", fun _ -> Unknown "decision"))
// adding a new TechNode or a new A-domain to the canon is a human call
```

### H. Cross-domain coupling — what no Rust test in `-research` can say

The highest-value rules, expressed as explicit deterministic `Implies` at the
composition root (never ad-hoc glue):

```fsharp
// faction research traits/bonuses are economic/political/research only — never physics.
let bonusFactionNeutral =
    rule "resp-bonus-neutral" Deterministic Cross
        (touches "data/research/traits.ron" ==>
            Not (touches "data/astro/**" .| touches "data/vehicle/**"))
    |> blocking

// a tech may be consumed by a vehicle/astro design only at TRL >= 6 (flyable).
let onlyFlyTrl6 =
    rule "resp-fly-trl6" AgentReviewed Cross
        (vehicleConsumesTech ==> techMaturityGte 6)
    |> asking "Does any vehicle/component design use a technology below TRL 6 (unflyable)?"

// admitting new propulsion ties research to four other domains (extends the
// new-propulsion composite in the test design's deeper-rules section).
let newPropulsionAdmission =
    rule "resp-new-propulsion" AgentReviewed Cross
        (   addsEngineeringNode "propulsion"
        ==> All [ hasCredibleSource            // Plausibility
                  rocketEquationHolds          // Conservation
                  ladderComplete'              // Research (this adapter)
                  physicsIsFactionNeutral      // Fairness
                  existsResearchableVehicle ]) // Mission coherence
    |> blocking
```

## 4. Composition

The adapter lifts into the project coproduct with the standard contramap, the same
way the eight in the [test design](testdesign-sojourn.md) do:

```fsharp
let private liftResearch (r: Rule<ResearchFact>) : Rule<SojournFact> =
    r |> Rule.contramapFacts (function ResearchProc f -> Some f | _ -> None)
```

`bonusFactionNeutral`, `onlyFlyTrl6`, and `newPropulsionAdmission` are the
cross-domain combinators; they live at the root, are small and named, and are the
only place research couples to physics, vehicle, and economy facts.

## 5. Fences and light-by-default

The research dial follows the project default — advisory in the inner loop, a short
blocking set at the merge fence:

- Editing `data/research/*.ron` or `data/tech/*.ron` — advisory provenance and
  well-formedness reports while you iterate.
- **The merge fence** blocks on: `resp-web-wellformed` (graph + constructive
  reachability), `resp-maturation-laws`, `resp-determinism` (G1 + replay),
  `resp-provenance` (sources + `blake3` pin), `resp-catchup-cap`, `resp-tide-stable`,
  `resp-basic-weighting`, and the cross-domain `resp-bonus-neutral`.
- `breakthroughCadence` and `noPermanentLockout` are **advisory balance reports** —
  loud when the sweep drifts, never a hard block, because the band is a human dial.
- `Sandbox` mode lets a designer prototype a wild tech-tree branch with zero
  friction; the fence recomputes independently from base, so it cannot reach
  `master` unreviewed.

## 6. What the adapter buys (the "vs not" summary)

Without the adapter, the research invariants are honoured by **six different
mechanisms** in two languages, none of which compose:

1. **Unification.** One surface evaluates, renders, hashes, and explains every
   research rule. A blocked merge reads "blocked: `resp-determinism` — `smoke_decade`
   replay diverged in `research/slice` at day 4,217", not "a Rust test failed
   somewhere".
2. **Laws over samples.** `reliabilityMonotone`, `ladderComplete`, and `catchupCapped`
   state *properties* the current point-sample unit tests only spot-check.
3. **Newly expressible rules.** The cross-domain `bonusFactionNeutral` / `onlyFlyTrl6`
   and the distributional `breakthroughCadence` / `noPermanentLockout` **cannot be
   written as a `sojourn-research` unit test at all** — they span crates or span
   seeds. The adapter is where they become first-class.
4. **The tier split fits the science.** "Is the ladder complete?" is mechanical;
   "is this domain's basic-science tag defensible?" is agent judgement; "admit a new
   technology to the canon?" is human. One subsystem exercises all three tiers.
5. **Honesty about the design's promises.** The breakthrough-cadence band turns a
   README sentence ("roughly once per 8–15 years") into a checked, drift-reporting
   artifact — governance of the *design*, not just the code.

And the cost stays at the adoption bar: new facts, artifacts, and probes only. The
kernel, tiers, severity, routing, rendering, and explanation are all reused, and
Sojourn takes no dependency on the external F# tool.

## 7. Status and caveats

- Proposal only; no code. Governance would be an **external** F# tool inspecting
  `data/**`, `crates/sojourn-research/**`, and Cargo/replay artifacts; Sojourn keeps
  building and testing if it is removed.
- Several probes (`researchReplayStable`, the seed sweeps, `constructiveReachability`)
  **wrap** work `sojourn-harness` and the existing CI sweep already do — the adapter
  orchestrates and explains them; it does not reimplement the simulation.
- The "law" probes (`reliabilityMonotone`, `ladderComplete`) are stated here as
  intent; in practice they would be discharged by a bounded property check or a
  symbolic argument over the curve, not exhaustive enumeration.
- The cross-domain rules are the one place coupling concentrates; per
  [theory](theory-and-composition.md) they stay small, named, and deterministic.

## References

- Research crate — <https://github.com/nuklearwanze/Sojourn/tree/master/crates/sojourn-research>
- Domains / UL — `crates/sojourn-research/src/domains.rs`
- Tech web / TRL ladder — `crates/sojourn-research/src/tree.rs`, `programs.rs`
- Reliability & heritage — `crates/sojourn-research/src/reliability.rs`
- Seeding / breakthroughs / tide — `seeding.rs`, `breakthrough.rs`, `tide.rs`
- Module & determinism (G1, RNG streams) — `crates/sojourn-research/src/module.rs`
- Research data — `data/research/{domains,params,traits}.ron`,
  `data/tech/{tech-tree,capability-categories}.ron`
- Governance design: [adapters](adapters.md), [rule eDSL](rule-edsl.md),
  [theory and composition](theory-and-composition.md),
  [Sojourn test design](testdesign-sojourn.md).
