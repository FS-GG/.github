# Collect independent failures before repeating qualification

Date: 2026-10-05. Status: agreed design; implementation and adoption pending.
Owner: `.github` for shared guidance; Coordination and runner owners for implementation.
Tracking: [V2-DIAG-01 roadmap](../roadmaps/diagnostic-execution.md).

Repeatedly stopping at the first defect can turn one useful investigation into several costly
repair-and-rerun cycles. The proposed default is to collect independent failures within the
existing execution budget, while stopping work whose prerequisites or execution safeguards no
longer hold. This applies to local commands, tests, CI and native qualification.

The existing [typed prerequisite item](../github-substrate-v2-roadmap.md#v2-preflight-01--typed-prerequisite-admission-next-item-2026-10-03)
prepares the actual assembled command and revalidates mutable facts before effects.
[ADR-0086](../adr/0086-proportionate-pipeline-preflight.md) selects proportionate pipeline checks;
[ADR-0081](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md)
addresses cadence and defect yield. This design extends failure collection within a run. It does
not change those decisions or [coherent qualification and reuse](../adr/0084-semantic-reuse-never-cancels-coherent-validation.md).

## Execution behavior

Preparation evaluates all independently checkable prerequisites against the real assembled
candidate: required files, imports, configuration, profile compatibility, artifact bindings,
output placement and test discovery. It collects actionable findings instead of returning only
the first. It does not execute dependent effects when any required prerequisite is failed or
unknown. Mutable facts still require revalidation immediately before their dependent effect.

During execution, the runner follows declared dependencies and shared-state boundaries. An
independent case may continue after another case fails only while its own prerequisites,
resource accounting, ownership and isolation remain valid. An unknown dependency is not assumed
independent. A failure that contaminates shared state blocks all cases relying on that state.
Safe continuation does not require parallel execution; serial collection is often sufficient.

| Finding | Proposed response |
|---|---|
| Independent assertion or prerequisite failure | Retain the finding; continue other valid independent checks within budget |
| Failed or unknown prerequisite | Block dependent work and name the prerequisite; do not count blocked work as passed |
| Shared state no longer trustworthy | Stop checks that depend on it; retain unaffected findings |
| Authority, process ownership, accounting or cleanup uncertainty | Stop affected execution and use the existing bounded cleanup path |
| Deadline or output/observation budget exhausted | Stop collection; report unexecuted or truncated coverage explicitly |

A separate diagnostic mode is useful when it can collect more evidence than strict qualification
without weakening these boundaries. It is optional, not an additional mandatory run. It requires
its own valid operation admission where applicable; diagnostic intent grants no extra authority.
It cannot broaden loader allowlists, tolerate unknown processes as zero usage, renew deadlines,
repeat consumed effects or turn a failed run into qualification. Read-only post-failure inspection
may continue only within the original applicable budgets and evidence-access rules.

## Results and stopping behavior

Retain the first causal failure and additional independent findings with their stage, candidate
identity and evidence references. Distinguish checks that passed, failed, were blocked by a
prerequisite, were not run because of a bound, or returned an unknown result. Preserve the actual
exit and cleanup outcomes separately. A late diagnostic or reporting failure must not overwrite
the original cause or suppress cleanup. Bound collection size and report omitted coverage.

Qualification remains failed or incomplete if any required check failed, was blocked, was not
run or is unknown. Independent successes are scoped observations, not permission to combine
partial failed runs into a passing qualification. Any reuse follows the existing semantic and
exact-candidate rules. Publication, installation and native acceptance remain distinct.

Repeated first-failure repair cycles should trigger a bounded composition review before another
costly attempt. Inspect the assembled entry point, transitive inputs and dependent stages; add
focused controls for the demonstrated gap. Record the choice in the existing work artifact.
Do not impose a new approval, service, per-run form or exhaustive model. Prefer existing checks,
and stop expanding the diagnostic machinery when its cost exceeds its plausible benefit.

## Shared placement and adoption

The permanent policy belongs in `.github` shared execution guidance, linked from `work-roadmap`,
`work-programme` and `pipeline-preflight`, with equivalent installed skill variants kept coherent.
Do not make the temporary programme coordinator the sole owner. Generated `AGENTS.md` remains a
projection of its existing sources, not a second policy authority.

Coordination owns reusable result and dependency semantics where existing contracts fit. Each
runner owner owns its actual continuation boundaries, cleanup behavior and integration. Reuse
those contracts rather than creating a parallel execution framework or copying product-specific
process supervision into central guidance. Implementations must demonstrate behavior at the real
entry points; a prose instruction or synthetic model alone does not establish adoption.

Pilot selection should include an ordinary test/CI runner that can safely collect multiple failures
and a bounded native runner that must stop on uncertain custody or accounting. Measure actionable
independent defects found per attempt, attempts to the next useful stage, diagnostic overhead,
false blocks and time to a useful result. Separate infrastructure disturbances from source bugs.
Report measured changes with their scope; make no savings claim from synthetic controls alone.
