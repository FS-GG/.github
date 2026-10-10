# Specairn (FS.GG v3): delivery roadmap

Status: future proposal, 2026-10-10. This roadmap implements the [clean rewrite design](../2026-10-10-fsgg-v3-clean-rewrite.md) only after completed v2. It creates no active work assignments, board items, publication authority or cutover approval.

## Programme shape

Build one integrated replacement, **Specairn**, in one platform monorepo, with self-hosted Forgejo as the proposed primary forge. The [GitHub reservation](https://github.com/FS-GG/specairn) was created on 2026-10-10; the canonical Forgejo instance, URL and version await M1 qualification. Implementation remains deferred until v2 completion. Keep v3 main usable in an isolated environment from the first vertical slice onward. Parallel work converges there continuously; it does not replace parts of production v2. Production adoption happens once, after the complete accepted scope is qualified.

Use completed v2 typed-SDD to develop the replacement. Each implementation change carries the minimum accepted semantic delta and focused evidence. A PR is the delivery object; implementation tools derive status and proof summaries. Do not generate a document bundle or a separate phase ledger for every task. Milestone evidence is a query over test and native delivery results, with a short human assessment only where judgment is needed.

Complete scenarios are the primary delivery units. Module workstreams provide ownership and expose parallel implementation opportunities within those scenarios; finishing a stream is not a substitute for an integrated outcome. Start with typed plan recipes, built-in packs, a complete fast test suite and a PostgreSQL-backed worker. Expand these mechanisms only when an accepted capability or measured operating cost justifies it.

There is no credible calendar estimate before the final v2 capability inventory, available contributor capacity and runtime experiment. Size work after M0 in reviewable slices. A useful planning unit is a complete scenario or a bounded module contract, not a repository-wide ownership claim. Calendar commitments should be made from observed throughput and rework after M1, without multiplying expected throughput by agent count.

## Milestones and exit conditions

| Milestone | Deliverable | Exit condition | Dependency |
|---|---|---|---|
| M0 — Completed-v2 baseline and bounded scope | Published/installed v2 baseline, capability dispositions, benchmark journeys, pinned typed-SDD bootstrap and supported environment matrix | All required capabilities have a disposition and testable outcome; actual v2 completion is established; ordinary development path works with real acceptance semantics | Current v2 programme complete |
| M1 — Architecture and walking skeleton | Monorepo, enforced module graph, accepted core model, one full isolated change journey, bounded runtime/persistence and Forgejo qualification | Fresh workspace → intent → runner → verification → PR → native result works; smallest viable runtime passes initial crash/unknown-effect cases; protected Forgejo delivery and required feed authentication are proven | M0 |
| M2 — Integrated capability slices | Complete scenarios using runtime, specifications, Forgejo, packs, runner and delivery tooling | Every in-scope capability implemented or deliberately retired; each completed slice passes integrated checks | M1 contracts and runtime decision |
| M3 — Whole-system completeness | Immutable candidate, installed tooling, supported packs/providers, product adoption and self-hosting qualification | Complete scenario suite passes from installed artifacts with explicit combination coverage; no “source implemented, consumer untested” scope remains | Required M2 slices |
| M4 — Release and cutover rehearsal | Same qualified candidate where unchanged, remaining recovery/restore scenarios, cutover runbook and bounded observation plan | No unresolved correctness/authority defect; required coverage is complete; a full isolated cutover rehearsal succeeds | M3 |
| M5 — Single production cutover | One switch from settled v2 to fresh v3 authority and tooling | v2 cannot dispatch; v3 owns new work; native checks confirm critical journeys; no shared active work or dual writer | M4 and explicit future cutover authority |
| M6 — Completion | Bounded operational acceptance and archived v2 operational surfaces | Required observation scenarios complete; serious defects resolved; v3 accepted as the framework; historical v2 evidence remains readable | M5 |

M6 is verification of the completed switch, not another rollout wave. A failure in M3 or M4 returns the relevant implementation to repair; it does not create a partially adopted production state.

## M0: establish what is being replaced

V2 completion means its agreed programme is finished and its required tools/materials are published and usable by the intended consumers. A closed issue or merged source PR alone is insufficient. Record immutable references to that completed release and its existing acceptance evidence; do not reopen its entire history for another retrospective qualification programme.

Create a finite capability matrix covering workspace creation, supported product kinds, typed lifecycle, agent providers, scheduling, recovery, GitHub/Projects, verification, packaging, publication, diagnostics and product use. For each row record the required user outcome, environment, v3 owner and retain/redesign/retire disposition. Retiring supported behavior requires an explicit product decision; replacing its interface does not.

Measure representative v2 journeys: new workspace; ordinary feature; prose edit; new pack; two independent changes; conflicting changes; stalled execution; publication failure. Record commands, manual facts entered, human interventions, elapsed/active/queue time, tests run and outcome. Reuse these outcomes as v3 scenarios without requiring byte-identical files or API compatibility.

Pin v2 typed-SDD and FsQuint/toolchain dependencies. Exercise a small real change through the bootstrap lifecycle. Resolve how exact-base proposals and actual acceptance map to normal PR work. Required fields are generated or linked automatically when possible; a required human act is captured honestly. This is the only point at which a discovered bootstrap contract mismatch should force a process-design correction.

Define the benchmark machine, provider limits, supported operating systems and expected concurrent workload. Proposed feedback targets in the design become a recorded, measurable budget. Do not promise timings on unspecified hardware or unavailable external services.

Include scheduling latency, sustained throughput, database cost and forge requests per completed change. Separate internal progress from native delivery waits. The runtime/database/worker path owns execution; Forgejo supplies collaboration facts and external effects, not per-step coordination or runtime persistence.

Identify actual external contract/pack consumers and required pack composition. Default to built-in packs versioned with the platform and exact dependency references; independent version negotiation is not launch scope without a concrete retained requirement. Define qualification coverage by pack, provider, environment and their coupled interactions. Every supported combination needs a coverage rationale, but independent dimensions do not automatically multiply every scenario into a full Cartesian matrix.

## M1: make the hardest assumptions testable

The first slice creates a minimal supported workspace, accepts a typed change, builds a plan from a typed recipe, starts one runner task, collects a candidate, runs verification, creates a PR in a sandbox repository and observes the native result. It includes a deliberately interrupted operation and a read-only inspection. No production v2 authority is involved.

Publish initial internal signatures for domain identities, recipe steps, operation outcomes, transactional persistence/recovery, execution assignments, artifact custody, forge observations and pack capabilities. These signatures unblock parallel work; they are not a frozen public v2-compatible API or a requirement for independently packaged contracts. Changes across modules are one atomic v3 change when required.

Evaluate a PostgreSQL-backed .NET hosted worker first, using one small plan and the required fault scenarios. Its baseline is transactional current state with immutable operation intent and an atomically appended audit history; in-memory queues are not durable authority. Test full event sourcing only where a concrete reconstruction requirement warrants replay and schema-evolution machinery.

Set a finite experiment budget before implementation. Reuse existing Akka evidence and test Akka/PostgreSQL or Temporal/.NET against a demonstrated gap or credible reduction in maintenance/operating burden. Compare correctness, scheduling latency, sustained throughput, implementation surface, inspectability, deployment and upgrades at the M0 workload. Do not build three substantial engines merely for a comparison. Select the smallest implementation that meets both correctness and performance requirements here and update the design in the same change. Temporal, if selected, replaces custom workflow durability; do not retain competing authorities or multiple runtime engines as product features.

Prove reuse and bounded cost in the walking skeleton: a second operation family uses the same durable lifecycle; increasing audit history does not increase unrelated rows read during ordinary state access or dispatch; independent effects do not acquire a global broker lock. Record query work and latency separately. Exercise evidence reuse after an unrelated change and invalidation after a relevant input change. These are acceptance conditions for the proposed architecture, not additional approval documents.

M1 closes Q27–Q30 for the walking-skeleton capabilities; M2 extends these cases to retained operations and providers. Q07 closes pack extensibility in M2. Each integrated journey captures its required evidence automatically, with no manually assembled receipt ledger.

The early invariants include durable-before-dispatch, command deduplication, stale-owner rejection, unknown-effect handling and cancellation settlement. Use typed-SDD model checks plus real PostgreSQL/provider-stub tests. A passing model with an untested adapter is not an exit condition.

Delay or disable the forge test adapter during an admitted journey. Internal steps whose prerequisites and authority remain valid must continue without polling the forge to advance state. Native-dependent effects wait with an honest reason. Exercise shared observation/coalescing and asynchronous projections, and measure API use so reduced local complexity cannot be purchased through excessive forge traffic.

Qualify a disposable Forgejo instance and one adapter in the walking skeleton. Pin its release and prove authentication scopes, authenticated webhooks, missing/duplicate notifications, pagination, exact PR-head status enforcement, review/merge protection and rejection of stale or absent required evidence. Select a minimal native planning representation with preserved human holds; do not require GitHub Projects schema parity. Measure concurrent PR/check/read traffic at the M0 workload, including overload and recovery. Self-hosting is a hypothesis to measure, not a throughput guarantee.

Resolve required feed authentication early: publish and read back a disposable immutable package through the proposed Forgejo registry and every required external feed. Forgejo OIDC support alone does not prove nuget.org accepts its issuer. Select an accepted identity route or explicit scoped broker credential. Verify uploaded and served archive identities separately where repository signing changes bytes; validate both package contents and signatures. Prove actual credential restrictions, including the pinned Forgejo release’s treatment of job `permissions`. Record an operational owner, operating-cost baseline and backup/upgrade scope. A failed mandatory Forgejo capability returns the design to an explicit decision before M2; it does not silently add a second forge or retain GitHub CI.

Start CI with the complete fast suite, one required result aggregator and an explicit mapping for expensive integration scenarios. Broaden selection for unknown/shared inputs. Measure feedback time before introducing fine-grained graph selection or cache-reuse machinery; these optimizations require their own correctness evidence.

## Parallel implementation workstreams

After the M1 boundary contracts, use the following streams as ownership categories within scenario delivery. They expose disjoint work, not six independent completion queues. This is future programme structure, not a request to spawn workers during documentation preparation.

| Stream | Scope and suggested work IDs | Independent output | Integration dependency |
|---|---|---|---|
| A — Semantics and planning | V3-A1 WorkspaceModel/change API; A2 obligation mapping; A3 exact pack references; A4 typed plan recipes | Pure model/plan-builder fixtures, diagnostics, deterministic plans | M1 identities; pack manifest contract with D |
| B — Durable runtime | V3-B1 transactional operation state/history; B2 scheduling/fairness; B3 reconciliation/cancellation; B4 restore/retention | Crash-tested engine with fake provider and artifact ports | Runtime/persistence choice; execution protocol with E |
| C — Forgejo infrastructure | V3-C1 native facts/webhooks; C2 PR/check/merge effects; C3 planning ownership/projection; C4 repository bootstrap and forge operations | Adapter contracts and sandbox integration scenarios | Operation identity and effect classification from B |
| D — Product packs | V3-D1 built-in pack format/materialization; D2 required product packs; D3 new-kind fixture; D4 agent guidance views | Fresh workspace and factored conformance coverage | A's descriptor/exact-reference contract; released product libraries |
| E — Execution and isolation | V3-E1 runner protocol; E2 provider adapters; E3 isolation/resource profiles; E4 candidate custody | Real provider qualification and containment tests | B's assignment/fencing semantics; artifact port |
| F — Experience and delivery | V3-F1 CLI/API/run views; F2 fast-suite CI and expensive-scenario mapping; F3 candidate build/publish; F4 installed bootstrap | User journeys, measured feedback, immutable artifacts and installation fixtures | M1 application API; integration with all streams |

Within D, independent packs can run in parallel once the manifest is stable enough. Within E, providers can run in parallel against one conformance harness. Within C, read-only fact collection can proceed independently of privileged delivery. Within F, UI projections and build selection do not need to wait for every provider implementation.

Keep shared contract edits narrow and owned by one integrator for that change; other work uses the agreed signature or a fixture. Use isolated worktrees and optimistic rebasing. Avoid simultaneous speculative edits to the same public contract. A discovered dependency changes the plan; it does not require a new cross-repository request chain.

Sequence slices around user outcomes: an ordinary change survives restart; two independent changes integrate safely; a workspace kind is added through pack content; a partial publication recovers using the original bytes. Each slice has one accountable outcome owner and brings its needed streams together. Implement independent portions concurrently, then close the integrated scenario before treating the slice as delivered. Shared abstractions grow from these scenarios, not from speculative stream backlogs.

The likely critical path is M0 → M1 contracts/runtime choice → durable execution plus forge effects → installed whole-system qualification → rehearsal → cutover. Pack and UI work should run beside that path, not become prerequisites for testing persistence. Integrate each completed scenario promptly so late discovery does not accumulate until M3.

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
| Q07 | Add a new product kind with existing capabilities | Pack and fixture changes only; no Domain, Runtime or CLI product switch added; materialization uses staging and no shared global template registration |
| Q08 | Crash before/after command commit and before acknowledgement | Recovery distinguishes absent, committed and duplicate command without duplicate intent |
| Q09 | Provider launch or forge write times out after effect | Native/provider reconciliation finds result or reports unknown; no unsafe blind repeat |
| Q10 | Runner loses heartbeat, returns late, or receives cancellation | Original deadline retained; stale generation cannot advance run; resource not reused while still active |
| Q11 | Duplicate, missing and reordered webhooks; partial board response | Inbox deduplication and reconciliation converge; incomplete observation is never an empty-work conclusion |
| Q12 | Human planning hold conflicts with machine projection | Hold remains intact; field ownership is respected; already-authorized work is handled according to explicit policy |
| Q13 | Read-only installed inspection | Zero mutations, launches or admissions; complete/incomplete observations distinguished |
| Q14 | Corrupt candidate, missing artifact or unknown persisted-record schema | Clear refusal before consumption/dispatch; no success inferred from metadata alone |
| Q15 | Stale runner or competing effect owner | State update rejected; external in-flight call handled without claiming database fencing cancels it |
| Q16 | Agent/build code attempts forbidden filesystem, network or credential access | Declared isolation enforced on supported environments; privileged publication credentials unavailable |
| Q17 | One publication feed fails | Same candidate bytes retained; stable manifest unpromoted; only safe missing work retried |
| Q18 | Restore database while external state has advanced | Reconciliation precedes new dispatch; unresolved effects remain owned and visible |
| Q19 | Metrics/logging backend unavailable | Durable run inspection and correctness continue even with a full export spool; missing telemetry is explicit and diagnostics remain bounded |
| Q20 | Required-check classifier or shared input changes | Affected obligations selected conservatively; final required context reflects actual candidate and selected integration mechanism |
| Q21 | Self-hosting in isolated resources | Installed v3 completes a real v3 change while independent bootstrap checks validate the result |
| Q22 | Full cutover and fallback rehearsal | No overlapping writer authority, no v2 state import, explicit handling of any v3 effects before fallback |
| Q23 | M3 candidate proceeds to M4 unchanged, then receives a targeted repair | Unchanged candidate/evidence reused without rebuilding for a milestone; repair creates a new identity, invalidates affected evidence and refreshes live checks |
| Q24 | Forgejo is slow or unavailable during admitted concurrent work | Eligible internal steps progress without native calls for state transitions; native-dependent effects wait; coalesced projections recover without overwriting human fields or bypassing fresh authority checks |
| Q25 | Protected delivery and publication on the selected Forgejo release | Exact-head checks and review policy enforced; stale/absent evidence rejected; scoped credentials, planning holds and required package-feed authentication work; measured concurrent workload meets M0 budgets |
| Q26 | Restore or upgrade the forge, then rehearse the canonical-host switch | Required repository/metadata/artifact state recoverable; reconciliation precedes dispatch; one canonical writable surface; optional mirror failure cannot create a second authority |
| Q27 | Add a second operation family | Same durable lifecycle handles dispatch, deadlines, observation, settlement and recovery; no separate orchestration script |
| Q28 | Short and long operation histories at the same active workload | Normal state reads and transitions avoid history replay and unrelated rows; indexed pending-work queries stay bounded; latency and query work recorded |
| Q29 | Unrelated source change, then relevant evidence-input change | Unrelated qualification reused; affected evidence invalidated; live authority still re-observed |
| Q30 | Request hard budgets from providers with different usage capabilities | Only enforceable bounds admitted as hard caps; observational/unsupported capability explained; unknown usage never treated as zero |

For deterministic cases, compare exact outcomes and invariant traces. For real agent tasks, evaluate the resulting change and obligations rather than requiring identical tool sequences. Record samples and variability; a single successful agent run does not establish reliability. Failure tests use controlled fault injection, not deliberate disruption of production repositories or providers.

## M3 and M4: qualify the complete replacement

M3 builds an immutable candidate, validates its payloads and closes the capability matrix against installed artifacts. Factor evidence into pack conformance, provider conformance, environment isolation and integrated journeys, with explicit tests for coupled interactions. Each required combination has a documented coverage argument and applicable candidate-bound evidence. Unsupported combinations are explicit; they cannot be silently dropped to make the matrix green. Product source adoption is rehearsed in fresh v3 workspaces, with newly accepted semantics and without importing v2 runtime/configuration state.

M4 takes the same candidate forward where its inputs remain unchanged; a milestone transition does not trigger another build or duplicate installed qualification. Complete any remaining recovery, restore and cutover scenarios. Candidate artifacts can be distributed to isolated qualification environments; this is not production adoption. Include Q26: restore the selected forge and required storage, test the supported upgrade path, then rehearse the canonical repository switch with optional mirrors disconnected. Confirm coverage for package metadata, cold install, dependency resolution, permissions, diagnostics, artifact retention and backup/restore. Rehearse the entire cutover against disposable repositories and databases, including partial failure. All required payload and installation checks precede publication.

A repair creates a new candidate. Invalidate affected evidence and reuse unaffected results only with complete relevant input correspondence; results are never transferred solely because filenames or versions match. Re-observe live authority, remote publication state and cutover conditions at their consequential boundaries. Simpler coverage does not weaken custody, recovery, isolation or native readback.

Produce one concise readiness view from these results: scope coverage, unresolved defects, selected versions, failure-test outcomes, usability/feedback measurements and the exact candidate identity. Do not create an independently maintained receipt hierarchy. A known correctness, custody, authority or required-capability defect blocks cutover. Cosmetic defects can be accepted explicitly without concealing their impact.

## Single production cutover

This is a proposed sequence to turn into a tested runbook during M4; it is not executable authority today.

1. Confirm the exact qualified v3 candidate, accepted scope, operator and cutover authorization. Preserve immutable v2 tools, source references and backups for historical inspection.
2. Stop new v2 admissions. Drain or cancel active work, then observe settlement of effects and cleanup. Resolve every unknown external effect before continuing; a timeout is not settlement.
3. Fence v2 dispatch and revoke or isolate its effect credentials. Confirm there is no in-flight privileged writer that can later resume. Preserve v2 records read-only.
4. Initialize fresh v3 storage, runtime epoch, resolved packs and native resource bindings. Do not import v2 events, claims, plans, board-derived execution state or old lifecycle manifests.
5. Switch the framework entry points, development tooling, canonical forge bindings and designated planning integration once. Preserve historical GitHub links read-only; any outward mirror follows the canonical Forgejo repository and accepts no independent delivery writes. Remove v2 from active dispatch paths. Verify the selected tool identity and absence of concurrent writers.
6. Run the agreed critical native journeys and admit new work under v3. Observe run correctness, recovery, board ownership and installed product behavior over the finite acceptance window set in M4.
7. Close the transition when the required observations pass and serious defects are resolved. Archive obsolete active guidance; retain historical evidence without treating it as live policy.

No product-by-product rollout, dual-write period or live shadow dispatcher is planned. Old product repositories may retain historical content, but active v3 work uses the new workspace semantics. Merely leaving old documentation available is not backward compatibility.

Before v3 effects begin, a failed cutover can return to the settled v2 configuration after verifying authority. After effects begin, first stop admission and settle or isolate those effects. Then choose fix-forward or a deliberate fresh v2 restart based on current native facts. Do not restore an old database and blindly resume either engine. If an effect remains ambiguous, keep its affected target paused; do not make the ambiguity disappear by changing frameworks.

## Risks, responses and decision ownership

| Risk | Early signal | Response and owner |
|---|---|---|
| Rewrite loses obscure but necessary behavior | Capability without scenario; repeated surprises from v2 | Product owner resolves scope; integrator adds behavioral case before declaring completeness |
| Clean modules reproduce old bureaucracy | Routine journey needs manual pins, receipt forms or several PRs | Experience owner removes the join or automates it; preserve only actual technical/authority checks |
| Custom durable runtime expands uncontrollably | Many operation-specific recovery branches in M1 | Runtime owner tests the plain worker baseline, evaluates alternatives against demonstrated gaps and selects one engine before M2 expansion |
| Planning or pack composition becomes a framework project | New abstractions without a retained scenario that needs them | Scenario owner keeps typed recipes and exact built-in references; expand only for a concrete accepted case |
| Parallelism increases conflicts instead of throughput | High rebase churn, overlapping contract changes, idle dependents | Integrator narrows work units and serializes the shared contract edit only |
| Monorepo CI becomes the bottleneck | Full fast-suite or explicit integration mapping exceeds the measured feedback budget | Delivery owner introduces justified graph/input selection; correctness obligations remain explicit |
| Forge becomes the runtime bottleneck | API traffic grows with internal steps or agent count; native outage stalls independent work | Runtime/adapter owners keep state and scheduling local, share observations and coalesce projections; preserve consequential native checks |
| Self-hosting transfers more operational cost than it removes | Forge latency, maintenance or recovery misses the recorded budget | Forge owner measures M1 workload and qualifies M4 restore/upgrade; revisit the forge choice explicitly before dependent expansion |
| Qualification grows as a Cartesian product or repeats by milestone | Independent dimensions rerun identical scenarios; unchanged candidate rebuilt for M4 | Delivery owner factors coverage, tests real interactions and carries applicable evidence with the same candidate |
| Pack extensibility becomes privileged arbitrary execution | Pack code loads into Host or obtains delivery credentials | Execution owner enforces runner capability boundary and tests containment |
| One-go scope grows without limit | New capabilities added faster than inventory closes | Product owner trades scope explicitly; defer optional features past rewrite completion |
| Research assumptions do not fit actual usage | Slow or confusing benchmark journeys despite tidy source | Experience owner changes boundaries using measured user outcomes |

Owners are responsibilities to assign at M0, not new committees or permanent approval roles. A contributor can hold several roles. Runtime and security-sensitive defects get targeted review; routine prose or pack content does not acquire a mandatory multi-reviewer chain.

## Definition of complete

The rewrite is complete when the accepted capability scope is implemented and installed, all required scenarios pass, the single production cutover has occurred, v2 has no active dispatch authority, and the bounded operational acceptance has succeeded. Completion includes a usable developer experience and recovery behavior, not merely a tidy repository and green compilation.

The documentation PR containing this proposal completes only the design task. It does not satisfy any implementation milestone or begin the rewrite before v2 is finished.
