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

- [x] **M1 — intent/status split**
  - Target: Days 3–7
  - Deliverables: Add `SchedulingIntent`; implement pure reducer; shadow old/new projections; migrate deliberate parks
  - Exit criteria: Reconciliation is idempotent; explicit Backlog and human parks survive; replay differences are explained; rollback is a projection switch

- [x] **M2 — complete-read boundary**
  - Target: Week 1
  - Deliverables: Add typed GraphQL adapter and generic connection draining; migrate all production readers; add fault-injection tests
  - Exit criteria: No production call site handles raw GraphQL envelopes; incomplete reads cannot be returned as success

- [x] **M3 — release saga**
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
  measurements in the first table. The issue and PR values are frozen in the
  content-addressed as-of manifest
  `docs/reports/evidence/2026-08-14-coordination-churn-as-of.json`. Its cohort was
  selected by the recorded GitHub GraphQL search, then `ClosedEvent`,
  `ReopenedEvent`, and `MergedEvent` values were replayed only through the exact
  `2026-08-14T07:05:08Z` cutoff. The manifest records the complete cohort and its
  disjoint as-of classifications; `jq '.summary'` reproduces 131/103 issue
  created/closed and 146/135/9/2 PR created/merged/open/closed-unmerged. Verify
  the counts and partitions with
  `jq -e '(.summary.issues_created == (.cohorts.issues.created | length)) and (.summary.issues_closed == (.cohorts.issues.closed_as_of | length)) and (.summary.pull_requests_created == (.cohorts.pull_requests.created | length)) and (.summary.pull_requests_merged == (.cohorts.pull_requests.merged_as_of | length)) and (.summary.pull_requests_open == (.cohorts.pull_requests.open_as_of | length)) and (.summary.pull_requests_closed_unmerged == (.cohorts.pull_requests.closed_unmerged_as_of | length)) and (((.cohorts.pull_requests.merged_as_of + .cohorts.pull_requests.open_as_of + .cohorts.pull_requests.closed_unmerged_as_of) | sort) == (.cohorts.pull_requests.created | sort))' docs/reports/evidence/2026-08-14-coordination-churn-as-of.json`.
  Present-day `is:open`/`is:closed` searches are intentionally not reproduction
  commands because later lifecycle transitions change their answers. The Git-backed
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
- **Post-merge projection closure:** The absolute main-branch skill-registry
  [run 31784705208](https://github.com/FS-GG/.github/actions/runs/31784705208)
  exposed that the generated `publishing-and-deployment` source digest had moved
  while `registry/skills.yml` still named its pre-M0 digest. Follow-up PR
  [#2594](https://github.com/FS-GG/.github/pull/2594) reconciles that one digest
  from the producer manifest and records it in `registry/skills.CHANGELOG.md`;
  no predicate, row position, or skill membership changes.

### M1 intent/status split evidence

- **Independent intent and pure projection:** `LifecycleProjection.fs` defines the
  revisioned `SchedulingIntent` cases `Auto`, `Backlog`, `HumanPark`, and
  `Deferred`, separates observed facts from intent, and reduces them through a
  policy-versioned pure function. Production reconciliation computes legacy and
  intended results together, selects with the bounded
  `FSGG_COORD_LIFECYCLE_PROJECTION=legacy|intent-v1` switch, and defaults to the
  intended projection.
- **Durable deliberate parks:** Lifecycle watermark v2 persists scheduling intent
  independently of the derived board column while remaining backward-readable
  with v1 receipts. Watermarks are restored from every live derived column, and
  explicit non-`Auto` intent owns Status precedence so legacy live-claim chores
  cannot suppress the lifecycle write and its receipt. The Backlog-plus-held-claim
  regression and `Auto` control are exercised in `ApplicationServiceTests.fs`;
  human parks and idempotent reconciliation are exercised in
  `LifecycleProjectionTests.fs`.
- **Classified shadow replay:** Setting
  `FSGG_COORD_LIFECYCLE_SHADOW_REPORT` records deterministic subject-sorted
  legacy/intended differences and distinguishes `same`, expected policy movement,
  deliberate-park preservation, and `unexpected`. The replay harness now requires
  a checked-in `reconcile-shadow.json` for every fixture. The two-pass
  `m1-backlog-park` transcript records legacy `Ready`, intended `Backlog`, and the
  explained `deliberate-park-preserved` classification; all representative replay
  fixtures pass 30/30 with no unexplained difference.
- **Acceptance:** Exact implementation head
  `1e3b8b4c96173c6f9b8478c746635adb293481a8` builds in Release with zero warnings
  and errors. Core passes 863/863, CLI 838/838, GitHub 602/602, replay 30/30,
  hermetic end-to-end 14/14, write-path 149/149, parity 648/648 with zero not
  measured, and mutation-corpus anchors 13/13. `git diff --check` is clean.
- **Critique:** The independent schema-v3 record at
  `reviews/roadmap/roadmap-coordination-churn-redesign-m1-intent-status-split.json`
  records three repair rounds. Its two major findings—intent loss through mutable
  Status/live-claim precedence and discarded/unrecorded shadow differences—are
  resolved, and the same critic confirmed `pass` against the exact implementation
  head above. The shipped validator accepts the record.
- **Lifecycle exception:** This kit-source tree still has no usable SDD cycle or
  feedback-report provider. Under the user-authorized Chainsaw break-glass path,
  M1 deliberately produced no fabricated SDD, cycle, or feedback artifact. The
  direct source/test patch, rollback switch, replay evidence, independent critique,
  normal pull-request checks, and exact post-merge inspection remain mandatory.

### M2 complete-read boundary evidence

- **One typed contract:** `GraphQl.fs` owns GraphQL envelopes, mixed
  `data`/`errors`, typed retry/rate-limit classification, decoding, and generic
  Relay connection draining. Drains have explicit page/item limits and reject
  missing or malformed page information, empty continuing pages, repeated
  cursors or identities, changing `totalCount`, and an incomplete final count.
  `GraphQlEnvelope.fs` is the internal metering half of the same boundary;
  `Budget.fs` no longer opens response envelopes.
- **Production migration:** Board discovery, scans, done checks, audit reads,
  and related production readers now pass through the complete-read contract.
  The Python/shell audit and archive entry points use the explicitly temporary
  `graphql_complete_read.py` compatibility frontend, whose typed failure
  metadata distinguishes primary and secondary limits, retryability, reset
  time, and retry-after duration. The architectural checker and inversion
  fixture reject a raw F# envelope reader, a direct production shell transport,
  or a second production page parser.
- **Acceptance:** Exact implementation head
  `2d932bfb96c0bd5de04543567f47fa076c221e87` passes Core 863/863, GitHub
  608/608, CLI 838/838, the Python fault matrix 10/10, archive planning 18/18,
  roster closure 79/79, replay 30/30, GraphQL monopoly 22/22, the boundary
  checker and its inversion fixture, and `git diff --check`.
- **Critique:** The independent schema-v3 record at
  `reviews/roadmap/roadmap-coordination-churn-redesign-m2-complete-read-boundary.json`
  records five repair rounds. Its two major findings—meter parsing outside the
  boundary and untyped audit/archive readers—are resolved; the same critic
  confirmed `pass` against the exact implementation head, with no unresolved
  blocker or major finding. The shipped validator accepts the record.
- **Rollback and removal:** The F# reader migrations retain narrow compatibility
  decoders until M6. The Python frontend is removed in M6 only after typed F# CLI
  equivalents exist for project visibility/id, repository policy, board scan,
  archive mutation, and meter reads and those paths complete three stable
  operating cycles. Until then, rollback is the ordinary revert of the M2
  commits; incomplete reads continue to fail closed on either path.
- **Lifecycle exception:** This kit-source tree still has no usable SDD cycle or
  feedback-report provider. Under the user-authorized Chainsaw break-glass path,
  M2 produced no fabricated SDD, cycle, or feedback artifact. Direct source and
  test evidence, the independent critique, normal pull-request checks, and exact
  post-merge inspection remain mandatory.

### M3 release-saga evidence

- **Immutable identity and policy:** Prepare run
  [31798384146](https://github.com/FS-GG/.github/actions/runs/31798384146) packed the coherent
  `0.54.0` set once from merged source
  `9c5f8c077e59c84301333b362596f2b185231d5a`. Release ID `github:0.54.0`, policy
  `release-saga/1`, and content ID
  `sha256:0c4c9269a5ad16efc17543a60da679804ef4ec73ddeefcc4a4f370689401b3e8`
  bind the [published stable release](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.54.0).
  The final manifest SHA-256 is
  `19cb9411774d25c9daf3b79ee51528f2bcbe8e2f28a9fccc2e7ba13cba23d374` after the final clean
  replay; the stable-channel
  receipt SHA-256 is `24abdeaad9cf2159ac4de515ce8e7baee2af316fc328cb6c2c3e4c032da65a71`.
- **Exact artifacts and feed order:** Input/GitHub archive SHA-256 values are Coord.Cli
  `36f0f0d62221ad6689e190207f59815789a205f48d79166794446cc82fc7639e`, Drivers
  `887c926cc9b92734fa0b6b51cff0d0ab47b0c92f23b19a11a1dd6c47d83a5f2f`, and Kit
  `e548749eea2b3e0f694873ee762a60abb3daa2d4b92e876c33d77280cce3340d`. NuGet's signed
  archives are respectively `9f59951e93891e2f54c9611206b76d2386d86094c435cf37fb9e7b722f29d04c`,
  `9ba54b538fdd0de76052b1c7272d66bc400671b4e1fcecf464935275e4e6db38`, and
  `9887f44b41333f88f64db68b84c8047397e498dba8cc1b99bfc5649f14bf305e`.
  Entry-by-entry comparison, excluding only NuGet's appended signature, proves identical payload
  hashes `61dc5e03a79eb23cf8359a1925cb106ea6b2e34ccc485bcbb37ea3eb81b0288a`,
  `60adee44ad4b00ee820d2be5743a8029a7240d02cabd850d0a7262a692a6c1aa`, and
  `51888ea07145a2c9c77ba51bd25b5f5bb9a30777ba014077187315fa904532b5`.
  Promotion run [31803041325](https://github.com/FS-GG/.github/actions/runs/31803041325)
  observed the full GitHub set before the full NuGet set and only then promoted stable.
- **Recovery and clean completion:** The durable manifest records every failed attempt, per-feed
  monotonic state, externally observed hashes, and 22 resumptions without byte drift or a duplicate
  immutable push. The forced mid-publish fixture in `tests/release-saga/run.sh` persists a failure,
  resumes from the same manifest and hashes, and rejects changed bytes. After recovery, a fresh
  observation-only coherent pass—engine
  [31803203853](https://github.com/FS-GG/.github/actions/runs/31803203853), Kit
  [31803205820](https://github.com/FS-GG/.github/actions/runs/31803205820), and Drivers
  [31803208329](https://github.com/FS-GG/.github/actions/runs/31803208329)—completed without
  manual recovery or a push; its idempotent promotion runs `31803249669`, `31803314176`, and
  `31803356405` all passed against the same live channel receipt.
- **Rollback and compatibility removal:** Published `0.54.0` bytes and tags are immutable. A bad
  release is not deleted, overwritten, or demoted to an older receipt; rollback is a newly prepared,
  higher coherent version from a reviewed source, promoted only after both feeds serve the full set.
  The three superseded independent publisher paths remain compatibility shims until M6 and are
  removed only after three stable operating cycles.
- **Delivery and critique:** Preparatory and production-recovery changes landed through normal PRs
  [#2600](https://github.com/FS-GG/.github/pull/2600),
  [#2601](https://github.com/FS-GG/.github/pull/2601),
  [#2602](https://github.com/FS-GG/.github/pull/2602),
  [#2604](https://github.com/FS-GG/.github/pull/2604), and
  [#2605](https://github.com/FS-GG/.github/pull/2605). The same independent critic's schema-v3
  record at `reviews/roadmap/roadmap-coordination-churn-redesign-m3-release-saga.json` covers the
  implementation and exact external release evidence; the shipped validator accepts it.
- **Lifecycle exception:** This kit-source tree still has no usable SDD cycle or feedback-report
  provider. The user-authorized Chainsaw path bypassed only those unavailable lifecycle mechanics;
  M3 fabricated no SDD, cycle, or feedback artifact and retained credentials, trusted publishing,
  security, review, CI, exact-artifact, registry, and post-merge verification requirements.

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
