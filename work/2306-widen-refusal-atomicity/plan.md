---
schemaVersion: 1
workId: 2306-widen-refusal-atomicity
title: Widen Refusal Atomicity
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2306-widen-refusal-atomicity/spec.md
sourceClarifications: work/2306-widen-refusal-atomicity/clarifications.md
sourceChecklist: work/2306-widen-refusal-atomicity/checklist.md
publicOrToolFacingImpact: true
---

# Widen Refusal Atomicity Plan

Prose status: planned

## Source Snapshot
- spec: work/2306-widen-refusal-atomicity/spec.md sha256:4d32482d01c55a4c59651a4c7a34601e9665b70de17537107cc3761dd64b8d12 schemaVersion:1
- clarifications: work/2306-widen-refusal-atomicity/clarifications.md sha256:37740c903381ca67ae5e733c48bcef02509b6eb1d416b76f35ee0ec1a6abd48f schemaVersion:1
- checklist: work/2306-widen-refusal-atomicity/checklist.md sha256:1e7b37ff5664d65a28750e77c04ac46b8397cf82fa716cc4b6303d960c74fcac schemaVersion:1

## Plan Scope
- Work item 2306-widen-refusal-atomicity is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: ROOT CAUSE, established by reading `Client.fs`'s `updateTouchSet` (the shared implementation behind both `widen` and `set-paths`, ~line 5473). `activeCollisions` runs BEFORE the write and its `Error` path already refuses cleanly (line 5586, `| Error e -> fail e`) — this is the half `Writes.fsi`'s own header comment (line 26) correctly describes as "GATES the PATCH". But the `Ok collisions` path calls `Writes.widen ctx.Transport held rewritten` (line 5588) UNCONDITIONALLY, before ever inspecting whether `collisions` is empty — the emptiness check happens only afterward, to choose which message/exit-code to print. So a scan that SUCCEEDS and finds a real collision still lands the PATCH; only an UNREADABLE scan (the `Error` arm) refuses. The doc comment at lines 5570-5577 states this as the current design, in the implementer's own words — the write is gated on "did the scan complete", not on "did the scan find zero collisions". `Writes.fsi`'s own claim overstates what the call site does today.
- PD-002 [AC-001] [AC-002] [AC-005] [FR-001] [FR-002] [FR-005] complete: FIX. Move the `Writes.widen` call inside the `match collisions with | [] -> ...` arm (line ~5628) so the PATCH executes only when `collisions` is empty; the non-empty arm reports OVERLAP, still runs the existing notify loop and eprint diagnostics unchanged, and exits `ExitContended` (6) exactly as before, but performs no write. Because the current code already writes the WHOLE merged declaration in one PATCH (there is no partial/subset commit path today), gating that one call on emptiness is sufficient for AC-002's all-or-nothing guarantee too: a multi-path request with one colliding token never reaches the PATCH at all, so none of its tokens land — not just the colliding one.
- PD-003 [AC-002] [FR-002] complete: PUBLISHED DECISION SURFACE. Add `TouchSet.UpdateDecision` (`CommitUpdate` / `RefuseUpdate`) and `TouchSet.decideUpdate: hasCollision: bool -> UpdateDecision` to `TouchSet.fs`/`.fsi`, with a doc comment stating the all-or-nothing contract explicitly (mirroring the module's own established idiom for `usability`'s ANY-not-every rule, which the file's header cites as the pattern this codebase wants for a load-bearing threshold). `Client.updateTouchSet` calls this function instead of inlining the `if List.isEmpty collisions` test, so the decision is testable and documented in the ONE place the module's own conventions say it belongs, and `TouchSet.fsi` — the published, receiver-pinned surface named in the delivery-route rationale — carries the contract rather than leaving it implicit in a CLI call site.
- PD-004 [AC-003] [FR-003] complete: `set-paths` (`Replace`) and `widen` (`Union`) are ALREADY the same `updateTouchSet` function (line 5717/5719: `let widen … = updateTouchSet Union …` / `let setPaths … = updateTouchSet Replace …`), so PD-002's fix applies to both by construction — there is no second call site to separately patch. AC-003 is satisfied by this shared path, recorded explicitly rather than left to be inferred: a `WidenRefusalTests.fs` leg exercises `set-paths` directly to prove it, not merely `widen`.
- PD-005 [AC-004] complete: OUT OF THIS ITEM'S `Paths:`. `.github#2248`'s body-edit repair and delivery-route re-affirmation are host actions per the item's own scope note; this worker establishes what `.github#2248` legitimately holds (via `gh issue view`/`overlap --active`, read-only) and hands the finding to the host rather than editing another item's body.
- PD-006 [AC-001] [FR-001] complete: SCOPE WIDENING. `tests/FS.GG.Coord.Cli.Tests/ApplicationServiceTests.fs:2250` (`Assert.Equal(7, world.RestCalls)`, comment: "the writes `widen` makes on top (the body PATCH and the courtesy notice)") pins the CURRENT defect's own REST cost for an OVERLAP `widen` — PD-002 removes one of those two writes, so this count must become 6. A `widen FS-GG/.github#2306 --paths tests/FS.GG.Coord.Cli.Tests/ApplicationServiceTests.fs` was issued and returned `disjoint` (no collision) before this plan was authored; the host must re-affirm the delivery-route receipt against the now-5-path declaration.

## Contract Impact
- PC-001 [PD-003] public-fsi: `TouchSet.fsi` gains `UpdateDecision` and `decideUpdate` — additive, new names, no existing signature changes; receiver repos that pin this surface see an addition, not a break.
- PC-002 [PD-002] cli-behavior: `widen`/`set-paths`'s JSON and text receipts, exit codes, and notify-courtesy behavior toward the OTHER (colliding) holder are UNCHANGED — only the SUBJECT item's own `Paths:` mutation is now conditional on a clean collision scan. Every existing recipe that gates on the exit code keeps working; recipes that additionally assumed a collision still landed the declaration (none identified in this repo's own scripts) would see the corrected, intended behavior.

## Verification Obligations
- VO-001 [PD-002] [PC-002] semanticTest: `WidenRefusalTests.fs` proves, by reading back the fake transport's world state (or by asserting zero `PATCH …/issues/<n>` log lines via `Fake.Recorder.Count`), that a full-collision `widen`, a partial-collision multi-path `widen`, and a colliding `set-paths` each leave zero PATCH calls and the item's in-memory body untouched, while a disjoint `widen` still PATCHes and a disjoint `set-paths` still PATCHes (the negative control, AC-005).
- VO-002 [PD-003] [PC-001] semanticTest: A focused `TouchSet` unit test (or a case inside `WidenRefusalTests.fs`) calls `TouchSet.decideUpdate` directly for both `hasCollision=true` and `hasCollision=false`, pinning `RefuseUpdate`/`CommitUpdate`.
- VO-003 [PD-006] regressionUpdate: `ApplicationServiceTests.fs:2250`'s REST-call assertion is corrected from 7 to 6 with its comment updated to state the corrected accounting, and the full `dotnet test tests/FS.GG.Coord.Cli.Tests` suite is run green after the change.
- VO-004 [PD-002] gateInversion: Per `pnext-item` §3, the fix is proven capable of failing: temporarily reverting the `Writes.widen` gate (calling it unconditionally again, as today) is run against the new `WidenRefusalTests.fs` legs and the observed RED failure is recorded as evidence, then the revert is discarded.
- VO-005 [PD-004] regressionRun: The hermetic `tests/coord-engine-e2e/writes.sh` and `tests/coord-engine-parity/run.sh` scripts (free to run; they start their own loopback fixture) are run unmodified after the fix and confirmed still green, since neither declared-Paths file they live in is touched and both already exercise `widen`/`set-paths` OVERLAP and NARROWING legs by exit code and stderr content only.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: `TouchSet.UpdateDecision`/`decideUpdate` (PD-003) are new names with no prior consumer, so there is nothing to migrate. `updateTouchSet`'s write-gating change (PD-002) has no on-disk or wire schema — it changes CONTROL FLOW inside an existing command, not a persisted shape — so no migration step applies; the only compatibility surface is the widened, corrected `ApplicationServiceTests.fs` assertion in PD-006, which is a test-only edit with no runtime migration concern.

## Generated View Impact
- GV-001 [PD-001] [PD-002] [PD-003] workModel: `readiness/2306-widen-refusal-atomicity/work-model.json` refreshes from this plan's PD-001..PD-006 and the FR-001..FR-005 they satisfy; `fsgg-sdd refresh` re-derives it after `tasks`/`analyze` rather than it being hand-authored here.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2306-widen-refusal-atomicity`.
