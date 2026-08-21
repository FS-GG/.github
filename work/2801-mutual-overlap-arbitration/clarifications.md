---
schemaVersion: 1
workId: 2801-mutual-overlap-arbitration
title: Automatic mutual-overlap arbitration
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2801-mutual-overlap-arbitration/spec.md
publicOrToolFacingImpact: true
---

# Automatic mutual-overlap arbitration Clarifications

## Source Specification
- work/2801-mutual-overlap-arbitration/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: Which existing public model owns the narrow wait/arbitration types?
- CQ-002 [AMB:AMB-002] blocking answered: Which stable identity makes automatic room creation retry-idempotent after an ambiguous transport failure?
- CQ-003 [AMB:AMB-003] blocking answered: How are freeze, precedence application, and loser resume represented without a second claim authority?
- CQ-004 [AMB:AMB-003] blocking answered: How does an external repository avoid competing with the live Coordination-board orchestrator?

## Answers
- CQ-001 [AMB:AMB-001] decision: Put the versioned receipt and closed arbitration outcome types in the existing `Client` contract, with GitHub mutation effects in `Writes`; do not add a generic Core module or widen beyond declared source files.
- CQ-002 [AMB:AMB-002] decision: Canonicalize the two item/generation participants and shared-token set, hash that cycle identity, and use it to rediscover/reuse the single ADR-0051 room and its two back-references after any retry.
- CQ-003 [AMB:AMB-003] decision: Live claim markers remain the sole ownership authority. The arbitration outcome is a receipt-bound transition: freeze is refusal to edit only the shared tokens, apply replaces the loser's declared paths while its marker remains, and resume requires new observed facts before an explicit re-widen.
- CQ-004 [AMB:AMB-003] decision: Store immutable, expiring board-orchestrator lease generations and generation-bound request receipts on one authority issue. A live lease forces routing; absence/expiry permits only the next generation, elected by lowest comment id.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: Use additive, versioned records and discriminated unions in `Client.fs/.fsi`; keep I/O in `Writes.fs/.fsi` and pure validation/detection independent of comment order.
- DEC-002 [CQ-002] [AMB:AMB-002]: The room idempotency key is the digest of the canonical participant claim generations plus canonical shared tokens. A retry first searches complete live room/back-reference state; unreadable or conflicting matches fail closed.
- DEC-003 [CQ-003] [AMB:AMB-003]: A precedence receipt is revisioned and digest-linked. Revision 1 has no prior digest; later revisions must name the exact prior digest and measured reversal reason. Apply/resume outcomes carry observed claim/path/head facts and requested next effects.
- DEC-004 [CQ-003] [AMB:AMB-003]: Narrowing the loser is a claim-preserving path replacement. Resumption is a separate guarded transition authorized only after observed winner landing, updated base/head, clean overlap, explicit re-widen, and required review evidence.
- DEC-005 [CQ-004] [AMB:AMB-003]: A complete lease/request census is the sole board-orchestrator authority. External requests are idempotent and bound to its live generation; stale generation races refuse. Same-generation contenders use GitHub comment order and losers delete only their own marker.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2801-mutual-overlap-arbitration`.
