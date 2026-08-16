---
schemaVersion: 1
workId: 2660-authored-judgement-contract
title: Authored Judgement Contract
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Authored Judgement Contract Specification

Prose status: specified

## User Value
Every independent critic in FS-GG reads an authored judgement contract that survived the v1 consolidation, and CI turns red when live prose cites a section that no longer exists.

## Scope
- SB-001: Adjudicate in writing all fifteen headings deleted from independent-review.md by b84423e7, restore those that are authored judgement contract the engine cannot enforce, extend check-prose-citations.py from file-existence to section-existence under a deliberately bounded grammar, and resolve the four dangling independent-review.md#repair-phase citations.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can every independent critic in FS-GG reads an authored judgement contract that survived the v1 consolidation, and CI turns red when live prose cites a section that no longer exists.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Authored Judgement Contract is available, when the user exercises it, then they can every independent critic in FS-GG reads an authored judgement contract that survived the v1 consolidation, and CI turns red when live prose cites a section that no longer exists.

## Functional Requirements
- FR-001: The adjudication of all fifteen deleted headings is recorded in writing, each carrying an explicit disposition and its reason. (Stories: US-001; Acceptance: AC-001)
- FR-002: Every heading adjudicated as authored judgement contract is restored to .claude/skills/pnext-item/references/independent-review.md and its .agents mirror, byte-identical between the two mirrors. (Stories: US-001; Acceptance: AC-001)
- FR-003: No content the structured review-decision/v2 ledger genuinely superseded is reintroduced, so tests/skill-quality/review-round-contract.py stays green. (Stories: US-001; Acceptance: AC-001)
- FR-004: check-prose-citations.py resolves a Markdown link fragment that names a repository-local tracked Markdown file against that file's headings, and exits 1 when the named heading is absent. (Stories: US-001; Acceptance: AC-001)
- FR-005: The section-citation grammar is bounded to Markdown inline link fragments, the bound is stated in the gate and in an ADR, and free-form natural-language section references are explicitly out of scope. (Stories: US-001; Acceptance: AC-001)
- FR-006: The gate returns permanent no-verdict rather than green when the section-citation corpus is empty, so found-nothing and looked-at-nothing do not share an exit code. (Stories: US-001; Acceptance: AC-001)
- FR-007: The four dangling independent-review.md#repair-phase citations resolve after the change, and the new gate is red on the pre-fix tree and green after. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2660-authored-judgement-contract`.
