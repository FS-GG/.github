---
name: work-roadmap
description: Use when explicitly asked to complete a markdown roadmap milestone by milestone. Run each milestone in a fresh disposable worker through the SDD lifecycle, merge it, update the roadmap, and finish with a report.
---

# work-roadmap

Burn down a markdown roadmap milestone by milestone. The roadmap—not a project board—is the ledger.

1. Read the complete roadmap and select the next unchecked, dependency-ready milestone.
2. Spawn one fresh disposable worker from current default branch and give it only that milestone.
   During worker setup, interactive/game work must explicitly invoke the [performance-first planning
   gate](../pnext-item/references/performance-first.md) before implementation begins.
3. Give the worker a stable feedback cycle id. It follows the repository's SDD lifecycle and item/PR
   merge discipline, invokes `fs-gg-feedback-report` at every required checkpoint boundary, updates the
   roadmap with evidence, and lands the milestone.
4. Verify the merge, tests, roadmap checkbox/evidence, release obligations, checkpoint state, and
   schema-v2 report externally. Missing, invalid, or unreadable feedback state fails closed.
5. Discard the worker, refresh default branch, re-read the roadmap, and select again.
6. After no unchecked milestone remains, validate every completed cycle and land the final report with
   a cross-cycle feedback roll-up; a report that omits a cycle or checkpoint disposition cannot finish.

Milestones are sequential unless the roadmap explicitly establishes disjoint parallel milestones and
the user authorized parallel execution. Load [host-loop](references/host-loop.md) for shared
fresh-worker and verification rules and [roadmap-ledger](references/roadmap-ledger.md) for markdown
state transitions.
Load [feedback-contract](references/feedback-contract.md) for the worker activation, exact validation
commands, zero-event representation, host acceptance gate, and final roll-up contract.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
