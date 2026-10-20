# Programme context efficiency and deterministic coordination

Identity: **V2-CTX-01**. Authored: **2026-10-05**.
Status: **delivered coordination components selected as the user-requested default; full integration and economics pending**.
Owner: `.github` programme-driver maintainer, with Coordination execution and UTEL/LEARN measurement owners.
Parent: [Unified Roadmap, section 9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).

## Outcome and scope

Reduce repeated parent investigation, unnecessary model wakeups and recovery-context growth while
preserving decisions, acceptance quality and original operation identities. Extend the existing
[temporary programme driver](unified-programme-driver.md), its F# helper and existing observation
adapters. Keep detailed investigation with the accountable lane owner; return current facts and explicit
exceptions to the parent. Evaluate total task cost as well as parent context size.

The current design has useful context controls and a deterministic advisory scheduler. It is **not yet
shown to minimize context churn**, and it does not implement an end-to-end deterministic decision and
execution loop. Maximizing the amount delegated is also the wrong objective: a smaller parent can hide
more duplicated work among children. The proposed objective is less redundant context and coordination
per accepted item, with unchanged quality and honest accounting for all participants.

This document proposes future source changes. It changes no skill instructions, model routes, schemas,
resource limits, execution authority, required checks or generated workspace bytes. It neither resumes
active lanes nor replaces their supervisors. No new permanent orchestrator, event journal, memory
service, vector database or per-lane manager is selected. Adoption remains bounded to the temporary
programme driver unless a later consumer is explicitly selected.

## User-selected default, 2026-10-05

The user explicitly selected the improved `$work-programme` workflow as the default. The skill now
uses delivered typed owner returns and compact delta views, bounded evidence retrieval, mechanical
reuse advice and bounded hosted-check watchers for applicable new work. Existing prose-only attempts
retain their identities and evidence; new packet-bound continuations establish truthful typed returns.
Missing bases resynchronize without releasing unknown reservations or renewing source timestamps.

This instruction supersedes the proposal's earlier default/adoption deferral for these delivered
components. It does not claim completion of .1 or .4–.6, an end-to-end deterministic executor, measured
savings, new process containment or installed consumer qualification. Resource and native acceptance
contracts remain in force. Continue measurement and recovery validation during ordinary work; roll
back the affected coordination component if it loses obligations, lineage or effect correspondence.

## Selected delivery reliability repair, 2026-10-11

The user selected publication reliability alongside original-journal recovery.
The existing UTEL publisher and terminal adapters remain the owning surfaces:
cache immutable Git objects, reread live authority, count admitted application
requests, checkpoint on provider waiting, and collect logs plus available result
bytes on every terminal outcome under the same original bounded operation.
Do not renew a deadline, normalize partial evidence into completion, or add a
separate observer service or ledger. These source changes require their own
qualification and native acceptance; historical ordinary request totals remain unknown.

Release source reservations when first publication is terminal and the
initialized journal permits a validated descendant publisher with unchanged
candidate intent. Documentation integration can then proceed; subsequent effects
still require fresh source, authority, quota and admission checks. Report the
current phase, concrete next task, blocker age and conditional remaining window.
Distinguish checks-green from accepted closure, and separate observed useful
verification, external waiting, repair and coordination intervals where evidence
supports that attribution. Leave unobserved active durations unknown.

The retained 2026-10-11 baseline is two projection deliveries: AT PR4420 was green
at 10:38:23 UTC and merged at 11:38:03 (3,580 seconds); Board PR4421 was green at
11:17:25 and merged at 11:39:05 (1,300 seconds). Original publication terminated
at 11:28:57 and its reservation was released at 11:37:59.129923. The intervening
542.13 seconds combine detection, reconciliation, eligibility and coordination;
category active time is unknown. Release-to-merge was 3.87 and 65.87 seconds.
These are projection delivery intervals, not whole-journey overhead or a measured
cost-saving result. Preserve their original receipts instead of another metric ledger.

## What exists and what remains unproved

