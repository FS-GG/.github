---
title: "Design: durable actor orchestrator and deterministic agent harness"
category: Design
categoryindex: 4
index: 28
description: "A proposed Akka.NET execution plane for authenticated agent supervision, deterministic scheduling, durable recovery, and guarded reuse of the FS.GG GitHub coordination substrate."
---

# Design: durable actor orchestrator and deterministic agent harness

This design adds a continuously running, authenticated agent harness above the existing FS.GG GitHub
coordination substrate. ASP.NET Core and SignalR form the public WebSocket boundary; Akka.NET supplies
durable workflow actors, supervision, isolation, timers, and an optional path to multi-node availability;
a pure deterministic policy kernel applies classical AI, operations-research, and decision-theoretic
methods to choose admissible work; disposable coding agents perform bounded creative tasks; and the
existing typed coordination engine plus GitHub/Git remain the authority for claims, fencing, mutation
legality, durable evidence, and recovery visible outside the orchestrator. The orchestrator improves
execution without discarding the already-built distributed control plane or allowing an LLM, socket,
process, actor, timer, or database lease to authorize an external mutation by itself.

| Field | Value |
|---|---|
| Status | Proposed architecture; records direction and implementation preparation, not production authorization |
| Authored | 2026-08-31 11:48 CEST (09:48 UTC) |
| Scope | Agent creation and communication, deterministic planning, GitHub mediation, persistence, authentication, supervision, liveness, availability, observability, and staged adoption |
| Preserves | GitHub-native multi-host coordination, typed transition checks, Git-ref fencing, exact-head evidence, durable receipts, and scheduled reconciliation |
| Builds on | [ADR-0034](../adr/0034-typed-coordination-engine.md), [ADR-0053](../adr/0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md), [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md), [ADR-0078](../adr/0078-github-substrate-v2-new-only-coordination-authority.md), [ADR-0079](../adr/0079-single-accountable-delivery-authority.md), and the [remaining-v2 architecture review](2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md) |
| Candidate runtime | ASP.NET Core + SignalR + Akka.Hosting + Akka.Persistence; PostgreSQL first, Akka.Cluster deferred |
| Primary decision | Treat Akka.NET as the execution plane and FS.GG/GitHub as the durable coordination control plane |

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
2. **Add one normal execution authority.** A hosted orchestrator becomes the preferred path for agent
   creation, scheduling, communication, GitHub access, retry, and recovery. It is not the sole store of
   coordination truth.
3. **Use actors for lifecycle, not policy authorship.** Akka.NET actors serialize work, persist events,
   supervise children, and schedule wakeups. Pure reducers and constraint solvers decide; actors do not
   hide mutable policy inside callbacks.
4. **Use deterministic admissibility around probabilistic agents.** LLMs may propose, implement, explain,
   and critique. Deterministic code decides whether a proposal is complete, safe, current, affordable,
   and legal to execute.
5. **Expose a narrow authenticated WebSocket protocol.** Clients never address arbitrary actor paths,
   submit arbitrary runtime objects, or receive GitHub credentials.
6. **Persist intent before effects and verify after effects.** Recovery is at-least-once. Idempotency,
   generation fencing, and post-state observation make duplicate execution safe.
7. **Begin as one durable node.** systemd restarts it; clients reconnect and replay. Akka.Cluster is added
   only after a measured availability requirement and an accepted split-brain/fencing design.

This is an additive architecture proposal. It does not amend the GitHub Substrate v2 cutover sequence or
authorize a continuously hosted service for that safety-critical cutover. The runtime operational boundary
must receive its own acceptance before it can become a required production writer.

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
- recover identically after process termination, machine reboot, lost messages, and duplicate commands;
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
| Deterministic policy kernel | Admissibility, prioritization, resource allocation, action selection, explanation | External mutation or transport retry |
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

## 4. Runtime topology

### 4.1 ASP.NET Core host

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

### 4.2 Actor hierarchy

