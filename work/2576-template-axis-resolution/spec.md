---
schemaVersion: 1
workId: 2576-template-axis-resolution
title: Template Axis Resolution
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Template Axis Resolution Specification

Prose status: specified

## User Value
ADR-0017 check 4 stops answering a confident `false` for every product FS.GG.Templates builds, so a dropped or off-template Fable product skill is caught instead of silently tolerated.

## Scope
- SB-001: FS-GG/.github's own evaluator only — scripts/skill-union-assert.sh, its regenerated dist/ bundle, tests/skill-union, docs/adr/0017, docs/coordination/skill-union-assertion.md, and registry/skills.yml's annotation. No FS.GG.SDD or FS.GG.Templates change is requested or required.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can ADR-0017 check 4 stops answering a confident `false` for every product FS.GG.Templates builds, so a dropped or off-template Fable product skill is caught instead of silently tolerated.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Template Axis Resolution is available, when the user exercises it, then they can ADR-0017 check 4 stops answering a confident `false` for every product FS.GG.Templates builds, so a dropped or off-template Fable product skill is caught instead of silently tolerated.

## Functional Requirements
- FR-001: skill-union-assert.sh --params binds a `template` predicate parameter derived from the provenance document's top-level `templateRef`, so the six FS.GG.Templates `template in [...]` predicates evaluate instead of reading the empty string. (Stories: US-001; Acceptance: AC-001)
- FR-002: The derivation takes the substring of `templateRef` after the last `#` when one is present, else the whole value, then strips a leading `fs-gg-` when present; an empty result binds nothing and leaves the parameter unset. (Stories: US-001; Acceptance: AC-001)
- FR-003: A `template` key already present in `.effectiveParameters` whose value equals the derived one is accepted; a different value is a fail-closed exit 2 naming both values and the file. (Stories: US-001; Acceptance: AC-001)
- FR-004: Against the live `EHotwagner/S.I.R.` provenance document (`providerName: fable-game`, `templateRef: FS.GG.Workspace.Template::0.8.0#fs-gg-fable-game`), the six predicates answer `false` for `template in [fable-bindings]` and `true` for the other five. (Stories: US-001; Acceptance: AC-001)
- FR-005: tests/skill-union/run.sh gains hermetic vectors proving [missing] fires for a declared, template-TRUE, absent skill and [unexpected] fires for a present, template-FALSE skill, both on a provenance document whose only source of `template` is `templateRef`. (Stories: US-001; Acceptance: AC-001)
- FR-006: Every added gate ships recorded inversion evidence — the exact mutation applied and the observed red — so the gate is proven able to fail. (Stories: US-001; Acceptance: AC-001)
- FR-007: dist/skill-union-assert.sh is regenerated so `scripts/generate-skill-union-bundle --check` stays green and downstream consumers that fetch the bundle get the same evaluator. (Stories: US-001; Acceptance: AC-001)
- FR-008: registry/skills.yml's `template` annotation and the FS.GG.Templates row block stop stating the axis is unanswerable and cite the demonstration instead. (Stories: US-001; Acceptance: AC-001)
- FR-009: docs/adr/0017 records the decision — where the axis is resolved and why neither cross-repo candidate was needed — and docs/coordination/skill-union-assertion.md documents the derived binding. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2576-template-axis-resolution`.
