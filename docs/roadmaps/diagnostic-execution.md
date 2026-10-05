# V2-DIAG-01: Collect independent failures within bounded execution

Date: 2026-10-05. Status: planned; no implementation or receiver adoption claimed.
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
