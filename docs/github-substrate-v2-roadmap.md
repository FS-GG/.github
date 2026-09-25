---
title: "Roadmap: GitHub Substrate v2 fleet cutover"
category: Design
categoryindex: 4
index: 26
description: "The complete execution sequence for independently building and qualifying FS.GG.Coordination, cutting the fleet to GitHub Substrate v2, and retiring v1."
---

# Roadmap: GitHub Substrate v2 fleet cutover

This is the living execution roadmap for replacing the current FS-GG coordination system. V2 is built in
a new `FS.GG.Coordination` repository, consumes the published FS.GG.SDD specification kernel, and is
qualified independently of the v1 lifecycle it replaces. The fleet is prepared additively, frozen once,
switched and verified while normal writes remain closed, opened at one explicit point of no return, and
then observed through 15 distinct completed v2 work items before destructive v1 contraction. V1 authoring is fenced immediately at
open; retained assets are inert forensic/recovery evidence and cannot restart v1. This document owns
cross-repository sequence and exit gates; the
[governing design](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md) owns the architecture
and rationale.

> **Quint-first candidate dependency:** [ADR-0077](adr/0077-quint-first-typed-specification-authority.md)
> and the [migration design](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md) require the
> behavioral protocol in GS2-02 to be a literate Quint source consumed through the published FS.GG
> compiled-contract boundary. Census/bootstrap work may proceed, but protocol implementation must wait for
> successful Q1 qualification, the post-qualification ADR-0077 amendment, and that producer artifact. The
> current F# P4 package remains production authority meanwhile.

> **Model-based-testing siting:** FS.GG.SDD supplies the published Quint/ITF and generic replay contract;
> `FS.GG.Coordination` owns its canonical protocol model, adapter, observable-state projection, and replay
> tests. `.github` owns the frozen v1 corpus, isolated GitHub qualification environment, registry, and
> routing policy. It must not grow a centralized v2 replay implementation or a product-specific shadow
> transition model.

> **Remaining-migration architecture amendment:** the
> [2026-08-30 architecture review](coordination/2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md)
> found that comment-order CAS and immediate post-open contraction do not meet the required concurrency and
> recovery contracts. GS2-03.10 accepted the sharded expected-parent Git-journal redesign and requalified
> the affected GS2-02 contracts; GS2-03.7 subsequently accepted the repaired candidate supply chain. The
> architecture amendment is therefore enforced rather than an outstanding blocker.