```text
/user/orchestrator
├── policy                         pure-decision facade and policy-version registry
├── scheduler                      candidate inventory, capacity, dispatch wakeups
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

### 4.3 Actor responsibilities

| Actor | Persistent | Responsibility |
|---|---:|---|
| `Scheduler` | Yes | Maintain dispatch decisions, capacity reservations, fairness debt, and wakeups |
| `WorkItem` | Yes | Execute one issue lifecycle and correlate every attempt and external artifact |
| `AgentAttempt` | Partly | Supervise one sandbox/process; durable facts live in its parent work item |
| `Session` | Yes | Identity, capability generations, replay cursor, and connection replacement |
| `Connection` | No | Translate outbound actor events to one SignalR connection and report transport state |
| `GitHubObservation` | No/cache | Perform bounded typed reads; never turn incomplete data into absence |
| `GitHubMutation` | Intent/receipt | Serialize by aggregate, mint token, re-read, apply, verify, record |
| `Operation` | Yes | Manage a resumable multi-step saga and compensation plan |
| `Reconciler` | Cursor/receipt | Repair missed events and out-of-band changes through complete audits |
| `Policy` | Version pointer | Invoke pure algorithms and return decision plus explanation |

## 5. Domain model and deterministic core

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
val decide : Policy -> WorkflowState -> ObservationSet -> Decision<DomainAction>
val compileEffects : WorkflowState -> DomainAction list -> EffectIntent<ProviderEffect> list
```

`evolve` is replayed by persistence recovery and property-tested for determinism. `decide` is called only
with a fingerprinted policy version and normalized observation set. `compileEffects` cannot manufacture a
missing generation or convert an incomplete observation into permission.

### 5.1 Why pure policy remains outside actors

Putting decisions directly in actor receive handlers would make concurrency safe but meaning difficult to
test, compare, simulate, or model-check. The actor should perform a small protocol:

```text
receive command or observation
→ validate envelope and expected revision
→ call pure decision function
→ persist selected domain events atomically
→ update state by evolve
→ dispatch persisted effect intents
→ receive verified receipts
→ persist outcomes
```

The same policy package can then drive simulation, replay an incident, explain a live refusal, generate
counterexamples, and compare proposed versions without starting Akka or contacting GitHub.

## 6. Classical AI, operations research, and decision management

The deterministic layer is not a single priority formula. It is a staged decision system combining hard
constraints, symbolic rules, planning, scheduling, optimization, and uncertainty-aware choice. Each stage
has a distinct job and produces inspectable evidence.

### 6.1 Separate constraints from preferences

The scheduler first constructs the feasible set. Examples of hard constraints include:

- issue is an actual unit of work and in a schedulable state;
- all blocking dependencies are satisfied with complete observations;
- no active touch-set or operation conflict exists;
- an authenticated session has the required capability;
- repository, toolchain, model, and credential capabilities are available;
- API, concurrency, disk, and monetary hard budgets are not exceeded;
- the current fleet epoch permits the proposed writer;
- an accepted delivery route exists;
- the required policy and compiled-contract versions are readable; and
- no quarantined invariant or stop-the-world condition applies.

Only feasible candidates reach preference scoring. A high expected payoff cannot buy permission to violate
one of these constraints.

### 6.2 Symbolic production rules

Typed production rules encode stable operational knowledge:

```text
IF observation completeness is not Complete
THEN do not infer absence; schedule bounded re-observation or escalate

IF agent terminated AND open PR exists
THEN prefer recovery/adoption over release

IF second related late-stage defect occurs
THEN freeze candidate, widen causal analysis, update fault model, and mint a new review epoch

IF required check is missing AND producing workflow cannot run on this head
THEN refuse waiting as a remedy and request a new triggering head or policy correction
```

Rules return typed reasons and citations to governing contracts. They do not directly call provider APIs.

### 6.3 State-space planning

For bounded operational workflows, the planner searches a graph whose nodes are normalized workflow states
and whose edges are typed actions with preconditions, effects, cost, and recovery information. A plan is
acceptable only if every terminal state satisfies declared invariants and every irreversible edge has its
required evidence.

Useful algorithms include:

- breadth-first search for shortest small recovery procedures;
- Dijkstra or A* when operations have different time/API/risk costs;
- partial-order planning when independent reads or builds may occur concurrently;
- constraint programming for touch sets, delivery routes, and capacity assignment; and
- saga planning for multi-step external effects with compensation or roll-forward actions.

The heuristic used by A* may improve speed but cannot make an invalid path valid. For safety-sensitive
transitions, the final candidate plan is checked independently against the pure transition model.

### 6.4 Scheduling and operations management

The fleet scheduler treats work as a constrained service system rather than a list sorted by one score.
Its inputs include:

