---
title: "Unified Roadmap v3 progress — 2026-10-13T02:25:45Z"
generated_at: "2026-10-13T02:55:16Z"
as_of: "2026-10-13T02:25:45Z"
window_start: "2026-10-12T02:25:45Z"
display_timezone: "Europe/Vienna"
roadmap_revision: "bd12db1ee33346582dfabd143c53892317364c12"
previous_report: "none"
comparison_baseline: "d035ce3a106ce9b74d31cface0ee51c9e7c87caf; formal roadmap closure only"
method: "show-progress/1; provisional equal-item readiness index"
status: "partial evidence snapshot; initial reporting baseline"
---

# Unified Roadmap v3 progress

**2 of 17 declared outcomes are accepted: 11.8%, unchanged over the window.**
The earlier conversation's per-item judgement ranges produce an equal-item readiness
index of approximately **66%** (range **62–71%**). This is a low-confidence
planning view of readiness, not measured programme effort completion. Fifteen open
outcomes have unequal size and external dependencies. Games and conditional outcomes
outside section 2 are excluded.

The window is **12 October 02:25:45 to 13 October 02:25:45 UTC** (04:25:45 CEST on
both dates). Evidence includes local repository history, retained handoffs and the
supplied conversation snapshot. The snapshot's later run/consumer claims were not
independently refreshed. No live work, native jobs or worker interactions were initiated
for this report. Generation time is later than the evidence cutoff; this is not a live dashboard.

**A measured readiness delta for the last 24 hours is unavailable.** The earlier chat's
rough +3–4 percentage-point estimate had no frozen acceptance weights or comparable
historical readiness baseline. It remains an unvalidated advisory guess, not this report's
measured result. The verified formal delta is **0/17 outcomes, 0 percentage points**;
substantial candidate, installed-package, source and qualification milestones advanced
inside open items. Original/previous total effort and attributed actual effort were not
available, so no numerical total-effort overrun or programme completion date is defensible.

## Overview

Readiness below preserves the earlier provisional ranges. The same 17 rows have equal
weight in the index; ranges are judgement bounds, not statistical confidence intervals.
No comparable per-item historical readiness baseline exists. Therefore “unavailable”
means an unmeasured delta, not no work performed.

| Item | Readiness | Whole outcome | 24h readiness change | Whole-item finish |
|---|---|---|---|---|
| UTEL | 75%–85% | Open | Unavailable | Unknown; see dependencies below |
| ROUTINE-COVERAGE | 40%–50% | Open | Unavailable | Unknown; see dependencies below |
| V2-EFF-01 | 55%–65% | Open | Unavailable | Unknown; see dependencies below |
| V2-PROC-01 | 60%–70% | Open | Unavailable | Unknown; see dependencies below |
| V2-CTX-01 | 65%–75% | Open | Unavailable | Unknown; see dependencies below |
| TSDD-KNOWLEDGE-01 | 65%–75% | Open | Unavailable | Unknown; see dependencies below |
| SVG-RELEASE-D | 75%–85% | Open | Unavailable | Unknown; see dependencies below |
| SVG-COHERENCE-01 | 40%–50% | Open | Unavailable | Unknown; see dependencies below |
| GAME-BOX2D-01 / GAME-PORTAL-01 P3 | 100% | Done | 0 pp formal | Already complete |
| FABLE-ADOPT-01 / GAME-TEMPLATE-01 | 70%–80% | Open | Unavailable | Unknown; see dependencies below |
| WASM-SHARED-01 | 75%–85% | Open | Unavailable | Unknown; see dependencies below |
| LEARN-01 | 50%–60% | Open | Unavailable | Unknown; see dependencies below |
| COORD-BOARD-V2-01 | 100% | Done | 0 pp formal | Already complete |
| V2-LANG-01 | 40%–50% | Open | Unavailable | Unknown; see dependencies below |
| OPS-TYPED-01 | 50%–60% | Open | Unavailable | Unknown; see dependencies below |
| FBX-07 | 60%–70% | Open | Unavailable | Unknown; see dependencies below |
| UNITYC-01 | 30%–40% | Open | Unavailable | Unknown; see dependencies below |

## What changed during the window

The retained [19:29 UTC handoff](../../handoffs/2026-10-12-192900-unified-roadmap-handoff.md)
records these accepted intermediate milestones, all distinct from whole-outcome closure:

