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
method: "show-progress/1; [weighted acceptance or provisional judgement]"
status: "[evidence snapshot / partial evidence snapshot]"
---
```

## Overview

State accepted outcomes/count, approximate readiness if defensible, window movement,
coverage, biggest achievements, current bottleneck and material forecast changes. Name
the population and weighting. Explain unknowns that could change the conclusion.

| Item / owner | Accepted state | Readiness now | Prior → now / Δ pp | Remaining effort / confidence | Completion forecast / change | Main dependency |
|---|---|---|---|---|---|---|
| [stable ID] | [scope reached] | [range / unknown] | [comparable change or unavailable] | [range, named units, basis] | [relative + dates; conditional if blocked] | [dependency] |

## What changed during the window

List delivered, qualified, published, installed and accepted milestones with their
actual event times and evidence. Separate work completed from source proposals and
ongoing runs. Include regressions, removals and corrected earlier claims. Show scope
changes separately from progress on unchanged scope.

## Complications and estimate revisions

| Item | First observed / expected? | Complication and evidence | Response / current status / owner | Added or saved effort | Calendar impact | Previous → current total estimate |
|---|---|---|---|---|---|---|
| [ID] | [UTC; unexpected/known/unknown] | [cause or hypothesis] | [action and reopening condition] | [units/range or unknown] | [observed/forecast; overlap treatment] | [EAC and Δ; unknown if missing] |

Also state “no new complications observed in the reviewed evidence” for covered items
without new issues; do not turn missing coverage into that claim.

## Item detail — repeat for each selected item

### [Stable ID — name]

- Owner, scope and definition of done: [owning plan / accepted population].
- Evidence and freshness: [revision, observed UTC, source links; stale/missing coverage].
- Accepted work: [completed assertions]; remaining work: [named acceptance steps].
- Readiness basis: [units, weights, earned credit or provisional rationale/confidence].
- Window movement: [delivered progress]; scope/evidence/method adjustment: [separate].
- Original total effort: [value/unit/date/scope or unavailable].
- Previous total forecast: [value/unit/date or unavailable].
- Actual effort: [value/unit/coverage or unknown]; ETC: [low/likely/high or range/basis].
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
method. Include relevant research links when introducing or changing the method. Keep
private raw records and credentials out of the repository report.
