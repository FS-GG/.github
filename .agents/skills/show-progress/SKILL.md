---
name: show-progress
description: Report roadmap progress, complications, effort and forecasts; explicit invocations save and PR/merge timestamped reports.
---

# showProgress

Report how far the selected roadmap outcomes have progressed, what changed, what became
harder, and when completion is plausible. The canonical invocation is `$show-progress`;
`showProgress` is the display name. Reporting does not resume roadmap execution.

## Scope and evidence

1. Resolve the FS-GG `.github` repository from the workspace or its existing configuration.
   `/docs/reports/progressReports` means that path **inside the repository**, never the
   operating-system `/docs`. Preserve the active branch, index and other workers' files.
2. Default to Unified Roadmap v3's declared outcomes and a trailing 24-hour window. Keep
   games and conditional scope separate unless selected. Use stable feature IDs, owners,
   acceptance boundaries and original operation identities; do not hard-code a row count.
3. Record one UTC `as_of` cutoff, window start, generation time and display timezone.
   Read the current roadmap, relevant owning-plan sections, accepted owner evidence and
   existing native results. Use available read-only sources for material current claims.
   Record each source's revision, observation time and coverage. A local ref or handoff
   may be stale: say which source was used. Do not launch tests, jobs, releases or workers
   to produce a status report, and do not interrupt an active owner for reporting alone.
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

- **Accepted status:** completed acceptance assertions and the remaining boundary; distinguish
  source, qualification, publication, installed use, activation and whole-outcome closure.
- **Readiness:** a supported percentage/range with its method, denominator, weights and
  confidence; preserve formal Done separately. Use an explicitly provisional judgement
  range when evidence supports only a rough estimate, or unknown when it supports neither.
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

Use [report-template.md](references/report-template.md) for the saved report. Fill every
selected item; use explicit unknown/not-applicable states instead of silently omitting
inconvenient fields. Keep the overview scannable and put detailed estimates and evidence
in each item's section. Include a visible complications summary even when none was observed.

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

Return the saved path, actual PR/merge result, overall accepted status, defensible approximate
movement, the most important forecast revisions and blockers. Reporting does not authorize
notifications, roadmap status changes or resuming roadmap execution.
