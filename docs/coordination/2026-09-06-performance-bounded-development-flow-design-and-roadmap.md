---
title: "Design and roadmap: Performance-bounded, Quint-governed development flow"
category: Design
categoryindex: 4
index: 36
description: "A research-grounded configurable development controller in which safety, progress, latency, resource efficiency, and bounded coordination overhead are coequal hard requirements."
---

# Design and roadmap: Performance-bounded, Quint-governed development flow

This design makes useful delivery, bounded latency, resource efficiency, and bounded coordination overhead
coequal with correctness. A workflow that is safe but can accumulate unlimited planning, review, evidence,
or retry work is not acceptable: it has failed its liveness contract. Quint owns the legal workflow and its
finite budgets; versioned policy selects models, batching, qualification cadence, and scheduling within that
legal envelope; a pure controller chooses among admissible actions; and a durable executor performs effects.
Independent projections generate the static state diagram and live execution view from the same compiled
identities. The first implementation is deliberately small: one routine code-change flow, optimistic check
batching with failure isolation, bounded review/repair, capability-based agent routing, and exact-head delivery.

| Field | Value |
|---|---|
| Status | Proposed design and implementation roadmap; no runtime or lifecycle authority changes yet |
| Authored | 2026-09-06 |
| Prior-art review | 2026-09-06; primary research papers and official project/practitioner documentation available on that date |
| Primary risk | Correctness bureaucracy grows without bound until useful work stops |
| Governing direction | Quint-first semantic authority, pure policy, durable execution, generated projections |
| Builds on | [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md), [ADR-0079](../adr/0079-single-accountable-delivery-authority.md), [ADR-0080](../adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md), [ADR-0081](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md), and the [operations-research orchestration design](2026-08-31-operations-research-first-agent-orchestration-design.md) |
| Initial owners | FS-GG/.github: policy and registry; FS.GG.SDD: generic Quint profile/compiler/replay tooling; FS.GG.Coordination: canonical workflow model, controller, executor, domain replay, receipts, and projections |

### Review disposition — 2026-09-06

Proceed with a bounded prototype; production feasibility remains unproven. The review found the following
implementation-blocking gaps and incorporates their proposed resolution below. These are design changes,
not amendments to accepted policy or evidence that an implementation already satisfies the requirements.

| Finding | Resolution | Qualification boundary |
|---|---|---|
| Repair invalidates evidence and can increase the proposed lexicographic rank. | Use a non-renewable transition budget; bound scope, operation creation, and waits separately. | PB1/PB2: repair and repeated-observation traces |
| Receipts arrive after spending; concurrent dispatch can overcommit the same remaining balance. | Reserve enforceable upper bounds atomically before dispatch, including settlement headroom. | PB2/PB3: concurrent reservations and delayed receipts |
| An immutable terminal state can strand an effect that succeeds after timeout. | Separate the item outcome from durable effect settlement; retain fencing and recovery ownership. | PB3: crash, cancellation, late success, and takeover |
| Per-item hard limits and cohort percentiles are conflated; a tiny canary cannot establish p99. | Separate runtime caps from promotion SLOs, with fixed denominators and an insufficient-data verdict. | PB0/PB6/PB7: predeclared comparison protocol |
| Mandatory exact-candidate reuse conflicts with scoped reuse and mandatory cold closure. | Reuse gate artifacts by semantic subject; bind acceptance to the current candidate; cold boundaries take precedence. | PB3: unchanged subject and cold-closure fixtures |
| The roadmap omits durable delivery/recovery ownership and puts optional optimizations on the first path. | Assign domain semantics to Coordination, add executor acceptance, and separate the core canary from optional features. | PB1–PB7 |

## 1. Decision

Performance and efficiency are acceptance properties, not optional objectives applied after safety. The
development controller must satisfy five classes of hard requirement before a policy can be promoted:

1. **Safety:** no action bypasses an authority, evidence, security, exact-head, or recovery predicate.
2. **Progress:** admitted finite-scope work reaches a classified terminal outcome under its declared
   environment assumptions, and qualification preserves a minimum useful-delivery rate. Refusing every
   item satisfies neither useful progress nor policy promotion.
3. **Latency:** useful work and actionable evidence begin within configured bounds; a controller may not
   remain in planning or ceremony while executable work and capacity exist.
4. **Efficiency:** orchestration, model, CI, review, storage, API, and runner consumption stay inside explicit
   per-item and fleet budgets.
5. **Bounded bureaucracy:** every control action must satisfy a requirement, reduce decision-relevant
   uncertainty, preserve recoverability, or terminate the work. An action that does none is illegal.

Optimization occurs only inside the feasible set defined by those requirements. When correctness and a
performance budget cannot both be satisfied, the workflow does not weaken correctness or loop indefinitely;
it reaches `Quarantined`, `BudgetExhausted`, or `Refused` with a concise decision record.

### 1.1 What this changes in emphasis

The existing design correctly places hard constraints before objectives and already treats SDD as sequential
value of information, CI batching as an execution mode, and qualification cadence as adaptive. This design
makes the missing operational consequence explicit: **a policy that violates its latency, capacity, or
control-overhead envelope is infeasible even when its outputs are correct**.

It is therefore not acceptable to:

- add a checklist, review pass, generated artifact, receipt, or gate without a named obligation and measured
  cost;
- retry an indeterminate effect merely to make the workflow move;
- run every check sequentially because that makes failures easier to read;
- run every check in a separate process when compatible checks share dominant startup cost;
- require the same expensive qualification at every child boundary and again at cumulative closure;
- keep asking agents or reviewers after their bounded repair route is exhausted;
- count waiting, repeated context reconstruction, or artifact production as useful progress; or
- improve average throughput by allowing tail latency, review queues, retry amplification, or recovery
  headroom to become unbounded.

## 2. Scope and non-goals

The first delivery covers the lifecycle of one finite, admitted work item from a complete observation through
implementation, qualification, critique, bounded repair, and terminal delivery/refusal. It includes model
selection, check planning, optimistic batching, evidence reuse, review/fix control, performance receipts, and
visual projections.

The first delivery does not:

- optimize a whole organization backlog;
- learn workflow topology online;
- let an LLM edit hard constraints or its own acceptance corpus;
- prove that a provider, GitHub, runner, or human will become available;
- require a continuously available hosted orchestrator;
- replace GitHub/Git or the typed coordination engine as external mutation authority; or
- make a visualization, prediction, or agent confidence score authoritative.

### 2.1 Prior-art research method and limits

The design was checked against primary research papers, official tool documentation, and first-party
industrial experience across formal workflow analysis, continuous delivery, build and test optimization,
code review, durable execution, developer productivity, and LLM-based software engineering. The review
looked for three things: the mechanism that produced a benefit, the failure or adoption problem reported,
and the condition under which the result should transfer to this system.

The sources are heterogeneous. Controlled trials support stronger causal claims than surveys; industrial
case studies show scale but may not generalize; benchmark results measure their benchmark and not this
repository; vendor engineering reports are useful implementation evidence but not neutral comparisons.
No percentage below is adopted as a universal target. Each is a hypothesis to replay, shadow, or canary
against an observed local baseline.

### 2.2 What prior systems teach us

| Prior-art family | What worked | Problems encountered | Consequence for this design |
|---|---|---|---|
| Workflow nets and formal state machines | Workflow-net soundness separates ability to complete, proper completion, and absence of dead transitions. Model checking exposes counterexample paths before runtime. | Soundness variants are not interchangeable; liveness needs environment/fairness assumptions; richer models face state explosion. | Check terminal stability, dead actions, anti-vacuity witnesses, and bounded rank separately. Keep finite qualification profiles and state assumptions explicitly. |
| Industrial formal methods | AWS reports that lightweight, high-level specifications and model checking found subtle design errors in systems where testing and conventional reasoning were insufficient. | A model is an abstraction, not implementation proof; learning and maintaining the right abstraction costs engineering time. | Use Quint on the small decision kernel, replay traces through the real reducer, mutation-test the properties, and refuse speculative detail that does not change a decision. |
| Continuous integration and small batches | Frequent mainline integration, fast automated feedback, and small independently testable changes reduce the search space and cost of resolving conflicts. | Slow builds and pre-integration review queues destroy the feedback advantage; calling branch builds “CI” does not remove delayed integration. AI makes oversized changes easier to create. | Make work-item size and time-to-first-evidence visible, prefer independently deliverable slices, and do not let the controller accumulate a large hidden batch before downstream validation. |
| Test selection, batching, and failure isolation | Meta reported catching more than 99.9% of regressions while running about one third of transitively dependent tests with learned selection. A 276-million-result batching study found dynamic and test-case batching could hold feedback time with substantially fewer machines. | Selection can miss defects, historical data drifts, flaky failures corrupt learning, and a failed batch obscures the culprit. Parallelism has nonlinear diminishing returns. | The initial system may rank but not silently skip soundly selected obligations. Optimistically batch high-pass compatible checks, calibrate continuously, and bisect only attribution-ambiguous members within a fixed isolation budget. |
| Incremental and cached builds | Microsoft CloudBuild reported 1.3x–10x speedups using content-based caching and distributed execution; Bazel makes actions and outputs addressable by declared inputs. | Under-declared inputs, nondeterministic tools, host leakage, and concurrent input mutation can produce unsafe or useless cache entries. | Cache keys include all declared semantic and execution inputs; qualify reproducibility, record miss reasons, sample cache hits with sentinels, and quarantine a suspect cache rather than trusting “green.” |
| Gated and speculative integration | GitHub merge queues test the merge group against current target state. Zuul parallelizes a dependency-ordered gate by assuming predecessors pass, then invalidates and reruns affected successors after a failure. | Speculation can discard large amounts of work; missing required check events can stall a queue; broad dependency chains amplify invalidation. | Bind every result to exact candidate and predecessor identities, cap speculative depth/waste, cancel superseded work, reserve recovery capacity, and always expose a non-speculative fallback. |
| Modern code review | Google’s nine-million-change study describes a genuinely lightweight practice; Microsoft found review creates knowledge transfer and alternative solutions as well as defects. Small changes and fast responses aid understanding. | Understanding the change is the dominant challenge. Review latency interrupts flow, large changes reduce useful feedback, and polish can turn into unbounded back-and-forth. | Present compact intent, semantic diff, tests, and unresolved risk together; make nits non-blocking; re-review affected deltas and sibling risk rather than the whole change; bound review rounds and wait time. |
| Durable workflow engines | Durable execution persists progress and replays after process or infrastructure failure; activities isolate fallible effects and can retry. | Replay requires deterministic workflow code, effects need idempotency or observation, version changes need compatibility, and histories/resource use can grow without bounds. | Persist intent before effects, use idempotency keys and observe-before-retry, version policy/workflow identities, compact only at modeled boundaries, and impose history/storage budgets. Durability does not define correctness. |
| Developer productivity research | SPACE shows productivity is multidimensional. DevEx identifies feedback loops, cognitive load, and flow state. DORA links small batches and robust testing to delivery performance and treats AI as an amplifier of the surrounding system. | Activity or output counts are gameable. Faster code generation can shift work into verification, destabilize delivery, or increase reviewer cognitive load. Self-reported speed can diverge from measured completion time. | Optimize a vector of delivery, quality, human-attention, and resource measures. Measure author and reviewer toil, wait, rework, and interruption; never qualify on tokens, commits, lines, or subjective speed alone. |
| Simple LLM workflows and coding agents | SWE-agent shows tool/interface design materially affects repository-task performance. Agentless achieved competitive SWE-bench Lite results with fixed localization/repair/validation stages and low reported cost. Simple composable workflows are repeatedly reported as easier to debug. | Repository context and task descriptions are incomplete; benchmark contamination and weak tests can misstate success; autonomy increases latency, cost, and debugging surface. METR’s early-2025 randomized trial found experienced maintainers took 19% longer with AI despite expecting a speedup. | Start at deterministic automation, then one bounded model call, then a fixed workflow, and use an autonomous agent only when evidence justifies it. Validate with exact repository tests and measured end-to-end time, not model confidence or benchmark rank. |
| Model routing and cascades | RouteLLM and FrugalGPT show that calibrated routing/cascades can improve cost-quality tradeoffs on evaluated tasks. | Router data can be out of domain or stale; cheapest-first escalation adds latency when the first choice predictably fails; benchmark quality is not delivery quality. | Route on qualified capabilities and local outcome distributions. Charge escalation latency and rework to the original route, keep a direct-strong route for known-hard classes, and expire bindings when models or workloads drift. |
| Multi-agent coordination | Independent parallel search can increase coverage, and specialized workers can be complementary. Recent multi-agent experiments show coordination can sometimes sustain long-running search. | Dependent software work produced conflicts, abandoned PRs, low merge fractions, conformity failures, and resource floods; adding role prompts or hierarchy alone did not fix product quality. | Parallel agents are opt-in only for disjoint touch sets or independent evidence. Enforce unique identities, leases, WIP limits, bounded messaging/polling, one accountable integrator, and compare against a single-agent baseline. |

