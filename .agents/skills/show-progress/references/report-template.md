# Compact report structure

Replace bracketed fields before saving. Every selected outcome gets exactly one current-state
row, including done, blocked, unchanged and unknown outcomes. Detail expands changed claims or
material exceptions; it does not repeat the table or the previous report's history.

```yaml
---
title: "Unified Roadmap v3 progress — [UTC cutoff]"
generated_at: "[actual UTC ISO-8601 time]"
as_of: "[UTC evidence cutoff]"
window_start: "[UTC comparison start]"
display_timezone: "Europe/Vienna"
roadmap_revision: "[revision or unknown]"
previous_report: "[relative path or none]"
comparison_baseline: "[cutoff, evidence and coverage]"
method: "show-progress/2; compact current state; [estimate basis]"
status: "[evidence snapshot / partial evidence snapshot]"
---
```

## Process health and expectations

Give a short verdict against the recorded plan or forecast, with evidence coverage and
confidence. Compare accepted outcomes and useful intermediate milestones with expectations;
name the most consequential rework pattern, external wait or unknown. State material effort
and calendar revisions separately, the affected IDs, owner and next corrective step. A mixed
picture may need several labels. Missing baseline or telemetry means insufficient evidence.
PR counts and lack of whole-outcome closure alone do not establish progress or churn.

Previous report: [relative link and embedded UTC cutoff, or first snapshot].
Comparison: [since previous report; separate trailing 24-hour baseline when different;
unavailable comparisons and why]. Scope: [selected population; additions/removals and exclusions].

## Current state

| Outcome / owner | Accepted now / remaining acceptance boundary | Meaningful change / evidence | Remaining work / blocker | Effort / completion forecast | Health / confidence |
|---|---|---|---|---|---|
| [stable ID; owning-plan link] | [accepted assertions and prepared work distinguished; remaining source/qualification/publication/installed/activation/whole-outcome boundary] | [before → after, event time and practical effect; source revision/observed time/coverage, or no new change observed within stated coverage] | [named tasks in dependency order; supplier and unblock condition] | [working/adverse ETC range and named units, task complexity/reason, estimate confidence/coverage; calendar range or conditional duration; previous → current revision or first estimate; gap/exception links] | [label, reason and assessment confidence; next corrective step/owner or exception link] |

Use On track, At risk, Blocked, Recovering, Churning, Done or Insufficient evidence. Keep
cells concise. Each row must show its own current acceptance boundary, even when unchanged.
Mark unknown and not applicable explicitly; do not repeat “Unknown → Unknown” comparisons.
An unchanged row links prior detail and states source freshness; an old observation is not
current verification. Retain intermediate gains while the whole outcome stays open.

## Shared gaps and assumptions

State each common evidence gap or assumption once, give it an anchor and name every affected
ID. Rows reference it and retain any item-specific limit. Cover original/previous total effort,
actual attributed effort, current total forecast, forecast dates and calibration when commonly
unavailable. Explicitly applying an unknown to named IDs satisfies those fields for those rows;
blank cells or a vague programme disclaimer do not. Known values remain visible with their
own scope, date and units. Missing historical estimates mean first estimate, not unchanged.

Record shared dependencies once in programme effort/scheduling and identify all affected
outcomes. State actual-effort coverage, capacity/calendar assumptions, critical-path waits,
exclusions and confidence meaning. Keep engineering effort, agent execution, hosted compute
and elapsed waiting separate. Unknown actual effort prevents AC + ETC total, but does not
prevent a supported remaining-work estimate. Unknown unblock dates require conditional time
after unblock and a next forecastable milestone, not a firm finish date.

## Changed claims and material exceptions

Include only outcomes needing explanation beyond their row. Omit this section if none do.
Use one anchored entry per affected outcome or shared cause; link it from the table.

- **Change and acceptance:** before → after, event time, practical effect, authoritative
  evidence and remaining assertions. Retain failures, regressions, corrected claims and
  unknown effects. Separate delivered change from changed scope, weights or evidence;
  keep the trailing 24-hour comparison distinct when its baseline differs.
- **Estimate revision:** original total baseline, previous total forecast, actual effort
  and coverage, remaining task breakdown/complexity, working/adverse ETC and current total
  where supported. Give previous → current effort/calendar revision and cause, including
  downward revisions or first estimates. State dependencies, capacity, start assumption,
  confidence and next milestone/reopening condition. Use unknowns or the named shared gap
  for unsupported fields; do not derive ETA from readiness.
- **Complication or churn:** new, recurring, resolved or ongoing; first observed time,
  anticipated risk or newly observed/surprise unknown, contradicted assumption, evidence,
  cause versus hypothesis, response/owner, actual effort and delay impact. Distinguish
  productive repair from activity without acceptance advancement. Partial counts are lower
  bounds with coverage; missing telemetry is insufficient evidence, not zero rework.

These are coverage prompts, not mandatory repeated headings or prose blocks. An unchanged
material blocker still belongs in its row; expand only if its current effect needs explanation.
Health assessment and corrective suggestions do not authorize resumed execution.

## Development process recommendations

Give evidence-backed process changes once, distinct from routine remaining technical tasks.
For each, name the concrete change, repository/path or practice, owner, observations and
coverage, affected IDs, expected effect/tradeoff and a check for whether it helped. Mark
proposed versus adopted and link the owning plan/decision. Consolidate shared causes and
link the exception above instead of repeating its diagnosis. If none is warranted, state
“No development process change recommended” with the evidence coverage or reason.
Recommendations do not authorize editing, dispatch or execution.

## Supported metrics and sources

Include metrics only with native observations, population, denominator, period and coverage.
Readiness needs native acceptance units and defensible weights; otherwise use qualitative
judgement or unknown. Formal accepted outcomes / selected outcomes is a closure count,
not effort completion. Do not silently exclude difficult unknown outcomes from aggregates.
Retain actual versus estimated effort, original failures and scope changes. Report bytes
separately from effort/usage; bytes alone establish no savings.

Link the authoritative roadmap, owning evidence, previous report and estimation method/revision.
Record source revision, observation time and coverage beside claims or in one shared source
entry with named affected IDs. Link detailed history rather than copying it. Keep private raw
records and credentials out. State calibration results only for comparable resolved forecasts
with population/error/coverage; otherwise mark calibration unavailable in shared gaps. Name the
next re-estimation trigger without scheduling automation.
