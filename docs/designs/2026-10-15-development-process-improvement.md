# Development process improvement

Identity: **V3-IMPROVE-01**. Selected direction: **2026-10-15**.
Owner: `.github` programme maintainer, with existing Coordination, diagnostic and receiver owners.
Status: **design selected; implementation and observed benefit remain open**.
Parent: [Unified Roadmap v3](../roadmaps/2026-10-07-unified-development-roadmap-v3.md#2-open-work-and-owning-plans).
Delivery: [three bounded changes](../roadmaps/2026-10-15-development-process-improvement.md).

## Intended result

Reduce repeated qualification attempts and time to useful diagnosis while preserving the same
acceptance obligations. Use available capacity for independent work, finish installed diagnostic
adoption and make routine guidance and progress reports easier to consume. Evaluate the changes
in one ordinary development cohort and one cohort crossing repositories before wider adoption.

The user selected three changes, in order: **capacity and preflight practice**, **V2-DIAG-01.4
adoption**, then **skill and report simplification**. The lane coordinates this finite improvement
scope. Existing owners retain their implementation, publication and acceptance work. This design
introduces no parallel execution framework, telemetry service or fixed worker count.

Landing this design records the selected direction. It does not edit operational skills, change
required checks, activate automation, release packages or accept installed behavior. Those changes
follow the phased owning roadmap and existing operation-specific authority.

## Evidence and limits

The [15 October report](../reports/progressReports/2026-10-15-082258Z-unified-v3-progress.md)
has cutoff **2026-10-15 08:16:54 UTC**, with a trailing window beginning 14 October at the same
time. It records recovering source delivery, recurring qualification friction and no new whole
outcome closure: two of eighteen selected outcomes were closed. Its evidence supports historical
diagnosis at that cutoff, not claims about later milestones or current operational permission.
The [previous report](../reports/progressReports/2026-10-14-152503Z-unified-v3-progress.md)
retains the preceding expectations and their uncertainty.

At inspected protected revision `8ff42c8b07485c2a05164f3a19b14fdab75dd4e7`, the
[13 October report](../reports/progressReports/2026-10-13-025516Z-unified-v3-progress.md)
contains 46,542 bytes and the 15 October report contains 115,056 bytes. This observed growth
supports investigating duplicated report content; byte size alone establishes neither model cost
nor avoidable effort. Formal closure counts do not measure engineering completion, and incomplete
usage and effort attribution prevent a programme-wide savings or cost claim.

[Delivery reliability](../../.agents/skills/work-programme/references/delivery-reliability.md)
records earlier cases where diagnostics disappeared, fixtures missed the actual caller, cheap
binding defects reached expensive qualification and source delivery stopped short of installed
acceptance. Recheck applicability to the selected current cohort. Preserve original failed
attempts and unresolved effects rather than converting historical repair into current success.

## Ownership and reuse

| Existing owner | Retained responsibility | Contribution to this lane |
|---|---|---|
| [V2-EFF-01 and UTEL](../roadmaps/process-efficiency-telemetry.md) | Canonical observations, corrections, assessment, dashboard and bounded calibration | Supply available measurements and explicit gaps. This lane does not duplicate their reducers or dashboard implementation. |
| [V2-CTX-01](../roadmaps/2026-10-05-programme-context-efficiency.md) | Compact returns, evidence reuse, watcher/restart integration and coordination adoption | Own affected driver/context changes and retain original identities and reservations. |
| [V2-DIAG-01](../roadmaps/diagnostic-execution.md) | Diagnostic policy, qualified runner behavior and publication/installed adoption | Complete the existing open .4 outcome; do not reopen accepted .1–.3. |
| [V2-PROC-01](../roadmaps/2026-10-05-shared-process-supervision.md) | Execution contracts, qualified profiles and consumers | Supply enforcing execution mechanisms and applicable resource/cleanup evidence. |
| [LEARN-01](../roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md) | Controlled statistical comparison and learning | Join a separately selected causal or statistical savings claim; ordinary correctness adoption retains its existing route. |
| Programme integrator and component owners | Scope, conflicts, delivery, receiver assertions and original effects | Carry the three changes to observed use and an explicit retain/revise/retire disposition. |

Use existing lane packets, owner artifacts, acceptance records and telemetry. Measure necessary
ordinary work; missing telemetry remains a declared gap rather than a reason to create reporting-only
workloads or a new mandatory review layer.

## Change 1: capacity and final-candidate preflight

### Schedule against observed capacity

The supplied development envelope is **up to 12 CPU cores and 32 GiB memory**. These are
user-provided bounds for the selected environment, not a hardcoded scheduling constant or evidence
of current free capacity. Each scheduling window uses current available resources, active and
unknown reservations, measured peak demand and the actual enforcing profile. CPU, memory, CLR/process
limits, storage contention, agent slots and hosted CI capacity are distinct constraints.

Start the pilot with one local compile/qualification workload. Record its observed peak memory,
CPU demand, elapsed behavior and cleanup outcome. Admit a second workload only when its actual
source/build outputs are disjoint, shared-state constraints are satisfied, and remaining headroom
supports the combined measured demand. Include build servers, helper processes and transitive
compiler launches. If measurement or reservations are incomplete, retain the uncertainty and choose
a narrower window. Do not extrapolate two successful workloads into unrestricted fan-out.

Use SSD-backed active worktrees, compiler outputs and hot dependency/build caches where the
supplied environment provides a qualified path. Use HDD storage for colder archives, retained
artifacts and other less latency-sensitive data. Verify the real backing filesystem and available
space; a directory name does not prove its storage class. Moving active data must retain ownership,
permissions, original evidence and cleanup obligations. Storage placement does not establish process
containment or settle an old operation. Coordinate any shared storage/profile change with
[V3-ENV-01](../roadmaps/2026-10-13-provisioned-podman-execution.md).

### Check the assembled candidate before costly qualification

Run the cheapest relevant checks against the **final assembled candidate**, including its actual
entrypoint, generated bindings, dependency pins, workflow inputs, fixtures and evidence metadata.
Check identity, shape and required inputs together; passing isolated fragments does not qualify
the assembled caller. Compile the affected unit early enough to expose type/API errors before
large fixture preparation or hosted qualification, within its admitted execution profile.

Inert tests should exercise the maintained caller, adapters and real control flow while replacing
external transport or effects at the declared seam. Include successful flow and representative
failure, timeout, malformed response and cleanup/reporting cases. Expose the selected preflight through one usable local command with explicit prerequisites and
actionable failures; measure cold setup, warm execution and integration/repair overhead. A fake
callback with a different lifetime or a substitute launcher cannot establish the actual caller's behavior. Inert success
remains separate from native, installed and external-effect acceptance.

After **two similar failures** in the selected area, the same owner performs a bounded review of
the complete affected path before another costly attempt: callers, shared helpers, guards, fixtures,
bindings, failure handling and cleanup. Identify the repeated assumption and sibling instances;
repair the connected cause rather than iterate on the most recent line. Record findings in the
existing owner artifact. A broader architectural change returns to the normal planning boundary.
This trigger does not renew an expired attempt or permit replay of an unknown effect.

An enforcing preflight allows valid input to reach the dependent workload and blocks known-bad,
missing, malformed, timed-out or uninterpretable input. Its pass proves only its checked scope.
[ADR-0086](../adr/0086-proportionate-pipeline-preflight.md) selects static checks, focused tests or a
bounded model proportionately; no model is required for every pipeline. Preserve
[ADR-0084](../adr/0084-semantic-reuse-never-cancels-coherent-validation.md): cheap checks and semantic
reuse never cancel the selected coherent validation obligation or replace exact-head acceptance.

### Release actual scheduling bottlenecks

When declared paths collapse independent work into too few lanes, use
[lane-steward](../../.agents/skills/lane-steward/SKILL.md) to inspect over-broad declarations.
The owner proposes accurate narrowing and the existing typed lane/conflict tools compute its
consequences. Never narrow below the actual touch-set, change another owner's claim or infer safety
from free agent slots. Keep one accountable owner per lane and one integrator for shared surfaces.

Keep existing PR slots: one open PR per dependency chain and at most two managed PRs per repository
across campaigns. Prepare independent local work while hosted checks drain; repair active PRs before
adding queue pressure. Across repositories, qualify compatible immutable candidate joins and
explicit producer/consumer boundaries. Avoid repeatedly waiting for moving `main` where the owning
contract supports pinned candidates. Source compatibility still does not imply publication or adoption.

Before changing CI fan-out, measure the actual setup, restore/build, partition, aggregation,
artifact and cleanup costs. Examine compatible shared-build reuse and independently executable
partitions; verify that aggregation preserves every required assertion and failure. Apply
[ADR-0081](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md)
and ADR-0086 to investment and cadence decisions. Missing measurements prevent a savings claim;
they do not justify automatically increasing concurrency, dropping suites or weakening admission.

## Change 2: complete diagnostic adoption

The [diagnostic policy](../coordination/diagnostic-execution.md) and
[V2-DIAG-01 roadmap](../roadmaps/diagnostic-execution.md) already own this behavior. The roadmap
records .1–.3 accepted for the selected ordinary runner and packaged native pilot, while **.4
publication, installed adoption and evaluation remains open**. Its historical package/version
references must be reconciled against the current coherent producer and receiver facts before action.

Use the existing coherent release routes for affected contracts and skill carriers. Verify actual
installed bytes and that both selected pilot owners execute their real entrypoints: the ordinary
work-programme test runner and Coordination's packaged native diagnostic qualifier. Keep their
existing scopes; BAR, SC2 and LEARN product adapters are not silently added to this pilot.

Both owners must observe adopted behavior: collect independently checkable failures within original
bounds, block dependent effects after failed or unknown prerequisites, stop affected work on
shared-state or safeguard uncertainty, preserve first cause and additional findings, and report
execution, cleanup and reporting outcomes separately. Include a complete successful journey and
appropriate existing adversarial controls. **Failed or partial runs never qualify a candidate.**
A published package is not an installed receiver proof; a local packaged pilot is not public adoption.

Run a bounded ordinary-use evaluation of defect collection, repeated attempts, time to diagnosis,
diagnostic overhead and infrastructure failures. Preserve the earlier failed attempts and missing
baseline. The two owners record adoption and remaining limits in the existing DIAG acceptance record.
This lane references that closure once; it creates no duplicate DIAG completion checklist.

## Change 3: simplify skills and progress reports

Reduce repeated entrypoint text in `pnext-item` and `work-roadmap` by keeping their actionable route
and moving detailed, conditional material into linked references. Preserve complete applicable
instructions, authority boundaries, failure semantics and discoverability. References are loaded
when applicable; moving text is not permission to omit obligations or shorten them to fit a packet.
Use existing V2-CTX transport and bounded retrieval rather than a new context mechanism.

Maintain the repository's `.agents`/`.claude` mirror and skill registry/manifest coherence through
its established owner route. Verify applicability, reference resolution and mirrored delivery.
Source edits and byte reduction do not establish installed skill adoption or whole-family savings.
Any packaged carrier changes need their normal publication and receiver readback.

Make `show-progress` compact while retaining the full selected outcome population. Put current
acceptance, meaningful changes, remaining work, blockers, confidence and comparable estimates in one
scannable view; link detailed evidence and history. Avoid repeating the same narrative in several
sections. Retain complications, expectation changes, actual/estimated effort distinctions, scope
changes and all unknowns. A concise report must not drop difficult, blocked or unchanged items,
claim completion from PR counts or fabricate aggregate percentages.

Six pre-existing local edits affect the `.agents` and `.claude` `show-progress` entrypoint,
`estimation-method.md` and `report-template.md`. They remain untouched by this design delivery.
The later implementation owner must inspect and reconcile those edits before applying overlapping
simplifications, preserving the user's intended coverage and estimate semantics. No result in this
plan implies those edits have been reviewed, accepted or discarded.

## Pilot and decision rules

Before the first treatment, select one ordinary cohort and one crossing repositories from genuine
ready work. Freeze each population, owner, expected acceptance, evidence cutoff, comparison basis
and bounded observation window in its existing artifact. Choose work with comparable scope when
available; record differences and unavailable baseline rather than manufacture a control. The three changes have the selected landing/evaluation order for this lane. Independent DIAG
publication/adoption and skill/report source work may proceed against their real prerequisites;
phase ordering introduces no artificial gate between existing owners. Their accepted results become
inputs to the corresponding evaluation stage.

| Question | Observation | Interpretation limit |
|---|---|---|
| Are repeated attempts reduced? | Original attempts per accepted milestone, repeated failure causes and first-pass qualification | Retain failed/stopped/interrupted attempts; differing scope is not a causal comparison. |
| Is diagnosis faster? | First observed failure to first actionable diagnosis, missing diagnostic fields and extra attempts needed | Separate active repair from external waiting; missing start/end evidence stays unknown. |
| Is capacity useful? | Concurrent admitted workloads, sampled peaks, storage location, queue/wall duration and settled cleanup | Sampling is not a hard maximum; free cores and more workers do not establish throughput. |
| Is acceptance preserved? | Required assertions/checks, installed receiver results, false passes, unknown effects and ownership gaps | Partial success or missing evidence cannot count as a pass or as zero defects. |
| Is coordination/reporting cheaper? | Repeated reads, handoffs, report bytes and observed collection/analysis effort | Bytes are a proxy; whole-family token/cost claims need genuine coverage. |
| Does improvement exceed its overhead? | Necessary preparation, execution, review, diagnostic and reporting work | Report observed bases separately; do not add overlapping elapsed intervals or infer labor from runtime. |

The pass criterion is **fewer repeated attempts and faster useful diagnosis at the same acceptance
standard**, with **no increase in unknown effects, false passes or unowned work** in the declared
observed population. Count and inspect each adverse case; do not hide one behind a favorable average.
Numeric targets beyond these comparative criteria are not currently supported by a baseline.

A regression in acceptance, custody or ownership stops the affected treatment and invokes its
existing rollback/repair route. Missing or incomparable observations yield an inconclusive result,
not a claimed improvement. Independently proven correctness/reliability changes may be retained with
explicitly inconclusive economics under the existing CTX boundary. Wider rollout needs a recorded
retain/revise/retire disposition; causal statistical claims retain their separate LEARN requirements.

## Completion and workspace impact

The lane closes when all three bounded changes have their required source and selected installed
adoption, both cohorts have a durable evaluation and each intervention has a disposition. DIAG,
CTX and EFF closure remains owned by their existing plans; this lane cites their accepted contributions.
Future candidates do not automatically extend the frozen scope.

Planning changes no generated workspace. During implementation, identify each affected producer and
skill carrier, selected default or opt-in behavior, clean-creation impact and any promised retaining
upgrade. Verify published packages and installed readback separately. Public summaries retain approved
aggregates; private diagnostic chains and raw logs remain in restricted custody. Capture reusable
accepted lessons in Atlas with immutable sources, keeping operational receipts in their existing stores.