- work classes and required capabilities;
- precedence/dependency graph;
- declared touch-set conflict graph;
- estimated duration distribution;
- deadline or service-level target;
- agent/model availability and cost;
- GitHub REST and GraphQL budgets;
- build-runner and repository mutation capacity;
- failure/rework probability;
- aging and fairness debt; and
- expected information gain from diagnostic work.

Candidate scheduling policies should be evaluated against historical and synthetic workloads. Initial
policy should be intentionally simple and explainable:

1. remove infeasible work;
2. reserve mandatory recovery and incident capacity;
3. prioritize unblocking work by downstream dependency count;
4. apply earliest-deadline or aging pressure where declared;
5. prefer short work only within the same priority class;
6. minimize simultaneous contention for scarce repositories and API budgets;
7. allocate remaining slots by weighted fair queue; and
8. break exact ties by a stable canonical key.

This avoids starvation while retaining deterministic replay. More sophisticated solvers are admitted only
when a corpus shows an improvement over the baseline.

### 6.5 Decision theory under uncertainty

GitHub and agent observations are partially observable. The system must distinguish:

- aleatory uncertainty: duration, transient API latency, build outcome;
- epistemic uncertainty: incomplete pagination, unknown branch state, ambiguous requirement;
- adversarial uncertainty: untrusted issue text or agent output; and
- model uncertainty: the policy may omit a relevant state or consequence.

An action may be ranked by expected utility only after admissibility. A suitable factored objective is:

```text
maximize expected delivered value
       + expected information gain
       - execution cost
       - expected rework
       - latency penalty
       - risk exposure
```

This value is not collapsed across hard constraints. The first implementation should use lexicographic
tiers and explicit weights within a tier. Weight changes are versioned policy changes with replay evidence.

For uncertain observations, the action set normally contains `Observe`, `Wait`, `Probe`, `AskHuman`, and
`Refuse`, not merely `Proceed` or `Stop`. Value-of-information analysis chooses whether another read, test,
or agent critique is worth its cost. The system must cap repeated observation and escalate rather than
poll forever.

### 6.6 Markov models and when not to use them

A Markov decision process can model repetitive operational choices such as retry, replace-agent, wait,
or escalate when transition probabilities are measurable. A partially observable model may help distinguish
a slow agent from a lost agent. These models are advisory until trained and calibrated on sufficient
receipts. They may rank admissible actions but may not authorize irreversible effects.

The initial system should prefer transparent rules, constraint solving, and deterministic planning. A
learned value estimator enters later as one versioned input with confidence bounds, drift monitoring, and
a baseline fallback.

### 6.7 Avoiding Goodhart effects

Agents must never receive a naked scalar reward that invites them to manufacture the proxy. Controls include:

- keep evidence predicates independent of agent self-report;
- calculate delivery only from verified GitHub and build facts;
- retain multiple objectives instead of one public score;
- bound retries and token expenditure independently of claimed progress;
- audit disagreement between prediction and observed outcome;
- randomize diagnostic evaluation cases where appropriate;
- preserve negative and refused outcomes rather than rewarding only completion; and
- prohibit policy optimization from editing its own acceptance corpus in the same change.

### 6.8 Explanation contract

Every policy result emits a stable explanation:

```fsharp
type DecisionExplanation =
    { PolicyVersion: PolicyVersion
      ObservationFingerprint: string
      Considered: CandidateSummary list
      RejectedByConstraint: ConstraintFailure list
      ObjectiveTerms: ObjectiveTerm list
      Selected: string option
      TieBreak: string option
      NextWakeups: Wakeup list }
```

An operator must be able to answer: what was known, what was unknown, which constraints applied, which
alternatives were considered, why this action won, and what observation would change the decision.

## 7. Agent harness lifecycle

### 7.1 Work admission

Work enters from an authenticated human command, GitHub event, scheduled audit, roadmap driver, or follow-up
generated by an existing workflow. The gateway normalizes it to a subject key and causation identity. The
scheduler performs a fresh inventory read or consumes a complete fingerprinted snapshot, asks the policy
kernel for an admission decision, and persists `WorkAdmitted` or a typed refusal.

No agent is spawned merely because an issue exists. The work item must be schedulable, scoped, and assigned
a capability and resource envelope.

### 7.2 Agent specification

The orchestrator creates an immutable `AgentSpecification`:

