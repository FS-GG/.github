# Coordination churn: redesign proposal and stabilization roadmap

Created: 2026-08-14 09:05:08 CEST

Baseline: `main` at `cb33188c`

Evidence window: 2026-08-10 through 2026-08-14

Status: proposal

## Executive summary

The recent churn is not one unusually difficult feature being worked through. It is a set of recurring boundary failures: mutable prose is treated as machine state, lifecycle intent is inferred from observations, GitHub reads can be partial without being typed as partial, and a multi-package/multi-feed release is presented as one operation even though it is several irreversible operations.

The changes landed since 2026-08-13 are useful local repairs, but they do not yet remove those failure classes. In particular, route subsequence matching reduces false invalidation while still allowing semantic scope growth; special cases preserve some lifecycle states while leaving scheduling intent implicit; and release validation catches more errors while publishing still lacks a resumable transaction record.

The recommendation is not a big-bang rewrite. Freeze new process features temporarily, finish the bounded repairs already in flight, and replace four boundaries behind compatibility adapters:

1. make scheduling intent explicit and derive board status with a pure lifecycle reducer;
2. put every GitHub GraphQL read behind one typed, complete-read boundary;
3. drive releases from a content-addressed, resumable release manifest;
4. move routing and review decisions from prose-derived identity to structured, revisioned inputs.

Then reduce the evidence and CI multiplier by storing compact manifests in Git and moving bulky execution evidence to immutable CI artifacts. This sequence attacks the mechanisms that recreate work while limiting migration risk.

## What happened in the four-day window

The activity volume is high, but the more important signal is amplification: fixes repeatedly create follow-up coordination work.

| Signal | Point-in-time result |
| --- | ---: |
| Issues created | 131 |
| Issues closed | 103 |
| Net issue growth | 28 |
| Pull requests created | 146 |
| Pull requests merged | 135 |
| Pull requests still open | 9 |
| Pull requests closed without merge | 2 |
| Landed files changed | 511 |
| Landed additions / deletions | +139,474 / -2,096 |
| Additions in `work/` and `readiness/` | approximately 69% |
| Additions in `src/` | 8,633, approximately 6% |
| Coordination-engine versions tagged | 14 (`0.22.0` through `0.52.0`) |
| Current board rows | 108 |
| Current board states | 76 Done, 18 Ready, 12 Blocked, 2 In review |
| Check scripts / workflows | 49 / 100 |
| Core source / F# test lines | approximately 46,700 / 44,600 |

Two issue-level measurements reinforce the pattern:

- Issue #2584 records 48 rows filed in 30 hours by one actor while the board grew by 12 even though 25 items landed.
- Issue #2587 classifies 9 of 22 repair commits, 41%, as corrections to statements about the system rather than corrections to product behavior.

This is consistent with a system whose operational representation has become a second product. The repository is paying for both the underlying behavior and an expanding set of projections, evidence files, workflow variants, release rules, and reconciliation repairs.

## Current failure classes

### 1. Observed state overwrites scheduling intent

The lifecycle projection derives `Ready` as a default from observable conditions. A deliberate move to `Backlog` is therefore not durable unless the projection also has a special case that recognizes it. The same family of defect was previously addressed for `Blocked` and `In review`; issue #2586 shows it recurring for `Backlog`.

This is not best solved by adding another protected-status exception. The model is missing a first-class input: the human or policy scheduling intent that explains why an otherwise-ready item must not be scheduled.

### 2. Complete reads are a convention, not a type boundary

The coordination engine has useful helpers for extracting GraphQL data and determining whether a connection is complete, but their protection is local. Board, scan, and done paths still handle envelopes, errors, and pagination independently.

That makes partial data a legal intermediate value across too much of the codebase. Every new query or call site can reintroduce the same omission even after an earlier path was fixed.

### 3. Releases cross irreversible boundaries without a transaction record

Release workflows pack and push multiple artifacts to the organization feed and NuGet in separate steps. Version `0.52.0` demonstrated the consequence: one feed accepted artifacts before NuGet rejected an oversized `PackageReleaseNotes` value. The result is a partial release, feed incoherence, and ambiguous recovery advice.

