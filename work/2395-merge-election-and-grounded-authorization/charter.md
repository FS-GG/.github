---
schemaVersion: 1
workId: 2395-merge-election-and-grounded-authorization
title: Merge Election And Grounded Authorization
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

# Merge Election And Grounded Authorization Charter

## Identity
- Work id: `2395-merge-election-and-grounded-authorization`
- Lifecycle stage: charter
- Status: chartered

## Principles
- A reader, gate or lock with no production writer is inert. Four of the six landed slices of
  `.github#1858`'s section 11.2 shipped exactly that, and this row is where the sequence's root
  instance is repaired. The acceptance test is therefore "a production writer exists, demonstrated by
  a test that reds when it does not", never "the code compiles".
- A gate's branch is not reached merely because its code is present. `scripts/check-claim-fence.py`
  returns at check 1 on today's four-field marker, so its check 4 has never been evaluated on a real
  pull request — while `.github/workflows/fsgg-claim-fence.yml` tells operators check 4 is expected to
  fail for a known reason. Closing this row must show check 4 both REACHED and ABLE TO FAIL, at the
  boundary of its predicate rather than merely inside its branch.
- Lowest-id-wins is written once. The election's ordering goes through the function slice 2 exported
  for it, `Reads.lowestId`; a second copy in the CLI layer is what the design forbids.
- Two writes that cannot be atomic are ordered so the failure window is safe. The election is
  append-only and never deleted, so a crash after it and before the authorization leaves a durable
  fact the next call reuses. The reverse order would leave an authorization naming an election that
  does not exist.

## Scope Boundaries
- Keep SDD lifecycle ownership separate from optional Governance enforcement.
- The bidirectional producer-versus-gate agreement leg against `scripts/check-claim-fence.py` belongs
  to `.github#2719`, which declares that file and `tests/claim-fence`; this work declares neither and
  must not write that leg.
- Arming the fence as a required status context is slice 8 (`.github#2723`) and is out of scope.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2395-merge-election-and-grounded-authorization`.
