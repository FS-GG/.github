---
schemaVersion: 1
workId: 2131-claim-to-done-lifecycle
title: Claim-to-Done Lifecycle and Guarded Landing
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2131-claim-to-done-lifecycle/spec.md
sourceClarifications: work/2131-claim-to-done-lifecycle/clarifications.md
sourceChecklist: work/2131-claim-to-done-lifecycle/checklist.md
publicOrToolFacingImpact: true
---

# Claim-to-Done Lifecycle and Guarded Landing Plan

Prose status: planned

## Source Snapshot
- spec: work/2131-claim-to-done-lifecycle/spec.md sha256:dd2fb4245cab5403d75057d4ab89f623b5b1a2c9c119596700c51fac726adba1 schemaVersion:1
- clarifications: work/2131-claim-to-done-lifecycle/clarifications.md sha256:9bf7b026c7dc9de94b47bdac273f6cfed5f44d76e520c75df3e5c30394085df9 schemaVersion:1
- checklist: work/2131-claim-to-done-lifecycle/checklist.md sha256:d22a5d0152651f1c9700024fb61c29afffd5c5298c4f4f463159ad72129f13ab schemaVersion:1

## Plan Scope
- Add a focused Core delivery-lifecycle module and signature, rather than expanding the
  existing driver module or adding another branch to `Client.fs`.
- Add an application boundary that composes current GitHub facts, the #2127 typed
  `Driver.ReviewChain`, and the Core transition decision; wire an additive CLI surface.
- Keep existing `claim`, `verify-paths`, `landable`, `done`, and `release` contracts intact.
- Update both pnext-item skill roots to delegate deterministic lifecycle gates to the new
  command and preserve human judgement for implementation and review materiality.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Model `Claimed`, `Implementation`, `ReviewReady`, `ReviewActive`, `Accepted`, `Landable`, `MergedAwaitingObligations`, `Done`, and terminal park states as a closed Core union. Derive exactly one action from a typed snapshot; missing or contradictory facts return a named no-verdict instead of guessing a stage.
- PD-002 [AC-001] [FR-002] complete: Bind every inspected snapshot to a freshness token containing the canonical item ref, live claim marker generation, executor identity, branch/worktree identity, PR number, head SHA, declared paths, and observed board state. A mutating request consumes that token and refuses if its facts no longer match a fresh read.
- PD-003 [AC-001] [FR-003] complete: The review-handoff action obtains the pull request and issue facts from the GitHub adapter and requires the canonical `item/<number>-*` branch, a canonical `Closes #<number>` reference, `verify-paths` success, and the current head before it can set `In review`.
- PD-004 [AC-001] [FR-004] complete: Reuse `Driver.ReviewChain` and its validator from #2127 as the sole typed review-chain input. Acceptance requires its valid marker, critic identity, ordered rounds, checks, host marker, and exact current head SHA; comment prose is never a substitute.
- PD-005 [AC-001] [FR-005] complete: The guarded land action refreshes the snapshot immediately before a single merge request and requires the current claim generation, accepted review token, unchanged head, green `landable` verdict, clean mergeability, and canonical closing linkage. The adapter recognizes an already-merged matching receipt as idempotent success and never retries an unknown or stale effect.
- PD-006 [AC-001] [FR-006] complete: Capture named release, publication, registry, dispatch, and deployment obligations before landing. After a matching merge receipt the lifecycle is `MergedAwaitingObligations` until every obligation has a durable verified receipt; the item remains claimed and visibly nonterminal.
- PD-007 [AC-001] [FR-007] complete: Completion re-reads default-branch reachability, issue closure, Done projection, obligation receipts, claim release state, pending-write queue, and cleanup eligibility. It emits `FSGG-DONE` only after all facts agree, otherwise the action identifies the first unmet fact.
- PD-008 [AC-001] [FR-008] complete: Every advance request carries the inspected freshness token and all GitHub mutations have deterministic action keys. Replays return the original matching receipt; stale generation or head data is refused before any new merge or done stamp.
- PD-009 [AC-001] [FR-009] complete: Terminal lifecycle actions explicitly return `cleanup-worktree`, `delete-item-branch`, or `route-follow-up` instructions. They are not executed as an implicit side effect of inspection or completion.

## Contract Impact
- PC-001 [PD-001] command report: Add a JSON-first `delivery` command family with inspect, review-handoff, accept, land, record-obligation, complete, and terminal-action projections. Its JSON includes lifecycle state, exact freshness token subject, one next action, and typed no-verdict/refusal details. Existing CLI verbs and their exit contracts remain byte-compatible.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Add Core, CLI, and GitHub regression cases for a clean no-obligation delivery, a release-obligation delivery, changed head after acceptance, missing closing linkage, stale claim generation, merge success with pending obligations, and cleanup refusal before Done. Exercise the CLI command surface against the controlled GitHub adapter fixture. The fixture is synthetic because a test must not merge a live PR; disclose that limitation in the test/PR evidence and use the production adapter's same command path.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing in-flight claims continue under the legacy verb sequence. The new lifecycle surface is opt-in until hosts consume its receipt; legacy data that lacks a freshness token can be inspected but cannot be advanced through a guarded mutation.

## Generated View Impact
- GV-001 [PD-001] workModel: Generate the SDD work model and readiness receipts for this item. Update `.agents/skills/pnext-item` and `.claude/skills/pnext-item` equivalently; run `scripts/repos.sh relock` and treat `registry/repos.lock` as generated expected drift rather than declaring it in Paths.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The current #1858 executor/claim-generation design remains an external dependency for any stronger cross-context ownership guarantee. This slice fences and checks the generation available from the live claim and fails closed when it cannot establish one.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2131-claim-to-done-lifecycle`.
