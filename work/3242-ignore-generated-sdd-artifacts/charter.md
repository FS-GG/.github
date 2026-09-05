---
schemaVersion: 1
workId: 3242-ignore-generated-sdd-artifacts
title: Accept independently regenerated ignored SDD artifacts
stage: charter
changeTier: tier1
status: chartered
publicOrToolFacingImpact: true
---

# Accept independently regenerated ignored SDD artifacts Charter

## Problem
The live observer requires generated SDD outputs to be committed even though standard SDD ignores them and the same observer independently regenerates and validates them.

## Goal
Remove that redundant remote-file prerequisite while retaining the independent exact-candidate SDD authority and all refusal gates.

## Boundaries
- Change only the roadmap acceptance live observer and focused tests.
- Preserve the immutable lifecycle ledger and GS2-07.3 implementation identity.
