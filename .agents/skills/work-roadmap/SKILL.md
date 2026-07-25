---
name: work-roadmap
description: Use when explicitly asked to complete a markdown roadmap milestone by milestone. Run each milestone in a fresh disposable worker through the SDD lifecycle, merge it, update the roadmap, and finish with a report.
---

# work-roadmap

Burn down a markdown roadmap milestone by milestone. The roadmap—not a project board—is the ledger.

1. Read the complete roadmap and select the next unchecked, dependency-ready milestone.
2. Spawn one fresh disposable worker from current default branch and give it only that milestone.
3. The worker follows the repository's SDD lifecycle and item/PR merge discipline, updates the roadmap
   with evidence, and lands the milestone.
4. Verify the merge, tests, roadmap checkbox/evidence, release obligations, and feedback externally.
5. Discard the worker, refresh default branch, re-read the roadmap, and select again.
6. After no unchecked milestone remains, create and land the final report required by the roadmap.

Milestones are sequential unless the roadmap explicitly establishes disjoint parallel milestones and
the user authorized parallel execution. Load [host-loop](references/host-loop.md) for shared
fresh-worker and verification rules and [roadmap-ledger](references/roadmap-ledger.md) for markdown
state transitions.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
