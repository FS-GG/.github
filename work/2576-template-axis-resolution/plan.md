---
schemaVersion: 1
workId: 2576-template-axis-resolution
title: Template Axis Resolution
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2576-template-axis-resolution/spec.md
sourceClarifications: work/2576-template-axis-resolution/clarifications.md
sourceChecklist: work/2576-template-axis-resolution/checklist.md
publicOrToolFacingImpact: true
---

# Template Axis Resolution Plan

Prose status: planned

## Source Snapshot
- spec: work/2576-template-axis-resolution/spec.md sha256:b100cb69b5835f6b574e29b864ffed3f11a0d3fd6d7d4a27cb0781bcd00da18e schemaVersion:1
- clarifications: work/2576-template-axis-resolution/clarifications.md sha256:f7847b9afe69c7b14205fe80dba711bc3dd102dcca9ea860aae92b234afe8c34 schemaVersion:1
- checklist: work/2576-template-axis-resolution/checklist.md sha256:e525623c09e026ba0d4bbb2f5b0b2164b56c14e884cc8d54292685d887953ddc schemaVersion:1

## Plan Scope
- Work item 2576-template-axis-resolution is planned from the current specification, clarification, and checklist facts.
- Requirement count: 9.
- Clarification decision count: 0.
- Checklist result count: 9.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Resolve the `template` axis inside `.github`'s own evaluator, from a
  provenance fact `FS.GG.SDD.Artifacts` ALREADY writes. Both cross-repo candidates named on the item are
  refused: candidate (a) asks FS.GG.SDD to add the provider name / template id to
  `scaffold-provenance.json`, and `ScaffoldProvenance.serialize` writes `providerName` and `templateRef`
  unconditionally today (`FS.GG.SDD/src/FS.GG.SDD.Artifacts/ScaffoldProvenance.fs:113-115`), so there is
  nothing to request; candidate (b) asks FS.GG.Templates to rewrite six predicates and its own
  `--assert-product` gate, which changes a working evaluator to accommodate a broken one. `load_params`
  therefore binds `PARAM[template]` from `.templateRef` after populating `.effectiveParameters`.
- PD-002 [AC-001] [FR-002] complete: Derive the bound value with the PRODUCER'S OWN function, not a
  parallel one. `FS.GG.Templates/scripts/generate-skill-manifest.fsx:89` defines
  `shortName templateId = templateId.Substring "fs-gg-".Length` and line 108 emits
  `sprintf "template in [%s]"` over exactly those short names, so the predicate vocabulary IS the
  short-name space. `templateRef` is observed in two shapes — the bare template id `fs-gg-fable-game`
  that `scaffold` writes (`HandlersScaffold.fs:687`, `TemplateRef = descriptor.TemplateId`) and the
  packaged `FS.GG.Workspace.Template::0.8.0#fs-gg-fable-game` a consumer-migration document carries — so
  take the fragment after the last `#` when present, then strip a leading `fs-gg-`. Bind NOT AT ALL when
  the result is empty: `devRepoRecord` writes `TemplateRef = ""`
  (`ScaffoldProvenance.fs:62`), and a dev repo has no template, so an unset parameter (predicate false)
  is the correct answer rather than a fabricated one.
- PD-003 [AC-001] [FR-003] complete: A `template` key already in `.effectiveParameters` that DISAGREES
  with the derived value is a fail-closed `die` (exit 2) naming both values, not a silent precedence
  rule. Silently preferring either side reintroduces exactly the defect this item exists to close — two
  evaluators over one vocabulary that can answer differently — and there is no correct answer to
  inherit. An EQUAL value is accepted, because agreeing sources are not a conflict.
- PD-004 [AC-001] [FR-004] complete: Discharge `.github#2547` acceptance 2 — the demonstration `.github`
  could not previously make — against the LIVE `EHotwagner/S.I.R.` provenance document the item's own
  Verification section cites, using the shipped `--eval-when` reference evaluator so the demonstration
  runs the same `load_params`/`eval_condition` the gate uses. Its `outcome` is `consumerMigration`, so it
  is used to demonstrate the AXIS RESOLVING from a real document, never as evidence about which skills
  that tree should carry.
- PD-005 [AC-001] [FR-005] complete: Add hermetic vectors to `tests/skill-union/run.sh` in the shape its
  7a/7c legs already use, on a provenance document whose ONLY source of `template` is `templateRef` — so
  a vector that passes proves the new binding ran, and cannot be satisfied by an `effectiveParameters`
  key. Cover both directions ([missing] and [unexpected]), both `templateRef` shapes, the empty
  `templateRef`, the agreeing-duplicate accept, and the disagreeing-duplicate exit 2.
- PD-006 [AC-001] [FR-006] complete: Record gate-inversion evidence at authoring time: for each new
  vector, the exact mutation applied to `scripts/skill-union-assert.sh` and the observed red, captured in
  `evidence.yml` and the PR body. A vector that survives its own inversion is a finding by definition.
- PD-007 [AC-001] [FR-007] complete: Regenerate `dist/skill-union-assert.sh` with
  `scripts/generate-skill-union-bundle`. The bundle is the artifact downstream consumers actually fetch
  (`FS.GG.Templates/tests/composition/lib/skill-union.sh:165` pins `dist/skill-union-assert.sh`), and
  `skill-union-bundle.yml` runs `--check`, so leaving it stale is both a red gate and a real behavioural
  split between the script this repo runs and the one Templates runs.
- PD-008 [AC-001] [FR-008] complete: Rewrite the `registry/skills.yml` annotations that assert the axis
  is unanswerable — the `parameters:` block comment and the FS.GG.Templates row-block comment — to state
  where it is now resolved and cite the demonstration. The `materializes-when` values themselves are NOT
  touched: the producer manifest is authoritative and check 6 reds on divergence.
- PD-009 [AC-001] [FR-009] complete: Record the decision durably: an ADR-0017 amendment section (the ADR
  that defined check 4 and named its inputs as scaffold PARAMETERS) plus the `--params` documentation in
  `docs/coordination/skill-union-assertion.md`, so the next reader of either surface learns that the
  binding environment is parameters PLUS derived provenance facts.

## Contract Impact
- PC-001 [PD-001] command report: `scripts/skill-union-assert.sh` and its generated `dist/` bundle are a
  cross-repo consumer contract (FS.GG.Templates fetches the bundle). The change is ADDITIVE and
  compatibility-preserving: no existing flag, exit code, or output line changes; a provenance document
  with no usable `templateRef` binds nothing and every existing verdict is unchanged. The only new
  refusal is the disagreeing-duplicate exit 2, which no observed document can reach.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `bash tests/skill-union/run.sh` (hermetic) and
  `bash tests/skill-union/conformance.sh` green; `bash scripts/generate-skill-union-bundle --check`
  green; `shellcheck scripts/skill-union-assert.sh`; the six-predicate demonstration against the live
  S.I.R. provenance; and one recorded inversion per new vector.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No schema version moves. `scaffold-provenance.json` stays schemaVersion 1
  and is READ ONLY — no producer is asked to emit a new field — and `registry/skills.yml` stays
  schemaVersion 3 with an unchanged `parameters:` list, because `template` was already declared there by
  `.github#2547`. Consumers that never pass `--params` are unaffected.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2576-template-axis-resolution/work-model.json` refreshes from the
  current plan sources. The one other generated artifact this work touches is
  `dist/skill-union-assert.sh`, regenerated by `scripts/generate-skill-union-bundle` and gated by its own
  `--check` in CI (PD-007), never hand-edited.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2576-template-axis-resolution`.
