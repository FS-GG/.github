---
schemaVersion: 1
workId: 2905-post-merge-verification
title: Post-merge verification before terminal delivery completion
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

# Post-merge verification before terminal delivery completion Charter

## Identity
- Work id: `2905-post-merge-verification`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Treat a successful merge and verified execution on the protected default branch as separate facts.
- Bind post-merge evidence to the exact merge SHA and default branch; PR-head success is never a substitute.
- Fail closed on absent, red, pending, malformed, or unreadable post-merge evidence while preserving a recoverable delivery state.
- Keep the completion saga resumable and idempotent: no terminal receipt, issue closure, Done projection, or claim release precedes verification.
- Ship the new gate with a discriminating inversion proving PR-green alone cannot complete delivery.

## Scope Boundaries
- Change the delivery model, live GitHub evidence adapter, completion transaction, and focused tests only.
- Reuse the existing merge/obligation saga and exact-SHA check-scoring semantics; do not weaken guarded landing or review acceptance.
- Do not infer success from issue state, board state, PR checks, reachability, an empty run set, or a failed read.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2905-post-merge-verification`.
