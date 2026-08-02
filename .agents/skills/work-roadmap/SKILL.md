---
name: work-roadmap
description: Use when explicitly asked to complete a markdown roadmap milestone by milestone. Run each in a fresh worker through SDD and independent critique, merge it, update the roadmap, and finish with a report.
---

# work-roadmap

Burn down a markdown roadmap milestone by milestone. The roadmap—not a project board—is the ledger.

1. Read the complete roadmap and select the next unchecked, dependency-ready milestone.
2. Spawn one fresh disposable worker from current default branch and give it only that milestone.
   During worker setup, interactive/game work must explicitly invoke the `pnext-item` performance-first
   planning gate before implementation begins. A milestone that ships or claims reachable game
   functionality must record `game_functionality: true` and a passing bot-driven headless player
   journey (`.github#2087`) — a bot driving the product through the same control messages a real
   player emits, booted at the product's real entry point, not seeded into a mid-game state. No such
   milestone may reach a `shipReady`-equivalent verdict without it.
3. Give the worker a stable feedback cycle id. It follows the repository's SDD lifecycle and item/PR
   merge discipline and invokes `fs-gg-feedback-report` at every required checkpoint boundary.
4. After the first green implementation/test/evidence loop, have the worker start one fresh independent
   critic. The critic reviews requirements, diff, tests, architecture, and roadmap evidence without
   editing the implementation. The worker repairs blocker/major findings, and the same critic confirms
   them before the worker updates the roadmap and lands the milestone. For a work-roadmap milestone,
   this milestone critique loop owns the review/repair count and supersedes `$pnext-item`'s normal
   three-round cap; all other applicable `$pnext-item` planning, review-evidence, exact-SHA, merge,
   release, and escalation discipline remains in force. Permit at most ten numbered
   repair/confirmation rounds. If round ten remains red, record the terminal human escalation, stop
   the milestone, and never start round eleven or merge.
5. Verify the merge, tests, roadmap checkbox/evidence, release obligations, critique artifact,
   checkpoint state, and schema-v2 report externally. Missing, invalid, or unreadable critique or
   feedback state fails closed.
6. Discard the worker and critic, refresh default branch, re-read the roadmap, and select again.
7. After no unchecked milestone remains, validate every completed cycle and land the final report with
   cross-cycle critique and feedback roll-ups; a report that omits a cycle, critique disposition, or
   checkpoint disposition cannot finish.

Milestones are sequential unless the roadmap explicitly establishes disjoint parallel milestones and
the user authorized parallel execution. Load [host-loop](references/host-loop.md) for shared
fresh-worker and verification rules and [roadmap-ledger](references/roadmap-ledger.md) for markdown
state transitions.
Load [feedback-contract](references/feedback-contract.md) for the worker activation, exact validation
commands, zero-event representation, host acceptance gate, and final roll-up contract.
Load [critique-contract](references/critique-contract.md) for critic isolation, severity and repair
rules, the artifact schema, exact validation command, and host acceptance gate.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
