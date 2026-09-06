---
title: "Design and roadmap: Performance-bounded, Quint-governed development flow"
category: Design
categoryindex: 4
index: 36
description: "A configurable development controller in which safety, progress, latency, resource efficiency, and bounded coordination overhead are coequal hard requirements."
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
| Primary risk | Correctness bureaucracy grows without bound until useful work stops |
| Governing direction | Quint-first semantic authority, pure policy, durable execution, generated projections |
| Builds on | [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md), [ADR-0079](../adr/0079-single-accountable-delivery-authority.md), [ADR-0080](../adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md), [ADR-0081](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md), and the [operations-research orchestration design](2026-08-31-operations-research-first-agent-orchestration-design.md) |
| Initial owners | FS-GG/.github: policy and registry; FS.GG.SDD: Quint profile/compiler/replay; FS.GG.Coordination: controller, executor, receipts, and projections |

## 1. Decision

Performance and efficiency are acceptance properties, not optional objectives applied after safety. The
development controller must satisfy five classes of hard requirement before a policy can be promoted:

1. **Safety:** no action bypasses an authority, evidence, security, exact-head, or recovery predicate.
2. **Progress:** admitted finite-scope work reaches a classified terminal outcome under its declared
   environment assumptions.
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

## 3. Requirements

### 3.1 Functional requirements

| ID | Requirement |
|---|---|
| FLOW-001 | One canonical literate Quint source declares states, actions, guards, budgets, terminal outcomes, invariants, and environment assumptions. |
| FLOW-002 | A pinned compiler emits a small versioned contract containing stable state/action/property identities, reads/writes, evidence obligations, and projection facts; generated artifacts are not coequal authority. |
| FLOW-003 | A pure reducer and planner consume only canonical state, complete observations, a versioned policy, and recorded time/random inputs. |
| FLOW-004 | Provider/model names are resolved through capability profiles outside the Quint topology. Changing a provider binding does not silently change workflow semantics. |
| FLOW-005 | Every external effect persists intent before execution, revalidates its preconditions, and records a verified outcome or an explicit indeterminate state. |
| FLOW-006 | Every candidate change invalidates older-head review and qualification evidence according to its semantic subject. |
| FLOW-007 | Findings have stable identities, severity, semantic subjects, disposition, and recurrence linkage. Duplicate prose cannot create duplicate repair obligations. |
| FLOW-008 | Scoped child qualification and comprehensive parent/release closure remain distinct execution profiles. |
| FLOW-009 | Static and live visualizations are generated from compiled/runtime projections with source, policy, and freshness fingerprints. |

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
| PERF-008 | Content-addressed evidence reuse is mandatory when subject, candidate, toolchain, environment, policy, and expiry identities match. Repeating an equivalent expensive check without a recorded reason is a policy violation. |
| PERF-009 | All internal rework cycles consume a monotonically decreasing budget and end in success, refusal, quarantine, cancellation, or budget exhaustion. |
| PERF-010 | Capacity policy reserves explicit headroom for recovery and high-priority failure isolation. Nominal utilization may not consume that reserve. |
| PERF-011 | Qualification records p50/p95/p99 latency, queue delay, setup duplication, cache/reuse, cancellation waste, retry amplification, and control-plane share. Averages alone cannot qualify a policy. |
| PERF-012 | A candidate policy must beat or remain within the accepted baseline envelope in replay, simulation, shadow, and canary evidence. A faster policy that weakens a hard property is infeasible; a safer policy that breaches the accepted performance envelope is also infeasible. |
| PERF-013 | Telemetry and visualization are asynchronous projections. Their failure may degrade observability but cannot block an otherwise authorized development transition unless the missing receipt is itself a declared acceptance obligation. |
| PERF-014 | Every new mandatory artifact or gate names its consumer, decision changed, expected unique-defect yield, execution cost, expiry/review date, and deletion condition. Missing metadata refuses promotion. |

### 3.3 Starter performance envelope

Policy values must ultimately be calibrated from fleet telemetry. The first canary nevertheless needs a
complete, falsifiable envelope. These are starter values, not universal constants:

| Budget | Routine child change | High-risk or closure change |
|---|---:|---:|
| Pure planning/verifier p95 | 2 seconds | 5 seconds |
| Time from complete admission to first useful dispatch p95 | 10 seconds | 30 seconds |
| Time to first actionable check result p95, excluding provider outage | 5 minutes | 10 minutes |
| Implementation attempts | 2 | 3 |
| Ordinary repair rounds | 2, then deep-dive | 2, then deep-dive |
| Total repair/review rounds | 3 | 4 |
| Consecutive infrastructure retries per operation | 2, observe before each | 2, observe before each |
| Recovery capacity reserve | at least 15% of relevant constrained capacity | at least 20% |
| Control-plane compute share | at most 5% of item runner-seconds, excluding required evidence execution | at most 8% |
| Speculative cancellation waste | at most 10% of item runner-seconds over rolling window | at most 5% |

