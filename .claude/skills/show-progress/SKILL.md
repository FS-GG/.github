---
name: show-progress
description: Delegate roadmap process-health and expectation checks, progress, effort and forecasts; explicit invocations save and PR/merge timestamped reports.
---

# showProgress

Assess whether roadmap development is healthy, progress matches recorded expectations,
and recurring failures or rework signal a process problem. Then report what changed,
what is accepted, what remains and when completion is plausible. The canonical
invocation is `$show-progress`;
`showProgress` is the display name. Reporting does not resume roadmap execution.

Lead the report with a concise process-health verdict. Compare actual acceptance,
rework, waiting and forecast movement with the recorded plan or previous forecast;
name material deviations, their causes or unknowns, the affected outcomes and the
next corrective step. Distinguish a productive repair from repeated activity without
acceptance progress. If no comparable baseline or sufficient observations exist,
say so before judging health. Place concrete progress and item details after this
opening assessment; do not let percentages or PR counts stand in for process health.

## Delegate an explicit report

On an explicit reporting invocation, first start one dedicated subagent and give it the
requested scope, this full skill and its references, the repository location, and any
current source or reservation pointers. The subagent owns the entire report: evidence
collection, estimates, validation, saving, and, when authorized below, PR delivery and
merge readback. The main thread continues its existing work without waiting for or
polling the report. Do not start another reporting worker or interrupt an active owner.
If delegation is unavailable, state that the report has not started; do not silently run
this reporting workflow in the main thread.

The subagent should send no progress messages or separate completion report to the
main thread. It must still return the minimal terminal response required by the agent
runtime, with the saved path and actual delivery state. The runtime may deliver that
completion to the parent automatically; this skill cannot suppress or guarantee that
such a notification will not affect the parent's turn. The parent need not react to
the completion unless it contains a blocking exception or the user asks for status.
An ordinary chat question about progress stays in the current conversation; do not
spawn a worker or save a report unless the user invokes reporting or requests saving.

## Scope and evidence

1. Resolve the FS-GG `.github` repository from the workspace or its existing configuration.
   `/docs/reports/progressReports` means that path **inside the repository**, never the
   operating-system `/docs`. Preserve the active branch, index and other workers' files.
2. Default to Unified Roadmap v3's declared outcomes. After the opening health verdict,
   show changes since the newest earlier report; separately show the trailing 24-hour
   comparison when its cutoff differs. Keep
   games and conditional scope separate unless selected. Use stable feature IDs, owners,
   acceptance boundaries and original operation identities; do not hard-code a row count.
   Give every selected outcome one current-state row, including done, blocked, unchanged
   and poorly observed outcomes. Compare the selected IDs with the table before saving.
3. Record one UTC `as_of` cutoff, window start, generation time and display timezone.
   Read the current roadmap, relevant owning-plan sections, accepted owner evidence and
   existing native results. Use available read-only sources for material current claims.
   Record each source's revision, observation time and coverage. A local ref or handoff
   may be stale: say which source was used. Beyond the dedicated reporting subagent,
   do not launch tests, jobs, releases or workers to produce status evidence, and do not
   interrupt an active owner for reporting alone.
4. Find the newest earlier progress report by its embedded UTC cutoff, not filesystem
   mtime. For a 24-hour comparison use evidence at or before the window start; separately
   show change since the previous report when its cutoff differs. A later observation
   cannot silently stand in for the earlier baseline. Reconstruct from contemporaneous
   handoffs/native events only where supported; otherwise mark the delta unavailable.
5. Distinguish observed facts, owner reports and analyst estimates. Retain failures,
   pending checks, unknown effects and unobserved consumers. Missing evidence means
   unknown, not zero or unchanged. Newer evidence discovered during writing either gets
   excluded after the cutoff or causes one explicit cutoff/window refresh for the report.

## Estimate progress, effort and time

Read [estimation-method.md](references/estimation-method.md) before assigning numbers.
Use [research.md](references/research.md) for the method's prior art and limitations;
refresh research when the method changes, rather than browsing anew on every report.

For every in-scope item provide:

- **Concrete work:** what changed since the last report (before → after and practical effect),
  what is already done overall, and named remaining tasks. Retain evidenced implementation,
  repair, qualification and publication milestones even when whole-outcome closure is unchanged
  or percentage baselines are missing. Distinguish prepared work from accepted capabilities.
- **Accepted status:** completed acceptance assertions and the remaining boundary; distinguish
  source, qualification, publication, installed use, activation and whole-outcome closure.
- **Readiness:** a supported percentage/range with its method, denominator, weights and
  confidence; preserve formal Done separately. Use a numerical readiness range only with
  existing native acceptance units and a defensible denominator; otherwise give a qualitative provisional judgement with its
  rationale/confidence, or unknown. Do not create weights solely to fill the report.
- **Change:** previous/current readiness and percentage-point movement on the same scope;
  separate delivered progress from changed scope, corrected evidence and revised weights.
- **Effort:** original total baseline, previous total forecast, current remaining effort,
  current total forecast and their changes, in named units with coverage. Distinguish
  engineering effort, agent execution, hosted compute and elapsed waiting. Unknown actual
  effort prevents a defensible actual-plus-remaining total; still estimate remaining work
  from a stated task breakdown when possible. Do not invent an earlier estimate.
- **Completion forecast:** remaining active work, calendar duration/date range, dependencies,
  capacity/calendar assumptions, confidence and change from the previous forecast. Prefer
  comparable historical evidence. Quantiles require a real model/data basis. Otherwise
  label a low-confidence scenario range. If an external blocker has no resolution date,
  give conditional time after unblock and the next forecastable milestone, not a firm date.
