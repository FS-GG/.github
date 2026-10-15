# V2-DIAG-01: Collect independent failures within bounded execution

Date: 2026-10-06. Reviewed: 2026-10-15.
Status: ordinary runner and result retention delivered; historical packaged native pilot qualified;
SDD guidance carrier published; selected release, native receiver refresh and evaluation remain open.
Design: [diagnostic execution](../designs/diagnostic-execution.md).
Parent: [V2 roadmap](../github-substrate-v2-roadmap.md#v2-diag-01--diagnostic-execution--2026-10-05).

Deliver a shared execution default that learns more from each attempt without continuing through
invalid prerequisites or weakening qualification. This extends V2-PREFLIGHT-01's preparation and
adapter work; it does not replace that selected item, reopen accepted V2 scope or interrupt already
admitted operations. Source preparation can proceed independently. Runtime integration depends on
the relevant shared contracts and each adapter's current admission.

## Delivery sequence

- [x] **V2-DIAG-01.1 — Shared policy and existing-contract assessment.** `.github` owns the permanent
  policy and links from `work-roadmap`, `work-programme` and `pipeline-preflight`, including applicable
  installed skill variants. Coordination and runner owners inspect existing prerequisite/result
  contracts and identify reusable dependency and outcome semantics. Record exact implementation
  surfaces and a bounded pilot plan in the existing work artifacts. Acceptance: guidance defines
  independent continuation, dependency blocking, shared-state invalidation, hard stops and honest
  partial results without adding mandatory diagnostic runs or approval ceremony.
- [x] **V2-DIAG-01.2 — Aggregate preparation and bounded result collection.** Coordination owns reusable
  changes where appropriate; adapters retain product behavior. Integrate with V2-PREFLIGHT-01 rather
  than adding a second readiness mechanism. Acceptance: the actual assembled entry point reports at
  least two independent prerequisite defects in one attempt, blocks their dependent effects, and
  distinguishes failed, blocked, unknown and budget-limited checks. Preserve the first causal failure,
  additional findings, candidate identity and cleanup result under existing time/output/resource bounds.
- [x] **V2-DIAG-01.3 — Qualify ordinary and native continuation boundaries.** Select one ordinary
  test/CI runner and one bounded native runner with their owners. Keep each pilot's scope explicit;
  BAR, SC2 or LEARN are candidates, not implicitly admitted executions. Acceptance controls cover
  two independent assertion failures collected together, shared-state contamination, unknown
  prerequisites, budget exhaustion, reporting failure and cleanup failure. Demonstrate that custody,
  authority or accounting uncertainty stops affected work and cannot qualify a result. Include a
  successful complete run, using real runner entry points and scoped fault injection. Retain failed
  evidence; never inject faults into a production effect without its applicable authority.
- [ ] **V2-DIAG-01.4 — Publish, adopt and evaluate.** Use existing coherent release routes for affected
  packaged contracts and skills, then verify the selected installed receivers actually use them.
  Record source, publication, installation and native results separately. Compare observed defect
  collection, attempts, overhead and infrastructure failures over a bounded pilot window; missing
  baseline data remains unknown. Acceptance: both pilot owners verify adopted behavior and remaining
  limits, required qualification stays intact, and the V2 roadmap records the actual outcome.

## V2-DIAG-01.1 source window — 2026-10-05

The permanent [execution policy](../coordination/diagnostic-execution.md) and equivalent `.agents`
and `.claude` `work-roadmap`, `work-programme` and `pipeline-preflight` source variants implement
the guidance portion of this item. Focused routine-route, byte-equality and local-link controls
passed. The source assessment below selects bounded pilots; no runtime entry point, installed
skill receiver or native runner was qualified by that guidance-only window. Guidance and assessment
landed in [PR #4251](https://github.com/FS-GG/.github/pull/4251), merge
[`f0b6fe1`](https://github.com/FS-GG/.github/commit/f0b6fe1583d104e4b5a253ef80bc59902da3836a).
Root read back the merge on 2026-10-05 after 52 checks passed and two skipped. This closes .1;
installed receiver refresh remains separate under .4.

### Existing contracts and bounded pilots

Read-only assessment observed Coordination at `8861ef868805346776b67155676d8ccee2b7d260`.
[`PreparedAttempt.fs`](https://github.com/FS-GG/FS.GG.Coordination/blob/b6c7ef23b5a45bf62b0b7386132d9bcb0c29a386/src/FS.GG.Coordination.Orchestration.Execution/PreparedAttempt.fs)
is already implemented through [merge #928](https://github.com/FS-GG/FS.GG.Coordination/pull/928).
Reuse its private prepared capability, command/closure identity, custody, observation and artifact
reducers, process lease and bounded cleanup. Its preparation loop stops after the first failed
check; `Result<PreparedAttempt,string>` cannot retain independent outcomes, and an unobserved
cleanup replaces the original checker failure. The optional CLI prerequisite source can fall back
to reviewed-contract preparation when absent; that fallback does not establish capsule discovery
or product-adapter adoption.

The assessed Coordination source window extends those existing surfaces with bounded detailed results
and explicit dependency/isolation declarations keyed by check IDs. Preserve current public record
construction and `prepareAsync` compatibility, keeping the genuine prepared capability as the sole
readiness authority. Missing dependency declarations retain conservative behavior. Start with
`PreparedAttempt.fs` and focused existing tests; expand executor/CLI or canonical Protocol/model
partitions only for demonstrated caller/result joins. Qualify aggregate defects, dependent blocking,
first-cause retention and original cumulative bounds through actual entry points and existing replay.

The selected ordinary pilot is [`tests/work-programme/run.sh`](../../tests/work-programme/run.sh),
called by its [workflow](../../.github/workflows/work-programme.yml). At assessment it stopped at the
first suite failure; existing Python unittest cases already collected independent assertions. The
ordinary source window below implements serial continuation for declared independent suites with
the original job timeout and bounded local deadline. Setup or shared-input failure blocks dependents. The selected native
pilot is Coordination's existing
[`eng/prepared-attempt-installed-qualification.fsx`](https://github.com/FS-GG/FS.GG.Coordination/blob/8861ef868805346776b67155676d8ccee2b7d260/eng/prepared-attempt-installed-qualification.fsx),
through `eng/run-packaged-portable-workspace-qualification.py` against an exact installed Execution
DLL. Its synthetic assembled capsule exercises real checker subprocesses, custody, accounting,
termination and cleanup; it is not a product workload. Use these entry points for the .2/.3 controls,
including complete success, independent failures, contamination, unknown prerequisites, exhausted
bounds and reporting/cleanup failure. Faults belong in disposable fixtures under applicable admission.

BAR, SC2 and LEARN remain deferred product adapters and are not admitted pilots. BAR/SC2 checked-route
adoption remains a separate V2-PREFLIGHT obligation. At assessment, the native pilot required fresh package/source/tool
identity, installed-status and host/resource readback before root admission. The later local package
qualification below records its exact scope. Existing coherent release
and receiver refresh routes own publication and installed adoption; source twins or a package alone
prove neither. This assessment ran no CLR, native qualification or fault injection and establishes no
measured savings. Guidance and assessment are merged as recorded above. The later pilot evaluation below records
.2/.3 acceptance; coherent release and installed receiver adoption remain open under .4.

## Ordinary pilot source and actual entry — 2026-10-05

The existing [work-programme entry](../../tests/work-programme/run.sh) now collects explicitly
independent suites serially, checks declared input identities before and after each suite, and
blocks failed or unknown prerequisites and contaminated shared inputs. It retains first cause,
additional failures, actual exits, cleanup/reporting and omitted coverage under one 170-second
local deadline, a five-second cleanup reserve and a cumulative 1 MiB output cap. The hosted
three-minute timeout and existing collector unittest behavior remain unchanged. Settlement retains
the unreaped child leader while observing its group, then reaps last; it never signals a numeric
group after reaping.

Eleven disposable [runner controls](../../tests/work-programme/test_run.py) cover independent
assertions, dependency and unknown blocking, shared contamination, deadline/output exhaustion,
reporter failure and scratch/process cleanup uncertainty. Root qualified the complete actual entry
at source `36430e2e7e10f1bfcf23cd3ea6adcd0afec48b60`: all three FSI suites, ten collector controls and
eleven runner controls passed. Exit, all cleanup and reporting succeeded; no coverage was omitted.
The observed outer duration was 9.728 seconds, with no resource failure under sampled limits.
The four runner source files remain byte-identical after rebasing onto the guidance merge.

The ordinary source landed through [PR #4252](https://github.com/FS-GG/.github/pull/4252), merge
[`4d33398`](https://github.com/FS-GG/.github/commit/4d333986d7d20a664ad447e49c79841c75e507e9).
The complete exact-head [hosted entry](https://github.com/FS-GG/.github/actions/runs/37360993148)
passed all five suites; its retained input closure matches the merged source. Its runner duration
was 39.908 seconds, including 1.445 seconds for the eleven runner controls. This proves the delivered
ordinary entry, with no claim of a separate post-merge hosted run. Repository skill twins were
verified equal; separate execution of the other twin and packaged receiver adoption remain pending.

## Local packaged native pilot and bounded evaluation — 2026-10-06

Coordination's preparation changes landed through
[PR #943](https://github.com/FS-GG/FS.GG.Coordination/pull/943), merge
[`065f6b1`](https://github.com/FS-GG/FS.GG.Coordination/commit/065f6b1e462e299fc85a9ff0a125c4910bbe1ac4),
after required hosted and coherent validation. Four separately qualified local test selections
passed 35, 64, 2 and 2 cases: 103 selected tests in total. They are separate from the ordinary
complete entry and the installed observations below, and do not substitute for whole-suite gates.

The existing packaged qualification entry exercised a genuine local package from source `54b10c4f`
with the independently pinned qualification-script correction prepared at `4445feea`. This was a
synthetic capsule with real checker subprocesses, held custody, accounting and cleanup. The actual
successful attempt exited 0 in 4.441 seconds, with sampled peaks of 440,401,920 RSS bytes, three
owned processes and one CLR process; settlement completed and no resource failure was reported.
Its evidence contains **21 observation records**, including complete success and ten diagnostic
negative groups. These records are not 21 additional source tests.

One independent-failure observation retained both missing-import defects in one attempt:
`imports` and `discovery` each exited 1 with cleanup observed, the first cause stayed unchanged,
the second finding remained additional, and no prepared capability was issued. The complete-success
observation passed both checks and produced readiness. Other controls covered dependency and
unknown blocking, shared-state contamination, exhausted bounds, reporting failure and cleanup
uncertainty. In the output-bound case, malformed observation remained the first cause, output-budget
refusal was recorded separately, and the next checker was blocked with no exit because it was never
launched. Cleanup uncertainty also preserved its first cause and stopped the next checker.

An earlier installed attempt exited 2 with `output-bound-unreported`. Its custody settled without
resource failure, but it remains failed; the un-emitted detailed output branch remains unknown.
The later independently qualified success does not rewrite that failure.

| Bounded evaluation | Observed result and limit |
|---|---|
| Controlled defect collection | Ordinary fixtures collected two independent assertion failures in one invocation; native fixtures retained two independent prerequisite defects in one attempt. These are injected defects, not natural production yield. |
| Complete ordinary entry | Local outer 9.728 seconds; hosted runner 39.908 seconds. Both passed all five suites and found no natural assertion or infrastructure failures. Local sampled peaks were 414,289,920 RSS bytes, five owned processes and two CLR processes. |
| Separate local source qualification | Four selected filters passed 103 tests. The final two-case window took 4.534 seconds with sampled peaks of 391,192,576 RSS bytes, six owned processes and three CLR processes; custody was clean and resource failure false. |
| Local packaged native pilot | One earlier failed attempt and one later successful 4.441-second attempt are retained separately. Success qualifies the local package and independently pinned script only. |
| Baseline and savings | No comparable before/after baseline, avoided-attempt count, total diagnostic overhead or complete model-usage coverage is established. Timing differences across environments do not measure savings. |

The canonical Coordination `0.2.1` release, fresh V2-LANG P4 package/provider/facts/grant joins,
coherent publication and selected installed receiver adoption remain pending. The local package
qualification does not establish canonical distribution or product adoption. The later Drivers
source in [PR #4254](https://github.com/FS-GG/.github/pull/4254) remains blocked by the genuine Contracts
source/feed coherence gate: the retained source frontier was `7.6.0`, while the registry/public feed
was `7.5.2`; organization-feed presence was unreadable. Publication proof must precede a registry
update and fresh source qualification. The original ordinary PR4252 acceptance stays scoped and valid.
This closes .2 and .3 for the selected ordinary runner and local packaged native pilot. The
aggregate result, independent-failure, dependency/unknown/shared-state, cumulative-bound,
reporting/cleanup and successful-consumption controls meet their stated acceptance. The separately
qualified executor controls refuse foreign or stale authority and oversized custody evidence;
earlier raw-exit/accounting proof refusals remain unqualified. Uncertainty never creates readiness
or a successful qualification. Canonical release, distribution and installed receiver adoption
belong to .4, which remains open. BAR, SC2 and LEARN are not admitted by these local results.

## Canonical candidate and provider diagnostic source — 2026-10-06

[Coordination #945](https://github.com/FS-GG/FS.GG.Coordination/pull/945) merged the
protected helper and dormant publisher binding at `fab17697d8eddfebc890cb05d8f6c768604c6e5f`.
The canonical candidate remains producer `bc55a3d1cc887d653c1bb1198e4823b24f47aaa8`:
[preparation 37403093489](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/37403093489)
passed with two independent package preparations agreeing. Its genuine installed diagnostic
entry passed 21 observation records in 5.226 seconds with clean custody and no sampled resource
failure. This qualifies that canonical candidate, not publication or product adoption.

[Sandbox #56](https://github.com/FS-GG/FS.GG.GitHub.Substrate.Sandbox/pull/56) merged at
`b1186d37c23a2fe9502a5725c47d9776a377fc8f`. Bounded diagnostics now retain provider collection
failure and cleanup observations without replacing the original outcome or relaxing writer
settlement. The exact candidate passed 59 pure controls and 87 offline compiler/caller tests;
the accepted wrapper completed in 17.492 seconds with owned processes settled. The earlier
adapter-pin refusal and diagnostic-output report-framing failure remain separate evidence.
This dispatch-only repository has no PR/push coherent workflow; its source qualification does
not substitute for the later hosted provider control.

Fresh [provider staging 37410297466](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/37410297466)
passed at the protected helper source. Intake verified the complete artifact archive and all nine
members; the three provider staging roles exactly matched their prepared inputs. The original failed P4
facts attempt and consumed effects remain unchanged. Fresh candidate facts, private facts/grants,
canonical publication, installed receivers and Drivers distribution remain pending under .4.

## Completion and rollout limits

Completion requires shared guidance plus implemented, qualified and adopted behavior in both selected
pilot classes. A merged design, new result type, passing synthetic test or published package alone
is insufficient. Broader rollout remains incremental, prioritizing repeated low-information failures;
there is no immediate fleet-wide retrofit or new central diagnostic service.

After recurring first-failure repairs, use the design's bounded composition review to choose between
better preparation, richer independent diagnostics, improved isolation and a justified fresh attempt.
Do not respond automatically by enlarging limits or suppressing the check that found the problem.
Required coherent validation, protected operation admission and original deadline/cleanup contracts
remain unchanged. Actual savings are an evaluation outcome, not a delivery prerequisite or promise.

## P4 provider failure diagnosis — 2026-10-06

The exact facts attempt `37420966628/2` failed during capability collection: Podman
`info` exited 125 and the collector returned 2. Cleanup separately refused because
container absence could not be established, although account processes settled.
Root verified the original 697-byte result artifact `11394316361`, SHA256
`b5e168aac0a36d7b800278d21ad43cc5a65e44990b40b39da82ce64579542baf`.
It reports no accepted facts, sealed capsule or completed writer cleanup. The owned
temporary admission was separately deleted and its absence read back; that does not
establish provider cleanup. The underlying Podman cause remains unknown.

[Sandbox #57](https://github.com/FS-GG/FS.GG.GitHub.Substrate.Sandbox/pull/57) delivers
bounded classification of already captured stderr at
`d22ff4dfefd21b64ce12b83915b4b2e74065b1a5`. Its four-file source change passed 53 local
controls, preserving the accepted cleanup fixture, first cause and refusal behavior
when classification or reporting fails. Hosted PR checks are absent. This is a
source-only diagnostic improvement: a fresh admitted attempt, provider acceptance,
publication and installed adoption remain open. Historical attempts are not reset.


The protected Sandbox diagnostic source subsequently passed all 93 selected source/runtime
cases with zero failures, errors or skips. The root-owned 120/128-second operation completed
with clean settlement, no sampled resource failure, a 436,776,960-byte observed owned RSS peak,
four owned processes and at most two task CLR processes. These are sampled observations, not
instantaneous hard limits. The original failed provider attempt and its unknown cleanup remain
unchanged. This accepts fixture/caller behavior only; fresh provider admission and qualification
are still required.

## SDD guidance carrier publication — 2026-10-06

[SDD #1088](https://github.com/FS-GG/FS.GG.SDD/pull/1088) merged at
`cf2f046a10497a336d6243c9a314c3f91fb15771`, carrying Drivers 0.98.0 diagnostic guidance.
Contracts 7.6.0 and coherent CLI, Artifacts and Knowledge 2.2.0 were published from the original
four archives retained by [preparation 37536203179/1](https://github.com/FS-GG/FS.GG.SDD/actions/runs/37536203179).
[Promotion 37538040787/2](https://github.com/FS-GG/FS.GG.SDD/actions/runs/37538040787/attempts/2)
passed literal dual-feed payload readback, public Knowledge and clean installation gates, and
58 Q2/Q3 receiver cases. Independent intake of receipt `11448064295`, archive SHA256
`e255c688a2d4a33771578563cda6a720ef669c0061c32a3f1fbcfe11d657bf55`, matched all eight
feed archives to the original candidates. The first attempt's public CLI discovery failure remains
historical; the receipt harness's placeholder-name refusal was corrected using the official script's
exact public-package substitution and reverified against the same retained archive.

The earlier source-local 26 tests and 16-member, two-root scaffold acceptance remain scoped evidence.
Selected installed diagnostic-guidance use and bounded evaluation remain open under .4; public package
installation gates do not establish that adoption. Kit #1090 and tool #1087 remain separate receivers.
Canonical Coordination publication, pilot adoption and unavailable baseline usage remain open;
no measured savings, automatic guidance reload or `.claude` execution is inferred.

## Ordinary result retention and remaining adoption — 2026-10-15

[PR #4530](https://github.com/FS-GG/.github/pull/4530), merge
[`3f0cdba`](https://github.com/FS-GG/.github/commit/3f0cdba10ffc726cf99546ea23a2c8e2e4e8f431),
adds result retention to the selected [ordinary workflow](../../.github/workflows/work-programme.yml).
The maintained runner writes its existing JSON report to a result file and keeps its own step exit.
Independent `always()` steps display and upload that file; missing-file upload fails. Display or upload
failure cannot suppress the other step. The original 170-second runner deadline, five-second cleanup
reserve, 1 MiB output bound and three-minute job timeout remain unchanged. A disposable shell control
retained its synthetic first cause while preserving exit 7; it is separate from hosted acceptance.

The actual [hosted entry, run 38718508221/1](https://github.com/FS-GG/.github/actions/runs/38718508221)
passed at qualified head `774c90751f8d17ec5740a1b52ef7d0b6becd8aff`. All nine suites passed in
13.899 seconds with 10,547 output bytes, no first cause or omitted coverage, and successful cleanup
and reporting. Actions display and upload steps passed separately. Independent download of artifact
`11964595788` matched its API archive SHA256
`7447ddf138405663254d0e6eb8a05e8bcae53935d693a5d5994c7af59c5af79b`;
the sole JSON member has SHA256
`fa62a6512798c2b51f5c28e48234024733f572314669774e6b91d8ab37a20d90`.
All 31 reported input hashes matched the exact Git source. This accepts the selected ordinary entry
and artifact acquisition at that head, without claiming a separate post-merge execution or native adoption.

The preceding [hosted observation 38682224377](https://github.com/FS-GG/.github/actions/runs/38682224377),
at `e71a23190a90bc93dc29af371cc79004d820c4c9`, passed the same nine suites with identical 31 input pins
in 23.179 seconds. Both observations contain zero natural suite failures. These two runs provide a
descriptive comparison only: environment variability, defect yield, avoided attempts, total diagnostic
overhead and complete usage coverage are unmeasured. Upload timestamps span one second at the native
timestamp resolution; that does not establish exact overhead. No savings are inferred.

### Both repository-native receiver roots — 2026-10-15

[PR #4539](https://github.com/FS-GG/.github/pull/4539), protected merge
`505c54cf630c33996d3fd7cafd68e7ab2496f019`, preserves the four `.agents` FSI suites and adds the
four `.claude` counterparts through the exact receiver selector and input closure. Five Python suites
and the original deadline, cleanup reserve and output bounds remain unchanged. The
[actual hosted entry 38729032134/1](https://github.com/FS-GG/.github/actions/runs/38729032134)
passed all thirteen suites at exact candidate `b8a536f36ad9f3311ba86b63fafb8d45f0721152`.
Independent download of artifact `11969673049` matched ZIP SHA256
`d2611794ba97f0d19bce3c3c67be9b0df186e79ad8c1a5fb02f143097d6a0eb2`; its sole JSON has SHA256
`e6c00e2251019e4413f6f24f14b06f958342e088f12f61d37b0dc2c9fa03b505`.
All 32 source pins matched; first cause was null, omitted coverage empty, and all exits, cleanup and
reporting passed. Runner, display and upload steps succeeded separately. Observed 47.256 seconds and
20,556 output bytes describe this run; they establish no overhead reduction or savings. This accepts
actual repository-native receiver use at that head, without claiming a separate post-merge run,
package installation or native custody. V2-DIAG-01.4 remains open for the owner joins and evaluation
below; the failed intake read and unconfirmed supplying owner remain unchanged.

### Protected-main ordinary readback — 2026-10-15

The [post-merge work-programme run 38730201243/1](https://github.com/FS-GG/.github/actions/runs/38730201243)
passed on protected `505c54cf630c33996d3fd7cafd68e7ab2496f019`. Independent readback of
artifact `11968989527` found one JSON result, SHA-256
`3d9edd293ff290b72dd22639a72881ac81b7b42310f0dbd57d7697c6eb5fbf0a`.
All 32 input hashes matched that protected source. All thirteen `.agents`, `.claude` and Python
suites passed with exit 0 and successful cleanup; the first cause was null, omitted coverage was
empty, and reporting and the hosted artifact upload passed. The runner recorded 39.781 seconds
and 20,556 output bytes. This confirms the selected repository-native ordinary entry after merge;
it supplies no natural failure yield, comparative overhead, packaged installation or native result.

### Publication and native owner joins still required

[Coordination CLI 0.3.0](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.3.0) is a published
CLI-only successor from producer `8eef1ab7f205132553632e03dc650e7ecdd03679`, publisher
`401a89e3e63c0eefda02124475e92d0c4b752cfd`. Independent public acquisition matched original archive
`a8cd6d602e1203257e1241df0b5dfdb9d867334b46dc406d8cdaa8e6d2b3019c` and the normalized nuget.org
payload. This is separate from the selected 0.2.1 package/image release and P4 joins in the
[Coordination owner plan](https://github.com/FS-GG/FS.GG.Coordination/blob/38da024a8dbc7f65a091fcc9a4a81a383f327e63/docs/roadmaps/v2-lang-portable-integration.md#diag-021-canonical-preparation-and-dormant-source-amendment).
The diagnostic preparation API and both installed qualification scripts are unchanged between those
producers, but Execution custody/bootstrap/project sources changed. Historical native qualification
cannot transfer to 0.3.0 from those selected equal files or the version number.

The programme root and Coordination diagnostic owner must supply and admit the exact enforcing outer
profile, original total/resource/cleanup bounds and custody/settlement evidence before a new packaged
native attempt. The existing package rewriter and qualifier inputs are prepared; no new native attempt
was executed in this window. Sampled process observations do not establish hard containment.

The P4 owner must select and record a supported current qualification route with its enforcing profile,
candidate/receiver custody and fresh receiver-bound grant. Historical or retired dispatch identities
remain unavailable as current authority; this update revives no route. Publisher credential exchange,
administrator provider enrollment, private-facts custody and installed receiver proof are distinct
owner joins. Missing credentials have not been established as their common blocker. The separate
general Python provider pack does not own this route decision. Publication and effects retain their
existing admission boundaries; original failed attempts and unresolved cleanup remain unchanged.
V2-DIAG-01.4 stays open for these joins, both owners' installed assertions and bounded evaluation.

### Exact inputs and next receiver gates

| Selected route | Accepted input or observation | Next accountable gate |
|---|---|---|
| Ordinary `.github` work-programme | Protected `505c54cf` and the result above; thirteen actual suites across both repository-native skill roots. | `.github` programme owner retains original attempts and defines a bounded evaluation window. Source use is accepted; package or fleet adoption is a separate claim. |
| Coordination canonical 0.2.1 | Producer `bc55a3d1cc887d653c1bb1198e4823b24f47aaa8`, preparation [37403093489/1](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/37403093489), artifact `11386361825`, and the historical 21-record installed diagnostic candidate result. The selected native entry is `eng/prepared-attempt-installed-qualification.fsx` through the packaged qualifier against the exact Execution DLL. | Coordination P4 release/diagnostic maintainer and supplying host, administrator grant and private-custody owners must join an enforcing outer profile, original package/image and receiver identity, a fresh receiver-bound grant, and observed termination/cleanup before native acceptance and coherent publication. Candidate qualification is not release or adoption. |
| Coordination CLI 0.3.0 | [Published CLI-only release](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.3.0), separate from canonical 0.2.1. | Coordination diagnostic owner must admit and qualify its own installed native route; equal diagnostic script/API bytes do not transfer changed Execution custody, bootstrap or project behavior. |
| Diagnostic skill carrier and receivers | Drivers 0.98.0 guidance and the SDD 2.2.0 carrier were published. The [read-only .github feed probe 38732551619/1](https://github.com/FS-GG/.github/actions/runs/38732551619) found 0.102.0 latest and prospective 0.104.0 absent on all six CLI/Kit/Drivers feed coordinates. | `.github` release owner must resolve original 0.103.0 `tag:intent`/unknown dispatch before any 0.104.0 publisher preflight or credential exchange; the .104 proposal is source only. SDD, Kit and tool receiver owners must separately prove selected installed diagnostic-guidance use. Feed freshness is neither publication nor receiver use. |

The [ordinary Atlas boundary](../atlas/records/5c3527e43fe24c5aa171c6fa4a3de7fe5e78f2527aed0dd19cb21d154d43da21.json)
and [closed 0.104 release boundary](../atlas/records/d31e8aa21d1498f2d4a07c4cc89ea6f5a6d3ce54d334dbff230e0f55db35c85e.json)
retain the reusable source/authority distinctions. This matrix records current observations, not
a new authorization or an assumption that the two release routes share a blocker.

### Prospective bounded evaluation

Before each owner starts a treated pilot window, record its start and end rule, exact source/package
and installed receiver identities, eligible original attempts and acceptance checks in the existing
owner evidence. For each attempt retain first cause, independent useful findings, dependent blocking,
time to first actionable diagnosis, total and active execution time when measured, infrastructure
failure, and separate reporting and cleanup outcomes. Count repeat attempts against the declared
eligible population and preserve failed or partial attempts. Record an unavailable or incomparable
baseline as **unknown**; the historical ordinary and local native timings above are descriptive and
cannot establish saved attempts, faster diagnosis or a process benefit. Do not enroll a historical
attempt retrospectively as a treated cohort or weaken required qualification to fill a denominator.

### Route-selection request delivery is blocked

At the root-reported 2026-10-15 10:54:22 UTC cutoff, typed validation of the public Coordination
route-selection draft succeeded with `kind=validated,writes=0`. Its subsequent `intake apply`
refused while reading the Coordination room: production v1 admission was unavailable for durable
operation scope, protected authority, the existing admission journal and provider reconciliation.
This is a **failed read**, not an empty room or a delivered request. Root reports no issue or board
write and no surviving CLR process; its later exact-ID/title issue search found no created issue.
Board state remains unverified. The transcript is root-reported evidence, not an independently
retained result artifact.

The [production client fence](../../src/FS.GG.Coord.Cli/Client.fs) preserves that refusal;
draft validation cannot supply installed mutation authority. The programme root owns routing this
blocker to the `.github` coordination-engine maintainer and the installed admission authority owner.
The latter supplying owner is not yet confirmed, and no alternative authorized public delivery
channel has been established. Preserve the draft until that owner identifies and admits the existing
supported intake route; do not replay apply or bypass it with direct issue creation. This request
delivery blocker does not stop independent ordinary evidence reconciliation or establish native
qualification, provider grant, publication or adoption authority.