A deployment may tighten these values. Loosening one is a versioned policy change with replay, simulation,
shadow, and canary evidence. An item may bind a smaller resource budget; it may not omit the field. A
deadline expiration produces a terminal classification and preserves resumable evidence rather than
silently granting more time.

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

Illustrative policy shape:

```yaml
schema: fsgg.development-policy/v1
policyId: routine-code-change/1

budgets:
  planningP95Ms: 2000
  dispatchP95Ms: 10000
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

terminalOnExhaustion: quarantined
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
  attemptsRemaining: int,
  repairRoundsRemaining: int,
  reviewRoundsRemaining: int,
  infrastructureRetriesRemaining: OperationId -> int,
  resourceBudget: ResourceBudget,
  terminal: Delivered | Refused | Quarantined | BudgetExhausted | Cancelled | None
}
```

Time is admitted only through explicit durable ticks or provider observations. Cost is admitted only through
verified usage receipts. Neither the Quint model nor pure reducer reads a wall clock, provider dashboard, or
mutable price table directly.

### 5.2 Principal actions

| Action | Guard | Main effect |
|---|---|---|
| `Observe` | required fact incomplete or effect indeterminate | admit a complete classified observation |
| `Plan` | observations sufficient and planning budget remains | select and independently verify a bounded next-action set |
| `DispatchImplementation` | implementation legal, capacity available | consume attempt reservation and create immutable agent specification |
| `AcceptCandidate` | agent output validates and touch set is respected | mint candidate revision and invalidate affected older evidence |
| `StartCheckBatch` | selected obligations share a compatible execution class | persist exact membership and start one batch |
| `RecordBatchPass` | complete successful result matches batch identity | satisfy each member obligation with individually addressable evidence |
| `RecordBatchFailure` | complete failure or indeterminate result matches batch | classify failure and create bounded isolation partitions |
| `IsolateFailure` | attribution ambiguous and isolation budget remains | split/rerun only the ambiguous partition |
| `StartReview` | required current-head checks satisfied | mint fresh critique epoch and consume review budget |
| `AcceptFindings` | current-head critique completed | deduplicate/classify findings and choose repair, deep-dive, or acceptance |
| `Repair` | accepted material finding and repair budget remains | dispatch bounded repair and consume a repair round |
| `DeepDive` | second related late-stage defect | inspect architecture/invariants/sibling paths and update fault model |
| `Deliver` | exact-head obligations and authority predicates satisfied | persist effect intent, reobserve, mutate, and verify |
| `Terminate` | success, refusal, cancellation, deadline, or budget condition | enter one immutable terminal classification |

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

For a frozen finite obligation set, bounded counters, and classified external outcomes, a lexicographic rank
can show that internal rework cannot continue forever:

```text
rank = (
  unsatisfiedRequiredObligations,
  unsettledEffects,
  attemptsRemaining,
  repairRoundsRemaining,
  reviewRoundsRemaining,
  isolationBudgetRemaining,
  resourceBudgetRemaining
)
```

Every internal cycle must strictly decrease an earlier component without increasing an earlier one. Waiting
does not pretend to decrease the rank. Conditional liveness is stated explicitly: if admitted external
operations eventually yield a classified result and required capacity becomes available within the declared
environment bound, the workflow eventually reaches a terminal outcome. Quint/Apalache checking establishes
the declared finite instances and bounds; it does not prove provider availability or arbitrary unbounded
workloads.

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
missing member result is incomplete, never successful.

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

### 6.4 Speculation

Speculation is permitted only for side-effect-free, reproducible work whose output is independently bound to
the final candidate. Examples include starting an expensive test shard while a cheap lint batch finishes,
or preparing a review context while qualification completes. Publishing, merging, commenting, changing a
claim, or mutating a board is never speculative.

The scheduler limits speculation by expected critical-path reduction, cancellation cost, queue pressure,
and recovery reserve. It cancels work only when cancellation is safe; otherwise the result may finish and be
retained as non-authorizing evidence. Speculative waste is measured explicitly rather than hidden inside
overall runner use.

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
- administrative action count and classified purpose; and
- terminal outcome, deadline/budget exhaustion, and recovery path.

Provider self-reports are not success authority. Measurements join verified process, runner, GitHub, and
receipt facts. Cancelled and timed-out work remains censored data; it is not rewritten as a successful short
duration or omitted from cost.

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