- **UTEL:** [#4452](https://github.com/FS-GG/.github/pull/4452) and original candidate
  `38290966724/a1` passed full candidate qualification; source engine `38285249501/a1`
  passed all 31 steps. Publication, host installation and live collection stayed open.
- **PROC:** [#967](https://github.com/FS-GG/FS.GG.Coordination/pull/967),
  [#968](https://github.com/FS-GG/FS.GG.Coordination/pull/968) and
  [#969](https://github.com/FS-GG/FS.GG.Coordination/pull/969) delivered producer/installed
  repairs. Original-A installed run `38296002826/a1` passed all 11 steps, with actual
  package-only compiler references and runtime/load accepted. Bootstrap passed 2,101 tests.
- **CTX:** [#4453](https://github.com/FS-GG/.github/pull/4453) and hosted `38279450593`
  qualified a bounded ordinary return window and parent reconstruction. Source-only new
  scope at that handoff was not retrospectively called accepted.
- **LEARN:** [#4451](https://github.com/FS-GG/.github/pull/4451) and Host `38275425561`
  passed the zombie-census repair. Native eight-case qualification remained NOT_RUN.

Repository commit times place [#4456](https://github.com/FS-GG/.github/pull/4456)
at **12 October 20:42:40 UTC** and [#4457](https://github.com/FS-GG/.github/pull/4457)
at **12 October 23:11:41 UTC**. These publisher-compatibility and CI wall-time repairs
are within the final six hours of the selected window. Merge time alone is not feature
acceptance. Published 0.102/schema-14 acceptance predates this 24-hour window and is not
counted as new progress.

The supplied conversation snapshot, with later claims not independently refreshed here,
adds scoped PROC consumer qualification (SDD 40 checks and 118 tests/57-roster; FsQuint
87 tests), CTX 45/16 scoped qualification, and green full engine results for the wall-time
repair. It also records [#4458](https://github.com/FS-GG/.github/pull/4458), a pending
canonical delivery-envelope repair with 95 focused tests passing. Its original full branch
run `38336132389/a1` started **13 October 02:16:54 UTC** and was pending in that snapshot.
The report does not infer its later result, merge, publication or activation.

No whole Rendering, SC2 or game outcome is claimed to have closed. Missing detailed
updates for quieter lanes are a coverage limitation, not evidence that no work occurred.

## Complications and estimate revisions

“Unexpected” requires an earlier assumption/risk baseline. That baseline is absent here;
these are **newly observed complications, surprise classification unknown**, except
where described as existing blockers. Exact first-seen times are unavailable unless given.

| Item | Observation / evidence | Response and status at cutoff | Effort impact | Calendar impact / total forecast change |
|---|---|---|---|---|
| UTEL | CI timeout/wall-time boundary and publisher compatibility; #4456/#4457 | Source repairs merged; supplied snapshot reports full engine passes | Additional diagnosis/rework occurred; hours unrecorded | Additional qualification was necessary; net critical-path delay and EAC delta unknown |
| UTEL | Missing routine PR-body marker caused ordinary preflight refusal; supplied snapshot | #4458 pending; original preflight retained 13 GET/0 POST, no publication effect claimed | Further envelope/tests work required; actual amount unknown | Installation/publication waits for valid admission; previous numerical ETA unavailable |
| PROC | Two installed attempts failed strict isolation; exact trailing separator identified by #969 | Narrow repair and distinct installed run passed | Additional repair and qualification effort; unquantified | Failure waits overlap other work; do not add job durations as programme delay |
| PROC | Public rights absent; original local process effects unknown | Existing explicit holds preserved | Waiting alone is not effort; recovery work not yet bounded | Whole completion cannot be dated; numerical total forecast unavailable |
| CTX | Identity syntax failure, then wrong expected notice; 19:29 handoff | Failures preserved; third ordinary operation accepted; later scoped qualification owner-reported | Correction and requalification consumed unrecorded effort | Remaining original same-owner restart capability still blocks a firm finish |
| LEARN | Zombie observations required repair; authentic native provisioner trust missing | #4451/Host passed; native eight-case work unlaunched | Repair consumed effort; native future scope conditional | No whole-item date until trust, custody and live qualification prerequisites exist |
| SVG-RELEASE-D / WASM | Original tag effect unknown / missing SC2 candidate custody | Diagnostics or source cannot substitute for original evidence | Recovery effort unknown | Completion blocked on original evidence; no numerical schedule shift calculable |

These discoveries show that necessary rework increased in several lanes. They do **not**
prove a quantified increase in total effort: there is no original estimate, complete
actual-effort ledger or prior EAC. Other covered lanes have no newly identified complication
in the reviewed handoffs; detailed owner-level coverage remains incomplete.

## Item details

For every item below, original total baseline, previous total forecast and attributed
actual effort are **unavailable**. Consequently current actual-plus-remaining total and its
absolute/percentage change are **unavailable**. A completed item's ETC is zero, while its
historic total effort still remains unknown. For open items, whole-item ETC is unknown;
explicitly bounded next-milestone scenarios below are not substitutes for it.

The declared scope and owner are taken from section 2 of the roadmap at the recorded
revision. Those acceptance boundaries define completion; short summaries below do not
extend them. The ranges are inherited analyst judgement, not owner-approved estimates.

### UTEL

- **Owner / roadmap row:** UTEL operational collection — `.github` telemetry/Coordination.
- **Declared completion boundary:** Reconcile the observed existing 0.100.0 installation/selector under original effect identities, then qualify the current-only dispatch contract and fresh CI/delivery collection. Recover retained pending publications through their supported original-identity route.
- **Accepted / remaining:** Candidate 0.103 passed its original qualification; publication, installed use and live collection remain open.
- **Readiness:** 75–85%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** Candidate qualification, publisher compatibility and CI wall-time repair advanced; the subsequent delivery-envelope repair remains pending at the cutoff. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** The initial whole-item estimate cannot be dated: original-operation recovery and publication admission remain dependencies. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Selected delivery-envelope integration/readback only: 1–3 focused owner-hours (integration 0.25–1, gate analysis 0.5–1, readback 0.25–1); scenario 3–8 elapsed hours after exact checks pass and an integrator is available. Excludes further failures and the complete publication/live-collection path.
- **Complication / response:** CI duration, publisher admission and a missing routine PR marker added rework; the marker repair was not yet merged in the supplied snapshot. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### ROUTINE-COVERAGE

- **Owner / roadmap row:** Routine activity, review and usage coverage — programme/telemetry owners.
- **Declared completion boundary:** Collect planning, implementation, review, validation, delivery, operations and repair spans; submit selected reviews for settled attempt populations under the bounded assessment policy; reconcile native usage with explicit unknowns; continue CI/delivery reconciliation. Every efficiency view exposes coverage, freshness, unresolved observations and its denominator.
- **Accepted / remaining:** Routine activity, review and usage coverage is partial; complete observation coverage is not established.
- **Readiness:** 40–50%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No comparable coverage denominator was available; causal and dashboard improvements are enabling work, not complete coverage. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Whole-item duration unknown until the selected worker/review population, missing observations and coverage acceptance are enumerated. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next estimation milestone: inventory missing observations and ownership; no duration estimate supported by the reviewed evidence.
- **Complication / response:** Coverage is incomplete. New complications cannot be determined from the reviewed sources. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### V2-EFF-01

- **Owner / roadmap row:** V2-EFF-01.2–.6 — telemetry, dashboard and receiver owners.
- **Declared completion boundary:** Qualify remaining canonical measurement/correction and completion-assessment behavior; install the coherent schema-14/Host-5 route, migrate the store, enroll supported providers, deploy matching dashboard/feed and run calibration plus a bounded live pilot. Deliver an evidence-backed process-health dashboard with explicit uncertainty, accepted-outcome and effort views, overhead-chain investigation, explainable anomalies and advisory interventions; calibrate its verdicts and verify the installed private diagnostic and approved public projections in that bounded pilot.
- **Accepted / remaining:** Dashboard and receiver source advanced; declared .2–.6 outcomes and economics are not all accepted.
- **Readiness:** 55–65%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** Process-health source and causal receiver qualification advanced. Shared UTEL work is counted once in programme effort. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Whole-item duration unknown; depends on installed/live data, complete attribution and selected evaluation scope. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: join qualified runtime data to the selected dashboard/measurement population. Remaining scope and effort must be bounded before estimating.
- **Complication / response:** Missing usage/cost coverage prevents a defensible efficiency conclusion; this is an existing evidence gap. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### V2-PROC-01

- **Owner / roadmap row:** V2-PROC-01.2–.8 — Coordination; SDD/FsQuint consumers.
- **Declared completion boundary:** Qualify shared I/O/deadline contracts, routine adapter and Linux containment, then publish, adopt selected consumers and consolidate proven replacements with a benefit review.
- **Accepted / remaining:** Original candidate-A installed package-only execution passed; supplied later snapshot reports scoped SDD/FsQuint consumer checks.
- **Readiness:** 60–70%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** Producer repairs #967–969 and installed qualification materially advanced the lane; scoped consumer evidence increased later. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Whole .2–.8 completion unknown: public package rights, .4 Linux containment and full .6 upgrade/rollback remain separate. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Selected coherent publication and public consumer qualification: 6–18 focused owner-hours (preparation 1–3, release gates 2–6, two consumer joins 2–6, readback 1–3); scenario 2–5 working days after rights and original-candidate admission are resolved. This excludes Linux containment and full whole-item closure.
- **Complication / response:** Strict installed-folder trailing-separator mismatch required repair. Public rights and original local process effects remain unresolved; waiting is not measured labour. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### V2-CTX-01

- **Owner / roadmap row:** V2-CTX-01.4–.6 — `.github` programme driver.
- **Declared completion boundary:** Complete owner/watcher/restart integration, bounded ordinary-window evaluation and qualified adoption/retirement; retain missing usage and baseline coverage. Controlled economic comparison remains conditional on a selected savings claim.
- **Accepted / remaining:** Ordinary return-window and parent reconstruction accepted; supplied later snapshot reports 45/16 scoped qualification passing.
- **Readiness:** 65–75%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** Source/native evidence advanced; same-owner restart, missed-event integration, adoption and economics are not thereby complete. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Whole .4–.6 date unknown without original same-owner restart capability and remaining adoption/economics scope. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Selected restart qualification after capability is available: 4–8 focused owner-hours (setup 1–2, execution/controls 2–4, analysis 1–2); scenario 1–3 working days. Does not include obtaining capability or whole adoption/economics completion.
- **Complication / response:** Initial identity syntax and incorrect notice expectation caused distinct failed profiles; later success does not erase them. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### TSDD-KNOWLEDGE-01

- **Owner / roadmap row:** TSDD-KNOWLEDGE-01.4–.5 — SDD/Templates; Wizard release owner.
- **Declared completion boundary:** Finish provider composition, genuine Wizard publication/install qualification, coherent fresh workspace creation and preserving retained extraction. Keep canonical text knowledge bounded to 10 MiB.
- **Accepted / remaining:** Reuse delivered knowledge and Wizard work; .4–.5 release/receiver boundaries remain open in the roadmap.
- **Readiness:** 65–75%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No newly accepted whole outcome identified in the reviewed window; detailed owning evidence was not refreshed. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown until Wizard release and selected installed receiver acceptance steps and capacity are inventoried. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next forecastable milestone: owner-confirmed release/receiver gap list. Insufficient evidence for a duration estimate.
- **Complication / response:** No new complication identified in the reviewed handoffs; owner-level coverage is incomplete. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### SVG-RELEASE-D

- **Owner / roadmap row:** SVG-RELEASE-D — producer and workspace owners.
- **Declared completion boundary:** Close remaining public Wizard/full installed workspace and selected activation outcomes; qualify any later single-lifecycle default separately.
- **Accepted / remaining:** Qualified candidate retained; publication and workspace adoption remain open.
- **Readiness:** 75–85%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** Candidate evidence and later diagnostic source exist; neither resolves the original publication effect. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown while original tag/publication effect remains unknown; a new publication attempt is not assumed. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: establish original tag/publisher terminal state and retained artifact custody. Duration depends on the supplying owner.
- **Complication / response:** Original tag push has unknown effect; diagnostics were delivered but acceptance/publication was not established. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### SVG-COHERENCE-01

- **Owner / roadmap row:** SVG-COHERENCE-01.3–.8 — Rendering/Templates; product consumers.
- **Declared completion boundary:** Complete realistic repeated-glyph software research, affected shared reconciliation/performance changes, integrated reference, consumer candidates, one consolidated host-GPU batch, then remaining publication/adoption/removal.
- **Accepted / remaining:** Shared coherence source is partial; .3–.8 consumer joins and accepted retirement remain open.
- **Readiness:** 40–50%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No newly accepted whole outcome identified in the reviewed window; detailed consumer inventory was not refreshed. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown until selected consumer gaps, upstream release dependencies and capacity are bounded. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: retain an authoritative per-consumer acceptance inventory; no defensible duration available.
- **Complication / response:** Shares publication/consumer dependencies with SVG-RELEASE-D. No separate new complication verified. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### GAME-BOX2D-01 / GAME-PORTAL-01 P3

- **Owner / roadmap row:** GAME-BOX2D-01 adoption / GAME-PORTAL-01 P3 — Game/Rendering/Templates.
- **Declared completion boundary:** Complete for the declared finite headless managed presentation and public Template adoption scope; retain the separate browser/server, viewport and P4 boundaries.
- **Accepted / remaining:** Done for the explicitly declared Box2D adoption/Portal P3 scope.
- **Readiness:** 100%; formal Done, evidenced by both roadmap revisions.
- **Window movement:** Already done at both boundary revisions; 0 pp formal movement. No quantified scope/weight change established.
- **Effort / change:** ETC 0; historical total unknown. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Complete for this declared scope before the window; 0 remaining effort/duration. No new completion date inferred. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** No remaining milestone for this accepted row. Later product work is separate scope.
- **Complication / response:** No reopening is recorded in the reviewed roadmap revisions. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### FABLE-ADOPT-01 / GAME-TEMPLATE-01

- **Owner / roadmap row:** FABLE-ADOPT-01 shared reference / GAME-TEMPLATE-01 — Rendering/Game/Templates.
- **Declared completion boundary:** Consolidate reusable completed games work into producer APIs and the installed `fable-game` reference; qualify remaining WASM/example composition, publication and clean/retained creation. Section 3 specifies coverage.
- **Accepted / remaining:** Shared reference delivery is advanced; template composition and generated-workspace proof remain selected outcomes.
- **Readiness:** 70–80%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No newly accepted whole outcome identified; do not count shared template work twice. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown until the remaining composition and generated-workspace qualification cases are enumerated. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: Templates owner confirms selected composition/receiver matrix and release dependencies; duration unavailable.
- **Complication / response:** Consumer/publication dependencies remain. No new specific complication verified. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### WASM-SHARED-01

- **Owner / roadmap row:** WASM-SHARED-01 remaining producer compatibility / .7 — Game.
- **Declared completion boundary:** Qualify the additive SC2 compatibility successor and genuine installed guest SDK/profile acceptance where still missing; support remaining consumer joins and retire only proven superseded duplication.
- **Accepted / remaining:** Published 0.2.0 boundary is retained; remaining SC2 compatibility and genuine installed guest SDK/profile acceptance are open.
- **Readiness:** 75–85%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No new whole closure. Existing source and publisher evidence do not replace missing original SC2 custody. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown until original SC2 candidate, owner and counterexample custody are resolved and installed scope is bounded. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: recover original operation identity/evidence through its owner; duration cannot be scheduled from available evidence.
- **Complication / response:** Original candidate/owner/counterexample custody is missing. Browser deadlines do not establish deterministic instruction-fuel acceptance. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### LEARN-01

- **Owner / roadmap row:** LEARN-01.2/.4–.5 / Akka orchestration — Coordination/telemetry/receiver owners.
- **Declared completion boundary:** Complete causal-declaration receiver and producer projection, then qualify publication and receiver-first adoption. Verify served Manager/runtime closure, inactive persistent-v3 images and genuine receiver grant/turn/capture/restart/cleanup; establish census/shared costs and enrollment, then a locked dataset or truthful stopped-window comparison.
- **Accepted / remaining:** Causal source/receiver work and Host verification advanced; native eight-case qualification remains NOT_RUN.
- **Readiness:** 50–60%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** Zombie-census repair #4451 and Host qualification landed; publication/receiver-first adoption and native runtime admission remain open. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Whole .2/.4–.5 completion unknown; authentic provisioner/controller trust, custody and live evaluation prerequisites remain. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Selected native eight-case qualification after trust/custody prerequisites: 6–14 focused owner-hours (preparation 2–4, eight-case execution/review 2–6, acceptance analysis 2–4); scenario 2–4 working days. Excludes building missing infrastructure and whole adoption/evaluation closure.
- **Complication / response:** Zombie observations required source repair; native qualification still lacks authentic provisioner provenance. Prior local process effects remain unknown. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### COORD-BOARD-V2-01

- **Owner / roadmap row:** COORD-BOARD-V2-01 — `.github`/SDD/Templates/selected products.
- **Declared completion boundary:** Complete for the declared organization outcomes and selected two public qualification workspace cohorts; retain unresolved consumers and fleet boundaries.
- **Accepted / remaining:** Done for the declared organisation outcomes and two selected public qualification workspace cohorts.
- **Readiness:** 100%; formal Done, evidenced by both roadmap revisions.
- **Window movement:** Already done at both boundary revisions; 0 pp formal movement. No quantified scope/weight change established.
- **Effort / change:** ETC 0; historical total unknown. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Complete for this declared scope before the window; 0 remaining effort/duration. Fleet-wide adoption is not implied. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** No remaining milestone for this accepted row. Unselected consumers remain outside its declared acceptance.
- **Complication / response:** No reopening is recorded. Existing fleet/remote and unselected consumer boundaries remain. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### V2-LANG-01

- **Owner / roadmap row:** V2-LANG-01.2 P3–P5 and full .5 adoption — Coordination/Governance/SDD/Templates/Sandbox.
- **Declared completion boundary:** Complete actual Governance Verify/package-backed SDD integration, blocked-read/uncertain-close capture retention coverage and bounded execution/evidence acceptance; qualify any selected stronger profiles, then qualify C3 public release and C4 receivers. Publish the portable successor; qualify genuine provider/runtime facts and cleanup, supported cold receivers, upgrade/matrix closure and TypeScript/Rust/Go installed adoption.
- **Accepted / remaining:** Portable/Governance bounded components delivered; actual providers, retention, package-backed joins and public cold receivers remain open.
- **Readiness:** 40–50%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No newly accepted whole outcome identified; detailed owner state was not refreshed. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown until provider/runtime admission, selected stronger profiles and receiver matrix are bounded and available. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: Governance/SDD owners identify executable provider and retention acceptance steps; duration unavailable.
- **Complication / response:** Retained readiness/Podman refusals and incomplete provider loading remain. Source-only components do not establish runtime acceptance. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### OPS-TYPED-01

- **Owner / roadmap row:** OPS-TYPED-01 remaining shared/operational adoption — Coordination and qualification owners.
- **Declared completion boundary:** Adopt published typed replacements at selected production entrypoints and complete the bounded administrative-owner migration. Product-specific FourD and SC2 slices are tracked in the games roadmap.
- **Accepted / remaining:** Typed source replacements exist; selected production entrypoints and bounded administrative-owner migration remain open.
- **Readiness:** 50–60%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No newly accepted operational adoption identified in the reviewed window. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown until selected entrypoints, exact artifacts, operator access and migration steps are inventoried. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: owner confirms migration acceptance and actual operational entrypoint access; duration unavailable.
- **Complication / response:** Source and operational acceptance are separate; no newly verified complication in the reviewed handoffs. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### FBX-07

- **Owner / roadmap row:** FBX-07 public successor and upstream assessment — Templates.
- **Declared completion boundary:** Qualify public installed second-runtime composition and resolve genuinely selected upstream updates.
- **Accepted / remaining:** Candidate and assessment retained; public installed second-runtime composition and selected upstream adoption remain open.
- **Readiness:** 60–70%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No newly accepted whole outcome identified in the reviewed window; upstream observations are not adoption. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown until exact installed second-runtime matrix, publication inputs and selected upstream work are bounded. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: Templates owner confirms remaining installed cases and genuinely selected upstream changes; duration unavailable.
- **Complication / response:** Upstream updates-found/unqualified status is an unresolved observation; no newly verified complication. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

### UNITYC-01

- **Owner / roadmap row:** UNITYC-01.1d and product delivery — Unity Client owner.
- **Declared completion boundary:** Qualify genuine Unity Editor/Server operation, then selected publication/installed adoption and remaining declared product outcomes.
- **Accepted / remaining:** Managed-reference/browser inputs exist; native Unity Editor/Server qualification and product delivery remain open.
- **Readiness:** 30–40%; provisional low-confidence judgement of mixed source/qualification/remaining-adoption evidence; no measured weights.
- **Window movement:** No newly accepted native outcome identified in the reviewed window. No quantified scope/weight change established.
- **Effort / change:** Whole-item ETC and EAC unknown; original and previous totals unavailable. No numerical overrun can be calculated.
- **Whole-item completion / forecast revision:** Unknown without genuine Editor/Server access, operator admission and remaining product acceptance scope. Prior numerical forecast unavailable; this is the first stored forecast record.
- **Next milestone / conditional estimate:** Next milestone: native owner establishes exact runnable Editor/Server qualification and access; duration unavailable.
- **Complication / response:** Native engine/title access and exact operator admission remain blockers; the reviewed evidence cannot date their resolution. Exact first-observed time and quantified effect are unavailable unless listed above.
- **Evidence / freshness:** roadmap revision `bd12db1e`, retained 12 October handoffs and the explicitly labelled supplied snapshot through 2026-10-13T02:25:45Z; detailed unmentioned owner evidence was not refreshed. Re-estimate when the named dependency or acceptance boundary changes.

## Forecast assumptions, calibration and next update

The four bounded next-milestone estimates are **analyst scenario ranges**, based on an
explicit remaining-task breakdown, not historical distributions or probability promises.
They assume one qualified owner with 4–6 focused hours available per working day, ordinary
CI capacity, same-day integrator availability and no new defect that changes the named
scope. Working days mean Monday–Friday; start dates are conditional on the named blocker
clearing and work being selected. No calendar finish dates are manufactured for unknown
unblock dates. The UTEL hours are elapsed hours after its narrower stated start condition.
These scenarios do not estimate all remaining work in their parent roadmap items.

Do not sum these ranges into a programme ETA: release infrastructure, integration capacity
and installed receiver work are shared dependencies, and waits may overlap. Actual owner
hours, agent execution, token consumption, CI compute and elapsed waiting remain different
quantities. No empirical sample, simulation, calibrated P50/P80 or capacity measurement
supports stronger probability claims in this first report.

The formal baseline uses repository history at or before the window start: revision
`d035ce3a106ce9b74d31cface0ee51c9e7c87caf`. Its section 2 has the same 17 items and two
Done rows as revision `bd12db1ee33346582dfabd143c53892317364c12`.
The earliest reviewed handoff at **12 October 04:10:22 UTC** is later than the requested
window start; it is useful corroboration but not a valid exact historical readiness baseline.

Calibration is unavailable because there are no earlier stored item forecasts with comparable
scope and resolved outcomes. Preserve this report, including misses and unknowns. A next
report should obtain owner-bounded acceptance units/weights, remaining-task estimates,
attributed effort where available, blocker supplier dates, and original-operation results.
Those data permit meaningful future percentage-point, effort and ETA revisions. This report
does not schedule a follow-up or authorize new roadmap execution.

## Sources and method

- [Unified Roadmap v3](../../roadmaps/2026-10-07-unified-development-roadmap-v3.md), section 2; exact revisions recorded above.
- [04:10 UTC handoff](../../handoffs/2026-10-12-041022-unified-roadmap-handoff.md), later than the comparison start.
- [14:56 UTC handoff](../../handoffs/2026-10-12-145609-unified-roadmap-handoff.md).
- [19:29 UTC handoff](../../handoffs/2026-10-12-192900-unified-roadmap-handoff.md).
- Linked source PRs and the supplied conversation snapshot; snapshot-only operational claims are labelled and are not independently refreshed evidence.

The new showProgress method separates accepted work, effort and schedule. Its research
notes cover 12 primary sources. Baseline/dependency discipline follows the
[GAO Schedule Guide](https://www.gao.gov/products/gao-16-89g) and
[GAO Cost Guide](https://www.gao.gov/products/gao-20-195g); the distinction between work
accomplished and effort spent is informed by [NASA EVM](https://www.nasa.gov/ocfo/ppc-corner/evm/tutorial/).
Forecast uncertainty and flow definitions draw on the
[Kanban Guide](https://kanbanguides.org/the-kanban-guide/2025.5/) and future-only forecast
evaluation described in [Forecasting: Principles and Practice](https://otexts.com/fpp3/toolbox.html).
The numerical readiness and scenario estimates here are local judgement; these sources do
not validate the FS-GG percentages or dates.
