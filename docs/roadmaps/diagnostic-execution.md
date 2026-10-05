# V2-DIAG-01: Collect independent failures within bounded execution

Date: 2026-10-05. Status: shared guidance source prepared; runtime implementation and receiver adoption pending.
Design: [diagnostic execution](../designs/diagnostic-execution.md).
Parent: [V2 roadmap](../github-substrate-v2-roadmap.md#v2-diag-01--diagnostic-execution--2026-10-05).

Deliver a shared execution default that learns more from each attempt without continuing through
invalid prerequisites or weakening qualification. This extends V2-PREFLIGHT-01's preparation and
adapter work; it does not replace that selected item, reopen accepted V2 scope or interrupt already
admitted operations. Source preparation can proceed independently. Runtime integration depends on
the relevant shared contracts and each adapter's current admission.

## Delivery sequence

- [ ] **V2-DIAG-01.1 — Shared policy and existing-contract assessment.** `.github` owns the permanent
  policy and links from `work-roadmap`, `work-programme` and `pipeline-preflight`, including applicable
  installed skill variants. Coordination and runner owners inspect existing prerequisite/result
  contracts and identify reusable dependency and outcome semantics. Record exact implementation
  surfaces and a bounded pilot plan in the existing work artifacts. Acceptance: guidance defines
  independent continuation, dependency blocking, shared-state invalidation, hard stops and honest
  partial results without adding mandatory diagnostic runs or approval ceremony.
- [ ] **V2-DIAG-01.2 — Aggregate preparation and bounded result collection.** Coordination owns reusable
  changes where appropriate; adapters retain product behavior. Integrate with V2-PREFLIGHT-01 rather
  than adding a second readiness mechanism. Acceptance: the actual assembled entry point reports at
  least two independent prerequisite defects in one attempt, blocks their dependent effects, and
  distinguishes failed, blocked, unknown and budget-limited checks. Preserve the first causal failure,
  additional findings, candidate identity and cleanup result under existing time/output/resource bounds.
- [ ] **V2-DIAG-01.3 — Qualify ordinary and native continuation boundaries.** Select one ordinary
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
skill receiver or native runner was qualified by this source window. All delivery
checkboxes remain open until their complete acceptance obligations and native source readback hold.

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

The next Coordination source window extends those existing surfaces with bounded detailed results
and explicit dependency/isolation declarations keyed by check IDs. Preserve current public record
construction and `prepareAsync` compatibility, keeping the genuine prepared capability as the sole
readiness authority. Missing dependency declarations retain conservative behavior. Start with
`PreparedAttempt.fs` and focused existing tests; expand executor/CLI or canonical Protocol/model
partitions only for demonstrated caller/result joins. Qualify aggregate defects, dependent blocking,
first-cause retention and original cumulative bounds through actual entry points and existing replay.

The selected ordinary pilot is [`tests/work-programme/run.sh`](../../tests/work-programme/run.sh),
called by its [workflow](../../.github/workflows/work-programme.yml). It currently stops at the first
suite failure; existing Python unittest cases already collect independent assertions. Extend serial
suite continuation only for declared independent suites, retaining the original job timeout and
bounded local deadline. Setup or shared-input failure blocks dependent suites. The selected native
pilot is Coordination's existing
[`eng/prepared-attempt-installed-qualification.fsx`](https://github.com/FS-GG/FS.GG.Coordination/blob/8861ef868805346776b67155676d8ccee2b7d260/eng/prepared-attempt-installed-qualification.fsx),
through `eng/run-packaged-portable-workspace-qualification.py` against an exact installed Execution
DLL. Its synthetic assembled capsule exercises real checker subprocesses, custody, accounting,
termination and cleanup; it is not a product workload. Use these entry points for the .2/.3 controls,
including complete success, independent failures, contamination, unknown prerequisites, exhausted
bounds and reporting/cleanup failure. Faults belong in disposable fixtures under applicable admission.

BAR, SC2 and LEARN remain deferred product adapters and are not admitted pilots. BAR/SC2 checked-route
adoption remains a separate V2-PREFLIGHT obligation. The native pilot requires fresh package/source/tool
identity, installed-status and host/resource readback before root admits it. Existing coherent release
and receiver refresh routes own publication and installed adoption; source twins or a package alone
prove neither. This assessment ran no CLR, native qualification or fault injection and establishes no
measured savings. Guidance and assessment source are covered; .1 completion still awaits exact-head
required checks and native merge readback, and .2–.4 remain open.

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