No workflow can make two independent registries truly atomic. The missing abstraction is a durable saga: a manifest describing exactly which bytes belong to the release and which irreversible steps have completed.

### 4. Mutable prose doubles as identity and authorization

Routing and review evidence is bound partly to issue-body content. PR #2588 improves tolerance by accepting a previous route as a subsequence of the new body. That reduces invalidations caused by harmless insertions, but an insertion can also change scope or meaning while retaining the old subsequence.

The core problem remains: free-form prose is simultaneously human explanation, machine input, identity, and authorization. A more permissive comparison cannot reliably distinguish editorial change from semantic change.

### 5. Evidence and workflow multiplication increases repair surface

The repository currently contains 49 check scripts and 100 workflows, with most recent additions concentrated in `work/` and `readiness/`. Large amounts of checked-in evidence improve auditability, but they also create more mutable representations that must agree with each other.

When policy is copied into separate workflows, skills, and evidence formats, a single rule change becomes a distributed migration. Small local fixes then appear cheap while their cross-projection consequences arrive later as repair work.

## Are the changes since yesterday enough?

No, although they are worth landing.

The current repairs narrow immediate defects:

- PR #2589 addresses the NuGet release-notes size failure;
- PR #2588 makes body-only route edits less disruptive;
- lifecycle special cases preserve selected human-set states;
- newer reconciliation and validation checks expose several inconsistencies sooner.

They are insufficient as an architectural stopping point because each leaves the original invalid state representable:

- a future status still needs another preservation exception;
- a new GraphQL call site can still consume partial data incorrectly;
- a later package can still fail after an earlier feed push;
- a semantic body insertion can still retain a valid-looking route subsequence;
- another workflow can still duplicate policy with a slightly different interpretation.

The right use of these repairs is to stabilize the system while the replacement boundaries are introduced, not to continue extending the current pattern indefinitely.

## Target architecture

```text
 Human intent ──> structured intent/revisions ──┐
                                                v
 GitHub facts ──> complete-read adapter ──> pure lifecycle reducer ──> board projection
                       |                        |
                       |                        └──> compact decision/evidence manifest
                       v
                 typed failure

 Source SHA ──> pack once ──> release manifest ──> preflight ──> resumable feed pushes
                                 |                                  |
                                 └── hashes and step state <────────┘
```

The central rule is that observation, intent, derivation, and evidence must be separate values. Invalid intermediate states should either be unrepresentable or be surfaced as typed failures rather than silently repaired downstream.

## Proposed redesigns

### A. Explicit scheduling intent and a pure lifecycle reducer

Model three independent concepts:

```text
ObservedFacts      = repository and project facts read from GitHub
SchedulingIntent   = Auto | Backlog | HumanPark | Deferred(reason, until?)
DerivedStatus      = Backlog | Ready | InReview | Blocked | Done
```

`DerivedStatus` becomes a pure function of facts, intent, and policy version. Projects `Status` is an output projection, not an input with special-case preservation. Every non-automatic state has an attributable reason and revision.

Migration should begin in shadow mode. Re-run representative historical board snapshots through both reducers, classify differences, add explicit intent for deliberate divergence, then cut over only after reconciliation is idempotent and the new reducer produces no unexplained movement.

This removes the class of bugs in which a projection undoes a deliberate scheduling choice. It does not remove legitimate policy disagreements; it makes them explicit and reviewable.

### B. One typed GraphQL boundary

Create a single adapter that owns GraphQL envelopes, errors, pagination, retry classification, and rate-limit metadata. Consumers receive either a complete typed domain result or a typed failure. Raw `data`, `errors`, and `pageInfo` values must not escape the adapter.

Use a generic connection-draining primitive with explicit limits and tests for empty pages, repeated cursors, mixed data/errors, rate limiting, and page-boundary mutation. Migrate board, scan, done, and audit reads one by one behind compatibility functions; then forbid direct GraphQL envelope handling outside the adapter.

