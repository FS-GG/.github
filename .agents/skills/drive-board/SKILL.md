---
name: drive-board
description: Use when explicitly asked to burn down the org-wide FS-GG Coordination board. Reconcile first, fan out disposable repo workers within safe lanes, verify results, and re-plan until empty.
---

# drive-board (FS-GG)

Burn down the org-wide Coordination board across repositories. The board is the ledger; this skill owns
cross-repo allocation, not item implementation.

1. Run [check-board](../check-board/SKILL.md), apply mechanical repairs, and surface judgement blockers.
2. Read typed lanes and active claims; choose bounded per-repo concurrency that respects touch-sets and
   available agent slots.
3. Spawn fresh disposable workers with fresh identities/worktrees. Each runs exactly one
   [pnext-item](../pnext-item/SKILL.md) loop in its assigned repo.
4. Verify each worker's PR, merge, publication/registry obligations, exact done stamp, released claim,
   and follow-up items against GitHub—not its narrative.
5. Despawn completed workers, reconcile again, and allocate the newly exposed lanes.
6. Stop only when a fresh org-wide reconcile has no startable item, live claim, unresolved mechanical
   repair, queued write, or actionable follow-up. Distinguish a genuinely empty board from blocked work.

Load [host-loop](references/host-loop.md) for the shared concurrency, verification, and termination
contract. Load [org-scope](references/org-scope.md) for the ledger/scope rules unique to this driver.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
