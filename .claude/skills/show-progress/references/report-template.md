# Report structure

Use this structure as a starting point. Replace bracketed fields before saving; explicit
unknowns are valid completed fields. Keep unchanged item detail concise, but preserve the
acceptance boundary, effort and completion fields for every selected item.

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
method: "show-progress/2; concrete milestone comparison; [estimate basis]"
status: "[evidence snapshot / partial evidence snapshot]"
---
```

## Process health and expectations

Lead with a short verdict: is development advancing within the recorded plan or
forecast, at risk, blocked, recovering, churning, done, or too poorly observed to
judge? Name the affected scope and evidence coverage. Compare accepted outcomes and
useful intermediate milestones with the expected path; state material effort or
calendar revisions separately. Identify the most consequential recurring failure,
rework pattern, reopened decision, external wait or other complication, including
when it first appeared and its practical effect. Name the owner and next corrective
step or the evidence needed to diagnose it. If items differ, state that mixed picture
instead of assigning one unsupported programme-wide label. Missing baseline or
telemetry means the comparison is unavailable, not that delivery is healthy.

| Scope | Recorded expectation | Observed delivery and process | Verdict / confidence | Effect and next step |
|---|---|---|---|---|
| [item, shared dependency or selected programme scope] | [planned acceptance/effort/date and source, or unavailable] | [accepted gains; rework, failures, waits and coverage] | [health label; reason] | [effort/calendar change; owner and corrective step] |

## Overview

After the health assessment, show what improved since the last report, what is
already usable or accepted overall, and the most important remaining work. State
formal outcome closure separately from intermediate progress. Summarize evidence
coverage, critical path, health/churn and material
estimate changes. Keep percentage comparisons secondary; missing percentages do not erase
concrete achievements. Explain shared evidence gaps once, with item-specific limits inline.

| Item / owner | Since last report | Done overall | Still to do | Remaining effort / complexity / uncertainty | Health / estimate change |
|---|---|---|---|---|---|
| [stable ID] | [before → after; practical effect; evidence] | [accepted capabilities/milestones; distinguish prepared work] | [named tasks in dependency order] | [working range + adverse scenario; named units; complexity/reason; confidence/coverage] | [label; reason; effort and calendar revision; corrective next step] |

Use On track, At risk, Blocked, Recovering, Churning, Done or Insufficient evidence.
Health includes process churn, not just blockers. Keep cells concise and link to detail.
Missing historical estimates are “first estimate”; missing comparison evidence identifies
exactly which change is unobserved, rather than implying no progress.

## What changed since the last report

Show concrete before → after changes with event times, evidence and what they enable.
Include implementation, fixes, qualification, publication, installation and acceptance;
distinguish proposals/ongoing runs from completed work and whole-outcome closure. Include
regressions and corrected claims. Separate scope changes and the trailing 24-hour comparison
when its baseline differs. If no earlier report exists, call this the first snapshot.

## What is done overall

Group evidenced capabilities and completed milestones by outcome. State what works or is
accepted now, including intermediate milestones in open outcomes. Name the remaining whole-
outcome acceptance boundary. Refer to the main table for unchanged details rather than
repeating the same catalogue in full.

## Remaining work and estimates

Expand only where the main table cannot explain the remaining path. Order tasks by priority
or critical path, with dependencies and named effort units. Give working/adverse ranges,
complexity and its reason, confidence and coverage beside each estimate. Separate active
work from hosted execution and calendar waiting. Count shared work once; identify affected
outcomes. Where estimation is unsupported, name the investigation that would bound it.

## Health, complications and churn

| Item | Health / confidence | Evidence since last report | Cause / next corrective step / owner | Effort revision | Completion revision |
|---|---|---|---|---|---|
| [ID] | [label; confidence and reason] | [repair/review rounds, recurring failures, reopened decisions, unplanned fixes versus accepted milestones; period and coverage] | [root cause known/hypothesis; action; first observed; anticipated/unknown] | [previous → current range; added/saved effort; first estimate if absent] | [delay/recovery or conditional forecast; waiting separate] |

Distinguish productive iteration from repeated fixes without acceptance advancement. Retain
accepted gains during repair. Quantify rework only when observed; incomplete counts are lower
bounds with coverage stated. Show new, recurring, resolved and ongoing complications. State
“no new complications observed in the reviewed evidence” only for adequately covered items.
Health and proposed corrective steps do not authorize resuming execution.

## Item detail — repeat for each selected item

### [Stable ID — name]

- Owner, scope and definition of done: [owning plan / accepted population].
- Evidence and freshness: [revision, observed UTC, source links; stale/missing coverage].
- Since previous report: [before → after; effect and evidence].
- Done overall: [accepted capabilities/assertions; prepared work distinguished].
- Remaining work: [named tasks, dependencies, acceptance steps].
- Complexity: [low/medium/high per task; technical reason].
- Health: [label; confidence, churn evidence/coverage and next corrective step].
- Readiness basis: [units, weights, earned credit or provisional rationale/confidence].
- Window movement: [delivered progress]; scope/evidence/method adjustment: [separate].
- Original total effort: [value/unit/date/scope or unavailable].
- Previous total forecast: [value/unit/date or unavailable].
- Actual effort: [value/unit/coverage or unknown]; ETC: [working range + plausible adverse scenario; basis and inline confidence/coverage].
- Current total forecast: [AC + ETC when supported]; change: [absolute/% or unknown].
- Completion: [start assumption, remaining duration, date range, timezone, confidence].
- Prior completion forecast → current: [date shift and reason or first forecast].
- Dependencies, available capacity and calendar: [shared work allocation and bottleneck].
- Complications: [unexpected/known/none observed/unknown; evidence and status].
- Next forecastable milestone and reopening condition: [action, owner and conditional ETA].

## Forecast assumptions and uncertainty

Document the data/cohort, exclusions, unfinished work, scope weights, actual-effort
coverage, schedule dependencies, capacity, working calendar, contingency and confidence
meaning. Explain why a requested estimate cannot be produced where necessary. Distinguish
a whole-item ETA from an intermediate milestone. Shared dependencies are counted once.

## Calibration and next update

Compare forecasts that have resolved with actual outcomes, when available: population,
signed/absolute error, interval coverage and scope changes. Otherwise say calibration
is unavailable. State the evidence or milestone that should trigger re-estimation; this
does not schedule an automatic task.

## Sources

Link the authoritative roadmap, owning evidence, comparison reports and estimation
method and its revision. Include relevant research links when introducing or changing the method. Keep
private raw records and credentials out of the repository report.
