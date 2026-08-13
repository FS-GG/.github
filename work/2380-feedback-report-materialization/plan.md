---
schemaVersion: 1
workId: 2380-feedback-report-materialization
title: Feedback Report Materialization
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2380-feedback-report-materialization/spec.md
sourceClarifications: work/2380-feedback-report-materialization/clarifications.md
sourceChecklist: work/2380-feedback-report-materialization/checklist.md
publicOrToolFacingImpact: true
---

# Feedback Report Materialization Plan

Prose status: planned

## Source Snapshot
- spec: work/2380-feedback-report-materialization/spec.md sha256:d8244ac725044eb409e15a455c86de90f2db9e875aac7a949affc604cb6c8abd schemaVersion:1
- clarifications: work/2380-feedback-report-materialization/clarifications.md sha256:02d8580dd5dc10a7ab023037e555a0209677c447e83aaca396c8134aac11f47f schemaVersion:1
- checklist: work/2380-feedback-report-materialization/checklist.md sha256:52397f10ff9d7c5294010c692657309ca4e407a122228259e2e4f0d780754628 schemaVersion:1

## Plan Scope
- Work item 2380-feedback-report-materialization is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 2.
- Checklist result count: 8.

## Approach — what "implementation" means for this item

This item's product is a **record**, not a code change. `.github#2380` states its own root cause as
*not established* and makes establishing it acceptance criterion 1; the mechanism that must change
lives in other repositories, which `.github#2366` SB-005/SB-006 and this item's delivery-route
re-affirmation both place out of scope. The declared `Paths:` are the SDD package alone.

So implementation proceeds in four steps, all already discharged in `spec.md`:

1. **Measure producers, not the consumer.** Read the emitting artifacts directly — Rendering's
   `.template.config/template.json`, the `fs-gg-fable-game` template definition, FS.GG.Templates'
   producer manifest, FS.GG.SDD's three enrollment sources, and the measured tree's own
   `scaffold-provenance.json`. Findings F1-F4.
2. **Execute the predicate evaluator rather than reason about it.** Every truth value in F5 is the
   output of `scripts/skill-union-assert.sh --eval-when`, run against a parameter set matching the
   measured tree, never a reading of the evaluator's source.
3. **Adjudicate the item's two candidates explicitly** (F7) and state whether a second tree is needed
   (F8), so the record closes the questions `#2380` left open rather than restating them.
4. **File at the cause, not the surface.** The structural gap, the two incidental defects found on the
   way, and the human decision each get their own deduped row; none is fixed here.

## Verification approach

There is no compiled artifact and no runtime behaviour to exercise, so the verification obligation is
**re-executability of the evidence**, not a test suite. Every load-bearing claim in `spec.md` carries
the command, `file:line`, or API call that produced it, chosen so a reviewer can re-run it and get the
same answer. Two classes matter most:

- **Executed measurements** — the `--eval-when` table (F5) and the exit-2 measurement (D1) are
  reproducible from this checkout plus one public API read, and both are stated with their exit codes.
- **Cited artifacts** — line-numbered citations are pinned to this checkout's `18af7595` for local
  files, and to `main` at read time for the four external repositories, with the read commands given.