The audit inspected `.github` main at
[`2859e8b2a8f0683b4947765a0ef23f863090bfa2`](https://github.com/FS-GG/.github/tree/2859e8b2a8f0683b4947765a0ef23f863090bfa2).
The source links below pin that observation. This is a source/design review, not a live census of
workers, a rerun of the acceptance suite or a token-cost experiment.

| Surface | Existing behavior | Gap and consequence |
|---|---|---|
| [Skill](https://github.com/FS-GG/.github/blob/2859e8b2a8f0683b4947765a0ef23f863090bfa2/.agents/skills/work-board/SKILL.md) | Parent owns scope, capacity and integration; owners retain investigation. New workers receive explicit routes and no inherited conversation; normal repairs reuse the owner. Returns target 150–300 words. | These are instructions to the model. The helper cannot establish that the parent actually avoids duplicate investigation, or that workers received all applicable obligations. |
| [Frontier function](https://github.com/FS-GG/.github/blob/2859e8b2a8f0683b4947765a0ef23f863090bfa2/.agents/skills/work-board/scripts/programme.fsx#L109) | Given explicit time and snapshot, sorts by priority and ID; checks freshness, dependency boundary, retained owners, PR limits, capacity and touch-set conflicts. Produces advisory actions. | Observations, priority and scalar capacity arrive from callers. It neither collects source facts nor performs admission, dispatch, review or effect settlement. CLI invocations obtain current time, so replay needs the same time input; identical files run later need not yield identical freshness decisions. |
| [Packet builder](https://github.com/FS-GG/.github/blob/2859e8b2a8f0683b4947765a0ef23f863090bfa2/.agents/skills/work-board/scripts/programme.fsx#L155) | Hash-pinned references, mandatory whole instructions, optional excerpts, explicit omissions, maximum 256 KiB. | Byte bounds do not measure tokens, relevance or subsequent retrieval. Completeness is relative to the caller's mandatory list; an omitted applicable instruction cannot be discovered from that list alone. |
| [Artifact verifier](https://github.com/FS-GG/.github/blob/2859e8b2a8f0683b4947765a0ef23f863090bfa2/.agents/skills/work-board/scripts/programme.fsx#L190) | Streams declared files, checks bytes/hashes and rejects linked paths. | Equality does not prove authenticity, complete evidence population, semantic acceptance, cleanup or current permission. Adding a generic success flag would erase essential distinctions. |
| [Measurements and report](https://github.com/FS-GG/.github/blob/2859e8b2a8f0683b4947765a0ef23f863090bfa2/.agents/skills/work-board/scripts/programme.fsx#L243) | Retains helper inputs/results and counts helper bytes, durations and failures. Reports native tokens as unknown and compactions as not observed. | This is not parent prompt accounting or whole-family cost. It excludes many reads, messages, model turns and worker histories. Fewer helper bytes cannot establish a cheaper programme. |
| [Acceptance fixtures](https://github.com/FS-GG/.github/blob/2859e8b2a8f0683b4947765a0ef23f863090bfa2/tests/work-programme/acceptance.fsx) | Covers frontier refusals, packet completeness relative to declarations, drift, links, strict JSON and deterministic replay. | Does not demonstrate adapter observation correctness, worker-return semantics, interruption recovery or context savings in real use. |

The driver plan records an entry-point reduction from 21,213 to 8,599 bytes, or 59.5%. That is a
historical static byte comparison, not an observed reduction in tokens, compactions or total cost.
The operational hypotheses to test are repeated evidence reads, hand-assembled snapshots, verbose
returns, redundant reviews and model polling. Their frequency and economic impact are currently
unmeasured by this audit; do not publish inferred counts from an anecdotal conversation.

Earlier work already owns important parts of the solution:

- [Skill context budgets and progressive disclosure](../reports/2026-07-25-223627-skill-context-budget-and-progressive-disclosure-roadmap.md)
  addresses catalog size, complete skill transport and selective reference loading. Preserve those
  mechanisms instead of another round of skill splitting.
- [Coordination churn redesign](../reports/2026-08-14-090508-coordination-churn-redesign-roadmap.md)
  identifies typed complete reads, explicit intent and revisioned inputs. Reuse its boundaries rather
  than parse prose into scheduling authority.
- [LEARN-01](2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md) owns controlled
  context comparisons and whole-issue economics. The existing [observation contract](../../.agents/skills/work-board/references/observation.md)
  owns lineage, coverage and telemetry integration. This feature supplies a candidate treatment.
- [V2-PROC-01](2026-10-05-shared-process-supervision.md) owns bounded command execution and containment.
  Shared process mechanics can reduce repeated supervisor investigation, but cannot by themselves
  select context, validate semantic returns or measure token savings.

## Prior art and disposition

Primary sources were reviewed online on 2026-10-05. The application column is a design inference;
results from other tasks and models are not estimates of FS-GG benefit. No dependency was installed or
benchmarked for this analysis.

| Source | Relevant finding or mechanism | Application and limit |
|---|---|---|
| [Anthropic: effective context engineering](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents) | Lightweight references, selective retrieval, compaction and specialized contexts address long-running work. | Load exact evidence on demand. Preserve obligations and unresolved facts through compaction; a short summary is not authoritative state. |
| [Anthropic: multi-agent research](https://www.anthropic.com/engineering/multi-agent-research-system) | Independent exploration benefits from clear task boundaries and compact returns, but delegation increases token use and is less suitable for tightly coupled work. | Use lane owners for independent work. Measure all children and coordination; do not transfer research benchmark gains to repository delivery. |
| [Anthropic: building effective agents](https://www.anthropic.com/research/building-effective-agents) | Predictable workflows and open-ended agents suit different kinds of work; complexity needs justification. | Put mechanical routing in deterministic code and semantic investigation with the owner. Keep the current F# implementation before selecting a framework. |
| [LangChain: context engineering](https://www.langchain.com/blog/context-engineering-for-agents) | Separates writing, selecting, compressing and isolating context; evaluation and traces are prerequisites for judging improvements. | Treat these as distinct controls. Offload raw outputs to artifacts before adding model-based summarization. |
| [Temporal architecture](https://github.com/temporalio/temporal/blob/main/docs/architecture/README.md) | Separates deterministic workflow decisions from activities; replay and activity retry have different requirements. | Pure decision replay must issue zero effects. Reconcile ambiguous original effects through existing Execution contracts. Do not add Temporal or a second journal. |
| [Kubernetes controllers](https://kubernetes.io/docs/concepts/architecture/controller/) | Reconciliation observes current state and works toward desired state through bounded control loops. | Keep user intent separate from observations. Reconcile source facts when events are missing; worker messages are hints, not the sole truth source. No Kubernetes dependency follows. |
| [Bazel Skyframe](https://bazel.build/reference/skyframe) | Incremental recomputation depends on declaring every input dependency; hidden reads invalidate cache correctness. | Reuse mechanical evaluations only with a complete input closure and evaluator identity. File hashes cannot substitute for semantic review or live permission. |
| [LangGraph persistence](https://docs.langchain.com/oss/python/langgraph/persistence) | Distinguishes thread checkpoints from cross-thread stores. | Separate bounded current session state from retained evidence and project knowledge. Reuse existing storage; a new memory database is unnecessary for the first pilot. |
| [Liu et al., Lost in the Middle](https://arxiv.org/abs/2307.03172) | Evaluated models showed sensitivity to the position of relevant information in long contexts. | Test obligation recall and exception handling, not merely context length. These older experiments do not quantify current routed models. |
| [Cemri et al., Why Do Multi-Agent LLM Systems Fail?](https://arxiv.org/abs/2503.13657) | Categorizes failures involving design, inter-agent alignment, verification and termination. | Include stale handoffs, missing constraints and false completion in negative controls. A taxonomy motivates fixtures, not a local failure-rate claim. |

## Proposed architecture

Keep one advisory decision core and the existing effect adapters. Add machine-readable observation and
return projections where repeated manual assembly is demonstrated. Proposed record names below describe
semantics; milestone .2 chooses the versioned contract and migration after checking existing types.

```mermaid
flowchart TD
    A[Native authorities and existing operation records] --> O[Bounded observation adapters]
    W[Accountable lane owner] --> R[Typed return with evidence references]
    O --> F[Derived current facts]
    R --> F
    F --> D[Pure deterministic frontier and delta evaluation]
    D --> V[Compact parent view and exceptions]
    V --> P[Parent scope and integration decisions]
    P --> E[Existing admission and execution adapters]
    E --> W
    D --> C[Mechanical validation requests]
    C --> O
```

### Current facts and replay

Retain a bounded current view per session, with the complete active-reservation inventory and
individually timestamped facts. Large immutable evidence stays in existing private or hosted artifact
storage. Existing journals retain effect history. The view is a disposable cache: deleting it must not
lose authority, release an active reservation or imply a new attempt.

A proposed observation envelope carries source identity/revision, observation time, completeness,
provenance, affected lane, original attempt, candidate/operation identity and the typed fact. Keep
`missing`, `unreadable`, `partial`, `pending`, `failed` and `succeeded` distinct. Do not renew source
freshness when copying a cache. A poll heartbeat may prove an observer is alive; it does not make an
unchanged source fact newer. Complete absence requires the source's complete-read contract.

The pure evaluator consumes facts, explicit evaluation time, user-scope revision and policy/evaluator
identity. Stable ordering resolves independent inputs; source revisions order updates within their
own domain. There is no invented total order across unrelated providers. Duplicate observations are
idempotent; older revisions cannot overwrite newer ones; incomparable conflicting observations produce
an explicit reconciliation action. Changed user scope invalidates affected suggestions immediately.

Retain replay inputs, including evaluation time, so a reproduced decision has the same freshness
meaning. Replay emits suggested actions only. Native adapters recheck current admission before effects;
material drift rejects the stale suggestion and refreshes facts. A suggested-action identity supports
deduplication, but is never an execution grant or an exactly-once guarantee. Ambiguous outcomes preserve
the original operation and reconcile before retry; expiry or reload does not grant new time or budget.

### Delta views and bounded returns

The default parent view contains changed lane facts, newly actionable work, blocked decisions, invalidated
evaluations and a compact complete active inventory. It names its base/current revisions and observation
coverage. Unchanged logs, successful hash rows and closed-lane history stay behind artifact references.
A missing base revision causes bounded resynchronization, not application of an ungrounded delta.

A lane return should carry:

| Field group | Meaning |
|---|---|
| Identity | Campaign, feature/item, owner, original/current attempt and candidate/operation; reuse existing lineage fields |
| Revision | Return revision, superseded revision, input packet digest and actual source revisions consumed |
| Outcome | Established boundary, pending work and unknowns; source, publication, installed and native results remain separate |
| Evidence | Typed evidence references, immutable digest/revision, scope and producing evaluator where applicable |
| Exception | Concrete decision requested, affected obligations and alternatives; explicit none when no decision is needed |
| Continuation | Next bounded action, retained ownership and stopping condition; acknowledgment and terminal completion are distinct |

Keep the existing short narrative target alongside these fields. Bound machine payload and narrative
separately; preserve overflow as a retrievable artifact and surface its existence. A malformed or
incomplete return is not success. Ask the same owner to repair it before launching a replacement
investigation. Late returns from superseded owners remain historical evidence and cannot close the
current candidate. Conflicting valid returns require reconciliation, not whichever arrived last.

This contract reduces routine parent rereads; it does not ban inspection. A parent expands evidence for
an identified inconsistency, an architectural join, an explicit user request or a required root-owned
review. Record the reason using existing observation fields where possible, so repeated expansion can
be measured without adding a separate review ledger.

### Mechanical evaluation and semantic review

Separate three responsibilities:

1. **Deterministic code:** parsing, declared population checks, hashes, lineage/revision joins, freshness,
   dependency boundaries, duplicate suppression and rendering. Return typed failures and references.
2. **Accountable owner:** investigate source, interpret tests, explain correctness and repair the item.
   A domain-specific validator may check structured evidence only within its established contract.
3. **Parent or required authority:** resolve scope, competing claims, cross-lane joins and exceptional
   decisions. Preserve existing independent review and operation acceptance when required.

Cache only evaluations with a declared complete input closure: evidence identities, schema/evaluator
version, relevant source/configuration, acceptance profile and applicable policy identity. Changes
invalidate the affected evaluations and their dependents. Time-sensitive facts expire independently.
If the dependency closure is unknown, recompute or request the missing evidence. A sampled audit cannot
replace a required check. Reuse follows [ADR-0084](../adr/0084-semantic-reuse-never-cancels-coherent-validation.md);
coherent validation and later disputes retain their existing meanings.

An evidence hash proves equality to an expected value only when that expectation is itself trustworthy.
A successful mechanical report cannot mint semantic acceptance, signature authenticity, cleanup or
permission. Historical semantic findings may be referenced with their exact scope; the cache must not
silently promote them to acceptance of a changed candidate.

### Context selection and delegation

Build task packets from the actual instruction hierarchy, explicit task scope and acceptance obligations.
Make instruction discovery visible to the packet caller; do not claim the current declared mandatory
list proves universal completeness. Include whole applicable instructions and a small current-state
summary, then references to pinned source/evidence. Keep instructions and untrusted data distinct.
Instructions embedded in logs, web pages or worker artifacts cannot override the governing packet.

Use bounded retrieval by artifact identity and section/range. Verify identity before returning an
excerpt; missing, changed or inaccessible artifacts produce explicit failure. Sanitize public output
without changing retained private evidence. Bound raw output before model exposure and return a handle
plus a useful summary. Never silently truncate acceptance-relevant findings. A cache permission check
must still apply when a later worker requests an artifact.

New independent owners use fresh contexts with explicit packets. Reuse an owner for normal repairs to
avoid reconstructing source knowledge; rotate only at a meaningful boundary or a demonstrated context
problem, with a compact checkpoint and current-source validation. Context rotation does not create a new
execution attempt or renew authority. Where the runtime cannot control inherited history or compaction,
record the capability gap; do not pretend a packet erased history already present.

Choose delegation when work is sufficiently independent and has a clear return contract. Prefer one
owner for tightly coupled changes; no critic or manager per lane by default. Worker count is bounded by
host slots, native resources, repository touch-sets and CI capacity through existing admission. Include
packet construction, worker startup, review and retries in the cost decision. A single-owner route uses
the same evidence boundaries when subagents are unavailable or prohibited. No runtime prohibition is
overridden by this design.

### Waiting, recovery and user updates

Coalesce notifications about the same observed revision. Existing bounded watchers emit material
transitions, observation failure or a due deadline; they do not wake a model for every CI tick. An
unchanged system may still need a freshness refresh or a user-requested status update. Preserve those
separate reasons for waking. Use backoff and a bounded deadline rather than an indefinitely running
service or polling agent. External events are hints; authoritative reconciliation repairs missed events.

A restart reads current user scope, active owners/reservations, unresolved effects, artifact references
and the latest factual view. It refreshes affected native facts before action. Closed history remains
retrievable without rebuilding the full conversation. Test missing checkpoints and stale references;
a summary alone must never establish completion or permission.

Generate scheduled user updates from established completion events and current blockers: completed in
the requested recent windows, work in progress, current problems and user-only dependencies. Deduplicate
by item and accepted boundary, retain timestamps, and state coverage gaps. Source merge and native
qualification may be different events; do not count the same boundary again after restart. Status
cadence follows the user's active request, not a hard-coded new programme policy.

## Measurement and experiment

Instrument the existing observation path before claiming benefit. Join parent, workers, reviewers,
retrieval, repairs and retries to the same original item. Report covered and uncovered activity; absent
usage is unknown, not zero. Do not infer native tokens or compactions from bytes, elapsed time or tool
count. The helper remains useful as a byte/duration proxy with that narrower label.

| Metric | Definition and interpretation |
|---|---|
| Parent context | Native input/output usage per decision and per item where exposed; report cached/uncached input separately. Prompt caching can reduce billed cost without reducing context length. |
| Duplicate retrieval | Repeated artifact-digest and range exposure within one owner/session, measured as repeated covered bytes. Distinguish justified refresh/review from unexplained rereads; this is not a token estimate. |
| Coordination work | Parent turns without new actionable facts, follow-ups, packet assembly, review and reconciliation cost; identify the work rather than classifying every parent turn as overhead. |
| Whole-family economics | Total observed usage and supported cost attribution for all participants, including failed attempts and abandoned work; accepted-item denominator plus failed/stopped cohort totals. No survivor-only savings. |
| Quality and progress | Acceptance failures, missed constraints, false completion, dispute/rework rate and completed scope under unchanged checks. |
| Latency and recovery | End-to-end item duration, time from actionable observation to response, and recovery reads/turns; separate CI/resource wait from model work. |
| Compaction | Native observed events only, with coverage. When unavailable, report not observed, not zero. |

Selected simplification, 2026-10-12: first use retained sanitized fixtures, then one necessary
bounded ordinary driver operation for functional acceptance. Exercise the .4 interruption/restart
and reservation obligations; report quality, parent transitions, feedback/closure latency,
coverage and available whole-family usage.
An explicit inconclusive economic disposition is valid; independently justified correctness/reliability
improvements can advance to .6 with rollback. This requires genuine operational evidence, not source tests
alone, and does not assert savings or complete LEARN. Do not replay product workloads or extend an
evaluation window just to collect metrics.

A controlled economic comparison is conditional on selecting a statistical savings or learning claim.
When that question and an existing LEARN window are admitted, compare the current driver against one fixed
treatment. Keep task population,
model/effort, acceptance obligations and resource constraints fixed; record unavoidable differences.
Randomize or use a declared matched design through LEARN, avoid cross-arm memory contamination, and
predeclare sample size, analysis, practical benefit and non-inferiority margins before observing results.
Do not treat repeated turns of one item as independent samples.

A candidate pilot target is **at least 30% less unexplained duplicate parent retrieval**, with no
increase in whole-family cost beyond a predeclared margin and no material loss of quality or response
latency. This is a proposed target, not a measured result or an active gate. Baseline findings may justify
a different declared target before treatment begins. A small smoke sample establishes feasibility only;
it cannot prove statistical benefit. Existing overhead rules remain in force; this plan adds no new
mandatory measurement service. If coverage is inadequate, publish an inconclusive result and retain
only independently justified correctness improvements.

## Selected three-outcome programme pilot — 2026-10-16

The user selected a stronger, outcome-led trial as the priority for V2-CTX-01.4–.6.
It targets **80% less coordination effort** than a comparable current-route baseline,
faster delivery and unchanged correctness, credential custody and replay protection.
The [governing replacement #4608](https://github.com/FS-GG/.github/pull/4608) is protected
and active for the three outcomes. [Automatic evidence integration #4609](https://github.com/FS-GG/.github/pull/4609)
delivers the maintained repository result and reader; its actual delivery and read-only
reconciliation passed with automatic run selection and identical tested/delivered trees.
Required tests, independent source review and operation-specific acceptance remain in force.
The earlier optional 30% duplicate-retrieval target is a different proxy; it neither proves nor substitutes for this coordination-effort target.

### ROUTINEV3 source extension — 2026-10-16

On protected delivery of this reviewed extension, apply the same lightweight route to
routine source slices selected from the Unified Roadmap v3 open index. Use the existing
owning issue and native PR; no new issue or status projection is required solely for a
routine source delivery. One accountable owner, one independent source review, relevant
tests and final required CI qualify the exact delivered code. Policy and skill behavior
remain governing changes. Existing source authorization persists without repeated parent
approval; operational admission, distinct credentials, independent trust-boundary readback
and original unknown reservations remain required.

The original three outcomes retain their completion conditions. The extension changes
coordination scope, not the current physical-copy contract or cap. The
[roadmap priorities](2026-10-07-unified-development-roadmap-v3.md#15-priority-unblockers--2026-10-16)
select the next useful work through existing owners. Apply the replacements and change
classes below to this extended scope; other work retains its existing route.

### Owners, completion and the one record

| Outcome | Accountable owner | Completion condition | Required current record |
|---|---|---|---|
| Supplier-journal `/3` compatibility | Coordination receiver/fdev1, implementing agent `/root/v3_coordination_receiver` | Merged implementation and passing `/2` compatibility plus `/3` refusal tests. Protected PR1012 supplies this source condition; native host acceptance remains in V3-ENV-01. | One owning issue linked to PR1012 and the accepted source result. |
| Finite UTEL cap | `.github` copy-accounting owner, Programme `/root` accountable until recorded transfer | Complete conservative simultaneous physical upper bound for one closed production profile; justified finite cap with headroom adopted. | One owning issue linked to ADR-0093 and the protected upper-bound contract. |
| UTEL authority service | SystemAdmin service owner; Programme owns policy, independent custodian owns enrollment | Qualified implementation and accepted deployment with custody, enrollment, verification and recovery. | One owning issue linking source, operational acceptance and restricted custody reference. |

Each issue's **current state** uses only `Owner`, `Done when`, `Current deliverable`
(PR or artifact), `Next action`, `Blocked on` (exact missing evidence or decision and
accountable owner), and `Acceptance evidence` (CI links plus native acceptance links
or restricted references when the completion condition needs them). GitHub is the source for
authors, times, commits, reviews and checks; do not transcribe them into packets,
return files, comments or handoffs. Preserve real operation identities and unknown
attempts where correctness needs them. A handoff points to the current issue and
genuinely private context; sensitive evidence and credentials stay in restricted
custody. The owning issues are [V3 `/3` #4599](https://github.com/FS-GG/.github/issues/4599),
[finite cap #4600](https://github.com/FS-GG/.github/issues/4600) and
[authority service #4601](https://github.com/FS-GG/.github/issues/4601).
The `/3` source issue is closed as historical baseline, excluded from prospective pilot
savings. #4516 and #4517 remain the existing cross-repository request mailboxes until
their reply contracts end or both sides agree to move them. The issue mapping and
governing replacement are delivered. Automatic evidence covers the maintained helper's
qualified joins; missing evidence stays unknown. Reply handling now uses deadlines
and session-start reconciliation.

One owner carries implementation, review and delivery through the completion
condition. A new lane must name the condition it materially advances or an
independently useful cross-team dependency. Keep one active implementation PR per
coherent change and one independent source review. Add no process artifact without
identifying the artifact it replaces, no check without its failure mode and
maintenance owner, and no parallel task whose coordination cost exceeds its
contribution to completion.

### Delivery classes and automatic evidence

The reviewed rule change removes the mandatory obligations identified below and
clarifies the already limited practices. Programme `/root`
is accountable for the combined governance and CI-evidence outcome; the designated
CI implementation owner carries the existing result producer and maintained reader
through source delivery. The ROUTINEV3 source extension uses the same replacements; work outside this scope retains
its existing route.

| Old requirement or recurring practice | Replacement for the original outcomes and ROUTINEV3 source | Retained boundary |
|---|---|---|
| `work-programme` default lane assignment, bootstrap and `references/bootstrap.md`: mandatory packet and dispatch index | Six-field issue, applicable instruction/source links and native PR identities | Exact scope, owner, authority, resource bounds and all reservations |
| `work-programme` lane-return/decision-view clauses, `references/adapter.md` and `routine-decisions`: serialized typed returns and reducer current view | Issue state and linked native evidence; old helpers optional for a named gap | Original attempts, first failures and uncertain effects |
| Recurring successful hash/file correspondence recitations; `work-programme` and `references/context.md` already discourage these, and no general parent file-by-file review is required | One exact native or qualified CI join, with a targeted read when missing; this clarifies the existing limit rather than retiring a mandatory review | One independent source review and semantic review of changed logic |
| Recurring unchanged-code test reruns; current guidance and ADR-0084 already limit them | Accepted unchanged receipts within proven scope; this clarifies the existing reuse rule | Relevant tests, required final-head CI and coherent validation under ADR-0084 |
| `agent-handoff`: five-minute mailbox reads and unchanged “no new mail” updates | Checks at agreed reply/escalation deadlines and session start/resume | Complete-thread recovery on missing baseline, unknown-read holds and no replay |
| `continue-from-handoff`: fixed thirty-minute status report | Material-event updates and one daily programme summary | Deadline changes, decisions and blockers are reported |
| Pre-pilot baseline/coverage declaration | Measure through ordinary delivery; evaluate savings after evidence accrues | No unsupported 80% claim or weaker correctness/custody acceptance |


Classify each coherent change once, revisiting only a material scope change:

| Class | Required route after the rule change |
|---|---|
| Non-executable, non-governing documentation and inert evidence | Owner review plus relevant automated validation. |
| Source, protocol or governing behavior | One independent review, relevant tests and required CI. Code, tests, schema, policy and skill behavior stay here even when described as inert. |
| Deployment, credentials or irreversible operations | Explicit operational acceptance, including custody, enrollment and recovery checks. |

For source review, independence means separation from implementation authorship. An authorized
fresh subagent can supply that review where the platform permits it; a second human collaborator
is not a universal requirement. Use the [governing review rule](../../.agents/skills/work-board/references/lifecycle-contract.md#qualify-the-actual-path-and-deliver-exact-source)
for exact-source review and reuse of accepted author-run Home source tests. Required CI, credential
separation and operational trust-boundary readback remain distinct obligations.

Existing authorization travels with the outcome. Ask the user again only when an
action exceeds it or a concrete unresolved user-owned decision remains. These
classes must replace conflicting governing rules in a reviewed repository change;
they do not waive branch protection, exact-head coherent validation or original
effect admission. Source delivery, publication, installation and native acceptance
remain distinct.

The reviewed governing change retires duplicate coordination artifacts and fixed
polling when protected. An automatic acceptance result qualifies separately for
the mechanical facts it covers. Until then, use native issue/PR/check facts and
one targeted read for a missing acceptance fact; mark unavailable or conflicting
facts unknown. Never restore duplicate packet/reducer machinery merely because
automation is incomplete. Retain original identities and holds.

Extend the existing `fsgg.routine-delivery/v1` result and relevant CI receipts rather
than building another evidence ledger. One machine-readable result per PR records
the actual checkout/commit and changed files, tests, required checks, reviewed
head, native delivered merge identity, and tested-versus-delivered Git tree or
canonical file-tree difference, with workflow/run/attempt references. A run's
`head_sha` alone need not identify its checkout; a synthetic PR merge and a tested
candidate head differ. Do not compare a `git archive` SHA-256 with a Git tree OID.
The owner performs the mechanical join once and requests qualification when
integration changes behavior or coherent validation later fails. Existing
consumers must accept the versioned result before it replaces a fact they currently
verify. A native exact join can already replace routine hash recitations and parent
file-by-file correspondence reads; a missing join gets a targeted read and remains
an explicit gap. Reuse unchanged-code test results only within their accepted scope;
never omit required final-candidate CI or coherent validation. Retain
independent readback where it crosses a further trust boundary, such as actual
deployment or credential enrollment. No self-declared result grants authority.

When relevant compile subjects are affected, the maintained `change-completeness` predecessor must
compile its declared engine and edge project roots before reporting compiler preparation complete. Preserve its existing focused checks and the
separate expensive-engine selector. When actual V2 dependencies are affected, run the existing
projection and actual CLI suites from those current Release outputs, with no rebuild/restore and
non-vacuous TRX checks. Collect both independent suite results before aggregate failure; failed or
unknown compiler prerequisites block them and the dependent engine. The original
[CLI compilation failure](https://github.com/FS-GG/.github/actions/runs/39040430406) and
[compiled fixture failure](https://github.com/FS-GG/.github/actions/runs/39040923947) establish distinct
coverage gaps, not passing qualification. Keep the existing five-minute predecessor cap until actual
cold/warm measurements justify a change; mock controls prove invocation/refusal wiring only. This
bounded source correction adds no workflow coordinator, model or evidence ledger and never replaces
required independent checks or complete final-candidate coherent qualification.

The selected compiler implementation derives an invocation-local non-SDK MSBuild traversal from
that same literal root inventory. Run one serial locked-restore phase with its existing default
configuration, then a separate serial Release build phase with no implicit restore. Preserve
all-root failure aggregation and missing-project/target refusal; reuse only the standard process's
completed-target cache, with no parallel output writers or independent root roster. The original
[240-second cutoff](https://github.com/FS-GG/.github/actions/runs/39065729185) compiled all roots
but interrupted the final focused CLI suite, so coverage was incomplete and the full engine remained
withheld. Keep the 240-second script and five-minute whole-job bounds, default cold fixture and
non-vacuity checks. Inert command/XML controls do not prove SDK behavior, savings or whole-job fit;
qualify those on the actual hosted candidate.

Post outcome updates when a deliverable is ready, a blocker appears or clears, a
decision is needed, or a promised delivery time changes. Publish one daily
programme summary while active. External requests retain an owner, exact reply
contract, original request identity and response/escalation deadline. Read pending
threads at session start/resume and at those deadlines; check earlier on a useful
notification, user request or before a dependent action. Failed, partial or
conflicting reads preserve the hold. Notifications are optional hints, not an
activation gate. The protected handoff rule retires five-minute unchanged-mail
checks and unchanged-hold comments. Capture
durable accepted contracts, architecture and reusable lessons beside the
implementation or in one consolidated follow-up; CI may generate Atlas metadata
and indexes, while the owner still verifies source and content. A dedicated
capture needs an independently useful finding.

### Blockers and pilot decision

The copy-accounting owner closes every retained-copy constructor and recipient
population, then proves maxima for authority, managed, Python, backend, peer,
transport/kernel/cache, diagnostic/export and observation domains. Count capacity,
headers, allocator rounding, storage metadata, transport/cache bytes, concurrent
and uncertain replacements through the original retention lifetime without a
garbage-collection credit. Calculate the conservative total and select finite
headroom and the replacement cap only after complete reviewed accounting. Send
one consolidated request to the host owner for the missing provider contract;
another isolated measurement starts only if it closes a named upper-bound gap.
The authority owner keeps one source-to-deployment plan for ownership, custody,
enrollment, verification and recovery. Original `.103` stays attempted-unknown and
unreplayed; `.104` and other original effects remain held.

Measure savings during ordinary delivery, without making measurement an activation
or delivery gate. For a supported savings claim, use a comparable current-route
population, active coordination-effort definition, attribution and coverage. Include owner,
reviewer and parent work; distinguish necessary review/refresh, implementation,
CI/resource wait, blocked time and unknown intervals. Track elapsed delivery,
coordination effort per accepted unit, blocked time, escaped defects and replay or
custody failures with the same acceptance boundaries in both periods. An 80%
reduction requires a supported effort ratio at or below 0.20, faster delivery and
no weaker correctness; incomplete coverage or incomparable work yields an
inconclusive result. Two recent Coordination source PRs had only 30–32 seconds
between final check and merge, so that interval alone cannot support the target.
The already protected `/3` compatibility result is historical baseline/recovery
evidence, not prospective pilot savings; do not rerun it to create pilot activity.
The ordinary delivery observation in [PR4609](https://github.com/FS-GG/.github/pull/4609)
records 20m22s from source preparation to protected delivery and zero extra packets,
typed-return reports, duplicate current views or manual issue polls by its implementation
owner. Owner/reviewer effort and comparable baseline coverage remain unknown, so the
80% savings claim is inconclusive. These observations did not gate delivery.
Adopt, adjust or retire the pilot after accepted results; the protected rule removes
duplicate coordination requirements, while each CI fact replaces a manual verification
only after its producer and consumer prove their failure and recovery paths.

## Implementation roadmap and parallelism

The bounded .1–.3 source window below is locally tested; milestone closure and operational adoption
remain separate. These are bounded extensions of the existing driver,
not a second rollout programme. Detail the next window when assigned; later rows define outcomes and
acceptance, not pre-authorized operation launches.

| Milestone | Owner and dependency | Deliverable and exit |
|---|---|---|
| **V2-CTX-01.1 — Baseline and bounded pilot selection** | `.github` driver owner with UTEL measurement owner; ready independently of LEARN container qualification and V2-PROC implementation | Inventory actual repeated reads, return failures and waits in an authorized observation window; identify telemetry coverage. Retain sanitized replay cases, exact baseline and one next treatment. Exit: reproducible byte/turn accounting with explicit unknown native usage and an agreed comparison population. |
| **.2 — Typed observations, returns and pure deltas** | `.github` helper owner; after .1; consult existing Coordination types | Add the smallest compatible contracts and deterministic projection, with time and evaluator identity retained. Exit: replay, duplicate/out-of-order/conflict, source freshness, changed scope and missing-base tests pass; no replay path launches effects. Version readers/writers together or preserve an explicit compatible fallback. |
| **.3 — Bounded evidence views and evaluation reuse** | `.github` packet/evidence owner; after .2 contract | Add selective artifact views, obligation discovery checks and complete-input mechanical reuse. Exit: mandatory omission/overflow, drift, untrusted instructions, hidden dependency, validator change and expired-evidence negative controls pass; semantic/native acceptance remains external. |
| **.4 — Owner and watcher integration** | `.github` driver owner with Coordination adapter owner; parallel with .3 after .2, disjoint files/worktrees | Connect typed returns and material-change notifications behind the existing route. Exit: same-owner repair, superseded return, interrupted dispatch, missed event, bounded wait and compact restart work without duplicate effects or lost reservations. Single-owner fallback is exercised. |
| **.5 — Bounded functional acceptance** | `.github` integration owner with UTEL; after .3 and .4; one ordinary admitted driver operation, independent of LEARN | Run offline correspondence and genuine integrated operation/restart checks. Exit: quality, lineage, original reservations and no-duplicate-effect controls pass; report available cost, parent churn, latency and coverage with unknowns explicit. Benefit may be inconclusive. A statistical economic comparison is required only for a statistical savings or learning claim; source tests cannot replace the real route. |
| **.6 — Bounded adoption and retirement** | `.github` driver owner; after .5 functional disposition | Adopt only qualified behavior, keep a rollback route and update mirrored skill guidance once. Exit: current driver entry points select the qualified implementation; obsolete glue is removed after retained-attempt compatibility is established. Retire with the programme or transfer only demonstrated reusable contracts to an existing owner. Economic benefit need not be proved for correctness/reliability adoption. |

Resume the current accepted frontier; do not repeat accepted portions of .1–.3. Remaining .4 integration and
.5 functional acceptance precede .6 adoption; neither a new cohort nor a benefit threshold is a gate.
Source and operational correctness work can proceed while LEARN qualification is blocked; missing
economic evidence stays explicit. V2-PROC-01
remains a complementary lane; its containment implementation is not a dependency of pure projections or
packet work. A later watcher/process-runner adoption must use its qualified profile and account for
helper/observer processes within actual capacity. Do not replace an active operation's backend.

Shared-file integration uses one integrator. .3 and .4 may proceed concurrently only after agreeing on
.2's contract and reserving disjoint touch-sets. Keep one current PR per dependency chain and existing
repository admission limits. Completed historical V2 work gains no retroactive dependency on this plan.

### Bounded source window, 2026-10-05

The [.1 baseline fixture](../../tests/work-programme/context-baseline/README.md) selects five retained
helper operations: three packet builds, a refused frontier and its repaired evaluation. Reproducible
artifact accounting covers39,546 input bytes,4,666 output bytes and267,542 packet bytes. Repeated
packet construction across owners is not actual parent reread evidence. Model turns, native usage,
compactions, waits, return failures and whole-family economics remain unknown or unobserved; this
population is an offline smoke comparison only, with economic conclusions inconclusive. The original
.1 measurement exit is not claimed complete.

The additive [.2 delta contract](../../.agents/skills/work-board/references/adapter.md#additive-offline-delta-input)
retains legacy commands and separate boundary statuses, owner/original-attempt/candidate joins,
explicit evaluation time and policy/evaluator/scope identity, and complete active reservations.
Conflicts reconcile, missing bases resynchronize, and acknowledgment cannot mint completion.
[PR #4235](https://github.com/FS-GG/.github/pull/4235) delivered the bounded .1/.2 source window.
Its combined entry point passed31 legacy,35 delta and10 Python baseline controls; two synthetic CLI
runs retained exact inputs and reproduced the expected result. This establishes source/CLI behavior,
not the original .1 whole-family measurement exit or operational adoption.

The additive [.3 evidence-view/reuse contract](../../.agents/skills/work-board/references/adapter.md#additive-bounded-evidence-view)
verifies a complete artifact's raw identity before bounded excerpts, preserves whole mandatory
instructions, and returns typed refusals without truncation. The shared reader consumes at most the
limit plus one overflow-detection byte during growth. Pure reuse advice requires the caller-declared
complete input population, pinned mechanical receipt, policy/profile/evaluator/source-config identity,
current access and independently fresh facts/receipt. It cannot reuse semantic/native acceptance or
authenticate a caller's pin. Root source qualification passed44 focused view/reuse controls and the
full120-case combined suite (31 legacy,35 delta,44 view/reuse,10 baseline); the initial compile failure
remains retained separately. [PR #4237](https://github.com/FS-GG/.github/pull/4237) delivered this source
with hosted checks and two actual CLI checks: whole-instruction evidence correspondence and
mechanical-only reuse advice. This .3 window is source qualification only. Owner/watcher integration,
live economics and operational adoption remain pending; default skill instructions are unchanged
and .4–.6 remain open.

The bounded .4 watcher slice extends the existing [delivery tool](../../tools/routine-delivery.py)
with opt-in `--watch-checks --watch-seconds N` (1–600 seconds). It checks the expected PR head before
and after each native check read, bounds direct query time and combined output during acquisition,
and emits initial/material changes, terminal state, observation failure or deadline. Unchanged polling
and unrelated PR update timestamps do not emit another revision. The route branches before telemetry
discovery and rejects `--apply`; delivery still requires its fresh canonical checks. It owns only its
direct query children and makes no descendant-containment claim.

All67 focused delivery tests passed, including deadline, cancellation, overflow, changed-head and
unchanged-output controls. An actual read-only invocation against merged PR #4237 made three native
queries and returned one terminal record covering57 checks, with readiness unevaluated and no apply
authority. This proves that terminal adapter path; it is not a measured context saving. Full .4 remains
open: historical prose dispatches lack the original packet/revision bindings required for truthful typed
returns, and interrupted dispatch, missed-event recovery and complete reservation reconstruction still
need an actual integrated window. No missing identity is reconstructed as fact.

### Bounded parent context path, 2026-10-07

The dependency-free [context helper](../../.agents/skills/work-board/scripts/context.py) projects
pinned JSON fields/lines, proof-obligation review bundles and native session diagnostics when CLR helper
execution is held. It also prepares fixed argument vectors for the existing guard without executing them
or renewing windows. [Context guidance](../../.agents/skills/work-board/references/context.md) defines
bounded parent exposure, original-identity owner rotation and compact restart navigation. Handoff recovery
has one full reader; missing delegation retains a bounded parent fallback.

The existing [serial suite](../../tests/work-programme/run.py) includes behavioral controls for drift,
whole instructions, obligation coverage, unknown counter baselines/resets, incomplete native records,
private retention and command identity joins. Native session diagnostics remain separate from genuine
dispatch/item attribution. These components do not close .4–.6 or establish whole-family savings;
actual programme use and the controlled comparison retain their original acceptance boundaries.

### Bounded restart navigation joins, 2026-10-14

The existing dependency-free context helper adds opt-in `restart` navigation verification. It checks
complete packet bytes and current owner/source joins, adjacent return revision and original-lineage
correspondence, and optional exact-head watcher identity. Watch-local revisions remain separate from
owner-return revisions. Full reservation artifacts remain opaque pinned pointers; unknown member
completeness or missing original identities hold continuation instead of creating helper lanes. Missing
bases request resynchronization. The command always requires fresh exact-head native observation and
canonical return/delta/routine validation; it grants no dispatch, retry, release or acceptance authority.

Eleven focused controls extend the existing context suite to41 passing cases. They cover changed owner,
source, packet, original attempt, missing base, watcher authority/head drift, reservation drift and overflow,
plus private CLI retention. The17 runner controls and routine eligibility/operation-boundary fixtures pass.
Retained genuine settlement packet and adjacent returns from the ordinary2026-10-14 window also verify
navigation correspondence, preserving the explicit missing439-member inventory hold. This read-only
retained-record check is not an actual interrupted dispatch/restart experiment, a qualified complete
inventory, a cost comparison or closure of .4–.6. The previous command route remains available for rollback.

## Validation, risks and rollback

Extend the existing focused F# suite and adapter fixtures rather than inventing a broad new gate.
Offline tests can verify the following without launching real workloads:

| Risk | Required negative control or recovery check |
|---|---|
| Compact state hides an active owner or late failure | Reconstruct the complete reserved set; preserve unknown effects and let a later dispute invalidate affected suggestions |
| Old evidence becomes current by copying or replay | Preserve source timestamps, source revisions and original attempt; advancing evaluation time expires affected decisions |
| Cached review ignores an input | Change policy, profile, validator, source or a declared transitive input independently; each invalidates its dependent result |
| A return falsely claims completion | Reject missing boundary/evidence, distinguish acknowledgment, and check authoritative closure instead of trusting prose |
| Compression loses a stopping rule | Inject obligations at varied locations and verify packet completeness, overflow refusal and recovery recall |
| Notifications are repeated, lost or reordered | Deduplicate known revisions; reconcile missing events; refuse conflicting state; wake for actual deadlines |
| Context offload increases cost or delays response | Include all descendants and repair work; compare quality and actionable-event latency as well as parent bytes |
| Observation or watchers interfere with qualification | Account for their process/resource use; keep pure tests independent of live workloads and reuse existing admission |
| Framework growth exceeds the benefit | Start in the existing helper; require a measured gap before adding a dependency, service or further abstraction |

Before adoption, exercise a bounded real route with normal admission, interruptions and fresh native
readback. Source tests establish source behavior only. Do not rerun native workloads solely to manufacture
context metrics or reinterpret past failures as new success.

Rollback selects the previously qualified driver for future decisions, drains or reconciles current
owners under their original identities, and retains evidence and failed results. Stop the treatment
for lost obligations, false completion, duplicate effects or unresolved lineage. A missing observer
alone is an economic-evidence gap under the existing advisory contract, not permission to discard an
otherwise valid native delivery or claim savings.

## Workspace impact and completion

This plan changes no fresh or retained workspace. The temporary repository-owned driver does not need
package publication merely to land its local source improvements. If reusable contracts later move into
Coordination or distributed skill tooling, that work separately owns source compatibility, publication,
pinned installation and selected receiver qualification. No installed consumer inherits acceptance from
this document or from a producer merge.

Design delivery means this proposal and its roadmap joins are merged. Source completion means the
selected .2–.4 implementation and tests are accepted. Operational adoption requires .5–.6's disposition;
measured context or cost benefit requires adequate observed evidence. Each claim keeps its own boundary.

## Ordinary operation/restart qualification and intake correction — 2026-10-19

The finite process Coordination V2 window under delivery4682 supplies the remaining scoped
functional .4–.6 joins. Its source/publication/installed/native evidence is retained in the
[owning V2 plan](../coordination/2026-09-29-coordination-v2-board-design.md#genuine019-selected-full-sdd-receivers-and-preservation--2026-10-19)
and [delivered adoption PR4698](https://github.com/FS-GG/.github/pull/4698). This is an actual
necessary ordinary delivery window, not a new benchmark or replay of lost work.

For .4, the same accountable owner carried source repairs and superseded native heads through
independent review and final checks. Original publisher failures retained their journals; admitted
forward observation/settlement completed the same release without package resend. The actual4698
bounded watcher ended at its deadline before checks settled; fresh canonical native reconciliation
subsequently admitted delivery. The watcher supplied no apply authority and its incomplete record
remains retained. Existing source correspondence controls supplement these native observations.
The actual protected4/installed107 rollback, separate original5 restoration and fresh108 process
using independently restored Host custody establish the bounded restart/rollback join. Original
identities and all439 inherited plus46later Unknown holds remain intact and unreleased.

For .5, genuine108 and019 publication, isolated physical installations, selected full public
Rendering and newly retained Game(Rendering game-profile) receivers, actual instruction discovery,
preserving retrofit and positive/refusal native reads supply functional quality and lineage joins.
The restored108 process read22Current/25memberships in185.93seconds with zero mutations; the
original270-second refusal remains failed. Public receiver operations completed in approximately
134 and109seconds; these observations are durations, not coordination-effort or token estimates.
Native usage, comparable whole-family cost, parent-churn baseline and statistical savings remain
unavailable or inconclusive. No80% or30% benefit, complete LEARN result or new measurement gate follows.

For .6, the qualified selected callers and actual discovered canonical work-board instructions,
retained old-contract compatibility and executed rollback route support bounded adoption. The
ROUTINEV3 replacements above retire mandatory packets, typed returns, reducer projections and fixed
polling; optional historical helpers need no new live reenactment. Earlier paragraphs describing
pending integrated acceptance retain their historical scope and are superseded for this finite window.
Actual client replacement, disk-loss/off-machine recovery, fleet-wide legacy upgrades and release of
unavailable original reservations remain outside this acceptance. One independent acceptance review
confirmed this mapping without repeating unchanged operations or tests. Native parent completion
still requires its accountable owner's separate reconciliation; child closure never performs it.

The continuation initially omitted intake triage for visible unenrolled cards lacking Status. The
user exposed that omission; it is retained as a failed clearing step, not an absence of underlying
work or a need for another coordinator. Fresh complete Project3 readback found25members and three
such cards. Existing accepted SOURCE deliveries4677 and4681 were closed/completed; SDD935 remained
open with missing accepted producer-contract evidence from SDD928. Programme/root recorded935's
intake ownership, exact supplier and reopening event on its existing issue, preserving its complete
original acceptance. A separately prepared actual108 invocation initialized exactly12 absent
planning/classification values on those three existing cards: two Done/Active delivery and one
Blocked/Follow-up, with genuine owning links and explicit Delivery classification. Separate original-
custody readback and full-snapshot comparison preserve all17fields/eightviews/25memberships and every
unrelated planning/native value; only selected additions and permitted timestamps differ. No membership,
protected enrollment, view, Observation or implementation dispatch changed. They remain unenrolled;
935 requires producer evidence, an implementation owner and separately admitted enrollment before
selection. Blocked intake is reconsidered on its determining event or the next clearing pass.

Canonical work-board now explicitly includes missing-Status/classification/enrollment cards in
unfiltered intake and records their disposition in existing owning issues. This repairs the omission
without adding an intake queue, Ready fallback or retrospective cards. Source guidance delivery and
any later installed consumer update remain distinct; no fleet adoption is inferred from this edit.

## Selected finite F# observer source — 2026-10-20

[Delivery4731](https://github.com/FS-GG/.github/issues/4731), under
[Outcome4730](https://github.com/FS-GG/.github/issues/4730), adds a finite attention invocation to
`FS.GG.Coord.Core`, `FS.GG.Coord.GitHub` and the existing engine CLI. The pure reducer preserves exact
issue/body revision, source, owner, slice/original attempt, PR/head, run/attempt and population identity.
The read-only adapter checks the complete bounded selected-run jobs between owning-issue, run and PR
barriers. `ObserveOriginal`, `AwaitWithinDeadline` and `ReturnAttention` keep unchanged pending ticks
inside deterministic code. The [canonical observation instructions](../../.agents/skills/work-board/references/observation.md#finite-original-operation-attention)
define the closed input, private original-clock continuation, output and rollback semantics.

This source candidate is not publication, installed adoption or full Delivery acceptance. Relevant
F# tests, one independent source review, exact-head required CI, coherent publication, physically
verified installed caller/instruction discovery and one necessary ordinary exact-head CI observation
remain required. That real window retains positive, changed-head/failure, interruption/restart/deadline
and rollback evidence; no unrelated workload is replayed for acceptance. All439 inherited and46later
Unknown original effects and reservations remain intact and unreleased.

The selected native population is one Actions run/attempt, not all required checks or a whole-board
read. A normal sample costs eight GETs (ten above100jobs), bounded by384requests and600seconds
including cleanup. The existing deadline transport may transiently read4MiB; the observer accepts at
most256KiB per page before parsing. This explicitly differs from the Python watcher's acquisition cap.
Linux boot/tick continuation, local cursor checksum/locking and flushed replacement do not establish
cross-host/disk-loss recovery, process containment or exactly-once result delivery. Actual native
coverage, whole-family usage and economics remain observed/Unknown according to available evidence;
no saving percentage is inferred from a language port or from unchanged-tick suppression.
