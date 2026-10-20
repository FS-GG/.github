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

At the start of an unrelated CI/runtime wait, begin known useful intake/enrollment and disjoint
source/design preparation within their own authority and remaining capacity. Gate each actual operation
and its transitive resource/effect demand rather than freezing an entire Delivery for a later expensive
step. Protected enrollment still precedes board-selected implementation; a free worker or prepared
source grants no enrollment or runtime authority.

Derive admission, reservation and request identity from the same exact typed `sliceId`, attempt,
operation, owner, PR/head and actual paths/touch-set. Preserve existing relevant known/Unknown holds;
a similar identifier, expired observation or absent owner is no release. Apply the
[final-input invocation guidance](https://github.com/FS-GG/.github/blob/main/.agents/skills/work-board/references/delivery-reliability.md#final-inputs-and-failure-gated-invocation)
before launch without introducing a new packet, ledger or scheduler.

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
