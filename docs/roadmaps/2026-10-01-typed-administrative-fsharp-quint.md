# OPS-TYPED-01 — F# administrative policy and Quint lifecycle correspondence

Accepted 2026-10-01 under the user instruction to use F# and Quint.

Feature **OPS-TYPED-01**, routine route. Owning programme repository `.github`; product policies remain in FSBarV2 and FS.GG.SC2.Client; common delivery/runtime administrative producers remain in Coordination; root integrates the programme. Backlink: [Unified section 9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).

Outcome: current qualification and administrative entrypoints derive identities and admission decisions from bounded typed F# inputs, and stateful operation policy has executable Quint/F# correspondence. Thin filesystem, process, HTTP, YAML and Playwright adapters remain where needed. A replacement counts as delivered only when the real consumer executes its qualified bytes. Existing Authority and settlement protocols remain their completion authority.

### Design boundaries

- Use private constructors/validated values for content digest, source revision, module binding, configuration binding, artifact role and operation binding. The constructor must establish the semantic relationship using acquired bytes or a verified acquisition observation. A 64-hex-character string wrapped in a type does not prove an artifact binding.
- Separate exact byte identity from semantic equivalence. Existing signed/hash-bound bytes keep their exact identity. A closed JSON selection with reordered properties represents the same selection; reject duplicate/missing/extra keys and wrong scalar kinds before constructing it. Preserve established canonical wire bytes, Unicode/number limits and schema versions.
- Derive repeated profile/module/configuration identities from one validated source snapshot. Generate manifests, admission arguments and workflow inputs from that snapshot; reject an independently supplied conflicting value. Bind receipts to the produced executable/artifact identity as well as source/model/input identity.
- Structured provenance uses explicit actor/role, source/input digests, access/authoring boundary and evidence identity. A prose report is explanatory. Neither an English regex nor a self-authored boolean proves custody; trust comes from the existing authorized producer and its observed boundary. Missing provenance stays unknown/refused. Do not synthesize a stronger attestation from old prose.
- Model concrete ordering hazards: acquisition can change after validation; an effect may start but return no acknowledgment; cancellation can arrive during acquisition/cleanup. Keep outcome and cleanup separate. Exit 0, test discovery, cleanup success and source delivery cannot imply native success.
- New qualification policy is F#. No new generic public package/API, daemon, policy registry, receipt ceremony or control plane is required. Preserve the existing narrow process route and real native safeguards.

### Ready milestones