```fsharp
type AgentSpecification =
    { AgentSpecId: string
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
      ReadCapabilities: Capability list
      WriteCapabilities: Capability list
      ToolAllowlist: ToolContract list
      TokenBudget: int option
      WallClockDeadline: System.DateTimeOffset
      HeartbeatInterval: System.TimeSpan
      RequiredOutputs: OutputContract list
      PolicyVersion: PolicyVersion
      SpecificationFingerprint: string }
```

The specification is content-addressed. Any change creates a new attempt or explicit amendment event; it
does not silently alter the running agent's authority.

### 7.3 Workspace preparation

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

### 7.4 Process creation

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

### 7.5 Context delivery

Context is layered and pullable:

1. **Invariant envelope:** authority boundary, security rules, completion contract, and stop conditions.
2. **Work envelope:** issue, exact snapshot, dependencies, touch set, expected outputs, and accepted design.
3. **Skill bindings:** content-addressed skill identities and only the references selected by those skills.
4. **On-demand evidence:** source files, logs, CI results, GitHub observations, or model traces requested
   through typed read capabilities.

The orchestrator stores context manifests and digests, not an assumption that a conversation transcript is
a stable protocol. A reconnecting or replacement agent receives the same immutable envelope plus subsequent
durable events.

### 7.6 Agent communication protocol

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

### 7.7 Tools and deterministic code

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

### 7.8 Subagents

A work agent may propose decomposition, but only the parent `WorkItem` actor may authorize another process.
Each subagent receives its own specification, session generation, workspace boundary, capability token,
budget, and completion contract. Parent and child do not share a mutable identity or an unrestricted
mailbox.

The policy kernel admits subagents when parallelism provides value and touch sets, dependencies, and
resources permit it. Fan-out is bounded. Results are joined through explicit artifact or finding contracts;
the parent agent does not treat a child's natural-language success claim as evidence.

### 7.9 Completion and disposal

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

## 8. WebSocket authentication and authorization

### 8.1 Three identities

The service keeps separate:

1. **connection identity:** the authenticated principal at the network boundary;
2. **session/workflow identity:** the durable agent or human session and its capabilities; and
3. **provider identity:** the GitHub App installation principal used for external effects.

A connection may be replaced without changing the session. A session generation may be revoked without
changing the human identity. A provider token may rotate without changing either.

### 8.2 Human authentication

Human clients use an authorization-code flow with PKCE through an accepted identity provider. GitHub
identity is suitable when organization membership and repository access are the relevant facts. The
orchestrator maps the provider's stable immutable user ID to local roles and revalidates authorization at
bounded intervals and before privileged operations.

Local roles are intentionally small: observer, operator, delivery owner, emergency operator, and service
administrator. Authentication does not imply any of them.

### 8.3 Machine authentication

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

### 8.4 Per-message authorization

SignalR authenticates the connection, but the gateway authorizes each command envelope. It verifies token
expiry, session generation, subject, capability, expected workflow revision, command schema, size, and rate
limit before forwarding an internal typed message.

Browser WebSocket implementations may transmit bearer tokens as an `access_token` query value. TLS is
mandatory; reverse-proxy, ASP.NET, tracing, exception, and analytics logs must redact it. Connections have
a bounded maximum age because the principal established at connection time does not automatically change
when external authorization changes.

### 8.5 GitHub authentication

Normal automation uses a GitHub App rather than a PAT. The credential broker holds the App private key
outside agent workspaces and mints installation tokens restricted to the required installation,
repositories, and permissions. Tokens are cached only until a conservative pre-expiry boundary and never
persisted in actor events, logs, traces, or command envelopes.

Where a GitHub action must be attributed to a human, the design either records the initiating principal in
the immutable FS.GG receipt or uses an explicitly accepted user-to-server flow. A client-supplied GitHub
token is never proxied through an agent tool request.

### 8.6 Akka transport security

Akka.Remote is disabled in the initial deployment and never serves public clients. If clustering is later
enabled, nodes communicate only on a private network with mutual TLS, explicit schema-bound serializers,
and registered message types. Polymorphic deserialization and fallback serialization are disabled. The
SignalR gateway remains the only client ingress.

## 9. Persistence and effect protocol

### 9.1 Event sourcing

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

### 9.2 Durable inbox and outbox

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

### 9.3 Delivery guarantees

Ordinary actor messages are treated as at-most-once. Akka reliable delivery may be used for internal
cross-node paths later, but consumers still deduplicate because at-least-once delivery can redeliver after
recovery. Public WebSocket messages use the harness sequence/ack protocol, not assumptions about Akka
mailboxes.