> **Accepted routine-development integration — 2026-09-07:** [Section 12](#12-accepted-amendment-routine-development-simplification-and-ordinary-v2-carryover)
> records the prospectively binding routine-profile, receiver, canary and measurement amendments, with the
> supporting [analysis](coordination/2026-09-07-131445-v2-roadmap-ci-simplification-analysis.md). It changes
> no current unit state, accepted evidence, dispatch state or operating authority; owning unit contracts,
> candidate qualification and exact roadmap pinning remain explicit prerequisites.

> **Qualified interlude — 2026-09-24:** [V2-CI-I1](#v2-ci-i1--unattended-credential-execution-interlude)
> installed and qualified an unattended trusted-CI credential path in the isolated sandbox.
> Dedicated production custody is enrolled, but its credential job remains inactive until
> the protected `OpenV2` and GS2-10 candidate gates. Current v1 genesis is unchanged.

| Field | Value |
|---|---|
| Status | GS2-00 and GS2-01 accepted; GS2-02.1–GS2-02.11, all GS2-03 units, all GS2-04 units, all GS2-05 units, all GS2-06 units, and GS2-07.1–GS2-07.8 and GS2-08.1–GS2-08.9 accepted; GS2-01.9 not applicable |
| Program | [GitHub modernization Epic `.github#2952`](https://github.com/FS-GG/.github/issues/2952) |
| Ratification | [`.github#2953`](https://github.com/FS-GG/.github/issues/2953) |
| Build and qualification | [`.github#2963`](https://github.com/FS-GG/.github/issues/2963) |
| Bridge and cutover ledger | [`.github#2964`](https://github.com/FS-GG/.github/issues/2964) |
| Fleet cutover and retirement | [`.github#2965`](https://github.com/FS-GG/.github/issues/2965) |
| Point of no return | Authoritative `OpenV2` transition in the protected cutover ledger |
| Current production authority | v1 until `OpenV2`; preparation and shadow reads do not change that |

## 1. How work is executed

The three program issues are too large to hand directly to a general worker. They are durable anchors for
ownership, roll-up, and cross-repository sequencing. Actual work is performed as one bounded roadmap unit
at a time.

### 1.0 Qualification strength at child and parent boundaries

[ADR-0080](adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md) makes scoped
content-addressed qualification the default for every ordinary child unit. A gate executes when its declared
semantic subject changes and may reuse independently validated immutable evidence when that subject is identical.
Formal-input drift always executes the canonical formal gate. The exact-head terminal manifest binds every current
or reused gate subject and artifact.

The final child of each parent milestone is also its explicit closure candidate. That candidate binds the complete
accepted child set and forces all declared gates cold; scoped reuse is forbidden. Protected merge and exact-merge
verification produce the append-only parent closure receipt. Freeze, release, cutover, rollback-authority, and
`OpenV2` boundaries are comprehensive regardless of their position in the hierarchy. GS2-04.9 is the first closure
candidate governed by this default.

[ADR-0081](adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md) adds the
operating principle inside that envelope: approximately daily, use observed gate cost, unique actionable-defect
yield, detection delay, closure equivalence, and blast radius to recommend retaining, increasing, or reducing each
gate's cadence. Sparse evidence stays explicitly inconclusive, cadence changes are reviewed versioned policy, and
closure plus production-authority boundaries cannot be weakened. A defect first found at closure feeds the next
cadence review instead of becoming an unpriced surprise.

**Recurring telemetry-health invariant.** At each meaningful V2 checkpoint (candidate qualification,
owner handoff, accepted-evidence review, or roadmap status report), and immediately before any protected
receipt, merge, or cutover action, run the configured client's authenticated `fdev-telemetry health`
probe and read the active V2 workspace with `fdev-telemetry exec fsgg-coord-engine telemetry workspace
status --workspace <active-workspace> --repository <repository>`. Require health `status=ready` and
workspace `status=configured`, `pending=0`, `pendingUnacknowledged=0`, and
`unacknowledgedLossy=false`. Record the observation time, workspace/repository, command verdicts, and
these fields without printing credentials. A missing, stale, or failing observation is a direct V2
blocker assigned to the reserved critical-path worker; hold the affected protected receipt, merge, or
cutover until the invariant is restored and freshly observed, while safe disjoint source work continues.
These two probes establish endpoint and workspace readiness only. Separately, accept telemetry
**end-to-end capture** only after one genuine work item runs through the existing instrumented
orchestration runner: record that item's native turn IDs and usage, obtain an **applied Host receipt**
whose workspace and item identities exactly match the run, and then verify the workspace queue returns
to `pending=0`, `pendingUnacknowledged=0`, and `unacknowledgedLossy=false`. Preserve the run/turn/receipt
correlation as evidence; an enqueued event, a ready Host, or a zero queue alone is not capture acceptance.
The acceptance record must bind the runner invocation and genuine work-item identity to the native
turn IDs and usage, the Host receipt's applied state and exact workspace/item pair, and a later
authenticated zero-queue observation. Do not substitute a synthetic item, an uninstrumented CLI
turn, a receipt for another workspace or item, or a pre-run zero-queue reading. At the 2026-09-25
checkpoint this acceptance is **pending**: the authenticated board query refused access, so no
genuine admitted item, runner turns or usage, or matching applied Host receipt was established.
At the same checkpoint, the read-only runtime capability probe reported a packaged `codex-exec`
adapter and configured remote workspace association, but `store=unconfigured`,
`hostActivation=not-assessed`, and `receiverReachability=not-checked`. A basic authenticated
GraphQL `viewer` read succeeded while the instrumented batch board query still returned
`Resource not accessible by personal access token`; no selected WorkItem was inferred.
Read-only follow-up isolated the minimal current access gap: Coordination Project 1 metadata,
fields and items are readable, but the same PAT receives HTTP 403 for unrelated organization
Project 2 with `organization_projects=read` named in the response. The runner's current
`Board.bootstrap` enumerates all organization Projects v2 by title before item admission;
effective organization Projects read access to Project 2 (including any needed organization
approval), or a separately accepted direct-Project-1 bootstrap change, is required for this
route. No WorkItem or native turn was admitted through that failed query.
At the 2026-09-25 16:36 UTC recheck, the current credential again read the exact
Coordination Project 1 (`PVT_kwDOEYAWY84Bb08W`) and again received
`Resource not accessible by personal access token` for Project 2. The direct Project 1
bootstrap drafts remain unaccepted and uninstalled, so this does not admit a runner item
or clear end-to-end capture acceptance.
The current direct interactive `codex --yolo` session is outside that runner. Wrapping it with
`fdev-telemetry exec` supplies credentials but does not emit native turn records; fleet capture of
direct fdev sessions requires a distinct session producer and its own qualification. Do not claim
this Codex transcript was captured without event-level evidence. Neither the readiness probes nor
an applied telemetry Host receipt clears the separate GS2-09.9 installed-provider/native-effect hold.
[Coordination draft #557](https://github.com/FS-GG/FS.GG.Coordination/pull/557) describes the
prospective direct-session producer and negative acceptance cases. Its source-only handoff now
distinguishes current-thread capability/assignment, private evidence packet boundaries, and
window-close versus native exit at head `7b08675a0f2c4eda6babed3bd2119b2cc0da0efa`; it is not
an installed producer, a captured turn, or an applied receipt.
[Dormant direct-session mapper draft #584](https://github.com/FS-GG/FS.GG.Coordination/pull/584)
maps supplied native turn ID and usage to the existing telemetry envelope with exact
workspace/repository/item/attempt/invocation/producer correlation and refusal controls;
61 Codex-adapter tests pass under installed SDK 10.0.401. It lacks an authenticated
current-session source, assignment verifier, submission and applied Host receipt; the
checkout's pinned SDK 10.0.400 was unavailable locally. The genuine runner acceptance
still cannot start through the denied Project 2 board bootstrap.
[Dormant assignment gate draft #586](https://github.com/FS-GG/FS.GG.Coordination/pull/586)
adds current-turn source and assignment-authenticator interfaces ahead of #584 and refuses
borrowed session IDs or mismatched workspace, item, attempt, challenge and thread bindings;
six focused controls pass after its final guard. The full adapter suite passed 67 controls
at its exact head in a subsequent rerun.
Neither trusted implementation, challenge issuer, submission nor Host receipt exists.
[Prospective-window draft #588](https://github.com/FS-GG/FS.GG.Coordination/pull/588)
adds dormant issuer/current-source interfaces and a pure one-use, UTC bounded challenge
gate that refuses stale, replayed, foreign and source-substituted windows; 73 adapter
controls pass. Trusted issuer, clock, authenticated session source and durable CAS replay
custody remain absent; it performs no capture or submission.
[Dormant challenge-CAS port #590](https://github.com/FS-GG/FS.GG.Coordination/pull/590)
reserves a prospective challenge before reading a session source and distinguishes replay,
unknown store outcomes and burned gaps; an in-memory 32-caller race has one winner, and
82 exact-head adapter controls pass. The issuer, trusted clock, authenticated current-session
source and durable CAS implementation remain absent. No Host submission or receipt exists.
[Dormant native-notification parser #593](https://github.com/FS-GG/FS.GG.Coordination/pull/593)
preserves exact thread/turn IDs and separate last/cumulative usage snapshots from authored
App Server fixtures; 87 adapter controls pass. It does not infer completed-turn usage from
snapshots or read this private transcript. An authenticated subscription to the already-running
thread with ordered start/usage/terminal continuity is absent, as are Host submission and receipt.
[Dormant subscription-continuity draft #595](https://github.com/FS-GG/FS.GG.Coordination/pull/595)
reduces supplied authenticated App Server scope and locally ordered start, usage and
terminal events while refusing gaps, replay, changed source, malformed frames and usage
regression; 94 adapter controls pass. Its local ordinal is not a native cursor. No private
thread access, trusted current-session source, durable journal or applied Host receipt exists.
[Dormant continuity-journal draft #597](https://github.com/FS-GG/FS.GG.Coordination/pull/597)
adds an injected atomic append port binding exact subscription, predecessor receipt,
ordinal and immutable notification bytes, with replay, foreign workspace/item, gap,
disconnect and uncertain-write refusals; 101 adapter controls pass. It has no installed
durable store/recovery reader, live authenticated session source or Host submission.
[Dormant journal-recovery draft #599](https://github.com/FS-GG/FS.GG.Coordination/pull/599)
requires a future authenticated, transactionally complete sealed snapshot and refuses
missing tail/terminal, broken predecessor chain, foreign binding, altered bytes and
post-gap entries; 109 adapter controls pass. It returns only provisional terminal or
gap. Durable store, seal issuer, trusted recovery source, live subscription and Host
submission remain absent.
[Prospective subscription gate #601](https://github.com/FS-GG/FS.GG.Coordination/pull/601)
reserves an issued challenge before a future authenticated current-session source and
checks workspace/item, native session, thread, transport, protocol and time bindings;
117 adapter controls pass. The trusted live source, subscribed `turn/started` journal
receipt and Host route are absent, so capture remains unaccepted.
[First native-start receipt draft #603](https://github.com/FS-GG/FS.GG.Coordination/pull/603)
binds a supplied first `turn/started` append receipt to the prospective subscription,
exact challenge/session, connection, canonical frame digest and time order; 124 adapter
controls pass. Authenticated live reader/transport, durable store and Host receipt remain
absent, so no direct-session capture is accepted.
[Sealed correlation draft #607](https://github.com/FS-GG/FS.GG.Coordination/pull/607)
binds a prospectively reserved first native start receipt to a supplied authenticated
sealed journal and verifies continuity to terminal; 130 adapter controls pass. It
returns correlation and usage-notification count only, because App Server last/total
snapshots do not establish completed-turn token usage. No telemetry envelope, Host
submission or capture acceptance follows from this draft.
[Native-usage no-verdict draft #610](https://github.com/FS-GG/FS.GG.Coordination/pull/610)
preserves exact workspace/item/session/thread/turn correlation but explicitly refuses
to infer completed-turn usage from installed App Server `turn/completed` status,
`thread/tokenUsage/updated` last/total snapshots, one upstream response or child
`codex exec` JSONL; 137 adapter controls pass. No telemetry envelope or direct-session
capture acceptance is emitted without a trustworthy native turn-usage source.
[Installed schema probe #612](https://github.com/FS-GG/FS.GG.Coordination/pull/612)
checks freshly generated ordinary and experimental Codex CLI 0.156.1 schemas and
preserves the same direct-session no-verdict: `turn/completed` has no usage,
`thread/tokenUsage/updated` gives snapshots, and a raw response is one upstream
completion. Authenticated attachment to this direct CLI session is also unproved;
143 adapter controls pass. The genuine instrumented runner route remains the separate
capture acceptance path once board admission and Host receipt are available.
[Dormant exact-board bootstrap #3751](https://github.com/FS-GG/.github/pull/3751)
queries the pinned Coordination Project 1 directly and refuses wrong owner, number,
title, ID or incomplete fields, while leaving the ordinary runner bootstrap unchanged;
838 exact-head GitHub adapter controls pass. A read-only direct Project 1 probe returned
the pinned ID and 24 fields under the same credential that still fails unrelated
all-project enumeration. Owner acceptance, opt-in runner wiring and qualification are
required before a genuine work item can be admitted; no capture is claimed.
[Opt-in runner bootstrap wiring #3752](https://github.com/FS-GG/.github/pull/3752)
passes an exact-Project-1 mode through batch/next/take while leaving the default route
unchanged; exact-head GitHub adapter 844/844 and scheduling CLI 8/8 pass. It is a source-only
draft, not an installed or admitted runner. The broader CLI has one UTEL-06D configuration
failure also reproduced on #3751's base. The denied all-project route remains blocked until
owner acceptance and installed qualification; no native turns or applied Host receipt exist.
[Exact-board duplicate-field draft #3772](https://github.com/FS-GG/.github/pull/3772)
stacks on #3752 at `56266f65174c492226c7a25c29a87652c38a64e9`. A red-before
complete Project 1 response with duplicate `Status` field names silently selected the
shadow ID/options; another complete response with duplicate `Ready` option names
silently selected a shadow option ID. The dormant direct route now refuses missing,
blank or duplicate field and single-select option names/IDs before map construction.
Its Release GitHub adapter suite passes 846/846. This draft neither installs the runner
mode nor admits an item; Project 2 remains PAT-forbidden on the installed enumeration route.
A fresh read-only Project 1 response returned pinned ID `PVT_kwDOEYAWY84Bb08W`,
`totalCount=24`, 24 field nodes and zero duplicate field or option identities; it
does not replace installed runner qualification.

**Agent-runtime invariant.** Every V2 orchestrator and worker must run the newest available Sol model
with high reasoning effort; the required target in this environment is `gpt-6-sol` / `high`.
Record the orchestrator's visible model/effort profile and launch every worker, including replacements,
with explicit `model=gpt-6-sol` and `reasoning_effort=high` arguments. Those explicit launch settings
are enforceable evidence for counting worker lanes; runtime self-introspection is optional and may be
unavailable, in which case record that limit without discounting an explicitly configured worker.
When reusing a completed agent identity, retain its original explicit launch settings as the evidence;
if a new identity is spawned, pass both arguments again. A follow-up task cannot change an agent's
launch model or effort.
Do not count a worker whose launch settings are missing or different. Preserve the reserved direct V2
worker among compliant lanes. If the orchestrator's visible profile is missing or different, report
the capability gap and arrange a compliant handoff before claiming compliant orchestration or
advancing protected actions. Model-family descriptions and task text alone are not launch evidence.

**Deterministic progress-update projection.** Prepare a source-only `.github`-owned F# renderer
for V2 checkpoints. An external collector supplies a typed `ProgressSnapshot`; the renderer
performs pure validation and derivation, then emits byte-stable Markdown. The snapshot records
each active lane's model, effort, owner and reserved-direct-V2 state; workstream status;
PR/evidence counts; telemetry readiness and separate end-to-end capture acceptance; protected
holds; checks, risks and next actions. Reject inconsistent lane counts or a claimed Sol/high
or reserved lane without explicit launch evidence. Capture may render `Accepted` only with a
genuine instrumented runner item, native turn IDs and usage, an applied Host receipt matching
the exact workspace and item, and a later authenticated zero-queue observation. Readiness alone
must render as readiness, never as capture. Render semantic markers with text labels in plain
GitHub Markdown: 🟢 Active/Healthy, 🟡 Pending, 🟠 Blocked/Incomplete evidence, 🔴 Failed/Unsafe,
🔵 Completed/Info, and 🔘 Unknown; do not depend on CSS. Completion history stores UTC
`DateTimeOffset` values and renders a deterministic newest-first **Last 5 completed** table
with time, item/workstream, result, evidence link and recorded roadmap head. Show at most five
real completions, exactly five when at least five exist, with no fabricated padding. Keep
collection, publication and Authority writes outside this pure draft. After owner acceptance,
the V2 status-update workflow may collect immutable facts, validate/render them, review the
output and publish through its existing route; a rendered summary cannot clear a protected
receipt, merge or cutover hold.
Accept authenticated, collector-verified CLI `/status` weekly allowance evidence only with an
observation time and provenance ID. Record remaining weekly percentage and reset as an explicit
local offset/time zone; report current context occupancy as a separate window measurement.
The live `/status` panel monitor observed **26% left**, reset **2026-09-30 09:28 Europe/Vienna**,
with **191K/258K context occupancy**. These observations lack authenticated collector
provenance and remain non-authoritative renderer fixtures until verified; context occupancy
is neither cumulative token usage nor a five-minute usage delta. Report period usage as
**Unknown** until genuine native turn IDs and input/output
usage or complete collector-verified native `token_count` histories support a bounded period;
do not subtract context readings to invent a delta.
Give consolidated V2 progress reports every ten minutes while work is active. Refresh weekly
allowance at each report from authenticated CLI `/status` or collector-verified native
`rate_limits.primary.used_percent`, recording time, provenance and reset. The native JSONL
collector must cover the root session and every transitive child linked by
`session_meta.parent_thread_id`, with complete ordered per-session cumulative
`token_count.info.total_token_usage` histories. Validate monotonic input, cached input,
output and total, `cached<=input`, `total=input+output`, and cached deltas no greater than
input deltas. Sum each session's boundary delta for the latest completed ten-minute **team**
period. Anchor all completed periods to root-session start, count zero-use periods, and compute
the **team mean per completed period** as cumulative team usage through the last completed
boundary divided by the number of completed periods. Render count, root start, completed end,
input, cached/noncached input, output and total; keep per-session diagnostics separate. Missing
lineage, history, provenance or completed boundary yields Unknown. Estimate weekly exhaustion
from the earliest and latest compatible used-percent readings across the root session,
bound to the same collector-verified account scope, limit and reset and at least ten
minutes apart. A flat rounded latest ten-minute pair must not suppress an all-session
rise, such as the locally observed 34% at 04:47 to 77% at 16:28 on 2026-09-25.
Label the projection approximate, continuous-use and account-wide; preserve Unknown when
all-session points are flat or incompatible. Never derive it from raw token totals. Native JSONL
counters are distinct from instrumented runner turn IDs and usage plus the matching applied
Host receipt. They do not establish end-to-end telemetry capture or clear GS2-09.9.
The read-only local audit at 2026-09-25 16:11:32 UTC covered this root session plus 29
descendants and 17,938 native `token_count` events, with zero counter decreases or component
mismatches; this is source evidence, not an authenticated Host capture receipt.
The 16:38:26 UTC read-only refresh found the same 30-session root family, 18,719
`token_count` events and zero counter-component or decrease findings. Across 71
completed root-anchored periods, the local diagnostic team mean was 30,194,132.73
total tokens/period; the latest completed period totaled 38,369,280 tokens. Root-session
weekly readings rose from 34% at 04:47 to 78% at 16:38, giving an approximate
continuous-use, account-wide projection of 22:34 UTC on 2026-09-25. This ad hoc
local scan lacks authenticated collector/account provenance and does not establish
instrumented runner turns, an applied Host receipt or capture acceptance.
The 16:49:27 UTC read-only refresh found 30 family sessions, 19,060 native events
and zero component/decrease findings. Across 72 completed root-anchored periods,
the local team mean was 30,340,884.71 total tokens/period; the latest completed
period totaled 40,760,275. The root-session weekly reading reached 79% used;
the conditional continuous-use account-wide projection was about 22:26 UTC.
This local diagnostic still lacks authenticated collector/account provenance and
does not establish end-to-end Host capture.
The 16:58:37 UTC local refresh found 30 family sessions, 19,332 native events and
zero counter findings. Across 73 completed root-anchored periods, the team mean was
30,431,217.37 total tokens/period; the latest period totaled 36,935,169. The root
weekly reading reached 80% used, with a conditional continuous-use, account-wide
projection near 22:16 UTC. This is a read-only diagnostic, not authenticated
collector/account evidence or a runner/Host capture receipt.
At the 17:09:05 UTC checkpoint, the same read-only local scan covered 30 family
sessions and 19,689 native counter events with zero findings. The 74th completed
root-anchored ten-minute period (16:57:05–17:07:05 UTC) totaled 30,836,139
tokens: 30,738,426 input (30,354,560 cached; 383,866 noncached) and 97,713
output. The all-period team mean, including zero-use periods, was 30,436,689.28
total tokens. Fresh root weekly usage was 80% at 17:08:57, up from 34% at
04:47 on the same reset; the conditional continuous-use, account-wide slope
projects near 22:31 UTC. Local JSONL lacks authenticated collector/account
provenance and does not prove the runner/Host capture. Authenticated telemetry
health was ready; the configured `main-fsharp-dev` workspace had pending=0,
pendingUnacknowledged=0 and unacknowledgedLossy=false.
At the 17:18:52 UTC checkpoint, the read-only local scan covered 30 family
sessions and 20,014 native counter events with zero findings. The 75th
root-anchored ten-minute period (17:07:05–17:17:05 UTC) totaled 39,291,172
tokens: 39,199,251 input (38,915,200 cached; 284,051 noncached) and 91,921
output. The all-period team mean, including zero-use periods, was 30,554,749.05
total tokens. Fresh root weekly usage was 81% at 17:18:43, up from 34% at
04:47 on the same reset; the conditional continuous-use, account-wide slope
projects near 22:22 UTC. This local diagnostic lacks authenticated collector
and Host receipt provenance. Authenticated health was ready; the configured
workspace again had pending=0, pendingUnacknowledged=0 and
unacknowledgedLossy=false.
At the 17:29:21 UTC checkpoint, the read-only local scan covered 30 family
sessions and 20,346 native counter events with zero findings. The 76th
root-anchored ten-minute period (17:17:05–17:27:05 UTC) totaled 43,753,754
tokens: 43,668,184 input (43,410,688 cached; 257,496 noncached) and 85,570
output. The all-period team mean, including zero-use periods, was 30,728,420.17
total tokens. Fresh root weekly usage was 82% at 17:29:15, up from 34% at
04:47 on the same reset; the conditional continuous-use, account-wide slope
projects near 22:15 UTC. This local diagnostic lacks authenticated collector
and Host receipt provenance. Authenticated health was ready; the configured
workspace again had pending=0, pendingUnacknowledged=0 and
unacknowledgedLossy=false.
At the 17:39:03 UTC checkpoint, the read-only local scan covered 30 family
sessions and 20,664 native counter events with zero findings. The 77th
root-anchored ten-minute period (17:27:05–17:37:05 UTC) totaled 37,326,702
tokens: 37,243,258 input (36,916,480 cached; 326,778 noncached) and 83,444
output. The all-period team mean, including zero-use periods, was 30,814,112.14
total tokens. Fresh root weekly usage was 83% at 17:38:56, up from 34% at
04:47 on the same reset; the conditional continuous-use, account-wide slope
projects near 22:06 UTC. This local diagnostic lacks authenticated collector
and Host receipt provenance. Authenticated health was ready; the configured
workspace again had pending=0, pendingUnacknowledged=0 and
unacknowledgedLossy=false.
At the 17:49:10 UTC checkpoint, the read-only local scan covered 30 family
sessions and 20,978 native counter events with zero findings. The 78th
root-anchored ten-minute period (17:37:05–17:47:05 UTC) totaled 39,562,179
tokens: 39,482,479 input (39,266,816 cached; 215,663 noncached) and 79,700
output. The all-period team mean, including zero-use periods, was 30,926,266.85
total tokens. Fresh root weekly usage was 84% at 17:49:04, up from 34% at
04:47 on the same reset; the conditional continuous-use, account-wide slope
projects near 21:59 UTC. This local diagnostic lacks authenticated collector
and Host receipt provenance. Authenticated health was ready; the configured
workspace again had pending=0, pendingUnacknowledged=0 and
unacknowledgedLossy=false.
[Renderer draft #3735](https://github.com/FS-GG/.github/pull/3735) is source-only; its
first owner repair adds explicit worker launch evidence, activity-based counts, a worker-only
reserved count, authenticated readiness observations and linked terminal completion rows.
Its follow-up requires exactly one running Sol/high orchestrator with visible-profile
evidence and running workers with explicit spawn evidence; 15 focused tests pass.
Its later freshness repair rejects healthy telemetry observations older than five minutes;
18 focused tests pass. A further review repair requires the post-Host zero queue observation
to carry authenticated, collector-verified evidence; three new refusal cases keep the
18 focused tests green at exact head `06914d60ba47d2ede36af6d48a04babfe883a0e6`.
The renderer revision at exact head `7e7ebdee9b502d20a438ea00916956262e3cf35a` adds typed
authenticated CLI status evidence, separate context occupancy and native-turn period usage.
Its source/test branch passes 21 focused tests, accepts valid UTC reset offsets and binds
native period runner usage to the report workspace; the
monitor-observed values above are not authenticated collector status evidence.
The next source-only renderer revision at exact head
`2b7b9f3dcd55e95cdbac0ffc51ef0860d7041803` adds typed native JSONL cumulative
team counters and root-anchored ten-minute periods. Its 23 focused tests cover a zero-use
period, 28 additional idle descendants, team rather than per-session mean, cached/noncached
arithmetic, byte stability and fail-closed lineage, provenance and counter claims. A
weekly exhaustion estimate derives only from same-reset percentage slope. The external
collector has not been installed or authenticated; no direct-session Host capture follows.
The follow-up source-only head `005c64d6ffadbf38b8748b23208656fc7f6aad90`
requires collector-verified account scope for weekly rates and uses the earliest compatible
same-account, same-limit, same-reset rate point with the latest fresh point. A flat rounded
last-ten-minute pair no longer hides a positive all-session slope; all-session flat or
incompatible points still render Unknown. The estimate says continuous-use and account-wide;
23 focused tests pass, including those refusal cases.
No live update workflow is pinned to that draft.

**Current bounded parallel source evidence.** Reserved direct GS2-09.9
[Coordination #618](https://github.com/FS-GG/FS.GG.Coordination/pull/618) at
`459a260f41c103dea96cfc506a21fe8aed223518` records the independently authenticated
integrated source/artifact, distinct reviewer and installed zero-effect readbacks required
before a protected decision; it is stacked on #617 and carries ten inherited focused controls.
Direct GS2-09.7 [Coordination #619](https://github.com/FS-GG/FS.GG.Coordination/pull/619)
at `5d66e0be94ca1b71c17162319e3a6957ecc6fa52` refuses unescaped delimiter/control
identities that previously let distinct rollback steps share a plan seal; 22 focused controls
pass under local SDK 10.0.401, while pinned 10.0.400 hosted checks are queued.
[Coordination #620](https://github.com/FS-GG/FS.GG.Coordination/pull/620) at
`67b92631f68eb69993c46c4c22ff9e4a7070913d` additionally refuses two rollback domains
sharing one target identity, a red-before map-collapse false green; 23 focused controls pass
under the local SDK, while pinned hosted checks and native target readback remain open.
[Coordination #623](https://github.com/FS-GG/FS.GG.Coordination/pull/623) at
`51d10bc41e166bb18d7505b5fa4da9f64e4a0ece` adds a separately pinned-seal Q6 resume
entry after a red-before validly resealed substitute passed the legacy self-pinned helper.
The accepted GS2-09.6 command bytes are unchanged; 24 focused local-SDK controls pass.
Protected expected-seal provenance, installed native five-domain readback and Q5/Q6 receipt
remain open.
[Coordination #625](https://github.com/FS-GG/FS.GG.Coordination/pull/625) at
`d5808778fa8439fd5aa034ed16f82e71e1f80ddb` checks a complete receipt prefix,
five ordered target/state claims and terminal OperatingV1/plan-seal claim. Twenty-eight
focused local-SDK controls pass, but the claims are fake inputs; protected native observer,
post-restore freshness and exact sandbox/epoch provenance remain absent.
[Coordination #627](https://github.com/FS-GG/FS.GG.Coordination/pull/627) at
`df7190bc807f16068c3d9f2d15c6d46db712fb7f` further binds each claim to its
receipt hash, run nonce, challenge, observer resource and native revision, with a
final-receipt-bound terminal claim; 31 focused local-SDK controls pass. The fields remain
self-asserted until a protected candidate-inaccessible observer and freshness witness exist.
These drafts authorize no protected effect, merge, Authority write, receiver flip or cutover.
Optional FSC-03 [`.github` Rule (b) #3753](https://github.com/FS-GG/.github/pull/3753)
at `194e08a11439ef7663085f3f3947c2f765500884` keeps `*` within one path segment in
the pure supplied-graph reducer; 83 Release controls and a three-case Python comparison pass.
Accepted #3698 and installed parity remain prerequisites. FSC-04
[SDD #1018](https://github.com/FS-GG/FS.GG.SDD/pull/1018) at
`6d12c523bcb02cd1644fe7e4429bd1ed82818a4c` characterizes co-batched authored and
generated writes without rollback; 1,424 tests pass, but producer verification/staging policy
is undecided. Stacked [SDD #1019](https://github.com/FS-GG/FS.GG.SDD/pull/1019) at
`773a91d1c3be1026e86c36300c7adc58b5b61b5d` adds a pure typed preview binding
proposed work-model JSON root, generated-view source, identity, output path and physical
capture to one v2 candidate; 1,429 Commands tests pass. The separate verification wave
and authored-file staging or rollback decision remain required. Independent review found
#1019 accepted duplicate JSON keys and a changed model version with intact source rows;
stacked [SDD #1020](https://github.com/FS-GG/FS.GG.SDD/pull/1020) at
`7e7582a28ecf5d465f24a1ba94161343c6b0b5fa` recursively refuses duplicate/case-aliased
properties and requires exact deterministic regeneration. All 1,435 Commands tests pass;
strict #1020 preview, physical custody and producer effect policy are still needed before
adoption. FSC-05
[Templates #547](https://github.com/FS-GG/FS.GG.Templates/pull/547)
at `cd0753d5f4cc7a20155b24f29eb58c6567442363` finds identical template configs can mask
39 extra members and 33 changed bodies in retained archives; selected/retained parity remains
NO_VERDICT, and local NuGet-shaped matching is payload-only, not served-feed custody.
Stacked [Templates #548](https://github.com/FS-GG/FS.GG.Templates/pull/548) at
`030cf73b572e2c3760beb648b72769b9d84cae0e` moves the pure template-payload comparison
to F# behind a bounded Python physical ZIP reader and refuses config-free, duplicate, aliased,
foreign-field and malformed payload maps; 14 new and 29 stacked controls pass. Its local
payload verdict does not establish selected, served or installed archive custody.
[Coordination GS2-09.9 #621](https://github.com/FS-GG/FS.GG.Coordination/pull/621)
at `cafbb64fb080cee43d924733db1e2883ce7fb1b5` adds a closed fake-port join of
installed refusal and independent zero-effect audit observations; 14 focused and inherited
tests pass. Its injected ports cannot authenticate a protected runner/auditor or prove
transient ABA absence, so it confers no effect authority. An independent review exposed an
unselected positive audit actor false green; stacked
[Coordination #622](https://github.com/FS-GG/FS.GG.Coordination/pull/622) at
`13a5d55b8a0bb976302bfbee70df9a5d1151d771` now requires that actor to match an
explicitly selected ID. Fifteen focused and inherited tests pass, but protected selection
of that actor, runner and artifact remains absent.
[Coordination #624](https://github.com/FS-GG/FS.GG.Coordination/pull/624) at
`d0c2948220b204219a8d17b2c68a4bf420aabc90` adds a read-only fake Git-object
witness binding seven scaffold source files to a selected commit/tree; 18 focused and
inherited tests and a local object smoke check pass. Protected reader identity/object
format, producer artifact and independent reviewer still lack authoritative proof.
[Coordination #626](https://github.com/FS-GG/FS.GG.Coordination/pull/626) at
`b433c86456f6ab241749e6dba58a5bd99dafe928` requires a separate read-only
repository-identity/object-format claim and distinct reader custody for that witness;
19 focused and inherited tests pass. Protected event, reader, artifact and reviewer
authentication remain open, with both workflows disabled and #550 held.
[Coordination #628](https://github.com/FS-GG/FS.GG.Coordination/pull/628) at
`0a28a6e7e12c8e2e0dd2ef686aad9db46ef309b7` binds a fake producer run and
separately read artifact bundle to selected source/archive identity and refuses a
digest-consistent symlink-mode ZIP false green; 23 focused and inherited tests pass.
The reviewed producer workflow is not yet in integrated source, and protected producer,
download and reviewer evidence remain absent. The closed output grants no native effect.
Later disjoint source-only drafts advance without changing those holds:
[Coordination GS2-09.9 #640](https://github.com/FS-GG/FS.GG.Coordination/pull/640)
at `2bf5bf8ef50a7689b4f31f3097514e3dcbca8835` refuses mutable runner/audit credential
scope drift (40 focused tests), while both workflows remain disabled and #545/#550 held;
[Coordination GS2-09.7 #639](https://github.com/FS-GG/FS.GG.Coordination/pull/639)
at `c5ef99dd93f4c543c312ec03d2f42e9c70cc58bb` binds declared receiver-pin raw reads
but explicitly returns partial inventory (107 tests), with #3690 unadmitted and Q5/Q6 held.
[`.github` FSC-03 #3767](https://github.com/FS-GG/.github/pull/3767) at
`f3e5d76cb6281b7092439239f2decf92585ddf2b` refuses a trailing-newline regex
false green in F# Rule (b) (103 tests); Python remains divergent and uninstalled.
[SDD FSC-04 #1023](https://github.com/FS-GG/FS.GG.SDD/pull/1023) at
`da2650c0bf404adfcd46bc2890db63393e30dd23` derives generator identity from the
referenced assembly (1,445 Commands tests), without installed-package proof.
[Templates FSC-05 #558](https://github.com/FS-GG/FS.GG.Templates/pull/558) at
`d62c008c6827283952e9dda8442cd95d53ddde5d` returns NO_VERDICT for the
`Straße`/`Strasse` casefold alias (37 payload and 29 archive controls), with full Unicode,
producer, served-byte, transaction and receiver parity open. None of these drafts grants
protected receipt, merge, native effect or cutover authority.
Subsequent source-only follow-ups preserve the same boundaries:
[Coordination GS2-09.9 #641](https://github.com/FS-GG/FS.GG.Coordination/pull/641)
at `5e8b87a8756adeeeda34afeff8a2cc80a90d86b2` refuses mutable protected
source/approval credential drift (41 focused tests), with both workflows disabled;
[Coordination GS2-09.9 #643](https://github.com/FS-GG/FS.GG.Coordination/pull/643)
at `dadfeb79470321c1cd1457adddeca6abd3e203d7` pins the disabled workflow's
exact SHA-256 and Git blob ID in producer/reviewer joins after forged-source false greens
(43 focused tests), without protected approval or effect;
[Coordination GS2-09.7 #642](https://github.com/FS-GG/FS.GG.Coordination/pull/642)
at `c661b089ff073bd6de241c7de9cbe2f5bf828561` binds a two-read core-settings
digest but marks settings authority incomplete (108 focused tests), with ten other
settings surfaces and Q5/Q6 open.
The next [Coordination GS2-09.7 #644](https://github.com/FS-GG/FS.GG.Coordination/pull/644)
at `b6cacedd1b818ea2f940e64283dde5b218f254a2` adds a final core-settings reread
after custom-property reads to refuse mid-capture drift (109 focused tests). Settings
authority remains explicitly incomplete with nine other surfaces unbound.
[SDD FSC-04 #1024](https://github.com/FS-GG/FS.GG.SDD/pull/1024)
at `901df809e74e473e329d1cc324366b72d88a815d` refuses preview capture when
blocking model diagnostics exist (1,447 Commands tests), while command diagnostics,
staging/rollback and installed parity remain held. [`.github` FSC-03 #3768](https://github.com/FS-GG/.github/pull/3768)
at `a5601c55831606f2eff307271bcbac12e00b1ee6` repairs the Python pure matcher
trailing-newline false green (116 fixtures); it does not assert end-to-end XML filename
coverage or installed F# parity.
[`.github` FSC-03 #3769](https://github.com/FS-GG/.github/pull/3769) at
`f6fcad8e686fcc7d6e8bf2a0eda192c27949b6cf` decodes XML character references
in Python ProjectReference Include after full-gate false greens/false findings (122
fixtures); F# XML provider and installed parity remain open.
[Templates FSC-05 #559](https://github.com/FS-GG/FS.GG.Templates/pull/559)
at `ca78ca177cb73fc2fec80e02717e372700066399` refuses Python Unicode 16.0 full-fold
expansion paths in the F# payload comparator (40 payload and 29 archive controls), with
simple-fold, Unicode-version, producer and receiver parity still open. These are draft
source facts, not protected receipts.
Further parallel source-only evidence: [Coordination GS2-09.9 #647](https://github.com/FS-GG/FS.GG.Coordination/pull/647)
at `6460807b9af2be5e611c551210d2cfd166611e1c` binds producer actor and source
record through reviewer joins (46 tests), with workflows disabled and #545/#550 held;
[Coordination GS2-09.9 #648](https://github.com/FS-GG/FS.GG.Coordination/pull/648)
at `dc8348369b230a6a18bad0e1122ebba98e1c15aa` also binds the checked
repository ID to the reviewer join after a foreign-repository witness false green
(47 tests), with the same protected holds;
[Coordination GS2-09.7 #646](https://github.com/FS-GG/FS.GG.Coordination/pull/646)
at `926e110f8daf37bb3ee0fbbe054e7648fc800b1f` adds partial Actions-policy
readback bracketed by core reread (110 tests), with Q5/Q6 and settings closure held.
[SDD FSC-04 #1025](https://github.com/FS-GG/FS.GG.SDD/pull/1025) at
`2cee1a8a71f75751bd878be72b8c8eee0bfec051` characterizes duplicate-work-ID
diagnostics outside the selected-source preview (1,448 Commands tests); a complete
pinned work inventory and effect decision remain absent. [`.github` FSC-03 #3771](https://github.com/FS-GG/.github/pull/3771)
at `629a5309daf027cd42cad6021dde77f14eb8ee9f` makes the pure F# XML adapter
refuse MSBuild Include expansion without an evaluated provider (117 tests); Python
and installed parity remain open. [Templates FSC-05 #560](https://github.com/FS-GG/FS.GG.Templates/pull/560)
at `4088d8367bbb253df0b2f76c9ae52ba4591652d1` labels the Python-16/.NET
NFKC disagreement at U+A7F1 as Unicode drift NO_VERDICT (41 payload, 29 archive
controls), without widening acceptance or receiver authority.
The next disjoint drafts remain provisional:
[Coordination GS2-09.9 #650](https://github.com/FS-GG/FS.GG.Coordination/pull/650)
at `bf5fbaf766630af0153df8b61d38812291e5fb38` binds repository-identity
event ID through the producer and immutable reviewer event (48 focused tests),
with both workflows disabled and #545/#550 held;
[Coordination GS2-09.7 #649](https://github.com/FS-GG/FS.GG.Coordination/pull/649)
at `29ce3293d3f605c96b1add2de885e1f610da21ac` adds partial repository
branch/tag ruleset readback (111 tests), with `SettingsAuthorityComplete=false` and
Q5/Q6 held. [`.github` FSC-03 #3773](https://github.com/FS-GG/.github/pull/3773)
at `b912d98fdc788c12485045bfd5917fcc67672730` returns no verdict for Python
ProjectReference Include forms requiring MSBuild item evaluation (126 fixtures),
without installed F# parity. [Templates FSC-05 #561](https://github.com/FS-GG/FS.GG.Templates/pull/561)
at `5877040fb77560468aa520455b5858c70187531e` turns malformed UTF-8 ZIP
member names into NO_VERDICT (42 payload, 29 archive controls), with served-byte,
transaction and receiver holds unchanged.
Later draft source evidence remains distinct from protected acceptance:
[Coordination GS2-09.9 #652](https://github.com/FS-GG/FS.GG.Coordination/pull/652)
at `4c4615f6e4761e39051d1f94ea867a9ff71b14b3` requires the immutable
reviewer witness before fake installed no-grant runner/audit readback (50 tests),
with both workflows disabled and #545/#550 held;
[Coordination GS2-09.7 #651](https://github.com/FS-GG/FS.GG.Coordination/pull/651)
at `b1b119f2b57a954e2e7a7c78792bb4b90e3e134d` adds partial workflow-permissions
readback (113 tests), with settings authority incomplete and Q5/Q6 held.
[SDD FSC-04 #1026](https://github.com/FS-GG/FS.GG.SDD/pull/1026) at
`cf92c80f45580f6ed818a5a058417b8fa6a0cb94` checks separately supplied
work-candidate rows but cannot prove physical inventory completeness (1,453
Commands tests). [`.github` FSC-03 #3774](https://github.com/FS-GG/.github/pull/3774)
at `286a2e4aa836002313f316d953cc10b7efd86e5e` makes the Python graph gate
refuse unevaluated MSBuild Import (127 fixtures), with installed parity pending.
[Templates FSC-05 #562](https://github.com/FS-GG/FS.GG.Templates/pull/562)
at `4bc07cac34d69fad7f97145c2c48e363c7982937` makes the Python archive reader
return NO_VERDICT on an unsafe ZIP member outside template paths (43 payload, 29
archive tests), without producer, served-byte or receiver authority.
Further source-only follow-ups: [Coordination GS2-09.9 #653](https://github.com/FS-GG/FS.GG.Coordination/pull/653)
at `91b4d339c1c3195a74f1b8f93795fdb3c1fb5953` refuses installed probe
before approval or after expiry (51 fake-port tests), still lacking authenticated
protected timestamps; [Coordination GS2-09.7 #654](https://github.com/FS-GG/FS.GG.Coordination/pull/654)
at `a78e29240b551378189119ea5d3b29ff7e636aa2` brackets partial Actions and
workflow readback (114 tests), without atomicity or Q5/Q6 acceptance.
[SDD FSC-04 #1027](https://github.com/FS-GG/FS.GG.SDD/pull/1027) at
`bbb2ba2e3d0ce802ce413a8bdf5ff3a0ec713c33` proves a supplied candidate
inventory can omit a physical duplicate work ID (1,456 Commands tests); complete
discovery remains open. [`.github` FSC-03 #3776](https://github.com/FS-GG/.github/pull/3776)
at `02d82b95aafa228c3d52d81a261ebedc66e1340e` refuses a non-Project XML
root in Python graph extraction (129 fixtures), still uninstalled.
[Templates FSC-05 #563](https://github.com/FS-GG/FS.GG.Templates/pull/563) at
`22cc8438eaf3f06f44a73c6ad52cbc985a61dde5` returns NO_VERDICT for a
non-template ZIP symlink (44 payload, 29 archive controls), without producer,
served-byte, transaction or receiver authority.
The next direct and Templates source-only drafts retain those limits:
[Coordination GS2-09.9 #655](https://github.com/FS-GG/FS.GG.Coordination/pull/655)
at `9b5cced3e2dd35f79153f086c9e0bca866b3f1ea` requires claimed no-grant
command start/completion times inside the approval/audit window (52 fake-port tests);
those timestamps are not independently authenticated and #545/#550 remain held.
[Templates FSC-05 #564](https://github.com/FS-GG/FS.GG.Templates/pull/564)
at `db3c80c5167deb28e093b6e9dffc774f1a18248c` consumes every ZIP member
under bounds so a corrupt non-template member returns NO_VERDICT (45 payload, 29
archive controls), without producer, served-byte, transaction or receiver proof.
[`.github` FSC-03 #3777](https://github.com/FS-GG/.github/pull/3777)
at `3e91858795e432995292395f5938bd541cab5528` refuses absolute Windows
and repository-escaping `ProjectReference` targets in Python graph extraction
(131 fixtures); the F# adapter and installed parity remain separate.
[Coordination GS2-09.7 #656](https://github.com/FS-GG/FS.GG.Coordination/pull/656)
at `8546ca6500004c0ffb59c7d910d8fdf8954e95bb` adds private-fork workflow
readback to a partial Q6 rollback bridge (116 focused tests), while settings
authority, Q5/Q6 receipts, and protected native effects remain held.
[Coordination GS2-09.9 #657](https://github.com/FS-GG/FS.GG.Coordination/pull/657)
at `dccb70b675b7f024ffe63a2080c78b55c13942db` requires the separate
audit to carry the same command interval as the runner probe (53 fake-port
tests); matching supplied data does not authenticate a protected native run.
Further bounded drafts preserve their parent gates:
[Coordination GS2-09.9 #658](https://github.com/FS-GG/FS.GG.Coordination/pull/658)
at `03e87db743590634d007d184ed3dbe1b84df63bd` refuses a runner actor
equal to the release reviewer before probe reads (54 fake-port tests); actor
claims are still unauthenticated and #545/#550 remain held.
[Coordination GS2-09.9 #659](https://github.com/FS-GG/FS.GG.Coordination/pull/659)
at `a526afb91eb9ed09c74a5c8815b2d37a09a37eeb` snapshots a mutable
effective-scope metadata read to prevent a reused object from hiding credential
drift (19 focused tests); this remains fake-port source evidence.
[SDD FSC-04 #1028](https://github.com/FS-GG/FS.GG.SDD/pull/1028)
at `8952bae1acac51eca771c1a8b273bd821b73c03f` characterizes a late
duplicate candidate inserted into a previously visited directory (1,458
Commands tests); one physical traversal is not a complete stable inventory.
[Templates FSC-05 #565](https://github.com/FS-GG/FS.GG.Templates/pull/565)
at `882ba0ab2ff60d51bd220b8017f959593696acf0` refuses trailing ZIP bytes
(46 payload, 29 archive controls), and [#566](https://github.com/FS-GG/FS.GG.Templates/pull/566)
at `a3bfd59de6934656a72e2c16e91b8da35041e134` refuses a leading overlay
(47 payload, 29 archive controls); neither proves full ZIP closure or producer,
served-byte, #511 CAS, installed or receiver authority.
[`.github` FSC-03 F# #3778](https://github.com/FS-GG/.github/pull/3778)
at `a8801357fa42e2cd29c65601f1eee069d8aad043` refuses explicit target-time
`ProjectReference` changes (123 tests); [Python #3779](https://github.com/FS-GG/.github/pull/3779)
at `f7da149a7b2d4db227db5e8931dfe2613e5a61b4` refuses the matching
dynamic Include/Remove shapes (134 fixtures). Task outputs, implicit imports,
installed parity, and receiver admission remain separate.
[`.github` telemetry source #3780](https://github.com/FS-GG/.github/pull/3780)
at `e58b5483fc51b04852c1d10e77272cecf6ec1f6e` refuses an unreadable
field in the dormant exact Project 1 map. A live read-only schema probe found
24 fields including known built-in kinds omitted from the writable map, so the
first overstrict draft was corrected to permit those explicit kinds while
refusing unknown, missing and unsupported writable kinds (853 GitHub adapter
tests, with red-before partial-map and built-in controls). The runner still enumerates all Projects;
no genuine instrumented item or matching applied Host receipt is established.
The following source-only drafts do not change those holds:
[Coordination GS2-09.9 #661](https://github.com/FS-GG/FS.GG.Coordination/pull/661)
at `fbffb34eb449ba69c473466df06e7b8477ac43c1` snapshots the mint-reader
scope after a mutable fake-port credential-drift false green (19 focused tests).
[Coordination GS2-09.7 #660](https://github.com/FS-GG/FS.GG.Coordination/pull/660)
at `e3cc09b6b34c698a3517b347d375ab5fa7ea5007` refuses duplicate native
issue/PR node IDs in a partial Q5 census (39 focused tests); journal/adapter and
Q5/Q6 acceptance remain open.
[Templates FSC-05 #567](https://github.com/FS-GG/FS.GG.Templates/pull/567)
at `e54e992cc790b670f4e26d6591b0cfdf9f78d927` refuses mismatched local
and central ZIP flags/methods (48 payload, 29 archive controls), without full
ZIP closure or producer/served/receiver proof.
[`.github` FSC-03 Python #3781](https://github.com/FS-GG/.github/pull/3781)
at `e28648547a8a2b00c4cf89ce9cb995c5f7cfa91f` refuses explicit MSBuild
task output into `ProjectReference` (136 fixtures); matching F# handling,
dynamic item names, implicit imports and installed parity remain open.
[Coordination GS2-09.9 #663](https://github.com/FS-GG/FS.GG.Coordination/pull/663)
at `021a1275a4631ced671c047979714779920d526e` snapshots a mutable
operation-plan reader scope after a fake credential-drift false green (15 tests);
protected plan/seal custody and native admission remain held.
[Coordination GS2-09.7 #662](https://github.com/FS-GG/FS.GG.Coordination/pull/662)
at `9c839d0aeda2a30b3656ec33847eb2cd467f25bf` refuses a census node ID
reused as an activity ID (40 focused tests); journal/adapter and Q5/Q6 remain
unaccepted. [`.github` FSC-03 F# #3782](https://github.com/FS-GG/.github/pull/3782)
at `7122041148d1f99395f1220d5b5075b5bde75fbf` refuses task output into
`ProjectReference` (126 tests), matching source-only Python #3781. Dynamic item
names, implicit imports and installed parity remain separate.
The 17:12 UTC read-only same-credential GraphQL probe returned exact Project 1
`PVT_kwDOEYAWY84Bb08W` / number 1 / Coordination, while Project 2 returned
`FORBIDDEN Resource not accessible by personal access token`. This confirms
the installed all-project runner's access blocker; it is not an instrumented
work item, native-turn attribution, applied Host receipt or queue-return proof.
[Coordination GS2-09.9 #664](https://github.com/FS-GG/FS.GG.Coordination/pull/664)
at `e6d083718a2e894482d037fb51906d4d3a70a05e` snapshots a mutable
prestate-reader scope after a fake credential-drift false green (14 tests),
without native permission or effect authority.
[SDD FSC-04 #1029](https://github.com/FS-GG/FS.GG.SDD/pull/1029)
at `6a835e74169174b9de229ec824955e83dbabcb41` repeats complete read-only
Linux `work/` capture to catch a persistent late duplicate candidate (1,463
Commands tests); matching passes still cannot prove atomicity, ABA or cross-root
inventory completeness.
[Templates FSC-05 #568](https://github.com/FS-GG/FS.GG.Templates/pull/568)
at `57bca74b4175a70bb4719f3a0fd1f42a3c4551df` refuses local/central
ZIP CRC and size mismatches without a data descriptor (49 payload, 29 archive
controls); descriptor-form, complete closure and producer/receiver custody remain.
More source-only drafts retain the same gates:
[Coordination GS2-09.9 #665](https://github.com/FS-GG/FS.GG.Coordination/pull/665)
at `bd1b482f92e6b4f39a6058cad22e2a854c410e3e` snapshots a mutable
review-audit scope (11 tests), and [#667](https://github.com/FS-GG/FS.GG.Coordination/pull/667)
at `9738b4674067fa06e72b941bccb70ab7f4622fb7` snapshots a mutable
source-release scope (16 tests); fake claims do not establish protected custody.
[Coordination GS2-09.9 #668](https://github.com/FS-GG/FS.GG.Coordination/pull/668)
at `bc721eafc8dd6861d4fbc7d7f9204e6e17b0f70d` snapshots a mutable
workflow-reader scope after a false green (11 fake-port tests); protected
workflow custody and installed native effects remain held.
[Coordination GS2-09.7 #666](https://github.com/FS-GG/FS.GG.Coordination/pull/666)
at `e31378905d22145aa6f0825ef5e6165af739f692` refuses foreign-repository
native activity pages (41 focused tests), with initial census admission,
journal/custom receipts, raw parsing and Q5/Q6 still open.
[`.github` FSC-03 Python #3783](https://github.com/FS-GG/.github/pull/3783)
at `cc7989dd92003be7c3e9d5c880c2b76e97b81380` refuses dynamic MSBuild
task output item names (137 fixtures); [F# #3784](https://github.com/FS-GG/.github/pull/3784)
at `c42697d0cf8f841fe0b717e734b52251788d6810` matches that refusal
(128 tests). The installed MSBuild probe characterizes expansion, without
installed receiver parity.
[Templates FSC-05 #569](https://github.com/FS-GG/FS.GG.Templates/pull/569)
at `3e7d7ecc3167eb263ee9a53c42c13a55b60dad4d` refuses ZIP members with
a data-descriptor flag (50 payload, 29 archive controls); descriptor support,
full closure and producer/receiver custody remain unproven.
[Coordination GS2-09.9 #670](https://github.com/FS-GG/FS.GG.Coordination/pull/670)
at `87dacd0c223fae47818d302236b821be9c93663b` snapshots a mutable
review-reader scope (12 fake-port tests); protected review custody remains held.
[Coordination GS2-09.7 #669](https://github.com/FS-GG/FS.GG.Coordination/pull/669)
at `79044871d7b43ff9e7d063aae1f8b82dad519812` refuses undersized or
misnumbered native activity pages (42 focused tests); initial census,
raw-to-typed adapter, journal/custom receipts and Q5/Q6 are still open.
[`.github` FSC-03 Python #3785](https://github.com/FS-GG/.github/pull/3785)
at `fb0b135288c8d66076c7645855c727f4976171b9` refuses a top-level
`ProjectReference Remove` that changes the evaluated graph (138 fixtures),
after an installed MSBuild scratch probe. Matching F# source handling and
installed parity remain open; the implicit Directory.Build import requires a
broader provider decision, because a blanket import refusal blocks this tree.
[Coordination GS2-09.9 #671](https://github.com/FS-GG/FS.GG.Coordination/pull/671)
at `e03323d2accca14833095aa38ed6f98da7ee7eb6` snapshots a mutable
target-reader scope (13 fake-port tests); caller-owned selected target aliasing
and protected native custody remain separate.
[`.github` FSC-03 F# #3786](https://github.com/FS-GG/.github/pull/3786)
at `b114db48d1c2220b12511a4950ada6415c8cd00d` refuses a top-level
`ProjectReference Remove` (131 tests), matching Python #3785; implicit imports
and installed parity remain open.
[SDD FSC-04 #1030](https://github.com/FS-GG/FS.GG.SDD/pull/1030)
at `7b7bd46354ac6c2d2781ac0e069bd1c4f1bd3dcc` retains child directory
handles and rechecks rosters after a transient late candidate escaped two
passes (1,466 Commands tests). ABA, post-check changes, cross-root atomicity
and producer/receiver effects remain held.
[Templates FSC-05 #570](https://github.com/FS-GG/FS.GG.Templates/pull/570)
at `7c0bcde8a253116dcc5e113a83fab0ba156cfdb0` refuses unreviewed ZIP
extra fields (51 payload, 29 archive controls); full closure and producer,
served-byte, #511 CAS and receiver custody remain unproven.
[Coordination GS2-09.9 #673](https://github.com/FS-GG/FS.GG.Coordination/pull/673)
at `1bf0a5f8f2d8abd97bd063863b806b6566a59591` copies a validated
caller-owned selected target before fake attestation, preventing later digest
mutation from qualifying (21 tests); native protected custody stays held.
[Coordination GS2-09.7 #672](https://github.com/FS-GG/FS.GG.Coordination/pull/672)
at `ca40a0c058230a9f96cdaefe3617ba422b3ef77d` refuses duplicate native
database IDs in typed activity records (43 tests); raw parsing, protected
journal/custom receipts and Q5/Q6 remain open.
[Coordination GS2-09.9 #674](https://github.com/FS-GG/FS.GG.Coordination/pull/674)
at `dbf749c006d2180791fd22a4c497ae304fdf890a` freezes a validated
workflow observation so caller mutation cannot turn dispatch into self-review
(18 fake-port tests); protected identities and native custody remain held.
[`.github` FSC-03 Python #3787](https://github.com/FS-GG/.github/pull/3787)
at `9eddb84a5c1270069dd73967541e7f85285bf87f` refuses direct
`ProjectReference` edges in the nearest in-repository `Directory.Build.props`
or `.targets` (142 fixtures), after an installed MSBuild scratch probe.
Transitive/overridden imports and installed F# receiver parity need evaluated
provider facts; the shipped root props remains readable.
[Templates FSC-05 #571](https://github.com/FS-GG/FS.GG.Templates/pull/571)
at `5f79c3451525dc10f2d0fd6ec83f218ea210c57e` refuses case-folded
file/child ancestor collisions across all ZIP members (52 payload, 29 archive
controls); producer/served-byte, #511 CAS, installed/receiver and full ZIP
closure remain held.
[Coordination GS2-09.9 #676](https://github.com/FS-GG/FS.GG.Coordination/pull/676)
at `64dc003ce1370d8d330110eaa3c7584ce80fc8e7` snapshots each fake
observation before the next port can mutate its source actor or artifact bytes
(23 tests); installed provider/native effects remain held.
[Coordination GS2-09.7 #677](https://github.com/FS-GG/FS.GG.Coordination/pull/677)
at `fa7af8513b2cfc5018b1561ce1fd3cec604c0b3f` refuses duplicate JSON
members in raw issue classification (133 focused tests), without initial
cohort census, full raw-to-typed adapter or Q5/Q6 acceptance.
[SDD FSC-04 #1031](https://github.com/FS-GG/FS.GG.SDD/pull/1031)
at `2a2a1e1aa97698560553927ef427f50914f2e518` caps one Linux pinned
capture at 256 retained child directory handles (1,469 Commands tests), with
ABA, post-check, cross-root and installed effects held.
[Templates FSC-05 #572](https://github.com/FS-GG/FS.GG.Templates/pull/572)
at `8a8094d503073f4489e27699709c46e3ad0f0937` refuses decomposed or
compatibility-form member names outside the template payload (54 payload, 29
archive controls); complete ZIP closure and producer/receiver custody remain.
[`.github` FSC-03 F# #3788](https://github.com/FS-GG/.github/pull/3788)
at `beabcd4db20b544d48568eae5f899ada93ab7ff7` adds a distinct local
observation for supplied `Directory.Build` XML and refuses direct references,
imports and relevant task outputs (140 tests). It does not authenticate nearest
file selection, transitive imports or installed receiver parity.
[Coordination GS2-09.9 #678](https://github.com/FS-GG/FS.GG.Coordination/pull/678)
at `3a5ba5d2cd6bddeee0557c84a0901a19c7474c64` snapshots an integrated
source result before a later approval port can replace its archive bytes (24
fake-port tests); [#679](https://github.com/FS-GG/FS.GG.Coordination/pull/679)
at `c09782e6eb9268dfddfd90fb98c06f7efea6e1d4` snapshots earlier producer
and artifact results before later reads can rewrite actor or digest (27 tests).
Neither supplies protected native custody or clears #545/#550.
[Coordination GS2-09.7 #680](https://github.com/FS-GG/FS.GG.Coordination/pull/680)
at `81dd56f7d59a68b4120b8cc5c207a6f4a5723b81` refuses duplicate raw
PR identity/revision JSON members (134 focused tests), while initial census,
full raw-to-typed inspection, journal/custom receipts and Q5/Q6 remain held.
[`.github` FSC-03 Python #3789](https://github.com/FS-GG/.github/pull/3789)
at `a2b1398f4d75a4d443d21c7939a195b6de156b7a` recognizes case-varied
MSBuild `ProjectReference` item names across direct, Remove, target-time and
implicit-file paths (146 fixtures). Matching F# main graph handling, evaluated
imports and installed parity remain open.
[Templates FSC-05 #573](https://github.com/FS-GG/FS.GG.Templates/pull/573)
at `3827c9ebfda7feef59058d326b7b3fa4a8f9a84a` refuses reserved
punctuation and ASCII controls in any ZIP member name (56 payload, 29 archive
controls); complete ZIP closure and producer/receiver authority remain open.
Read-only source-branch CLI integration for [`.github` #3780](https://github.com/FS-GG/.github/pull/3780)
at `e58b5483fc51b04852c1d10e77272cecf6ec1f6e` used the current credential
and `exact-project1` bootstrap mode to return pinned Project 1
`PVT_kwDOEYAWY84Bb08W`, owner FS-GG, title Coordination, number 1 and 12
editable fields from the live 24-field schema. This was not the installed
orchestration runner and produced no native turn or applied Host receipt.
[Coordination GS2-09.9 #681](https://github.com/FS-GG/FS.GG.Coordination/pull/681)
at `a03ad4ee57e33543860f5db63305741783f7fa52` snapshots an inactive
reviewer before a later event read can make it active (34 fake-port tests);
[#683](https://github.com/FS-GG/FS.GG.Coordination/pull/683)
at `3e737227adab498106140b6f67b293de79c5c6e9` snapshots a prestate
transcript before metadata read can replace its hash (33 tests). Protected
actor and native-effect custody remain held.
[Coordination GS2-09.7 #682](https://github.com/FS-GG/FS.GG.Coordination/pull/682)
at `87c66eb0bc1183782558277116ad418fe5ae1974` refuses duplicate raw PR
review state, commit and actor JSON members (135 focused tests); initial census,
full typed inspect, journal/custom receipts and Q5/Q6 are still open.
[`.github` FSC-03 F# #3790](https://github.com/FS-GG/.github/pull/3790)
at `175398359ba9bbe92b6d7f66bef05ab16e81a620` matches the Python
case-varied `ProjectReference` source handling across direct, Remove and
target-time shapes (144 tests). Implicit-file provenance, Import closure,
evaluated graph and installed parity remain held.
[SDD FSC-04 #1032](https://github.com/FS-GG/FS.GG.SDD/pull/1032)
at `d2b509adc253583b1c355136833721bdef79f638` caps one pinned file at
32 MiB on the Linux read paths (1,472 Commands tests); aggregate bytes,
peak memory, ABA and cross-root atomicity remain unbounded/unproved.
[Templates FSC-05 #574](https://github.com/FS-GG/FS.GG.Templates/pull/574)
at `cb460fc9c1a4185f7da412ee6f972deb85c14bd1` refuses dot-ended or
ASCII-space-ended ZIP path segments (58 payload, 29 archive controls), with
full closure, producer/served-byte, #511 CAS and receiver proof open.
[Coordination GS2-09.9 #684](https://github.com/FS-GG/FS.GG.Coordination/pull/684)
at `cfffc8b8d3c495646b2ce984b58297307970b7b5` snapshots the probe
before a later audit callback can replace a foreign no-grant response (38
fake-port tests); protected probe/audit provenance and #545/#550 remain held.
[Coordination GS2-09.7 #685](https://github.com/FS-GG/FS.GG.Coordination/pull/685)
at `c147efb973729eccfa4f8353ca450707543888f0` refuses duplicate raw
issue-event fields (136 focused tests); initial cohort census, full typed
inspect, protected journal/custom receipts and Q5/Q6 remain open.
[`.github` FSC-03 Python #3791](https://github.com/FS-GG/.github/pull/3791)
at `6ff69a76c8d55aafbc64e98e9d235cd5db85cb7a` refuses literal or
dynamic task outputs in nearest supplied `Directory.Build` files that can emit
`ProjectReference` (148 fixtures). Import closure, nearest-file provenance,
evaluated graph and installed parity remain open.
[Templates FSC-05 #575](https://github.com/FS-GG/FS.GG.Templates/pull/575)
at `9a102feeddf679acc08a96a82ed9721d6dbccfda` refuses reserved device
stems in any ZIP member path (61 payload, 29 archive controls); descriptor
support, full closure and producer/served/receiver custody remain open.
[Coordination GS2-09.9 #686](https://github.com/FS-GG/FS.GG.Coordination/pull/686)
at `bd35be4b914ef6b831fbd11ad760c7de74761b90` snapshots caller source
bytes before an identity callback can restore a foreign archive (35 fake-port
tests); [#688](https://github.com/FS-GG/FS.GG.Coordination/pull/688)
at `748f7f7338bb013c02d6b6e63c5bfa0e355cd680` copies selected workflow
digest before a Git callback can replace it (28 tests). Both remain without
protected source/identity custody or native-effect authority.
[Coordination GS2-09.7 #687](https://github.com/FS-GG/FS.GG.Coordination/pull/687)
at `4429006ba0009962ee89f7f77ac68bf6912e48d0` refuses duplicate raw
comment subject, body and actor members (137 focused tests); initial census,
full typed inspect, journal/custom receipts and Q5/Q6 remain open.
[`.github` FSC-03 Python #3792](https://github.com/FS-GG/.github/pull/3792)
at `c389e65f90f15bd7e2f2c7aae677005b007b8029` refuses project or
selected implicit `DirectoryBuildTargetsPath` overrides after installed
MSBuild exposed hidden references (150 fixtures). Matching F# refusal,
PropsPath/import switches, provider provenance and installed parity remain.
[SDD FSC-04 #1033](https://github.com/FS-GG/FS.GG.SDD/pull/1033)
at `c3d482fc331b05c160b3098fa874139e2db9d5b0` caps retained raw
complete-root payload at a provisional 64 MiB (1,474 Commands tests); file
count, repeated-copy peak memory, ABA and cross-root atomicity remain open.
[Templates FSC-05 #576](https://github.com/FS-GG/FS.GG.Templates/pull/576)
at `245e0ffbe955557cc63952ad74d2b4624846d5ea` applies a 255 UTF-8-byte
per-segment bound to all ZIP member names (64 payload, 29 archive controls);
full closure and producer/served-byte/receiver custody remain held.
[Coordination GS2-09.9 #690](https://github.com/FS-GG/FS.GG.Coordination/pull/690)
at `2ef694b69a7128b112dd4868fb901db4079c75c1` copies the selected
identity event before a scope callback can change the closed producer result
(34 fake-port tests); protected actor/source custody and #545/#550 remain held.
[Coordination GS2-09.7 #689](https://github.com/FS-GG/FS.GG.Coordination/pull/689)
at `79048c55f2c32584665da27b0b49d2959ea8b3cf` refuses duplicate raw
inline review-comment fields (138 focused tests); initial census, full typed
inspect, protected journal/custom receipts and Q5/Q6 remain open.
[`.github` FSC-03 F# #3793](https://github.com/FS-GG/.github/pull/3793)
at `54225a2bc4dde89b97f15f6ac4c0d3ac3efe05e9` matches the Python
`DirectoryBuildTargetsPath` override refusal for project and supplied
implicit XML (147 tests); nearest provenance, Import closure, evaluated graph
and installed receiver parity remain held.
[Coordination GS2-09.9 #691](https://github.com/FS-GG/FS.GG.Coordination/pull/691)
at `9f5a8741eb0afcf14d8460bd0cd5148061b45591` copies selected identity
event before a final scope callback can change the closed approval result
(42 fake-port tests); protected identity/native effects remain held.
[`.github` FSC-03 Python #3794](https://github.com/FS-GG/.github/pull/3794)
at `3477f3e83391bd7f4dba756190f1519f25b210ad` refuses a nearest
`Directory.Build.props` or `.targets` source above the supplied root after
installed MSBuild exposed an uncovered reference (153 fixtures); external
contents are not trusted, and symlink/global override/import closure remains.
[Templates FSC-05 #577](https://github.com/FS-GG/FS.GG.Templates/pull/577)
at `77d1ef9219552f2bd440eb1891a6164ed7822173` refuses full case-fold
expansions in any ZIP member name (67 payload, 29 archive controls), with
descriptor/full closure and producer/served/receiver proof open.
[Coordination GS2-09.7 #692](https://github.com/FS-GG/FS.GG.Coordination/pull/692)
at `1be3038da7d9131cbc1ffd1d2b3902ae2e12f93a` refuses duplicate raw
initial and continuation relation JSON members (139 focused tests); initial
cohort census, full typed inspect, journal/custom receipts and Q5/Q6 remain.
[Coordination GS2-09.9 #693](https://github.com/FS-GG/FS.GG.Coordination/pull/693)
at `cae277cad4bf1bcc58851921549c3812525726c5` copies selected audit
actor before a final scope callback can change the closed readback result
(45 fake-port tests); protected audit/native-effect custody remains held.
[SDD FSC-04 #1034](https://github.com/FS-GG/FS.GG.SDD/pull/1034)
at `03c196a8f90a0c1a68c65a3d196aebe433a25b1b` caps provisional Linux
pinned complete-root capture at 4,096 files (1,477 Commands tests). ABA,
post-check, cross-root and Windows/effect parity remain open.
[Templates FSC-05 #578](https://github.com/FS-GG/FS.GG.Templates/pull/578)
at `9e29cd0573d3cde787a20e9cb44b190a2d9bc4b1` refuses ZIP readback when
the runtime Unicode data version differs from the F# comparator's pinned
16.0.0 data; 68 payload and 29 archive controls pass. Selected native versus
retained release remains NO_VERDICT; producer/served-byte, #511 CAS,
installed parity and receiver adoption remain held.
[Coordination GS2-09.9 #694](https://github.com/FS-GG/FS.GG.Coordination/pull/694)
at `95e53a9ec72d12514730c42eff6ff99b453ca4f7` copies selected identity
event before a final scope callback can alter a Git-tree witness (38 focused
fake-port tests); installed native effects and protected identity custody
remain held.
[`.github` FSC-03 Python #3795](https://github.com/FS-GG/.github/pull/3795)
at `475dc96c41ac8a5dbc4eecffc7cc60f428c98709` refuses external
symlinked nearest `Directory.Build.props` and `.targets` sources while
accepting an in-root symlink (156 fixtures). Installed MSBuild can read the
external target through the lexical in-root link; project-file provenance,
Import closure, evaluated graph and installed parity remain open.
[Coordination GS2-09.7 #695](https://github.com/FS-GG/FS.GG.Coordination/pull/695)
at `7f36e810a40cbd340f7e262dbae7437fe17a9d8f` refuses duplicate raw
issue-type name and pagination members before typed interpretation (140
focused tests). Initial census, full typed inspect, protected journal/custom
receipts and Q5/Q6 remain held.
[Coordination GS2-09.7 #696](https://github.com/FS-GG/FS.GG.Coordination/pull/696)
at `3afbe206ba6eb6a602759ce22431712f463381b6` refuses duplicate raw
project-item content type, repository ID and pagination members (141 focused
tests). The same initial census, journal/custom receipt and Q5/Q6 holds apply.
[`.github` FSC-03 Python #3796](https://github.com/FS-GG/.github/pull/3796)
at `2572e39ea2430ee869ca02595cc830fec00e4414` refuses a project XML
symlink that resolves outside the supplied root (158 fixtures), while an
in-root symlink remains valid. Installed MSBuild read the external source
through the lexical in-root path; F# provider authentication, Import closure,
evaluated graph and receiver parity remain held.
[`.github` exact-board capture #3797](https://github.com/FS-GG/.github/pull/3797)
at `741a4a3f679d4db0c7fa4fd8eeba1f058af694a4`, stacked on #3780,
recursively refuses duplicate raw JSON members in the dormant direct
Project 1 response. Five red-before identity/count/type/option cases had
returned `Ok`; the Release GitHub adapter suite now passes 858/858. The
installed all-project runner and genuine item/native turn/applied Host
receipt/queue-return proof remain absent.

The subsequent source-only drafts advance independent prerequisites without
changing the protected holds:

| Workstream / draft | Exact PR head | Bounded source evidence |
| --- | --- | --- |
| [GS2-09.9 #697](https://github.com/FS-GG/FS.GG.Coordination/pull/697) | `73d16c27fad9d8c2b396d786405de52dc1324831` | Copies selected SHA and target before read/reservation callbacks; 71 fake-port tests. |
| [GS2-09.7 #698](https://github.com/FS-GG/FS.GG.Coordination/pull/698) | `959c518a6fc3844ea52f34ea9206aef89a568bb9` | Refuses duplicate raw project-field/type/pagination members; 142 focused tests. |
| [GS2-09.7 #699](https://github.com/FS-GG/FS.GG.Coordination/pull/699) | `f7715c1a93aa194f05f29fe666e30bdccba93e57` | Refuses duplicate raw project-value/option/pagination members; 143 tests. |
| [GS2-09.9 #700](https://github.com/FS-GG/FS.GG.Coordination/pull/700) | `62e1c404f501378113d41cafe8605a31d3a5b7f2` | Freezes native reader selection before transport callbacks; 79 fake-port tests. |
| [GS2-09.7 #701](https://github.com/FS-GG/FS.GG.Coordination/pull/701) | `69c4cabe79e3a1028f8f559f9adf53f3ed0ebc38` | Refuses duplicate raw repository identity/settings members; 144 focused tests. |
| [GS2-09.9 #702](https://github.com/FS-GG/FS.GG.Coordination/pull/702) | `20d50cbac2037f7ea7171ba3d9d3e307fb9c4b81` | Refuses a present foreign REST URL despite matching selected repository ID/name; 80 fake-port tests. |
| [FSC-03 #3798](https://github.com/FS-GG/.github/pull/3798) | `28673f19da4357e92718d4874beda409e6704574` | Discovers case-varied project extensions that MSBuild loads; 161 fixtures. |
| [FSC-03 #3799](https://github.com/FS-GG/.github/pull/3799) | `c3588047db13b4e9f6ce5b84ab116738f062452f` | Refuses an existing referenced project outside the discovered roster; 163 fixtures. |
| [FSC-03 #3800](https://github.com/FS-GG/.github/pull/3800) | `c8a772e742cbbc1539d891473c64d68e36f305d5` | Refuses a missing referenced project and completes two historical workflow inventories; 164 fixtures. |
| [FSC-04 #1035](https://github.com/FS-GG/FS.GG.SDD/pull/1035) | `4ca06d9a6ed535110883b0ac3986c353290d841c` | Caps provisional pinned relative paths at 1,024 UTF-16 code units; 1,481 Commands tests. |
| [FSC-04 #1036](https://github.com/FS-GG/FS.GG.SDD/pull/1036) | `ed9b4c6e88e16a4e5414580e47cafb280b077fab` | Uses fixed 80 KiB second-pass compare buffer, without peak-memory or atomicity proof; 1,482 Commands tests. |
| [FSC-05 #579](https://github.com/FS-GG/FS.GG.Templates/pull/579) | `ab392ae799c24f2be8b6e32d63d3c3bbc762577f` | Refuses unowned bytes before ZIP central directory; 69 payload/29 custody controls. |
| [FSC-05 #580](https://github.com/FS-GG/FS.GG.Templates/pull/580) | `840f32f4be5db240e61da0d5a4bf98fb91926f90` | Refuses nonzero multi-disk ZIP markers; 72 payload/29 custody controls. |
| [FSC-05 #581](https://github.com/FS-GG/FS.GG.Templates/pull/581) | `8f8252a7588a0079fa7653b65614dc64010782e5` | Binds ZIP end-record entry counts to parsed members; 76 payload/29 custody controls. |
| [GS2-09.7 #703](https://github.com/FS-GG/FS.GG.Coordination/pull/703) | `42d1adafadb6b3c849ee3a27a8a72fe0c5338d7b` | Refuses duplicate raw ruleset condition members; 145 focused tests. |
| [GS2-09.9 #704](https://github.com/FS-GG/FS.GG.Coordination/pull/704) | `50bce2b99ca0347ccce055303f3dc44293ab585d` | Binds nested pull repository URLs to selected target and refuses list/detail drift; 83 fake-port tests. |
| [FSC-03 F# #3801](https://github.com/FS-GG/.github/pull/3801) | `b3ae107d4e5b3d911d850fbb34029412099bf451` | Pure Rule B refuses a referenced node absent from its supplied graph; 148 policy tests. Supplied graph remains unauthenticated. |
| [FSC-05 #582](https://github.com/FS-GG/FS.GG.Templates/pull/582) | `9dc235eb69778c0e74740fe24fc9675ab74f1141` | Refuses nonempty ZIP central member comments; 77 payload/29 custody controls. |
| [GS2-09.7 #705](https://github.com/FS-GG/FS.GG.Coordination/pull/705) | `7af17b858f2d274011fde2f71cd562926a2cb6f4` | Refuses duplicate nested ruleset parameter members before retaining JSON; 146 focused tests. |
| [GS2-09.9 #706](https://github.com/FS-GG/FS.GG.Coordination/pull/706) | `bd85c334c41b1f9c78179e918829c55b817ea0c6` | Binds present pull URL to selected repository and number; 86 fake-port tests. |
| [FSC-03 F# #3802](https://github.com/FS-GG/.github/pull/3802) | `7418842a49da10f9944a2106d14e1d65a3401794` | Refuses Windows drive-prefixed paths in pure Rule B graph and project inputs; 154 policy tests. |
| [FSC-04 #1037](https://github.com/FS-GG/FS.GG.SDD/pull/1037) | `4e2b5bf0aa760154b006197beea9281cd60c84e2` | Reserves checked first-pass file length to reduce test-thread allocation; 1,483 Commands tests, no peak-memory or atomicity proof. |
| [FSC-05 #583](https://github.com/FS-GG/FS.GG.Templates/pull/583) | `6f65af89837b6da010a5a5983be247490de07b1f` | Refuses altered selected central/local ZIP header metadata; 82 payload/29 custody controls. |
| [GS2-09.9 #707](https://github.com/FS-GG/FS.GG.Coordination/pull/707) | `6f7c4de6f8c892ecde9a34c392cb284834eb75ca` | Binds present branch commit URL to selected repository and SHA in both protection probes; 88 fake-port tests. |
| [FSC-03 F# #3803](https://github.com/FS-GG/.github/pull/3803) | `9912114a5a5651ba5624486183614dac2499fff7` | Refuses surrounding whitespace in supplied ProjectReference Include that installed MSBuild trims; 158 policy tests. |
| [FSC-05 #584](https://github.com/FS-GG/FS.GG.Templates/pull/584) | `1fa5d682b7c864948ea9c216f04a8ea5ceba4865` | Refuses matching nonzero ZIP flags in selected local/central headers; 85 payload/29 custody controls. |
| [GS2-09.9 #708](https://github.com/FS-GG/FS.GG.Coordination/pull/708) | `01c206a474e9f40f9369ce291a6e8e34237158e8` | Uses type-sensitive canonical JSON digest for initial/terminal protection policy comparison; 90 fake-port tests. |
| [FSC-03 F# #3804](https://github.com/FS-GG/.github/pull/3804) | `310f702287f7db8b2565c977d61c08a82321a6c1` | Dormant pure supplied-source assembler refuses duplicate project identities, absent references and empty lists before graph construction; 163 policy tests. |
| [GS2-09.7 #709](https://github.com/FS-GG/FS.GG.Coordination/pull/709) | `bf322d6d36fa18df67af4fc10a44908f2e4c992b` | Requires exact PR marker-number set across issue page, PR page and raw-to-typed adapter, closing same-count subject substitution; 711 full unit tests. Initial sandbox census and Q5/Q6 remain held. |
| [FSC-04 #1038](https://github.com/FS-GG/FS.GG.SDD/pull/1038) | `6ca4befec823793fcb6f585db7bfb8b48984b8ad` | Transfers only freshly completed private Linux pinned-read bytes into `CapturedFile`, preserving defensive copy for supplied arrays; 1,486 Commands tests, no peak-memory or atomicity proof. |
| [FSC-05 #585](https://github.com/FS-GG/FS.GG.Templates/pull/585) | `519603fa4c4bb46755dc28b221005de0f251083f` | Requires selected ZIP local DOS time/date to match central record; 87 payload/29 custody controls, selected archive still NO_VERDICT. |
| [GS2-09.9 #710](https://github.com/FS-GG/FS.GG.Coordination/pull/710) | `6ced357930e81ada8753b7305627f6dd73d4fea5` | Requires present and type-sensitive equal selected pull list/detail fields; 92 fake-port tests, native effect still held. |
| [FSC-03 F# #3805](https://github.com/FS-GG/.github/pull/3805) | `a927784f1496d6644bf3311c7f9bceeaffe03e9b` | Pure supplied-source assembler refuses `.proj`/`.targets` identities outside the live Python project's discovered extension roster; 166 policy tests, source provenance still unauthenticated. |
| [GS2-09.7 #711](https://github.com/FS-GG/FS.GG.Coordination/pull/711) | `74ea9066da07a2adba2e8e0971e4c3decafe2a43` | Raw-to-typed provider adapter refuses duplicate PR marker members in a captured issue page before extraction; 133 focused tests. Initial census and Q5/Q6 remain held. |
| [GS2-09.9 #712](https://github.com/FS-GG/FS.GG.Coordination/pull/712) | `826d9c1dc38b43a59f3acb8dbed35e0a91edcb33` | Strict native JSON parser refuses exponent overflow before repository readback; 94 fake-port tests, one-attempt native effect held. |
| [FSC-05 #586](https://github.com/FS-GG/FS.GG.Templates/pull/586) | `1e80421fcec60f95824ce64508d4858fc03f962c` | Refuses invalid DOS date/time fields even when ZIP local and central values agree; 94 payload/29 custody controls, selected archive NO_VERDICT. |
| [FSC-03 F# #3806](https://github.com/FS-GG/.github/pull/3806) | `a4ea873e96ef51628469a4a30cd9bab00e7c5289` | Dormant pure handoff requires supplied project XML identities to match a supplied expected roster; 174 policy tests, expected roster still unauthenticated. |
| [GS2-09.9 #713](https://github.com/FS-GG/FS.GG.Coordination/pull/713) | `55ba5c9d8fee433c79a0c892b8699cd0bd01d4fb` | Refuses `.` or `..` repository path segments before injected native callbacks; 95 fake-port tests. Installed provider and one-attempt native effect remain held. |
| [GS2-09.7 #714](https://github.com/FS-GG/FS.GG.Coordination/pull/714) | `d3c90bce8947b796a7bd34d0181f32b0a35ee6dd` | Raw-to-typed project binder refuses duplicate `hasNextPage` members in item, field and value pages; 135 focused tests. Q5/Q6 remain held. |
| [FSC-04 #1039](https://github.com/FS-GG/FS.GG.SDD/pull/1039) | `38e48ef03bda4a52528aa5a4941e27e181ae24e8` | Fills one budget-checked private array directly from a pinned descriptor and refuses short or extra-byte reads; 1,490 Commands tests. Allocation fixture does not prove peak memory or atomicity. |
| [FSC-05 #587](https://github.com/FS-GG/FS.GG.Templates/pull/587) | `028e1ab32f87113acfdbba9de808a43f2d15470e` | Refuses trailing bytes after a raw-deflate end marker inside the declared compressed member size; 95 payload/29 custody controls, selected archive NO_VERDICT. |
| [FSC-03 F# #3807](https://github.com/FS-GG/.github/pull/3807) | `1a6b278279ab6554fc1066cd1239e5a3c585e569` | Pure supplied-source handoff binds raw XML bytes to caller-supplied SHA-256 digests before exact-roster graph assembly; 182 policy tests. Digest and roster provenance remain unauthenticated. |
| [GS2-09.9 #715](https://github.com/FS-GG/FS.GG.Coordination/pull/715) | `331d4b80c7473c92f75e324c1127b78c355125eb` | Native reader binds present repository owner and name to the selected full name; lost-response fake stays unknown after one attempted PUT, 97 focused tests. Installed provider and #550 remain held. |
| [GS2-09.7 #716](https://github.com/FS-GG/FS.GG.Coordination/pull/716) | `f2b13c35abeb9a46114f98e75f0993b28a45b0bf` | Raw-to-typed relation binder refuses duplicate root `data` JSON members in initial and continuation pages; 136 focused tests. Hosted checks queued, initial census and Q5/Q6 held. |
| [FSC-05 #588](https://github.com/FS-GG/FS.GG.Templates/pull/588) | `f3692ddbbabd11ec089826f360bc0429fc208b18` | Refuses nonzero DOS external-attribute bits even when a ZIP member has Unix regular-file mode; 97 payload/29 custody controls, selected archive NO_VERDICT. |
| [FSC-03 F# #3808](https://github.com/FS-GG/.github/pull/3808) | `5d1c0254da01d868c3b057eeee21ff44c43df968` | Pure Rule B refuses unsupported GitHub glob `?`, `+` and `[]` operators instead of false-green coverage; 185 tests. Matching live Python repair is pending. |
| [GS2-09.7 #717](https://github.com/FS-GG/FS.GG.Coordination/pull/717) | `f33214b088db881cc7603006ef452310e57f0af9` | Raw-to-typed issue binder refuses duplicate repository identity members in captured responses; 137 focused tests. Hosted checks queued, initial census and Q5/Q6 held. |
| [GS2-09.9 #718](https://github.com/FS-GG/FS.GG.Coordination/pull/718) | `6d1e5d86c502a270851ed7196b018d158d7900d9` | Pure closed-candidate check compares canonical selected bytes with packet bytes, refusing Python boolean/float aliases for integer facts; 80 focused tests, candidate remains non-authorizing. Closed archive pin changed to `d665e9b66f42aa3df8b270b792d5958aec6c4bd2bd136010cd146b5e237dcd19`; #550 held. |
| [FSC-05 #589](https://github.com/FS-GG/FS.GG.Templates/pull/589) | `177d8a53f5b2e48d25b5dab4b9979fdf0cb48979` | Requires Unix 0644 mode for non-template regular ZIP members, retaining the signed signature exception; 99 payload/29 custody controls, selected archive NO_VERDICT. |
| [FSC-04 #1040](https://github.com/FS-GG/FS.GG.SDD/pull/1040) | `1bab7cdc32635a2b6d45b8c7457313907f740f71` | Provisional Linux pinned reader refuses NFC/ignore-case aliases in declarations, physical roster and selected path; 1,496 Commands tests, Windows/ABA/atomicity still unproved. |
| [FSC-03 Python #3809](https://github.com/FS-GG/.github/pull/3809) | `94c44071bc75dd5de7b243c2b3c9c7efbf66fce5` | Live Rule B gate refuses unsupported GitHub path operators `?`, `+` and `[]` before coverage; 168 fixture controls. Stack acceptance and installed receiver proof remain open. |
| [GS2-09.9 #719](https://github.com/FS-GG/FS.GG.Coordination/pull/719) | `371948c4e749585e0b0fc599396ad0d25cd540cc` | Closed scaffold verifier refuses a float `nativeSource.size` in a resealed manifest; 19 focused tests. Local approval digest remains caller supplied, #550 held. |
| [GS2-09.7 #720](https://github.com/FS-GG/FS.GG.Coordination/pull/720) | `677274aac21e234bf1ec4f13c848bbf212a35f1a` | Raw-to-typed issue binder independently checks unique issue identities, unique PR markers and disjoint issue/marker numbers; 139 focused tests. Hosted checks queued, Q5/Q6 held. |
| [FSC-03 Python #3810](https://github.com/FS-GG/.github/pull/3810) | `cd2617ca07ccdf07fbbad556bb49a1fe72201c71` | Live static graph refuses unverified custom SDK declarations that can import hidden `ProjectReference` edges; 170 fixtures after an installed MSBuild probe. Built-in resolver provenance and F# parity remain open. |
| [GS2-09.9 #721](https://github.com/FS-GG/FS.GG.Coordination/pull/721) | `70ccbf3b0505cb90eb50035e377f4157ed660c27` | Release preflight refuses incomplete or falsely authorizing byte-verifier results; 59 focused tests. Closed archive and manifest pins unchanged, workflows disabled and #550 held. |
| [FSC-05 #590](https://github.com/FS-GG/FS.GG.Templates/pull/590) | `a6b25cf6f7bfb0448cd01491be7aa1224faf2fb2` | Read-only ZIP reader refuses embedded NUL filenames that `zipfile` silently shortens; 101 payload/29 custody controls, selected archive NO_VERDICT. |
| [FSC-04 #1041](https://github.com/FS-GG/FS.GG.SDD/pull/1041) | `2079be649681c496629b5af11c3760026ae7486b` | Pinned closed-root selection refuses Unicode/case aliases, including a late alias during capture; 1,500 Commands tests. ABA, Windows and cross-root proof remain open. |
| [GS2-09.7 #722](https://github.com/FS-GG/FS.GG.Coordination/pull/722) | `08ecebc9fa2b407ae1a76f3b3f575c7f1113b938` | Raw-to-typed project binder refuses duplicate or blank item IDs across captured rows; 140 focused tests. Hosted checks queued, initial census and Q5/Q6 held. |
| [FSC-03 F# #3811](https://github.com/FS-GG/.github/pull/3811) | `979afbcc3bc26f41488caad55ec0f4f788e64d6b` | Pure supplied-graph reader refuses unverified custom SDK declarations, matching Python #3810; 191 policy tests. SDK/Import provenance and evaluated graph remain open. |
| [GS2-09.9 #723](https://github.com/FS-GG/FS.GG.Coordination/pull/723) | `fb3df0645d60759f5bc8c056bc4f98af5e27f178` | Git-tree witness requires the exact versioned closed byte-verifier success result; 21 focused tests, witness cannot dispatch and #550 held. |
| [FSC-05 #591](https://github.com/FS-GG/FS.GG.Templates/pull/591) | `298a18acf89cf393df0d6a17aaa14460e2661f16` | Pin/roster custody observer refuses NUL-shortened non-owner member names as NO_VERDICT; 31 custody/101 payload controls, selected archive still NO_VERDICT. |
| [GS2-09.9 #724](https://github.com/FS-GG/FS.GG.Coordination/pull/724) | `e4fb1f90ab986ba426802fc0db5b8ef5586bb57f` | Read-only native-effect admission matrix separates the closed native-free scaffold from a future reviewed runnable revision and installed no-grant controls; 16 local links resolve. It supplies no protected selection or authority. |
| [FSC-04 #1042](https://github.com/FS-GG/FS.GG.SDD/pull/1042) | `161e07ad766d2e92f7c720c60da6563f00aef577` | Provisional Linux preview discovers complete `work/` candidate inventory through held descriptors rather than a caller file list; 1,505 Commands tests. Atomicity, Windows and installed proof remain open. |
| [FSC-05 #592](https://github.com/FS-GG/FS.GG.Templates/pull/592) | `82acf2df0a0966b6c37ef26216402fecf6b3e6de` | Custody observer refuses leading/trailing ZIP overlays and local-to-central gaps as NO_VERDICT; 34 custody/101 payload controls. Selected archive remains NO_VERDICT. |
| [GS2-09.7 #725](https://github.com/FS-GG/FS.GG.Coordination/pull/725) | `8f46f41a16fb6707b3f2ae8a555fbd621d02c934` | Native activity reconciler binds initial issue-page origin, owner/repo and repository ID to configured options; 720 full unit tests. Protected option pins and provider byte custody, Q5/Q6 remain open. |
| [GS2-09.9 #726](https://github.com/FS-GG/FS.GG.Coordination/pull/726) | `d753d9c4470ba8a8903301197794d950790579d9` | Adversarial correction to #724 adds missing #550 v5 contract, source/workflow, request, journal and grant evidence cells and fixes the late-drift journal-read counter; 18 links resolve. Runnable source and protected reviewer remain absent. |
| [FSC-03 F# #3812](https://github.com/FS-GG/.github/pull/3812) | `ef20439a8b326cedf66e12595f6df3224197b50d` | Pure SHA-1 Git-tree object walker derives a complete supplied project roster; 199 tests, local read-only 350-object/27-project dogfood matched `git ls-tree`. Root tree and project bytes lack authenticated binding. |
| [GS2-09.7 #727](https://github.com/FS-GG/FS.GG.Coordination/pull/727) | `65bbb126db8718c57aeb1715dd7e77082751f70b` | Read-only owner packet distinguishes registered Q4 diagnostic repository from an unselected migration copy and records protected admission, exact run/attempt/target, native byte custody and journal/receipt joins for Q5/Q6. No protected action. |
| [GS2-09.9 #728](https://github.com/FS-GG/FS.GG.Coordination/pull/728) | `82d2b4344d5a0c6767aff0c2a24a1b75344b78e3` | Source-only typed v5 port proposal maps protected roles but its only entry exits 78 without grant or port access; 10 focused tests. Closed archive and disabled workflows unchanged, #550 held. |
| [FSC-03 F# #3813](https://github.com/FS-GG/.github/pull/3813) | `925ab69551bd412d797e4dfaa6bd588ca2ced3b1` | Pure adapter binds supplied project bytes to Git blob IDs before XML graph inspection; 203 tests. Root tree remains unauthenticated to repository/commit. |
| [FSC-04 #1043](https://github.com/FS-GG/FS.GG.SDD/pull/1043) | `70b76458a97d0a530f7885afa406288bf45ece4f` | Read-only preview re-verifies raw byte overlap for every selected `work/` file shared by model bundle and discovered inventory; 1,510 Commands tests. Common instant/ABA still unproved. |
| [GS2-09.9 #729](https://github.com/FS-GG/FS.GG.Coordination/pull/729) | `23130dd81e7d59cf061a53fbecd109af2adce279` | Seals the source-only closed v5 result against caller-forged authorization, dispatch, effect count, exit and schema fields; 12 focused tests. Protected role identities remain unproved, #550 held. |
| [Telemetry schema #730](https://github.com/FS-GG/FS.GG.Coordination/pull/730) | `d195d603bf996b56532abf1191e4416504e4567c` | Direct-session schema probe binds `turn/completed` through the selected server notification route rather than an unused definition; 146 adapter tests. Current-session attachment and native completed-turn usage remain no-verdict. |
| [GS2-09.9 #731](https://github.com/FS-GG/FS.GG.Coordination/pull/731) | `96b76b7d607713ad215aef9d6b0fd287d5ee825b` | Source-only design for a separate runnable revision's file/member/workflow graph; eight links resolve. Current closed archive, exit-78 entry and disabled workflows remain unchanged, #550 held. |
| [FSC-03 F# #3814](https://github.com/FS-GG/.github/pull/3814) | `c4411260de3b4f25bbd7c91c090f0aa7e98ef652` | Pure exact-commit reader port verifies supplied raw Git commit hash and binds its tree to the project graph; 212 tests. Reader and accepted repository/commit pin remain unauthenticated. |
| [Telemetry schema #732](https://github.com/FS-GG/FS.GG.Coordination/pull/732) | `d5f30278a62c904ad2a07dcf21f680603ec2fae1` | Direct-session schema probe binds the actual `thread/resume` client request route; 149 adapter tests. Current-session attachment and native completed-turn usage remain no-verdict. |
| [GS2-09.7 #733](https://github.com/FS-GG/FS.GG.Coordination/pull/733) | `3b03e4bb1e9c237e10764a73e3f06a4a0bfd8014` | Source-only protected issue-census reader/store port checks run/attempt/nonce/candidate/workflow/repository/store and page/typed joins; 726 full unit tests. Installed ACL/provider byte custody and Q5/Q6 held. |
| [GS2-09.9 #734](https://github.com/FS-GG/FS.GG.Coordination/pull/734) | `1f9360eeab7d590d04f3f93b95e35e56f6681717` | Offline cross-module controls bind sealed operation-plan and native PR request bytes/digest; 62 focused tests. Protected plan/seal, runnable artifact and #550 effect held. |
| [FSC-04 #1044](https://github.com/FS-GG/FS.GG.SDD/pull/1044) | `ec4637bda6c52ef8001c62ed154f2d90961229c1` | Read-only preview compares two bundle/work-tree capture pairs and refuses changed roster/raw bytes; 1,515 Commands tests. Four sequential captures still lack common-instant/ABA proof. |
| [GS2-09.9 #735](https://github.com/FS-GG/FS.GG.Coordination/pull/735) | `a19627eedb34a5060787a9dfcc7c28f811b2f31f` | Source-only sealed-plan adapter returns immutable, non-authorizing canonical request bytes from the same verified read; 17 focused tests. Protected plan/seal and #550 dispatch remain held. |
| [Telemetry usage #736](https://github.com/FS-GG/FS.GG.Coordination/pull/736) | `f60d2cf10ecb5aec5ea2a6747a86722c6bab80e1` | Dormant direct-session usage reducer refuses malformed but equal reservation/subscription scope keys; 152 adapter tests. Current-session authentication and completed-turn usage remain no-verdict. |
| [GS2-09.7 #737](https://github.com/FS-GG/FS.GG.Coordination/pull/737) | `f4bc3c8cfad696497eef0addb05c7549fb5758c2` | Source-only census binder refuses captured POST, redirect and byte-substituted bodies; 727 full unit tests. Protected native HTTP/provider custody and Q5/Q6 remain held. |
| [FSC-03 F# #3815](https://github.com/FS-GG/.github/pull/3815) | `7d5a67e3ce29970e309ae294a844b8e1044ae98a` | Pure GitHub GraphQL response adapter binds exact repository, commit and tree identity and refuses partial/foreign/duplicate JSON; 221 tests. Authenticated transport and accepted pin remain uninstalled. |
| [GS2-09.9 #738](https://github.com/FS-GG/FS.GG.Coordination/pull/738) | `2b21d54a65b5b453538fef55792ce5ce73f9730f` | Source-only sealed-plan adapter binds attestation to a distinct exact read-seal scope, rechecks both readers for drift, and closes five fake-port false passes; 20 focused tests. Fake scopes remain non-authoritative and #550 stays held. |
| [FSC-04 #1045](https://github.com/FS-GG/FS.GG.SDD/pull/1045) | `ff8215ec6125ffafc058b24b5cfa7439dabd31da` | Physical ABA and post-final-capture controls show four matching reads still lack a common instant; the read-only success type is explicitly `ObservedAgreement`, not authorization. 1,517 Commands tests. |
| [Telemetry usage #739](https://github.com/FS-GG/FS.GG.Coordination/pull/739) | `35c90bfc2bcecb1ca0506fba4c55d883a4233d0f` | Dormant direct-session correlation refuses malformed reservation, transport, protocol and UTC facts; 157 adapter tests. Native completed-turn usage and Host capture remain no-verdict. |
| [GS2-09.7 #740](https://github.com/FS-GG/FS.GG.Coordination/pull/740) | `f06b927e91fec650a3c0ffd620ecc6981fb0aeec` | Source-only census binder refuses foreign provider IDs and ambiguous headers, and includes recorded headers in its corpus digest; 729 full unit tests. Native wire custody, freshness and Q5/Q6 stay held. |
| [FSC-03 F# #3816](https://github.com/FS-GG/.github/pull/3816) | `b7920dfdf4b6d3b815d8e287954da24dfbdd2420` | Dormant GraphQL HTTP reader fixes the endpoint and refuses redirect, wrong origin/status/media, mutation and oversized/underreported body; 230 F# tests. No live credential, accepted pin or installed receiver. |
| [GS2-09.9 #741](https://github.com/FS-GG/FS.GG.Coordination/pull/741) | `de3bc6d714da6dbae57d0a23b220e59c336df217` | Closed read-only plan-seal event witness binds canonical event identity/digest to verified plan/request and selected custody facts; eight new and ten existing focused tests. The event backend/identities remain fake-port only and #550/#545 stay held. |
| [Telemetry usage #742](https://github.com/FS-GG/FS.GG.Coordination/pull/742) | `d33d0b1df180b89701112a15a9f85169ff6b62b4` | Dormant usage adapter refuses malformed typed snapshot/exec/upstream candidates using counter grammars and cumulative domination checks; 160 Release execution tests. Native use and Host receipt remain no-verdict. |
| [FSC-04 #1046](https://github.com/FS-GG/FS.GG.SDD/pull/1046) | `f5434a4d6be635b8ce080e6707e67bbf4c65101b` | Physical core-source schedule returns the same observed A0/B1 pair twice although it never coexisted on disk; four-capture preview remains non-authorizing `ObservedAgreement`. 1,519 Commands tests; common-instant producer custody remains open. |
| [GS2-09.7 #743](https://github.com/FS-GG/FS.GG.Coordination/pull/743) | `0452ad506ea95cb4944db7d4048179e9a227ae99` | Second read-only custody port compares exact store descriptors and object-by-object run/selection/raw capture records, refusing missing or changed store evidence; 730 full unit tests. Protected ACL/native custody, inventory, Q5/Q6 remain held. |
| [Telemetry usage #744](https://github.com/FS-GG/FS.GG.Coordination/pull/744) | `809b1255933f8e7e1f93f1fe08536868a923a32d` | Source-only continuity gate refuses equal malformed expected/bound scopes before authenticator read; 161 Release execution tests. Current-session usage and Host capture remain no-verdict. |
| [FSC-03 F# #3817](https://github.com/FS-GG/.github/pull/3817) | `b5f47897f5146f223d83b85ad104f614a733f379` | Source-only strict protected-main response reducer derives a provisional commit pin and composes it with commit/tree/project bytes; 237 tests. Authenticated REST reader is uninstalled and a moving tip is not a durable acceptance receipt. |
| [GS2-09.9 #745](https://github.com/FS-GG/FS.GG.Coordination/pull/745) | `cee907ebcd0c3a65bcae61e6953a433eba0e841e` | Closed fake-port v5 journal intent readback distinguishes claimed CAS acknowledgment from independent committed-marker readback; 20 focused and adjacent tests. Real protected writer/replay/grant and #550 effect remain held. |
| [Telemetry usage #746](https://github.com/FS-GG/FS.GG.Coordination/pull/746) | `ba8173aeb79b09d7cd9ef983ff9e29122de81a5d` | Source-only prospective-window validation refuses equal malformed scopes before reservation CAS/current-session source; 163 Release execution tests. Native completed-turn usage and Host receipt remain absent. |
| [FSC-03 F# #3818](https://github.com/FS-GG/.github/pull/3818) | `f63ad22d9b8568635dff93c16b6eeb9356aaee03` | Dormant fixed-URL protected-branch HTTP reader refuses redirects, status/origin/media mismatch and oversized responses; 247 F# tests. No live request, accepted freshness or installed receiver. |
| [GS2-09.7 #747](https://github.com/FS-GG/FS.GG.Coordination/pull/747) | `d496707cc0cd8a379dce7f487e8d198d3c4ef68f` | Source-only store installation descriptor pins artifact/ACL digest and role principals before reader invocation, refusing candidate read/write, mutable objects and drift; 730 full unit tests. Real IAM/native custody and Q5/Q6 remain held. |
| [Telemetry usage #748](https://github.com/FS-GG/FS.GG.Coordination/pull/748) | `45e86f719c7a94f504ab93bfd7bb28cfa19e3db4` | Source-only reservation chronology refuses pre-reservation observation and second-clock rollback with a burned challenge gap; 165 Release execution tests. Trusted clock/current-session usage and Host receipt remain absent. |

These are draft sources, not GS2 receipts. GS2-09.9 still lacks installed
provider/native-effect acceptance (#545 disputed, #550 held); GS2-09.7 still
lacks initial census, full typed inspect, journal/custom receipts and Q5/Q6.
The immediate #550 prerequisite remains an independently reviewed runnable
effect artifact/workflow at an exact integrated Coordination commit, followed
by installed no-grant refusal using a protected observer/image/runtime and
zero-effect counters. Authenticated dispatch/reviewer/issuer events, selected
disposable target and effective single-repository App scope, protected CAS
writer with independent replay reader, and one-use grant are still absent.
The source-only closed scaffolds do not satisfy these installed identities or
authorize the one-POST effect.
The protected Coordination sequence is
[#532](https://github.com/FS-GG/FS.GG.Coordination/pull/532) →
[#527](https://github.com/FS-GG/FS.GG.Coordination/pull/527) →
[#526](https://github.com/FS-GG/FS.GG.Coordination/pull/526) →
[#529](https://github.com/FS-GG/FS.GG.Coordination/pull/529), under the
Coordination owner's gate. At the 2026-09-25 18:36 UTC read-only check,
#532 was behind with no review decision, #527 was blocked with cancelled
old-head jobs, and #526/#529 were draft and behind. No protected merge
was inferred from source-only draft checks.
FSC-03 lacks authenticated roster/Import/evaluated-graph and installed
receiver parity. FSC-04 lacks ABA/post-check/cross-root/Windows and effect
proof. FSC-05 selected native versus retained release remains NO_VERDICT and
lacks complete ZIP closure, producer/served-byte custody, #511 CAS and
receiver proof. No draft authorizes a protected merge or cutover.

[V2-PROG-01 renderer draft #3735](https://github.com/FS-GG/.github/pull/3735)
at `48ef2d177c4d209b81ed979110d62bf8b7018f90` adds a typed local JSONL
diagnostic path and a script workflow that obtains fresh authenticated
telemetry readiness, scans the root session family, and renders the pure F#
snapshot. Local counter, weekly allowance and projection rows explicitly say
their collector/account scope is unverified; they do not claim Host capture.
The separate metadata file supplies lanes, workstreams and completion history;
the F# script requires that metadata to be dated within two minutes of the
scan, and the orchestrator must rebuild it from live roster/PR evidence before
each ten-minute report. A fresh fixture rendered with unverified usage and
pending capture; a three-minute-old fixture was refused. The local path passed
24 focused renderer tests and a live read-only render. It remains a source-only
draft pending owner acceptance. Its later adapter refuses non-UTC report
timestamps and any completion that postdates metadata verification; focused
fresh/offset/postdated fixtures passed or refused as expected.
The script README now requires a new evidence cutoff for each run, with live
roster, explicit launch settings, exact PR heads/commit times, current counts,
and a pushed roadmap head before assigning that head to completions. A mere
metadata timestamp change is insufficient evidence of those checks.
Each typed lane now carries required `CurrentWork`; the Markdown lane table
renders it beside the lane identity. A running lane with blank work is refused.
The JSON adapter requires `currentWork` for every lane. Focused tests pass
25/25; missing and blank current-work JSON fixtures each exited 2 without
creating a report. Every ten-minute metadata rebuild must set all six
concrete assignments from the live roster rather than carry forward stale
task descriptions.

At the 18:12:37 UTC rendered checkpoint, the source-only workflow counted six
active Sol/high lanes (one reserved GS2-09.9 and one direct GS2-09.7), 30
family sessions and 80 completed ten-minute periods from the 04:47:05 UTC root
start. The latest completed 17:57:05–18:07:05 UTC period had 34,676,694
input tokens (34,395,008 cached; 281,686 noncached), 98,119 output and
34,774,813 total; the all-period team mean, including zero-use periods, was
31,084,008.81 total. Local root weekly usage was 85% at 18:12:17, leaving
15%; the same-reset earliest-to-latest percentage slope gives a conditional
continuous-use, account-wide projection near 22:09 UTC. Counter and account
scope remain **unverified local diagnostics**, not collector/Host capture.
Fresh direct authenticated telemetry probes were ready/configured on
`main-fsharp-dev`, pending=0, pendingUnacknowledged=0 and
unacknowledgedLossy=false. The five completion rows referred to this roadmap's
then-current head `932d3f0e0f96feb05979a19175127eb0bb453ad3`, committed
after those draft heads were recorded. A subsequent read-only same-credential
GraphQL probe again resolved pinned Coordination Project 1 but returned
`FORBIDDEN Resource not accessible by personal access token` for Project 2;
the installed all-project runner remains unable to qualify an end-to-end item.
At the 18:43 UTC read-only same-credential recheck, pinned Project 1 again
resolved as `PVT_kwDOEYAWY84Bb08W` / Coordination while Project 2 again
returned `FORBIDDEN Resource not accessible by personal access token`. The
minimal installed-runner admission blocker is unchanged; this query produced
no selected work item, native turn or applied Host receipt.

At the 18:34:59 UTC rendered checkpoint, a newly checked roster and exact PR
heads counted six active Sol/high lanes, including the reserved GS2-09.9 and
direct GS2-09.7 workers. The local root family had 30 sessions and 82 completed
ten-minute periods. The latest 18:17:05–18:27:05 UTC period had 42,264,763
input tokens (41,966,592 cached; 298,171 noncached), 96,581 output and
42,361,344 total; the all-period team mean, including zero-use periods, was
31,362,490.20 total. Local weekly usage was 87% at 18:34:34, leaving 13%,
with a conditional continuous-use, account-wide same-reset projection near
21:57 UTC. These remain **unverified local diagnostics**. Authenticated health
and `main-fsharp-dev` workspace status were ready/configured with zero pending
and unacknowledged items and no lossy state; end-to-end runner/Host capture
remained pending. The five completion rows cited roadmap head `c01dc9f8`,
which had already recorded those exact source heads. The wrapper command
rendered successfully with zero build warnings/errors; the 24 focused tests
passed. A separate three-minute-old metadata run exited 2 and left no report
file, confirming the source-only freshness guard refuses that stale input.

At the 18:45:14 UTC rendered checkpoint, freshly checked metadata again counted
six active Sol/high lanes with reserved GS2-09.9 and direct GS2-09.7 workers.
The local root family had 30 sessions and 83 completed ten-minute periods.
The latest 18:27:05–18:37:05 UTC period had 36,996,957 input tokens
(36,650,624 cached; 346,333 noncached), 100,266 output and 37,097,223 total;
the all-period team mean, including zero-use periods, was 31,431,583.36 total.
Local weekly usage was 88% at 18:44:52, leaving 12%, with a conditional
continuous-use, account-wide same-reset projection near 21:51 UTC. These are
**unverified local diagnostics**. Direct authenticated telemetry was
ready/configured with a zero lossless queue; runner/Host capture remained
pending. The five completion rows cited previously pushed roadmap head
`a9aaf097`, which contained each draft's exact source head. The script rendered
with zero build warnings/errors; 24 focused tests remained green.

At the 18:57:12 UTC rendered checkpoint, six Sol/high lanes remained active;
the fifth worker had moved from FSC-05 to source-only telemetry capture
qualification, while the reserved GS2-09.9 and direct GS2-09.7 workers
continued. The local root family had 30 sessions and 85 completed ten-minute
periods. The latest 18:47:05–18:57:05 UTC period had 37,941,021 input tokens
(37,612,800 cached; 328,221 noncached), 119,635 output and 38,060,656 total;
the all-period team mean, including zero-use periods, was 31,551,863.54 total.
Local weekly usage was 89% at 18:56:53, leaving 11%, with a conditional
continuous-use, account-wide same-reset projection near 21:47 UTC. These
remain **unverified local diagnostics**, not runner/Host capture. Fresh direct
authenticated telemetry was ready/configured with a zero lossless queue;
capture acceptance remained pending. The five completion rows cited pushed
roadmap head `c77d72d6`, which recorded each exact draft head. The script
rendered with zero build warnings/errors, and 24 focused tests remained green.

At the 19:07:53 UTC rendered checkpoint, the freshly checked roster contained
six active GPT-6-Sol/high lanes, each with a concrete current-work cell: the
orchestrator on roadmap/reporting; reserved GS2-09.9 on sealed-plan byte
custody; direct GS2-09.7 on HTTP-header/provider freshness; FSC-03 on
authenticated GitHub commit transport; FSC-04 on ABA/common-instant preview;
and telemetry source on trusted current-session/native usage qualification.
The local root family had 30 sessions and 86 completed ten-minute periods.
The latest 18:57:05–19:07:05 UTC period had 38,677,177 input tokens
(38,359,808 cached; 317,369 noncached), 113,374 output and 38,790,551
total. The all-period **team** mean, including zero-use periods, was
31,636,034.33 total. The local weekly diagnostic was 90% used at 19:07:26,
leaving 10%, with a conditional continuous-use, account-wide same-reset
projection near 21:41 UTC. These remain **unverified local diagnostics**, not
collector/Host capture. Direct authenticated telemetry health was ready;
`main-fsharp-dev` was configured with pending=0, pendingUnacknowledged=0 and
unacknowledgedLossy=false. End-to-end runner/Host capture stayed pending.
The five completion rows cited previously pushed roadmap head `7f0c1bd6`,
which already recorded their exact source heads. The F# script rendered with
zero build warnings/errors, and 25 focused tests passed. Drafts #738, #1045,
#739 and #740 arrived after this render; none was counted as a completed row
at that cutoff. Their workers were immediately recycled into disjoint source
tasks, preserving six active lanes.

The next source evidence cutoff was frozen at 19:18:06 UTC rather than chased
through later arriving drafts. Its F# report rendered at 19:21:42 UTC after
fresh validation of that frozen source set and the live six-lane task roster;
the script's two-minute metadata guard had correctly refused the unrefreshed
19:18 snapshot. The recorded source roadmap head was `a4bd72bd`, with five
draft completions #3817, #744, #743, #1046 and #742 in newest-first order.
Drafts #745 (19:19:44 commit) and #746 (19:20:18 commit) were outside the
cutoff and are assigned to the next report. The latest complete local team
period, 19:07:05–19:17:05 UTC, had 27,557,809 input tokens (27,217,792
cached; 340,017 noncached), 106,355 output and 27,664,164 total; the mean
over 87 completed periods including zero-use periods was 31,590,380.64
total. Local unverified weekly usage was 91% at 19:21:25, leaving 9%; the
conditional continuous-use, account-wide projection was near 21:39 UTC.
Authenticated telemetry health and `main-fsharp-dev` workspace status were
ready/configured at 19:21:42 with pending=0, pendingUnacknowledged=0 and
unacknowledgedLossy=false. This is readiness only; genuine runner native
turn/usage and matching applied Host receipt are still absent. Release render
had zero warnings/errors and renderer tests remained 25/25. No protected
gate was cleared by this report.

At the 17:51 UTC source checkpoint, the newest five completed **draft
commits** were verified against their PR heads. Their completion is source
preparation, not an accepted V2 receipt or authorization to merge:

| Committed UTC | Workstream / draft | Exact head | Evidence |
| --- | --- | --- | --- |
| 2026-09-25 17:50:10 | GS2-09.7 duplicate issue-type refusal | `7f36e810a40cbd340f7e262dbae7437fe17a9d8f` | [Coordination #695](https://github.com/FS-GG/FS.GG.Coordination/pull/695), 140 focused tests |
| 2026-09-25 17:50:05 | FSC-03 external symlink refusal | `475dc96c41ac8a5dbc4eecffc7cc60f428c98709` | [`.github` #3795](https://github.com/FS-GG/.github/pull/3795), 156 fixtures |
| 2026-09-25 17:49:58 | GS2-09.9 selected identity copy | `95e53a9ec72d12514730c42eff6ff99b453ca4f7` | [Coordination #694](https://github.com/FS-GG/FS.GG.Coordination/pull/694), 38 fake-port tests |
| 2026-09-25 17:49:53 | FSC-05 pinned Unicode version | `9e29cd0573d3cde787a20e9cb44b190a2d9bc4b1` | [Templates #578](https://github.com/FS-GG/FS.GG.Templates/pull/578), 68 payload / 29 archive controls |
| 2026-09-25 17:49:29 | FSC-04 capture file bound | `03c196a8f90a0c1a68c65a3d196aebe433a25b1b` | [SDD #1034](https://github.com/FS-GG/FS.GG.SDD/pull/1034), 1,477 Commands tests |
[`.github` FSC-03 #3754](https://github.com/FS-GG/.github/pull/3754) at
`238362b2179456092fce92b0bbf9b45b3d5f26a3` stops an embedded `run: |` marker from
excusing a missing Rule (b) dependency in the F# source; 91 Release tests pass.
Differential review found the live Python `allow_uncovered()` still accepts a marker inside
a valid multiline quoted scalar on current main. Source audit found draft #3698 already
repairs that case through its shared scalar-span logic; stacked
[`.github` #3755](https://github.com/FS-GG/.github/pull/3755) at
`c5e4cd7bb935c5a6183819a2e20fd2575f25bc0f` adds independent full-gate Rule (b)
regression controls (91 Python fixtures) without duplicating the repair. Current main
falsely passes the fixture, while #3698/#3755 refuse and a real later comment remains
accepted. The repair is not live until this stack receives owner acceptance and protected
readback. The F# replacement remains unaccepted.
[Templates #549](https://github.com/FS-GG/FS.GG.Templates/pull/549) at
`b3b3b9622e78ced0839a60b0dfd7c61625978a23` makes the F# comparator return
NO_VERDICT for decomposed Unicode template paths instead of a false payload match;
16 focused and 29 stacked controls pass. Producer, served-byte, transaction, installed
and receiver custody remain open.
[Templates #550](https://github.com/FS-GG/FS.GG.Templates/pull/550) at
`e272ae4288a6941f46fbcfb57a9032837851cfed` also refuses an extra nested
config-shaped template member that previously yielded a payload-only match; 18 focused
and 29 stacked controls pass. Ordinary nested assets remain accepted.
[Templates #551](https://github.com/FS-GG/FS.GG.Templates/pull/551) at
`38f0a162d42983f377c3e62d17f8a38090ae7f7b` refuses a file/child-path collision,
including case-variant ancestor aliases, that previously returned a payload match;
20 payload and 29 archive controls pass. It is not served or installed custody proof.
[Templates #552](https://github.com/FS-GG/FS.GG.Templates/pull/552) at
`0eb690ce4a7699ae3ffe235dc32047936d2a83be` refuses a 64-hex digest with a trailing
newline that a regex anchor had accepted; 21 payload and 29 archive controls pass.
Producer, served-byte, transaction, installed and receiver holds remain unchanged.
[Templates #553](https://github.com/FS-GG/FS.GG.Templates/pull/553) at
`09130c225779c76be33d1e8c050c6e91d34f84ae` refuses asset path components with
trailing periods or ASCII spaces that previously returned a payload match; 24 payload
and 29 archive controls pass. This is still local comparator evidence only.
[`.github` FSC-03 #3756](https://github.com/FS-GG/.github/pull/3756) at
`ee36fbce6784a6edd1915b0fdb216ba80ed078bd` checks PR and push path filters
independently in the pure Rule (b) reducer, closing a red-before push-only omission;
98 F# and 91 Python focused fixtures pass. Malformed one-sided `paths: null` is still
stricter in F# than current Python main. Stacked
[`.github` #3757](https://github.com/FS-GG/.github/pull/3757) at
`b391106d5d0e6051d535302f1c64cdf462dcaeb3` repairs three red-before Python
false greens for present one-sided `paths: null`, `[]` and scalar filters while leaving
an absent key unfiltered; 95 Python fixtures pass. The stack and F# replacement still
need owner acceptance and installed parity. Stacked
[`.github` #3758](https://github.com/FS-GG/.github/pull/3758) at
`0a6daa906ead404b0df8f06c52b71f9142d670d2` additionally refuses bare
`[true]`, `[42]` and `[null]` path-list values that produced red-before false greens;
quoted strings remain valid. Ninety-nine Python and 11 matching F# syntax controls pass.
Stacked [`.github` #3759](https://github.com/FS-GG/.github/pull/3759) at
`e037e951c02cfab48e0087f8b3967b42a1e3034d` refuses plain or explicitly tagged
duplicate YAML mapping keys that previously overwrote a project-naming filter and
returned a false green; 101 Python and four matching F# duplicate-key controls pass.
The repair stack is still draft, with owner acceptance and installed parity pending.

### 1.1 Before active `FS.GG.Coordination` bootstrap

The README-only repository exists at the explicitly authorized inert bootstrap commit
`ce22e4d10f2efae7aa09018521487b598c082350`; it has no v2 code or production authority. Only `GS2-00`
and preparation for the active-bootstrap portion of `GS2-01` are assignable. The instruction to an agent
must name one unit and its permitted effects, for example:

> Execute `GS2-00.2` from the GitHub Substrate v2 roadmap. Produce the proposed ADR and authority census;
> preserve the inert repository, do not modify live GitHub configuration, and do not start v2 implementation.

Any active bootstrap beyond that inert creation, installing the GitHub App, creating environments, or
changing organization settings requires explicit organization-administrator authority. A worker may
prepare and verify an exact plan, but must not infer that authority from the roadmap.

### 1.2 After the bootstrap repository exists

`GS2-01` creates a repository-owned `github-substrate-v2-work` skill and a small roadmap command. That
command reads this document's stable unit IDs and the new repository's typed specification/evidence index,
then reports the next unit whose prerequisites are satisfied. It never schedules directly from mutable
Project status.

The normal instruction becomes:

> In `FS.GG.Coordination`, use `github-substrate-v2-work` to execute the next ready unit. Work only that
> unit, publish its required qualification evidence, and stop at its named exit gate.

Every unit must have:

- one owning repository;
- an explicit touch-set or administrative target set;
- prerequisite unit IDs and issue references;
- an exact candidate/source revision;
- positive acceptance cases and independent negative controls;
- a permission ceiling;
- a rollback or no-write boundary;
- generated and independently authored evidence; and
- a merged PR or protected administrative receipt before it is marked complete.

### 1.3 Evidence and status

The Coordination Project is a visibility projection. V1 claim, review, delivery, verification, and done
records do not qualify v2. Completion authority is the custom qualification receipt stored against the
exact source and artifact fingerprints in `FS.GG.Coordination`.

Each unit has these states:

```text
Planned -> Ready -> Implementing -> Candidate -> Qualified -> Accepted
                    |                 |
                    +-> Refused       +-> Rejected
```

- `Ready` means every prerequisite receipt is accepted.
- `Candidate` means code or a settings plan exists but has no authority to enter production.
- `Qualified` means all generated and independent controls pass against exact artifacts.
- `Accepted` means the owning PR is merged or the protected administrative operation is verified.
- `Refused` and `Rejected` retain evidence and return to design or implementation; they are not failures
  hidden by a retry.

Roadmap checkboxes are updated only from accepted receipts. They are a human projection, not proof.

### 1.4 Typed SDD handoff and parallel-work rule

Typed SDD P0–P4 are complete and provide the published generic kernel. The former coordination M0–M9
sequence is not another lane to burn down beside this roadmap. It is an inventory whose still-valid
requirements are mapped into GS2 units. In particular, `.github#2932` must not be claimed as written:

- its authority/mutation/compatibility census and baselines move to `GS2-00.3`–`GS2-00.7`;
- its omission and byte-compatibility controls enter the frozen v1 corpus;
- its coordination types and compiler surface move to `GS2-02` in `FS.GG.Coordination`; and
- its Standard SDD completion artifacts remain historical evidence, not v2 qualification.

Ordinary product work continues through v1 and does not need a v2 rebase. Typed SDD P5 readiness and soak
work may run in parallel through `GS2-09`. The P5 default flip has only two admitted schedules: complete
and stabilize it before `GS2-10`, or defer it until `OperatingV2`. At `GS2-10`, the kernel version,
lifecycle defaults, providers, scaffolder, registry, receiver heads, workflows, and settings become frozen
candidate inputs. Any later change invalidates approval and requires a new candidate plus the complete
Q0–Q7 rerun. No such change crosses `GS2-11`–`GS2-12`.

Before dispatching a unit, classify every adjacent row as one of:

| Classification | Meaning | Scheduling effect |
|---|---|---|
| `v2-unit` | Directly implements one named GS2 unit | Schedulable only when that unit's prerequisites hold |
| `v2-blocker` | Proven external work without which a unit cannot be authored or qualified | File/retain in its owning repo and record a real dependency edge |
| `parallel-product` | Ordinary product or Typed SDD work whose output can be re-observed before candidate freeze | May proceed; it does not become part of the v2 worker's touch-set |
| `candidate-input-change` | Kernel, lifecycle default, provider, registry, workflow, receiver, or settings change | Refresh before `GS2-10`; mint a new candidate or defer afterward |
| `superseded-inventory` | Former M-series work already transferred into GS2 acceptance | Never claim from Project status; amend/close the obsolete row |
| `cutover-deferred` | Safe work intentionally held across the freeze | Resume only after `OperatingV2` through the new system |

The Coordination board remains the visibility and dependency projection. It is not allowed to recreate the
superseded execution plan by presenting a historical M-series row as `Ready`.

The [F# automation convergence interlude](#v2-fs-i1--f-automation-convergence-interlude)
uses these same classes. Its `FSC` slices are planning anchors, not new GS2 receipt IDs or a second
scheduler. A port selected for the cutover candidate is a `candidate-input-change`; a separately
proven defect in a required gate is a `v2-blocker`; optional ports can remain `parallel-product` or
be explicitly `cutover-deferred`. Count shebangs, inline workflow code, and generated receivers when
classifying an executable path; suffix-only inventory is incomplete.

### 1.5 Single-operator execution readiness

The [governing execution contract](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md#single-operator-execution)
and [authority audit](coordination/2026-09-25-v2-single-operator-autonomy-audit.md) require one accountable
operator to drive the remaining programme through agents and protected CI. Named review disciplines
are fresh evidence assessments under ADR-0079; independently authored controls and native identity
predicates remain mandatory. Reuse qualified infrastructure and automate routine handoffs.

Before GS2-10 freeze, inventory and rehearse every installed execution route through retirement. Missing
interpreters, unobserved permissions, credential relays, unreconciled approval policy and recovery gaps
are candidate preparation work. Resolve them before closing the fleet. Current `fleet-cutover`
self-review restrictions still apply; [proposed ADR-0090](adr/0090-single-operator-v2-cutover-execution.md)
prepares a one-owner profile without substituting automation for protected human `OpenV2` approval.
No acceptance checkbox or historical receipt changes from this execution requirement alone.

## 2. Non-negotiable program invariants

1. The published FS.GG.SDD specification kernel is consumed as a package; no source-project shortcut or
   copied generic kernel is permitted.
2. GitHub owns facts it can represent with the required identity, revision, completeness, and relation
   semantics. FS-GG owns only the missing process, concurrency, evidence, and transaction semantics.
3. V2 is implemented in `FS.GG.Coordination`; `.github` retains organization policy, registries, designs,
   the cutover ledger, desired-state instances, and thin reusable workflow entry points.
4. The v1 engine receives only the universal epoch bridge, necessary security fixes, and retirement work.
5. Preparation may create inert schema and read-only projections. V1 and v2 normal production writers are
   never enabled together.
6. Every write is a typed, revision-bound, idempotent mutation plan with a durable outcome that includes
   partial and indeterminate states.
7. Generated model tests are necessary but insufficient. Every safety-critical invariant has at least one
   independently authored black-box oracle.
8. Failure, absence, incomplete reads, unsupported capabilities, insufficient permission, stale data,
   contradiction, partial success, and indeterminate success remain distinct.
9. Before `OpenV2`, rollback restores the bridge v1 authority while writes remain controlled. At and after
   `OpenV2`, recovery is roll-forward and v1 never resumes.
10. No compatibility reader, migration adapter, workflow, command, field, or exception survives without a
    deletion unit and observable deletion test.
11. A lease, comment order, or pre/post read is not a concurrency authority. Every claim, review epoch,
    operation grant, and cutover transition uses protected expected-parent Git-ref CAS and presents its
    monotonically increasing generation as a fencing token at the external effect boundary.
12. Webhooks and commands schedule reconciliation; only the shared fresh-observe/reduce/plan/apply/verify
    reconciler performs normal production writes, and complete scheduled audits repair lost hints.

## 3. Program dependency map

```text
GS2-00 Ratify design and freeze authority census
   |
   +-------------------------+
   |                         |
   v                         v
GS2-01 Bootstrap repo     GS2-08 Specify v1 bridge/ledger contract
   |                         |
   | waits for Quint Q1      |
   | + ADR-0077 amendment    |
   v                         |
GS2-02 Protocol core         |
   |                         |
   +----------+--------------+
   |          |              |
   v          v              v
GS2-03      GS2-04         GS2-08 Implement/publish/adopt bridge
Qualification GitHub IO       |
   |          |               |
   +----+-----+               |
        |                     |
        v                     |
   GS2-05 Work model          |
        |                     |
        +---------+-----------+
        |         |
        v         v
   GS2-06       GS2-07
   Settings/CI  Events/queue
        |         |
        +----+----+
             |
             v
        GS2-09 Migration/archive tooling
             |
             v
        GS2-10 Qualify exact candidate and prepare fleet
             |
             v
        GS2-11 Freeze and snapshot
             |
             v
        GS2-12 Switch and verify while closed
             |
             v
        GS2-13 Open v2 and retire v1
             |
             v
        GS2-14 Observe, normalize, and close
```

`GS2-03` and `GS2-04` may proceed in parallel after the protocol envelope is frozen. `GS2-06` and
`GS2-07` may proceed in parallel after the work model and adapter boundaries are qualified. The bridge may
be developed in parallel with v2, but it cannot publish until the epoch wire contract is frozen. No fleet
preparation begins from an unqualified candidate.

## 4. Qualification gates

These gates replace the existing coordination validation/verification process for v2.

| Gate | Proves | Required evidence |
|---|---|---|
| Q0 Architecture | Authorities and boundaries are coherent | Accepted ADR, authority/mutation/deletion census, threat model, independent review |
| Q1 Compiler | Literate Quint source extracts, typechecks, and compiles deterministically and completely | Literate-source and extracted-module fingerprints, pinned extractor/Quint/profile identities, Quint type/effect results, compiled-contract canonical bytes, semantic diff, source mapping, schema/projection freshness, and wrong-source/extraction/model controls |
| Q2 Pure model | The canonical Quint model's state and decisions obey invariants | Quint examples, simulation, property and bounded model checking, safety/liveness witnesses and counterexamples, plus independently authored transition tests |
| Q3 Adapter | External observations and writes preserve meaning | Consumer-owned ITF replay adapter, observable-state projection, recorded fixtures, pagination/completeness, revision, idempotency, partial/indeterminate and mapping-mutation controls |
| Q4 Sandbox | Real GitHub behavior matches the adapter contract | Isolated repositories/project, destructive test identities, API/permission/rate evidence |
| Q5 Shadow | V2 reads the live fleet without changing it | Complete snapshots, v1/v2 decision comparison, explained divergence ledger, zero v2 write permission |
| Q6 Recovery | Every multi-step path resumes safely | Failure after every step, receipt replay, compensation/roll-forward, rollback rehearsal |
| Q7 Supply chain | The exact candidate can be trusted and installed | Reproducible build, package hashes, SBOM, attestations, both-feed/public-read verification |
| Q8 Closed fleet | The switched fleet works before normal writes open | Schema/settings/receiver proof, isolated canary journeys, injected wrong paths, rollback readiness |
| Q9 Retirement | V1 can no longer author production state | Static/runtime writer census, deletion ledger, old-client refusal, sealed-history verification |
| Q10 Operations | V2 is stable after opening | Immediate baseline and source-bound readings after each of 15 distinct completed v2 work items; SLOs, incidents, repair lag, API cost, queue/CI/release measurements |

No single test generator may satisfy both sides of a safety claim. For example, the protocol compiler may
generate all legal epoch transitions, but an independent black-box suite must still attempt an old-client
write after freeze and reject a forged or rewound ledger.

Q2 model checking and Q3/Q4 correspondence answer different questions. Q2 proves properties of the Quint
model. Q3 replays fingerprinted ITF traces through the real pure implementation and compares only its
declared model-observable state. Q4 exercises the same adapter boundary against isolated GitHub behavior.
Passing one gate cannot substitute for either of the others, and `.github` may not host a fake v2
implementation merely to make replay pass.

Q1 and Q2 also cannot be split across different authored models. Q1 qualifies the exact literate Quint
source and extracted module set whose behavior Q2 explores; an F#-only reducer suite, generated shadow
model, or separately authored formal model cannot substitute for native Quint type/effect checking and
model checking. F# tests remain required implementation evidence and enter correspondence through Q3.

## 5. Detailed milestones

### GS2-00 — Ratify architecture and freeze the v1 corpus

**Parent:** `.github#2953`
**Owner:** `.github`
**Exit gate:** Q0

- [x] **GS2-00.0 — Accept the program handoff.** Finish or explicitly disposition Typed SDD P4
  release/tag/feed/registry residue (including `.github#2968` while it remains open); amend or resolve
  `.github#2932` as superseded and map each retained clause to this roadmap; record whether P5 is not
  started, readiness-only, finishing before `GS2-10`, or deferred until `OperatingV2`; classify every
  active Typed SDD claim, review, delivery, release, and receiver PR as `finish`, `park`, or `defer`.
  Re-adjudicate `.github#2932`'s declared dependencies (`.github#2903`, `.github#2905`, `.github#2841`,
  and `.github#2850`) independently as current-v1 defects, corpus inputs, real v2 blockers, or deferred
  work; the superseded edge itself is not a v2 prerequisite.
  Repair the modernization Epic's task-line acceptance and place dependencies for `.github#2964` and
  `.github#2965` in the authoritative Project `Blocked by` field rather than inert issue-body lines.
- [x] **GS2-00.1 — Resolve review questions.** Review every authority assignment, retained custom
  mechanism, GitHub plan/API limitation, permission boundary, and rollback claim in the governing design.
- [x] **GS2-00.2 — Author the org ADR.** Record the dedicated repository, published-kernel dependency,
  new-only writer policy, independent qualification lane, protected Git epoch ledger, native/custom
  authority table, and `OpenV2` boundary. Keep status Proposed until independent review is complete.
- [x] **GS2-00.3 — Complete the v1 authority census.** Enumerate every issue/body/Project/registry/comment,
  workflow, command, JSON contract, environment variable, file, package, schedule, and setting that can
  affect a coordination decision.
- [x] **GS2-00.4 — Complete the mutation census.** Name every always-writing and conditionally writing
  command, workflow, App route, repair script, release route, and administrative path; bind each to its
  current precondition and eventual v2 disposition.
- [x] **GS2-00.5 — Freeze the defect and behavior corpus.** Content-address representative claim,
  touch-set, dependency, hierarchy, intake, review, delivery, merge, release, pagination, rate-limit,
  partial-write, stale-read, and self-hosting incidents. Import `.github#2932`'s reproducible 72-hour
  churn, mutation-entry, protocol-string, replay, omission, and misclassification baselines without
  importing its superseded v1 implementation plan.
- [x] **GS2-00.6 — Freeze public compatibility surfaces.** Inventory CLI verbs/flags/exit codes, JSON and
  marker schemas, package IDs/versions, reusable workflow inputs/outputs/job IDs, required contexts, and
  receiver pins. Classify each `Preserve`, `Migrate`, `Seal`, or `Retire`.
- [x] **GS2-00.7 — Freeze the deletion ledger.** Every v1 source tree, parser, projection, field, schedule,
  workflow, exception, and package must have a later deletion unit and a test proving absence.
- [x] **GS2-00.8 — Accept Q0.** Architecture, security, operations, and cross-repository reviewers sign the
  exact design/census fingerprints; unresolved material questions block `GS2-01`.
- [x] **GS2-00.9 — Decide runtime operations.** Select or reject a deployment boundary for the App/webhook
  host, including ownership, availability target, secrets, ingress verification, logs/metrics, upgrades,
  incident response, data retention, cost, and disaster recovery. If no acceptable host is approved,
  scheduled audits remain authoritative and `.github#2961` is removed from the cutover critical path by an
  explicit amendment rather than by shipping an unowned service.
  **Q0 decision recorded 2026-08-26 under delegated maintainer authority:** reject a hosted boundary for
  this cutover. The complete operational evidence has no accepted owner/service boundary, availability
  target, secret/ingress design, observability, upgrade and incident process, retention policy, cost
  envelope, or disaster-recovery proof. Scheduled complete audits therefore remain authoritative; events
  are an optional post-`OperatingV2` accelerator and `.github#2961` is not on the critical path.

  **Q0 acceptance evidence:** repair PR #3002 head
  `d07cc9daeef46f6f034e2e4cf23dcf3deeea6da0`, fingerprint
  `febaa98f354fcad88f50c4c17e7592f3d46d9e6c1d0c381831b6a705e4d68668`, exact role attestations
  ([architecture](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419411396),
  [security](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419418539),
  [operations](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419407467),
  [cross-repository](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419406435)), and the final
  [repair-phase confirmation](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419425110).
  Ratification evidence additionally requires structural SDD analysis with exact `noChange`,
  `coherent: true`, `implementationReady`, zero stale/generated-view findings, no diagnostics, and a clean
  tracked tree after all gates. The immutable
  [revision-4 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419558809)
  diagnoses the stale substring-only verifier at old head `b0b63507f115f0de9e488c9f68dcde22b6992c67`
  and authorizes repair; it does not close the finding. Commits
  [0ced1901](https://github.com/FS-GG/.github/commit/0ced1901) and
  [c9e82ef](https://github.com/FS-GG/.github/commit/c9e82ef) implement and seal the fix, while the independent
  [c9 architecture narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419650173)
  verifies that the stale-SDD defect is fixed and separately requires this provenance correction. The
  [revision-5 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419655595)
  and [cross-repository narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419636584)
  diagnose and authorize correction of the provenance contradiction; they make no future acceptance claim.
  Every changed head/fingerprint still requires fresh live role attestations.
  The later [revision-6 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419728195)
  and [operations narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419704123) diagnose
  stale downstream `analysis.json` digests and authorize repair; they are not acceptance. Q0 additionally
  hashes every evidence source snapshot and every top-level verify, ship, and governance-handoff source
  against current bytes and carries a stale-analysis inversion before asserting a clean tracked tree.
  The [revision-7 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419837439)
  and [architecture narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419807264)
  diagnose and authorize correction of source-set omission; they are not acceptance. Q0 requires exact,
  duplicate-free per-artifact label/path multisets and rejects malformed, missing, duplicate, unexpected,
  or stale rows, with independent omission, duplicate, and extra-row mutations.
  The [revision-8 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419940163)
  and [operations narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419915373)
  diagnose and authorize repair of the remaining source-row schema gap; they are not acceptance. Q0 now
  binds exact per-path kinds, required/allowed row and nested digest keys, integer schema version, current
  schema status where applicable, and one canonical lowercase SHA-256 representation per projection.
  Table-driven controls reject missing/wrong kind, missing/wrong schema or status, unexpected keys,
  alternate digest forms, malformed types, and invalid digest casing/length across evidence, verify, ship,
  and governance handoff while retaining all source-set and stale-byte inversions.
  Hosted [run 32925218156](https://github.com/FS-GG/.github/actions/runs/32925218156) / job
  [98048225279](https://github.com/FS-GG/.github/actions/runs/32925218156/job/98048225279) then proved that
  `author_association` is viewer-dependent for private organization membership: the maintainer view
  reported `MEMBER`, while the workflow token reported the same immutable comments as `CONTRIBUTOR` and
  rejected all four roles. Q0 therefore binds an exact unique GitHub User login allowlist into the signed
  fingerprint and accepts a User when either its live association is allowed or its login is allowlisted;
  Bots, missing users, and non-allowlisted `CONTRIBUTOR`/`NONE` records remain fail-closed.
  The later [revision-10 diagnosis](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420215570),
  [security evidence](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420187392), and
  [cross-repository evidence](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420206908) require
  one login grammar before both authorization routes: 1–39 ASCII characters, alphanumeric endpoints,
  and only alphanumerics or single internal hyphens. Empty, whitespace, leading/trailing/double hyphen,
  overlength, non-string, underscore, dot, and Unicode identities fail even with an allowed association.

### GS2-01 — Bootstrap `FS.GG.Coordination`

**Parent:** `.github#2963`
**Owner:** organization administrator, then `FS.GG.Coordination`
**Depends on:** GS2-00
**Exit:** independently buildable empty product and executable qualification skeleton

**Producer route:** ADR-0077's successor backend is implemented through
[Q1–Q3 of the Quint-first migration sequence](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md#7-prepared-feature-sequence).
`GS2-01.4` consumes that route's published artifact; the bootstrap worker must not recreate its extractor,
profile, compiled contract, or generic ITF machinery inside `FS.GG.Coordination`.

- [x] **GS2-01.1 — Complete repository provisioning.** Preserve the explicitly authorized README-only
  creation receipt, then apply least-privilege teams, branch ruleset, signed/immutable tag policy, secret
  scanning, dependency graph, Dependabot alerts, Actions policy, auto-merge policy, and default branch
  settings. Record the exact settings receipt; the early inert creation alone does not complete this unit.
- [x] **GS2-01.2 — Register the component.** Add the repository and ownership/release topology to the
  reviewed registry, architecture map, custom-property projection, GitHub App installation scope, and
  Coordination Project membership rules.
- [x] **GS2-01.3 — Establish the solution boundary.** Create packages/projects for protocol specification,
  pure core, GitHub adapters, CLI host, App/webhook host, qualification contracts, and tests. Enforce
  one-way dependencies and keep GitHub SDK/HTTP concerns out of the pure core.
- [x] **GS2-01.4 — Pin the published Quint-capable kernel.** Restore the exact published FS.GG.SDD artifact
  carrying the accepted Quint profile and compiled-contract boundary from the supported read feed, verify
  its identity and bundle digest, and prohibit source-project or checkout-relative references. An earlier
  bootstrap may temporarily pin P4 for non-semantic scaffolding, but that pin cannot qualify GS2-02. This
  unit is not ready until Q1 has succeeded and ADR-0077 has been amended with the accepted literate source,
  extraction, authority, fingerprint, and compatibility contract.
- [x] **GS2-01.5 — Establish custom CI.** Add only bootstrap qualification jobs: deterministic build,
  compiler/unit tests, dependency/security checks, package/install smoke, and evidence-manifest validation.
  Do not import v1 coordination completion gates.
- [x] **GS2-01.6 — Create the work skill.** Add `github-substrate-v2-work` with commands to inspect this
  roadmap, check unit prerequisites, create a unit evidence manifest, run the relevant Q gates, and stop at
  the unit boundary.
- [x] **GS2-01.7 — Create evidence storage.** Version schemas and directories for corpus inputs, external
  observations, independent oracles, generated cases, test results, artifact manifests, reviews, and
  accepted qualification receipts. Generated bulky output remains in immutable CI artifacts/releases;
  compact indexes and digests remain in git.
- [x] **GS2-01.8 — Prove bootstrap recovery.** A clean machine clones, restores, builds, tests, packs,
  installs, and validates an empty candidate using published dependencies only.
- **GS2-01.9 — Provision non-production runtime (not applicable to this cutover).** Accepted GS2-00.9
  rejected a hosted App/webhook boundary, so this historical conditional branch is not a pending checkpoint.
  If a future ratification accepts that boundary, it must provision development and qualification
  environments, deployment identity, secret rotation, observability, backup/recovery, and a kill switch
  before any production event subscription exists.

### GS2-02 — Implement the typed coordination specification

**Parent:** `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-01, including the final Quint-capable GS2-01.4 pin
**Exit gates:** Q1 and the pure portion of Q2

**Implementation references, in authority order:**

1. [ADR-0077](adr/0077-quint-first-typed-specification-authority.md) owns the authoring and authority
   decision, including the mandatory post-Q1 amendment.
2. The [Quint-first migration design](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md)
   owns literate extraction, the FS-GG Quint profile, compiled-contract boundary, ITF replay ownership, and
   producer-before-consumer sequence; its Q5 is the direct GS2 handoff.
3. The [Quint experiment](quint/README.md) and
   [assessment](quint/reports/assessment.md) are the executable baseline and recorded limitations, not
   production authority.
4. The [governing cutover design](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md)
   owns the coordination domain, proof obligations, authority boundaries, and cutover semantics that the
   Quint protocol must express.
5. Quint's [literate-specification documentation](https://quint.sh/docs/literate) defines the upstream
   workflow being qualified; only the pinned profile/toolchain accepted by ADR-0077 may enter CI.

- [x] **GS2-02.1 — Author the canonical literate Quint protocol.** Keep reviewer-oriented Markdown prose
  beside deterministically extracted, named Quint blocks that specify subjects, authorities, codecs, commands,
  events, mutations, projections, observation plans, settings profiles, evidence obligations, and version
  IDs under the published FS-GG Quint profile. Generate stable integration identities through the compiled
  contract; prose cannot add hidden semantics, extracted `.qnt` files cannot be edited independently, and
  no parallel F# protocol AST is permitted.
- [x] **GS2-02.2 — Implement authority bindings.** Model native GitHub, repository registry, protocol
  stream, git ledger, Actions, package feed, and other external authorities with explicit revision and
  completeness contracts.
- [x] **GS2-02.3 — Implement observations.** Distinguish observed, proven absent, contradictory,
  unreadable, unsupported, unauthorized, incomplete, stale, and rate-limited outcomes.
- [x] **GS2-02.4 — Implement lifecycle intent.** Separate human scheduling intent from claims, blockers,
  PR/review/delivery observations and derived lifecycle status.
- [x] **GS2-02.5 — Implement native relation algebra.** Represent parent/child and blocking relations as
  typed edge sets with idempotent add/remove intent rather than scalar replacement.
- [x] **GS2-02.6 — Implement protocol streams.** Type claim/lease/touch-set, operation-lock/election,
  review, delivery, and operation-receipt envelopes; classify ephemeral retention versus durable
  checkpointing.
- [x] **GS2-02.7 — Implement mutation algebra.** Cover create, append, add/remove edge, set, clear,
  transition, and compensate with expected revision, idempotency, and all terminal/uncertain outcomes.
- [x] **GS2-02.8 — Implement durable plans.** Compile decisions into ordered resumable steps with
  causation/correlation, receipt re-read, compensation boundary, and roll-forward classification.
- [x] **GS2-02.9 — Implement desired-state specifications.** Type issue schema, Projects, repository
  profiles, rulesets, workflow pins, permissions, releases, security, and supply-chain settings.
- [x] **GS2-02.10 — Implement compiled-contract outputs.** Derive schemas, command metadata, permission census,
  mutation census, settings plans, Markdown/JSON views, semantic diff, diagrams, and model-test inventory.
- [x] **GS2-02.11 — Prove deterministic identity.** Equivalent literate Quint authoring forms extract and
  normalize identically; semantic changes produce stable, reviewable diffs; prose-only changes cannot alter
  behavioral identity; unsupported source, extractor, Quint, profile, or schema versions fail before execution.

### GS2-03 — Build the independent qualification system

**Parent:** `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-02.1–GS2-02.8
**Exit gates:** Q1, Q2, Q6, and Q7 harness capability

- [x] **GS2-03.1 — Define the qualification manifest.** Bind source, model, compiler, dependencies,
  generated cases, independent cases, external fixtures, package bytes, environment, results, and reviewers.
- [x] **GS2-03.2 — Import the frozen corpus.** Preserve original bytes, provenance, expected behavior,
  ambiguity, and current-v1 result; never normalize away the defect being tested.
- [x] **GS2-03.3 — Add generated structural tests.** From the qualified Quint source and compiled contract,
  derive vocabulary completeness, transition coverage, command/mutation registration, permission coverage,
  schema round-trip, and projection freshness cases without creating a second behavioral model.
- [x] **GS2-03.4 — Add independent black-box oracles.** Hand-author tests for claim exclusion, stale
  projections, dependency set concurrency, partial operations, old-client fencing, ledger rewind/tamper,
  exact-head review, post-merge verification, and dual-feed release recovery. Include independently authored
  scale and abstraction oracles that force existing and future Quint roots to preserve required outcomes and
  anti-vacuity witnesses across bounded concrete-versus-abstract comparisons; record root closure, dependency
  depth, state/sample counts, elapsed time, peak memory, and artifact volume against runner-calibrated budgets.
  **Accepted 2026-08-29:** protected-main merge
  [`17a6f0e`](https://github.com/FS-GG/FS.GG.Coordination/commit/17a6f0e48f79356cbfd673c8e0e8f5bb5f3efd30)
  and exact-merge [qualification run 33273274618](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33273274618)
  completed the single fresh repair phase. The typed
  [repair-phase record](https://github.com/FS-GG/FS.GG.Coordination/pull/93#issuecomment-5464629983), fresh
  [successor pass](https://github.com/FS-GG/FS.GG.Coordination/pull/93#issuecomment-5464669577), and
  [host acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/93#issuecomment-5464677128) bind exact
  head `0e3de0aaac9fc2f95a1625ad3f43e0f2ee90a455`; source-derived executable closure and reproduced
  digest-pinned Quint typechecking close both terminal escalation findings with no accepted exception.
- [x] **GS2-03.5 — Add native Quint model/property/formal tests.** Run examples, simulation, reachability
  witnesses, safety properties, temporal liveness checks, and bounded model checking over claim/election,
  relation mutation, lifecycle, operation saga, epoch, and rollback state spaces, retaining reproducible
  Quint/ITF counterexamples.
  **Accepted 2026-08-30:** protected-main merge
  [`893997af`](https://github.com/FS-GG/FS.GG.Coordination/commit/893997af87cb723c06cb9e1865c56165f28e22ed)
  and exact-merge [qualification run 33282988853](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33282988853)
  accepted exact candidate `aa2f8d165aeeb107c210ed231a9702f69c0e76a6`. The fresh
  [successor pass](https://github.com/FS-GG/FS.GG.Coordination/pull/97#issuecomment-5465677710) and
  [host acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/97#issuecomment-5465697691) bind a
  clean-checkout SDD projection with all 24 tracked observations verified, six reproducible temporal
  transition-removal counterexamples with retained Quint/ITF/trace/manifest/receipt digests, and
  runner-calibrated hosted elapsed budgets. All findings closed without exception.
- [x] **GS2-03.6 — Add fault injection.** Fail before and after every external step; lose responses;
  duplicate/reorder events; return partial pages; exhaust rate budgets; revoke permission; mutate concurrent
  revisions; and require convergence or typed refusal.
  **Accepted 2026-08-30:** protected-main merge
  [`df8e7d29`](https://github.com/FS-GG/FS.GG.Coordination/commit/df8e7d290c9990087ada6c2cd62e859db976f0ee),
  exact-merge [Bootstrap qualification run 33287672800](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33287672800),
  and [push CodeQL run 33287672761](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33287672761)
  accepted exact candidate `5b5b1fd82a47e1bc14dcc9c94b72307c61c76b6d`. The fresh
  [successor pass](https://github.com/FS-GG/FS.GG.Coordination/pull/101#issuecomment-5466125131) and
  [host acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/101#issuecomment-5466191671) bind
  15 deterministic executions across the source-derived Inspect, Plan, Apply, and Verify boundary: 11
  converge and four return accepted typed refusals. Seven subject-defect and nine artifact inversions fire;
  independent final-state comparison rejects duplicate and reorder corruption even when their trace labels
  are restored to the healthy labels. All findings closed without exception.
- [x] **GS2-03.10 — Amend and requalify the remaining-migration architecture.** Implement the
  [2026-08-30 review](coordination/2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md):
  replace comment-order concurrency authority with sharded expected-parent Git journals and fencing
  generations; make one reconciler the normal write path; bind full snapshot epochs; add audit repair;
  change cutover to open, observe, then contract; and requalify every affected GS2-02 contract with
  independent counterexamples and black-box negative controls. This unit blocks GS2-03.7 and all later
  implementation; historical receipts do not qualify the amended candidate. Track the bounded work and
  fresh review on [`.github#3075`](https://github.com/FS-GG/.github/issues/3075).
  **Accepted 2026-08-30:** [Coordination PR 109](https://github.com/FS-GG/FS.GG.Coordination/pull/109)
  merged as protected-main commit
  [`624ae14f`](https://github.com/FS-GG/FS.GG.Coordination/commit/624ae14f9a1d9d4baf92045a2357fc549d274f04)
  after [exact-head hosted qualification run 33331721108](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33331721108)
  accepted candidate `a45837654be041ee8366bb857266005e5eb13c10`, exact source
  `7d6755e0e723796eb30486451cb3610e6a74874f26055a3c382986ce525d3218`,
  and compiled contract `947262bc9f70c371d79a917804d2ed4adcabbb1cc2ff683eedc637e36e6b163e`.
  The canonical non-refresh replay is green in 593,599 ms with result
  `d68b2c8a25bff5174cab55cb5292d382a1f577cdf807739ba759705c42fbd33b` across seven bounded roots, eleven formal
  scenarios, 8 positive invariants, and 126 catalogue-derived negative controls. The journal race/fencing
  model explores distinct retry and fencing properties over 20 states/48 transitions; reconciliation
  explores 51/163 with total webhook loss, duplicate/reordered hints, mid-read authority change, and audit
  restart; review epochs explore 10/22 including a valid current effect and rejected stale effect; and
  evidence-rich observation-gated contraction explores 136/532 across clock, success, freshness, and
  snapshot binding. Under [ADR-0079](adr/0079-single-accountable-delivery-authority.md), these checks and
  critique records are evidence for one accountable acceptance decision; no external or multiple
  authorization is required. Every affected
  safety mutant fails and every removed-step temporal counterexample reproduces with retained deterministic
  ITF, Quint trace, and manifest digests. Protected branches in a dedicated authority repository are now
  the durable CAS journal, using exact old-OID Git receive-pack leases;
  winning generations fence effects, comments/webhooks are projections or hints, and complete audits repair
  loss. Qualification preflights all projection witnesses before expensive TLC work, derives rejection
  coverage as `71 + 5 × formal scenarios`, and reconciles the labeled 186 external / 161 Quint / 47 verify
  launch inventory through an atomic concurrent observer. After the second related late-stage verifier
  defect, the Accountable Delivery Owner paused retries, traced the shared default Apalache endpoint as the
  ordering hazard, isolated every compile/verify invocation on a unique endpoint, retained full stdout/stderr
  failure evidence, and reran the complete corpus. The hosted gate passed in 1,222 seconds; no exception or
  retry-only acceptance was used. The owner bound the exact commit and evidence identities; another person,
  account, or agent was not an authorization gate.
  **Provisioning update 2026-08-30:** the public
  [`FS-GG.Coordination.Authority`](https://github.com/FS-GG/FS.GG.Coordination.Authority) repository now
  exists as repository `1351660651`. Active ruleset `v2-journal-writer` (`21872113`) restricts create/update
  and bypasses only App `4166418`; active `v2-journal-integrity` (`21872115`) independently rejects
  deletion/non-fast-forward with no bypass actors. Both use the corrected GitHub fnmatch
  `refs/heads/fsgg/v2/journal/**/*`. Live rule suite `3875208315` proves a human administrator cannot
  create a matching ref, and effective-rule readback returns all four rules. The first `/**` target was
  found by the negative control to match nothing, removed, and replaced before any authority opened.
  The policy-owner qualification workflow landed through [`.github` PR 3076](https://github.com/FS-GG/.github/pull/3076)
  and [run 33330220225](https://github.com/FS-GG/.github/actions/runs/33330220225) proved App
  creation/fast-forward CAS plus stale, rewrite, and deletion rejection without exposing the App private key
  to the authority repository. The single-accountable acceptance contract then landed through
  [`.github` PR 3078](https://github.com/FS-GG/.github/pull/3078) and shipped in coherent set
  [`0.77.0`](https://github.com/FS-GG/.github/releases/tag/coherent-set%2Fv0.77.0), content
  `sha256:ec52502830d351b841dc64fe1833a9640991a413cfd6230e0d2ecce9f6115711`.
- [x] **GS2-03.7 — Add reproducibility and supply-chain checks.** Build twice in independent clean
  environments and compare package bytes, designate one candidate byte set, verify provenance and SBOM
  predicates separately, publish those identical candidate bytes to every allowed pre-production feed,
  then download and install them from isolated feeds in clean consumers with repository-local empty caches.
  Retain package, symbol, SBOM, attestation, served-download, and installed-assembly digests.
  **Accepted 2026-08-30:** protected candidate
  [`ea9781c`](https://github.com/FS-GG/FS.GG.Coordination/commit/ea9781c89d169eec2e4f6aad004acc3a764d59d7)
  passed [hosted run 33336821654](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33336821654),
  which packed once, published only version `0.0.0-gs2-03-7.ea9781c89d16` to the candidate channel,
  compared served bytes, and executed two clean consumers. The retained Actions artifact is bound by
  repository/run/artifact IDs and zip `sha256:592cfd8ece9ce30a2430e81425ae08ee241e43366c91e0b484dc3bf5d0541ade`;
  package, canonical symbol package, portable PDB, installed assembly, SPDX SBOM, provenance, and served
  attestation digests are recorded in the
  [accepted receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/9341a5a299bfd2cb90c5ea9b1e2355d34297ab4e/evidence/github-substrate-v2/accepted/GS2-03.7.json),
  merged through [Coordination PR 112](https://github.com/FS-GG/FS.GG.Coordination/pull/112).
- [x] **GS2-03.8 — Add critique evidence gates.** Architecture, security, adapter, migration, and cutover
  perspectives produce findings against exact candidate fingerprints. The Accountable Delivery Owner may
  perform every perspective under distinct phase identities and makes the sole acceptance decision; a green
  roll-up must still be derived from the bound evidence rather than asserted in prose.
  **Accepted 2026-08-31:** protected implementation
  [`2427478b`](https://github.com/FS-GG/FS.GG.Coordination/commit/2427478b6fffba470e86ff46cf2ca22106a11a6d)
  introduced the typed `critique-evidence/1` generator/validator, executable reviews/v2 schema, closed
  five-perspective inventory, distinct phase identities, exact candidate/evidence/content bindings, and
  derived `all-required-bound-green/1` roll-up. [PR 116](https://github.com/FS-GG/FS.GG.Coordination/pull/116)
  passed [full hosted qualification 33342488041](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33342488041)
  and CodeQL; protected main then reused the identical complete tracked tree and revalidated the merge.
  The Accountable Delivery Owner retained five separately hashed perspective findings and an executable
  acceptance harness that checks the PR-execute/protected-main-reuse relationship, Q7 gate inventory,
  exact tree/unit identities, and every hosted conclusion before regenerating the canonical
  [critique bundle](https://github.com/FS-GG/FS.GG.Coordination/blob/4c7878623f2fc73c4a5d8eb55697d159e6e08e86/evidence/github-substrate-v2/reviews/GS2-03.8.json).
  Acceptance [PR 117](https://github.com/FS-GG/FS.GG.Coordination/pull/117) passed
  [hosted run 33343928942](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33343928942), including
  reproduction of the actual bundle; protected-main [run 33344891169](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33344891169)
  revalidated acceptance merge `4c787862` while skipping all six expensive lanes. The append-only
  [accepted receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/4c7878623f2fc73c4a5d8eb55697d159e6e08e86/evidence/github-substrate-v2/accepted/GS2-03.8.json)
  has self-digest `67e73228b0c6ef658b1294314e75e2bc62021f070f1c446b0ffdbda19834e116`.
  One Accountable Delivery Owner made the sole decision; no reviewer count, external account, or additional
  authorization was required. The observed evidence-only acceptance route still paid one full formal run;
  that selector-granularity cost is retained for a later CI architecture improvement rather than hidden.
- [x] **GS2-03.9 — Prove the harness can fail.** Mutation-test or invert every gate class so a vacuous,
  absent, stale, truncated, forged, or generated-only evidence set is red.
  **Accepted 2026-08-31:** protected implementation
  [`53f0338d`](https://github.com/FS-GG/FS.GG.Coordination/commit/53f0338dea988fd79b95092286709df7c0fb4745)
  introduced the typed `harness-mutation-proof/1` boundary, closed ten-gate and six-mutation inventories,
  ten healthy controls, and the derived sixty-cell negative Cartesian matrix. Every cell is created by the
  generator, executes the production qualification-manifest validator, records its actual stable diagnostic,
  and is regenerated during validation; callers cannot assert outcomes, coverage counts, or a hand-picked
  subset. Valid-hash forgery is resealed through every dependent manifest digest and rejected only against
  the independently frozen healthy baseline. Generated-case producers are also red when they are the sole
  provenance for any non-generated evidence class. [Implementation PR 120](https://github.com/FS-GG/FS.GG.Coordination/pull/120)
  passed [full qualification run 33348201595](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33348201595)
  and protected main reused the identical subject through
  [run 33349197336](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33349197336).

  The first hosted candidate exposed an opaque TLC transition-removal result after a prior related formal
  failure. Under the standard second-defect rule, the owner stopped retries, inventoried the complete formal
  result boundary, and reproduced the exact relation model independently. The model still produced its
  two-state liveness counterexample, while the runner had conflated semantic green, tool exit, output-protocol
  drift, and nondeterminism into one message. The class-level repair now retains exit code/stdout/stderr for
  every simulation-measurement, transition-removal, and counterexample-marker failure and hashes both
  projection and temporal-diagnostic reproductions when they differ. It changes no success predicate, lane,
  or ordering edge. The repaired local canonical run passed Q1/Q2 with 8 positive invariants, 126 rejected
  controls, 11 reproducible formal counterexamples, and the exact 186 external / 161 Quint / 47 verify
  process census before the complete hosted run passed.

  Independent [acceptance PR 121](https://github.com/FS-GG/FS.GG.Coordination/pull/121) retained exact Q7,
  hosted formal, PR-execute, protected-main-reuse, and four-run observations; five separately hashed critique
  perspectives; a deterministic combined digest over both validator source files; and the canonical
  [mutation proof](https://github.com/FS-GG/FS.GG.Coordination/blob/d7029dd485419e19a3b6b6491932b39b7e29ecba/evidence/github-substrate-v2/mutation-proofs/GS2-03.9.json)
  (`sha256:4585fb2f68700dd8d8f0a470a55591fc0d5b6e8a31d2936ff2388fe655204060`).
  Acceptance passed [full run 33349742847](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33349742847)
  and merged as protected commit
  [`d7029dd4`](https://github.com/FS-GG/FS.GG.Coordination/commit/d7029dd485419e19a3b6b6491932b39b7e29ecba);
  protected-main [run 33350667104](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33350667104)
  then revalidated the identical acceptance subject. The append-only
  [accepted receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/d7029dd485419e19a3b6b6491932b39b7e29ecba/evidence/github-substrate-v2/accepted/GS2-03.9.json)
  has self-digest `c5b0bf313583e26dc6a2f471b58e22d6315f4ff425d05cf6f74070c45c5ecde2`.
  One Accountable Delivery Owner made the decision; no external, multiple, or approval-count authorization
  was required, and every declared technical predicate remained fail-closed.

### GS2-04 — Implement GitHub authority adapters and interpreters

**Parent:** `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-02; GS2-03 manifest contract
**Exit gates:** Q3 and Q4

- [x] **GS2-04.1 — Transport foundation.** Implement typed REST/GraphQL requests, response envelopes,
  retries that respect idempotency, ETags/revisions, rate budgets, pagination, node/connection completeness,
  API-version headers, redaction, and deterministic fixture capture.

  Accepted 2026-08-31. [Implementation PR 124](https://github.com/FS-GG/FS.GG.Coordination/pull/124)
  bound typed REST and GraphQL transport, idempotency-aware retry decisions, revisions and rate facts,
  pagination completeness, redaction, and deterministic fixtures to exact candidate
  `15737cfd698216fb5a232178eb5e2f36233efb4b`. The candidate passed
  [full qualification run 33358807559](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33358807559)
  and merged as protected commit
  [`3182fccc`](https://github.com/FS-GG/FS.GG.Coordination/commit/3182fcccf81ad3e519624ad12cbd7f7ce5c3b66a),
  whose [protected-main run 33360184506](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33360184506)
  revalidated the identical subject. Qualification comprised 34 focused unit tests, 258 architecture tests,
  and fourteen Q3 positive/inversion controls. The hosted candidate also exposed an implicit Apalache server
  lifecycle assumption; the repair made server start, readiness, verification, and teardown explicit so the
  canonical formal lane passed without weakening a property.

  [Acceptance PR 126](https://github.com/FS-GG/FS.GG.Coordination/pull/126) added the append-only
  [GS2-04.1 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/dbaecade8d47d872336601b3b3bb9785082dbb81/evidence/github-substrate-v2/accepted/GS2-04.1.json)
  with self-digest `867e1d9842f0821be0af1bacfc175ef23da1351f22bb353de6ffb3671229a58e`
  after [full run 33360908095](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33360908095),
  including canonical Quint, passed. It merged at
  [`dbaecade`](https://github.com/FS-GG/FS.GG.Coordination/commit/dbaecade8d47d872336601b3b3bb9785082dbb81)
  and [protected-main run 33362412616](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33362412616)
  passed. The implementation completion digest is
  `1e1f1b894b9449ab8fa1bacfc175ef23da1350f33bed09915ff55b09fcec88f`; the acceptance
  [completion receipt](https://github.com/FS-GG/FS.GG.Coordination/issues/125#issuecomment-5474420007)
  is `a7eb421bff718dc81cef9500f17ebf4d46bfefa7612e3215e4bc225550950b3c`.

  The landing exposed a second class boundary: the released delivery client sent an exact head but relied on
  GitHub's implicit merge-commit default, while the repository was squash-only. The safe exact-head squash
  fallback landed this accepted candidate; the standard second-defect response then stopped retries and
  redesigned delivery to observe typed merge capabilities, choose deterministically `squash > rebase > merge`,
  serialize the method explicitly, and refuse before any write when policy is unreadable or permits none.
  The class repair is tracked by [`.github#3091`](https://github.com/FS-GG/.github/issues/3091) and
  [PR 3092](https://github.com/FS-GG/.github/pull/3092). One Accountable Delivery Owner made every decision;
  no external, multiple, or approval-count authorization was required.

- [x] **GS2-04.2 — Issue/type/field adapter.** Resolve semantic identities to live IDs, verify type and
  option sets, read complete values, and plan guarded create/update/clear operations.
- [x] **GS2-04.3 — Native relation adapter.** Read complete hierarchy/dependency sets and perform
  add/remove with stale re-read and post-state verification.

  Accepted 2026-08-31. [Implementation PR 138](https://github.com/FS-GG/FS.GG.Coordination/pull/138)
  passed [exact-head qualification run 33399640438](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33399640438)
  and merged as protected commit
  [`ee0353ac`](https://github.com/FS-GG/FS.GG.Coordination/commit/ee0353acc753fb33d02ff3addeeb03bb1b2b2c4c),
  whose [protected-main run 33400527635](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33400527635)
  passed. [Acceptance PR 140](https://github.com/FS-GG/FS.GG.Coordination/pull/140) added the append-only
  [GS2-04.3 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/c524f8410323e7a79a6bab4ad691a99e75f530e1/evidence/github-substrate-v2/accepted/GS2-04.3.json)
  with self-digest `210ae250d081c39ec422d59c9bd4a72c653650380497aef586164b6fc3a52507`,
  merged at [`c524f841`](https://github.com/FS-GG/FS.GG.Coordination/commit/c524f8410323e7a79a6bab4ad691a99e75f530e1),
  and passed [protected-main run 33402803310](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33402803310).

- [x] **GS2-04.4 — Project adapter.** Treat membership and Status as projections; handle archived,
  duplicated, external, draft, missing, and unreadable items without inventing absence.

  Accepted 2026-08-31. [Implementation PR 144](https://github.com/FS-GG/FS.GG.Coordination/pull/144)
  passed [exact-head qualification run 33409563736](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33409563736)
  with canonical Quint reused, and merged as protected commit
  [`36abdce0`](https://github.com/FS-GG/FS.GG.Coordination/commit/36abdce0a907b0acd3d8ace1f4ac6d6491f4e080),
  whose [protected-main run 33410269364](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33410269364)
  passed. [Acceptance PR 146](https://github.com/FS-GG/FS.GG.Coordination/pull/146) added the append-only
  [GS2-04.4 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/62396de0b38a13fd8f2cb5fec7ba9e8e42770823/evidence/github-substrate-v2/accepted/GS2-04.4.json)
  with self-digest `a2a71df6fe5f871c8f40b353c161b58fbb2206643b02dcb3ef5987802a82c252`,
  merged at [`62396de0`](https://github.com/FS-GG/FS.GG.Coordination/commit/62396de0b38a13fd8f2cb5fec7ba9e8e42770823),
  and passed [protected-main run 33412110063](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33412110063).

- [x] **GS2-04.5 — Comment/projection adapter.** Preserve server-issued identity/order, validate marker
  JSON and referenced journal digests, distinguish edit/delete/tamper, and regenerate human projections
  from durable authority. A comment never authorizes a concurrency-sensitive transition.

  Accepted 2026-08-31. [Implementation PR 150](https://github.com/FS-GG/FS.GG.Coordination/pull/150)
  passed [exact-head qualification run 33433701416](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33433701416)
  and merged as protected commit
  [`76782e3f`](https://github.com/FS-GG/FS.GG.Coordination/commit/76782e3f86646f2303d0aad90b28d27198a4f7ef),
  whose [protected-main run 33434459359](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33434459359)
  passed. [Acceptance PR 152](https://github.com/FS-GG/FS.GG.Coordination/pull/152) added the append-only
  [GS2-04.5 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/a8e6c061918cc351de76d7a42be4f1c2a1792686/evidence/github-substrate-v2/accepted/GS2-04.5.json)
  with self-digest `27b27b76cf52fca137059aa466c7922dc096cae33c0a4e1045735bd937497091`,
  merged at [`a8e6c061`](https://github.com/FS-GG/FS.GG.Coordination/commit/a8e6c061918cc351de76d7a42be4f1c2a1792686),
  and passed [protected-main run 33436270111](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33436270111).

- [x] **GS2-04.6 — Sharded Git journal adapter.** Perform protected expected-parent commits for claim,
  review, operation, and global cutover aggregates in the dedicated `FS.GG.Coordination.Authority`
  repository. Use `refs/heads/fsgg/v2/journal/<kind>/<shard>`, explicit old-OID
  `--force-with-lease` receive-pack CAS, the split `v2-journal-writer` and `v2-journal-integrity`
  rulesets targeting `refs/heads/fsgg/v2/journal/**/*`, and a repository-limited journal App token;
  read back both rulesets and effective branch rules.
  Issue monotonically increasing fencing generations; verify ancestry; create immutable phase anchors;
  detect rewind/deletion/divergence; compact only after a terminal checkpoint; and project state to issues
  without making one fleet-wide normal-operation ref. Custom refs and API-path-scoped bypass claims fail.

  Accepted 2026-09-01. [Implementation PR 156](https://github.com/FS-GG/FS.GG.Coordination/pull/156)
  passed [exact-head qualification run 33443440969](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33443440969)
  and merged as protected commit
  [`b6c25c0b`](https://github.com/FS-GG/FS.GG.Coordination/commit/b6c25c0b3f26211d4cfcfcdc8f08f9b87e43586c),
  whose [protected-main run 33444027453](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33444027453)
  passed. [Acceptance PR 158](https://github.com/FS-GG/FS.GG.Coordination/pull/158) added the append-only
  [GS2-04.6 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/53fe450f8d60fdfe4cc68aaaeaa181666796e31a/evidence/github-substrate-v2/accepted/GS2-04.6.json)
  with self-digest `4011bbe0d7e2db27aff2b6e0a36d9bc342dc89e8310b56d4e358b6d73bc96511`,
  merged at [`53fe450f`](https://github.com/FS-GG/FS.GG.Coordination/commit/53fe450f8d60fdfe4cc68aaaeaa181666796e31a),
  and passed [protected-main run 33445770572](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33445770572).
- [x] **GS2-04.7 — Repository/settings adapter.** Inspect and plan custom properties, rulesets, merge
  policies, Actions policy, environments, releases, tags, security, and dependency features with supported,
  unauthorized, and unavailable outcomes.

  Accepted 2026-09-01. [Implementation PR 162](https://github.com/FS-GG/FS.GG.Coordination/pull/162)
  merged as protected commit
  [`99dd3ca2`](https://github.com/FS-GG/FS.GG.Coordination/commit/99dd3ca27df05dc65ffea2b1c513c0423460f51a),
  whose [protected-main run 33450426877](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33450426877)
  passed. [Acceptance PR 164](https://github.com/FS-GG/FS.GG.Coordination/pull/164) added the append-only
  [GS2-04.7 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/89d0dda060efbe28d2dd25ad5a475fe865392b6f/evidence/github-substrate-v2/accepted/GS2-04.7.json)
  with self-digest `5a54e22d217044a4b5cc2424c8d7e8e24f86dbbe8c6d6373c0c487819a930acf`,
  merged at [`89d0dda0`](https://github.com/FS-GG/FS.GG.Coordination/commit/89d0dda060efbe28d2dd25ad5a475fe865392b6f),
  and passed [protected-main run 33451465408](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33451465408).
- [x] **GS2-04.8 — Actions/release/feed adapter.** Observe runs/checks/merge groups, immutable releases,
  attestations, packages, and public downloads without treating upload responses as served artifacts.

  Accepted 2026-09-01. [Implementation PR 171](https://github.com/FS-GG/FS.GG.Coordination/pull/171)
  merged as protected commit
  [`42465b95`](https://github.com/FS-GG/FS.GG.Coordination/commit/42465b9514ac460fab228464641a9f918232c624),
  whose [protected-main run 33457032627](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33457032627)
  passed. [Acceptance PR 173](https://github.com/FS-GG/FS.GG.Coordination/pull/173) added the append-only
  [GS2-04.8 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/cd8d41417aff13a4248dfaa414b31c31e918aeb8/evidence/github-substrate-v2/accepted/GS2-04.8.json)
  with self-digest `9f80d0048b06a7ca3c06491ee4c1e6f2baad6bf704f763b7ed018ff43b62e19b`,
  corrected and merged at [`cd8d4141`](https://github.com/FS-GG/FS.GG.Coordination/commit/cd8d41417aff13a4248dfaa414b31c31e918aeb8),
  and passed [protected-main run 33459978204](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33459978204).
- [x] **GS2-04.9 — Sandbox qualification and comprehensive GS2-04 closure.** Exercise destructive create/update/delete/rollback behavior in
  isolated test repositories and a test Project using non-production identities and quotas.
  Bind every accepted GS2-04 child receipt, execute Q3/Q4 and all repository qualification gates cold under
  ADR-0080 comprehensive mode, and retain the protected GS2-04 closure receipt before GS2-05 may consume the
  adapter milestone.

  Accepted 2026-09-01. [Implementation PR 179](https://github.com/FS-GG/FS.GG.Coordination/pull/179)
  merged as protected commit
  [`b0a2a8eb`](https://github.com/FS-GG/FS.GG.Coordination/commit/b0a2a8eb6827fd66cd04f20c7356080074e35289),
  whose comprehensive [protected sandbox run 33467333141](https://github.com/FS-GG/.github/actions/runs/33467333141)
  passed all eight cold Q3 adapters and the Q4 closure with complete cleanup. [Acceptance PR 181](https://github.com/FS-GG/FS.GG.Coordination/pull/181)
  added the append-only
  [GS2-04.9 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/5cc9db538c9c8a43e38ee7991f2211bbec75cbaa/evidence/github-substrate-v2/accepted/GS2-04.9.json)
  with self-digest `11defafd12353bbcb9b96cc06d3d9e29553ddca4ba912bacd7476c067f9802ed`,
  merged at [`5cc9db53`](https://github.com/FS-GG/FS.GG.Coordination/commit/5cc9db538c9c8a43e38ee7991f2211bbec75cbaa),
  and passed [protected-main run 33468604425](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33468604425).

### GS2-05 — Implement the native work and roadmap model

**Parents:** `.github#2954`, `.github#2960`, `.github#2963`
**Owner:** `FS.GG.Coordination`, with `.github` schema instances
**Depends on:** GS2-02–GS2-04
**Exit:** complete work lifecycle passes Q2–Q5 without production writes

- [x] **GS2-05.1 — Finalize taxonomy.** Ratify native issue types and eliminate parallel Class/Kind
  authority. Define exact migration for every current combination and refuse ambiguity.

  Accepted 2026-09-01. [Implementation PR 185](https://github.com/FS-GG/FS.GG.Coordination/pull/185)
  merged as protected commit
  [`c2cdc9d8`](https://github.com/FS-GG/FS.GG.Coordination/commit/c2cdc9d8126ed65874616afd0877d5f0a0c59368),
  whose [protected-main run 33474575558](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33474575558)
  passed. [Acceptance PR 187](https://github.com/FS-GG/FS.GG.Coordination/pull/187) added the append-only
  [GS2-05.1 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/d61ef112875bdfa74223c2596776dc3bdcaf77e5/evidence/github-substrate-v2/accepted/GS2-05.1.json)
  with self-digest `6a2c26033946b986b25fc4fc99257d21d6fa5d728e1f365170d9bc17a9226df5`,
  merged at [`d61ef112`](https://github.com/FS-GG/FS.GG.Coordination/commit/d61ef112875bdfa74223c2596776dc3bdcaf77e5),
  and passed [protected-main run 33475728297](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33475728297).
- [x] **GS2-05.2 — Finalize organization issue fields.** Specify scheduling, hold reason, priority,
  effort, dates, severity, phase, workstream, contract, and touch-set projection with minimal vocabularies.

  Accepted 2026-09-01. [Implementation PR 191](https://github.com/FS-GG/FS.GG.Coordination/pull/191)
  merged as protected commit
  [`775bb0d3`](https://github.com/FS-GG/FS.GG.Coordination/commit/775bb0d350119c370089710f1847fe7609b080a8),
  whose [protected-main run 33480138353](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33480138353)
  passed. [Acceptance PR 193](https://github.com/FS-GG/FS.GG.Coordination/pull/193) added the append-only
  [GS2-05.2 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/5e0dca1e2c8daf53f7ed745be1d947a4d3fc8b43/evidence/github-substrate-v2/accepted/GS2-05.2.json)
  with self-digest `a8474e696d2c1ff149ec1efb6a4c4b4cb6fe6e56b86ec840871b4430864f0a50`,
  merged at [`5e0dca1e`](https://github.com/FS-GG/FS.GG.Coordination/commit/5e0dca1e2c8daf53f7ed745be1d947a4d3fc8b43),
  and passed [protected-main run 33482054038](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33482054038).
- [x] **GS2-05.3 — Implement intake.** Provide pure validate/plan, exact-plan apply, and live inspect for
  issues, fields, Project membership, hierarchy, dependencies, and protocol initialization.

  Accepted 2026-09-01. [Implementation PR 197](https://github.com/FS-GG/FS.GG.Coordination/pull/197)
  merged as protected commit
  [`d99f8033`](https://github.com/FS-GG/FS.GG.Coordination/commit/d99f803330bf42aee7238022928b8899d0de2cae),
  whose [protected-main run 33490893417](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33490893417)
  passed. [Acceptance PR 199](https://github.com/FS-GG/FS.GG.Coordination/pull/199) added the append-only
  [GS2-05.3 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/a0ddf90f49ba406b7242dc7357573de109ba223d/evidence/github-substrate-v2/accepted/GS2-05.3.json)
  with self-digest `f5ac79b55dfa001903a4173209f09a71e7265641f5891c6498c65ce395364be0`,
  merged at [`a0ddf90f`](https://github.com/FS-GG/FS.GG.Coordination/commit/a0ddf90f49ba406b7242dc7357573de109ba223d),
  and passed [protected-main run 33493517326](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33493517326).
- [x] **GS2-05.9 — Implement staged intake admission.** Preserve GS2-05.3's sealed
  validate/plan/apply/inspect, explicit create-or-reuse identity, idempotent recovery, and authoritative
  readback while separating item-local Backlog capture from Ready promotion. Capture accepts an explicitly
  unknown root cause, deferred verification detail, and an unspecified touch set; it may perform at most six
  authority-read operations and six mutation operations in its sealed plan, independent of total Project and
  Backlog cardinality, and may not traverse or retriage unrelated work or invoke organization-wide
  reconciliation. Ready promotion requires the complete phase-local touch set, verification contract,
  dependencies, route decision, native type and fields, and all other schedulability inputs. Claim and PR
  evidence remain later lifecycle outputs and therefore cannot be prerequisites for Ready. Event-targeted,
  scheduled, or explicitly requested maintenance owns global reconciliation. Preserve unknown and partial
  observations, report every missing promotion input, keep `fsgg.coord.intake/v1` transaction guarantees
  during migration, and prove the fixed budget and phase boundary with cardinality-growth and inversion
  fixtures. This inserted unit uses the next unused stable `GS2-NN.N` identity while remaining ordered before
  GS2-05.4. Use the [filing's measured baseline](https://github.com/FS-GG/.github/issues/3134) of 35
  mechanical projection repairs and 10 unrelated delivery-route refusals incurred during one admission.
  Receiver: [FS.GG.Coordination#200](https://github.com/FS-GG/FS.GG.Coordination/issues/200).
  [Implementation PR 201](https://github.com/FS-GG/FS.GG.Coordination/pull/201) merged the registered
  contract and qualified implementation as
  [`30474c05`](https://github.com/FS-GG/FS.GG.Coordination/commit/30474c0553073765c54b1b89f5cc93bb511d07e3),
  then passed [protected-main run 33504664191](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33504664191).
  [Acceptance PR 203](https://github.com/FS-GG/FS.GG.Coordination/pull/203) added the append-only
  [GS2-05.9 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/f069df4da305f3976b7df65a8fb3dccf20b42bec/evidence/github-substrate-v2/accepted/GS2-05.9.json)
  with self-digest `59398e603e39b04ff6d971ef923d19513e03d3990a970323add90cf7ce593861`,
  merged at [`f069df4d`](https://github.com/FS-GG/FS.GG.Coordination/commit/f069df4da305f3976b7df65a8fb3dccf20b42bec),
  and passed [protected-main run 33507525817](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33507525817).
- [x] **GS2-05.4 — Implement roadmap intake.** Compile a roadmap into an Epic, bounded work issues,
  parent edges, dependency edges, dates/fields, and drift inspection without making Project fields the
  execution ledger. Its registered prerequisites must include the accepted GS2-05.9 receipt.

  Accepted 2026-09-01. [Implementation PR 205](https://github.com/FS-GG/FS.GG.Coordination/pull/205)
  merged as protected commit
  [`e036e8ed`](https://github.com/FS-GG/FS.GG.Coordination/commit/e036e8edfa35985644528511e8f9fe0a31af8ce7),
  whose [protected-main run 33544243766](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33544243766)
  passed. [Acceptance PR 207](https://github.com/FS-GG/FS.GG.Coordination/pull/207) added the append-only
  [GS2-05.4 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/2ebca83bd3df4dcbcacba8c23c4d0ec53f96c6b2/evidence/github-substrate-v2/accepted/GS2-05.4.json)
  with self-digest `0017ef59099ee14e6c3d0df73b4fb05a9c45a34f2067cecdf19a4b29e0a7a0fe`,
  merged at [`2ebca83b`](https://github.com/FS-GG/FS.GG.Coordination/commit/2ebca83bd3df4dcbcacba8c23c4d0ec53f96c6b2),
  and passed [protected-main run 33547250026](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33547250026).
- [x] **GS2-05.5 — Implement claims/touch sets.** Use protected journal expected-parent CAS plus a fencing
  generation for each claim and conflict domain; treat lease expiry only as successor eligibility; use the
  native field/comment projections only as candidate prefilters; acquire multi-touch grants in a
  deterministic compensatable plan; and re-prove exclusion and generation at every external effect.

  Accepted 2026-09-01. [Implementation PR 209](https://github.com/FS-GG/FS.GG.Coordination/pull/209)
  merged as protected commit
  [`3def07ad`](https://github.com/FS-GG/FS.GG.Coordination/commit/3def07adda22806ed62cb739d76cab64ed118b8b),
  whose [protected-main run 33556504859](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33556504859)
  passed. [Acceptance PR 211](https://github.com/FS-GG/FS.GG.Coordination/pull/211) added the append-only
  [GS2-05.5 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/0d0ed46343c12def5af15565cba0ed0ae3294990/evidence/github-substrate-v2/accepted/GS2-05.5.json)
  with self-digest `f382502968cf634bf93c7318d24f629eb3ccfbbac6cf759a99434f1a33975059`,
  merged at [`0d0ed463`](https://github.com/FS-GG/FS.GG.Coordination/commit/0d0ed46343c12def5af15565cba0ed0ae3294990),
  and passed [protected-main run 33558097446](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33558097446).
- [x] **GS2-05.6 — Implement review/delivery.** Bind accountable critique evidence to an immutable full-snapshot
  `ReviewEpochKey` separate from its stable chain identity; allow succession only inside one epoch; require
  a fresh phase seat after any snapshot change without requiring another authority; distinguish merged from
  protected post-merge verification; and generate journal-bound delivery/done receipts.

  Accepted 2026-09-01. [Implementation PR 213](https://github.com/FS-GG/FS.GG.Coordination/pull/213)
  merged as protected commit
  [`afa05338`](https://github.com/FS-GG/FS.GG.Coordination/commit/afa053382c749ccc6bc31884b1c00bed0543e24f),
  whose [protected-main run 33564712056](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33564712056)
  passed. [Acceptance PR 215](https://github.com/FS-GG/FS.GG.Coordination/pull/215) added the append-only
  [GS2-05.6 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/44cc1f7c19b123e6dc74347649cb383d514aa80f/evidence/github-substrate-v2/accepted/GS2-05.6.json)
  with self-digest `24de35789ad18aff1409e873e9aa63edc2d2cff313d8b63f34168c70f7494368`,
  merged at [`44cc1f7c`](https://github.com/FS-GG/FS.GG.Coordination/commit/44cc1f7c19b123e6dc74347649cb383d514aa80f),
  and passed [protected-main run 33566409238](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33566409238).
- [x] **GS2-05.7 — Implement lifecycle projection.** Derive Status from scheduling intent, holds,
  dependencies, claim, PR/review, delivery, and issue state; no operator writes derived state as intent.

  Accepted 2026-09-01. [Implementation PR 217](https://github.com/FS-GG/FS.GG.Coordination/pull/217)
  merged as protected commit
  [`7dac2aac`](https://github.com/FS-GG/FS.GG.Coordination/commit/7dac2aac18cd9be7668eb27cb5e22d51461519fb),
  whose [protected-main run 33571327530](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33571327530)
  passed. [Acceptance PR 219](https://github.com/FS-GG/FS.GG.Coordination/pull/219) added the append-only
  [GS2-05.7 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/4f33cc536b2becc4faa8e356e51fb370ef929c51/evidence/github-substrate-v2/accepted/GS2-05.7.json)
  with self-digest `77ba4ae9ddf350ec93afe7021b320474c5f04ed5f7a255fa2136a3f15af5af12`,
  merged at [`4f33cc53`](https://github.com/FS-GG/FS.GG.Coordination/commit/4f33cc536b2becc4faa8e356e51fb370ef929c51),
  and passed [protected-main run 33572673424](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33572673424).
- [x] **GS2-05.8 — Shadow the complete live fleet.** Compare v1 and v2 read-only decisions, record every
  divergence, and reach zero unexplained divergence without granting v2 mutation permission.

  Accepted 2026-09-02. [Implementation PR 221](https://github.com/FS-GG/FS.GG.Coordination/pull/221)
  merged as protected commit
  [`fb114d17`](https://github.com/FS-GG/FS.GG.Coordination/commit/fb114d17d3eb180eb9dfc5aa39bbbbaa975eb917),
  whose [protected-main run 33577749862](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33577749862)
  passed. [Acceptance PR 223](https://github.com/FS-GG/FS.GG.Coordination/pull/223) added the append-only
  [GS2-05.8 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/f9f7b6260ecd01a722e653a8dc9ccf7e1480b620/evidence/github-substrate-v2/accepted/GS2-05.8.json)
  with self-digest `a267b70003b955e4cd171e30d6f22f52eca6655002e17a52df22a19383fdfd53`,
  merged at [`f9f7b626`](https://github.com/FS-GG/FS.GG.Coordination/commit/f9f7b6260ecd01a722e653a8dc9ccf7e1480b620),
  and passed [protected-main run 33579036245](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33579036245).

### GS2-06 — Implement desired state, CI, release, and supply-chain policy

**Parents:** `.github#2955`, `.github#2956`, `.github#2957`, `.github#2958`, `.github#2963`
**Owner:** `FS.GG.Coordination` model; `.github` instances and reusable entry points
**Depends on:** GS2-02–GS2-05
**Exit:** every rostered repository has a verified plan and rollback, not yet necessarily applied

- [x] **GS2-06.1 — Repository profiles.** Derive expected settings from the reviewed roster and project
  selected attributes into native custom properties; retain external rows and rich registry authority.

  Accepted 2026-09-02. [Implementation PR 225](https://github.com/FS-GG/FS.GG.Coordination/pull/225)
  merged as protected commit
  [`dba99645`](https://github.com/FS-GG/FS.GG.Coordination/commit/dba99645facf479ef885e71edbeef73c84ee7155),
  whose [protected-main run 33582624167](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33582624167)
  passed. [Acceptance PR 227](https://github.com/FS-GG/FS.GG.Coordination/pull/227) added the append-only
  [GS2-06.1 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/f3a92488d6c15e1a4592686c6f00c375c62b167d/evidence/github-substrate-v2/accepted/GS2-06.1.json)
  with self-digest `0f6a142023f21a266242997ae896e494dfa668e895e308ad73d2d5e01404c042`,
  merged at [`f3a92488`](https://github.com/FS-GG/FS.GG.Coordination/commit/f3a92488d6c15e1a4592686c6f00c375c62b167d),
  and passed [protected-main run 33583686226](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33583686226).
- [x] **GS2-06.2 — Required-check census.** Union classic protection and rulesets, classify every check,
  prove unconditional PR/merge-group production, and reduce the external contract to stable aggregates.

  Accepted 2026-09-02. [Implementation PR 229](https://github.com/FS-GG/FS.GG.Coordination/pull/229)
  merged as protected commit
  [`35f9398f`](https://github.com/FS-GG/FS.GG.Coordination/commit/35f9398f528c033eaebed2ac2499ee2e865963f8),
  whose [protected-main run 33588758947](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33588758947)
  passed. [Acceptance PR 231](https://github.com/FS-GG/FS.GG.Coordination/pull/231) added the append-only
  [GS2-06.2 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/3e02166ba6f1ab5ba9e89b72cedeaf56e3286032/evidence/github-substrate-v2/accepted/GS2-06.2.json)
  with self-digest `7157ad56a4879e48642dbb055b0b35158353cbc020fca9a008ed901446d74d0c`,
  merged at [`3e02166b`](https://github.com/FS-GG/FS.GG.Coordination/commit/3e02166ba6f1ab5ba9e89b72cedeaf56e3286032),
  and passed [protected-main run 33590153931](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33590153931).
- [x] **GS2-06.3 — Ruleset plans.** Define branch/tag protection, reviews, conversations, merge methods,
  auto-merge/queue, branch deletion, bypass principals, and expiring exceptions per profile.

  Accepted 2026-09-02. [Implementation PR 234](https://github.com/FS-GG/FS.GG.Coordination/pull/234)
  merged as protected commit
  [`d8a284c7`](https://github.com/FS-GG/FS.GG.Coordination/commit/d8a284c7f238ed77d5b0824d866a21b0a3148915).
  [Acceptance PR 236](https://github.com/FS-GG/FS.GG.Coordination/pull/236) added the append-only receipt
  with self-digest `eec15747e2e5c1cf0ae91fbf370eb82a3e6ea88d6fe3c0f2f738a556e63e5063`, merged as
  [`a9ac6c89`](https://github.com/FS-GG/FS.GG.Coordination/commit/a9ac6c891a885ee37ab69967c6a9dbb542e10840),
  and passed [protected-main run 33613116278](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33613116278).
  [Provider-evidence repair PR 238](https://github.com/FS-GG/FS.GG.Coordination/pull/238) then merged as
  [`e25727a8`](https://github.com/FS-GG/FS.GG.Coordination/commit/e25727a89ad0101188da74414669a556059d251e),
  passed protected-main runs [33616257051](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33616257051)
  and [33616256028](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33616256028), and emitted typed
  completion digest `23f3638fb4356d96394fdfbaff975674cd63673a211371e3c0b59903a74794a7`.
  The validated [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m6-gs2-06-3-ruleset-plans.json)
  and [schema-v2 feedback report](../feedback/2026-09-02-roadmap-github-substrate-v2-m6-gs2-06-3-ruleset-plans.md)
  close typed cycle `cycle-b4037c9a48b9ed22`; its guarded update digest is
  `sha256:825e01d5c52156f39ca264c8114cc2a0fc5d1c276e550e960ac2207ec646d274`.
- [x] **GS2-06.4 — Immutable execution pins.** Move reusable workflows and third-party Actions to full
  commit SHAs, define immutable workflow publication, and retain Renovate as the sole automated update path.

  Accepted 2026-09-02. Ordinary PR [240](https://github.com/FS-GG/FS.GG.Coordination/pull/240)
  closed unmerged after its third repair confirmation; repair-phase implementation PR
  [241](https://github.com/FS-GG/FS.GG.Coordination/pull/241) then merged as
  [`b5d00bb1`](https://github.com/FS-GG/FS.GG.Coordination/commit/b5d00bb18070bbd1b57dfe2032b647f7699276c9).
  Acceptance PR [243](https://github.com/FS-GG/FS.GG.Coordination/pull/243) added the append-only receipt
  with self-digest `9f2476ebea520372f836b69fc8b1d11300d5299ed1796fc34cc70afead9e2a76`, merged as
  [`94f28e12`](https://github.com/FS-GG/FS.GG.Coordination/commit/94f28e12e81bfd68dc685230a1ca52a863f0688b),
  and passed protected-main runs [33638647452](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33638647452)
  and [33638648190](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33638648190).
  Provider-evidence PR [245](https://github.com/FS-GG/FS.GG.Coordination/pull/245) retained exact observed
  SDD currency, merged as
  [`34fdebc4`](https://github.com/FS-GG/FS.GG.Coordination/commit/34fdebc438c04c81039c767a0d2bbbc13f060c47),
  and passed protected-main runs [33643133114](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33643133114)
  and [33643132834](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33643132834).
  The validated [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m6-gs2-06-4-immutable-execution-pins.json)
  and [schema-v2 feedback report](../feedback/2026-09-02-roadmap-github-substrate-v2-m6-gs2-06-4-immutable-execution-pins.md)
  close typed cycle `cycle-a6b1456aa04fc557`; its guarded update digest is
  `sha256:2407358a20eb68f8581d2c4c0bfee1eb6d48e8a2034d5e0c369ef71f6b306781`.
- [x] **GS2-06.5 — Permission compilation.** Derive App and workflow permissions from registered
  interpreters; separate normal coordination, admin/cutover, and release principals/environments.

  Accepted 2026-09-02. [Implementation PR 247](https://github.com/FS-GG/FS.GG.Coordination/pull/247)
  merged exact reviewed candidate `88f515d083e8bf6de8f8a7da3de8dc7731e8af24` as
  [`b6845fbd`](https://github.com/FS-GG/FS.GG.Coordination/commit/b6845fbdd80344de8346d33f61c968f87ed80e3b),
  with protected-main [CodeQL run 33656169897](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33656169897)
  and [Bootstrap run 33656170405](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33656170405)
  successful. [Acceptance PR 249](https://github.com/FS-GG/FS.GG.Coordination/pull/249) added the
  append-only [GS2-06.5 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/e1ae428d916f61e5336d6996cd21f669943561e4/evidence/github-substrate-v2/accepted/GS2-06.5.json)
  with self-digest `9227977242b530755cbc28ff9093fa810aab9647037d3ae4b60cd7311c86cd0f`,
  merged as [`e1ae428d`](https://github.com/FS-GG/FS.GG.Coordination/commit/e1ae428d916f61e5336d6996cd21f669943561e4),
  and passed protected-main [Bootstrap run 33670195315](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33670195315)
  and [CodeQL run 33670194486](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33670194486).
  A roadmap-critique rejection then proved the first provider repair depended on ignored dirty-worktree
  outputs. Successor provider-evidence [PR 253](https://github.com/FS-GG/FS.GG.Coordination/pull/253)
  retained all required SDD inputs plus a clean-checkout guard, merged as
  [`84e488f0`](https://github.com/FS-GG/FS.GG.Coordination/commit/84e488f046c624b2789d520cd062bf99d964b3b5),
  and passed protected-main [Bootstrap run 33681065304](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33681065304)
  and [CodeQL run 33681062103](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33681062103).
  The validated [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m6-gs2-06-5-permission-compilation.json)
  and [schema-v2 feedback report](../feedback/2026-09-02-roadmap-github-substrate-v2-m6-gs2-06-5-permission-compilation.md)
  close typed cycle `cycle-f7201370a11c6a38`; its guarded update digest is
  `sha256:1e567eb9141e0da05aa1bc75c4ad5685a204b3690dadd442721ff1d77a663769`.
- [x] **GS2-06.6 — Release hardening.** Preserve OIDC and dual-feed saga semantics while adding protected
  environments, immutable releases/tags, one pack, SBOMs, attestations, dependency submission/review, and
  public-download verification.

  Accepted 2026-09-02. [Implementation PR 255](https://github.com/FS-GG/FS.GG.Coordination/pull/255)
  merged exact reviewed candidate `5e876400371947033fdf99ab0e6d2a93782bbda0` as
  [`cc4cb1b7`](https://github.com/FS-GG/FS.GG.Coordination/commit/cc4cb1b738b6be18044a8a6c24d34439efe469ec),
  with protected-main [Bootstrap run 33690674962](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33690674962)
  and [CodeQL run 33690674525](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33690674525)
  successful. [Acceptance PR 257](https://github.com/FS-GG/FS.GG.Coordination/pull/257) added the
  append-only [GS2-06.6 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/d0178670c2f8e63d4c214116c8e04f00ba6c4005/evidence/github-substrate-v2/accepted/GS2-06.6.json)
  with self-digest `517172e0eb31d3fd2eefb5844ed426d67d128f795c16195010eb772b7fcd2a5f`,
  merged as [`d0178670`](https://github.com/FS-GG/FS.GG.Coordination/commit/d0178670c2f8e63d4c214116c8e04f00ba6c4005),
  and passed protected-main [Bootstrap run 33692742405](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33692742405)
  and [CodeQL run 33692740214](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33692740214).
  Clean-checkout preflight then found that required SDD provider outputs were ignored and absent.
  Linked [Coordination issue 258](https://github.com/FS-GG/FS.GG.Coordination/issues/258) and independently
  reviewed [provider repair PR 259](https://github.com/FS-GG/FS.GG.Coordination/pull/259) retained all four
  exact provider inputs plus a durability guard, merged as
  [`42457a5e`](https://github.com/FS-GG/FS.GG.Coordination/commit/42457a5e215386b9151a4d6670c35a662dc13f80),
  and passed protected-main [Bootstrap run 33695108140](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33695108140)
  and [CodeQL run 33695107570](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33695107570).
  Roadmap projection [issue 3165](https://github.com/FS-GG/.github/issues/3165) and
  [PR 3167](https://github.com/FS-GG/.github/pull/3167) bind the final four-path ledger update.
  The validated [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m6-gs2-06-6-release-hardening.json)
  and [schema-v2 feedback report](../feedback/2026-09-03-roadmap-github-substrate-v2-m6-gs2-06-6-release-hardening.md)
  close typed cycle `cycle-ff8060a5a0d68217`; its guarded update digest is
  `sha256:400c32cabeab23c29e42d970ec55c8e826f1656166be4317bfc8e3307fcfd94b`.
- [x] **GS2-06.7 — Workflow consolidation and change-impact selection.** Replace duplicated policy jobs
  with typed inventory, composite steps, reusable job contracts, and stable aggregate outputs. Compile a
  versioned dependency graph from changed subjects and non-file inputs to the smallest sound transitive
  closure of build, test, policy, coordination, packaging, and release obligations; policy may still mark
  an obligation unconditional. Required aggregates always resolve, but an unselected child reports a typed
  `NotApplicable` reason without provisioning its expensive job. Unknown, ambiguous, stale, or incomplete
  impact fails closed rather than silently skipping work, and merge-group selection is recomputed against
  the queued head and current base/settings. Independently test representative source, test, workflow,
  dependency, generated-output, documentation, policy, and release changes plus mixed and unknown changes.
  Before cutover, record per-repository baselines and accepted targets for workflow/job fan-out, billed
  minutes, queue time, and p50/p95 completion time, and prove the selector meets them without a missed
  obligation. Keep a small unconditional core suite, run scheduled full-suite sentinels that compare the
  selected closure with actual failures, and disable selection fleet-wide after any missed obligation.
  Record every removed workflow and obligation.

  Accepted 2026-09-03. Registration [issue 260](https://github.com/FS-GG/FS.GG.Coordination/issues/260)
  and [PR 261](https://github.com/FS-GG/FS.GG.Coordination/pull/261) merged as
  [`ff65faa6`](https://github.com/FS-GG/FS.GG.Coordination/commit/ff65faa6697ea5835de72c1e6fa1b53af0a883e3).
  Implementation [issue 262](https://github.com/FS-GG/FS.GG.Coordination/issues/262) and
  [PR 263](https://github.com/FS-GG/FS.GG.Coordination/pull/263) merged as
  [`3277a7a5`](https://github.com/FS-GG/FS.GG.Coordination/commit/3277a7a581f9b75001a851e0b614de3c1eadf812);
  acceptance [issue 264](https://github.com/FS-GG/FS.GG.Coordination/issues/264) and
  [PR 265](https://github.com/FS-GG/FS.GG.Coordination/pull/265) merged as
  [`81574bc8`](https://github.com/FS-GG/FS.GG.Coordination/commit/81574bc8d12d46d08d60448b96db641fc3cb0de9)
  with receipt self-digest `c6d1662e7df93f8b6ca8f577b5143e1e8a45eb9ac6fe55922488659ff9363036`.
  Clean-checkout provider repair [#266/#267](https://github.com/FS-GG/FS.GG.Coordination/pull/267)
  merged as [`57305e54`](https://github.com/FS-GG/FS.GG.Coordination/commit/57305e540267f3f4696ba5a6cdfc84361de577d3).

  Independent critique then held the projection for three substantive repair layers. Runtime selector,
  workflow, aggregate, sentinel, and measured fleet-provenance repair
  [#268/#269](https://github.com/FS-GG/FS.GG.Coordination/pull/269) merged as
  [`286bde7a`](https://github.com/FS-GG/FS.GG.Coordination/commit/286bde7afd607ac8e62a4ca71f6f82d363c052b4),
  followed by append-only receipt [#270/#271](https://github.com/FS-GG/FS.GG.Coordination/pull/271)
  at [`6d3b7662`](https://github.com/FS-GG/FS.GG.Coordination/commit/6d3b7662ac4d9474a9976ac093ec910f55fb6087).
  Current-authority repair [#272/#273](https://github.com/FS-GG/FS.GG.Coordination/pull/273) merged as
  [`588e1a4b`](https://github.com/FS-GG/FS.GG.Coordination/commit/588e1a4bcceeef1cc5a110c924aa52636f263b07),
  followed by receipt [#274/#275](https://github.com/FS-GG/FS.GG.Coordination/pull/275) at
  [`1dd4f111`](https://github.com/FS-GG/FS.GG.Coordination/commit/1dd4f111fc80251e1e6107fbff1e5c4c73667762).
  Durable authority-v2 repair [#276/#277](https://github.com/FS-GG/FS.GG.Coordination/pull/277) merged as
  [`48a3880c`](https://github.com/FS-GG/FS.GG.Coordination/commit/48a3880c695111df360fbe0efd8bf35071ce8194),
  and its separately reviewed receipt [#278/#279](https://github.com/FS-GG/FS.GG.Coordination/pull/279)
  advanced protected main to
  [`6c0c75ab`](https://github.com/FS-GG/FS.GG.Coordination/commit/6c0c75ab9b355e9e4e91b350711b7ab257755ff5).

  Protected runs [33738452137](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33738452137)
  and [33738452009](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33738452009) passed. A fresh
  current-main scheduled sentinel after that receipt rollover passed 189 unit and 468 architecture tests,
  Q3's 23 controls, Q7's 12 controls over ten repositories, package and release checks; decision
  `67c9d6283df2ec528e99052d0c9e88d10b083f0e7b3f36a813b8ecfa42040a6a` records full suite passed,
  no missed obligations, selection eligible, and no production mutation. Receipt
  `repair-GS2-06.7-276` has self-digest
  `d52268765d8e55da1bcba530c54fb0eeb617776110af83af83a8029f02f953a7`; prior receipts remain
  byte-identical. The first actual hosted sentinel at that protected head failed two registered Q2 tests
  because the runner lacked Quint. Pinned-tool repair
  [#280/#281](https://github.com/FS-GG/FS.GG.Coordination/pull/281) mapped reviewed candidate
  `20c3e30c03cd058d8f7f35fe68fada617cffb674` to protected merge
  [`2f9d2e7d`](https://github.com/FS-GG/FS.GG.Coordination/commit/2f9d2e7dcb4dbc8519df0f8b685e6ffeb40c08cc);
  exact-head hosted [run 33744634200](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33744634200)
  passed after checksum-pinned Quint 0.32.0 provisioning and an acquisition-failure inversion.

  The complete canonical preflight then exposed two absent ship-stage provider views. Typed repair
  [#284/#285](https://github.com/FS-GG/FS.GG.Coordination/pull/285) retained `ship.json` and
  `governance-handoff.json`, extended their deletion-sensitive tracked-byte guard, and mapped candidate
  `4d1a1930082bfe7a3b23b4aaa19652e8af8fbf30` to protected merge
  [`e8b9c5ff`](https://github.com/FS-GG/FS.GG.Coordination/commit/e8b9c5ff978e0f14462514406ac86a71c4826425).
  Protected [Bootstrap run 33762801775](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33762801775)
  and [CodeQL run 33762800840](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33762800840)
  passed. From a fresh checkout of that exact merge, canonical FS.GG.SDD.Cli 1.0.0 verify twice and ship
  twice each returned `noChange`, `coherent=true`, only no-change operations, and zero diagnostics; Git
  remained clean. Authority resolution passed at implementation merge `48a3880c`, receipt descendant
  `6c0c75ab`, unrelated descendant `2f9d2e7d`, and current `e8b9c5ff`, while relevant stale-input
  mutations failed closed. Final exact-head hosted
  [run 33763107314](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33763107314) passed; its sole
  retained artifact `9896556087` contains decision SHA-256
  `67c9d6283df2ec528e99052d0c9e88d10b083f0e7b3f36a813b8ecfa42040a6a`, with no missed obligation or
  production mutation.

  Roadmap projection [issue 3175](https://github.com/FS-GG/.github/issues/3175) supersedes the
  content-valid but review-ledger-wedged [issue 3168](https://github.com/FS-GG/.github/issues/3168)
  and unmerged [PR 3171](https://github.com/FS-GG/.github/pull/3171), preserving their append-only
  evidence while binding the final exact four-path ledger update through a fresh delivery identity.
  Open process defects [#2853](https://github.com/FS-GG/.github/issues/2853) and
  [#3173](https://github.com/FS-GG/.github/issues/3173) retain the claim-turnover and moved-pass
  successor causes; this projection does not close or repair them. The validated
  [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m6-gs2-06-7-workflow-selection.json)
  and [schema-v2 feedback report](../feedback/2026-09-03-roadmap-github-substrate-v2-m6-gs2-06-7-workflow-selection.md)
  close reissued typed cycle `cycle-a2884e80de219a84`; its source-bound guarded update digest is
  `sha256:aa9df7f27207dc8f1c72e4459855100c534aa40cad1d5f66ad58a85f253be5ed`.
- [x] **GS2-06.8 — Fleet dry plans.** Inspect, plan, serialize, review, and re-inspect all repository
  settings without applying them. Unsupported plan/permission cases receive explicit dispositions.

  Coordination [issue 288](https://github.com/FS-GG/FS.GG.Coordination/issues/288) and
  [PR 289](https://github.com/FS-GG/FS.GG.Coordination/pull/289) accepted mutation-free fleet planning
  over 10 repositories, 120 endpoints, 121 pages, and 356 observed items. Candidate
  `6da61d7006444352670e27fbd850157768dc53b0` produced 107 supported, four unsupported, and nine
  indeterminate dispositions, retained two complete terminal reads with zero operations, and passed all
  nine registered Q3/Q5/Q7 gates, 198 unit tests, and 485 architecture tests. The independent critique
  repaired two blockers and four majors over two rounds before confirming that exact candidate; the
  implementation protected-merged as `3ac29ec0f46d744bfe607fea6c7fdceffc689673`.

  Acceptance [PR 290](https://github.com/FS-GG/FS.GG.Coordination/pull/290) protected-merged the indexed,
  append-only receipt as `84729d040a7d8baf039d5490d95ef982bb5e3f09`, with canonical digest
  `c8831d8e3b06f77ae26d23579b738347794a8d08e460c84c5856cbbff50abd0e`; exact-main Bootstrap run
  `33793312264` and CodeQL run `33793311567` succeeded, and #288 read back Closed/Done with no live claim.
  The validated [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m6-gs2-06-8-fleet-dry-plans.json)
  and [schema-v2 feedback report](../feedback/2026-09-03-roadmap-github-substrate-v2-m6-gs2-06-8-fleet-dry-plans.md)
  bind normalized SDD unit key `gs2-06-8` to typed cycle `cycle-7649c275b420340b`; its source-bound guarded
  update digest is `sha256:f76c5b2a15980c6617b561b1370693c8153371895263e0ee7a2e91e94817c56d`.

### GS2-07 — Implement event reconciliation and merge readiness

**Parents:** `.github#2961`, `.github#2962`, `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-04–GS2-06
**Exit:** events and audits converge; merge-group policy is qualified before any production queue

- [x] **GS2-07.1 — Event envelope and cursor.** Normalize source, delivery/event identity, subject,
  revision, causation, correlation, and receipt; duplicate and reordered delivery is idempotent.

  Coordination [issue 294](https://github.com/FS-GG/FS.GG.Coordination/issues/294) and
  [PR 295](https://github.com/FS-GG/FS.GG.Coordination/pull/295) accepted a canonical length-framed
  envelope and complete ordered cursor over source, delivery/event identity, subject, revision, causation,
  correlation, and receipt. Candidate `b05804181117736dbcf47cf77ad0b130637b8105` passed 22 generated
  and independently authored Q3 controls, 208 unit tests, 492 architecture tests, warning-free Release build,
  exact roadmap gates, and clean-checkout SDD verify/ship fixed points. The independent critique repaired one
  blocker and five majors in one round before confirming that exact candidate; implementation protected-merged
  as `83facfdea578d2ceddb1a80da9b6255f5ff29bc8`.

  Acceptance [PR 296](https://github.com/FS-GG/FS.GG.Coordination/pull/296) protected-merged the indexed,
  append-only receipt as `37a8c8275e101f0da9f26b1d0ce120533a879833`, with canonical digest
  `825781cedeebbd56aad3a3d41499d6f9bbc647da372f8a91df7c7e2a5ed336e1`; exact-main Bootstrap run
  `33844921620` and CodeQL run `33844921088` succeeded, and #294 read back closed/Done with no live claim.
  The validated [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m7-gs2-07-1-event-envelope.json)
  and [schema-v2 feedback report](../feedback/2026-09-04-roadmap-github-substrate-v2-m7-gs2-07-1-event-envelope.md)
  bind normalized SDD unit key `gs2-07-1` to typed cycle `cycle-dba9a7021f8c15c3`; its source-bound guarded
  update digest is `sha256:a1628ad879bf9886dfc0a44f482366e0a45d04fb352afcb487d6d1d6d461e7e2`.
- [x] **GS2-07.2 — Narrow reconciliation.** Route supported issue, relation, Project, repository, ruleset,
  run/check, release, and installation events to a deduplicating subject queue. Commands and events only
  schedule work; the shared fresh-observe/reduce/sealed-plan/apply/verify reconciler is the exclusive normal
  writer path.

  Coordination [issue 299](https://github.com/FS-GG/FS.GG.Coordination/issues/299) and
  [PR 300](https://github.com/FS-GG/FS.GG.Coordination/pull/300) accepted supported-event routing, a
  deduplicating subject queue, and the exclusive fresh-observe/reduce/sealed-plan/apply/verify writer path.
  Candidate `ba32c93adfe581dab113a692687314a345a3f013` passed 23 Q3 controls, 218 unit tests, 500
  architecture tests, a warning-free Release build, exact roadmap gates, and clean provider verification.
  The independent critique repaired three majors over two rounds before confirming implementation commit
  `44fbef7e5b13ce4c9a954296d979767315cb929a`; implementation protected-merged as
  `47d628dc16ca9cca12853dd5c55a016356998173`.

  Acceptance [PR 301](https://github.com/FS-GG/FS.GG.Coordination/pull/301) protected-merged the indexed,
  append-only receipt as `de1b7475505fa34748dc3b1b76d649f2bd3c84d3`, with canonical digest
  `6ae56a7c9dce52f3ac25e39145b275ed5e8127a1020ee8c65a392b976661c298`; exact-main Bootstrap run
  `33858431320` and CodeQL run `33858430654` succeeded, and #299 read back closed/Done with no live claim.
  The validated [schema-v3 critique](../reviews/roadmap/roadmap-github-substrate-v2-m7-gs2-07-2-narrow-reconciliation.json)
  and [schema-v2 feedback report](../feedback/2026-09-04-roadmap-github-substrate-v2-m7-gs2-07-2-narrow-reconciliation.md)
  bind normalized SDD unit key `gs2-07-2` to typed cycle `cycle-c02683c1aead6745`; its source-bound guarded
  update digest is `sha256:ea9c59e68dd5e15b9ea6e4d82c1974454431c0cfeb604974e63e97371091ea43`.
- [x] **GS2-07.3 — Audit repair.** Retain a complete scheduled audit as authority for dropped deliveries,
  preview gaps, external repositories, and schema drift; prove event/audit convergence under replay.

  Coordination [issue 304](https://github.com/FS-GG/FS.GG.Coordination/issues/304) and
  [PR 307](https://github.com/FS-GG/FS.GG.Coordination/pull/307) accepted audit-authoritative repair for
  dropped deliveries, preview gaps, external repositories, and schema drift, including replay convergence.
  The canonical accepted receipt has digest `4c6a18a3c8cca8ebd59ce040f63f0192c07f9468ad155e7941f676b0b611719c`
  and tracked-file SHA-256 `27195572e11d21b4438b0dbf94ec8ec341f4fb99e65574e92203bf37d1ceefd2`.

  Recovery used the explicitly authorized, non-reconstructing
  [synthetic checkpoint](https://github.com/FS-GG/FS.GG.Coordination/blob/4ad66d094092cb1ee858b6cb76f60d6d39e17079/evidence/github-substrate-v2/synthetic-checkpoints/GS2-07.3.json)
  with digest `832bf1e5b02f9ef5c44bbe79aa7ba65618cc85a60bc063dfbe444e0467515ad0`;
  its revision-28 anchor is `e0db53dd84b48a598a20bf8f6d888be1b23be0089656c1526e677d8f3be39346`,
  after which ordinary strict lifecycle processing completed at revision 42 with digest
  `6be3c3164d4b72178d52871f69e2bfe8a143768c6ea5608135520159122bd274`.
  Exact-head [independent confirmation](https://github.com/FS-GG/FS.GG.Coordination/pull/307#issuecomment-5558086307)
  and [host acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/307#issuecomment-5558107267)
  preceded protected merge `4ad66d094092cb1ee858b6cb76f60d6d39e17079`; exact-main Bootstrap run
  `34022719859` and CodeQL run `34022720160` succeeded, and #304 read back closed/Done. The docs-only
  acceptance projection is owned by [`.github#3269`](https://github.com/FS-GG/.github/issues/3269).
- [x] **GS2-07.4 — Event security.** Verify signatures, installation/repository scope, replay bounds,
  payload/API disagreement, and least privilege; events schedule reconciliation but never directly mutate
  derived state.

  Coordination [issue 310](https://github.com/FS-GG/FS.GG.Coordination/issues/310) and implementation
  [PR 311](https://github.com/FS-GG/FS.GG.Coordination/pull/311) accepted signature verification,
  installation/repository scope, replay bounds, payload/API disagreement, least privilege, and the
  schedule-only event boundary at candidate `dc59dd0238d7b0e5af5cbd5b9fd90c2f36bd45b8`, protected-merged as
  `f290382850c8698125b296e9dbeb00aa59d392da`. Acceptance
  [PR 312](https://github.com/FS-GG/FS.GG.Coordination/pull/312) merged the indexed
  [receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/c460923ccef2f6ef30d9457967e717bec0e084ed/evidence/github-substrate-v2/accepted/GS2-07.4.json)
  as `c460923ccef2f6ef30d9457967e717bec0e084ed`, with canonical digest
  `d2cf3b943fc153047652d73de77bfdcb35fe6a087f414a494eec35542edd2a50`.

  The validated [schema-v3 critique](https://github.com/FS-GG/FS.GG.Coordination/blob/c460923ccef2f6ef30d9457967e717bec0e084ed/reviews/roadmap/roadmap-github-substrate-v2-m7-gs2-07-4-event-security.json)
  has digest `2dc2a920548beda7e05cf387cef3a616d5211362fcf23495256c3680c2db5060`.
  The strict terminal/reconciled lifecycle completed at revision 30 with digest
  `ff409f5421578f9a0ed87202bf298ab24bb9c2cdcdfa57ce469149b78b925970`; exact-main Bootstrap run
  `34043739110` and CodeQL run `34043738634` succeeded, and #310 read back closed/Done.
- [x] **GS2-07.5 — Merge-group support.** Ensure every aggregate required check runs on merge groups and
  re-evaluates temporal claim, review, head, dependency, and release obligations.

  Coordination [issue 315](https://github.com/FS-GG/FS.GG.Coordination/issues/315) and implementation
  [PR 316](https://github.com/FS-GG/FS.GG.Coordination/pull/316) accepted merge-group execution for every
  aggregate required check, with temporal claim, review, head, dependency, and release-obligation
  re-evaluation at candidate `75373222ccfe50ab73313805f8496edde1dcf482`, protected-merged as
  `9cf69344a84ca45b90a45775e486a0c1e6a2611f`. Acceptance
  [PR 317](https://github.com/FS-GG/FS.GG.Coordination/pull/317) merged the indexed
  [receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/4072c24f9abeb85d64349c7866ec6d4995aff53f/evidence/github-substrate-v2/accepted/GS2-07.5.json)
  as `4072c24f9abeb85d64349c7866ec6d4995aff53f`, with canonical digest
  `dd321136fe28e135ba5ee29a3b81a2041b81c8eb29126762cf893bb98ece34d8`.

  Recovery openly used the authorized, non-reconstructing
  [synthetic checkpoint](https://github.com/FS-GG/FS.GG.Coordination/blob/4072c24f9abeb85d64349c7866ec6d4995aff53f/evidence/github-substrate-v2/synthetic-checkpoints/GS2-07.5.json)
  with digest `4b49aacf93960dfd4bf865715e2318e35670548131540906747113e9d1c607c1` and trusted
  lifecycle anchor `70dbae3361e2f765bf295ead17ba981ad268e57eefd2146eda135e794eaf66d6`;
  missing provenance was declared unnecessary and no missing data was reconstructed. The validated
  [schema-v3 critique](https://github.com/FS-GG/FS.GG.Coordination/blob/4072c24f9abeb85d64349c7866ec6d4995aff53f/reviews/roadmap/roadmap-github-substrate-v2-m7-gs2-07-5-merge-group-support.json)
  has digest `1f932878662302036e70648dd24b9c1dadd23a74085d771991727fa92d69e829`.
  The strict terminal/reconciled lifecycle completed at revision 34 with digest
  `f3d391bef2793bcbd37ed8c8202166ba06b0c89f39ca584a8a8a44129a3622c8`; exact-main Bootstrap run
  `34071019843` and CodeQL run `34071019547` succeeded, and #315 read back closed/Done.
- [x] **GS2-07.6 — Queue sandbox/pilot.** Exercise queue admission, base movement, check growth, expiry,
  failure recovery, and rollback in a low-volume isolated or representative repository before fleet enablement.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-07.6.json).

- [x] **GS2-07.7 — Measure event benefit.** Record latency, dropped-event repair, API cost, schedule count,
  and false/unknown outcomes; reduce polling only from evidence.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-07.7.json).

- [x] **GS2-07.8 — Qualify runtime operations.** Qualify the runtime boundary actually included in this
  candidate. Under the accepted GS2-00.9 decision, no App/webhook host, event listener, or continuously
  running writer enters this cutover: scheduled complete audits remain the authority, and accepted narrow
  reconciliation and audit-repair paths provide recovery at their implemented boundary. Prove host exclusion
  from evaluated build and runtime inputs, not only from an `enabled=false` declaration, and exercise event
  absence, provider unavailability, incomplete audit, backlog replay, and interruption without dropping a
  subject or page, using stale authority, or inventing settlement.

  Classify every original runtime-operations clause as exercised, inherited from exact accepted evidence, or
  inapplicable through GS2-00.9. Host deployment, host rollback, host secret rotation, regional host failover,
  host alert routing, and emergency host disable are inapplicable for this no-host candidate; they are not
  successful exercises. Redaction and diagnostic claims require execution at a relevant implemented boundary
  or remain explicit limitations. Enabling a host requires a new accepted amendment after `OperatingV2` and
  the original deployment, rollback, rotation, outage, regional/provider failure, redaction, alerting, and
  emergency-disable exercises before that host may enter a later candidate.

  Acceptance binds the complete ordered GS2-07.1–GS2-07.8 child set and every accepted child receipt, retains
  model identity and applicable formal gates, runs the declared GS2-07 parent closure comprehensively and
  cold, and rejects an omitted child, altered command or input, false no-host disposition, or missing applicable
  recovery. GS2-07.8 completion qualifies this cutover candidate only; it does not claim production v2, an
  installed audit execution, GS2-08, reduced polling, or authority to deploy or enable a host or writer.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-07.8.json).

### GS2-08 — Ship the universal v1 bridge and protected epoch ledger

**Parent:** `.github#2964`
**Owner:** `.github` for v1; `FS.GG.Coordination` for contract and independent tests
**Depends on:** GS2-00; GS2-02 epoch/manifest vocabulary before publication
**Exit:** every released v1 writer is fenced fleet-wide

- [x] **GS2-08.1 — Freeze the epoch wire contract.** Define the complete
  `OperatingV1 -> Preparing -> FreezeRequested -> Frozen -> SwitchedV2 -> VerifiedV2 -> OpenV2 -> ObservingV2 -> ContractingV1 -> OperatingV2`
  sequence, legal pre-open rollback transitions, manifest binding, canonical fleet identity, exact
  ledger/ref/tag/genesis layout, ancestry proof, explicit partial/refused/indeterminate failure semantics,
  fresh-read/cache policy, claim and operation-generation fencing at the effect boundary, and non-authoritative
  issue projection. `Preparing` preserves only already-admitted eligible incumbent operations under the current
  manifest and exact generations; `FreezeRequested` and every later state refuse new ordinary v1 effects.
  `RollingBack` never independently reopens writing, and no transition restores v1 after `OpenV2`.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.1.json).

- [x] **GS2-08.2 — Complete ledger protections.** Preserve and continuously audit the authority
  repository's split branch rulesets; add immutable tag rules, the protected `fleet-cutover` environment,
  a contents-only selected-repository journal App (or explicit security acceptance of the shared App),
  control issue, effective-rule readback, and tamper/rewind monitoring.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.2.json).

- [x] **GS2-08.3 — Map every v1 writer.** Turn the GS2-00 mutation census into an executable coverage list;
  unknown or dynamically discovered write entry points fail the bridge build. The producer census derives the
  coordination roots from candidate-built typed command metadata and separately binds tracked direct REST/GraphQL,
  routine merge, release/repair/dispatch/registry automation, protected-admin, build/declaration, and local-only
  telemetry source identities. Its structural leg is universal; candidate metadata is checked after build and
  again before release. The same producer contract derives all seven coordination-kit receivers from the registry,
  binds each exact revision/tree and complete source manifest, retains deduplicated route/dependency bytes, and
  distinguishes installed legacy tool sources, mutable workflow observations, delegated writer callees, and
  read-only/local-only paths without executing receiver code. This is source coverage only: receiver fencing begins at GS2-08.4 and acceptance remains
  with the native Coordination qualification rather than this roadmap checkbox.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.3.json).

- [x] **GS2-08.4 — Add one common precondition.** Every normal v1 mutation entry reads and verifies the
  fresh ledger epoch before its first effect. Unreadable, contradictory, frozen, switched, or v2-open state
  refuses before write.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.4.json).

- [x] **GS2-08.5 — Preserve OperatingV1 behavior.** Current regression/corpus behavior remains unchanged
  when the verified epoch is `OperatingV1`; the bridge adds no second semantic authority.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.5.json).

- [x] **GS2-08.6 — Independently attack the fence.** From outside the v1 test generator, attempt every
  write class under all epochs, stale cache, lost response, ledger rewind, missing tag, wrong manifest,
  permission loss, and older client versions.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.6.json).

- [x] **GS2-08.7 — Publish one immutable bridge.** Build once, sign/attest, publish to required feeds, verify public
  installation, and record exact tool/kit/workflow identities.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.7.json).

- [x] **GS2-08.8 — Adopt the immutable bridge across every receiver.** Update `.github`, SDD, Rendering, Governance, Templates, Game,
  Audio, and Net; resolve superseded dependency-update PRs; prove each live route uses the exact bridge.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.8.json).

- [x] **GS2-08.9 — Seal every residual github-v1 writer route.** If an old writer cannot read the epoch, disable/revoke its
  dispatch, credential, schedule, or installation before freeze and record that as its fence proof.

  [Accepted native receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-08.9.json).

### V2-CI-I1 — Unattended credential execution interlude

**2026-09-24 disposition:** The [six-milestone subroadmap](roadmaps/v2-ci-i1-unattended-credential-execution.md)
and [isolated installed qualification](operations/v2-ci-i1-installed-qualification.md) are complete.
The exact profile is selected for GS2-10 candidate preparation. Production remains inactive at
`OperatingV1`; the protected `OpenV2` decision and exact candidate qualification still gate activation.
The six hosted cases measure a bounded latency envelope, not a matched savings percentage.

**Scope:** bounded cross-repository implementation alongside unfinished GS2-09
source/rehearsal work; join before the selected GS2-10 candidate/receiver freeze. This is a
planning part, **not a newly accepted GS2 unit or a second scheduler**. The
[design](coordination/2026-09-24-v2-unattended-ci-credential-interlude.md) owns its slices,
refusal cases and performance acceptance; [ADR-0088](adr/0088-ci-owned-unattended-credential-execution.md)
owns the prospective cross-repository decision.

The normal future v2 credential-bearing operation should need no human prompt, interactive
host dependency, host-agent credential conversation, token relay, status-restatement turn or
receipt-only PR. Development and secret-free local CI stay inside rootless fdev Podman; the
host wallet, DBus/Secret Service, SSH agent and Podman socket are not credential paths. A selected
reviewer subagent checks credential-bearing source against a fixed checklist and can flag
repair, but it neither holds raw credentials nor impersonates an independent approval. A
remote ephemeral **post-merge** CI job, not ordinary PR CI or a host/self-hosted runner,
binds exact source, public qualification, current Authority state, operation scope and
one-attempt readback before using a scoped key.
The explicit trusted-fdev/CI assumption does not claim protection from compromise of that
writer. Current v1 genesis approval and GS2-13's irreversible human `OpenV2` gate remain in
force until each is separately amended and qualified; this roadmap merge performs neither.

`.github` owns policy, credential topology and trusted workflow; Coordination owns the typed
plan/installer/reconciliation; receiver owners qualify their selected installed profile.
Policy, installer/refusal tests, reviewer guidance and CI cost baseline can advance in
parallel. They join at an isolated installed success plus wrong-key, stale-approval,
duplicate-attempt and unknown-effect tests. Activation requires an exact source/secret scope,
no PR-secret path, a public receipt and independent readback. If not ready at GS2-10, defer
the profile explicitly rather than change a frozen candidate. GS2-09.7/09.8 source work
does not wait for these parallel slices; candidate adoption of this path does.

Use the [unified roadmap's 5% target/10% narrow bureaucracy ceiling](2026-09-07-154210-fs-gg-unified-development-roadmap.md#74-narrow-bureaucracy-budget-tests-excluded),
not a new per-PR approval. Measure both administrative critical-path delay and useful CI
duration; preserve all required technical gates while removing duplicate exact-subject work
and balancing observed shard runtimes. The selected installed job design requires no routine human
interaction; production activation and any efficiency percentage remain separate evidence claims.

### Concurrent development before cutover

Independent successor token-telemetry work may proceed alongside GS2-09 and
the preparatory parts of GS2-10. Keep roadmap and telemetry changes on separate
branches or worktrees, give each instrumented work item distinct assignment
and item identities, and serialize changes to a shared telemetry Host,
publisher, updater, or schema-migration transaction. A genuine roadmap item
may supply telemetry qualification evidence only when it follows the normal
instrumented runtime, delivery, CI-population, receipt, and private-readback
paths; do not manufacture outcome, population, or token-attribution facts.

Treat telemetry releases, Host migrations, receiver changes, and other work
that could cross the cutover window as concurrent changes for GS2-10.6 and
GS2-10.9. Before readiness approval, finish or explicitly park them and refresh
the exact candidate and manifest when their source or plans changed. From
GS2-11.3 through the switch and verification window, do not start or advance
independent telemetry mutations: stop ingress, drain active work, and preserve
the fleet freeze until the roadmap explicitly releases deferred programs.

### V2-FS-I1 — F# automation convergence interlude

The [17-repository review](reports/2026-09-25-fsgg-fleet-code-architecture-review.md)
and [design and staged roadmap](coordination/2026-09-25-fsharp-automation-convergence-design-and-roadmap.md)
identifies policy, registry, manifest, provider and projection logic suitable for owner-specific F#
tools. It also identifies shell launchers, independent Python oracles, native credential adapters,
generated receivers and historical evidence that remain until a separately qualified replacement
exists. This is a parallel planning part, **not a new GS2 unit, acceptance receipt, or universal
Python/Bash removal gate**. The prior
[telemetry/roadmap-closure design](reports/2026-09-04-fsharp-roadmap-telemetry-and-projection-automation-design.md)
requires current-source reconciliation before any remaining work is scheduled; current Host and
dashboard roadmaps and V2-CI-I1 retain their owners.

The preparation lanes are independent when touch sets do not overlap: executable/receiver census,
confirmed source-closure, CI-output and path-coherence repairs, pure `.github` policy scaffolding,
effective executable-step wiring repair, SDD generic artifact contract, Templates provider composition,
product skill-staging characterization, and read-only Coordination/release transport analysis. At each
scheduling pass, fill `min(available agent slots, safe disjoint bounded lanes)` with accountable owners;
do not leave a slot idle solely because a later receipt, producer publication or protected merge is queued.
Reserve at least one **active worker lane** (in addition to the roadmap orchestrator) for direct V2
critical-path delivery at every scheduling pass. That worker advances the current GS2 gate, its
concrete blocker, or the next V2 prerequisite; queue observation alone does not fill the reservation.
Optional F# ports, review and refactoring use only the remaining worker slots. When the reserved
worker finishes, assign another direct V2 task before filling an optional slot; never backfill the
reservation with migration work. If no direct V2 source or evidence task can start, record the exact
external authority or source blocker and keep the reserved slot unassigned rather than counting an
optional worker as V2 delivery. This reservation is a scheduling rule, not permission to bypass any gate.
Provisional source branches, draft PRs, characterization, scaffolds, tests and refactors may proceed
against a recorded base while their merge or activation prerequisite is pending. Record the prerequisite,
non-authoritative status, exact head and intended follow-up repair/rebase in the owning PR. When the
prerequisite lands, the same owner rebases or repairs that PR, reruns affected local and hosted controls,
and only then seeks normal admission. Serialize overlapping shared files, immutable producer publication
and receiver pinning, protected merge order, sandbox mutations, live gate flips and Authority writes. A published
producer and exact installed receiver proof precede removal of old logic. Preserve the independent
refusal controls, owner-specific BOM/CRLF policies, credential custody, ambiguous-effect recovery,
and Q0–Q10 qualification strength. Enduring `.github` policy tooling avoids V1-only Coord runtime
dependencies. Telemetry Contracts and Store reference the Coord Core assembly; Store compiles telemetry
source from a historical Coord CLI path into the Store assembly. Host references Contracts and Store,
not the Coord CLI binary, and its package includes Core. Q9 inventories this exact closure and
qualifies any V1-only dependency removal. GS2-14 does not require deleting every Coord project.

Before GS2-10, record which bounded ports actually enter the candidate and explicitly defer the
remainder. Selected package/lock, registry, workflow, generated receiver and settings changes must
be qualified and stabilized before freeze. A later change mints a new full Q0–Q7 candidate or waits
for `OperatingV2`; no optional port may cross GS2-11–GS2-12. The GS2-09.7 isolated rehearsal,
GS2-09.9 callable handoff, `OpenV2` human approval and 15-real-item observation remain unchanged.
At this proposal's review, **no optional F# port is selected** for GS2-10; census, current-source
reconciliation, source-only implementation and read-only parity can proceed, while publication and receiver flips default to
deferral. The confirmed source repairs follow their own normal gates. Change that selection
only with exact installed evidence and an explicit prefreeze candidate-input disposition.

### GS2-09 — Build migration, archive, and rollback tooling

**Parents:** `.github#2954`, `.github#2963`, `.github#2965`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-05–GS2-08
**Exit gates:** Q5 and Q6 over full snapshots

- [ ] **GS2-09.9 — Qualify callable ordinary v2 execution.** The protected Coordination delivery supplies
  the installed ordinary-delivery command, durable recovery behavior, one accepted isolated native operation,
  and the exact callable-readiness handoff consumed by discovery. It performs no fleet migration, does not open
  v2, and remains an additive prerequisite before representative rehearsal. The exact GS2-09.9 qualification
  and custom acceptance receipt are pending.

  An independent offline review of the sealed isolated-operation adapter reproduced wrong PR head/base
  acceptance after an ambiguous POST, force-push-enabled protection acceptance after an ambiguous PUT,
  and an exception-chain secret leak. [Coordination draft #547](https://github.com/FS-GG/FS.GG.Coordination/pull/547)
  records the non-authoritative characterization. The current #545 candidate's green checks do not prove
  these identity-bound Q3/Q6 refusals; its auto-merge was disabled and its receipt is disputed. Keep the
  historical packets immutable. Qualify a versioned operator with rotated contract/proposal/validator
  digests and fresh exact Q3/Q6 negative controls before issuing any GS2-09.9 acceptance receipt.
  [Provisional operator draft #549](https://github.com/FS-GG/FS.GG.Coordination/pull/549) and
  [stacked loopback transport draft #551](https://github.com/FS-GG/FS.GG.Coordination/pull/551)
  exercise strict offline predicates and controlled lost-response HTTP cases. The
  [v5 contract and qualification draft #550](https://github.com/FS-GG/FS.GG.Coordination/pull/550)
  remains non-authoritative. [Native classifier draft #574](https://github.com/FS-GG/FS.GG.Coordination/pull/574)
  repairs a wrong-target source false green: Python boolean `true` previously equaled selected
  repository ID `1` in native reads and pull reconciliation. Its 23 focused offline controls
  require exact integer identities, but the provisional operator remains inactive.
  [Protection-context draft #575](https://github.com/FS-GG/FS.GG.Coordination/pull/575)
  repairs a second source false green after an ambiguous response: an extra required status
  context was ignored while `ExactProtection` was returned. Its 24 focused offline controls
  refuse foreign, duplicate or malformed contexts without activating the operator.
  [Protection-flag draft #576](https://github.com/FS-GG/FS.GG.Coordination/pull/576)
  refuses an unselected enabled optional protection flag that previously still produced
  `ExactProtection` after an ambiguous response; 25 focused offline controls pass. The
  protected target, credential, journal, grant and one-POST authority remain absent.
  [Selected-null draft #577](https://github.com/FS-GG/FS.GG.Coordination/pull/577)
  repairs a missing-versus-null false green for required review and restriction fields after
  ambiguous readback; 26 focused offline controls pass. It does not authorize the provisional
  operator or any protected provider effect.
  [Protection-URL drafts #578](https://github.com/FS-GG/FS.GG.Coordination/pull/578)
  and [#579](https://github.com/FS-GG/FS.GG.Coordination/pull/579) refuse foreign root,
  nested and branch protection URLs after ambiguous responses; 27 and 28 focused offline
  controls pass respectively. [Pull-list draft #580](https://github.com/FS-GG/FS.GG.Coordination/pull/580)
  requires listed PR state, draft, title and body to be present and agree with detail before
  classifying a lost-response pull as exact; 29 focused offline controls pass. These stacked
  source drafts do not supply installed-provider custody or protected one-POST authority.
  [Selected-ref draft #581](https://github.com/FS-GG/FS.GG.Coordination/pull/581)
  closes a lost-response tag-object false green by requiring the selected ref URL, commit
  object type and selected-repository commit URL; 30 focused offline controls pass. Its
  protected installed-provider and native-effect hold is unchanged.
  [Pagination-coherence draft #582](https://github.com/FS-GG/FS.GG.Coordination/pull/582)
  refuses contradictory first/previous page links that previously allowed `ExactPull`
  after a lost POST; 31 focused offline controls pass. It is source-only and stacked on #581.
  [Read-only custody packet #583](https://github.com/FS-GG/FS.GG.Coordination/pull/583)
  pins the draft source/artifact heads and hashes, records #563 disabled, and names the
  absent producer, observer, issuer/reviewer, target, credential, CAS journal, grant and
  native readback evidence. Its proposed negative controls are unrun; the packet is not
  protected install or one-POST authorization.
  [No-effect candidate verifier #585](https://github.com/FS-GG/FS.GG.Coordination/pull/585)
  checks supplied source/workflow/artifact/runtime pins, actor, review, target and one-POST
  intent against independent expectations, with four focused synthetic test methods.
  Even matching input remains `authorized=false` and `can_dispatch=false`; protected
  observers, selected credential/target, CAS/replay, grant and installed effect are absent.
  [Read-only observer ports #587](https://github.com/FS-GG/FS.GG.Coordination/pull/587)
  bind three previously omitted producer identities and compose source, workflow, review,
  target-scope and plan observations from supplied bytes; five observer and four candidate
  tests pass with fake ports closed to file/token/socket/SQLite effects. Protected GitHub
  authentication, installed artifact/workflow custody and one-POST authority remain absent.
  [Injected workflow observer #589](https://github.com/FS-GG/FS.GG.Coordination/pull/589)
  binds exact workflow run attempt and file-at-commit bytes to the candidate through a
  read-only injected port; five adapter and five observer tests pass. Real evidence needs
  a separately reviewed protected `.github` read-only App transport and identity
  attestation with selected Actions/Contents/metadata scope. No effect route is added.
  [Review/audit observer #591](https://github.com/FS-GG/FS.GG.Coordination/pull/591)
  composes injected read-only approval history and membership with a distinct protected
  audit-event port; 15 relevant fake controls pass. Protected App identity/permission
  attestation and immutable approval event ID/time remain absent; dispatch is refused.
  [Selected-target observer #592](https://github.com/FS-GG/FS.GG.Coordination/pull/592)
  binds selected repository, installation-list and ref reads to raw-response hashes and
  an independent protected attestation port; 20 relevant fake controls pass. Protected
  App identity/effective scope, complete target prestate and actual execution credential
  record remain unproved; the one-POST gate stays closed.
  [Injected plan reader #594](https://github.com/FS-GG/FS.GG.Coordination/pull/594)
  binds canonical operation request bytes, the current native PR marker, and exact source,
  workflow, run, review and target identities through a separate seal port; five new and
  20 upstream fake controls pass. It remains a stacked draft with no installed seal,
  protected source/release observer, credential, CAS, grant or native effect.
  [Source/release observer #596](https://github.com/FS-GG/FS.GG.Coordination/pull/596)
  binds a supplied Coordination commit/tree, successful producer run/attempt/actor,
  published artifact digest and size, and three ZIP member hashes to a distinct
  attestation port; five new and 25 upstream fake controls pass. Protected artifact
  attempt/redirect custody, actual effect artifact, App identity and execution credential
  remain absent, so the installed native-effect gate stays closed.
  [Injected audit-event adapter #598](https://github.com/FS-GG/FS.GG.Coordination/pull/598)
  binds a supplied workflow-approval document, actor, repository, run and time to a
  separate immutable joint record for attempt, environment and approval-response hash;
  five new and 30 upstream fake controls pass. Protected audit identity/permission,
  joint-record issuer and installed effect remain absent; dispatch stays refused.
  [Credential-scope observer #600](https://github.com/FS-GG/FS.GG.Coordination/pull/600)
  compares supplied token-free App mint metadata with an independent effective-scope
  witness, binding repository, App/installation IDs, exact permissions, target response
  hashes and expiry; five new and 35 upstream fake controls pass. Protected issuer
  custody, real effective-token scope and complete target prestate remain unproved.
  [GET-only target prestate #602](https://github.com/FS-GG/FS.GG.Coordination/pull/602)
requires two complete matching absent-marker censuses and binds selected run, attempt,
repository, refs and transcript digest to a distinct witness; five new and 40 upstream
fake controls pass. Protected reader identity, witness custody, target continuity and
  execution credential reconciliation remain unproved; no dispatch is enabled.
  [Scope/prestate join #604](https://github.com/FS-GG/FS.GG.Coordination/pull/604)
  compares supplied effective App scope and target prestate through closed fake ports;
  a red-before Python boolean-as-repository-ID false green was repaired, and 16 focused
  controls pass. Protected reader identity, provider-backed scope, witness custody and
  target continuity remain absent; installed one-POST authority stays closed.
  [Independent prestate-witness draft #605](https://github.com/FS-GG/FS.GG.Coordination/pull/605)
  carries witness principal, credential and time window through the target prestate
  reader into the App-scope join, refusing reused reader identity and stale claims;
  16 focused fake controls pass. Protected immutable witness custody, authenticated
  reader, effective scope and target continuity remain absent.
  [Witness-ID type guard #606](https://github.com/FS-GG/FS.GG.Coordination/pull/606)
  refuses a witnessed Python boolean `true` as selected numeric repository or
  installation ID `1` in the target prestate adapter; 17 focused fake controls pass.
  Protected witness event/producer, reader identities, effective App scope and target
  continuity remain unproved.
  [Credential-ID type guards #608](https://github.com/FS-GG/FS.GG.Coordination/pull/608)
  refuse Python boolean or float aliases for the selected repository ID in supplied
  App issuance, mint, response and effective-scope records; 18 focused fake controls
  pass. Protected issuer/witness identities, effective scope and target continuity
  remain absent.
  [Protected-owner qualification packet #609](https://github.com/FS-GG/FS.GG.Coordination/pull/609)
  pins the provisional source heads and separates inspect-only release evidence from
  later native effect admission. It names independent producer/artifact/image/runtime,
  review, target/App, journal/replay and native readback controls; 18 inherited focused
  tests pass. The next source prerequisite is a distinct closed effect artifact and
  disabled workflow with installed no-grant zero token/CAS/POST controls. No protected
  release selection, install, grant or one-POST decision follows from this packet.
  [Closed effect scaffold #611](https://github.com/FS-GG/FS.GG.Coordination/pull/611)
  adds a separate deterministic zipapp and disabled proposal workflow with unselected
  pins and permissions. Its entry always exits 78 and the closed port performs zero
  token, CAS and HTTP calls; eight focused controls and a local clean-install canary
  pass. It is not a protected install, grant or executable native effect.
  [Closed artifact hardening #613](https://github.com/FS-GG/FS.GG.Coordination/pull/613)
  makes alternate native source inert rather than importable and rejects a ZIP with
  trailing bytes even if its supplied manifest is rewritten; ten focused controls
  pass. Its approved manifest digest remains caller-supplied without protected
  independent approval. Both proposal workflows stay disabled, and no installed
  effect authority follows.
  [Native-free closed scaffold #614](https://github.com/FS-GG/FS.GG.Coordination/pull/614)
  removes extractable provisional native bytes from the closed archive and requires
  separate builder/native source inputs for its pure verifier; 11 focused controls pass.
  Independent integrated revision, approved manifest digest, artifact/runtime custody
  and a separately authorized runnable effect remain absent. Both workflows stay disabled.
  [Stacked grant-parser draft #554](https://github.com/FS-GG/FS.GG.Coordination/pull/554)
  fail-closes a proposed one-POST envelope but always refuses dispatch; it has no issuer, trusted
  replay reservation or installed effect entry. [Stacked inspect-only zipapp draft #555](https://github.com/FS-GG/FS.GG.Coordination/pull/555)
  is a deterministic local clean-install candidate whose inspection still refuses dispatch; it is
  not a protected installed provider or native-effect proof. [Stacked authority-port draft #559](https://github.com/FS-GG/FS.GG.Coordination/pull/559)
  sketches independent issuer, observer, replay and journal CAS boundaries, but its ports are
  unimplemented and every result remains non-dispatchable. [Stacked install-pin draft #560](https://github.com/FS-GG/FS.GG.Coordination/pull/560)
  checks local archive/source and interpreter bytes with read-only negative controls; it lacks
  protected artifact and runtime provenance or an installed effect path. [Stacked runtime-closure draft #561](https://github.com/FS-GG/FS.GG.Coordination/pull/561)
  hashes a local interpreter, standard-library tree and observed mapped files, but its local
  manifest is not a protected image or independent release pin. [Stacked provenance draft #562](https://github.com/FS-GG/FS.GG.Coordination/pull/562)
  refuses the existing ordinary CLI release workflows and models the required immutable source,
  workflow, image, runtime and independent approval packet; its observer ports are not installed.
  [Stacked workflow draft #563](https://github.com/FS-GG/FS.GG.Coordination/pull/563) adds a disabled,
  inspect-only release skeleton with empty pins and permissions; it cannot install or dispatch.
  [Readback draft #564](https://github.com/FS-GG/FS.GG.Coordination/pull/564) binds a proposed
  protected source/workflow/artifact/image/approval packet to later installed refusal controls, but
  its observations and immutable coordinates have not been supplied by a protected release.
  [Adversarial qualification draft #565](https://github.com/FS-GG/FS.GG.Coordination/pull/565)
  closes a source-only selection gap: a resealed packet could previously substitute the dispatch
  actor or artifact producer IDs while satisfying pure consistency checks. Its 42 isolated controls
  bind those IDs and refuse self-review, but no protected release packet or installed native effect
  has been observed.
  [Installed-control draft #566](https://github.com/FS-GG/FS.GG.Coordination/pull/566)
  repairs a second source-only false green: observation without a source tree, dispatch actor or
  producer run/artifact IDs passed the proposed installed control. Its 44 isolated controls now
  require exact identities; a matching local draft packet remains non-authorizing without
  independent protected source readback and selected digest approval.
  [Nonzero-pin draft #567](https://github.com/FS-GG/FS.GG.Coordination/pull/567) repairs a
  source-only verifier gap that accepted nine all-zero source, workflow, image and runtime
  placeholders with matching synthetic observations. Its 45 isolated controls pass locally;
  the workflow remains disabled with empty permissions and a refreshed exact verifier pin.
  Protected publication, independent approval, installed path and native-effect readback remain open.
  [Historical-digest draft #568](https://github.com/FS-GG/FS.GG.Coordination/pull/568)
  refuses both known disabled workflow digests after a red-before control showed the earlier
  template digest could pass a later proposed selection. Its 46 isolated controls pass locally;
  the first protected owner step is still review of integrated source and a new runnable,
  inspect-only release with independently selected immutable runner, artifact and reviewer facts.
  [Selection-review draft #569](https://github.com/FS-GG/FS.GG.Coordination/pull/569)
  repairs a self-sealed selection false green by requiring a fourth distinct read-only review
  observation bound to exact selection bytes, digest, reviewer, actor and approval time. Its
  49 isolated controls pass locally, but the observer has no protected implementation and every
  fixture remains non-authorizing.
  [Review-coordinate draft #570](https://github.com/FS-GG/FS.GG.Coordination/pull/570)
  repairs two source-only false greens: a review event without its own origin coordinates, and
  a review timestamp preceding installed-control observation. Its 51 isolated controls pass;
  no protected observer or installed inspect-only release exists.
  [Probe-invocation draft #571](https://github.com/FS-GG/FS.GG.Coordination/pull/571)
  repairs a refusal-record false green that omitted the pinned interpreter and archive. Its
  52 isolated controls require the exact interpreter, `-I -S`, canonical installed archive path
  and subcommand for both proposed probes; no protected installation or native effect follows.
  [Archive-object draft #572](https://github.com/FS-GG/FS.GG.Coordination/pull/572)
  repairs a source-only observation that named the archive path and hash without the object
  actually probed. Its 53 isolated controls require matching regular-file real path, device,
  inode and exact digest before and after both probes; a protected observer must still collect
  those facts from the installed filesystem under read-only custody.
  [Archive-metadata draft #573](https://github.com/FS-GG/FS.GG.Coordination/pull/573)
  further requires exact size, read-only regular mode, one link, and stable modification and
  change times; 54 isolated controls pass. Before/after metadata cannot exclude a transient
  mutation, so protected immutable-directory custody throughout both probes is still required.
  The #550 staged exact-copy loopback
  harness exercises corrected classifier, HTTP and durable-fence paths, but an independent boundary
  review found it insufficient for the installed provider/credential path and corrected native-effect clause. Qualify the corrected
  version through an actual installed provider path or a new protected isolated native operation,
  revalidate the historical archive as historical evidence, and rerun exact Q3/Q6 before rotating
  the gate/index or accepting GS2-09.9. Loopback results alone do not prove that boundary.

  [Protected callable discovery handoff](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/gs2-09-9/callable-discovery-handoff.json)
  records the packet received by discovery; it is not the GS2-09.9 acceptance receipt.

- [x] **GS2-09.1 — Implement complete discovery.** Read every open and relevant closed issue, Project
  item, field, hierarchy/dependency edge, claim/event stream, review/delivery/release record, repository
  setting, workflow pin, and receiver identity. Retain terminal pagination proofs and per-authority
  high-water marks, then require two complete quiescent reads with identical normalized digests.

  [Protected complete-discovery acceptance](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-09.1.json).
- [x] **GS2-09.2 — Implement the immutable manifest.** Bind old/new model and artifact fingerprints,
  global IDs, old bytes/values, v2 results, live operations, receiver heads, settings plans, archive digests,
  dispositions, phase plans, reviewers, and rollback inputs.

  [Protected immutable-manifest acceptance](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-09.2.json) ([receipt PR #483](https://github.com/FS-GG/FS.GG.Coordination/pull/483), merge `584df2e6deb2d5128e0a401c96c0bf3def8d721b`).
- [x] **GS2-09.3 — Implement typed transforms.** Map taxonomy, planning fields, repo scope, body metadata,
  blockers, hierarchy, scheduling holds, touch sets, lifecycle receipts, and desired settings as
  `Migrated`, `Ambiguous`, or `Unsupported`.

  [Protected typed-transform acceptance](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-09.3.json) ([receipt PR #486](https://github.com/FS-GG/FS.GG.Coordination/pull/486), merge `eb6bc92178f54adef04c7dcfa533c2271e095190`).
- [x] **GS2-09.4 — Implement live-operation handling.** For each claim, queued write, review, delivery,
  release, and cutover-adjacent operation, choose drain, migrate, park, or explicit invalid disposition.

  [Protected live-operation acceptance](https://github.com/FS-GG/FS.GG.Coordination/blob/main/evidence/github-substrate-v2/accepted/GS2-09.4.json) ([receipt PR #489](https://github.com/FS-GG/FS.GG.Coordination/pull/489), merge `42afaacb1edb3f915e715040fd040c1087d97f39`).
- [x] **GS2-09.5 — Implement sealed history.** Preserve source schema/bytes/digests, verifier artifact,
  expected outcomes, and lookup index without putting permanent v1 upcasters in the v2 production closure.
- [x] **GS2-09.6 — Implement rollback plans.** Restore settings, receiver pins, v1 projections, schedules,
  and authority snapshot through `VerifiedV2`; make each step resumable from receipts.
- [ ] **GS2-09.7 — Rehearse on the registered isolated cohort.** Extend the protected `.github`
  qualification workflow to seed a representative, nonce-owned fixture in the existing private sandbox
  repository and Project 2 using its App identity and target guards. Recheck exact targets and grants before
  effects; run migrate,
  interrupt every step, retry, rollback, re-run, verify the archive, and prove cleanup and zero residue.
  No separately supplied copy or credential is a prerequisite. Full nine-authority discovery, Q5/Q6
  evidence and independent controls remain required; a source-only or historical Q4 run does not accept
  this unit. The [governing cohort contract](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md#registered-migration-rehearsal-cohort)
  owns the target and permission boundary.
  [Coordination draft #552](https://github.com/FS-GG/FS.GG.Coordination/pull/552) prepares a
  candidate-side validator for the sanitized #3690 mint proof, token digest, selected target/grants,
  expiry, and host pin before either live candidate path calls a provider. This is source-only
  consistency evidence, not independent protected authorization, host run-binding, or Q5/Q6 acceptance.
  [`.github` #3690](https://github.com/FS-GG/.github/pull/3690) merged as
  `ff425734d277fa54c3d71601da90fe7b22619c15` on 2026-09-25 05:35 UTC
  without common OperatingV1 effect admission. Record that process violation
  separately from any sandbox receipt; the merge does not accept Q5/Q6 or authorize
  a sandbox run. [Coordination draft #556](https://github.com/FS-GG/FS.GG.Coordination/pull/556)
  records the protected-rehearsal decision packet and its hold on authenticated run/candidate/nonce
  binding, installed command, and native Q5/Q6 evidence. [Stacked verifier draft #558](https://github.com/FS-GG/FS.GG.Coordination/pull/558)
  checks a proposed signed run envelope with offline refusal tests, but #3690 has no protected signer,
  pinned verifier key or admitted host release gate. [Host signer draft #3711](https://github.com/FS-GG/.github/pull/3711)
  is source-only and refuses before reading a credential while its reviewed public-key pin is empty;
  it has no installed signer, token release or sandbox effect. [Stacked release-contract draft #3712](https://github.com/FS-GG/.github/pull/3712)
  requires a trusted durable host claim and revoke verdict before handoff, but its authority ports
  are uninstalled; a one-time handoff alone cannot make a GitHub App token single-use.
  [Stacked host-claim draft #3713](https://github.com/FS-GG/.github/pull/3713) models atomic claim,
  crash and unknown-result refusals, but its protected store pins remain empty and no native
  revocation port is installed. [Refusal-finalizer draft #3714](https://github.com/FS-GG/.github/pull/3714)
  attempts a separately pinned native revoke after post-handoff store failure but keeps the outcome
  pending without durable intent, observation and receipt; all live pins remain empty.
  [Recovery-worker draft #3715](https://github.com/FS-GG/.github/pull/3715) handles one sealed pending
  token with exact journal/vault/binding identity and no repeat revoke after uncertain claim, but has
  no installed queue, scheduler or authority ports. [Pending-token census draft #3716](https://github.com/FS-GG/.github/pull/3716)
  requires a sealed high-water snapshot, complete ordered pages, exact identities and independent
  digest/readback before releasing recovery subjects; its protected mint index and scheduler are absent.
  [Adversarial boundary draft #3717](https://github.com/FS-GG/.github/pull/3717) repairs two
  source-only false greens: a claimed `committed` CAS response without exact durable token-digest
  readback could hand off a token, and incomplete worker/finalizer/revoker pins could release
  census subjects. Its 77 stacked `.github` controls and 18 Coordination controls pass locally;
  protected signer, custody, scheduler, native Q5/Q6 and installed receiver readback remain absent.
  [Signer-custody draft #3718](https://github.com/FS-GG/.github/pull/3718) repairs two more
  source-only false greens: a vault descriptor could omit candidate-read denial, and a non-string
  equality spoof of escrow readback could invoke the candidate. Its 79 stacked `.github` tests
  pass locally; host-only ACL, signer/store/vault pins and native Q5/Q6 remain unproved.
  [Protected-revision draft #3720](https://github.com/FS-GG/.github/pull/3720) repairs a
  source-only gap where a coherently changed workflow SHA could be signed and released. Its
  82 local controls require an independently admitted exact workflow SHA before credential
  access or handoff. A workflow cannot self-pin its own commit; the distinct protected
  admission authority and its durable run/candidate facts remain unimplemented.
  [Protected-admission draft #3722](https://github.com/FS-GG/.github/pull/3722) repairs a
  second self-assertion false green: copying `PINNED_WORKFLOW_SHA` from the same runner context
  previously allowed signing and fake candidate release. Its 87 local controls require a
  separate current native decision readback at signer and release, bound to exact workflow,
  candidate, run/attempt/nonce, target, signer and policy facts. The real durable authority,
  authenticated adapter, ACLs and live pins have not been installed.
  [Admission-freshness draft #3724](https://github.com/FS-GG/.github/pull/3724) repairs
  stale-decision false greens at signer and release by requiring canonical UTC issuance,
  expiry and a maximum ten-minute lifetime. Its 90 local controls pass; the fake port cannot
  prove protected authority, clock or durable storage. At this layer, one-use claim identity
  remained unresolved because the source keyed claims by signed token binding digest.
  [Decision-claim draft #3725](https://github.com/FS-GG/.github/pull/3725) repairs that
  false green: two distinct token bindings under one admission decision previously reached
  the fake candidate twice. Its 94 local controls derive a stable decision ID excluding token
  and validity time, then require durable claim and exact decision/binding/token readback.
  Protected single-key CAS durability, ACLs and revocation-versus-claim ordering remain open.
  [Atomic-claim draft #3727](https://github.com/FS-GG/.github/pull/3727) repairs a
  red-before revocation just before CAS that previously allowed fake handoff. Its 96 local
  controls require an atomic admitted-state claim and exact native readback; the installed
  shared transaction boundary and post-claim revocation policy remain unproved.
  [Launch-fence draft #3728](https://github.com/FS-GG/.github/pull/3728) repairs a
  post-claim revocation false green by requiring one same-authority candidate invocation
  decision. Its 98 local controls keep definite refusal at zero exposure and treat unknown
  outcome as possibly exposed without retry; the real launch interlock and token custody
  remain unproved.
  [Cancellation-finalizer draft #3730](https://github.com/FS-GG/.github/pull/3730)
  repairs a cancellation path that skipped native token revocation after possible exposure.
  Its 102 local controls attempt revoke in `finally`, retain unknown outcomes pending and
  forbid a second launch after a consumed claim; installed recovery scheduling and native
  readback remain unproved.
  [Recovery-observer draft #3732](https://github.com/FS-GG/.github/pull/3732)
  repairs a duplicate native revoke after restart by observing the first revoke before any
  retry; 107 focused local controls pass. Installed recovery and native readback remain open.
  [Native-attempt draft #3734](https://github.com/FS-GG/.github/pull/3734)
  closes a second revoke after an ambiguous finalizer attempt with a protected shared attempt
  claim and exact readback in 115 local controls. The shared CAS authority, complete mint
  census, scheduler and crash-resolution policy remain uninstalled.
  [Sealed-mint census draft #3736](https://github.com/FS-GG/.github/pull/3736)
  refuses an empty pending scan that omits minted tokens by requiring a sealed full mint
  count/digest and one exact pending or terminal revoked entry through the high-water mark;
  120 local controls pass. Authentic append-only mint index, native terminal readback and
  durable scheduler remain uninstalled.
  [Native-terminal census draft #3737](https://github.com/FS-GG/.github/pull/3737)
  refuses a journal-only terminal receipt that hides an active token: a fresh challenge-bound
  native readback must match the sealed mint, token and installation identities; 124 local
  controls pass. The protected adapter, authentic joint seal and scheduler remain uninstalled.
  [Scheduled-recovery draft #3738](https://github.com/FS-GG/.github/pull/3738)
  refuses a native revoke without a pinned durable host schedule and independent joint-seal
  readback; 132 local controls pass. Protected complete enqueue, atomic schedule/claim
  ordering and pre-pending mint escalation remain uninstalled.
  [Complete-batch recovery draft #3739](https://github.com/FS-GG/.github/pull/3739)
  refuses a standalone committed job without a sealed full batch, binds batch ID to the
  one-use claim, and models no-effect append/readback refusals; 139 focused local controls
  pass. Authentic joint seal, candidate-inaccessible protected scheduler store and atomic
  batch/claim authority remain uninstalled.
  [Atomic-store contract draft #3740](https://github.com/FS-GG/.github/pull/3740)
  binds claim readback to exact batch, schedule, seal and protected identities and defines
  no-effect atomic append/withdraw/claim ports with race negatives; 148 focused local
  controls pass. The authentic joint seal and installed candidate-inaccessible durable
  authority are still required.
  [Signed joint-seal draft #3741](https://github.com/FS-GG/.github/pull/3741)
  requires a pinned RSA-PSS signer key, exact signed mint/pending envelope and current
  protected head/generation readback across census, scheduler batch, job and recovery
  claim; 165 focused local controls pass. Live pins are blank. Protected signer/store
  custody, monotonic head, complete input and generation-enforcing append/claim CAS are
  absent, so Q5/Q6 and sandbox dispatch remain held.
  [Challenge-bound head draft #3742](https://github.com/FS-GG/.github/pull/3742)
  refuses a replayed old signed seal plus old self-reported head by requiring two distinct
  fresh challenges and signed current-head attestations from a declared linearizable
  store; 170 focused local controls pass. The protected signer must actually read the
  current durable head with monotonic generation across crashes. Its custody, ACL and
  clock evidence, live pins and Q5/Q6 remain absent.
  [Durable generation-floor draft #3743](https://github.com/FS-GG/.github/pull/3743)
  refuses a freshly signed lower head after restart through an injected atomic
  nondecreasing full-record floor and two readbacks; 181 focused local controls pass.
  Protected owners must choose a separately durable rollback domain with pinned
  signer/store/floor identities, ACLs and transaction evidence before live use.
  [Generation-checked append/claim draft #3744](https://github.com/FS-GG/.github/pull/3744)
  carries the verified generation and floor identity into atomic batch append and
  recovery claim, then rechecks signed head after durable readback; red-before fakes
  exposed false success and native revoke after a head advance. 185 focused local
  controls pass. Protected signer/store/floor installation, ACL and transaction proof,
  #3690 admission and Q5/Q6 remain open.
  [Shared native-attempt claim draft #3745](https://github.com/FS-GG/.github/pull/3745)
  binds recovery's one-use marker to the durable claim, expected generation and pinned
  floor before native revoke; a head advance before the claim and a marker without a
  durable claim were red-before false greens. 189 focused local controls pass. Installed
  shared namespace, transaction ordering and native readback remain unproved.
  [Shared namespace pin draft #3746](https://github.com/FS-GG/.github/pull/3746)
  requires an exact blank-by-default native-attempt resource identity through finalizer
  descriptor, marker readback and pending census; 196 focused local controls pass.
  Installed one-key serializable store/ACL and native provider-call ordering remain
  unproved, including emergency outage and unknown-readback double-revoke risk.
  [Emergency recovery characterization #3747](https://github.com/FS-GG/.github/pull/3747)
  reproduces two best-effort native revokes across a restart after a lost response and
  unknown readback while both verdicts remain pending and candidate handoff stays
  blocked; 197 focused local controls pass. Source-only changes cannot establish
  durable one-use authority here. Protected owners must qualify a shared attempt
  store, provider idempotency or authoritative exact-token readback with crash,
  lost-result and ACL traces before Q5/Q6.
  [Protected supplier packet #3748](https://github.com/FS-GG/.github/pull/3748)
  assigns admission, App/vault, signer/floor, shared marker store, revoker/provider,
  outage policy and Q5/Q6 facts to owner roles without asserting a protected principal.
  It requires proved idempotency or settled exact-token native readback before retry;
  active or unknown alone holds. The focused finalizer suite passes 35 controls, while
  live pins and protected receipts remain absent.
  [Q5/Q6 packet correction #3749](https://github.com/FS-GG/.github/pull/3749)
  maps all nine accepted authorities in two passes, exact manifest/effects, six
  interruption cuts, archive, five rollback domains, distinct rerun, independent
  omissions and zero residue. It reuses the accepted Q4 sandbox/App route under
  ADR-0089 and requests no new credential; 35 focused finalizer controls pass.
  Installed host authority and native rehearsal evidence remain absent.
  [Q4 admission/installation packet #3750](https://github.com/FS-GG/.github/pull/3750)
  records that the installed Q4 workflow still runs GS2-04.9 with direct candidate
  token handoff, while #3690 merged source has no separate OperatingV1 effect admission
  and live signer/store/recovery pins remain blank. It names independent protected
  release, workflow, credential, store, revoker and Q5/Q6 readback actions; no installed
  authority or sandbox acceptance is inferred.
  [Pure interruption-cut draft #615](https://github.com/FS-GG/FS.GG.Coordination/pull/615)
  adds before-intent, after-readback and before-receipt cases to Coordination's
  in-memory migration-step model with fake read/write/dispatch assertions; 11 focused
  tests pass under local SDK 10.0.401. The repository pins unavailable local 10.0.400,
  and real fresh-process provider cuts, nine-authority Q5 interpretation, rollback and
  protected Q5/Q6 receipt remain unproved.
  No rehearsal dispatch is implied by these drafts.
- [ ] **GS2-09.8 — Prove idempotency and no omission.** Re-running an exact manifest changes nothing;
  adding one unknown live subject or losing one page prevents qualification.
  [Coordination draft #553](https://github.com/FS-GG/FS.GG.Coordination/pull/553) adds source-only
  refusal controls for case-variant and split `Link` pagination headers that previously let a
  migration REST reader treat an incomplete page as terminal. It also refuses two discovered subjects
  collapsing onto one v2 result identity and changes the manifest seal when a result identity changes.
  Its provisional tests do not accept
  GS2-09.8; the GS2-09.7 receipt, exact live rerun, unknown-subject and no-omission Q5/Q6 proof remain.

### GS2-10 — Qualify the exact candidate and prepare the fleet

**Parent:** `.github#2965`
**Owner:** `FS.GG.Coordination`, `.github`, and every receiver
**Depends on:** GS2-03–GS2-09
**Exit gates:** Q0–Q7 accepted against one immutable candidate

- [ ] **GS2-10.1 — Freeze candidate identities.** Record source commits, dependency locks, model/compiler
  fingerprints, packages, container/tool assets if any, workflows, App build, verifier artifacts, Typed
  SDD lifecycle-default decision, provider/scaffolder identities, every receiver head/settings profile,
  and any selected F# tooling contract, canonicalization policy and installed artifact identity.
- [ ] **GS2-10.2 — Run the full qualification matrix.** No selective rerun may replace a failed full
  result; repairs create a new candidate identity.
- [ ] **GS2-10.3 — Complete live shadow comparison.** Read the complete fleet repeatedly over a bounded
  operational window, explain all v1/v2 differences, and prove v2 has no production write permission.
- [ ] **GS2-10.4 — Generate the cutover manifest.** Capture the complete live source, transformations,
  archive, prepared receiver heads, settings plans, exact phase operations, and rollback plans.
- [ ] **GS2-10.5 — Prepare receiver changes.** Create exact-head, green PRs or protected plans for pins,
  workflows, rulesets, settings, environments, and permissions; do not merge/apply switch changes yet.
  Bind every remaining effect through retirement to its installed interpreter/workflow, target, observed
  grant, approval scope, durable receipt, readback and recovery route. Resolve the current cutover
  environment/conformance drift and qualify the selected approval profile before candidate freeze.
- [ ] **GS2-10.6 — Drain backlog hazards.** Resolve or disposition obsolete Renovate PRs, coordination-tool
  adoption PRs, release candidates, conflicting settings changes, and work expected to cross the window.
- [ ] **GS2-10.7 — Rehearse the whole cutover.** Execute freeze through rollback and freeze through
  simulated `OpenV2` in isolated fleet replicas; record durations, API budgets, operator decisions, and all
  manual steps. Exercise the installed one-operator route, automate mechanical relays and retain exact
  dispositions for genuine human approval or unavailable administrative authority. Include observation
  and contraction controls in the isolated exercise; synthetic completions cannot satisfy production Q10.
- [ ] **GS2-10.8 — Approve readiness.** Independent architecture, security, operations, migration, and
  receiver reviewers assess the exact candidate/manifest. Under [ADR-0079](adr/0079-single-accountable-delivery-authority.md),
  the accountable owner accepts readiness from their separately generated critique evidence and the
  required technical gates. Any source or plan change invalidates approval. The owner may orchestrate
  these critiques; they remain distinct from the owner's acceptance verdict. All independent controls and
  protected native-identity requirements remain, including any explicit independent-human rule.
  Readiness includes the complete installed execution inventory and the residual operator-action trace
  from GS2-10.7.
- [ ] **GS2-10.9 — Close the concurrent-change gate.** Prove there is no active kernel publication,
  lifecycle-default flip, provider/registry flip, coordination receiver change, reusable-workflow change,
  repository-settings mutation, or release saga expected to cross the cutover window. Defer each remaining
  row until `OperatingV2` or mint a new candidate and repeat the complete Q0–Q7 matrix.

### GS2-11 — Freeze the production fleet

**Parent:** `.github#2965`
**Owner:** protected cutover operators
**Depends on:** GS2-10; approved cutover window
**Exit:** authoritative `Frozen(snapshot)`; all normal writes demonstrably closed

- [ ] **GS2-11.1 — Announce and verify the window.** Confirm operators, approvers, communication channel,
  status page, abort criteria, rate budget, credentials, backups, and candidate/manifest fingerprints.
- [ ] **GS2-11.2 — Acquire the cutover grant.** Enter the protected environment and commit/anchor
  `FreezeRequested(manifest)` with exact expected parent.
- [ ] **GS2-11.3 — Stop ingress.** Refuse new claims, intake/roadmap applies, board mutations, review and
  delivery advances, coordination merges/dispatches, settings reconciles, releases, lifecycle-default
  changes, provider/registry flips, and receiver updates.
- [ ] **GS2-11.4 — Drain active work.** Complete, release, or explicitly park every claim, queued write,
  review, delivery, merge election, operation lock, and release saga. Active count must reach zero.
- [ ] **GS2-11.5 — Restrict repositories temporarily.** Apply the approved update restrictions with only
  the cutover App/operator bypass; verify every repository and exception.
- [ ] **GS2-11.6 — Prove the fence.** Attempt representative operations through every client/workflow/App
  generation; all normal writes must refuse for the epoch reason before external effect.
- [ ] **GS2-11.7 — Take the frozen snapshot.** Perform two complete reads, prove no intervening mutation,
  bind the result to the manifest, and commit/anchor `Frozen(snapshot)`.
- [ ] **GS2-11.8 — Decide continue or rollback.** Any active writer, unreadable authority, manifest drift,
  unplanned head/settings change, or unsettled operation executes the rollback plan before switch.

### GS2-12 — Switch and verify while the fleet remains closed

**Parent:** `.github#2965`
**Owner:** protected cutover operators
**Depends on:** authoritative Frozen state
**Exit gate:** Q8 and authoritative `VerifiedV2(evidence)`

- [ ] **GS2-12.1 — Activate exact v2 artifacts.** Promote/install only the accepted candidate; verify
  package bytes, model identity, App build, workflow SHAs, and receiver resolution.
- [ ] **GS2-12.2 — Apply receiver switch changes.** Merge/apply prepared heads in the recorded dependency
  order and refuse any changed head, run set, or settings precondition.
- [ ] **GS2-12.3 — Migrate authority.** Apply issue types/fields, native hierarchy/dependencies, scheduling
  holds, touch-set streams/projections, Project membership/status, and other authoritative transformations.
- [ ] **GS2-12.4 — Apply desired settings.** Install custom properties, rulesets, aggregate checks,
  immutable pins, App permissions, environments, release/security features, and event routes.
- [ ] **GS2-12.5 — Disable v1 execution.** Turn off old schedules, dispatch routes, workflows, credentials,
  and writers without deleting rollback assets; commit/anchor `SwitchedV2(candidate)`.
- [ ] **GS2-12.6 — Verify schema and migration.** Re-read every migrated subject, relation, field,
  projection, archive digest, repository setting, receiver, and phase receipt against the manifest.
- [ ] **GS2-12.7 — Run closed-fleet canaries.** In the isolated cutover program, exercise intake,
  roadmap, hierarchy, dependency, claim/touch set, review, delivery/done, settings, event repair, merge group,
  and release observation without opening ordinary production writes.
- [ ] **GS2-12.8 — Inject wrong paths.** Test stale/missing/contradictory/partial/rate-limited/unauthorized
  observations, lost webhooks, altered fields, wrong receiver, missing check, package-not-served, and ledger
  tamper; each must refuse, repair, or remain explicitly indeterminate.
- [ ] **GS2-12.9 — Rehearse rollback from the switched state.** Verify every rollback precondition and
  operation without deleting v2 evidence. If any rollback step is not executable, do not open.
- [ ] **GS2-12.10 — Commit verification.** Independent reviewers accept Q8 and the operator commits/anchors
  `VerifiedV2(evidence)`. Failure chooses repair-and-reverify or rollback while writes remain closed.

### GS2-13 — Open v2, fence v1, and enter observation

**Parent:** `.github#2965`
**Owner:** protected cutover operators followed by repository maintainers
**Depends on:** authoritative VerifiedV2 state and final human approval
**Exit gate:** Q9 authoring-fence proof and authoritative `ObservingV2`

- [ ] **GS2-13.1 — Present the irreversible decision.** Show exact manifest/candidate, Q0–Q8 roll-up,
  open risks, rollback status, operational ownership, and the consequence that v1 cannot resume afterward.
- [ ] **GS2-13.2 — Commit `OpenV2`.** Obtain protected human approval, commit/anchor
  `OpenV2(acceptance)`, verify it independently, then enable only v2 normal writers.
- [ ] **GS2-13.3 — Complete one bounded real journey.** Take a low-risk work item from intake through
  native hierarchy/dependency, claim/touch set, review, delivery, merge/post-merge verification, and done.
- [ ] **GS2-13.4 — Establish operational watch.** Monitor errors, indeterminate operations, repair lag,
  event backlog, API budget, queue/CI latency, release state, and old-client attempts; recovery is roll-forward.
- [ ] **GS2-13.5 — Disable v1 authoring.** Revoke old write credentials, schedules, dispatch routes,
  installations, moving-ref exceptions, and mutation entry points. Retained source/binaries are inert and
  no longer resolvable from a normal production route.
- [ ] **GS2-13.6 — Prove the permanent fence.** Attempt every v1 write class and representative old-client
  generation after `OpenV2`; each refuses before external effect for independently observed epoch,
  credential, route, or installation reasons.
- [ ] **GS2-13.7 — Seal observation assets.** Publish the content-addressed v1 archive, verifier, manifest,
  lookup guide, retained inert recovery inputs, and 15-item observation/contraction plan outside the v2
  production dependency closure.
- [ ] **GS2-13.8 — Normalize safe repository policies.** Remove only temporary freeze restrictions needed
  to operate v2, enable approved merge queues/settings, retain contraction safeguards, and re-inspect every
  repository profile.
- [ ] **GS2-13.9 — Commit `ObservingV2`.** Bind the open receipt, permanent v1 authoring-fence proof,
  first real journey, operational dashboard, sealed assets, and the fixed 15-item reading definition below.
- [ ] **GS2-13.10 — Hand off the observation.** Assign owners and SLOs for incidents, indeterminate
  operations, action items, old-client attempts, and the later contraction; no destructive v1 deletion is
  allowed before the 15-item Q10 gate.

### GS2-14 — Observe, improve, and close the renovation

**Parent:** `.github#2965` and Epic `.github#2952`
**Owner:** `FS.GG.Coordination` and `.github`
**Depends on:** ObservingV2
**Exit gate:** Q10, authoritative `OperatingV2`, and closed Epic

- [ ] **GS2-14.1 — Record the immediate reading.** At entry to `ObservingV2`, capture journey success, incident count,
  partial/indeterminate operations, old-client attempts, API cost, event repair, queue/CI/release latency,
  and remaining deletion debt.
- [ ] **GS2-14.2 — Record 15 completed-work readings.** Count 15 distinct, real production work items first
  admitted after `ObservingV2`, each completed through the enabled v2 path with native delivery and required
  post-delivery verification accepted. The GS2-13.3 first journey, migration rehearsals, canaries, synthetic
  items, duplicate completions, and work completed under v1 do not count. Fix the eligible population,
  completion definition, SLOs, and observation fields before the first counted item. Record a source-bound
  reading after each completion, including incidents, unresolved effects, repair lag, old-client attempts,
  API cost, and queue/CI/release behavior. Keep failed, abandoned, and pending attempts in the denominator
  and incident record; they cannot be counted as completions or silently replaced. Preserve identical
  definitions across all 15 readings and the immediate baseline. No elapsed-day minimum substitutes for
  completed work.
- [ ] **GS2-14.3 — Complete remaining roll-forward repairs.** Each incident receives a typed cause,
  bounded fix, regression oracle, and evidence; recurring missing concepts return to the specification.
- [ ] **GS2-14.4 — Approve contraction.** After the unchanged 15-item definition passes, independently
  review incidents, open action items, old-client attempts, sealed assets, and exact deletion plans; commit
  `ContractingV1(plan)` or extend observation without changing the eligible population or denominator.
- [ ] **GS2-14.5 — Delete v1 runtime code.** Remove v1 readers/writers, public generic mutation routes,
  compatibility adapters, old event/schema decoders, and source packages/workflows after exact static and
  runtime inventory checks. Include Contracts/Store's Core assembly dependency, Store's historical
  CLI-path source link and Host's packaged closure in that inventory. Retain or extract reusable code
  only with clean-install, package/loaded-assembly and old-client refusal proof.
- [ ] **GS2-14.6 — Delete v1 data authorities.** Remove Class/Kind/Repo Scope/Blocked-by Project fields,
  body sentinels/metadata parsers, old status writers, control comments used as authority, and temporary
  backfill projections after exact deletion checks.
- [ ] **GS2-14.7 — Delete v1 operational infrastructure.** Remove remaining obsolete App permissions,
  environments, branch/ruleset exemptions, caches, and retained recovery assets that are unsafe after the
  observation gate; preserve only the sealed audit package and verifier.
- [ ] **GS2-14.8 — Verify deletion and sealed-history access.** A clean checkout/install contains no v1
  production path, every old client remains fenced, and a maintainer can still explain and verify every
  archived operation.
- [ ] **GS2-14.9 — Reconcile public architecture and operations docs.** Update the component map,
  coordination guide, recovery guide, security model, release guide, skills, and website status from v2
  authority; remove renovation warnings only when the contraction gate passes.
- [ ] **GS2-14.10 — Release deferred programs.** Re-observe the normalized receiver fleet and authorize
  P5 or other `cutover-deferred` work to resume through v2. Do not reuse a pre-cutover claim, review,
  candidate, or release receipt.
- [ ] **GS2-14.11 — Commit `OperatingV2`.** Bind the completed deletion ledger, clean-install/runtime
  absence proof, sealed-history verification, normalized settings, and final Q9/Q10 evidence.
- [ ] **GS2-14.12 — Close children and Epic.** Verify every child issue and native subissue relationship,
  accept Q10, publish the final report, and close `.github#2952` only when no legacy production authority or
  unowned follow-up remains.

## 6. Work intentionally outside the cutover critical path

The following work may use the same kernel or GitHub capabilities, but it is not necessary to make v2 live
or v1 retired. It must not delay `GS2-11` once all actual cutover prerequisites are qualified:

- [`.github#2959`](https://github.com/FS-GG/.github/issues/2959), the advisory-only GitHub Agentic
  Workflows pilot;
- migration of the ADR corpus to a future typed `DecisionExtension`;
- broader Typed SDD extensions for contract topology, skill delivery, Governance rules, provider/template
  composition, and executable TestSpecs;
- a later decision to make `typed-sdd` the default lifecycle, subject to the `GS2-10` candidate-freeze
  rule above;
- optional F# ports of Coordination credential transport, release custody, telemetry successors and
  shell launchers from [V2-FS-I1](#v2-fs-i1--f-automation-convergence-interlude), except a bounded
  selected prefreeze cohort or a separately proven required-gate repair; and
- convenience UI, reports, or projections that do not authorize a coordination decision.

These may proceed independently with their own evidence. If one becomes a real prerequisite, the governing
design and this dependency map must be amended before it can block the fleet cutover.

## 7. Fleet-by-fleet adoption checklist

For `.github`, SDD, Rendering, Governance, Templates, Game, Audio, and Net, the cutover manifest must carry
one row proving all applicable obligations:

- [ ] bridge artifact and epoch behavior;
- [ ] exact v2 tool/kit/workflow pins;
- [ ] repository custom properties and desired profile;
- [ ] required aggregate checks on pull requests and merge groups;
- [ ] immutable Actions/reusable-workflow references;
- [ ] App installation and least permissions;
- [ ] branch/tag rulesets, merge policy, bypass, and temporary freeze restriction;
- [ ] release environment, OIDC, immutable release/tag, SBOM, attestation, and feed behavior where publishing;
- [ ] webhook/event coverage and scheduled audit repair;
- [ ] open claims, reviews, deliveries, releases, queues, and dependency-update PR disposition;
- [ ] migration and rollback receipts; and
- [ ] post-retirement absence of v1 writers and configuration.

External roster rows are observed and reported. They change only through their owner and an explicit
disposition; the FS-GG cutover must not silently assume administrative authority over them.

## 8. Mandatory failure matrix

The qualification corpus must include at least these independent controls:

- an old client attempts every write class after `FreezeRequested`;
- the epoch ref, ancestry, phase tag, manifest digest, or issue projection disagrees;
- a page truncates while total count or terminal cursor claims more data;
- GitHub returns success but authoritative re-read is absent or contradictory;
- a relation changes concurrently between plan and apply;
- a Project item is missing, duplicated, archived, draft, external, or unreadable;
- an issue field exists under the wrong data type or option vocabulary;
- a claim/touch-set projection is stale, edited, deleted, or from another generation;
- two workers or executors race claim, operation-lock, relation, review, and delivery decisions;
- a webhook is duplicated, reordered, dropped, forged, or outside installation scope;
- a required check is absent, path-filtered, renamed, stale-green, or missing on merge group;
- a receiver resolves the wrong package, workflow SHA, model fingerprint, or settings profile;
- a ruleset/settings plan partially applies or loses permission mid-operation;
- a package upload succeeds but either feed or public download does not serve the exact bytes;
- an immutable release/tag rejects an attempted rewrite needed by a mistaken recovery path;
- rollback is interrupted after every step through `VerifiedV2`;
- `OpenV2` is attempted without exact protected approval or complete Q8 evidence;
- v1 deletion starts before `OpenV2`, or a v1 production path survives Q9; and
- a generated test set is self-consistent while an independent black-box oracle disagrees.

## 9. Stop and return to design when

- a unit requires an untyped authority, mutation, or permission escape hatch;
- GitHub cannot provide the revision/completeness semantics assigned to a native authority;
- a v1 writer cannot be fenced, disabled, or credential-revoked before freeze;
- a normal v1 and v2 writer must be active simultaneously;
- the candidate changes after qualification without receiving a new identity and full rerun;
- migration needs a heuristic default for an ambiguous semantic fact;
- live work cannot be drained or migrated without rewriting accepted evidence;
- rollback through `VerifiedV2` depends on deleting new evidence;
- merge queue can bypass or omit a temporal required check;
- a cutover operation exceeds its measured API/permission/time envelope with no reviewed alternative;
- the new repository starts copying generic kernel or another component's private domain union; or
- added compatibility, workflow, command, and parser surface exceeds the accepted deletion ledger.

## 10. Definition of live and retired

The new system is **live** only when:

1. the protected ledger is at `OperatingV2` descending from the accepted manifest;
2. only exact qualified v2 artifacts can perform normal coordination mutations;
3. native issue types/fields/relations and desired repository settings match the compiled model;
4. claims, touch sets, reviews, delivery, releases, and repair paths retain their required custom guarantees;
5. event and full-audit reconciliation converge;
6. every receiver resolves the expected immutable artifacts and required checks; and
7. the first bounded real journey and immediate operational reading are accepted.

V1 is **retired** only when:

1. no executable, workflow, schedule, App route, credential, public command, or admin recipe can author v1
   production state;
2. old Project fields and issue-body semantic parsers no longer influence decisions;
3. compatibility and migration code is absent from the production dependency closure;
4. temporary cutover bypasses and restrictions are removed;
5. historical state is sealed with independently runnable verification;
6. old clients fail closed against the v2 epoch; and
7. Q9 and the 15-item Q10 reading report no unowned legacy authority.

Closing an issue, merging the last implementation PR, or setting a Project card to Done is not by itself
evidence that either definition holds.

## 11. Updating this roadmap

- Architecture changes require the governing design and ADR to change first.
- Sequence, prerequisites, unit boundaries, and exit-gate changes update this file.
- Typed SDD P5, generic-kernel, provider/scaffolder, registry, workflow, receiver, or settings changes
  update the concurrent-change ledger until `GS2-10`; after that point they invalidate the candidate or are
  recorded as `cutover-deferred`.
- A former M-series issue may not return to a schedulable state without an explicit mapping showing which
  GS2 requirement is missing and why the existing transfer does not already own it.
- Implementation semantics live in the typed `FS.GG.Coordination` specification and are projected here by
  stable unit/subject links once that compiler exists; do not copy union cases into this roadmap.
- Accepted unit receipts append their source/artifact/evidence links to the unit; they do not replace its
  acceptance text.
- A newly discovered requirement is recorded even if the vocabulary is missing. It blocks the affected
  transition until the specification is extended; discovery itself is never suppressed.
- The “Ongoing renovations” website notice remains until `GS2-14.5` and the 15-item Q10 gate are accepted.

## 12. Accepted amendment: routine-development simplification and ordinary v2 carryover

Status: **amended by explicit human decision on 2026-09-08: routine delivery is the active default process;
candidate and receiver qualification and protected-effect authority remain independently required**.
The [analysis and disposition](coordination/2026-09-07-131445-v2-roadmap-ci-simplification-analysis.md)
compares the [radical development proposal](2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md)
with the current roadmap. This section is the accepted integration contract for that profile. Sections 1–11,
all unit IDs/titles/states, accepted histories, prerequisites and exit gates retain their current meaning.
No row below independently authorizes execution, accepts a unit, changes the operating epoch or approves a
protected effect.

For all unified-roadmap work, only a recorded explicit human instruction selects heavyweight ceremony for
named scope; absence or ambiguity selects routine. Strict labels, GS2 registration, protected paths, policy
changes, modeled work, protected operations and inherited strict state are not heavyweight triggers. One
accountable owner uses one routine branch and PR, focused and native checks, same-PR repair, native
merge/readback and asynchronous telemetry. Issue/claim, SDD artifact families, phase lifecycles, mandatory
critics, feedback/receipt cycles, metadata-`Done` and projection PRs are not delivery prerequisites.
Substantive formal/model checks, permissions, release/deploy/credential/destructive/cutover safeguards and
external acceptance remain orthogonal and fail-closed; missing authority leaves the affected effect pending.

### 12.1 Obligation scope and selection

GS2-06.7 establishes sound selection over a declared obligation graph, stable aggregates and explicit
NotApplicable outcomes. The radical proposal also removes obligations and permits simpler routine
selection with a deliberately lower guarantee. Automation and guarantee reduction are different decisions.

The implementation sequence is to remove proven duplicate or diagnostic obligations from the approved routine
policy, preserve cheap automatic subject-based reuse, then measure the resulting path. Adopt a simpler
path/project selector only if its remaining cost justifies that further policy change and its accepted
coverage/recovery tradeoff is explicit. No mutable label, skip flag or candidate-edited classification can
remove an obligation. Until a replacement is accepted, GS2-06.7's soundness, unknown-impact refusal and
sentinel response remain authoritative; its accepted receipt is not rewritten to describe a new policy.

The accepted profile applies routine delivery to ordinary development, modeled protocol changes and source
work associated with protected operations. GS2 registration does not select heavyweight ceremony or require a
new policy registry. Formal-input changes, comprehensive GS2 parent closure, freeze, release, cutover,
rollback authority and OpenV2 retain their accepted qualification. Removing an agent handoff does not remove
Quint authority, current implementation correspondence, typed effect plans or external fencing.

### 12.2 Integration by existing unit

The following is the binding amendment scope to reconcile with each owning versioned unit contract before
that unit is used for execution. It adds requirements or scoped distinctions; it does not silently delete
existing acceptance cases. Already registered work needs explicit owner disposition, and a new requirement
is not retroactively attributed to an old receipt.

| Existing surface | Accepted amendment | Evidence or disposition before claiming it |
|---|---|---|
| §1 execution/evidence and GS2-05.6 delivery semantics | Use routine source delivery by default while retaining automatic technical records where useful | Map native merge, technical checks, journal authority and publication state; a merge alone does not authorize a protected effect |
| GS2-06.7 CI policy | Record retained, removed and asynchronous obligations for the chosen routine profile, plus the explicit selection guarantee and fallback scope | Accepted policy/model change where necessary, corresponding aggregate/settings behavior and real receiver selection cases; preserve the original accepted record |
| GS2-07.6 queue pilot | Add bursts of PR edits, superseded hints, unrelated routine PRs, source/base movement and required-check identity changes | Bound work and waiting attributable to the burst; no unsafe cancellation of in-flight effects, missing required context or stale-green authorization |
| GS2-07.7 event benefit | Measure narrow reconciliation, coalescing, audit repair and queue isolation by subject | Lost hints eventually repair; distinct subjects and non-coalescible commands are preserved; full audits do not become universal routine merge dependencies |
| GS2-10.1 candidate freeze | Bind the approved routine-policy version, selected obligation/aggregate set, driver/tool identities and intended receiver classes | Identify whether the profile is enabled in this candidate or explicitly deferred; an unimplemented proposal is not frozen as supported behavior |
| GS2-10.5 receiver preparation | Prepare exact pins, required contexts, generated guidance and configuration for the default routine profile | Installed clean/upgrade receiver cases show the effective policy; no hidden legacy validator or phase/receipt requirement contradicts the profile |
| GS2-12.7 closed-fleet canaries | Add a routine-profile example beside the comprehensive protocol journey, in the isolated cutover environment | Same-PR repair, absent usage and delayed views preserve correct delivery facts; no ordinary production writes are opened early |
| GS2-12.8 failure matrix | Cover self-edited eligibility, omitted or stale required checks, wrong policy pins, lost/duplicated hints and unavailable observers | Missing technical/authority evidence refuses its action; missing telemetry cannot certify efficiency and does not become a merge authorizer |
| GS2-13.3 real journey | Exercise an actual low-risk ordinary change under the enabled profile as well as the required protocol capabilities | One owner/PR and selected checks where adopted; native code delivery distinct from publication pending; required protocol cases remain covered separately or in the same valid journey |
| GS2-14.1/14.2 operational readings | Add whole-unit model overhead, absolute cost per delivered unit, delivered fraction, unknown coverage and attributed repairs | Common definitions across the immediate baseline and 15 completed-work readings; ordinary v2 measured independently of migration-driver history |
| GS2-14.5–14.9 deletion and documentation | Include obsolete routine caller instructions, required contexts and receipt/projection-only work in the adopted profile's retirement inventory | Inspect published clean/upgrade receiver behavior; retain forensic history and still-required automatic protocol evidence |
| GS2-14.10 deferred programmes | Carry pending profile/default work with an explicit owner and target receiver population | Resume under the authorized current epoch; do not claim a pre-cutover pilot establishes post-adoption defaults |

### 12.3 Make asynchronous reconciliation precise

Native PR/check/merge state can establish source-delivery facts under the adopted policy. A delayed board,
roadmap or usage projection must not independently revoke that fact. Missing required CI, an unknown merge
outcome, unverified publication or absent mutation authority remains a technical/recovery condition, not an
observer failure. Keep code-delivered, publication-pending and derived-status-stale distinguishable.

A future event policy may coalesce hints for the same subject and re-observe fresh state before planning.
It may not discard distinct-subject work, semantic journal events, approval changes or non-idempotent
commands merely because a newer event arrived. Safe coalescing of pending hints is different from cancelling
an effect already applying. Preserve the shared external write authority and scheduled full-audit recovery;
do not split writing concurrency groups simply to shorten the queue. For ordinary merge, any retained
required check should establish a named technical or authority predicate rather than wait for a full board
scan. Changes to actual workflow triggers and required contexts belong to their owning policy implementation.

Qualification examples are a burst of edits on one PR followed by an unrelated routine PR; duplicate
and dropped hints across two subjects; observer outage during valid delivery; source or policy drift during
merge readiness; and a protected operation racing with coalesced observations. Declare event count, queue
age, API/runner cost, detection/repair delay and delivery outcome. A fixed numerical bound comes from the
candidate's measured workload and policy, not from the historical migration-token percentage.

### 12.4 Carryover, performance and operating-epoch decisions

R5 carryover needs actual ordinary v2 execution and the effective published receiver guidance. Source
adapters, generated fixtures, a current-v1 pilot or one comprehensive migration journey cannot substitute.
The example set includes an ordinary code PR, same-PR source repair, unrelated base advance under the chosen
native policy, usage loss, delayed projections, an applicable supported PR-less operation, clean/upgrade
receivers and protected-route refusal. Reuse existing generic evidence only when its subject covers the
receiver. No engine or template must build an unsupported operation solely to produce an artificial example.

Functional correctness of a profile claimed by the frozen candidate belongs in that candidate's receiver
and canary qualification. The accepted 10% model-overhead objective/20% ceiling is a separately measured
efficiency claim. Record productive work P, overhead O, O/(P+O), absolute costs, delivered fraction and
cancelled attempts; include planning, review, coordination, delivery, shared-flow maintenance and 30-day
attributed repairs. Input/output tokens, priced cost, runner time, active time and wall time remain separate.

Missing quantified usage U uses the conservative bound (O+U)/(P+O+U); unquantified gaps mean insufficient
measurement. The radical proposal's initial comparison uses at least ten candidate and ten comparable
baseline code items, at least 95% independently assessed usage coverage and a conservative bound within the
ceiling. This is not p95/p99 or rare-defect equivalence evidence. Keep every over-budget item visible; do not
improve the score by routing expensive work out of the accounting population.

Accepted gate disposition: absent efficiency evidence blocks a simplification-success claim, not `OpenV2`
or `OperatingV2` solely because of this profile's efficiency objective. Existing Q10 SLOs and gates still
apply. The numerical ceiling is not an additional migration gate; changing that disposition requires a later
explicit accepted amendment with its cohort, minimum coverage, stop rule and owner. A profile that fails its
required functional/safety claims is a different case and cannot be represented as qualified merely because
its overhead is low.

The routine profile is adopted before GS2-10, so qualify and freeze its actual published artifacts or
explicitly defer it from that candidate. Any later inclusion mints a new candidate through the existing full
requalification route; never modify a frozen candidate in place. Workspace-default changes deferred until
`OperatingV2` remain so.
A later default/profile adoption starts its own applicable receiver and repair observation; earlier Q10
readings do not magically cover users who only receive it afterward. R4 current-route completion and R5
ordinary-v2 completion remain separate, with pending work owned rather than counted as delivered.

### 12.5 Adoption order and roadmap identity

The implementation order is: decide policy/guarantee scope; amend the governing design/ADR
where needed; publish affected producer contracts and runtime behavior; update receiver guidance/settings;
reconcile the named GS2 unit contracts/catalog and exact roadmap pin; qualify the candidate through its
existing boundaries; and measure real enabled operation. Preserve accepted historical receipts and
explicitly identify any superseded contract for future candidates. Optional OR/PB work is not a prerequisite.

Classify profile changes against §2's restricted v1-engine scope as well as the future v2 surface. Removing
shared-driver instructions is not automatically a permissible v1-engine feature change. A helper that changes
v1 protocol meaning needs the relevant governing decision; calling it native delivery does not evade that
boundary. `.github` owns policy/current supported delivery, Coordination owns v2 semantics, SDD owns its
producer boundaries, and receiver owners verify effective installed behavior.

The current roadmap/index protocol pins an exact Git revision and full-file digest. Accepting this amendment
changes those bytes even though executable headings, unit bodies and acceptance history are unchanged.
Existing consumers continue to resolve their exact previously pinned revision and must not pair that old
catalog digest with current-main bytes. Adoption therefore requires the normal protected pin refresh; a
unit-contract change additionally needs its own contract/evidence disposition. This amendment does not by
itself update a receiver pin, force an active worker to restart, enable a v2 writer or claim the profile has
passed candidate qualification.
