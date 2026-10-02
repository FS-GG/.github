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

- [ ] **OPS-TYPED-01.3 — FourD acquisition policy and root orchestration have typed lifecycle ownership.** Route: routine; source candidate is in independent review and awaits workflow integration. Existing FourD owner, independent from .1/.2 and P4. Preserve the prepared R1/R2/R3 regressions, then create a consumer-owned Quint model and F# reducer/codecs before porting decisions. A qualification-local F# executable owns admission, source/profile joins, acquisition state, effect eligibility, outcome and cleanup classification. Existing bounded process/filesystem/HTTP/sealer mechanisms may remain thin adapters; Python must no longer independently choose admission/success/cleanup policy on this selected route. Use a bounded local adapter boundary, not a service. Keep held-FD ownership and guaranteed emergency cleanup in the adapter; typed transitions consume actual observations, never an assumed successful command. Scope includes the prepared root operation policy, not just a pure validator beside the still-authoritative Python wrapper. Acceptance: the actual selected entrypoint runs the compiled reducer; nonroot raw commit and umask077 reconstruction succeeds; cancellation after child-created plaintext settles child scope before wiping held inodes; replacement paths remain untouched; partial/unacknowledged effects and cleanup failure stay unknown/incomplete. All relevant source and root-wrapper regression scenarios run through the replacement. No actual key/source/capsule/remote operation is part of worker verification.

- [ ] **OPS-TYPED-01.4 — Published replacements reach the selected production entrypoints.** Depends separately on the corresponding .1/.2/.3 source delivery, not on completion of every lane. Root integrates/protects source, builds exact replacement artifacts and creates immutable successor packets. Reuse the owning repository's actual publication route: BAR/SC2 source-derived browser/qualification artifacts, and FourD's qualified source/runtime acquisition route. If an installed CLI/package is used, publish and pin that exact existing producer; a local build is not installed delivery. Perform effect-free preflight using the final packet and real entrypoint. Then root may perform the already-authorized bounded operation with its existing safeguards. Record native result and cleanup separately; a refused/unknown result leaves native acceptance open. Retire each superseded policy path only after its real consumer demonstrably uses the replacement and the owning acceptance is met.

- [ ] **OPS-TYPED-01.5 — SC2 advisor terminal diagnostics, first administrative-owner migration slice.** Route: routine; owning producer FS.GG.SC2.Client, native acceptance remains SC2C-01.6f. Actual attempt n passed genuine controller pointer/keyboard movement and exposed a missing identity-bound advisor refusal reason. Add a closed F# advisor/input terminal-disposition reducer, its Fable production projection, branch-authored supervisor observations, bounded capture/driver consumption and an independent small Quint/FsQuint correspondence model. Preserve preparation model .2, 250 ms processing maximum, queue/capability limits, guest ABI and controller/advisor authority. Missing invocation evidence remains Unknown; at-most-one terminal is safety, bounded completion is verified separately under explicit progress assumptions. Acceptance exercises the actual fanout, runtime, supervisor and public capture, including post-dispatch destruction, replacement and stale-controller false matches. Root integrates shared compile lists and separately adopts exact generated assets and a new private summary/runner successor before a later actual operation. Source completion establishes diagnostics, not advisor success, replay or native acceptance.

The missing process record does not prove pre-dispatch refusal: supervisor destruction after dispatch can settle a pending request without `abiInvoked`. Existing worker errors/timeouts can also stamp that compatibility flag without proving an ABI return. New observations retain dispatch and available return evidence separately and never classify legacy error strings as authority. A full target/input/session-incarnation identity is snapshotted before awaiting work. Legal empty successful output can be Processed; absent observations remain Unknown. Controlled real-supervisor tests must verify terminal identity, no duplicate/conflicting completion, unchanged controller command count, exact refusal/capture matching and post-dispatch destruction.

### Later outcome outline, not another ready queue

**Remaining OPS-TYPED-01.5 administrative-owner migration outline.** The first ready slice above is a material diagnostic prerequisite from actual SC2 qualification; it is the bounded exception to waiting for broad producer adoption. The inventory below stays deferred until its own measured coherent slice is selected. PR/delivery/telemetry/helper rewrites retain their completion authority. The inventory below identifies ownership, not permission to rewrite every script.

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

**2026-10-02 HOST first-refusal diagnostics source READY.** Source commit
`992f2d3d5f6edbbdcde8e877c7fe877b2e106ad4`, tree
`ce84ee1700cc2ccd099f763b50262749aa4d958d`, adds an invocation-local,
first-failure-only closed F# diagnostic at the existing preparation and
transport boundaries. One bounded compiled-caller witness retained
`transport` / `invoke-binding` / returned exit 2 /
`process-cleanup-unknown` while the original state remained
`source-invalidated` with no secret or dispatch intention. This identifies the
published HostBinding process-scope retirement boundary; the specific internal
poison or settlement predicate remains unknown. The final source passed 40 F#,
16 transport and five CLI controls. The canonical reducer, seven ITFs and 91
transition correspondence are byte unchanged. The hosted gate now asserts the
40-test F# inventory. A later green caller cannot settle the retained failed
scope. Root still owns source admission, any bounded receiver-side diagnostic,
protected rebuild, private custody and native acceptance.

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
