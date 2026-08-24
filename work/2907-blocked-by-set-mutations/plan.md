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
- spec: work/2907-blocked-by-set-mutations/spec.md sha256:bbfc8ac46a0af4e5487e6e08af4603eb9e97d26877c78774d4f96d892930802f schemaVersion:1
- clarifications: work/2907-blocked-by-set-mutations/clarifications.md sha256:29371de9aa66598f033214d6fd42665dac6d53189f32f572cdd1327bb6537bc5 schemaVersion:1
- checklist: work/2907-blocked-by-set-mutations/checklist.md sha256:2799d51aeaf8b672cd72d857c78bd907426e73a20a8b7c8cda8ea1ad2c71fe95 schemaVersion:1

## Plan Scope
- Work item 2907-blocked-by-set-mutations is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 4.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Extend `Options` with mutually exclusive `--add`, `--remove`, `--replace`, and `--clear` intents scoped to `set-field`; in `Handlers.setField`, read and canonicalize the live set, union requested additions in stable canonical order, and issue the derived write through the guarded boundary.
- PD-002 [AC-002] [FR-002] complete: Subtract canonical requested refs from the observed set while retaining every other edge; select `Board.Clear` only when subtraction produces an empty set. Removing an absent edge remains an idempotent guarded write outcome, not replacement of the whole set.
- PD-003 [AC-001] [AC-002] [AC-003] [FR-003] complete: Add `Board.boardWriteGuarded` beside the existing chokepoint. It resolves the item, re-reads `{Value; Revision}`, refuses any mismatch as stale, emits the derived single-field mutation only after a match, and never places guarded writes on the unconditional deferred queue.
- PD-004 [AC-004] [FR-004] complete: For `Blocked by`, refuse the legacy positional-value form and require one explicit intent. `--replace` uses canonical replacement and `--clear` maps directly to `Board.Clear`; scalar fields retain their positional value/empty-clear behavior and batch `Field=Value` remains explicitly replacement-shaped.
- PD-005 [AC-005] [FR-005] complete: Put the deterministic body/field comparison in `LintApplication`, expose it through the existing compatibility seam, and invoke it during the fresh lint census. The body is projection-only: the rule reports inert, divergent, or invalid text and never creates an edge or a reconcile chore.
- PD-006 [AC-006] [FR-006] complete: Add parser/residue and command-handler tests, Board transport tests with independently controlled observations, and lint verdict plus application-route tests. Capture four bounded inversions and then run BoardOps, GitHub, CLI, full solution, formatting, projections, and SDD analyze gates.

## Contract Impact
- PC-001 [PD-001] [PD-004] command report: `set-field` advertises four explicit `Blocked by` mutation intents. Existing bare `Blocked by` replacement is intentionally refused with migration guidance; unrelated scalar writes and batch equality syntax remain compatible. `lint` adds stable `BLOCKED-BY-BODY-INERT` diagnostics without changing JSON schema.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PC-001] semanticTest: Run focused parser, BoardOps, GitHub Board, and lint application tests; demonstrate red/green inversions for union preservation, subtraction preservation, stale refusal, and lint comparison; then run all affected projects, the solution, formatting, signature/projection generation checks, and `fsgg-sdd analyze`.

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
