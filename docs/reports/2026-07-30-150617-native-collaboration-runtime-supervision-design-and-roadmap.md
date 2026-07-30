# Native collaboration-runtime supervision: design and roadmap

- **Created:** 2026-07-30 15:06:17 CEST (13:06:17 UTC)
- **Owner:** Codex collaboration runtime and `FS-GG/.github` coordination maintainers
- **Status:** design proposal and gated roadmap; not implementation authorization
- **Scope:** correlate Codex agent lifecycles with durable `fsgg-coord` claims, recover abandoned
  work safely, and reuse the same event/reconciliation substrate for heartbeat, CI, and dispatch
- **Incident basis:** `.github#1843` / `.github#1863` during the 2026-07-30 drive-board run
- **Research basis:** current Codex documentation, Kubernetes controller and lease patterns,
  OpenTelemetry lifecycle modeling, GitHub Actions events, and the local coordination engine,
  protocol, tests, ADRs, and mutation evidence

---

## 1. Executive summary

The collaboration runtime should become the lifecycle supervisor for work it starts, while
`fsgg-coord` remains the authority for what a GitHub claim means and which mutations are safe.

The triggering failure was simple and expensive: this run spawned a worker, the worker claimed
`.github#1843` as `kite-c7b3`, and the worker later disappeared from the collaboration tree without
releasing or heartbeating the claim. The durable 120-minute claim survived its in-memory owner.
`.github#1863` was correctly held behind the overlapping path, but nothing correlated the missing
agent with the still-live claim. The operator eventually reconstructed the facts, verified that no
worktree, branch, or pull request existed, released the claim, and allowed `.github#1863` to finish.

This is not primarily a lease-duration problem. A shorter lease would reduce one delay while
increasing false abandonment during long work or API outages. The missing invariant is:

> Every durable claim created by a runtime-owned worker is registered against that worker's durable
> lifecycle identity, reconciled after every terminal event and restart, and left untouched whenever
> the provider cannot prove the proposed transition safe.

The recommended architecture has two layers:

1. The collaboration runtime records agent/worker/claim correlations, consumes native lifecycle
   events, schedules reconciliation, and owns retry, recovery dispatch, and operator notification.
2. A thin `fsgg-coord` provider adapter asks the existing engine for fresh, typed claim and work
   evidence, then invokes guarded provider primitives. It does not reinterpret markers, leases,
   pull requests, board columns, or Done evidence.

The first useful release is read-only: expose correlated lifecycle state and prove that the runtime
would have detected `.github#1843`. Automated heartbeats and wakeups follow. Automatic orphan
recovery comes later, only after the engine exposes an explicit claim-generation token and
compare-and-swap mutation contract. Force-stealing, automatic human decisions, and timer-only
cleanup remain prohibited.

## 2. Decision requested

Approve the responsibility boundary and milestone order in this report. Approval should authorize
design refinement and milestone extraction only; it should not silently authorize implementation,
enable automatic cleanup, or create a new scheduler.

The recommended decision is:

- build native runtime supervision around Codex lifecycle events;
- integrate through typed `fsgg-coord` provider operations;
- ship observation before mutation;
- require fresh evidence plus compare-and-swap for every automated claim transition;
- recover useful work before considering release;
- keep policy and ambiguous decisions with agents or operators;
- extract implementation issues only after the M0 contract review.

## 3. Evidence from the incident

### 3.1 What happened

During the 2026-07-30 drive-board run:

1. A runtime subagent claimed `.github#1843` under worker id `kite-c7b3` at approximately 11:10 UTC.
2. The claim marker reserved paths overlapping `.github#1863` and carried the default 120-minute
   lease.
3. The subagent disappeared from the runtime's active and completed agent tree. No terminal cleanup
   correlated that disappearance with the GitHub claim.
4. There was no heartbeat, worktree, branch, pull request, or response to coordination messages.
5. `reap` correctly refused to treat the still-unexpired lease as stale.
6. The operator verified the absence of work artifacts and released the exact worker's claim.
7. `.github#1863` then proceeded and landed.

