---
title: "Design: durable actor orchestrator and deterministic agent harness"
category: Design
categoryindex: 4
index: 28
description: "A proposed Akka.NET execution plane for authenticated agent supervision, deterministic scheduling, durable recovery, and guarded reuse of the FS.GG GitHub coordination substrate."
---

# Design: durable actor orchestrator and deterministic agent harness

This design adds a continuously running, authenticated agent harness above the existing FS.GG GitHub
coordination substrate. A pure operations-research control kernel models the delivery system, shapes work,
chooses information-gathering and execution modes, schedules constrained resources, and emits independently
checkable plans. ASP.NET Core and SignalR form the public WebSocket boundary; Akka.NET supplies durable
workflow actors, supervision, workflow isolation, timers, and an optional path to multi-node availability;
disposable coding agents perform bounded creative tasks; and the
existing typed coordination engine plus GitHub/Git remain the authority for claims, fencing, mutation
legality, durable evidence, and recovery visible outside the orchestrator. The orchestrator improves
execution without discarding the already-built distributed control plane or allowing an LLM, socket,
process, actor, timer, or database lease to authorize an external mutation by itself.

| Field | Value |
|---|---|
| Status | Proposed architecture; records direction and implementation preparation, not production authorization |
| Authored | 2026-08-31 11:48 CEST (09:48 UTC) |
| Revised | 2026-09-01 — recentered on an OR-first closed-loop controller and hardened authority, cutover sequencing, determinism, claim ordering, crash consistency, and runner boundaries |
| Scope | Agent creation and communication, deterministic planning, GitHub mediation, persistence, authentication, supervision, liveness, availability, observability, and staged adoption |
| Preserves | GitHub-native multi-host coordination, typed transition checks, Git-ref fencing, exact-head evidence, durable receipts, and scheduled reconciliation |
| Builds on | [ADR-0034](../adr/0034-typed-coordination-engine.md), [ADR-0053](../adr/0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md), [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md), [ADR-0078](../adr/0078-github-substrate-v2-new-only-coordination-authority.md), [ADR-0079](../adr/0079-single-accountable-delivery-authority.md), and the [remaining-v2 architecture review](2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md) |
| Candidate runtime | ASP.NET Core + SignalR + Akka.Hosting + Akka.Persistence; PostgreSQL first, Akka.Cluster deferred |
| Lifecycle placement | Independent of the GitHub Substrate v2 critical path; read-only and shadow work may prepare the runtime, but normal mutation requires the external epoch and a separately accepted operating boundary |
| Primary decision | Treat the OR kernel as the execution-decision control plane, Akka.NET as its preferred durable realization, and FS.GG/GitHub as external coordination authority |

## 1. Context and decision

FS.GG already contains valuable coordination behavior developed from real multi-agent failures: worker
identity, claim fencing, touch-set exclusion, dependency classification, exact-head review, post-write
verification, release recovery, typed unknown/incomplete observations, durable receipts, and an externally
inspectable GitHub representation. Rebuilding those semantics inside a daemon would spend completed work,
create two authorities, and make correctness depend on one machine.

The current operator experience nevertheless asks short-lived agents and their prompts to carry too much
runtime responsibility. They reconstruct state, poll GitHub, remember heartbeats, coordinate reviews,
retry effects, and decide what to do next. A terminated agent can leave durable work behind. A new agent
must recover context from GitHub and prose. Long waits consume interactive turns, and procedural guidance
grows as each incident adds another instruction.

FS.GG therefore adopts the following target direction:

1. **Keep the existing GitHub/Git substrate.** It remains the externally durable coordination and fencing
   authority, usable by independent machines and by the emergency CLI when the orchestrator is unavailable.
2. **Make operations research the execution-decision center.** A versioned OR kernel models work,
   information, uncertainty, dependencies, resources, queues, and risk; it chooses the shape and timing of
   admissible SDD, agent, CI, review, delivery, and recovery actions.
3. **Add one preferred normal executor.** A hosted orchestrator becomes the default path for agent
   creation, scheduling, communication, GitHub access, retry, and recovery. It is not an authorization
   authority, the sole store of coordination truth, or proof that an external transition is legal.
4. **Use actors for lifecycle, not policy authorship.** Akka.NET actors serialize work, persist events,
   supervise children, and schedule wakeups. Pure reducers and constraint solvers decide; actors do not
   hide mutable policy inside callbacks.
5. **Use deterministic admissibility around probabilistic agents.** LLMs may propose, implement, explain,
   and critique. Deterministic code decides whether a proposal is complete, safe, current, affordable,
   and legal to execute.
6. **Expose a narrow authenticated WebSocket protocol.** Clients never address arbitrary actor paths,
   submit arbitrary runtime objects, or receive GitHub credentials.
7. **Persist intent before effects and verify after effects.** Recovery is at-least-once. Idempotency,
   generation fencing, and post-state observation make duplicate execution safe.
8. **Begin as one durable node.** systemd restarts it; clients reconnect and replay. Akka.Cluster is added
   only after a measured availability requirement and an accepted split-brain/fencing design.

This is an additive architecture proposal. It does not amend the GitHub Substrate v2 cutover sequence or
authorize a continuously hosted service for that safety-critical cutover. The runtime operational boundary
must receive its own acceptance before it can become a required production writer.

Design, pure-kernel, read-only, and shadow work may proceed without putting this service on the v2 critical
path. The default sequencing is that bounded mutation canaries begin only after `OperatingV2`. An earlier
mutation role would require a separate accepted decision that names the epoch, writer class, rollback
posture, and reason it does not weaken the cutover. No harness milestone is an implicit prerequisite for
`OpenV2`, and no incomplete harness milestone may delay the independently qualified v2 cutover.

### 1.1 Decision posture

This document deliberately separates architectural commitments from product selections that still need
evidence:

| Posture | Decisions |
|---|---|
| Direction fixed by this proposal | OR-first execution decisions; external GitHub/Git fencing; typed-engine legality; disposable agents; deterministic plan verification; durable intent before effects; one preferred normal executor; emergency CLI retained |
| Candidate pending a vertical slice | Akka.Persistence plugin and serializer; PostgreSQL schema and outbox realization; SignalR replay implementation; identity provider; sandbox/runner technology; artifact store; optimizer and solver libraries |
| Separately accepted before production | operational owner and SLO; data classification and retention; GitHub App principal split; mutation canary scope; disaster recovery; any required hosted-service dependency |
| Deferred by design | Akka.Cluster, sharding, remote actor transport, learned scheduling policy, and a mandatory webhook runtime |

A candidate selection becomes binding only when its named qualification evidence is accepted. Replacing a
candidate must not change the authority split or the public domain contracts silently.

## 2. Goals and non-goals

### 2.1 Goals

The harness must:

- accept authenticated human and machine clients over `wss://`;
- maintain durable logical sessions independently of transient WebSocket connections;
- create a fresh, bounded agent for each unit of creative work;
- supervise agent processes and correlate them with GitHub claims, branches, worktrees, pull requests,
  checks, review epochs, and receipts;
- select and sequence work through inspectable deterministic policy;
- centralize normal GitHub API access, credentials, pagination, rate budgets, mutation serialization,
  retries, and postcondition checks;
- replay local state deterministically and recover safely after process termination, machine reboot, lost
  messages, duplicate commands, and changed external facts;
- retain the existing CLI and GitHub-visible protocol as emergency, diagnostic, and multi-host paths;
- expose why each action was selected, refused, deferred, retried, or escalated;
- isolate one failed issue, agent, repository, or provider without corrupting unrelated workflows; and
- provide a measured path from one node to passive failover or clustering without changing domain policy.

### 2.2 Non-goals

The harness does not:

- replace GitHub issues, Projects, protected refs, checks, releases, or attestations as authoritative facts;
- replace the typed coordination engine or reimplement its transition predicates in actor code;
- treat actor existence, socket connectivity, heartbeat, lease expiry, or agent confidence as delivery evidence;
- provide exactly-once external effects;
- expose Akka.Remote to browsers, agents, or any untrusted network;
- give an agent the GitHub App private key, installation token, database credential, or unrestricted shell;
- make utility optimization capable of overriding a hard invariant;
- require Akka.Cluster, sharding, distributed data, or multi-node discovery in the first release; or
- make a second agent or reviewer an independent delivery authority contrary to ADR-0079.

## 3. Authority and trust boundaries

The architecture has five distinct authorities. Keeping them separate prevents a convenient runtime
mechanism from becoming an accidental source of truth.

| Authority | Owns | Must not own |
|---|---|---|
| Quint / compiled FS.GG contract | Declared process behavior, invariants, model traces, stable semantic identities | Runtime scheduling, credentials, live GitHub facts |
| OR control kernel | Feasibility, work shape, information choice, scheduling, resource allocation, plan verification, explanation | External mutation or transport retry |
| Akka.NET execution plane | Serialization, lifecycle, supervision, timers, persistence, routing, recovery | GitHub transition legality or identity authentication |
| Typed coordination engine | Complete observation classification, claim/effect legality, mutation plans, receipts | Agent creativity or network session management |
| GitHub/Git | Native work facts, protected history, checks, externally visible journals and fencing generations | Agent runtime state or unverified inferred intent |

The public network, agent sandbox, orchestrator process, persistence store, GitHub API, and Git authority
repository are separate trust zones:

```text
Human or machine client
        │  untrusted network
        ▼
TLS / authentication / authorization boundary
        │  typed command envelope
        ▼
ASP.NET Core + SignalR gateway
        │  internal typed messages only
        ▼
Akka.NET actor system ───── durable journal / snapshot / outbox
        │                              │
        │ spawn contract               │ encrypted storage boundary
        ▼                              │
isolated agent worker                  │
        │ proposal/artifact only       │
        └──────────────► deterministic policy
                                      │ guarded plan
                                      ▼
                             typed GitHub adapter
                                      │ short-lived App token
                                      ▼
                              GitHub + protected Git refs
```

An agent response is untrusted content. A WebSocket command is authenticated input, not automatically an
authorized effect. A persisted actor event proves what the orchestrator recorded, not what GitHub accepted.
A GitHub projection may describe a claim, while the protected journal generation fences it. Each boundary
retains its own receipt and identity.

### 3.1 Authority precedence and disagreement

When durable views disagree, precedence is explicit:

1. protected Git history and complete current GitHub observations decide external facts and fencing;
2. the versioned typed coordination engine decides whether those facts admit a transition;
3. the orchestrator journal decides which local command, plan, or attempt it recorded;
4. caches, dashboards, WebSocket state, agent reports, and Project projections are hints or views.

Reconciliation never overwrites an external fact merely to make it agree with the actor journal. It records
the divergence, re-runs the typed reducer over a complete observation, and either adopts the external result,
continues a still-current intent, or quarantines the workflow. A local workflow revision, actor identity,
database lock, or scheduler reservation cannot substitute for the external fencing generation.

## 4. Operations-research control architecture

FS.GG treats software delivery as a partially observed, stochastic, resource-constrained flow system. The
controller repeatedly decides four things: what information to acquire, how to shape the work, which
admissible execution mode to use, and when to commit scarce resources. It does not optimize one backlog
score or ask an LLM to improvise the workflow.

The closed loop is:

```text
complete observations + event history + accepted policy
                         │
                         ▼
              canonical PlanningSnapshot
                         │
          ┌──────────────┼────────────────┐
          ▼              ▼                ▼
    feasibility      OR planners      uncertainty model
    and authority    and simulation   and estimators
          └──────────────┼────────────────┘
                         ▼
                checked DecisionPlan
                         │
                         ▼
             durable actor execution
                         │
                         ▼
        agents / CI / review / provider effects
                         │
                         ▼
          verified outcomes and new observations
                         └──────────► replan
```

