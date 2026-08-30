---
title: "Architecture review: GitHub Substrate v2 remaining migration"
category: Design
categoryindex: 4
index: 27
description: "Evidence-backed re-baseline of the remaining GitHub Substrate v2 implementation, migration, cutover, and retirement work."
---

# Architecture review: GitHub Substrate v2 remaining migration

This review re-baselines the unimplemented part of the GitHub Substrate v2 roadmap after repeated
late-stage defects exposed a shared design weakness: mutable GitHub comments were doing work that requires
an atomic concurrency authority. The review covers every remaining unit from GS2-03.7 through the former
GS2-14.7 endpoint, now amended through GS2-14.12, not only the review launcher that triggered it. Earlier
accepted receipts remain valid evidence of what was tested, but they do not exempt an affected design
surface from amendment and requalification.

| Field | Value |
|---|---|
| Status | Architecture amendment; implementation remains paused at GS2-03.7 until GS2-03.10 is accepted |
| Authored | 2026-08-30 |
| Scope | GS2-03.7 through the amended GS2-14.12 endpoint and affected GS2-02 protocol contracts |
| Execution issue | [`.github#3075`](https://github.com/FS-GG/.github/issues/3075) |
| Governing design | [GitHub Substrate v2 fleet-cutover design](2026-08-25-github-substrate-v2-fleet-cutover-design.md) |
| Execution roadmap | [GitHub Substrate v2 roadmap](../github-substrate-v2-roadmap.md) |
| Decision posture | Redesign existing work where a local repair would preserve a systemic weakness |

## 1. Executive decision

Keep the fleet-wide new-only cutover, native-GitHub authority model, typed mutation plans, independent
qualification, and explicit `OpenV2` boundary. Replace or strengthen five parts of the design:

1. **Replace comment-order CAS with sharded Git-ref CAS journals.** Claims, review epochs, operation
   grants, and other concurrency-sensitive protocol transitions append expected-parent commits to a
   protected per-aggregate ref. Comments and Project fields become replaceable projections only. Each
   grant carries the journal generation as a fencing token and every effect validates it.
2. **Make reconciliation the only normal writer path.** Commands and webhooks enqueue a subject and
   causation identity. A shared reducer performs fresh observation, derives desired state, persists the
   exact plan, applies guarded effects, verifies post-state, and appends a receipt. A complete scheduled
   audit is the loss-recovery authority.
3. **Use snapshot epochs consistently.** Review, plan, migration, settings, and release approval bind the
   full mutable authority/evidence/configuration snapshot, not only a source SHA. A changed snapshot mints
   a new epoch; same-epoch succession never carries authority into a new snapshot.
4. **Change cutover from switch-then-immediate-delete to expand/migrate/open/observe/contract.** Revoke or
   fence v1 writers at `OpenV2`, but retain inert source, verifier, manifests, and recovery evidence through
   the fixed 30-day observation. Destructive v1 contraction starts only after the observation gate. The
   system remains roll-forward-only after `OpenV2`; retained assets are forensic and recovery inputs, not
   permission to restart v1.
5. **Separate reproducibility, provenance, and served-artifact verification.** Build independently twice
   to prove reproducibility, designate exactly one candidate byte set, attest that set, publish those same
   bytes to both feeds, and install/download from clean consumers. An SBOM or signature alone is not proof
   of provenance, reproducibility, or feed coherence.

This is a bounded redesign, not a new hosted workflow database. Git remains the small strongly ordered
protocol authority; GitHub remains the native work/configuration authority; the reconciler remains
stateless apart from durable journals, cursors, plans, and receipts.

## 2. Why the redesign is warranted

The late review failure was not an isolated conditional. The protocol read a mutable comment stream,
selected a reviewer, then attempted to publish a result after the reviewed snapshot changed. GitHub REST
documents conditional requests as a caching mechanism and does not provide general conditional mutation
for unsafe methods. A pre-read/post-read check can detect a race but cannot make the write atomic
([GitHub REST best practices](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api)).

The same weakness exists anywhere v2 combines a lease, mutable projection, and external effect. etcd's
documentation is explicit that a lease alone does not guarantee mutual exclusion after expiry; the
protected resource must validate a revision/fencing value. etcd transactions provide atomic comparisons
over version, revision, or value, while Git history provides the analogous immutable parent relation
([etcd transactions and leases](https://etcd.io/docs/v3.6/learning/api/),
[etcd fencing explanation](https://etcd.io/docs/v3.6/learning/why/)).

The Git smart protocol carries the old and new object IDs for a ref update, and explicit
`--force-with-lease=<ref>:<expect>` rejects the push unless the remote ref still has that exact expected
object ID. The proposed journal commit remains a fast-forward; force-push rules continue to reject history
rewrites. This—not GitHub's REST update-ref body—is the usable CAS primitive. GitHub rulesets can protect
branches and admit a repository-scoped App as the sole bypass actor, but cannot scope bypass to one API
path, so the authority is isolated in a dedicated repository
([Git push lease semantics](https://git-scm.com/docs/git-push#Documentation/git-push.txt---force-with-leaseltrefnamegtltexpectgt),
[GitHub ruleset rules and bypass](https://docs.github.com/en/rest/repos/rules)). An immutable tag anchors an
accepted phase, and an issue comment only explains the result. Sharding by protocol aggregate avoids
turning a single global branch into a fleet-wide serialization bottleneck.

## 3. Research findings by remaining roadmap area

### 3.1 Reproducibility and supply chain — GS2-03.7, GS2-04.8, GS2-06.4, GS2-06.6

**Observed practice.** SLSA separates authentic provenance from stronger build isolation. Build L2
requires authenticated provenance; Build L3 requires it to be generated by the control plane with signing
material unavailable to user build steps. Hermeticity and isolation are different properties
([SLSA build requirements](https://slsa.dev/spec/v1.2/build-requirements),
[SLSA provenance](https://slsa.dev/spec/v1.2/provenance)). Reproducible-build ecosystems standardize build
time through `SOURCE_DATE_EPOCH`; .NET 11 also supports deterministic NuGet package timestamps
([SOURCE_DATE_EPOCH](https://reproducible-builds.org/specs/source-date-epoch/),
[deterministic NuGet packages](https://learn.microsoft.com/en-us/nuget/create-packages/deterministic-packages)).

GitHub artifact attestations bind artifact digests to repository, workflow, and source identity, but an
attestation verifier must request the intended predicate. GitHub changed its CLI default after finding that
verification could pass because an SBOM attestation existed when provenance was intended
([artifact-attestation verification change](https://github.blog/changelog/2025-02-18-recent-improvements-to-artifact-attestations/)).
NuGet source mapping reduces dependency-confusion exposure, but old clients ignore it, the global package
cache can bypass source lookup, metadata queries are outside its scope, and every transitive package must
be mapped ([NuGet package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)).

**Decision.** GS2-03.7 becomes a five-proof gate: two clean independent builds compare bytes; one candidate
is selected; provenance and SBOM predicates are verified separately; identical candidate bytes are
published to both feeds; clean consumers install by exact version from each isolated feed with an empty
repository-local cache. Record package, symbol package, SBOM, attestation, feed download, and installed
assembly digests. Package signing is additional identity evidence, not a substitute for these proofs.

**Trade-off.** The second clean build and isolated feed installs cost time. They prevent a much more
expensive class of false green where the workflow attests different bytes than consumers receive or a
warm cache hides a feed defect.

### 3.2 Independent review and anti-vacuity — GS2-03.8, GS2-03.9, GS2-05.6, GS2-10.8

Gerrit, GitLab, GitHub, and Azure DevOps all bind review state to a revision/patch set and offer policies
for invalidating approvals when new commits arrive; their trade-off is between review reuse and stale
approval risk ([Gerrit label copy conditions](https://gerrit-review.googlesource.com/Documentation/config-labels.html),
[GitLab merge request approvals](https://docs.gitlab.com/api/merge_request_approvals/),
[GitHub ruleset review rules](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets),
[Azure branch policies](https://learn.microsoft.com/en-us/azure/devops/repos/git/branch-policies)).

**Decision.** Use immutable `ReviewEpochKey = hash(full snapshot)` separately from a stable review-chain
identity. One deterministic critic seat exists per epoch. Any snapshot change creates a new epoch and a
fresh seat; succession is only recovery inside the same epoch. The snapshot includes source head plus the
mutable authority, evidence, toolchain, and policy digests a reviewer relied on, excluding review records
themselves. The reducer used by inspect, wait, write, and accept is one implementation.

Every gate class gets an independently maintained semantic inversion: remove one required artifact, make
a result stale, truncate one page, swap one digest, forge one generated roll-up, or turn one assertion into
a no-op. Mutation scores are diagnostic; named safety mutants are mandatory because equivalent or
irrelevant generated mutants can distort a percentage. PIT's model—run tests against deliberate program
mutations and enforce mutation/test-strength thresholds—supports this use, but not a blind global score
([PIT basic concepts](https://pitest.org/quickstart/basic_concepts/),
[PIT thresholds](https://pitest.org/quickstart/commandline/)).

### 3.3 GitHub API completeness and mutation safety — GS2-04

GitHub imposes primary and secondary rate limits, discourages concurrent mutations, recommends at least a
one-second pause between mutating requests, and requires clients to respect `Retry-After` and rate reset
headers. GraphQL connections return at most 100 nodes per page, queries have node and point limits, and
timeouts can return partial results
([REST rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api),
[GraphQL query limits](https://docs.github.com/en/graphql/overview/rate-limits-and-query-limits-for-the-graphql-api)).

**Decision.** Every adapter returns `Complete`, `Incomplete(reason,cursor)`, `Unauthorized`, `Unsupported`,
or `Indeterminate`; an empty page is never synonymous with absence. A collection observation stores query
identity, page cursors, node counts, cost, API version, and terminal-page proof. Mutations are serialized per
installation and aggregate, use stable idempotency identities, re-read immediately before effect, and
verify post-state. An indeterminate response schedules reconciliation; it is never blindly retried.

**Trade-off.** This rejects more operations during degradation and increases read traffic. Subject-bounded
reconciliation, caching only immutable observations, and explicit budgets contain the cost without
weakening correctness.

### 3.4 Work authority, claims, and leases — GS2-05

**Decision.** Native issue types, fields, hierarchy, and dependencies remain authoritative where their
semantics fit. Derived lifecycle status remains a projection. Replace comment-order claim CAS and touch-set
authority with Git journals:

- a dedicated `FS.GG.Coordination.Authority` repository whose only mutable branches are
  `refs/heads/fsgg/v2/journal/<kind>/<shard>`;
- an active branch ruleset targeting `fsgg/v2/journal/**` that restricts creation/update/deletion,
  blocks force pushes, disables administrator bypass, and gives always-bypass only to the dedicated
  journal App; bootstrap and audit read back both the ruleset and effective branch rules;
- exact old-object compare-and-swap through Git receive-pack using
  `--force-with-lease=<ref>:<observed-object-id>`; the proposed commit remains a one-parent
  fast-forward and a rejected lease is a conflict;
- one conflict-domain ref for a normalized touch set, or a deterministic multi-ref acquisition plan with
  compensation before any work grant becomes usable;
- a monotonically increasing grant generation used as a fencing token;
- lease expiry only makes a successor eligible; it does not authorize an old owner or new owner by itself;
- all delivery/release effects validate the current generation and exact review epoch.

Comments retain human-readable claim and review projections, including journal commit and generation.

**Pros.** Atomic expected-parent transition, immutable audit, deterministic replay, enforceable GitHub
branch protection, repository-scoped App write authority, and the same primitive for claims, reviews,
operations, and cutover. **Cons.** One additional repository, more refs, ruleset administration, Git
transport latency, and multi-touch acquisition complexity. These costs are preferable to an unfixable
comment write race. Custom non-branch refs and an API-path-scoped App bypass are explicitly rejected
because GitHub cannot enforce those controls.

### 3.5 Desired state and settings — GS2-06

Terraform's normal plan refreshes remote state before comparing desired configuration; saved plans bind
the reviewed operations, state locking prevents concurrent writers, and HCP Terraform discards a saved plan
when another run changes state. Targeted planning can hide drift, and disabling refresh can produce an
incorrect plan ([Terraform plan](https://developer.hashicorp.com/terraform/cli/commands/plan),
[Terraform state locking](https://developer.hashicorp.com/terraform/language/state/locking),
[HCP run modes](https://developer.hashicorp.com/terraform/enterprise/workspaces/run/modes-and-options)).

**Decision.** Settings use `observe -> normalize -> diff -> sealed plan -> approve -> refresh -> apply ->
verify`. Approval binds desired-state fingerprint, observation revisions, capability/permission profile,
and operation order. Any refreshed difference invalidates the plan. One settings reconciler owns normal
writes; emergency repair uses the same plan algebra and a separately authorized principal.

**Trade-off.** Operators must regenerate plans after benign drift. This is deliberate: applying an old
ruleset or permission plan is more dangerous than the delay.

### 3.6 Change-impact CI and merge queues — GS2-06.7, GS2-07.5, GS2-07.6

Bazel's reverse-dependency query illustrates the sound basis for affected-target selection: compute reverse
dependencies in an explicit universe, rather than matching only changed paths
([Bazel reverse dependencies](https://bazel.build/versions/9.1.0/query/quickstart)). GitHub merge queues
test synthetic merge groups; workflows must subscribe to `merge_group`, required checks can time out, and
policy may require every queue entry or only the combined group head to pass
([GitHub merge queue rules](https://docs.github.com/en/enterprise-cloud@latest/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets),
[merge queue behavior](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/incorporating-changes-from-a-pull-request/merging-a-pull-request-with-a-merge-queue)).

**Decision.** Compile a versioned subject-to-obligation graph. A small policy/core suite is unconditional;
expensive jobs use the sound transitive reverse closure; unknown or stale graph inputs select the full
suite. Required aggregates always report a typed result. Recompute selection for `merge_group` against its
synthetic head, current base, and current settings. Run scheduled full-suite sentinels and compare their
results with what the selector would have chosen; any missed obligation disables selection fleet-wide.

**Trade-off.** This will not always produce the mathematically smallest job set. It optimizes only inside
a fail-closed envelope and buys confidence with periodic full-build cost.

### 3.7 Events, reconciliation, and operations — GS2-07

GitHub does not automatically redeliver failed webhooks and does not guarantee delivery order, so webhooks
cannot be the completeness authority
([failed webhook deliveries](https://docs.github.com/en/webhooks/using-webhooks/handling-failed-webhook-deliveries),
[webhook best practices](https://docs.github.com/en/webhooks/using-webhooks/best-practices-for-using-webhooks)).
Kubernetes controllers instead compare desired and current state continuously. Their list-then-watch model
uses `resourceVersion`; when history is compacted and a watch returns `410 Gone`, clients clear local state,
re-list, and resume. Controller logic must be idempotent and must not assume cache read-after-write
([Kubernetes controllers](https://kubernetes.io/docs/concepts/architecture/controller/),
[Kubernetes API watches](https://kubernetes.io/docs/reference/using-api/api-concepts)).

**Decision.** Webhooks are authenticated hints that enqueue a stable subject key. A deduplicating queue
invokes the same reconciler used by commands and audits. Durable delivery IDs prevent duplicate work, but
fresh authority observations determine state. Scheduled complete audits use overlap-safe cursors and
stable double reads to repair dropped events and out-of-band edits. Periodic resync is mandatory for
external systems even when webhook metrics look perfect.

**Trade-off.** Event latency remains low while correctness no longer depends on delivery. The cost is a
hosted reconciler/queue and scheduled scans; if the host cannot meet its operational SLO, v2 must fall back
to scheduled reconciliation rather than weakening the state model.

### 3.8 Epoch bridge and fencing — GS2-08

**Decision.** Keep the protected global epoch ledger, but make every normal writer present both the epoch
commit and its operation/claim generation to the effect boundary. A cached `OperatingV1` observation is
never sufficient for the first external effect. The bridge reads the protected ref fresh, verifies
ancestry and manifest binding, and includes the observed commit in the receipt. Unfenceable clients lose
their credential, installation, schedule, or dispatch route before freeze.

The global epoch ref is intentionally singular because cutover must serialize fleet phase. Normal claims,
reviews, and operations are sharded refs so they do not contend on that global ledger.

### 3.9 Migration, rollback, and contraction — GS2-09 through GS2-14

GitLab documents expand/migrate/contract as separate compatibility phases and warns that old and new code
coexist during rolling deployment. Column/table removal and other post-deploy migrations are points of no
return and are delayed until compatible code has operated successfully
([GitLab multi-version compatibility](https://docs.gitlab.com/development/multi_version_compatibility/),
[GitLab continuous deployment migrations](https://about.gitlab.com/blog/continuously-deploying-the-largest-gitlab-instance/)).
AWS RDS blue/green deployments retain the old environment after switchover, but document asymmetric
rollback hazards: PITR history does not carry over, replication checkpoints can become invalid, tags and
identities can differ, and writes to the new environment can create conflicts
([RDS blue/green limitations](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/blue-green-deployments-considerations.html),
[RDS switchover guardrails](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/blue-green-deployments-switching.html)).

**Decision.** Split retirement into two gates:

1. `OpenV2 -> ObservingV2`: v2 becomes the sole writer; all v1 write credentials/routes are revoked or
   cryptographically fenced; inert v1 source, binaries, manifests, archive verifiers, and recovery inputs
   remain retained and hash-bound.
2. After fixed 0/7/14/30-day readings pass, `ObservingV2 -> ContractingV1 -> OperatingV2`: delete v1
   parsers, projections, fields, workflows, credentials, and temporary compatibility surfaces with a
   deletion receipt for each item.

Before `OpenV2`, rollback is executable and rehearsed. After it, recovery is roll-forward; retained v1
assets cannot authorize a write. The 30-day delay limits premature destruction without making a dishonest
rollback promise.

Complete discovery cannot be one cross-GitHub transaction. Qualification therefore requires two complete
quiescent reads with identical normalized digests, terminal pagination proofs for every collection, and a
manifest high-water mark per authority. Adding one subject between reads must either appear in the second
manifest or invalidate the snapshot.

GitLab's 2017 database incident is the caution against calling an untested artifact a backup: backup jobs
had silently failed, notifications were rejected, restore was slow, and no owner regularly exercised the
procedure. V2 recovery evidence therefore requires destructive rehearsal in isolated copies, measured
restore time, and a named owner—not only stored bytes
([GitLab database outage postmortem](https://about.gitlab.com/blog/postmortem-of-database-outage-of-january-31/)).

### 3.10 Observation and process learning — GS2-14 and the standard FS.GG process

Google SRE recommends predefined postmortem triggers, independent review, tracked preventive actions, and
an organization-level analysis when incidents repeat. It specifically calls repeated similar incidents a
signal to stop applying band-aids and consider a refactor
([Google SRE postmortem culture](https://sre.google/workbook/postmortem-culture/)).

**Decision.** The adopted FS.GG trigger is: after the second related defect in one late-stage acceptance
area, freeze that candidate and its production path; preserve exact evidence; map the entire affected
architecture; research comparable systems; model concurrency/irreversibility where relevant; generate at
least one independent negative control; and require a fresh reviewer against a new snapshot epoch. The
repair closes only when the systemic action is incorporated into the owning SDD/process projection.

## 4. Alternatives considered

| Alternative | Advantages | Disadvantages | Verdict |
|---|---|---|---|
| Patch comment-order CAS with pre/post reads | Small code change; no new refs | Still has an atomicity gap; stale writers can publish between checks; repeats the review defect in claims and operations | Reject as authority; retain only as detection around projections |
| One global Git journal for all protocol state | Simplest total order and audit | Fleet-wide contention, rate-limit concentration, unrelated failure coupling, large replay surface | Reject except for the intentionally global cutover epoch |
| Sharded protected Git refs | Expected-parent CAS, immutable history, independent replay, bounded contention | Ref/ruleset lifecycle, multi-aggregate transaction planning, Git API latency | Select |
| External transactional database/queue | Strong transactions, indexing, scalable workers | New hosted authority, backup/DR/on-call burden, credential and schema lifecycle, contradicts the small-substrate goal | Defer; use only if measured Git limits fail a named capacity gate |
| Pure polling | Simple completeness story | Higher latency and API cost | Keep as correctness fallback/audit, not sole fast path |
| Webhook-only | Low latency and API cost | Dropped/unordered delivery and preview gaps make it incomplete | Reject |
| Immediate v1 deletion after `OpenV2` | Fastest surface reduction | Destroys forensic/recovery assets before operational evidence; makes early defects harder to diagnose | Reject |
| Cohort-by-cohort v1/v2 fleet cutover | Smaller blast radius | Cross-repository operations straddle two authority generations and need a long-lived bridge | Reject for this eight-repository coupled fleet; retain sandbox/pilot canaries |
| Fleet-wide closed switch plus delayed contraction | One writer, bounded compatibility, full-fleet invariant | Larger coordinated window and roll-forward-only boundary remains | Select |

## 5. Required roadmap amendments

The implementation order changes as follows:

1. Add GS2-03.10 as the blocking architecture-amendment unit before GS2-03.7.
2. Amend and requalify affected GS2-02 contracts: process events, external observations, mutation plans,
   review/claim generations, protocol compiler output, and epoch invariants. Historical acceptance receipts
   remain linked but cannot qualify the amended candidate.
3. Change GS2-03.7 from “pack once, compare bytes” to “build twice, select once, publish identical bytes.”
4. Add the sharded Git-journal adapter to GS2-04 and remove comment-authoritative transitions.
5. Replace comment-order claim CAS in GS2-05.5 with journal CAS plus fencing generation.
6. Make the shared reconciler the exclusive normal write path across GS2-04–GS2-07.
7. Require an unconditional CI core, scheduled full-suite selector sentinel, and fleet-wide selector disable
   on a missed obligation.
8. Add per-authority high-water marks and stable double-read completeness to migration qualification.
9. Change the epoch sequence to `OpenV2 -> ObservingV2 -> ContractingV1 -> OperatingV2` and move
   destructive v1 deletion after the fixed 30-day observation gate.
10. Add capacity escape criteria for an external state store: measured Git journal p95 exceeds the accepted
    transition SLO, ref/ruleset count exceeds an administered ceiling, or multi-aggregate contention cannot
    pass the adversarial model and sandbox. Crossing a criterion requires a new ADR; it does not silently
    introduce a database.

## 6. Acceptance criteria for the amendment

### 6.1 Candidate qualification progress

As of 2026-08-30, [Coordination PR 109](https://github.com/FS-GG/FS.GG.Coordination/pull/109)
implements the first executable amendment slice. Its canonical source digest is
`5e4797762f8fe2fa26f184926ccc22ee8df949217700e97036887b48b6ced3a0`; its compiled-contract digest
is `947262bc9f70c371d79a917804d2ed4adcabbb1cc2ff683eedc637e36e6b163e`. A non-refresh canonical
replay passed seven bounded roots, seven formal scenarios, and 106 negative controls in 445,082 ms.
The new shared-state scenario exhaustively explores five states and five transitions through depth six,
proves stale-generation writes cannot commit, and proves scheduled audit convergence after webhook loss;
removing audit repair produces a retained temporal counterexample. The validator now derives process
inventory from selected roots and declared formal scenarios, preventing future model additions from
requiring an unrelated magic-count repair.

This is not yet an acceptance receipt. The remaining acceptance bullets below still require independent
review and the black-box external-effect, paginated-completeness, snapshot-epoch, hint-order, and role
evidence against the exact amended candidate.

GS2-03.10 is accepted only when:

- the governing design and roadmap contain the ten amendments above;
- the literate Quint authority includes journal expected-parent CAS, grant generations, stale owner,
  snapshot-epoch invalidation, lost response, duplicate/reordered hint, audit repair, and
  `OpenV2/ObservingV2/ContractingV1` transitions;
- an independent model author supplies counterexamples for comment-order CAS and lease-without-fencing;
- one black-box harness proves a stale grant cannot cause an external effect even when it still holds a
  locally valid lease;
- one completeness harness loses a webhook and a collection page, then proves audit convergence or typed
  refusal;
- architecture, security, migration, and operations reviewers assess the exact amended snapshot; and
- an accepted receipt identifies which earlier GS2-02 receipts are superseded for future candidates.

Until then, GS2-03.7 and all later implementation units remain paused. Research and design artifacts may
advance, but production authority, repository settings, releases, feeds, and receiver pins do not change.