The issue retains the coordination messages sent to the missing worker, including a progress and
heartbeat request, but the deleted claim marker is intentionally no longer present. See
[`.github#1843`](https://github.com/FS-GG/.github/issues/1843).

### 3.2 Root cause

The runtime had an in-memory agent lifecycle and GitHub had a durable claim lifecycle, but no durable
record joined them. Each subsystem behaved consistently inside its boundary:

- the runtime stopped listing a vanished agent;
- GitHub retained a live claim until explicit release or expiry;
- the scheduler continued reserving the claimed touch-set;
- the reaper refused an unexpired claim;
- no component owned the cross-system invariant.

The operational error was initially described as an “external” claim because the current agent tree
no longer contained the worker. That classification was false: the worker had been spawned inside
the same run. A durable correlation record would have made provenance directly inspectable instead
of reconstructing it from chat history.

### 3.3 Why the existing protections were right

The recovery delay was undesirable, but weakening the existing guards would have been worse:

- lease expiry is evidence of abandonment, not proof;
- an open item pull request is proof that work survived the worker and must block blind reaping;
- an unreadable marker, branch, or pull-request probe is unknown, not absent;
- `adopt` is for finished green work, not a generic ownership transfer;
- `done` is earned from merge/evidence facts, never inferred from time or a terminal agent status.

The design therefore adds correlation and reconciliation around the protocol. It does not loosen the
protocol to make orphan cleanup easier.

## 4. Research findings

### 4.1 Current Codex surface

**Documented facts.** Current Codex exposes much of the lifecycle signal a supervisor needs:

- Codex orchestrates subagent spawn, follow-up routing, waiting, result collection, and thread
  closure. Supported clients surface agent threads and status.
- `SubagentStart` and `SubagentStop` hooks include the `agent_id`, parent `session_id`, active
  `turn_id`, and agent type. Only command handlers execute today; prompt and agent hook handlers are
  parsed but skipped.
- `SessionEnd` does not run for subagents, so it cannot be the sole cleanup mechanism.
- App-server streams `thread/*`, `turn/*`, and `item/*` notifications. `turn/completed` distinguishes
  `completed`, `interrupted`, and `failed`; `thread/status/changed` exposes runtime state; and
  `collabToolCall` items carry sender, receiver, and new-thread identifiers.
- Inference from the current public API overview: no separate stable direct-spawn RPC is documented.
  Delegation is shown as agent/tool-driven, so a supervisor should observe and steer the documented
  collaboration mechanism rather than depend on an assumed client endpoint.
- Scheduled tasks provide cadence. They can start a standalone fresh chat or run inside an existing
  chat; desktop Git-project tasks may use a dedicated worktree. They are not documented as a
  transaction manager for an active multi-agent run.

**Design inference.** These facts make native integration feasible, but hooks alone are insufficient.
`SubagentStop` is a useful edge notification; it is not a durable registry, it does not provide a
provider claim generation, and a process crash can prevent a command hook from running. App-server
events plus persisted correlation and startup reconciliation are the stronger foundation.

App-server notifications are connection- and subscription-scoped, not documented as a global durable
feed of every child lifecycle. The supervisor must capture child thread ids from collaboration items,
subscribe to or resume the relevant threads, persist the last observed lifecycle position, reconnect
after transport loss, and repair gaps by listing/reading persisted threads plus registry
reconciliation. A notification is a wakeup hint; the persisted thread/runtime state and provider
inspection determine convergence.

Primary sources:

- [Codex subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents.md)
- [Codex hooks](https://learn.chatgpt.com/docs/hooks.md)
- [Codex app-server](https://learn.chatgpt.com/docs/app-server.md)
- [Codex scheduled tasks](https://learn.chatgpt.com/docs/automations.md)
- [Codex worktrees](https://learn.chatgpt.com/docs/environments/git-worktrees.md)

### 4.2 Controller and lease patterns

Kubernetes controllers continuously compare observed state with desired state and act to reduce the
difference. Kubernetes Lease objects persist holder identity, acquisition and renewal times,
duration, and transition count. Leader acquisition uses optimistic concurrency so simultaneous
candidates cannot both win.

The applicable lessons are:

- reconciliation must be repeatable after crashes;
- a durable resource version is safer than “last writer wins” cleanup;
- active release can reduce transition latency, but timeout recovery still exists;
- lease expiry permits a takeover attempt; it does not justify deleting unrelated durable work;
- one supervisor instance should be active per coordination domain, or every mutation must be
  independently fenced so split-brain cannot duplicate it.

Primary sources:

- [Kubernetes controllers](https://kubernetes.io/docs/concepts/architecture/controller/)
- [Kubernetes Leases](https://kubernetes.io/docs/concepts/architecture/leases/)
- [Kubernetes coordinated leader election](https://kubernetes.io/docs/concepts/cluster-administration/coordinated-leader-election/)

### 4.3 Lifecycle observability

OpenTelemetry models a trace as correlated spans across processes and span events as meaningful
points in time. Its event guidance explicitly fits state transitions, lifecycle moments, and outcomes
in asynchronous flows.

The supervisor should emit structured events using stable runtime, agent, worker, item, claim, branch,
pull-request, and reconciliation identifiers. Logs without those correlations would recreate the
manual reconstruction that failed in this incident.

Primary sources:

- [OpenTelemetry traces](https://opentelemetry.io/docs/concepts/signals/traces/)
- [OpenTelemetry event conventions](https://opentelemetry.io/docs/specs/semconv/general/events/)

### 4.4 GitHub event integration

GitHub Actions exposes `workflow_run` activity for requested, in-progress, and completed states, and
`check_suite` completion events. These events can reduce polling latency, but they do not replace an
exact-head landability read: workflow chaining has constraints, events can arrive late or be missed,
and a green event for an old head is not permission to merge a newer head.

Primary source:

- [GitHub Actions workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)

### 4.5 Local engine and protocol

The local engine already owns the difficult provider semantics:

- The claim lock is an `fsgg:claim` issue-comment marker. The lowest live marker id wins the
  comment-order compare-and-swap race; assignee and board Status are projections for humans.
- A claim records worker, optional session, age, and previous board status. Liveness is explicitly
  `LeaseHeld`, `LeaseExpiredNoPr`, `LeaseExpiredPrOpen`, `LeaseExpiredBranchPushed`, or
  `LivenessUnknown`.
- The default lease is 120 minutes. `heartbeat` renews only the exact current holder and refuses to
  resurrect an expired claim.
- `release` deletes the lock before attempting board restoration, so a projection failure cannot
  strand the lock.
- `reap` is dry-run by default, requires complete marker scans, re-verifies immediately before
  deletion, and refuses proof of surviving work or unknown liveness.
- `adopt` refuses a live claim and requires an expired claim plus a finished, green, mergeable item
  pull request.
- `done` validates merge and evidence facts, rolls up parents where permitted, and releases only the
  caller's exact lock.

The main local references are:

- [claim and liveness types](../../src/FS.GG.Coord.Core/Types.fs)
- [GitHub claim mutations](../../src/FS.GG.Coord.GitHub/Writes.fs)
- [CLI lifecycle orchestration](../../src/FS.GG.Coord.Cli/Client.fs)
- [worker-keyed claim ADR](../adr/0027-worker-keyed-claim-lock-and-worker-channel.md)
- [coordination engine design](../design/coordination-engine.md)
- [parallel-work protocol detail](../../.agents/skills/intra-repo-parallel-work/references/deep-detail.md)
- [generated protocol facts](../../.agents/skills/intra-repo-parallel-work/references/protocol-facts.md)
- [coordination mutation matrix](2026-07-28-coord-engine-mutation-matrix.md)

## 5. Goals and non-goals

### 5.1 Goals

1. Detect a runtime-owned worker that terminates while retaining provider resources.
2. Preserve a durable mapping from runtime agent identity to minted worker identity and claims.
3. Reconcile safely after normal completion, interruption, crash, process restart, or missed event.
4. Recover useful work before releasing its claim.
5. Keep all provider reads fail-closed and all mutations fenced by fresh state.
6. Reuse one lifecycle substrate for heartbeat, CI wakeups, merge readiness, bounded dispatch, and
   operational telemetry.
7. Make every automatic action explainable and replay-safe.
8. Allow provider adapters other than GitHub without moving provider semantics into the runtime.

### 5.2 Non-goals

The design does not:

- replace `fsgg-coord` scheduling, overlap, board, claim, adoption, landability, or Done logic;
- make policy decisions about human-blocked items;
- narrow or widen touch-sets automatically;
- steal live claims or invoke `claim --force`;
- treat agent completion as evidence that code merged or an issue is Done;
- close pull requests, discard worktrees, delete branches, or release claims on ambiguous evidence;
- promise exactly-once external mutation;
- bypass GitHub branch protection or exact-head CI verification;
- create implementation issues before design approval and milestone extraction.

## 6. Responsibility boundary

| Concern | Collaboration runtime | `fsgg-coord` provider |
|---|---|---|
| Agent lifecycle | Authoritative | Observes only supplied identity |
| Worker-id minting and registration | Initiates and persists mapping | Validates marker provenance |
| Claim meaning | Never reinterprets | Authoritative |
| Heartbeat timing | Schedules | Verifies holder and renews exact marker |
| Orphan detection | Detects missing/terminal agent | Classifies fresh claim/work evidence |
| Recovery dispatch | Creates or steers recovery work | Provides guarded adopt/release/finalize operations |
| Pull-request safety | Requests current verdict | Authoritative landability/evidence checks |
| Retry and restart | Authoritative | Makes individual operations idempotent or CAS-fenced |
| Human decisions | Escalates | Reports typed blocked/unknown reasons |
| Audit/telemetry | Correlates end-to-end | Emits provider receipts and reason codes |

The runtime owns **when to inspect**. The provider owns **what the inspection means** and **whether a
requested transition is still legal**.

## 7. Proposed architecture

```text
Codex lifecycle events                  GitHub / board / CI events
          │                                        │
          ▼                                        ▼
  lifecycle event journal ◄──────────── event normalizer
          │
          ▼
  runtime claim registry ───────► reconciliation queue
          │                              │
          │                              ▼
          │                     supervisor/reconciler
          │                              │
          │                              ▼
          └────────────────────► fsgg-coord provider adapter
                                         │
                                         ▼
                              typed inspect / heartbeat /
                              adopt / release / finalize
                                         │
                                         ▼
                                  GitHub provider state
```

### 7.1 Lifecycle event journal

Append a normalized record for agent start, collaboration call, claim registration, heartbeat,
terminal status, runtime restart, reconciliation intent, provider receipt, and escalation. The
journal may initially be a local durable store owned by the runtime, but its schema must not depend on
transcript parsing. Codex documentation explicitly says transcript format is not a stable hook
interface.

Each event has an idempotency key. Replaying the journal after a crash must reconstruct the same
registry and enqueue the same logical reconciliation, not duplicate provider mutations.

### 7.2 Runtime claim registry

One record represents one runtime/worker/item association:

| Field | Purpose |
|---|---|
| `runtime_session_id` | Parent runtime provenance |
| `agent_id` | Native lifecycle identity |
| `agent_type` | Profile used for dispatch and policy |
| `worker_id` | Distinct minted FS-GG worker identity |
| `provider` | Adapter name and version |
| `resource_ref` | Canonical repository/item reference |
| `claim_marker_id` | Provider's observed lock identity |
| `claim_generation` | Opaque CAS token for this observed claim |
| `claim_observed_at` | Fresh-read timestamp, not lease truth by itself |
| `lease_expires_at` | Wakeup hint, never an abandonment verdict |
| `worktree`, `branch`, `pr` | Known durable work artifacts |
| `last_heartbeat_at` | Runtime scheduling fact |
| `agent_state` | Starting, active, terminal, missing, or unknown |
| `reconcile_state` | Current state-machine position |
| `last_provider_receipt` | Exact machine receipt or error |
| `policy_version` | Policy that authorized an automated action |

The marker id is necessary but may not be sufficient as a generation token if a delete/recreate cycle
can produce a different semantic claim. The provider contract should return one opaque token that
changes whenever the authoritative claim identity changes.

### 7.3 Reconciliation queue

The queue accepts deduplicated work keyed by provider/resource/claim generation. Events enqueue work;
they do not perform destructive cleanup inline. This keeps hooks short, isolates provider outages,
supports backoff, and makes terminal processing replayable.

Priority classes:

1. terminal or missing agent with a registered claim;
2. exact-head CI/landability transition for recoverable work;
3. heartbeat deadline;
4. lease inspection deadline;
5. capacity/dispatch wakeup;
6. periodic drift audit.

### 7.4 Supervisor/reconciler

For each queue entry, the reconciler:

1. acquires a durable, lease-based per-resource reconciliation fence;
2. reads the current registry record;
3. requests a fresh typed provider inspection;
4. compares runtime and provider facts;
5. chooses only among policy-allowed actions;
6. records intent with an idempotency key;
7. verifies that it still owns the reconciliation fence, then asks the provider to perform a
   CAS-fenced mutation;
8. records the exact receipt;
9. re-reads the postcondition when the provider contract requires it;
10. schedules the next wakeup or escalates.

The reconciliation fence is a registry CAS record containing owner, generation, acquisition time,
renewal time, and expiry. A crashed owner cannot leave a permanent lock; a resumed owner whose fence
expired must stop, reacquire, and inspect again. Fence ownership is checked immediately before every
provider mutation. It reduces duplicate policy and dispatch work, while the provider's claim
generation remains the final authority over the external effect.

M0 and M1 should enforce one active supervisor per registry domain. M4 automatic release is permitted
only in that single-supervisor/single-registry topology. Production multi-instance supervision
requires the M6 leader lease and failover work. Correctness still comes from the per-resource fence
and provider CAS because process pauses, network partitions, and leadership transitions can briefly
overlap even after leader election.

### 7.5 Provider adapter

The first adapter wraps `fsgg-coord`. It should expose typed operations rather than shell prose:

```text
inspect(resource) -> ClaimInspection
heartbeat(resource, expectedGeneration, worker, session) -> MutationReceipt
release(resource, expectedGeneration, reason, targetStatus?) -> MutationReceipt
adopt(resource, expectedGeneration, expectedPr, expectedHead,
      recoveryWorker, recoverySession) -> AdoptionReceipt
landable(pr, expectedHead) -> LandabilityReceipt
merge(pr, expectedHead) -> MergeReceipt
finalize(resource, expectedGeneration, evidence) -> DoneReceipt
```

`inspect` must distinguish:

- no claim observed;
- exact live claim;
- expired claim with no work evidence;
- expired claim with open pull request;
- expired claim with pushed branch;
- unreadable or incomplete evidence;
- claim generation changed.

Existing CLI verbs remain the implementation authority for the guarantees they currently provide,
but this proposed surface is not a claim that every guard exists today. In particular, current
`adopt` transfers the claim after its own landability read; it does not accept an expected head and it
does not merge the pull request. The existing `landable --sha` path can require a head, and GitHub's
merge request can be SHA-guarded, but M0 must define and parity-test one composed expected-head
contract before either operation is automated. M0 also decides whether the adapter calls a library
API, a stable JSON command surface, or a local protocol service. A new integration must not
screen-scrape human output.

Provider mutations must not be implemented through app-server `thread/shellCommand` or experimental
`process/*` escape hatches. Those are not a typed coordination boundary; `thread/shellCommand` runs
outside the thread sandbox, and experimental process APIs do not provide the stable provider receipt
and capability contract required here.

## 8. State machine

```text
registered ──► active ──► terminal-observed ──► inspecting
    ▲             │               │                 │
    │             ├──heartbeat────┘                 ├── no claim ──► settled
    │             │                                 ├── unknown ───► escalated/retry
    │             └──runtime missing───────────────►├── live work ─► recovering/waiting
    │                                               ├── green PR ──► adopting ─► merging ─► finalizing
    │                                               └── no work ───► releasing
    │                                                                    │
    └──────────────────── generation changed / new owner ◄───────────────┘
```

Terminal states are `settled`, `escalated`, and `superseded`. `failed` is not terminal while a safe
retry remains. A generation change supersedes the old reconciliation; it never authorizes an action
against the new holder.

## 9. Recovery policy

| Runtime fact | Provider fact | Automated response |
|---|---|---|
| Agent active | Exact live claim | Heartbeat on schedule |
| Agent completed | No claim | Settle registry record |
| Agent terminal/missing | Exact live claim, active work evidence | Create recovery handoff; do not release |
| Agent terminal/missing | Expired claim, green mergeable exact-head PR | Dispatch bounded recovery; automate adoption/merge only after the expected-head contract exists |
| Agent terminal/missing | Expired claim, pending/red PR | Dispatch diagnosis or escalate; retain work |
| Agent terminal/missing | Claim with no PR/branch/worktree evidence | Release only when the runtime-owned registration and terminal fact survive the grace period, the fresh inspection is complete, M4 policy is enabled, the exact generation matches, and the postcondition is re-read |
| Any | Claim owned by another generation/session | Supersede; no mutation |
| Any | Incomplete/unreadable evidence | Fail closed, retry with backoff, then escalate |
| Agent completed | PR merged but Done evidence incomplete | Dispatch finalizer; never infer Done |
| Runtime restarted | Registry record not settled | Re-enqueue inspection |

For an unexpired claim with no work evidence, the default rollout policy remains observation and
operator notification. Automatic early release is an optimization with real destructive potential;
it belongs in M4 after false-positive testing, not in the first mutating milestone.

## 10. Reuse for existing runtime features

The same substrate can safely improve several current features:

### 10.1 Heartbeat service

Registering a claim schedules renewal before the lease boundary. Renewal uses the expected generation
and exact worker/session. A failed or expired heartbeat transitions to inspection; it does not retry
blindly or revive a dead marker.

### 10.2 CI and merge monitoring

GitHub events enqueue an exact-head landability inspection. Polling remains a fallback for missed
events. While checks run, the authoring turn may finish instead of consuming a lane. When the verdict
changes, the supervisor may resume a persisted thread where the documented surface supports it;
otherwise it dispatches a fresh bounded recovery or scheduler turn. `turn/steer` is reserved for a
currently in-flight turn, not treated as a generic wake mechanism.

### 10.3 Staggered dispatch and lane filling

Completion, release, touch-set change, budget recovery, and blocker-clear events can wake the
scheduler. The runtime still asks `fsgg-coord` for schedulable work and claims through its existing
CAS. It does not cache board availability as truth.

The configured worker maximum is a resource policy, not a correctness constant. The dispatcher keeps
all permitted lanes full when work is independently schedulable, but respects provider budgets,
touch-set overlap, user caps, and backpressure.

### 10.4 Recovery agents

A recovery agent receives a structured handoff: original objective, item reference, worktree/branch/
pull request, last provider receipt, current exact-head verdict, and permitted next actions. It does
not inherit an undocumented assumption that the original worker “must have been finished.”

The finished-work sequence remains explicit and interruptible:

1. transfer the expired claim to the recovery worker using the expected claim generation, pull
   request, and observed head;
2. re-run exact-head landability;
3. merge using an expected-head guard;
4. re-read the merge result;
5. invoke evidence-based `done` as the exact current holder.

Adoption alone is not landing, and landing alone is not Done. Until the provider exposes the composed
guards, a recovery agent performs these steps with fresh reads; the supervisor does not collapse them
into one inferred transition.

### 10.5 Audits and scheduled maintenance

Periodic drift audits reconcile runtime registry records against provider claims and surface:

- claim with no runtime owner;
- runtime owner with no claim;
- heartbeat overdue;
- terminal agent with open work;
- settled record with live provider resource;
- generation mismatch;
- repeated unknown provider reads.

Scheduled tasks can invoke audits, but the durable registry and provider CAS remain the source of
recovery safety.

### 10.6 Skill propagation and synchronization

A permanent runtime is also a good coordinator for skill propagation, provided it supervises the
existing delivery fabric instead of becoming another source of skill bytes.

The current system already separates several authoritative fabrics. They are not one universal
pipeline:

- producer skill manifests and canonical `SKILL.md` bodies feed the
  [`registry/skills.yml`](../../registry/skills.yml) catalog, reconciled by the detection/response
  pair `skill-registry-coherence` and `skill-registry-autofix`;
- some skills are catalog/gate surfaces and require no receiver delivery;
- product/process skills can flow through their owning producer, scaffold, or generated-view
  machinery;
- driver skills can flow through their driver package and consumers;
- coordination-kit content flows through `FS.GG.Kit`, receiver pins, `kit-materialize` /
  `coordination-sync`, and receiver gates.

The runtime must derive an applicable delivery graph per skill, capability, and receiver from the
live registries and manifests. It must not force every skill through a package pin or coordination-kit
materialization stage.

The permanent runtime can add a durable rollout record across those stages:

| Rollout state | Runtime responsibility | Existing authority |
|---|---|---|
| Source changed | Correlate producer commit and affected skill ids | Producer manifest/body |
| Registry pending | Observe or request the existing reconciliation path | `registry/skills.yml` validators/autofix |
| Delivery graph derived | Select only applicable producer/package/scaffold/view/kit edges | Skill registry, producer manifests, capability roster |
| Artifact owed, if applicable | Track publish prerequisite and immutable digest/version | Owning release workflow and package registry |
| Receiver pending, if applicable | Build the intended receiver set for that delivery edge | Applicable roster, manifest, scaffold, or consumer declaration |
| Receiver in flight | Stagger the authorized workflow for that edge | Existing producer, Renovate, materialize, sync, or scaffold workflow |
| Receiver verifying | Track the edge's required head, checks, and supersession | That receiver/delivery contract |
| Edge current | Retain the authority-specific postcondition receipt | Applicable digest, package, pin, generated view, committed bytes, and/or CI |
| Blocked/unknown | Stop that branch, preserve the reason, and escalate | Typed workflow/provider verdict |

This turns a cross-repository propagation into a resumable state machine. After a runtime restart it
can answer which receivers are current, pending, in flight, blocked, superseded, or unverifiable,
without re-copying files or guessing from the age of a pull request. It can also stagger dispatch to
respect API budgets and avoid updating every receiver simultaneously.

The safety boundary is strict:

- the runtime never hand-copies a skill or synthesizes a registry digest;
- publish-before-flip remains mandatory on edges that publish an artifact;
- artifacts are addressed by immutable version and digest, not “latest”;
- an edge is current only when its applicable authority's required postconditions agree; a
  coordination-kit receiver needs its pin/materialized bytes/gates, while a catalog-only row does not;
- workflow dispatch, pull-request creation, merge, or rollback requires the same explicit authority
  it requires today;
- an existing workflow that already reconciles or merges keeps that ownership—the runtime observes
  its receipt rather than racing it with a second implementation;
- partial rollout is a first-class state, not automatically “fixed” by rolling every receiver
  forward or backward;
- producer, registry, package, pin, materialization, and CI unknowns all fail closed.

This reuse should begin after the core supervision registry and reconciliation semantics are proven.
It can share the event journal, queue, idempotency keys, capability discovery, budget backpressure,
and telemetry, but it needs separate skill-delivery provider adapters and rollout policy. Claim
cleanup and skill propagation should not share mutation enablement merely because they share a
runtime.

## 11. Safety invariants

1. **A timer never proves abandonment.**
2. **Unknown is not absent.** Incomplete pagination, rate limits, or unreadable work evidence prevent
   mutation.
3. **Every mutation names an expected claim generation.**
4. **Generation mismatch is success-for-safety:** the old action is obsolete and must not be retried
   against the new holder.
5. **Recovery precedes release** whenever durable work evidence exists.
6. **Agent completion is not provider completion.**
7. **Done remains evidence-based.**
8. **Force operations are never automatic.**
9. **Board columns are projections, not locks.**
10. **Provider receipts, not runtime intention, determine reported mutation outcome.**
11. **Retries are at-least-once and effects are idempotent or fenced.**
12. **One unreadable dependency holds the transition closed.**
13. **A runtime may clean up only resources it registered or an operator explicitly adopted into its
    registry.**
14. **Policy changes are versioned and auditable.**

## 12. Race and failure analysis

| Failure/race | Required behavior |
|---|---|
| Agent finishes while terminal event is delayed | Startup/periodic reconciliation reaches the same result |
| Hook process fails | Journal gap is repaired from app-server/runtime state plus registry audit |
| Runtime crashes after provider mutation but before receipt persistence | Retry with same idempotency key; provider returns already-applied or generation-mismatch receipt |
| Original worker returns during recovery | Claim generation/session decides ownership; one path is superseded |
| Claim is released and re-created before stale cleanup runs | New generation rejects stale release |
| Pull-request head changes after green event | Exact-head landability read rejects merge/finalize |
| GitHub read is rate-limited | No absence inference; backoff and expose degraded supervision |
| Supervisor split-brain | Per-resource CAS permits at most one current mutation |
| Clock skew | Provider timestamps are hints; fresh provider classification is authoritative |
| Worktree exists but branch/PR does not | Preserve and hand off; do not equate unpublished work with no work |
| Board projection write fails after lock release | Report deferred projection repair without recreating the lock |
| Telemetry export fails | Supervision continues; local audit journal remains authoritative |

## 13. Security and policy

The supervisor increases the runtime's power, so its authority must be narrower than its visibility:

- provider credentials should be scoped to the repositories and mutations already authorized for the
  active task;
- read-only observation should be independently configurable from mutation;
- managed configuration may set maximum workers, enabled providers, allowed automatic actions,
  grace periods, and escalation destinations;
- repository-local configuration may further restrict behavior but not widen managed authority;
- hooks from plugins remain subject to Codex trust review;
- secrets and prompt/transcript content must not be copied into telemetry;
- recovery records should contain stable identifiers and receipts, not full model reasoning.

Suggested configuration shape, explicitly hypothetical:

```toml
[collaboration.supervision]
enabled = true
mode = "observe"                 # observe | heartbeat | recover
max_workers = 5
reconcile_interval_seconds = 30
terminal_grace_seconds = 60
unknown_retry_limit = 5

[collaboration.supervision.providers.fsgg_coord]
enabled = true
allow_heartbeat = true
allow_adopt_green = false
allow_release_no_work = false
```

The final names and layering belong to M0. This report does not claim that these settings exist.

## 14. Observability contract

Minimum event names:

- `collaboration.agent.started`
- `collaboration.claim.registered`
- `collaboration.claim.heartbeat.requested`
- `collaboration.claim.heartbeat.completed`
- `collaboration.agent.terminal`
- `collaboration.agent.missing`
- `collaboration.reconcile.started`
- `collaboration.provider.inspected`
- `collaboration.recovery.dispatched`
- `collaboration.claim.released`
- `collaboration.item.finalized`
- `collaboration.reconcile.escalated`

Minimum metrics:

- active agents, registered claims, and unowned claims;
- heartbeat age and failures;
- terminal-to-inspection and terminal-to-settled latency;
- recovery/adoption/release counts by reason;
- generation-mismatch count;
- unknown-read count and duration;
- queue age and reconciliation attempts;
- provider budget/rate-limit state;
- lane utilization and idle-with-schedulable-work duration.

Every event should carry `runtime_session_id`, `agent_id`, `worker_id`, `resource_ref`,
`claim_generation`, `reconcile_id`, and `policy_version` when known. Provider-specific identifiers
remain namespaced attributes.

## 15. Test strategy

### 15.1 Pure model tests

- transition table coverage for every runtime/provider fact pair;
- property: unknown evidence never produces a destructive action;
- property: generation change never mutates the new holder;
- property: terminal agent alone never produces Done;
- property: surviving work is never released by the no-work path;
- property: replaying the same event sequence reaches the same settled state.

### 15.2 Provider contract tests

Extend existing `coord-engine-parity` fixtures and GitHub write tests for:

- inspect receipt completeness and stable JSON vocabulary;
- release with matching and mismatching generation;
- renewal racing release;
- delete/recreate marker ABA resistance;
- green PR adoption with exact and changed head;
- branch-only and worktree-only recovery evidence;
- pagination failure and rate-limit failure;
- already-gone marker idempotency;
- board restoration deferred after successful lock deletion.

### 15.3 Runtime fault injection

Kill the runtime:

- before claim registration persistence;
- after registration but before worker receives the claim;
- after provider mutation but before receipt persistence;
- during heartbeat;
- after PR green but before merge;
- after merge but before Done;
- while a hook is running;
- during supervisor leadership transition.

Each scenario must converge after restart without duplicate work or unsafe release.

### 15.4 Incident replay

Encode the `.github#1843` sequence as a regression scenario:

1. spawn worker;
2. register live claim;
3. remove worker without terminal cleanup;
4. observe no branch/PR/worktree;
5. prove read-only detection immediately;
6. prove observe-mode escalation;
7. under later recover policy, prove exact-generation release;
8. prove the overlapping `.github#1863` wakeup occurs only after provider postcondition.

## 16. Roadmap

### M0 — Contract, threat model, and incident fixture

- Ratify the runtime/provider responsibility table.
- Specify the durable registry and event schemas.
- Define a Codex capability matrix: stable versus experimental methods/events, version-specific
  generated schema compatibility, required initialization capabilities, and fallback behavior when a
  lifecycle signal is unavailable.
- Define claim-generation semantics and ABA protection with `fsgg-coord` maintainers.
- Define provider inspection JSON, exit classes, and idempotency behavior.
- Define expected-head adoption and merge receipts; record explicitly that current `adopt` transfers
  ownership but neither pins the observed head nor merges.
- Model permissions, managed configuration, and multi-instance supervision.
- Add the `.github#1843` incident replay as a non-mutating executable fixture.
- Record baseline false-positive/false-negative measures from representative runs.

**Exit gate:** architecture and security review approve the contract; the incident fixture detects the
orphan without mutating GitHub; no ambiguity remains about which layer decides claim liveness.

### M1 — Read-only lifecycle registry and observability

- Persist `SubagentStart`, collaboration-call, claim-registration, terminal, and restart facts.
- Capture child thread ids from collaboration items, subscribe/resume each relevant thread, checkpoint
  observed lifecycle state, and reconnect after transport loss.
- Consume supported app-server lifecycle events; use hooks as supplemental edges, not sole truth, and
  reconcile persisted threads/registry state to repair missed notifications.
- Implement startup and periodic read-only reconciliation.
- Surface orphan, overdue heartbeat, provider-unknown, and generation-drift findings.
- Emit correlated lifecycle telemetry and an operator-readable audit view.

**Exit gate:** canary runs account for every runtime-owned claim; simulated event loss is repaired
after restart; no provider mutation is possible in this mode.

### M2 — Managed heartbeat and event-driven wakeups

- Schedule exact-holder heartbeat through the provider adapter.
- Stop heartbeat on terminal, expired, or generation-mismatch facts.
- Subscribe to CI/PR events and retain polling fallback.
- Resume a persisted thread when supported, or dispatch a fresh bounded turn, on blocker, capacity,
  and landability changes.
- Add provider-budget backoff and degraded-state reporting.

**Exit gate:** long-running canaries retain claims without agent polling; missed events converge by
polling; no expired claim is revived.

### M3 — Verified finalization and recovery handoff

- Detect merged-but-not-finalized items.
- Dispatch a bounded finalizer with exact receipts and Done evidence requirements.
- Create structured recovery handoffs for branch/PR/worktree evidence.
- Add and parity-test expected-generation plus expected-head adoption, exact-head landability, guarded
  merge, merge-result readback, and evidence-based Done as separate receipts.
- Keep red, pending, conflicted, and unknown work for agent judgment.

**Exit gate:** fault injection after PR green and after merge converges without lost commits,
duplicate merge, head-substitution, or false Done; no test treats successful adoption as proof of
merge or successful merge as proof of Done.

### M4 — CAS-fenced orphan release

- Add provider claim-generation and expected-generation mutation support.
- Implement observe-first policy for live but ownerless claims.
- Permit automatic release only for runtime-owned, no-work-evidence claims under explicit policy.
- Restrict mutation to one active supervisor and one authoritative registry domain; defer
  multi-instance mutation until M6 leader fencing is proven.
- Re-read provider postconditions before waking overlapping work.
- Add rollback switch to return instantly to observe-only mode.

**Exit gate:** race, ABA, outage, and restart suites are green; canary evidence shows no false release;
security review approves enabling mutation for a bounded repository cohort; topology enforcement
refuses a second mutating supervisor.

### M5 — Budget-aware staggered dispatch

- Feed settled/released/blocker-cleared events into scheduling wakeups.
- Maintain the configured number of lanes when disjoint work and provider budgets permit.
- Pause rather than churn when the board is genuinely overlap-bound.
- Coalesce duplicate wakeups per scheduling domain and cap claim attempts per interval.
- Make lane utilization, provider budget, and unschedulable reasons visible together.
- Preserve `fsgg-coord take`/claim CAS as the only path from candidate to work.

**Exit gate:** load tests keep permitted lanes occupied without duplicate claims, budget collapse, or
busy polling; fan-in from completion, CI, budget, and blocker events stays within the configured
claim-attempt budget; “no work” and “could not read work” remain distinct.

### M6 — Production hardening and additional providers

- Add supervisor leader lease and cross-instance failover.
- Define retention, migration, and disaster-recovery procedures for the registry.
- Stabilize provider SDK boundaries and compatibility tests.
- Document extension requirements for non-GitHub providers.
- Graduate configuration from experimental to managed support after measured adoption.

**Exit gate:** multi-instance chaos testing converges; upgrades preserve registry and generation
semantics; operators can explain every automated mutation from retained receipts.

### M7 — Optional skill-propagation supervision

- Add read-only adapters for producer manifests, `registry/skills.yml`, package releases, receiver
  pins, materialization receipts, and receiver CI.
- Derive a per-skill/per-capability rollout graph from existing authorities, including catalog-only
  rows and delivery paths that do not use a package pin.
- Prove restart recovery and partial-rollout reporting in observe mode.
- Add event-driven wakeups with periodic audit fallback.
- Stagger only already-authorized registry, release, pin, materialize, and receiver-PR workflows;
  never replace their validation or merge gates.
- Record immutable source commit, registry digest, artifact version/digest, receiver head, and final
  CI/postcondition for every leg.
- Add per-stage and per-receiver kill switches so claim supervision can remain enabled while skill
  propagation returns to observe-only.

**Entry gate:** M6 registry durability, provider capability negotiation, leadership, and audit
retention are proven; the skill-delivery owners approve each adapter's authority boundary.

**Exit gate:** a canary skill change can be followed from producer through a bounded receiver cohort,
survives runtime restarts and superseded heads, never copies bytes outside an existing authoritative
path, and stops with a typed explanation on any unknown stage.

## 17. Rollout and rollback

Roll out in increasing authority:

```text
shadow observation
    └─► operator-visible warnings
          └─► automatic heartbeat
                └─► recovery/finalization dispatch
                      └─► bounded CAS release
                            └─► staggered dispatch integration
                                  └─► optional skill-propagation observation/coordination
```

Each stage requires:

- a per-repository canary cohort;
- a measured comparison with operator adjudication;
- a kill switch that reduces authority without deleting state;
- backward-compatible event and registry migrations;
- an audit export before rollback;
- no dependency on the next milestone for safe shutdown.

Rollback always stops new mutations first, preserves the registry, and leaves current provider
resources intact. It must not “clean up” uncertain state as part of disabling the supervisor.

## 18. Risks and mitigations

- **False orphan detection:** process/UI visibility can lag. Use durable events, grace periods,
  startup reconciliation, and provider evidence; begin in observe mode.
- **Split-brain supervisor:** leader leases reduce overlap but do not eliminate it. Fence every
  provider mutation with an expected generation.
- **ABA claim recreation:** marker absence followed by a new marker can fool identity-by-resource.
  Use opaque generation tokens, not resource name plus timestamp.
- **Provider outages:** failed reads can look like absence. Preserve typed unknown, back off, and
  expose degraded supervision.
- **Useful work loss:** a terminal worker may leave commits without a PR. Register worktree and branch
  as they appear; recover before release.
- **Duplicate policy engines:** reimplementing claim semantics in the runtime will drift. Keep
  provider classification and mutation in `fsgg-coord`.
- **Hook overreach:** command hooks run concurrently and may be skipped by process failure. Keep them
  small, non-destructive, and journal-oriented.
- **Telemetry leakage:** transcripts and prompts may contain secrets. Emit identifiers, states,
  reasons, and receipts only.
- **Lane churn:** aggressively filling lanes can exhaust API budgets or create repeated claim races.
  Use event wakeups, backpressure, and typed unschedulable reasons.
- **Roadmap scope drift:** turning the report directly into many issues can pull the board away from
  current commitments. Require the extraction gate below.
- **Propagation blast radius:** a permanent runtime can fan one bad source or policy decision across
  every receiver. Keep source, registry, artifact, pin, materialization, and CI authorities separate;
  canary cohorts, staggered rollout, immutable receipts, and per-stage kill switches are mandatory.
- **Duplicate rollout controllers:** existing autofix, Renovate, release, and materialize workflows
  already mutate state. Observe and dispatch their supported entry points; never create a parallel
  copy/merge path.

## 19. Alternatives considered

### Shorten the lease

Rejected as the primary fix. It reduces the maximum orphan delay but increases false expiry and does
not correlate a worker with its claim.

### Add a periodic `reap --apply`

Rejected. Reaping only expired claims misses the long unexpired window and is intentionally
conservative around surviving work. A blind cron also lacks runtime provenance.

### Implement cleanup only as hooks

Rejected as the durable design. Hooks provide valuable events, but command execution is not
guaranteed across process crashes, matching hooks run concurrently, and hook state is not the claim
registry.

### Put orchestration inside `fsgg-coord`

Rejected. The engine should not own Codex agent creation, thread state, or runtime restart. That would
couple provider semantics to one collaboration host and duplicate the runtime's lifecycle authority.

### Put provider semantics inside the collaboration runtime

Rejected. The local engine already contains years of fail-closed claim, liveness, adoption,
landability, and Done invariants. Reimplementation would create two safety authorities.

### Build a standalone external daemon first

Useful as a prototype harness, but not the preferred product boundary. Native events and configuration
belong in the collaboration runtime; the provider adapter may still run out-of-process if isolation
or release cadence requires it.

## 20. Open design decisions

These are deliberately retained for M0, in recommended order:

1. **Registry scope:** begin per local runtime installation, then evaluate shared coordination only
   after single-host recovery is proven.
2. **Provider API shape:** prefer a stable typed library/service boundary; accept JSON CLI transport
   initially if it is versioned and parity-tested.
3. **Generation token:** prefer a provider-issued opaque value over exposing GitHub-specific comment
   ids as the cross-provider contract.
4. **Supervisor leadership:** use one lease per coordination domain, while keeping resource CAS
   mandatory.
5. **Live orphan grace:** default to observe and notify; do not enable early release in the initial
   mutating cohort.
6. **Worktree evidence:** register worktrees natively where the runtime creates them; treat filesystem
   discovery as supplemental evidence.
7. **Recovery model:** dispatch a fresh bounded recovery agent rather than silently reusing an
   unrelated active agent.
8. **Telemetry backend:** emit OpenTelemetry-compatible events but keep a local durable audit journal
   independent of exporter availability.

## 21. Issue-extraction gate

This report is intentionally a design artifact rather than a new issue fleet. Implementation issues
may be extracted only when all of the following are true:

1. the responsibility boundary is approved;
2. M0 open decisions are resolved;
3. each issue maps to one milestone exit gate;
4. touch-sets are narrow enough to avoid turning the roadmap into a lane of one;
5. dependencies are explicit and publish-before-flip ordering is preserved;
6. the current board owner confirms that extraction will not displace higher-priority commitments;
7. automatic mutation remains disabled until its milestone's evidence is reviewed.

The roadmap can then be worked from the document in order. Until that gate is crossed, the unchecked
milestones are planning records, not queued work.

## 22. Definition of done

Native collaboration supervision is complete when:

1. every runtime-owned claim is durably correlated with its agent and worker identities;
2. normal completion, interruption, crash, missed hook, and runtime restart all converge;
3. heartbeats use exact holder/generation checks;
4. useful branch, pull-request, or worktree evidence is recovered before release;
5. unknown provider state blocks mutation;
6. every automated transition has a retained intent, policy version, provider receipt, and
   postcondition;
7. Done and merge remain evidence-based;
8. staggered dispatch fills permitted lanes without duplicating scheduler semantics or exhausting
   provider budgets;
9. canary and chaos evidence show no false release;
10. operators can disable mutation while retaining enough state to finish reconciliation manually.