### 2.3 Primary evidence register

The following sources are the evidence base for the table and the changes below:

- W.M.P. van der Aalst et al., [Soundness of workflow nets: classification, decidability, and analysis](https://link.springer.com/article/10.1007/s00165-010-0161-4) (Formal Aspects of Computing, 2011).
- Quint, [Checking properties](https://github.com/quint-co/quint/blob/main/docs/content/docs/checking-properties.mdx) and [What does Quint do?](https://quint.sh/docs/what-does-quint-do), including the current limits of temporal-property tooling.
- C. Newcombe et al., [How Amazon Web Services uses formal methods](https://www.amazon.science/publications/how-amazon-web-services-uses-formal-methods) (CACM, 2015).
- Martin Fowler, [Continuous Integration](https://martinfowler.com/articles/continuousIntegration.html) (updated 2024).
- DORA, [Working in small batches](https://dora.dev/capabilities/working-in-small-batches/), [2025 State of AI-assisted Software Development](https://dora.dev/research/2025/dora-report/), and the 2026 analysis [Balancing AI tensions](https://dora.dev/insights/balancing-ai-tensions/).
- M.-A. Storey et al., [The SPACE of Developer Productivity](https://www.microsoft.com/en-us/research/publication/the-space-of-developer-productivity-theres-more-to-it-than-you-think/) (ACM Queue, 2021), and A. Noda et al., [DevEx: What Actually Drives Productivity?](https://doi.org/10.1145/3610285) (CACM, 2023).
- C. Sadowski et al., [Modern Code Review: A Case Study at Google](https://research.google/pubs/modern-code-review-a-case-study-at-google/) (ICSE SEIP, 2018), and A. Bacchelli and C. Bird, [Expectations, Outcomes, and Challenges of Modern Code Review](https://repository.tudelft.nl/record/uuid%3Ad629803b-bbec-4593-a7f2-6f4b2266ff5a) (ICSE, 2013).
- Meta Engineering, [Predictive test selection](https://engineering.fb.com/2018/11/21/developer-tools/predictive-test-selection/) (2018), and E. Fallahzadeh et al., [Accelerating Continuous Integration with Parallel Batch Testing](https://arxiv.org/abs/2308.13129) (2023).
- C. Ziftci and D. Cavalcanti, [De-Flake Your Tests](https://research.google/pubs/de-flake-your-tests-automatically-locating-root-causes-of-flaky-tests-in-code-at-google/) (ICSME, 2020).
- H. Esfahani et al., [CloudBuild: Microsoft's Distributed and Caching Build Service](https://www.microsoft.com/en-us/research/publication/cloudbuild-microsofts-distributed-and-caching-build-service/) (ICSE SEIP, 2016), and Bazel, [Remote caching](https://bazel.build/remote/caching).
- GitHub, [Managing a merge queue](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue), and Zuul, [Pipeline managers and speculative gating](https://zuul-ci.org/docs/zuul/latest/config/pipeline.html).
- Temporal, [Activity execution and cancellation](https://docs.temporal.io/activity-execution) for timeout, retry, and cancellation boundaries.
- J. Yang et al., [SWE-agent: Agent-Computer Interfaces Enable Automated Software Engineering](https://arxiv.org/abs/2405.15793), and C.S. Xia et al., [Agentless: Demystifying LLM-based Software Engineering Agents](https://arxiv.org/abs/2407.01489).
- S. Kapoor et al., [AI Agents That Matter](https://arxiv.org/abs/2407.01502) (TMLR, 2025), and METR, [Measuring the Impact of Early-2025 AI on Experienced Open-Source Developer Productivity](https://metr.org/Early_2025_AI_Experienced_OS_Devs_Study-paper.pdf) (2025).
- Anthropic, [Building effective agents](https://www.anthropic.com/engineering/building-effective-agents) (2024) and [Patterns and problems in multiagent systems](https://www.anthropic.com/research/multiagent-systems) (2026).
- I. Ong et al., [RouteLLM](https://arxiv.org/abs/2406.18665) (ICLR, 2025), and L. Chen et al., [FrugalGPT](https://arxiv.org/abs/2305.05176) (TMLR, 2024).

## 3. Requirements

### 3.1 Functional requirements

| ID | Requirement |
|---|---|
| FLOW-001 | One canonical literate Quint source declares states, actions, guards, budgets, terminal outcomes, invariants, and environment assumptions. |
| FLOW-002 | A pinned compiler emits a small versioned contract containing stable state/action/property identities, reads/writes, evidence obligations, and projection facts; generated artifacts are not coequal authority. |
| FLOW-003 | A pure reducer and planner consume only canonical state, classified observations, a versioned policy, and recorded time/random inputs. Each action requires complete facts for its own guards; unknown unrelated facts do not block observation or recovery. |
| FLOW-004 | Provider/model names are resolved through capability profiles outside the Quint topology. Changing a provider binding does not silently change workflow semantics. |
| FLOW-005 | Every external effect persists intent before execution, revalidates its preconditions, and records a verified outcome or an explicit indeterminate state. |
| FLOW-006 | Every candidate change invalidates older-head review and qualification evidence according to its semantic subject. |
| FLOW-007 | Findings have stable identities, severity, semantic subjects, disposition, and recurrence linkage. Duplicate prose cannot create duplicate repair obligations. |
| FLOW-008 | Scoped child qualification and comprehensive parent/release closure remain distinct execution profiles. |
| FLOW-009 | Static and live visualizations are generated from compiled/runtime projections with source, policy, and freshness fingerprints. |
| FLOW-010 | Each work class defines a complexity ladder: deterministic tool, single model call, fixed model workflow, single autonomous agent, then bounded multi-agent execution. The planner selects the least complex qualified level and records why escalation is needed. |
| FLOW-011 | Parallel workers require disjoint declared touch sets or independent evidence duties, unique identities, leases, bounded communication, and one accountable integration authority. |
| FLOW-012 | Exact-candidate invalidation covers code, target-branch predecessors, toolchain, environment, policy, and review epoch; a stale green result cannot authorize a newer composition. |

### 3.2 Nonnegotiable performance and efficiency requirements

| ID | Requirement |
|---|---|
| PERF-001 | Every admitted item binds absolute budgets for planning latency, dispatch latency, agent attempts, repair rounds, review rounds, tokens, money, runner time, API use, retained bytes, and wall-clock deadline. No unbounded or omitted value is valid. |
| PERF-002 | The controller is work-conserving: when a legal useful action and required capacity exist, it must dispatch or terminate within `maxDispatchLatency`; it may not remain in analysis or planning. |
| PERF-003 | Every control-plane action is classified as obligation closure, information acquisition, recoverability, or termination. Unclassified administrative work is rejected. |
| PERF-004 | Required checks are selected by sound semantic-subject closure. Execution may batch, parallelize, cache, speculate, or reorder them, but may not silently remove an obligation. |
| PERF-005 | Compatible, high-pass-probability checks use optimistic batching when expected setup/queue savings exceed bounded failure-isolation cost. Sequential execution is a failure-localization fallback, not the default. |
| PERF-006 | A failed batch is isolated adaptively. The executor reruns only failed, indeterminate, or attribution-ambiguous partitions; it does not automatically replay the complete batch one check at a time. |
| PERF-007 | Side-effect-free downstream checks may start speculatively before all upstream checks finish when cancellation cost and capacity stay within policy. Their result cannot authorize delivery until prerequisites pass. |
| PERF-008 | In scoped mode, reuse a validated artifact when gate contract, semantic subject, toolchain, environment, applicable policy, and expiry match. Bind it into a new current-candidate manifest with its original provenance. Mandatory cold boundaries, sentinels, and suspect-cache recovery override reuse. Other repeated equivalent checks require a recorded reason. |
| PERF-009 | All internal rework cycles consume a monotonically decreasing budget and end in success, refusal, quarantine, cancellation, or budget exhaustion. |
| PERF-010 | Capacity policy reserves explicit headroom for recovery and high-priority failure isolation. Nominal utilization may not consume that reserve. |
| PERF-011 | Qualification records p50/p95/p99 latency, queue delay, setup duplication, cache/reuse, cancellation waste, retry amplification, and control-plane share. Averages alone cannot qualify a policy. |
| PERF-012 | A candidate policy must beat or remain within the accepted baseline envelope in replay, simulation, shadow, and canary evidence. A faster policy that weakens a hard property is infeasible; a safer policy that breaches the accepted performance envelope is also infeasible. |
| PERF-013 | Telemetry and visualization are asynchronous projections. Their failure may degrade observability but cannot block an otherwise authorized development transition unless the missing receipt is itself a declared acceptance obligation. |
| PERF-014 | Every new mandatory artifact or gate names its consumer, decision changed, expected unique-defect yield, execution cost, expiry/review date, and deletion condition. Missing metadata refuses promotion. |
| PERF-015 | Admission and parallelism are WIP-limited by bottleneck capacity and recovery reserve. More ready work does not authorize a worker, PR, check, or polling storm. |
| PERF-016 | Qualification includes developer/reviewer active time, wait time, interruptions, context reconstruction, and perceived ease alongside delivery and compute measures. No single activity or output metric can qualify a policy. |
| PERF-017 | Escalation cost includes all failed lower-tier calls, verification, rework, and latency. A router cannot appear efficient by charging only the final successful model. |
| PERF-018 | Cache and selected-test savings are accepted only with reproducibility checks, complete-key validation, miss/flake classification, and a configured sentinel/full-closure cadence. |

### 3.3 Starter performance envelope

Policy values must ultimately be calibrated from fleet telemetry. The values below are proposed starting
points, not a complete deployable envelope. PB0 must supply every missing absolute cap, unit, observation
window, and sample sufficiency rule before admission is enabled.

| Budget | Routine child change | High-risk or closure change |
|---|---:|---:|
| Pure planning/verifier p95 | 2 seconds | 5 seconds |
| Time from complete admission to first useful dispatch p95 | 10 seconds | 30 seconds |
| Time to first actionable check result p95, including queueing and outages | 5 minutes | 10 minutes |
| Initial implementation attempts, before first accepted candidate | 2 | 3 |
| Ordinary repair rounds | 2, then deep-dive | 2, then deep-dive |
| Total repair rounds, including any repair after deep-dive | 3 | 4 |
| Critique/confirmation epochs, including initial critique | 4 | 5 |
| Consecutive infrastructure retries per operation | 2, observe before each | 2, observe before each |
| Recovery capacity reserve | at least 15% of relevant constrained capacity | at least 20% |
| Control-plane compute share over a fixed cohort window | at most 5% of total attributed runner-seconds | at most 8% |
| Speculative cancellation waste over the same window | at most 10% of total attributed runner-seconds | at most 5% |

A deployment may tighten these values. Loosening one is a versioned policy change with replay, simulation,
shadow, and canary evidence. An item may bind a smaller resource budget; it may not omit the field. A
deadline expiration stops new work and follows the settlement rules in §8.2; it never silently grants more
time or asserts that an outstanding mutation did not happen.

### 3.4 Enforcement and measurement semantics

An initial implementation attempt, a repair dispatch, and a critique epoch debit distinct counters plus the
shared transition/resource allocation. Deep-dive can occur at most once per item and grants no new repair
rounds. The second related late-stage defect triggers it even if the ordinary repair threshold has not been
reached. Every resulting candidate still needs its required current-head checks and confirmation; if their
remaining budget cannot be reserved, stop rather than granting an extra round or omitting confirmation.

**Runtime caps** are per-item absolute limits enforced before dispatch: planning and dispatch deadlines,
controller transitions, observation polls, total effect attempts, isolation runs, review/repair rounds,
tokens by provider-defined unit, money in a named currency, runner-seconds, API calls, retained bytes, and
overall deadline. Each external call has a timeout and enforceable maximum charge. Fleet admission reserves
capacity atomically, using integer slots; on a one-slot runner pool a fractional reserve requires scheduled
headroom or separate recovery capacity. A reserve that rounds to zero is not available recovery capacity.

**Promotion SLOs** are distributional acceptance criteria over a fixed eligible cohort. A p95 target is not
an individual timeout and does not specify a p99 bound. PB0 pins separate absolute runtime caps, the required
percentile targets, and the maximum tolerated exceedance rate. Cohorts with insufficient tail observations
remain `insufficient-data`; they cannot receive a performance-qualified verdict from a handful of passes.

Measure demand from the first eligible request as well as admission: admission delay, refusal rate, delivered
fraction, and total cost per eligible request prevent a controller from winning by refusing difficult work
or moving its queue before the admission timestamp. Freeze work-class definitions before comparison. Useful
dispatch starts implementation or an obligation-relevant check; polling, a progress message, and context
formatting do not reset that clock. Actionable evidence identifies a completed required check or a specific
failure that can change the next decision. Report first result and first red separately.

Charge orchestration compute, including off-runner compute converted under a pinned accounting rule, to the
control numerator. Required check execution belongs in the total denominator and is also reported separately;
labeling an action “required” cannot remove planning, receipt, or telemetry work from overhead. Report absolute
costs beside ratios, including zero-denominator cases. Record total elapsed latency including outages; an
outage-adjusted diagnostic may supplement it under a predeclared classification rule. Optional surveys and
human-attention samples are cohort research, never synchronous delivery gates.

## 4. Authority and configuration layers

The system has four deliberately different kinds of configuration:

| Layer | Examples | Change procedure |
|---|---|---|
| Semantic protocol | states, actions, guards, terminal outcomes, invariants, budget-decrease rules | edit literate Quint; typecheck, simulate, model-check selected profiles, semantic diff, review |
| Safety/performance envelope | maximum rounds, required closure classes, minimum reserve, maximum dispatch latency | versioned policy; cannot exceed compiled hard bounds; replay/simulation/shadow/canary |
| Optimization policy | batch threshold, partition heuristic, speculative start rule, queue objective, cache preference | versioned policy with independent feasibility verification and performance comparison |
| Provider bindings | model ID, reasoning level, region, price, context/tool limits, availability | capability registry update; qualification within the already-accepted profile contract |

This keeps rapidly changing provider names out of the formal state space while ensuring that switching a
model cannot bypass a capability, cost, privacy, or recovery requirement.

Illustrative policy fragment, not an executable configuration or complete schema. Units and remaining caps
must be supplied by the PB0 contract; no consumer may accept this fragment as an admission policy:

```yaml
schema: fsgg.development-policy/v1
policyId: routine-code-change/1

promotionTargets:
  planningP95Ms: 2000
  dispatchP95Ms: 10000

budgets:
  controllerTransitions: 500
  observationPolls: 30
  effectAttempts: 30
  isolationRuns: 16
  implementationAttempts: 2
  repairRoundsBeforeDeepDive: 2
  totalRepairRounds: 3
  infrastructureRetriesPerEffect: 2
  recoveryCapacityPercent: 15
  runnerSeconds: 3600
  tokenUnits: 100
  deadlineMinutes: 120

checks:
  selection: semantic-subject-closure
  execution: optimistic-batch
  minBatchPassProbability: 0.80
  failureIsolation: adaptive-bisect
  allowSpeculation: side-effect-free-only
  maxSpeculativeWastePercent: 10
  evidenceReuse: required-when-identical

routing:
  specification: formal-reasoning
  implementation: balanced-coding
  critique: strong-fresh-critic
  repair: balanced-coding
  deepDive: strongest-architecture
```

The schema validator rejects missing budgets, negative or internally inconsistent values, a batch policy
without deterministic failure isolation, speculation over effectful operations, a profile lacking required
capabilities, or a configuration outside compiled safety/performance bounds.

## 5. Workflow model

### 5.1 State sketch

The initial Quint type sketch should model one work item coordinating through durable shared state and
external observations. It does not need Choreo: the protocol has one accountable controller; agents,
reviewers, CI, and GitHub are nondeterministic external operations rather than peer protocol authorities.

```text
WorkflowState = {
  phase: Observing | Planning | Implementing | Checking | Reviewing |
         Repairing | DeepDive | Delivering | Reconciling | Terminal,
  candidate: CandidateRevision?,
  observations: ObservationSet,
  obligations: Set[ObligationId],
  evidence: EvidenceId -> EvidenceState,
  findings: FindingId -> FindingState,
  batches: BatchId -> BatchState,
  controllerStepsRemaining: int,
  recoveryStepsRemaining: int,
  observationPollsRemaining: int,
  effectAttemptsRemaining: int,
  isolationRunsRemaining: int,
  attemptsRemaining: int,
  repairRoundsRemaining: int,
  reviewRoundsRemaining: int,
  infrastructureRetriesRemaining: OperationId -> int,
  resourceBudget: ResourceBudget,
  reservations: ReservationId -> ReservationState,
  effects: EffectId -> EffectState,
  ownerGeneration: int,
  deadline: RecordedTime,
  terminal: Delivered | Refused | Quarantined | BudgetExhausted | Cancelled | None
}
```

Time is admitted only through explicit durable ticks or provider observations; accepted time cannot move
backward. Upper-bound resource reservations are committed before dispatch and reconciled with verified usage
receipts afterward (§8.1). Neither the Quint model nor pure reducer reads a wall clock, provider dashboard,
or mutable price table directly. This is a conceptual state sketch; finite profile bounds also cap findings,
candidate generations, operations, batches, evidence, and history entries.

### 5.2 Principal actions

| Action | Guard | Main effect |
|---|---|---|
| `Observe` | required fact incomplete or effect indeterminate; poll/step budget remains | record complete, partial, unavailable, or indeterminate facts; only sufficient facts authorize the dependent action |
| `Plan` | observations sufficient and planning budget remains | select and independently verify a bounded next-action set |
| `DispatchImplementation` | implementation legal, capacity available | consume attempt reservation and create immutable agent specification |
| `AcceptCandidate` | agent output validates and touch set is respected | mint candidate revision and invalidate affected older evidence |
| `StartCheckBatch` | selected obligations share a compatible execution class | persist exact membership and start one batch |
| `RecordBatchPass` | complete successful result matches batch identity | satisfy each member obligation with individually addressable evidence |
| `RecordBatchFailure` | failure, partial, or indeterminate result matches batch | preserve proven results; classify findings and create bounded isolation partitions when needed |
| `IsolateFailure` | attribution ambiguous and isolation budget remains | split/rerun only the ambiguous partition |
| `StartReview` | required current-head checks satisfied | mint fresh critique epoch and consume review budget |
| `AcceptFindings` | current-head critique completed | deduplicate/classify findings and choose repair, deep-dive, or acceptance |
| `Repair` | accepted material finding and repair budget remains | dispatch bounded repair and consume a repair round |
| `DeepDive` | second related late-stage defect | inspect architecture/invariants/sibling paths and update fault model |
| `Deliver` | exact-head obligations, owner authority, fencing, and settlement reserve satisfied | persist intent and reservation; dispatch through the typed engine; settle by external observation |
| `ReconcileEffect` | pending/indeterminate effect; settlement budget remains | observe outcome without duplicating the mutation; retain unresolved effects under recovery ownership |
| `Terminate` | terminal condition and effects settled or durably handed to recovery | freeze the item classification; prohibit new item work while preserving pending-effect ownership |

### 5.3 Formal properties

The qualification model must include at least these named properties:

**Safety**

- `deliveryRequiresExactHeadEvidence`
- `incompleteObservationNeverAuthorizes`
- `indeterminateEffectObservedBeforeRetry`
- `reviewEpochMatchesCandidate`
- `batchPassSatisfiesOnlyExactMembers`
- `speculationNeverAuthorizes`
- `resourceUseNeverExceedsBoundBudget`
- `spentPlusReservedNeverExceedsAllocation`
- `terminalItemNeverOrphansEffect`
- `staleOwnerCannotDispatchEffect`
- `duplicateReceiptCannotReleaseReservationTwice`
- `hardObligationCannotBeOptimizedAway`

**Bounded bureaucracy and termination**

- `everyInternalCycleConsumesBudget`
- `administrativeActionHasDeclaredPurpose`
- `noFindingCreatesDuplicateObligation`
- `noUnboundedRepairPath`
- `terminalStateIsStable`
- `exhaustionHasTerminalRoute`
- `enabledUsefulWorkCannotStutterPastDispatchBound`

**Reachability/anti-vacuity witnesses**

- delivery without repair;
- one batched pass satisfying several checks;
- batch failure followed by successful partition isolation;
- speculative work cancelled after prerequisite failure;
- review finding, repair, requalification, and delivery;
- repeated related defect entering deep-dive;
- infrastructure failure exhausting retry budget;
- terminal quarantine on resource exhaustion; and
- recovery from an indeterminate external effect.

### 5.4 Honest termination claim

An obligation-count-first rank is invalid here: accepting a repair can invalidate passing checks and increase
the number of unsatisfied obligations. Dispatch can likewise increase unsettled effects. Use an explicit,
non-renewable control-transition budget as the initial termination measure:

```text
rank = controllerStepsRemaining + recoveryStepsRemaining + (if terminal then 0 else 1)
```

Every admitted transition before terminal consumes one step from its permitted allocation, including planning,
new observations, candidate invalidation, and replanning after duplicate findings; terminal classification
decreases the final indicator. When the ordinary allocation reaches zero, only settlement/handoff and terminal
classification are legal, under the separately pre-reserved finite recovery allocation. Duplicate transport events
are idempotent no-ops; ingress limits and accounting still bound their processing cost. Specific attempt,
repair, review, isolation, and polling counters impose tighter limits without replenishing the global budget.
Continuation, restart, provider rebinding, and a new operation ID cannot reset these counters.

Freeze the requirement set and allowed semantic scope at admission. Findings may refine repairs within that
scope under finite finding/round limits; new requirements require new admission. A resumed or replacement
item retains its lineage and cumulative cost in the fleet comparison, so splitting or reopening work cannot
launder exhaustion. Evidence invalidation is allowed to increase outstanding work because it cannot increase
the remaining transition budget.

Waiting does not decrease the rank by itself. Bounded wall-clock response additionally assumes that a durable
scheduler or recovery watchdog eventually delivers deadline ticks within a declared service bound. An
offline controller cannot enforce an elapsed-time promise; on restart it first processes the expired deadline.
External availability is needed for delivery and final effect resolution, not for classifying the item as
quarantined with durable unresolved-effect ownership. The claim is bounded controller work and conditional
terminal classification, not unconditional external completion.

This claim is deliberately split into independently checkable obligations because current Quint guidance
notes that invariants are the mature path while temporal-property support is partial and liveness commonly
depends on fairness assumptions:

1. **Finite-domain safety:** no negative counter, illegal delivery, orphan effect, unstable terminal state,
   or dead nonterminal state in each declared finite profile.
2. **Rank preservation:** every admitted state-changing transition decreases the remaining-step rank;
   repair may invalidate evidence but cannot refill either allocation. Terminal classification decreases
   the final indicator; journal updates after terminal belong to the separate recovery protocol.
3. **Bounded waiting:** each wait has an explicit deadline and an assumed bounded tick/restart service;
   unresolved effects are preserved under §8.2 rather than declared absent.
4. **Conditional liveness:** where temporal checking is supported, named weak/strong fairness assumptions
   are applied only to the environmental actions that justify them and are tested with witness scenarios.
5. **Runtime correspondence:** compiled identities and ITF traces are replayed through the production
   reducer, so a proof about the model is not presented as proof about unrelated executor code.

The primary termination evidence is therefore finite scope, decreasing budgets, deadline exits, deadlock
checks, and runtime correspondence. Record the pinned tool's actual supported checks, domain bounds, search
depth, elapsed time, and explored-state count when available. Distinguish sampled simulation, bounded symbolic
checking, complete finite-state exploration, and inductive proof. A bounded Apalache search without a
counterexample does not establish all reachable states, even when variable domains are finite. This follows
the distinctions in [Quint's checking guide](https://quint.sh/docs/checking-properties); moving documentation
does not establish the capabilities of the repository's pinned toolchain.

## 6. Optimistic batching and adaptive failure isolation

### 6.1 Preserve obligations; optimize execution shape

The planner first computes a sound closure of required checks. Batching changes how those checks execute,
not whether they are required. It groups checks only when they share all load-bearing execution inputs:

- candidate and semantic subject;
- toolchain, environment, permissions, and secrets class;
- setup/restore/build prefix;
- compatible constants, bounds, seed policy, and timeout;
- artifact-retention and data-classification rules; and
- failure output sufficient to identify a failed member or drive deterministic isolation.

A successful batch emits one envelope plus an addressable result for every member. A green process with a
missing member result is incomplete, never successful. Compatibility also requires isolated mutable work
directories or a qualified reset protocol: shared setup alone does not establish that checks are independent.

### 6.2 Selection rule

For a candidate batch `B`, the controller estimates:

```text
BatchValue(B) = avoided setup + avoided queue delay + avoided artifact transfer
              - P(any failure) * expected isolation cost
              - expected cancellation waste
              - attribution and retry amplification penalty
```

The batch is selected only when it is feasible, `BatchValue(B)` exceeds the configured robustness margin
across credible estimate ranges, and the deterministic isolation plan fits the remaining budget. Sparse or
uncalibrated history produces a conservative prior; it does not become an invented high pass probability.

Batch size is dynamic rather than a global constant. The controller considers queue age, arrival rate,
shared setup cost, observed failure correlation, available isolation capacity, and the nonlinear point at
which more parallel machines stop improving feedback. It may wait only up to a small configured coalescing
window; an old or high-priority item dispatches without waiting for an “efficient” batch. Learned test
selection may order members or choose early-result sentinels, but the initial implementation cannot remove a
required member unless a separately accepted sound closure rule proves it irrelevant.

For the first implementation a batch contains checks for one candidate, never changes from several items.
The numerical test-batching research in §2 is supporting evidence for an experiment, not a measured gain for
this executor. In that single-item case, do not wait for unrelated arrivals. Express `BatchValue` terms in a
declared common unit or compare separate latency/cost estimates; raw seconds and currency cannot be added.
Estimate joint failure probability with correlation rather than multiplying unqualified marginal pass rates.

### 6.3 Failure path

On failure:

1. classify the result as deterministic finding, infrastructure failure, timeout, cancellation, or
   indeterminate;
2. retain all individually proven passing member results when the runner contract makes attribution sound;
3. form the smallest attribution-ambiguous partition;
4. split that partition using historical failure correlation, dependency boundaries, and approximately
   balanced expected diagnostic cost;
5. run partitions in parallel when capacity permits;
6. continue until each failure is attributed or the isolation budget is exhausted; and
7. enter repair or quarantine with the evidence already learned.

Simple binary splitting is the safe baseline. A learned partitioner may improve the split, but its output is
verified and it cannot merge across incompatible execution identities. Sequential execution is reserved for
an indivisible partition, a check whose order is semantically required, or a resource-constrained final
diagnostic—not used as the automatic response to any batch failure.

If the parent batch fails but every isolated partition passes, retain an interaction/flake finding. Do not
erase the parent failure or authorize by the last green rerun. A bounded reproduction of the original batch,
an accepted diagnosis under the gate's explicit flake/interaction contract, or quarantine resolves it; a
diagnosis cannot override a required red gate. Charge every repeated member execution and infrastructure
retry to both the isolation allocation and the item resource budget. A new partition ID grants no new budget.

### 6.4 Speculation

Speculation is permitted only for side-effect-free, reproducible work whose output is independently bound to
the final candidate. Examples include starting an expensive test shard while a cheap lint batch finishes,
or preparing a review context while qualification completes. Publishing, merging, commenting, changing a
claim, or mutating a board is never speculative.

The scheduler limits speculation by expected critical-path reduction, cancellation cost, queue pressure,
and recovery reserve. It cancels work only when cancellation is safe; otherwise the result may finish and be
retained as non-authorizing evidence. Speculative waste is measured explicitly rather than hidden inside
overall runner use.

Speculation across candidate changes follows merge-queue semantics: a downstream candidate is tested against
the exact ordered predecessor set it assumes. If a predecessor fails or changes, only evidence whose subject
includes that predecessor set is invalidated. The maximum speculative chain depth and fan-out are policy
bounds, and superseded work is cancelled before new speculative work is admitted. This captures the
common-case parallelism without accepting unbounded invalidation cascades.

### 6.5 Evidence reuse and precedence

The current acceptance manifest always binds the exact candidate. A reusable gate artifact instead binds
the gate contract and complete semantic input subject, plus toolchain, execution environment, relevant policy,
and expiry. It retains its original source candidate. An unrelated candidate edit can therefore reuse an
unchanged gate result through a newly validated manifest, as required by ADR-0080. Review acceptance still
requires a current epoch; retained unaffected critique is input to that epoch, not old-head authorization.

Apply these rules in order: mandatory comprehensive boundary → declared sentinel or suspect-cache recovery
→ changed/unknown subject executes → validated unchanged subject reuses. Unknown dependency coverage selects
the conservative full gate set. Comprehensive mode executes every declared gate cold, including any cache
whose hit would substitute for execution; immutable tool downloads may be reused when the gate contract
allows them. The closure fixture must observe actual execution, not merely a fresh wrapper receipt.

Build caching and acceptance-evidence reuse are separate trust decisions. Neither a cache hit nor a sentinel
proves that every semantic input was declared. Input-omission mutations, immutable snapshots, and provenance
validation establish the declared boundary; sentinels detect drift. Bazel documents the related hazards of
[undeclared tools and concurrent input changes](https://bazel.build/remote/caching).

## 7. Review, repair, and agent routing

### 7.1 Review as bounded information acquisition

Review is not a ritual after every tool result. The planner selects a critique when its expected information
can change acceptance, repair scope, fault model, or delivery risk. Routine current-head changes receive one
consolidated fresh critique. High-risk changes may receive focused architecture, security, or formal lenses
in parallel, joined into one deduplicated finding set.

Any candidate change invalidates the affected review epoch. A confirmation checks the repair and relevant
sibling risk; it does not mechanically repeat unrelated lenses. After the second related late-stage defect,
the workflow enters the ADR-0079 deep-dive route. At the total round bound it terminates safely rather than
asking for another reviewer.

The review package is optimized for understanding rather than comment count: intent and non-goals,
semantic/touch-set diff, risk and invariant delta, selected and omitted checks with reasons, current-head
evidence, and unresolved questions. Formatting and policy checks run before human review. Findings are
proposed as correctness/security/contract issues, bounded follow-up, or non-blocking nits. A finding blocks
when the Accountable Delivery Owner accepts it as material; required technical gates remain independently
binding. Fresh critique is a phase/evidence property and may be performed by that same owner under ADR-0079.
An optional external reviewer cannot become an undeclared authorization dependency. Nits cannot keep the
state machine in `Repairing`. Review wait and active review time are separate measurements,
and an unanswered review reaches its configured reassignment, synchronous-review, or terminal route instead
of remaining open forever.

### 7.2 Capability-based model profiles

Formal policy refers to stable capabilities, not vendor model names:

| Profile | Intended work | Required characteristics |
|---|---|---|
| `fast-mechanical` | classification, formatting, bounded extraction | low latency/cost, deterministic tool use, no architectural authority |
| `balanced-coding` | ordinary implementation and localized repair | repository/tool competence, patch discipline, test interpretation |
| `strong-fresh-critic` | exact-head code and design critique | fresh context, broad fault search, structured findings, no reliance on implementer confidence |
| `formal-reasoning` | Quint authoring and counterexample analysis | temporal/state-machine reasoning, precise assumption handling, tool competence |
| `strongest-architecture` | deep-dive after recurrent defects or authority changes | large context, cross-boundary reasoning, fault-model revision |

The allocator chooses the cheapest eligible profile whose measured capability and tail-risk satisfy the work
contract. Escalation is evidence-driven: recurrence, failed repair, novelty, semantic breadth, or high blast
radius may require a stronger profile. Merely reaching a later stage does not.

Provider bindings include exact model/reasoning identity, context limit, tools, sandbox, region/privacy,
price, observed duration and failure distributions, and expiry. A model update enters shadow/canary
qualification before becoming the default binding. The workflow remains valid when no provider is eligible:
it waits within budget, routes to a deterministic/human alternative, or terminates classified.

Routing follows an explicit complexity ladder:

1. use a deterministic parser, formatter, compiler, search, or test when it can decide the obligation;
2. use one bounded model call for classification, extraction, or a proposed patch with a deterministic
   verifier;
3. use a fixed localization → implementation → validation workflow for a well-shaped repository task;
4. use one autonomous agent when the route genuinely depends on observations discovered during work; and
5. use multiple agents only when disjoint search or implementation lanes have measured value over the
   single-agent baseline.

The router uses local, time-decayed outcome distributions by work class—not a provider leaderboard or a
model's self-assessment. It compares direct-strong routing with cheap-then-escalate routing on total latency,
cost, accepted-result rate, verification burden, and downstream defects. A failed cheap attempt is charged
to the cascade. Exploration receives a small explicit budget and cannot silently turn production work into
router training.

### 7.3 Bounded multi-agent composition

Multi-agent execution is an optimization mode, not the default architecture. It is legal only when workers
have disjoint declared touch sets or independent evidence duties, immutable task inputs, unique branch/job
identities, leases, and a deterministic join. One accountable integrator owns final conflict resolution and
delivery; a peer vote cannot authorize a merge.

The controller caps worker count, shared-message bytes, polling frequency, open PRs, merge attempts, and
integration retries. It stops spawning when the integration queue or review capacity is the bottleneck.
Independent critiques may use deliberately different prompts or eligible model families to reduce
correlated blind spots, but disagreement is preserved as evidence rather than averaged away. Dependent work
stays sequential unless the speculative predecessor identity and invalidation cost are explicit. These rules
directly address observed conflict abandonment, conformity, hidden-information, and resource-flood failure
modes in agent swarms.

## 8. Pure controller and durable executor

The runtime boundary remains small:

```text
complete observation + workflow state + policy + explicit tick
                         │
                         ▼
                 pure plan and verifier
                         │ checked bounded actions
                         ▼
             persist decision and effect intents
                         │
                         ▼
           durable executor / external providers
                         │ verified receipts
                         ▼
                    pure reducer
```

The controller commits only the next bounded actions. It does not generate an entire immutable project plan
and force reality to follow it. Material completions, failures, changed estimates, queue conditions,
candidate revisions, and external facts trigger replanning. Hysteresis prevents small estimate changes from
churning already-running work.

Planner output is untrusted until an independently implemented verifier proves:

- every chosen action is legal in the current compiled state;
- all selected obligations remain covered;
- resource intervals and total budgets fit;
- batch and speculation requirements hold;
- recovery capacity remains reserved;
- every loop edge has a decreasing budget or an external-wait classification;
- the objective and performance-envelope totals recompute from canonical bytes; and
- terminal and next-replan conditions exist.

The executor borrows durable-execution mechanics without delegating policy to them. Workflow code and policy
are versioned for deterministic replay; each effect carries a stable idempotency identity; an ambiguous
timeout is observed before retry; and execution history has byte/event ceilings plus an explicit modeled
continuation or archival boundary. Retry defaults distinguish application findings from transient
infrastructure failure. A durable engine may resume an action forever, so its retry feature is always capped
by the controller's semantic and resource budgets.

### 8.1 Reservation before execution

For each resource dimension enforce `spent + outstanding reservations <= item allocation`, with the same
atomic check against fleet capacity. Commit the decision, effect intent, and reservations together using an
expected state version; only the winning committed intent may dispatch. A pure feasibility check alone does
not prevent two controllers from spending the same balance.

Each operation reserves its enforceable maximum usage, including provider retries, billing granularity,
cancellation lag, result retention, and settlement work. Reconcile actual usage exactly once by stable receipt
identity; release unused reservation only after verified completion/cancellation or a provider-enforced end
bound. A timeout alone does not release capacity. Delayed usage remains reserved at its upper bound. An
adapter without an enforceable bound is ineligible for a strict-budget route; estimated p95 cost is not a cap.

Pin currency, rounding, token-unit definitions, and the applicable price schedule. A provider charge above
its declared enforceable bound is a contract breach that stops further admission and is reported as a budget
violation, never rewritten to fit the model. Reserve settlement compute/API/storage separately from ordinary
work so exhaustion cannot prevent journaling or safe handoff. Charge all recovery to the originating lineage.

### 8.2 Effect settlement, fencing, and terminal outcomes

Use the existing typed coordination engine and external Git/GitHub fencing for mutations. Durable ownership
has an expected generation; revalidate it and candidate/target preconditions immediately before dispatch.
Use provider-side conditional writes where supported. Reobservation followed by an unconditional write is
not atomic: a route lacking the required conditional mutation or qualified exclusion mechanism is refused.
Transport retries keep the same logical effect identity. A delivery effect is not a single atomic reducer
transition: intent, dispatch, unknown outcome, observed post-state, and settlement are separate modeled events.

At deadline or cancellation, stop new implementation/check/mutation dispatch. Settle known effects within
the reserved recovery allowance. If resolution is unavailable, record `Quarantined` with the proposed reason
(`deadline`, `budget`, `cancelled`, or `indeterminate-effect`) and atomically retain its pending effects in the
existing durable recovery journal, with a named owner, fence, reservation, and blocked subject. Clean
`Refused`, `BudgetExhausted`, or `Cancelled` outcomes assert that no unresolved mutation remains. `Delivered`
requires verified protected post-state and all declared post-merge obligations.

The item outcome is immutable; the separate append-only effect journal can record a late success without
reopening the item or claiming that cancellation undid a merge. The summary exposes both facts. Recovery has
bounded automatic attempts, then an explicit unresolved handoff; affected mutation subjects remain fenced
until settlement. No successor item or rollback route may write that subject merely because its prior item
is terminal. Reserve retained-journal capacity before admission so repeated quarantines cannot grow storage
without bound. Settlement liveness remains conditional on the external service and recovery owner becoming
available.

Crash qualification covers before intent commit, after commit/before dispatch, after external success/before
receipt, duplicate or reordered receipts, lost cancellation acknowledgment, stale-owner takeover, and late
success after quarantine. Cancellation is a request whose external effect needs verification; for example,
[Temporal activities may accept or ignore cancellation](https://docs.temporal.io/activity-execution).

Restart replays the item's pinned model, policy, reducer, adapter, and budget identities. History compaction
preserves counters, outstanding effects, reservations, deduplication identities, and lineage. A workflow
upgrade needs a qualified migration or drains old items on their retained runtime. Rollback fences the
candidate owner and reobserves pending effects before assigning an incumbent owner; it does not merely
change a routing flag or silently translate old state.

## 9. Performance evidence and anti-bureaucracy controls

### 9.1 Required measurements

Every attempt records, with appropriate privacy and retention controls:

- admission-to-plan, plan-to-dispatch, dispatch-to-first-output, and time-to-first-actionable-red;
- queue, setup, execution, isolation, repair, review, reconciliation, and total elapsed time;
- p50/p95/p99 by work class, execution mode, profile, repository, and runner class;
- runner-seconds, peak memory, retained bytes, token/money units, and API calls;
- batch size, member results, pass probability calibration, isolation depth, and rerun amplification;
- cache lookup, hit/miss reason, reused receipt identity, and avoided work;
- speculative work completed, reused, cancelled, or wasted;
- unique actionable defects by check/review lens and the later closure check that would have caught them;
- author active time, reviewer active time, wait time, handoffs, interruptions, and context-reconstruction
  events, sampled with privacy-preserving aggregation;
- perceived ease, cognitive load, and flow from a small stable survey, kept beside rather than substituted
  for observed delivery outcomes;
- work-item/change size, WIP at each bottleneck, merge/integration queue age, abandoned outputs, conflict
  rate, and time spent verifying model-generated work;
- route level, all attempted model/tool costs, escalation reason, accepted-result rate, downstream rework,
  and time-decayed calibration error;
- cache sentinel results, flaky-test rate, cache-key completeness failures, and comprehensive-closure misses;
- administrative action count and classified purpose; and
- terminal outcome, deadline/budget exhaustion, and recovery path.

Provider self-reports are not success authority. Measurements join verified process, runner, GitHub, and
receipt facts. Cancelled and timed-out work remains censored data; it is not rewritten as a successful short
duration or omitted from cost.

The primary scorecard is a vector, not a weighted vanity number: delivery lead time and throughput; change
failure/recovery and escaped defects; author/reviewer attention and flow; and compute/token/money/storage.
Policy comparisons report the full vector and uncertainty intervals. Lines changed, commits, agent turns,
comments, tokens consumed, and utilization are diagnostic quantities only. A policy cannot qualify by
raising visible activity while shifting cost to review, integration, recovery, or a later closure boundary.

### 9.2 Bureaucracy ledger

Every mandatory process element has a registry row:

```text
ControlId
Owner
ObligationId
DecisionChanged
SemanticSubject
ExecutionCostDistribution
UniqueFindingYield
FailureBlastRadius
MinimumCadence
ExpiryOrReviewDate
DeletionCondition
ReplacementControl
```

This metadata extends the existing control registry and is projected from its authoritative entries; it
must not create a parallel registry or a per-item authoring task. Safety controls may justify retention by
obligation and blast radius despite zero observed findings. Missing telemetry means unknown yield.

The controller refuses an unregistered mandatory element. A scheduled review identifies controls with high
cost, low unique yield, duplicate coverage, or expired rationale. It recommends consolidation, outward
cadence movement, or deletion; it does not weaken policy automatically. Comprehensive closure calibrates
whether the faster child path missed defects.

### 9.3 Performance regression gate

A candidate controller/policy is compared with the accepted baseline over identical replay snapshots,
deterministic simulation seeds, and fixed cohort definitions. Before observing candidate results, PB0 pins
eligibility, baseline version, observation and delayed-defect follow-up windows, primary improvement measure,
non-inferiority margins for the other dimensions, confidence method, sample sufficiency, and stopping rules.
Repeatedly inspecting a small canary cannot silently change those rules. Promotion fails when:

- any hard invariant or required witness regresses;
- a sufficiently measured required latency percentile exceeds its predeclared envelope;
- control-plane share, rerun amplification, speculative waste, or queue age exceeds its bound;
- the policy increases `Unknown`/`Indeterminate` outcomes by hiding missing observations;
- apparent savings result from fewer selected obligations without valid semantic closure; or
- measurement coverage, denominator, or retained evidence is incomplete.

Promotion has three verdicts: `qualified`, `rejected`, and `insufficient-data`. Separate time to terminal
classification from time to delivery. Record every eligible request and its outcome; unfinished deliveries
are censored, and refusal/cancellation are competing outcomes, not fast successful deliveries. Compare
delivered fraction, refusal rate, age of unfinished work, and total attributed cost alongside latency. A
two-item smoke test exercises behavior but cannot qualify p95/p99 or rare escaped-defect rates. Continue a
bounded canary or retain the incumbent when evidence is insufficient; never infer zero risk from zero defects.

Historical replay validates decisions on recorded facts; it cannot observe results for checks or model calls
the incumbent never ran. Simulation explores explicit assumptions. Shadow validates feasibility and measures
its own overhead, but cannot establish causal end-to-end savings for effects it did not execute. PB7 supplies
the online comparison using randomized cohorts or a documented matched/switchback design that accounts for
shared runner queues and human learning. Attribute delayed closure failures back to their original cohort.

There is no mutable label or per-run input that bypasses this gate. An emergency exception is a signed,
expiring, versioned policy record with scope, owner, rollback, and compensating evidence; it cannot authorize
a correctness violation or exceed compiled hard limits. A different performance envelope requires its own
accepted version and comparison; an explanation after a failed run cannot turn that run into a pass.

## 10. Visualization

One deterministic diagram grammar consumes compiled workflow and live receipt projections:

- states are nodes grouped by implementation, qualification, review, delivery, recovery, and terminal phase;
- legal transitions are edges with guard and budget-consumption identities;
- retry/rework edges are dashed and show remaining budget;
- batched checks are compound nodes whose members remain inspectable;
- speculative edges are visually distinct and never use the authorization style;
- blocked guards show the exact missing observation, capacity, or evidence identity;
- every view displays candidate, policy, compiled-contract, observation, and freshness fingerprints; and
- a table provides an exact-value and accessible fallback for every graph.

Static documentation SVG and live SVG use the same semantic graph. ELK may compute layout; it never decides
node or edge meaning. The runtime overlay may highlight current state and recent path, but cannot mutate the
workflow. Visual snapshot tests assert stable identities and that cycles, terminal exits, hidden batch
members, and exhausted budgets remain visible.

## 11. Qualification strategy

Each qualification profile declares its enabled actions and required witnesses. Disabled optional modes
must be rejected at the runtime boundary; their positive witnesses are required when that extension is
qualified. The complete acceptance stack for the enabled profile is:

1. schema and pure-function unit tests;
2. Quint typecheck and executable simulations after each model increment;
3. witnesses for every major transition and terminal path;
4. bounded model checking for safety, deadlock/terminal classification, and finite liveness profiles;
5. named mutations that remove budgets, exact-head checks, batch members, isolation exits, recovery reserve,
   or terminal routes and must make qualification red;
6. model/runtime correspondence by replaying fingerprinted traces through the real reducer;
7. property tests for determinism, budget conservation, obligation closure, and batch partition coverage;
8. discrete-event simulation over pass/failure correlation, arrivals, queueing, outages, flaky checks,
   provider latency, repair probability, and runner scarcity;
9. historical incident replay and baseline policy comparison;
10. live shadow decisions with no mutations;
11. bounded mutation canary with automatic rollback; and
12. comprehensive exact-head closure before a default or production-authority change.

Fast qualification batches compatible Quint properties and test cases to amortize startup. Failed batches
retain their exact membership and deterministic seed and use the same isolation algorithm being qualified.
The test harness therefore exercises the optimization under realistic failure, rather than verifying only
the all-green path.

## 12. Delivery roadmap

Each milestone is a vertically qualifiable boundary. Later repositories consume only published or otherwise
immutable accepted predecessor contracts. Cross-repo issues are created when implementation begins, with one
owning repository, narrow paths, acceptance criteria, and real `blockedBy` edges; this design document is not
itself a substitute for those requests.

**First evidence path:** PB0 → PB1 → PB2 → core PB3/PB4 → PB6 → PB7. Use one admitted routine item,
one fixed qualified implementation route, one fresh critique phase, shared-setup batching with binary
isolation, scoped reuse with a cold-closure override, and durable delivery/recovery. PB5 runs alongside this
path; a canonical receipt/table export supplies initial inspection. Adaptive batching, speculative chains,
learned routing, multi-agent execution, and automatic cadence recommendations are optional extensions after
the core canary. Disabled features do not require their runtime implementation to qualify that canary.

PB0 also fixes an engineering time/resource budget for the prototype and its stop decision. If the bounded
baseline shows no material batching opportunity, or the minimum executor cannot meet the proposed envelope,
retain the incumbent and record the failed hypothesis instead of building the remaining optimization stack.

### Roadmap at a glance

The roadmap is evidence-gated rather than date-driven. PB0 establishes the local cost and flow baseline needed
to estimate later work honestly. Each stage ends with a decision to proceed, revise the candidate, or stop and
retain the incumbent. Passing a component milestone does not authorize production mutation; PB7 has a separate
operating-authority gate, and PB8 has a separate default-adoption decision.

| Stage | Milestones | Primary owner | Deliverable outcome | Exit decision |
|---|---|---|---|---|
| 0. Establish the case | PB0 | `.github` | Reproducible baseline, complete candidate envelope, comparison protocol, and bounded prototype budget | Proceed only if the baseline exposes a material opportunity and the prototype has a falsifiable success threshold |
| 1. Define the kernel | PB1 → PB2 | Coordination; SDD only for missing generic tooling | Checked workflow model, compiled identities, corresponding pure reducer, policy parser, and independent verifier | Proceed only when traces correspond, every loop is bounded, and concurrent reservations cannot overspend |
| 2. Prove one vertical slice | PB3 core + PB4 core; PB5 in parallel | Coordination | One fixed route from admission through batching, isolation, critique, repair, delivery, and durable recovery | Proceed only when the integrated failure/recovery corpus passes and unsupported optimization modes fail closed |
| 3. Establish operational evidence | PB6 → PB7 | Coordination, then `.github` operating authority | Historical replay, simulation, live shadow comparison, and one reversible routine-work canary | Promote only with sufficient cohort evidence, no hard-property regression, and verified rollback |
| 4. Decide the default and expand | PB8 | `.github` policy; Coordination implementation | Accepted default decision, adaptive control review, and separately qualified work classes or optimization modes | Expand one class or mode at a time; retain or remove each control from measured cost, yield, and delayed-defect evidence |

The delivery increments are intentionally useful at different boundaries:

| Increment | Included milestones | What it establishes | What it does not establish |
|---|---|---|---|
| Baseline package | PB0 | Whether the proposal is worth prototyping and how success will be measured | Runtime correctness or a delivery improvement |
| Executable kernel | PB1–PB2 | Model/reducer correspondence and deterministic feasibility decisions | Safe external effects or end-to-end delivery |
| Core prototype | Core PB3–PB4 | A complete replayable vertical slice with bounded repair, accounting, and recovery | Production authority or comparative performance |
| Observable prototype | PB5 | Inspectable static and live projections over canonical identities | Additional workflow authority |
| Canary candidate | PB6 | A calibrated policy that is credible enough to seek bounded operating authority | Causal evidence of real delivery improvement |
| Qualified routine flow | PB7 | Reversible real-work evidence against the incumbent | Fleet default or eligibility for other work classes |
| Default-eligible flow | PB8 | Comprehensive closure and a separately accepted default decision | Automatic enablement of unqualified optimizations or work classes |

### Execution lanes and joins

The critical path is PB0 → PB1 → PB2 → core PB3/PB4 integration → PB6 → PB7 → PB8. Parallel work is
limited to boundaries with independently reviewable outputs:

- PB3 execution/recovery and PB4 review/routing can proceed in parallel after PB2. They join on the first
  check-failure → repair → requalification → delivery/recovery trace.
- PB5 can begin after PB1 supplies graph identities and PB2 supplies receipts. Its canonical table export is
  enough for PB6, so interface polish cannot hold the evidence path open.
- An SDD lane exists only when PB1 demonstrates a missing generic profile, compiler, or replay capability.
  Coordination work that consumes that capability waits for its published immutable artifact; unrelated
  domain modeling continues.
- Adaptive batching, speculation, learned routing, multi-agent execution, and automatic cadence advice begin
  as separate extension lanes only after the core route is measurable. Each rejoins at its own comparison and
  comprehensive-qualification gate.

At every join, the receiving milestone verifies the exact predecessor artifact rather than accepting a
roadmap status. If a predecessor contract changes, only its affected descendants replay. PB7 and PB8 always
take the comprehensive path regardless of earlier scoped evidence.

### Roadmap control points

| Gate | Evidence reviewed | Decision |
|---|---|---|
| G0 — Prototype | PB0 baseline and prototype budget | Fund the bounded kernel prototype, revise the hypothesis, or stop |
| G1 — Kernel | PB1/PB2 model checks, mutations, trace correspondence, and reservation tests | Admit the core executor work or return to the model/policy boundary |
| G2 — Vertical slice | Integrated PB3/PB4 success, failure, crash, deadline, and recovery traces | Admit simulation and shadow operation or repair the core design |
| G3 — Canary authority | PB6 replay/simulation/shadow receipt and cold qualification of the enabling candidate | Grant a scoped, expiring PB7 operating authority or retain shadow-only operation |
| G4 — Performance | PB7 cohort, delayed-defect follow-up, rollback, and complete denominators | Continue bounded observation, reject the candidate, or qualify the routine class |
| G5 — Default | PB8 comprehensive exact-head closure and proposed policy/ADR | Accept or refuse default eligibility; separately decide every later class or optional mode |

Roadmap status is tracked in implementation issues only after work begins. This document remains the proposed
sequence and acceptance design; it must not acquire mutable checkboxes that compete with repository and board
state. A gate that lacks evidence stays pending. `Insufficient-data` extends only the predeclared bounded
observation window or retains the incumbent; it never counts as acceptance.

### PB0 — Baseline, glossary, and candidate performance envelope (`.github`)

**Deliverables**

- Freeze representative routine, formal, failure, flaky, and high-risk workflow traces.
- Define useful work, control work, actionable evidence, unique finding, batch, isolation, speculation,
  terminal outcome, and control-plane share.
- Measure current time-to-first-evidence, p50/p95/p99 latency, review/repair rounds, runner setup duplication,
  check pass/failure correlation, cache reuse, agent attempts, token use, and administrative action count.
- Measure author/reviewer active and waiting time, interruptions, work/change size, WIP, integration conflicts,
  verification effort, flaky outcomes, and perceived ease/flow using stable privacy-preserving instruments.
- Establish the incumbent and applicable deterministic/fixed-workflow baselines for the core route. Add
  single-call, single-agent, or multi-agent comparisons when proposing those extensions; do not compare a
  candidate only with a deliberately weak agent.
- Publish the initial hard envelope and bureaucracy-ledger schema as candidate contracts.
- Pin the §3.4 accounting rules and §9.3 comparison protocol, including useful-delivery floors and explicit
  `insufficient-data` handling. Resolve every missing cap before enabling admission.

**Acceptance**

- Empty, partial, cancelled, timed-out, and unavailable observations are distinguishable.
- At least five historical failure classes replay from immutable inputs.
- Baselines show denominators and censored observations.
- The scorecard preserves delivery, quality, attention, and resource dimensions without collapsing them into
  one activity score.
- No workflow authority changes.

### PB1 — Canonical workflow model and generic tooling boundary (`FS.GG.Coordination`; SDD if needed)

**Dependencies:** PB0 definitions and the published tooling accepted under ADR-0077 and its
[Q1 qualification amendment](2026-08-26-adr-0077-q1-qualification-amendment.md). Inspect current producer
artifacts before requesting an extension; do not assume that new compilation or a profile revision is needed.

**Deliverables**

- Coordination owns the routine-code-change literate Quint model and domain catalogue, using §5 with the
  existing accepted source layout. Domain guards and reducer semantics are not SDD tooling responsibilities.
- Reuse SDD's pinned extraction, typecheck, simulation, verification, ITF validation, and semantic diff.
  Request an SDD producer child only for a demonstrated missing generic profile/compiler/replay capability;
  qualify and publish that extension before the Coordination consumer pins it.
- Declare compiled identities for budgets, batches, findings, evidence, performance obligations, and visual
  transitions without copying Quint expressions into a second behavioral language.
- Add anti-vacuity witnesses and required named mutations.
- Separate invariant/rank/deadlock evidence from conditional temporal claims; record every fairness and
  environment assumption and whether each run was exhaustive or sampled.
- Define small finite profiles and symmetry/independence abstractions so verification cost cannot grow with
  provider inventory, telemetry cardinality, or arbitrary backlog size.

**Acceptance**

- Every action is witnessed; every internal cycle consumes a budget; every exhaustion path reaches a named
  terminal state.
- Removing any performance budget, required batch member, exact-head guard, or terminal exit makes a named
  control red.
- Generated contract and diagram facts are reproducible from pinned source/tool identities.
- No result is labeled a termination proof solely because a bounded simulation found no counterexample.
- Repair invalidation, no-progress observations, restart, and new operation IDs cannot renew item budgets.
- Generic tooling changes, if required, are published before consumers pin them; the consumer owns domain
  source, action adapters, state projection, and runtime correspondence under ADR-0077.

### PB2 — Pure reducer, policy schema, and independent verifier (`FS.GG.Coordination`)

**Dependencies:** PB1 accepted domain contract and published producer artifacts.

**Deliverables**

- Implement canonical workflow state/events and deterministic evolution.
- Implement versioned safety/performance policy parsing with complete-budget validation.
- Implement baseline legal-action selection and an independent feasibility verifier.
- Implement WIP admission, complexity-ladder selection, coalescing deadlines, and wait/terminal routes before
  adding learned scheduling or routing.
- Replay PB1 ITF traces through the reducer and compare observable states/actions.
- Emit content-addressed decision and performance receipts.
- Model atomic expected-version commitment of intents/reservations, settlement allocation, owner generation,
  and terminal handoff; implement runtime-cap enforcement separately from cohort SLO evaluation.

**Acceptance**

- Same canonical envelope reproduces byte-identical decision/explanation output.
- Incomplete observations never create permission.
- Planner timeout with a candidate is distinguished from optimality and still verified.
- Performance-envelope violations produce infeasible/refused decisions, not warnings.
- Concurrent dispatch, duplicate receipts, and late usage cannot double-spend or release reservations twice.

### PB3 — Durable executor, delivery/recovery, batching, and reuse (`FS.GG.Coordination`)

**Dependencies:** PB2.

**Deliverables**

- Implement sound obligation closure and execution-equivalence grouping.
- Implement intent/outbox commit, existing-engine dispatch, conditional mutation/fencing, deadlines,
  reservation reconciliation, protected post-state verification, and durable recovery handoff from §8.
- Implement optimistic batches with per-member results and content-addressed evidence.
- Implement binary isolation baseline, parallel partition execution, infra/finding/indeterminate
  classification, and isolation-budget exhaustion.
- Implement fixed bounded batch sizing, immutable candidate/target identity, supersession, flaky/interaction
  classification, scoped reuse, cold closure, and cache sentinels. Required pending effects survive restart.
- Defer adaptive sizing and side-effect-free speculation to an independently qualified extension with
  cancellation accounting and bounded depth/fan-out. The core contract refuses unsupported modes.

**Acceptance**

- All-green fixture performs one shared setup where the sequential baseline performs N.
- One planted failure is attributed without rerunning already proven independent members.
- Multiple correlated failures terminate within the isolation budget.
- Missing member output, truncated result, stale cache key, or wrong candidate refuses success.
- Queue bursts and predecessor failures cannot cause unbounded coalescing waits or invalidation reruns.
- Replay/simulation shows improved p95 feedback or runner cost without obligation loss.
- Crash and stale-owner fixtures from §8.2 preserve effect ownership and enforce observe-before-retry.
- A failed parent batch followed by passing partitions retains the unresolved interaction finding.
- Unchanged semantic subjects reuse across unrelated candidate edits; comprehensive closure executes cold.
- Deadline during merge followed by late success cannot produce a false clean cancellation or a second merge.

### PB4 — Bounded review/repair and capability routing (`FS.GG.Coordination`)

**Dependencies:** PB2; PB3 for integrated qualification.

**Deliverables**

- Add stable finding identities, deduplication, recurrence, disposition, and semantic subjects.
- Implement fresh review epochs, current-head invalidation, bounded confirmation, deep-dive entry, and
  terminal exhaustion.
- Implement capability-profile registry and provider binding qualification.
- Implement one fixed qualified route plus a bounded escalation/fallback, accounting for whole-cascade cost.
- Defer adaptive routing and multi-agent composition to separate extensions. Before enabling multi-agent
  mode, implement §7.3 identity, lease, touch-set, WIP, communication, and deterministic-join controls.

**Acceptance**

- Duplicate wording cannot multiply repair obligations.
- A moved head cannot reuse accepted review improperly.
- The second related late defect enters deep-dive; total exhaustion terminates.
- When no model is eligible, the controller chooses a classified wait/fallback/terminal route without a loop.
- Stronger models are used only when required capabilities or measured risk justify them.
- Enabling the optional multi-agent extension requires it to outperform the single-agent/fixed-workflow
  baseline on an applicable local cohort without increasing conflict abandonment, review queue age, or total
  verification burden.

PB3 and PB4 may develop in parallel after PB2 because their primary touch sets and contracts differ. Their
integration candidate must replay combined check-failure → repair → requalification traces before either is
considered complete at the parent boundary.

### PB5 — Generated static and live visualization (`FS.GG.Coordination` + `.github` docs)

**Dependencies:** PB1 compiled graph identities and PB2 receipts.

**Deliverables**

- Define the bounded visualization read model and diagram grammar.
- Generate deterministic static SVG from the compiled workflow.
- Render live execution state, batches, blocked guards, budgets, evidence freshness, and recent transitions.
- Add accessible table fallback, keyboard navigation, text export, and visual snapshot/property tests.

**Acceptance**

- Static and live nodes/edges resolve to canonical identities.
- Retry cycles, batch membership, speculative/non-authorizing paths, and all terminal exits are visible.
- Layout/client state cannot modify authority.
- Projection failure does not block an otherwise legal workflow.

### PB6 — Simulation, shadow operation, and policy calibration (`FS.GG.Coordination`)

**Dependencies:** core PB3/PB4 and PB0 comparison protocol. PB5 is not a prerequisite; use canonical exports.

**Deliverables**

- Build discrete-event scenarios for arrival bursts, correlated failures, flaky checks, provider slowdown,
  runner scarcity, GitHub degradation, review recurrence, cache corruption, stale router data, dependent-agent
  conflicts, reviewer saturation, and speculative invalidation cascades.
- Replay accepted historical incidents against baseline and candidate policies.
- Run live shadow planning and record disagreements without mutation.
- Calibrate batching thresholds, isolation strategy, and the enabled routing choices. Speculation and
  multi-agent scenarios join this suite when their extensions are proposed.
- Finalize the paired/randomized canary protocol and sample plan for PB7. PB6 performs no candidate mutation
  and does not claim measured end-to-end savings from simulated or shadow decisions.

**Acceptance**

- Candidate preserves every hard property; simulation predicts the predeclared primary improvement within
  the non-inferiority margins. Report assumptions and uncertainty; online confirmation belongs to PB7.
- Tail latency, queue age, cancellation waste, and recovery reserve stay within envelope.
- Prediction calibration and sparse-data cases are visible.
- Whole-cascade routing cost and human verification/review cost are attributed to the originating decision.
- Shadow disagreement is explained from canonical constraint/objective facts.

### PB7 — Bounded canary and routine-flow adoption (`FS.GG.Coordination`, then `.github`)

**Dependencies:** PB6 feasibility receipt, a separately accepted bounded operating authority, comprehensive
qualification of the canary-enabling candidate, and the existing external mutation epoch/fencing predicates.
A shadow receipt alone never authorizes the first production mutation.

**Deliverables**

- Enable one low-blast-radius, reversible routine code-change class.
- Keep the existing path as rollback and emergency operation.
- Promote exact model bindings and policy version for the canary cohort.
- Verify delivery, post-merge state, telemetry retention, and rollback.
- Run the PB0/PB6 comparison protocol, including delayed-defect follow-up, admission/refusal denominators,
  shared-capacity interference, and whole-lineage cost. Use a bounded extension or retain the incumbent when
  sample sufficiency remains unmet.

**Acceptance**

- Canary completes enough successes and planted/real failures to exercise both batch and isolation paths.
- Core qualification covers supersession, flakiness, sentinel mismatch, escalation, and indeterminate effects.
  Optional speculation and multi-agent modes require their additional failure fixtures before enablement;
  planted faults run in isolated fixtures, not by corrupting a production candidate.
- No safety, obligation-coverage, or exact-head regression occurs.
- The predeclared statistical acceptance criteria pass with complete denominators and sufficient samples.
  Behavioral smoke success may justify continued bounded observation, never general performance qualification.
- Rollback restores the prior route without rewriting receipts.

### PB8 — Adaptive cadence and default eligibility (`.github` policy + Coordination implementation)

**Dependencies:** PB7 soak and comprehensive closure.

**Deliverables**

- Produce scheduled recommendations for check/review cadence from cost, unique yield, delayed detection, and
  closure equivalence.
- Add the bureaucracy-ledger review and deletion workflow.
- Expand to additional work classes one at a time.
- Qualify optional optimization modes separately against the core baseline; retain only measured improvements.
- Decide default eligibility through a separate accepted policy/ADR change.

**Acceptance**

- Recommendations never automatically weaken a hard boundary.
- A closure-found miss is charged to the responsible selection/cadence decision and moves the check inward.
- Expired or low-yield controls receive an explicit retain/change/delete disposition.
- Default adoption has a comprehensive exact-head closure receipt and a tested rollback.

## 13. Cross-repository sequence

```text
.github PB0 baseline + candidate envelope
    │
    ▼
Coordination PB1 domain model ◄── published SDD tooling
    │                            (extend only if needed; publish before consume)
    ▼
Coordination PB2 reducer/verifier + reservation contract
    ├──► PB3 durable executor + delivery/recovery + batching/reuse ──┐
    ├──► PB4 fixed routing + bounded review/repair ─────────────────┤
    └──► PB5 visual projection (parallel; canonical export first)   │
                                                                  ▼
                                                             PB6 shadow
                                                                  │
                                  accepted canary authority + cold qualification
                                                                  │
                                                                  ▼
                                                             PB7 canary
                                                                  │
                                                                  ▼
.github PB8 default decision ◄── sufficient evidence + comprehensive closure
```

Any new cross-repo schema follows publish-before-flip: the producer first publishes a validator/compiler that
still accepts the current consumer document; `.github` then bumps and pins the declared schema in a separate
ordered change. No roadmap checkbox substitutes for a published artifact or verified consumer pin.

## 14. Rollout and rollback rules

- Begin with offline replay and simulation; then shadow; then one bounded reversible canary.
- Keep old and candidate decision outputs side by side during shadow, but only the incumbent may mutate.
- During canary, exactly one policy version owns a subject; dual writers are forbidden.
- Ownership transfer and rollback use §8.2 fencing and recovery; unresolved effects block conflicting work.
- A safety or authority failure stops the canary immediately.
- A performance-envelope failure stops admission of new canary work, allows already-safe effects to settle,
  and rolls routing back after current external facts are reobserved.
- A provider degradation may rebind an equivalent qualified capability profile without changing workflow
  semantics; a missing equivalent profile follows the modeled fallback/terminal route.
- Rollback preserves immutable model, policy, decision, execution, and telemetry receipts.

## 15. Risks and controls

| Risk | Control |
|---|---|
| The performance gate becomes more bureaucracy | One compact receipt derived from existing execution facts; asynchronous projection; its own control-plane share is measured and bounded. |
| Optimistic batching delays a rare failure | Calibrated pass probability, failure-localization penalty, early-output runners, adaptive partitioning, and high-risk exclusions. |
| Speculation wastes scarce runners | Side-effect-free only, recovery reserve, explicit waste ceiling, cancellation accounting, and automatic policy refusal on breach. |
| Cached green evidence hides drift | Semantic subject plus candidate/toolchain/environment/policy key, expiry, digest revalidation, and comprehensive closure. |
| The optimizer games easy metrics | Constraint-first feasibility, tail measures, fixed denominators, censored outcomes, named mutations, and independent verifier. |
| Formal termination is overstated | Frozen-scope and fairness assumptions are explicit; waiting and external availability are not counted as internal progress. |
| Model checking itself becomes a slow gate | Small finite profiles, abstraction, pinned bounds, state-count/time receipts, fast invariant batches, and deeper exhaustive runs only at semantic or closure boundaries. |
| Review findings expand scope without bound | Stable identities, deduplication, active-scope freeze, cross-scope backlog routing, bounded rounds, and terminal quarantine. |
| Fast generation overloads reviewers | Small deliverable slices, author-side deterministic checks, WIP tied to review capacity, compact review packages, non-blocking nits, and separate review-wait/active-time budgets. |
| Strong models are used everywhere | Least-cost eligible capability routing and measured escalation triggers. |
| Cheap models cause rework | Outcome/rework distributions feed eligibility; repeated defects escalate and can disqualify a binding. |
| Cheap-first routing makes known-hard work slower | Compare direct-strong and cascade routes by work class; charge every failed tier and verification step to the original route. |
| Multi-agent activity overwhelms integration | Opt-in disjoint/independent lanes, one integrator, WIP/message/poll/PR caps, unique identities, leases, and a single-agent baseline. |
| Agent agreement hides a correlated mistake | Preserve dissent, diversify independent critique where justified, require deterministic/external evidence, and never authorize by vote alone. |
| A visualization becomes a second workflow editor | One-way generated projection, fingerprint binding, read-only default, and typed commands outside the renderer. |

## 16. Alternatives considered

### Safety-first with performance as a weighted objective

Rejected. A sufficiently large safety weight still permits unbounded process when no alternative is feasible,
and it obscures whether the latency/overhead contract was actually satisfied. Safety and performance both
define feasibility; optimization begins afterward.

### Always run checks sequentially

Rejected. It minimizes attribution work only by paying maximum repeated setup and critical-path latency on
the overwhelmingly common all-green path. Sequential execution remains a bounded diagnostic fallback.

### Always run every check independently in parallel

Rejected. It can minimize wall time with unlimited capacity but amplifies checkout, restore, process startup,
artifact, queue, and rate-limit cost. The planner must compare shared setup, batching, and parallelism under
real capacities.

### Predictively skip likely-passing checks

Rejected as the initial optimization. Prediction may rank and shape execution, but obligation removal requires
sound semantic-subject closure and sentinel evidence. Optimistic batching captures much of the common-case
gain without weakening coverage.

### Let the agent decide when enough process has occurred

Rejected. Agent judgement can propose a stop or escalation, but deterministic budgets, obligations, evidence,
and terminal guards decide. Otherwise the same probabilistic component being supervised controls its own
resource limit.

### Encode provider/model names directly in Quint

Rejected. Provider inventory changes faster than protocol semantics and would enlarge the formal state space.
Stable capability profiles plus versioned qualified bindings retain control without semantic churn.

### Adopt a durable workflow product without a formal kernel

Rejected as the complete solution. Temporal, Durable Functions, or an actor runtime can persist execution and
retry effects, but durability alone does not establish obligation closure, bounded rework, exact-head review,
or bureaucracy termination. A durable executor remains useful beneath the Quint/policy boundary.

### Use an autonomous agent for every task

Rejected. Tool-interface design matters, but repository benchmarks and industrial evidence do not show that
maximum autonomy is the most efficient universal route. A fixed localization/repair/validation workflow can
be cheaper and more interpretable, while deterministic tools are better for already-formalized decisions.
The complexity ladder escalates only when a simpler route is unqualified or has worse local outcomes.

### Use multiple agents by default

Rejected. Parallel independent search can improve coverage, but dependent software changes create conflict,
integration, conformity, messaging, and resource-amplification costs. Multi-agent mode is a bounded execution
policy for demonstrably separable work, not a synonym for throughput.

### Optimize one productivity score

Rejected. Any scalar weighting invites the controller to exchange quality, human attention, or tail risk for
visible output. The feasibility envelope and multidimensional scorecard retain hard bounds and expose the
trade rather than hiding it in a coefficient.

### Treat Quint model checking as unconditional termination proof

Rejected. The formal result applies to the stated abstraction and finite bounds; liveness may depend on
fairness and external availability, and current Quint temporal support is partial. The accepted claim joins
rank/deadlock checks, explicit deadlines and assumptions, supported temporal evidence, and reducer trace
correspondence.

## 17. Definition of done

The core behavioral smoke test requires one real routine delivery and an isolated planted-failure change
that exercises bounded isolation/repair or an honest terminal refusal. Both produce:

- checked Quint and compiled-contract identities;
- a verified policy decision with explicit safety and performance feasibility;
- optimistic batched qualification on the common path;
- adaptive isolation that does not replay proven work on the failure path;
- bounded review/repair with fresh exact-head evidence;
- least-complex qualified routing, capability-based model selection, and complete cascade usage receipts;
- WIP-bounded coordination with attributed verification cost and cohort-level attention measurements;
- exact-head delivery or an honest terminal refusal;
- reconciled resource reservations and settled effects or a durable, fenced recovery handoff; and
- canonical receipt/table exports that allow the full path to be inspected.

Performance qualification additionally requires the PB7 cohort to meet the predeclared improvement,
non-inferiority, delivery-rate, sample, and follow-up criteria in §9.3. The two smoke items are insufficient
for that claim. Default eligibility requires PB8's accepted decision and comprehensive exact-head closure;
program completion also includes PB5's static/live projections. Optional optimization modes remain disabled
unless separately qualified and are not prerequisites for the core canary or default eligibility.

The comparison must include the incumbent and a qualified deterministic/fixed-workflow baseline; a multi-agent
extension additionally compares against a single-agent route. A subjective impression of speed, a higher
agent solve rate without cost, or a green bounded simulation without the stated proof assumptions is insufficient.

Until then, the design is preparation and evidence—not production authority.