Only a bounded first part of a plan is committed. New provider facts, completions, failures, claims,
deadlines, estimates, or resource changes create a new planning snapshot. This rolling-horizon design
retains strategic intent without pretending that long software plans execute exactly as estimated.

### 4.1 Controlled system

The OR kernel models six coupled graphs:

| Graph | Nodes | Edges or capacities | Decisions supported |
|---|---|---|---|
| Product/contract | behaviors, contracts, components, repositories | dependency, compatibility, ownership | release scope and coherent change boundaries |
| Work | SDD and delivery activities | precedence, alternative mode, rework, cancellation | work-item shape and execution sequence |
| Coordination | people, agents, teams, repositories | communication need, touch conflict, independence | decomposition and assignment |
| Evidence | claims, observations, tests, reviews, receipts | proves, invalidates, supersedes | next information and acceptance actions |
| CI | build/test/policy obligations | consumes, produces, requires | selection, partitioning, ordering, runner placement |
| Resources | agent profiles, humans, runners, APIs, mutation lanes, budgets | capacity and calendars | admission, reservation, fairness, and recovery headroom |

The dependency and coordination graphs are distinct. Two tasks may be technically independent yet compete
for one touch set, reviewer, runner, or API budget. Conversely, related tasks may execute in parallel when
their contracts and integration evidence are explicit. [Design-structure-matrix analysis](https://doi.org/10.1109/17.946528)
supplies candidate partitions, while the protected coordination substrate decides actual claims.

### 4.2 Canonical planning snapshot

Every planning decision consumes one immutable snapshot:

```fsharp
type Estimate =
    { Samples: int
      P50: decimal
      P80: decimal
      P95: decimal
      DistributionId: string
      CalibrationWindow: string }

type ExecutionMode =
    { ModeId: string
      RequiredCapabilities: Set<string>
      Duration: Estimate
      Cost: Estimate
      FailureProbability: decimal option
      ReworkProbability: decimal option
      InformationGain: decimal option
      SetupClass: string
      Recoverability: string }

type PlanningSnapshot =
    { SnapshotId: string
      DecisionTime: System.DateTimeOffset
      FleetEpoch: string
      PolicyVersion: PolicyVersion
      WorkGraph: WorkGraph
      CoordinationGraph: CoordinationGraph
      EvidenceGraph: EvidenceGraph
      CiGraph: CiGraph
      ResourceState: ResourceState
      ExecutionModes: Map<string, ExecutionMode list>
      ObservationCompleteness: Map<string, Completeness>
      EstimateSetId: string }
```

Point estimates without provenance are not planning facts. Initially, an estimate may be an explicit wide
prior or a declared unknown. Completed and failed attempts update versioned empirical distributions by
work class, repository, execution mode, agent profile, CI shape, and review path. Censored observations such
as cancellation and timeout remain censored; they are not rewritten as successful durations.

### 4.3 Decision variables

The controller may choose:

- whether to admit, defer, cancel, recover, or quarantine a work subject;
- whether a candidate scope remains one item or is partitioned into independently qualifiable slices;
- which SDD action comes next: clarify, specify, model, prototype, plan, decompose, implement, or review;
- which execution mode and agent profile, if any, performs each activity;
- agent count, context package, tool surface, budget, deadline, and join contract;
- start time, capacity reservation, and dependency order;
- CI obligation closure, job partition, ordering, batching, runner class, and retry policy;
- reviewer set, role coverage, assignment, and review timing;
- whether another observation, experiment, test, or critique is worth its delay and cost;
- whether an uncertain operation should be observed, resumed, compensated, rolled forward, or escalated; and
- which near-term actions are committed before the next replanning boundary.

An LLM may propose work, estimates, decompositions, or model features. It cannot insert them into the
accepted planning snapshot without schema validation, provenance, and deterministic checks.

### 4.4 Constraints before objectives

The feasible set is defined before any optimization. Hard constraints include:

- authority epoch, claim generation, touch-set exclusion, and typed transition legality;
- complete-enough observations for the proposed decision class;
- work and contract precedence;
- independently verifiable vertical-slice and delivery routes;
- reviewer independence, review-epoch freshness, and one accountable delivery owner;
- mandatory SDD, formal, CI, security, release, and post-merge obligations;
- agent, runner, human, repository, API, mutation, disk, time, token, and money capacity;
- sandbox, credential, data-residency, and provider restrictions;
- WIP ceilings plus reserved recovery and incident capacity; and
- explicit stop-the-world and quarantine conditions.

Safety and authority are not penalties. No expected value, deadline, aging debt, or solver objective can buy
permission to violate them. Operational uncertainty inside the feasible set may use robust or chance-bounded
constraints—the adjustable conservatism in [robust optimization](https://doi.org/10.1287/opre.1030.0065)
is useful here—but missing legal authority is never modeled as a probability.

Within the feasible set, objectives are lexicographic:

| Tier | Objective |
|---:|---|
| 1 | Minimize worst-case or bounded probability of missing accepted safety, recovery, and service targets |
| 2 | Minimize weighted tardiness, blocking time, and age of admitted work; protect the critical path |
| 3 | Minimize expected rework, coordination delay, failure recovery, and review/CI feedback time |
| 4 | Minimize money, tokens, runner/API use, duplicated setup, and energy proxies |
| 5 | Reduce fairness and knowledge-concentration debt and prefer information-rich actions where otherwise equivalent |

Scalar weights are allowed only within one declared tier. The receipt preserves the Pareto alternatives and
sensitivity ranges when materially different plans remain feasible.

### 4.5 Decomposed planner stack

One giant model would be difficult to explain, calibrate, and recover. The kernel therefore composes bounded
planners with explicit contracts:

| Planner | Primary model | Output |
|---|---|---|
| `WorkSizer` | DSM/graph partitioning plus setup, cut, variance, and rework costs | candidate independently qualifiable work packages |
| `SddPlanner` | mandatory-stage rules plus value of information and bounded optimal stopping | next design, experiment, or implementation action |
| `PortfolioScheduler` | robust multi-mode resource-constrained project scheduling | rolling start, mode, capacity, and WIP plan |
| `CiPlanner` | sound obligation closure, set cover, bin packing, and parallel-machine scheduling | CI jobs, order, runner placement, and sentinels |
| `ReviewPlanner` | constrained bipartite matching/min-cost flow | reviewers, roles, deadlines, and knowledge-spread disposition |
| `AgentAllocator` | generalized assignment/contract-net selection | content-addressed agent specifications and reservations |
| `RecoveryPlanner` | shortest-path or bounded stochastic planning over saga states | observe, retry, adopt, replace, compensate, roll forward, or escalate |

Each planner reports `Feasible`, `Infeasible`, `Unknown`, or `TimedOutWithCandidate`. A timed-out candidate
is never called optimal. Its plan must still pass the same independent feasibility checker.

### 4.6 Rolling horizon, commitment, and stability

The controller plans farther than it commits. The committed horizon normally contains only:

- capacity and external-claim acquisition for the next dispatches;
- already-approved CI/review actions;
- effect intents whose external preconditions are about to be revalidated; and
- wakeups or observations needed before another decision.

Activities beyond that horizon are forecasts, not promises. Replanning is triggered by material events or a
bounded clock, not every telemetry sample. Already running work has a switching cost, and the planner uses
hysteresis so small estimate changes do not churn agents, reviewers, or CI jobs. A changed hard constraint,
lost generation, safety finding, or newly blocked critical path overrides that stability preference.

Queue control uses bottleneck-specific WIP ceilings rather than maximizing utilization.
[Little's law](https://doi.org/10.1287/opre.9.3.383) makes cycle time, throughput, and WIP jointly accountable;
[heavy-traffic queueing](https://doi.org/10.1017/S0305004100036094) explains why small remaining capacity can
have disproportionate latency value. The policy therefore reserves explicit recovery capacity and treats
persistent near-saturation as an admission defect, not evidence that the system is efficient.

### 4.7 Independent plan verification

Solver output is untrusted until checked by a small deterministic verifier that does not reuse the solver's
search code. The verifier confirms:

- every selected activity and execution mode exists in the snapshot;
- every hard constraint and precedence edge holds;
- all resource intervals fit declared calendars and capacities;
- touch conflicts and independence rules are respected;
- CI obligations and evidence routes are complete;
- committed actions have stable identities and executable recovery dispositions; and
- objective terms and explanation totals recompute from canonical bytes.

For high-risk changes, named semantic mutations remove a dependency, capacity constraint, required gate,
claim generation, reviewer role, or recovery step and must make verification red. Feasibility verification
is required even when a human chooses a non-optimal alternative.

### 4.8 Decision and execution receipts

```fsharp
type OrDecisionReceipt =
    { DecisionId: string
      SnapshotId: string
      PolicyVersion: PolicyVersion
      ModelVersions: Map<string, string>
      SolverIdentity: string
      SolverSettingsDigest: string
      FeasibleAlternatives: string list
      RejectedByConstraint: ConstraintFailure list
      ObjectiveVector: decimal list
      Sensitivity: string list
      SelectedPlanDigest: string
      VerificationDigest: string
      CommittedActions: string list
      NextReplanTriggers: string list }
```

Execution receipts later join predicted and observed duration, cost, failure, rework, queue delay, CI yield,
review yield, and delivery outcome. The estimator never trains on an agent's self-reported success; it uses
verified workflow and provider facts.

### 4.9 Simulation and policy promotion

No new policy moves directly from a notebook or LLM proposal into live dispatch. Promotion proceeds through:

1. deterministic unit and feasibility tests;
2. replay over historical snapshots and named incidents;
3. discrete-event simulation with arrival, duration, failure, rework, and outage scenarios;
4. adversarial/mutation cases for hard constraints and estimator error;
5. live shadow decisions against the incumbent policy;
6. a bounded canary with explicit rollback; and
7. an accepted versioned policy receipt.

Historical replay cannot prove counterfactual outcomes, so policy comparison reports uncertainty and relies
on simulation or controlled canaries for causal claims. The simulator follows a declared modeling method
rather than an ad hoc queue script; prior work provides a
[systematic discrete-event method for software processes](https://arxiv.org/abs/1403.3559). Online learning
may update advisory estimates inside accepted bounds; it may not rewrite constraints, objectives, or
promotion criteria.

### 4.10 Solver realization

[Google OR-Tools CP-SAT](https://developers.google.com/optimization/cp/cp_solver) is the first candidate for
finite-horizon scheduling, alternative execution modes, interval capacities, and assignment because it has
a supported .NET surface and established [scheduling](https://developers.google.com/optimization/scheduling/job_shop)
and [assignment](https://developers.google.com/optimization/assignment/assignment_example) models. Graph
partitioning, transitive closure, min-cost flow, and small value-of-information enumerations remain dedicated
pure algorithms rather than being forced into CP-SAT.

Every production solve pins solver binary, parameters, worker count, seed, canonical input, time/deterministic
budget, and objective hierarchy. Where deterministic reproduction across a solver upgrade cannot be proved,
the accepted plan bytes and independent verifier are authoritative; upgrade qualification compares feasible
sets and objective bounds before changing the solver identity. A simple baseline heuristic remains available
when the optimizer is unavailable, but it must satisfy the same hard constraints and disclose its degraded
objective quality.

## 5. Runtime topology

### 5.1 ASP.NET Core host

One .NET Generic Host runs:

- Kestrel behind a TLS-terminating reverse proxy or with direct TLS;
- SignalR hubs and small HTTP control endpoints;
- authentication and authorization middleware;
- liveness, readiness, dependency, and metrics endpoints;
- Akka.Hosting and the actor system;
- PostgreSQL connectivity for Akka.Persistence and the transport inbox/outbox;
- the GitHub App credential broker; and
- graceful startup and shutdown coordination.

Akka.Hosting is the preferred integration package because it binds Akka.NET to
`Microsoft.Extensions.Hosting`, configuration, dependency injection, logging, OpenTelemetry, and health
checks. The public service remains an ordinary ASP.NET Core application; actors do not replace its security
middleware or HTTP lifecycle.

### 5.2 Actor hierarchy

```text
/user/orchestrator
├── decision                       canonical snapshot and pure OR facade
│   ├── policy                     policy/model/estimate version registry
│   ├── planners                   work, SDD, portfolio, CI, review, agent, recovery
│   └── verifier                   independent feasibility and receipt checker
├── scheduler                      committed capacity, dispatch, and replan wakeups
├── reconciler                     complete audits and subject reconciliation
├── github
│   ├── observations               bounded concurrent, cache only immutable facts
│   ├── mutations/<aggregate>      serialized guarded mutation lanes
│   ├── rate-budget                REST/GraphQL/install budget and backpressure
│   └── credential-broker          mints scoped short-lived installation tokens
├── sessions/<session-id>          durable logical human/agent sessions
│   └── connections/<connection>   transient SignalR connection proxies
├── work/<repo>/<issue>            durable issue process entities
│   ├── agent/<attempt>            one disposable creative-agent attempt
│   ├── critic/<epoch>             optional fresh critique phase identity
│   └── delivery/<generation>      exact-head delivery process
├── operations/<operation-id>      cross-step mutation saga
├── audit                          evidence and invariant projections
└── quarantine                     failed entities requiring operator decision
```

The top-level guardian applies supervision policy. An issue actor owns one logical workflow and is the
only actor allowed to evolve that workflow's local state. GitHub mutation actors are partitioned by
conflict aggregate rather than represented by one global bottleneck. Connection actors are disposable;
session and work actors are durable.

### 5.3 Actor responsibilities

| Actor | Persistent | Responsibility |
|---|---:|---|
| `Decision` | Receipt/pointer | Materialize a canonical snapshot, invoke pure planners/verifier, and publish a checked plan |
| `Scheduler` | Yes | Execute only committed checked-plan actions; maintain capacity reservations and replan wakeups |
| `WorkItem` | Yes | Execute one issue lifecycle and correlate every attempt and external artifact |
| `AgentAttempt` | Partly | Supervise one sandbox/process; durable facts live in its parent work item |
| `Session` | Yes | Identity, capability generations, replay cursor, and connection replacement |
| `Connection` | No | Translate outbound actor events to one SignalR connection and report transport state |
| `GitHubObservation` | No/cache | Perform bounded typed reads; never turn incomplete data into absence |
| `GitHubMutation` | Intent/receipt | Serialize by aggregate, mint token, re-read, apply, verify, record |
| `Operation` | Yes | Manage a resumable multi-step saga and compensation plan |
| `Reconciler` | Cursor/receipt | Repair missed events and out-of-band changes through complete audits |
| `Policy` | Version pointer | Resolve exact constraint, objective, model, estimate, solver, and baseline identities |

### 5.4 Durable ownership inside the execution plane

Actor serialization is local to one persistence identity. Cross-entity decisions therefore use explicit
protocols rather than assuming that the hierarchy supplies a transaction:

- `Scheduler` owns bounded local capacity reservations and fairness accounting;
- `WorkItem` owns the lifecycle revision and agent-attempt lineage for one canonical issue subject;
- protected coordination journals own claims, touch-set grants, review epochs, and operation generations;
- mutation lanes serialize provider calls but do not own the domain decision; and
- `Operation` owns saga progress while each external step remains independently fenced and verified.

Messages between these owners carry stable command identities and expected revisions. A timeout produces an
unknown outcome followed by inspection, not an inferred rollback. Capacity reservation and external claim
acquisition form a resumable saga: either may need compensation, and neither alone is a work grant. The
canonical work-item persistence ID and conflict aggregate are derived by one versioned normalizer so aliases,
case differences, repository transfers, or issue URL forms cannot create parallel actors for one subject.

## 6. Domain model and deterministic core

Actor messages are commands and observations. Persisted records are domain events. External writes are
effects. These categories must not share a catch-all object type.

Illustrative F# contracts:

```fsharp
type WorkflowRevision = WorkflowRevision of int64
type PolicyVersion = PolicyVersion of string
type IdempotencyKey = IdempotencyKey of string
type FencingGeneration = FencingGeneration of int64

type Completeness =
    | Complete
    | Incomplete of reason: string * cursor: string option
    | Unauthorized of reason: string
    | Unsupported of capability: string
    | Indeterminate of reason: string

type CommandEnvelope<'command> =
    { CommandId: IdempotencyKey
      CausationId: string
      CorrelationId: string
      PrincipalId: string
      SessionId: string
      SessionGeneration: int64
      Subject: string
      ExpectedRevision: WorkflowRevision option
      IssuedAt: System.DateTimeOffset
      ExpiresAt: System.DateTimeOffset
      Command: 'command }

type Decision<'action> =
    | Act of actions: 'action list * explanation: DecisionExplanation
    | Wait of wakeups: Wakeup list * explanation: DecisionExplanation
    | Refuse of reasons: Refusal list * explanation: DecisionExplanation
    | Escalate of question: Escalation * explanation: DecisionExplanation

type SolveStatus =
    | Optimal
    | FeasibleWithBound of bound: decimal option
    | TimedOutWithCandidate of bound: decimal option
    | Infeasible
    | Unknown of reason: string

type CandidatePlan =
    { PlanId: string
      SnapshotId: string
      Status: SolveStatus
      ObjectiveVector: decimal list
      Actions: DomainAction list
      CommittedActionIds: string list }

type PlanningDecision =
    | Proposed of plan: CandidatePlan * explanation: DecisionExplanation
    | WaitForPlanningInput of wakeups: Wakeup list * explanation: DecisionExplanation
    | PlanningRefused of reasons: Refusal list * explanation: DecisionExplanation
    | PlanningEscalated of question: Escalation * explanation: DecisionExplanation

type CheckedPlan =
    { Candidate: CandidatePlan
      VerificationDigest: string }

type EffectIntent<'effect> =
    { IdempotencyKey: IdempotencyKey
      Subject: string
      ExpectedWorkflowRevision: WorkflowRevision
      ExpectedExternalGeneration: FencingGeneration option
      PolicyVersion: PolicyVersion
      Effect: 'effect }
```

The central functions remain pure:

```fsharp
val evolve : WorkflowState -> WorkflowEvent -> WorkflowState
val materializeSnapshot : Policy -> ObservationSet -> WorkflowState list -> Result<PlanningSnapshot, Refusal list>
val plan : Policy -> PlanningSnapshot -> PlanningDecision
val verifyPlan : Policy -> PlanningSnapshot -> CandidatePlan -> Result<CheckedPlan, ConstraintFailure list>
val compileEffects : PlanningSnapshot -> CheckedPlan -> EffectIntent<ProviderEffect> list
```

`evolve` is replayed by persistence recovery and property-tested for determinism. `plan` is called only with
a canonical fingerprinted snapshot. `verifyPlan` uses independent constraint-checking code before any
commitment. `compileEffects` sees only verified committed actions and cannot manufacture a missing generation
or convert an incomplete observation into permission.

### 6.1 Determinism envelope

Determinism is a contract over recorded inputs, not a claim that wall clocks, APIs, solvers, or model
providers are naturally deterministic. Every policy evaluation binds:

- canonical state and observation bytes plus their schema versions and completeness proofs;
- an explicit decision time or logical tick rather than a direct wall-clock read;
- policy, compiled-contract, rule-corpus, solver, and tool versions;
- a recorded random seed for any permitted randomized tie-break or search;
- canonical collection ordering, string normalization, and identifier comparison;
- numeric representation, rounding, timeout, and solver optimality settings; and
- declared resource budgets and capability inventory.

The decision receipt contains those identities and the selected plan digest. Stable ordering resolves equal
solutions unless a recorded seed is an intentional policy input. Time passing changes a decision only after
a durable tick or fresh observation is admitted. Replaying the same envelope must reproduce the same
decision and explanation bytes; obtaining a different live observation correctly creates a new envelope.

Deterministic replay proves reproducibility, not correctness or freshness. Invariants, independent oracles,
and provider re-observation remain mandatory.

### 6.2 Why pure policy remains outside actors

Putting decisions directly in actor receive handlers would make concurrency safe but meaning difficult to
test, compare, simulate, or model-check. The actor should perform a small protocol:

```text
receive command or observation
→ validate envelope and expected revision
→ materialize canonical planning snapshot
→ call pure planners and independent verifier
→ persist decision receipt and committed domain events atomically
→ update state by evolve
→ dispatch persisted effect intents
→ receive verified receipts
→ persist outcomes
```

The same policy package can then drive simulation, replay an incident, explain a live refusal, generate
counterexamples, and compare proposed versions without starting Akka or contacting GitHub.

## 7. OR models for the delivery lifecycle

The common planning snapshot does not imply one common algorithm. Each lifecycle surface receives the
smallest model that captures its real decision, uncertainty, and failure cost.

### 7.1 Work-item shape and decomposition

`WorkSizer` begins from required behavior, contract boundaries, evidence obligations, and a design structure
matrix—not a target number of lines, files, or hours. Candidate cuts must preserve an independently
qualifiable vertical slice and name the integration contract between slices. Empirical work on
[coordination requirements](https://doi.org/10.1145/1180875.1180929) supports modeling the communication
created by technical dependencies rather than assuming repository or team boundaries contain it.

For a candidate partition `P`, the model estimates:

```text
ShapeCost(P) = fixed claim/branch/context/setup cost
             + expected implementation and integration time
             + cut-edge coordination and contract cost
             + expected CI and review cost
             + expected contention delay
             + expected rework and recovery cost
             + critical-path and tail-latency penalty
```

Very small items repeatedly pay setup, claims, context compilation, CI startup, review, and merge overhead.
Very large items increase duration variance, time to feedback, review context, touch contention, stale-base
risk, and failure blast radius. The minimum is learned per work class and repository; it is not a universal
size threshold. A [large multi-platform empirical study](https://arxiv.org/abs/2203.05045) found no general
relationship between pull-request size and merge time, reinforcing the decision not to use LOC as the
sizing authority.

The planner proposes `Keep`, `Split(partition)`, `Merge(subjects)`, or `Probe(question)`. Splitting is admitted
only when the expected flow/rework gain exceeds the added coordination cost across credible estimate ranges.
A human or agent may propose a partition, but the graph, constraints, evidence routes, and estimate provenance
must compile independently.

### 7.2 Portfolio flow, WIP, and robust scheduling

`PortfolioScheduler` solves a rolling multi-mode resource-constrained project scheduling problem. Activities
have alternative execution modes, precedence, calendars, shared resources, touch conflicts, setup classes,
and uncertain duration/rework. Robust scenarios cover credible duration, outage, and rework variation; the
nominal fastest schedule is not accepted if small perturbations collapse it. A
[two-stage robust RCPSP formulation](https://arxiv.org/abs/2004.06547) demonstrates that adjustable robust
project scheduling can remain computationally tractable enough to serve as a candidate basis.

Admission control is bottleneck-specific:

- cap active implementation by agent and touch-set capacity;
- cap review-ready work by reviewer capacity;
- cap merge-ready work by CI and mutation capacity;
- reserve capacity for recovery, incidents, and critical unblockers;
- admit new work from measured departure and aging behavior rather than utilization targets; and
- stop dispatch when observation completeness or estimate calibration falls outside policy.

Little's law provides the accountability identity between throughput, WIP, and cycle time. Heavy-traffic
queueing supplies the warning that high utilization plus variable service time creates nonlinear waiting.
The controller therefore reports bottleneck queue age, utilization distribution, service-time variation,
and capacity headroom together. It does not celebrate throughput purchased with an exploding review queue.

The first baseline remains transparent: feasible critical-path work, recovery reserve, earliest accepted
deadline, aging, downstream unblock count, setup affinity, weighted fair allocation, then canonical tie-break.
The robust solver must beat that baseline in replay and simulation before taking dispatch authority.

### 7.3 SDD as sequential value of information

The SDD lifecycle is a sequential decision problem, not a mandatory amount of prose. Its action set includes:

```text
Clarify | Specify | Model | Prototype | Research | Plan | Decompose
ImplementSlice | Review | Reobserve | AskHuman | StopAsReady | Refuse
```

Mandatory constitutional, authority, safety, and evidence gates remain hard constraints. Within them,
`SddPlanner` compares the expected value of another information action with its cost and delay. The value of
sample information is the expected reduction in downstream decision loss after observing its result. A
clarification, model, prototype, research pass, or critique proceeds when that reduction plausibly exceeds
the action cost and the result can change a real decision.

Examples:

- model a concurrency protocol when counterexamples could change the authority or fencing design;
- prototype an uncertain provider capability before writing a fleet migration plan around it;
- clarify an ambiguity when alternative answers produce materially different work or acceptance paths;
- stop elaborating when remaining uncertainty cannot change the selected feasible plan;
- return from implementation to specification when a finding invalidates a bound assumption; and
- reject ceremonial artifacts that add no information, constraint, or executable evidence.

This combines [Boehm's risk-driven spiral](https://doi.org/10.1109/2.59) with
[formal value-of-information accounting](https://doi.org/10.1214/aoms/1177728069). Requirements and
release choices use cost, value, dependency, risk, and uncertainty together rather than prioritizing value
alone; this extends established [cost-value requirements prioritization](https://doi.org/10.1109/52.605933)
and [robust next-release planning](https://doi.org/10.1145/2576768.2598334). The decision receipt records the
uncertain decision, candidate information actions, expected decision change, cost/delay, chosen action, and
stopping reason.

### 7.4 CI and qualification shape

`CiPlanner` separates four decisions that are often conflated:

1. **Obligation closure:** derive every required build, test, formal, policy, security, package, release, and
   post-merge obligation from changed semantic subjects and non-file inputs. This is a soundness constraint.
2. **Selection:** remove an obligation only when the accepted dependency model proves it not applicable.
3. **Partition and placement:** group selected obligations into jobs and runners.
4. **Ordering and cadence:** decide which failures can be surfaced first and which full sentinels run later.

Job partitioning minimizes expected feedback makespan, runner cost, repeated checkout/restore/setup,
retry amplification, artifact transfer, and failure-localization penalty. Constraints preserve dependency
order, job limits, required aggregate outputs, independent-control separation, runner capability, and full
merge-group behavior. More parallel jobs can reduce makespan, but queue and setup overhead create a measured
point of diminishing returns.

Regression selection starts with dependency-based sound closure. Historical predictive selection may rank
or supplement tests only after its miss rate, flakiness behavior, and drift are measured. Test ordering
maximizes expected fault detection per unit feedback time while the complete selected gate continues. Prior
CI research supports both [cost-aware selection and failure-early prioritization](https://doi.org/10.1145/2635868.2635910)
and [dynamic dependency selection](https://doi.org/10.1145/2771783.2771784). An industrial
[predictive-selection deployment](https://arxiv.org/abs/1810.05286) shows large cost savings are possible but
also demonstrates why explicit detection guarantees and full sentinels remain necessary.

Batching is an execution mode, not a default. The planner compares single-change runs, fixed/dynamic batches,
and shared test-case batches using arrival rate, setup cost, failure prevalence, localization/retest cost,
runner count, and deadline. Every batch preserves exact change membership and a deterministic isolation plan
for failures. [Large-scale CI batching research](https://arxiv.org/abs/2308.13129) shows that feedback time
and machine use respond nonlinearly to batch and runner count, supporting empirical policy calibration.
Scheduled full-suite sentinels compare selected closure with actual failures; a miss disables selection for
the affected policy scope.

### 7.5 Review and delivery pipeline

`ReviewPlanner` treats review as constrained information acquisition plus human capacity allocation. It uses
a bipartite assignment/min-cost-flow model with:

- required architecture, security, operations, domain, migration, or delivery roles;
- exact independence, authorization, and conflict constraints;
- expertise over the changed contracts and fault classes;
- active workload, queue age, calendars, and service targets;
- knowledge-concentration and succession risk;
- review-epoch freshness and change invalidation; and
- setup/context affinity without allowing self-review.

The objective balances time to qualified review, fault-domain coverage, workload concentration, and knowledge
spread. [Large-scale reviewer-recommendation evidence](https://arxiv.org/abs/1806.07619) shows that no one
model is best across repositories, and [expertise/workload/turnover simulation](https://arxiv.org/abs/2312.17236)
demonstrates that reviewer choice can deliberately reduce knowledge risk.
Recommendations remain advisory until the selected human or agent identity satisfies the real authority rule.

Review size is measured by semantic and evidence surface, not LOC. The model considers changed behaviors,
contracts, authority boundaries, generated evidence, novelty, reversibility, and reviewer context. It may
request an early architecture critique before implementation, focused independent critiques in parallel, or
one consolidated exact-snapshot review. [Fagan's inspection process](https://doi.org/10.1147/sj.153.0182)
separates preparation, inspection, rework, and follow-up: a finding is not a fix, and a fix is not closed until
independently re-observed. Modern review research also shows that understanding and knowledge transfer are
material outcomes beyond defect discovery ([Microsoft](https://doi.org/10.1109/ICSE.2013.6606617),
[Google](https://doi.org/10.1145/3183519.3183525)).

The OR controller never creates a second delivery authority. It schedules critique and review evidence around
the single accountable delivery owner required by ADR-0079.

### 7.6 Agent allocation and context compilation

Agents are stochastic execution modes, not workflow owners. `AgentAllocator` chooses among no agent, one
agent, sequential specialist agents, bounded parallel agents, a human, or a deterministic tool. Each profile
is described by measured capability, duration, token/money cost, success, rework, tool needs, context limits,
and failure/recovery behavior.

The assignment resembles generalized assignment and the
[contract-net pattern](https://doi.org/10.1109/TC.1980.1675516): a work contract declares the
objective and constraints, eligible profiles expose capabilities and estimates, and the controller awards a
bounded attempt. No agent negotiates away authority, evidence, sandbox, or budget constraints.

Context compilation has two classes:

- **mandatory:** authority boundary, exact snapshot, objective, dependencies, touch set, allowed paths/tools,
  governing contracts, safety rules, required outputs, and completion/evidence contract;
- **selectable:** source excerpts, history, examples, logs, research, prior attempts, and specialized skills.

Selectable context is a budgeted information problem. The compiler prefers evidence expected to change the
agent's decision or reduce error, discounts redundant material, and records omitted candidates. The immutable
context manifest, custom instructions, model/reasoning profile, sandbox, skills, tools, token budget, and
deadline form the `AgentSpecification`. Current
[Codex subagent facilities](https://learn.chatgpt.com/docs/agent-configuration/subagents) already support
specialized instructions and per-agent model, reasoning, sandbox, MCP, and skill configuration; the harness
adds durable work identity, external claims, resource accounting, and evidence contracts around that
execution surface.

Parallelism is selected only when expected critical-path or information-diversity benefit exceeds duplicated
setup, context, merge, review, and reconciliation cost. Write-heavy agents require disjoint touch sets or
isolated speculative outputs. Results join through typed artifacts/findings, never natural-language success.

### 7.7 Recovery, release, and reconciliation

`RecoveryPlanner` searches a bounded state graph whose actions include `Observe`, `Wait`, `Resume`, `Adopt`,
`Replace`, `RetryIdempotent`, `Compensate`, `RollForward`, `AskHuman`, and `Quarantine`. Every edge declares
preconditions, expected observations, irreversible effects, cost, deadline, and terminal evidence. An unknown
effect outcome always selects observation before retry.

Release planning extends the same model across coherent package sets, two feeds, tags, attestations, clean
consumer verification, and recovery capacity. It may optimize start time, sequencing, and reserved capacity,
but byte identity, version coherence, provenance, and protected environment requirements remain constraints.

Webhooks, scheduled audit, and emergency CLI actions all feed reconciliation. The planner does not assume its
previous plan still owns reality; it rebuilds from complete external observations and records adoption or
divergence explicitly.

### 7.8 Policy integrity and Goodhart resistance

Agents and optimizers never receive one naked completion score. Controls include:

- keep evidence predicates independent of agent and planner self-report;
- calculate delivery only from verified GitHub, build, review, and receipt facts;
- retain the lexicographic objective vector and Pareto alternatives;
- bound retries, WIP, tokens, money, and wall time independently of claimed progress;
- audit predicted versus observed duration, cost, yield, failure, and rework;
- preserve refused, failed, cancelled, timed-out, and censored outcomes;
- prohibit a policy change from editing its own acceptance corpus or estimator history; and
- require independent feasibility checks even for incumbent and human-selected plans.

### 7.9 Explanation contract

Every result emits a stable explanation:

```fsharp
type DecisionExplanation =
    { PolicyVersion: PolicyVersion
      PlanningSnapshot: string
      ObservationFingerprint: string
      Considered: CandidateSummary list
      RejectedByConstraint: ConstraintFailure list
      ObjectiveVector: ObjectiveTerm list
      Sensitivity: string list
      Selected: string option
      TieBreak: string option
      CommitmentHorizon: string list
      NextReplanTriggers: string list }
```

An operator must be able to answer: what was known and unknown, which constraints applied, which alternatives
were considered, why this action won, how robust the choice is to estimate error, what has actually been
committed, and what observation would change the decision.

## 8. Agent harness lifecycle

### 8.1 Work admission

Work enters from an authenticated human command, GitHub event, scheduled audit, roadmap driver, or follow-up
generated by an existing workflow. The gateway normalizes it to a subject key and causation identity. The
decision facade performs a fresh inventory read or consumes a complete fingerprinted snapshot, asks the OR
kernel for a checked admission and execution-mode decision, and persists `WorkAdmitted` or a typed refusal.

No agent is spawned merely because an issue exists. The work item must be schedulable, scoped, and assigned
a capability and resource envelope.

Admission is not a claim. For ordinary write-capable work, the ordering is:

```text
complete external observation
→ deterministic candidate decision
→ bounded local capacity reservation
→ typed external claim/touch-set plan
→ protected-journal claim and fencing generation
→ complete verification of the accepted claim
→ durable WorkGranted event
→ workspace preparation and agent spawn
```

The local reservation has a short bounded lifetime and is released if the external claim loses a race. The
verified external claim—not the scheduler row—authorizes the attempt to begin write-capable work. Every
privileged tool request and delivery transition rechecks that the attempt still names the current
generation. Losing the generation revokes the session capability, freezes or terminates the runner, closes
provider-effect capabilities, and sends the workflow through adoption, replacement, or quarantine policy.
Files the process managed to change locally remain untrusted stale artifacts and cannot be promoted without
a fresh claim and base inspection.

Explicit speculative work may skip the external claim only when its specification is read-only, produces no
shared branch or provider mutation, cannot reserve a delivery route, and labels every artifact uncommitted.
Promotion of speculative output requires the ordinary claim sequence against a fresh base and observation.

### 8.2 Agent specification

The orchestrator creates an immutable `AgentSpecification`:

```fsharp
type AgentSpecification =
    { AgentSpecId: string
      PlanningSnapshotId: string
      DecisionPlanId: string
      ExecutionModeId: string
      ResourceReservationId: string
      WorkItem: string
      Attempt: int
      Objective: string
      AllowedRepositories: string list
      BaseRevisions: Map<string, string>
      AllowedPaths: string list
      ForbiddenPaths: string list
      RuntimeProfile: string
      ModelProfile: string
      Skills: SkillBinding list
      ContextManifestId: string
      ReadCapabilities: Capability list
      WriteCapabilities: Capability list
      ToolAllowlist: ToolContract list
      TokenBudget: int option
      WallClockDeadline: System.DateTimeOffset
      HeartbeatInterval: System.TimeSpan
      RequiredOutputs: OutputContract list
      JoinContract: JoinContract option
      PolicyVersion: PolicyVersion
      SpecificationFingerprint: string }
```

The specification is content-addressed. Any change creates a new attempt or explicit amendment event; it
does not silently alter the running agent's authority.

### 8.3 Workspace preparation

Before process creation, a workspace provisioner:

1. verifies the exact base revision and repository identity;
2. creates an isolated worktree or disposable checkout;
3. applies path and tool policy;
4. materializes only the required skills and context;
5. writes no secret into the workspace;
6. records environment, toolchain, dependency-lock, and policy fingerprints;
7. allocates CPU, memory, disk, process, network, and time limits; and
8. returns a signed `WorkspacePrepared` receipt.

Arbitrary deterministic code runs in the same sandbox class as agent-authored code, not in the
credential-bearing orchestrator. It receives explicit input files or messages and returns artifacts plus
digests. Network is denied by default and granted per tool contract.

### 8.4 Runner trust boundary

The runner is a separate security principal from the credential broker and Web host even if the first
deployment places them on one machine. A compromise of agent-authored code must not expose the App private
key, session-signing key, database credential, host container/runtime socket, another workspace, or a
provider token. The selected isolation mechanism must explicitly address process, filesystem, device,
network, IPC, kernel, and resource boundaries; a different working directory alone is not isolation.

Workspace ingress rejects or neutralizes executable Git hooks, unsafe configuration includes, symlink or
hard-link escapes, device files, sockets, path traversal, archive expansion bombs, and artifacts whose
declared size or digest does not match the transferred bytes. Workspace egress enters a quarantine area and
is inspected before the credential-bearing process parses or publishes it. Large or untrusted content is
passed by immutable object reference, never embedded in actor messages or privileged logs.

Model-provider access follows the same rule as GitHub access. The agent receives either a narrowly scoped,
short-lived provider capability or a brokered inference tool; it does not receive the orchestrator's durable
model credential. Provider prompts, responses, retention, region, and training-use policy are part of the
data-classification decision, not merely runner configuration.

### 8.5 Process creation

The `AgentAttempt` actor requests process creation from a narrow runner service. The runner starts the
configured harness with:

- a one-time bootstrap token;
- orchestrator WebSocket URL;
- durable session ID and generation;
- specification fingerprint;
- workspace path visible only inside the sandbox;
- no GitHub credential; and
- an initial context bundle reference.

The agent exchanges the bootstrap token for a short-lived session capability after mutually establishing
the expected specification fingerprint. Reusing the bootstrap token or connecting with the wrong
fingerprint is refused and audited.

### 8.6 Context delivery

Context is layered and pullable:

1. **Invariant envelope:** authority boundary, security rules, completion contract, and stop conditions.
2. **Work envelope:** issue, exact snapshot, dependencies, touch set, expected outputs, and accepted design.
3. **Skill bindings:** content-addressed skill identities and only the references selected by those skills.
4. **On-demand evidence:** source files, logs, CI results, GitHub observations, or model traces requested
   through typed read capabilities.

The orchestrator stores context manifests and digests, not an assumption that a conversation transcript is
a stable protocol. A reconnecting or replacement agent receives the same immutable envelope plus subsequent
durable events.

### 8.7 Agent communication protocol

The WebSocket protocol is a versioned discriminated union, serialized with a schema-bound format. Clients
cannot submit CLR type names or arbitrary Akka messages.

Client-to-server messages include:

- `Hello(specificationFingerprint, protocolVersion, resumeCursor)`;
- `Heartbeat(activity, progressRevision, resourceUse)`;
- `ObservationRequest(subject, fields, reason)`;
- `Proposal(plan, assumptions, expectedOutputs)`;
- `Progress(summary, changedArtifacts, nextStep)`;
- `ArtifactProduced(kind, digest, location, provenance)`;
- `ToolRequest(toolContract, arguments, idempotencyKey)`;
- `Finding(severity, subject, evidence, suggestedAction)`;
- `CompletionCandidate(outputs, tests, residualRisks)`;
- `Blocked(reason, missingCapability, requestedDecision)`; and
- `CancelAcknowledged(checkpoint)`.

Server-to-client messages include:

- `Accepted(sessionGeneration, capabilities, serverCursor)`;
- `ContextManifest(manifest)`;
- `ObservationResult(completeness, fingerprint, facts)`;
- `ProposalAccepted(planRevision)` or `ProposalRejected(reasons)`;
- `ToolReceipt(outcome, evidence)`;
- `CapabilityChanged(newGeneration, capabilities)`;
- `Pause(reason, checkpointRequired)`;
- `Resume(contextDelta)`;
- `Cancel(reason, deadline)`;
- `Replace(reason, handoffContract)`; and
- `SessionClosed(disposition, durableReceipt)`.

Every message carries `messageId`, `sessionId`, `sessionGeneration`, `sequence`, `causationId`, `issuedAt`,
and `expiresAt`. The server durably deduplicates message IDs before acknowledgment. On reconnect the client
presents its last contiguous received sequence; the server replays retained events or supplies a new
content-addressed snapshot plus its first subsequent sequence.

### 8.8 Tools and deterministic code

Agents never invoke privileged provider operations directly. They request a named tool contract:

```fsharp
type ToolContract =
    { ToolId: string
      Version: string
      InputSchema: string
      OutputSchema: string
      RequiredCapability: string
      SideEffectClass: SideEffectClass
      Timeout: System.TimeSpan
      MaximumOutputBytes: int
      NetworkPolicy: NetworkPolicy }
```

Tool classes are:

- **pure:** deterministic transformation over explicit inputs;
- **read:** external observation with completeness and freshness metadata;
- **workspace-write:** mutation confined to the assigned worktree;
- **provider-plan:** creates a sealed effect plan but performs no external mutation;
- **provider-effect:** privileged, actor-executed, fenced, and never available directly to agents.

Pure tools record input, executable, configuration, and output digests. A claimed deterministic tool is
qualified by replaying a corpus in clean environments. Nondeterministic outputs are either normalized or
named honestly.

### 8.9 Subagents

A work agent may propose decomposition, but only the parent `WorkItem` actor may authorize another process.
Each subagent receives its own specification, session generation, workspace boundary, capability token,
budget, and completion contract. Parent and child do not share a mutable identity or an unrestricted
mailbox.

`AgentAllocator` admits subagents when parallelism provides value and touch sets, dependencies, and
resources permit it. Fan-out is bounded. Results are joined through explicit artifact or finding contracts;
the parent agent does not treat a child's natural-language success claim as evidence.

### 8.10 Completion and disposal

An agent's `CompletionCandidate` starts verification; it does not complete the workflow. The work actor:

1. checks required output contracts and artifact digests;
2. inspects the workspace diff and touch-set compliance;
3. runs selected deterministic tests and model correspondence checks;
4. requests an independent policy verdict over current observations;
5. persists accepted artifacts and the agent disposition;
6. closes the agent capability generation;
7. terminates the sandbox; and
8. proceeds to delivery or records a typed refusal.

Fresh disposable agents remain the norm. Durable workflow state belongs to the harness, not to a long-lived
LLM context.

## 9. WebSocket authentication and authorization

### 9.1 Three identities

The service keeps separate:

1. **connection identity:** the authenticated principal at the network boundary;
2. **session/workflow identity:** the durable agent or human session and its capabilities; and
3. **provider identity:** the GitHub App installation principal used for external effects.

A connection may be replaced without changing the session. A session generation may be revoked without
changing the human identity. A provider token may rotate without changing either.

### 9.2 Human authentication

Human clients use an authorization-code flow with PKCE through an accepted identity provider. GitHub
identity is suitable when organization membership and repository access are the relevant facts. The
orchestrator maps the provider's stable immutable user ID to local roles and revalidates authorization at
bounded intervals and before privileged operations.

Local roles are intentionally small: observer, operator, delivery owner, emergency operator, and service
administrator. Authentication does not imply any of them.

### 9.3 Machine authentication

Machine clients use mutually authenticated TLS where practical, then exchange a one-time bootstrap token
for a short-lived, audience-bound session capability. A capability contains:

- issuer and audience;
- immutable subject and session generation;
- work item and allowed repositories;
- exact capabilities;
- issue and path constraints where applicable;
- issued, not-before, and expiry times;
- unique token ID; and
- policy/specification fingerprint.

Static API keys and bearer tokens without expiry are prohibited. Revocation increments the durable session
generation and disconnects every older connection.

### 9.4 Per-message authorization

SignalR authenticates the connection, but the gateway authorizes each command envelope. It verifies token
expiry, session generation, subject, capability, expected workflow revision, command schema, size, and rate
limit before forwarding an internal typed message.

Browser WebSocket implementations may transmit bearer tokens as an `access_token` query value. TLS is
mandatory; reverse-proxy, ASP.NET, tracing, exception, and analytics logs must redact it. Connections have
a bounded maximum age because the principal established at connection time does not automatically change
when external authorization changes.

### 9.5 GitHub authentication

Normal automation uses a GitHub App rather than a PAT. The credential broker holds the App private key
outside agent workspaces and mints installation tokens restricted to the required installation,
repositories, and permissions. Tokens are cached only until a conservative pre-expiry boundary and never
persisted in actor events, logs, traces, or command envelopes.

The production design does not assume one broad App principal. Read-only observation, normal coordination
journal writes, repository mutations, release operations, and administration are distinct permission
classes. H0 must decide which classes require separate Apps or protected environments and must document any
temporary shared-principal risk acceptance. A token minted for one class cannot be reused to cross into
another merely because the credential broker can technically request both permissions.

Where a GitHub action must be attributed to a human, the design either records the initiating principal in
the immutable FS.GG receipt or uses an explicitly accepted user-to-server flow. A client-supplied GitHub
token is never proxied through an agent tool request.

### 9.6 Akka transport security

Akka.Remote is disabled in the initial deployment and never serves public clients. If clustering is later
enabled, nodes communicate only on a private network with mutual TLS, explicit schema-bound serializers,
and registered message types. Polymorphic deserialization and fallback serialization are disabled. The
SignalR gateway remains the only client ingress.

## 10. Persistence and effect protocol

### 10.1 Event sourcing

Each durable entity has a stable persistence ID derived from a canonical subject, not from an actor's
physical location. Events are append-only and versioned. Snapshots accelerate recovery but are disposable
projections of the event stream.

Persistence rules:

- persist facts and decisions, not secrets or actor references;
- use explicit schema versions and upcasters;
- make event application pure and total;
- reject an unknown event version rather than partially recover;
- snapshot only after a confirmed journal position;
- test recovery from every supported historical schema; and
- keep provider evidence by immutable reference and digest when storing full bytes is inappropriate.

### 10.2 Durable inbox and outbox

Transport messages and provider effects use durable inbox/outbox semantics:

```text
receive command
→ validate and deduplicate
→ persist domain event + effect intent
→ acknowledge durable command position
→ dispatch outbox item
→ re-read external authority
→ perform fenced effect
→ verify post-state
→ persist receipt
→ mark outbox item settled
```

If the process dies after GitHub accepts an effect but before the receipt is persisted, recovery sees an
unsettled intent, re-observes GitHub, and records `AlreadyApplied` or a conflict. It does not blindly repeat
the mutation.

The arrows above require one precise write-side transaction boundary. `domain event + effect intent` is
one committed aggregate event or one atomic journal transaction; an inbox position is not acknowledged
before that commit. Likewise, the verified receipt and settlement of its outbox identity are recorded by
one committed aggregate transition. A projection table, background queue, or separately committed SQL row
cannot be the only copy of an intent.

If the selected persistence plugin cannot atomically commit arbitrary outbox rows with actor events, the
event stream is authoritative and the dispatcher derives or rebuilds its pending outbox from those events.
The implementation must not approximate atomicity with write ordering. H0 must publish the exact plugin and
transaction semantics, and qualification must kill the process at each boundary to prove that no accepted
command is lost and no unpersisted intent causes an effect.

### 10.3 Delivery guarantees

Ordinary actor messages are treated as at-most-once. Akka reliable delivery may be used for internal
cross-node paths later, but consumers still deduplicate because at-least-once delivery can redeliver after
recovery. Public WebSocket messages use the harness sequence/ack protocol, not assumptions about Akka
mailboxes.

### 10.4 Storage

PostgreSQL is the first production journal, snapshot, inbox/outbox, and projection store. It provides a
well-understood backup and transactional boundary without making its rows the GitHub coordination
authority. SQLite is acceptable for developer and single-process prototypes only after crash and locking
behavior is qualified.

Backups require encrypted storage, retention policy, point-in-time recovery where available, and scheduled
restore rehearsals. A backup that has never been restored is not availability evidence.

### 10.5 Data classes and deletion

The store distinguishes protocol metadata, security audit records, prompts and responses, source artifacts,
build logs, human identity data, and secrets. Each class has a named owner, purpose, retention period,
encryption requirement, export rule, and deletion mechanism. Content needed for a durable FS.GG receipt is
kept by digest or moved to the accepted evidence store; that requirement does not justify retaining every
conversation or workspace indefinitely.

Deleting a session, workspace, or personal-data projection must not corrupt an append-only protocol chain.
Where erasure and audit requirements conflict, the design stores a pseudonymous stable reference in the
chain and keeps the separately controlled identity mapping only for its accepted lifetime. Backups and
telemetry follow the same classification rather than silently extending retention.

## 11. GitHub gateway and mutation safety

### 11.1 Observation path

The gateway centralizes:

- GitHub API version and media types;
- REST and GraphQL pagination;
- completeness, permission, unsupported, and indeterminate outcomes;
- conditional caching of immutable observations;
- primary and secondary rate budgets;
- request correlation and sanitized telemetry;
- bounded retries that respect `Retry-After` and reset metadata; and
- normalization into typed facts consumed by the existing engine.

Mutable facts are re-read at the decision/effect boundary. A webhook or cached actor event is a wakeup, not
permission.

### 11.2 Mutation lanes

Mutations are serialized by installation and conflict aggregate. There is not one global GitHub actor.
Reads may run concurrently under a bounded pool; mutations use conservative concurrency and provider
backpressure.

Each mutation receives a sealed plan containing:

- stable operation and idempotency identity;
- initiating principal and causation chain;
- exact subject and intended change;
- expected workflow revision;
- expected fleet epoch and provider generation;
- policy and compiled-contract versions;
- precondition observation fingerprint;
- compensation or roll-forward procedure; and
- required postcondition.

The adapter asks the typed coordination engine whether the transition remains legal immediately before
the effect. A red, incomplete, unauthorized, unsupported, indeterminate, or stale verdict settles as a
refusal or retryable observation state, never as success.

### 11.3 Existing GitHub/Git fencing

Actor ownership and a database lease reduce duplicate execution but do not fence a paused or partitioned
writer. External mutation authority remains bound to the protected FS.GG epoch and operation/claim
generation. Every effect validates those values at the provider boundary. Comments and Project fields are
human projections; protected Git history supplies strong ordering where selected by GitHub Substrate v2.

### 11.4 Emergency and multi-host operation

The existing CLI remains available when the orchestrator is down. Emergency actions:

- use the same typed engine and provider primitives;
- acquire or present the same external generation;
- record an emergency principal and reason;
- produce ordinary receipts; and
- are discovered by the next orchestrator reconciliation.

The orchestrator never assumes that all valid changes originated from its journal. GitHub reconciliation
is therefore a permanent correctness mechanism, not migration scaffolding.

## 12. Supervision, liveness, and availability

### 12.1 Failure classification

| Failure | Local response | Durable response |
|---|---|---|
| Malformed or unauthorized client message | Reject and rate-limit connection | Audit refusal; preserve session |
| Agent tool/process failure | Restart within attempt budget or replace | Persist attempt outcome and new generation |
| Poison workflow command | Stop/quarantine entity | Preserve event position and diagnostic envelope |
| GitHub transient/rate limit | Circuit-break and schedule wakeup | Persist wait reason and next eligible time |
| GitHub incomplete read | Refuse inference | Persist incomplete observation and bounded retry |
| Journal write failure | Stop affected persistent actor; fail readiness | No external effect from unpersisted intent |
| Snapshot failure | Recover from journal if safe | Alert and repair snapshot path |
| Actor system termination | Terminate host | systemd restarts and journal replays |
| Host or machine loss | Clients reconnect later | Journal recovery plus complete reconciliation |
| Duplicate orchestrator | External generation rejects stale effect | Audit conflict and stop losing writer |

Supervision strategy is explicit per child class. Restart budgets prevent infinite crash loops. A repeated
programming defect moves one work item to quarantine rather than repeatedly consuming fleet capacity.

### 12.2 Liveness hierarchy

| Signal | What it proves | What it does not prove |
|---|---|---|
| WebSocket connected | A network path exists | Agent is progressing or authorized now |
| Ping/pong | Endpoints recently processed transport traffic | Workflow code is healthy |
| Agent heartbeat | Agent runtime can send a protocol event | Work is correct or useful |
| Progress event | Agent reports a new revision/artifact | Artifact satisfies its contract |
| Process watch / DeathWatch | Local process or actor terminated | Remote durable work is absent |
| Journal health | Persistence endpoint responds | Every actor recovered correctly |
| Workflow deadline | Inspection is due | Abandonment is proven |
| Fresh provider inspection | Current external facts are known completely | A later effect remains legal indefinitely |
| Fencing generation | Holder is current at validation | Effect succeeded |
| Post-state receipt | Required external result was observed | Unrelated workflow invariants hold |

The policy treats expiry as evidence prompting inspection, not proof authorizing cleanup. An open pull
request, branch, worktree receipt, or unknown provider observation changes the recovery action.

### 12.3 Health endpoints

- `/health/live`: process event loop, actor system, and watchdog are responsive;
- `/health/ready`: startup replay completed, required actors registered, journal writable, schemas current,
  credential material loadable, and this node may accept its configured traffic class;
- `/health/dependencies`: GitHub, database, disk, runner, authority ref, and model/toolchain status with
  typed `Healthy`, `Degraded`, or `Unhealthy` reasons;
- `/health/workflows`: counts and oldest age for waiting, retrying, quarantined, and uncertain-effect states.

A GitHub outage degrades provider readiness but must not cause a process restart loop. Liveness measures the
process; readiness controls traffic; dependency health guides operations.

### 12.4 systemd

The initial service uses:

- `Type=notify` and startup readiness only after recovery;
- `Restart=on-failure` with bounded restart delay and rate limit;
- `WatchdogSec` fed only by a healthy top-level runtime loop;
- an unprivileged service account;
- `LoadCredential` or an equivalent secret mechanism;
- filesystem, device, syscall, privilege, and network hardening compatible with the selected sandbox;
- explicit graceful-stop timeout; and
- logs and metrics shipped outside the unit's ephemeral lifetime.

systemd restores the process. It does not persist mailboxes, prove effect outcomes, or prevent split brain.

### 12.5 Single-node availability

Release one runs one active orchestrator. Availability comes from:

- durable PostgreSQL events;
- systemd restart;
- reconnecting clients with resume cursors;
- replayable work actors;
- persistent effect intents;
- post-restart GitHub reconciliation; and
- an emergency CLI over the external authority.

The target recovery point for accepted commands is zero after their durable acknowledgment. The recovery
time target is measured from process loss to replay completion, readiness, and settlement of uncertain
effects. These targets must be set and tested before the service becomes required.

### 12.6 Multi-node availability

Akka.Cluster, sharding, singleton, discovery, and distributed data are deferred. If machine-level failover
is later required, the design must specify:

- failure detector and split-brain resolver;
- minimum node count and failure domains;
- journal availability and consistency;
- singleton or sharding handoff behavior;
- serialization compatibility during rolling upgrades;
- client ingress routing and session reconnection;
- maximum unavailable interval;
- external lease if used; and
- mandatory GitHub/Git fencing against overlapping writers.

Cluster Singleton improves placement and eventual availability; it does not prove an eternal single writer
during every partition. Actor delivery can lose or duplicate messages depending on the selected mechanism.
The external generation remains decisive.

## 13. Backpressure and resource governance

Every ingress and work queue is bounded. When capacity is exhausted the system returns an explicit deferred
or overloaded response; it does not accept unbounded memory growth.

Resource governors include:

- maximum active agents globally, per repository, and per principal;
- maximum pending commands per connection and session;
- artifact and message byte limits;
- per-tool time, CPU, memory, disk, process, and network limits;
- REST and GraphQL request/cost buckets;
- concurrent GitHub mutation limit;
- build-runner and model-provider quotas;
- monetary and token budgets;
- retry and replacement budgets; and
- quarantine thresholds.

Akka Streams may be introduced for high-volume event, log, or artifact pipelines where its bounded
non-blocking backpressure is useful. Ordinary domain workflows remain actors with small typed messages;
large artifacts travel through content-addressed storage, not mailboxes.

## 14. Observability and audit

Every command, decision, agent attempt, effect, and receipt shares stable correlation fields:

```text
traceId
causationId
correlationId
commandId
principalId
sessionId + generation
workItem
agentAttempt
operationId
workflowRevision
policyVersion
providerGeneration
```

Metrics include:

- authenticated connections, reconnects, expiry, and authorization refusals;
- command ingress rate, durable acknowledgment latency, duplicates, and replay depth;
- actor restarts, quarantines, recovery duration, and mailbox pressure;
- active agents, heartbeat age, progress age, replacements, and completion dispositions;
- arrivals, departures, WIP, queue/flow/tail time, service-time variation, and capacity headroom by bottleneck;
- solver status, feasible gap/bound, runtime, objective vector, sensitivity, degraded-baseline use, and
  independent-verifier outcome;
- scheduling feasibility reasons, critical-path delay, replan churn, commitment changes, fairness debt, and
  capacity utilization;
- work-shape setup/cut/rework estimates versus outcomes and split/merge dispositions;
- SDD information actions, stopping decisions, downstream assumption invalidation, and rework avoided/created;
- CI obligation count, job/setup duplication, time-to-red, makespan, selection rate, sentinel misses, and retries;
- review queue age, assignment latency, role coverage, review yield, workload concentration, and knowledge risk;
- GitHub rate budget, incomplete observations, circuit state, mutation latency, and conflicts;
- unsettled effect age and reconciliation outcomes;
- journal write/replay/snapshot latency and storage growth; and
- predicted versus observed duration, cost, failure, and utility.

Logs must exclude bearer tokens, GitHub tokens, App private keys, cookies, raw secrets, and unbounded agent
content. Security-relevant records are immutable or exported to a protected sink. Operator dashboards link
runtime state to the GitHub-visible subject and receipt without making the dashboard authoritative.

## 15. Testing and formal assurance

### 15.1 Pure model tests

- property-test `evolve` determinism and invariant preservation;
- canonicalize logically equivalent planning snapshots to identical bytes;
- compare planner feasibility with brute-force enumeration on bounded generated instances;
- run every solver result and every human-selected alternative through the independent checker;
- mutate dependency, capacity, touch, evidence, independence, budget, and authority constraints and require red;
- prove CI obligation closure and work-slice evidence closure on generated graphs;
- reproduce pinned solver and baseline results from exact envelopes or disclose an unsupported solver identity;
- replay every accepted historical event schema;
- generate workflow action sequences and compare with the Quint model where correspondence exists;
- retain historical coordination defects as regression cases;
- mutation-test high-risk predicates with named non-vacuity controls; and
- compare policy explanations and chosen actions against golden decision fixtures.

### 15.2 OR policy and simulation qualification

- backtest duration, cost, failure, rework, CI-yield, and review-yield distributions with calibration plots
  and proper handling of censored outcomes;
- verify Little's-law aggregates over stable windows and explain material conservation mismatches;
- compare incumbent, candidate, and deliberately bad policies over identical scenario seeds;
- vary arrival, service, outage, reviewer, runner, agent, rework, and estimate-error distributions;
- test robust schedules against nominal, tail, correlated-delay, and capacity-loss scenarios;
- measure policy sensitivity and reject cliff-edge parameter regions without an operational disposition;
- demonstrate that additional agents/runners, smaller work, or more review are not assumed monotonically better;
- shadow live decisions and adjudicate all constraint and material objective disagreements; and
- prohibit causal or counterfactual claims from historical replay alone.

### 15.3 Runtime tests

- kill an agent before and after every protocol message;
- kill the orchestrator before and after event persistence, effect dispatch, provider acceptance, and receipt;
- drop, duplicate, reorder, delay, and reconnect WebSocket messages;
- expire and revoke session generations during active connections;
- inject poison messages and persistence failures;
- exhaust each resource and GitHub rate budget;
- rotate App and data-protection keys;
- restore from snapshots plus subsequent events and from full replay;
- perform database backup restoration in an isolated environment; and
- verify that one quarantined actor does not stop unrelated work.

### 15.4 Security tests

- attempt cross-issue and cross-repository capability use;
- replay bootstrap and session tokens;
- forge session generation, sequence, subject, and expected revision;
- submit oversized, malformed, unknown-version, and polymorphic payloads;
- confirm token redaction across every log and trace path;
- verify agents cannot read host credentials or contact denied networks;
- attempt arbitrary actor-path addressing and Akka.Remote access;
- test authorization revocation on an established socket; and
- verify provider tokens are narrowed and absent from persistence.

### 15.5 Availability tests

Single-node qualification must demonstrate:

1. accepted commands survive `SIGKILL` and reboot;
2. clients reconnect and resume without duplicating logical commands;
3. every uncertain GitHub effect converges to applied, refused, or escalated;
4. no actor begins external effects before recovery readiness;
5. watchdog termination recovers a deliberately wedged runtime;
6. a GitHub outage degrades without a restart storm; and
7. emergency CLI mutations are discovered and reconciled.

Multi-node qualification, if ever selected, adds partition, asymmetric reachability, clock skew, rolling
upgrade, stale singleton, sharding relocation, journal failover, and external-fence tests.

## 16. Deployment and operational model

Production operation requires named ownership for:

- service availability and incident response;
- GitHub App registration, permissions, key rotation, and installation scope;
- database upgrades, backup, restore, retention, and capacity;
- TLS, identity provider, session signing, and revocation;
- agent runner images, sandbox controls, and toolchain patching;
- Akka.NET, persistence plugin, SignalR, and .NET upgrades;
- policy versions, optimization parameters, and decision regressions;
- observability retention and privacy; and
- emergency disable, degraded mode, and disaster recovery.

Operational modes are explicit:

| Mode | Reads | Agent creation | GitHub mutations | Use |
|---|---:|---:|---:|---|
| Observe | Yes | No | No | Initial qualification and audit |
| Assist | Yes | Yes | Agent/manual path only | Prove harness lifecycle |
| Plan | Yes | Yes | Human invokes sealed plans | Prove deterministic decisions |
| Normal writer | Yes | Yes | Orchestrator through typed adapter | Target steady state |
| Read-only degraded | Cached/fresh where possible | Optional pause | No | Credential/provider/authority incident |
| Emergency CLI | Direct typed reads | No | Explicit operator path | Orchestrator outage or repair |
| Stop-the-world | Audit only | No | No | Epoch, security, or systemic invariant breach |

The kill switch reduces authority without deleting state. Revoking the GitHub App installation or moving
the fleet epoch must stop mutations even if the process remains alive.

## 17. Implementation roadmap

### H0 — OR domain, measurement, and authority specification

- Accept or amend the OR-first architecture and name the policy, data, security, and operational owners.
- Specify one end-to-end issue lifecycle in Quint and the compiled FS.GG contract.
- Define the six graphs, canonical `PlanningSnapshot`, execution modes, stable IDs, distributions, censored
  outcomes, decision receipts, and authority boundaries.
- Freeze hard constraints, lexicographic objectives, WIP/recovery-capacity policy, and replan triggers.
- Define planner/verifier contracts for work shape, SDD, portfolio, CI, review, agent allocation, and recovery.
- Build the historical corpus inventory and document missing, selected, censored, and biased observations.
- Define the incumbent heuristic and the replay, simulation, mutation, shadow, and canary comparison protocol.

**Exit:** an independently reviewable mathematical/domain specification and measurement contract exist;
unknown data is explicit; no hosted runtime or provider mutation is required.

### H1 — offline decision laboratory

- Implement the pure feasibility checker and incumbent baseline first.
- Implement `WorkSizer`, `SddPlanner`, `CiPlanner`, `ReviewPlanner`, `AgentAllocator`, and the robust rolling
  `PortfolioScheduler` behind stable interfaces.
- Build the discrete-event simulator and scenario corpus for arrivals, durations, rework, outages, CI misses,
  reviewer scarcity, agent failures, and estimate error.
- Pin and qualify the candidate CP-SAT solver; add independent plan verification and degraded heuristics.
- Replay historical board and incident snapshots and publish objective, sensitivity, calibration, and
  constraint-mutation evidence.

**Exit:** every planner is deterministic at its contract boundary, independently checked, explainable, and
no worse than the incumbent on declared safety/fairness measures; it still cannot dispatch work.

### H2 — authenticated read-only host

- Build ASP.NET Core + SignalR + Akka.Hosting.
- Implement human and machine authentication, per-message authorization, session generations, and replay.
- Add actor-system, journal, dependency, OR-decision, and workflow health endpoints.
- Observe GitHub through the typed engine without mutation and materialize canonical planning snapshots.
- Persist shadow decisions and compare them with actual operator/CLI choices and outcomes.

**Exit:** restart, reconnect, revocation, malformed-message, read-completeness, snapshot, and shadow-decision
tests pass; the service cannot mutate GitHub or create a write-capable agent.

### H3 — durable agent execution

- Add workspace provisioning and the credentialless sandboxed runner.
- Compile content-addressed `AgentSpecification` packages from accepted OR decisions.
- Create, supervise, heartbeat, cancel, replace, and dispose agents.
- Collect typed artifacts/findings and measure duration, cost, failure, rework, and context use.
- Correlate agents with claims, branches, worktrees, and PRs read-only.

**Exit:** agent and host kill tests recover without losing accepted commands or falsely abandoning work;
agent observations feed estimates but do not self-certify success.

### H4 — closed-loop OR shadow operation

- Run work-shape, SDD, scheduling, CI, review, and agent-allocation decisions on complete live snapshots.
- Replan on real events without dispatching provider effects.
- Adjudicate every divergence from actual operator choices and every infeasible or unstable plan.
- Measure queueing, WIP, throughput, cycle/tail time, CI feedback, review delay, rework, cost, fairness,
  knowledge concentration, and forecast calibration.
- Promote no learned estimate or parameter outside the accepted policy-update workflow.

**Exit:** the full loop is stable and explainable over the declared observation window, hard-constraint
mutations are caught, and material shadow divergences have explicit dispositions.

### H5 — sealed plans and shadow effects

- Compile committed OR actions into durable effect intents.
- Demonstrate the event/inbox/outbox crash boundary with the candidate PostgreSQL persistence plugin and
  document its exact transaction and schema-evolution semantics.
- Simulate provider acceptance, loss, duplication, stale generations, compensation, and roll-forward.
- Run shadow comparison against real typed-engine verdicts.
- Qualify token minting, rate limits, circuit breakers, postcondition reads, principal separation, and kill switch.
- Complete the runner/WebSocket/database/App/Git threat model, data classification, and disaster-recovery proof.

**Exit:** every injected interruption converges without an unfenced effect; shadow divergence is adjudicated;
no production mutation permission exists.

### H6 — bounded mutation canary

- Require `OperatingV2` unless a separate accepted sequencing decision explicitly authorizes an earlier epoch.
- Enable one low-risk repository, execution mode, and operation class.
- Require explicit operator selection among verified alternatives and approval of the sealed plan.
- Retain CLI parity, complete reconciliation, immediate kill switch, and incumbent-policy fallback.
- Compare predicted and observed outcomes and stop on unexplained effects, constraint misses, or drift.

**Exit:** canary duration and volume targets pass with zero unexplained effects, stale writes, lost commands,
or hard-constraint violations and with accepted objective/calibration bounds.

### H7 — normal single-node writer

- Route separately approved operation classes through the OR controller and orchestrator by default.
- Retain external GitHub/Git fencing, independent plan verification, and emergency CLI.
- Establish SLOs, on-call/incident ownership, backup restoration, key rotation, policy rollback, and upgrades.
- Remove only prompt choreography proven redundant; keep typed provider and evidence semantics.

**Exit:** the accepted operational boundary permits the service and exact policy set to become the normal writer.

### H8 — optional availability expansion

- Measure whether host recovery fails the availability target.
- Compare passive standby, service-manager failover, and Akka.Cluster as execution alternatives.
- If clustering wins, qualify split-brain resolution, sharding/singleton behavior, rolling compatibility,
  reliable delivery, solver-version placement, and external fencing.

**Exit:** a separate accepted decision authorizes multi-node deployment. Otherwise remain single-node.

## 18. Acceptance criteria

The architecture is ready for normal-writer consideration only when:

- every decision binds one complete canonical planning snapshot, accepted policy/estimate set, and exact
  solver or baseline identity;
- work shape, SDD depth, portfolio/WIP, CI topology, review assignment, agent allocation, and recovery each
  have a bounded model, incumbent baseline, independent checker, and stable explanation;
- hard constraints remain separate from objectives and named constraint-removal mutations fail;
- rolling plans commit only a bounded horizon, reserve recovery capacity, and avoid replanning churn without
  hiding material state changes;
- historical replay, discrete-event simulation, live shadowing, and a canary establish declared safety,
  calibration, flow, fairness, and cost bounds without claiming unsupported counterfactual certainty;
- no public endpoint exposes Akka.Remote or arbitrary actor messages;
- every WebSocket command is authenticated, authorized, bounded, versioned, deduplicated, and durably acknowledged;
- reconnecting clients resume from a durable cursor;
- session revocation prevents all older generations from issuing commands;
- agents receive no GitHub, durable model-provider, or shared service credential and cannot escape their
  sandbox contract;
- every agent is created from a content-addressed OR-selected specification with measured execution-mode,
  context, capability, budget, output, and join contracts;
- write-capable agents start only after a verified external claim and stop provider capabilities when its
  generation is lost;
- deterministic policy explains feasible alternatives, rejected constraints, objective vector, sensitivity,
  commitment horizon, and next replan triggers;
- the same complete determinism envelope reproduces byte-identical decision and explanation records;
- persistent actors recover across supported event versions and snapshots;
- no command is acknowledged before its event and effect intent are durable, and no external effect begins
  from a projection-only or separately committed intent;
- uncertain effects reconcile rather than blindly retry;
- the typed coordination engine authorizes every provider mutation;
- the current GitHub/Git generation fences every concurrency-sensitive effect;
- GitHub outage, database degradation, and one poisoned workflow fail in their intended isolation domains;
- systemd restart and full machine reboot meet measured recovery targets;
- database restore and key rotation are rehearsed;
- stored prompts, artifacts, identities, audit records, telemetry, and backups have accepted classification,
  retention, export, and deletion rules;
- emergency CLI use remains possible and is reconciled; and
- an accepted owner, SLO, incident process, retention policy, cost envelope, and disaster-recovery proof exist.

## 19. Consequences and trade-offs

### Benefits

- Work size, WIP, SDD depth, CI shape, reviewer assignment, and agent parallelism become explicit,
  evidence-backed decisions rather than accumulated conventions.
- Rolling robust schedules expose bottlenecks, uncertainty, and recovery headroom instead of optimizing
  nominal utilization.
- Independent plan verification keeps solver sophistication outside the authority boundary.
- Agents become disposable compute rather than fragile workflow owners.
- GitHub credentials and API behavior are centralized and narrowed.
- Supervision and persistence become standard runtime capabilities instead of custom prompt procedures.
- Deterministic policy makes scheduling, retry, and escalation inspectable and replayable.
- WebSocket clients can disconnect, reconnect, and change machines without losing workflow identity.
- Failures are isolated per actor/work item while durable state survives process loss.
- Existing FS.GG coordination work remains useful as the externally verifiable control plane.
- Akka.NET provides credible expansion paths for streams, reliable delivery, sharding, and multi-node
  availability if later measurements justify them.

### Costs

- The organization acquires a real hosted-service security and operational boundary.
- Akka.NET and persistence add concepts, configuration, serialization, schema evolution, and upgrade work.
- PostgreSQL, TLS, identity, GitHub App, sandbox, backup, and monitoring need named ownership.
- There are intentionally two durable views: internal execution history and external GitHub coordination
  history. Reconciliation is permanent.
- At-least-once recovery requires idempotency and effect verification throughout.
- The organization must operate an OR product: graph/schema governance, estimators, censored data, solvers,
  simulation, calibration, policy promotion, and independent verification.
- Optimization models can become opaque, brittle, or misaligned unless kept versioned, constrained, measured,
  sensitivity-tested, and explainable.
- A future cluster would add partition and rolling-compatibility risks and therefore remains deferred.

## 20. Alternatives considered

### Start with the actor runtime and add optimization later

Would deliver process supervision quickly, but actor handlers and queues would silently encode decomposition,
admission, WIP, retry, CI, and review policies before their objectives and constraints are understood.
Rejected because the execution mechanism would become the de facto planning architecture. The offline OR
domain and decision laboratory therefore precede the hosted runtime.

### Fix one standard workflow for every work item

Would simplify observability and qualification. Rejected because uncertainty, contract surface, review need,
CI cost, recovery risk, and information value differ materially across work. Mandatory authority and safety
gates stay standard; optional depth and execution mode are selected from a bounded, versioned action set.

### Continue with agent-driven skills only

Retains no hosted service and uses the existing substrate directly. Rejected as the target because agents
continue carrying runtime supervision, polling, retry, and recovery obligations in prompts and ephemeral
contexts. Retained as the emergency and compatibility path.

### Replace the GitHub substrate with the orchestrator database

Creates a simpler centralized implementation. Rejected because it discards completed protocol work,
removes external inspectability and multi-machine operation, and makes one service necessary for correctness.

### F# `MailboxProcessor` or `System.Threading.Channels` only

Provides a small dependency surface and sufficient local message passing. A reasonable implementation for
a small daemon, but the harness already requires durable entities, restart semantics, supervision,
backoff, health integration, and likely future availability features. Recreating those selectively would
grow a local actor runtime. Retained for narrow queues outside domain entities.

### Hopac

Provides elegant Concurrent-ML alternatives and efficient F# jobs. It is attractive for selecting among
timeouts, cancellation, agent messages, and provider events, but it does not supply durable actors,
supervision, hosted-service integration, or cluster lifecycle. Its public package has a materially older
release cadence than the candidate Akka.Hosting stack. Rejected as the primary runtime; admissible behind a
narrow internal interface if a measured subsystem benefits.

### Akka.NET from the public edge inward

Exposing Akka.Remote to clients would unify transport and actors. Rejected because actor remoting is not an
application authentication/authorization protocol, arbitrary deserialization is a security boundary, and
clients must not address internal actor topology. SignalR remains the edge.

### Akka.Cluster from release one

Provides machine-level failover and entity distribution. Rejected initially because systemd plus durable
recovery likely satisfies the first availability target, while clustering introduces split-brain,
discovery, journal, serializer, deployment, and rolling-upgrade concerns before they are measured needs.

### Put policy directly in LLM prompts

Maximizes adaptability and minimizes deterministic implementation. Rejected because prompts cannot supply
replayable legality, stable optimization, complete observation semantics, or reliable external mutation
authorization. Agents remain proposal and implementation engines inside deterministic rails.

### One scalar utility optimizer

Offers simple global ranking. Rejected because safety, authority, and completeness are constraints rather
than prices, and one exposed proxy invites Goodhart behavior. Use lexicographic constraints and factored
objectives with a stable explanation.

## 21. External references

### OR and decision foundations

- [Little, “A Proof for the Queuing Formula L = λW”](https://doi.org/10.1287/opre.9.3.383) — accountable
  relationship among average WIP, throughput, and cycle time.
- [Kingman, “The single server queue in heavy traffic”](https://doi.org/10.1017/S0305004100036094) —
  theoretical grounding for utilization/variability-driven queue delay.
- [Bertsimas and Sim, “The Price of Robustness”](https://doi.org/10.1287/opre.1030.0065) — adjustable
  protection against uncertain coefficients without treating every risk as worst case.
- [Bold and Goerigk, robust resource-constrained project scheduling](https://arxiv.org/abs/2004.06547) —
  compact two-stage formulation for uncertain activity durations.
- [Lindley, “On a Measure of the Information Provided by an Experiment”](https://doi.org/10.1214/aoms/1177728069)
  — Bayesian information value for choosing experiments.
- [Browning, design structure matrices for decomposition and integration](https://doi.org/10.1109/17.946528)
  — product, organization, activity, and parameter dependency models.
- [Smith, the Contract Net Protocol](https://doi.org/10.1109/TC.1980.1675516) — task announcement,
  capability matching, and bounded award in distributed problem solving.
- [Google OR-Tools CP-SAT](https://developers.google.com/optimization/cp/cp_solver),
  [job-shop scheduling](https://developers.google.com/optimization/scheduling/job_shop), and
  [assignment](https://developers.google.com/optimization/assignment/assignment_example) — candidate .NET
  solver and reference formulations.

### Software-delivery and process evidence

- [Boehm, spiral software-development model](https://doi.org/10.1109/2.59) — risk-driven iteration rather
  than a fixed linear process.
- [Karlsson and Ryan, cost-value requirements prioritization](https://doi.org/10.1109/52.605933) and
  [Xuan et al., robust next-release planning](https://doi.org/10.1145/2576768.2598334) — requirements and
  release selection under value, cost, and uncertainty.
- [Sullivan et al., modularity, design structure matrices, and real options](https://doi.org/10.1145/503209.503224)
  — valuing decompositions by the options they preserve.
- [Cataldo et al., identification of coordination requirements](https://doi.org/10.1145/1180875.1180929) —
  technical dependencies imply volatile cross-team communication needs.
- [Do small code changes merge faster?](https://arxiv.org/abs/2203.05045) — large multi-language and
  multi-platform evidence against a universal PR-size/merge-time rule.
- [Elbaum et al., CI regression selection and prioritization](https://doi.org/10.1145/2635868.2635910),
  [Ekstazi dynamic dependency selection](https://doi.org/10.1145/2771783.2771784), and
  [Rothermel et al., fault-detection prioritization](https://doi.org/10.1145/347324.348910) — sound
  selection and faster failure feedback.
- [Predictive Test Selection at Facebook](https://arxiv.org/abs/1810.05286) — production cost/failure-detection
  trade-offs including flaky outcomes.
- [Parallel Batch Testing](https://arxiv.org/abs/2308.13129) — non-linear feedback and machine-use effects
  of CI batch and runner count.
- [Build Systems à la Carte](https://doi.org/10.1145/3236774) — executable framework for separating build
  dependency, scheduling, rebuilding, and caching choices.
- [Fagan, design and code inspections](https://doi.org/10.1147/sj.153.0182) — staged preparation,
  inspection, rework, follow-up, and process control.
- [Modern code review at Microsoft](https://doi.org/10.1109/ICSE.2013.6606617) and
  [Google](https://doi.org/10.1145/3183519.3183525) — empirical review purposes, understanding needs, and
  industrial practice.
- [Large-scale reviewer recommendation](https://arxiv.org/abs/1806.07619) and
  [expertise/workload/turnover-aware recommendation](https://arxiv.org/abs/2312.17236) — project-specific
  reviewer models and multi-objective knowledge/workload effects.
- [Software-process discrete-event simulation methodology](https://arxiv.org/abs/1403.3559) — systematic
  construction of simulation-based decision models.

### Agent and runtime substrate

- [Codex subagents and custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents) —
  specialized instructions plus per-agent model, reasoning, sandbox, MCP, and skill configuration.
- [Akka.NET persistence architecture](https://getakka.net/articles/persistence/architecture.html) — event
  replay, snapshots, and at-least-once delivery semantics.
- [Akka.NET persistence failure behavior](https://getakka.net/articles/persistence/event-sourcing.html) —
  stop-on-persist/recovery-failure and backoff supervision.
- [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting) — .NET hosting, DI, logging, OpenTelemetry, and
  health-check integration.
- [Akka.NET reliable delivery](https://getakka.net/articles/actors/reliable-delivery.html) — ordering,
  durable queues, confirmation, and possible duplicate delivery after recovery.
- [Akka.NET Cluster Singleton](https://getakka.net/articles/clustering/cluster-singleton.html) and
  [Split Brain Resolver](https://getakka.net/articles/clustering/split-brain-resolver.html) — availability
  capabilities and partition limitations.
- [Akka.NET serialization security](https://getakka.net/articles/serialization/serialization.html) —
  schema-bound serialization and disabling unregistered-type fallback.
- [Akka Streams backpressure](https://getakka.net/articles/streams/basics.html) — bounded asynchronous flow.
- [SignalR authentication and authorization](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz)
  and [configuration](https://learn.microsoft.com/aspnet/core/signalr/configuration) — bearer transport,
  principal lifetime, keepalive, and timeout behavior.
- [GitHub App permissions](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/choosing-permissions-for-a-github-app)
  and [installation access tokens](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-an-installation-access-token-for-a-github-app) — least privilege and short-lived provider identity.

## 22. Further reading

- [Native collaboration-runtime supervision design history](../reports/2026-07-30-150617-native-collaboration-runtime-supervision-design-and-roadmap.md)
- [GitHub Substrate v2 fleet-cutover design](2026-08-25-github-substrate-v2-fleet-cutover-design.md)
- [GitHub Substrate v2 remaining-migration architecture review](2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md)
- [Coordination engine design](../design/coordination-engine.md)
- [Parallel-work protocol](parallel-work.md)
- [Untrusted-content boundary](untrusted-content-boundary.md)
- [ADR-0053: disposable milestone agents](../adr/0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md)
- [ADR-0077: Quint-first typed authority](../adr/0077-quint-first-typed-specification-authority.md)
- [ADR-0078: GitHub Substrate v2 authority](../adr/0078-github-substrate-v2-new-only-coordination-authority.md)
- [ADR-0079: one accountable delivery owner](../adr/0079-single-accountable-delivery-authority.md)
