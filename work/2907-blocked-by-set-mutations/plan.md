---
schemaVersion: 1
workId: 2907-blocked-by-set-mutations
title: Blocked By Set Mutations
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2907-blocked-by-set-mutations/spec.md
sourceClarifications: work/2907-blocked-by-set-mutations/clarifications.md
sourceChecklist: work/2907-blocked-by-set-mutations/checklist.md
publicOrToolFacingImpact: true
---

# Blocked By Set Mutations Plan

Prose status: planned

## Source Snapshot
- spec: work/2907-blocked-by-set-mutations/spec.md sha256:0c67131cd75f9737152d3f9ff3318365c8ad48644e84fb2634238552972e9421 schemaVersion:1
- clarifications: work/2907-blocked-by-set-mutations/clarifications.md sha256:115ac7f1746e8903a61e362007d2e4e4270c4ecd32383a4576134cfb1eaa0861 schemaVersion:1
- checklist: work/2907-blocked-by-set-mutations/checklist.md sha256:2799d51aeaf8b672cd72d857c78bd907426e73a20a8b7c8cda8ea1ad2c71fe95 schemaVersion:1

## Plan Scope
- Work item 2907-blocked-by-set-mutations is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 4.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Extend `Options` with mutually exclusive `--add`, `--remove`, `--replace`, and `--clear` intents scoped to `set-field`; in `Handlers.setField`, read and canonicalize the live set, union requested additions in stable canonical order, and issue the derived write through the guarded boundary.
- PD-002 [AC-002] [FR-002] complete: Subtract canonical requested refs from the observed set while retaining every other edge; select `Board.Clear` only when subtraction produces an empty set. Removing an absent edge remains an idempotent guarded write outcome, not replacement of the whole set.
- PD-003 [AC-001] [AC-002] [AC-003] [FR-003] complete: Route every authoritative `Blocked by` writer through `Writes.withBlockedByMutationLease`, acquired before observation and held through mutation. Explicit set-field intents, `set-field --batch`, `release --blocked-by`, intake, and reconcile all share that server-ordered lease. Keep `Board.boardWriteGuarded` beside the existing chokepoint as a secondary stale detector inside the lease: it re-reads `{Value; Revision}`, refuses mismatch, and never defers guarded writes, but it is not described as compare-and-set.
- PD-004 [AC-004] [FR-004] complete: For `Blocked by`, refuse the legacy positional-value form and require one explicit intent. `--replace` uses canonical replacement and `--clear` maps directly to `Board.Clear`; scalar fields retain their positional value/empty-clear behavior. Batch `Field=Value` remains explicitly replacement-shaped, while batch, release, intake, and reconcile join the same lease as derived mutations.
- PD-005 [AC-005] [FR-005] complete: Put the deterministic body/field comparison in `LintApplication`, expose it through the existing compatibility seam, and invoke it during the fresh lint census. The body is projection-only: the rule reports inert, divergent, or invalid text and never creates an edge or a reconcile chore.
- PD-006 [AC-006] [FR-006] complete: Add parser/residue and command-handler tests, Board transport tests with independently controlled observations and issue-comment leases, and lint verdict plus application-route tests. Add a discriminating interleaving control where a lower-ID lease contender fences `set-field --batch`, assert `release --blocked-by` posts the same lease, capture bounded inversions including an authoritative-writer lease bypass, and then run BoardOps, GitHub, CLI, full solution, formatting, projections, and SDD gates.

## Contract Impact
- PC-001 [PD-001] [PD-004] command report: `set-field` advertises four explicit `Blocked by` mutation intents. Existing bare `Blocked by` replacement is intentionally refused with migration guidance; unrelated scalar writes and batch equality syntax remain compatible. `lint` adds stable `BLOCKED-BY-BODY-INERT` diagnostics without changing JSON schema.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PC-001] semanticTest: Run focused parser, BoardOps, GitHub Board, and lint application tests; demonstrate red/green inversions for union preservation, subtraction preservation, authoritative-writer lease bypass, stale refusal, and lint comparison; prove batch/release use the shared lease and a losing contender emits zero mutations; then run all affected projects, the solution, formatting, signature/projection generation checks, and the SDD evidence/analyze/verify/ship fixed point.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatible: Scalar and batch callers need no migration. Single-field `Blocked by` callers replace the bare positional value with `--replace`, use `--add`/`--remove` for edge-local intent, and use `--clear` for clearing; the refusal prints these exact remedies.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate `readiness/2907-blocked-by-set-mutations/work-model.json` and `analysis.json` only through `fsgg-sdd tasks`/`analyze`; regenerate checked-in command/protocol projections through the repository generator if its check reports drift.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The lint application boundary lives in `src/FS.GG.Coord.Cli/LintApplication.{fs,fsi}` and its invocation/compatibility surface in `Client.{fs,fsi}`, which were absent from the filed touch-set. Widen those exact paths before editing and stop on overlap.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2907-blocked-by-set-mutations`.
