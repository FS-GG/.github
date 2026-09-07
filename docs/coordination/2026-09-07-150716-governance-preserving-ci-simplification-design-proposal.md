---
title: "Design proposal: Preserve governance while simplifying CI"
category: Design
categoryindex: 4
description: "Separate delivery ceremony, synchronization, rule enforcement and optional scheduling so routine CI can become smaller without discarding shared-state safety or existing Governance functionality."
---

# Design proposal: Preserve governance while simplifying CI

Authored: **2026-09-07 15:07:16 UTC**. Status: **proposal for review; no policy, runtime, required-check or migration-contract changes**.

## 1. Recommended decision

Simplify the developer experience while preserving the functions that make concurrent delivery safe.
One owner and one PR can remain the ordinary experience even when inexpensive automatic coordination,
rule evaluation and recovery records operate underneath it. Removing an agent-authored receipt cycle does
not require removing a Git journal. Reducing the number of CI jobs does not determine the number of
obligations that still apply.

The proposed architecture separates four responsibilities:

1. **Policy owners and Governance determine applicable obligations and evidence.** Retain existing
   normative ownership, routing, enforcement profiles, evidence freshness and explanations.
2. **Coordination and Git enforce shared-state transitions.** Preserve expected-parent journal updates,
   fencing, current authority, effect settlement and operating-epoch restrictions.
3. **An execution owner runs a bounded delivery attempt.** Use the supported CLI/workflow first. An OR
   scheduler or durable controller is an optional implementation investment, not an alternative authority.
4. **Observers derive reporting.** Board views and economics can lag without invalidating delivery facts;
   missing evidence that actually authorizes an effect cannot be treated as a reporting delay.

The main correction to the simplification design is to make **delivery profile and synchronization need
independent decisions**. A routine change can still require a shared claim or resource grant. Conversely,
an isolated change does not need a new global claim merely to manufacture an audit trail. Keep existing
shared grants binding, automate necessary coordination, and allow a lock exemption only for a bounded
operation class with established isolation. This is a proposed refinement of the adopted pilot, not a
claim that its current classifier already proves isolation.

## 2. Evidence and current implementation boundary

This is a repository/source analysis, not external research, a performance experiment or an end-to-end
production certification. It distinguishes implemented components, accepted pilot policy and proposed
future integration.

The shared checkout was at `.github` `6315cf8f980dd49c31b0bed64309676d28ee51b0`. A fetch during this analysis
found `origin/main` at `5da8bff9654b61fdfcd6e3538439ad9605af3201`. The two intervening commits matter:

- `b09987d30f8a0602b647d248fb1cc9dcfc537b02` adopted the routine pilot and amended governing sources.
- `5da8bff9654b61fdfcd6e3538439ad9605af3201` added native one-PR delivery and readback.

Accordingly, earlier statements that all routine-policy changes remain unadopted are now historical.
The OR/PB proposal alignment and v2 appendix still provide design context, but implementation status must
come from the applicable versioned policy and producer. No blanket fleet-wide or ordinary live-v2
activation is inferred from these two commits.

