# Settled usage and review census, 2026-10-23 to 2026-10-24 (continuation)

Issue: [FS-GG/.github#4639](https://github.com/FS-GG/.github/issues/4639), the Outcome for routine
activity, review and usage coverage. This report continues the
[#4823 census](2026-10-23-usage-census-4823.md), which covers
`[2026-10-19T18:19:36Z, 2026-10-23T00:00:00Z)`. The scope of unsupported counters is set by the
[root decision of 2026-10-24](https://github.com/FS-GG/.github/issues/4639#issuecomment-6385678471):
counters the provider does not support are reported permanently as **Unknown** and are not acceptance
prerequisites. Census run on 2026-10-24 by fdev.

The method, readers, review rules and redaction rule are those of the #4823 report. This report states
only what differs. It adds no store, no collector and no attribution that the #4823 census did not use.

## Coverage of the settled population

| Segment | Window (end exclusive) | Merged PRs | Project 3 items closed | Report |
|---|---|---|---|---|
| Census #4823 | `[2026-10-19T18:19:36Z, 2026-10-23T00:00:00Z)` | {n_old} | {p3_old} | [report](2026-10-23-usage-census-4823.md) |
| This continuation | `[2026-10-23T00:00:00Z, 2026-10-24T18:00:00Z)` | {n_new} | {p3_new} | this report |
| Together | `[2026-10-19T18:19:36Z, 2026-10-24T18:00:00Z)` | {n_all} | {p3_all} | |

- **No gap and no overlap.** The new window starts at the instant the #4823 window ended. A merged PR or
  closed item belongs to exactly one segment. No Actions run is assigned to a member of both segments.
- **Start.** The start is the merge of [#4711](https://github.com/FS-GG/.github/pull/4711), as set by the
  [rescope](https://github.com/FS-GG/.github/issues/4639#issuecomment-6359589160). Work before it is **not
  covered**. Native usage on the coordinator host is retained only from 2026-10-19 (Codex rollouts) and
  2026-10-22 (Claude Code transcripts), so earlier native usage is permanently **Unknown**.
- **End.** The end is a fixed cutoff, `2026-10-24T18:00:00Z`. Anything merged or closed later is outside
  this report, and every later extension needs its own cutoff. The population is settled at the cutoff:
  every matched Actions run and job was `completed` when read.
- **Run time.** Population and review evidence were read on 2026-10-24 after 18:30Z. Review
  statements added to a PR after that read are not seen.

## Window and population

- **Window:** `[2026-10-23T00:00:00Z, 2026-10-24T18:00:00Z)`.
- **Merged PRs:** {n_new} PRs in FS-GG organization repositories had `mergedAt` inside the window. Each of the
  {repo_count} organization repositories was enumerated through GraphQL `pullRequests(states: MERGED)`.
  The organization-wide search count for the same start, taken after the frozen read, was 95: the {n_new} members
  plus 7 PRs merged after the cutoff. By repository: {by_repo}.
- **Project 3 items closed:** {p3_new} of the {p3_vis} items visible on Project 3 have content whose
  `closedAt` falls in the window. All {p3_new} are issues closed as `COMPLETED` with Status `Done`.
- **Frozen member lists.** Digests are defined as in the #4823 report:
  - PRs: digest `{pr_digest}`.
  - Project 3: digest `{p3_digest}`.
- **Private repository.** One population repository is private. Its {n_priv} PRs are counted and are inside
  the PR digest. Their numbers, titles, SHAs, dates and review wording are redacted here, and they are
  listed last so that their position does not reveal a timestamp. The placeholder labels
  (`private-repository-member-N`) are per file: the same label in the #4823 evidence names a different PR.
  No private Project 3 item closed in the window.

## Results

| Measure | This window | Together with #4823 |
|---|---|---|
| Route marker | {mk_new} | see the previous report |
| Exact-head independent verdict | {V_new} of {n_new}. The other {U_new} are **unsampled**: {rv_a} state a verdict without naming the head, {rv_e} record a verdict only at an earlier head, and {rv_n} have no independent verdict in the PR record (8 of these mention an independent review or verification and give no verdict). | {V_all} of {n_all}. {U_all} are **unsampled**. |
| Required checks on final head | {req_new} | {req_all} |
| Actions job time, PR phase | {pr_runs} runs, {pr_jobs} jobs, {pr_h} of job time | {all_pr_runs} runs, {all_pr_jobs} jobs, {all_pr_h} |
| Actions job time, merge-commit pushes | {mg_runs} runs, {mg_jobs} jobs, {mg_h} of job time | {all_mg_runs} runs, {all_mg_jobs} jobs, {all_mg_h} |
| Open→merge elapsed | median {el_med}, 90th percentile {el_p90}, max {el_max}. This includes overlapping queues; it is not idle time or critical-path time. | not combined |
| Native tokens, coordinator host | Claude Code: {cl_resp} responses, {cl_total} tokens. Codex: {cx_resp} responses, {cx_total} tokens. | Claude Code: {all_clr} responses, {all_cl} tokens. Codex: {all_cxr} responses, {all_cx} tokens. |
| Tokens attributed to a member | {attr_total} tokens ({attr_share} of the Claude Code total) on {attr_n} PR. Everything else is **unattributed**. | {all_attr} tokens on {all_attr_n} PRs; everything else is **unattributed**. |
| Billed cost | **Unknown** | **Unknown** |
| Other hosts, including Home | **Unknown** | **Unknown** |

The combined columns add two disjoint segments. Token responses are counted once by provider response ID
in each segment, and a record belongs to the segment that contains its timestamp.

## Counter register

Each counter is either supported by a maintained reader or reported as Unknown. An Unknown is not zero.

| Counter | Status | Value or reason |
|---|---|---|
| Merged PRs and closed Project 3 items | Supported | Frozen by digest, above. |
| Exact-head independent verdict | Supported where the PR record carries one | {V_new} of {n_new}; the rest are unsampled, never presented as reviewed. |
| Required-check conclusion | Supported | The private repository is **Unknown** (its rules and head checks cannot be read). |
| Actions job wall time | Supported | Job wall time from the Jobs API. It is not billed minutes. Runs on force-pushed heads are reported separately, below. |
| Claude Code and Codex tokens, coordinator host | Supported | Strict reader at `.github` `61526e94`, deduplicated by provider response ID. |
| Tokens per member | Supported only where a branch join exists | {attr_n} PR in this window. Codex rollouts and Claude Code responses on `main` have no member join: **Unknown**. |
| Tokens per reviewer or critic, per issue | Unsupported | **Unknown.** No record separates them. |
| Usage on other hosts, including Home | Unsupported on this host | **Unknown.** |
| Billed cost | Unsupported | **Unknown.** No list-price estimate is given. |
| Human idle time and critical-path delay | Not measured | **Unknown.** Elapsed time overlaps queues. |
| Work before the #4711 merge | Not covered | **Unknown.** |
| Work after the cutoff | Not covered yet | Needs its own cutoff. |

## What differs from the #4823 method

### Review evidence

The rules are unchanged: a PR scores `V` only when one review statement or one dedicated review comment
names an independent review or critic, gives an explicit verdict, and binds it to the final head SHA.
Every PR whose record mentions a review was adjudicated by hand.

- {rv_e} PRs record a verdict only at an earlier head. Some of these are verdicts of REJECT or REQUEST
  CHANGES at an earlier head with no later verdict at the final head.
- For {rv_n} PRs the record has no independent verdict at all. Eight of them mention an independent review
  or verification and give no verdict for this PR's final head: Coordination#1057, Templates#760, #767 and
  #768, Rendering#1466, and .github#4850, #4851 and #4853. Templates#760 and Rendering#1466 refer to a
  review of other work.
- PR .github#4855 is the first member with **submitted GitHub reviews**: two `COMMENTED` reviews by the
  author account, the second a re-review of the final head with the verdict APPROVE. It scores `V` on
  that review. No other member has a submitted review.
- One account authored, merged and posted every review record. Review independence is therefore as the
  record asserts it.
- Reviews held only in linked issues or private custody are not visible here and count as unsampled.

### Required checks

The rules were read on 2026-10-24 from base-branch rulesets and classic protection. FS.GG.Game is a new
population repository with 19 required contexts. FS.GG.Coordination now returns its six required contexts
through a ruleset and returns "not protected" for classic protection. The private repository's rules and
head checks cannot be read (the rules API refuses for its plan, and the census credential is also
refused), so its {n_priv} rows are **unknown**. Changes to the rules during the window are **Unknown**.

### Actions machine time

1. The census listed all workflow runs created from 2026-10-22 to 2026-10-24 in each of the 8 repositories
   that have members: {all_runs} runs. Days at the listing cap were split into hourly slices.
2. Each run goes to at most one member by the #4823 rules: PR-phase runs by head SHA and head branch, and
   merge-commit runs only when the event is `push`. In this window every PR-phase run matched by head
   branch. {ms_merge_sha_nonpush_excluded} runs on merge SHAs were excluded because the merge did not
   cause them: {ms_issues} `issues`, {ms_schedule} `schedule`,
   {ms_workflow_dispatch} `workflow_dispatch` and {ms_dynamic} `dynamic`.
3. Every matched run's jobs were read with the Jobs API (`filter=all`, all attempts). All {pr_runs} plus
   {mg_runs} runs and all their jobs were `completed`. No run was unread and no job lacked a timestamp.
   Job time is the sum of `completed_at − started_at`. A job whose `completed_at` precedes its
   `started_at` counts as 0 s: {neg_pr} PR-phase jobs, {neg_mg} merge-push jobs and {neg_ex} jobs of the
   force-pushed-head runs below. The #4823 census applied the same rule
   without stating it.
4. **Runs on force-pushed heads.** {extra_runs} `pull_request` runs on the head branches of {extra_members}
   members ran inside the member's open interval, but on commits that the PR's current commit list no
   longer holds (rebases and force-pushes). The #4823 method leaves such runs uncounted; for #4823 the
   count was 36 and its job time was not measured. They are not in the table totals or member rows.
   Their Jobs API time is {extra_jobs} jobs and {extra_h} of job time, with the same rule. Adding it would raise the PR-phase
   figure of this window from {pr_h} to {pr_h_plus}.

### Native usage

The reader is the same source build at `61526e94`, run read-only on every retained transcript, with the
window `[2026-10-23T00:00:00Z, 2026-10-24T18:00:00Z)` and a receipt store outside the current
directory, every repository and the temporary directory. No receiver, telemetry store or UTEL database
was opened. The census-local join parser is the one #4823 used; it emits no content.

Reader outcomes:

| Runtime | Files | Read | No response in window | No responses at all | Withheld or refused |
|---|---|---|---|---|---|
{reader_rows}

Usage by model (coordinator host, in window, deduplicated; {dupes} responses appeared in two files):

| Runtime | Model | Responses | Input | Cached input | Cache-write input | Output | Total |
|---|---|---|---|---|---|---|---|
{model_rows}

Usage by session (hashed identity):

| Runtime | Session (SHA-256 prefix) | Threads | Responses | Total tokens | First response | Last response |
|---|---|---|---|---|---|---|
{session_rows}

Attribution. A response joins a member only when the `gitBranch` on its own record equals the PR's unique
head branch and its timestamp is no later than the merge:

| Member | Responses | Tokens |
|---|---|---|
{attr_rows}

The coordinator runtime changes twice in the window: Claude Code to 2026-10-23T15:22Z, Codex from
2026-10-23T14:54Z to 2026-10-24T12:35Z, and Claude Code again from 2026-10-24T14:32Z. The sessions overlap for
about half an hour on 2026-10-23, so the sum is not a count of one agent's work. The last Claude Code
response is the cutoff, not the end of that session. Almost all responses carry branch `main` and have no
member join.

## Gaps and Unknowns

- **Before the start and after the cutoff:** not covered, see above.
- **Other hosts, including the Home agent:** usage **Unknown**. This census did not read the Home UTEL
  store.
- **Billed cost:** **Unknown**. No list-price estimate is given.
- **Withheld or refused Claude Code subagent transcripts:** 2 subagent transcripts have a response with no
  completed usage record, so the reader withholds their totals. 2 subagent transcripts mix session
  identities and were refused. Those tokens are **Unknown**.
- **Pruned transcripts:** transcripts and rollouts deleted or pruned before the census are **Unknown**.
- **Attribution:** per-member token figures cover only the {attr_n} branch-joined PR. Per-issue tokens and
  reviewer or critic tokens are not separable.
- **Required checks:** these use rules read at census time. The private repository's rules and checks are unreadable.
- **Review:** independence rests on the record's own assertion.
- **Elapsed time:** this is not idle time or critical-path time. No savings or efficiency claim follows.
- **Project 3:** items removed from the project before the census, and reopened items, are not in the
  population.

## Reproduction

Native reads: GraphQL member enumeration, PR commits, comments and reviews; the Actions runs listing and
Jobs API; repository rules and branch protection; Project 3 items; and issue `ClosedEvent` closers. The
scripts and private per-response rows stay in fdev scratch custody on the coordinator host and are not
published. They are the #4823 scripts with the window, the repository list, the required-check lists and
the adjudication table changed. The compact per-member result is in
[`evidence/2026-10-24-usage-census-4639.json`](evidence/2026-10-24-usage-census-4639.json) as
column/row tables. It holds no session identities and no transcript content.
