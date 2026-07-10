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

<!-- This decision adds a contract (`skill-registry`) + a registry node surface, so
docs/architecture.md is reconciled as part of resolution — after the registry update. See
docs/coordination/README.md#system-overview--the-architecture-map. -->

[ADR-0011]: 0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md
[ADR-0014]: 0014-skill-vendoring-one-manifest-one-materialize-verify.md
[ADR-0015]: 0015-register-the-registry-schema-as-a-governed-contract.md