This removes partial reads as an ordinary success value and prevents each feature from inventing its own pagination semantics.

### C. Content-addressed, resumable release saga

Produce all packages once from a single source SHA. Before any push, inspect the exact generated packages and nuspecs against both registries' constraints. Record a release manifest containing:

- release identity, version, source SHA, and policy version;
- every package name, byte hash, and dependency relation;
- validation results for each target feed;
- per-feed push state and externally observed package hash;
- retry, recovery, and channel-promotion state.

One orchestrator advances the manifest through monotonic steps. Retries resume from observed state and reject bytes that differ from the manifest. The stable-channel pointer changes only when every required feed serves every expected artifact.

This cannot create true registry atomicity. It makes partial publication explicit, safe to resume, and impossible to mistake for a coherent release. If strict atomic consumption is required, publish a single bundle artifact or define coherence at a higher-level channel manifest instead of claiming transactionality across feeds.

### D. Structured routing and review decisions

Give machine-significant decisions their own revisioned fields or append-only ledger entries. A route decision should identify structured scope, dependencies, touch set, policy version, and route revision. A review decision should identify head SHA, critic identity, verdict, accepted exceptions, and decision revision.

The issue body remains the human narrative. Body edits do not silently preserve or invalidate authorization; only changes to the structured inputs create a new decision revision. During migration, dual-read the structured record and legacy evidence, emit differences, and write only the new form before removing legacy body hashing.

This eliminates the need to decide whether arbitrary prose edits are semantically harmless.

### E. Reduce the evidence multiplier

Keep compact, content-addressed evidence manifests in Git. Store TRX files, long logs, generated reports, and other bulky execution output as immutable CI artifacts referenced by hash and URL. Retain locally reproducible commands and the small decision record needed for audit.

Define policies and skills once in a neutral source, then generate runtime-specific projections. Consolidate workflow entry points around shared subject discovery and policy execution rather than adding one checker and one workflow per edge case.

The goal is not less evidence. It is fewer independently mutable descriptions of the same fact.

## Roadmap and milestones

Durations are sequencing estimates, not calendar commitments. Each milestone has an exit condition that prevents an incomplete migration from becoming another permanent compatibility layer.

- [x] **M0 — stabilize**
  - Target: 0–2 days
  - Deliverables: Land bounded release, feed-coherence, project-audit, engine-pin, and claim-auth repairs; triage lint; pause new process features; capture replay fixtures and baseline metrics
  - Exit criteria: Main has no standing red checks; open repair PRs are mergeable or explicitly superseded; baseline is reproducible

- [ ] **M1 — intent/status split**
  - Target: Days 3–7
  - Deliverables: Add `SchedulingIntent`; implement pure reducer; shadow old/new projections; migrate deliberate parks
  - Exit criteria: Reconciliation is idempotent; explicit Backlog and human parks survive; replay differences are explained; rollback is a projection switch

- [ ] **M2 — complete-read boundary**
  - Target: Week 1
  - Deliverables: Add typed GraphQL adapter and generic connection draining; migrate all production readers; add fault-injection tests
  - Exit criteria: No production call site handles raw GraphQL envelopes; incomplete reads cannot be returned as success

- [ ] **M3 — release saga**
  - Target: Week 2
  - Deliverables: Add release manifest; pack once; validate exact artifacts; add resumable orchestration and coherent-channel promotion
  - Exit criteria: One full coherent-set release reaches both feeds without manual recovery; forced mid-publish failure resumes safely with identical hashes

- [ ] **M4 — structured decisions**
  - Target: Weeks 2–3
  - Deliverables: Add route and review revision records; dual-read legacy evidence; migrate active items
  - Exit criteria: Body-only edits neither grant nor revoke machine authorization; every effective decision is bound to structured inputs and a revision

- [ ] **M5 — evidence and CI consolidation**
  - Target: Weeks 3–4
  - Deliverables: Introduce compact evidence manifests and artifact retention; generate skill projections; consolidate policy runners and subject discovery
  - Exit criteria: Material policy has one source; bulky evidence leaves Git; checker/workflow count and duplicated policy decline without coverage loss

