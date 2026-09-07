---
title: "Analysis: Integrate radical CI simplification into the v2 migration roadmap"
category: Design
categoryindex: 4
description: "A proposed integration of routine CI simplification, event coalescing, receiver carryover and measured outcomes, with accepted migration contracts and roadmap pins preserved."
---

# Analysis: Integrate radical CI simplification into the v2 migration roadmap

Authored: **2026-09-07 13:14:45 UTC**. Status: **analysis and prospective amendment**.

The accompanying [roadmap section 12](../github-substrate-v2-roadmap.md#12-prospective-amendment-radical-ci-simplification-and-ordinary-v2-carryover)
incorporates the concrete proposed integration. Existing executable GS2 headings, unit bodies, accepted
histories, prerequisites and exit gates remain unchanged. This is neither adoption of a relaxed policy nor
an assertion that the ordinary v2 path is available. It prepares the decisions and acceptance examples that
must be reconciled with governing policy, producer contracts and receiver pins before execution changes.

## 1. Findings and evidence boundary

The current roadmap already addresses workflow consolidation, sound impact selection, stable aggregate
checks, negative controls, event repair, queue pilots and operational cost. It does not yet establish that
ordinary post-cutover development inherits the reduced process proposed in the
[radical development design](../2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md).
Simply renaming a gate or linking that design would leave this behavioral gap unresolved.

This assessment used `.github` source at `2285a55043562ceffee13f6d854d25e81e936abd`, the local Coordination
checkout at `a87dcf18e7065d5985141f15b3ff10f23d006328`, the accepted ADRs and the aligned
[development-flow analysis](2026-09-07-112837-development-flow-proposal-alignment-analysis.md). These are
bounded source/document observations, not a complete inventory of external consumers. No v2 production
journey or new performance experiment was executed.

The inspected Coordination CLI exposes roadmap work, qualification manifests and workflow selection, and
reports that production commands are not enabled. Its review/delivery and lifecycle adapters retain
accepted-review and journal authority. Therefore a cheap automatic record and an expensive agent-authored
receipt cycle cannot be treated as equivalent, and source-adapter tests cannot establish the cost of a
complete ordinary live-v2 journey.

A recent incumbent example illustrates the integration problem without proving v2 has it:
[PR #3314's required reconciliation run](https://github.com/FS-GG/.github/actions/runs/34117339578)
was created at 11:34:47 UTC; its job started at 12:06:20 and completed successfully at 12:08:28. That is
31m33s from run creation to job start and 2m08s of job elapsed time. During that wait, repeated runs for
another PR were observed ahead of it in the shared queue. These timestamps are one observation, not a
latency percentile or a token-cost measurement. Creation-to-start includes scheduling time; it is not an
instrumented decomposition of all waiting causes.

The [incumbent workflow](../../.github/workflows/coord-board-reconcile.yml) has a repository-wide FIFO
concurrency group, no in-progress cancellation, and a board-writing reconcile operation. Serializing those
writes is intentional. The improvement opportunity is to avoid unnecessary trigger work and routine
merge dependence on derived board status, not to permit concurrent unguarded writers. No workflow is
changed by this analysis.

## 2. Simplification has four distinct mechanisms

| Mechanism | Meaning | Appropriate treatment |
|---|---|---|
| Obligation removal | A duplicate report, phase or diagnostic condition no longer participates in routine delivery | Amend the actual obligation/driver policy, not merely the job selector |
| Execution optimization | The same obligation is fulfilled with cached evidence, narrower subjects or cheaper deterministic work | Retain sound identity/reuse and current candidate bindings; measure cost |
| Asynchronous projection | A board, roadmap or usage view follows authoritative delivery facts | Separate observer health from delivery authority; retain repair and visible staleness |
| Guarantee reduction | A less complete test selector or weaker review/snapshot policy deliberately accepts additional risk | Explicit owner decision and changed contract; do not describe it as equivalent optimization |

GS2-06.7 principally implements the second mechanism, with sound transitive obligation selection and a
full-suite sentinel. The radical design proposes all four. First remove proven expensive duplication and
retain cheap automatic safeguards. Only then consider a simpler selector if the remaining measured cost
justifies accepting its reduced coverage guarantee. This order avoids weakening an already-efficient v2
mechanism to solve a migration-driver problem.

The accepted GS2-06.7 history remains evidence of its original qualification. A changed obligation graph,
selection guarantee, required aggregate set or sentinel fallback is a prospective contract change. A path
filter cannot silently replace sound semantic selection, and an absent required context cannot become a
successful NotApplicable result without the appropriate policy and provider behavior being implemented.

## 3. Recommended changes and preserved guarantees

| Surface | Necessary improvement | Boundary to preserve |
|---|---|---|
| §1 execution and GS2-05.6 | Separate GS2 migration acceptance from ordinary source delivery; map actual driver ceremony versus automatic runtime records | Native merge is not sufficient migration qualification; journal/approval semantics require explicit amendment |
| GS2-06.7 | Make obligation dispositions, selection guarantees, aggregate identities and receiver behavior part of the future profile decision | Accepted soundness and fleet-wide sentinel refusal remain until a replacement is accepted |
| GS2-07.6/07.7 | Add event-burst, coalescing, independent-subject and unrelated-PR latency cases | No cancelled indeterminate effect, lost command or new unsafe write-concurrency lane |
| GS2-10.1/10.5 | Freeze the enabled profile, real required contexts, driver/tool pins and effective receiver guidance | Full candidate identity and requalification rules remain; unsupported profiles cannot be marked ready |
| GS2-12.7/12.8 | Exercise ordinary-profile behavior and observer independence under closed-fleet isolation | No normal production writes before OpenV2; technical/authority gaps still refuse their actions |
| GS2-13.3 | Demonstrate the actual ordinary v2 route in addition to comprehensive protocol coverage | No migration wrapper as substitute, no removal of required protocol cases |
| GS2-14.1/14.2 | Measure whole-unit overhead and delivery/repair outcomes with coverage | No inference from old migration costs, no hidden denominator changes or token/runner conversions |
| GS2-14.5–14.10 | Retire adopted-profile ceremony from real callers and account for deferred defaults | Historical evidence and required v2 protocol machinery remain; fallback respects the current epoch |

Formal-input drift, comprehensive GS2 parent closure, freeze, release, rollback authority and OpenV2 keep
their accepted qualification strength under
[ADR-0080](../adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md) and
[ADR-0081](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md).
The native one-PR experience does not displace canonical Quint behavior, typed mutation plans,
expected-parent CAS, fencing generations or snapshot-bound authority from the
[remaining-migration architecture amendment](2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md).

[ADR-0079](../adr/0079-single-accountable-delivery-authority.md) already distinguishes a single accountable
owner from review-phase evidence. A mandatory second person is not the same issue as mandatory phase
choreography. Similarly, removing usage from authorization does not turn an indeterminate publication into
an ordinary missing metric. Retain package identity, required-feed verification and actual release recovery.

## 4. Event and CI acceptance examples

The prospective appendix contains unit-level placements. The useful underlying examples are:

- **One-subject burst:** multiple PR edits arrive before observation. Pending hints can coalesce where
  the contract permits, while the eventual plan reflects the latest authoritative state. Count execution
  amplification, not merely successful webhooks.
- **Distinct subjects:** a second PR changes during the burst. It remains independently observable and
  schedulable; coalescing one subject must not lose another subject's obligation.
- **Unrelated delivery:** a routine PR whose required technical predicates are satisfied does not wait
  solely for an unrelated full board projection. Preserve the shared external mutation authority.
- **Dropped hint:** no event arrives. The complete audit repairs the discrepancy within the accepted
  bound, and outage/backlog behavior remains explicit rather than falsely current.
- **Semantic command or approval change:** an event carries meaning that cannot be reduced to an
  interchangeable refresh hint. Preserve journal/command identity and invalidate affected authority.
- **In-flight effect:** an older operation is already applying when a newer event arrives. Observe and
  settle the effect; do not treat cancellation as proof that no mutation occurred.
- **Wrong required context:** an omitted, renamed, stale or policy-mismatched check cannot authorize merge.
  A deliberately unselected child has the accepted typed disposition through the proper aggregate.
- **Observer outage:** missing usage or delayed projections preserve valid source-delivery facts while
  preventing unsupported efficiency claims; missing technical evidence remains a refusal.

These are proposed qualification cases for actual implementations, not additional per-item reports.
Workload size, coalescing policy, queue bound, audit cadence and API/runner budget need explicit candidate
values backed by observation. A single favorable run does not establish a fleet percentile.

## 5. Carryover and completion are different claims

There are three independently reportable results:

1. The incumbent supported route has reduced ceremony and measured delivery outcomes (R4).
2. Actual ordinary v2 entry points and installed receivers preserve the approved behavior (R5 functional
   carryover), including clean installs, upgrades and protected-operation routing.
3. A sufficiently observed v2 cohort establishes the requested efficiency result (R5 economics).

A future profile claimed by the frozen candidate must work under candidate qualification. This is a
functional compatibility obligation. The numerical overhead ceiling is a different possible migration
condition. Recommended treatment: insufficient efficiency evidence prevents a simplification-success
claim, but does not by itself create a new OpenV2 or OperatingV2 veto. Existing Q10 gates remain binding.
Any additional numerical migration gate requires a separate accepted amendment with the cohort, coverage,
stop rules and responsible owner specified before the candidate is frozen.

This avoids both extremes: opening a candidate that falsely claims a working profile, and holding a safe
cutover indefinitely because a small cohort cannot support a performance claim. An actual protected-route
or safety failure is never explained away as an overhead-budget issue.

The initial radical comparison uses at least ten candidate and ten comparable baseline code items, 95%
independently assessed usage coverage and a conservative overhead bound within 20%, with 10% the objective.
Include failed/cancelled attempts, shared flow-tool maintenance, review, delivery, reporting and subsequent
repairs. Report O/(P+O), absolute overhead, total cost per delivered unit and delivered fraction. Quantified
unknown usage U is conservatively included as (O+U)/(P+O+U); unquantified gaps mean insufficient measurement.
Ten items are not a p95/p99 or rare-defect claim. Runtime token caps are observational unless enforceable.

Q10's 0/7/14/30-day readings can carry these observations without creating another service or phase-ledger
family. They do not necessarily cover a profile or lifecycle default adopted only after OperatingV2.
That later population needs its own applicable receiver qualification and attributed-repair follow-up.
No-work denominators are undefined, not zero; expensive protected operations remain visible in programme
cost even when ordinary eligibility is narrower.

## 6. Ownership, sequencing and active-candidate handling

The existing ownership split is sufficient: `.github` owns common policy/current supported delivery;
Coordination owns v2 runtime and lifecycle semantics; SDD owns its producer/default/materializer boundary;
receiver owners verify what their installed entry points actually do. No OR controller, new registry,
federation programme or dashboard is required to integrate the simpler route.

Before GS2-10, a candidate-affecting simplification can be implemented, published, observed and incorporated
into receiver/candidate inputs through existing processes. At or after freeze it must be deferred or create
a new candidate with the required full requalification. The workspace-default programme's separately
accepted OperatingV2 condition is not changed by calling its update a routine profile. Do not infer that
all consumers have adopted the profile because a shared driver or one generated template has changed.

The roadmap's invariant restricting v1-engine changes to bridge, security and retirement also matters.
A shared-driver deletion may fit outside a v1 protocol change; a new engine transition may not. Classify
the concrete implementation rather than assume R0 permits extending any current CLI internals. Resolve a
governing-design conflict before use; preserve production authority while the new contract is prepared.

For already accepted or registered units, retain the original evidence and explicitly disposition the new
requirement. Do not insert unregistered executable rows, change a checked box, move the frontier or declare
a previous receipt to cover a new acceptance case. Section 12's table maps future adoption work to existing
surfaces without pretending that the corresponding versioned unit contracts are already amended.

## 7. Exact roadmap identity and landing scope

The local [preparation compiler](../../src/FS.GG.Coord.Core/RoadmapWorkUnit.fs) reads GS2 checkbox headings,
checks unit/title ordering against the catalog and hashes all roadmap bytes. Coordination's inspected
`docs/architecture/roadmap-work.md` documents the same exact-revision/digest boundary. At the inspected
snapshot, its registered index ends at GS2-07.6 and pins `.github` revision
`7e5754e23d274b31d21f9a2b4c0c0a00265ee366` with roadmap SHA-256
`33d303a888752d0b0f53e5443b2322bd601ebce43c86166ab6dc8d8387bd82ee`.
This is a snapshot observation, not a claim about every currently installed consumer.

Any prose edit changes the full-file digest. An existing consumer remains valid when it reads its exact
old pinned revision; pairing its old index with new-main bytes must refuse. A reviewed future pin refresh
can preserve unchanged unit contracts and their accepted evidence. A changed unit contract requires its
own compatibility/evidence disposition. No consumer pin or active execution is changed by this PR.

Accordingly, the canonical roadmap receives a clearly marked prospective section and one notice pointing
to it. All pre-existing text outside that notice/addendum is retained. The companion analysis records the
rationale; it is not a second execution roadmap. Targeted validation checks unchanged extracted headings,
unit order/frontier and existing contract text, local links, exact-digest refusal and the relevant preparation
compiler behavior. Passing those checks is documentation/parser compatibility evidence, not acceptance of
any proposed simplification.

This is ordinary documentation work for an execution-bound roadmap, using one focused PR. The
[min-docs skill](../../.agents/skills/min-docs/SKILL.md) excludes parsed documentation and changes to required
evidence; its proportionate delivery principles still inform the scope. No accepted ADR, workflow,
settings, registry, schema, unit catalog, release artifact or required check is changed. Adopting the
prospective unit amendments remains an explicit future contract/policy operation, not a documentation waiver.
