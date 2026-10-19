---
title: "FS-GG Unified Development Roadmap v3"
category: Design
categoryindex: 4
description: "Strategic development outcomes, owning plans and the Coordination V2 selection boundary; game products have a separate roadmap."
---

# FS-GG Unified Development Roadmap v3

**Current roadmap, 2026-10-07.** This replaces the
[unified v2 roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md).
The old document is the archive for completed outcomes, original designs, superseded stages and
historical evidence. This version preserves strategic outcome scope and owning-plan navigation. Current planning and
lane selection follow the accepted Coordination V2 consumer boundary in section 2. The version number describes
this document; it does not select the historical V3 cutover stage or restart V2 migration.

BAR, SC2 and FourD product work belongs to the separate
[games roadmap](2026-10-07-games-development-roadmap.md). Shared Game/Rendering capabilities and
the `fable-game` template remain here. The split preserves existing feature IDs, accountable owners,
plans, attempts and dependencies; moving a row creates neither a new attempt nor an acceptance claim.

The starting evidence is protected `.github` revision
[`65c8d8ce`](https://github.com/FS-GG/.github/commit/65c8d8ced152cea2634e44521cf25a572c26f79c).
Strategic outcomes retain their owning acceptance; delivered source does not imply whole completion.
Before selecting an operation, reconcile the owning plan and current native facts. Sealed handoffs
retain their original identities; their old roadmap links lead to the archive and then here.

## 1. Scope and operating boundaries

Use [work-board](../../.agents/skills/work-board/SKILL.md) for requested programme advancement
and [continue-from-handoff](../../.agents/skills/continue-from-handoff/SKILL.md) for requested recovery.
Editing these documents or skills does not begin execution. Routine source delivery, effect-specific
admission, native required checks and [ADR-0084](../adr/0084-semantic-reuse-never-cancels-coherent-validation.md)
remain in force. [ADR-0091](../adr/0091-speed-first-clean-v2-start.md) retains the accepted clean-start
route. Archived GS2-09–14 migration/cutover rows are not current prerequisites.

Plan one independently useful outcome at a time using its existing owner plan. Split lanes only
where outcomes, dependencies and touch-sets are independent. Preserve unknown operations and owner
reservations. Source delivery, package publication, installed qualification, activation and actual
product acceptance remain separate. S.I.R. stays read-only. New defaults require their own selected
receiver proof; navigation changes and package pins do not activate them.

## 1.1. Selected simplifications — 2026-10-12

Use the following delivery choices with the owner-level development authority in
[work-board](../../.agents/skills/work-board/SKILL.md). They reduce repeated work and
unnecessary dependencies; they do not close rows, change package versions, admit live effects or
waive the owning contract's technical assertions. Original attempts and unknown custody remain intact.

| Work | Simplified route | Retained limit or tradeoff |
|---|---|---|
| UTEL operational collection, V2-EFF installation and LEARN causal receiver | Share one compatible coherent publication/installation and one selected receiver cohort. Link each row to the assertions it actually exercises, including ingest/reopen/replay, corrections and causal conflicts where required. | Different artifact sets, stores, profiles or authority need their own proof. A shared install does not prove provider execution, live collection, activation or learning. No duplicate migration of an already accepted exact store. |
| V2-CTX integration and adoption | Qualify a bounded ordinary driver window and record an explicit benefit disposition; adopt independently justified correctness/reliability changes with rollback while economic evidence remains inconclusive. | Statistical savings and policy-learning claims still require the declared controlled experiment. LEARN infrastructure does not gate ordinary driver correctness/adoption. |
| Routine coverage and V2-EFF assessments | Keep deterministic status/coverage for all observable items; use bounded model assessments for exceptions and a prospectively selected success sample. | Unsampled items carry no inferred review. Sampling limits item-specific explanations; calibration includes all required outcome classes and reports selection/coverage. |
| TSDD knowledge, SVG Release-D successor and FABLE-ADOPT reference | Use one compatible published Wizard/SDD/Templates receiver cohort and shared acquisition/build/browser setup, with separate outcome assertions. | Fresh generation, promised retaining upgrades, accessibility, knowledge limits and activation are still distinct assertions. Reuse requires exact compatible inputs; one failure cannot hide independent results. |
| V2-PROC and OPS-TYPED adoption | Deliver each qualified profile/consumer independently; run its replacement and retirement proof in the consumer's existing acceptance route. Freeze the selected adoption population before its window. | Routine process adoption need not wait for Linux containment. New consumers do not silently extend the current milestone; contained/native use still requires its enforcing backend. |
| V2-LANG installed matrix | Run common lifecycle conformance once per identical enforcing runtime/profile, plus every selected language's real entrypoint, serialization/toolchain and distinct failure cases. | Retain TypeScript/Rust/Go and selected mixed/no-.NET routes. Different enforcement or cleanup behavior is a separate profile; no cross-language success inference. |

The shared receiver cohort is an execution optimization, not a new project or ledger. Put its artifact
selection and assertion-to-outcome mapping in the existing owning acceptance record. Batch ready compatible
work without waiting for unrelated consumers. Failed/stopped results remain visible per assertion. Reuse
of required coherent validation still follows ADR-0084; no existing required CI check is removed here.
The [context](2026-10-05-programme-context-efficiency.md#measurement-and-experiment),
[efficiency](process-efficiency-telemetry.md#delivery-objective),
[process](2026-10-05-shared-process-supervision.md#implementation-milestones),
[template](2026-10-01-staged-fable-game-adoption.md#remaining-game-template-consolidation) and
[language](2026-09-29-language-independent-workspaces-and-agent-integration.md#language-qualification-population)
plans define the corresponding boundaries.

Do not reopen completed Board adoption, completed inventory stages or stopped SVG profiling to keep lanes
busy. Resume the remaining accepted owner frontier; a new research window needs a demonstrated unresolved
decision. Keep the existing late consolidated GPU batch and product-native acceptance boundaries. Historical
release closure is not reopened by a later dependency refresh; qualify the successor's actual changed joins.

## 1.2. Provisioned Podman profile and ephemeral nested storage — 2026-10-13

[V3-ENV-01](2026-10-13-provisioned-podman-execution.md) proposes a finite common
execution/profile outcome for the supplied Linux/Podman environment. Its first storage
pilot targets slow nested-build file I/O with a fresh host-backed store, comparing actual
native-overlay, fuse-overlayfs and VFS behavior inside the selected unchanged envelope.
Default development starts without prior writable engine, dependency-cache or workspace
state; a second compatible machine reconstructs declared inputs instead of transferring a
writable store. The existing provisioner owns scoped allocation, export and observed teardown.

The [design](../designs/2026-10-13-provisioned-podman-execution.md) and
[prior-art assessment](../reports/2026-10-13-provisioned-podman-prior-art.md) preserve existing
Coordination identity/deadline/custody contracts. No privileged troubleshooting recipe,
host socket, global reset, new resource cap, automatic restart or broader compatibility
withdrawal is selected by this document. Actual host/version/filesystem, provisioning
owner and native capability facts precede dependent experiments. Storage performance does
not establish containment, publication, installed acceptance or LEARN activation.

The common base Containerfile and qualified image release belong in `.github`; Coordination
owns the launcher/execution implementation and Templates/SDD materialize product toolchain/profile
selections. Framework development is the first pilot; ordinary product workspaces consume pinned
published images through the same launch/storage contract. Host bootstrap and ephemeral allocation
are provisioner responsibilities, while product production deployment remains product-owned.

Common profile/storage delivery is counted once here; PROC/LANG/LEARN and receiver owners
retain their existing consumer assertions and can reuse only exact compatible evidence.
Adding this outcome changes section 2 from 17 rows/two Done to 18 rows/two Done. This is
scope growth, not an effort-completion percentage. Independent qualified lanes and the
completed Board and Box2D/Portal P3 outcomes remain unchanged.

## 1.3. Speed-first practice in ordinary work — 2026-10-15

The [speed-first design](../designs/2026-10-15-speed-first-agent-development.md) and
[open-row routing notes](2026-10-15-speed-first-agent-development.md) inform the existing
[work-board](../../.agents/skills/work-board/SKILL.md). This index has **19
rows: three Done and sixteen open**. Keep `V3-IMPROVE-01`, `GAME-BOX2D-01 /
GAME-PORTAL-01 P3` and `COORD-BOARD-V2-01` closed. The finite process trial's
accepted conclusion remains **process changes delivered; throughput benefit
unproven**. Diagnostic publication/installed use and skill release/receiver adoption
remain open with their original owners.

For ordinary advancement, select a ready, independently useful slice of an open owning
plan. Keep one accountable owner and one integrator per shared surface; prepare truly
disjoint work in parallel within measured local and hosted capacity. The linked routing
notes are navigation, not a new lane, cohort, packet form or acceptance ledger. Use
cheap relevant checks on the assembled candidate during owner-local repair, then run
the required exact-head PR scope and comprehensive protected parent, release and
installed-consumer checks at their existing boundaries. ADR-0084 coherent execution
and ADR-0086 fail-closed preflight remain; this guidance selects no cadence reduction.
Required CI, formal-input drift, security/credential and release holds, original
unknown effects, applicable independent safeguards and V3-ENV's **no human host agent
in normal use** remain unchanged. Use normal progress reporting; this guidance makes
no throughput claim or mandatory comparison.

Apply these five narrowings through the existing owners, without a new trial or
acceptance lane:

| Existing owner | Ordinary speed-first boundary | Still required |
|---|---|---|
| [V2-DIAG-01.4](diagnostic-execution.md) | Observe attempts, useful findings and overhead from necessary qualification and installed use. A separate comparison window is needed only for a quantified benefit claim. | Coherent publication and selected installed ordinary/native receiver behavior; failed or unknown partial attempts never qualify. |
| [V2-EFF-01.5/.6](process-efficiency-telemetry.md) | A net-value comparison is optional unless claiming economic benefit. Do not delay adoption merely to fill a benefit denominator. | Finite held-out public-claim calibration, privacy and factual controls, then real installed assessment/projection/browser use, correction and rollback. |
| [Owner-local repair](../../.agents/skills/work-board/SKILL.md) | Run the smallest relevant deterministic checks while editing; run comprehensive required coherent qualification on the final assembled candidate submitted for acceptance and again for each changed final head. | Exact-head PR/native gates, protected parent/release/installed boundaries, original effects and cleanup. |
| [Routine review](../../.agents/skills/work-board/SKILL.md) | Select a distinct reviewer from actual semantic, security or effect risk; ROUTINEV3 source work uses one independent source review under the extended pilot. | Any already selected strict or independent review and protected authority safeguard. |
| [ADR-0086 preflight](../adr/0086-proportionate-pipeline-preflight.md) | Use static or focused checks for simple paths; model ordering, retries, cancellation or shared state only when their interaction warrants it. | Fail closed on missing/unknown results; preflight never replaces ADR-0084 coherent validation. |

V2-CTX-01.5/.6 likewise accepts the bounded integrated operation/restart and
qualified adoption with economics inconclusive; its reservation and no-duplicate-effect
checks remain. These decisions narrow optional comparison and process overhead, not
required CI cadence or the consumer acceptance boundary.

## 1.4. Selected three-outcome programme pilot — 2026-10-16

Prioritize the [outcome-led pilot](2026-10-05-programme-context-efficiency.md#selected-three-outcome-programme-pilot--2026-10-16)
under V2-CTX-01.4–.6. The target is **80% less coordination effort** against a
comparable current-route baseline, with faster delivery and no loss of correctness,
credential custody or replay protection. The [governing change #4608](https://github.com/FS-GG/.github/pull/4608)
is protected and active for these outcomes; required CI and native admission remain in force.

| Outcome and accountable owner | Done when | Current boundary |
|---|---|---|
| Supplier-journal `/3` compatibility — Coordination receiver, fdev1; implementing agent `/root/v3_coordination_receiver` | Reviewed source is merged and `/2` compatibility and `/3` refusal tests pass. | [Coordination PR1012](https://github.com/FS-GG/FS.GG.Coordination/pull/1012) is protected; this narrow source result does not complete V3-ENV-01 or admit host/native effects. |
| Fresh-profile UTEL resource containment — `.github` copy-accounting owner, with Programme `/root` accountable until durable transfer | A separately authorized fresh profile qualifies finite installed resource budgets and safe refusal/Unknown behavior across its explicit execution, storage, transport and recipient boundaries. | [ADR-0095](../adr/0095-utel-resource-containment-for-fresh-profiles.md) selects this acceptance objective; implementation, production budgets and admission remain pending. The original [ADR-0093](../adr/0093-original-103-authentication-domain-policy.md) profile keeps its 65,536-byte cap, physical bound null, `fit=unproved` and `admission=false`. |
| UTEL authority service — SystemAdmin service owner, with Programme policy and independent credential custody | Implementation and deployment qualify; custody, enrollment, verification and recovery are accepted. | The standalone ready source join has accepted independent static review and [Home-observed integration](https://github.com/FS-GG/.github/issues/4517#issuecomment-6217399500). Full service composition, installed qualification, custody and enrollment remain open in [#4601](https://github.com/FS-GG/.github/issues/4601). |

One owner carries each outcome through implementation, review and delivery. A new lane
must materially advance its completion condition or deliver an independently useful
cross-team dependency. Use one current issue record per outcome, one independent
source review, one active PR per coherent implementation change, and CI-produced
acceptance evidence. The protected governing rule retires duplicate packets, typed
returns, reducer views, fixed polling and unchanged-status comments. Native exact joins
replace routine hash/file correspondence recitations; the accountable CI integrator
qualifies automatic results for additional facts. Required final-head tests remain.
Keep private evidence restricted and original-operation identities
where correctness needs them. The owning issue records are [V3 `/3` #4599](https://github.com/FS-GG/.github/issues/4599)
(closed source baseline), [finite cap #4600](https://github.com/FS-GG/.github/issues/4600)
and [authority service #4601](https://github.com/FS-GG/.github/issues/4601).
[V3 #4516](https://github.com/FS-GG/.github/issues/4516) and
[UTEL #4517](https://github.com/FS-GG/.github/issues/4517) remain cross-repository
reply channels for their existing host requests. The one-issue-per-outcome mapping is established.

The protected [reader repair #4602](https://github.com/FS-GG/.github/pull/4602) and
[producer migration #4604](https://github.com/FS-GG/.github/pull/4604) supply one checkout witness
from the existing required `coherence/projection` job on unfiltered PR events. The
[genuine producer run](https://github.com/FS-GG/.github/actions/runs/38865089059) supplies the
artifact verified before and after native delivery. Readback confirms identical tested/delivered
trees and all eight required checks passing.
The [maintained reader](../coordination/routine-delivery-evidence.md) distinguishes the actual
synthetic merge checkout from the PR head and preserves failed checks, coherent disputes and
unavailable evidence. This witness covers the projection job's entry checkout.

[Automatic evidence integration #4609](https://github.com/FS-GG/.github/pull/4609) is protected.
Ordinary calls to the maintained repository delivery helper now select the required projection
run and assemble bounded acceptance evidence automatically. Its actual source delivery and
subsequent read-only collection both reported matching tested/delivered trees, all eight required
checks passing and no evidence issues, without an explicit run argument. Coverage is the
projection job entry checkout plus native check results; it does not prove every job's test-time
filesystem or installed fleet adoption. Deadline and session-start issue reconciliation are
active without a notification platform prerequisite.

The pilot's three change classes, issue fields, CI acceptance result, decision-driven
updates, knowledge capture, cap-accounting sequence, authority-service plan and
measurement/retirement rule live in the owning plan. The mandatory duplicate artifacts,
five-minute reply polling and fixed thirty-minute reports are retired for this pilot.
Measure savings during ordinary delivery; measurement is not an activation gate. `.103`
remains attempted-unknown and unreplayed; `.104`
and other original effects remain held under their existing admission gates.


## 1.5. Priority unblockers — 2026-10-16

Retain the original acceptance and evidence below; the later selected focus in section1.6 governs
new work. This historical ordering does not dispatch a deferred product lane.

Advance these priorities through the existing open outcomes and owner plans; this section
is their ordering and acceptance guidance, not another task ledger. The
[ROUTINEV3 extension](2026-10-05-programme-context-efficiency.md#routinev3-source-extension--2026-10-16)
applies after its reviewed governing delivery. Keep one accountable owner, one independent
source review, relevant tests, final required CI and exact delivered-code qualification.
Retire duplicate packets, repeated parent approvals and duplicate status projections within
that scope; operational authority, independent credential custody and original effects remain.

| Priority and existing owner | Next useful result | Retained boundary |
|---|---|---|
| **1. Authority-service composition and SC2 evidence access** — Programme review owner, SystemAdmin source owner and SC2 repository administrator | The [narrowed independent static review](https://github.com/FS-GG/.github/issues/4517#issuecomment-6217315149) accepts the repaired standalone AUTH ready join. [#4601](https://github.com/FS-GG/.github/issues/4601) records Home-observed integration and independent reviewed/delivered source correspondence. Full PhaseService composition, production ports, custody and enrollment remain open. The SC2 administrator still supplies exact **Checks: read** permission and successful affected-check readback; a 403 is unresolved access. | The earlier [platform-flagged reviewer turn](https://github.com/FS-GG/.github/issues/4517#issuecomment-6212493456) remains historical; it establishes neither a separate Auto-review denial nor a universal human-review condition. Preserve the rejected ready-v1 source and withdrawn erroneous review. Later accepted source review grants no deployment, credentials or native admission; restricted evidence and original holds remain. |
| **2. Forward recovery and independently admitted fresh work** — Programme recovery owner with host/provisioner | The unavailable historical evidence search is **closed as unavailable**. Preserve **439 inherited reservations and 46 later original records** separately, with unproved outcomes Unknown. For each concrete new operation, reconcile current relevant ownership, remaining capacity and demonstrated separation of generation, writable state and outputs; fence shared destinations against old-actor interference. See the [selected forward-recovery boundary](2026-10-07-games-development-roadmap.md#2-custody-recovery-and-safe-continuation). | No blanket release, inferred cleanup, fabricated empty coverage or disguised retry. Unrelated unavailable history is not a global prerequisite for proved-independent work. Use existing journals; the separate V3 two-machine portability/unattended-lifecycle requirements remain unchanged and unclosed. |
| **3. Early compiler feedback** — existing source owners and CI integrator | [Coordination #1017](https://github.com/FS-GG/FS.GG.Coordination/pull/1017), merged at `fc1a3306`, delivers early compile/focused-test gating with final required checks and exact tested/delivered correspondence. Use this maintained source path; retain first-failure diagnostics during repair. | Source qualification does not replace full final required CI, ADR-0084 coherent validation, installed adoption or native acceptance. Local CLR/native holds still apply; use only admitted execution. |
| **4. Canonical delivery and active review** — delivery-tooling owner and designated source reviewer | [Guidance repair #4624](https://github.com/FS-GG/.github/pull/4624), merged at `aa9014d2`, aligns the documented repair sequence with the existing canonical updater and preserves native admission metadata. Continue exact-head reviewer activation and the qualified delivery route. | Admission behavior is unchanged. Retain original stale-marker failures, canonical update/readback and fresh final-head checks; never infer source review from an assignment or check success. Source guidance delivery does not establish installed adoption. |
| **5. Lightweight ROUTINEV3 source delivery** — Programme and existing outcome owners | Apply the extended pilot consistently in governing skills and mirrors: one owner, one independent source review, relevant tests, required final CI and native delivery evidence. Use the existing issue/PR. | No duplicate packet/reducer/status machinery or repeated parent approvals for ordinary authorized repair. Changed authority, shared-surface conflicts, uncertain effects and missing credentials still escalate. |
| **6. One working producer/provider journey** — Coordination, SystemAdmin and `.github` UTEL owners | The [18-case joined managed/private pipe SOURCE qualification](https://github.com/FS-GG/.github/issues/4517#issuecomment-6220575332) and [supplied source/Atlas correspondence](https://github.com/FS-GG/.github/issues/4517#issuecomment-6220757659) are accepted. Home reports private PR155 merged at `f8a2902e`; the [owning plan](utel-release-successor.md#current-joined-source-frontier--2026-10-16) records the accepted scope and remaining UTEL provider/budget/enforcement dependencies from [#4600](https://github.com/FS-GG/.github/issues/4600) and [#4601](https://github.com/FS-GG/.github/issues/4601). | Private native merge remains Home-observed; supplied-byte correspondence is independently verified. No hosted PASS, full remote-tree reconstruction, installed adoption or operational acceptance is claimed. Preserve the earlier 16/18 failed cohort and two missing receipts. No further pipe runtime job is selected; original-bound reservations, custody, enrollment and operational admission remain separate. |
| **7. Fresh-profile enforced resource containment** — finite-cap owner, SystemAdmin/provisioner and Programme acceptance owner, tracked in [#4600](https://github.com/FS-GG/.github/issues/4600) | Qualify the [ADR-0095](../adr/0095-utel-resource-containment-for-fresh-profiles.md) contract for one operational construction and one retained original: finite measured budgets with justified headroom, independently read-back enforcement, integrated success/fault journeys and explicit coverage exclusions. | Acceptance-contract reassessment is settled: enforced resource containment is selected for separately authorized fresh profiles; it supplies no universal physical-copy or erasure guarantee. Production budgets, installed enforcement and fresh profile/original admission remain pending. The legacy ADR-0093 profile retains **65,536 bytes**, physical bound null, `fit=unproved`, `admission=false`, all 439 reservations and the `.103`/`.104` holds. |

V3 receiver source progress remains separate: [Coordination #1022](https://github.com/FS-GG/FS.GG.Coordination/pull/1022)
and [#1023](https://github.com/FS-GG/FS.GG.Coordination/pull/1023) are merged at `5b99dee4` and
`a0897a55`, with exact tested/delivered tree correspondence. The selected uncertainty-cut, locked-read
and inert unsigned ACK SOURCE assertions are delivered. The final [bootstrap](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38943378505)
and [coherent qualification](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38943377854) passed
at `8f513dd9`. The original [formal-base timeout](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38936797965/job/117097229642)
remains unexplained and retained. Both successful qualifications used the existing canonical Q2
lifecycle retry; the new pre-parser retry remains unexercised. [Post-merge coherent validation](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38949025174)
also passed at the exact delivered revision. No further receiver surrogate is selected. Durability
remains Unknown; production
`prepareAck` and `reserveHost` still refuse, and no live ACK authority is established.

[Coordination #1024](https://github.com/FS-GG/FS.GG.Coordination/pull/1024), merged at `6b2bab64`,
delivers the controlled-source genesis checkpoint and unsigned ACK export boundary with explicit
input/hash selection and verified output readback. Strict compilation, 226 focused tests, required
checks and [full coherent qualification](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38977082516)
passed at `a99356b8`, with exact tested/delivered tree correspondence. The existing genuine Fact
exercised one export and nine refusals. The owning Atlas boundary
`coordination/controlled-genesis-checkpoint-export-boundary` is protected with the source delivery.
Post-merge results are tracked separately in the [delivery thread](https://github.com/FS-GG/.github/issues/4516#issuecomment-6226508518).
This public SOURCE delivery does not qualify Home's fresh input, environment, driver or
authentication-factory joins, current external clocks, signing/ACK authority or production durability.

Home's host transaction, current codecs and structural consumption have [accepted bounded SOURCE
qualification](https://github.com/FS-GG/.github/issues/4516#issuecomment-6222834756): 47/47 cases passed
with no skips or test/collection errors. The earlier failed qualification remains retained. [Supplied
private source/Atlas delivery correspondence](https://github.com/FS-GG/.github/issues/4516#issuecomment-6222985659)
is accepted; native private merge remains Home-observed, with no hosted CI PASS or installed acceptance
inferred. [Current Home activity](https://github.com/FS-GG/.github/issues/4516#issuecomment-6226508518)
remains unconfirmed; fresh input/environment/driver integration and authentication-factory source
acceptance remain open. Receiver residency/custody remains unresolved. Durability remains Unknown, and actual
authentication, physical/native/production qualification and installed acceptance remain unestablished.

Keep source delivery, publication, installed qualification and runtime acceptance separate.
Original `.103` remains attempted-unknown and unreplayed; `.104` remains held. Use
[#4516](https://github.com/FS-GG/.github/issues/4516) and
[#4517](https://github.com/FS-GG/.github/issues/4517) for their existing reply contracts;
refresh current facts at agreed deadlines and before dependent actions. No source-only
milestone closes the provider journey or the physical-fit outcome.

## 1.6. System capability focus — 2026-10-18

The user selected usable system capabilities as the current development focus. Advance the existing
owning outcomes and independently meaningful delivery issues through canonical work-board. This is
strategic priority guidance, not another current-status table, scheduler, enrollment or closure ledger.
The coordinator and normal/best drivers remain reusable for organization and explicitly selected
product scope; this selection is not a permanent skill lane allowlist.

| Selected capability | Owning scope and necessary shared suppliers |
|---|---|
| Canonical coordinator and driver | [Coordinator source4677 and full outcome4641](unified-programme-driver.md#canonical-board-coordinator-migration--2026-10-18), [context and installed adoption](2026-10-05-programme-context-efficiency.md), [V2 contract](../coordination/2026-09-29-coordination-v2-board-design.md). Retain programme/product journeys, recovery, exact selected instruction discovery and caller qualification. |
| UTEL and useful reporting | [Telemetry correctness](utel-01-telemetry-correctness.md), [successor](utel-release-successor.md), [dashboard](utel-telemetry-dashboard.md), [Host release](../coordination/telemetry-host-release.md). Usable collection/reporting needs actual qualified producer, receiver and retained data, not source or publication alone. |
| PROC, typed administration and diagnostics | [Shared supervision](2026-10-05-shared-process-supervision.md), [process contracts](2026-10-11-shared-process-contracts.md), [typed administration](2026-10-01-typed-administrative-fsharp-quint.md), [diagnostics](diagnostic-execution.md). Select the enforcing profile and actual consumer assertions; no new product feature is implied. |
| Podman and the selected persistent receiver | [Provisioned profile/storage](2026-10-13-provisioned-podman-execution.md), [persistent receiver](learn-c2-persistent-receiver-v3.md). Retain actual allocation, storage, launcher, persistent execution, teardown/recovery and separately promised two-machine/unattended proof. |
| LEARN | [Learning outcome](2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md), [causal receiver](learn-01-causal-receiver.md). Retain genuine turn/capture/restart, census/data coverage and the separately selected comparison; infrastructure alone is not learning acceptance. |
| Necessary shared lifecycle and knowledge suppliers | SDD/Templates/Wizard/providers and Governance loaders supply the exact contract needed by a named selected system acceptance. [Creator source boundary](unified-programme-driver.md#independent-creator-source-closure), [language/provider plan](2026-09-29-language-independent-workspaces-and-agent-integration.md) and [Atlas](../../.agents/skills/atlas/SKILL.md) retain their own publication, installed receiver, compatibility and knowledge-delivery obligations. |

Defer new SVG, Unity, SC2 and other product features unless a named system acceptance assertion
requires that specific shared capability. An open product epic, parent Blocked state or unrelated
unfinished child is not a global prerequisite for an independently qualified system delivery. State
the exact supplied contract, affected assertion and supplying owner; share that dependency once rather
than reopening the product's whole promise. Product scope, accepted milestones and all remaining
technical, installed, native and participant obligations remain intact for later explicit selection.

Bring already running operations to their safe observed terminal or authorized stop and retain their
original evidence, uncertain effects and reservations. Deferral neither cancels operations blindly nor
means cleanup, completion, release, retry or abandonment. All439 inherited and46 later original holds
remain. Source4677/full parent4641 scope is unchanged; source, package publication, installed discovery,
native migration/enrollment/views, receiver activation and restart/rollback remain separate acceptance.
Report usable capabilities, the next executable delivery and its exact missing inputs/supplying owners.
Only canonical work-board and current native/owning facts determine concrete action admission.

## 2. Open work and owning plans

The user-selected [BOARD-LIFECYCLE-01 design](../coordination/2026-10-19-board-clearing-lifecycle-design.md)
and [owning outcome #4701](https://github.com/FS-GG/.github/issues/4701) unify the next board/process
redesign, active skill retirement and meaningful decomposition of the current large Outcomes.
Its deliveries retain source, installed and native acceptance separately. This scope does not reopen
accepted migrations or authorize implementation of every product outcome it helps shape.

This catalogue preserves strategic outcome scope and owning-plan navigation. Root accepted the
actual coordinated programme, board-driver, recovery and progress instruction loads and decided
operational cutover at **2026-10-18 07:42:17.703507 UTC**, effective upon protected delivery in the
[canonical owning plan](../coordination/2026-09-29-coordination-v2-board-design.md#operational-programme-cutover-acceptance--2026-10-18).
Until that delivery, the
[previous protected current-status table](https://github.com/FS-GG/.github/blob/bba5d43f134e610ae0be8054ed4dd987d6877a21/docs/roadmaps/2026-10-07-unified-development-roadmap-v3.md#2-open-work-and-owning-plans)
retains its prior boundary. At delivery, Coordination V2 Project 3 supplies current planning and lane
selection through the qualified installed binding-v4/schema2/inspection2 route, and duplicate manual
Entry/blocking and Done upkeep retires. Use this catalogue for changed strategic scope or plan links;
retain the immutable table as history.

Current owning issues, human planning and native acceptance retain their separate authority;
Observation Verified and human Done do not prove outcome completion. Completed scopes stay closed
through their accepted owning evidence. A catalogue row does not enroll a card or dispatch a lane.
Games and conditional work in section 6 retain separate selection and acceptance boundaries.

| Feature and accountable owner | Strategic outcome | Owning plan |
|---|---|---|
| **UTEL operational collection — `.github` telemetry/Coordination** | Reconcile historical installation/selector, usage and pending effects separately under their original identities. Qualify demonstrably isolated fresh telemetry dispatch and CI/delivery collection through established admission without blanket release of historical holds. Recover retained pending publications only through their supported original-identity route. | [Operational completeness](utel-operational-completeness.md), [local store](utel-local-telemetry-store.md), [correctness](utel-01-telemetry-correctness.md) |
| **Routine activity, review and usage coverage — programme/telemetry owners** | Collect planning, implementation, review, validation, delivery, operations and repair spans; submit selected reviews for settled attempt populations under the bounded assessment policy; reconcile native usage with explicit unknowns; continue CI/delivery reconciliation. Every efficiency view exposes coverage, freshness, unresolved observations and its denominator. | [Observation guidance](../../.agents/skills/work-board/references/observation.md), [efficiency measurement](process-efficiency-telemetry.md) |
| **V2-EFF-01.2–.6 — telemetry, dashboard and receiver owners** | **First release facts and coverage**, following the [telemetry delivery order](process-efficiency-telemetry.md#selected-telemetry-delivery-order--2026-10-16): visible missing usage, freshness, populations/denominators and unknown cost that never becomes zero. First operational acceptance is one telemetry collection provider → selected receiver/store → projection → browser on exact coherent versions, with a real item and separately admitted success, interruption/Unknown, recovery and correction cases; never replay an uncertain original. Defer calibrated explanations, model-assessment adoption and savings claims to their own later qualification. | [Efficiency delivery plan](process-efficiency-telemetry.md), [dashboard and process-health phases](utel-telemetry-dashboard.md#development-process-health-dashboard--proposed-2026-10-12) |
| **V2-PROC-01.2–.8 — Coordination; SDD/FsQuint consumers** | Complete reconciled coherent publication, selected receiver adoption, remaining Linux containment and consolidation. | [Shared process supervision](2026-10-05-shared-process-supervision.md#original-publisher-failure-and-complete-policy-repair--2026-10-14) |
| **V2-CTX-01.4–.6 — `.github` programme driver** | Prioritize the [outcome-led pilot](#14-selected-three-outcome-programme-pilot--2026-10-16) and [ROUTINEV3 priority unblockers](#15-priority-unblockers--2026-10-16): governing replacement and repository automatic CI result/reader are delivered in [#4608](https://github.com/FS-GG/.github/pull/4608) and [#4609](https://github.com/FS-GG/.github/pull/4609). Use the qualified route and deadline/session-start issue reconciliation; qualify further consumers only when selected. Measure the selected 80% coordination-effort claim during ordinary delivery without making measurement an activation gate. Retain one genuine admitted operation/restart check and qualified adoption/retirement; missing usage and coverage remain explicit. | [Context efficiency](2026-10-05-programme-context-efficiency.md#selected-three-outcome-programme-pilot--2026-10-16), [driver](unified-programme-driver.md) |
| **V3-IMPROVE-01 finite development-process trial — `.github` programme** | Finite trial complete: **process changes delivered; throughput benefit unproven**. Retain measured one-workload profile, qualified source changes, prospective V3 source-cohort evaluation and inconclusive benefit. | [Finite-trial disposition and transfers](2026-10-15-development-process-improvement.md#finite-trial-disposition-and-open-transfers), [design](../designs/2026-10-15-development-process-improvement.md), [DIAG owner](diagnostic-execution.md), [release owner](utel-release-successor.md) |
| **TSDD-KNOWLEDGE-01.4–.5 — SDD/Templates; Wizard release owner** | Finish provider composition, genuine Wizard publication/install qualification, coherent fresh workspace creation and preserving retained extraction. Keep canonical text knowledge bounded to 10 MiB. | [SDD owner](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/roadmaps/tsdd-knowledge-01.md), [Wizard](tsdd-knowledge-wizard.md) |
| **SVG-RELEASE-D — producer and workspace owners** | Close remaining public Wizard/full installed workspace and selected activation outcomes; qualify any later single-lifecycle default separately. | [Release D](svg-release-d.md), [coherent receiver dependency window](svg-coherence-and-instancing-01.md#svg-coherence-018--wizard-coherent-099-dependency-window) |
| **SVG-COHERENCE-01.3–.8 — Rendering/Templates; product consumers** | Complete realistic repeated-glyph software research, affected shared reconciliation/performance changes, integrated reference, consumer candidates, one consolidated host-GPU batch, then remaining publication/adoption/removal. | [Coherence and instancing](svg-coherence-and-instancing-01.md) |
| **GAME-BOX2D-01 adoption / GAME-PORTAL-01 P3 — Game/Rendering/Templates** | Complete for the declared finite headless managed presentation and public Template adoption scope; retain the separate browser/server, viewport and P4 boundaries. | [Box2D](game-box2d-physics.md), [portals](game-area-portals.md) |
| **FABLE-ADOPT-01 shared reference / GAME-TEMPLATE-01 — Rendering/Game/Templates** | Consolidate reusable completed games work into producer APIs and the installed `fable-game` reference; qualify remaining WASM/example composition, publication and clean/retained creation. Section 3 specifies coverage. | [Staged adoption](2026-10-01-staged-fable-game-adoption.md), [disposition](evidence/fable-adopt-01.1-inventory-disposition-20261001.md) |
| **WASM-SHARED-01 consumer joins / .7 — Game and product owners** | Complete selected SC2 joint S1 conformance/S2 browser-source adoption, remaining product/native joins, and retire only proven superseded duplication. | [Shared WASM](2026-10-02-shared-wasm-foundation.md) |
| **LEARN-01.2/.4–.5 / Akka orchestration — Coordination/telemetry/receiver owners** | Complete causal-declaration receiver and producer projection, then qualify publication and receiver-first adoption. Verify served Manager/runtime closure, inactive persistent-v3 images and genuine receiver grant/turn/capture/restart/cleanup; establish census/shared costs and enrollment, then a locked dataset or truthful stopped-window comparison. | [LEARN](2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md), [actor observation](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/learn-01-actor-observation.md), [persistent-v3 receiver](learn-c2-persistent-receiver-v3.md) |
| **COORD-BOARD-V2-01 — `.github`/SDD/Templates/selected products** | Complete for the declared organization outcomes and selected two public qualification workspace cohorts; retain unresolved consumers and fleet boundaries. | [V2 board design](../coordination/2026-09-29-coordination-v2-board-design.md) |
| **COORD-BOARD-V2-01.7–.11 programme cutover — `.github` / programme integration** | Accepted at the declared current programme scope upon protected delivery of the operational decision; preserve strategic navigation and retire duplicate manual current-status upkeep. | [Operational cutover acceptance](../coordination/2026-09-29-coordination-v2-board-design.md#operational-programme-cutover-acceptance--2026-10-18) |
| **V2-LANG-01.2 P3–P5 and full .5 adoption — Coordination/Governance/SDD/Templates/Sandbox** | Complete actual Governance Verify/package-backed SDD integration, blocked-read/uncertain-close capture retention coverage and bounded execution/evidence acceptance; qualify any selected stronger profiles, then qualify C3 public release and C4 receivers. Publish the portable successor; qualify genuine provider/runtime facts and cleanup, supported cold receivers, upgrade/matrix closure and TypeScript/Rust/Go installed adoption. | [Language-independent workspaces](2026-09-29-language-independent-workspaces-and-agent-integration.md), [GOV-423 C2 phases](https://github.com/FS-GG/FS.GG.Governance/blob/91ba9aeb304763f9c75635cf35f94381a789b50e/docs/roadmaps/governance-423-neutral-capability-bindings.md#c2-selected-source-phases), [bounded component window](https://github.com/FS-GG/FS.GG.Governance/blob/9ec0c651e06100a320050f36c9b0ecb954eca71b/docs/roadmaps/governance-423-neutral-capability-bindings.md#c2-bounded-direct-child-source-window), [explicit selector slice](https://github.com/FS-GG/FS.GG.Governance/blob/fb634622deea757f01dd375bf43b647dec98a10b/docs/roadmaps/governance-423-neutral-capability-bindings.md#gov423-c2-explicit-selector-source-slice-2026-10-09), [capture component and remaining gaps](https://github.com/FS-GG/FS.GG.Governance/blob/74dde68efd5fc4a1d5264ae88a82fc86d7858f06/docs/roadmaps/governance-423-neutral-capability-bindings.md#gov423-c2-retained-provider-document-capture-tier-1-local-qualification) |
| **OPS-TYPED-01 remaining shared/operational adoption — Coordination and qualification owners** | Adopt published typed replacements at selected production entrypoints and complete the bounded administrative-owner migration. Product-specific FourD and SC2 slices are tracked in the games roadmap. | [Typed administrative policy](2026-10-01-typed-administrative-fsharp-quint.md) |
| **FBX-07 public successor and upstream assessment — Templates** | Resolve compatibility and qualify genuinely selected upstream updates; retain live assessment without automatic pin changes. | [Accepted public receiver and remaining Xantham boundary](https://github.com/FS-GG/FS.GG.Templates/blob/5acf3bba234595fc6404545777c687140b4390be/docs/roadmaps/fable-bindings-xantham-candidates.md#public-0190-second-runtime-receiver-window--2026-10-14) |
| **UNITYC-01.1d and product delivery — Unity Client owner** | Qualify genuine Unity Editor/Server operation, then selected publication/installed adoption and remaining declared product outcomes. | [Unity design](../2026-09-08-144823-unity-native-shim-fable-client-design-roadmap.md), [owner](https://github.com/FS-GG/FS.GG.Unity.Client/blob/main/docs/roadmaps/unityc-01-reference-admission.md) |
| **V3-ENV-01 provisioned Podman execution / ephemeral nested storage — host provisioner/Coordination; selected receiver owners** | Qualify one finite supplied host/profile, fresh host-backed nested storage and a useful bounded driver/build comparison; deliver an installed provisioner/launcher that automatically handles allocation, binding, launch, teardown and worker-loss recovery after one-time setup with no human host-agent dependency, plus a two-machine blank-slate journey and independently observed scoped cleanup. | [Profile and storage roadmap](2026-10-13-provisioned-podman-execution.md), [design](../designs/2026-10-13-provisioned-podman-execution.md), [prior art](../reports/2026-10-13-provisioned-podman-prior-art.md) |

## 3. Game template consolidation

**GAME-TEMPLATE-01** is the remaining template outcome within **FABLE-ADOPT-01**, not a duplicate
extraction programme. Templates owns composition and generated-workspace proof. Rendering, Game
and the operational tooling owners retain their implementations. Review all relevant completed
BAR/SC2/FourD work against the accepted [F01–F12 disposition](evidence/fable-adopt-01.1-inventory-disposition-20261001.md)
and the [games roadmap](2026-10-07-games-development-roadmap.md), including subsequent fixes.

| Relevant games work | Template outcome still required | Acceptance boundary |
|---|---|---|
| FourD local authority, four-coordinate command/projection, teaching controls and save/replay examples | Finish installed local-session reference, accessible grid/forms and pure adapter examples; document domain extension points and versioned save/replay integration. | Fresh generated workspace exercises an actual encounter/control/save/replay path; semantic parity belongs to FourD. Do not copy FourD rules or change its default. |
| SC2/BAR external authority, accepted/rejected/unknown receipts, reconnect/rearm and revision fencing | Compose published external-authority hosts and a mockable gateway seam; demonstrate stale reply refusal, suspension, fresh generation and explicit recovery without automatic command replay. | Actual generated browser journey; no local Game clock advances a native gateway, and no example grants native command authority. |
| Shared physical input, transformed coordinates, held-key release, editing/IME and ordinary DOM panels | Reuse normalized input/hit testing; include pointer/keyboard alternatives, focus/editing preservation and blur/visibility cases. | Keyboard-accessible generated browser tests retain DOM focus and text editing through updates/replacement. |
| Shared lifecycle, ordered commands, projection coalescing, mount/replacement/disposal | Demonstrate one owner, bounded subscriptions/requests and stale generation refusal in local and external modes. | Ordered commands/completions survive projection bursts; repeated replacement/disposal settles documented owned counts. |
| BAR/SC2 independent WASM guests and FourD optional policy example | Add published host/SDK examples, independent Rust/C authoring inputs, closed compatibility profiles and bounded failure/replacement/disposal behavior. | Clean package-only production Worker/ESM graph and adversarial browser fixtures; native command/schema/ABI interpretation stays in product adapters. |
| Box2D and area/portal work | Offer explicit opt-in physics/presentation examples and traversal-safe interpolation where the selected backend is supported. | Package-only example and retaining-upgrade proof; unsupported browser physics remains explicit and is not a default dependency. |
| Product packaging, unknown-effect refusal, evidence and producer regressions | Fold generic lessons into owning conformance checks and template documentation; remove proven duplicate glue only after parity. | Exact producer packages, template descriptor, SDK and generated bytes join; Fable/Vite and real browser import graph work with empty caches and no repo-source fallback. |

Finish by publishing the coherent template/dependency selection and observing both clean generation
and every promised retaining upgrade. Maintain a mapping of findings to producer, template example,
product-only disposition or explicit non-applicability. A checklist alone is not acceptance.
The [staged adoption plan](2026-10-01-staged-fable-game-adoption.md#remaining-game-template-consolidation)
owns the next implementation window and detailed conformance.

## 4. Parallel opportunities and integration joins

These are technical separation opportunities, not a selected work queue. Apply the current system
focus in section1.6 before proposing new work; deferred product examples below do not dispatch themselves.

These are opportunities, subject to current ownership, resource and CI admission. They do not
reserve workers or authorize operations. Resume existing partial work before creating replacements.
The section 1.3 ready-slice map narrows the next owner and join for each open outcome; these
broader opportunities do not override it or reopen any Done row.

| Parallel work | Why it is independent now | Join that must remain serial |
|---|---|---|
| Ephemeral nested-storage fixtures, actual backing-filesystem/profile inventory and language-specific input reconstruction | Separate allocation, pure receipt/configuration and consumer toolchain surfaces; existing qualified ordinary routes remain available. | One provisioner owns scoped storage lifetime; matched native driver/build comparisons share bounded host resources. Common runtime/profile and release descriptors have one integrator; installed and second-machine proof follow real provisioning. |
| Telemetry dispatch/batch repair; remaining process adapters; accepted SVG implementation/conformance gaps | Distinct owning source surfaces and existing plans. Completed evaluation/Board work and stopped SVG diagnostics are not a fresh queue; pure checks need no LEARN activation. | One integrator for overlapping `.github` source, guidance and release pins; compile/native qualification only inside available resources. |
| Telemetry activity/review integration and dashboard freshness/coverage preparation | Separate producer/consumer surfaces once the measurement contract is fixed. Fixtures can retain unavailable inputs truthfully. | Canonical store migration and live collection follow qualified release/adoption; real reviews need settled populations and usage needs native evidence. |
| Ordinary ready slices and finite-trial transfers | The V3-IMPROVE trial is closed; disjoint open-row source and receiver preparation can use the [routing notes](2026-10-15-speed-first-agent-development.md#2-owned-ready-slices). DIAG .4 and packaged skill adoption remain with their existing owners. | One integrator owns shared guidance/contracts. Installed DIAG and carrier proof follow exact producer/authority joins; measured host/CI capacity and existing PR slots bound concurrency. |
| Game template capability inventory, WASM example composition and portal presentation preparation | Independent example/adaptor touch-sets, using accepted published prerequisites where applicable. | Shared Game/Rendering APIs have one owner; one Templates integrator joins composition and release descriptors. Installed proof follows producer publication; compatible knowledge/Wizard/reference outcomes share the cohort in section 1.1. |
| V2-PROC routine adapter and Linux containment | Independent implementations after shared contract selection. | Containment qualification, public package and consumer adoption need exact shared contracts and profiles. |
| LEARN runtime/toolchain custody inventory and inactive image preparation | Inspection/preparation can proceed while collection repairs land. | Genuine installed turn/capture and later comparisons need admitted runtime/credentials, custody and adequate data coverage. |
| Selected language-provider fixture/receiver preparation | Distinct language contracts/consumers; preparation requires neither gameplay nor GPU findings. Closed Board adoption is excluded. | Publication and live cold-runtime/cleanup qualification keep their own prerequisite fences. |
| SC2 custody/source diagnosis; BAR custody/useful-play preparation; FourD evaluation/package preparation | Separate product repositories and native authority; detailed lanes live in the games roadmap. | Recover original unknown operations before retry. Exclusive native environments/CPU slots remain serialized. |
| FourD presentation pilot; later SC2 and BAR presentation adoption | After a qualified reference, FourD is the first product pilot. SC2 and BAR can then prepare disjoint adapters concurrently once external authority is qualified. | FABLE-ADOPT presentation .5→.6 ordering remains. WASM-only product adoption does not wait for the FourD presentation pilot. |
| Consumer inventories, realistic SVG assets and host packet assembly | Source/fixture preparation does not require GPU access. | One late consolidated host-GPU batch follows candidate readiness; affected artifacts rejoin focused qualification. |

Keep one open PR per dependency chain and at most two managed PRs per repository across campaigns,
using [PR admission](../../tools/pr-lane-admission.py). Read the live hosted runner queue before admission;
free worker slots do not establish CI capacity. Prepare locally while checks drain, repair active PRs
first and use [bounded check watchers](../../tools/routine-delivery.py). Do not enlarge queues just to
keep every lane busy. Local CPU, memory, CLR/process and exclusive-operation limits remain independent.

## 5. Workspace adoption and open-index maintenance

Producer source changes do not alter installed workspace bytes. For each relevant row, publish its
exact producer set, update SDD/Templates/provider pins, qualify clean generation and any promised
retaining upgrade, then separately select activation/default changes. Game template adoption does
not imply BAR/SC2/FourD native acceptance. Knowledge, external authority, WASM, portable language
routes and physics retain their distinct consumer contracts.

When an item is done, close it in its owning plan with durable acceptance evidence. Set its row to
**Done: Yes** in the same `.github` change or immediate cross-repository follow-up, and link the closure
evidence from the owning-plan cell. Retain that compact row and exclude it from the ready frontier.
Keep a partially closed row narrowed to its remaining outcomes with **Done: No**. Do not append a
rolling completed-work log; detailed history belongs in owning plans, reports and the v2 archive.
A CI tick, wait, source checkpoint or publication without required adoption does not close a row.
Games closures update the games index instead; their shared producer/template joins update this one.

No programme-wide completion percentage, total savings or total cost is supported by missing usage,
activity or dependency coverage. Any efficiency view states population, coverage and freshness.
Summed overlapping job queue time is an observed workload metric, not programme delay.

## 6. Conditional open scope

These retain existing design lineage but need selection and a demonstrated need before scheduling.
They are not hidden gates for the active rows.

| Conditional outcome | Selection and dependency | Retained design |
|---|---|---|
| Controlled context/cost comparison beyond bounded functional acceptance | Select a statistical savings or learning question, with adequate baseline/usage and an admitted LEARN window; inconclusive economics does not block independently qualified driver adoption. | [Context measurement](2026-10-05-programme-context-efficiency.md#measurement-and-experiment) |
| Additional scheduling/OR allocation and bounded execution experiment | Measured residual need, supported baseline, independent feasibility checks and a separately admitted shadow/canary window; reuse the existing Akka executor. | [Archived E0/E1 design](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#96-e0e1-later-capability-development) |
| Cooperative F0–F5 enrollment, sandbox contribution, verification and canary | Explicit cooperative scope, authenticated sessions, bounded execution/recovery, quarantined submission and owner-controlled verification. | [Archived F0–F5 design](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#97-f0f5-cooperative-orchestrator-development) |
| Portal P4 queries and later cross-boundary solver/browser physics | Selected product need and independently qualified backend/query contract; not a prerequisite for P3. | [Portals](game-area-portals.md#later-physics-and-workspace-boundaries) |
| Wider receiver/default adoption, community learning intake and other portfolio extensions | Owner selection, current native facts and specific acceptance; historical unchecked proposals do not become automatic work. | [Archived portfolio](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#15-wider-portfolio-preserved-without-hidden-prerequisites), [design inventory](../development-design-inventory.md) |