- [ ] **M6 — retire compatibility paths**
  - Target: After 3 stable cycles
  - Deliverables: Remove old reducer, raw GraphQL helpers, legacy body hashes, and superseded release paths; document operations
  - Exit criteria: Three consecutive operating cycles meet the health measures below; no same-class successor issue remains open

### M0 stabilization evidence

- **Temporary feature freeze:** From the start of M0 until its exit criteria are
  satisfied, this repository accepts only bounded stabilization repairs and the
  integration needed to prove them. New process features resume with M1; M0 does
  not extend the current process machinery.
- **Baseline fixture:** The report header fixes the source baseline at
  `cb33188c`, the evidence window at 2026-08-10 through 2026-08-14, and the
  point-in-time issue, PR, board, release, workflow, source, test, and evidence
  measurements in the first table. The issue and PR values are reproduced by
  `gh api -X GET search/issues -f 'q=repo:FS-GG/.github is:<issue|pr> created:2026-08-09T22:00:00Z..2026-08-14T07:05:08Z <state>' -f per_page=1 --jq .total_count`,
  using `is:closed` for closed issues and `is:merged`, `is:open`, or
  `is:closed -is:merged` for the three PR dispositions. The Git-backed
  workflow/checker counts are
  reproducible with `git ls-tree -r --name-only cb33188c .github/workflows` and
  `git ls-tree -r --name-only cb33188c scripts | rg '^scripts/check-'`. GitHub
  measurements remain timestamped observations; later live reads are comparison
  samples, not rewrites of this baseline.
- **Replay fixtures:** The bounded-release failure is preserved at
  `tests/engine-release-notes/regression/release-notes-at-2579.xmlfragment` and
  exercised by `tests/engine-release-notes/run.sh`. Feed split-brain and
  ordering cases are exercised by `tests/feed-coherence/run.sh` and
  `tests/feed-coherence/feed_reader_cases.py`. Engine-pin partial-release cases
  are exercised by `tests/engine-pin/run.sh`. Claim-authorization consolidation
  cases use the production-derived corpus in
  `tests/FS.GG.Coord.Cli.Tests/consolidation-corpus/` and
  `ConsolidationTaxTests.fs`.
- **Lint triage:** Pinned ShellCheck 0.11.0 reports no repository finding through
  `tests/shell-lint/run.sh` and `scripts/lint-shell.sh`; no lint debt is deferred
  by M0.
- **Lifecycle exception:** This kit-source tree has no usable SDD cycle or
  feedback-report machinery. The user-authorized break-glass integration records
  no fabricated SDD, cycle, or feedback artifact; independent tests, critique,
  GitHub checks, and exact merged-head inspection are the acceptance evidence.
