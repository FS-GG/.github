# Specairn: reuse and example-driven delivery addendum

This addendum records the agreed refinements to the [v3 design](../2026-10-10-fsgg-v3-clean-rewrite.md)
and [delivery roadmap](03-delivery-roadmap.md), dated 2026-10-26. It updates the future
proposal only. It does not start v3, change current operating policy, release historical
holds, qualify infrastructure, or authorize a cutover.

Complete the agreed v2 programme, then build a small set of real example projects
before finalizing v3 scope. Use those projects to establish which capabilities help,
which require manual intervention, and which can be simplified. Preserve the single
production cutover and repeated isolated integration testing during v3 development.

Where the earlier proposal differs, this addendum refines M0 infrastructure reuse,
M1 sequencing, the Forgejo decision, measurable simplification, and the future cutover
treatment of unresolved effects. Other completion and correctness requirements remain.

## Reuse inventory and implementation boundaries

Extend the existing M0 capability matrix with a concise reuse disposition. Do not
create a separate manually synchronized inventory. Cover runners and supervision,
qualified images and toolchains, storage and artifact handling, publication and feeds,
product libraries, execution adapters, and relevant tests.

| Disposition | Meaning | Evidence needed |
|---|---|---|
| Reuse unchanged | Preserve the implementation and its supported operating assumptions | Exact source or artifact reference, supported environment, existing evidence and v3 caller compatibility |
| Adapt | Preserve useful implementation behind a changed interface or deployment boundary | Named delta, affected assumptions and focused integration checks |
| Replace | Implement a new mechanism | Concrete limitation of reuse, replacement scope and behavioral scenarios |
| Retire | Deliberately omit a capability | Product decision identifying the outcome no longer promised |

Distinguish historical qualification from current availability and from qualification
of the new caller. Reuse unchanged evidence where its relevant inputs and assumptions
still match; test the changed integration and refresh consequential live facts. Do
not repeat an entire infrastructure qualification solely because v3 consumes it.

New platform contracts, fresh storage and removal of compatibility obligations do
not require new implementations of every mechanism. Suitable v2 code and tests may
be reused behind v3 interfaces after checking dependencies, licensing, authority
boundaries and behavior. Reuse must not reintroduce a second workflow authority or
depend on importing v2 operational state. Product runtime APIs remain outside the
rewrite as already specified.

## Example projects become acceptance fixtures

Choose a small set of examples that exercises different real journeys, such as a
small application, an interactive game, and a change to an existing repository.
Finalize the selection against completed v2 capabilities and available facilities;
these examples do not automatically add new platform features.

Use published tools from a consumer environment. Preserve each starting revision,
requested change, relevant environment, expected result and observed difficulties.
Record maintainer-only knowledge, manual repairs, coordinator interventions and
missing capabilities. Separate product-specific requirements from platform defects.

Carry these fixtures into M0 and run comparable journeys through v3. Compare useful
outcomes rather than identical commands, generated files or agent tool sequences.
Existing example results can supply the v2 baseline; M0 need not repeat them when
their relevant inputs and measurement conditions still apply. Fix v2 completion
blockers, but keep optional improvements as evidence for v3 instead of expanding
the v2 completion scope automatically.

## Smaller executable M1 checkpoints

Keep M1 as one milestone, with three implementation checkpoints rather than three
new approval stages or independent delivery ledgers:

1. **First real journey.** Use an existing suitable runner and one supported provider
   to take an accepted change through execution, verification and a protected PR in
   an isolated repository. Establish only the signatures needed for this journey.
   Use the smallest required forge setup; a new GitHub adapter is not implied.
2. **Recovery and reuse.** Interrupt the journey at meaningful boundaries. Prove
   recovery, cancellation, uncertain-effect handling and a second operation family
   using the same lifecycle. Exercise evidence reuse and relevant-input invalidation.
3. **Infrastructure and workload closure.** Complete the selected forge's policy,
   credential, publication, planning and workload qualification, including required
   feed authentication and bounded runtime queries.

Independent preparation may proceed in parallel. All existing M1 exit conditions
still apply before broad M2 expansion. These checkpoints bring the first integrated
execution forward; they do not defer mandatory correctness checks until cutover.

## Forge choice and migration cost

Keep Forgejo as the proposed primary candidate, but distinguish a product requirement
from an efficiency hypothesis at M0. Record whether operator control or another
required capability makes the move mandatory. If it does, budget and qualify the
forge migration separately from the control-plane simplification.

If the move is justified primarily by expected efficiency, compare its measured
benefit with setup, adapter, maintenance, backup, recovery and migration costs before
making it a dependency of broad implementation. Retaining the existing forge is an
alternative requiring an explicit proposal revision, not an automatic fallback or
permission to implement two adapters. Keep one canonical writable delivery surface.

Internal scheduling and recovery must remain independent of forge requests whichever
forge is selected. A faster control plane does not by itself demonstrate that a
forge migration was beneficial.

## Scoped treatment of unresolved effects at cutover

Refine design section 14 and roadmap cutover step 2: settle or demonstrably isolate
every active or unresolved v2 effect that could interfere with v3 before proceeding.
An unavailable historical outcome need not be reconstructed when its possible effects
are demonstrably unable to interfere with the new environment.

Retain the original outcome, evidence and named responsibility. Identify potentially
affected resources, writers, credentials and execution channels; demonstrate the
boundary preventing late or continuing effects from reaching v3. Isolation must
cover relevant shared storage and backing, processes and external mutations. A new
operation name, database, lease expiry or runtime epoch alone is insufficient.

Where interference remains possible or the boundary cannot be established, keep the
affected resource or operation blocked. Do not relabel Unknown as success, release
its resources on assumption, or hide it by switching frameworks. No overlapping
writer authority is permitted. Apply the same settlement-or-isolation rule to
fallback after v3 effects have begun. Concrete boundaries belong in the tested M4
runbook and require the applicable future operational authority.

## Demonstrate simplification before declaring benefit

Use the example journeys to set explicit v3 acceptance targets at M0 for operator
interventions, manually supplied facts, end-to-end delivery time and operational
maintenance. Include model and compute cost where measurable; otherwise record the
gap. Separate execution, queueing, verification and recovery time.

Record benchmark environment, task scope, cache conditions and sample variability.
Assess correctness, escaped defects and recovery behavior alongside speed. Existing
proposed feedback targets remain subject to these measurements. Do not invent a
percentage saving or infer token savings from fewer approvals.

Before cutover, assess the measured results against the recorded targets. A missed
target needs either a repair or an explicit product tradeoff stating the shortfall;
it cannot be silently renamed as improvement. Correctness, custody and authority
defects retain their existing blocking treatment. Reuse makes a shorter rewrite
plausible; observed delivery after M1 supplies the basis for an estimate.
