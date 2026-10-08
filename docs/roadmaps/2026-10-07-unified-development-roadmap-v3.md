---
title: "FS-GG Unified Development Roadmap v3"
category: Design
categoryindex: 4
description: "Open development outcomes, owning plans and dependency-ready parallel work; game products have a separate roadmap."
---

# FS-GG Unified Development Roadmap v3

**Current roadmap, 2026-10-07.** This replaces the
[unified v2 roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md).
The old document is the archive for completed outcomes, original designs, superseded stages and
historical evidence. This version tracks remaining outcomes and their explicit closure status. The version number describes
this document; it does not select the historical V3 cutover stage or restart V2 migration.

BAR, SC2 and FourD product work belongs to the separate
[games roadmap](2026-10-07-games-development-roadmap.md). Shared Game/Rendering capabilities and
the `fable-game` template remain here. The split preserves existing feature IDs, accountable owners,
plans, attempts and dependencies; moving a row creates neither a new attempt nor an acceptance claim.

The starting evidence is protected `.github` revision
[`65c8d8ce`](https://github.com/FS-GG/.github/commit/65c8d8ced152cea2634e44521cf25a572c26f79c).
Rows describe remaining acceptance, rather than counting delivered source as completed features.
Before selecting an operation, reconcile the owning plan and current native facts. Sealed handoffs
retain their original identities; their old roadmap links lead to the archive and then here.

## 1. Scope and operating boundaries

Use [work-programme](../../.agents/skills/work-programme/SKILL.md) for requested programme advancement
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

## 2. Open work and owning plans

Rows with **Done: No** are open at their stated boundary. A source milestone already delivered is an
input to that boundary, not another task. Set **Done: Yes** only after all required acceptance outcomes
are verified and the owning plan records durable closure evidence. Done rows are closed and excluded
from scheduling; retain their compact evidence-linked row rather than reopening them on recovery.
Conditional work in section 6 is not automatically scheduled.

| Feature and accountable owner | Remaining outcome | Entry or blocking join | Owning plan | Done |
|---|---|---|---|---|
| **UTEL operational collection — `.github` telemetry/Coordination** | Install and qualify the published 0.100.0 current-only dispatch contract; verify fresh dispatch, CI and delivery observations reach the canonical store. Reconcile unresolved observations and recover retained pending publications through their supported original-identity route. | Source checks and publication passed; installed adoption remains pending. Preserve historical records and unknown effects before canonical migration/collection. | [Operational completeness](utel-operational-completeness.md), [local store](utel-local-telemetry-store.md), [correctness](utel-01-telemetry-correctness.md) | No |
| **Routine activity, review and usage coverage — programme/telemetry owners** | Collect planning, implementation, review, validation, delivery, operations and repair spans; submit reviews for settled attempt populations; reconcile native usage with explicit unknowns; continue CI/delivery reconciliation. Every efficiency view exposes coverage, freshness, unresolved observations and its denominator. | Reliable collection and actual settled populations. Overlapping queue intervals cannot prove human idle time or critical-path delay. Delivery counts cannot prove completed features or adoption. | [Observation guidance](../../.agents/skills/work-programme/references/observation.md), [efficiency measurement](process-efficiency-telemetry.md) | No |
| **V2-EFF-01.2–.6 — telemetry, dashboard and receiver owners** | Qualify remaining canonical measurement/correction and completion-assessment behavior; install the coherent schema-14/Host-5 route, migrate the store, enroll supported providers, deploy matching dashboard/feed and run calibration plus a bounded live pilot. | Reuse delivered schema-14 and Responses source and published coherent 0.100/Host 0.5. Actual installation, migration, provider execution and end-to-end collection remain separate. | [Efficiency delivery plan](process-efficiency-telemetry.md), [dashboard](utel-telemetry-dashboard.md) | No |
| **V2-PROC-01.1–.8 — Coordination; SDD/FsQuint consumers** | Finish library evaluation including error-path exit/cleanup; qualify shared I/O/deadline contracts, routine adapter and Linux containment, then publish, adopt selected consumers and consolidate proven replacements with a benefit review. | Resume retained evaluator/source work. Contract selection precedes routine/Linux implementation; native containment needs its own custody and resource profile. | [Shared process supervision](2026-10-05-shared-process-supervision.md) | No |
| **V2-CTX-01.4–.6 — `.github` programme driver** | Complete owner/watcher/restart integration, whole-task controlled comparison and bounded adoption/retirement; retain missing usage and baseline coverage. | Reuse delivered delta/view/reuse/default guidance. Integration can proceed independently; economic conclusions need the admitted comparison and telemetry denominator. | [Context efficiency](2026-10-05-programme-context-efficiency.md), [driver](unified-programme-driver.md) | No |
| **TSDD-KNOWLEDGE-01.4–.5 — SDD/Templates; Wizard release owner** | Finish provider composition, genuine Wizard publication/install qualification, coherent fresh workspace creation and preserving retained extraction. Keep canonical text knowledge bounded to 10 MiB. | Reuse delivered storage/init/path-refusal source and current template inventory. Recover original Wizard publication/custody state before a separately admitted successor; genuinely published dependencies precede full receiver qualification. | [SDD owner](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/roadmaps/tsdd-knowledge-01.md), [Wizard](tsdd-knowledge-wizard.md) | No |
| **SVG-RELEASE-D — producer and workspace owners** | Close remaining public Wizard/full installed workspace and selected activation outcomes; qualify any later single-lifecycle default separately. | Reuse public Rendering 0.32.1 and Templates 0.18.1 and the Templates-only receiver. A Templates-only pass does not close full Wizard/Release-D adoption. | [Release D](svg-release-d.md), [coherent receiver dependency window](svg-coherence-and-instancing-01.md#svg-coherence-018--wizard-coherent-099-dependency-window) | No |
| **SVG-COHERENCE-01.3–.8 — Rendering/Templates; product consumers** | Complete realistic repeated-glyph software research, affected shared reconciliation/performance changes, integrated reference, consumer candidates, one consolidated host-GPU batch, then remaining publication/adoption/removal. | Reuse existing inventory/harness and bounded source repairs. Software evidence precedes the late host batch; wider perf/GPU conclusions remain unknown until qualified. Product-specific acceptance is in the games roadmap. | [Coherence and instancing](svg-coherence-and-instancing-01.md) | No |
| **GAME-BOX2D-01 adoption / GAME-PORTAL-01 P3 — Game/Rendering/Templates** | Qualify presentation/interpolation at traversal markers, fresh opt-in consumer and preserving retained adoption; integrate applicable examples in the Game template. | Public Game 0.17.0 and P1/P2 are inputs. P3 source preparation is not native/installed acceptance. Browser/Fable physics and rollback limits require explicit qualification; do not imply cross-area contact solving. | [Box2D](game-box2d-physics.md), [portals](game-area-portals.md) | No |
| **FABLE-ADOPT-01 shared reference / GAME-TEMPLATE-01 — Rendering/Game/Templates** | Consolidate reusable completed games work into producer APIs and the installed `fable-game` reference; qualify remaining WASM/example composition, publication and clean/retained creation. Section 3 specifies coverage. | Reuse the accepted inventory/disposition and delivered local/external reference slices. Producer changes require reproducers; product authority stays local. Product pilots/removal belong to the games roadmap. | [Staged adoption](2026-10-01-staged-fable-game-adoption.md), [disposition](evidence/fable-adopt-01.1-inventory-disposition-20261001.md) | No |
| **WASM-SHARED-01 remaining producer compatibility / .7 — Game** | Qualify the additive SC2 compatibility successor and genuine installed guest SDK/profile acceptance where still missing; support remaining consumer joins and retire only proven superseded duplication. | Reuse the connected published 0.2.0 boundary and later native publisher evidence in the owner plan. Browser deadlines do not prove deterministic instruction fuel; each adapter retains its wire/ABI and native authority. Product adoption is in the games roadmap. | [Shared WASM](2026-10-02-shared-wasm-foundation.md) | No |
| **LEARN-01.4–.5 / Akka orchestration — Coordination/telemetry/receiver owners** | Verify served Manager/runtime closure, inactive persistent-v3 images and genuine receiver grant/turn/capture/restart/cleanup; establish census/shared costs and enrollment, then a locked dataset or truthful stopped-window comparison. | Reuse accepted O0–O3/Akka core and delivered .1–.3 source. Installed runtime, custody, cgroup/UID and two cold-image qualification precede activation; no calendar date follows from source delivery. | [LEARN](2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md), [persistent-v3 receiver](learn-c2-persistent-receiver-v3.md) | No |
| **COORD-BOARD-V2-01 remaining .4–.6 — `.github`/SDD/Templates** | Reconcile current open population and delivered-history disposition; finish carryover/consumer adoption and published fresh/retained workspace qualification. | Refresh actual issue/project facts. Historical four-target bindings cannot establish currentness after a target closes. Reuse existing bounded refresh and installed `check-board` qualification. | [V2 board design](../coordination/2026-09-29-coordination-v2-board-design.md) | No |
| **V2-LANG-01.2 P3–P5 and full .5 adoption — Coordination/SDD/Templates/Sandbox** | Publish the portable successor; qualify genuine provider/runtime facts and cleanup, supported cold receivers, upgrade/matrix closure and TypeScript/Rust/Go installed adoption. | Reuse source-qualified portable executor, exact-SDK repairs and image profiles. Retained Podman/readiness refusals remain failures or unknown facts, not runtime acceptance. | [Language-independent workspaces](2026-09-29-language-independent-workspaces-and-agent-integration.md) | No |
| **OPS-TYPED-01 remaining shared/operational adoption — Coordination and qualification owners** | Adopt published typed replacements at selected production entrypoints and complete the bounded administrative-owner migration. Product-specific FourD and SC2 slices are tracked in the games roadmap. | Each product's source, exact artifact and native safeguards join separately; .1/.2 source delivery does not qualify operations. | [Typed administrative policy](2026-10-01-typed-administrative-fsharp-quint.md) | No |
| **FBX-07 public successor and upstream assessment — Templates** | Qualify public installed second-runtime composition and resolve genuinely selected upstream updates. | Reuse delivered candidate and existing assessment. `updates-found`/`unqualified` is an observation, not accepted upstream adoption. | [Xantham owner](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/roadmaps/fable-bindings-xantham-candidates.md) | No |
| **UNITYC-01.1d and product delivery — Unity Client owner** | Qualify genuine Unity Editor/Server operation, then selected publication/installed adoption and remaining declared product outcomes. | Managed-reference/browser source is an input; native title/engine access and exact operator admission are separate. | [Unity design](../2026-09-08-144823-unity-native-shim-fable-client-design-roadmap.md), [owner](https://github.com/FS-GG/FS.GG.Unity.Client/blob/main/docs/roadmaps/unityc-01-reference-admission.md) | No |

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

These are opportunities, subject to current ownership, resource and CI admission. They do not
reserve workers or authorize operations. Resume existing partial work before creating replacements.

| Parallel work | Why it is independent now | Join that must remain serial |
|---|---|---|
| Telemetry dispatch/batch repair; process evaluator cleanup; SVG command-stream research; board population reconciliation | Distinct owning source surfaces and reusable local plans. Board facts and pure SVG streams need no native games or LEARN image activation. | One integrator for overlapping `.github` source, guidance and release pins; compile/native qualification only inside available resources. |
| Telemetry activity/review integration and dashboard freshness/coverage preparation | Separate producer/consumer surfaces once the measurement contract is fixed. Fixtures can retain unavailable inputs truthfully. | Canonical store migration and live collection follow qualified release/adoption; real reviews need settled populations and usage needs native evidence. |
| Game template capability inventory, WASM example composition and portal presentation preparation | Independent example/adaptor touch-sets, using accepted published prerequisites where applicable. | Shared Game/Rendering APIs have one owner; one Templates integrator joins composition and release descriptors. Installed proof follows producer publication. |
| V2-PROC routine adapter and Linux containment | Independent implementations after shared contract selection. | Containment qualification, public package and consumer adoption need exact shared contracts and profiles. |
| LEARN runtime/toolchain custody inventory and inactive image preparation | Inspection/preparation can proceed while collection repairs land. | Genuine installed turn/capture and later comparisons need admitted runtime/credentials, custody and adequate data coverage. |
| Board carryover and language-provider fixture/receiver preparation | Distinct contracts/consumers; neither requires gameplay or GPU findings. | Publication and live cold-runtime/cleanup qualification keep their own prerequisite fences. |
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
| Additional scheduling/OR allocation and bounded execution experiment | Measured residual need, supported baseline, independent feasibility checks and a separately admitted shadow/canary window; reuse the existing Akka executor. | [Archived E0/E1 design](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#96-e0e1-later-capability-development) |
| Cooperative F0–F5 enrollment, sandbox contribution, verification and canary | Explicit cooperative scope, authenticated sessions, bounded execution/recovery, quarantined submission and owner-controlled verification. | [Archived F0–F5 design](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#97-f0f5-cooperative-orchestrator-development) |
| Portal P4 queries and later cross-boundary solver/browser physics | Selected product need and independently qualified backend/query contract; not a prerequisite for P3. | [Portals](game-area-portals.md#later-physics-and-workspace-boundaries) |
| Wider receiver/default adoption, community learning intake and other portfolio extensions | Owner selection, current native facts and specific acceptance; historical unchecked proposals do not become automatic work. | [Archived portfolio](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#15-wider-portfolio-preserved-without-hidden-prerequisites), [design inventory](../development-design-inventory.md) |