- [x] **OPS-TYPED-01.1 — BAR production harness uses a typed stock-selection contract.** Route: routine. Source delivered through [FSBarV2 #15](https://github.com/FS-GG/FSBarV2/pull/15) at `5c4a39a0b656bd1e65503697334b01c0182e6a30`, tree `ebec7dde289eb1076ff10d61c2b01de51e6551e6`, equal reviewed head `b969ab54c9cf5f72448bfc4d10af204e10814199`. Production 29/29 checks and .NET/Fable parity passed; hosted validation is not required in this repository. Selected native operation remains pending. No dependency on SC2, FourD, P4 or later Fable product adoption. Move the four-field closed selection policy into a small qualification-owned F# module compiled to JS with the repo's pinned Fable 5.18.0. `validateStockSmokeHandoff` calls that generated function. Preserve the existing closed raw JSON parser and all other production checks. Acceptance: actual canonical Python-order input and all 24 property permutations pass the real production function; missing/extra/duplicate keys, changed values and string/bool counts refuse; .NET and Fable outputs agree; production `--list` discovers exactly one Count1 test without browser launch. This is a stateless language repair, not a reason to invent a state machine. Source delivery does not establish Count1/native acceptance.

- [x] **OPS-TYPED-01.2 — SC2 preparation and browser configuration use one typed binding contract.** Route: routine; source delivered through [SC2 #32](https://github.com/FS-GG/FS.GG.SC2.Client/pull/32) at `ee0469a2c1241d04a5e724517121e567ad5519f3`, tree `422812af17d06de7f5314d31a25826fcc5f25cd9`, equal reviewed head `75c01bc5af273f5e7b82c1c00b0f2aa3ae233786`. [Hosted verification 36896845198](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36896845198) passed after clean build-order repair. Protected artifact adoption, advisor and replay acceptance remain open. First model acquisition, validation, admission, invalidation and pre-effect refusal in Quint; then implement the corresponding F# reducer and constructor. Reuse `ModuleConfigurationCodec.encode/decode` and manifest validation. A bounded preparation entrypoint reads actual Wasm, manifest, JSON configuration and provenance, derives the Wasm digest and canonical configuration digest, joins all identities and emits a content-bound prepared result. Source preflight must execute it before any display/Gateway/browser/game acquisition, and the consuming runner verifies the admitted byte snapshot again at its effect boundary. Use the same F# document-validation/encoding function via Fable behind `module-configuration.js`; keep WebCrypto/file reading as adapters. Acceptance: stale module identity fails before any process-acquisition call; the accepted values retain their established canonical configuration digest; bad module/manifest/config/raw-file combinations and altered-after-validation inputs fail; source preflight and the public browser parser exercise the same typed policy. Structured provenance must retain its original independent-author boundary and fail on missing evidence. A private fixture or source-only validator is not sufficient completion.

- [ ] **OPS-TYPED-01.3 — FourD acquisition policy and root orchestration have typed lifecycle ownership.** Route: routine; source and workflow integration delivered through [Coordination #917](https://github.com/FS-GG/FS.GG.Coordination/pull/917) at `2f8437ccbf25606ea9cd567b02924309e3a45fde`, reviewed head `fa776bd8fee2c5038b746e2b26c3d9274797c7bc`. Exact-head [full validation 36908992547](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36908992547) and [bootstrap 36908992422](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36908992422) passed. The [owning source evidence](https://github.com/FS-GG/FS.GG.Coordination/blob/2f8437ccbf25606ea9cd567b02924309e3a45fde/docs/roadmaps/evidence/ops-typed-01.3-source-integration-20261001.md) retains the compiled policy, generated Quint model and production-reducer correspondence. Protected capacity/reservation, encrypted source transport, source-bound producer/root packet and artifact adoption, native qualification and cleanup/credential retirement remain open.

  Existing FourD owner, independent from .1/.2 and P4. The accepted source scope includes preserving the prepared R1/R2/R3 regressions and establishing a consumer-owned Quint model and F# reducer/codecs before porting decisions. A qualification-local F# executable owns admission, source/profile joins, acquisition state, effect eligibility, outcome and cleanup classification. Existing bounded process/filesystem/HTTP/sealer mechanisms may remain thin adapters; Python must no longer independently choose admission/success/cleanup policy on this selected route. Use a bounded local adapter boundary, not a service. Keep held-FD ownership and guaranteed emergency cleanup in the adapter; typed transitions consume actual observations, never an assumed successful command. Scope includes the prepared root operation policy, not just a pure validator beside the still-authoritative Python wrapper. Acceptance: the actual selected entrypoint runs the compiled reducer; nonroot raw commit and umask077 reconstruction succeeds; cancellation after child-created plaintext settles child scope before wiping held inodes; replacement paths remain untouched; partial/unacknowledged effects and cleanup failure stay unknown/incomplete. All relevant source and root-wrapper regression scenarios run through the replacement. No actual key/source/capsule/remote operation is part of worker verification.

- [ ] **OPS-TYPED-01.4 — Published replacements reach the selected production entrypoints.** Depends separately on the corresponding .1/.2/.3 source delivery, not on completion of every lane. Root integrates/protects source, builds exact replacement artifacts and creates immutable successor packets. Reuse the owning repository's actual publication route: BAR/SC2 source-derived browser/qualification artifacts, and FourD's qualified source/runtime acquisition route. If an installed CLI/package is used, publish and pin that exact existing producer; a local build is not installed delivery. Perform effect-free preflight using the final packet and real entrypoint. Then root may perform the already-authorized bounded operation with its existing safeguards. Record native result and cleanup separately; a refused/unknown result leaves native acceptance open. Retire each superseded policy path only after its real consumer demonstrably uses the replacement and the owning acceptance is met.

- [ ] **OPS-TYPED-01.5 — SC2 advisor terminal diagnostics, first administrative-owner migration slice.** Route: routine; owning producer FS.GG.SC2.Client. Diagnostic source and production compile/gate integration delivered through [SC2 #33](https://github.com/FS-GG/FS.GG.SC2.Client/pull/33) at `039feddc07c3c0d47802485dd51c85f3d0a9d54c`, reviewed head `bdca1e55f994bcf73889f8575dcf889a7c1ac6ea`; exact-head [hosted verification 36908772155](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36908772155) passed.

  Protected artifact rebuilding/adoption, private failure-summary adoption, actual advisor processing and replay/native acceptance remain open; historical attempt n remains Unknown and native acceptance stays SC2C-01.6f. That attempt passed genuine controller pointer/keyboard movement and exposed a missing identity-bound advisor refusal reason. Delivered diagnostic scope includes a closed F# advisor/input terminal-disposition reducer, its Fable production projection, branch-authored supervisor observations, bounded capture/driver consumption and an independent small Quint/FsQuint correspondence model. Preserve preparation model .2, 250 ms processing maximum, queue/capability limits, guest ABI and controller/advisor authority. Missing invocation evidence remains Unknown; at-most-one terminal is safety, bounded completion is verified separately under explicit progress assumptions. Acceptance exercises the actual fanout, runtime, supervisor and public capture, including post-dispatch destruction, replacement and stale-controller false matches. Production compile lists are integrated; root separately adopts exact generated assets and a new private summary/runner successor before a later actual operation. Source completion establishes diagnostics, not advisor success, replay or native acceptance.

The missing process record does not prove pre-dispatch refusal: supervisor destruction after dispatch can settle a pending request without `abiInvoked`. Existing worker errors/timeouts can also stamp that compatibility flag without proving an ABI return. New observations retain dispatch and available return evidence separately and never classify legacy error strings as authority. A full target/input/session-incarnation identity is snapshotted before awaiting work. Legal empty successful output can be Processed; absent observations remain Unknown. Controlled real-supervisor tests must verify terminal identity, no duplicate/conflicting completion, unchanged controller command count, exact refusal/capture matching and post-dispatch destruction.

### Selected ordinary receiver activation — SDD

The producer installed by [SDD #1110](https://github.com/FS-GG/FS.GG.SDD/pull/1110)
subsequently admitted genuine routine source changes through its unchanged default-branch
`pull_request_target` workflow. [Run 38585012934, attempt 1](https://github.com/FS-GG/FS.GG.SDD/actions/runs/38585012934)
admitted [SDD #1111](https://github.com/FS-GG/FS.GG.SDD/pull/1111) at exact head
`01bafdd2ec2e6e79bcd63aaa4780c4349eb78034` (25 changed paths).
[Run 38589186761, attempt 1](https://github.com/FS-GG/FS.GG.SDD/actions/runs/38589186761)
admitted [SDD #1112](https://github.com/FS-GG/FS.GG.SDD/pull/1112) at exact head
`3a4246d138239fcf9de140e347cafc14dc122a09` (one changed path). Both logs report
`check-claim-generation: OK` with `operation=source-change`. The latter PR's required
checks passed, and native merge readback identifies protected commit
`3555ce34456d0d60f1ad63852e40c8f179cc70dd`.

This closes SDD's selected ordinary routine-producer activation assertion. Published typed
replacement installation, downloaded-only package adoption, native acceptance and administrative
retirement remain open under their owning gates. [FsQuint #44](https://github.com/FS-GG/FsQuint/pull/44)
was source installation only: its protected routine workflow had no genuine run at the
2026-10-15 readback (activation is recorded in the next section). The original FsQuint Execution adoption candidate and its reservations
remain held pending accepted producer publication and the existing receiver qualification.

### Selected ordinary receiver activation — FsQuint

The producer installed by [FsQuint #44](https://github.com/FS-GG/FsQuint/pull/44) (merge
`48cf3075914583f9830e49a2f400678f1546024d`) subsequently admitted a genuine routine source
change through its unchanged default-branch `pull_request_target` workflow.
[Run 40120326445, attempt 1](https://github.com/FS-GG/FsQuint/actions/runs/40120326445)
admitted [FsQuint #46](https://github.com/FS-GG/FsQuint/pull/46) (org-wide publication writer
lock adoption, FS-GG/.github#4753) at exact head `1690d96b89d872e9cfec6643aa0cc678d1cc6f4a`
(one changed path), base `48cf3075914583f9830e49a2f400678f1546024d`. The log reports
`check-claim-generation: OK` with `operation=source-change`. The PR's `qualify` check
([run 40120326526](https://github.com/FS-GG/FsQuint/actions/runs/40120326526)) and the
`routine-eligibility` check passed, and native merge readback identifies protected commit
`9d28d27dad8b3a8d9040deb3fce18dcc7d8f551e`; the main-push `ci` run
[40120617845](https://github.com/FS-GG/FsQuint/actions/runs/40120617845) also passed. The five producer
files (`.fsgg/routine-development.json`, `.github/workflows/routine-eligibility.yml`,
`scripts/check-claim-generation.py`, `scripts/check-routine-eligibility-envelope.py`,
`scripts/lib/gate.py`) are byte- and mode-identical at the installation merge (which was also #46's base) and at
the protected merge, and match FS-GG/.github commit `416381bc4da37cb5089da08a45282e2222104c27`.

This closes FsQuint's selected ordinary routine-producer activation assertion
([FsQuint #45](https://github.com/FS-GG/FsQuint/issues/45)); no synthetic change was needed.
FsQuint has no repository branch protection, so "required" checks are the policy list in
`.fsgg/routine-development.json`, not a GitHub-enforced merge gate; of its three names only
`routine-eligibility` exists as a check run on #46 (beside `qualify`), the other two are
echoed by the validator but not separate checks. No old FsQuint route is
established as superseded: the vendored `work-roadmap` skill is an explicitly invoked tool, not
a gate that the producer replaces, so nothing is retired. The producer is the canonical Python
eligibility check, not a published typed (F#) replacement; typed replacement installation,
downloaded-only package adoption, native acceptance and administrative retirement remain open
under their owning gates.

### Later outcome outline, not another ready queue

**Remaining OPS-TYPED-01.5 administrative-owner migration outline.** The diagnostic source slice above is delivered; its native adoption remains open and does not wait for broad producer adoption. The inventory below stays deferred until its own measured coherent slice is selected. PR/delivery/telemetry/helper rewrites retain their completion authority. The inventory below identifies ownership, not permission to rewrite every script.

| Existing surface | F# owner and migration boundary | Evidence to make next slice ready |
|---|---|---|
| `.github/tools/pr-lane-admission.py` | Coordination's existing CLI/GitHub administrative boundary for pure campaign/chain/head admission; GitHub remains the remote creation authority. Python may forward process/I/O while policy moves. | Exact open-PR/head re-read fixtures, same-chain/third-managed-slot refusals, competing caller race stated honestly, one real participating integrator route. No invented GitHub-wide lock. |
| `.github/tools/routine-delivery.py` | Existing Qualification.Contracts, qualification selection, ordinary settlement/provider APIs; preserve ADR-0084 current/reused/deferred/failed and pending/disputed behavior. | .NET replay of current behavioral fixtures, optimistic reuse still starts coherent validation, late failure fences dependent activation, correlated merge readback, one published CLI adoption. Do not create another settlement authority. |
| Telemetry adapters/helper ownership | Existing Orchestration.Observer and roadmap-telemetry CLI. | Exact batch persistence/retry identity, terminal usage join, applied-vs-durable receipt, missing usage remains unknown, advisory failure never blocks valid delivery. Avoid observer replacement merely to change language. |
| HOST workflow/profile construction | Existing F# recipe/runtime owner; workflow renderer and packet builder derive one profile/source snapshot. | Regression with predecessor `5a30fc…` versus current `1ef6d54e…`, exact emitted-workflow/recipe/admission join before dispatch. Completed V2 acceptance stays closed. |
| BAR private preparation/runtime closure | FSBar qualification producer owns selection/profile/artifact-role policy; HighBar retains native engine facts. | Selected highBar **and NullAI** are derived into the expected executable closure; actual mapped modules and acquired identities join. Preserve current passed closure and cleanup evidence; no native adapter rewrite. |
| SC2 author provenance/remaining qualification JS | SC2 authoring/qualification producer, retaining separate independent author and product qualifier roles. | Machine provenance generated at authoring source, imported by published preparation, same contract consumed by browser/runner; genuine mixed-input/native/replay proof remains SC2C-01's authority. |

**2026-10-01 HOST binding source integration.** The qualification-local F#
constructor and dedicated Linux process scope now have a standalone hosted gate
in `telemetry-host-package.yml`: exact .NET SDK/runtime provenance, 26 compiled
F# tests, 38 Python consumer/template tests, and rendered YAML/shell validation
run before the existing browser and package work. This is a linear static and
test preflight; a new state model would duplicate the existing stateless
binding and OS adapter tests. The joined private template remains a source
candidate. Protected adoption, private placement, credentials and a genuine
native qualification stay separate root-owned gates.

FABLE-ADOPT-01 remains the independent staged Rendering/Game/Templates/FourD product adoption programme. OPS-TYPED changes qualification/admin seams only. Its immediate SC2 configuration-codec reuse is a named seam to record in that inventory, not a restart or duplicate of later whole-product SC2/BAR adoption.

**2026-10-02 HOST acquired-attempt source CLOSED.** The qualification-local
[F# caller](../../deployment/telemetry-collector/host-attempt/README.md) drives
the actual operation reducer from fixed HTTP and inherited-channel observations.
The canonical Quint model covers ownership, effect uncertainty, deadlines and
cleanup; seven generated ITFs compare 91 transitions with the production reducer,
and an acquired-caller execution contributes 16 observed transitions. The hosted
gate now expects 38 compiled controls, 16 transport controls and five CLI controls,
including a separately compiled acquisition-identity guard mutation. It builds
the unchanged binding from published recipe `8ad0da67004d670c6803f34755dfe759a7fc84e7`.
The independent B1/B2 review qualifies source `550e201270cb2eee6822aec6b30c8da9e2a32815`,
tree `3111c5386e284c41d3e8f8eb24ef1fc836297e34`, selected DLL
`5433508768b9a12a94fccd152d5fbf38826983e693be34deac51aad08e90ed2d`
and review record SHA256 `d09afa7201719364c76b6c099851b65b8e6f7b4e5a4008042b2a5dbbcccef3cc`.
An authority `process-cleanup-unknown` refusal is terminal after one call, and
effect-time revalidation reopens and binds all four fixed payload bytes even
when Git status is hidden by an index flag. [PR #4089](https://github.com/FS-GG/.github/pull/4089)
merged at protected `59cfb2a9a47c74ebcbe08a0605428f2b12cde9d2`, tree
`debb71994c236ec129e3861a50780a43bdac8498`, and exact-tree native package run
`36972071422` passed. Protected runtime adoption, private channel/custody joins and genuine
native acceptance remain open.

**2026-10-02 HOST owned-launch repair source READY.** The qualification-local receiver now
holds its fixed Git guest behind an exact inherited gate until the parent records and rechecks the
direct PID/start identity and pidfd. One release intention precedes same-PID exec. Identity failure,
cancellation before/after release, deadline, lost response and post-release failure stay sticky;
settlement still requires the direct process, descendants and readers. Production exposes no test
launcher roles. Exact-source checks pass 35 receiver and 41 caller controls. The canonical extension
adds eight launch ITFs / 23 actual reducer transitions while retaining the caller's seven ITFs / 91
transitions; correspondence verifies both selected/loaded DLL hashes. Published recipe `8ad0da...`,
profile `1ef6d54e...` and four payload bytes remain fixed caller inputs. Protected rebuild, custody,
private channel joins and native acceptance remain open.

The workflow uses the existing linear static preflight, adds the canonical
model and compiled correspondence checks before browser/package work, and
retains SDK custody and the existing binding gate. All eight resulting shell
blocks parse; 38 private-template controls pass without skips. A copied older
test expected six blocks; integration corrects the expectation to the actual
eight. No separate pipeline state model or measured CI saving is claimed.
Native authorization channels, private custody, workflow admission, dispatch,
artifact evidence and scoped cleanup remain root-owned operating gates.
There is no generated workspace or default activation effect; published
receiver/profile and the four native payloads remain unchanged.

**2026-10-02 P4 task-private SDK selection source CLOSED.** The protected
[Sandbox #47](https://github.com/FS-GG/FS.GG.GitHub.Substrate.Sandbox/pull/47) source at
`3924409829e96fc4b436e2a26179d885bbf8837c`, tree
`d0b24b64a44e3af1e0f0612ddf270c6793550c74`, is equal to reviewed
`5fec142a5ca56472d29968e4dd069256528058dd`. It selects exact SDK `10.0.400`
in a fresh task-private root and uses the absolute regular host for version inspection,
locked restore, build and readiness. Twenty-two controls and the actual cold locked
restore/build passed. Earlier run `36973970176` remains refused before restore with
`dotnet-version-mismatch` and no facts or effects. Fresh credential-free readiness,
private facts and native qualification remain open; no hosted source gate is required.

### Model and correspondence contract

SC2's model has one qualification-owner state and opaque artifact/version IDs. FourD has one operation-owner state plus an explicit set of owned resource identities and observations; it does not model unrelated distributed Authority consensus. Plain Quint fits this shared-state boundary. Bound the instance to two artifact generations and a small fixed set of owned roles. Time is a monotonically advancing budget/epoch; include aggregate deadline exhaustion and cancellation, not real timers.

Keep separate transitions for acquire, validate, admit, mutate/invalidate, begin-effect, observe-result-or-unknown, begin-cleanup, observe-owned-resource-closed and finish. Do not fuse validation and consumption: their intervening mutation is the defect being checked. In FourD, a process can create plaintext before its caller receives success; represent that owned resource before cancellation settles it. Unknown operation outcome and settled cleanup can coexist. Later authoritative result readback may resolve unknown; cleanup alone never does.

Required invariants: effect requires the currently validated/admitted exact identity; a changed artifact invalidates admission; no stale/mixed module/profile/configuration join; no unowned resource deletion; terminal-success requires positive operation evidence; cleanup-complete requires observations covering every acquired/possibly-created owned resource; unknown result never becomes success merely through timeout/exit/cleanup; aggregate budget never renews at a subprocess boundary. Required witnesses cover admitted success, stale-input refusal, cancellation before/after acquisition, effect response loss, and successful/failed cleanup with unknown outcome.

Implement incrementally: typecheck, then sampled `quint run` after each action. Use a checked concrete instance and positive `--witnesses` with nonzero reachability; no exhaustive `quint verify` was requested. Start with 100 bounded samples, then run 5,000 samples at the finished small-model gate if runtime is proportionate. Record seed/steps/sample count and model/compiler/source identities. A sampling pass is not exhaustive proof.

Use published FsQuint 0.1.0's `QuintReplay.decodeItf` and `compare` to compare traces with observations from the **actual F# reducer**, plus .NET/Fable equivalence where the same contract is browser-consumed. Bind each action to its implementing function and source identity. Add independent mutation/negative cases for stale identities, changed bytes, duplicate keys, skipped cleanup and false success so a shared wrong oracle cannot self-certify. Never loosen a property to fit a failed implementation. Hashing, filesystem identity and OS cleanup correctness remain implementation/adapter obligations with focused tests; symbolic Quint IDs do not prove cryptographic or OS behavior.

### Generated-workspace effect and completion

OPS-TYPED-01.1–.3 have **no generated workspace/default effect**. The first enabled operational effect is .4 when the selected real BAR/SC2/FourD entrypoint invokes the replacement. No family is enrolled by package availability. Later shared administrative changes affect fresh `typed-sdd`/routine workspace content only if the published Coordination/driver helper is adopted by the actual SDD materializer and selected Templates/provider release. Their exact release identities are not yet selected. Clean creation must prove the generated route invokes the pinned replacement and its refusal case. Existing workspaces require separate preserving pin/adoption evidence; source merge does not upgrade them. Defaults remain as selected.

The SC2 diagnostic slice has no generated-workspace/default effect. Its first operational change requires the clean browser artifact and selected runner/failure-summary successor to retain the typed terminal. Historical attempt n keeps its unknown cause.

Feature completion requires the first production seams adopted and the selected administrative ownership/retirement outcomes resolved or explicitly scoped by root; checking the current ready window does not silently declare the broader migration finished. Root records source delivery, publication, adoption, native acceptance and cleanup separately in the existing programme report. Telemetry remains the existing observer route; missing configuration and usage are unknown, not zero-cost/compliant. Current dispatch telemetry is not configured; usage and economics remain unknown.

### Local registry preparation closure — 2026-10-17

[Shared registry PR #4631](https://github.com/FS-GG/.github/pull/4631) reconciled the genuinely
published Game WASM 0.4 inventory while retaining open installed/product acceptance. Its original
Atlas seed and component-inventory failures exposed a local preparation gap: registry regions,
packaged public knowledge, complete skill-tree manifests and own registry digests must travel
together. This is a bounded source preparation correction, with no change to typed operation
authority, current published packages or installed consumers.

Run `python3 -B scripts/prepare-registry --check` for a read-only diagnosis, or `--write` to prepare
the generated closure. Python with PyYAML, bash and jq remain prerequisites; CLR is not invoked.
The command uses the existing public Atlas archive validator, the generator's canonical targets
and source declarations, both maintained manifest builders and existing own-row digest/predicate
reconciliation. Its order is public seeds, every non-protocol registry projection, both skill
manifests, existing `.github` registry rows, then affected-output verification. It reads neither
private archives nor foreign producer checkouts. Foreign rows, authored capture decisions,
registry dates/changelogs and publication judgement remain outside automatic reconciliation.

Check reports all independently checkable stale families; manifests and own rows are explicitly
blocked when upstream seed/projection inputs are stale or unavailable. Write stops dependent
mutation on invalid inputs and rechecks the prepared closure. `generate-projections --registry-only`
is the underlying explicit preparation subset, selected from its sole target/source declarations;
the default full generation and `--check` still require genuine compiled protocol facts.
Every preparation result states that protocol/full qualification was not run.

Static dependency inspection and inert production-caller controls cover the actual version-to-six
region join, both public seed mirrors, projected skill/tree/own-row binding, foreign-row preservation,
ambiguous identity refusal, missing/malformed targets, unavailable prerequisites, read-only checks,
idempotence and the unchanged full-mode engine fence. Reuse the existing projection test runner
and hosted workflow; no separate model or workflow is needed for this finite composition.
Local checks were observed at about half a second and the focused suite at about twenty seconds;
cache state was uncontrolled and comparative effort/latency savings remain unknown. Full canonical
projection, typed registry, semantic and required hosted checks remain final delivery obligations.
