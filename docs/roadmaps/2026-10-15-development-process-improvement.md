# Development process improvement roadmap

Identity: **V3-IMPROVE-01**. Selected: **2026-10-15**.
Owner: `.github` programme maintainer with Coordination and the selected diagnostic/receiver owners.
Status: **design selected; three bounded implementation changes remain open**.
Parent: [Unified Roadmap v3](2026-10-07-unified-development-roadmap-v3.md#2-open-work-and-owning-plans).
Design: [development process improvement](../designs/2026-10-15-development-process-improvement.md).

## Delivery boundary

Deliver capacity/preflight practice, finish the existing diagnostic adoption outcome, then simplify
skills/reports. Evaluate one ordinary cohort and one crossing repositories against the same acceptance
standard. Success means fewer repeated attempts and faster useful diagnosis, with no increase in
unknown effects, false passes or unowned work. Inconclusive data stays explicit.

This is a finite improvement lane, not a new execution or telemetry framework. V2-CTX, V2-EFF,
V2-PROC and V2-DIAG retain their own implementation and acceptance authority. The design's historical
baseline ends at **2026-10-15 08:16:54 UTC**; later source or operational acceptance must be refreshed.
No skill/runtime implementation, package publication or pilot execution is completed by this plan.

## Change 1 — capacity and final-candidate preflight

- [ ] **V3-IMPROVE-01.1 — Adopt the bounded capacity/preflight practice.** Route: routine.

Owner: programme maintainer and selected component owner; one `.github` integrator for shared skills.
Depends on: current source, live reservations, actual enforcing execution profile and one ready
ordinary cohort plus one ready cohort crossing repositories. Name the cohorts and their comparison
basis in existing artifacts before treatment; do not impose unrelated fleet qualification.

Scope: update the owning practice/source only where the design identifies a real gap. Use the supplied
up-to-12-core/32-GiB envelope with observed availability and measured peaks, never a hardcoded count.
Begin with one local compile; add a second only with actual disjoint outputs, valid shared state and
measured headroom. Inspect backing storage and use qualified SSD paths for active builds/caches,
HDD paths for cold retention where available. Preserve original custody and cleanup.

Place cheap checks on the final assembled candidate, compile early and exercise the maintained
caller with inert external seams. After two similar failures, review and repair the complete affected
path before another costly attempt. Reconcile path declarations through lane-steward and typed tools;
retain PR slots and immutable cross-repository joins. Measure shared-build, partition and aggregation
costs before any CI fan-out change. ADR-0081, ADR-0084 and ADR-0086 remain controlling.

Acceptance: valid and invalid assembled inputs demonstrate the intended preflight boundary; missing,
malformed, timed-out and unknown results cannot pass. The actual caller regression covers the
selected failure mechanism. The recorded capacity window retains live/unknown reservations, sampled
peaks, actual concurrency and cleanup. If a second workload cannot be admitted, record the blocker;
that does not establish two-workload acceptance. Provide one usable local preflight command through the owning tool, with explicit prerequisites
and actionable failures. Measure its cold setup, warm execution and integration/repair overhead;
report the observed basis and gaps. Required coherent checks and consumer assertions remain intact.
Compare attempts and diagnosis in the declared cohorts with gaps explicit.

Likely touch-set: relevant `work-programme`/`work-roadmap` references and their mirrors, selected
component caller/preflight/tests, existing acceptance evidence. Resolve exact paths before assignment;
this plan does not reserve broad directories or authorize unrelated CI redesign.

### Partial evidence — 15 October, 10:27 UTC

[PR #4531](https://github.com/FS-GG/.github/pull/4531) delivered the final-candidate preflight at
protected source `877dd41610ed48aa39895845d99202285d5f3955`.
[Hosted qualification](https://github.com/FS-GG/.github/actions/runs/38718668310) passed all nine
required runner suites at its exact candidate; all branch-required checks passed. The local preflight
command needed no provisioning/downloads: first observed invocation 47.1 ms, three warm invocations
46.7–49.3 ms. OS cache state was uncontrolled; active integration/repair effort was not instrumented.
The original fixture declaration failures and the independently found post-preflight pin/symlink gap
were repaired with retained negative controls; these repairs do not establish comparative savings.

The programme maintainer observed one local `bash tests/work-programme/run.sh` profile at that protected
source, **10:26:57.537–10:27:08.573 UTC**, using its original 170-second deadline. All nine suites passed
in 11.036 seconds wall time (runner: 10.959 seconds), with no first cause or omitted coverage and successful
cleanup/reporting. Across 110 samples at approximately 0.1-second intervals, aggregate family RSS peaked
at **547,225,600 bytes (521.875 MiB)** and sampled CPU demand at **1.8785 cores**; peak threads were 61.
The cgroup's sampled memory peak was 15,692,103,680 bytes including unrelated background work; its
OOM/max counters did not increase. No known family survivor remained, and the final process readback
found no live CLR process. Two observed CLR descendants belonged to this single serial workload.

The earlier prelaunch inventory correctly held execution on seven unknown MSBuild processes. Their
original owner was subsequently identified and absence observed before launch; no signal was sent and
their termination cause remains unknown. The maintainer retains the original hold, process identities,
profile, samples, runner output and summary in restricted pilot evidence. These are owner-observed local
aggregates, separate from the linked hosted result. Sampling can miss short peaks, aggregate RSS can
count shared pages twice, and the historical cgroup peak was not reset. The overlay was observed; SSD
backing relied on the supplied environment admission rather than independently visible device evidence.

**Second workload held:** background process ownership/reservation completeness and the second candidate's
profile, disjoint output/cache joins and combined measured headroom have not been established. No second
workload was requested or admitted in this pilot. Follow the existing
[worked admission route](../../.agents/skills/work-programme/references/context.md#worked-admission-and-evidence-route)
when real ready work supplies those prerequisites. The selected Coordination/SystemAdmin cross-repository
cohort still needs next-candidate treatment and comparison; attempts/diagnosis baselines remain incomplete.
Retain the preflight correctness change with **inconclusive economics**; this is not a wider-rollout decision.
The .1 checkbox remains open: this evidence establishes one bounded local profile and partial source
adoption, not two-workload acceptance, cross-repository evaluation or selected installed adoption.

## Change 2 — finish V2-DIAG-01.4 adoption

**V3-IMPROVE-01.2** is the integration join to
[V2-DIAG-01.4](diagnostic-execution.md#delivery-sequence), whose existing checkbox remains the sole
completion authority for publication, installed diagnostic adoption and evaluation.

Owner: Coordination release/diagnostic owner and both existing pilot owners. Route: routine source
delivery; publication and native effects retain their applicable admission. Depends on the exact coherent producer/carrier prerequisites and each receiver's actual authority
and admission. Change 1 acceptance is not a technical prerequisite for independent DIAG publication
or adoption. Existing owners may advance their ready work in parallel; this lane records the three
bounded rollout/evaluation changes in the selected order without withholding useful accepted work.

Reconcile current releases against the DIAG plan's historical package claims and original operations.
Use existing coherent routes, publish required artifacts and independently verify installed receivers.
Both owners exercise their real entrypoints: ordinary work-programme tests and the Coordination
packaged native diagnostic qualifier. Preserve the selected pilot scopes; product adapters are not
implicitly enrolled. Reuse accepted .1–.3 evidence only within its exact compatibility boundary.

Acceptance stays in DIAG .4: both owners verify installed behavior, independent failure collection,
dependent blocking and first-cause retention; original cumulative budgets and separate cleanup/reporting
outcomes remain truthful. Successful complete behavior is observed; failed/partial runs never qualify.
Record source, publication, installation and native results separately. Evaluate defect collection,
attempts, diagnosis time, overhead and infrastructure failures in a bounded window. Missing baseline
prevents a benefit claim, not truthful adoption evidence.

This integration phase records the durable DIAG acceptance link and any remaining constraints in this
plan. It neither clones DIAG .4 nor reopens .1–.3. No separate diagnostic service or fleet retrofit is selected.

## Change 3 — simplify skill entrypoints and reports

- [ ] **V3-IMPROVE-01.3 — Adopt concise guidance and full-coverage reporting.** Route: routine.

Owner: `.github` skill/report maintainer with V2-CTX and applicable skill-carrier receiver owners.
Depends on: current skill/report obligations, overlap reconciliation and the exact carrier/receiver
join when installed adoption is claimed. Independent skill/report source simplification does not wait
for DIAG adoption; only an assertion that consumes diagnostic behavior depends on that behavior.
The selected rollout/evaluation order does not create a new cross-component delivery gate.

Scope: slim `pnext-item` and `work-roadmap` by moving conditional detail into linked references while
preserving complete applicable instructions. Reconcile `.agents`/`.claude` and registry/manifest
coherence using the existing source-of-truth route. Make `show-progress` compact with complete outcome
coverage and linked detailed evidence. Preserve meaningful changes, blockers, complications,
expectations, estimate bases and unknowns; remove duplicated narrative rather than outcome rows.

Before overlapping edits, inspect and reconcile the six existing local `show-progress` modifications
in both skill roots: `SKILL.md`, `references/estimation-method.md` and
`references/report-template.md`. This docs delivery preserves them untouched. Keep the user's
coverage/estimation intent and separate unrelated work; do not assume the local edits are already delivered.

Acceptance: applicable reference traversal and mirror/registry checks pass; representative ordinary
and cross-repository work retains authority, evidence and original failures. A compact sample report
covers the full selected population and supports comparison with the prior report without fabricated
aggregate progress or savings. Verify installed carrier adoption wherever delivery changes packaged
skills. Record report size and actual effort/usage coverage separately.

## Cohort evaluation and closure

Before each bounded window, retain scope, owners, baseline/comparison limitations, exact implementation
identity, required assertions, observations and stop conditions in the existing owner artifacts.
Use necessary ordinary work, including failed/stopped attempts. Do not add reporting-only workloads.

| Measure | Required interpretation |
|---|---|
| Repeated attempts and first-pass qualification | Count original attempts per accepted milestone and common-cause recurrence; preserve missing coverage. |
| Time to actionable diagnosis | Retain the original failure and first useful diagnosis timestamps; distinguish active effort from external waiting. |
| Capacity and pipeline behavior | Record sampled peaks, reservations, storage class, useful concurrent work and real setup/build/partition/aggregation cost. |
| Acceptance and ownership | Keep the same required assertions; investigate unknown effects, false passes and unowned work individually. |
| Guidance/report overhead | Record repeated reads, handoffs, report bytes and genuinely available usage/effort, without converting bytes to savings. |

Use the design's comparative pass criteria; there is no unsupported numeric speedup or delivery-date
promise. If evidence is missing or incomparable, record inconclusive benefit. If the treatment causes
an acceptance, custody or ownership regression, stop that treatment and use the owning rollback or
repair route. Broader adoption follows an explicit retain/revise/retire disposition, with existing
correctness adoption and statistical-claim boundaries preserved.

Close V3-IMPROVE-01 after the three changes' selected adoption and both cohort evaluations are recorded,
including DIAG's owning closure link and every intervention's disposition. Update the Unified Roadmap
row with durable evidence. New process candidates require a separate bounded selection.

## Dependencies, concurrency and installed workspaces

The selected three-change landing/evaluation sequence is .1 → .2 → .3 for this lane's rollout.
It is not a blanket dependency chain over existing owners: independent DIAG publication/adoption
and skill/report source work can proceed when their actual prerequisites are met. Do not delay or
reopen useful accepted work merely to preserve a diagram. Record it as an input when its evaluation
stage arrives. Shared policy/contracts have one integrator; runtime adoption waits only for its
actual producer/authority join. Keep existing PR and host limits. A complete V2-EFF dashboard or
LEARN installation is not a blanket prerequisite for ordinary correctness work; conclusions needing
missing evidence remain unavailable.

This plan changes no generated workspace. Each implementation owner identifies affected producer
packages/carriers, default or opt-in selection, clean creation and any promised retaining upgrade.
Source, publication, installation and activation remain separate. Reconcile reusable findings in Atlas
at delivery and retain private operational records in their existing custody.
