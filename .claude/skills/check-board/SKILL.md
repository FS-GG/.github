---
name: check-board
description: Use when the FS-GG Coordination board looks stale, blockers may have cleared, or planning needs ground truth. Reconcile project fields against live issues and surface judgement calls.
---

# check-board (FS-GG)

The board is a planning projection; native issues, owning plans and actual delivery evidence remain
authority. Select the organization V2 or explicitly historical/product scope before running a pass.

## Selected organization V2 scope

Read [organization-v2-inspection](references/organization-v2-inspection.md). Its root adoption gate is
pending until actual four-target qualification and selected consumer readback. Source/manifest changes
do not activate it. A missing or failed V2 read reports Unknown and preserves current source work.

After root selects adoption, use only the bound read-only inspection for this organization scope.
Report exact selected population and read coverage, source/native/dependency/plan gaps, human planning
values beside Observation, and unresolved acceptance or owner disagreements. Do not reconcile, apply,
flush, auto-repair or claim these V2 items. Before any resulting dispatch, the current integrator still
checks intake authorization, actual PRs, touch sets, available capacity and owning-plan prerequisites.

## Separately selected historical or product scope

The commands below retain their existing legacy/product binding. They are not a V2 fallback.
`fsgg:claim` markers retain meaning only for the selected legacy lifecycle. Reconcile that scope before
legacy planning and after its worker changes its board.

### Run the legacy/product pass

```bash
scripts/fsgg-coord budget
scripts/fsgg-coord reconcile --json
scripts/fsgg-coord lint --json
```

`reconcile` is always fresh and is a dry-run unless `--apply` is present. Its findings are the typed
mechanical chores the engine can prove and safely repair. Review the JSON, then:

```bash
scripts/fsgg-coord reconcile --apply
scripts/fsgg-coord flush
scripts/fsgg-coord reconcile --json
scripts/fsgg-coord lint --json
```

Never translate a failed read into an empty board. `reconcile --apply` reports a repair as applied only when its own fresh post-mutation scan observes every intended field value; an accepted mutation with a stale or missing row is failed, not clean. Exit 75 means back off until the reported reset.
A queued write is not landed until `flush` and the final fresh pass confirm it.
Run the bounded executable positive, partial-projection, and missing-row receipt examples in
[mechanical reconciliation](references/mechanical-reconciliation.md#executable-receipt-examples) when
validating automation that consumes the apply document.

### Legacy/product judgement boundary

`lint` findings are report-only. Investigate unreadable/unparseable blockers, undeclared paths,
unclaimed `In progress` items, open issues in `Done`, and epic roll-up questions. Do not change issue
bodies, close work, invent dependencies, or decide an epic from the reconcile pass. File or surface the
decision with evidence.

For the typed rule set, wire vocabulary, output schema, and manual investigation recipes, load
[mechanical-reconciliation](references/mechanical-reconciliation.md). For epic and decision handling,
load [judgement-findings](references/judgement-findings.md).
For rare edge cases, REST recipes, and the incident rationale behind the boundaries, load
[deep detail](references/deep-detail.md).

### Finish the legacy/product pass

Report mechanical changes, queued/failed writes, judgement findings, and the fresh post-apply result.
If planning follows, use only that final result.
