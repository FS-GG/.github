---
title: "Analysis: Align development-flow proposals without weakening v2 migration"
category: Design
categoryindex: 4
description: "Disposition of conflicting routine-development, controller and orchestration proposals, with explicit preservation of accepted v2 migration requirements."
---

# Analysis: Align development-flow proposals without weakening v2 migration

Authored: **2026-09-07 11:28:37 UTC**. Status: **proposal analysis and design alignment only**.
This document and the accompanying edits change proposed scope, investment sequence and rationale. They
change no accepted ADR, execution instruction, parsed roadmap gate, runtime, required evidence, release
contract or production authority. They are not permission to use the proposed lightweight path today.

## 1. Conclusion and evidence boundary

Both earlier designs need substantive scope and roadmap alignment with the
[September 7 bureaucracy-reduction proposal](../2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md).
The [September 6 performance design](2026-09-06-performance-bounded-development-flow-design-and-roadmap.md)
originally made a new controller its first route to routine improvement. The
[August 31 orchestration design](2026-08-31-operations-research-first-agent-orchestration-design.md)
made a hosted executor the intended normal path. Neither should become a hidden prerequisite for deleting
routine ceremony. Both contain useful architecture that should remain available for independently
justified needs rather than be discarded wholesale.

This is a local-document consistency analysis, not a new measurement or external research study. Its
baseline is the two proposals, the September 7 proposal, the
[v2 roadmap](../github-substrate-v2-roadmap.md), the
[remaining-migration architecture amendment](2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md),
and accepted ADR-0077 through ADR-0082 where relevant. Earlier proposals' cited external studies and
historical measurements have not been independently revalidated here. No ordinary live-v2 journey was run.

The September 7 evidence distinguishes three subjects: SDD authoring; shared/migration drivers; and the
Coordination v2 product. Its 68.4% historical coordination/delivery share, or 73.7% after also counting
product critique as overhead, describes the sampled migration work. Neither percentage measures ordinary
live-v2 execution. Removing an expensive agent handoff is justified more directly by that evidence than
removing an automatic journal record or changing the protocol's safety properties.

## 2. Change dispositions

| Concern and source | Necessary or appropriate proposal change | What is not justified |
|---|---|---|
| OR §1 normal executor; §17 initial slice | Make harness adoption optional by named operation class; compare the supported native route first; make the first funded slice local | Requiring a daemon, solver, remote peer or graph before routine work can complete |
| OR §8A federation and §14 visualization | Retain as deferred, independently justified features; qualify security/accessibility for actual shipped surfaces | Removing contributor isolation or UI security because the ordinary path is simpler |
| PB §§1, 12–13 model/controller critical path | Separate R0–R5 simplification from PB0–PB8 controller research; allow a stop after a negative investment case | Requiring a new workflow model, verifier, reservation system or PB closure to delete driver ceremony |
| PB §3 initial owner allocation | Preserve current `.github` execution ownership; Coordination owns actual v2 semantics and any separately justified controller | Moving the incumbent CLI simply because the future controller proposal names Coordination |
| OR §7.5; PB §§3.3–3.4, 7 review/repair | Distinguish the proposed owner-review profile from retained controller fresh-critique/confirmation mechanics | Assuming every current review needs another person, or silently treating current required evidence as optional |
| OR §7.3; PB FLOW-001–004 authoring | No compulsory per-item SDD family solely for routine process; retain modeling where it addresses behavior or authority | Removing canonical Quint authority, compiler identity or implementation conformance from modeled protocols |
| OR §7.4; PB PERF-004/008/018 qualification | Preserve inexpensive semantic-subject reuse; label path-based selection as a proposed lower guarantee, not equivalent proof | Using a routine test-selection shortcut to qualify formal drift, GS2 closure or cutover |
| OR §§4, 10–11; PB FLOW-011/012, §8 authority | Separate the native one-PR experience from automatic typed plans, journals and effect evidence beneath it | Replacing fencing or full mutable approval snapshots with a PR head check without a contract decision |
| PB PERF-013 and §9 observers | Missing telemetry prevents a performance claim; delayed telemetry or projections do not invalidate valid routine delivery | Treating missing technical checks, unknown publication state or missing mutation authority as diagnostic gaps |
| PB PERF-014 and §9.2 inventory | Keep obligation analysis inside controller design/existing policy; remove it as a universal routine admission premise | Building a new registry or agent-written evidence family to prove that fewer artifacts are needed |
| PB §8 reservations | Keep hard reservations for a controller that promises concurrent aggregate caps; ordinary runtime limits can be observational when enforcement is unavailable | Claiming strict token-budget enforcement from receipts that arrive after spending, or requiring a reservation service for one owner |
| OR §14; PB §§3.3, 9 economics | Add the common whole-unit model-overhead measure, retain separate resource and outcome measures | Equating 5%/8% runner-share bounds with 10%/20% model-overhead targets, or moving cost into CI to claim savings |
| PB PB7/PB8; OR H6/H7 adoption | Bound adoption by operation class, actual operating authority and installed receiver behavior | A source fixture, migration pilot or component milestone certifying ordinary v2 carryover or a fleet-wide default |

## 3. Preserve migration goals and distinguish unresolved guarantee changes

The [governing v2 design](2026-08-25-github-substrate-v2-fleet-cutover-design.md) aims for one semantic
authority per fact, native GitHub ownership where sufficient, typed remote mutation plans, generated
projections, a new-only cutover and measurable surface reduction. Deleting agent-authored duplicates and
synchronous view updates supports these goals. A hosted controller is already outside the v2 critical path.

