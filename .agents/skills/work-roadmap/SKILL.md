---
name: work-roadmap
description: Use when explicitly asked to complete a markdown roadmap milestone by milestone. Routine delivery is the default; use heavyweight ceremony only for human-named scope.
---


Use [$atlas](../atlas/SKILL.md) first for relevant project knowledge before substantial work.
Capture reusable findings in the owning Atlas archive during delivery; cite stable IDs in handoffs.
Verify source and live state before action; unavailable Atlas does not block authorized development.
# work-roadmap

Burn down a markdown roadmap milestone by milestone. The roadmap—not a project board—is the ledger.

## Routine is the default process

Read `.fsgg/routine-development.json`. Use the routine route for every unified-roadmap milestone unless a
recorded explicit human instruction selects heavyweight process for named scope. Absence,
ambiguity, a strict label, GS2 registration, a protected or sensitive path, a policy change, modeled work,
a protected operation, or inherited strict state does not select heavyweight ceremony. Preserve existing
evidence as history, not as a route selector. Do not manufacture a heavy instruction from risk, size, rank,
labels, artifacts, or tooling defaults.

Technical and operational safeguards are orthogonal. Keep focused and repository-native checks, canonical
model/formal authority, permissions, and release/deploy/credential/destructive/cutover/external-acceptance
controls fail-closed. A missing or invalid authorization blocks only its affected effect and may leave that
operation pending; it does not add an issue/claim/SDD/critic/receipt lifecycle to source delivery.

For routine delivery:

1. Keep one accountable owner in the current implementation session. Create a fresh
   `routine/<outcome-slug>` branch from current default branch. Complete adjacent dependent micro-steps
   as tested commits on that branch, then open one PR for a coherent, reviewable outcome. Do not open a
   draft PR for each checkpoint or test. The relevant roadmap checkbox updates and concise evidence land
   in the same PR, and completion is recorded only after its native merge readback. Under
   `work-programme`, the parent integrator admits that PR within its live queue capacity; workers
   keep branches local until admitted, and the integrator uses the canonical `.github`
   `tools/pr-lane-admission.py` helper for new PR creation.
2. Do **not** create or require an issue, claim, SDD artifact family, phase lifecycle ledger,
   independent critic, feedback report, telemetry/receipt cycle, roadmap cycle envelope, receipt-only
   or projection PR except the mandatory current open-index closure follow-up in step 5, or metadata-`Done`
   write. Independent review is optional or sampled and never a
   second authority. Best-effort telemetry is asynchronous and cannot block valid delivery.
3. Run the smallest relevant automated technical checks. Before opening the PR, run the routine
   eligibility and operation-boundary fixtures. Put exactly one
   `<!-- fsgg:routine-development/v1 head=<exact-head-sha> operation=<allowed-operation> -->` marker in
   the PR body. The distinct `routine-eligibility` context is defined by a default-branch
   `pull_request_target` workflow and loads its validator and policy from the exact PR base; it never
   checks out or executes candidate bytes. It validates exact base/head, operation, and changed paths
   without reading board, issue, claim, SDD, review, feedback, or Done state. Candidate-controlled
   `claim-generation` remains only the strict item gate and treats routine branches as not applicable.
   Until branch protection is armed after observation, the pilot owner must still require the reported
   `routine-eligibility` success. A moved head refuses. For an existing-PR repair, review the local repair SHA,
   push that exact SHA without force, then use `tools/pr-lane-admission.py --update-pr` with
   `--previous-head-sha` to rebind the marker while preserving native admission metadata. The helper
   requires the native PR head to equal the new SHA; verify its readback before qualifying fresh
   exact-head checks. Preserve early stale-marker failures as history; they cannot qualify the repair.
   Never replace this update route with a manual PR-body write or a fabricated initial admission.
   This is a trusted-repository-writer reliability boundary: it catches accidental mistakes and drift but
   does not claim adversarial protection from a writer able to alter Actions workflows or spoof a name-based
   check context. The accountable human explicitly accepted that smaller guarantee for routine work;
   protected/high-assurance operations retain their stronger authority boundaries.
4. Repair on the same PR, normally in no more than two material attempts. After required checks pass
   for the exact head, merge through GitHub's native merge boundary and read back the PR's merged state
   and merge commit. Report code delivery separately from any protected publication still pending.
5. Re-read the roadmap from default branch and continue. Keep projection asynchronous; do not launch a
   model turn or PR merely to copy already-merged facts. The sole exception is the mandatory Unified Roadmap v3
   (<https://github.com/FS-GG/.github/blob/main/docs/roadmaps/2026-10-07-unified-development-roadmap-v3.md>)
   or games roadmap
   (<https://github.com/FS-GG/.github/blob/main/docs/roadmaps/2026-10-07-games-development-roadmap.md>) open-index closure update after authoritative cross-repository readback: land its immediate
   closure follow-up under `work-programme`, removing or narrowing the closed outcome and retaining
   completed evidence in its owning plan/report. Do not append a completed-work log to either open index. This exception does not apply to CI ticks, waits,
   intermediate checkpoints or any other status copy, and does not create a PR per checkpoint.

## Qualification and conditional routes

Apply the permanent qualification-selection doctrine in
ADR-0084 (`https://github.com/FS-GG/.github/blob/main/docs/adr/0084-semantic-reuse-never-cancels-coherent-validation.md`). Run the cheap
content/semantic classifier first; after acceptance, start the coherent run, and only a validated exact-head
`reused` receipt may start delivery concurrently with it. `current` or missing valid reuse waits for a
pass, while `deferred`/`failed` refuses. Keep post-merge pending distinct from disputed, and block dependent
acceptance or activation on a late failure. Use the shared `tools/routine-delivery.py` behavior rather than
reimplementing these transitions in the skill.

For source work associated with a protected operation, use the routine steps for the source PR and leave the
operation pending until its independent authority and safeguards permit it. Invalid or unknown technical or
operation authorization fails the affected effect; it does not reclassify delivery as heavyweight.

Apply the permanent [diagnostic execution policy](https://github.com/FS-GG/.github/blob/main/docs/coordination/diagnostic-execution.md)
within the original budgets. Preserve first causes and additional independent findings; failed or unknown
prerequisites block dependent effects, and partial coverage cannot qualify. Read [execution diagnostics](references/execution-diagnostics.md) and its policy before diagnostic execution.

| Condition | Read before the affected work |
|---|---|
| Driver entry, native dispatch or a prospectively selected observed normal-root item | [Runtime observation](references/runtime-observation.md), including the default `fsgg-coord-engine skill roadmap-telemetry` route. Missing usage stays unknown; observation cannot block delivery. |
| Receiver has no routine producer | [Routine producer installation](references/routine-producer-installation.md). Its closed installation predicate does not establish ordinary eligibility or activation. |
| Recorded explicit human instruction selects heavyweight process for named scope | [Heavyweight roadmap](references/heavyweight-roadmap.md), including lifecycle, status, [feedback-contract](references/feedback-contract.md) and critique obligations. Read its complete applicable protocol before dispatch. |
| Interactive/game implementation | Load the local [performance-first planning gate](references/performance-first.md) before implementation begins. |

Milestones stay sequential unless the roadmap establishes disjoint parallel scope and the user authorizes
parallel execution. Source delivery, publication, installation and activation remain separate claims.