- **Landed stabilization:** Superseding PR
  [#2591](https://github.com/FS-GG/.github/pull/2591) merged as
  `22eeec6ac0a2f6f050e64e1c6be2c2ed7201d558` after its required checks passed;
  PRs [#2588](https://github.com/FS-GG/.github/pull/2588) and
  [#2589](https://github.com/FS-GG/.github/pull/2589) are closed as superseded.
  The independent critique and repaired-head confirmation are recorded in
  `reviews/roadmap/roadmap-coordination-churn-redesign-m0-stabilize.json`.
- **Coherent stable channel:** `coord-engine/v0.53.0`, `kit/v0.53.0`, and
  `drivers/v0.53.0` all peel to the merged SHA above. Trusted-publishing runs
  [engine 31782583439](https://github.com/FS-GG/.github/actions/runs/31782583439),
  [Kit 31782583496](https://github.com/FS-GG/.github/actions/runs/31782583496), and
  [Drivers 31782583720](https://github.com/FS-GG/.github/actions/runs/31782583720)
  passed in org-feed-first/NuGet.org-second order. Both feeds serve the full
  0.53.0 trio; 101 normalized package entries compare byte-identically, all
  nuspecs bind the packages to the merged SHA, and a fresh NuGet.org-only tool
  install reports `0.53.0.0`. The registry, generated compatibility projection,
  and canonical tool pin now select 0.53.0; 0.52.0 remains explicitly rejected.

### Cross-cutting health measures

Measure these weekly from M0 onward:

- issue creation stays below issue closure for at least three consecutive measurement periods;
- fewer than 10% of repair commits correct statements or projections without changing behavior;
- no deliberate scheduling intent is reversed by reconciliation;
- no successful read result is later discovered to have been paginated or partial;
- releases either complete coherently or remain visibly resumable, with no ambiguous channel state;
- the number of independent policy implementations, check scripts, and workflows trends down;
- checked-in generated evidence grows more slowly than core implementation and tests.

## Sequencing and risk control

M0 is a prerequisite for all other work because permanent red checks destroy the signal needed for a migration. M1 and M2 can then proceed independently. M3 depends on reliable complete reads when it verifies remote feed state, but its manifest format can be designed in parallel. M4 should reuse the revision and evidence primitives established by M1. M5 follows once the new boundaries reveal which legacy evidence is redundant.

Every replacement should use the same pattern:

1. define the new typed boundary and invariants;
2. replay production-derived fixtures through old and new implementations;
3. run both in shadow mode and record classified differences;
4. switch one consumer or operation at a time;
5. keep a bounded rollback switch;
6. remove the old path once the milestone exit criteria hold.

This avoids a big-bang rewrite while preventing indefinite dual systems. Compatibility code must have an owner, removal condition, and expiration milestone.

## Alternatives considered

### Continue working through the current design

This is appropriate for M0 but not as the long-term plan. The observed successor defects show that local exceptions and extra checks move failures rather than eliminating their causes.

### Rewrite the coordination engine at once

Rejected. The engine encodes substantial operational knowledge, and a full rewrite would combine semantic migration, tooling migration, and production cutover into one unverifiable change. Boundary-by-boundary replacement preserves working behavior and creates comparison oracles.

### Add more edge-specific checks and workflows

Rejected as the default response. Checks are valuable when they enforce a stable invariant at one boundary. They are harmful when they compensate for ambiguous state shared across many boundaries, because each check becomes another policy projection to synchronize.

### Treat multi-feed publication as atomic

Rejected because the registries do not provide a shared transaction. The honest designs are a resumable saga, a single bundle artifact, or a coherent channel manifest above the feeds.

### Keep prose identity with increasingly tolerant matching

Rejected as the end state. Exact hashes are too sensitive; subsequence matching is too permissive. Structured revisions make semantic changes explicit while leaving prose editable.

## Immediate decisions and next actions

1. Approve a temporary freeze on new process features until M0 is green.
2. Assign one owner per replacement boundary and require a compatibility-removal milestone.
3. Capture the current issue, PR, board, release, and workflow measurements as M0 fixtures.
4. Implement M1 and M2 first; they remove the broadest sources of recurring reconciliation defects.
5. Treat PR #2589 and issues #2580, #2584, #2586, and #2587 as input evidence and acceptance tests, not isolated incidents.
6. Review progress at each milestone exit condition; stop adding exceptions when a proposed repair belongs to a target boundary already scheduled for replacement.

## Evidence and reproducibility notes

This report is a timestamped analysis, not a live dashboard. Counts and states are point-in-time values from the baseline named above and GitHub state observed on 2026-08-14. Re-run repository history, board reconciliation, workflow checks, and GitHub issue/PR queries before using the numbers for a later decision.

Primary implementation evidence includes the lifecycle projection and chore reconciliation paths, the GraphQL read helpers and their separate consumers, the release workflows for the coordination engine, kit, and drivers, and the current `work/` and `readiness/` corpus. GitHub issues #2580, #2584, #2586, and #2587 and pull requests #2588 and #2589 provide the incident-level evidence referenced above.

The proposal deliberately distinguishes measured facts from architectural inference. The counts establish the volume and shape of churn; the recommendation that boundary ambiguity is the common cause is an inference supported by the repeated defect classes and current implementation structure.
