---
schemaVersion: 1
workId: 2070-workspace-provider-activation
title: Workspace Provider Activation
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2070-workspace-provider-activation/spec.md
publicOrToolFacingImpact: true
---

# Workspace Provider Activation Clarifications

## Source Specification
- work/2070-workspace-provider-activation/spec.md

## Clarification Questions
- Q-001 [AMB:AMB-001]: FS.GG.Templates' `providers/fable-game.providers.yml` declares
  `minimumFsggSdd.version: "0.6.0"`, but the owner-sourced Game skill it materializes needs SDD
  PR#819's fix, first published in `FS.GG.SDD.Cli` 1.0.0. Does this item correct that descriptor, or
  register the registry mirroring the descriptor's stated (understated) floor and file a cross-repo
  finding?

## Answers
- A-001: Registry rows mirror what the owning repo's provider descriptor actually declares — this
  item's `Paths:` do not include FS.GG.Templates' `providers/*.providers.yml`, and correcting another
  repo's file from here would be exactly the kind of unowned edit `Paths:` discipline exists to
  prevent. The gap is real and is owned by FS.GG.Templates.

## Decisions
- DEC-001 [AMB:AMB-001]: Register `minimum-fsgg-sdd.version: "0.6.0"` for the four new provider
  identities, mirroring FS.GG.Templates' current `providers/*.providers.yml` declarations exactly
  (same discipline as the existing `fs-gg-ui-template` row: the registry mirrors the descriptor, it
  does not lead it). Add an explicit registry comment on the `fable-game` identity naming the known
  gap (materializer correctness needs `FS.GG.SDD.Cli` >= 1.0.0, not 0.6.0) so no reader mistakes the
  mirrored floor as sufficient. Filed as FS-GG/FS.GG.Templates#407, recommending
  `providers/fable-game.providers.yml`'s `minimumFsggSdd.version` advance to 1.0.0, evidenced by
  `git compare v0.32.0...aa1d6d4c` (ahead_by=2, i.e. the fix postdates 0.32.0) and
  `git compare aa1d6d4c...v1.0.0` (ahead_by=12 behind_by=0, i.e. 1.0.0 contains it) — root cause is
  FS.GG.Templates' descriptor never having been re-mirrored after SDD#817/PR#819 landed, the same
  "provider-descriptor re-mirror lag" class the existing `fs-gg-ui-template` row documents extensively
  for `minimum-fsgg-sdd` advances (e.g. FS.GG.Templates#99).

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2070-workspace-provider-activation`.