The controller refuses an unregistered mandatory element. A scheduled review identifies controls with high
cost, low unique yield, duplicate coverage, or expired rationale. It recommends consolidation, outward
cadence movement, or deletion; it does not weaken policy automatically. Comprehensive closure calibrates
whether the faster child path missed defects.

### 9.3 Performance regression gate

A candidate controller/policy is compared with the accepted baseline over identical replay snapshots,
deterministic simulation seeds, and canary cohort definitions. Promotion fails when:

- any hard invariant or required witness regresses;
- p95 or p99 latency exceeds its envelope without an accepted risk-class explanation;
- control-plane share, rerun amplification, speculative waste, or queue age exceeds its bound;
- the policy increases `Unknown`/`Indeterminate` outcomes by hiding missing observations;
- apparent savings result from fewer selected obligations without valid semantic closure; or
- measurement coverage, denominator, or retained evidence is incomplete.

There is no mutable label or per-run input that bypasses this gate. An emergency exception is a signed,
expiring, versioned policy record with scope, owner, rollback, and compensating evidence; it cannot authorize
a correctness violation.

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

The complete acceptance stack is:

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

### PB0 — Baseline, glossary, and performance constitution (`.github`)

**Deliverables**

- Freeze representative routine, formal, failure, flaky, and high-risk workflow traces.
- Define useful work, control work, actionable evidence, unique finding, batch, isolation, speculation,
  terminal outcome, and control-plane share.
- Measure current time-to-first-evidence, p50/p95/p99 latency, review/repair rounds, runner setup duplication,
  check pass/failure correlation, cache reuse, agent attempts, token use, and administrative action count.
- Publish the initial hard envelope and bureaucracy-ledger schema as candidate contracts.

**Acceptance**

- Empty, partial, cancelled, timed-out, and unavailable observations are distinguishable.
- At least five historical failure classes replay from immutable inputs.
- Baselines show denominators and censored observations.
- No workflow authority changes.

### PB1 — Quint workflow authority and compiled contract (`FS.GG.SDD`)

**Deliverables**

- Author the routine-code-change literate Quint model using the state/actions/properties in §5.
- Define the constrained workflow profile and compiled identities for budgets, batches, findings, evidence,
  performance obligations, and visual transitions.
- Provide deterministic tangle, typecheck, simulation, bounded verification, ITF export, semantic diff, and
  trace-validation commands.
- Add anti-vacuity witnesses and required named mutations.

**Acceptance**

- Every action is witnessed; every internal cycle consumes a budget; every exhaustion path reaches a named
  terminal state.
- Removing any performance budget, required batch member, exact-head guard, or terminal exit makes a named
  control red.
- Generated contract and diagram facts are reproducible from pinned source/tool identities.
- Runtime-neutral package/tooling is published before consumers pin it.

### PB2 — Pure reducer, policy schema, and independent verifier (`FS.GG.Coordination`)

**Dependencies:** PB1 published artifacts.

**Deliverables**

- Implement canonical workflow state/events and deterministic evolution.
- Implement versioned safety/performance policy parsing with complete-budget validation.
- Implement baseline legal-action selection and an independent feasibility verifier.
- Replay PB1 ITF traces through the reducer and compare observable states/actions.
- Emit content-addressed decision and performance receipts.

**Acceptance**

- Same canonical envelope reproduces byte-identical decision/explanation output.
- Incomplete observations never create permission.
- Planner timeout with a candidate is distinguished from optimality and still verified.
- Performance-envelope violations produce infeasible/refused decisions, not warnings.

### PB3 — Optimistic batch executor, evidence reuse, and isolation (`FS.GG.Coordination`)

**Dependencies:** PB2.

**Deliverables**

- Implement sound obligation closure and execution-equivalence grouping.
- Implement optimistic batches with per-member results and content-addressed evidence.
- Implement binary isolation baseline, parallel partition execution, infra/finding/indeterminate
  classification, and isolation-budget exhaustion.
- Add side-effect-free speculation and cancellation accounting behind a disabled-by-default policy flag.

**Acceptance**

- All-green fixture performs one shared setup where the sequential baseline performs N.
- One planted failure is attributed without rerunning already proven independent members.
- Multiple correlated failures terminate within the isolation budget.
- Missing member output, truncated result, stale cache key, or wrong candidate refuses success.
- Replay/simulation shows improved p95 feedback or runner cost without obligation loss.

### PB4 — Bounded review/repair and capability routing (`FS.GG.Coordination`)

**Dependencies:** PB2; PB3 for integrated qualification.

**Deliverables**

- Add stable finding identities, deduplication, recurrence, disposition, and semantic subjects.
- Implement fresh review epochs, current-head invalidation, bounded confirmation, deep-dive entry, and
  terminal exhaustion.
- Implement capability-profile registry and provider binding qualification.
- Route cheap deterministic work before agents and select the least-cost eligible model profile.

