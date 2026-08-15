---
schemaVersion: 1
workId: 2545-rendering-owned-product-skill-channel
title: Rendering Owned Product Skill Channel
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2545-rendering-owned-product-skill-channel/spec.md
publicOrToolFacingImpact: true
---

# Rendering Owned Product Skill Channel Clarifications

## Source Specification
- work/2545-rendering-owned-product-skill-channel/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.
- CQ-002 [AMB:AMB-002] blocking open: Resolve source ambiguity AMB-002 before checklist.
- CQ-003 [AMB:AMB-003] blocking open: Resolve source ambiguity AMB-003 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: Route B — an owner-published, pinned, content-addressed FS.GG.Rendering.Skills package consumed by the FS.GG.SDD scaffold materializer. Route A is refuted, not deprioritised, because .github would need a frozen copy of another repository's SKILL.md bytes.
- CQ-002 [AMB:AMB-002] decision: A separate .github-owned file, registry/skill-delivery-channels.yml, not a new top-level key in registry/skills.yml.
- CQ-003 [AMB:AMB-003] decision: No. ADR-0063 builds nothing and delegates transport per class to coordination rows; this item changes no decision it records.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: Route B — an owner-published, pinned, content-addressed FS.GG.Rendering.Skills package consumed by the FS.GG.SDD scaffold materializer. Route A is refuted, not deprioritised, because .github would need a frozen copy of another repository's SKILL.md bytes.
- DEC-002 [CQ-002] [AMB:AMB-002]: A separate .github-owned file, registry/skill-delivery-channels.yml, not a new top-level key in registry/skills.yml.
- DEC-003 [CQ-003] [AMB:AMB-003]: No. ADR-0063 builds nothing and delegates transport per class to coordination rows; this item changes no decision it records.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2545-rendering-owned-product-skill-channel`.