The accepted architecture amendment retains expected-parent Git-journal CAS, fencing generations,
reconciliation as the normal writer path and full snapshot epochs. Cheap automatic records may be how a
native experience safely preserves those guarantees. The appropriate first change is at the expensive
caller or driver boundary, followed by measurement. Removing those records merely because their names
resemble migration receipt PRs would conflate different mechanisms.

The September 7 proposal deliberately goes further in several places. Those are unresolved policy choices,
not consequences of this alignment:

- **Milestone closure:** [ADR-0080](../adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md)
  makes comprehensive closure the backstop for scoped child qualification.
  [ADR-0081](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md) preserves that
  boundary while permitting evidence-based cadence changes. The proposal's routine child-reference closure
  cannot silently apply to GS2 parent acceptance. Keep GS2 child/parent gates and comprehensive migration
  qualification intact; any later generic closure reduction needs its own accepted policy scope.
- **Semantic authority:** [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md) makes embedded
  Quint the behavioral authority for the successor Typed SDD backend. Opting ordinary work out of mandatory
  paperwork does not make source prose, generated bindings or an implementation a replacement authority.
- **Review:** [ADR-0079](../adr/0079-single-accountable-delivery-authority.md) already permits one owner to
  implement, critique, repair and deliver. Removing organizational independence and removing a fresh review
  phase are different changes. The proposed routine path removes the latter choreography too; current
  review/evidence rules require an explicit amendment before that path can replace them.
- **Publication:** one-feed routine release is a proposed contract reduction. Existing coherent-set,
  publish-before-consume, byte-verification and required-feed obligations remain until their owners change
  them. Batching compatible releases does not imply permission to leave a required publication unfinished.
- **Telemetry:** [ADR-0082](../adr/0082-durable-private-content-addressed-telemetry-receipts.md) and applicable
  callers must be reconciled before their required receipt path can be removed. Missing usage can be a
  measurement gap; an indeterminate external effect still needs its real recovery owner.

The strongest route is not a destination for all expensive work merely to improve the routine average.
Shared protected-operation costs remain visible. Conversely, an overhead ceiling cannot waive an existing
safety obligation. Unknown or mixed eligibility follows the stronger applicable route or a useful split;
the candidate cannot rewrite its own classification authority to qualify itself.

## 4. Measurement and investment sequence

Use the September 7 proposal's R0 mapping before implementation: distinguish observed driver ceremony,
automatic v2 mechanisms and unmeasured receiver behavior. Agree the actual policy/contract changes before
changing required checks or dispatch. R1/R2 then establish native delivery and asynchronous observation
through the current owner; R3 simplifies eligible publication/recovery; R4 evaluates the current route;
R5 separately proves ordinary v2 carryover. These are integration references, not new gates or work items.

Routine economics include planning, review, coordination, delivery, observer/tooling maintenance, cancelled
attempts and process-induced repair. The 10% objective and 20% ceiling use model overhead divided by
productive plus overhead model usage. Report absolute overhead, total cost per delivered unit, delivery
fraction and every over-budget item; a good average does not erase expensive failures. Cached input remains
counted once in total input-plus-output tokens. Money, runner-seconds and elapsed/active time remain separate.

The initial R4/R5 comparison is at least ten candidate and ten comparable baseline code items, with at least
95% independently assessed usage coverage and a conservative unknown-usage bound within the ceiling.
Unquantified gaps mean insufficient measurement. Ten items do not establish p95/p99 or rare-defect
non-inferiority. Charge 30-day escaped-regression and recovery observations to the originating cohort;
quality, delivered fraction and total cost remain part of promotion. A controller-specific percentile or
hard-cap claim still needs its own sufficient evidence. Telemetry loss does not reopen code delivery.

PB0 or H0 may reuse these observations and proceed independently on a demonstrable unmet need. They need
not wait for all R5 evidence to perform read-only research, but they cannot claim v2 savings from a current
migration baseline. Fund a bounded prototype only when a smaller supported route cannot meet the named
need. Account for maintaining the new service, inventory, verifier, UI and recovery machinery. Stopping
an unhelpful controller is a successful investment decision, not a reason to preserve expensive ceremony.

## 5. Carryover and completion boundaries

R5's real ordinary-v2 examples cover one code PR, same-PR repair, unrelated base advancement under the
chosen native policy, delayed projections, lost telemetry, applicable PR-less operations, clean and upgraded
receivers, and protected routing. Verify published tools and actual generated guidance. Keep code-delivered
and publication-pending facts distinct. Internal automatic journals may satisfy the examples without a
redesign; their runtime cost still needs measurement.

The existing GS2-10/12/13/14 candidate, closed-fleet, authorized live and observation journeys are proposed
integration points from the September 7 document. This analysis does not edit those gates or run a canary.
A current-route pilot, controller test suite or installed source checkout cannot stand in for that evidence.
After cutover, all fallback and emergency routes must respect the current epoch; no service failure or
budget saving can justify returning to a fenced v1 writer.

The accompanying edits amend both proposals' opening scope, internal applicability, investment roadmap and
adoption criteria. They retain the detailed stronger controller/harness designs as conditional research,
rather than claiming that the new routine profile already supplies their guarantees. No accepted ADR,
GS2 execution roadmap, schema, generated guidance, runbook or product code is changed. Adopting the routine
policy, implementing either optional service and approving their external effects remain separate work.
