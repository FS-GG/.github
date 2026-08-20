---
schemaVersion: 1
workId: 2726-boardops-handler-registration
title: Extract FS.GG.Coord.Cli.BoardOps and establish handler registration
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

# Extract FS.GG.Coord.Cli.BoardOps and establish handler registration Charter

## Identity
- Work id: `2726-boardops-handler-registration`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Preserve every listed command's observable output, exit code, and side effects.
- Replace central dispatch ownership with composable, exactly-once handler registration.
- Move implementation and its focused tests together so the BoardOps family owns a real lane.
- Keep the Kernel/Options contracts established by #2725 as the shared dependency boundary.

## Scope Boundaries
- In scope: the fifteen BoardOps handlers named in FS-GG/.github#2726, their registration table,
  their tests, and project/solution wiring within the declared paths.
- Out of scope: behavioral changes, other command-family extractions, and changes outside
  `src/FS.GG.Coord.Cli*` or `tests/FS.GG.Coord.Cli*`.
- Packing and release-payload verification remain required compatibility gates.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2726-boardops-handler-registration`.
