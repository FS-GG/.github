# Speed-first agent development for Unified Roadmap v3

Status: ordinary work-board guidance, revised 2026-10-18. Owner: `.github` programme
integrator; each affected repository keeps its own implementation and acceptance owner.
Open-row navigation: [routing notes](../roadmaps/2026-10-15-speed-first-agent-development.md).
This document selects no release, credential, migration, production effect or CI bypass.

## Decision and objective

Use the canonical [work-board](../../.agents/skills/work-board/SKILL.md) to finish
accepted consumer outcomes with short owner-local feedback, narrow ready slices and
parallel preparation only where dependencies and touch sets are disjoint. Run all current
required exact-head PR checks and comprehensive protected parent, release and installed
acceptance checks at their existing boundaries. Keep the three already closed Unified
Roadmap outcomes closed. More agents, PRs or completed source files are not substitutes
for accepted outcomes. This guidance creates no implementation trial, mandatory cohort,
new metric, reporting cadence or additional closure gate.

This is a speed-first *ordinary practice*, not another execution service or permission to
weaken a protected gate. It clarifies the [existing v3 simplifications](../roadmaps/2026-10-07-unified-development-roadmap-v3.md#11-selected-simplifications--2026-10-12).
The canonical coordinator retains the ordinary guidance that
allows same-owner local build/test repair without per-attempt parent approval and directs
owners to compile early, review the affected path after repeated similar failures and use
bounded check watchers. [ADR-0080](../adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md)
already separates scoped child work from comprehensive parent closure. Apply these
practices in ordinary work without an empirical trial or speed claim.

## What the evidence supports

The [finite V3-IMPROVE-01 trial](../roadmaps/2026-10-15-development-process-improvement.md)
delivered capacity, preflight, diagnostic-source and skill/report-source changes, but its
throughput benefit is unproven. Its one-workload profile and prospective V3 source cohort
do not establish a second-workload capacity or an ordinary comparison. Diagnostic
publication/installed use and packaged skill adoption remain with their owning roadmaps.
The [15 October progress report](../reports/progressReports/2026-10-15-082258Z-unified-v3-progress.md)
describes a historical 08:16 UTC cutoff, not the current frontier. Current state must be
read from the owning plan and live operation before dispatch.

Current primary research supports careful use of these practices, not a promised speedup:

| Source | Relevant result | Limit for FS-GG |
|---|---|---|
| [Geng et al., asynchronous software engineering agents](https://arxiv.org/abs/2603.21489) | Dependency-aware assignment, isolated workspaces and executable integration checks improved reported PaperBench and Commit0 benchmark results by 25.6 and 14.7 percentage points. | Benchmark gains do not predict FS-GG protected-release or installed-receiver latency. |
| [Anthropic multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system) | Parallel breadth-first work improved its internal **research** evaluation by 90.2% while consuming about 15 times the tokens; its account notes coding has fewer independent branches. | This is research, not coding-throughput evidence. Use parallel agents for separable preparation and review, not to multiply overlapping edits or claim cost savings. |
| [AgenticFlict](https://arxiv.org/abs/2604.03551) | From more than 142,000 collected real agent PRs, deterministic merge simulation on more than 107,000 reported 27.67% textual conflicts. | The simulation does not estimate a live FS-GG conflict rate; use exact touch sets, isolated worktrees and one integrator for shared files. |
| [METR randomized developer study](https://metr.org/blog/2025-07-10-early-2025-ai-experienced-os-dev-study/) and [2026 update](https://metr.org/blog/2026-02-24-uplift-update/) | Its 2025 trial of 16 experienced developers and 246 tasks measured AI-allowed work 19% slower; its later update says selection and concurrent-agent timing make the new signal unreliable. | Do not infer speed from agent count or elapsed PR time. |
| [Chen and Stratton's firm-level study](https://fion.ac/jellyfish.pdf) | Across 718 firms and roughly 300 million work events, agent adoption correlated with about 30% more code, 20% more commits and 23% more PRs, while issue/epic resolution changed little and review time rose 49%. | Observational data cannot establish a causal FS-GG effect; review and CI capacity can erase source-output gains. |
| [ESEM 2026 repository study](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.ESEM.2026.67) | Agent-associated PR descriptions grew 76.8%; testing changed modestly, without evidence of reduced productive effort. | Treat artifact volume as a weak proxy and measure accepted outcomes instead. |
| [DORA 2025 research](https://dora.dev/research/2025/dora-report/) | AI amplifies the surrounding delivery system; small batches and feedback quality matter. | Use ordinary owner feedback; this is not a new rollout gate. |
| [Google pre-submit regression work](https://research.google/pubs/techniques-for-improving-regression-testing-in-continuous-integration-development-environments/) and [test-selection assessment](https://research.google/pubs/assessing-transition-based-test-selection-algorithms-at-google/) | Earlier selected feedback can reduce expensive integration feedback; selection and flaky tests are difficult. | Focused early checks supplement, and do not certify or cancel, the exact-candidate comprehensive run. |
| [OWASP Agent Security guidance](https://cheatsheetseries.owasp.org/cheatsheets/AI_Agent_Security_Cheat_Sheet.html) | Bound agent tool authority, credentials, data access and high-impact actions. | Maintain FS-GG effect-specific admission, original identities and protected publication gates. |

## Freeze completed scope and define small slices

The v3 index has 19 rows: three **Done: Yes** and sixteen **Done: No**.
Freeze `V3-IMPROVE-01`, `GAME-BOX2D-01 / GAME-PORTAL-01 P3`, and
`COORD-BOARD-V2-01` at their accepted evidence. A later defect or requested extension
gets a new bounded correction or conditional selection; it does not turn historical
completion back into a general queue. This preserves the finite trial's exact conclusion:
**process changes delivered; throughput benefit unproven**.

The selected V2 binding and enrolled delivery identities govern scheduling; roadmap rows are
historical navigation. The owning plan retains the full acceptance promise. Select the next independently
useful delivery assertion from existing work: exact producer source, coherent publication,
installed receiver or native behavior. One owner carries ordinary preparation, focused
tests and PR repair; the designated integrator handles a shared surface. Use existing
owner packets and original-effect custody, without extra mandatory fields or forms.
Producer and receiver preparation may overlap when their work is disjoint, but their
acceptance join remains explicit; a source merge does not close the receiver assertion.

The [owning lane map](../roadmaps/2026-10-15-speed-first-agent-development.md#2-owned-ready-slices)
names the residual frontier. It is navigation, not a second acceptance ledger or a
mandated schedule. Do not duplicate the closed process trial, reopen completed
Board/Game work or create make-work second workloads. New scope needs an accepted owner and
specific consumer outcome. Keep one active PR per dependency chain and at most two managed
PRs per repository across campaigns; inspect hosted capacity before admission. Free agent
slots do not establish free CI, process or native-effect capacity.

## Use agents where work is independent

The programme integrator owns horizon choice, shared surfaces, capacity and changed
authority. Each lane owner owns its code, focused tests, first failure, PR qualification and
routine repair. A distinct reviewer may inspect security or semantic obligations when the
actual change warrants it. Researchers can gather primary-source evidence, owners can
prepare disjoint worktrees, and a watcher can read terminal native results concurrently.
Only one integrator changes a shared release descriptor, policy or generated projection.

Dispatch from dependency readiness and resource measurements, not the number of available
agents. Verify current cgroup limits, reservations and measured workload demand before
local concurrency. The finite process trial measured one workload; a second is conditional
on genuinely ready disjoint work and enough headroom. Use the container's SSD for active
work and the approved HDD pool for its storage/export role only after verifying actual
placement and ownership. Preserve unknown reservations and original effects across agent
restart. Worker loss never implies operation termination.

For cross-repository joins, the producer supplies immutable revision, artifact/contract
identity and acceptance evidence. The consumer independently verifies the selection,
installed bytes and its own assertions. Parallel preparation stops at the join; one side
cannot self-certify the other. V3-ENV normal use specifically requires the installed
provisioner and launcher to allocate, bind, launch, tear down and recover worker loss
without a human host agent after one-time setup. A manual host pilot supplies evidence
only for the assertion it actually exercised.

## Move feedback earlier while preserving final qualification

The present policy is already less strict than a full gate after every edit: same-owner
local compile/focused tests are allowed, and ADR-0080 scopes child qualification. Cost
still accumulates when a malformed final composition reaches hosted CI, when repeated
repairs restart long candidate checks, when source and receiver work wait serially despite
disjoint preparation, and when too many ready PRs queue for the same hosted capacity.
The ordinary sequence is:

| Boundary | Current governing route | Speed-first operation | Proof and limit |
|---|---|---|---|
| Edit and local repair | Owner may build, focus-test and repair within its admitted profile. | Use existing cheap relevant compile/shape/caller checks, with inert external transport where applicable. Keep one owner and original failed attempt. | Early pass covers only its declared subject; an unknown/missing enforcing preflight fails closed. |
| Assembled candidate | [ADR-0086](../adr/0086-proportionate-pipeline-preflight.md) chooses proportionate static/model checks. | Check actual generated metadata, pins, workflow selectors, required files and caller/adapter behavior before expensive qualification. After two similar failures, inspect the complete affected path before retry. | Known-bad candidate stops early; valid candidate proceeds. No new mandatory check is introduced here. |
| Hosted PR | Required workflow and [ADR-0084](../adr/0084-semantic-reuse-never-cancels-coherent-validation.md) govern exact-head coherent validation, including its narrowly permitted scoped profile. | Admit candidates only when ready and capacity permits; run required checks concurrently where isolated. Reuse only the audited exact-head disposition and donor proof. | No selected coherent run is cancelled by semantic reuse. Required checks, branch protection and failed-head history remain. |
| Parent milestone, release and receiver | [ADR-0080](../adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md), [ADR-0081](../adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md) and owning contracts require comprehensive closure. | Join the exact published set, installed receiver, native effects and cleanup at their real boundaries. This guidance selects no gate-cadence change. | Comprehensive closure, formal-input drift, high-impact checks, original identities and no-human host lifecycle remain hard gates. |

The difference is owner focus and ordering, not a new universal “minimal test” rule.
No required CI job or existing acceptance gate is removed. A future cadence reduction
requires a separate reviewed ADR-0081 policy update. Each required exact-head PR/native
boundary still receives its selected coherent run; comprehensive protected boundaries
remain additional obligations. Publication and installed use retain separate acceptance.
A failed, unknown, cancelled or partially observed run cannot qualify.

## Chosen tradeoffs and non-negotiable bounds

The user selected speed as a priority within all current acceptance gates. Ordinary
work may accept these operational costs; the guidance amends no ADR:

| Faster choice | Cost or risk accepted | Boundary |
|---|---|---|
| Parallel disjoint research, source preparation and focused local checks | More simultaneous CPU/memory use, agent tokens and possible integration rework. | Measured resource headroom, truthful touch sets, one integrator; stop admission on unknown reservations or pressure. |
| Small slices with cheap early checks | A focused check may miss a defect later found by the unchanged required exact-head run, causing rework and CI cost. | No skipped required check or delayed security/corruption/auth minimum cadence; current comprehensive boundaries remain. |
| One accountable owner for ordinary reversible source, with independent review when selected | Reviewer effort can fall, while some errors may reach CI or final review later. | An already selected strict or independent-review route remains selected. Security-sensitive code, credentials, effects, release, native host and uncertain state retain applicable independent safeguards. |
| At most two managed PRs per repository and preparation during hosted waits | Ready local work may queue even with idle agents. | Protect integration quality and runner capacity; do not use speculative PRs to manufacture activity. |
| Reuse authentic exact-head evidence when ADR-0084 permits | A semantic classifier defect could yield a later disputed state. | Retain coherent execution, donor/scope proof, disputed-state recovery and dependent activation hold. |

Security and robustness bounds are not traded for speed here. Keep least-privilege tool
and credential scope, protected PR checks, fail-closed unknown results, original operation
identity, one-use/custody/deadline rules, checked cleanup, private evidence custody and
separate exit/reporting results. Preserve UTEL's original credential and release holds.
Do not replay a dispatch with unknown effect. V3's unattended provisioner requirement is
unchanged. A user may separately select a reviewed policy change with its own risk evidence;
this document grants no such blanket permission.

## Reporting and stop rules

Use existing owner artifacts and normal progress reporting. Retain original failures,
unknowns and accepted consumer results. The V3-IMPROVE source cohort remains historical
evidence with no ordinary comparator; this guidance creates no new baseline or cohort.
Do not claim a throughput gain without comparable evidence. Stop affected work on resource
pressure, ownership conflict, unknown original effect or an acceptance regression, while
unrelated ready work continues under its own authority.
