# ADR-0017: Org skill registry — condition-aware materialization for the skill union

- **Status:** Accepted
- **Date:** 2026-07-04
- **Affects:** .github (registry + skill-union gate), Rendering (product manifest), SDD (process manifest + `fs-gg-project` seam), Templates (composition gate)

## Context

[ADR-0011] and [ADR-0014] established the agent-skill invariant: each of the three roots
(`.claude`/`.codex`/`.agents` `skills/`) carries the **byte-identical union** of process +
product skills, produced by one content-addressed `mirror`/`verify` (`Fsgg.SkillMirror`), with
**one manifest per producer** (`{ id, scope, sha256 }`). The consumer arm is the
[skill-union assertion](../coordination/skill-union-assertion.md) — a reusable gate that proves a
scaffolded product's roots are that union.

That manifest is deliberately a **superset catalog**: a producer declares every skill it *can*
emit, but emission is lifecycle/profile-conditioned, so the assertion treats **declared ∧ absent
from every root as legitimate** (skipped, surfaced only as a count — see
`skill-union-assertion.md` §"Set semantics"). The condition that governs emission lives only in
the producer's build inputs (Rendering's `.template.config/template.json` `sources[].condition`;
SDD's in-code `skillNames`) — it is **not recorded in any machine-readable, cross-repo surface**.
Three consequences follow, all observed on the shipped `SpaceInvaders`/`SpaceInvaders1` products
(`new-sdd-workspace`, effective params `profile=game, lifecycle=sdd, feedback=false`):

- **C1 — the shipped manifest is indistinguishable from corruption.** Each product ships a
  manifest declaring **12** product skills while materializing **8**. A consumer (a human, a
  `doctor` check, a reviewer) cannot tell *"absent because off-profile"* from *"absent because
  dropped"* — there is no per-entry reason. Three of the four gaps (`fs-gg-testing`,
  `fs-gg-samples`, `fs-gg-feedback-capture`) are **correct** for a `game`/`sdd`/`feedback=false`
  scaffold; nothing on disk says so.

