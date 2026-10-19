---
name: intra-repo-parallel-work
description: Coordinate authorized concurrent workers with isolated worktrees, clear owners, disjoint touch-sets and overlap handling.
---

# Parallel work in one repository

Use [work-board](https://github.com/FS-GG/.github/blob/main/.agents/skills/work-board/SKILL.md) for scope, capacity and selected operations and
[pnext-item](../pnext-item/SKILL.md) for each admitted Delivery. Delegation must be authorized by the
runtime and useful; this skill does not require spawning workers.

Keep one accountable owner and isolated worktree/branch per coherent Delivery, with explicit actual
touch-set and one integrator for shared surfaces. Continue existing owners/PRs/attempts; do not create
a second assignment or demand reservation for the same work. Before launch reconcile active demand,
known/Unknown reservations, CPU/memory, hosted capacity and shared outputs. Free slots do not admit work.

Communicate overlap through the [active worker channel](https://github.com/FS-GG/.github/blob/main/docs/coordination/worker-channels.md).
Pause the affected edits when scope grows or conflicts; have owners/integrator reconcile the touch-set
before proceeding. Do not overwrite another worker's files or infer release from silence/lease expiry.
An expired original operation or killed parent retains its custody and Unknown effects until observed.
Same-owner repair, independent exact-candidate review and normal delivery do not require routine worker
minting, claims, heartbeat/done stamps, packets or phase chains.

Only explicitly selected heavyweight scope or a separately qualified legacy PRODUCT route loads
[legacy protocol facts](references/protocol-facts.md), [worktrees and overlap](references/worktrees-and-overlap.md)
and [deep detail](references/deep-detail.md) within that original authority. Those contracts preserve
substantive locks/custody already present; they do not activate organization V1 operations or change the
default route. Use [cross-repo coordination](../cross-repo-coordination/SKILL.md) for shared suppliers.
