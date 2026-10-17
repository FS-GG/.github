# Estimation method — show-progress/2

The .9/.11 programme consumer is prepared only under the
[organization contract](../../check-board/references/organization-v2-inspection.md). After accepted
cutover, use the same binding-v4/inspection2, six-field owning issue, current slice/native PR and
acceptance evidence as execution/recovery, with actual observation times and coverage. Human Status
and Verified are neither accepted completion nor resource capacity. Preserve partial milestones,
uncertain effects and unselected/historical discrepancies. Until cutover retain the existing manual
v3 current-status boundary; reporting does not activate or mutate the board.


This is a lightweight reporting method, not a replacement for owning plans or an earned
value management system. The percentages and forecasts describe explicitly named
populations. A scope change, a new uncertainty or additional necessary work may reduce
readiness while accepted work remains unchanged.

## Process health before detail; concrete progress before percentages

Start with whether accepted delivery, effort and elapsed time match the recorded
expectations, and identify material rework or failure patterns with their evidence
coverage. Where the baseline or observations are missing, mark the judgement
uncertain. Then show evidenced before → after capabilities since the previous report,
accumulated completed work and named remaining tasks. Preserve intermediate
implementation, repair, qualification and publication milestones while whole outcomes remain open. A missing
historical percentage or effort baseline does not prevent a concrete milestone comparison.
Use the trailing 24-hour window as a separate comparison when its baseline differs.

## Compact presentation without lost coverage

Use one current-state row per selected outcome and link the previous report. Describe
current acceptance, remaining boundary, changes, blockers, confidence and comparable
estimates there. Expand only changed or materially exceptional claims; link historical
detail instead of repeating it. Shared gaps and assumptions name all affected IDs once;
retain item-specific uncertainty inline. Unchanged estimates need a dated basis and
freshness limit; absent earlier estimates are unknown, not unchanged. All estimation
obligations still apply even when their presentation is shared or linked.

Use native observations for metrics and state their population, denominator, period and
coverage. Report bytes as bytes; effort/usage savings need actual comparable attribution.
Missing telemetry is unknown, never zero. Do not start extra workloads for reporting.

## Progress and its denominator

Report three different quantities only when their inputs exist:

1. **Formal outcome closure:** accepted Done rows / in-scope rows. This is a count, not
   an effort-weighted completion percentage. Show additions/removals separately.
2. **Acceptance readiness:** credit for evidenced acceptance units within each item's
   fixed scope. Prefer the owner's existing breakdown and planned relative effort weights.
   An assertion is earned when its defined evidence exists; split an oversized assertion
   into useful independently accepted units rather than awarding credit for time spent.
3. **Effort forecast:** actual attributed work plus estimated remaining work. Spending
   more time or performing more retries does not earn acceptance credit.

For baseline weights `w[j]` and evidenced credit `a[j]` between 0 and 1:

`readiness = 100 * sum(w[j] * a[j]) / sum(w[j])`

Declare what partial credit means before using it. Do not give source delivery the
publication/adoption weight or create universal phase weights across unrelated items.
Use existing native acceptance units and defensible owner weights; do not manufacture
units or weights to fill the report. When these do not exist, use a qualitative analyst
judgement with rationale and low confidence, or unknown; a guessed numerical readiness
range has no supported denominator. This restriction does not prevent a bottom-up effort
or calendar scenario range supported by named remaining tasks. Do not retrospectively
invent weights to make an earlier conversational guess look measured.

Aggregate only compatible, covered items. Use baseline effort weights if available.
An explicitly requested equal-item overview may average supported native item readiness,
but label it an **equal-item readiness index**, never programme effort completion.
Report the population, missing items, coverage and weighting; an item without a native
denominator remains unknown and prevents a claimed full-population index. No aggregate
estimate is better than silently excluding the hardest unknowns. Keep formal closure beside any approximate aggregate.

## Change over a window

Keep scope IDs, weights, acceptance rules and evidence cutoffs stable for comparison.
Show a bridge when they change: old scope/current evidence first, then added/removed
scope or changed methodology. For example, 6 of 10 accepted equal units becomes 8 of 10
(+20 percentage points delivered); adding four required units then yields 8/14 = 57.1%.
The net -2.9 points does not mean no useful work was done.