### 9.4 Storage

PostgreSQL is the first production journal, snapshot, inbox/outbox, and projection store. It provides a
well-understood backup and transactional boundary without making its rows the GitHub coordination
authority. SQLite is acceptable for developer and single-process prototypes only after crash and locking
behavior is qualified.

Backups require encrypted storage, retention policy, point-in-time recovery where available, and scheduled
restore rehearsals. A backup that has never been restored is not availability evidence.

## 10. GitHub gateway and mutation safety

### 10.1 Observation path

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

### 10.2 Mutation lanes

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

### 10.3 Existing GitHub/Git fencing

Actor ownership and a database lease reduce duplicate execution but do not fence a paused or partitioned
writer. External mutation authority remains bound to the protected FS.GG epoch and operation/claim
generation. Every effect validates those values at the provider boundary. Comments and Project fields are
human projections; protected Git history supplies strong ordering where selected by GitHub Substrate v2.

### 10.4 Emergency and multi-host operation

The existing CLI remains available when the orchestrator is down. Emergency actions:

- use the same typed engine and provider primitives;
- acquire or present the same external generation;
- record an emergency principal and reason;
- produce ordinary receipts; and
- are discovered by the next orchestrator reconciliation.

The orchestrator never assumes that all valid changes originated from its journal. GitHub reconciliation
is therefore a permanent correctness mechanism, not migration scaffolding.

## 11. Supervision, liveness, and availability

### 11.1 Failure classification

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

### 11.2 Liveness hierarchy

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

### 11.3 Health endpoints

- `/health/live`: process event loop, actor system, and watchdog are responsive;
- `/health/ready`: startup replay completed, required actors registered, journal writable, schemas current,
  credential material loadable, and this node may accept its configured traffic class;
- `/health/dependencies`: GitHub, database, disk, runner, authority ref, and model/toolchain status with
  typed `Healthy`, `Degraded`, or `Unhealthy` reasons;
- `/health/workflows`: counts and oldest age for waiting, retrying, quarantined, and uncertain-effect states.

A GitHub outage degrades provider readiness but must not cause a process restart loop. Liveness measures the
process; readiness controls traffic; dependency health guides operations.

### 11.4 systemd

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

### 11.5 Single-node availability

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

### 11.6 Multi-node availability

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

## 12. Backpressure and resource governance

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

## 13. Observability and audit

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
- scheduling feasibility reasons, queue age, fairness debt, and capacity utilization;
- GitHub rate budget, incomplete observations, circuit state, mutation latency, and conflicts;
- unsettled effect age and reconciliation outcomes;
- journal write/replay/snapshot latency and storage growth; and
- predicted versus observed duration, cost, failure, and utility.

Logs must exclude bearer tokens, GitHub tokens, App private keys, cookies, raw secrets, and unbounded agent
content. Security-relevant records are immutable or exported to a protected sink. Operator dashboards link
runtime state to the GitHub-visible subject and receipt without making the dashboard authoritative.

## 14. Testing and formal assurance

### 14.1 Pure model tests

- property-test `evolve` determinism and invariant preservation;
- replay every accepted historical event schema;
- generate workflow action sequences and compare with the Quint model where correspondence exists;
- retain historical coordination defects as regression cases;
- mutation-test high-risk predicates with named non-vacuity controls; and
- compare policy explanations and chosen actions against golden decision fixtures.

### 14.2 Runtime tests

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

### 14.3 Security tests

- attempt cross-issue and cross-repository capability use;
- replay bootstrap and session tokens;
- forge session generation, sequence, subject, and expected revision;
- submit oversized, malformed, unknown-version, and polymorphic payloads;
- confirm token redaction across every log and trace path;
- verify agents cannot read host credentials or contact denied networks;
- attempt arbitrary actor-path addressing and Akka.Remote access;
- test authorization revocation on an established socket; and
- verify provider tokens are narrowed and absent from persistence.

### 14.4 Availability tests

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

## 15. Deployment and operational model

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

## 16. Implementation roadmap

### H0 — decision and vertical-slice specification

- Accept or amend this architecture and its operational owner.
- Specify one end-to-end issue lifecycle in Quint and the compiled FS.GG contract.
- Define stable IDs, messages, events, receipts, and authority boundaries.
- Choose PostgreSQL persistence plugins and supported schema-evolution strategy.
- Threat-model the WebSocket, sandbox, database, App, and Git authority boundaries.

