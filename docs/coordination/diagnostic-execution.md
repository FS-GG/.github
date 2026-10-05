# Collect independent failures within bounded execution

Collect actionable independent findings in one attempt while prerequisites, shared state and
execution safeguards remain valid. This permanent shared policy applies to preparation, local
commands, tests, CI and native qualification. It implements the
[agreed design](../designs/diagnostic-execution.md); the
[V2-DIAG-01 roadmap](../roadmaps/diagnostic-execution.md) tracks runtime implementation and adoption.

## Prepare the actual candidate

Evaluate all independently checkable prerequisites against the real assembled command and its
transitive inputs: required files, imports, configuration, profile compatibility, artifact
bindings, output placement and test discovery. Collect independent defects rather than stopping
at the first finding. Name each failed or unknown prerequisite and block its dependent effects.
Do not launch work merely to discover a defect already visible during preparation. Revalidate
mutable facts immediately before the dependent effect, using the existing
[typed prerequisite admission](../github-substrate-v2-roadmap.md#v2-preflight-01--typed-prerequisite-admission-next-item-2026-10-03)
contracts where applicable.

An unknown dependency is not evidence of independence. A check whose inputs require another
failed check must be reported as blocked; it cannot be counted as passed or silently omitted.

## Continue only valid independent work

Follow the runner's declared dependencies and shared-state boundaries. Continue an independent
case after another case fails only while its own prerequisites, isolation, process ownership,
custody and resource accounting are known valid. Serial collection is sufficient; this policy
does not require parallel execution. Retain the original deadline and time, output, observation
and resource budgets, including cleanup obligations.

| Finding | Required behavior |
|---|---|
| Independent assertion or prerequisite failure | Retain the finding; continue other valid independent checks within the original budget |
| Failed or unknown prerequisite or dependency | Block affected dependent work and name the reason |
| Shared-state contamination | Stop every case relying on that state; continue only demonstrably unaffected checks |
| Authority, process ownership, custody, accounting or cleanup uncertainty | Stop affected execution and follow the existing bounded cleanup path |
| Deadline or collection budget exhausted | Stop collection and report unexecuted or truncated coverage |

Diagnostic mode is optional when it can collect more evidence than strict qualification within
these boundaries. It is not another mandatory run. Diagnostic intent supplies no operation
authority: preserve existing admission, loader allowlists, root review, one-use effects and
cleanup contracts. Do not count unknown processes as zero usage, renew a deadline, repeat a
consumed effect or turn a failed run into qualification. Read-only inspection after failure
requires valid evidence access and must remain within the original applicable budgets.

## Preserve causes and partial outcomes

Retain the first causal failure and additional independent findings with their stage, exact
candidate identity and evidence references. Distinguish passed, failed, prerequisite-blocked,
unknown and not-run-because-of-a-bound checks. Bound collection size and state omitted coverage.
Preserve the actual exit, cleanup and diagnostic/reporting outcomes separately. A late reporting
or diagnostic failure must not replace the original cause or suppress cleanup. Cancellation
requested is not termination observed; unknown effects require observation under their original
identity before retry.

Qualification remains failed or incomplete if any required check failed, was blocked, was not
run or is unknown. Independent successes describe only their checked scope. Do not combine
partial failed runs into passing qualification. Preserve existing semantic and exact-candidate
reuse rules and [ADR-0084 coherent validation](../adr/0084-semantic-reuse-never-cancels-coherent-validation.md),
including the fence on dependent acceptance or activation after a late failure.

## Review repeated low-information attempts

Repeated first-failure repair cycles trigger a bounded composition review before another costly
attempt. Inspect the assembled entry point, transitive inputs and dependent stages. Choose focused
controls for the demonstrated gap: better preparation, richer independent findings or improved
isolation. Record the choice and bounds in the existing work artifact, using
[proportionate preflight](../adr/0086-proportionate-pipeline-preflight.md) and the existing
[qualification cadence](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md).
Prefer existing checks. Add no approval gate, per-run form, service, separate readiness mechanism
or exhaustive model. Stop expanding optional diagnostics when their cost exceeds plausible benefit;
cost estimates cannot waive required technical checks or operation safeguards.

## Implement and verify adoption at existing boundaries

Coordination owns reusable prerequisite, dependency and result semantics where existing contracts
fit; each runner owner owns actual continuation, isolation and cleanup behavior. Extend those
surfaces rather than introducing a parallel execution framework or copying runner-specific process
supervision into shared guidance. The temporary programme coordinator is a consumer of this policy.
Generated agent guidance remains a projection of its existing sources.

Guidance source delivery, runtime qualification, publication, installed adoption and native acceptance
are separate outcomes. Equivalent `.agents` and `.claude` skill source variants do not prove that an
installed receiver uses them. Verify implementation at real entry points in an explicitly selected
ordinary test/CI pilot and a bounded native pilot, under each owner's current admission. A prose
instruction, synthetic control or package publication alone cannot establish feature completion.

The owning roadmap retains the pilot controls and completion criteria. Measure actionable independent
defects per attempt, attempts to the next useful stage, diagnostic overhead, false blocks and time to
a useful result. Separate infrastructure disturbances from source bugs and retain failed evidence.
Missing baseline or receiver evidence remains unknown; claim savings only from scoped observations.
