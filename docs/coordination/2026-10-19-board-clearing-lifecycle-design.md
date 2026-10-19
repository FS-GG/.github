# BOARD-LIFECYCLE-01 — Continuous board clearing and coherent skill retirement

Design and implementation plan, 2026-10-19. Owning repository: `FS-GG/.github`.
Stage: design for the user's single board/process outcome; implementation follows this design.
Root owns integration, the native outcome, live planning writes and protected delivery. The dedicated
planner owns this document only. Strategic navigation is the [Unified v3 outcome index](../roadmaps/2026-10-07-unified-development-roadmap-v3.md#2-open-work-and-owning-plans);
this design belongs to its process/coordination scope without reopening completed CTX or board migration.
The native owning Outcome is [#4701](https://github.com/FS-GG/.github/issues/4701), created and
independently read back by Root after semantic deduplication. Its board visibility/classification and
protected enrollment are separate effects; the earlier 25-member census below predates its addition.

The requested outcome is a coherent way to add, understand, decompose, execute, accept and resume work
until the authorized board scope is cleared or genuinely cannot advance. It includes meaningful native
decomposition of the current large outcomes and retirement of contradictory process routes. It does not
authorize delivering every other product outcome merely because its next delivery is identified.
The design, skills, necessary tools, native decomposition, distribution and demonstrated use are one
outcome. Its implementation slices do not become competing process-improvement outcomes.

## Evidence and what is actually missing

Source inspection used protected `.github` revision `b694d798345e8528fd1d31f5d1256f9efc983cc9`.
Atlas was consulted first. Every citation in these active records was checked against its immutable Git
blob and matched its recorded SHA256: `github/work-board-intake-triage` (`e8bc1b4f`),
`github/work-board-parallel-opportunities` (`4d86a276`), `programme/stop-resume-recovery` (`66d41461`),
`programme/context-restart-navigation-boundary` (`95f0c5f3`),
`github/programme-binding-custody-recovery` (`aa70263d`) and `contract/new-sdd-workspace` (`63854592`).
These are knowledge pointers, not authority or current operational receipts.

Root supplied a complete native Project 3 census and current open issue bodies, direct-child lists and
native dependency lists collected on 2026-10-19. That bounded census has 25 members: 19 open, comprising
15 Outcomes and four Deliveries; six closed members retain their scoped completion. Its private original
is retained by Root. The selected operational baseline is the accepted organization
binding5/schema3/inspection3/integrator2 path with authenticated installed CLI 108, 22 enrolled members
and three visible unenrolled members. These figures describe that census, not an invariant or a future
cohort. Public documents carry public issue links and conclusions, not private enrollment identities,
bindings, raw snapshots or operation custody. Re-read all determining facts before mutations or dispatch.

| Capability | Evidence inspected | Disposition |
|---|---|---|
| One current coordinator, native six-field ownership, source/effect separation | [work-board](../../.agents/skills/work-board/SKILL.md), [organization contract](../../.agents/skills/work-board/references/organization-v2-inspection.md), [accepted V2 plan](2026-09-29-coordination-v2-board-design.md) | Implemented and accepted at the selected baseline; reuse. Some prose still calls an older binding current. |
| All-items intake, useful parallel opportunity checks, gap-only stop/restart | Current work-board, stop/continue and the verified Atlas records above | Recently delivered safeguards; preserve and integrate. Do not present them as new inventions or repeat their completed acceptance. |
| Consistent filing and auxiliary entrypoints | [p-add](../../.agents/skills/p-add/SKILL.md), [padd-item](../../.agents/skills/padd-item/SKILL.md), [lane-steward](../../.agents/skills/lane-steward/SKILL.md), [board-analyst](../../.agents/skills/board-analyst/SKILL.md), [intra-repo parallel work](../../.agents/skills/intra-repo-parallel-work/SKILL.md) | Missing. p-add still requires organization V1 reconcile/apply/flush/intake/add. Lane-steward and parallel-work still prescribe claims; analyst still mandates packets and a separate filing actor. |
| Single-item execution through complete acceptance | [pnext-item](../../.agents/skills/pnext-item/SKILL.md), [work-roadmap](../../.agents/skills/work-roadmap/SKILL.md), [routine delivery](../../tools/routine-delivery.py) | Same-owner source delivery exists. pnext's source-merge return does not by itself settle publication/installed acceptance or tell a coordinator to continue clearing. Two execution instructions duplicate rules. |
| Safe board planning changes | [V2Migration contract](../../src/FS.GG.Coord.GitHub/V2Migration.fsi), [CLI implementation](../../src/FS.GG.Coord.Cli/BoardV2Application.fs) | Existing typed preparation/apply/readback supports memberships, views, field creation and **absent-only** planning/classification initialization. It is not a general existing-value transition or native issue creator/closer. |
| Meaningful decomposition | Current 15 Outcome bodies and direct-child reads | Most have no native delivery children. EFF and LEARN have completed source children but retain substantial unrepresented acceptance. Empty native dependency lists do not clear owning-plan prerequisites. |
| Semantic guard coverage | [quality validator](../../scripts/check-skill-quality.py), [filing checks](../../tests/skill-quality/validated-intake-filing.py), [routine-route controls](../../tests/skill-quality/routine-board-route.py), [catalog generator](../../scripts/generate-driver-manifest) | Current guards actively require parts of the legacy recipes. Green source checks therefore do not establish coherent entrypoint behavior. |
| Distributed consumer behavior | [registry roster](../../registry/repos.yml), [driver manifest generator](../../scripts/generate-driver-manifest), [driver staging](../../src/FS.GG.Drivers/stage-drivers.py), [Creator receiver](../../scripts/NewSddWorkspace/ProductBoard.fs), accepted Creator019/CLI108 evidence in the V2 plan | Selected prior receiver behavior is accepted. This redesign still needs its own final payload/publication/installed joins. No fleet-upgrade claim follows. |

The historical `work-programme` directory and tests are mostly compatibility loaders and optional
helper contracts; their names do not prove an active second coordinator. Conversely, operator-only
skills are still callable and cannot be dismissed as archives. `check-board` correctly fences legacy
product recipes, but its prepared-only organization wording is stale. Historical failed attempts and
Unknown effects are evidence to retain, not obsolete process code to delete.

## Architecture: one loop, two primary actions

`work-board` owns the continuous loop. `pnext-item` owns one selected Delivery through its declared
acceptance boundary. The short intake and compatibility entrypoints call these same instructions;
there is no new scheduler, queue, service, lifecycle ledger, mandatory packet or seventh issue field.
Roadmaps retain technical scope, acceptance and strategic navigation. Native issues retain the existing
Owner, Done when, Current deliverable, Next action, Blocked on and Acceptance evidence fields.
Comments and native PR/run/operation links retain evidence and decisions without a second status table.

An Outcome expresses the whole user promise. It is never an implementation assignment. A Delivery
expresses an independently useful result and can be assigned after admission. Work level remains
Outcome or Delivery; Status, Track and Roadmap retain their owner-intent meanings. A source-only
Delivery is legitimate when that source is independently useful and its dependent adoption is explicitly
retained. It cannot silently narrow an Outcome's installed, recovery, participant or publication promise.
A direct user assignment with sufficient existing scope need not manufacture an issue merely to execute;
board-derived selection still requires the actual native cohort and classification contract.

### The clearing loop

1. **Recover the selected scope.** Resolve organization versus configured product explicitly. Retrieve
   Atlas pointers and current native issues, owners, PRs, dirty worktrees, pending requests, original
   operations and relevant resource holds. Continue an unambiguous existing owner/branch/PR before
   creating another lane. Instruction loading is not executable adoption.
2. **Reconcile All items.** Read complete unfiltered membership, including missing Status/Work level,
   unexpected and unenrolled cards, and reconcile any outstanding intake in the selected scope.
   Investigate each card's native issue, full promise, owning plan, actual delivery and dependencies.
   Missing metadata is work to triage. Each unfinished intake has an accountable intake owner, concrete
   next action and reconsideration event in its existing issue. Do not demand a complete organization
   census to repair one explicitly assigned issue; a requested board-clearing pass does require its
   selected complete population.
3. **Shape the next useful work.** Deduplicate and reuse current Deliveries/PRs; decompose broad Outcomes
   as described below. Classify deliberately and enroll only through the protected selected route.
   Reconcile stale blockers from producer evidence, not closure labels alone. Scope unresolved intake
   and admission gaps so independently authorized work continues.
4. **Select fairly within admission.** Prefer finishing owned deliverables and their applicable consumer
   acceptance, then useful prerequisites and the closest independently useful outcome. Respect explicit
   user priority. Compare all eligible Deliveries before choosing, including older deferred work. Record
   a material reason and reopening event when a delivery is repeatedly passed over. Do not let endless
   new process fixes starve product work or use fixed weights/turn counts as a substitute for judgement.
5. **Execute and integrate.** The same owner carries preparation, implementation, repair, review response,
   exact-head CI, merge and applicable publication/installed acceptance. One integrator owns each shared
   surface. While hosted work waits, reconsider useful disjoint preparation, review or evidence work.
   Account admitted demand before selecting another operation; a free model slot is not machine capacity.
6. **Accept and reconsider.** Read back the delivered source, checks and actual required consumer effects.
   Record the exact accepted slice and remaining assertions; close only the fulfilled native promise.
   Revisit its parent, newly supplied prerequisites, unfinished intake and parallel opportunities, then
   return to selection. A merge, a worker return, one completed milestone or one stopped watcher never
   independently ends the coordinator.

This is an execution policy, not a new mutable state machine stored beside GitHub. The existing
qualified integrator remains the supplier of one-invocation action/prerequisite/intake/capacity facts.
Any behavioral change to its actual compiled selection contract needs corresponding source and
installed qualification; prose cannot create a second selection authority.

### Completion, waiting and quiescence

The coordinator may finish the requested scope when its full acceptance is established. A broad
board-clearing run can instead become **quiescent**: complete current intake has been reconciled,
no authorized action is eligible in remaining capacity, and all remaining work has a concrete blocker,
supplier/owner, deferred-scope decision or attributed Unknown with a reopening condition. Quiescence
is not board completion. Untriaged cards are not quiescence; neither is the absence of a Ready filter result.

If a running owned job or expected reply can resolve the next step within its existing bounded window,
continue useful independent work and observe it through its deadline. Do not end merely because it
is waiting, and do not poll unchanged state through model turns. When an external dependency has no
currently actionable continuation, return a concise restart pointer and its next observation condition;
state whether any actual monitor remains running. No monitoring is implied after the session ends.
A user stop/cancel overrides further selection immediately; necessary retirement and recovery saving
follow the existing stop skill. Cancelled scope is not completed scope.

## Intake, decomposition and native field changes

### One evidence-driven intake path

`p-add` means organization intake; `padd-item` means the explicitly configured product's intake.
Both call a single canonical intake reference under work-board. Filing alone does not start execution.
A clearing invocation continues the loop after intake. A direct invocation to add work returns the
created/reused native issue and exact enrollment/planning disposition, or a concrete draft if the
actual effect route remains unavailable.

Resolve the owner from source, contracts and actual users of the behavior. Search native open issues
and PRs, relevant recent closures and owning plans for semantic overlap. A title match is insufficient.
Reuse a matching issue; attach evidence to an existing class/cause issue when it really covers the
finding. Repair an in-scope finding on the current PR where useful. Otherwise create a distinct issue
only when the requested result, acceptance or accountable owner makes separate work worthwhile.
A user-requested capability need not be a present failing test to deserve intake. Independent analysis
can help an ambiguous filing decision, but availability of an analyst does not require another actor.
Retain a rejected/duplicate/deferred finding in its existing relevant native context with its reason.

Use the six existing fields for new V2 owner records and retain original Acceptance/Verification text
where it carries a broader promise. Infer neither implementation readiness nor ownership from issue
creation. Native issue creation, hierarchy linking, board membership, protected enrollment, initial
classification and subsequent planning transitions are separate effects with separate readback.
A failed later effect leaves the earlier issue usable and visibly pending; it does not justify recreating it.

### Shape every current Outcome, materialize only useful Deliveries

For every live Outcome, read the entire original promise and its current owning plan. Identify accepted
scope, residual independently useful results, shared prerequisites, actual suppliers and the next
executable window. Reuse existing native children and active PRs. Give each next delivery a meaningful
result, accountable owner, acceptance, actual touch-set and required suppliers; broad or unknown
later scope stays as an outline in the parent/plan until it is useful to materialize. Do not create a card
for every paragraph, closed milestone or prospective test. This is a complete shaping pass over the
current Outcomes, not a command to create every later outline card now.

Native hierarchy expresses ownership/decomposition; prerequisite links express actual sequencing.
A Delivery can depend on a shared result owned elsewhere without copying it or having two parents.
A blocked sibling or parent does not block disjoint source work. Empty child/dependency reads count
only when complete; truncated, missing, stale or cyclic relationships remain Unknown for affected joins.
All children closed still does not establish the parent's undecomposed acceptance.

### Bounded write route and the real gap

Keep ordinary authorized native issue creation/edit/link/close on existing authenticated GitHub paths;
prepare the concrete content and verify target, current body/state/relationships and authority before
sending. Preserve original response/readback and partial effect identity in the existing private
operation context. An ambiguous creation response requires original-result reconciliation, not a
second create. User authorization to do this redesign includes its necessary native decomposition;
no approval ceremony is added for each ordinary child issue.

Board enrollment remains the existing authenticated private manifest/public protected commitment pair.
Native linking or adding a Project item does not enroll it. Prepare the exact new cohort from current
qualified membership, preserve unrelated members/fields/views, obtain the required protected delivery,
and independently qualify the installed reader against the successor pair. The 22-member enrolled
baseline is not silently widened by this document or by the 25-member visible census.

For missing field values, reuse the installed typed migration writer's absent-only initialization.
For existing planning/classification changes, first establish whether an already qualified bounded
native route covers the exact effect. The inspected migration writer does not. The smallest necessary
extension is an exact selected transition in the existing board writer: target issue/item/field,
expected current value and determining revision, requested next value, accountable owner and selection
reason, current schema/options and a bounded cooperative planning window. Re-read immediately before
sending, refuse conflicting user edits, independently read back only the intended change, and preserve
all unrelated values. Preserve native API concurrency limits; do not claim a true server-side compare-
and-swap when only a cooperative window plus pre/post read is available. Conflict detection after a
write requires explicit reconciliation, never silent overwrite or rollback from an old snapshot.

Do not clear a populated field to exploit absent-only initialization. Do not repurpose Observation
refresh, import, the old V1 queue or a child link as a planning writer. No general GraphQL command
escape hatch is added. Extend the existing closed typed contract and its tests only as needed; its
prepared operation artifact is effect custody, not a scheduling ledger. Native closing and Status
change may partially complete: preserve the original close, reconcile the remaining independently
read-back effect, and never replay both because the second response was lost. Status reflects the
owner's explicit disposition, not an automatic inference from green checks. Parent closure always
has its own complete acceptance decision.

### Scale complete intake without weakening action admission

Decomposition increases the real population. Root's retained installed108 full22-member reads consumed
about331requests and175–186seconds inside the unchanged500-request/270-second ceiling; the earlier107
read exhausted its request ceiling. Those are attributed observations, not a fitted cost model.
Enrolling useful children for15Outcomes can exceed the currently demonstrated window. Repeatedly
stopping at that ceiling, dropping original members, silently selecting a smaller enrollment or raising
an already-running budget would defeat the requested design.

The source mechanism is concrete: `V2ProjectionSource.inspectFixed` collects source/dependencies/plan,
planning and hierarchy per enrolled issue, reads the native integrator, then repeats each issue's
source/planning/intake/hierarchy at its final barrier and rechecks complete membership/protected
enrollment. `BoardV2Application.bounded` applies the500/270 ceilings to actual transport sends.
Selection's supplied slices do not reduce this eager acquisition. Thus supplying fewer integrator
slices is **not** an implemented selective-inspection optimization.

Choose a bounded batched full-cohort reader as the first implementation, preserving the existing
semantics. Acquire complete project membership and visible planning once per initial/final phase;
batch selected issue identity/body/hierarchy and compatible source requests through bounded native
queries, preserving per-node identity, pagination and per-field failures. Share genuinely identical
protected-plan/policy acquisition only within one phase and exact identity; final determining reads
remain fresh. Native PR ownership/files/checks and unenrolled in-flight conflicts still cover the
selected scope. A partial batch cannot turn missing items or nested pages into empty results.
All admitted members remain represented; closed Outcomes need their truthful presence/closure
facts but are never dispatch candidates. Optimize transport composition, not acceptance coverage.

The cheap complete membership/intake census and expensive action qualification have different
purposes. Use the census to find every card and prepare the next useful candidates; use fresh actual
action joins and final barriers before execution. Under108 the actual installed inspection still
acquires its full enrolled cohort; do not advertise an unimplemented per-action mode. A future mode
that keeps a complete census but reads only selected actions' transitive acceptance/ownership/conflict
closure would need a separately versioned completeness contract, explicit deferred/Unknown coverage,
qualified caller/binding adoption and real acceptance. It is not the initial shortcut in this plan.

Qualify the batched reader against the actual expanded cohort selected in .4 and a larger bounded
fixture with hierarchy/dependency pagination and concurrent changes. Compare old and new decisions
on the same complete fixtures, count actual transport requests, exercise loss/drift/budget exhaustion,
and retain a causal control proving an omitted final barrier is rejected. A real-size installed read
must fit a prospectively admitted unchanged finite budget with complete coverage before that expanded
cohort becomes the active scheduling population. Preparation may create visible pending children,
but they cannot be reported schedulable before their exact protected enrollment/caller is accepted.
If batching still cannot fit the measured cohort, stop that adoption boundary and materially replan a
versioned multi-window acquisition policy; do not hide unsampled members or make a chain of partial
reads look like one atomic current snapshot. Original108/Creator019 artifacts remain immutable.

### One maintained interpretation of native results

Root's prior bounded native owner transition demonstrates an available item-edit path, but its
post-send collector initially expected the wrong union shape and needed separate read-only
reconciliation. That evidence is specific to returned text/select values, not all hidden project fields.
Replace repeated ad-hoc decoder assumptions with maintained interpretation in the existing caller/
qualification path: distinguish product3/organization5 report shapes, text/select values, and the
migration-readback private custody output from ordinary stdout. Surface a lost/invalid decoder result
as Unknown after send and reconcile the original identity without resend. Do not loosen native
preservation claims to whichever fields a convenient collector happens to decode. Cover actual
reported output forms and their refused/partial variants in focused fixtures; this is reader reuse,
not a new packet schema, ledger or central workflow service.

The maintained interpretation now lives in
[`board_v2_native_results.py`](../../scripts/board_v2_native_results.py), consumed through the
existing command helper's opt-in qualifier. It distinguishes genuine unchanged preparation reports,
original migration custody readback and product/organization inspections. Decoded observations grant
no provenance, authority, admission or retry permission. Root demonstrated the bounded existing-value
route on [SDD #928](https://github.com/FS-GG/FS.GG.SDD/issues/928): exact adjacent native preimages
and options, one item-edit from Delivery to Outcome, then independent complete reported preservation.
A conflicting preimage refuses before send. This uses the existing native API without extending the
absent-only writer or its grammar. The cooperative window is not server CAS; opaque system-field
contents and hidden native fields remain unproved. The full original promise stays open.

## Current Outcome decomposition

This table is a design map derived from the complete 2026-10-19 native census and owning acceptance.
It is not a second live status board. Root reconciles its proposed children against fresh native state
before materialization; roles below identify the supplying repository/owner, not newly running agents.
Every original Done when and acceptance anchor survives verbatim or by an explicit retained link.
Existing accepted work is reused. No row authorizes a product runtime, publication or replay.

| Existing Outcome | Next independently useful Delivery to create or reuse | Full remainder retained; dependencies and useful parallel work |
|---|---|---|
| [UTEL #4638](https://github.com/FS-GG/.github/issues/4638) | Qualify one authenticated, durable producer-to-current-receiver receipt with the existing application/receiver owners. Separate compatible production cutover from source preparation and identify the exact missing credential/receiver assertion. | Preserve installed crash/full-spool recovery, dedup/correction, durable restart and provider population/usage limits. Published Host0.7 staging and partial local capture are accepted only at their recorded scopes. Its stale Next action pointing to completed #4682 needs reconciliation, not another publication. Home/application readiness is an actual supplier; passive local work remains independent. |
| [Coverage #4639](https://github.com/FS-GG/.github/issues/4639) | Account for actor, review and available usage coverage for one already necessary real Delivery using existing observation tooling. Root/telemetry owner names actual gaps and suppliers. | Retain the whole settled population and honest time/token/cost denominators; one sample is not complete coverage. Share UTEL facts, not a duplicate collector or demonstration PR. Missing counters cannot block ordinary development. |
| [Efficiency #4640](https://github.com/FS-GG/.github/issues/4640) | Qualify the first real provider → receiver → projection → browser facts-and-coverage journey, reusing UTEL's receiver receipt and the completed #4679 source child. | Later interruption/Unknown/recovery/correction, calibrated explanations, assessment adoption and rollback remain separate. The dashboard owner and receiver owner have distinct assertions; expensive compatible setup can be shared without merging conclusions or claiming savings. |
| [PROC Coordination #1026](https://github.com/FS-GG/FS.GG.Coordination/issues/1026) | Resolve the concrete complete-trace/enforcing-profile qualification boundary against the current producer; then represent coherent publication and selected consumer acceptance as the next independently useful result. Continue the original publisher where applicable. | Four consumed unaccepted qualification ordinals, raw8MiB/max4 limits and original effects remain binding. Do not create a fifth attempt by renaming it. Linux containment and consolidation retain separate plan acceptance; ordinary consumer source/adoption need not inherit unrelated containment dependencies. |
| [Knowledge #4642](https://github.com/FS-GG/.github/issues/4642) | Qualify the first missing knowledge-aware clean Wizard creation assertion on the exact compatible public producer set; use the existing Wizard release owner and original custody. | Retained preserving upgrades, refusal controls, useful concise knowledge and the 10MiB canonical-store boundary remain. Reconcile current published Creator evidence against the actual knowledge assertion rather than repeating old W013 history. The stopped unlanded bulk store remains non-delivery. |
| [Release D #4644](https://github.com/FS-GG/.github/issues/4644) | Qualify the missing full Wizard/workspace receiver assertion for the selected current SVG coherent successor, sharing exact compatible setup with #4642. | Preserve the original public/default, explicit lifecycle/SVG choices and separately promised retained upgrades. Templates-only acceptance is insufficient. Durable public hosting #491 stays its separate scope; source versions do not prove activation. |
| [SVG coherence #4645](https://github.com/FS-GG/.github/issues/4645) | Continue the actual existing Rendering owner/PR [#1447](https://github.com/FS-GG/FS.GG.Rendering/pull/1447), refreshed head `88fd8cec62b7a0db6f53fccd07143384ccfbe730`, for bounded public-circle count scaling. Materialize/reuse one Delivery around its actual 100/250/500 shared/expanded fixture, maintained 17-control/three-trial software qualification and exact source delivery, retaining the original shadowing failure. | Normal packed three-browser CI does not execute this complete research stream. Preserve .3–.8, realistic-glyph/update-path/performance matrix, actual-player composition, late consolidated host-GPU, publication and installed adoption. Accepted camera/A-B witnesses remain; no timing improvement, GPU, profiling restart or new native operation is implied. |
| [Fable/reference #4646](https://github.com/FS-GG/.github/issues/4646) | Qualify the first missing clean downloaded shared-game-reference assertion from the compatible published set under the existing Templates receiver owner. | Preserve retained upgrades, accessibility/provider journeys, remaining composition and FourD-first staged presentation adoption. Reuse 0.19.0 only where the assertion/source still matches. WASM-only work remains independent; template compilation is not whole-game/native acceptance. |
| [Shared WASM Game #718](https://github.com/FS-GG/FS.GG.Game/issues/718) | Materialize the concrete SC2 reserved-storage/setup/custody supplier result and its dependent native consumer qualification as distinct meaningful Deliveries where ownership/effects differ. Reuse existing storage/source issues and accepted producer both-feed qualification. | Preserve real protocol/gameplay, replay/recording, container recreation/recovery and remaining foundation .7. Actual setup/mount, relevant resource disposition and exact operation binding gate native work. Existing private-build license scope and official acquisition are already settled; no repeated upload/consent or new physical-machine requirement. |
| [LEARN #4647](https://github.com/FS-GG/.github/issues/4647) | Represent the authentic installed controller/profile/custody supply and the dependent causal receiver journey, reusing completed recipe-source child #4681. Home/SystemAdmin supplies actual runtime capability; the LEARN owner qualifies its use. | Preserve actor attribution, reader inventory, cgroup/UID enforcement, cold-image/receiver cases, persistent recovery, refusal/conflict/replay and selected adoption. UTEL cannot certify these stronger assertions. Statistical/economic decision and conditional community scope remain in the owning plan; do not invent a controller factory to bypass unavailable capability. |
| [Language #4648](https://github.com/FS-GG/.github/issues/4648) | Reuse the active Governance loader PR462 and supplier SDD#1113; define its supported package/normal-acceptance Delivery. Preserve and connect the existing #3008 provider hierarchy rather than recreating it. | Portable P3–P5 enforcing-profile/custody/publication/fresh-retained acceptance is a separate delivery sequence; #3008 does not cover it. Provider contracts → neutral Governance → language packs → wizard catalog remain actual cross-repo dependencies. Expired supplier reply times require a fresh original-thread read, not an assumed failure or repeated request. |
| [Typed operations #4649](https://github.com/FS-GG/.github/issues/4649) | Verify the delivered FsQuint receiver's typed entrypoint during its next genuinely useful routine change, under the existing FsQuint/Coordination owners. | Keep each remaining declared administrative consumer/profile and real old-route retirement explicit. No synthetic PR solely to produce activation evidence. Accepted Coordination/Templates/SDD/BAR activations remain accepted; source eligibility does not retire the whole administrative population. |
| [FBX Templates #725](https://github.com/FS-GG/FS.GG.Templates/issues/725) | Settle one exact already-assessed upstream candidate's netstandard2.1 compatibility question, producing an adopt/retain/unqualified decision with concrete evidence. | Current alpha.2 executable baseline and accepted public0.19.0 receiver remain. If adoption is selected, retain actual declared runtime matrix, publication and installed compatibility as separate acceptance; an upstream update notice is not a pin decision. |
| [Unity Client #7](https://github.com/FS-GG/FS.GG.Unity.Client/issues/7) | Continue the selected persistent Personal direction from the actual Unity owner plan: Home-owned rootless Podman with private Hub/browser sign-in, source-selected Editor `6000.3.18f1` and the existing fixed `reference-import-compile` entry/result. Recover Home's exact environment/action executor and current supplier interface before materializing its native import/compile Delivery; qualify that bounded observation through same-operation source/environment/result, independent terminal exit and durable output custody, then .1d's actual built-server result. | Personal eligibility/activation, Home rootless/display/keyring/UI/action/resource/custody and real Editor behavior remain unqualified. The container route is experimental; preserve exact failed tuple and use owner-activated supported-host fallback if needed. Import/compile source or managed tests do not establish Editor tests/server build. Preserve .1a–.1c, unqualified external-title assessment, unresolved NGO/backend/codec/transport/package-lock/scene choices for their steps, and .2–.7 reference/product/publication/clean-retained promises. |
| [Environment #4650](https://github.com/FS-GG/.github/issues/4650) | Represent the authentic protected policy/profile/custody supplier result and the first admitted unattended provision/run/retire journey; reuse accepted current access/TLS and disposable backup/restore preparation. | Retain distinct development/delegated/service profiles, fresh-current-data production cutover, durable replacement credentials, worker-loss recovery, storage driver/cleanup evidence and two-machine blank-slate portability. Home source PASS is not a native executor. SC2's same-host scope does not inherit this outcome's two-machine condition. |

The four existing open Deliveries also require intake review: SDD#928 (provider/catalog producer),
SDD#935 (consumer-defined sources), Templates#441 (four language packs) and `.github`#3010 (wizard/catalog
adoption). Their existing acceptance and dependency sequence remain; SDD#935 is visibly unenrolled.
Do not reconstruct these as new Outcome copies. SDD#928's original promise spans contract, SDK/harness,
publication and downstream compatibility: if current evidence shows it now needs independent delivery
ownership, reclassify it through the bounded route and preserve its original identity/child935. Do not
infer Outcome solely because it already has a child. The root shaping pass must decide and document
that question from the complete existing scope. Templates#441 may share Node infrastructure while
retaining distinct TypeScript/JavaScript/Rust/Go results and their matrix; do not force four cards if one
coherent owner/candidate is better. Closed #3009, #4634, #4641, #4677, #4681 and #4682 retain their exact
closure; this feature does not reopen them or backfill retrospective Done cards.

Older process issues outside the census also need a semantic disposition within this single outcome:
[deterministic-process #2130](https://github.com/FS-GG/.github/issues/2130),
[roadmap intake #2960](https://github.com/FS-GG/.github/issues/2960),
[analyst directive #2695](https://github.com/FS-GG/.github/issues/2695) and
[orchestrator room #3228](https://github.com/FS-GG/.github/issues/3228).
Read their complete promises before action. Link reusable acceptance, explicitly supersede the now
contradictory mandatory process directions under the user's present instruction, and preserve any
independent still-valid remainder. Superseded/cancelled machinery is not retrospectively completed work.
Do not mass-close these issues, silently absorb a broader promise, or let them recreate four process
outcomes. Root owns the evidence-based native disposition and links it to this outcome.

A complete bounded read of the 96 open `.github` issues also identifies current process scope in
[#2874](https://github.com/FS-GG/.github/issues/2874),
[#2843](https://github.com/FS-GG/.github/issues/2843),
[#2844](https://github.com/FS-GG/.github/issues/2844),
[#2850](https://github.com/FS-GG/.github/issues/2850),
[#2849](https://github.com/FS-GG/.github/issues/2849),
[#2864](https://github.com/FS-GG/.github/issues/2864),
[#2852](https://github.com/FS-GG/.github/issues/2852),
[#2903](https://github.com/FS-GG/.github/issues/2903) and
[#3070](https://github.com/FS-GG/.github/issues/3070). Reuse these under this Outcome rather than
create another process epic. Preserve evidence deduplication, actual guard reachability, unclaimed-PR
collision coverage and producer-to-runtime identity acceptance. Supersede mandatory critic pools,
architecture packets and hand-authored protocol upkeep only at their changed process scope;
retained API defects require their own evidence or an explicit consumer retirement verdict.

The old finding/draft indexes #2691/#2703 and orchestration room #3227 independently repeat standing
instructions. Reconcile those directions explicitly while preserving original pending findings,
custody and Unknown holders; source retirement cannot establish another actor's termination.
The larger GS2/fleet renovation, automatic publisher #2106 and independently useful retained API
contracts are not silently completed by this selected lifecycle redesign. Their original acceptance
stays with its owner unless the actual accepted consumer retirement removes that requirement.


## Ownership, useful parallel work and delivery

Each selected Delivery has one accountable owner for its whole accepted boundary. Review independence
means a separate judgement of the exact source, not a mandatory fresh implementation worker after each
failure. Keep the same owner/PR during ordinary repair. After recurring same-class failures, inspect
shared assumptions/callers and make one coherent repair; do not make an arbitrary attempt count an
automatic park or replacement trigger. Scope or effect-authority changes still require their actual
reconciliation. Inert prose retains proportionate owner validation; governing skill/code/schema changes
retain independent exact-source review and the repository's required checks.

Before delegation compare expected useful contribution with coordination cost, actual runtime permission,
owner/PR/touch-set conflicts, output/cache/state overlap and remaining model, CPU, memory, storage,
integrator and hosted-CI resources. Preserve relevant known and Unknown reservations. Use isolated
worktrees and one integrator for shared files. Admit expensive execution serially until the existing
capacity/profile evidence supports overlap. Owners may prepare disjoint source while a shared hosted
queue drains. If delegation is unavailable, continue eligible work serially and name the limiting
condition. A broad declaration should be narrowed only to what the actual work touches; never claim
independence by omitting a shared output or prerequisite. Do not create reporting-only workers.

Use existing PR admission and `tools/routine-delivery.py` for campaign/chain identity, current head,
required checks, coherent validation/reuse and native merge readback. Preserve tested-versus-delivered
source correspondence and late-failure fences from ADR-0084. A pnext owner reports accepted source,
remaining publication/installation/native gates and the actual next owner/action. If its selected
Delivery includes those gates, it continues them while authorized and actionable; it does not return
“done” at merge. If a distinct release owner is necessary, transfer the precise pending acceptance and
original operation identity. Root continues its loop after either return; the executor never independently
selects another board item.

## Stop, drain, resume and knowledge

Retain the recent stop/continue design. A stop immediately prevents new selection, source repair pushes,
publication and admissions. Interrupt active owned workers; preserve partial files and identify actual
process/job identities. Perform only necessary safe retirement of already-running owned operations and
an authorized recovery save. Cancellation requested is not observed termination. Unknown external jobs,
partial publication and unresolved reservations remain visible; do not kill broad process-name matches
or stale PIDs. An ordinary hosted wait is not a stop request.

If native issue/PR evidence plus Atlas pointers already preserve recovery, return those pointers without
a report or save PR. Save only real gaps such as dirty worktree locations, original operation custody,
pending reply identities/deadlines, cleanup uncertainty or absent scope constraints. Keep private evidence
private; an access path does not establish durable backup. Resume from the original owner/PR/attempt
and current determining facts, not a copied snapshot or the newest timestamped report. Preserve accepted
scope and renew only separately allowed observation, never original execution grants.

All 439 inherited holds and 46 later historical originals remain distinctly Unknown/unreleased. The old
search is closed unavailable. No repeated search, reconstruction, new-identity retry or global release
is part of this redesign. A relevant unresolved interference blocks its operation; unrelated history
is not a global source-development gate. Capture reusable design/delivery findings in Atlas with
explicit supersession/conflict; Root owns the capture gap until source delivery provides immutable
citations. Atlas contains compact knowledge, not this census, logs or custody payloads.

## Retirement and distribution closure

Retirement removes contradictory **active behavior**, not every historical name or evidence type.
The change covers both declared skill roots, actual runtime route bindings, generated catalog metadata,
validation fixtures and installed consumers.

Delete the six dormant organization renderers in `scripts/generate-projections`: filing-rules,
reconcile-rules, wave-policy, blocker-states, snapshot-shape and board-statuses. None has an active
target or executable caller. Keep the separate retired-region rejection/removal algorithm, which
prevents resurrection and protects surrounding content. Retained pure exported packet/churn validators
remain compatibility APIs, not required actors or a reason to build the obsolete proposed churn CLI.
The current V2 caller uses neither the legacy wave scheduler nor its claim writers. Any later removal
of those exported product command contracts needs an explicit compatibility disposition; old tests
alone establish no installed consumer, and original readback/custody must remain recoverable.

| Surface | Final role and required change |
|---|---|
| work-board, normal/best | One canonical clearing/intake/recovery loop; wrappers select only explicit scope/model routing and supported effort. Preserve runtime delegation limits and fresh actual caller selection. |
| pnext-item | Canonical single-Delivery executor. Own repair/review/source delivery and remaining selected acceptance; no source-merge stop for a broader promise. Use shared work-board delivery rules rather than a copied scheduler. |
| p-add / padd-item | Thin organization/configured-product intake routes into the same reference. Remove organization V1 commands. Product V2 filing remains pending where its qualified writer is absent; preserve explicitly qualified legacy-product behavior in a fenced conditional reference. |
| work-roadmap | Retire its independent executor prose; retain a compatibility adapter that supplies the selected owning-plan slice to pnext-item and returns to work-board. Preserve technical performance/diagnostic/heavyweight references and active packaged helper paths. |
| drive-board and routed variants; work-programme | Retain minimal scope/routing redirects and required helper loaders. Remove duplicated host-loop prescriptions. No roadmap-checkbox scheduling or fixed waves. |
| check-board / cross-repo-coordination | Keep scoped native inspection and genuine cross-repo ownership/dependency work. Correct stale organization adoption claims from accepted source/native evidence; preserve product contract distinctions and qualified product compatibility. |
| board-analyst / lane-steward | Retire mandatory separate actor, packet, churn emission and V1 write mechanisms. Keep callable compatibility entries for optional dedupe/evidence or touch-set analysis, returning concrete findings to the current owner without new scheduling authority. Historical worked evidence and schema consumers remain. |
| intra-repo-parallel-work | Keep isolation, ownership and overlap coordination; remove unconditional routine identity/claim/heartbeat/Done ceremony. Explicit heavyweight/product compatibility stays conditional and cannot restore V1 organization writes. |
| routine-decisions and context helpers | Retain optional diagnostics and existing wire/helper compatibility. No mandatory reducer view, packet, return or new store is introduced. Delete only proven unreachable implementation in the same caller-qualified change. |
| stop / continue / agent-handoff | Keep gap-only saving, actual drain/custody and original ownership recovery. Align canonical links and remove stale pilot wording that contradicts event/deadline observation. |

Exact caller/projection scope to reconcile in the implementation candidate:

- `.agents/skills/` and `.claude/skills/` matching bodies/references and `agents/openai.yaml`; declared
  `.agent-skill-roots`; `.claude/agents/fsgg-worker-{normal,best}.md`, `fsgg-critic-{normal,best}.md`
  and `fsgg-analyst-{normal,best}.md`. Current worker bindings still prescribe minted identities,
  claims and fresh repair workers even when a routine pnext invocation does not.
- `scripts/check-skill-quality.py`, `tests/skill-quality/{run.sh,validated-intake-filing.py,analyst-dispatch.py,
  routine-board-route.py,routine-development-route.py,work-board-migration.py,forward-triggers.json}`,
  `tests/skill-registry/catalog-triggers.json`, relevant progressive-disclosure/report callers and
  their workflow path filters. Replace old recipe assertions with behavioral/scope negative controls.
- `scripts/generate-driver-manifest`, `scripts/prepare-registry`, `scripts/generate-projections`,
  `registry/{driver-skill-manifest.json,coordination-kit-skill-manifest.json,skills.yml,repos.yml}` and their
  generated lock/digest/changelog projections. Verify actual generated filenames/ownership before
  editing; generator output is not hand-authored. The inspection reference is generated from its
  check-board owner into work-board; do not fork those copies.
- `src/FS.GG.Drivers/stage-drivers.py`, the current coherent `release-successor-candidate.yml`
  package qualification, the kit materialization/release checks, `scripts/NewSddWorkspace/ProductBoard.fs` and
  its fixed materialization/preimage closure, plus the preserved-update receiver tests. These actual
  consumers can reject a catalog-correct but incomplete reference tree.
  Historical `release-drivers.yml` is sealed and byte-bound; retain it unchanged. Current manifest/link
  staging and the compiled Creator fixture prove the new reference closure through active consumers.
- Existing `tests/work-programme` and `fsgg.programme.*`/`programme.fsx` compatibility, native
  `Roadmap`, `roadmap-telemetry`, strict lifecycle/review/feedback types and historical churn fixtures
  retain their current consumer contracts. No blind rename or corpus deletion is justified.

The primary driver/operator catalog and kit have different delivery paths. work-board, work-roadmap,
stop/continue, Atlas and related drivers materialize downstream; p-add, drive-board, analyst and steward
are operator-only. pnext-item, check-board, cross-repo and intra-repo skills are kit members.
Consequently, source edits may require both Drivers and Kit/coherent publication. A catalogue change
alone does not update a scaffold or an installed skill, and deleting a skill file without its manifest,
receiver and caller disposition is not retirement.

## Implementation milestones

These milestones are the single outcome's owning plan, not a second status ledger. Each is routine
unless an explicit human instruction names heavyweight scope. Detail the first two now; expand later
windows with their actual observed result. Do not preselect release numbers or repeat original operations.

### BOARD-LIFECYCLE-01.1 — Coherent active instructions and caller closure

Depends on this design and Root's deduplicated owning Outcome. One Sol-medium implementation owner;
Root integrates. Scope: canonical loop/intake reference, pnext executor, adapters/auxiliary routes,
both skill roots, route metadata, Claude bindings, validators/forward controls and generated manifests.

First executable window:

1. Put intake, fair selection, return-to-loop and explicit completion/quiescence in work-board and its
   existing references. Route p-add/padd-item and selected-plan execution to canonical intake/pnext.
2. Remove active organization V1 and mandatory analyst/claim/packet mechanisms from all listed callers;
   preserve qualified product and explicit heavyweight boundaries. Correct stale prepared/current
   statements without hard-coding one global product/organization binding into every skill.
3. Reconcile actual semantic validators, trigger metadata and mirrors/generators in the same candidate.
   Exercise current runtime routes end to end as instruction selection, including Claude bindings.
   Do not claim new issue writes or installed adoption from this source change.
4. Review the final coherent source independently and use normal exact-head required CI/delivery.

Acceptance: every public entry routes to the same selected scope and lifecycle; a source merge with
remaining consumer acceptance continues; a missing-Status card reaches intake; a runtime without
parallel agents executes serially; no routine path asks for claims, packets or repeated approval.
Old names resolve usefully, and actual compatibility fixtures still pass.

### BOARD-LIFECYCLE-01.2 — Qualify bounded native changes and complete expanded reads

Depends on the concrete transition cases required by .1 and the fresh native decomposition plan.
Scope: existing `V2Migration`/transport/CLI writer, `V2ProjectionSource.inspectFixed`, maintained
result decoding and focused `tests/coord-board-v2-projection` / `tests/coord-board-v2-cli-join` cases.
Add writer behavior only if no already qualified route supplies the exact existing-value transition.
Use the full-cohort batched acquisition decision above for expanded population acceptance; do not
create another general transaction framework or selective inspection mode.

Prepare explicit unchanged/absent/existing/conflicting planning cases, state/closure partial-effect
readback and exact native hierarchy joins. Keep normal inspection read-only and existing absent-only
semantics intact. Test final-barrier drift, unrelated preservation, lost-response settlement without
resend, original expiry and unsupported-field refusal through the actual caller. Determine whether a
contract version extension is required from the changed closed wire grammar; do not reuse a version
with incompatible required input. Publish/installed qualification remains .3, not a source-test claim.
Native issue create/link/close uses the authorized existing API path with its own exact readback; it is
not accidentally bundled into a field initialization command. Implement and measure phase-bounded
batched acquisition over complete real-sized fixtures, preserving final barriers and all previously
enrolled members. The final selected expanded native cohort is an installed acceptance gate in .3/.4.

Acceptance: one evidenced authorized existing field transition succeeds, a concurrent conflicting
owner change refuses or exposes a conflict, and unsupported/ambiguous effects cannot become an
automatic retry. No legacy queue, field clearing, automatic Ready or parent rollup is used. The actual reader handles
the selected expanded cohort within prospective bounds without lost membership/hierarchy or weakened
selection. Product/organization and migration custody outputs are decoded through maintained paths.

### BOARD-LIFECYCLE-01.3 — Publish and qualify the actual selected receivers

Depends on delivered .1 and any necessary .2. Use existing coherent release and custody mechanisms,
with the single accountable release owner. Refresh actual current producer and dependency identities;
CLI108/Creator019 are the accepted baseline, not predetermined successor versions.

Publish byte-identical required artifacts through the ordinary admitted feeds. Qualify an actual isolated
installed operator/CLI, complete driver+kit reference closure, discovery and canonical entrypoint behavior.
If the changed driver tree alters Creator's fixed materialization/preimage allowlist, deliver its bounded
source successor and publish it through its existing route before claiming fresh/retained creator impact.
Use the smallest supported current Rendering and selected Game receiver families that exercise the
changed contract; extend the family set only for real changed behavior. Product binding3 remains its
qualified schema unless a separate successor is actually delivered and selected.

Acceptance: fresh public-only creation discovers the right skills/links and calls the actual installed
route; preserving existing-workspace update keeps user files/provenance and refuses foreign scope or
unrecognized preimages. Operator-only skills remain absent from product payloads. Existing workspaces
are upgraded only through their selected preserving route. Defaults do not change except those explicitly
selected by this implementation; no implicit lifecycle/binding/fleet activation follows.

The Creator020 publisher source slice belongs to Delivery [#4712](https://github.com/FS-GG/.github/issues/4712);
its source PR must retain that Delivery's full consumer promise. The closed current020 candidate uses the
unchanged genuine promoted108 dependency pin, while ordinary source020 qualification uses coherent109.
Fresh release qualification and source-only evidence have distinct schemas; the latter remains refused
by the artifact reader and publisher even if its eligibility booleans are flipped. Publication still
requires its own actual first-attempt candidate, independent review, held current-main source, fresh
collision/protection observations and protected admission. The prospective creator020 journal does not
reset consumed019 or any original effect. Published metadata and installed pins remain unchanged.

Pipeline preflight selection: reuse the existing static workflow/profile checks, bad-input CLI and
artifact controls, historical seal inversions, and actual eight-effect/final-generation fake-port route.
The assessment is capped at 30 minutes and adds no custom model: ordering, retry, cancellation, journal
state transitions and resource limits are unchanged. These maintained controls are reusable on future
closed profile advances. Pin and malformed-profile refusal precede graph acquisition/compilation;
source-only, wrong-graph/run/source and historical-binding controls precede Authority or publication
effects. Preserve the exact019 attestation, reverse the current job and complete workflow to accepted019
bytes, and retain013 recovery and all older proofs. Local source-only controls do not establish compiled
qualification, CI success, live publication or measured savings. The single costly CLR slot and required
CI remain separately admitted; qualify the genuine staged108 producer and current020 compiled caller,
then preserve the same packed output and original three-file Actions archive for native acceptance.

### BOARD-LIFECYCLE-01.4 — Materialize current Outcome decomposition and reconcile intake

Depends on .1 and actual availability/qualification of each necessary native effect; .3 gates any new
compiled writer use. Ordinary authorized issue drafting/linking can proceed earlier without pretending
pending membership or enrollment is complete.

Root reconciles every row of the 15-Outcome map, reuses current children/PRs, records the full remaining
acceptance, creates only useful near-term Deliveries, supplies explicit owners/prerequisites and performs
selected classification/enrollment via the protected route. Reconcile all four open Deliveries and the
three unenrolled census members. Resolve old process issue directives by scoped supersession/verdict.
Refresh the entire final membership and hierarchy after changes, preserving all unrelated planning/views.

Acceptance: every broad Outcome has a reviewed decomposition and useful next native Delivery or an
explicit reason no child is yet meaningful; each materialized delivery has complete six-field scope and
real dependencies; the full parent promise survives; existing PR462 and completed source children are
reused; no Outcome is dispatched; no unenrolled child is accidentally schedulable. The redesign Outcome
owns all process work, with no new forest of parallel process epics.

### BOARD-LIFECYCLE-01.5 — Demonstrate continuous clearing, stop/resume and finish retirement

Depends on the delivered active routes, selected installed receiver and actual decomposition. Use the
already necessary work of this outcome for real acceptance; do not manufacture demonstration PRs,
mandatory measurement campaigns or product runtime tasks. Exercise at least the real next selection
after a delivery and a hosted wait opportunity. If no useful parallel work is admitted, retain the actual
serial constraint and continuation instead of fabricating overlap.

Verify one available ordinary recovery boundary against the same owner/PR/remaining acceptance, plus
focused adversarial stop/unknown controls. A real user stop is never manufactured merely for a test;
controlled fixtures establish only their tested scope. Inspect each remaining obsolete active call site,
retire the superseded instruction/mechanism and retain required compatibility. Capture concise Atlas
knowledge and immutable delivery pointers. Close this Outcome only when its source, applicable publication/
installed behavior, native decomposition and observed continuous-loop acceptance are complete. Any
unavailable installed or native assertion remains a named remainder with an owner, not a source-only
completion. Economic benefit remains unclaimed without complete evidence.

## Acceptance and adversarial forward checks

Use existing production validators, transport/CLI controls, package/receiver checks and real necessary
native operations. The implementation must call changed assertions through the maintained runner;
source text matching or an uncalled fixture cannot prove behavior. Preserve the first failing result
and require a causal negative control to fail for the intended boundary.

| Scenario | Required observable result |
|---|---|
| Add a new request already covered by an open issue/PR | Reuse its identity; preserve complete acceptance, owner and PR; no duplicate create/enrollment or mandatory analyst. |
| Add a legitimate new capability with no current failing test | Evidence-backed native intake or concrete pending draft; no “red today” rejection of user intent. |
| Card has no Status, no Work level or no enrollment | It remains in All-items intake with owner/action/reopening event. Missing metadata never hides it, and repair does not authorize execution. |
| Broad parent has one closed source child and missing installed/recovery promises | Child remains completed; parent stays open with meaningful residual Deliveries. Healthy Observation and all children closed do not roll it up. |
| Native children/dependencies are truncated, cyclic, changed or unreadable | Affected joins stay Unknown; independent qualified action facts remain usable; no empty-list fallback. |
| Expanded enrollment and nested pagination approach the reader ceiling | Complete bounded batching preserves every original member and final barrier; refusal stays Unknown, never a partial-success census or silent cohort shrink. |
| Product/organization or text/select/migration custody output differs | Maintained decoder accepts the actual schema, rejects wrong unions and reconciles any original post-send ambiguity without resend. |
| Existing human field changes during preparation | Refuse stale transition or expose actual post-write conflict; preserve unrelated values. No clear-and-reinitialize workaround. |
| Issue creation succeeds but linking/enrollment times out | Preserve original issue/effect; independently settle remaining work. No replacement issue or duplicate send. |
| Source merge completes during broad clearing | Accept only its declared slice, revisit suppliers/intake/opportunities, continue the next eligible step. |
| Existing owner has a failed check | Same owner and PR repair under current bounds, exact head re-reviewed; no automatic new claim, worker, branch or attempt-budget reset. |
| One hosted run waits; disjoint source work exists | Assess actual runtime/resources/output overlap and continue useful admitted work; fallback serially when delegation is unavailable. |
| An older eligible delivery is repeatedly bypassed by new easy fixes | Explain the real priority/dependency reason, reconsider its path to completion and prevent indefinite unexplained starvation. No new priority-score store. |
| Unknown historical operation overlaps one resource domain | Preserve its hold and fence that action; unrelated source work proceeds. No reconstruction or global release. |
| Stop while files are dirty and an external effect is ambiguous | New admissions stop; original files/custody survive; retirement is observed or Unknown. Save only missing recovery data. |
| Resume without a new timestamped handoff | Current issue/PR and Atlas pointers suffice when complete; recover same identity and refresh admission. Missing optional report is not a blocker. |
| A renamed/retained old skill or Claude route is invoked | It reaches the canonical route or explicitly fenced compatibility behavior; no hidden V1 claim/intake, mandatory packet or fixed-wave scheduler. |
| Source and registry are coherent but installed payload is old | Report source-only delivery and pending receiver adoption; refuse new compiled behavior on old caller bytes. |
| All eligible work is externally blocked and intake is complete | Report quiescence with owners and reopening conditions; do not call the board complete or imply background monitoring. |

Observe material changes through existing native evidence and best-effort telemetry. Preserve original
item/attempt lineage and attribution gaps. Accepted outcomes, first failures, waiting and active owners,
blocker ages and useful checkpoints are enough for progress; PR counts are not outcome percentages,
and elapsed time is not tokens, spend or avoidable delay.

## Decisions and remaining evidence gates

The architectural decisions are to keep one coordinator, make pnext the single-item executor, share
one intake path, preserve complete Outcome acceptance, make useful bounded parallel work an explicit
reconsideration step, and retire active contradictions through their entire caller/packaging closure.
These decisions implement the user's stated direction; ordinary implementation needs no repeated
permission round.

The exact successor package versions, concrete native child identities and necessary existing-value
writer contract version follow current evidence during implementation. Missing external receiver/runtime
supplies in the Outcome map are real scoped dependencies, not new permissions requested by this design.
A wider removal of published helper schemas or independently qualified legacy-product behavior needs
actual consumer migration evidence; until then retain the compatibility surface while retiring the old
organization workflow. Root captures the accepted design and delivery in Atlas and keeps #4701 as the durable
native owning-issue navigation.
