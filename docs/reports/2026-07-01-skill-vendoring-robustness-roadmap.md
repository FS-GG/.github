# Skill vendoring & mirroring — robustness roadmap

- **Date:** 2026-07-01
- **Owner:** `.github` (cross-repo coordination)
- **Decision:** [ADR-0014](../adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md)
  (extends [ADR-0011](../adr/0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md))
- **Coherence id:** `skill-mirror-verified` (`registry/dependencies.yml`)
- **Board epic:** Coordination → "Epic — Skill vendoring robustness (one manifest · one
  materialize-and-verify · content-addressed)"

## 1. Why

A four-repo audit (2026-07-01) found the skill-vendoring path satisfies ADR-0011's
*invariants* by multiplying *implementations*, and never verifies the one property the whole
apparatus exists to guarantee. Evidence:

| # | Finding | Evidence |
|---|---|---|
| F1 | **Four** hand-maintained "materialize union → 3 roots" implementations | SDD `HandlersScaffold.fs` (scaffold), `HandlersRefresh.fs` (refresh), `Drift.fs` (doctor/upgrade expected-set); Rendering Feature 230 = 12 sources × 3 roots = **36 `template.json` twins** |
| F2 | **No content verification** — presence only | `HandlersDoctor.fs`, `Drift.fs` check `Option.isSome`; composition `run.sh` asserts nothing about skill roots; `scaffold-provenance` has **no digest** (ADR-0011's sha256 rationale is counterfactual) |
| F3 | **Dangling wrappers shipped to products** (~13) | `fs-gg-ui` template vendors the repo's own `.agents/skills/` wholesale; `fs-gg-product-*`/framework wrappers route to `../../../src/**` and `../../../template/product-skills/**`, absent in the product |
| F4 | **Ownership enforced downstream of the violator** → cross-repo lockstep | #47: one-line template condition → 5-repo, 3-reframing epic, still open; manual `rm`+`doctor` incoherence window |
| F5 | Cosmetic token leak | `effectiveNameLower.replaces:"product"` rewrites the English word "product" in skill prose |

Current state (2026-07-01): Rendering `main` = template `0.1.60-preview.1` (Features 228/229/230
merged, **unreleased**); Templates pins `0.1.58-preview.1` + `minimumFsggSdd 0.3.0`; SDD `0.4.0`
ships the orchestrated fan-out; **#47 still open**. The system does not produce a byte-identical
union at the currently-pinned combination and cannot detect when it fails to.

## 2. Target design (ADR-0014)

**Skills are content-addressed data with one canonical body each, materialized and verified by
one shared algorithm across both lanes.**

- **Manifest** per producer: `{ id, scope: process|product, sha256, body }`. The contract, not
  directory scans or `template.json` strings.
- **One library** in `FS.GG.Contracts`: `mirror(union, roots)` + `verify(roots, union)`.
  Orchestrated lane = `fsgg-sdd` (scaffold/refresh/doctor/upgrade all route through it);
  standalone lane = template ships the manifest + one thin materialize step (same algorithm),
  replacing the 36 twins.
- **Content-addressed provenance** (`sha256` per skill) + a **content-equality guard**:
  present-in-each-root ∧ byte-identical-across-roots ∧ matches-manifest, for **process and
  product** skills. Enforced by `doctor`, repaired by `upgrade`, asserted by the composition gate.
- **Product/dev boundary**: ship `scope: product` only; a **no-dangling-route** guard rejects
  any emitted skill whose body references a path absent in the product.
- **`AGENT_SKILL_ROOTS`** = one declared constant; destinations computed, never hand-written.

Outcome: 4 implementations → 1; presence-only → content-verified in every lane; leaky boundary →
declared manifest + guard; hand-written destinations → one constant.

## 3. Phases (sequenced, publish-before-flip)

Migration is **additive**: new mechanism lands alongside the old behind identical outputs;
verification runs **advisory** first, flips **enforcing** when green; the old mechanisms are
deleted **last**. Each phase is one epic-child issue per repo.

### P0 — Decision & contract shape (`.github`, SDD/Contracts)
- **D0.1** ADR-0014 (this) + roadmap + `skill-mirror-verified` coherence id. *(this PR)*
- **D0.2** (SDD/Contracts) Define the `skill-manifest` + `AGENT_SKILL_ROOTS` types and the
  `scaffold-provenance` `sha256` field in `FS.GG.Contracts` (contract minor bump). Register the
  `scaffold-provenance` bump in the registry.
- **Exit:** contract types published; registry records the additive schema bump.

### P1 — One materialize-and-verify library (SDD)
- **S1.1** Implement `mirror`/`verify` in `FS.GG.Contracts` (pure; root-set-parameterized;
  content hashes).
- **S1.2** Route `scaffold` + `refresh` through it; delete their bespoke fan-outs. Behaviour
  byte-identical to today (golden-output test).
- **S1.3** Make `doctor`/`upgrade` **content-aware** and cover **provider** skills: assert
  present ∧ byte-identical-across-roots ∧ matches-hash; `upgrade` re-materializes on drift.
  Ship **advisory** first.
- **Exit:** one code path for all four verbs; drift detects content divergence and provider-skill
  loss in a red test; CLI coherent release cut (advances ADR-0008 minimum, publish-before-flip).

### P2 — Provider manifest + single standalone materialize (Rendering)
- **R2.1** Publish a **product skill manifest** (`scope: product`); stop vendoring the repo's
  `.agents/skills/` dev surface; remove the `fs-gg-product-*`/`src/**`-routing wrappers from
  product output. Fixes F3.
- **R2.2** Replace Feature 230's 36 `template.json` twins with **one** materialize step
  (post-action / build target) invoking the shared algorithm from the manifest. Fixes F1
  (standalone half).
- **R2.3** Scope `effectiveNameLower.replaces:"product"`. Fixes F5.
- **R2.4** Add the **no-dangling-route** guard to the template's release gates.
- **Exit:** a standalone spec-kit product has 3 byte-identical union roots, zero dangling
  wrappers, produced by one mechanism; coherent template set released.

### P3 — Verification everywhere (`.github`, Templates)
- **G3.1** (`.github`) Publish the reusable **skill-union assertion** contract/snippet the
  composition gate and any consumer CI can call (content-equality over `AGENT_SKILL_ROOTS`).
- **T3.2** (Templates) `tests/composition/run.sh` asserts the three roots are the byte-identical
  union in **every** lane (orchestrated + standalone) — replacing the current "grep for the
  failure string and skip". Fixes F2 (consumer half).
- **Exit:** the invariant is asserted where skills are produced (P1/P2) **and** where they're
  consumed (P3); a non-identical set fails a gate instead of shipping green.

### P4 — Flip enforcing, re-pin, retire (all)
- **A4.1** Flip `doctor`/composition skill-union checks **advisory → enforcing**.
- **A4.2** Re-pin Templates `providers/rendering.providers.yml` to the P2 template + P1 CLI
  minimum; composition green in both lanes. **Closes the #47 chain.**
- **A4.3** Delete the last hand-maintained mirror code (any residual twins/fan-outs).
- **A4.4** (`.github`) Flip `skill-mirror-verified` → `coherent: true`; reconcile
  `docs/architecture.md`'s skill picture.
- **Exit:** one mechanism, content-verified end-to-end, enforcing; #47 closed; registry coherent.

## 4. Sequencing & dependencies

```
P0.D0.2 (contract) ──► P1 (SDD library + verify) ──► P2 (Rendering manifest+materialize)
        │                        │                              │
        └────────────► P3.G3.1 (assert contract) ──► P3.T3.2 (composition asserts) ──► P4 (flip/re-pin/retire)
```

- P1 needs P0.D0.2 (the contract types). P2 needs P1 (the shared library) + P0.D0.2 (manifest
  schema). P3.T3.2 needs P2 (a product to assert against) + P3.G3.1. P4 needs P1+P2+P3 all green
  and follows **publish-before-flip** (CLI and template released before Templates re-pins and the
  registry flips).
- The existing `agent-skill-mirror` + `fsgg-sdd-orchestrator-axis` coherence ids remain the
  ADR-0011 rollout trackers; `skill-mirror-verified` tracks the ADR-0014 robustness overlay and
  flips last.

## 5. Acceptance criteria (definition of done)

1. **One implementation**: `mirror`/`verify` in `FS.GG.Contracts` is the only code that writes or
   checks skill roots; no repo hand-writes per-root skill destinations.
2. **Content-verified**: for every union skill (process ∧ product), all `AGENT_SKILL_ROOTS` copies
   are present, byte-identical, and hash-match the manifest — asserted by `doctor` **and** the
   composition gate, enforcing.
3. **Self-contained products**: zero dangling skill routes in a scaffolded product (guarded).
4. **One knob**: adding a runtime root is a one-line `AGENT_SKILL_ROOTS` change with no per-repo
   source edits.
5. **#47 closed**; `skill-mirror-verified` `coherent: true`; `architecture.md` reconciled.

## 6. Risks

- **Two lanes, one algorithm** — the standalone lane vendors a copy of the `FS.GG.Contracts`
  logic; a content-parity test must assert the vendored copy equals the library (else the two
  lanes drift — the exact failure mode we're removing).
- **Contract bump ordering** — the `scaffold-provenance` `sha256` field is additive; consumers on
  the old schema must ignore-unknown (validator already additive-tolerant per FS.GG.SDD#32/#49).
- **Mid-flight incoherence** — until P4, keep the current mechanisms working; do not delete a
  fan-out before its replacement is green in both lanes.

## 7. Issue map

| Phase | Repo | Issue |
|---|---|---|
| P0.D0.2 | FS.GG.SDD | contract: `skill-manifest` + `AGENT_SKILL_ROOTS` + provenance `sha256` |
| P1 | FS.GG.SDD | one `materialize-and-verify` library; content-aware drift over process+product skills |
| P2 | FS.GG.Rendering | product manifest + single standalone materialize; drop dev-surface vendoring; no-dangling guard; scope `product` token |
| P3.G3.1 | `.github` | reusable skill-union assertion contract |
| P3.T3.2 | FS.GG.Templates | composition gate asserts byte-identical union (both lanes) |
| P4 | FS.GG.Templates / `.github` | re-pin + flip enforcing + `skill-mirror-verified` coherent (closes #47 chain) |

Each row is a Coordination-board child of the epic above, with `Phase`/`Repo`/`Workstream:
Composition`/`Contract` fields set.
