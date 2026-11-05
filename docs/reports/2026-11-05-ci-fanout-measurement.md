# CI fan-out measurement and fixture consolidation, 5 November 2026

[Delivery #5182](https://github.com/FS-GG/.github/issues/5182) remains open. The earlier
[consolidation PR #5186](https://github.com/FS-GG/.github/pull/5186) reduced observed fan-out
on two ordinary report-only PRs. This follow-up combines five remaining fixture/check pairs and
consolidates the two job-context workflows into one run and one job. Its native after-state, exact-head
review, required CI and protected integration remain pending.

## Real PR observations

Both PRs changed exactly one progress report, each had one observed run-creation batch on its
final head, and both include the two `pull_request_target` workflows. These are observed final-head
batches, not an assertion that every run in the week corresponds to a distinct push.

| Observation | Before: [PR #5169](https://github.com/FS-GG/.github/pull/5169), 1 November | After earlier consolidation: [PR #5298](https://github.com/FS-GG/.github/pull/5298), 4 November |
|---|---:|---:|
| Exact final head | `3e58f64591d148d1bce13d12b71844168f3ff0f9` | `983d129e95343366b5496f16d8faf3c1829f2de3` |
| Effective base (native base and local merge-base agree) | `d4fa2cb87374f0ce1366002905934e095bfb3d47` | `33412050a00d89bab57c0a5da2fade114d5ab36a` |
| PR runs / target runs | 19 / 2 | 13 / 2 |
| Total runs | 21 | 15 |
| Reported jobs, including skipped jobs | 38 | 27 |
| Jobs with complete runner timestamps | 36 | 24 |
| Job queue wait p50 / p90, seconds | 271.5 / 472 | 2 / 3 |
| Observed runner-minutes | 14.617 | 14.917 |
| Run job sets without observations | 0 | 0 |

The reduction is observed; the size of a causal queue or cost improvement is unproved. Other source
changes and organization load differ between the PRs. In particular, `coherence` reports six jobs
before and five after, while `claim-generation` appears only after. Runner-minutes increased by
0.300 in this pair. No billed-cost or runner-minute saving is claimed.

The full week's PR-head populations do not show a comparable overall reduction. Grouping only
`pull_request` runs by head SHA, 336 populations with one of the earlier eight workflow names have
median 33 distinct workflows and 37 total runs; 107 populations with `always-gates` have medians
37 and 40. Two populations contain neither signature. None contains both. These groups differ in
changed-file mix, source and observation-window coverage; repeated events remain inside each head
population. The favorable report-only pair must not be generalized to all PRs.

Job queue wait is `started_at - created_at` on a job with a named runner and complete timestamps.
Runner-minutes sum `completed_at - started_at` across those jobs. Skipped jobs contribute neither
a zero queue sample nor runner work. Run-level `run_started_at` equaled run creation in the earlier
measurement and cannot substitute for job queueing. The inspected GraphQL `CheckRun` schema has
start and completion fields but no creation field; batching it cannot fill the queue-wait gap.

## Week coverage and method

The window is **29 October 00:00 UTC through 5 November 00:00 UTC, end exclusive**.
The observed population contains **18,357 unique runs: 17,192 `pull_request` and 1,165
`pull_request_target` runs, across 141 workflow paths**. Run IDs deduplicate overlapping saved pages.

The earlier authentic collection contains 14,394 PR runs over 25 October–1 November 12:00 UTC.
Its saved per-window query totals sum to 14,394, matching the unique run IDs; no query exceeds
922 runs. Only its 29 October onward subset enters this report. New collection drained six-hour
windows through 5 November 00:00, including the target-event windows omitted from that array.
All new windows exhausted their reported totals below the provider's 1,000-result cap. There is no
known residual run-list pagination in the selected window. Historical raw response retention is
weaker than the new page/endpoint manifests; that distinction is retained.

Job timing is **partial**, not a whole-week total: 2,040 run job sets contain 3,327 jobs; 2,865 jobs
have complete runner timing. **16,317 run job sets lack job observations**. Most timing observations
come from the earlier 1 November 00:00–12:00 collection; 34 further exact-attempt job reads cover
21 recent target-workflow runs and 13 missing comparison runs. All 34 new job pages reached their
reported total below 100 jobs. The historical job map lacks preserved pagination/attempt response
metadata, so its attempt completeness remains Unknown. Observed timing sums to 3,407.733 runner-minutes,
with job queue p50 94 seconds and p90 436 seconds. These statistics describe the observed job subset;
no whole-week runner-minute estimate follows.

Current-filter replay uses source **`8465501f44273b464e9dfc507c77063adb857987`**, plus this
follow-up's five local workflow edits. It can classify two exact-head file sets obtained from native
PR file responses, with their file counts checked against the PR records. Historical filters and
changed-file sets for other heads remain Unknown. A broad match alone does not establish unnecessary
work: several gates intentionally inspect all committed files. The table's filter column describes
the current source, not a claim that every historical run matched that filter.

The offline [measurement helper](../../scripts/measure-ci-fanout.py) consumes saved run arrays or
API pages and an optional run-ID-to-jobs map. It performs no network or workload operations. Its
`--start`/`--end` window is explicit; `--root` enables current-filter replay using the existing
paths-coherence matcher. Unsupported patterns and missing complete file sets remain Unknown.
A head population groups event and SHA; body edits and reruns can share it, so it is never named a
push count. Private raw pages and manifests remain with the Delivery owner; they are not Atlas records.

## Local follow-up and preflight

Closing-keywords, kit-published-coherence and scheduled-engine-provisioning keep their existing
production job names, scripts, verdict handling and event sets. Their fixtures run first in the
same job. The kit fixture runs in either its PR authoring arm or its non-PR published arm.
Closing-keywords still runs its fixture on dispatch and checks production subjects only on PR events.

The plain-name and reusable-ID coherence gates share the existing `reusable-job-id-coherence.yml`
workflow and one explicitly named job, **Job contexts: unique names and stable reusable IDs**.
This deliberately replaces the non-required `job-names-stable` and `job-ids-stable` contexts.
Neither old workflow exposes `workflow_call`, and none of the ten native required contexts or
producers moves. The obsolete uniqueness workflow is removed after reference closure; the preserved
workflow unions both script/test filters and retains the existing whole-workflow-tree subject.

Both real gates and both fixtures run on all six existing events: opened, synchronize, reopened,
edited, labeled and unlabeled. The source check is never replaced by a newer metadata-skipped job.
Each fixture requires successful checkout and Python setup. Each gate depends on its own fixture;
explicit `!cancelled()` guards let the independent gate collect
its verdict after another gate fails. Any fixture or gate failure still fails the combined job.

The existing concurrency block is unchanged. Head actions use the PR number; metadata events use
separate run IDs. Existing policy-model controls bind to the actual YAML and prove that all nine
head/metadata action pairs preserve both running and pending head validation, while a newer head
supersedes its predecessor. Three deliberately unsafe variants lose the pending original: dropping
the head-action guard, admitting metadata into the head-action list, and using an unguarded PR key
with cancellation disabled. Cancellation disabled alone does not protect a pending run.

| Workflows before this follow-up | Resulting job | Fewer runner jobs when all listed workflows match |
|---|---|---:|
| `closing-keywords.yml` | `declared-closes` | 1 |
| `kit-published-coherence.yml` | `pr-arm` / `published` | 1 |
| Plain-name and reusable-ID coherence | `Job contexts: unique names and stable reusable IDs` | 3 |
| `scheduled-engine-provisioning.yml` | `provisioned` | 1 |

This predicts two fewer runner jobs for an ordinary report-only batch. A head event matching all
five original workflows predicts one fewer workflow run and six fewer runner jobs. A metadata
event keeps one combined runner and adds the short uniqueness check inside that runner. These are
static predictions until a real candidate PR runs. Observed week populations include 172 heads with
both old coherence workflows and two with only reusable-ID runs; head grouping is not a per-push
savings calculation.

Paths and timeout workflows remain intact: their `workflow_call` interface exposes the fixture job,
so deleting it would change public APIs. The current engine workflow already gates its expensive
engine job on impact; this follow-up claims no further engine reduction. Broader skill/docs filter
obligations remain with this Delivery. A broad filter cannot be narrowed merely because its runs are
small: the retirement checker scans all docs for stage restatements, for example. `skill-quality.yml`
is separately owned by another frozen source change.

Preflight uses existing static checks, fixture suites and the existing concurrency policy model;
no custom state model is added. New measurement/wiring controls share the existing
scheduled-engine-provisioning job, adding no hosted job. The local command is
`python3 tests/workflow-selection/run.py`; it uses the pinned PyYAML provided by
`setup-policy-python`, with no additional download. Twelve tests, 46 wiring mutations and three unsafe
concurrency variants passed in 0.164 seconds end-to-end locally. Cold hosted setup, future maintenance
and realized savings remain unmeasured.

## Verification and retained failures

Prior component `d3a75149` passed closing-keywords 61 assertions, reusable-job-ids 19 and
scheduled-engine-provisioning 29; those results are reused only where inputs are unchanged. The
successor uniqueness fixture passed 26 assertions including eight event/field/skip mutations; its
measurement/wiring suite passed 12 tests and 46 planted missing, masked, reordered or conditional
fixture/job/gate variants. The kit suite reports
`270 passed, 0 failed`, then three further inversion controls, and
`all 273 declared legs ran`.

The first kit attempt reported 248 passed and 22 failed because a 2 GiB virtual-address limit
prevented CLR thread startup during `dotnet msbuild`. Its original output is retained. The same
suite passed under an actual systemd user scope requested with 2 GiB memory, zero swap and two
CPU equivalents. That correction changes local containment, not gate semantics.

Live classic branch protection and effective rules were read before validation. Candidate and exact
protected-source `check-required-contexts.py` outputs are byte-identical: nine contexts are proved
producible, with inherited exit 3 for the tenth, `board-linkage`, whose job condition the validator
cannot prove universal. This is an inherited static Unknown, not a passing required-context check.
None of the removed or edited workflows produces one of the ten native required contexts. The actual
required native gate remains mandatory; no settings or producer migration is made here.

The first full concurrency fixture attempt reached a local 180-second execution guard before completion;
that result is budget-incomplete, not a source assertion failure. Its bounded retry passed all
721 assertions on that component. The successor also passed all 721 assertions. Paths-coherence passed 190 fixture assertions and checked 126 matching trigger pairs,
120 project subjects and 20 script surfaces; successor timeout validation audited 306 jobs; reusable-context
validation preserved all 49 reusable workflow contracts; successor job-uniqueness graded 178 names and nine
plain required contexts; test-selector passed 69 assertions. The first policy-inventory invocation
used a nonexistent guessed script name (exit 2); its correction uses the existing `policy-runner.py inventory`
entry point, which passed for 22 large checkers and 76 pinned workflow bootstraps. All six policy
subjects passed; prose citations checked 698 live documents, 61 local citations and 504 section citations.
The cgroup readback retained `memory.max=2147483648`, `memory.swap.max=0` and
`cpu.max=200000 100000`. A successor scratch-runner path error overwrote earlier local
`coherence-*` logs; those first results survive in the original tool transcript and the historical
independent review, not as original files at the overwritten paths. Current successor results were
moved to a separate directory, and later outputs refuse overwrites. The gap remains explicit. The successor's first wiring-control attempt failed two missing-gate mutations
in the kit arms because another different gate remained. The control was repaired to require both
production arms; the successful expanded-mutation run does not erase that retained failure. Source review,
final native required CI and candidate after-state remain pending. Delivery
acceptance must retain that remainder. The reusable Atlas finding previously drafted as
`github/ci-fanout-cost-is-runner-slots` was reverted from PR #5186 because Atlas packaging would
expand that source touch-set. Capture gap owner remains the #5182 source owner; this report does not
claim capture or full Delivery completion.

## Per-workflow observed week population

Jobs and minutes cover only saved observations. **Unknown** means no timing sample, never zero
work. Filter replay columns count observed runs whose exact head has a complete supplied file set;
all other path-filter decisions remain Unknown. “Unfiltered” classifies the current trigger itself.

| Workflow | Runs | Job sets observed / missing | Jobs | Queue p50 s | Observed runner-min | Current PR filter | Replay matched / excluded / unfiltered / Unknown |
|---|---:|---:|---:|---:|---:|---|---|
| `adr-coherence.yml` | 11 | 0 / 11 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 11 |
| `always-gates.yml` | 107 | 3 / 104 | 6 | 28.5 | 6.717 | unfiltered | 0 / 0 / 107 / 0 |
| `architecture-map.yml` | 739 | 83 / 656 | 83 | 83 | 10.067 | unfiltered | 0 / 0 / 739 / 0 |
| `atlas.yml` | 109 | 4 / 105 | 4 | 17.5 | 1.017 | 17 paths | 0 / 0 / 0 / 109 |
| `board-drift-observer-selftest.yml` | 41 | 7 / 34 | 7 | 2 | 23.05 | 12 paths | 0 / 0 / 0 / 41 |
| `board-linkage.yml` | 387 | 2 / 385 | 2 | 138.5 | 0.25 | unfiltered | 0 / 0 / 387 / 0 |
| `board-paths-live-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 3 |
| `board-v2-import-preflight.yml` | 100 | 26 / 74 | 26 | 60 | 53.017 | 15 paths | 0 / 0 / 0 / 100 |
| `bridge-package.yml` | 93 | 25 / 68 | 25 | 103 | 37.283 | 22 paths | 0 / 0 / 0 / 93 |
| `c3-audio-ordinary-enrollment-bridge-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `c3-coordination-ordinary-enrollment-bridge-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `c3-game-ordinary-enrollment-bridge-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `c3-governance-ordinary-enrollment-bridge-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `c3-net-ordinary-enrollment-bridge-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `c3-rendering-ordinary-enrollment-bridge-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `claim-generation.yml` | 206 | 22 / 184 | 22 | 155 | 14.917 | unfiltered | 0 / 0 / 206 / 0 |
| `closing-keywords.yml` | 739 | 82 / 657 | 160 | 80 | 44.917 | unfiltered | 0 / 0 / 739 / 0 |
| `coherence.yml` | 655 | 72 / 583 | 409 | 138 | 108.9 | unfiltered | 0 / 0 / 655 / 0 |
| `coherent-set-version.yml` | 47 | 5 / 42 | 5 | 138 | 1.383 | 7 paths | 0 / 0 / 0 / 47 |
| `coord-board-archive-selftest.yml` | 11 | 2 / 9 | 2 | 19 | 0.15 | 4 paths | 0 / 0 / 0 / 11 |
| `coord-core.yml` | 37 | 5 / 32 | 5 | 142 | 5.283 | 7 paths | 0 / 0 / 0 / 37 |
| `coord-engine.yml` | 446 | 47 / 399 | 94 | 40 | 918.1 | unfiltered | 0 / 0 / 446 / 0 |
| `coord-github.yml` | 171 | 30 / 141 | 30 | 97 | 110.367 | 46 paths | 0 / 0 / 0 / 171 |
| `coord-guards.yml` | 4 | 0 / 4 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 4 |
| `coord-report-selftest.yml` | 17 | 1 / 16 | 1 | 554 | 0.183 | 5 paths | 0 / 0 / 0 / 17 |
| `coordination-sync-selftest.yml` | 167 | 10 / 157 | 10 | 184 | 4.4 | 8 paths | 0 / 0 / 0 / 167 |
| `cross-repo-request-predicate.yml` | 2 | 0 / 2 | 0 | Unknown | Unknown | 2 paths | 0 / 0 / 0 / 2 |
| `dashboard-tick-selftest.yml` | 9 | 0 / 9 | 0 | Unknown | Unknown | 7 paths | 0 / 0 / 0 / 9 |
| `dispatch-preflight-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 3 |
| `drivers-package.yml` | 192 | 15 / 177 | 15 | 37.5 | 5.767 | 16 paths | 0 / 0 / 0 / 192 |
| `engine-build-determinism.yml` | 68 | 17 / 51 | 17 | 22 | 85.733 | 10 paths | 0 / 0 / 0 / 68 |
| `engine-flag-narrative.yml` | 339 | 46 / 293 | 92 | 94 | 12.5 | not in current source | 0 / 0 / 0 / 339 |
| `engine-release-notes.yml` | 47 | 5 / 42 | 5 | 73 | 1.9 | 11 paths | 0 / 0 / 0 / 47 |
| `fdev-board-tend-selftest.yml` | 12 | 0 / 12 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 12 |
| `fdev-host-hygiene-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `fdev-lane-capacity-selftest.yml` | 6 | 3 / 3 | 3 | 16 | 1.183 | 3 paths | 0 / 0 / 0 / 6 |
| `feed-autofix.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `feed-coherence.yml` | 40 | 0 / 40 | 0 | Unknown | Unknown | 8 paths | 0 / 0 / 0 / 40 |
| `fsc-progress-evidence.yml` | 30 | 4 / 26 | 4 | 38 | 3.3 | 15 paths | 0 / 0 / 0 / 30 |
| `fsgg-claim-fence.yml` | 739 | 82 / 657 | 81 | 116 | 41.5 | unfiltered | 0 / 0 / 739 / 0 |
| `fsgg-dispatch-broker-selftest.yml` | 172 | 21 / 151 | 21 | 115 | 7.4 | 5 paths | 0 / 0 / 0 / 172 |
| `fsgg-receiver-validate-selftest.yml` | 14 | 0 / 14 | 0 | Unknown | Unknown | 6 paths | 0 / 0 / 0 / 14 |
| `gate-mutation-selftest.yml` | 6 | 0 / 6 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 6 |
| `generator-list.yml` | 267 | 19 / 248 | 19 | 79 | 2.983 | 8 paths | 0 / 0 / 0 / 267 |
| `graphql-monopoly.yml` | 333 | 36 / 297 | 72 | 99 | 9.667 | 26 paths | 0 / 0 / 0 / 333 |
| `gs2-09-7-receiver-observer-probe-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `harness-identity.yml` | 277 | 32 / 245 | 64 | 84.5 | 8.467 | 3 paths | 0 / 0 / 0 / 277 |
| `host-protected-artifact-custody.yml` | 1 | 0 / 1 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 1 |
| `ignored-author-coherence.yml` | 172 | 21 / 151 | 21 | 81.5 | 18.233 | 5 paths | 0 / 0 / 0 / 172 |
| `job-uniqueness-coherence.yml` | 221 | 24 / 197 | 48 | 87 | 18.4 | not in current source | 0 / 0 / 0 / 221 |
| `kit-auto-publish-selftest.yml` | 34 | 0 / 34 | 0 | Unknown | Unknown | 8 paths | 0 / 0 / 0 / 34 |
| `kit-bump-shape.yml` | 24 | 0 / 24 | 0 | Unknown | Unknown | 7 paths | 0 / 0 / 0 / 24 |
| `kit-package-selftest.yml` | 25 | 0 / 25 | 0 | Unknown | Unknown | 7 paths | 0 / 0 / 0 / 25 |
| `kit-package.yml` | 162 | 10 / 152 | 10 | 118 | 7.15 | 9 paths | 0 / 0 / 0 / 162 |
| `kit-published-coherence.yml` | 446 | 49 / 397 | 147 | 54 | 63.517 | unfiltered | 0 / 0 / 446 / 0 |
| `landed-revert-guard.yml` | 453 | 83 / 370 | 83 | 101.5 | 10.367 | unfiltered | 0 / 0 / 453 / 0 |
| `landed-reverts-selftest.yml` | 8 | 2 / 6 | 2 | 27.5 | 0.6 | 6 paths | 0 / 0 / 0 / 8 |
| `learn-02-selftest.yml` | 12 | 0 / 12 | 0 | Unknown | Unknown | 24 paths | 0 / 0 / 0 / 12 |
| `learn-ack-writer-selftest.yml` | 4 | 0 / 4 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 4 |
| `learn-finalizer-r5-selftest.yml` | 8 | 2 / 6 | 2 | 354.5 | 0.983 | 6 paths | 0 / 0 / 0 / 8 |
| `learn-finite-profile-selftest.yml` | 29 | 5 / 24 | 5 | 152.5 | 0.45 | 3 paths | 0 / 0 / 0 / 29 |
| `learn-operator-r6-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 3 |
| `learn-pod-harness-selftest.yml` | 2 | 0 / 2 | 0 | Unknown | Unknown | 8 paths | 0 / 0 / 0 / 2 |
| `learn-pod-image-selftest.yml` | 2 | 0 / 2 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 2 |
| `learn-pod-operator-selftest.yml` | 8 | 0 / 8 | 0 | Unknown | Unknown | 7 paths | 0 / 0 / 0 / 8 |
| `learn-seal-claim-selftest.yml` | 12 | 0 / 12 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 12 |
| `lock-range-coherence-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 3 |
| `lockfile-cold-selftest.yml` | 5 | 0 / 5 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 5 |
| `main-health-observer-selftest.yml` | 68 | 21 / 47 | 21 | 51 | 12.017 | 4 paths | 0 / 0 / 0 / 68 |
| `new-sdd-workspace-selftest.yml` | 116 | 26 / 90 | 26 | 117.5 | 335.1 | 29 paths | 0 / 0 / 0 / 116 |
| `observed-command-report-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 2 paths | 0 / 0 / 0 / 3 |
| `org-permission-policy.yml` | 34 | 4 / 30 | 4 | 96 | 3.267 | 9 paths | 0 / 0 / 0 / 34 |
| `org-policy.yml` | 30 | 4 / 26 | 4 | 113.5 | 1.883 | 7 paths | 0 / 0 / 0 / 30 |
| `parity-fixtures.yml` | 7 | 0 / 7 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 7 |
| `paths-coherence.yml` | 219 | 33 / 186 | 66 | 73.5 | 41.317 | 6 paths | 0 / 0 / 0 / 219 |
| `permission-coherence.yml` | 178 | 21 / 157 | 63 | 78 | 45.683 | 5 paths | 0 / 0 / 0 / 178 |
| `pin-coherence.yml` | 22 | 0 / 22 | 0 | Unknown | Unknown | 16 paths | 0 / 0 / 0 / 22 |
| `pipefail-assertions.yml` | 339 | 46 / 293 | 92 | 82 | 12.5 | not in current source | 0 / 0 / 0 / 339 |
| `policy.yml` | 212 | 21 / 191 | 21 | 99 | 12.3 | 22 paths | 0 / 0 / 0 / 212 |
| `pr-concurrency-coherence.yml` | 33 | 7 / 26 | 7 | 174 | 29.533 | 68 paths | 0 / 0 / 0 / 33 |
| `pr-lane-admission.yml` | 6 | 0 / 6 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 6 |
| `preset-repo-scope-coherence.yml` | 9 | 0 / 9 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 9 |
| `preset-resolution.yml` | 7 | 0 / 7 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 7 |
| `project-field-options.yml` | 16 | 0 / 16 | 0 | Unknown | Unknown | 10 paths | 0 / 0 / 0 / 16 |
| `projections.yml` | 231 | 30 / 201 | 30 | 52 | 125.05 | 34 paths | 0 / 0 / 0 / 231 |
| `prose-citations.yml` | 339 | 47 / 292 | 47 | 124 | 8.267 | not in current source | 0 / 0 / 0 / 339 |
| `protected-intake-preflight-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 6 paths | 0 / 0 / 0 / 3 |
| `provisioned-podman-source.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 3 |
| `publication-writer-lock-selftest.yml` | 174 | 21 / 153 | 42 | 88 | 3.333 | 6 paths | 0 / 0 / 0 / 174 |
| `publish-flags.yml` | 10 | 0 / 10 | 0 | Unknown | Unknown | 7 paths | 0 / 0 / 0 / 10 |
| `recipe-followup.yml` | 391 | 39 / 352 | 39 | 51.5 | 14.567 | 10 paths | 0 / 0 / 0 / 391 |
| `recipe-landable.yml` | 391 | 39 / 352 | 39 | 80 | 14.883 | 9 paths | 0 / 0 / 0 / 391 |
| `recipe-pagination.yml` | 315 | 32 / 283 | 64 | 93 | 8.7 | 7 paths | 0 / 0 / 0 / 315 |
| `reconcile-concurrency-scope-coherence.yml` | 4 | 0 / 4 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 4 |
| `registry-changelog.yml` | 37 | 0 / 37 | 0 | Unknown | Unknown | 6 paths | 0 / 0 / 0 / 37 |
| `release-saga-tooling.yml` | 21 | 2 / 19 | 2 | 59.5 | 5.567 | 33 paths | 0 / 0 / 0 / 21 |
| `release-successor-authenticate-source.yml` | 2 | 0 / 2 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 2 |
| `release-tag-anchors-selftest.yml` | 12 | 0 / 12 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 12 |
| `repo-filter-monopoly.yml` | 97 | 25 / 72 | 50 | 134 | 6.967 | 5 paths | 0 / 0 / 0 / 97 |
| `repos-audit-selftest.yml` | 12 | 0 / 12 | 0 | Unknown | Unknown | 12 paths | 0 / 0 / 0 / 12 |
| `repos-registry-selftest.yml` | 294 | 28 / 266 | 28 | 92 | 18.8 | 17 paths | 0 / 0 / 0 / 294 |
| `required-context-coherence-selftest.yml` | 16 | 0 / 16 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 16 |
| `rest-budget-preflight-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 3 |
| `retirement-order-coherence.yml` | 324 | 34 / 290 | 68 | 82 | 28.867 | 7 paths | 2 / 0 / 0 / 322 |
| `reusable-job-id-coherence.yml` | 299 | 39 / 260 | 78 | 95.5 | 24.517 | 5 paths | 0 / 0 / 0 / 299 |
| `routine-eligibility-selftest.yml` | 23 | 2 / 21 | 2 | 56 | 0.483 | 9 paths | 0 / 0 / 0 / 23 |
| `routine-eligibility.yml` | 778 | 2 / 776 | 2 | 220 | 0.2 | unfiltered | 0 / 0 / 778 / 0 |
| `scheduled-engine-provisioning.yml` | 124 | 26 / 98 | 52 | 101 | 17.883 | 4 paths | 0 / 0 / 0 / 124 |
| `shell-lint.yml` | 446 | 47 / 399 | 94 | 169.5 | 65.967 | unfiltered | 0 / 0 / 446 / 0 |
| `ship-verdict-provenance.yml` | 4 | 0 / 4 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 4 |
| `signature-coverage.yml` | 339 | 46 / 293 | 92 | 130 | 14.567 | not in current source | 0 / 0 / 0 / 339 |
| `signature-doc-siting.yml` | 339 | 47 / 292 | 141 | 93 | 103.2 | not in current source | 0 / 0 / 0 / 339 |
| `skill-quality.yml` | 342 | 37 / 305 | 37 | 54 | 263.983 | 45 paths | 0 / 0 / 0 / 342 |
| `skill-registry-autofix.yml` | 5 | 0 / 5 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 5 |
| `skill-registry-coherence.yml` | 172 | 10 / 162 | 20 | 187 | 24.933 | 13 paths | 0 / 0 / 0 / 172 |
| `skill-roots-selfcheck.yml` | 171 | 10 / 161 | 10 | 164 | 1.067 | 10 paths | 0 / 0 / 0 / 171 |
| `skill-union-bundle.yml` | 119 | 12 / 107 | 12 | 198.5 | 6.75 | 10 paths | 0 / 0 / 0 / 119 |
| `skill-union-selftest.yml` | 122 | 12 / 110 | 12 | 65 | 3.583 | 8 paths | 0 / 0 / 0 / 122 |
| `skill-view-parity.yml` | 163 | 10 / 153 | 20 | 197 | 2.567 | 10 paths | 0 / 0 / 0 / 163 |
| `skill-view.yml` | 163 | 10 / 153 | 20 | 278.5 | 2.3 | 10 paths | 0 / 0 / 0 / 163 |
| `skillmirror-freshness.yml` | 4 | 0 / 4 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 4 |
| `skillmirror-redrive.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 7 paths | 0 / 0 / 0 / 3 |
| `source-coherence.yml` | 38 | 0 / 38 | 0 | Unknown | Unknown | 6 paths | 0 / 0 / 0 / 38 |
| `sparse-checkout-closure.yml` | 172 | 21 / 151 | 21 | 125 | 9.15 | 5 paths | 0 / 0 / 0 / 172 |
| `surface-impact-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 3 |
| `sync-build-config-selftest.yml` | 43 | 4 / 39 | 4 | 89 | 0.817 | 8 paths | 0 / 0 / 0 / 43 |
| `telemetry-collector-container-qualification.yml` | 4 | 0 / 4 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 4 |
| `telemetry-dashboard.yml` | 24 | 4 / 20 | 12 | 22 | 3.267 | 18 paths | 0 / 0 / 0 / 24 |
| `telemetry-git-safety.yml` | 339 | 47 / 292 | 47 | 95.5 | 6.9 | not in current source | 0 / 0 / 0 / 339 |
| `telemetry-host-package.yml` | 101 | 26 / 75 | 26 | 12 | 302.183 | 45 paths | 0 / 0 / 0 / 101 |
| `test-selector-selftest.yml` | 279 | 32 / 247 | 32 | 62.5 | 24.083 | 3 paths | 0 / 0 / 0 / 279 |
| `timeout-coherence.yml` | 172 | 21 / 151 | 42 | 92 | 15.467 | 3 paths | 0 / 0 / 0 / 172 |
| `touch-set-drift-selftest.yml` | 7 | 0 / 7 | 0 | Unknown | Unknown | 4 paths | 0 / 0 / 0 / 7 |
| `touch-set-drift.yml` | 446 | 48 / 398 | 48 | 78 | 72.267 | unfiltered | 0 / 0 / 446 / 0 |
| `update-fsgg-coord-cli-selftest.yml` | 7 | 0 / 7 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 7 |
| `usage-census-selftest.yml` | 3 | 0 / 3 | 0 | Unknown | Unknown | 3 paths | 0 / 0 / 0 / 3 |
| `v1-writer-census-advisory-selftest.yml` | 1 | 0 / 1 | 0 | Unknown | Unknown | 5 paths | 0 / 0 / 0 / 1 |
| `v1-writer-census-advisory.yml` | 113 | 47 / 66 | 47 | 91 | 6.783 | not in current source | 0 / 0 / 0 / 113 |
| `wizard-graph-seal.yml` | 265 | 47 / 218 | 47 | 131 | 20.133 | not in current source | 0 / 0 / 0 / 265 |
| `work-programme.yml` | 43 | 8 / 35 | 8 | 110 | 6.15 | 8 paths | 0 / 0 / 0 / 43 |
| `worker-id-attractor.yml` | 396 | 43 / 353 | 86 | 73.5 | 39.833 | 9 paths | 2 / 0 / 0 / 394 |
