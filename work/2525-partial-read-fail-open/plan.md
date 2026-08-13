---
schemaVersion: 1
workId: 2525-partial-read-fail-open
title: Partial Read Fail Open
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2525-partial-read-fail-open/spec.md
sourceClarifications: work/2525-partial-read-fail-open/clarifications.md
sourceChecklist: work/2525-partial-read-fail-open/checklist.md
publicOrToolFacingImpact: true
---

# Partial Read Fail Open Plan

Prose status: planned

## Source Snapshot
- spec: work/2525-partial-read-fail-open/spec.md sha256:24fa8db343d1fc2cc134bc1b8019cbe318bf80997809864b0c4d993d44279225 schemaVersion:1
- clarifications: work/2525-partial-read-fail-open/clarifications.md sha256:16e3cc0372e270fa4641b657624e34fcbb0904ab4a1ec8881449149de72a9569 schemaVersion:1
- checklist: work/2525-partial-read-fail-open/checklist.md sha256:48843efa84faebf9a5b9f95ade5fe8fffc54f68fe1a9e563f1cf2c7c31ed147f schemaVersion:1

## Plan Scope
- Work item 2525-partial-read-fail-open is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 0.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Port `Board.fs:485-504`'s connection-completeness refusal into `Scan.scanFresh`'s pagination loop rather than inventing a second shape for it. `pageInfo` absent or not an object, `hasNextPage` absent or not a Boolean, and `hasNextPage: true` with no non-blank string `endCursor` each become `Error(Malformed …)`. Deliberately NOT a retry or a best-effort continue: the scan's only honest answer when it cannot prove it saw every page is a refusal, and the existing 100-page guard already establishes that shape in this same function.
- PD-002 [AC-001] [FR-002] complete: Split `parseRow`'s single `None` exit into "structurally not an issue row" and "an issue row I could not read". A node whose `content.__typename` is `Issue` or `PullRequest` but whose `number`/`repository.nameWithOwner` will not parse becomes a scan-level `Error`; a draft card keeps the existing silent skip because it has no ref to reserve and inventing one would put a phantom on the queue. `content` absent or null keeps the skip as well — it is the redacted-item case, and refusing it would refuse whole boards that legitimately contain items this token cannot see. That boundary is a deliberate, stated limit, not an oversight.
- PD-003 [AC-001] [FR-003] complete: Give `Projection` an explicit `Unreadable: Classified list` and make `renderText`'s active line fail closed on it. The projection already computes these rows; it simply discards them at the `isActive` filter. Carrying them means the renderer can distinguish "I measured an empty active set" from "I could not measure the active set", which is the distinction the termination rule actually needs. `isActive` itself is NOT changed — an unreadable item is not active, and making it active would corrupt every count downstream.
- PD-004 [AC-001] [FR-004] complete: Keep the exact literal `no active items` for the complete-and-empty case, and gate the new line solely on `Unreadable` being non-empty. The controlled counterpart is the thing that stops this fix from degrading into "always refuse", so it is pinned by its own test rather than left implied.
- PD-005 [AC-001] [FR-005] complete: Report `List.length result.Decisions` — the candidates actually considered — beside the `nothing schedulable right now.` headline, in the one shared spelling both `batch` and `take` already route through (`printChosen`). No new field on `BatchResult`: `Decisions` already IS the measured set, and adding a parallel counter is how the two drift apart. Distinguishability in the exit code comes from PD-001/PD-002, which make a partial scan an `Error` with a non-zero code long before it can present as a green empty ranking.
- PD-006 [AC-001] [FR-006] complete: Stop rendering the cursor's last-known state as a present-tense fact. `missingActiveRefs` keeps reporting the state it has — that information is the point — but words it as an explicitly superseded, last-known-before-this-read observation, and stops using `%A` on the union, whose `Claimed "curlew-307b"` rendering is what reads as a live holder. The sticky-cursor fold is left alone: it is load-bearing for persistence, and the defect is the presentation, not the retention.
- PD-007 [AC-001] [FR-007] complete: Every guard above ships with its inversion recorded — the exact mutation applied, the suite run, and the observed red — at authoring time. The scan guards are driven through `Fake.Recorder` at the transport seam so the fixture exercises the real pagination loop rather than a re-implementation of it; the projection guards are pure and driven directly.

## Contract Impact
- PC-001 [PD-001] command report: `Scan.board`'s `IoResult<Row list>` signature is unchanged — this work makes the `Error` arm reachable in cases that previously returned `Ok`, and adds no new type to that boundary. `DriverEvents.Projection` gains one field (`Unreadable`), a source-compatible record extension whose `.fsi` is updated in the same change; `driver --events --json` gains one additive key. `batch`'s stdout machine contract (the JSON id array, and the `  → <ref>` lines `take` parses) is untouched: the measured-candidate count rides on the human headline only. No exit code changes meaning; codes that were 0 for a partial scan become the scan's own existing error code.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: For each of PD-001, PD-002, PD-003 and PD-006, a fixture that reds when the guard is deleted, plus the PD-004 controlled counterpart that must stay green throughout — both halves run in the same suite so "always refuse" cannot pass as a fix. Scan-side fixtures drive `Scan.board` through `Fake.Recorder` with a truncated board page while claim rows exist, exactly the .github#2525 AC3 scenario. Full `dotnet test` across Core, GitHub and Cli, plus the repository's own gate suites, before handoff.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No data migration. The one operational posture change is that a board scan which previously returned a truncated `Ok` now refuses — callers already handle `Error` from this read on every other failure path, so no caller gains an unhandled case. A scan cache written before this change can still hold a truncated batch for up to its 90s TTL; it expires on its own and no invalidation step is required.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2525-partial-read-fail-open/work-model.json` is re-derived from these plan sources. No projection under `scripts/generate-projections` is affected: this change touches no `SKILL.md` body and no emitted protocol fact, so the generated skill corpus is unchanged and its coherence gates should stay green untouched.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2525-partial-read-fail-open`.