- **C2 — a real supply gap hides in the blanket tolerance.** `fs-gg-project` is declared
  `scope: product`, gated `lifecycle == spec-kit` in Rendering's template, **and** SDD's
  `SeededSkills.skillNames` *deliberately excludes* the project skill (its comment: "excluding the
  product-internal `fs-gg-sdd-project`"). Under the `lifecycle=sdd` lane **neither producer supplies
  it** — a genuine gap. Because "declared ∧ absent" is unconditionally legitimate, the union gate
  cannot see it: `fs-gg-project` (dropped) is treated identically to `fs-gg-testing` (correctly
  off-profile). (Adjacent to the Rendering↔SDD skill-tree seam already tracked as
  [FS.GG.SDD#53](https://github.com/FS-GG/FS.GG.SDD/issues/53).)

  - **C2 RESOLVED (2026-07-04, FS.GG.Rendering#91 / PR #101; registry reconcile [.github#183](https://github.com/FS-GG/.github/issues/183)).**
    Rendering chose **Option A**: widen the producer condition from `lifecycle == spec-kit` to the
    profile-scoped, **lifecycle-independent** predicate `profile in [app, headless-scene, governed,
    sample-pack, game]`, so the product-orientation umbrella now materializes on **every** lane whose
    profile matches — the `sdd`/`none` supply gap is closed at the source. `fs-gg-project` is now an
    ordinary profile-gated product row in `registry/skills.yml` (same predicate as `fs-gg-scene`);
    SDD's `SeededSkills` exclusion of the product-internal project skill is **moot** for this
    orientation-skill gap (SDD never owned it — Rendering supplies it on all lanes). `sha256` +
    `source` unchanged, so registry = manifest = bytes holds. The gap that "declared ∧ absent"
    tolerance hid no longer exists to hide.

- **C3 — no authoritative full-set catalog exists.** The process set lives only in
  `SeededSkills.fs`; the product set only in Rendering's `template/skill-manifest/`. The union is
  an **emergent per-scaffold computation** (SDD manifest ∪ provider manifest). No single surface
  states "the correct skill set" for a given profile, so "does this product have the right skills?"
  has no authority to check against.

## Decision

Make skill **absence checkable** by recording the emission condition, and give the org a single
skill catalog — mirroring the [registry-schema governance][ADR-0015] precedent. Four parts,
**all additive and backward-compatible**:

1. **`materializes-when` on the manifest entry.** Extend ADR-0014's `{ id, scope, sha256 }` with an
   optional `materializes-when`: a predicate over the scaffold parameter set (`profile`,
   `lifecycle`, `feedback`, `designSystem`, …), plus optional `supplied-by` when a skill crosses a
   producer boundary. Absent ⇒ defaults to `always` (today's behavior). For Rendering this is
   **mechanical**: the predicate *is* the `template.json` `condition` the generator already reads.
   This is the **condition-aware manifest** model — keep the superset catalog; make each absence
   *justified* instead of blanket-tolerated.

2. **Org skill registry `registry/skills.yml`** (owned by `.github`), the single authoritative
   catalog: every skill `id`, `scope` (`process`|`product`), `owner` repo, canonical `source`,
   `sha256`, and `materializes-when`. Generated/reconciled from the producer manifests (never
   hand-authored bytes), projected to `docs/registry/` and changelogged like
   `dependencies.yml`. Registered as a governed contract **`skill-registry`** per [ADR-0015]: a
   typed validator owns it, schema growth is a tracked `contract-change`.

3. **Condition-aware union gate.** When a scaffold's effective parameters are available
   (`scaffold-provenance.json`), `skill-union-assert.sh` evaluates `materializes-when` per declared
   skill and adds two classes:
   - declared ∧ condition **true** ∧ absent → **`[missing]`** (FAIL — closes the blind spot; catches
     `fs-gg-project`);
   - present ∧ condition **false** → **`[unexpected]`** (materialized off-profile).
   - declared ∧ condition **false** ∧ absent → legitimate (as today, now *justified*).
   Existing `[drifted]`/`[dangling]`/`[partitioned]`/`[divergent]` are unchanged. **Without**
   provenance the gate degrades to today's superset semantics — no caller is forced to change at
   once.

4. **A documented add / manage / remove lifecycle** (`docs/coordination/skill-registry.md`): author
   `SKILL.md` in the owning repo → register the row (id, scope, owner, source, sha256, condition) →
   the owning generator emits it → the validator asserts registry = manifest = materialized bytes,
   condition-aware → CHANGELOG. Removal is the reverse; the gate's `[missing]`/`[dangling]` classes
   make a half-done add or remove fail-closed in **both** directions.

## Consequences

- **Rendering** — the product-manifest generator emits `materializes-when` from the
  `template.json` `condition` it already parses (no new source of truth). Owns the `fs-gg-project`
  resolution: **RESOLVED via Option A** (Rendering#91 / PR #101; registry reconcile .github#183) —
  arranged for it to be supplied on every lane by widening the condition to the profile-scoped,
  lifecycle-independent `profile in [app, headless-scene, governed, sample-pack, game]` (see §C2
  RESOLVED above), rather than the narrower "record `lifecycle == spec-kit` and stop
  `new-sdd-workspace` implying it" alternative.
- **SDD** — the process manifest carries `materializes-when: always` for the 15 `fs-gg-sdd-*`
  skills; SDD owns the other half of the `fs-gg-project` seam (`SeededSkills` exclusion) and its
  `doctor`/provenance path (which today derives "expected" from `ProducedPaths`, not the manifest —
  the condition-aware manifest lets it check against intent instead).
- **.github** — owns `registry/skills.yml`, the `skill-registry` contract entry + validator, and
  the gate tightening. Per [ADR-0015], the next schema growth is a tracked contract-change (bump
  `skill-registry.version` + `schemaVersion`, advance the validator pin), not silent additive drift.
- **Templates** — the composition gate passes `scaffold-provenance.json` to the union assertion so
  `[missing]`/`[unexpected]` are enforced in **both** the orchestrated and standalone lanes (the
  first real caller, extending the [T3.2](https://github.com/FS-GG/FS.GG.Templates/issues/49) wiring).
- **Backward compatible.** `materializes-when` is additive (absent ⇒ `always`); the gate degrades
  without provenance; `schemaVersion` on the producer manifest is untouched until a producer opts
  in. Rollout is sequenced on the Coordination board (Rendering/SDD manifests → registry + gate →
  Templates wiring → flip `skill-mirror-verified` enforcing), not a flag day.

### 2026-07-30 measurement — retain conditional materialization

The `.github#1863` review retains `materializes-when`. Of the 28 current product rows, 27 are
conditional. Using each row's canonical producer `SKILL.md` discovery metadata (`id`, source path,
and front-matter description), the conditional catalog versus the full product catalog measures:

| Profile | Conditional | Full | Saved |
| --- | ---: | ---: | ---: |
| `app` | 3,275 | 7,311 | 4,036 |
| `headless-scene` | 1,119 | 7,311 | 6,192 |
| `governed` | 1,119 | 7,311 | 6,192 |
| `sample-pack` | 6,213 | 7,311 | 1,098 |
| `game` | 7,186 | 7,311 | 125 |

The unused bodies remain progressively disclosed. The measured cost is catalog context, not a
demonstrated wrong-skill selection; nevertheless, the savings are material for the `app`,
`headless-scene`, and `governed` profiles. Conditional materialization therefore remains justified.

### 2026-08-15 amendment — the predicate's inputs are scaffold parameters **plus derived provenance facts** (.github#2576)

Decision 1 above describes `materializes-when` as "a predicate over the scaffold parameter set
(`profile`, `lifecycle`, `feedback`, `designSystem`, …)", and Decision 3 wires the gate to read those
values from `scaffold-provenance.json`. The gate read that literally — `.effectiveParameters` and
nothing else — and one real axis turned out not to be expressible there **at all**.

`FS.GG.Templates`' six `scope: product` rows are conditioned on a `template` axis
(`template in [fable-game]`, `template in [fable-game, fable-bindings]`,
`template in [fable-bindings]`). No scaffold can carry a `template` *parameter*, and that is a
consequence of what a parameter **is**, not an omission anyone can repair: `fsgg-sdd scaffold`
computes `effectiveParameters` as the provider descriptor's declared parameters overlaid with the
author's `--param`, then forwards **every** entry verbatim to `dotnet new <templateId>` as
`--<key> <value>`, and `dotnet new` exposes only declared template *symbols* as options. Declaring
`template` in `FS.GG.Templates/providers/fable-game.providers.yml` to make the axis visible would emit
`dotnet new fs-gg-fable-game --template fable-game …` against a template that declares no such symbol
and **break scaffolding**. The descriptor's `parameters:` list is a `dotnet new` argument list, not a
provenance annotation channel.

So check 4 answered a confident `false` for all six rows against **every product that producer
builds** — tolerating a dropped skill as "justified" and reporting a correctly materialized one as
`[unexpected]`, in both directions, silently. Measured against the live `EHotwagner/S.I.R.`
provenance document: all six answered `false`, and five of those answers were wrong.

**Amendment.** The predicate's binding environment is the scaffold parameters **plus derived
provenance facts** — today exactly one: `template`, derived from the provenance document's own
top-level `.templateRef` (the id after the last `#`, less an `fs-gg-` prefix).

Two candidate repairs were considered on `.github#2576` and both are **refused**:

- *(a) ask `FS-GG/FS.GG.SDD` to record the provider name / template id as a first-class provenance
  fact* — there is nothing to ask for. `FS.GG.SDD.Artifacts.ScaffoldProvenance.serialize` already
  writes `providerName` and `templateRef` unconditionally, beside `effectiveParameters`, and every
  live document carries them. The fact was never missing; it was simply never read.
- *(b) ask `FS-GG/FS.GG.Templates` to rewrite the six predicates in a vocabulary provenance already
  carries, and re-express its own `--assert-product` gate to match* — that changes a **working**
  evaluator to accommodate a broken one. Templates' gate resolves this axis correctly today, from a
  `--template <templateId>` argument mapped through `shortName`.

The axis is therefore resolved in `.github`'s own evaluator, and **no cross-repo change is
requested**. It binds from `templateRef` rather than `providerName` because the predicate vocabulary
*is* the short-name space: Templates' manifest generator emits these predicates as
`sprintf "template in [%s]"` over `shortName templateId` (`shortName t = t.Substring "fs-gg-".Length`)
and its `--assert-product` gate re-derives the same way, so binding the same function of the same fact
is what makes the two evaluators **unable** to answer differently. `providerName` only coincides for
the fable providers — for the `rendering` provider the name is `rendering` while the template id is
`fs-gg-ui`.

Boundaries, both fixture-pinned (`tests/skill-union/run.sh` §7p-7w): an unusable `templateRef`
(absent, `null`, empty — what `devRepoInit` writes — a bare `#`, or a bare `fs-gg-`) binds **nothing**,
so `template in [...]` stays false, because a repo with no provider template has no template axis; and
an `.effectiveParameters` `template` that **disagrees** with the derived value is a fail-closed exit 2
naming both, because two sources for one axis that answer differently is the defect this amendment
closes and a precedence rule would re-create it one layer down. An agreeing duplicate is accepted.

Nothing about `registry/skills.yml` changes: `template` was already in its `parameters:` list
(.github#2547), and the six rows' `materializes-when` values are the producer's bytes and stay
untouched. The demonstration `.github#2547` acceptance 2 asked for — the six predicates answering
correctly for a real `fs-gg-fable-game` scaffold — is discharged by that fixture section and by the
`--eval-when` run against the live S.I.R. document.

<!-- This decision adds a contract (`skill-registry`) + a registry node surface, so
docs/architecture.md is reconciled as part of resolution — after the registry update. See
docs/coordination/README.md#system-overview--the-architecture-map. -->

[ADR-0011]: 0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md
[ADR-0014]: 0014-skill-vendoring-one-manifest-one-materialize-verify.md
[ADR-0015]: 0015-register-the-registry-schema-as-a-governed-contract.md