The gate-inversion obligation that normally accompanies a change does not attach here, because this
package **adds no gate**. The gates whose inversion evidence is owed are the fixtures required by
`.github#2545` acceptance 3 and `.github#2546` acceptance 3, and each of those rows carries that
obligation explicitly rather than leaving it implied.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: State the single root cause in `spec.md` F6 — no delivered channel exists for Rendering-owned `scope: product` rows, so a non-rendering-provider scaffold receives none — and attach to every load-bearing claim the artifact, `file:line`, or API read that produced it, so the cause is checkable rather than asserted.
- PD-002 [AC-002] [FR-002] complete: Adjudicate `#2380`'s two candidates in `spec.md` F7 as REFUTED-as-stated, each with the measurement that kills it: candidate 1 by the `fs-gg-fable-game` template carrying no `profile` symbol and emitting no skills at all, candidate 2 by `fs-gg-game-fable` being delivered while its predicate evaluates false.
- PD-003 [AC-003] [FR-003] complete: Produce F5's predicate table by executing `scripts/skill-union-assert.sh --eval-when` against a parameter set matching the measured tree, recording `always` -> true and each `profile`-gated predicate -> false, and reproduce the invocation so a reviewer can re-run it.
- PD-004 [AC-004] [FR-004] complete: Name Rendering's `fs-gg-ui` template as the sole channel (F1) and prove its absence from the measured chain by quoting the tree's own provenance attribution — `producedPaths`, `driverPaths`, `gameSkillPaths`, `mirroredPaths`, `sddOwnedPaths` — plus FS.GG.SDD's three enrollment sources (F3).
- PD-005 [AC-005] [FR-005] complete: Answer "no second tree required" in F8 and justify it from the producer side, distinguishing the mechanism question this work answered from the distribution question `#2380` asked, and state that blast radius is consequently NOT claimed.
- PD-006 [AC-006] [FR-006] complete: File each out-of-scope defect at its own cause after a REST dedupe against that cause — `.github#2545`, `#2546`, `#2547` — and record the numbers plus the dedupe reads in `spec.md`'s Filed rows table.
- PD-007 [AC-007] [FR-007] complete: Route the `EHotwagner/S.I.R.` posture as `Class: decision` row `.github#2548` carrying three options, a recommendation with its reasoning, and the `registry/repos.yml` link that discharges `#2380` AC4 — never resolving it inside this package.
- PD-008 [DEC-003] acceptedDeferral: Carry the fix-route choice to `.github#2545` as its first acceptance criterion, handing over both candidate routes and the ADR-0058/ADR-0063 consistency argument, so task generation plans no route work here.
- PD-009 [CR-008] acceptedDeferral: Keep CR-008's deferral of that same route decision visible to tasks and evidence, so no task claims to implement a channel this item deliberately does not choose.

## Contract Impact
- PC-001 [PD-001] command report: No cross-repo API, schema, wire format, or published surface changes. The only tool-facing artifacts are this package's own SDD command reports, which stay schemaVersion 1 and compatibility-preserving; the record's conclusions bind no consumer, and the mechanism changes they motivate are owned by `.github#2545`.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Re-execute the recorded measurements — the `--eval-when` predicate table and the `--params` exit-2 measurement — and confirm each cited `file:line` resolves, since this package ships a record rather than a compiled artifact.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Nothing migrates. This package adds files under two new directories and alters no existing artifact, so there is no data, schema, or consumer state to move; plan schemaVersion 1 is accepted and an unsupported schema diagnoses before any write.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2380-feedback-report-materialization/` carries the generated views for this package — `work-model.json` and `analysis.json` — regenerated from the current plan sources by `fsgg-sdd`. They are tool output, never hand-edited; editing an upstream source without re-running the stage must surface as `staleGeneratedView` rather than pass silently.

## Accepted Deferrals
- DEC-003 acceptedDeferral: The fix-route choice (extend `.github`'s byte-transport versus stand up a Rendering-owned product channel) is deferred to `.github#2545` acceptance 1, with both routes and their ADR-0058/ADR-0063 consistency argument handed over rather than the bare question.
- CR-008 acceptedDeferral: The checklist's advisory record of that same deferral, kept visible so evidence and tasks both show the route decision as owned elsewhere and deliberately unmade here.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- Scope note, non-blocking: the item's declared `Paths:` cannot reach `registry/repos.yml`, so `#2380` acceptance criterion 4 is undeliverable inside this item. Rather than widen a shared registry file onto a lane other workers may hold — for a line whose content depends on an unmade human decision — it is carried by `.github#2548` acceptance 3 and 4. Recorded because leaving it silent would recreate the untracked-follow-up defect `#2380` was filed about.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2380-feedback-report-materialization`.