**Acceptance**

- Duplicate wording cannot multiply repair obligations.
- A moved head cannot reuse accepted review improperly.
- The second related late defect enters deep-dive; total exhaustion terminates.
- No eligible model produces a classified wait/fallback/terminal route rather than a routing loop.
- Stronger models are used only when required capabilities or measured risk justify them.

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

**Dependencies:** PB3–PB5.

**Deliverables**

- Build discrete-event scenarios for arrival bursts, correlated failures, flaky checks, provider slowdown,
  runner scarcity, GitHub degradation, review recurrence, and cache corruption.
- Replay accepted historical incidents against baseline and candidate policies.
- Run live shadow planning and record disagreements without mutation.
- Calibrate batching thresholds, isolation strategy, speculation limits, and capability routing.

**Acceptance**

- Candidate preserves every hard property and improves at least one primary p95 latency/resource measure
  without a material regression in another, or is explicitly rejected.
- Tail latency, queue age, cancellation waste, and recovery reserve stay within envelope.
- Prediction calibration and sparse-data cases are visible.
- Shadow disagreement is explained from canonical constraint/objective facts.

### PB7 — Bounded canary and routine-flow adoption (`FS.GG.Coordination`, then `.github`)

**Dependencies:** PB6 accepted policy receipt.

**Deliverables**

- Enable one low-blast-radius, reversible routine code-change class.
- Keep the existing path as rollback and emergency operation.
- Promote exact model bindings and policy version for the canary cohort.
- Verify delivery, post-merge state, telemetry retention, and rollback.

**Acceptance**

- Canary completes enough successes and planted/real failures to exercise both batch and isolation paths.
- No safety, obligation-coverage, or exact-head regression occurs.
- Starter performance envelope holds at p95/p99 with complete denominators.
- Rollback restores the prior route without rewriting receipts.

### PB8 — Adaptive cadence and default eligibility (`.github` policy + Coordination implementation)

**Dependencies:** PB7 soak and comprehensive closure.

**Deliverables**

- Produce scheduled recommendations for check/review cadence from cost, unique yield, delayed detection, and
  closure equivalence.
- Add the bureaucracy-ledger review and deletion workflow.
- Expand to additional work classes one at a time.
- Decide default eligibility through a separate accepted policy/ADR change.

**Acceptance**

- Recommendations never automatically weaken a hard boundary.
- A closure-found miss is charged to the responsible selection/cadence decision and moves the check inward.
- Expired or low-yield controls receive an explicit retain/change/delete disposition.
- Default adoption has a comprehensive exact-head closure receipt and a tested rollback.

## 13. Cross-repository sequence

```text
.github PB0 policy/design authority
          │
          ▼
FS.GG.SDD PB1 profile + compiler + published tooling
          │ publish before consume
          ▼
FS.GG.Coordination PB2 reducer/verifier
          ├────────► PB3 batching/isolation
          ├────────► PB4 review/model routing
          └────────► PB5 live projection
                         │
                         ▼
                    PB6 shadow
                         │
                         ▼
                    PB7 canary
                         │
                         ▼
.github PB8 policy/default decision
```

Any new cross-repo schema follows publish-before-flip: the producer first publishes a validator/compiler that
still accepts the current consumer document; `.github` then bumps and pins the declared schema in a separate
ordered change. No roadmap checkbox substitutes for a published artifact or verified consumer pin.

## 14. Rollout and rollback rules

- Begin with offline replay and simulation; then shadow; then one bounded reversible canary.
- Keep old and candidate decision outputs side by side during shadow, but only the incumbent may mutate.
- During canary, exactly one policy version owns a subject; dual writers are forbidden.
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
| Review findings expand scope without bound | Stable identities, deduplication, active-scope freeze, cross-scope backlog routing, bounded rounds, and terminal quarantine. |
| Strong models are used everywhere | Least-cost eligible capability routing and measured escalation triggers. |
| Cheap models cause rework | Outcome/rework distributions feed eligibility; repeated defects escalate and can disqualify a binding. |
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

## 17. Definition of done

This program is complete only when one real routine change and one planted-failure change both traverse the
new flow and produce:

- checked Quint and compiled-contract identities;
- a verified policy decision with explicit safety and performance feasibility;
- optimistic batched qualification on the common path;
- adaptive isolation that does not replay proven work on the failure path;
- bounded review/repair with fresh exact-head evidence;
- capability-based model selection and complete usage receipts;
- exact-head delivery or an honest terminal refusal;
- a generated static diagram and live execution projection; and
- replay, simulation, shadow, canary, rollback, and comprehensive closure evidence showing that throughput
  improved without buying speed by deleting obligations or hiding failures.

Until then, the design is preparation and evidence—not production authority.
