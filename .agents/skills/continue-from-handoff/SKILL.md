---
name: continue-from-handoff
description: "Resume FS-GG work from the newest timestamped docs/handoffs report, reconcile live state, and finish the Unified Roadmap autonomously through work-programme when continuation is requested."
---

# Continue From Handoff

Resume the saved programme and work the roadmap to its actual acceptance outcomes.
Creating, editing, or inspecting this skill does not start programme execution.

## Recover the newest saved state

Resolve the FS-GG `.github` repository from the current checkout or its existing
remote/workspace configuration. `/docs/handoffs` means `docs/handoffs` inside
that repository, not the operating system's `/docs` directory. Preserve dirty
checkouts and interrupted worktrees; fetching current default-branch refs does
not authorize resetting, cleaning, or overwriting them.

List Markdown handoffs locally and on the current protected default branch.
Select the newest by its recorded UTC timestamp, not filesystem modification
time. Accept existing `YYYY-MM-DD-HHMM` and new `YYYY-MM-DD-HHMMSS` filenames;
use a document's explicit UTC timestamp for other names and the full path to
break equal-time ties. Prefer the protected copy when the same report exists
both locally and remotely. A genuinely newer local report can preserve unsaved
work: identify it as unmerged and verify its claims. Read the selected report
fully and report its path, timestamp, and source revision. If none exists,
record the missing handoff and recover from the canonical roadmap and live work
rather than waiting for human input.

Reconcile its open PRs, current heads/checks, merged trees, publication and
receiver state, outstanding progress projections, partial files, and worker or
process status against available live evidence. Keep observed results separate
from worker reports and proposals. Missing temporary evidence remains unknown;
do not reconstruct results or repeat a consumed operation to fill that gap.
Restore stable feature/item/cost lineage and campaign/dependency-chain IDs.
A handoff's historical pause is superseded by this continuation request;
explicit narrower constraints in the current request still apply.

## Execute through the programme coordinator

Read and apply [$work-programme](../work-programme/SKILL.md), including
its referenced guidance when relevant. Resolve its actual installed path if
the sibling is absent; if the required skill cannot be found, report that
capability gap instead of inventing a replacement route. Pass the recovered
frontier, priorities, ownership, evidence, and interrupted work to that skill.
It owns model routing, parallel lanes, telemetry, PR admission, native checks,
source integration, publication, receiver adoption, and progress projections.
Do not duplicate its lifecycle here.

Invocation authorizes autonomous decisions and completion of the recovered
Unified Roadmap without human intervention, including its necessary source
changes, PRs, merges, releases, and receiver work within the recorded programme
scope. Choose implementation details and resolve routine tradeoffs directly;
do not stop for preference questions or repeated confirmation. This does not
create missing credentials, bypass protected checks, renew expired grants, or
replace effect-specific admission. Inspect and obtain valid machine admission
through the existing route before each concrete effect. Where access or an
external dependency genuinely prevents an effect, preserve the blocker and
continue independent ready work; never claim unavailable human feedback.

Seek dependency-ready parallel opportunities aggressively within available
worker, CPU, integrator and hosted-CI capacity. Repair and integrate the existing
queue before opening excess PRs. Resume interrupted source preparation before
starting replacements; recheck source and admission before any fresh operation.

Give structured updates every 30 minutes with **Progress**, **Working on**, and
**Problems**, plus concise material updates during active work. Keep executing
through ordinary failures, CI waits and context compaction. Neither a checked
local milestone list nor a session boundary proves completion: verify the
roadmap's required acceptance outcomes, including applicable publication,
adoption and activation, before reporting it complete.

On a user stop/save request, immediately switch to
[$stop-with-handoff](../stop-with-handoff/SKILL.md). If the environment forces
termination or leaves no possible independent progress, preserve an honest
handoff and report the limitation. Do not imply that a stopped session continues
running or installs automatic future execution.
