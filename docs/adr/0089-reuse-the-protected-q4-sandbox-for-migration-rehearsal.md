# ADR-0089: Reuse the protected Q4 sandbox for migration rehearsal

- **Status:** Accepted
- **Date:** 2026-09-25
- **Affects:** `.github` qualification workflow; `FS.GG.Coordination` migration rehearsal

## Context

GS2-09.7 called for separately provisioned repository and Project copies plus scoped credentials. That
would require a maintainer to prepare a second isolated cohort before the migration code could be exercised.
The [GS2-04.9 Q4 workflow](../../.github/workflows/github-substrate-v2-sandbox-qualification.yml) already
owns a protected non-production App credential and a private sandbox repository and Project. A fresh
[sandbox run](https://github.com/FS-GG/.github/actions/runs/36086215835) against Coordination commit
`387585f5e3d9556dd67a56746c50c3b48306efdc` verified the App actor, repository node
`R_kgDOUKXpqQ`, Project 2 node `PVT_kwDOEYAWY84BiESo`, eight live effects, complete cleanup, and zero
residue. It did not execute migration.

## Decision

The protected `.github` workflow provisions each GS2-09.7 rehearsal fixture inside that registered sandbox.
It mints the existing App token, pins exact actor and target identities before every effect, seeds a
nonce-owned representative cohort from the frozen migration corpus, and retains source, effect, rollback,
archive, and cleanup evidence. `FS.GG.Coordination` owns the migration interpreter and independent controls;
it receives the token only inside the protected run. The live Coordination Project and production Authority
journal are never mutation targets. The [governing design](../coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md#registered-migration-rehearsal-cohort)
defines the exact guard and evidence contract.

No new maintainer-provisioned Project copy or credential is required for GS2-09.7. The full Q5/Q6 acceptance
requirements remain: complete discovery, exact manifest, interruption and recovery, archive, rollback,
rerun, independent omission controls, and provider readback. The existing Q4 run proves that the route is
available; it is not a GS2-09.7 receipt.

On 2026-09-25 the sandbox environment was restricted to exact deployment branch `main` (environment
`20974131908`, sole custom policy `60973171`). Fresh [Q4 run 36090885744](https://github.com/FS-GG/.github/actions/runs/36090885744)
passed under that policy against Coordination `eabd2760d60c5e74e08a492daf946f0efd542b55`, with
`cleanup.disposition=complete` and `residualCount=0`. This is installed sandbox-custody evidence, not a
migration acceptance receipt. The App secret remains at its existing organization scope; the environment
policy governs jobs that reference it and is not a claim of exclusive secret custody.
The [feature-branch dispatch 36091479649](https://github.com/FS-GG/.github/actions/runs/36091479649)
was rejected by the environment branch rule before any job step ran.

## Consequences

The App's organization Projects write grant reaches beyond Project 2, so the workflow and interpreter must
both enforce the exact Project node before mutation. The sandbox repository cannot stand in for the
production Authority repository's protected journal rules; its Git ref exercises CAS and recovery while
GS2-08.2 supplies separate production protection evidence. Any target drift, missing grant, incomplete
seed, cleanup residue, or uncovered migration authority refuses GS2-09.7. GS2-10 readiness and `OpenV2`
keep their independent approvals.

## Alternatives considered

- Ask a maintainer to provision fresh copies and credentials: duplicates an existing protected sandbox
  route and delays the work without adding an authority needed for this bounded rehearsal.
- Use the live Coordination Project or production Authority journal for test writes: would put production
  state at risk and is rejected.
- Replace the live rehearsal with local fixtures alone: loses provider and cleanup correspondence and is
  rejected.