- **Complications:** new/unexpected, recurring, resolved or ongoing; first observed time,
  prior assumption contradicted, evidence, cause, response, owner, effort impact and delay.
  Classify surprise relative to the recorded baseline; use “newly observed; surprise
  unknown” if no earlier risk record exists. Explain estimates that fall as well as rise.
- **Complexity and uncertainty:** rate each remaining task low/medium/high with a technical
  reason. Give a working effort range and a plausible adverse scenario with assumptions,
  confidence and evidence coverage directly beside the estimate. Missing earlier estimates
  mean “first estimate,” not unchanged. Unknown actual totals do not prevent a supported
  remaining-work estimate; when even that is unsupported, name the bounded investigation
  needed to estimate it. Do not invent probability percentages for judgement ranges.
- **Health / estimate change:** use On track, At risk, Blocked, Recovering, Churning, Done
  or Insufficient evidence, with reason, assessment confidence, effort/forecast revision and
  next corrective step. Assess process churn as well as delivery: repeated repair/review
  rounds, recurring failures, reopened decisions and unplanned fixes against newly accepted
  milestones. Report available counts, periods and rework effort/coverage. Multiple small
  fixes are productive iteration when each removes a verified obstacle; activity without
  acceptance advancement is a churn signal, not proof by itself. Missing telemetry means
  insufficient churn evidence, not healthy or zero rework. Health is a work-item assessment,
  not a worker score, and reporting does not authorize corrective execution.

Add a **Development process recommendations** section after the complications and churn
summary. Recommend a change to how work is planned, reviewed, built, qualified or
delivered only when observed evidence supports a material process gap. Distinguish a
process change from the next technical task needed to finish an item. For each
recommendation, name the concrete change, the repository and file/skill/workflow or
operating practice where it belongs, its accountable owner, the observations and
affected outcomes, the expected effect and tradeoff, and a check for whether it helped.
Mark proposed versus already adopted changes and link an existing owning plan or
decision when available. Consolidate shared causes instead of repeating one change
per item. When no process change is warranted, state that conclusion with its evidence
coverage. Reporting a recommendation does not authorize its execution.

Always account for a shared dependency once in programme effort and scheduling, while
showing all affected outcomes. Never extrapolate an ETA from a readiness percentage or
assume parallel agent count translates into equivalent delivery capacity.

## Write the report

An explicit reporting invocation of this skill authorizes saving the report and automatically
committing it, opening a PR and merging after required checks pass. Complete that delivery
without asking for separate landing confirmation, unless the user limits the request to a
draft, local save or another narrower scope. For an ordinary chat question, answer in chat
unless saving is requested; automatic skill selection alone does not authorize saving or
landing. A request to modify this skill does not itself invoke reporting or schedule automation.

Use [report-template.md](references/report-template.md) for the saved report. Open with a
brief process-health verdict against expectations, then one current-state table with one
row per selected outcome. Each row carries accepted capability and remaining acceptance
boundary, meaningful change, remaining work/blocker, comparable effort and completion
forecast, health/confidence and evidence links. Link the previous report visibly.

Expand only changed claims or material exceptions: expectation or scope changes,
complications, recurring failures, unknown effects and estimates needing explanation.
Do not repeat an outcome's history, accepted work or forecast in multiple sections.
Unchanged rows still state current acceptance and evidence freshness; link prior detail
without treating a historical observation as a fresh check. Record shared evidence gaps
and assumptions once with named affected IDs; rows reference those gaps and retain their
specific uncertainty. An explicit shared unknown applies to each named item; silence does
not. Preserve every applicable estimation field through a row, shared statement or linked
exception. Metrics require native observations, a defined denominator and coverage; omit
unsupported ratios and mark missing measurements unknown. Report bytes alone prove no
cost or effort saving. Retain the development process recommendations verdict once;
link any shared cause instead of repeating it in item details.

Save as `docs/reports/progressReports/YYYY-MM-DD-HHMMSSZ-unified-v3-progress.md`, using the
actual generation UTC time. Create the directory if absent. Use exclusive creation; on
a same-second collision append `-02`, `-03`, etc. Preserve older reports. A draft may be
assembled privately, then written as one complete file; do not replace a report written
by another caller. No separate database, service, dashboard or mandatory sidecar is needed.

Before saving, check arithmetic, percentage-point versus relative changes, interval
ordering, item coverage, relative links, timestamps, comparable baselines and source
freshness. Ensure completed rows have acceptance evidence and open rows do not become
100% by rounding. Remove secrets, private raw logs, usernames and local credential paths;
link public or repository evidence where possible and describe unavailable private proof.

## Land the report

After validating and saving a report from an explicit reporting invocation, use the repository's
current delivery route to make one focused report-only commit and PR from the current default
branch, then merge at the first green required-check state. Required delivery CI is permitted;
the restriction on launching work to produce status evidence still applies. Preserve the active
branch, index and unrelated files, using an isolated checkout when needed and permitted by the
conversation. Do not include unrelated changes or bypass required checks or branch protection.
Fix report-related failures in the same PR. If delivery is blocked by access, an unrelated CI
failure or a conversation-specific limit, retain the saved report and any PR, report the blocker
and actual delivery state, and stop rather than claiming a merge or retrying indefinitely.

The delegated owner retains the saved path, actual PR/merge result, overall accepted
status, defensible approximate movement, important forecast revisions and blockers in
the report and its minimal terminal response. Reporting does not authorize
notifications, roadmap status changes or resuming roadmap execution.
