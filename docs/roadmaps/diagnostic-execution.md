# V2-DIAG-01: Collect independent failures within bounded execution

Date: 2026-10-06. Status: guidance and ordinary runner landed; local ordinary and packaged native pilots qualified; canonical release and receiver adoption pending.
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
passed at the protected helper source. Artifact intake and exact member comparisons are still
required before assigning its provenance to the private input roles. The original failed P4
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