If only unpaired readiness ranges exist, do not subtract like endpoints and call the
result a measured interval. From previous `[L0,U0]` and current `[L1,U1]`, a conservative
arithmetic bound is `[L1-U0, U1-L0]`. Scenario-paired changes are preferable when supported;
neither calculation creates a probability statement. Round rough estimates to sensible
increments, ordinarily 5 points. Report absent baselines as unavailable, not 0% progress.

## Work effort and revised total

Use one unit per comparison. Record actual attribution coverage and avoid converting
tokens, elapsed chat time, CI wall time or agent count into person-hours.

- `ETC`: estimate to complete, the remaining active effort for the defined scope.
- `AC`: actual attributed effort to the cutoff, including necessary failed attempts and
  rework. If incomplete, report known effort as a lower bound and the missing coverage.
- `EAC = AC + ETC`: revised total effort, only when units/coverage are compatible.
- `delta EAC = current EAC - previous EAC` and
  `delta EAC % = 100 * delta EAC / previous EAC` for a positive comparable baseline.
- Also compare current EAC with the original total baseline, retaining its date/scope.

When actual work is unknown, provide a bottom-up remaining-work range without fabricating
EAC. A separately justified top-down whole-item estimate may be reported as a different
method, with its own basis. Unknown prior EAC means “first total estimate; change unknown.”
Keep original, previous and current estimates; a rebaseline does not erase prior variance.

Explain the forecast bridge by added/removed scope, technical discovery, defects/rework,
qualification/release work, capacity, external delay and corrected measurement. Avoid
double-counting consumed repair effort both in AC and future ETC. Waiting normally changes
calendar time; it changes effort only when work such as diagnosis or repeated setup occurs.
Record risk contingency separately and avoid adding it again when the forecast already
includes the same historical failures or risks. Do not apply cost CPI/SPI formulae without
the corresponding planned value, earned value, actual cost and a valid baseline.

## Complexity, uncertainty and process health

For each remaining task, use low/medium/high complexity with a technical explanation:
local well-understood change; interacting components with known boundaries; or unfamiliar
integration/qualification with unresolved contracts. Complexity is not duration: a short
credential check can have high uncertainty. Use owner evidence and actual task structure,
not a fixed complexity-to-hours multiplier.

State a working effort range and plausible adverse scenario, assumptions and confidence
inline. High confidence needs well-bounded scope and relevant observed examples; medium
has a concrete path with material integration uncertainty; low has sparse evidence or
unresolved scope. These labels are judgement, not calibrated probabilities. Quantify
coverage when countable (for example, 6/9 assertions verified, 2 pending, 1 unobserved),
and identify whether those assertions are equal or effort-weighted. Unknown actual effort
or an absent previous forecast does not invalidate a supported prospective task estimate.
Unknown external waiting does not invalidate an estimate of active effort after unblock.

Assess health using delivery, flow, rework and estimate movement together:

| Label | Evidence needed |
|---|---|
| On track | Concrete advancement consistent with the supported current plan; no material observed divergence in covered evidence |
| At risk | Specific unresolved risk threatens an acceptance boundary or working estimate |
| Blocked | Required dependency prevents the next step; identify supplier/unblock condition |
| Recovering | Prior failure is being resolved with verified gains; state remaining boundary |
| Churning | Recurring repair/review cycles, reopened decisions or unplanned fix sequences with weak acceptance advancement; explain the pattern |
| Done | Named scope accepted, with evidence |
| Insufficient evidence | Coverage cannot support a health judgement; identify what is missing |

Combine labels when appropriate, for example “Blocked; churning before the block.” State
assessment confidence and the observation period. Report repair/review rounds, repeat failure
families, reopenings, rework effort and newly accepted milestones where observable; partial
counts are lower bounds. A rework share needs attributed rework/total effort in compatible
units and covered scope. Avoid universal churn thresholds, invented zero counts and
cross-item ratios that treat milestones as equal value. Repeated small fixes can be healthy
if each demonstrably removes an obstacle. Lack of whole-outcome closure alone is not churn.

