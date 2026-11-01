---
name: stop-with-handoff
description: "On session stop/save, retire owned work and preserve missing recovery state; use durable issue and Atlas pointers when sufficient."
---

# Stop With Handoff

Stop programme execution immediately and preserve enough verified state for a
later [$continue-from-handoff](../continue-from-handoff/SKILL.md) invocation.
Creating, editing, or inspecting this skill does not stop another task.

Include delivered Atlas IDs and any unfinished capture gap/owner in the restart pointer;
follow [$atlas](../atlas/SKILL.md) without restarting capture work after shutdown.

## Stop execution before saving

Stop new board selections and dispatches, source edits, repair pushes, publications, and
operation admissions as soon as the stop request arrives. List active workers
and interrupt them; tell each owner to preserve partial files and perform no
new work. Collect available state without waking idle workers or dispatching
a new agent merely to write the report.

Observe already-running jobs and processes. Retire only owned work whose actual
identity and cleanup route are established; preserve terminal status, partial
evidence and cleanup outcomes. Do not kill from stale numeric PIDs or broad
process-name matches. A protected publication or operation already in flight
may need its existing safe retirement/recovery path: do only that necessary
cleanup, and record any external job that cannot be stopped. Never claim all
external work stopped when its state is unknown.

Before stopping, push review repairs and in-progress work to the owning PR branch, so a resume starts
from durable state instead of patches saved off the branch or in tmpfs. A draft or WIP commit is enough.
Push only that branch: never the default branch, never with force. A WIP head is unreviewed and merges
nothing; the PR keeps its normal review, rebind and required checks. Work with no PR yet goes to a new
`routine/<item-slug>` branch with a draft PR that `Refs` its Delivery. This pushes work already made and
starts no new repair, so it does not breach the stop above. Put the pushed head in the restart pointer. If
a push cannot land, record the worktree path and the blocker as a recovery gap.

Close private observations only when their actual attempts are terminal, using
the real completed, failed, or cancelled outcome; reconcile available usage.
Keep coverage gaps explicit. Mark an existing programme goal paused when the
runtime supports it. A stop request does not mean the roadmap is complete.

## Reconcile durable state and save only recovery gaps

Use the board, existing owning issues and native PR/operation evidence for scope, ownership,
accepted progress, blockers and next actions. Use Atlas for curated knowledge and stable retrieval
pointers. Reconcile changed facts there within existing authority; do not duplicate them in a
session report or create another current-status ledger. A stop does not authorize new enrollment,
planning-field changes or retries of uncertain effects.

Preserve the accepted Project 3 cutover and each selected product's qualified binding/caller scope.
Source delivery still does not adopt a new executable/caller or change a binding/selector. Do not
revive V1 fallback or manual status upkeep. Keep exact
outcome/slice/attempt and owner, existing PR/head, issue/body/plan revisions, pending requests and
original/uncertain reservations. A successor refreshes determining native/action/intake/runtime facts
through canonical work-board before resuming. Saved inspections and stdin digests renew no authority;
missing facts stay scoped Unknown. Never settle or retry original effects from board status.

Check whether durable records and evidence pointers cover the restart. Recovery gaps can include:

- branch/worktree locations, dirty or interrupted files and unpublished source;
- running jobs, actual process identities, cleanup outcomes and external work that remains Unknown;
- original consumed/uncertain operations, partial evidence, expired admission and retry constraints;
- private evidence access paths/digests and pending reply channels, identities and deadlines;
- scope or authorization constraints and the next safe action that are absent from the owning issue.

If nothing is missing, return a restart pointer to the owning issue(s), relevant Atlas IDs and any
original operation evidence. No new handoff file, commit or PR is required. Old timestamped reports
remain history; a successor need not read one merely because it is the newest file.

If a gap remains, preserve only that missing recovery information, with links to established facts.
Use an existing appropriate durable record when it can safely hold the information. When a separate
report is needed, resolve the owning FS-GG `.github` repository and write repo-relative
`docs/handoffs/YYYY-MM-DD-HHMMSS-unified-roadmap-handoff.md` using current UTC time. Create the
directory if absent; never overwrite an earlier report. Read earlier reports only as needed to retain
unresolved obligations. Include the stop timestamp, unresolved state and owners, evidence pointers,
Atlas capture gaps and the short ordered restart action; do not copy the board or accepted history.

Apply [agent-handoff](../agent-handoff/SKILL.md) for evidence and writing quality. Use the optional
`$technical-writer` skill when available; its absence does not block saving. Keep credentials, raw telemetry,
private payloads and unnecessary personal identifiers out of public records. A private evidence path
can identify where an authorized successor should inspect; it does not prove persistent custody.

## Deliver any required save and end the session

When a recovery gap requires a repository change, invocation authorizes only that save's commit,
push, PR and merge, unless the user explicitly asks for a local-only save. Use an isolated branch/worktree
from current protected main when the working checkout contains unrelated work.
Preserve that work. Validate Markdown links and `git diff --check`; retain the
normal native required checks and branch protection.

For programme PR admission and delivery, use the existing
[work-board](../work-board/SKILL.md) controls without restarting
its implementation loop. Use at most one save PR, the existing campaign identity, exact
head, and both normal PR markers. Read the live PR queue; do not bypass its cap
or merge unrelated feature work merely to make room for the save. If admission,
credentials, checks or infrastructure prevent landing, preserve the local
file and any pushed branch, state the exact remaining boundary, and stop rather
than resuming roadmap work. Do not call a pending PR merged.

If a save PR was needed and merged, read back the saved file and full merged tree. Return any
checkout moved solely for that save to updated main when safe; otherwise preserve its branch and dirty
state and report why it was retained. Do not reset, clean, or overwrite unrelated
work to return to main. The save itself is
the only new work allowed after shutdown, apart from necessary cleanup and its
native delivery checks. Do not select a next item or continue monitoring feature
lanes. End with one durable restart pointer: owning issue links, Atlas IDs and any gap-report path,
plus PR/merge status only when a save PR was needed. State any unresolved running job or cleanup,
that programme execution is paused, and that no background monitoring is implied. Stop.
