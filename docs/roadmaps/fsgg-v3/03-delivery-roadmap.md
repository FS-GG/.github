# Specairn (FS.GG v3): delivery roadmap

Status: future proposal, 2026-10-10. This roadmap implements the [clean rewrite design](../2026-10-10-fsgg-v3-clean-rewrite.md) only after completed v2. It creates no active work assignments, board items, publication authority or cutover approval.

## Programme shape

Build one integrated replacement, **Specairn**, in the public [FS-GG/specairn monorepo](https://github.com/FS-GG/specairn). The repository was created on 2026-10-10 to reserve the destination; implementation remains deferred until v2 completion. Keep v3 main usable in an isolated environment from the first vertical slice onward. Parallel work converges there continuously; it does not replace parts of production v2. Production adoption happens once, after the complete accepted scope is qualified.

Use completed v2 typed-SDD to develop the replacement. Each implementation change carries the minimum accepted semantic delta and focused evidence. A PR is the delivery object; implementation tools derive status and proof summaries. Do not generate a document bundle or a separate phase ledger for every task. Milestone evidence is a query over test and native delivery results, with a short human assessment only where judgment is needed.

There is no credible calendar estimate before the final v2 capability inventory, available contributor capacity and runtime experiment. Size work after M0 in reviewable slices. A useful planning unit is a complete scenario or a bounded module contract, not a repository-wide ownership claim. Calendar commitments should be made from observed throughput and rework after M1, without multiplying expected throughput by agent count.

## Milestones and exit conditions

| Milestone | Deliverable | Exit condition | Dependency |
|---|---|---|---|
| M0 — Completed-v2 baseline and bounded scope | Published/installed v2 baseline, capability dispositions, benchmark journeys, pinned typed-SDD bootstrap and supported environment matrix | All required capabilities have a disposition and testable outcome; actual v2 completion is established; ordinary development path works with real acceptance semantics | Current v2 programme complete |
| M1 — Architecture and walking skeleton | Monorepo, enforced module graph, accepted core model, one full isolated change journey, durability comparison | Fresh workspace → intent → runner → verification → PR → native result works; selected runtime passes initial crash/unknown-effect cases | M0 |
| M2 — Parallel implementation | Runtime, specifications, GitHub, packs, runner and delivery tooling against shared contracts | Every in-scope capability implemented or deliberately retired; module/contract checks pass; integrated scenarios run continuously | M1 contracts and runtime decision |
| M3 — Whole-system completeness | All supported packs/providers, installed tooling, product adoption and self-hosting qualification | Complete scenario suite passes from installed artifacts; no “source implemented, consumer untested” scope remains | Required M2 slices |
| M4 — Release and cutover rehearsal | Immutable release candidate, recovery/failure suite, restore rehearsal, cutover runbook and bounded observation plan | No unresolved correctness/authority defect; all required environment combinations pass; a full isolated cutover rehearsal succeeds | M3 |
| M5 — Single production cutover | One switch from settled v2 to fresh v3 authority and tooling | v2 cannot dispatch; v3 owns new work; native checks confirm critical journeys; no shared active work or dual writer | M4 and explicit future cutover authority |
| M6 — Completion | Bounded operational acceptance and archived v2 operational surfaces | Required observation scenarios complete; serious defects resolved; v3 accepted as the framework; historical v2 evidence remains readable | M5 |

M6 is verification of the completed switch, not another rollout wave. A failure in M3 or M4 returns the relevant implementation to repair; it does not create a partially adopted production state.

## M0: establish what is being replaced

V2 completion means its agreed programme is finished and its required tools/materials are published and usable by the intended consumers. A closed issue or merged source PR alone is insufficient. Record immutable references to that completed release and its existing acceptance evidence; do not reopen its entire history for another retrospective qualification programme.

Create a finite capability matrix covering workspace creation, supported product kinds, typed lifecycle, agent providers, scheduling, recovery, GitHub/Projects, verification, packaging, publication, diagnostics and product use. For each row record the required user outcome, environment, v3 owner and retain/redesign/retire disposition. Retiring supported behavior requires an explicit product decision; replacing its interface does not.

Measure representative v2 journeys: new workspace; ordinary feature; prose edit; new pack; two independent changes; conflicting changes; stalled execution; publication failure. Record commands, manual facts entered, human interventions, elapsed/active/queue time, tests run and outcome. Reuse these outcomes as v3 scenarios without requiring byte-identical files or API compatibility.

Pin v2 typed-SDD and FsQuint/toolchain dependencies. Exercise a small real change through the bootstrap lifecycle. Resolve how exact-base proposals and actual acceptance map to normal PR work. Required fields are generated or linked automatically when possible; a required human act is captured honestly. This is the only point at which a discovered bootstrap contract mismatch should force a process-design correction.

Define the benchmark machine, provider limits, supported operating systems and expected concurrent workload. Proposed feedback targets in the design become a recorded, measurable budget. Do not promise timings on unspecified hardware or unavailable external services.

## M1: make the hardest assumptions testable

The first slice creates a minimal supported workspace, accepts a typed change, compiles a plan, starts one runner task, collects a candidate, runs verification, creates a PR in a sandbox repository and observes the native result. It includes a deliberately interrupted operation and a read-only inspection. No production v2 authority is involved.

Publish initial signatures for domain identities, plan nodes, operation outcomes, journal append/recovery, execution assignments, artifact custody, GitHub observations and pack capabilities. These signatures unblock parallel work; they are not a frozen public v2-compatible API. Changes across modules are one atomic v3 change when required.

Run a bounded runtime comparison using the same small plan and fault scenarios for Akka/PostgreSQL and Temporal/.NET. Compare implementation surface, recovery correctness, inspectability, deployment burden and upgrade behavior. The default is Akka/PostgreSQL; retaining it requires demonstrating that the durable kernel stays small and comprehensible. If Temporal removes material custom failure-handling without unacceptable operational cost, select it here and update the design in the same change. Do not maintain both engines as product features.

The early invariants include durable-before-dispatch, command deduplication, stale-owner rejection, unknown-effect handling and cancellation settlement. Use typed-SDD model checks plus real PostgreSQL/provider-stub tests. A passing model with an untested adapter is not an exit condition.

## Parallel implementation workstreams

After the M1 boundary contracts, the following streams can progress independently. This is future programme structure, not a request to spawn workers during documentation preparation.

| Stream | Scope and suggested work IDs | Independent output | Integration dependency |
|---|---|---|---|
| A — Semantics and planning | V3-A1 WorkspaceModel/change API; A2 impact selection; A3 pack resolution; A4 finite plan compiler | Pure model/compiler fixtures, diagnostics, deterministic plans | M1 identities; pack manifest contract with D |
| B — Durable runtime | V3-B1 transactional journal; B2 scheduling/fairness; B3 reconciliation/cancellation; B4 restore/retention | Crash-tested engine with fake provider and artifact ports | Runtime choice; execution protocol with E |
| C — GitHub infrastructure | V3-C1 native facts/webhooks; C2 PR/check/merge effects; C3 Projects ownership/projection; C4 repository bootstrap | Adapter contracts and sandbox integration scenarios | Operation identity and effect classification from B |
| D — Product packs | V3-D1 canonical pack format/materialization; D2 required product packs; D3 new-kind fixture; D4 agent guidance views | Fresh workspace and conformance matrix | A's descriptor/resolver contract; released product libraries |
| E — Execution and isolation | V3-E1 runner protocol; E2 provider adapters; E3 isolation/resource profiles; E4 candidate custody | Real provider qualification and containment tests | B's assignment/fencing semantics; artifact port |
| F — Experience and delivery | V3-F1 CLI/API/run views; F2 impact CI; F3 candidate build/publish; F4 installed bootstrap | User journeys, build graph, immutable artifacts and installation fixtures | M1 application API; integration with all streams |

Within D, independent packs can run in parallel once the manifest is stable enough. Within E, providers can run in parallel against one conformance harness. Within C, read-only fact collection can proceed independently of privileged delivery. Within F, UI projections and build selection do not need to wait for every provider implementation.

Keep shared contract edits narrow and owned by one integrator for that change; other work uses the agreed signature or a fixture. Use isolated worktrees and optimistic rebasing. Avoid simultaneous speculative edits to the same public contract. A discovered dependency changes the plan; it does not require a new cross-repository request chain.

The likely critical path is M0 → M1 contracts/runtime choice → durable execution plus GitHub effects → installed whole-system qualification → rehearsal → cutover. Pack and UI work should run beside that path, not become prerequisites for testing persistence. Integrate each completed scenario promptly so late discovery does not accumulate until M3.

## Acceptance scenarios

These scenarios are product outcomes, not implementation-mirroring unit tests. M0 adds concrete cases for every retained completed-v2 capability.

| ID | Scenario | Required observation |
|---|---|---|
| Q01 | Fresh installation outside the source checkout | Tool/pack digests match candidate; no source-tree-only assets or implicit tool dependencies |
| Q02 | Create every supported workspace kind | Correct tree, resolved tools, successful relevant build/test, generated guidance from canonical source |
| Q03 | Ordinary typed feature | Exact-base accepted intent, useful candidate, affected verification, one PR and native delivery readback |
| Q04 | Prose-only change | Honest no-semantic-change disposition; focused validation; no unnecessary full formal run or manual receipt |
| Q05 | Two independent changes | Both execute concurrently; no global repository lock; correct integration results |
| Q06 | Conflicting source or semantic changes | Conflict/stale base detected; replan/rebase reruns affected obligations; neither intent silently lost |
| Q07 | Add a new product kind with existing capabilities | Pack and fixture changes only; no scheduler/wizard product switch added |
| Q08 | Crash before/after command commit and before acknowledgement | Recovery distinguishes absent, committed and duplicate command without duplicate intent |
| Q09 | Provider launch or GitHub write times out after effect | Native/provider reconciliation finds result or reports unknown; no unsafe blind repeat |
| Q10 | Runner loses heartbeat, returns late, or receives cancellation | Original deadline retained; stale generation cannot advance run; resource not reused while still active |
| Q11 | Duplicate, missing and reordered webhooks; partial board response | Inbox deduplication and reconciliation converge; incomplete observation is never an empty-work conclusion |
| Q12 | Human Project hold conflicts with machine projection | Hold remains intact; field ownership is respected; already-authorized work is handled according to explicit policy |
| Q13 | Read-only installed inspection | Zero mutations, launches or admissions; complete/incomplete observations distinguished |
| Q14 | Corrupt candidate, missing artifact or unknown event schema | Clear refusal before consumption/dispatch; no success inferred from metadata alone |
| Q15 | Stale runner or competing effect owner | State update rejected; external in-flight call handled without claiming database fencing cancels it |
| Q16 | Agent/build code attempts forbidden filesystem, network or credential access | Declared isolation enforced on supported environments; privileged publication credentials unavailable |
| Q17 | One publication feed fails | Same candidate bytes retained; stable manifest unpromoted; only safe missing work retried |
| Q18 | Restore database while external state has advanced | Reconciliation precedes new dispatch; unresolved effects remain owned and visible |
| Q19 | Metrics/logging backend unavailable | Durable run inspection and correctness continue; missing telemetry is explicit |
| Q20 | Required-check classifier or shared input changes | Affected obligations selected conservatively; final required context reflects actual candidate/merge group |
| Q21 | Self-hosting in isolated resources | Installed v3 completes a real v3 change while independent bootstrap checks validate the result |
| Q22 | Full cutover and fallback rehearsal | No overlapping writer authority, no v2 state import, explicit handling of any v3 effects before fallback |

For deterministic cases, compare exact outcomes and invariant traces. For real agent tasks, evaluate the resulting change and obligations rather than requiring identical tool sequences. Record samples and variability; a single successful agent run does not establish reliability. Failure tests use controlled fault injection, not deliberate disruption of production repositories or providers.

## M3 and M4: qualify the complete replacement

M3 closes the capability matrix against installed artifacts. Required pack/provider/environment combinations have fresh evidence tied to the candidate. Unsupported combinations are explicit; they cannot be silently dropped to make the matrix green. Product source adoption is rehearsed in fresh v3 workspaces, with newly accepted semantics and without importing v2 runtime/configuration state.

M4 builds an immutable release candidate and validates all payloads before any publication. Candidate artifacts can be distributed to isolated qualification environments; this is not production adoption. Confirm package metadata, cold install, dependency resolution, permissions, diagnostics, artifact retention and backup/restore. Rehearse the entire cutover against disposable repositories and databases, including partial failure.

Produce one concise readiness view from these results: scope coverage, unresolved defects, selected versions, failure-test outcomes, usability/feedback measurements and the exact candidate identity. Do not create an independently maintained receipt hierarchy. A known correctness, custody, authority or required-capability defect blocks cutover. Cosmetic defects can be accepted explicitly without concealing their impact.

## Single production cutover

This is a proposed sequence to turn into a tested runbook during M4; it is not executable authority today.

1. Confirm the exact qualified v3 candidate, accepted scope, operator and cutover authorization. Preserve immutable v2 tools, source references and backups for historical inspection.
2. Stop new v2 admissions. Drain or cancel active work, then observe settlement of effects and cleanup. Resolve every unknown external effect before continuing; a timeout is not settlement.
3. Fence v2 dispatch and revoke or isolate its effect credentials. Confirm there is no in-flight privileged writer that can later resume. Preserve v2 records read-only.
4. Initialize fresh v3 storage, runtime epoch, resolved packs and native resource bindings. Do not import v2 events, claims, plans, board-derived execution state or old lifecycle manifests.
5. Switch the framework entry points, development tooling and designated planning/project integration once. Remove v2 from active dispatch paths. Verify the selected tool identity and absence of concurrent writers.
6. Run the agreed critical native journeys and admit new work under v3. Observe run correctness, recovery, board ownership and installed product behavior over the finite acceptance window set in M4.
7. Close the transition when the required observations pass and serious defects are resolved. Archive obsolete active guidance; retain historical evidence without treating it as live policy.

No product-by-product rollout, dual-write period or live shadow dispatcher is planned. Old product repositories may retain historical content, but active v3 work uses the new workspace semantics. Merely leaving old documentation available is not backward compatibility.

Before v3 effects begin, a failed cutover can return to the settled v2 configuration after verifying authority. After effects begin, first stop admission and settle or isolate those effects. Then choose fix-forward or a deliberate fresh v2 restart based on current native facts. Do not restore an old database and blindly resume either engine. If an effect remains ambiguous, keep its affected target paused; do not make the ambiguity disappear by changing frameworks.

## Risks, responses and decision ownership

| Risk | Early signal | Response and owner |
|---|---|---|
| Rewrite loses obscure but necessary behavior | Capability without scenario; repeated surprises from v2 | Product owner resolves scope; integrator adds behavioral case before declaring completeness |
| Clean modules reproduce old bureaucracy | Routine journey needs manual pins, receipt forms or several PRs | Experience owner removes the join or automates it; preserve only actual technical/authority checks |
| Custom durable runtime expands uncontrollably | Many operation-specific recovery branches in M1 | Runtime owner completes bounded Temporal comparison and selects one engine before M2 expansion |
| Parallelism increases conflicts instead of throughput | High rebase churn, overlapping contract changes, idle dependents | Integrator narrows work units and serializes the shared contract edit only |
| Monorepo CI becomes the bottleneck | Routine changes fan out to nearly every job | Delivery owner fixes graph/input boundaries; correctness obligations remain explicit |
| Pack extensibility becomes privileged arbitrary execution | Pack code loads into Host or obtains delivery credentials | Execution owner enforces runner capability boundary and tests containment |
| One-go scope grows without limit | New capabilities added faster than inventory closes | Product owner trades scope explicitly; defer optional features past rewrite completion |
| Research assumptions do not fit actual usage | Slow or confusing benchmark journeys despite tidy source | Experience owner changes boundaries using measured user outcomes |

Owners are responsibilities to assign at M0, not new committees or permanent approval roles. A contributor can hold several roles. Runtime and security-sensitive defects get targeted review; routine prose or pack content does not acquire a mandatory multi-reviewer chain.

## Definition of complete

The rewrite is complete when the accepted capability scope is implemented and installed, all required scenarios pass, the single production cutover has occurred, v2 has no active dispatch authority, and the bounded operational acceptance has succeeded. Completion includes a usable developer experience and recovery behavior, not merely a tidy repository and green compilation.

The documentation PR containing this proposal completes only the design task. It does not satisfy any implementation milestone or begin the rewrite before v2 is finished.
