# ADR-0048: FR-level classification keys a per-requirement, non-synthetic evidence obligation

- **Status:** Accepted
- **Date:** 2026-07-19
- **Affects:** FS-GG/FS.GG.SDD (spec grammar, evidence derivation, verify/ship readiness — owner); every repo whose specs author FRs (additive, opt-in); FS-GG/FS.GG.Governance (binds the resulting per-FR readiness count as a gate — see [ADR-0049](0049-template-profile-gate-inheritance.md)).
- **Decides:** the SDD half of the "gameplay must be headlessly tested" initiative — the org goal that a product scaffolded on the `game` profile cannot ship a gameplay requirement it has not exercised headlessly through real input.

## Context

The initiative wants **real teeth**: every *gameplay* functional requirement in a game must be
covered by a headless test driven through the standard input frontier, and that must be
un-fakeable at the ship boundary. The lever already exists and is unusually strong:

- SDD's satisfaction rule — an obligation is satisfied only by `result: pass` **and**
  `synthetic: false` (`docs/reference/authoring-contracts.md`, evidence section) — is universal;
  a synthetic pass never satisfies anything.
- Governance's `evidenceNotSynthetic` check is the **one rule no dial, disclosure, or override
  relaxes** (Governance `Adapters.SpecKit/Catalog.fsi`), and synthetic evidence **taints down the
  dependency DAG** (`AutoSynthetic`, `Kernel/Evidence.fs`). There is no waiver, by design.

So a *required, non-synthetic test obligation*, once expressible and gated, cannot be satisfied by
a synthetic-state shortcut or an un-pinned agent playthrough — which is exactly the guarantee the
initiative needs. The problem is that SDD cannot express it **per gameplay FR** today:

1. **No FR classifier.** The FR grammar is id + colon + prose + acceptance/story refs only
   (`Specification.fs:147`). The structured `Requirement` type carries a `Priority: string option`
   that is hard-coded `None` at its only construction site (`RequirementModel.fs:71`) and never
   parsed. There is no facet on an FR to say "this is gameplay" vs "this is config".
2. **No required evidence *kind*.** Obligations are keyed to FRs transitively (each FR derives
   exactly one requirement task, which derives an obligation carrying `LinkedRequirementIds`,
   `HandlersEvidence.fs:461`), but `ExpectedEvidenceKinds` is **inert** — set to a constant, read
   nowhere — and an unrecognized `kind` silently becomes `verification`. An obligation cannot
   demand "a test, not a self-attestation".
3. **No per-FR readiness gate.** `verify`/`ship` expose aggregate scalar counts only; there is no
   "every gameplay FR has a satisfied non-synthetic test" disposition, though the raw linkage
   (`Verify.fsi` `AffectedRequirementIds`) is present to build one.

These three are one coherent decision: **classify FRs, derive a non-synthetic test obligation for
the classified ones, and report it per-FR at ship.**

The nearest working precedent is `.fsgg/project.yml` `project.visualSurface: true`, which already
injects a standing, drift-guarded obligation with a *strengthened* satisfaction rule
(`pass ∧ synthetic:false ∧ a named rendered artifact`; gate
`evidence.missingVisualInspectionArtifact`). This decision follows that shape one granularity
finer — keyed per classified FR rather than once per work item.

## Decision

1. **A bounded FR-classification facet.** An FR may carry an optional class annotation on its
   coverage line, drawn from a small closed set (initially `{ gameplay }`, extensible additively).
   It is **opt-in and backward compatible**: an unannotated FR is *unclassified*, and every
   existing spec across the org stays valid with no migration. The parsed value populates the
   currently-inert classification field on `Requirement`.
2. **A per-classified-FR, kind-gated obligation.** For each FR whose class is in the
   obligation-bearing set, derivation mints a standing obligation whose satisfaction requires a
   real test — `kind` in a recognized *test* set **and** `synthetic: false`. This makes
   `ExpectedEvidenceKinds` (or a successor `RequiredEvidenceKinds`) finally **read and enforced**
   for classified FRs, rather than coerced away.
3. **A per-FR ship disposition.** `verify`/`ship` gain a per-classified-FR readiness disposition
   (satisfied / unmet) and an aggregate "classified-FR obligations unmet" count that Governance can
   bind as a block-on-ship check ([ADR-0049](0049-template-profile-gate-inheritance.md)).
4. **The classifier is the only sanctioned relief valve.** Because Governance forbids waivers and
   the synthetic gate cannot be relaxed, the sole way to exempt a requirement is to *not classify
   it gameplay* — and that is a visible act in the spec, surfaced in diff and checklist review, not
   a silent override. Declassifying to dodge the gate is reviewable by construction.

## Consequences

- SDD owns the grammar, the derivation, and the gate. `docs/reference/authoring-contracts.md`
  (drift-guarded) grows the facet grammar and the per-class satisfaction rule; this is **schema
  growth → publish-before-flip** ([ADR-0037](0037-schema-growth-is-publish-before-flip.md)).
- Consuming repos *may* classify FRs; unannotated FRs remain valid, so no repo is forced to migrate
  and no existing readiness flips. The teeth arrive only where the facet is authored and where
  Governance binds the gate.
- Registry: the SDD authoring grammar is a versioned surface; the facet lands as a `contract-change`
  with a `registry/dependencies.yml` + `docs/registry/compatibility.md` update.
- The per-FR granularity is deliberate and was the org's explicit call over the cheaper blanket
  alternative below. It raises authoring cost (each gameplay FR now owes a real headless test),
  which is the point, and which is why the companion harness (`FS.GG.Game.Harness`) and
  `fs-gg-playtest` skill — sequenced on the board, not decided here — exist to keep that cost low.

## Alternatives considered

- **Blanket per-game obligation** — clone `project.visualSurface` to a `project.headlessGameplay`
  boolean that requires ≥1 non-synthetic gameplay test per *work item*. Cheapest, reuses a proven
  gate. **Rejected**: the org chose per-FR granularity; a single shallow test would satisfy a
  blanket flag, which is not "all gameplay tested". Recorded because it remains the natural fallback
  if per-FR authoring cost proves unbearable.
- **Overload `Requirement.Priority`** as the classifier. **Rejected**: `Priority` is an unparsed
  free string; using an ordinal-sounding field for a categorical class is a latent trap.
- **Classify in front matter** (per work item) rather than per FR. **Rejected**: front matter is
  per-document, the wrong granularity — the class is a property of a requirement.
- **Checklist convention, no gate.** **Rejected**: no teeth. The initiative's premise is
  block-on-ship enforcement, which a convention cannot provide.