| Inspected evidence | What it establishes | What it does not establish |
|---|---|---|
| [Adopted pilot policy](https://github.com/FS-GG/.github/blob/5da8bff9654b61fdfcd6e3538439ad9605af3201/.fsgg/routine-development.json) | Routine source/docs operations, protected operations/paths, no mandatory claim or artifact family, trusted-repository-writer model | Dynamic resource isolation, semantic independence between PRs, or adversarial resistance to repository writers |
| [Eligibility workflow](https://github.com/FS-GG/.github/blob/5da8bff9654b61fdfcd6e3538439ad9605af3201/.github/workflows/routine-eligibility.yml) and [validator](https://github.com/FS-GG/.github/blob/5da8bff9654b61fdfcd6e3538439ad9605af3201/scripts/check-claim-generation.py) | Base-sourced evaluator, exact observed base/head, operation/path checks and head-bound declaration; candidate bytes are not executed by that workflow | A shared-resource grant; a branch prefix and declaration are not a runtime conflict census |
| [Delivery helper](https://github.com/FS-GG/.github/blob/5da8bff9654b61fdfcd6e3538439ad9605af3201/tools/routine-delivery.py) | Expected source head, native merge/readback, bounded ambiguous-merge handling and separate publication status | Independent Governance evaluation, journal acquisition, publication settlement or a generic exactly-once effect executor |
| [Pilot trust record](https://github.com/FS-GG/.github/blob/5da8bff9654b61fdfcd6e3538439ad9605af3201/.fsgg/routine-eligibility-activation.json) | Explicit acceptance of a trusted-writer reliability boundary and recorded limitations of stronger check authority | A newly verified live administrator configuration or authorization to extend that trust to protected operations |
| [V2 authority amendment](2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md) and [roadmap](../github-substrate-v2-roadmap.md) | Accepted journal/fencing/snapshot design and recorded qualification; proposed CI integration in §12 | Current production activation of every v2 journey |
| [OR design](2026-08-31-operations-research-first-agent-orchestration-design.md), especially §§1.2, 3, 7.8 and 13 | Intended authority split, plan verification, policy integrity and resource governance | A deployed optional controller |
| [PB design](2026-09-06-performance-bounded-development-flow-design-and-roadmap.md), especially §§1.2, 4, 8 and 9 | Separate configuration layers, bounded execution, recovery and evidence requirements | Hard aggregate budgets supplied by ordinary CI alone |

The local FS.GG.Governance checkout was clean at `3f265769d2b23ce36cbe0d05985e3c715dfead4f`.
Inspection of its [routing core](https://github.com/FS-GG/FS.GG.Governance/blob/3f265769d2b23ce36cbe0d05985e3c715dfead4f/src/FS.GG.Governance.Kernel/Route.fs),
[enforcement core](https://github.com/FS-GG/FS.GG.Governance/blob/3f265769d2b23ce36cbe0d05985e3c715dfead4f/src/FS.GG.Governance.Enforcement/Enforcement.fs),
[freshness inputs](https://github.com/FS-GG/FS.GG.Governance/blob/3f265769d2b23ce36cbe0d05985e3c715dfead4f/src/FS.GG.Governance.FreshnessKey/Model.fs)
and [reuse decision](https://github.com/FS-GG/FS.GG.Governance/blob/3f265769d2b23ce36cbe0d05985e3c715dfead4f/src/FS.GG.Governance.EvidenceReuse/EvidenceReuse.fs)
confirms implemented pure decisions for routing, effective severity and exact-key evidence reuse. This
does not prove that every routine receiver invokes them, that every installed version matches, or that
all proposed Typed SDD integration exists. That wiring needs an explicit consumer qualification.

## 3. Findings: where simplification can lose functionality

**Optional hosting is not optional governance.** The OR service combines scheduling, admission limits,
durable execution and observability. Deferring Akka, a solver or a dashboard is reasonable; deferring the
only implementation of a necessary concurrency limit or recovery owner is a functional omission. Each
enabled operation needs a named enforcing component even when no harness is installed.

**One owner does not establish resource isolation.** Separate worktrees prevent simultaneous writes to one
checkout. They do not exclude overlapping published packages, shared test environments, settings, issue
claims or the same dependency contract. Native merge arbitration also does not prove two individually
green changes compose correctly. The pilot's static protected-path and operation checks provide a useful
boundary, but the inspected routine evaluator deliberately reads no board/claim state.

**Governance is already more than CI job selection.** The
[consumer guidance](../consumer/governance.md) and
[Typed SDD Governance integration design](2026-08-24-174459-typed-sdd-governance-integration-design.md)
describe fact sensing, applicable rules, enforcement posture, competency, provenance, inherited floors,
ship/release boundaries and reusable evidence. Removing a workflow must not accidentally remove the only
consumer of one of these functions. The successor constitutional integration remains deferred; it is not
a prerequisite for reusing already-published Governance capabilities.

**Classification vocabularies currently differ.** Governance's inspected routing core makes a change
routine when no declared fence matches. The CI-alignment proposal handles unknown/mixed protected scope
conservatively. These can coexist only if a complete negative match is distinguished from an incomplete
observation. A missing path page, unsupported adapter or unreadable policy must not be normalized into
“no fence matched.” A wrapper should supply complete facts or refuse the affected decision; the pure
Boolean routing core cannot infer facts it was not given.

**Evidence reuse and ambient-base churn are not already solved by one PR.** Governance's inspected
freshness key includes both base and head revisions. Reuse requires a full match. It would be inaccurate
to promise that an unrelated base advance reuses that evidence automatically. Preserve existing semantics
first; a narrower semantic subject requires a versioned key/contract change and negative controls.

**A green check is not a perpetual capability.** A CI result can establish a historical test or eligibility
fact. A grant may subsequently be revoked, a review epoch may change, or an operating epoch may advance.
Protected effects still require the accepted current-authority mechanism. A read immediately before a
write is not, by itself, atomic fencing of a remote provider action.

## 4. Proposed architecture and ownership

| Responsibility | Owner and enforcement location | Simplification opportunity |
|---|---|---|
| Normative obligations, exceptions and amendments | Existing organization/product policy owners; SDD retains lifecycle constitutional ownership and applicable Quint authority | One adopted source; generated guidance references it instead of restating competing rules |
| Rule applicability, evidence, severity, competency and explanations | FS.GG.Governance where adopted; existing supported evaluator otherwise | Invoke deterministic evaluation at the relevant boundary; reuse supported evidence; no new universal service |
| Claims, conflict domains, operation grants and current epoch | FS.GG.Coordination typed engine and protected Git journals | Automatic transitions through the accepted writer; remove agent-mediated election and receipt choreography |
| Native source merge | Current supported delivery owner and provider enforcement under the adopted profile | One PR, expected head, required technical predicates and native outcome readback |
| Protected external effects | Authorized adapter/reconciler with operation-specific fencing, permissions and recovery | Reuse the same execution semantics from CLI and any later service |
| Scheduling, work shaping and capacity optimization | Simple bounded executor first; optional OR kernel when justified | Add optimization only for a measured bottleneck; never let utility override legality |
| Durable unattended progress | Existing recovery path or separately qualified PB/OR executor for its enabled classes | Persist necessary intent and settlement state, without making a second journal the external authority |
| Reporting and economics | Observers reading authoritative facts | Batch, coalesce refresh hints, report gaps and repair asynchronously |

Governance answers whether the applicable evidence satisfies a rule at this boundary. Coordination answers
whether the current subject, grant and epoch permit the transition. Both can be necessary; neither result
substitutes for the other. The orchestration database records local intent and attempts, while protected
Git history and provider observations retain external authority.

No new governance registry is proposed. Record obligation ownership and disposition in existing policy and
design sources. Extend an existing versioned handoff only where its current fields cannot express the
accepted decision. Avoid copying Governance predicates into prompts, workflow conditions and Coordination
implementations as three independently maintained authorities.

### 4.1 Independent decisions for each supported operation

| Decision | Possible outcomes | Why it is separate |
|---|---|---|
| Delivery/evidence profile | Routine, protected, or not yet classifiable | Determines applicable tests, review and release obligations |
| Synchronization | Isolated, existing shared grant required, or unresolved conflict scope | Determines exclusion and stale-writer protection independently of paperwork |
| Operating authority | Permitted in the current epoch, deferred, or refused | Prevents a cheap route from reopening a retired writer |
| Execution capacity | Admit, defer, or refuse under the stated limits | Stops optional optimization from becoming the only capacity control |

These are conceptual decisions, not a proposed new schema or four per-item documents. An automatic
explanation can cite the policy identity, subject, applicable predicates and disposition in the existing
check/operation output. A PR author should not have to write these results by hand.

Examples make the separation concrete:

- An internal prose change with no shared mutable effects may use the routine profile and native merge.
- Two routine changes needing the same exclusive test environment remain routine, but the environment
  grant is required before either uses it. Its absence defers that action, not every unrelated PR.
- Editing shared protocol code or governance policy takes the applicable protected change route even if
  the editor has a private worktree.
- A source PR may be delivered while required publication remains pending. Publication acquires its own
  current authority and fulfills the installed release contract.
- Work already admitted under a strict shared claim cannot escape that claim by renaming its branch.

### 4.2 Preserve synchronization without preserving its ceremony

For supported shared operations, retain the accepted expected-parent update, monotonic generation,
ancestry/integrity checks, conflict-domain acquisition and compensation rules. Lease expiry permits
recovery; it does not independently authorize the old or replacement worker. Multiple required grants
must be settled under the accepted acquisition protocol before the effect is permitted.

Scope synchronization to the actual conflict domain. Avoid a global work lock merely because records
share a repository. Conversely, per-PR locks cannot protect two PRs that mutate the same package or
environment. Shared-control transitions remain journaled even when no issue, phase ledger or receipt PR
is required for the source change.

At each protected provider boundary, retain the demonstrated ordering between grant revocation and the
external effect. A journal CAS linearizes the journal update, not an arbitrary API write that follows it.
If the provider cannot validate a fencing token, the implementation must use the already-qualified
serialized/capability boundary and its declared in-flight/revocation semantics, or leave that operation
unsupported. This proposal supplies no new exactly-once claim.

Recommended refinement of the pilot's unconditional absence of routine claims: make absence of a new
claim conditional on established isolation for the supported operation class. Preserve automatic shared
coordination where required. This needs an explicit policy/producer amendment and receiver validation;
the proposal itself neither revokes the pilot nor authorizes a new runtime grant path.

## 5. Governance with fewer CI jobs

### 5.1 Select obligations before selecting jobs

First determine the retained technical, authority and release predicates. Then decide how to execute them.
One aggregate job may report many named predicate results; consolidation alone removes no obligations.
Where Governance is adopted, preserve its configured floors and protected-boundary enforcement. Where
it is absent, the independently supported route must still enforce its declared obligations without
silently installing the complete Governance/Typed SDD successor.

The merge result should be derived from the adopted blocking set, with individual results explainable.
A missing required predicate, failed evaluator or incomplete routing observation cannot become success.
Not-applicable requires a supported classification, not an absent job. Advisory findings remain visible
without accidentally becoming blockers because a client indiscriminately scores every check.

Policy and evaluator identity must come from the trusted adopted source. Candidate edits to policy,
workflows, adapters or gate-selection inputs require their applicable protected route. The current pilot
explicitly trusts repository writers and does not claim adversarial check provenance. Preserve that honest
scope; stronger adversarial enforcement is a separate decision with separately available credentials and
provider support, not a security property created by this document.

### 5.2 Reuse evidence without reusing expired authority

Reuse a deterministic test result only under its accepted freshness contract. Bind the selected evidence
to the current decision while retaining its original provenance. Changing the source, rule, toolchain,
environment or relevant policy may require recomputation. Current full-base keys remain full-base keys
until their owners qualify a narrower identity.

An unrelated base advance should not require an agent to recreate unchanged intent or repeat a receipt
PR. It may still require automatic recomputation under the installed key or merge policy. An optimization
that avoids this work must state which dependencies and integration guarantees it retains. Keep full
snapshot qualification for protected reviews, settings, releases and migration where the accepted contract
requires it; do not replace it wholesale with source-head equality.

### 5.3 Put blocking decisions at their actual boundary

| Boundary | What may block | What should not independently block |
|---|---|---|
| Local exploration | Applicable sandbox/resource restrictions | A delayed board, optional dashboard or missing usage report |
| Source merge | Adopted eligibility, technical checks, required review and relevant authority | A full-fleet projection scan with no merge-authorizing predicate |
| Shared resource use or publication | Current grant, operating epoch, required evidence, resource identity and unresolved prior effects | A missing narrative summary of already-established facts |
| Policy/profile promotion | Required correctness, operational and measurement evidence | A desire to complete a controller roadmap with no demonstrated need |

Before removing a required context, inspect what it actually enforces. Split a mixed authority/projection
check only after the authority predicate has an enforced replacement and every supported merge path
honors it. Do not remove `reconcile` merely because of its name, or replace journal branch protections
while changing source-branch checks. Both use GitHub controls but protect different resources.

## 6. Reconciliation, outages and recovery

Distinguish authoritative state reconciliation from view rebuilding. Commands, events and audits may
enter the same typed transition machinery without requiring each routine PR to wait for a global board
scan. The incumbent board workflow currently serializes writes for a reason; shortening its queue must
not create a second unguarded writer.

Coalesce refresh hints for the same subject and read fresh state before planning. Preserve distinct
subjects, approval/grant changes, durable commands and non-idempotent operation identities. Never cancel
an applying effect merely because a more recent webhook arrived. Scheduled complete audits repair lost
hints. Measure detection and repair delay separately from source-delivery latency.

An optional scheduler or observer outage should leave the independently authorized routine route usable.
An unavailable required authority service is different: the affected protected action waits or refuses.
Fallback uses the same accepted protocol and current epoch. No timeout or cost ceiling revives a fenced
v1 writer, bypasses an existing grant or permits an indeterminate publication to be retried blindly.

An item may stop accepting new work while an already-issued effect still needs settlement. Preserve the
effect's identity, recovery owner and outcome observation until resolved. PB's distinction between item
termination and effect settlement remains necessary even when its proposed full controller is deferred.

## 7. Resource governance and policy improvement

Use existing limits for active workers, attempts, runner time, API calls and retained data where available.
Clearly distinguish enforced limits, provider estimates and after-the-fact observations. A hard aggregate
cap across concurrent workers needs atomic allocation or an equivalent qualified mechanism. If the
current executor cannot provide it, do not advertise that cap or admit work whose safety depends on it.
Token receipts arriving after spending cannot alone enforce a token ceiling.

The OR controller remains useful for competing work, resource allocation, dependency scheduling,
information selection and durable unattended execution. Its optimizer may choose among feasible actions;
it may not lower the policy floor, replace a grant or alter its own acceptance evidence. Start with a
deterministic baseline and add a solver only when measured outcomes justify its cost.

PB's bounded repair, recovery reserve and explicit terminal outcomes should inform the supported executor
without importing every proposed PB component. Budget exhaustion stops new discretionary work; it does
not erase pending effects or turn an unsafe action into a permitted one. Reserve recovery capacity for
the operations whose effects can outlive their initiating attempt.

Economic reporting remains asynchronous. Compare delivery fraction, escaped defects, repair cost, owner
attention, queue delay and total compute, not only tokens or PR count. Preserve the radical proposal's
10% model-overhead objective and 20% measured ceiling as its stated measure, separate from runner share,
money and wall time. Include automatic Governance/journal work and maintenance in their appropriate cost
measures rather than hiding it because no agent authored the record.

The existing R4/R5 comparison calls for at least ten candidate and ten comparable baseline code items,
95% usage coverage with bounded unknowns, and 30-day repair observations. That supports an initial
comparison, not p95/p99 or rare-race safety claims. Missing telemetry prevents an efficiency claim;
failed authority evidence prevents the action. Demonstrate concurrency safety with the applicable model,
negative controls and external-boundary qualification, not an average cost improvement.

## 8. Proposed changes to existing designs

| Source | Necessary amendment or implementation decision |
|---|---|
| Radical design and adopted routine policy | Separate delivery profile from synchronization need; scope any no-claim exemption; distinguish adopted R0/R1 from future R2–R5 and v2 carryover |
| OR §1.2 and §3 | State that optional hosting does not make required governance optional; explicitly place FS.GG.Governance adjudication alongside Coordination legality without duplicating predicates |
| OR §§7.4–7.5, 8 and 13 | Consume the adopted evidence profile; retain automatic shared grants and enforceable capacity controls; specify the non-harness owner of every required function |
| OR H0/H1 and H6/H7 | Compare against the actual supported routine route; qualify service loss and same-protocol fallback; promote by operation class rather than making the harness a universal writer |
| PB §1.2, FLOW-011/012 and §4 | Add the independent synchronization decision; map Governance evidence/freshness ownership; distinguish proposed semantic reuse from current base/head key behavior |
| PB §8 and PB2/PB3 | Retain finite attempts and effect settlement with or without the new controller; require coordinated reservations only where hard concurrent caps are promised |
| PB §9 and PB7/PB8 | Keep reporting independent of delivery; qualify correctness, budget enforcement and efficiency as distinct claims with appropriate evidence |
| Governance integration design and consumer guidance | Clarify complete negative routing versus unknown observation; preserve normative/operational ownership and existing optional adoption; avoid making the deferred constitutional successor a prerequisite |
| V2 roadmap §12 and future owning unit contracts | Add governance/routing/synchronization acceptance examples when adopted; preserve journal integrity, full applicable snapshots, comprehensive GS2 qualification and receiver pin rules |
| Current routine validator/helper and installed consumers | Evaluate whether existing inputs can establish the proposed operation scope; add only the missing boundary behavior after contract agreement, through the proper producer |

This table proposes changes; it does not edit those sources. In particular, published Governance reuse
semantics and executable GS2 unit contracts cannot be revised through prose alignment alone. Source and
operator guidance should describe the effective installed policy, while historical analysis retains its
dated evidence boundary. Avoid repeatedly rewriting accepted histories to resemble the latest profile.

## 9. Implementation sequence for a later accepted change

These are proposed increments, not new executable roadmap units, mandatory per-item artifacts or an
instruction to mutate any external repository now.

1. **Agree the boundary using existing policy sources.** Name supported operation classes, conflict domains,
   inherited obligations, trust posture and current writer. Resolve no-claim eligibility, unknown routing
   and current-v1 versus future-v2 behavior. Record deliberate guarantee reductions separately from
   automation/removal of duplicates. Owners: `.github` policy, Coordination runtime, Governance rules;
   SDD only where the lifecycle/constitutional handoff actually changes.
2. **Deliver one vertical integration through existing components.** Show a routine isolated change and
   a routine change needing a shared resource. Reuse Governance decisions where installed, retain the
   accepted journal boundary, and emit one compact automatic explanation. Start without a solver, new
   daemon, dashboard or universal artifact family. Keep unsupplied operation classes unsupported.
3. **Remove redundant waits and invocations.** Only after retained predicates are enforced, change relevant
   required checks, duplicate evaluators and projection triggers. Verify CLI/UI/automation paths supported
   by the policy. Preserve the current serialized write boundary until its replacement is qualified.
4. **Qualify installed behavior and economics.** Exercise clean and upgraded consumers, protected fallback,
   lost telemetry, delayed views, overlapping resources and stale authority. Publish changed contracts
   before adoption. Separate current-route pilot evidence from actual ordinary-v2 evidence.
5. **Consider OR/PB expansion for a remaining need.** Fund a bounded scheduling/durability slice only when
   the supported baseline cannot meet the named requirement. Keep safety verification independent of
   optimizer quality and retain an authorized recovery path.

V2 integration follows its current epoch and freeze rules. No production experiment before the relevant
opening authority is granted. If behavior or pins change after freeze, requalify under the existing rules
or defer to a later candidate. A future roadmap edit needs a reviewed full-file pin refresh for adopting
consumers; this standalone proposal changes no roadmap bytes or receiver catalog.

## 10. Acceptance examples and rejection criteria

| Scenario | Required observable result |
|---|---|
| Two isolated routine PRs | Independent progress without a global claim or full board-scan dependency; each satisfies its actual technical profile |
| Two routine workers request one exclusive environment | At most one usable grant; the other defers; no need to convert both source changes into full SDD workflows |
| Stale worker resumes after grant replacement | Protected effect is rejected under the qualified boundary, even if its old CI remains green |
| Partial multi-resource acquisition or process crash | No prematurely usable grant; compensation/recovery follows the accepted protocol |
| Existing strict work is relabeled routine | The existing authority and applicable obligations remain binding |
| Candidate edits its own eligibility policy or check selection | Evaluation uses adopted authority and requires the applicable protected change route; the trusted-writer pilot's adversarial exclusions remain explicit |
| Complete negative fence match versus incomplete diff/policy observation | The supported isolated class may proceed; the incomplete observation cannot masquerade as that class |
| Routine and protected changes mixed in one PR | Apply the protected obligations or split coherently; do not discard protected deltas to improve routine metrics |
| Base or policy changes after an earlier check | Recompute/rebind according to the installed freshness and authority contract; no stale evidence silently passes |
| Required aggregate predicate absent, advisory check red | Missing required evidence refuses; an advisory finding does not accidentally become a blocker |
| Same-subject event burst followed by unrelated PR | Redundant hints can collapse; unrelated work is not lost; authority writes remain safe and queue cost is measured |
| Journal operation races with observer refresh | Views follow authoritative facts and cannot revoke a valid grant or authorize a stale one |
| Observer or usage collector unavailable | Valid delivery can finish; freshness/measurement gaps remain visible and prevent unsupported efficiency claims |
| OR scheduler unavailable | Independently authorized work can use its supported route; required authority outages still block their own effects |
| Publication response lost or success arrives after item timeout | Durable operation identity and recovery owner survive; no blind retry, false completion or fenced-v1 fallback |
| Concurrent workers exhaust a claimed hard resource cap | Admission respects actual coordinated capacity; observational accounting is never reported as hard enforcement |
| Governance evidence has the wrong key or changed base | Existing reuse semantics reject it; narrower reuse requires its own qualified contract |
| Clean versus upgraded consumer | Same adopted profile and obligations; no stale generated guidance recreates receipt cycles or removes shared coordination |

Run relevant existing model and implementation checks when those components change. Add targeted negative
controls at the changed boundaries; do not require the entire table to be repeated for every later code
PR. Comprehensive GS2 parent, freeze, release and cutover qualification retain their governing strength.

Reject an implementation that saves time by dropping shared-state exclusion, inventing native exactly-once
semantics, treating missing authority as telemetry loss, trusting the candidate to reduce its own policy,
or moving an equivalent burden into uncounted CI and maintenance. Also reject a design that calls the
harness optional while leaving it as the only owner of an essential operation with no supported fallback.

## 11. Decision requests for design adoption

The recommended defaults are: retain automatic shared coordination; keep isolated source delivery small;
reuse existing Governance rather than duplicate it; preserve its current freshness contract initially;
and defer a new controller until a measured need exists. Later adoption must settle these concrete choices:

- Which operation classes have enough isolation evidence to omit a new claim, and which existing shared
  grants remain mandatory even for routine work?
- Which receivers already invoke the required Governance rules, and what is the smallest supported
  handoff for those that do not? Which inherited rules are intentionally advisory versus blocking?
- Which budget guarantees are currently enforceable, and which require allocation or remain observational?
- Which operation classes stay within the accepted trusted-writer model, and which require stronger
  authority/provenance before activation?
- Which exact authority predicates must replace mixed CI/projection checks before those waits can be removed?

These are policy and implementation decisions for a concrete adoption change, not reasons to attach five
questions or a new governance packet to every ordinary PR. This proposal preserves the purpose of v2's
concurrency repair while directing simplification at repeated ceremony, redundant checks and unrelated waits.
