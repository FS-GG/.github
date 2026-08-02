---
schemaVersion: 1
workId: 2127-driver-transition-state-machine
title: Coord driver wave and housekeeping state machine
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Coord driver wave and housekeeping state machine Charter

## Identity
Make the coordination driver advance two waves and their housekeeping gates from a
typed, inspectable state machine rather than host-memory prose.

## Principles
- Live board facts and fresh receipts are the sole input to deterministic transition
  choices; human judgement remains an explicit input.
- A dispatch is fail-closed until its prerequisite reconciliation, flush, triage,
  and engine-currency evidence is fresh.
- Review-chain and worker-liveness states are typed contracts, not inferred from
  prose or terminal chat output.

## Scope Boundaries
- In: coordination CLI/core planner transitions, typed receipts, their tests, and
  equivalent drive-board guidance for Codex and Claude.
- Out: automatically classifying findings, deciding whether objectives consolidate,
  changing GitHub's review policy, or dispatching/merging real work on a caller's
  behalf.

## Policy Pointers
- `.fsgg/constitution.md` principles I, II, VI, VII, and VIII.
- Issue `.github#2127` acceptance criteria and mandatory `sdd-required` route.
- Existing `fsgg:wave-model:v1` and independent-review protocol contracts.

## Lifecycle Notes
- Tier 1: this adds tool-facing transition and receipt contracts.
- The SDD artifacts, generated readiness evidence, and PR will remain linked to
  `.github#2127`.
