---
name: check-board
description: Use when the FS-GG Coordination board looks stale, blockers may have cleared, or planning needs ground truth. Reconcile project fields against live issues and surface judgement calls.
---

# check-board (FS-GG)

The board is a planning projection; native issues, owning plans and actual delivery evidence remain
authority. Select the current organization V2 or independently qualified product scope before a pass.
Historical organization examples are data only; selecting history cannot authorize live operations.

## Selected organization V2 scope

Read [organization-v2-inspection](references/organization-v2-inspection.md) and resolve the actual
qualified installed caller, selected authenticated binding and admitted membership/views. The accepted
organization baseline uses binding5/schema3/inspection3/integrator2; retained binding4 and product3
contracts keep their actual qualified behavior. Source preparation, publication and installed adoption
remain distinct; refresh operational truth before action. No draft field IDs or historical report
activates a new route. Do not revive manual v3 current-status upkeep for an accepted cutover.

After accepted coordinated cutover, report complete selected/historical/deferred/unexpected/unresolved
membership, scoped read gaps, current native issue/owner/plan/PR/head evidence and per-slice ordered
selected/deferred decisions. Keep human Status/Track/Roadmap beside Observation; never infer Ready or
Done from healthy reads or merge alone. Do not reconcile/apply/flush, auto-repair or claim V2 items.
Inspection grants no dispatch. The existing integrator verifies genuine action/prerequisite/intake/
capacity/reservation suppliers and refreshes determining facts beside actual owner execution. Missing
or failed V2 reads preserve Unknown and independent authorized issue/plan/PR work, with no V1 fallback.

## Selected outcome/delivery successor

The [organization contract](references/organization-v2-inspection.md#outcomedelivery-successor-source-contract--2026-10-18)
also defines binding5/schema3/inspection3/integrator2 with explicit Work level and bounded
hierarchy/closure metadata. Require its separately qualified caller and selected binding before use;
retained binding4 and product3 do not acquire these semantics from new instructions. Canonical
[work-board](https://github.com/FS-GG/.github/blob/main/.agents/skills/work-board/SKILL.md) owns action
selection and closure. Inspection stays read-only; Unknown metadata remains visible, not fallback.

## Separately qualified product scope

Product bindingVersion3/schemaVersion1 keeps its qualified inspection/1 contract and five-status
planning schema. Use the actual authenticated product binding and installed caller; never substitute
the organization binding or add the organization-only integrator stdin flag:

```sh
fsgg-coord-engine board-v2 inspect --binding-file product-binding.json --report-file new-product-inspection.json
```

Inspection remains read-only and grants no writes, selection or operation authority. Local-only workspaces
retain their existing route. This organization preparation changes no product source or acceptance.

### Independently qualified legacy-product reconciliation

The legacy recipes below apply only to a separately qualified legacy-product target and lifecycle.
Before any product command, verify the actual product target/binding and configured product repository;
a repository filter alone does not select a board. Refuse missing or organization-default target facts.
No historical organization live opt-in exists. These recipes cannot repair unavailable V2 evidence or
select Project 1, including when a caller requests a historical organization scope.

For that verified product target, replace `<configured-product-repo>` with its actual repository:

```bash
scripts/fsgg-coord reconcile --repo <configured-product-repo> --json
scripts/fsgg-coord lint --repo <configured-product-repo> --json
```

`reconcile` is fresh and dry-run unless `--apply` is present. Review its typed mechanical findings and
the product's existing write authority before the scoped apply; this skill grants no new effect:

```bash
scripts/fsgg-coord reconcile --repo <configured-product-repo> --apply
scripts/fsgg-coord reconcile --repo <configured-product-repo> --json
scripts/fsgg-coord lint --repo <configured-product-repo> --json
```

A repair is applied only after its fresh post-mutation scan observes the intended fields. Failed or
stale readback remains failed, never clean. Exit75 retains the reported backoff. Queued writes are not
landed: queue-wide replay has no repository filter and needs its separately admitted original-effect
recovery route and genuine target readback. No replay command is authorized by this check-board pass.
The [offline receipt examples](references/mechanical-reconciliation.md#executable-receipt-examples)
retain their independently qualified meaning and perform no live writes.

`lint` findings are report-only. Investigate unknown blockers, undeclared paths, unclaimed work and
outcome/child disagreements without rewriting issues, inventing dependencies or closing work. Report
product-scoped mechanical results, queued/failed writes, judgement findings and the final fresh result.
Use [mechanical reconciliation](references/mechanical-reconciliation.md) and
[judgement findings](references/judgement-findings.md) within that product boundary.

## Historical organization examples are data only

[Deep detail](references/deep-detail.md) retains incident and protocol evidence. Organization
reconcile/apply/flush, claim/done, Class/Phase and scheduling-ledger recipes there are historical data,
not a live opt-in or fallback. Product live use requires the verified product scope above; archival
examples alone grant no current operation, enrollment, retry or field-write permission.