Explain effort and calendar changes separately, including downward revisions and first
estimates. A churn finding should name whether the root cause is known or hypothesized and
a next step that can resolve it, such as an end-to-end reproduction or a contract decision.
This reports process health; it neither scores individuals nor dispatches remediation.

## Completion forecasting

Separate **when work can start**, **active effort**, **unattended execution**, **external
waiting** and **calendar finish**. Record the dependency path and genuinely available
owner/integrator/CI capacity. Selected work may compete for one integrator even when agents
are free. Do not sum overlapping waits as critical-path delay or simply divide by workers.

Choose the least elaborate method justified by the evidence:

| Situation | Forecast | Required disclosure |
|---|---|---|
| Comparable completed items, stable workflow | Empirical cycle-time or throughput forecast | Start/finish definition, cohort, period, n, exclusions, WIP, age and process changes |
| Known remaining units and useful throughput history | Bootstrap/Monte Carlo using the established tooling | Inputs, scope growth, zero-output periods, seed/model, capacity/dependencies, simulations and sensitivity |
| Sparse history but a concrete remaining path | Bottom-up optimistic/likely/adverse scenario range | Task breakdown, analogous observations, waiting/capacity assumptions and low confidence |
| External dependency with unknown resolution | Conditional duration after unblock; calendar finish unknown | Supplier/owner, missing evidence, unblock condition and next forecastable milestone |
| No credible remaining scope | Forecast unavailable | The specific missing scope decision and next estimation step |

Use `P50`/`P80` (or another explicitly chosen quantile) only for a real distributional
forecast. An empirical percentile is model output, not proof of calibration. State when
coverage has not been tested. A guessed range must say “scenario range,” not P80 or 80%
confidence. A PERT mean, if used, does not establish tail probabilities or a finish date.

For aged in-progress work, condition the remaining-time forecast on the elapsed age and
current stage. Subtracting age from an unconditional median can produce impossible
negative remaining time. Keep unfinished observations right-censored, timed-out attempts
failed and cancellations distinct from successful completion; disclose selection bias.
Use a supported survival model only when the event definition and censoring assumptions
fit. Do not automatically treat cancelled or blocked work as non-informative censoring.

Model shared blockers and serial dependencies explicitly. Sum/max of marginal P80 values
is not generally the programme P80. Retain correlation, queues and bottlenecks or use
honest scenario bounds. Historical infrastructure-project uplift percentages are not
software/agent multipliers; use comparable local reference classes instead.

Give relative durations plus absolute date ranges where calendar assumptions permit.
State timezone, working days/hours, start assumption and forecast generation cutoff.
An unscheduled item's date is conditional on being selected, not “today plus duration.”
For a blocked item, distinguish the next milestone forecast from full item completion.

## Unexpected complications and learning

For each complication record first observed time (or unknown), whether it was anticipated,
the contradicted assumption, evidence, actual effect, corrective action/status, supplying
owner and reopening condition. Separate cause, symptom and hypothesis. Report elapsed
delay from observed intervals only; overlapping incidents need attribution before summing.
“No new complication observed” is scoped to the examined sources, not a fleet-wide claim.

Once outcomes complete, compare their original stored forecasts with actual completion
using the same scope and forecast horizon. Report signed error, absolute error and interval
coverage with n; evaluate on later observations, not the training data. Do not let one easy
success justify a calibrated-confidence claim. Separate scope-change error from forecast
error and retain misses to improve the next estimate.

## Review examples

- A source PR merged but installation failed: source credit stands; item stays open;
  actual rework counts as effort and the installation forecast changes.
- Two parallel 2-hour CI waits overlap fully: 4 job-hours observed; at most 2 elapsed
  hours attributable to that wait path, not a 4-hour programme delay.
- EAC rises 40 to 52 owner-hours: +12 hours/+30%; readiness moving 60% to 65% is +5 pp,
  not +5% relative progress. If AC is unknown, this EAC calculation is unavailable.
- Publishing rights have no supplier date: calendar finish unknown; show the remaining
  supported execution range after rights are granted, plus the next validation milestone.
- A common install supports three outcomes: allocate its effort once, preserve three
  different acceptance assertions and show all dependent forecasts.
- The first report has no historical effort estimate: write “baseline unavailable,”
  not “no change,” and establish a dated prospective estimate where possible.
