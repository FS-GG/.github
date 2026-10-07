---
name: stop-with-handoff
description: "Stop active FS-GG roadmap workers, save a timestamped restartable report in docs/handoffs, land the handoff through the normal PR route, and end work when a session stop or save is requested."
---

# Stop With Handoff

Stop programme execution immediately and preserve enough verified state for a
later [$continue-from-handoff](../continue-from-handoff/SKILL.md) invocation.
Creating, editing, or inspecting this skill does not stop another task.

## Stop execution before saving

Cancel new roadmap dispatches, source edits, repair pushes, publications, and
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

Close private observations only when their actual attempts are terminal, using
the real completed, failed, or cancelled outcome; reconcile available usage.
Keep coverage gaps explicit. Mark an existing programme goal paused when the
runtime supports it. A stop request does not mean the roadmap is complete.

## Save a restartable handoff

Resolve the owning FS-GG `.github` repository. `/docs/handoffs` means its
repo-relative `docs/handoffs` directory; create that directory if absent.
Use `YYYY-MM-DD-HHMMSS-unified-roadmap-handoff.md` with current UTC time. Never
overwrite an earlier report; add a suffix if an exact name already exists.
Read the latest handoff and current authoritative state to retain unresolved
obligations without copying stale claims.

Write the smallest complete report containing:

- Stop timestamp, original programme objective, latest user priorities and
  authorization, paused/incomplete status, and canonical
  [v3 roadmap](../../../docs/roadmaps/2026-10-07-unified-development-roadmap-v3.md),
  [games roadmap](../../../docs/roadmaps/2026-10-07-games-development-roadmap.md) and skill links.
  Preserve older v2 references as archive lineage, not current scheduling authority.
- Verified progress with exact source revisions, PRs, native runs, publication
  and receiver results; distinguish source delivery from installed behavior.
- Each lane's owner, working/waiting/stopped state, next concrete action, and
  the acceptance signal or dependency that unlocks it.
- Branch/worktree locations, dirty or interrupted files, open PR/check state,
  pending source packets and progress projections, and independent parallel
  opportunities for the successor.
- Failures, unresolved causes, actual attempt outcomes, operation consumption,
  cleanup status, immutable evidence access paths/digests, expired admission,
  and the constraints needed to avoid unsafe or duplicate retries.
- A short ordered restart plan and the remaining gates to actual completion.

Apply [agent-handoff](../agent-handoff/SKILL.md) and
[technical-writer](../technical-writer/SKILL.md) for evidence and writing quality.
Keep credentials, raw telemetry, private payloads and unnecessary personal
identifiers out of the committed report. Private evidence paths can identify
where authorized successors should inspect it; do not copy their contents.

## Land the save and end the session

Invocation authorizes the handoff-only commit, push, PR and merge, unless the
user explicitly asks for a local-only save. Use an isolated branch/worktree
from current protected main when the working checkout contains unrelated work.
Preserve that work. Validate Markdown links and `git diff --check`; retain the
normal native required checks and branch protection.

For programme PR admission and delivery, use the existing
[work-programme](../work-programme/SKILL.md) controls without restarting
its implementation loop. Use one handoff PR, existing campaign identity, exact
head, and both normal PR markers. Read the live PR queue; do not bypass its cap
or merge unrelated feature work merely to make room for the save. If admission,
credentials, checks or infrastructure prevent landing, preserve the local
file and any pushed branch, state the exact remaining boundary, and stop rather
than resuming roadmap work. Do not call a pending PR merged.

After merge, read back the saved file and full merged tree. Return the session
checkout to updated main when safe; otherwise preserve its branch and dirty
state and report why it was retained. Do not reset, clean, or overwrite unrelated
work to return to main. The save itself is
the only new work allowed after shutdown, apart from necessary cleanup and its
native delivery checks. Do not select a next item or continue monitoring feature
lanes. End with the saved path/link, PR and merge status, and any unresolved
running job or cleanup. State that programme execution is paused and stop.
