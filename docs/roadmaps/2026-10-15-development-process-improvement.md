# Development process improvement roadmap

Identity: **V3-IMPROVE-01**. Selected: **2026-10-15**.
Owner: `.github` programme maintainer with Coordination and the selected diagnostic/receiver owners.
Status: **implementation underway; all three selected adoption/evaluation outcomes remain open**.
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
The selected plan alone completed no implementation. The partial source and pilot evidence below
records later delivery without implying package publication, installed adoption or comparative benefit.

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
when real ready work supplies those prerequisites. The Coordination/SystemAdmin cross-repository source cohort is evaluated below; comparable
attempts/diagnosis baselines remain incomplete.
Retain the preflight correctness change with **inconclusive economics**; this is not a wider-rollout decision.
The .1 checkbox remains open: this evidence establishes one bounded local profile and partial source
adoption, not two-workload acceptance, cross-repository evaluation or selected installed adoption.

[PR #4533](https://github.com/FS-GG/.github/pull/4533) subsequently delivered the bounded capacity
admission guidance and this evidence record on protected main at
`3c5589e81a8cb70cd0b8e5c681bf0046605724a6`. Its exact final head passed required hosted checks,
including Atlas and semantic skill quality. It did not admit a second workload or resolve the cohort
comparison, so the .1 acceptance boundary above is unchanged.

[PR #4537](https://github.com/FS-GG/.github/pull/4537) delivered the assembled carrier preflight at
protected source `83e9778003a6255af3483d8d82790ba887b2d430`. The existing skill-quality entry now
checks Atlas archive/seed delivery and the same partial skill tree used by full qualification before
building the gate engine; it also provides `--preflight BASE_SHA` without CLR or PyYAML. The two original
hosted packaging misses (omitted Atlas seed and repository-only relative link) remain recorded. A later
local review found that an Atlas import generated ignored bytecode and broke mirror validation; inherited
bytecode suppression and the delivered-skill byte guard now refuse that defect. Thirteen real command
controls covered the original defects, generated caches/bytes, malformed/missing prerequisites and
normal-entry blocking, with no dotnet or gate-engine sentinel launch.

The corrected complete command measured **2.034 seconds** on its first fresh-fixture invocation and
**1.998/1.989 seconds** on warm invocations, including preparation and cleanup; OS caches were uncontrolled
and no provisioning/downloads were needed. These are observations, not avoided-attempt or savings claims.
[Full semantic qualification](https://github.com/FS-GG/.github/actions/runs/38727681459) and the
[actual runner](https://github.com/FS-GG/.github/actions/runs/38727681396) passed at exact candidate
`1e46a7459f2b641dad05c85a929153182ed1680c`; all eight required contexts passed, including fresh
[metadata-sensitive coherence](https://github.com/FS-GG/.github/actions/runs/38728495058) after the final
PR-body edit. That body-only edit triggered five metadata-sensitive runs, created at 11:36:07 UTC
and last reported updated at 11:37:45 UTC: a **98-second provider interval** across those runs. This
includes provider bookkeeping and waiting; it is not active engineering effort or billed runner time.
Full required validation was preserved. The second-workload and cohort holds
above remain; .1 is open and comparative economics remain inconclusive. The reusable finding is
[Atlas lesson `platform/assembled-carrier-preflight-and-bytecode`](../atlas/records/bac3431138e986d6426c1e81bdfe0a968394896c0751cbbcc8f36002fd0ed069.json).

### Cross-repository source cohort — 15 October, 13:44 UTC

The [prospective selection](https://github.com/FS-GG/.github/issues/4516#issuecomment-6193021836)
at **13:18:33 UTC (T0)** enrolled one necessary SystemAdmin decoder source attempt against accepted
Coordination input `16b30651e1197abae5ee6e9b607e96c15ed4a766`, before the host decoder edit.
SystemAdmin owned the three-path change and a separate programme reviewer owned source review.
The declared window ended at an accepted source result or 20:04 UTC, whichever came first; the
[completion receipt](https://github.com/FS-GG/.github/issues/4516#issuecomment-6193555735) closes it
at the accepted integration result, 13:42:15 UTC. Earlier carrier readback and framing work preceded
T0 and differed in scope; they are not a comparable baseline.

| Milestone | UTC | Evidence and boundary |
| --- | --- | --- |
| Branch/owner preflight began | 13:20:41 | Owner-reported; first edit followed at 13:23:48 |
| Candidate delivered | 13:29:35 | [Candidate receipt](https://github.com/FS-GG/.github/issues/4516#issuecomment-6193248972), exact `e12430e1743a668a2a7c4d6ec905662e28042a2f` |
| Independent source review passed | 13:34:57 | [Review receipt](https://github.com/FS-GG/.github/issues/4516#issuecomment-6193359821), independent 23 pure tests passed |
| Ordinary host source landed | 13:36:40 | [Owner landing receipt](https://github.com/FS-GG/.github/issues/4516#issuecomment-6193443268), merge `d41559e0c8909ef012b29cc6234bb88e10ff9d5f`; host main is unprotected |
| Fresh actual-main tests passed | 13:37:24 | Owner-reported 23 tests, zero failures/errors/skips; 251 wire and 31 sizing cases |
| Programme integration audit passed | 13:42:15 | [Bounded integration receipt](https://github.com/FS-GG/.github/issues/4516#issuecomment-6193512676), supplied Git objects, scoped source equivalence and hash-bound transcript verified |

**Original failure retained:** the first suite invocation hit a fixture transport error on an
intentionally oversized negative row: the production document bound blocked the row before decoder
refusal. The fixture transport was corrected without widening that bound. The original transcript
and per-case history remain in restricted custody; final green counts do not erase that attempt.

The public completion receipt reports **111.14 seconds** initial owner-mail discovery latency,
**0.47/0.52 seconds** for the failed/passing test processes, and **28.47 seconds** from the first
source-test failure to a subsequent passing result. The programme independently hash-verified the
restricted completion summary (`5a19636d23f7c8f17f749d6c19e3493e3703413ed5024cc46440a8cf90351202`);
these durations remain owner-measured. Failure-to-pass is not a measured first-useful-diagnosis
interval. Exact failure/diagnosis timestamps and complete invocation count remain unavailable here.

T0 to candidate delivery was **11 minutes 2 seconds**, to independent review **16 minutes 24 seconds**,
and to reported host landing **18 minutes 7 seconds**. These are elapsed milestone intervals, not active
engineering effort or runner cost. The measured test processes and initial discovery latency provide
a partial separation; complete queue versus execution time, whole-family effort and a comparable
baseline remain missing. Faster diagnosis, fewer repeated attempts and savings cannot be established.
The treatment used candidate source checks and separate independent review; these data do not isolate
the effect of programme preflight changes. Retain the bounded source route; comparative benefit remains
**inconclusive**, with no wider-rollout inference.

The programme audited the supplied integration packet, including exactly the three admitted paths and
unchanged scoped subtree. Private live remote currentness and fresh host test execution remain
SystemAdmin-reported, supported by that packet rather than independently rerun by the programme.
No private source, evidence location or credential is copied here. The accepted
[unattended installed gate](../designs/2026-10-13-provisioned-podman-execution.md#unattended-normal-use-acceptance-gate)
at protected source `5480370339889e96ab805e3cab5fd9c934ee30b1` remains open: reviewed source,
manual handoffs and pure fixtures do not establish installed allocation, binding, launch, teardown
or worker-loss recovery without a human host agent.

No genuinely unstarted ordinary source cohort has yet been selected prospectively for this evaluation.
Completed ordinary lanes cannot be relabelled as prospective cohorts, and no work is created solely
for measurement. **V3-IMPROVE-01.1 remains open:** ordinary cohort evaluation, comparable denominators,
second-workload admission and selected installed adoption remain unresolved. Local CLR and native
effects remain held; this evidence update grants no execution or installation authority.

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

[PR #4539](https://github.com/FS-GG/.github/pull/4539), protected merge
`505c54cf630c33996d3fd7cafd68e7ab2496f019`, adds actual execution of all four `.claude`
work-programme FSI counterparts, preserving the four `.agents` suites, five Python suites and original
budgets. The [exact-head hosted entry](https://github.com/FS-GG/.github/actions/runs/38729032134)
passed all thirteen suites at `b8a536f36ad9f3311ba86b63fafb8d45f0721152`. Independent artifact
acquisition matched all 32 source pins; first cause was null, omitted coverage empty, and exits,
cleanup, reporting, display and upload passed. The observed 47.256 seconds and 20,556 output bytes
are descriptive only. This accepts both repository-native helper roots at that source, without
claiming a post-merge run, packaged installation, native custody or comparative benefit. Phase 2 and
V2-DIAG-01.4 remain open; package/native/P4 owner joins and the existing failed intake read are unchanged.

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

The source implementation reconciled the six existing local `show-progress` modifications in both skill
roots: `SKILL.md`, `references/estimation-method.md` and `references/report-template.md`. Preserve the
accepted coverage and estimation intent in any later consumer or report changes.

Acceptance: applicable reference traversal and mirror/registry checks pass; representative ordinary
and cross-repository work retains authority, evidence and original failures. A compact sample report
covers the full selected population and supports comparison with the prior report without fabricated
aggregate progress or savings. Verify installed carrier adoption wherever delivery changes packaged
skills. Record report size and actual effort/usage coverage separately.

### Partial source evidence — 15 October, 10:48 UTC

[PR #4532](https://github.com/FS-GG/.github/pull/4532) delivered the `pnext-item` and `work-roadmap`
entrypoint simplification on protected main at `42ce800441d45b1b045cc17478ca306eb4db9fbf`.
The strict, heavyweight, first-install and observed-runtime obligations remain in linked references;
the mirrored skill manifests and hosted semantic checks passed. This is source and carrier-input delivery,
not publication or installed receiver adoption.

[PR #4534](https://github.com/FS-GG/.github/pull/4534) delivered the compact `show-progress` guidance
on protected main at `f311f0d9d3d89639390243372289c03d8dc6d88d`. A bounded sample covered
all 19 selected outcomes (two done, seventeen open) and measured 33,116 bytes against the prior
115,056-byte report. Different report populations and missing effort/usage coverage prevent a savings
claim. `show-progress` is repository-native; no package release follows from that edit alone.

The .3 checkbox remains open pending the coherent skill-carrier release and installed receiver
readback, ordinary and cross-repository use, and the selected cohort comparison. The source changes
do not close the diagnostic or capacity outcomes.

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
