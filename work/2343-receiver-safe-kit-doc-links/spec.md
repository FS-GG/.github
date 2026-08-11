---
schemaVersion: 1
workId: 2343-receiver-safe-kit-doc-links
title: Make coordination-kit documentation links receiver-safe
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Make coordination-kit documentation links receiver-safe Specification

Prose status: specified

## User Value
A coordination-kit receiver (e.g. EHotwagner/S.I.R.) materializes only skill-directory bytes from registry/repos.yml's kit: block, so every relative link inside a delivered SKILL.md or reference must resolve inside that receiver's own tree instead of dangling on a source-repo-only docs/ path.

## Scope
- SB-001: .agents/skills/cross-repo-coordination/SKILL.md and .agents/skills/pnext-item/references/deep-detail.md (and their byte-identical .claude/skills/ mirrors), plus a new receiver-safe-links check in scripts/check-skill-quality.py and its regression fixture in tests/skill-quality/run.sh. Out of scope: changing which files registry/repos.yml's kit: block delivers, and any other skill catalog.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can A coordination-kit receiver (e.g. EHotwagner/S.I.R.) materializes only skill-directory bytes from registry/repos.yml's kit: block, so every relative link inside a delivered SKILL.md or reference must resolve inside that receiver's own tree instead of dangling on a source-repo-only docs/ path.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Make coordination-kit documentation links receiver-safe is available, when the user exercises it, then they can A coordination-kit receiver (e.g. EHotwagner/S.I.R.) materializes only skill-directory bytes from registry/repos.yml's kit: block, so every relative link inside a delivered SKILL.md or reference must resolve inside that receiver's own tree instead of dangling on a source-repo-only docs/ path.

## Functional Requirements
- FR-001: scripts/check-skill-quality.py --root <tree> --contract <contract> exits 0 against the fixed catalog, and exits 1 naming a receiver-unsafe link when a kit-delivered skill's relative link is made to resolve outside the union of kit-delivered skill directories. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2343-receiver-safe-kit-doc-links`.
