# ADR-0079: One accountable owner authorizes delivery

- **Status:** Accepted
- **Date:** 2026-08-30
- **Decision owner:** delegated FS-GG migration owner
- **Affects:** FS-GG delivery skills, coordination review records, repository protection, and GitHub Substrate v2
- **Clarifies:** ADR-0078 review evidence and the historical `pnext-item` review procedure do not create additional authorizers

## Context

FS-GG already protects delivery with exact-head required checks, deterministic evidence, mutation controls,
formal models, immutable receipts, and post-write verification. Requiring additional people, accounts, or
agents to authorize the same candidate added queueing and role choreography without resolving the ordering
and concurrency failures those technical controls address. It also blurred the distinction between useful
critique evidence and authority to decide.

The GitHub Substrate v2 migration has one explicitly delegated owner authorized to make all necessary
decisions. Routine delivery must not manufacture another authorization boundary after that delegation.

## Decision

Every work item or migration has exactly one **Accountable Delivery Owner**. That owner decides whether the
candidate is accepted and may implement, critique, repair, and deliver it. No second human, GitHub account,
agent, critic, reviewer quorum, or external approval is required unless an external legal or third-party
control explicitly requires separation of duties. No such exception currently applies to FS-GG.

Required technical gates remain fail-closed. Exact-head CI, model checking, mutation/inversion controls,
content-addressed evidence, rulesets, permissions, and post-state verification are predicates the owner must
satisfy or explicitly redesign; they are not separate authorizers. A red required predicate blocks delivery.
An observation or review finding blocks only when the owner accepts it as material under the governing
requirements.

The structured review ledger remains an evidence and sequencing mechanism. When its wire contract needs
distinct implementer, critic, or host identities, the same accountable owner may mint distinct **phase
identities** and perform a fresh critique pass. Those identities prevent stale-record reuse and ambiguous
ordering; they do not represent different authorities. External review remains optional input.

Repository rulesets require zero native approvals. They require current technical checks, protected history,
and resolved delivery predicates. Standard skills must never stop merely because an independent agent or
external approver is unavailable.

After a second related late-stage defect, the owner pauses the repair loop for the standard deep-dive
analysis: map the affected architecture and invariants, search sibling paths for the same cause, update the
test/fault model, and then resume. The deep dive improves the decision evidence; it does not add an approval.

## Consequences

- Accountability is unambiguous and routine delivery has no reviewer-availability dependency.
- Formal, automated, and adversarial checks retain their strength because their results remain required
  evidence where declared.
- Phase identities preserve deterministic review generations without pretending they are organizational
  separation of duties.
- The owner carries the risk of a missed judgement. Exact-head evidence, deep-dive triggers, durable decision
  records, and rollback/roll-forward plans are the compensating controls.