**Exit:** an independently reviewable specification and failure matrix exists; no runtime mutation is enabled.

### H1 — authenticated read-only host

- Build ASP.NET Core + SignalR + Akka.Hosting.
- Implement human and machine authentication, per-message authorization, session generations, and replay.
- Add actor-system, journal, dependency, and workflow health endpoints.
- Observe GitHub through the typed engine without mutation.
- Persist decisions and compare them with current operator/CLI behavior.

**Exit:** restart, reconnect, revocation, malformed-message, and read-completeness tests pass; the service
cannot mutate GitHub.

### H2 — durable agent lifecycle

- Add workspace provisioning and sandboxed runner.
- Create, supervise, heartbeat, cancel, replace, and dispose agents.
- Deliver content-addressed context and collect typed artifacts/findings.
- Correlate agents with claims, branches, worktrees, and PRs read-only.

**Exit:** agent and host kill tests recover without losing accepted commands or falsely abandoning work.

### H3 — deterministic policy and operations management

- Implement feasibility constraints, symbolic rules, baseline scheduler, bounded planning, and explanations.
- Replay historical board and incident snapshots.
- Measure throughput, latency, API cost, fairness, starvation, and rework against the existing driver.
- Add parameter/version governance and a safe baseline fallback.

**Exit:** the policy is deterministic, explainable, corpus-backed, and no worse than the baseline on declared
safety and fairness measures.

### H4 — sealed plans and shadow effects

- Compile decisions into effect intents.
- Persist inbox/outbox and simulate provider acceptance, loss, duplication, and stale generations.
- Run shadow comparison against real typed-engine verdicts.
- Qualify token minting, rate limits, circuit breakers, and postcondition reads.

**Exit:** every injected interruption converges without an unfenced effect; shadow divergence is adjudicated.

### H5 — bounded mutation canary

- Enable one low-risk repository and operation class.
- Require explicit operator approval of sealed plans.
- Retain CLI parity and immediate kill switch.
- Run scheduled complete reconciliation and compare all receipts.

**Exit:** canary duration and volume targets pass with zero unexplained effects, stale writes, or lost commands.

### H6 — normal single-node writer

- Route approved operation classes through the orchestrator by default.
- Retain external GitHub/Git fencing and emergency CLI.
- Establish SLOs, on-call/incident ownership, backup restoration, key rotation, and upgrade rehearsals.
- Remove only prompt choreography proven redundant; keep typed provider semantics.

**Exit:** the accepted operational boundary permits the service to become the normal writer.

### H7 — optional availability expansion

- Measure whether host recovery fails the availability target.
- Compare passive standby, service-manager failover, and Akka.Cluster.
- If clustering wins, qualify split-brain resolution, sharding/singleton behavior, rolling compatibility,
  reliable delivery, and external fencing.

**Exit:** a separate accepted decision authorizes multi-node deployment. Otherwise remain single-node.

## 17. Acceptance criteria

The architecture is ready for normal-writer consideration only when:

- no public endpoint exposes Akka.Remote or arbitrary actor messages;
- every WebSocket command is authenticated, authorized, bounded, versioned, deduplicated, and durably acknowledged;
- reconnecting clients resume from a durable cursor;
- session revocation prevents all older generations from issuing commands;
- agents receive no GitHub or service credential and cannot escape their sandbox contract;
- deterministic policy separates hard constraints from preferences and explains each decision;
- the same state, observations, and policy version produce the same plan;
- persistent actors recover across supported event versions and snapshots;
- no external effect begins before its intent is durable;
- uncertain effects reconcile rather than blindly retry;
- the typed coordination engine authorizes every provider mutation;
- the current GitHub/Git generation fences every concurrency-sensitive effect;
- GitHub outage, database degradation, and one poisoned workflow fail in their intended isolation domains;
- systemd restart and full machine reboot meet measured recovery targets;
- database restore and key rotation are rehearsed;
- emergency CLI use remains possible and is reconciled; and
- an accepted owner, SLO, incident process, retention policy, cost envelope, and disaster-recovery proof exist.

## 18. Consequences and trade-offs

### Benefits

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
- Classical AI and optimization models can become opaque or misaligned unless kept versioned, constrained,
  measured, and explainable.
- A future cluster would add partition and rolling-compatibility risks and therefore remains deferred.

## 19. Alternatives considered

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

## 20. External references

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

## 21. Further reading

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
