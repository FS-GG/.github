# ADR-0090: Prepare v2 cutover for one accountable operator

- **Status:** Proposed
- **Date:** 2026-09-25
- **Decision owner:** FS-GG accountable programme owner
- **Affects:** GS2-10 readiness, protected cutover execution, and GS2-13.2 approval
- **Applies:** [ADR-0079](0079-single-accountable-delivery-authority.md) to remaining v2 delivery and critique
- **Activation:** No live environment, credential, accepted receipt, or epoch change

## Context

The programme must be executable by one operator with autonomous agents and protected CI. The
[authority audit](../coordination/2026-09-25-v2-single-operator-autonomy-audit.md) found that ordinary
source delivery already supports one owner, but the live `fleet-cutover` environment prevents that
owner from approving a run they initiated. It lists two eligible reviewers, has no administrator bypass,
and currently holds no secrets. Its existing workflow emits an initializer authorization receipt; it
does not implement all cutover transitions. The ordinary-v2 credential route cannot substitute for a
cutover or administrative executor.

[ADR-0087](0087-single-owner-v1-admission-genesis-approval.md) removed the second-person requirement for
v1 admission genesis only. [ADR-0088](0088-ci-owned-unattended-credential-execution.md) made ordinary-v2
execution unattended while expressly preserving protected `OpenV2` approval. Neither decision supplies
the remaining cutover authority.

## Decision

The following profile is proposed and has no live authority until its acceptance requirements pass.

Use one accountable operator for execution, independent critique passes, evidence assessment, and
operational ownership. Preserve independently authored black-box tests, fresh provider readback, exact
candidate qualification, and any machine-enforced identity contract. A reviewer agent sharing the
operator's credentials is not a second security principal.

Prepare a dedicated `fleet-cutover-owner` profile for this programme. Its sole required reviewer is the
accountable human owner, GitHub user ID `1645484`; self-review is permitted, administrator bypass is
disabled, and only exact `main` is deployable. A five-minute wait precedes execution. The existing shared
`fleet-cutover` environment is not changed by this proposal. The candidate must explicitly bind which
environment authorizes each phase, and update every validator, workflow, custody binding, and monitoring
expectation that currently names the shared environment before the new profile can be installed.

The same human may dispatch and approve a protected run. GS2-13.2 still requires that human's native,
run-bound confirmation of the exact `VerifiedV2` candidate and irreversible `OpenV2` intent. It requires
no distinct second human under the proposed profile. A bot dispatch, agent using the owner's token,
chat instruction, earlier readiness decision, or approval for another run cannot supply that
confirmation. Until this profile is accepted, installed, and qualified, the existing live restrictions
continue to apply.

Automate preparation, checks, effects, readback, recovery, and reporting through reviewed protected
workflows and published Coordination interpreters. Keep secret-free qualification before credential
use. Cutover journal credentials remain distinct from ordinary settlement and administrative
credentials. Each operation binds the candidate, plan, targets, expected state, stable attempt identity,
expiry, and approval scope. Unknown outcomes reconcile from receipts; they do not acquire a new effect
identity by rerunning the workflow. An irreversible contraction executes only the exact independently
verified deletion plan after Q9/Q10 acceptance.

Before GS2-10 freeze, qualify the complete execution route in existing isolated resources. Account for
every manual action from preparation through `OperatingV2`, replace mechanical relays with workflow
steps, and retain an explicit disposition for protected human decisions or unavailable administration.
Additional repositories, Apps, hosts, or credentials are requested only after current installed
capabilities and safe reuse have been inspected and found insufficient. A token's absence in the agent
container alone does not establish a missing capability.

## Required acceptance evidence

- The governing design, roadmap, Coordination contracts, environment policy and operator documentation
  agree on one exact approval profile; source/live self-review drift is resolved prospectively.
- The authorized settings plan installs the profile, and fresh provider evidence binds its ID, sole
  reviewer, wait timer, branch policy, bypass prohibition, executor and credential scope.
- Native runs demonstrate same-owner dispatch and genuine human approval, then reject absent approval,
  wrong owner, wrong run or attempt, stale candidate, altered intent, expired approval, and policy drift.
- Isolated freeze/switch/verify/open/observation/contraction and pre-open rollback exercises prove every
  effect and interruption boundary. A sandbox result cannot authorize a production transition.
- GS2-10 binds the accepted policy and executable inventory; the full Q0–Q7 candidate qualification and
  later Q8–Q10 gates remain mandatory. Historical accepted receipts remain immutable.

## Consequences

One human can operate the programme without recruiting another reviewer. Automated execution remains
bounded by exact evidence and permissions. End-to-end unattended agent completion is still unavailable
at the protected human `OpenV2` decision, and any unavailable administrator grant remains an external
capability gap. The audit identifies these limits before the fleet freezes, when independent source
and qualification work can continue.
