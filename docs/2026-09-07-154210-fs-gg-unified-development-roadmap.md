---
title: "FS-GG Unified Development Roadmap"
category: Design
categoryindex: 4
description: "A researched successor plan from the current v2 frontier: process selection, governance, migration, installed adoption, and one conditional execution and orchestration architecture."
---

# FS-GG Unified Development Roadmap

Short name: **Unified Roadmap**. In FS-GG development discussions, **“the roadmap”**, **“current roadmap”**
and **“compacted roadmap”** refer to this document unless another roadmap is explicitly named.

Authored: **2026-09-07 15:42:10 UTC**. Section 0 evidence reconciliation: **2026-09-30**.
Status: **active programme; `.github` C0–C2 and selected C3 ordinary-V2 adoptions, including Rendering, are complete**.

**Start from completed development simplification and the existing v2 implementation. Keep the proven
`.github` settlement active, then add explicitly selected repositories through checked source, dedicated
credential custody, separate activation and observed ordinary use.** This document specifies
which development process applies to each kind of work, where its behavior belongs, what evidence is
needed, and how the programmes join.

This is the consolidated plan for the current tracks in the
[development master](development-master.md): v2, development simplification carryover, OR orchestration,
PB performance-bounded flow, and governance integration. It incorporates their relevant dependencies and
preserves later portfolio options. It does not restart simplification, completed v2 units, or old kernel
programmes. The broader inventory is accounted for in section 15 without making every proposal a current
implementation commitment.

Publication of this prose does not adopt a policy, change a GS2 unit, enable a writer, alter a lifecycle
default, or authorize an administrative operation. Existing accepted contracts, exact roadmap pins and
operating authority remain binding. The stage labels below are planning joins, not a second executable
queue. Implementation uses the existing owning units after any necessary versioned amendment.

[ADR-0091](adr/0091-speed-first-clean-v2-start.md) is the accepted owner amendment for the active V2
route. It prospectively supersedes ADR-0090 and the GS2-09 through GS2-14 migration sequence for this
clean start. Earlier accepted evidence remains historical evidence; unfinished migration, archive,
rollback and mandatory V1-admission work is cancelled or superseded, not completed.

For the operating model, start with [process selection](#4-which-development-process-applies-and-where).
For sequencing, use the [unified roadmap](#9-unified-roadmap-from-the-current-v2-frontier) and
[GS2 integration map](#10-exact-gs2-integration-and-contract-change-boundaries). Sections 1–2 establish
the evidence; sections 14–15 account for every active source track and the wider proposal inventory.
For Astra's planning boundaries and the available subroadmaps, use the
[feature-part index](#98-feature-parts-and-subroadmap-index). For when progress reaches a newly created
workspace, use the [workspace impact map](#99-when-new-workspaces-change).
For the client orchestrator that connects to a project master and receives jobs, see
[cooperative orchestrators](#85-cooperative-orchestrators-a-project-master-assigns-jobs-to-contributor-clients)
and their [F0–F5 roadmap](#97-f0f5-cooperative-orchestrator-development).
For stable execution policies, context/token optimization and evidence-based improvement, use
[LEARN-01](roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md).
Its later community milestone includes opt-in data contribution through an isolated Main intake and
reviewed public aggregate releases on GitHub.

## 0. Current progress report

**UNITYC-01.1a–.1b source and admission closed on 2026-09-30.** The new private
`FS-GG/FS.GG.Unity.Client` owner delivered a locked .NET 6, `netstandard2.1`
tactical reference and a dated Nebulous admission in
[Unity Client PR #1](https://github.com/FS-GG/FS.GG.Unity.Client/pull/1), merged at
`ffe3bd41e0c3be8cb81ae4670f7d8feb86a29fe6`. The exact-head
[verify run 36668103216](https://github.com/FS-GG/FS.GG.Unity.Client/actions/runs/36668103216)
passed; the reference's 11 tests also passed locally. The
[owning plan](https://github.com/FS-GG/FS.GG.Unity.Client/blob/ffe3bd41e0c3be8cb81ae4670f7d8feb86a29fe6/docs/roadmaps/unityc-01-reference-admission.md)
retained .1c owned tactical Unity source and .1d genuine server qualification. Nebulous
admission remains unqualified because its native installation and authority
capabilities were not established; no installed Unity play is claimed.

**UNITYC-01.1c source closed on 2026-09-30.**
[Unity Client PR #3](https://github.com/FS-GG/FS.GG.Unity.Client/pull/3) merged the
owner-thread and scene-incarnation shim, Unity adapter, UPM package and small
consuming server project at `1ff59f3235a433a4a9ca06173bd1258d389a5df1`.
Its exact-head [verify run 36669092820](https://github.com/FS-GG/FS.GG.Unity.Client/actions/runs/36669092820)
passed; 17 locked Release tests passed locally. The updated
[owning plan](https://github.com/FS-GG/FS.GG.Unity.Client/blob/1ff59f3235a433a4a9ca06173bd1258d389a5df1/docs/roadmaps/unityc-01-reference-admission.md)
keeps .1d open. No Unity Editor, Linux Dedicated Server module, environment
binding or license was available for native compile and play qualification.

**UNITYC-01.2a browser contract source closed on 2026-09-30.**
[Unity Client PR #4](https://github.com/FS-GG/FS.GG.Unity.Client/pull/4) merged the
versioned handshake, grant/revoke/intent/result, entitled baseline/delta/resync and
bounded managed reference driver at protected `ef573307cfea31d658b84754c39bfa21425063af`.
Its [exact-head verify run 36677152832](https://github.com/FS-GG/FS.GG.Unity.Client/actions/runs/36677152832)
passed; local Release tests passed 30/30 and independent F#/.NET and emitted-Fable/Node
JSON vectors passed 7/7. The [owning browser plan](https://github.com/FS-GG/FS.GG.Unity.Client/blob/ef573307cfea31d658b84754c39bfa21425063af/docs/roadmaps/unityc-01-browser-reference.md)
freezes parallel .2b native and .2c browser seams. The actual joined browser-to-native
journey, Unity Editor qualification under .1d, publication and installed adoption
remain open. This source slice changes no workspace or enabled runtime.

**UNITYC-01.2b–.2c joined source closed on 2026-09-30.**
[Unity Client PR #5](https://github.com/FS-GG/FS.GG.Unity.Client/pull/5) merged the
native/reference bridge and Fable browser/WASM host at protected
`6bdf0e6ad2c560521db5d255361d2a33058a6be0` (tree
`b88ca39df963bcf8060639a6db1859118306fd2d`). Its
[exact-head verify run 36680190552](https://github.com/FS-GG/FS.GG.Unity.Client/actions/runs/36680190552)
passed. The joined source passed 36 Release tests, seven cross-runtime JSON vectors,
33 browser/WASM assertions and eight emitted-client public API checks. The
[owning checkpoint](https://github.com/FS-GG/FS.GG.Unity.Client/blob/6bdf0e6ad2c560521db5d255361d2a33058a6be0/docs/roadmaps/unityc-01-browser-reference.md)
retains `.2d` runnable browser-to-reference transport, command/recovery journey and
measurements. `.1d` real Unity Editor/AOT qualification, publication and installed
adoption remain open; no new workspace or enabled runtime is established.

**UNITYC-01.2d bounded reference journey source closed on 2026-09-30.**
[Unity Client PR #6](https://github.com/FS-GG/FS.GG.Unity.Client/pull/6) merged the
opt-in loopback managed reference host and browser/WASM command, recovery and
bounded refusal journey at protected `d83e4143cd5dec5ace1c5af80db1cd481ad4fc63`.
Its [exact-head verify run 36682858322](https://github.com/FS-GG/FS.GG.Unity.Client/actions/runs/36682858322)
passed. Joined local checks passed 37 Release tests, seven cross-runtime vectors,
33 browser assertions and eight public API checks; the process journey observed
one native move, a sequence-gap resync, a two-participant cap and a 32-command
outstanding cap. The [owning checkpoint](https://github.com/FS-GG/FS.GG.Unity.Client/blob/d83e4143cd5dec5ace1c5af80db1cd481ad4fc63/docs/roadmaps/unityc-01-browser-reference.md)
retains real Unity Editor/Server and AOT qualification, publication and installed
receiver adoption. This opt-in source journey makes no Unity-native play or default
workspace claim.

**V2-LANG-01.3 read-only projection source closed on 2026-09-30.**
[Coordination PR #894](https://github.com/FS-GG/FS.GG.Coordination/pull/894) merged the optional
AG-UI 1.0 SSE adapter over durable work-item replay at protected
`5d86daf3898be683bd6720bdd36da479a29a0260`; its exact-head hosted checks passed and protected
main readback matched. The Python client, reconnect, duplicate, gap, stale-generation and observer-loss
cases qualify this source boundary. The same PR delivered portable workspace schemas and adapters as a
.2 source checkpoint; .2 publication and receiver adoption remain open. No AG-UI endpoint, live
service, package adoption or write authority is established.

**V2-LANG-01.4 bounded Agent Framework evaluation closed on 2026-09-30.**
[Coordination PR #899](https://github.com/FS-GG/FS.GG.Coordination/pull/899) merged the optional
Microsoft Agent Framework 1.22.0 `AIAgent` facade, fixed workflow, adverse-lifecycle qualification and
retained measurements at protected `9516006663393709e8f96ecd1f21b9ce16d729bd`. Its
[coherent run 36709064662](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36709064662)
succeeded on attempt 2 after one bounded Apalache timeout rerun. The trial preserves one bound launch,
keeps recovery and unknown-effect ownership in the durable coordinator, refuses framework checkpoint
replay and never claims delivery. Its measured decision rejects production adoption for this path because
the framework adds dependency, runtime and maintenance cost without a demonstrated unmet need. The source
trial remains repository evidence; no package publication, installation, activation or generated-workspace
change follows. The [owning trial record](https://github.com/FS-GG/FS.GG.Coordination/blob/9516006663393709e8f96ecd1f21b9ce16d729bd/docs/roadmaps/v2-lang-agent-framework-trial.md)
retains the exact boundary and reproducible evidence.

**V2-LANG-01.2 and .5 source and native gates advanced on 2026-09-30; adoption remains open.**
[Coordination PR #900](https://github.com/FS-GG/FS.GG.Coordination/pull/900) merged the pinned
portable-workspace qualification-image source at protected
`aca2093cf7186eee2f57f4bba8ff55c8563ab861`. [Templates PR #650](https://github.com/FS-GG/FS.GG.Templates/pull/650)
then merged fixed Rust and Go image profiles at protected
`66ce4faacc122ef4a2d2331a0c10fe388e7c3b69`; its strict hosted
[native run 36744671457](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36744671457)
passed both actual built-entrypoint routes. Coordination
[PR #902](https://github.com/FS-GG/FS.GG.Coordination/pull/902) remains open, now rebased onto protected capability source at
`241a2a25b8eacf867fc3bdd37f740cea4457ebf8`, with current-head hosted qualification pending. Its earlier exact-head
[native executor run 36747384736](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36747384736)
on `9ec9535d6e4b5da5c0840caeb17dd5abb0331c27` passed six actual isolated operations with no failed or unknown result and complete cleanup; independently
downloaded artifact `11113157066` had SHA-256
`cafe68ba23e26489ebe0d741887c15e94ac2400314d3f564ecf14f19bb33f18c`.
That native result does not merge the candidate: P2 source delivery is pending, and P3 producer publication,
P4 receiver adoption and P5 matrix closure remain open. No complete .5 language-route adoption or generated
workspace change is claimed.

**Fixed native capability diagnostic source delivered on 2026-09-30; collector qualification remains open.**
[Coordination PR #901](https://github.com/FS-GG/FS.GG.Coordination/pull/901) merged the fixed
capability operation and Main-independent replacement plan at protected
`6210dc1612e38acc7f16a6a6ce62bfad9f280c97`. The independent development result used authenticated
discovery and advertised `gpt-5.6-sol` / `medium`, with zero model sessions and complete owned cleanup.
This is a bounded capability-source result. It is not an installed collector, native capture, model turn,
enrollment, experiment or Main-independent collector/capture qualification.

**LEARN native capture and fixed-profile source are delivered; installed experiments remain open.**
[.github #3940](https://github.com/FS-GG/.github/pull/3940) delivered protected native capture and
shared-cost mechanisms, and [#3986](https://github.com/FS-GG/.github/pull/3986) bound captures to their
retained native source. Coordination [#876](https://github.com/FS-GG/FS.GG.Coordination/pull/876)
delivered the installer-v2 source and [#886](https://github.com/FS-GG/FS.GG.Coordination/pull/886)
closed LEARN-01.3's fixed-profile execution and observation source boundary. These source results do not
establish installed readiness. The later authenticated Home assessment prepared exact inactive Host 0.2.0
and manager images, but 0.2.0 predates #3986's current `capture/2` hardening. Actual discovery returned
`not-configured`, and the selected service-account configuration had no native collector role or grant.
The [user's replacement-route steering](https://github.com/FS-GG/.github/blob/9dc0a67b1e46adaae12c5c07c1caf935542d33f9/MAILBOX.md)
supersedes every earlier Home/Main-specific future installation dependency: CI owns builds, releases and
disposable diagnostics; a dedicated collector container with private persistent storage receives evidence
from development containers. Source audit confirms that the authorized replacement receiver owner can issue
its own genuine config/2 native-collector role and grant with a private CSPRNG secret and durable init/enroll
registry. No external grant issuer is a delivered dependency. Actual native-runtime custody and observed
capability, W6 readiness, capture, census/shared-cost allocation and enrollment remain open. LEARN-01.5 also
remains open. The
[owner report](https://github.com/FS-GG/.github/blob/0d317ffc72fdcaafce8eb245a960e61124d4445f/MAILBOX.md)
keeps that preparation separate from the installed fixed diagnostic described below.

The remaining LEARN route uses five ordered boundaries:

1. Publish and read back immutable current `capture/2` Host bytes through CI. Version 0.2.1 source is
   prepared at `9a0d17f11e63002bc67246bb819d0080f5b93799`, but is not published.
2. Deploy the dedicated collector container with private persistent storage and receiver-owned config/2
   role, grant, CSPRNG secret and durable init/enroll record. Keep it independent of Main accounts and files.
3. Feed it from development containers and observe the actual native provider/model/effort capability before
   W6 composition or enrollment; requested values are not observed capability.
4. Prove clean deployment, native capture, restart/recovery and owned cleanup with Main unavailable, while
   retaining canonical records across restart.
5. After replacement acceptance and retained-record inventory, preserve the required records, disable and
   remove obsolete Main jobs, services and routing, then revoke obsolete credentials. No retirement effect
   occurs before those gates pass. `main` remains the ordinary protected source branch; it is not an
   operational host dependency.

**Current frontier: ordinary V2 settlement is active in `.github`, Audio, Rendering, Net, Governance, Game, SDD, Templates and Coordination.**
`.github`’s tool manifest pins Coordination CLI 0.1.7 through [#3923](https://github.com/FS-GG/.github/pull/3923) at `bcde11a3b5996e670647c4fe306a8de1674a85c6`; its protected ordinary/rehearsal workflows continue to install the separately sealed, qualified CLI **0.1.2**. The manifest update does not upgrade that runtime. [Earlier ordinary run 36533075149](https://github.com/FS-GG/.github/actions/runs/36533075149) passed at protected `035e7d9153a2f7649d2eca1f593e323c6ea2947e` with actual CLI 0.1.2 and receipt `530a30ac84c07e1b15bf959743120fe3aad0393efd6d04ddfc0676b927d31df6`. Independent exact-main observer verification matched the native preflight byte-for-byte (SHA-256 `17f9e41b0233d859ea5f5c8ef0131e6d739590fa2ab13d47c28fc35e45c14e43`). Authority readback verified one completed generation-3 operation and one matching effect at ref `operation/61`, head `c956fba8961ad1ea4c503af82d36fc934d2415fa`. Audio uses 0.1.3, Rendering uses 0.1.4, the first combined
Net/Governance/Game wave uses 0.1.5, and SDD and Templates use 0.1.6. Checked source delivery, the shared
`OpenV2` epoch, real settlement and normal rerun are observed for each selected receiver.
The later protected-main [ordinary run 36537914121](https://github.com/FS-GG/.github/actions/runs/36537914121)
on `56aab634727483e46f74e81cfbaae9fca1356ee3` returned `SettlementSucceeded` on attempt 1 and
`SettlementAlreadyComplete` on the normal unchanged attempt 2 with receipt
`ced0902eca7900e3744c364fe9e9220b99b2db9b1e475099304bb652c3e5bf29`. Independent observer verification
passed both artifacts. Authority `operation/79` remained at
`ac0edd759e8d1a9eef2a7c7972364eebb128884b` with one completed generation-3 entry and one effect.
The earlier callable, GS2-09 and OperatingV1 evidence remains valid at its recorded scope but no longer
forms the active dependency chain. No clean-start step may relabel that evidence as migration completion,
`VerifiedV2`, or a human-run receipt.

The accepted 2026-09-29 amendment separates the selected profile's functional result from historical
economics. The bounded profile covers native protected ordinary-source delivery, actual ordinary V2
settlement, unchanged normal replay, truthful observer-loss behavior and C0–C3 evidence for `.github`,
Audio, Rendering, Net, Governance, Game, SDD, Templates and Coordination. Its five-gate evidence is joined
in the [R5 functional V2 acceptance report](reports/2026-09-29-r5-functional-v2-acceptance.md). Functional
closure does not assert installed collection, complete usage, every operation class, fleet-wide adoption or
an efficiency benefit. Historical economics remains insufficient.

### Full V2 acceptance amendment — 2026-09-29

The user removed authentic-owner and human-participation gates from full V2 acceptance.
This instruction supersedes earlier full-acceptance dependencies on the Learning installed
experiment and FourD player study, including requirements recorded in linked owning roadmaps.

- **Owner inputs:** no fresh owner attestation, provision of protected custody/configuration,
  independent window authority, prospective census/shared-cost records, per-route owner capability
  certification or live experiment enrollment is required for full V2 acceptance. LEARN-01.4's
  remaining installed experiment and W6 composition are follow-up work. Missing inputs remain
  unknown; source and diagnostic qualification do not assert installed operational readiness.
- **Human participation:** FourD's unfamiliar-player recruitment, six consenting participants,
  four completed paired comparisons and participant-derived design selection are follow-up work.
  Full V2 acceptance uses the automated technical comparison, actual downloaded runtime and
  retained-save/browser qualification. Keeping both qualified candidates opt-in is a valid
  disposition; it does not establish a player preference or usability result.
- **Remaining closure:** BAR useful-play acceptance remains at **0/6** required cases. SC2's selected
  full-session/fault-recovery cases and Learning's bounded source/diagnostic qualification have passed
  their technical gates at the scopes recorded below; SC2's bounded realtime/recovery closure and
  protected readback are delivered, while Learning's installed operation/research work remains
  follow-up. Verify remaining
  protected delivery and roadmap evidence without backdating the frozen R5 cohort. Record
  unavailable installed, human and economic evidence explicitly. Full V2 remains pending; historical
  economics may close with an insufficient-evidence disposition and no efficiency claim.
- **Host execution boundary:** V2 must qualify and operate without unattended, general-purpose
  AI agents on operational Home/Main hosts. `work-main` and `home-main` are not acceptance or
  runtime prerequisites. Development agents remain in isolated development environments; host
  actions use fixed, pre-reviewed, versioned operations with pinned artifacts, allowlisted
  parameters, narrowly scoped credentials, bounded runtime and owned cleanup. An operator or
  restricted job runner performs those actions under existing authority; arbitrary shell execution,
  broad standing SSH/sudo access and autonomous follow-up work do not meet this boundary. Source,
  artifact and diagnostic qualification must demonstrate this execution path. Genuine installed
  validation remains separately scoped. The selected fixed version/login diagnostic now has the bounded
  owner-reported installed result below; broader installed behavior and missing evidence remain open or
  unknown. The
  [V2 execution roadmap](github-substrate-v2-roadmap.md#required-host-execution-boundary--2026-09-29)
  owns the requirement and its acceptance evidence; the bounded fixed diagnostic result is recorded below.

**V2-HOST-01 fixed diagnostic boundary closed at its selected technical scope on
2026-09-30.** [Coordination #893](https://github.com/FS-GG/FS.GG.Coordination/pull/893)
and [#895](https://github.com/FS-GG/FS.GG.Coordination/pull/895) delivered the
closed reviewed `executor-compatibility/1` operation, bounded environment,
process-tree cleanup and one-shot disposable qualification. Protected #895
`d99aa20` produced [fixed run `36681426685`](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36681426685)
and result artifact `11081962815`: diagnostic-only passed, cleanup complete,
zero model sessions and zero follow-up work. Independent download matched its
API SHA-256 digest; exact Host/runner candidate identities and the reviewed
profile are bound in the [owning evidence](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/evidence/v2-host-01-fixed-qualification.md).
The #895 [postmerge coherent run `36681361583`](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36681361583)
succeeded. This closes the fixed-job source, artifact and bounded hosted diagnostic requirement.

Work-main subsequently reported one genuine installed execution of that exact fixed operation in the
[13:03 UTC owner report](https://github.com/FS-GG/.github/blob/0d317ffc72fdcaafce8eb245a960e61124d4445f/MAILBOX.md),
whose public delivery and private SystemAdmin synchronization were recorded in the
[13:12 UTC handoff](https://github.com/FS-GG/.github/blob/870ab84933220d66cd23f4b4bac3ac66bf30bcde/MAILBOX.md).
The owner-private image `sha256:a362c2b2869fef8ef247568655c3a145dbaaecb15db8de4d033331b6e859e352`
contained exact Host `ed28abb1c198a5196ae44f7c11592253573e4d23ee9938ce54a529e5420c9f4a`,
runner `eb4919fe2f3e27bbe8a75b294f5da9dc1b13a8d153edb6bc6e04b52fbd3ba76b` and Codex
0.154.0 `3188814c35471432d4123203e0eb38e5bddc60226e3d7ddf0e59e649ea140022`.
Reviewed profile `home-v2-host-fixed-20260930-r2@93c3e16ffb13ea5c6fae4368d22413e310452b62`
had SHA-256 `e75d28fd0fa60c657032c23de72345104d04d47573d86da4fca0fac873e00e64`.
From `13:00:37.3943397` to `13:00:38.7542065` UTC, `executor-compatibility/1` returned passed with
private result SHA-256 `8916fa3cf0c16ab7fbdb8de6535e6828b888509bc9db1991337256e3d8c7af23`.
All process-tree termination and workspace-removal flags were true, and owner readback found the disposable
workspace and one-shot container absent. The older runtime remained byte-identical at
`03b349d012bd0fa6db9eaf9c7ed8623f94c196e11cf883e4d814cdfd1fdf1fac`. Root verified the public
report delivery but could not inspect the private runtime artifacts, so this is an owner-reported installed
version/login-readiness result. It establishes no model or resume session, general Host service activation,
retained-runtime upgrade or LEARN collector readiness. Full V2 remains pending on its other selected gates.

These removed requirements are outside the full V2 acceptance gate, rather than completed tests.
The frozen R5 cohort, cutoff and historical original-item outcomes stay intact. Required native
checks and existing runtime authentication, custody and effect-authority enforcement still apply
to any operation actually performed; this amendment grants no credentials or access.

[Functional acceptance #3966](https://github.com/FS-GG/.github/pull/3966) delivered the selected profile at protected `ddb2e3284dc250442580b95e49594e5289f23026`. Its automatic post-merge [ordinary run 36541554972](https://github.com/FS-GG/.github/actions/runs/36541554972) succeeded with receipt `488130cb9441b7d3d5b9c081b1a55cbd2509b4ab4244246b5cbb0220a47a6949`. Independent observer verification ran from that exact protected checkout. Fresh Authority readback in `FS-GG/FS.GG.Coordination.Authority` found one completed generation-3 entry and one matching effect at `operation/c9`, head `f9bf3cb03cd7f12519141e6256d852ef71527015`. This supplements the unchanged normal-replay proof above; it does not upgrade installed telemetry or historical economics.

On **2026-09-28**, the shared authority advanced by one fast-forward append to generation 2 `OpenV2`
at [commit `26d1882`](https://github.com/FS-GG/FS.GG.Coordination.Authority/commit/26d1882af9293b264df17a1fa98515e108313fe5).
The exact original cutover-ref writer rule was restored and independently read back. The conflicting
V1 genesis workflow is disabled. [Activation PR #3913](https://github.com/FS-GG/.github/pull/3913)
merged as `a98162fb119c43f2e5d60c2b284d01e81ac468db`. The [live run](https://github.com/FS-GG/.github/actions/runs/36395767759)
reported `SettlementSucceeded` on attempt 3 and `SettlementAlreadyComplete` on the normal whole-run
replay, attempt 4, both binding `96eebd38b0639d4c446d62dcfa413adffae32c839b387006d68322b1ca7399d6`.
C0–C2 are complete for this bounded path; C3 is continuous `.github` use and explicit later repository
adoption. Earlier attempts stopped at observation/artifact acquisition and did not establish settlement.
This does not establish fleet-wide activation or any historical migration result.

Audio is the first selected C3 repository. [Coordination PR #865](https://github.com/FS-GG/FS.GG.Coordination/pull/865)
delivered the `audio-v1` settlement profile at `a7ac52aa63b62fe192642b7f74006c6a0797a307`.
[`.github` PR #3919](https://github.com/FS-GG/.github/pull/3919) delivered explicit
Audio observation at `4ac2224cbefa55387ab1273a09ef23daf462628c`, and
[Audio PR #326](https://github.com/FS-GG/FS.GG.Audio/pull/326) delivered the disabled receiver at
`08a46576320b0043d43a0ce4eeffb3cd2e736e56`; all three passed their native required checks. The
[durable Audio receiver plan](https://github.com/FS-GG/FS.GG.Audio/blob/main/docs/roadmaps/v2-ordinary-adoption.md)
owns the remaining adoption boundaries. [Coordination CLI 0.1.3](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.3)
was published from reviewed source `eb464f5215d8b1696842eecd367027724add6923`; its protected
[publisher run](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36407941780) verified both feeds and
an anonymous install of package payload `c505159023f0740c885696ebe24f8e066e197d9cf09cc3e64050d4210bcd0cdb`.
The Audio `ordinary-v2` environment has an exact `main` branch policy and independently read-back
dedicated three-secret custody. [Activation PR #327](https://github.com/FS-GG/FS.GG.Audio/pull/327)
and source repairs [#330](https://github.com/FS-GG/FS.GG.Audio/pull/330),
[#331](https://github.com/FS-GG/FS.GG.Audio/pull/331) and
[#332](https://github.com/FS-GG/FS.GG.Audio/pull/332) passed native gates. The first three protected
attempts stopped before Authority mutation on artifact-byte, policy-ID and selected-check mismatches.
[Run `36413290713`](https://github.com/FS-GG/FS.GG.Audio/actions/runs/36413290713) on Audio main
`99207b52298352f16cd383b5d389b0dacb4b49ca` then returned `SettlementSucceeded` with digest
`9605af1e28ae79518018d03ca4fdfe5c33ab33c268153ec25d2220830fb9fc4e`. Independent Authority
readback found one complete entry under `refs/heads/fsgg/v2/journal/operation/9a` at
`a51c567cb5f738402d3ee6c76eff96ecae8ac49a`; the normal whole-run rerun returned
`SettlementAlreadyComplete` with the same digest, and the journal head stayed unchanged. This closes
the selected Audio adoption, not a fleet rollout or V1 migration. The owning
[Audio readback plan PR #333](https://github.com/FS-GG/FS.GG.Audio/pull/333) is merged on protected main.

Rendering's disabled receiver [PR #1361](https://github.com/FS-GG/FS.GG.Rendering/pull/1361)
merged at `cd8f9472fe44dfa648a1b293be535bfb8b5e68a6`; activation
[PR #1362](https://github.com/FS-GG/FS.GG.Rendering/pull/1362) merged at
`c04e509413bc4362c67b20cf93c85b5cf5929fa6` through native required checks. The
[published Coordination CLI 0.1.4](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.4)
asset is pinned to SHA-256 `10a51295db43e454b7692196533cceda48508165a8023dc98e87639be89f5c50`;
Rendering's `ordinary-v2` environment has dedicated three-secret custody and a `main`-only branch policy.
[Run `36432736356`](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/36432736356) on that
protected main returned `SettlementSucceeded` on attempt 1 and `SettlementAlreadyComplete` on the normal
whole-run attempt 2, both binding receipt `d02f30b55baa7a07ae956902e1c9e470d8468272490095553cb98e04661e29ce`.
Independent [Authority journal readback](https://github.com/FS-GG/FS.GG.Coordination.Authority/blob/00e1b26c63b150712d8cffbdc266b9711d3c2095/ordinary-v2/7eba1f934e32151d6c17f402afbca7325f4cba65c2836fc38d44f9a1d7b9e699.json)
found the matching completed entry and effect at operation/7e. Rendering's selected C3 adoption is complete;
this makes no fleet-wide or historical-migration claim.

Coordination itself is now adopted at the bounded clean-path C3 boundary. Public [CLI 0.1.7](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.7) binds source `fdfdcfc91e65f814b42d8c9e061fec0e6221a139`. Activation [#881](https://github.com/FS-GG/FS.GG.Coordination/pull/881) and observer repair [#883](https://github.com/FS-GG/FS.GG.Coordination/pull/883) culminated in protected source `4589cd685d2b62b588a120e37d4429dea5b61062`. [Settlement run 36484724833](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36484724833) returned `SettlementSucceeded` on attempt 1 and `SettlementAlreadyComplete` on the normal unchanged whole-workflow attempt 2, both binding receipt `e70ebd05401b4a229cfd07139ebd36baa45bd316185a4e3c258faaec73e067ef`. Independent Authority readback found operation/31 unchanged at `851f28b1e5f326c5015ba6f0a6d239700eac0177`, with one generation-3 completed entry and one effect. The earlier failed activation run stopped before credentials or effects. **The selected Coordination C3 adoption is complete.** Its [owning closure #884](https://github.com/FS-GG/FS.GG.Coordination/pull/884) is independently read back on protected main at `2ca91a6ed039a8d74cc4e00d2fa8e2af0b053ea9`. Historical upgrade and measured efficiency remain separate.

[Templates PR #635](https://github.com/FS-GG/FS.GG.Templates/pull/635) merged at
`f3a7cd6ab6f035d4ba335d03fdc367db6164793f` after public installed Xantham clean and retained
receiver proof and green protected composition, kit and materialization gates. Public Templates 0.14.0
already contained the 20-member payload; no republishing was required. FBX-05 is complete at this
receiver boundary; the observed upstream assessment remains unqualified.

FBX-07's second independent npm runtime witness merged in [Templates #645](https://github.com/FS-GG/FS.GG.Templates/pull/645) at `1e29a45ba26cddf900cb89064c8a89e2f3960f35`. The exact candidate [hosted composition 36473506070](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36473506070/job/109101353158) passed both `ansi-regex` and `strip-ansi` witnesses through the clean delivered lifecycle, Fable runtime, Chromium and candidate archive. This closes the bounded candidate-composition outcome. Public Templates 0.15.0 predates that source; successor publication, public installed adoption and the live upstream assessment remain separate.

The source and publication frontier was updated from protected evidence on **2026-09-27**. The broader
[code audit](reports/2026-09-24-v2-roadmap-code-audit.md) records its 2026-09-24 revisions, implementation boundaries,
limitations and corrections. The [dependency graph and parallel lanes](#91-dependencies-and-parallelism)
identify executable work; the [feature index](#98-feature-parts-and-subroadmap-index) links its owners.
The I1 closure projection below uses its later protected [installed qualification](operations/v2-ci-i1-installed-qualification.md).
The LEARN-01.1 source projection uses its later [protected merge](https://github.com/FS-GG/.github/pull/3845).

Native accepted receipts, merged source, immutable publication, installed adoption and live operation are
different evidence classes. Each claim below retains that distinction. Earlier progress narratives and
per-milestone SVG rows are preserved in the
[pre-reconciliation snapshot](https://github.com/FS-GG/.github/blob/d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md#0-current-progress-report).
They are historical observations, not another current task list.

**Mandatory closure update:** every Unified Roadmap item that reaches authoritative **Closed** or **Done** must
update this report after native readback. Include same-repository updates in the owning PR; cross-repository
projection follows asynchronously without delaying its delivery. The programme driver must not select the
next item on that item's dependency chain while the completed item is absent here. Independent ready lanes
may continue while the projection lands. Update the relevant current row and its evidence link;
retain detailed milestone history in the owning plan. CI ticks and intermediate checkpoints need no edit.

### 0.1 Stage progress

The active profile is **C0–C3**: C0 checked source delivery; C1 fresh `.github` V2 pilot; C2 one real
journey plus one normal rerun; C3 explicit-repository rollout with repair forward. `.github`, Audio,
Rendering, Net, Governance, Game, SDD, Templates and Coordination are accepted selected receivers. Other repositories join by selection, and
generated/scaffold defaults remain unchanged. The V0–V6 rows below preserve the superseded staged route
for history and must not be used as clean-start prerequisites.

| Stage | Verified position | Remaining exit and owner |
|---|---|---|
| **V0 — Simplified baseline and v2 binding** | Routine is the adopted source-delivery default. The selected clean-start ordinary-source profile is functionally accepted across nine receivers; governance routing/reuse and typed nonblocking observation are retained. | Complete usage, provider population and comparative economics remain insufficient. Future receivers, defaults and operation classes qualify under their own authority. |
| **V1 — Events, queue and incumbent fence** | GS2-07.1–.8 and GS2-08.1–.9 retain accepted native evidence. The exact 0.90.0 bridge and residual-writer retirement are reusable inputs. | Preserve the accepted fence and census through new source and receiver changes. Do not reopen completed units or infer Q4. |
| **I1 — Unattended CI credential interlude** | Milestones 01–06 are complete: source policy and Coordination 0.1.2 were delivered, the two-job path passed isolated hosted refusal, recovery and readback, and its exact receiver profile was selected for GS2-10. [Installed qualification](operations/v2-ci-i1-installed-qualification.md) records the evidence and bounded timing. | Production credential execution remains inactive at `OperatingV1`; GS2-10 candidate qualification and a separate protected `OpenV2` decision precede activation. The measured cohort does not establish a production bureaucracy percentage. |
| **V2 — Callable path and migration rehearsal** | V2-CALL-01.1–.5 complete at their bounded callable boundary. Package 0.1.1, native isolated effect/recovery, cleanup and discovery handoff are evidenced. GS2-09.1–.6 accept pure migration contracts. [GS2-09.7 migration source](https://github.com/FS-GG/FS.GG.Coordination/pull/847), [claim/event parser source](https://github.com/FS-GG/FS.GG.Coordination/pull/852), [host source](https://github.com/FS-GG/.github/pull/3879), [direct-session source](https://github.com/FS-GG/FS.GG.Coordination/pull/848), and [GS2-09.9 source](https://github.com/FS-GG/FS.GG.Coordination/pull/846) are merged. Protected OperatingV1 admission genesis is installed with exact typed readback. | GS2-09.9 still needs versioned operator/contract/gate rotation and protected Q3/Q6 acceptance. Coordination then qualifies complete provider capture, migration execution and isolated representative rehearsal (.7), followed by independent omission/idempotency proof and parent closure (.8). Merged parsers lack delivery/intake/done producers and canonical claim/event authority. Post-genesis append and copy-specific effect authority remain separate joins. |
| **V3 — Exact candidate and receivers** | Not accepted. Existing publication, provider, tool and receiver evidence can support preparation. | GS2-10 freezes exact inputs, qualifies the complete candidate and clean/retained receivers, rehearses the cutover, and closes concurrent changes. |
| **V4 — Closed switch** | Not entered by this audit. | GS2-11–12: authorized freeze/drain, exact switch, verification, and executable rollback while still closed. |
| **V5 — Open and ordinary use** | No fleet production `OpenV2` acceptance identified. The synthetic callable target is not the fleet. | GS2-13 owns irreversible open, permanent v1 fence, real ordinary journeys and `ObservingV2`. |
| **V6 — Observation and retirement** | R5's first-ten cutoff is fixed at `2026-09-29T07:15:35Z`; repair follow-ups cover all 15 enrolled originals through that cutoff. The evaluator remains insufficient. | Historical GS2-14/Q10 stays superseded for the clean-start route. Future economics work must declare its own valid comparison without rewriting this frozen cohort. |
| **E0/E1 and O0–O3** | Selected single-host O0–O3 is accepted; Choreo C0–C6 is source/formal-qualified. | Reuse these foundations. Exact installed inclusion of later Choreo fixes and comparative-value claims require their own evidence; neither blocks migration source work. |
| **F0–F5 / LEARN-01** | LEARN-01.1's baseline is source-delivered through [PR #3845](https://github.com/FS-GG/.github/pull/3845). LEARN-01.2's analysis, durable facts, capture/export and native-source binding are source-delivered through [#3916](https://github.com/FS-GG/.github/pull/3916), [#3927](https://github.com/FS-GG/.github/pull/3927), [#3940](https://github.com/FS-GG/.github/pull/3940) and [#3986](https://github.com/FS-GG/.github/pull/3986); Coordination [#876](https://github.com/FS-GG/FS.GG.Coordination/pull/876) delivered installer-v2 source. LEARN-01.3 fixed-profile proposal, durable treatment, execution and observation source is closed through Coordination [#882](https://github.com/FS-GG/FS.GG.Coordination/pull/882), [#885](https://github.com/FS-GG/FS.GG.Coordination/pull/885) and [#886](https://github.com/FS-GG/FS.GG.Coordination/pull/886) at `513acdbddcb52b6f3e219619605de458070e033f`. Native PostgreSQL and formal correspondence qualify the source. The owner-reported installed fixed diagnostic proves only its historical version/login scope. LEARN's inactive Host 0.2.0 and manager staging has no installed native receipt or observed native capability, and 0.2.0 predates #3986's `capture/2` hardening. Capture, census/shared-cost completeness, enrollment, comparative result and live experiment remain unestablished. | Publish/read back current `capture/2` bytes, then deploy the Main-independent collector container. Its authorized receiver owner issues the genuine config/2 collector role/grant and private secret locally; no external issuer is required. Qualify devcontainer capture, restart/recovery and cleanup with Main unavailable before W6/enrollment. After replacement acceptance and retained-record inventory, preserve required records, then retire obsolete Main services/routing/credentials. LEARN-01.5 still needs the locked dataset or truthful stopped-window result. |

#### Product source milestone projection

SC2C-01.1 and SC2C-01.2 are source delivered in the private [SC2 Client PR #2](https://github.com/FS-GG/FS.GG.SC2.Client/pull/2) and [PR #3](https://github.com/FS-GG/FS.GG.SC2.Client/pull/3), merged at `c90c07fc32c9c17374ecfef9d1df1b019e973f2a` and `d17e6b4943d112b127ce8fb8d0f95ff53450e0cb`. SC2C-01.3a's live contracts and serialized native session owner are source delivered in private [PR #4](https://github.com/FS-GG/FS.GG.SC2.Client/pull/4), merged at `dad6d257af488f6668f8f2dd076b2dde5899ce03`; its exact-main [`verify`](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36314544918) passed. SC2C-01.3b's injected-owner paired gateway core is source delivered in private [PR #5](https://github.com/FS-GG/FS.GG.SC2.Client/pull/5), merged at `819e1bb535324fcd3fb5c352ad625267d06af932`; its exact-main [`verify`](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36316009310) passed. SC2C-01.3c's executable gateway and browser journey are source delivered in [PR #6](https://github.com/FS-GG/FS.GG.SC2.Client/pull/6), merged at `7a3f6eaec37fa4da071cb1251ebc2525c79b9e60`; its exact-main [`verify`](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36324212682) passed. The fail-closed .3d preflight merged in [PR #7](https://github.com/FS-GG/FS.GG.SC2.Client/pull/7). Native compatibility and reconnect command repair then merged in [PR #8](https://github.com/FS-GG/FS.GG.SC2.Client/pull/8) at `c867fef0fe24891a5478a0f68f33aba16ce449c7`; [exact-main verification 36482338876](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36482338876) passed. The owner rebuilt that exact clean source and repeated the genuine SC2 4.10 native journey: pointer and keyboard movement, local and native refusal, changed session/epoch and rearm after reconnect, owned cleanup, and response loss after exactly one upstream write with no repeat submission. Independent readback verified the integrated receipt SHA-256 `074a5ef70e05511d282b230142018797ea9d19adee40abd560f62791a47a5e83` and four private proof commitments. **SC2C-01.3d and the bounded first playable SC2C-01.3 outcome are complete.** The owning closure [PR #9](https://github.com/FS-GG/FS.GG.SC2.Client/pull/9) merged at `34182a57940493d3083e24413d9c34219876278b`; its [integrated evidence](https://github.com/FS-GG/FS.GG.SC2.Client/blob/34182a57940493d3083e24413d9c34219876278b/docs/SC2C-01.3d-integrated-evidence.md) retains the accepted runtime source and artifact separately. Game assets and raw evidence remain private; product publication and broader installed adoption remain separate.


**SC2C-01.4a–.4d are source/native accepted at the bounded worker-squad boundary.** [SC2 Client #10](https://github.com/FS-GG/FS.GG.SC2.Client/pull/10) merged at `4b834ff8c20450f7f327c05a19ec43cf1d608155`; its [exact-main verification](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36507053735) passed. Fresh pointer and keyboard participant sessions on that clean merged source each completed 11 successful native commands through the actual WASM controller: queued progression toward both Move targets, replacement, Stop/Hold, Gather and resource observation, fog scouting and combat against the exact visible selected target. Groups, minimap, remapping and zero-write refusal guards passed. A separate post-write-loss session retained one durable Unknown, revoked authority, submitted no automatic replay, and completed a distinct explicit Stop after fresh rearm. All 13 qualification coverage checks required by the [owning tactical plan](https://github.com/FS-GG/FS.GG.SC2.Client/blob/4b834ff8c20450f7f327c05a19ec43cf1d608155/docs/SC2C-01.4-plan.md) passed with no omissions; merged receipt SHA-256 is `d2b6f84f89ac0c2c075650c399a583d25f5f62886ca287875cb8fcfa9aac24eb`. Independent readback joined all 446 pointer, 412 keyboard and 37 fault binary messages and their durable command digests. Owned processes exited; licensed assets and raw evidence remain private. Full .4 remains open for .4e production/placement and .4f the complete representative scenario. Publication, installed adoption and realtime coverage remain separate.

**SC2C-01.4e production/placement source and exact merged-source native acceptance are complete.** [SC2 Client #11](https://github.com/FS-GG/FS.GG.SC2.Client/pull/11) merged at `0c2243cba9245c4788fb8a4c302807730b0f8fac`, protected tree `9dd089e224cf5181379af91520bede5d9b51de70`, exactly equal the qualified candidate; its [candidate hosted verification](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36576756760) passed. Fresh SC2 4.10 qualification on that source used the same pointer and keyboard native sessions committed by the [owning integrated evidence](https://github.com/FS-GG/FS.GG.SC2.Client/blob/83a770b9c9ec6b595dd173ec0dcf8ce5248b640b/docs/SC2C-01.4e-integrated-evidence.md) as `c10ae40d5782a1b6c2c6e809378b085e02924f87a80a7bc4abdb5c1a77a016f0` and `3393fb59106cdd46dba65e703252e9f919ab34533bd968cb94bfc0fce9fd29f9`. They completed Supply Depot, Barracks and two-Marine production through the public browser/WASM path while retaining placement freshness, cancellation, refusal, Pause-publication and full-UInt64 guards. Two production after-write-loss cases and one tactical case each proved one durable Unknown after exactly one upstream Action write, no replay, revoked authority, distinct fresh-session success and disposed/cleared cleanup. Independent readback matched all five native receipts and all 23 final-wrapper bindings (`408124420cfebdf664e8736a10202c54852177c66d9c932d96cb5f1ba65b7cd0`); tactical receipt `4525ad8e7831dc0375cb1f8b9358b32c2581964e82cf067d1056ef69d880c1a6` and production receipt `bf7f73486df33f052b57c3b328ce3446374686131394905c07374f4fc3923c2b` both pass with empty omissions. The additive qualifier binding retains the served bundle and byte-identical 54-file distribution separately; earlier failures and raw proofs remain history. Owning closure [PR #12](https://github.com/FS-GG/FS.GG.SC2.Client/pull/12) merged at protected `83a770b9c9ec6b595dd173ec0dcf8ce5248b640b`, tree `5ba5e51eea8719f3758886b48ff228d20ca4ca6f`; its fresh protected [verification run `36593424610`](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36593424610) passed all steps. That closure changes only the production browser guard test and two owning documents relative to qualified product source `0c2243cb`; product paths and native evidence are unchanged. Full .4 and .4f, publication, installed adoption and realtime coverage remain open. This later completion does not change the frozen R5 first-ten cutoff, historical 12/15 delivery count, or insufficient economics/unknown usage.

**SC2C-01.4f and bounded SC2C-01.4 are complete.** [SC2 Client #13](https://github.com/FS-GG/FS.GG.SC2.Client/pull/13) merged the representative qualifier and sanitized native evidence at protected `a40b564f3742a19872712d43137ee9d5457eb303`; its exact-head [verify run 36669293779](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36669293779) passed 41 browser cases. Fresh genuine SC2 4.10.0.75689 pointer and keyboard sessions on the pinned Simple64 map joined tactical, production and public-control evidence with no omissions. Protected readback matched the [receipt](https://github.com/FS-GG/FS.GG.SC2.Client/blob/a40b564f3742a19872712d43137ee9d5457eb303/docs/SC2C-01.4f-representative-receipt.json) SHA-256 `ef255be859156cb39ad6434f37208e6d11b1104e1f1b3febdf68c995bf65ad43` and the [owning plan](https://github.com/FS-GG/FS.GG.SC2.Client/blob/a40b564f3742a19872712d43137ee9d5457eb303/docs/SC2C-01.4-plan.md) closure. This qualifies the declared Terran/Simple64 scenario only; wider races/maps, .5 recovery, .6 module workflow and .7 publication remain open. Licensed assets and raw traces stay private.

**SC2C-01.5f and bounded SC2C-01.5 are complete.** [SC2 Client #16](https://github.com/FS-GG/FS.GG.SC2.Client/pull/16) merged at `b94dd753bfd0e41ba1e3b22690c7f7a25d0ac18c` from accepted source `ad50425dcdcc526215fce1904e33015369b2ac69`, protected tree `6ea5ef7029d5369b6f52481202ff90c0a34b4581`; [exact-source verification `36734647781`](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36734647781) passed. Fresh genuine SC2 4.10.0.75689 pointer commit and keyboard abort journeys on pinned Simple64 each recovered a new socket and generation-2 authority before a real query and command. Both returned an accepted `Sc2Result` with action result `1`, made exactly one native write, durably completed and cleaned up without dropped audit records or writer failure. The accepted source differs from qualified candidate `e20d37488ff71e019a9af7227302107dfc46f3c4` only by four assertions in two browser test files; product source is unchanged. Independent readback verified private evidence summary SHA-256 `0121ab824b1fb4eee2ee1b30453fdb3b7138e74d111bad80a385c9c1d15373cc`. This closes the bounded query-result to newer-authority publication and command-admission seam. Licensed assets and raw traces remain private. Wider races/maps and multiplayer, SC2C-01.6 custom-module authoring and recordings, and SC2C-01.7 publication, installation and platform qualification remain open.

FOURD-01.1 and FOURD-01.2 are source delivered in the private [FourD PR #1](https://github.com/FS-GG/FS.GG.FourD/pull/1) and [PR #2](https://github.com/FS-GG/FS.GG.FourD/pull/2), merged at `5661d85ce62264b84a56a4fb32716cd1350408ff` and `8096fd19431a74fff7366217142908c4bf226496`. FOURD-01.3's deterministic tactical encounter was delivered in private [PR #3](https://github.com/FS-GG/FS.GG.FourD/pull/3), followed by the browser-readiness repair in [PR #4](https://github.com/FS-GG/FS.GG.FourD/pull/4). FOURD-01.4's browser teaching and local evaluation source merged in [PR #5](https://github.com/FS-GG/FS.GG.FourD/pull/5) at `f3b16d748b67ee665c8ca521ebf93aa9da607d45`; its exact-main [`verify`](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36320371608) passed. The bounded .5a-2 finite-wall breach, cross-runtime query and replay qualification merged in [PR #8](https://github.com/FS-GG/FS.GG.FourD/pull/8) at `9b3dfad568e31ee5433c26634bb7acdbe63b72e1`; its [exact-main verify](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36453144626) passed. The .5a-3 active-opponent turn and replay qualification merged in [PR #9](https://github.com/FS-GG/FS.GG.FourD/pull/9) at `f9243e02889364e6f465b59bdb6f6a6ba142c809`; its [exact-main verify](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36469755250) passed, including 21 browser cases and .NET/Fable/Node parity. The .5a-4 native-control browser restart and active-opponent recovery case merged in [PR #10](https://github.com/FS-GG/FS.GG.FourD/pull/10) at `9109bfe061ece3637cad8e3fcafcfc6643458b43`; its [exact-main verify](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36473282583) passed with 22 browser cases and cross-runtime parity. This closes the bounded .5a technical source cases. Consenting-player evaluation (.4-E), full .5–.6, publication and installed adoption remain open. The September28 [Three Design Directions Version2](roadmaps/2026-09-28-four-dimensional-skirmish-design-v2.md) is now incorporated for bounded Commitment-versus-Pressure prototyping, with Firelanes deferred. Facing, hidden enemy readiness, automatic Guard/Ambush/Hold and contact stops are selected design requirements; the default entry remains `fourd-tactics-v1`, with separately qualified Commitment/Pressure and save/2 available through explicit opt-in. The source plan has separate geometry/information, reducer, browser/replay, technical comparison and evaluation-pack outcomes; no human comparison or revised runtime completion is inferred.

**FOURD-01.V2.1–.V2.3 are source delivered.** [FourD #11](https://github.com/FS-GG/FS.GG.FourD/pull/11) merged at `b9126588db10469ef51b7cec7433567db0471485` from exact qualified head `98d44564c59e68c6a6ba95ebcd3f398e5a7cca70`; fresh protected readback verified tree `c87f9e98efa1d8d91a683d1cb327546f8e78c867` equals the qualified tree and the three owning outcomes are checked. The shared foundation implements six facings, exact ramp/sight/cover geometry, private readiness projections, automatic policies and bounded contact stops; Commitment and Pressure each implement complete six-round encounters. [Native verify 36525153306](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36525153306) passed .NET/Fable/Node fixture, workload, breach and opponent parity and all 22 retained Chromium journeys. Fresh comparison found all 30 prior v1 output fields unchanged. The [delivered owning plan](https://github.com/FS-GG/FS.GG.FourD/blob/main/docs/FOURD-01.design-v2.md) records these pure-core outcomes; browser/comparison delivery follows below and evaluation task wiring remains separate. The joined evaluation protocol/scorer is preparation only; no unfamiliar-player evidence or winning-design claim follows. These three prospectively selected substantive originals formed the earlier **5/10** R5 tally; usage, population and comparative economics remain insufficient. Current selection and proofs are in the [cohort input](reports/evidence/2026-09-07-routine-route-cohort.json).

**FOURD-01.V2.4 and .V2.5 are source delivered.** [FourD #12](https://github.com/FS-GG/FS.GG.FourD/pull/12) merged at `7739f9bd8830e09b4644efa7e45b2cb3ac61b81f` from exact head `364cecbd96fe53f3144fb1ce138b9fe87392d697`. Protected readback at `2026-09-29T06:35:44Z` verified tree `dff90af27b8218b872c98b9d0ce0ce8d65cc6979` equals the qualified tree and both owning outcomes are checked. [Native verify](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36531113993) passed full .NET/Fable/Node parity, 132 comparison transcripts, the ten-actor envelope and 28 Chromium journeys. The explicit opt-in browser provides private AI, public previews, facing/policies, contact Continue/Stop and exact save/2 recovery. Clean-source evidence measures cold start, initialized queries, reload, load and continuation separately. V1 stays the default. The source-ready evaluation-pack delivery follows below; consenting player evidence, publication and installed adoption remain open. These two whole originals formed the earlier **7/10** tally.

**FOURD-01.V2.6 is source delivered.** [FourD #13](https://github.com/FS-GG/FS.GG.FourD/pull/13) merged at `b7afd577b0669d8a25b2e4b8ecc52c80d581c2e9` from exact head `15c2e83af27a21c242eaabc9d2f5115bce809723`. Protected readback at `2026-09-29T07:10:28Z` verified the owning checked outcome and tree `c214abf8941bf58430194c876cb7eaf0ec944e46` equals the qualified tree. [Native verify](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36534310893) passed full .NET/Fable/Node qualification and 30 Chromium journeys. The pack binds actual emitted assets, rules/map/first side, pre-feedback public contact and accepted movement to actual cover. Synthetic rehearsals remain incomplete where required evidence is absent and are excluded from human acceptance. All six amendment source outcomes are delivered; consenting paired-player evaluation and design selection remain open. This formed the earlier **8/10** tally; the additional apps below reach the fixed ten-item cutoff.

**FOURD-01.6 product and retained-save preparation is source delivered.** [FourD #14](https://github.com/FS-GG/FS.GG.FourD/pull/14) merged at `aced62aa72bee5509fc70cc96089cab2477cca0e` from qualified head `6ee97fb968296caea03f9dbb34e8cf597062e8f2`; independent protected readback verified equal tree `73a04ae69814329eeaf51c197f3c784a852ef12f`. [Native qualification 36549439415](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36549439415) passed .NET/Fable/Node parity, all 30 existing browser checks, 3 isolated-archive checks and 4 retained-save checks. A dedicated product entry starts without qualification fixtures; a static candidate contains the actual import closure and source/tool/rules/map identity. Two locked builds matched normalized payload `sha256:d8b59da03fa54630b2cf6aeef1c57df86d1d0be42a1fe77656c0c38255418947`; archive identity is separately source-bound. Saved bytes from protected `b7afd577` produce fixed old-build continuations and preserve same-origin stored bytes until explicit save. The [owning preparation window](https://github.com/FS-GG/FS.GG.FourD/blob/aced62aa72bee5509fc70cc96089cab2477cca0e/docs/FOURD-01.6.md) retains v1 default and both explicit candidates. Original .6, full .5, human comparison/design selection, publication and installed adoption remain open; this partial window adds no completed cohort original.

**FOURD-01.6 private distribution and fresh technical consumption are qualified.** [FourD #15](https://github.com/FS-GG/FS.GG.FourD/pull/15) delivered source at `abe58b3aec2c6c7b7a4914aab3b5b2d37e453da9`, with equal candidate/protected tree `b33ab223763231941f6dcb46862a9474eef08b70`. The exact protected-main [verify run36559749952, attempt1](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36559749952) passed and retained private [artifact11029645675](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36559749952/artifacts/11029645675), expiring `2026-12-28T11:07:06Z`. Immutable-ID download matched the API wrapper digest `6d8fcb84b1b7806c449e92d5d7ed611900f6e6f94da435d4637b2f620bdf3cad`; the separately checked inner tar is `c827bac084ddc5819b3ead040458c8b025857fd2274b9e3d61d3dc2aafda5199`, and its protected-source manifest binds normalized payload `sha256:ebde8a29d764d9c5f7f826404c3135fd49298554a1e447a8272effb9fc03759a`. The downloaded-only runtime passed three product and five retained-save/origin browser checks. Earlier #14's candidate run retained no hosted archive. Existing repository readers require authenticated access to this exact time-bounded artifact; no public host or access grant is added. V1 stays default and the revised candidates stay opt-in. Genuine consenting-player comparison, full original .5/.6, permanent publication and installed adoption remain open; no completed cohort original or economic result is added.

**FOURD-01 bounded automated V2 technical handoff is closed; original .5 and .6 remain open.**
[FourD #18](https://github.com/FS-GG/FS.GG.FourD/pull/18) merged the product test repair at protected
`5b6e313ba6298950edcef46ecff3e69038314474`. Its [exact-main run 36676838988](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36676838988)
passed and retained [artifact 11080637269](https://github.com/FS-GG/FS.GG.FourD/actions/runs/36676838988/artifacts/11080637269).
Independent download verified wrapper SHA-256 `eadc15885fcceaf8e7964b7ec335b6e2892d80c96a7cd4fd350b26dae11ac1e6`,
inner tar `590abfa1ed862081cbc17dcff86712cf9514f844fd3cd4624712b5ed342a73d8`, and
payload `sha256:012af80fca641a38016cbe28260f86fac31c388424eed73eaaf0525e4c27a233`.
Seven extracted product/profile/evaluation checks and five retained-save/origin checks passed; one
desktop touch case was an expected skip. [FourD #19](https://github.com/FS-GG/FS.GG.FourD/pull/19)
recorded the protected evidence at `931869236b4253b1489c4e8095a36017709194e2`.
The 2026-09-29 full-V2 amendment accepts this automated technical scope without a player study.
Original FOURD-01.5 still needs its player-derived usability outcome, and original .6 retains that
prerequisite. No player preference, physical-device result, permanent publication or installed adoption
is claimed.

**LEARN-01.4 W1–W3 source and served-candidate preparation is delivered; the installed window remains open.** Coordination [#887](https://github.com/FS-GG/FS.GG.Coordination/pull/887) delivered the disabled source at `c6e378e1d00e49eda5ee372bc50cd294460be04a`. [#888](https://github.com/FS-GG/FS.GG.Coordination/pull/888) delivered the owning source/artifact evidence at protected `92669c866006dfb228f3034b8dd9b20842601c6c`, equal qualified tree `f925679c54d2bc1dbbf5612a1374cafa94c5a02a`; native [bootstrap](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36546191857) and [coherent qualification](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36546191805) passed. Exact-main [Host 36541708227](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36541708227) and [runner 36541711540](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36541711540) candidates passed independent downloaded-byte/native verification and both 11-mutation suites. The [served-candidate evidence](https://github.com/FS-GG/FS.GG.Coordination/blob/92669c866006dfb228f3034b8dd9b20842601c6c/docs/roadmaps/evidence/learn-01.4-served-candidates.md) pins their immutable identities. These are qualified Actions-storage candidates; release promotion, Main/runner installation, approved private configuration/authentication, provider support, complete capture/usage and actual comparison enrollment remain unproven. Whole .4 and its declared experimental endpoint remain open; the fixed R5 cutoff is unchanged.

**LEARN-01.4 W4 owner-assessment source is delivered.** [.github #3968](https://github.com/FS-GG/.github/pull/3968) merged at protected `dda80cf68f2a6179810544d194d2a6d182f773d2`, equal candidate `01e5d1fd1c74626cb85824335752027a98965870` tree `f1265896e8343bc536e52f53f63176bf2d48212b`. The existing [analyzer](../tools/learn-01-analysis.py) acquires the closed v1 export directly from the executable-pinned protected process and assesses one original/repository/window selection before assignment. Its selected-record digest excludes unrelated store traffic and export timestamps. It refuses imported snapshots, malformed identifiers/relations, conflicting assignments and executable/capture provenance drift; explicitly foreign scope is excluded and missing retained scope remains unknown. All41 analyzer checks pass, including actual protected-process CLI controls. Unborn originals do not need future terminal counters. The [owner-input contract](research/learn-01-observation-contract.md#bounded-pre-admission-owner-assessment) keeps `operationalReady=false`: protected window authority, independent prospective dispatch census/shared allocation, supported root/descendant capability and exact native delivery provenance remain missing. This is assessment source preparation, not a readiness adapter, installed receiver or enrolled experiment.

**LEARN-01.4 W5 direct-runner exact-version source is delivered.** [Coordination #889](https://github.com/FS-GG/FS.GG.Coordination/pull/889) merged at protected `424f0c7bcba19e512ac425c2f64a56d53bae8d43` from qualified candidate `e1b7725272fc82f57a8387c6c7f9c260cc135721`; independent readback verified equal tree `3db7df1390fbf391324f88f61ec05809f47e5c3d`. Native bootstrap and complete coherent qualification passed. Optional `--expected-codex-version 0.158.0` preserves exact authentication/capability/launch and executable/config drift fences; omission retains0.154.0. The [protected-main runner candidate](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36558581557) passed. Root independently downloaded candidate11029151807 and verification11029021896 by immutable ID, verified both API outer digests, ran native fresh `verify-served`, and matched the generated receipt byte-for-byte to hosted verification (`sha256:1e0285c476b3683c7d5e9fec5763052f9cb6341158a56ca4ac165abf88e7eb06`). Inner archive `sha256:373685f2c8aea25e5ce3f2006356571ded0241a653f323cdd0af40216bcd7464` and runner payload `sha256:20aec343a74b26a9017382e3aeaf8bd0899fd767893907a8b6cd14816a9d0283` remain distinct. Work-main independently qualified those downloaded bytes and reported actual ordinary Home readiness at11:00:49Z using native ELF `sha256:167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9` and exact0.158.0: authenticated subscription provenance, ready lifecycle, correlated command ID and absent provider session. A separate11:03:16Z response advertised resume support; no model/resume journey was exercised. This direct-runner receiver result remains work-main-observed and does not establish installed Learning acceptance. Old archives cannot qualify the delivered repair.

**LEARN-01.4 W5 Host forwarding source is delivered.** [Coordination #890](https://github.com/FS-GG/FS.GG.Coordination/pull/890) merged11:54:27Z at protected `5fe45df2ba70bc5e12aa3a0518dc145f7e632941`; root independently fetched protected main and verified tree `b0e4979e6d9bbd0c0e52a9da710557b0e02781d4` equals qualified candidate `75413b5390411175e3655559304b3d6b89bbc782`. Native bootstrap and complete coherent qualification passed. Trusted local-executor Host configuration now forwards the optional canonical exact Codex version; omission preserves the runner default. Full106 Host tests and root actual Host/runner/provider controls passed. The [fresh protected Host candidate run](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36564698130) succeeded on that exact protected source. Candidate11031147563 and verification11031267447 were independently downloaded by immutable ID; both API outer digests matched. Exact protected helpers passed `verify` and fresh `verify-served`; root checked the proof manifest, reran prepared verification and confirmed generated/hosted receipt equality (`sha256:d440212ab9e7573ecea91223306894e3776a939beb0ebe7744d493335a290469`). Inner archive `sha256:1fa9219a2645cee64f974997d98cdbd59330269f4d35b37b60cf68a774081ac1` and actual Host payload `sha256:d48f9d51e96e3493c4eafb06656e33e2547440b81736e91fbca5ee609f0303a5` are qualified for this forwarding-only source; the older Host archive does not qualify it. The actual served Host surface has no readiness-only consumer invocation: production `serve` opens PostgreSQL and startup-pause state. A separate selected non-execution diagnostic source window must preserve that boundary before an integrated receiver probe. Source delivery does not certify installed adoption, protected custody, an operational readiness adapter or whole .4. The [owning plan](https://github.com/FS-GG/FS.GG.Coordination/blob/5fe45df2ba70bc5e12aa3a0518dc145f7e632941/docs/roadmaps/learn-01-installed-window.md) retains those gates.

**LEARN-01.4 W7 bounded native delivery capture technical qualification is complete.** Source [.github #3973](https://github.com/FS-GG/.github/pull/3973) delivered at `e575c517c566155bcc21ae5846d0697b7af8a677`; dependency/package repair [#3974](https://github.com/FS-GG/.github/pull/3974) merged at protected `55260447d5acea2278f89102c022ddb7ebf061b5`, equal qualified candidate `666d7a39ee7db7e4483ce4661270c40c1fb09d6b` tree `341d99f4ab71a6549cfbfa0447f4fbbdbd0676f0`. All native checks passed before delivery, including fresh [package](https://github.com/FS-GG/.github/actions/runs/36567198344), [engine](https://github.com/FS-GG/.github/actions/runs/36567198146), change-completeness and coherent checks. The earlier missing transitive lock closure (`NU1004`) and package24/25 exact-file-list failure remain failures. The repair adds the missing project dependency and admits exactly its DLL/PDB/XML, explicitly requiring the runtime DLL. Two clean local package builds produced identical normalized payloads. The protected capture command reads one fixed GitHub PR endpoint through the counted30-second,4 MiB,non-redirecting transport. Existing collector/grant and repository scope are mandatory; immutable primary response, candidate and envelope bytes precede receipt submission and are revalidated without recapture. An open PR's speculative merge SHA never becomes protected merge identity. Compatible v1 export and explicit opt-in bounded v2 export passed controlled source verification, including42 analyzer,32 Host,1021 Core and558 CLI tests. The analyzer reports `native-state-verified/original-window-binding-unverified`, with `operationalReady=false`. No live grant, credential installation, capture, original/window binding, census/allocation or installed experiment is supplied. Those operation inputs remain follow-up under the [full acceptance amendment](#full-v2-acceptance-amendment--2026-09-29). The [capture contract](research/learn-01-observation-contract.md#protected-native-delivery-source-readback) owns this boundary.

**LEARN-01.4 W8 bounded diagnostic technical qualification is complete.** [Coordination #891](https://github.com/FS-GG/FS.GG.Coordination/pull/891) merged at protected `c6fff6590055672174037b313642ade1502b71c4`, equal qualified candidate `ef7185ba80510833176a505ea2a169b2ad49c8ce` tree `4034a5a8adbbeebdbceb5e400df5a810b4697fb5`. Native bootstrap and complete coherent qualification passed. The closed Host-to-runner diagnostic uses the shared production version/login probe before execution-runtime construction; exact executable/configuration hashes, closed bounded correlation, default0.154.0 and explicit canonical version selection remain enforced. Eight actual diagnostic controls,20 provider controls and114 Host tests passed. It neither creates a database connection nor dispatches a model turn or execution intent. Fresh exact-protected [Host36572192445](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36572192445) and [runner36572197020](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36572197020) workflows succeeded. Immutable candidate/verification artifacts11036570454/11036645379 and11036140315/11035321485 were independently downloaded and matched API digests; exact protected helpers passed prepared and fresh native served verification. Root checked all36 proof hashes and independently reran native verification, matching both hosted receipts byte-for-byte (`d1432634346e24c9727114522a690d410ab80c1ab510eb89b8757e699eb69071` and `8ff510c6835d893a0a5589702d22339a8e0cce881e7d9aa73c95ba3c2851adb2`). The actual downloaded Host (`936dbc95c82430194367c7743a42b27fe4351b1dc5342cdf87630c70e1d4397c`) and runner (`7197fa482355b167a5a733d6458327477eefc6af1a53df8605446f2ea5bbfcae`) then passed one controlled receiver invocation: explicit0.158.0 and default0.154.0 matched; mismatch performed one version call and zero login calls; terminal-LF version refused before any provider invocation. All23 receiver output hashes, selected executables, parent environment and six runtime-root sentinels remained unchanged. Receiver result SHA-256 `9a007fb1ffdc78183c48eb3eca89dc47398e4df90b7954c61fcbccbea2f7119a` binds this pair. Authentication was observed only through a generated controlled provider sentinel. Genuine provider/private Home authentication, model/effort support, resume execution, installation, admission, grants and third-party filesystem behavior are not established. The optional old-runner served case was not selected; existing source-level unsupported-command controls remain separate. All actual attempts returned not-configured telemetry, with unknown usage. This closes the selected bounded technical diagnostic gate under the full acceptance amendment; whole installed .4 is unchanged. Older424 runner/5fe Host archives do not qualify W8. The [owning installed-window plan](https://github.com/FS-GG/FS.GG.Coordination/blob/c6fff6590055672174037b313642ade1502b71c4/docs/roadmaps/learn-01-installed-window.md) retains these boundaries. Host exact-version forwarding and its fresh forwarding-only artifact qualification are already delivered through#890. W6 composition still requires authentic retained authority/census/allocation/capability inputs. Whole .4 remains follow-up operation/research work, with no new completed original or change to the frozen R5 cohort.

**TODO-01.1, TTT-01.1, SNAKE-01.1 and HELLO-01.1 are source delivered.** Each whole app was prospectively enrolled at `2026-09-29T06:31:33Z` before planning or implementation outcomes under the [enrollment scope](roadmaps/2026-09-29-r5-additional-app-enrollment.md). [Templates #648](https://github.com/FS-GG/FS.GG.Templates/pull/648) merged at `ba760d7725fe64b9311c20b57a05ef8630533804` from exact head `1225ffb33f2f74ed0c79413299328d055154d295`. Protected readback at `2026-09-29T07:15:35Z` verified all four owning checked outcomes and tree `b79dbd9513ea3c0b77ab8b9558003e05addee5cd` equals the qualified tree. Required [composition](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36534240755), kit and receiver checks passed; [app qualification](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36534240553) passed 18 domain checks and 14 actual Chromium cases. Todo supports task management and local persistence; tic-tac-toe supports complete two-player games; Snake supports timer, food/growth, score, collision, pause and restart; Hello World runs through an accessible real module entry. These opt-in source samples do not change installed template or lifecycle defaults. The actual [ordinary-V2 settlement](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36535499830) on this new Templates merge passed with sealed CLI 0.1.6 and receipt `b13d3ff911040fe086ae0f0e179ffec67f836ee6553a015a3744c883d1696e4a`. Independent exact-main observer verification matched the native preflight byte-for-byte (SHA-256 `a5868c82008cc39933118675a82f9936a4e6d839efbec54dd5dad3f0dcf5e73e`); Authority readback found one completed generation-3 operation and one matching effect at `operation/cb`, head `9600b3a971cc2961b1f43ed5a4ab5f15c0795c25`.

**The R5 ten-completed-item window has reached its fixed cutoff at `2026-09-29T07:15:35Z`.** Twelve prospectively enrolled originals have delivered; the predeclared completion-time/canonical-original ordering retains the first ten: LEARN-01.3, BARC-01.4, FOURD-01.V2.1–.V2.6, HELLO-01.1 and SNAKE-01.1. The four apps share one readback instant, so canonical-original ordering resolves the tie; TODO-01.1 and TTT-01.1 remain delivered and enrolled outside the first-ten count. Repair follow-ups cover all 15 selected originals through the cutoff, including the three pending originals SC2C-01.4e, BARC-01.5 and LEARN-01.4. Usage, repair cost and provider-population coverage remain incomplete; the evaluator therefore reports **insufficient**, with no efficiency, bureaucracy or release-readiness claim.

**LEARN-01.4 source is delivered after the frozen R5 cutoff.** [Coordination #887](https://github.com/FS-GG/FS.GG.Coordination/pull/887)
merged at `c6e378e1d00e49eda5ee372bc50cd294460be04a`; protected readback verified tree
`344a4bd42b2938ce5cc062246bd36bdac7637e53` equals the qualified tree. This is source delivery only.
Installed provider operation, authentication, capture, complete usage/cost attribution and the full .4
acceptance remain pending. The later merge does not alter the frozen `2026-09-29T07:15:35Z` cohort or turn
LEARN-01.4 into a delivered original inside that as-of record.

[Coordination #892](https://github.com/FS-GG/FS.GG.Coordination/pull/892) is protected at
`cd1d666e575b9bbb7b995ae533b0d0d18773679f`, tree `4f8e2365ded46d4da6a09e8d39ed6e36668f0c81`,
and supplies the current durable owning pointer for the bounded technical work. It does not establish
installation, authentic provider operation, complete usage/cost attribution or the full .4 experiment.

BARC-01.2's correlated native producer and local real-engine proof merged into the FS-GG staging fork in [HighBarV3 PR #1](https://github.com/FS-GG/HighBarV3/pull/1) at `81016ee38b123a490e6ff06037cdc2f56a105a96`, with the same tree as qualified candidate `9c8eb4d9f5ace44ab463ea6acc73f143a580f2a3`. The fresh native proof distinguishes full-uint64 admission results, an APPLIED dispatch and observed Move displacement; a rejected batch produced no dispatch or effect. The owning [native asset proof](https://github.com/FS-GG/HighBarV3/blob/81016ee38b123a490e6ff06037cdc2f56a105a96/docs/roadmaps/barc-01-native-asset-proof.md) binds source, schema and installed plugin hashes. This is fork staging and local native evidence. The correlated FSBar receiver and real scripting-client Move harness then merged in [FSBarV2 #2](https://github.com/FS-GG/FSBarV2/pull/2) at `b096ea17608c572ad2d0d3d4a961ad7f1c4a3b78`; independent readback found the same tree as candidate `9b0616b8968baa0d822190e398279bce1fd78c2e`. The live proof observed broker ACK, matching native acceptance, APPLIED dispatch at frame 196 and 81.4 world units of later displacement through the production Protocol host. BARC-01.2c is source/native-qualified at this fork boundary. The fork has no hosted checks or branch protection. The refreshed viewer then merged in [FSBarV2 #1](https://github.com/FS-GG/FSBarV2/pull/1) at `396af034057c826ed11b871f27b49984f30c1d8e`, closing BARC-01.1h and restoring the full Release solution build with zero warnings or errors. Focused suites passed; three integration fixture failures were independently reproduced on the earlier protected base and remain attributed. Upstream EHotwagner delivery remains access-blocked; browser product UI, clean generated receiver and published adoption remain separate. The authenticated preview boundary, isolated Worker/Rust guests and real Fable codec compatibility then merged in [FSBarV2 #3](https://github.com/FS-GG/FSBarV2/pull/3) at `16e0a1bddaa64f38754824b012195c803361aedc`, with a tree identical to locally qualified candidate `c00d11d4eb7eda57bf8ab9491b2522cd8c08dcb5`. Combined Release build passed with zero warnings/errors, gateway 10/10 and actual Chromium Worker 24/24 passed, and the three existing integration fixture failures remain attributed. **BARC-01.3a/.3b are source-qualified at the fork boundary.** The full bounded **BARC-01.3 preview is now source-delivered** through [FSBarV2 #4](https://github.com/FS-GG/FSBarV2/pull/4), independently read back at `c537040fe719cd3b8420c43c28eee08157e5ded8` with the exact qualified `736b9bc` tree. Actual product and clean public-generated receiver Chromium journeys both pass, preserve pointer/keyboard/custom-guest behavior, and independently record zero native submissions and clean teardown. Joined Release build has zero warnings/errors; client boundaries 11/11, browser 6/6, companion 3/3 and generated receiver .NET 64 pass. Final archive SHA-256 is `9da86831b50f54b042134b5d590fd9765656d86e844302b9bd5eda31aa2e83a6`. The [owning plan](https://github.com/FS-GG/FSBarV2/blob/c537040fe719cd3b8420c43c28eee08157e5ded8/docs/roadmaps/barc-01-browser-preview.md) and [qualification](https://github.com/FS-GG/FSBarV2/blob/c537040fe719cd3b8420c43c28eee08157e5ded8/docs/roadmaps/evidence/barc-01.3cde-preview.md) bind Templates 0.15.0/SDD 2.0.3 provenance, identical served assets, collision/retained-file refusal and arena isolation. The full bounded **BARC-01.4 live-control outcome is source/native-delivered** through [FSBarV2 #5](https://github.com/FS-GG/FSBarV2/pull/5) at `f1a18c52246b88e958344cb3bcc87f3c2035a62e` and [HighBarV3 #2](https://github.com/FS-GG/HighBarV3/pull/2) at `680b62480bfb60a19b3591b7250e03787e1f93d1`; independent fetches verified the delivered FSBar tree and the exact qualified native tree. Six fresh real-engine journeys—local pointer, keyboard and custom guest plus the same three generated-receiver paths—retained 13/17/3/14/16/3 current observations, 23 submissions, 22 accepted actions and 66 ordered broker/native/dispatch stages. Replace Move, appended Move with installed SHIFT bit 32, Stop and visible selected-unit Attack produced native effects; the independent guest changed APPEND input to REPLACE output. The durable [owning plan](https://github.com/FS-GG/FSBarV2/blob/main/docs/roadmaps/barc-01-live-control.md) and [sanitized qualification](https://github.com/FS-GG/FSBarV2/blob/main/docs/roadmaps/evidence/barc-01.4-live-control.md) retain separate source, component and native identities. `.5` is the next ready BAR horizon after this projection lands. `.7` publication, installed defaults and upstream adoption remain pending.

#### SVG milestone projection

The owning [SVG programme](2026-09-07-064259-svg-game-engine-template-design-roadmap.md) and feature plans
retain individual milestone evidence. Foundation, model qualification, scene, authoring, input, runtime,
presentation, replay, network, scale and public Releases A–C are delivered at their declared scopes.
[SVG-WORKSPACE-01.1–.6](https://github.com/FS-GG/FS.GG.Templates/blob/273c9218960215ee856de9e59a769a0e7b63da8f/docs/roadmaps/svg-workspace-01.md)
is frozen; [Release D.1–.4](roadmaps/svg-release-d.md) publishes and activates the compatible SVG product
default through Templates 0.14.0 and wizard 0.11.2. Release D.5's public SDD 2.0.2 receiver source
merged in [Templates #641](https://github.com/FS-GG/FS.GG.Templates/pull/641) at `848fe5a1fb581707be67ecb6bda326ecd43a2fe8` after the focused public qualification and native required checks passed; its [exact-main public receiver run](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36454677283) also passed. Its later lifecycle
default follows the selected generation-2 clean-start route and separate receiver/activation proof;
the historical `OperatingV2` gate is superseded. Durable public hosting remains Templates #491.

Release D.5's Templates 0.15.0 and wizard 0.12.0 are now public at their immutable sources. [Templates #644](https://github.com/FS-GG/FS.GG.Templates/pull/644) merged the exact public qualification at `59d0c521446a3d9d362d81a978b25434ce4d2d6f`; [run 36472328832](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36472328832) passed lifecycle omission to `typed-sdd`/`quint-specification-v1`, explicit tokens, bundles, 18 locked builds, authored authority, refusal and sampled invariants. [Activation #3955](https://github.com/FS-GG/.github/pull/3955) merged at `2574f02aa8cf835ab1e0b5ff6c62504c33098183`. The fresh [public-only repeat 36474756648](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36474756648) passed against those protected pins. Independent readback verified the effective registry/projection, qualification receipt `42c882fab211f15bf05745c98ddaaa870b3f923040c7aa0ee59a734420d1a9fb`, all 63 evidence hashes and both authored authorities. **SVG-RELEASE-D.5 and bounded Release D are complete.** Retained upgrades and durable public hosting remain separate; sampled invariants make no exhaustive SDD verification-readiness claim.

SDD 2.0.0 introduced the omitted **Typed SDD backend** choice; the current inspected SDD release record is
2.0.3, verified through the selected public SKILL-FS receiver. This does not change the distinct provider lifecycle tokens or prove every generated receiver adopted 2.0.3. Product default, backend default and workspace lifecycle default must not share one completion label.

### 0.2 Process and telemetry progress

Routine delivery is selected by the adopted policy, not a sensitive-path or GS2 label. Canonical model
checks and effect safeguards remain independently binding. Governance's pure route, severity and exact-key
reuse functions are implemented; their effective consumer wiring belongs in V0/V3 acceptance.

The former “0.91.0 publication blocked” status is superseded: the
[telemetry release owner](roadmaps/utel-release-successor.md#release-result-and-remaining-host-boundary)
records publication/readback and later successor work. Read its current promoted artifact and the operator's
installed receipt separately. A public package or dashboard source commit does not identify a running Host.

The coherent [Kit, Drivers and Coord.Cli 0.91.5 release](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.91.5)
is public from exact source `941a82c0e06c9afe9db4c88fc29997d7627a5895`; the [protected publisher run](https://github.com/FS-GG/.github/actions/runs/36330319790)
verified GitHub Packages and nuget.org readback. Publication does not establish a specific receiver's
installed version. [Cross-repository `verify-paths` #3894](https://github.com/FS-GG/.github/pull/3894)
merged on protected main at `b583fa65`; [SKILL-FS-01 #3893](https://github.com/FS-GG/.github/pull/3893)
merged on protected main at `0cc2082dd3fa6636f232d59fc1e6d849a989051c`. Its .1–.4 source
window is delivered. [PR #3918](https://github.com/FS-GG/.github/pull/3918) delivered additive
configuration discovery. The coherent [0.92.0 release](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.92.0)
published Kit, Drivers and Coord.Cli from `eb0f7318` to both feeds; [registry PR #3926](https://github.com/FS-GG/.github/pull/3926)
and [SDD PR #1075](https://github.com/FS-GG/FS.GG.SDD/pull/1075) then bound clean SDD scaffolds to
Coord.Cli 0.92.0. Retained 0.91.5 manifests still refuse without mutation, and Kit/Drivers remain
at 0.91.5 in that earlier SDD receiver. The later F# caller switch and Python retirement merged in [#3941](https://github.com/FS-GG/.github/pull/3941); coherent 0.94.0 is public and [#3954](https://github.com/FS-GG/.github/pull/3954) reconciled its canonical pin. The selected public SDD 2.0.3 receiver merged in [SDD #1083](https://github.com/FS-GG/FS.GG.SDD/pull/1083) at `0c26ac591e76d2839177da823b3f6ada5c09a698`. [Readback-only recovery 36478312479](https://github.com/FS-GG/FS.GG.SDD/actions/runs/36478312479) passed, with independently verified equal dual-feed payloads. The receiver owner's fresh public-package clean and retained qualification passed without a target-command override, including mirrors, retired-helper absence, guarded upgrade/resume/idempotence, co-tenant preservation and schema-v1 pending replay. **SKILL-FS-01.5–.6 and the selected feature exit are complete.** Wider materializer adoption remains separate; not-configured telemetry supplies no usage or efficiency result.

The [Host 0.2.0 successor](roadmaps/utel-host-release-020.md) is publicly promoted from source `933328fdf76e921a0198625f9ba834ddea0e12ed`. [Publisher 36482933665](https://github.com/FS-GG/.github/actions/runs/36482933665) passed; protected `rel-07` generation 17 verifies all eight effects, the three release assets and equal normalized dual-feed payloads. Anonymous exact-version installation passed. The manifest declares schema range 10–12; public Host 0.1.7 remains immutable. Main received the artifact packet in telemetry mailbox `3cdaec20` after independently reviewed SystemAdmin source delivery. That historical receiver is no longer a future LEARN prerequisite. The separately staged inactive image is `sha256:114d12fc2301d3d747223e8401878cf579942eb7b996169f2771d5a1a8936886`, with published archive `d831c05d4b88da5690d86110aa93d5859b73f70f2ec968c648615bb9ba0d855e` and normalized payload `bd001b8fe1509fd17112dc80b0d1f66ab040455c431d4f15baf4e532e9032cc5`. The reviewed manager stage remains bound to manifest `6589bf82bd494035c036506f62e1e3fdc56c74ea4dac3ff023004245a85c99ad` and assembly `3b8981e725365f6a3825ef3bda1b382208facdd9264821a7defa88cb53013976`. [Work-main's byte inspection](https://github.com/FS-GG/.github/blob/4fdeca1ac6c1c459701d3f62affcb3257ff3ae19/MAILBOX.md) reports that this served assembly contains `capture/1`, while protected #3986 at `70303b8ca7b9a2ed30a9d809a66e95a615aa4d24` is 31 commits ahead of the 0.2.0 source and requires `capture/2`. Root independently verified the commit comparison and that 0.2.0 remains the newest public Host release; the served literal inspection remains owner-reported. Version 0.2.1 source is prepared at `9a0d17f11e63002bc67246bb819d0080f5b93799`, but no successor release exists yet. Keep every preparation image inactive. The replacement container owner can issue the scoped config/2 role/grant and private secret locally; remaining gates are published successor bytes, actual native capability, Main-unavailable deployment/capture/recovery/cleanup, W6 and enrollment. Coordination [C3 activation #881](https://github.com/FS-GG/FS.GG.Coordination/pull/881) and its [observer repair #883](https://github.com/FS-GG/FS.GG.Coordination/pull/883) qualify independently of that historical private telemetry operation.

LEARN-01.3’s bounded proposal/context source window is delivered in [Coordination #882](https://github.com/FS-GG/FS.GG.Coordination/pull/882) at `13d9eac78051710eb9e705f8e0157be99de5d587`, from qualified head `67a9aa6430d04947adc10e4a6131625a38b6e79b`. The pure compiler preserves current/focused context manifests, typed `create`, `keep`, `investigate` and `decompose` proposals, fixed Astra-high/Sol-medium profiles, direct-small eligibility and canonical literal touch-set refusal. Observer 44/44, architecture 3/3 and native hosted gates passed. Outputs remain inert: no dispatch or write authority is granted. At that slice, durable pre-dispatch original-item treatment assignment, crash/replay ownership and the existing executor/observation adapter remained required.

The earlier .3 assignment slice is source-delivered in [Coordination #885](https://github.com/FS-GG/FS.GG.Coordination/pull/885), merged at `39cb47312586a8e5a0b949c89cfce4bcb4a1955c` from exact head `9095099753d1a9a333fa5391a9c08cb7d761fc29`. The existing Observer journal now converts inert proposal/context digests and fixed profiles into one canonical treatment stream per original item before dispatch, transactionally retaining the exact source session, head, digest, workflow revision and generation. Current/focused ownership is immutable; restart, new-session recovery, descendants and retries replay the assignment, while redraw, concurrent ownership and a second owner refuse. The [native PostgreSQL 18.6 job](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36492864046/job/109165695983) passed 7/7, including actual stop/start and concurrent ownership; observer 49/49, architecture 3/3 and the [coherent run](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36492864099) also passed. At that checkpoint, the treatment remained inert; executor dispatch, observation and requested-versus-observed provider correspondence followed in #886. No installed custody, live experiment or measured benefit was established by #885.

**LEARN-01.3 source is delivered at its qualification-only boundary.** [Coordination #886](https://github.com/FS-GG/FS.GG.Coordination/pull/886) merged at `513acdbddcb52b6f3e219619605de458070e033f` from exact qualified head `23aef131d3c66845dc41ffc5d3a17db80a3d0cbd`; protected readback verified its tree is identical to the qualified head. The source connects sealed proposal/context preparation and canonical recovered treatment authority to the existing Host, executor, runner and qualification-only telemetry producer. A durable acyclic execution binding preserves the immutable original assignment while carrying each root, child or retry's current validated input; unsupported or unknown provider capability causes zero launch calls, and requested-versus-native absence or mismatch remains explicit. Native PostgreSQL 18.6 passed 40/40 on source checkpoint `8a41c569fcea5a1946b18ca272ba716a203554ae`, including restart, idempotence and conflict fencing (TRX SHA-256 `142a366455301c0010a7264f46f28c4cc99eaf75bc4ed46b042eda13ba64b40f`); final-head [bootstrap](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36518018697) and [coherent qualification](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36518018765) passed. The pinned Quint refresh passed Q1/Q2, 8 positive invariants, 166 negative controls and 19 formal counterexamples; the repair updated current source/assembled identities while preserving retained ITF, trace, ordered-state, bound, toolchain and outcome identities. Installed provider/model support, authentication, enrollment, private collector custody and capture, complete usage and cost attribution, the separately declared LEARN experimental endpoints, canary and measured benefit remain open. No live experiment or installed-operation claim follows.

Prospective telemetry can join native usage only where the configured runtime exposes a verifiable record.
Missing parent/child population, terminal usage, CI attribution or Host acknowledgment remains an explicit
gap. Private stores and credentials remain private; public projection keeps its closed allowlist.
This audit neither requalified a live Host nor measured an efficiency cohort.

### 0.3 Historical GS2 evidence and superseded work

This table preserves the earlier staged route's evidence and uncompleted scope. ADR-0091 supersedes
its unfinished migration, archive, rollback and mandatory V1-admission requirements for the clean
start; these rows are not current prerequisites. The active C0–C3 results are recorded above.

| Evidence | Historical scope established | Historical scope not established |
|---|---|---|
| [GS2-07 and GS2-08 accepted receipts](https://github.com/FS-GG/FS.GG.Coordination/tree/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/accepted) | Event/queue qualification, bridge, receiver adoption and residual-writer disposition. | Preserve validity across later changes; no Q4 or production open inferred. |
| [OperatingV1 admission installation and typed readback](https://github.com/FS-GG/FS.GG.Coordination/pull/508#issuecomment-5814570932) | The protected genesis operation ref exists at the exact planned parentless commit; independent exact-object replay restores generation 1 `AdmissionsOpen`. The [governing issuer design](https://github.com/FS-GG/.github/pull/3877) is merged. | The [held public-key verifier](https://github.com/FS-GG/FS.GG.Coordination/pull/533) is one inactive prerequisite. Native job/PR identity reading, durable one-shot consume, full plan codec, dedicated key custody, provider reconciliation, installed post-genesis append and copy-specific effect authority still require qualification. |
| [Callable readiness](https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/gs2-09-9/callable-readiness.json) and [handoff](https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/gs2-09-9/callable-discovery-handoff.json) | Installed 0.1.1 in one admitted synthetic disposable repository; sealed plan, provider/journal recovery, no-op replay and cleanup. | Migration/cutover, production targets, release effects and general writer enablement are excluded. |
| [GS2-09.1–.6 receipts](https://github.com/FS-GG/FS.GG.Coordination/tree/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/accepted) | Discovery, manifest, transforms, live-operation dispositions, sealed history and rollback **contracts**. | Fresh nine-authority provider capture, effect execution and actual representative rollback. |
| [Replay/omission source controls, PR #507](https://github.com/FS-GG/FS.GG.Coordination/pull/507) | Exact manifest replay, added-subject refusal and every rollback receipt prefix under controlled tests. | Live interruption/retry, complete copy population and independent .7/.8 acceptance. |
| [GS2-09.7 source PR #847](https://github.com/FS-GG/FS.GG.Coordination/pull/847) and [host PR #3879](https://github.com/FS-GG/.github/pull/3879) | Migration and host source are delivered on protected main, preserving the earlier implementation stack. | The registered isolated cohort, full nine-authority capture, Q5/Q6, interruption/rollback, cleanup and independent protected acceptance are still open. |
| [GS2-09.7 claim/event parser PR #852](https://github.com/FS-GG/FS.GG.Coordination/pull/852) | Protected merge `fed499d500299cf7a4daa00cf63da48ea4e7809b` adds audited intake, historical claim and legacy receipt parsers as source only. | `RosterComplete=false`: delivery, protected intake and legacy done producers, exhaustive reserved-prefix registry, canonical authority, provider outcome and Q5/Q6 acceptance remain open. |
| [GS2-09.9 source PRs #845](https://github.com/FS-GG/FS.GG.Coordination/pull/845) and [#846](https://github.com/FS-GG/FS.GG.Coordination/pull/846) | The closed v2/v5 effect source and readback controls are delivered; proposed workflows remain disabled and grant no protected effect. | The [#545 qualification hold](https://github.com/FS-GG/FS.GG.Coordination/pull/545) still requires versioned operator, contract/proposal, validator and typed-index rotation, fresh registered Q3/Q6, and protected acceptance. Merged negative source tests alone do not clear it. |
| [GS2-09.9 `/5` candidate PR #851](https://github.com/FS-GG/FS.GG.Coordination/pull/851) | Protected merge `92a77486ed0f753f3d3a20f3be8056c3823b58f5` delivers the internal runtime, protected adapter and read-only candidate workflow as source only. | No hosted candidate, installed authority, protected journal CAS or native `/5` acceptance is established. |

### 0.4 Active work and immediate critical path

1. **Audio C3 is closed at the selected receiver.** CLI 0.1.3 publication, dedicated custody,
   protected activation, one settlement, independent Authority readback and the normal already-complete
   rerun are observed. The owning [Audio plan](https://github.com/FS-GG/FS.GG.Audio/blob/main/docs/roadmaps/v2-ordinary-adoption.md)
   retains the exact evidence.
2. **SDD and Templates C3 are closed at their selected receivers.** Net, Governance and Game completed their combined
   0.1.5 wave. The protected CLI 0.1.6 publication and anonymous clean-install proof enabled both
   receivers; SDD then merged at its checked head and completed its own settlement, independent Authority
   readback and normal already-complete rerun. Templates' separate activation merged after native checks;
   its first ordinary run settled once with independent Authority readback, and its normal rerun returned
   `SettlementAlreadyComplete` without a second effect.
3. **Coordination C3 is closed at its selected clean-path receiver.** Published CLI 0.1.7, dedicated custody, protected activation and observer repair are delivered. Settlement run `36484724833` succeeded once, its normal unchanged rerun was already complete, and independent Authority readback found one completed effect with the journal head unchanged. LEARN installed custody and measurement remain independently gated.

   Independent SKILL-FS, LEARN and product work continues through its own boundaries.

The [lane table](#91-dependencies-and-parallelism) names each owner, ready work and join condition.

### 0.5 Known limits and decision state

This is an evidence-backed planning reconciliation, not a new contract or effect authorization.
The [GS2 execution roadmap](github-substrate-v2-roadmap.md) preserves accepted migration evidence as
history while ADR-0091 governs the current clean-start route. Historical “pending” fields in a sealed handoff describe its observation time; new accepted
receipts supersede their status without rewriting those bytes.

No stage-wide percentage is asserted: pure contracts, isolated provider execution, installed receivers and
fleet cutover have materially different remaining work. Missing evidence is unknown, not zero or failure.
Broader portfolio proposals remain visible in section 15 without becoming hidden prerequisites.

## 1. Starting point and the simplification handoff

### 1.1 What “after simplification” means

The predecessor is the [R0–R5 simplification programme](2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md).
This successor takes its **adopted policy, working current-route delivery, applicable release/recovery
behavior, truthful incumbent observation, and published receiver contract** as inputs. R0–R4 implementation
is predecessor work, not the first phase of this roadmap.

The 2026-09-29 amendment closes R5 functionally for the selected clean-start ordinary-source profile at the
existing receiver/canary/observation boundaries below. Its actual ordinary-v2 evidence could not precede the
ordinary V2 path; it now exists for all nine selected receivers. The measured economics cohort remains
insufficient and cannot be reused as performance evidence. This distinction does not rename R0–R4 or the
cancelled GS2 migration sequence as completed work.

The handoff consists of existing source references, not a new report family:

| Input from simplification | Required meaning at handoff | What remains in this successor |
|---|---|---|
| Adopted routine policy and trust boundary | Eligible work can use one owner and one PR with selected technical checks; protected operations retain their authority | Bind that exact policy to v2 semantics and installed receiver behavior |
| Native code and operation outcomes | Code delivery, publication pending, verified publication and unknown outcome are distinguishable | Preserve those meanings through v2 journals, adapters and recovery |
| Working observation path | Original events and usage can be joined; incomplete coverage is visible; observer loss does not block delivery | Independently measure ordinary-v2 use and any later controller |
| Current-route observation | R4's effective supported route and repair follow-up are functionally qualified; the declared comparison remains insufficient where usage or cost evidence is missing | Do not reuse the incumbent observation as v2 performance evidence or claim an efficiency benefit |
| Removed obligations and retained predicates | The owning policy says what was deleted, made advisory, moved asynchronous or retained blocking | No generated guidance or v2 caller silently restores removed ceremony |
| Receiver ownership | Policy publisher, runtime owner and installation families are identified | Qualify clean installs, upgrades and retained protected paths |

The source snapshot below establishes implementation and evidence boundaries, not completion of every
predecessor measurement. Existing authorized GS2 work can continue under its current contracts while
predecessor work finishes; this roadmap does not suspend it.

### 1.2 Verified source snapshot

The current review fetched each default branch and inspected the exact revisions below, rather than
assuming a local checkout was current. Source inspection and retained acceptance evidence support the
claims; this review did not repeat live release, admission, migration or installed-host operations.

| Repository | Protected default revision inspected | Concrete boundary inspected |
|---|---|---|
| `FS-GG/.github` | `d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5` | Routine policy/helper, receiver tool manifest, GS2 sequence, release/default plans and source skill roots |
| `FS-GG/FS.GG.Coordination` | `e96f4821a40c595ebe960e6cf126ace748852f30` | Callable CLI/runtime, accepted GS2-09.1–.6 contracts, replay/omission controls and retained callable native receipts |
| `FS-GG/FS.GG.Governance` | `df47d597fbddbbf41b8facc034185fc9755ef6f6` | Pure routing, severity and freshness-key reuse; consumer installation is a different boundary |
| `FS-GG/FS.GG.SDD` | `cb89bcafb95efa1d11ecd29c21ca4108cd30a2cf` | Driver materialization, 2.0.x backend/lifecycle distinction and FsQuint consumer release qualification |
| `FS-GG/FS.GG.Templates` | `273c9218960215ee856de9e59a769a0e7b63da8f` | Exact Rendering provider pin, frozen workspace and public Release-D receiver evidence |

The [audit](reports/2026-09-24-v2-roadmap-code-audit.md) supplies code-level links and remaining acceptance.
The September 7 source survey remains available in the
[historical snapshot](https://github.com/FS-GG/.github/blob/d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md#12-verified-source-snapshot);
its GS2-07.5 frontier, CLI-only-preparation observation and SDD 1.5.0 release are historical inputs, not
current implementation gaps.

The local Coordination branch `routine/gs2-09-7-rehearsal` at `82b4772d…` is explicitly separate from
protected main. Its provider-reader and step-execution source can be reused after review, but no remote
publication or native migration acceptance is inferred.

## 2. Research method and findings

### 2.1 Offline analysis

The [September 24 code audit](reports/2026-09-24-v2-roadmap-code-audit.md) supersedes the original
implementation-status observations below. The following design rationale remains useful; it does not
reopen delivered capabilities or certify newer installed behavior.

The review compared the master and programme inventory with the v2 roadmap and architecture amendment,
R0–R5, OR H0–H8/F0–F5, PB0–PB8, the governance proposal, Quint migration, and telemetry automation.
It inspected relevant policy, workflow, delivery, observer, Governance and SDD source. GitHub PR and Actions
checks supplemented the documentation. It did not run a production mutation, a complete board census, a
performance experiment, or an organization-wide package/receiver audit. Unpublished local telemetry is not
used as independently verified measurement and is not published by this document.

| Finding | Evidence and limits | Design response |
|---|---|---|
| Policy intent is more aligned than implementation | September 7 OR/PB amendments already make controllers optional; their older detailed clauses still describe richer workflows | One explicit process matrix and one component ownership model |
| Routine does not imply isolated | The pilot has static operation/path checks and no board/claim observation | Separate delivery profile, conflict domain, current authority and capacity |
| Governance vocabulary is not interchangeable with pilot routing | `Route.fs` makes unfenced work advisory; complete input is a caller obligation | Preserve existing semantics; qualify a complete-fact handoff instead of matching enum names |
| Source-head equality does not replace freshness or fencing | Governance freshness includes base/head; v2 review snapshots bind base/head and checks | Separate test reuse, integration evidence, review authority and current operation grants |
| Valid usage arithmetic is not population coverage | The observer marks supplied reconciled counts measured without an independent usage denominator | Separate record validity, join integrity, coverage and performance qualification |
| Delivery/observer integration has a concrete schema gap | Helper emits `expectedHead`/`observedHead`; observer reads `head` | Require a real producer/consumer identity join at handoff; no source-only adapter success claim |
| OR and PB overlap in stateful execution | Both describe budgets, verification, reservations, durable effects and recovery | One bounded execution component; OR adds scheduling over it |
| Candidate stabilization is a shared-resource problem | GS2-10 freezes many fleet inputs and requires comprehensive requalification after drift | Decide inclusion early, bound the stabilization window and exercise aborted cutovers |

Relevant source: [routine policy](https://github.com/FS-GG/.github/blob/a6880e965bba91400090dc387ed74f27d39bce9a/.fsgg/routine-development.json),
[classifier](https://github.com/FS-GG/.github/blob/a6880e965bba91400090dc387ed74f27d39bce9a/scripts/check-claim-generation.py),
[delivery helper](https://github.com/FS-GG/.github/blob/a6880e965bba91400090dc387ed74f27d39bce9a/tools/routine-delivery.py),
[observer](https://github.com/FS-GG/.github/blob/a6880e965bba91400090dc387ed74f27d39bce9a/tools/routine-observer.py),
[Governance routing](https://github.com/FS-GG/FS.GG.Governance/blob/3f265769d2b23ce36cbe0d05985e3c715dfead4f/src/FS.GG.Governance.Kernel/Route.fs),
[freshness inputs](https://github.com/FS-GG/FS.GG.Governance/blob/3f265769d2b23ce36cbe0d05985e3c715dfead4f/src/FS.GG.Governance.FreshnessKey/Model.fs),
[reuse decision](https://github.com/FS-GG/FS.GG.Governance/blob/3f265769d2b23ce36cbe0d05985e3c715dfead4f/src/FS.GG.Governance.EvidenceReuse/EvidenceReuse.fs),
and [v2 review adapter](https://github.com/FS-GG/FS.GG.Coordination/blob/e2be0cca5a9398cc80e341f68fbc17cd2b3962d6/src/FS.GG.Coordination.GitHub/ReviewDeliveryAdapter.fs).

A small synthetic boundary diagnostic passed helper-shaped delivery data to the observer and reproduced
a missing head; a self-consistent 100-token usage example was classified measured without a population
denominator. These are synthetic input checks of the inspected functions, not actual delivery, runtime
usage, or evidence of efficiency. The gaps belong to the predecessor handoff if still present there;
they do not justify rebuilding telemetry as the first successor programme.

### 2.2 Online research and transfer limits

Primary sources were opened on September 7, 2026. The table separates published observations from this
design's inferences. Product documentation establishes mechanism, industrial reports establish experience
in their own environment, and neither predicts FS-GG savings. Moving documentation does not upgrade a
pinned FS-GG toolchain or prove that a feature is enabled for a particular repository.

| Question | Primary evidence | Consequence for this design |
|---|---|---|
| How should productivity be judged? | [SPACE, Microsoft Research, 2021](https://www.microsoft.com/en-us/research/publication/the-space-of-developer-productivity-theres-more-to-it-than-you-think/) argues against a single activity or efficiency measure | Measure delivery, quality, attention and resources together; tokens alone cannot qualify a process |
| Does faster generation imply faster delivery? | [DORA, March 2026](https://dora.dev/insights/balancing-ai-tensions/) describes creation savings shifting into verification and AI amplifying existing strengths and weaknesses | Include review, integration, repair and shared maintenance in the baseline |
| Which delivery measures apply? | [Current DORA guidance](https://dora.dev/guides/dora-metrics/) distinguishes throughput and instability and warns about incomparable populations and over-investment in measurement | Keep source merge, package delivery and deployment denominators distinct; use deployment measures only where deployment exists |
| How small should implementation slices be? | [DORA small batches](https://dora.dev/capabilities/working-in-small-batches/) relates smaller changes to faster feedback and easier recovery | Prefer a usable capability or one independently testable protocol change; do not batch unrelated features merely to amortize ceremony |
| Does simplified review mean review is useless? | [Google's modern code review study, 2018](https://research.google/pubs/modern-code-review-a-case-study-at-google/) examines a mature lightweight review practice across millions of changes | Keep focused critique where uncertainty or consequences justify it; deleting universal role choreography is not evidence that critique has zero value |
| Can native merge integration reduce base churn? | [GitHub merge queue documentation](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue) describes checking the target plus queued predecessors; required Actions checks need `merge_group` coverage | Pilot queue behavior and check identity before enablement; a source-head check alone is not integration evidence |
| Can retries be transparent? | [AWS Builders' Library: idempotent APIs](https://aws.amazon.com/builders-library/making-retries-safe-with-idempotent-APIs/) distinguishes caller intent from merely identical request parameters | Preserve stable operation identity, payload binding and result readback; identical payloads can still represent different intended operations |
| Does a durable workflow settle an external effect? | [Temporal activity execution](https://docs.temporal.io/activity-execution) distinguishes timeout/retry and cancellation; an activity can ignore cancellation | Item termination cannot assert that an effect stopped; recovery ownership survives cancellation and timeouts |
| Do actors provide reliable business completion? | [Akka.NET delivery semantics](https://getakka.net/articles/concepts/message-delivery-reliability.html) give ordinary messages at-most-once delivery and pairwise ordering | If a host is justified, qualify persistence, acknowledgments, inbox/outbox and recovery explicitly; actor serialization is not provider atomicity |
| Where do formal methods help? | [AWS formal-methods experience, 2015](https://www.amazon.science/publications/how-amazon-web-services-uses-formal-methods) reports their use on difficult critical-system designs | Model small authority, reservation and recovery kernels; do not require a new behavioral model for every routine source edit |
| What does Quint prove? | [Quint's current explanation](https://quint.sh/docs/what-does-quint-do) distinguishes simulation and checking, and identifies partial temporal-property support | Pin supported evidence modes and bounds; report sampled versus exhaustive results and explicit environment assumptions |
| Can cache reuse replace execution? | [Bazel remote caching](https://bazel.build/remote/caching) documents hazards from changing inputs and environmental dependencies | Reuse only under the adopted subject and execution-input contract, with integrity checks and cold boundaries |
| Is predictive test omission already justified? | [Meta predictive selection, 2018](https://engineering.fb.com/2018/11/21/developer-tools/predictive-test-selection/) reports calibrated selection using large historical datasets | It motivates an optional experiment, not silent omission of FS-GG obligations or transfer of Meta's detection percentage |
| Is more CI parallelism always better? | [Fallahzadeh et al., 2023](https://arxiv.org/abs/2308.13129) study parallel batch testing using Ericsson and Chrome data and find nonlinear resource/feedback tradeoffs | Measure shared setup, queue delay and failure isolation before adaptive batching; do not import their reported savings as a local target |
| When are agents or multiple agents justified? | [Anthropic's agent guidance](https://www.anthropic.com/engineering/building-effective-agents) recommends simple composition; [its 2026 multiagent research](https://www.anthropic.com/research/multiagent-systems) discusses coordination failures and independently divisible work | Start with deterministic tools and one owner; qualify parallel composition against a fixed single-worker baseline |

The research supports a restrained architecture: retain the accepted distributed-systems protections,
reuse native integration and published tooling, measure actual bottlenecks, and add only the next needed
execution capability. It does not justify a universal controller, a new default lifecycle, or a promise
that lighter review will preserve an identical escaped-defect rate.

## 3. Intended architecture and ownership

### 3.1 One authority chain, one shared execution implementation

```mermaid
flowchart TD
    Request[Request or existing work reference] --> Facts[Complete relevant facts and adopted policy]
    Facts --> Rules[Governance or supported rule evaluator]
    Facts --> Coord[Coordination legality and current epoch]
    Rules --> Exec[Supported execution entry point]
    Coord --> Exec
    Exec --> Provider[GitHub Git and other providers]
    Provider --> Observe[Verified outcomes and asynchronous views]
    Observe -. measurements .-> OR[Optional OR scheduler]
    OR -. proposed allocations .-> Bounds[Shared bounded execution component]
    Bounds --> Exec
    Quint[Published Quint contract and domain model] -. constrains changed modeled behavior .-> Coord
    Quint -. constrains enabled controller .-> Bounds
```

The arrows express proposed responsibility, not new network services. The current supported CLI remains
the incumbent entry point until the owning migration delivers a replacement. Pure decisions should remain
usable from tests, CLI, scheduled jobs and any future host without separate behavioral implementations.

| Responsibility | Owner | Implementation boundary |
|---|---|---|
| Programme policy, accepted obligation profiles, receiver topology, release coordination policy | `.github` and existing product policy owners | Versioned adopted sources and thin distribution; no domain engine copied into this repo |
| Lifecycle constitution and generic specification tooling | FS.GG.SDD | Published compiler/profile/ITF/binding/materialization boundaries; no Coordination or game semantics |
| Rule evaluation, effective severity, evidence freshness/reuse where adopted | FS.GG.Governance | Existing supported pure evaluator; consumers supply complete facts and enforce the result |
| Work facts, grants, epochs, transition legality, provider plans and reconciliation | FS.GG.Coordination | Existing v2 domain and adapter boundaries, protected Git journals and provider verification |
| Bounded workflow execution when additionally needed | FS.GG.Coordination | One component owning attempt lineage, finite budgets, reservations and durable local effect intent |
| Portfolio scheduling and allocation when justified | FS.GG.Coordination, policy owned by `.github` | Optional OR planner over the shared execution component; independent plan validation |
| Hosted supervision and authenticated observation | Same execution owner if a host is enabled | One selected runtime; it supplies lifecycle mechanics without replacing external authority |
| Receiver behavior | Each consuming repository; Templates for generated workspace families | Exact published pins, effective configuration, generated guidance, clean/upgrade acceptance |
| Product semantics | Game, Rendering, Audio, Net, S.I.R. and other actual producers | Their own models, implementations, compatibility and installed consumers |

OR and PB become **two views of the same execution architecture**. PB specifies bounded per-item progress,
resource accounting and recovery; OR allocates work and shared capacity across those items. They do not
independently implement an outbox, reservation ledger, retry engine, provider writer or completion model.
Their original detailed requirements are mapped in section 14; unneeded features remain unimplemented.

### 3.2 Four separate decisions

For any supported action, determine:

1. **Delivery process and evidence:** routine by default; heavyweight only for explicit human-named scope.
   Modeled/contract work selects substantive checks, and protected effects select their own safeguards.
2. **Synchronization need:** isolated, a named shared resource/grant, or unresolved conflict domain.
3. **Operating authority:** permitted now, preparation only, deferred, or refused under the current epoch.
4. **Capacity:** available within an enforceable limit, deferred, or observationally budgeted.

These are conceptual decisions emitted through existing supported outputs, not four forms or new registries.
A routine source change can require an exclusive test-environment grant. A private worktree does not
authorize a release or authority effect; its source PR still follows the selected delivery process. An unresolved observation cannot be normalized to a negative match.

## 4. Which development process applies, and where

### 4.1 Process selection matrix

**Binding human decision — 2026-09-08:** lightweight routine delivery is the default process for all work
under this unified roadmap. Only a recorded explicit human instruction selects heavyweight ceremony for
named scope; absence or ambiguity selects routine. Strict labels, GS2 registration, protected paths, policy
changes, modeled work, protected operations and inherited strict state do not select the heavyweight route.
The matrix's process distinctions describe substantive technical evidence and effect safeguards within the
routine route unless such a human instruction says otherwise.

One accountable owner uses one routine branch and one PR, focused and native checks, same-PR repairs, native
merge/readback and asynchronous telemetry. No issue/claim, SDD artifact family, phase lifecycle, mandatory
critic, feedback/receipt cycle, metadata-`Done`, or projection PR is implied. Canonical model authority,
permissions and release/deploy/credential/destructive/cutover/external-acceptance safeguards remain
independent and fail-closed; invalid or unknown authorization blocks the affected effect, not source delivery.

[ADR-0084](adr/0084-semantic-reuse-never-cancels-coherent-validation.md) governs qualification selection
inside that route. A validated exact-head semantic reuse may advance native delivery while the independent
coherent run continues; current or unvalidated work waits, and a late failure disputes dependent acceptance
without changing the rule that only explicit human-named scope selects heavyweight process.

| Work class and examples | Where it occurs | Development process | Evidence needed before delivery | What is needed to enable it |
|---|---|---|---|---|
| Non-executable design, analysis, prose index | Any repository | Proportional prose route; one author/PR | Diff, links, applicable formatting and native required checks | Existing prose route; no policy/recipe/parser change hidden in prose |
| Ordinary reversible implementation with no modeled semantic or public-contract change | Product internals, helpers and adapters within their admitted scope | Simplified one-owner route; concise intent and behavioral example, implementation, focused tests, same-PR repair, native protected merge | Selected technical predicates and exact source identity; optional focused critique | Installed routine profile, complete classification and a supported merge path |
| Ordinary source work using an exclusive shared environment | Any eligible product | Same routine process plus automatic resource acquisition only for the shared action | Valid environment grant and relevant technical results | Qualified resource adapter, conflict identity and recovery owner; otherwise the resource action waits |
| Implementation change under an existing canonical model | Coordination protocols, modeled product behavior | Model-constrained implementation: inspect model, plan correspondence, implement, test/replay affected behavior | Current conformance and affected invariants; scoped reusable evidence where allowed | Published SDD/Quint toolchain and consumer-owned replay; no ceremonial model edit when semantics are unchanged |
| Change to modeled behavior or an authority protocol | Coordination, Governance semantics where modeled, product protocols | Typed specification process: change canonical source explicitly, review semantic delta, implement and independently test correspondence | Type/effect checks, witnesses, bounded invariant/formal evidence, negative controls, real adapter tests as applicable | Owning domain model and supported profile; protected scope selects its stronger delivery predicates |
| Significant feature with unresolved requirements but no useful formal state model | Product/consumer owner | Routine source delivery; clarify the uncertain behavior and deliver small slices; heavyweight SDD only for explicit human-named scope | Behavioral requirements and relevant implementation/consumer evidence | Existing lifecycle selection; no silent lifecycle-token change or inferred heavyweight process |
| Public API/schema, provider, lifecycle, policy or generated-guidance change | Actual producer plus receivers | Routine source delivery with contract qualification and publish-before-adopt; modeled checks where applicable | Compatibility, producer publication and installed receiver evidence | Named producer/receiver boundary; protected policy route for changes affecting eligibility or authority |
| A registered GS2 implementation unit | Primarily Coordination; `.github` for bridge/authority work | Routine source delivery plus the exact registered technical acceptance contract | Scoped child qualification; cold comprehensive parent closure; required review evidence and accepted unit result | Current pinned unit and satisfied predecessor receipts; a routine-looking diff does not waive GS2 acceptance |
| Release, deployment, credentials, destructive migration or cutover | Authorized operation owner | Protected operation process, distinct from source PR delivery | Current authority, exact plan/artifacts, protected effects, output verification and recovery | Operation-specific permissions, grants and current epoch; no invented PR for a PR-less operation |
| Read-only OR/PB investigation | Coordination domain owner; `.github` policy analysis | Bounded research or pure prototype, with a named question and stop budget | Reproducible local evidence and honest limits | Existing tools; no hosted writer, model-dispatch service or default activation prerequisite |
| Stateful PB/OR execution change | Coordination | Modeled controller change and adversarial replay/correspondence; protected canary activation separately | Finite progress, reservations, late effects, crash recovery and policy comparison | Shared executor contract, published tooling and operation-specific eligibility |

“Protected” does not mean additional human or agent authorizers. Preserve
[ADR-0079's accountable delivery owner](adr/0079-single-accountable-delivery-authority.md). Required
independent technical or critique evidence remains evidence; exact provider approval requirements and
explicitly delegated authority still apply. Neither an agent vote nor a new role identity creates authority.

### 4.2 How much specification and review

Keep existing canonical Quint models authoritative. Introduce a new model where concurrency, authority,
retry, resource-budget or protocol correctness justifies a state model; a simple locally tested state
transition does not automatically require a formal programme. Consume existing generic tooling first.
The domain repository owns the model and implementation correspondence. A generated contract carries
stable relationships and identities, not a second language for the same semantics.

For an implementation-only change, keep the model unchanged and prove affected correspondence. For a
semantic change, amend the actual canonical source and make the before/after behavior reviewable. When
the shipped toolchain cannot verify a desired temporal claim, record the supported narrower evidence or
request a bounded producer extension; do not claim a simulation proves eventual delivery.

Routine work uses one owner and at most one optional independent critique under the predecessor
profile. Material findings repair on the same PR; style preferences do not create a confirmation cycle.
Modeled/protected work uses the applicable substantive review and negative controls, while avoiding
duplicate acceptance actors. Existing strict migration artifacts remain evidence, not a process selector.
Telemetry retirement does not remove substantive GS2 qualification predicates.

Requirement uncertainty first calls for focused clarification and a behavioral example. It does not by
itself force a feature into a complete SDD artifact family. Use heavyweight SDD ceremony only when a human
explicitly selects it for named scope.

Omitted lifecycle remains whatever the installed accepted configuration specifies. The inspected SDD 2.0.x release
changes the omitted Typed SDD backend to Quint while preserving provider lifecycle tokens; this document
does not flip the workspace lifecycle to `typed-sdd` or `none`. A lightweight delivery profile and a
lifecycle/backend selection are separate dimensions. If an installed consumer cannot express the intended
combination, that is a real integration gap to fix before claiming the profile works there.

For changed hosted-writer behavior, reuse the accepted Choreo model and consumer-owned replay. Acceptance
uses actual model-generated traces against production decisions, negative mutations that fail at the intended
boundary, and first-divergence diagnostics. Recording adapters supply external facts rather than deciding
policy or recreating model transitions. Other modeled components use the same correspondence principle where
applicable; Choreo is not a mandatory library or a new model requirement for every change. Keep sampled,
bounded exhaustive and temporal evidence distinct, including fairness assumptions and explored fault limits.

### 4.3 Process selection over operating time

| Period | Ordinary development | V2/migration work | Shared or protected operations | OR/PB |
|---|---|---|---|---|
| Before GS2-10 | Supported incumbent simplified route on admitted receivers; re-observe candidate-affecting changes | Current GS2 units and their registered qualification | Current accepted v1/protected authority only; v2 sandbox effects within explicit bounds | Read-only research and pure component work may proceed; no normal v2 writer |
| GS2-10 candidate stabilization | Work that changes bound inputs either joins a new candidate or is deferred; do not assert arbitrary source changes are harmless when receiver heads are bound | Exact candidate, full matrix and rehearsal | Candidate preparation is not application permission | Candidate-affecting host/runtime/policy changes are frozen or deferred |
| GS2-11–12 frozen/switching | No ordinary production writes within the frozen scope | Authorized cutover and isolated qualification only | Cutover-owned operations; pre-open rollback under the accepted plan | No optional production experiment |
| After OpenV2, during ObservingV2 | Only enabled v2 operation classes through prepared receivers | Verify real journeys, monitor and repair; retain observation assets | V2 authority; recovery is roll-forward, no v1 restart | Continue read-only work; normal OR mutation remains deferred to OperatingV2 under H6's default |
| OperatingV2 after Q10 and contraction | Installed simplified v2 profile for its qualified classes | Remaining adopted improvements and deferred programmes | Current v2 grants and published operation contracts | One separately justified, qualified class may enter canary; broader adoption is a separate decision |

The difference between **OpenV2** and **OperatingV2** matters: the first opens normal v2 writing and
irreversibly fences v1; the latter follows the observation/contraction programme. Do not use the names as
synonyms. No purported fallback may restore a fenced v1 writer.

### 4.4 Just-in-time feature planning and execution

Use the temporary repository-owned
[`work-unified-roadmap` skill](../.agents/skills/work-unified-roadmap/SKILL.md) to advance this programme.
It is committed in both declared agent skill roots, so a fresh checkout carries the instructions and
supporting material. Creating or inspecting the skill does not start roadmap work.

At each new major feature, a fresh **Astra high** (`gpt-6-astra`, `high`) subagent analyzes actual prior
work, the relevant unified stages, original plans and targeted current primary sources. It produces one
digestible feature subroadmap: a few ready milestones with acceptance examples and a later outcome
outline. Do not detail the entire programme in advance. Resume valid active plans; expand the near-term
window when needed, or replan when a material assumption, dependency or scope changes.

The [feature-part index](#98-feature-parts-and-subroadmap-index) defines the default scope of those
planning assignments. Stages describe dependency and operating boundaries; a named part describes the
outcome Astra plans. Each subroadmap links back to its part and records its generated-workspace impact
using [section 9.9](#99-when-new-workspaces-change). Add its actual document link to the index when it is
created, preserving links to earlier bounded plans and their evidence.

A **Sol medium** (`gpt-5.6-sol`, `medium`) worker executes that bounded subroadmap through the installed
`work-roadmap` skill and the owning repository's actual route. Reuse the worker for routine milestones
and repairs where the route permits. Existing GS2 ledgers remain authoritative; window completion does
not imply feature completion or publication. Apply section 7.4 through available automatic observation,
report missing instrumentation as a gap, and avoid a new planning or reporting cycle for each item.

Retire the temporary coordinator when the shared driver provides these behaviors or the programme ends,
preserving active plans and evidence. Its presence does not amend protected contracts or enable stages
whose prerequisites have not been met.

## 5. Governance, synchronization and evidence

### 5.1 Preserve selected guarantees explicitly

At integration, each affected obligation receives one disposition in its existing owning policy:
retained blocking, retained advisory, asynchronous, duplicate retired, or deliberately removed with its
accepted tradeoff. Preserve protected-history integrity, complete relevant observations, external effect
authority, recovery ownership and declared technical qualification. Do not infer that every historical
artifact is an essential guarantee, or that a fewer-job CI layout removes any guarantee at all.

Governance determines rule applicability and evidence under its installed contract. Coordination determines
whether current external facts, grants and epochs admit the transition. A positive result from either
cannot substitute for the other. A complete negative fence match differs from incomplete input.

The inspected Governance `Route` treats unfenced work as advisory, including in gate mode. Therefore the
integration must identify which native technical predicates remain independently mandatory and how
inherited enforcement floors are supplied by the actual supported enforcement component. Merely feeding
the pilot's word “routine” into that API is not a proven policy translation. Do not silently change the
pure library's semantics to make two vocabularies appear equivalent.

### 5.2 Synchronization scope and current authority

Isolated source work should have no global claim solely for tracking. Shared environment, package,
settings and protocol effects require their actual conflict domain and qualified grant. Existing strict
claims cannot be escaped by relabeling a branch. Multiple grants use the accepted acquisition and
compensation protocol; a partial acquisition must not enable the effect.

A Git expected-parent update linearizes a journal transition, not an arbitrary later API call. At each
protected provider boundary, qualify generation, revocation and in-flight ordering through the supported
serialized/capability mechanism. If an adapter cannot meet the declared ordering, keep that operation
unsupported. Neither a pre-read nor an actor lock creates remote fencing.

The routine pilot intentionally trusts repository writers. Its check is not adversarial protection from
writers able to alter workflows or imitate name-based contexts. Preserve that scope honestly. Stronger
provenance for protected operations requires actual available provider controls and permissions, not a
statement that the same trusted-writer pilot became a security boundary.

### 5.3 Four different kinds of freshness

| Evidence/fact | Subject | Reuse or refresh rule |
|---|---|---|
| Deterministic technical result | Adopted semantic/execution inputs, toolchain, environment and policy | Reuse only when the installed key matches; verify artifact integrity |
| Integration result | Candidate combined with target/predecessors under chosen merge policy | Recompute where native integration or accepted candidate identity changes |
| Review/acceptance evidence | Required current review subject and candidate | Preserve accepted base/head semantics until a qualified narrower contract exists |
| External mutation authority | Current grant, epoch, operation and resource | Revalidate/enforce at the effect boundary; historical green evidence cannot preserve revoked authority |

Use native merge queues when their measured contention benefit justifies them and GS2-07 qualification
covers the required events and contexts. Low-volume repositories may retain their accepted native merge
policy. A queue is not a universal grant or an exemption from current authority. Unrelated base movement
should not trigger an agent-mediated receipt restart, but can still require automatic integration checks.

## 6. Execution, recovery and releases

### 6.1 One ordinary journey

A request or existing issue supplies bounded intent. The owner implements and runs selected checks in an
isolated checkout. The adopted classifier resolves scope and required shared effects. Material failures
repair in the same PR. The supported provider path merges the intended source under its required checks.
Native readback establishes delivery; any release is a separate operation. Observers join facts later.

The effective experience contains no mandatory receipt-only PR, per-phase public comment, extra acceptance
actor, synchronized roadmap update, or SDD artifact created solely for routine process. Cheap machine
records can remain when a real consumer needs them. Those records must not recreate the removed handoffs.

### 6.2 Minimum recovery without a hosted controller

| Event | Supported behavior | Enforcing/recovery owner |
|---|---|---|
| Source changes before merge | Refuse stale source; refresh relevant checks and continue the same PR | Delivery entry point and provider |
| Merge response is lost | Read native state; retry only under the bounded operation's proven retry conditions | Delivery entry point; pending outcome remains explicit if observation fails |
| Board, usage or dashboard disappears | Preserve known delivery, show stale/missing observation and refresh later | Model-free observer |
| Shared grant is revoked | Prevent new protected effects; reconcile already in-flight work under declared semantics | Coordination and qualified adapter |
| Publication partially succeeds | Verify package identity and bytes; finish only missing required steps | Existing release owner and saga |
| Timeout occurs while an effect is applying | Stop new ordinary work, preserve operation identity and settlement owner | Operation recovery path |
| Owner or future host disappears | Recover from durable relevant intent and provider facts; do not reset budgets or duplicate effects | Supported successor owner under current authority |
| Observer reports contradictory or malformed data | Isolate the diagnostic input; do not manufacture a clean effect result | Observer for reports; effect owner for actual unknown outcome |

A model session is not the durable storage system. Conversely, durable storage does not establish provider
success. A terminal item outcome may coexist with an outstanding effect settlement; that settlement
continues to reserve its needed authority and resources and blocks conflicting actions as required.

### 6.3 Release and fleet adoption

Consume the simplified release path delivered by the predecessor. Preserve the installed coherent-set,
OIDC, byte identity, feed verification, provenance and recovery obligations for the enabled release class.
Do not reinterpret release convenience as permission to reduce those contracts. A same-identity replay
converges only after byte verification; mismatched bytes refuse. A PR-less operation completes from its
native verified result rather than a fabricated source PR.

Publish producer changes before receiver adoption. Batch compatible tool/registry updates with one coherent
adoption operation when the release contract permits; do not bundle unrelated product behavior simply to
make overhead ratios look smaller. Pin stable tools for active work and support explicit continuation and
history interpretation across upgrades without pretending old evidence was produced by the new tool.

## 7. CI, observation and performance contracts

### 7.1 Optimize in the order that preserves meaning

Use the predecessor's decided obligation set. First remove duplicate invocations and unrelated synchronous
views. Then use accepted content-based reuse and shared setup. Only afterward consider a different
selection guarantee, batching strategy or predictor. GS2-06.7 sound selection, unknown-impact handling,
formal-input drift, and comprehensive closure retain their accepted meaning until amended.

The accepted reuse optimization is the four-disposition contract in
[ADR-0084](adr/0084-semantic-reuse-never-cancels-coherent-validation.md): `current`, `reused`, `deferred`, or
`failed`. Classification runs first; an accepted classification starts the coherent run, and valid reuse starts
delivery alongside it. Independent tests shard where safe, and coherent candidates may overlap. Valid reuse may leave coherent validation pending after merge; only a later
pass closes it, while a late failure marks it disputed and blocks dependent acceptance or activation.

One stable aggregate can expose many named predicate results. Missing required evidence must not become
success, and an advisory result must not become blocking because a client indiscriminately treats every
red context as required. Policy/evaluator identity comes from adopted authority; candidate changes to the
selector, policy or enforcement workflow take their applicable protected route.

Coalesce pending refresh hints for the same subject, not durable commands or distinct subjects. Preserve
grant/approval changes and non-idempotent operation identity. Cancellation of superseded analysis is
different from cancellation of an applying external effect. Full scheduled audits repair missed hints;
routine merge should not await a full fleet scan unless a named required predicate actually depends on it.

The completed Choreo work supplies concrete regression requirements for formal qualification: bind model,
tool/profile, gate scripts, source/license provenance, raw fixtures and retained counterexamples into the
applicable reuse identity; demonstrate invalidation for changed inputs and preservation for unrelated prose.
Verify that named scenarios actually execute and that negative controls emit their expected failure evidence.
Enforce verifier deadlines on child processes, with explicit bounded infrastructure retry accounting; neither
an empty successful exit nor a killed verifier proves a property. Partition bounded models when justified,
record their coverage and exclusions, and measure preparation, execution, queueing and superseded-run cost
separately. These belong to the owning qualification gates, not a manual checklist for every routine PR.

### 7.2 Measurement has four separate statuses

1. **Record validity:** schemas, units and arithmetic are meaningful.
2. **Join integrity:** the record identifies the actual request/attempt, PR or operation, source and policy.
3. **Population coverage:** independently enumerated activity or provider totals bound missing usage.
4. **Qualification:** the declared cohort, uncertainty, performance and follow-up conditions pass.

The observer must not collapse the first into the fourth. Extend existing observation output only where
needed; no per-item agent writes a measurement certificate. Aggregate public counts without publishing
private conversations, credentials or raw usage receipts. Retention and access should fit the existing
private evidence contract and cohort follow-up period.

Functional acceptance is separate from measurement qualification. For the selected clean-start
ordinary-source profile, typed missing, corrupt, unsupported or unreachable observation can leave otherwise
valid native delivery intact while measurement remains unknown or not evaluated. Observation never supplies
source, check or protected-effect authority. The R2 functional gate can therefore pass while the economics
qualification remains insufficient.

Use separate populations for the migration workflow, incumbent routine development, ordinary v2, and each
optional controller experiment. Initial v2 R5 measurement uses the predecessor's common definitions:
productive work P, process overhead O, and quantified unknown usage U. Charge planning, semantic review,
coordination, delivery, failed attempts, observer/tooling maintenance and attributed repairs appropriately.
The measured ratio is O/(P+O); the conservative ratio is (O+U)/(P+O+U). Unquantified missing usage prevents
an efficiency qualification, even if the supplied records balance.

For the predecessor's broader comparison, preserve its **10% objective and 20% ceiling**, at least ten
candidate and ten comparable baseline code items for the first viability comparison, at least 95%
independently assessed usage coverage, and the
conservative bound within the ceiling. The source also requires every successfully measured routine item
to fit 20%; keep outliers visible and do not quietly relax that criterion for small changes. If fixed
cost makes it unsuitable for a work class, propose a versioned absolute-budget alternative before using
it for promotion. No denominator change, task enlargement or rerouting may manufacture a pass.
This broader source-contract series is not an alternative to the successor's **10% bureaucracy ceiling**
in section 7.4; report the two definitions separately. Any affected adopted promotion predicate requires
its owning versioned amendment before implementation uses the successor definition.

Report tokens by provider semantics, priced cost, runner time, API calls, human attention and wall time
separately. Cached input counts once; avoid double-counting reasoning already included in output. Price
changes and provider counters need identified measurement versions; tokens are not interchangeable money
or compute seconds. Shared work is allocated once under a stated rule and also shown as an absolute total.

### 7.3 Outcomes and experimental discipline

Compare useful delivered fraction, cost per delivered unit, lead time from the first eligible request,
age of unfinished work, escaped regressions, rollback/recovery effort and attention. Source merges are
not deployments or published packages. Use DORA-style deployment measures only for actual deployed services;
do not call a PR count deployment frequency.

Choose eligible requests before outcomes are known. Prefer interleaving or a documented matched/switchback
comparison that accounts for shared runner interference. Historical unmatched comparisons remain
observational. Failed, cancelled, refused and unfinished requests stay in the denominator; whole attempt
lineage and delayed repairs stay with the original policy. Specify primary improvement, margins, sample
sufficiency and stopping rules before reviewing results.

Ten items per route is a viability check, not p95/p99 or rare-defect equivalence evidence. Keep runtime
absolute caps separate from distributional promotion targets. Small samples return insufficient-data;
they can justify a bounded continuation, not a general tail-latency claim. Retain the predecessor's
severe-incident and early-rollback stop rules.

**Current R5 observation amendment, declared prospectively at 2026-09-29T04:06:41Z.** The first ten
distinct completed canonical roadmap/original items across independently progressing BARC-01, SC2C-01
and LEARN-01 replace R5's fixed 30-day repair gate; future-selected FOURD-01 originals join prospectively
at `2026-09-29T04:15:52Z`. TODO-01, TTT-01, SNAKE-01 and HELLO-01 join prospectively at `2026-09-29T06:31:33Z`, each selecting one whole app original under the [additional-app enrollment](roadmaps/2026-09-29-r5-additional-app-enrollment.md). BARC-01.4, SC2C-01.4e and LEARN-01.3 were unresolved and explicitly enrolled
at the first declaration. Their known partial results and missing earlier usage stay with those lineages;
no already completed item is selected retrospectively. Completed FOURD-01.1–.5a work and its pre-existing
.4-E acceptance cannot count. Stable original identity spans retries and repairs. Actions, games, CI jobs,
worker turns, projection-only documentation and failed attempts do not count.

A completion requires the owning acceptance's native `Done` readback and actual source delivery.
Publication, installation and activation remain separate claims. Failed, cancelled and pending attempts
stay in the denominator and repair cost without advancing the count. The tenth completion fixes the
cohort cutoff; charge all escaped regressions, interruptions and repairs observed by that cutoff to their
originating items. There is no fixed elapsed wait. Credential exposure, irreversible data loss or an
authority breach stops candidate admission immediately; two process-attributable rollbacks in the first
ten completed items stop expansion for focused diagnosis.

The independent baseline-ten plus candidate-ten comparison, at least 95% usage coverage, predecessor
10% objective and broader 20% ceiling remain separate. Completion of the item window does not certify
efficiency or adoption, change operation authority, or satisfy Q10's 15-item gate. Missing usage remains
unknown; no counter or collector installation is inferred. Historical schema-1 snapshots retain the
30-day rule, and the current schema-2 cohort record applies this completed-item policy.

The cutoff accounting covers all 15 enrolled originals through `2026-09-29T07:15:35Z`, including the 12
delivered originals and pending SC2C-01.4e, BARC-01.5 and LEARN-01.4. Completing repair observation does not
mark a pending original delivered. Usage and repair cost remain missing and provider population remains
unknown. The evaluator removes only `routine-completed-item-repair-accounting-pending`; its remaining
insufficiency reasons continue to block an efficiency claim.

Missing efficiency evidence blocks an efficiency claim and applicable controller promotion. It does not
independently block a valid routine merge or add a numerical gate to OpenV2/OperatingV2. Existing Q10 gates
still apply. Functional or authority failure is different: an enabled profile cannot claim qualification
while its required behavior is broken.

### 7.3.1 Stable policies, context efficiency and statistical learning

The September 12 planning direction is **broad, stable execution policy followed by increasingly precise
evidence**, detailed in [LEARN-01](roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md).
Collect task characteristics, execution choices and outcomes from the beginning, but begin routing with
a few fixed profiles. Classification uses information available before assignment and preserves its
version, uncertainty and provenance. Analysis refreshes do not change policy. Finer task distinctions,
model/effort/context routing and adaptive allocation need sufficient independent observations, practical
benefit with uncertainty, repair follow-up and explicit versioned promotion at declared evaluation points.
Insufficient evidence means continued observation or an inconclusive result, not automatic promotion.

**Context and token efficiency are first-class improvement goals.** Evaluate total original-issue
resources, including Astra planning, context assembly/retrieval, all workers, retries, reviews,
integration and delayed repairs. Mandatory instructions, contracts and technical evidence remain present.
A smaller initial prompt only helps if total resource use improves while completion quality and latency
remain acceptable. Keep provider-native accounting, subscription capacity and known monetary costs
distinct. Children, rescue attempts and new PRs do not create extra independent successes or reset costs.

LEARN-01's first proposed controlled comparison holds model, effort, decomposition and process fixed
while comparing the current context package with a focused package. Randomization, cohort admission,
assignment units, follow-up, sample/precision requirements and analysis rules are specified before use.
Its proposed minimum four-week enrollment and bounded evaluation cadence are research-design inputs,
not new per-PR gates. Historical associations, prediction, simulation and causal experimental results
have distinct claims. The selected roadmap owns this detail; sections 7.2–7.4 retain their existing
accounting and intervention definitions. Necessary repairs proceed under their existing authority and
mark materially affected experiments interrupted rather than silently mixing policy versions.

The later community path keeps local analysis available without sharing. Explicitly opted-in clients
submit minimized statistical reports to an isolated HTTPS intake on Main, with separate credentials,
storage and no dispatch or internal-store access. Private validation and disclosure review precede
public aggregate dataset releases in a dedicated GitHub repository. Sharing observations and joining an
experiment are separate opt-ins; removing names alone does not establish anonymity. Community
self-selection, measurement differences and unverified submissions remain visible in analysis.
Actual hostname/repository selection, publication and deployment belong to LEARN-01.6; no intake or
collection is activated here, and community participation does not gate the internal comparison.

This design extends the existing observation path and selected O0–O3 runtime. It does not establish
measured benefit, complete R2/R4/R5 or E0/O2/O3, or add a prerequisite for V0–V6. Live execution and
experimental enrollment retain their owning operational permissions.

### 7.4 Narrow bureaucracy budget: tests excluded

The requested bureaucracy budget concerns administrative checks, waiting for checks/CI, and process churn.
It is narrower than the predecessor's broad overhead measure, which also includes planning and semantic
review. **Target 5% bureaucratic overhead, with a 10% ceiling.** Retain the broader
10%/20% measure separately so this narrower definition does not hide the cost of review or planning.
The intervention rule is **15 cumulative distinct items above 10%, or any one item above 25%**. An item
above 10% breaches the ceiling immediately, but does not by itself launch a repair cycle or block delivery.
These are the requested design thresholds, not constants established by external research or additional
currently accepted GS2/per-PR gates. The 25% trigger is not a second permissible ceiling.

| Dimension | Proposed routine budget | Interpretation |
|---|---|---|
| Active owner effort spent administering the process | Target at most 5%; ceiling 10% | Administrative active effort divided by all attributed active effort |
| Model usage spent administering the process | Target at most 5%; ceiling 10% | Administrative usage divided by all attributed usage under fixed provider/classification semantics |
| Attributed CI runner time and cost spent on administrative work | Report absolute amounts and their share of CI | Diagnostic breakdown, not a separate trigger: a tiny docs-only CI run can be entirely administrative while adding little overall item cost |
| Added critical-path delay from administrative execution, CI queueing, check dispatch/reporting and process repair | Target at most 5%; ceiling 10% of end-to-end item lead time | Exclude actual useful test execution from this numerator; retain administrative waiting, including provider outages, with cause attribution |
| Absolute administrative delay | Diagnostic aims: median at most 2 minutes; p95 at most 5 minutes | Show beside the fractions; these are not additional per-item intervention triggers |
| Required agent polling/status-restatement turns | Zero on the normal path | Events or bounded machine polling observe native facts; no model session exists solely to wait |
| Receipt-only or status-only PRs | Zero | Native delivery and operation results remain the source; observers publish derived views asynchronously |
| Administrative recovery | At most one automatic recovery attempt before a visible pending/failed diagnostic | Retry only when safe; this does not cap or abandon required settlement of a real external effect |

For active effort, the numerator is effort on eligibility paperwork, state copying, check-status handling,
delivery ceremony and repairing those mechanisms. The denominator is total attributed active effort,
including useful implementation, test-related work and substantive review. Use the analogous definition
for model usage, measured separately. Automated administrative compute is also reported in absolute
runner-seconds and cost; moving model work into a runner does not make it free.

Actual execution of useful product tests, security analysis or formal verification is **not bureaucracy**.
A workflow's label does not determine its classification: a required test remains useful validation, while
a required check that merely revalidates duplicated process records remains administrative. Attribute the
administrative or avoidable part of setup and reruns explicitly; report necessary technical validation
separately. An expensive duplicate rerun is a separate avoidable-CI cost even when its test body is excluded
from the narrow bureaucracy numerator, so the definition cannot hide wasted testing.

For elapsed delay, count only intervals on the item's actual delivery critical path. A CI queue wait
overlapping productive implementation does not add its full duration to delivery time. Parallel waits
are counted once, and actual useful test execution is removed from the administrative interval rather
than counted as both testing and waiting. Show total latency and useful test duration alongside this
decomposition. Missing timing attribution remains unknown; do not subtract guessed test time to obtain
a passing bureaucracy budget.

Use the absolute delay aims beside the percentage measures: fixed administration can dominate a tiny
change's ratio, and a large implementation can conceal excessive waiting. Keep such outliers visible;
do not enlarge tasks to improve the fraction. Insufficient samples cannot qualify p95. Avoid treating a
good cost ratio as evidence of acceptable waiting: the dimensions are measured independently. No weighted
average lets a cheap model session cancel out a blocked CI queue.

**Counter and trigger semantics.** For each usable percentage-budget dimension above, compute administrative amount divided by
total attributed amount in the same units. Count a routine item once if any dimension exceeds 10%; trigger
the severe condition if any exceeds 25%. Never add minutes to tokens or count the same item three times.
No activity in a dimension is not applicable; missing activity or missing attribution is unknown, not zero.
Provider usage that cannot be combined meaningfully stays separated, with priced cost reported alongside.

The counter covers distinct original routine items since monitoring started or the last completed
intervention. It is cumulative, not consecutive and not reset by a good item, a week boundary, another PR,
a retry or a new worker. Evaluate complete item totals at delivery or a definite failed/stopped outcome,
and revise them when attributable follow-up cost arrives. An initial administrative prefix before useful
work is not a complete item with 100% overhead. Unfinished items, their provisional costs and their age
remain visible; leaving them open must not remove them from population coverage or total cost reporting.

At the fifteenth distinct breach above 10%, or the first item above 25%, enter one intervention state.
Exact 10% does not count; exact 25% counts as a normal breach but does not alone trigger the severe path.
The severe path acts on the first usable observation and does not wait for fifteen items. Replayed events,
later samples and multiple breached dimensions update the existing item. Corrections remain auditable;
they may correct a mistaken count but cannot erase real expense. Missing or bounded-but-inconclusive
attribution cannot certify compliance or fabricate a confirmed breach. Surface one deduplicated observer
health diagnostic for that gap, not a new report request on every item.

While intervention is open, further breaches join it. Do not spawn another intervention for every new
event or pause otherwise valid routine merges. The explicit tradeoff is that modest breaches can persist
until fifteen distinct items accumulate, especially at low throughput. Their count, age and absolute cost
stay visible without inventing a second automatic escalation rule.

**Aggressive intervention, with a low administrative cost.** Prioritize removing the measured sources of
overhead: delete duplicate checks and mirrored records, take derived projections off the merge path,
combine refresh hints, eliminate agent polling, share setup and repair the existing recovery path. Start
with the largest attributable costs. Prefer deletion or a small deterministic fix over another controller,
reporting layer or universal review. Retained technical predicates and unsettled effects keep their meaning.

One accountable owner uses the existing log to make the bounded implementation change and inspect its
real before/after behavior. Target a return toward 5%, not oscillation just below 10%. A completed
intervention means the fix is deployed to the affected route and ordinary-path evidence demonstrates the
claimed reduction; changing a policy number, producing a report or scheduling future work is insufficient.
Then begin a new counter epoch. Preserve the old epoch, every item's original lineage and all intervention
cost. Do not reset on opening the intervention. If the fix fails, continue the same intervention; new
post-fix breaches can trigger the next one once the previous intervention is actually complete. Historic
breaches do not re-trigger merely because the observation job reruns.

Charge diagnosis, implementation, verification and logger maintenance to shared process overhead under
the existing fixed allocation rule, and show their absolute cost separately. This makes an intervention
that costs more than it saves visible. Use ordinary implementation verification, not a new acceptance
artifact family or an overhead check on every intervention step. Genuine incidents and authority failures
still receive their existing immediate response; the fifteen-item rule batches overhead repair only.

This separation between a service objective and an actionable response is consistent with
[Google SRE's alerting analysis](https://sre.google/workbook/alerting-on-slos/), which evaluates precision,
detection and reset behavior and shows how immediate threshold alerts can generate excessive noise.
The particular 15-item and 25% choices are a local design decision, not a result established by that source.

**Comprehensive automatic logging is required for the design to work.** Extend the existing observation
path instead of creating a second ledger. Capture the complete population, including no-op, failed,
cancelled and retried work, and retain enough machine evidence to reconstruct each reported ratio:

| Evidence | Minimum useful content |
|---|---|
| Identity and lineage | Original item, attempt/run, repository, source/base where relevant, effective process/policy version, observation and intervention epoch |
| Event and timing | Trigger event and activity when available, occurrence/ingestion, dispatch, queue admission, job start/end, native delivery or stopped outcome, with clock/provenance gaps |
| Resource use | Provider-native usage counters and pricing identity, active effort when observable, runner duration/cost, API calls, shared-cost attribution, with collection coverage |
| Attribution | Useful implementation/validation, useful test execution, administration, administrative waiting, duplicate work and unknown; overlap and critical-path accounting |
| Churn and outcome | Retry/rerun cause, superseded head, reused evidence, process repair, actual merged/published/pending result, later regression/recovery links |
| Intervention state | Distinct breach IDs and dimensions, trigger reason, one owner/change reference, deployed fix, measured effect, reset reason and full intervention cost |

Record events and deltas automatically; do not require a model to narrate phases, restate check status,
reconstruct unavailable human time or approve each observation. Reconcile against independent request,
run/attempt and usage populations. Preserve raw source references and explicit collection gaps; corrected
derived aggregates remain reproducible. Logging loss does not stop valid delivery, but an incomplete
population cannot prove the overhead ceiling was met. Capture all work, not unrestricted content: avoid
credentials and private conversation bodies, use the existing evidence access controls, and retain source
evidence through the declared comparison and repair follow-up. Logger/observer failures and their repair
cost are part of the accounting, rather than reasons to start one ceremony per affected item.

Migration, release and cutover use separately declared absolute administrative/wait budgets derived from
their rehearsals and operating requirements. A temporary protected-operation exception does not raise the
routine allowance. Crossing an intervention threshold is not permission to bypass a required technical predicate or
abandon an indeterminate effect. The routine profile's eventual adoption should explicitly decide these
narrow budgets; the predecessor's existing broad performance contract retains its meaning meanwhile.

### 7.5 Observed bottleneck: full-board projection on the merge path

The measurements and workflow/protection observations in this section are from **September 7**.
They motivate the retained optimization criteria; re-observe the current workflow and enforcement before
assigning a repair or claiming this remains the live bottleneck.

The current [board lifecycle workflow](https://github.com/FS-GG/.github/blob/86669a2ccb4a23e42609f55b4fe8f85e6a5104a3/.github/workflows/coord-board-reconcile.yml)
admits a run for each of the following events, with no changed-path filter:

| Trigger | Activity |
|---|---|
| Pull request | Opened, reopened, edited, synchronized by a push, or closed |
| Pull request review | Submitted, edited or dismissed |
| Schedule | Minute 17 of every hour, subject to provider scheduling delay |
| Manual dispatch | Explicit workflow run request |

Issue/comment events were already removed after earlier queue incidents. Each admitted run builds the
engine and, when its API-budget preflight permits, invokes the full-board applying reconciler. All runs
share one repository-wide concurrency group, with `cancel-in-progress: false` and `queue: max`.
[GitHub documents](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)
that this permits one active run and up to 100 pending runs; a larger queue preserves work but does not
increase service capacity. Filtering a job after workflow-level admission does not prevent it taking a
queue slot.

At approximately 16:10 UTC on September 7, the live API showed one active and 17 pending runs. Several
pending pairs shared a PR head; the run metadata identifies `pull_request`, but does not distinguish the
exact activity that produced each pair. One [completed run](https://github.com/FS-GG/.github/actions/runs/34140348581)
was created at 15:50:35, started its job at 16:08:24, and completed that job at 16:10:09: **17m49s before
job start versus 1m45s of execution**. This is a measured queue incident, not a p95 estimate. While the
board job's bare `reconcile` context was required, it also blocked this prose PR after its other required
checks passed.

A fresh branch-protection read at 16:10:38 UTC showed `architecture-map reconcile` required instead of
the bare board `reconcile`. That live configuration change removes this particular required-check wait;
this document did not perform it. The architecture-map check verifies a different concern, and the
observed replacement is not evidence that the two jobs are semantically equivalent. The full-board
backlog and its runner/API cost remain even when they no longer block merge.

The proposed simplification carryover is concrete:

1. Keep derived board projection asynchronous. Give each retained merge-blocking check a specific
   code, contract or authority predicate; do not use a global projection pass as a proxy for those
   predicates. Qualify the actual protection configuration and installed delivery helpers together.
2. Combine redundant state-refresh hints and reconcile affected items, while retaining a periodic full
   audit for missed events. Keep authorization commands and unsettled external effects distinct from
   disposable refresh hints; losing a hint must be recoverable from native facts.
3. Preserve the existing writer's serialization until its non-idempotent writes have a qualified
   replacement. Per-PR concurrency or cancellation of an applying run is not a safe shortcut. For this
   current writer, reduce admission volume before increasing concurrency.
4. Measure trigger counts, pending age, dispatch-to-job delay, scan scope, API cost and projection age
   automatically. Exercise a realistic event burst, duplicate hints, missed events and budget deferral;
   verify that an unrelated routine PR still completes within the proposed administrative delay budget.

This is a predecessor simplification repair to consume at handoff, with v2 narrow reconciliation and
missed-event behavior qualified in GS2-07.7. It does not justify waiting for an OR scheduler or building
a second general executor. Reuse a verified fix if it has landed before this successor is activated.

## 8. One optional PB/OR extension path

The selected [standalone O0–O3 work](roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md#o2-source-window-after-the-provider-session-correction)
has brought forward the trusted single-host Akka.NET execution foundation from this later path.
Its existing roadmap is the implementation and acceptance ledger: provider-neutral session supervision,
PostgreSQL execution persistence, bounded subscription admission, the container executor and Main effect
driver belong there. Sections 8–9 now describe reuse and the remaining extensions, not a second build of
that foundation. Source delivery, installed qualification, live O2 acceptance and controlled O3 adoption
remain separate. This synchronization records the already selected scope; it does not claim comparative
benefit, activate a wider service, or change v2 cutover prerequisites.

### 8.1 Investment trigger and smallest experiment

After the simplified supported route is measured, select one residual problem: repeated CI setup, an
unmet unattended-recovery need, or a real shared-capacity scheduling bottleneck. Begin with existing
deterministic tools. A richer executor, host or optimizer is justified only if the baseline cannot meet
the named requirement at acceptable total cost.

Proposed initial funding limit: **five engineering days for one experiment**, with a scope/stop decision
after two days if no executable comparison exists. These are investment caps, not delivery estimates or
new per-PR gates. Name the expected operating volume and maintenance burden. Set the material improvement
threshold from the baseline before measuring the candidate. If there is no credible benefit after build,
operation and recovery costs, retain the baseline and close the experiment.

Do not put the full six-graph planning catalogue, every model route, all dashboards, or federation before
that first comparison. Bound the experiment to one operation class and the failure cases relevant to it.
Independent read-only research can occur before v2 completion without becoming a migration prerequisite.

[LEARN-01](roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md) now supplies
the selected planning question for context/token efficiency: establish the actual broad-profile baseline,
then compare one focused context package under a fixed process. Its source/research window can proceed
before live pilot authority; it does not claim that the residual cost or comparative benefit is already
measured. The engineering investment cap and the separately bounded observation/follow-up window are
different budgets. Reuse its dataset and analysis rather than starting another E0 measurement programme.

### 8.2 Bounded execution component

When justified, Coordination owns a small canonical workflow with finite scope and a non-renewable
attempt/transition budget. Repair, polling, restart, replacement identities and new operation IDs cannot
renew the original allocation. Item outcome and effect settlement are distinct. Deadline/budget exhaustion
stops new work while the assigned recovery path settles existing effects.

Model and implement reservation before concurrent dispatch. Atomically bind the local decision, effect
intent and capacity reservation to an expected state version. Keep spent plus outstanding reservations
within the item and shared allocation, including retries, cancellation lag and settlement headroom.
External grant acquisition remains a separate recoverable protocol; a local reservation alone never
authorizes provider work. Avoid distributing a local transaction across actors and assuming message order
makes it atomic.

Where an adapter cannot enforce an upper bound, label its budget observational and exclude it from a route
promising a strict aggregate cap. Estimated p95 cost is not a maximum charge. A timeout does not release an
outstanding reservation. Reserve actual recovery capacity, including a viable strategy for a one-slot
runner pool, and record the owner that can use it without the optional host.

Publish one model/contract and share implementation across CLI and host entry points. Qualify finite
profiles, witnesses, negative controls, ITF/reducer correspondence, crash recovery and upgrade behavior.
Keep persisted intent and inbox/outbox atomicity explicit for the chosen store. No second external
completion authority is created by local receipts.

### 8.3 OR scheduling over the shared component

The [LEARN-01 feature plan](roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md)
owns broad fixed profiles, context comparison and evidence-conditioned later refinement. Detailed
observations do not require granular dispatch rules; learned allocation and additional planner investment
remain conditional on repeatable results from that stable baseline.

Start with a deterministic priority/FIFO policy with aging, actual resource constraints, bounded WIP and
recovery headroom. Admit work according to the bottleneck's available capacity; increasing implementation
parallelism while review or integration queues grow is not progress. Observe demand before admission so
the scheduler cannot hide queueing by postponing its start timestamp.

Advanced allocation, work shaping, value-of-information review, CI optimization and model routing are
separate planners behind stable contracts. They propose allocations and sequencing. An independent
feasibility check verifies legality, capacity, evidence closure and recovery. Utility never overrides a
hard predicate. Commit a short horizon and use bounded replan triggers/hysteresis to avoid churn.

Historical replay tests decisions on observed facts, simulation tests declared assumptions, and shadow
tests live feasibility and its own overhead. None observes the outcomes of unexecuted counterfactual
actions. Real comparative value requires an authorized canary. A baseline plan or solver timeout must not
be labeled an optimal solution.

### 8.4 Hosting and runtime selection

For the selected O0–O3 single-host scope, Akka.NET is already the implementation choice: Main owns
durable orchestration and execution-session actors; a provider-neutral boundary connects the bounded
container executor. The [source window](roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md#o2-source-window-after-the-provider-session-correction)
records the historical core/adapter/persistence and Main composition delivery. The selected O0–O3 installed
pilot and controlled adoption are now accepted at their recorded artifact identities. The later Choreo replay
and two production fixes are separately qualified source; installed inclusion must be established by exact
artifact readback and the section 9.6 follow-up. Do not rerun runtime selection or create a competing executor
for this scope.
Its selected trusted same-user profile retains the explicit deferral of stronger credential isolation;
it does not qualify hostile contributors or the federation trust boundary below.

Keep the supported CLI/workflow path. For an additional, unselected hosting need,
compare the same small failure-heavy lifecycle using the existing route, the OR-preferred Akka.NET option,
and a workflow-oriented option such as Temporal. Test duplicate input, lost provider response, process
loss, cancellation, schema upgrade and one credible concurrent-child extension. Compare effort, debugging,
operational footprint and remaining custom recovery code. Select one runtime; do not build two production
executors or adopt clustering from a future feature list.

An enabled host needs authenticated human/machine sessions, per-command authorization, durable
acknowledgment, bounded replay, sandboxed workers, scoped credentials, separate failure domains,
backup/restore, key rotation, retention, an operating owner and an outage-tested CLI/recovery boundary.
Restore must not resurrect stale authority. Start single-node unless measured recovery targets demand
more. Federation adds a separate trust boundary and is deferred until local execution has demonstrated value.

UTEL-02 contributes one concrete datapoint to that later selection: concurrent workers producing observations
for one host-local writer naturally fit actor mailbox serialization, supervision/durable delivery and explicit
parent-child correlation. Its immutable inbox batch is deliberately compatible with a future per-host
telemetry-writer actor message contract. This remains evidence for additional runtime comparisons, not
proof of production telemetry-writer actor adoption: the SQLite lock and native identity/digest
deduplication remain necessary across process recovery, upgrades and old/new overlap even if an actor
owns normal-path drains.

Begin observation with one compact table and inspectable operation history. Rich UI is selected by an
actual operator question. Enabled charts, graphs and exports still require accessibility, truthful
partial/stale states, secure content handling, canonical identities and bounded rendering cost; rendering
state can never authorize a retry or effect.

The selected [V2-LANG-01 amendment](roadmaps/2026-09-29-language-independent-workspaces-and-agent-integration.md)
separates the coordination runtime, agent implementation and product languages. Publish portable
workspace/toolchain contracts and qualify non-.NET and mixed-language products without an Akka or
agent-framework dependency in product code. Evaluate AG-UI for read-only presentation and Microsoft
Agent Framework for bounded attempts, keeping independent roadmap lanes outside a shared workflow
barrier. These prospective integration windows preserve the accepted V2 profile, original budgets,
effect ownership, frozen R5 cohort and current host execution restriction.

### 8.5 Cooperative orchestrators: a project master assigns jobs to contributor clients

**Retain the cooperative feature:** a user's client orchestrator connects outward to a project master
orchestrator, offers bounded capacity, accepts jobs, supervises local agents and returns contributions.
The original OR design calls this
[federated cooperative orchestrators](coordination/2026-08-31-operations-research-first-agent-orchestration-design.md#8a-federated-cooperative-orchestrators--proposed-extension).
It remains a later conditional capability in this unified design. Its user journey and development stages
belong here; the source section retains the detailed protocol, threat model and qualification catalogue.

“Master” means the orchestrator accountable for one project's assignments and delivery. Client and master
are roles in a relationship: the same installation may own its projects and contribute to several other
masters. A contributor initiates an outbound connection, so participating does not require exposing an
inbound public service. Each side retains its own state, policy and local resource limits.

| Component | Responsibility |
|---|---|
| Project master / project-owner orchestrator | Offers and selects jobs, retains external claims and current generations, chooses verification policy, accepts a candidate and performs protected delivery |
| Client / contributor orchestrator | Enrolls approved masters, advertises capacity, admits jobs under local policy, reserves resources, supervises sandboxed agents and submits results |
| Owner-controlled verification service | Independently checks the submitted candidate in suitable isolation; supplies evidence to the master without acquiring a second delivery-authority role |

The normal user journey is:

1. Enroll the identified master and agree project/data scope, availability, cost, model/tool permissions
   and eligible job classes. Both sides approve the relationship. Local policy can preauthorize job
   classes, so ordinary assignment admission does not need a fresh human prompt for each job.
2. Connect and advertise available capacity. The master offers a job; the client accepts or declines
   within its own limits. An offer alone does not authorize execution or project mutation.
3. Receive and durably acknowledge a bounded assignment tied to the project, intended client, exact
   baseline, allowed scope, current generation/epoch, budget and expiry. The master retains the external
   claim; the client receives no ambient project GitHub or release credentials.
4. Execute through the shared bounded component and local agents. Return the candidate, artifacts and
   attributed observations with their assignment identity. Remote progress, submission, verification,
   acceptance and actual delivery remain distinct visible states.
5. The master quarantines the submission and obtains owner-controlled verification. It selects a valid
   candidate and delivers through the ordinary protected path, then reports the observed outcome.

Reconnects and lost acknowledgments resume by stable assignment/attempt identity. Duplicate submissions
must not create duplicate delivery; late results from revoked or reassigned generations cannot authorize
new effects. Disconnect, pause, cancel and revoke retain their separate recovery meanings. Neither side's
timeout erases an unresolved external operation or renews a budget.

Both sides treat incoming project code, instructions and artifacts as untrusted. Client-signed test
reports are claims, not independent proof that tests ran or passed. The verifier protects its own policy,
credentials and caches from submitted code. Enrollment does not grant access to unrelated local projects,
host credentials or private telemetry. Onward delegation, anonymous contributors and payments stay outside
the first enrolled-peer mode. A narrow versioned application protocol connects peers; they do not join
one actor cluster or share a journal. The source proposes SignalR transport; F0 owns the final protocol
and identity selections rather than silently fixing a second runtime in this consolidation.

Use the same scheduling, bounded execution and observation components as E1. Account for transfer,
administrative waiting, verification capacity, integration, recovery and maintenance when judging benefit.
Useful test execution retains section 7.4's exclusion from bureaucracy. Client-reported usage remains
explicitly classified and cannot certify the 10% ceiling merely because it is signed. Comprehensive logs
join both sides by assignment while retaining disclosure boundaries; verification and recovery capacity
must be reserved so more clients do not merely lengthen the master's queue.

## 9. Unified roadmap from the current v2 frontier

ADR-0091 replaces the active V0–V6 cutover sequence with this bounded clean-start route:

| Stage | Work | Observable exit |
|---|---|---|
| **C0 — Deliver source** | Use ordinary GitHub delivery with current authenticated identity, applicable checks and an exact-head merge condition. Preserve native branch protections; do not force-update or use an admin merge. | The intended source is merged at the checked head and independently read back. |
| **C1 — Start `.github` clean** | From `OperatingV1`, a one-shot repository administrator appends a fresh shared `OpenV2` generation in the authority repository under a temporary narrow cutover-ref writer grant, then restores and reads back the exact prior permission. The existing published CLI 0.1.2 and workflow consume that epoch to activate `.github` policy. | The shared generation, restored permission and `.github` policy activation are independently observed. No migration or `VerifiedV2` receipt is invented. |
| **C2 — Smoke real use** | Run one real working journey and one ordinary rerun through the activated `.github` policy. | Both journeys complete with native provider readback; failures are repaired forward. |
| **C3 — Roll out explicitly** | Bundle ready profiles in one immutable CLI release when practical; prepare selected receivers and dedicated credentials concurrently, then activate a bounded wave separately per repository. Keep generated/scaffold defaults unchanged. | Each selected repository has a clean pinned install, real settlement, independent Authority readback and normal already-complete rerun. Its current required checks, custody and exact-head merge remain separate; unselected repositories retain their behavior. |

The first combined C3 wave used published [Coordination CLI 0.1.5](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.5) for Net, Governance and Game. Its [publisher run](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36437778484) passed anonymous installation and dual-feed readback at the pinned source. [Net #107](https://github.com/FS-GG/FS.GG.Net/pull/107), [Governance #439](https://github.com/FS-GG/FS.GG.Governance/pull/439) and [Game #670](https://github.com/FS-GG/FS.GG.Game/pull/670) each merged at its checked head and completed a real protected settlement, independent Authority readback and an unchanged `SettlementAlreadyComplete` rerun ([Net run](https://github.com/FS-GG/FS.GG.Net/actions/runs/36441643056), [Governance run](https://github.com/FS-GG/FS.GG.Governance/actions/runs/36444065570), [Game run](https://github.com/FS-GG/FS.GG.Game/actions/runs/36444699657)). All three selected receivers are adopted at that bounded clean-path boundary; this is not an efficiency or historical-upgrade claim.

The next shared-release wave selected SDD and Templates. Their disabled receivers merged separately ([SDD #1080](https://github.com/FS-GG/FS.GG.SDD/pull/1080), [Templates #638](https://github.com/FS-GG/FS.GG.Templates/pull/638)); at that stage neither had a package pin or active settlement. [Coordination #877](https://github.com/FS-GG/FS.GG.Coordination/pull/877) combined both profiles at protected source `275cccb30a5c9ade4b3bba344ede13d7df446d13`; its exact-source [CLI 0.1.6 preparation](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36450952246) and [protected publisher retry](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36457989575) passed. The protected `v0.1.6` tag points to that source, and the [public release](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.6) passed both-feed payload readback and anonymous clean installation. Sealed custody [PR #3944](https://github.com/FS-GG/.github/pull/3944) merged at `3ed9b4f8316a14c5e15bfb4c1e11554730b9ae3f`; its [SDD](https://github.com/FS-GG/.github/actions/runs/36450825631) and [Templates](https://github.com/FS-GG/.github/actions/runs/36450830306) bridge runs succeeded. Each receiver has independently read-back main-only environment policy and its three dedicated encrypted secret names. The public package is now verified. [SDD #1081](https://github.com/FS-GG/FS.GG.SDD/pull/1081) merged on protected main `1a081fdef954f08f8cacf44b26c80fac784e0c28`; [run 36460253176](https://github.com/FS-GG/FS.GG.SDD/actions/runs/36460253176) settled once and its normal rerun returned `SettlementAlreadyComplete`, with independent Authority shard `fd` readback unchanged. SDD is adopted at this clean-path boundary. [Templates #643](https://github.com/FS-GG/FS.GG.Templates/pull/643) merged on protected main `938f9b11ee1f148ff495774d346b011c9f23d394` after its native checks passed. Its [ordinary run 36463172201](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36463172201) settled once; the normal whole-workflow rerun returned `SettlementAlreadyComplete` with the same receipt `95e74b3d8652c83c1b856e3b1e58474c2688761e2aed6acb3d68a9457386a506`. Independent Authority shard `b6` readback found its head unchanged at `71bf408a68ca0758746fc08f716a1caacc6b0d5f` and exactly one completed effect. Templates is adopted at this clean-path boundary.

Independent source lanes also advanced: [LEARN collector PR #3940](https://github.com/FS-GG/.github/pull/3940) merged at `76ae9d3e` with native capture and same-host export tests, while installer provenance and installed collector acceptance remain open. [SKILL-FS Python retirement PR #3941](https://github.com/FS-GG/.github/pull/3941) merged at `f38a0ec1` with protected checks and detached installed-closure verification; coherent [0.94 publication](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.94.0) now passed with dual-feed readback and a clean public CLI install; wider receiver adoption remains separate. Neither source result establishes a live efficiency reading.

The V0–V6 table, dependency graph and migration lanes below are retained as historical design context.
They no longer schedule work or constrain C0–C3. Accepted predecessor results keep their original scope;
unfinished GS2-09 through GS2-14 work is cancelled or superseded rather than recorded as complete.

| Stage | Work and owning sources | Development process | Observable exit |
|---|---|---|---|
| **V0 — Receive the simplified baseline and decide v2 bindings** | `.github` policy, Coordination runtime, Governance and receiver owners; predecessor outputs plus governance integration | Proportional integration/contract process; protected changes for eligibility/authority | One adopted profile mapping identifies enabled classes, obligations, synchronization, trust, current writer and R5 receiver contract; any needed GS2 changes are registered before use |
| **V1 — Preserve accepted event/queue qualification and incumbent fencing** | Accepted GS2-07.1–07.8 and GS2-08; requalify only affected changes | Routine source delivery with owning technical acceptance; isolated effects keep their permission ceilings | Queue/reconciliation/operating behavior is qualified; all live incumbent writer routes are fenced or explicitly disabled |
| **I1 — Unattended CI credential interlude** | `.github` policy and trusted workflow, Coordination one-attempt installer, reviewer guidance and CI cost baseline; parallel with unfinished GS2-09 source | Rootless fdev for development/secret-free checks, ephemeral remote post-merge credential CI; no host interaction or subagent raw-key role; current v1/OpenV2 human gates unchanged | Isolated installed refusal/recovery/readback with host wallet unavailable; zero routine human/host-agent relay; selected profile joins V3 or is deferred, measured against §7.4 |
| **V2 — Deliver the complete callable v2 path and migration rehearsal** | GS2-09 and affected existing adapter/runtime contracts | Modeled implementation and installed/sandbox acceptance, not fixture-only completion | Real installed entry points connect observations, decisions, provider effects and recovery; migrate/retry/rollback/omission tests pass on representative copies |
| **V3 — Freeze the coherent candidate and prepare receivers** | GS2-10; R5 profile qualification inputs | Comprehensive exact-candidate process | Q0–Q7, installed clean/upgrade profiles, whole-cutover rehearsal, staffed bounded window and concurrent-change disposition |
| **V4 — Freeze, switch and verify while closed** | GS2-11–12 | Protected cutover operation | Normal writes closed, every receiver/settings transformation verified, isolated protocol and routine journeys pass, pre-open rollback executable |
| **V5 — Open v2 and prove ordinary use** | GS2-13; functional R5 journeys | Protected OpenV2 decision followed by enabled ordinary-v2 process | Permanent v1 fence, actual routine and required protocol journeys, named recovery owner, ObservingV2 |
| **V6 — Observe, complete carryover and contract v1** | GS2-14; R5 cohort and receiver retirement | Ordinary repair under v2, protected contraction and existing Q10 | Immediate baseline and 15 distinct completed-work readings, required operational gates, deletion/clean-install proof; R5 efficiency claimed separately only when its own evidence passes |
| **E0 — Test one residual execution hypothesis** | Shared OR H0/H1 and PB0 investment decision | Bounded research/prototype | Measured unmet need, chosen experiment, comparison protocol, investment/stop limits; stop is an acceptable result |
| **E1 — Qualify and optionally adopt one shared execution capability** | Reuse the selected standalone O0–O3 actor/execution foundation; remaining PB/OR hosting, scheduling and comparative-value scope | Reuse completed O0–O3; qualify only additional gaps through modeled implementation, shadow and authorized canary/receiver work | Selected source, installed operation and live acceptance remain distinct; broader class/default and comparative-value claims need their own evidence |
| **F0–F5 — Cooperative clients receive jobs from a project master** | Retained OR §8A feature, using E1 foundations; see section 9.7 | Protocol modeling, enrolled read-only sessions, sandbox lab, independent verification shadow, separately authorized canary and measured adoption | An enrolled client can receive and execute a bounded job; the master can reject fabricated/stale submissions and independently verify and deliver a valid contribution through reconnect/restart |

### 9.1 Dependencies and parallelism

The active dependency chain is `C0 checked source → C1 shared clean epoch and .github activation → C2 journey and rerun → C3
explicit-repository adoption`. Repair forward within that chain; do not require migration, archive,
rollback, retained-state transformation, GS2-10 candidate freeze, or GS2-11–GS2-14 gates.

Within C3, independent receiver and credential preparation can overlap. Several ready profiles may
share one qualified, immutable Coordination CLI publication. Publication precedes activation, but one
repository's settlement need not precede another's preparation or activation. Activate at most three
selected repositories concurrently in the first Net/Governance/Game wave; retain per-repository
head-conditioned protected merges, required checks, credential custody, Authority binding and independent
settlement readback. Pause affected outstanding activations if a shared-runtime defect appears.
Clean-path proof is the C3 acceptance gate. Retained-upgrade and old-client tests apply only when the
receiver promises those capabilities; other installed copies receive repair-forward treatment.
Long-running efficiency readings and claims remain separate from a working-repository decision.

The graph and lane table below describe the superseded staged route. **Solid arrows are its historical
acceptance dependencies. Dashed arrows permit preparation or reuse; they do not grant
effect authority.** Completed inputs remain visible so the graph does not schedule them again. The exact
GS2 contracts retain their historical acceptance; this graph introduces no active clean-start unit or
mutable completion ledger.

```mermaid
flowchart TD
    V1["Accepted GS2-07 / GS2-08 fence"] --> Contracts["Accepted GS2-09.1-.6 contracts"]
    Call["Callable 0.1.1 isolated acceptance / handoff"] --> Join["GS2-09.7 exact isolated candidate"]
    Contracts --> Read["Complete nine-authority provider capture"]
    Contracts --> Execute["Closed migration interpreter / durable recovery"]
    Read --> Join
    Execute --> Join
    Genesis["Installed OperatingV1 admission genesis"] --> Authority["Post-genesis CAS admission + copy-specific effect authority"]
    Authority --> Join
    Join --> Rehearse["GS2-09.7 migrate / interrupt / retry / rollback / rerun"]
    Rehearse --> Omit["GS2-09.8 no omission / idempotency / parent closure"]
    Contracts -. prepare controls concurrently .-> Omit
    Policy["V0 effective routine policy and receiver binding"] --> Freeze["V3 / GS2-10 exact candidate qualification"]
    Policy --> I1["I1 trusted CI credential path"]
    V1 --> I1
    I1 -. selected profile adopted or deferred .-> Freeze
    Receivers["Receiver inventory / published pins / clean and retained proof"] --> Freeze
    Omit --> Freeze
    Changes["Finish or defer concurrent candidate-input changes"] --> Freeze
    Freeze --> Closed["V4 / GS2-11-12 freeze / switch / verify while closed"]
    Closed --> Open["V5 / GS2-13 separately authorized OpenV2"]
    Open --> Observe["V6 / GS2-14 baseline + 15 distinct items / contraction"]
    Observe --> Operating["OperatingV2"]
    Open -. actual routine population .-> R5["Separate R5 cohort / 10 completed originals"]
    Operating -. superseded by ADR-0091 .-> Lifecycle["Release D.5 lifecycle activation and receiver proof"]
    Foundations["Accepted O0-O3 / source-qualified Choreo"] -. reuse .-> Extensions["Optional E0/E1 / LEARN / F0-F3 preparation"]
    Extensions --> Canary["Separately selected E1 / F4 production canary"]
    Operating --> Canary
```

The immediate **source** lanes below can overlap when their touch-sets and shared resources are disjoint.
Protected operation ownership, credential custody, journal writes and final cutover stay serialized at their
real authority boundary. A blocked effect does not block a source-only lane.

I1 is the [V2-CI-I1 interlude](github-substrate-v2-roadmap.md#v2-ci-i1--unattended-credential-execution-interlude),
not a retroactive GS2 acceptance. Its policy, typed installer, selected reviewer guidance and
CI baseline can be prepared alongside GS2-09.7/09.8. Only adopting its credential-bearing
receiver profile joins V3; an unqualified profile is explicitly deferred. This keeps the
existing source/effect separation and does not authorize a current protected write.

| Lane and accountable owner | Ready work | Join / stop condition |
|---|---|---|
| **Migration observations — Coordination** | Reconcile the local prototype, finish all nine authority readers, terminal/nested pagination and two-pass source identity; independent refusal controls. | Exact complete isolated-copy observations before manifest/effects. Partial live diagnostics cannot satisfy this join. |
| **Migration execution/recovery — Coordination** | Reuse callable adapters and the draft step boundary; build the closed effect interpreter, ordered durable driver, archive verification and executable pre-open rollback. | Reviewed source and unchanged canonical-model correspondence; native run waits for exact copy, artifact and effect authority. Coordinate shared adapter edits with the observation lane. |
| **Post-genesis admission/effect readiness — protected-operation and Coordination owners** | Reuse the installed genesis readback; finish scoped expected-parent append, incumbent operation/claim inventory, credential custody for later admissions and copy-specific effect authorization. | Genesis alone is not an append permit. Governed effects wait on exact authority, credentials and fresh prestate; source-only lanes continue independently. Ordinary CLI stays fenced until installed runtime acceptance. |
| **I1 unattended credential path — `.github` and Coordination** | In parallel, select trusted CI/secret topology, compose one-attempt installer and refusal tests, write a narrow reviewer checklist, and measure duplicated CI/administrative delay. | Isolated installed join and receiver decision before GS2-10 if selected. Current v1 admission/OpenV2 gates do not change in this lane. |
| **Omission controls — Coordination qualification owner** | Extend exact-population, added-subject, duplicate, replay and receipt-prefix controls already started in PR #507. | GS2-09.8 acceptance waits for representative .7 evidence; authoring independent controls does not. |
| **Routine profile / receivers — `.github`, Governance, SDD, Templates and actual receivers** | Record actual enforcement call sites, tool/package/default identities, clean/upgrade cases and supported/deferred profiles. | GS2-10 accepts a coherent exact candidate; every changed input is refreshed before freeze or explicitly deferred. Pure Governance APIs alone do not qualify wiring. |
| **Telemetry / measurement — `.github` producer, installed operator** | Repair attributed observation gaps and qualify prospective published/installed changes under their own authority. | No general migration dependency. R5 needs its actual ordinary-v2 cohort; missing efficiency evidence cannot manufacture or veto unrelated operational acceptance. |
| **Optional/product work — owning repositories** | Authorized independent research/source work, reused O3/Choreo/FsQuint foundations, and unrelated product fixes. | The historical fleet freeze does not constrain clean-start product work. Release D.5 follows its generation-2 clean-start amendment; production canaries retain their separately selected operating authority. |

For programme-wide advancement, dispatch independently ready lanes in parallel within actual worker,
integrator and hosted-CI capacity; a named-feature request stays within that feature. Give concurrent
workers isolated worktrees and disjoint touch-sets. Use one integrator for shared Coordination surfaces
and exact candidate assembly. Pause only the dependent effect when an authority input is missing;
state which independent source work remains useful.

[ADR-0084](adr/0084-semantic-reuse-never-cancels-coherent-validation.md) still governs CI concurrency:
validated exact-head `reused` evidence may overlap native delivery with coherent closure; `current` or
missing reuse waits for closure; `deferred`/`failed` refuses. Pending post-merge confidence is distinct
from disputed evidence, which blocks dependent qualification, publication and activation.

### 9.2 V0: bind the handoff without restarting it

Inspect the adopted predecessor policy and actual installed behavior. Keep R2/R4/R5 economics
insufficiency explicit; it does not reopen accepted functional source or suspend independent work.
Resolve the governance proposal's
independent synchronization decision and the ownership table in section 3. Record every retained predicate
at its actual enforcing boundary. If a missing capability belongs to the predecessor's promised scope,
return that exact gap to its owner; do not conceal it inside a new general controller.

Map routine native delivery to GS2-05.6 review/delivery and lifecycle semantics. Preserve cheap automatic
records where they already satisfy the intended experience. Amend only actual semantic conflicts. A
required acceptance/phase validator that still blocks the path is a real incompatibility, not something
an explanatory prompt can override.

For the historical staged route, candidate inclusion would still require agreeing producer/consumer bytes,
unit contracts and exact pins. ADR-0091's selected clean-start profile instead closes through its C0–C3
evidence; it does not revive GS2-10 or claim an unrelated default or operation class.

### 9.3 V1–V2: make v2 usable before freezing it

**Historical staged route.** ADR-0091 removes this section from the active dependency chain. The existing
CLI 0.1.2 and workflow support the clean `.github` pilot; no remaining GS2-09 migration slice, V1 admission
extension, archive or rollback proof is a prerequisite. Retain the evidence below at its original scope
without presenting unfinished items as complete.

**Reuse completed inputs.** GS2-07.1–.8 and GS2-08.1–.9 are accepted. Preserve their event/audit behavior,
bridge artifact, receiver census and residual-writer dispositions. New writers still need the common
fence; accepted source controls do not authorize a new installed route.

The callable gap identified in the original survey is closed at its declared scope. The
[owning V2-CALL-01 plan](https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/docs/roadmaps/callable-ordinary-v2-execution.md)
records published 0.1.1, canonical opt-in adoption, native isolated-provider recovery/cleanup and handoff.
Its `delivery inspect|plan|advance` composition is one ordinary source-delivery operation. Its explicit
permission ceiling excludes fleet migration, production targets and general activation. Do not rebuild
this path or substitute its synthetic `OpenV2` observation for the fleet epoch.

**Next executable window: GS2-09.7.** This remains the owning unit; the rows below decompose its
implementation and qualification without creating a second set of acceptance IDs.
The [OperatingV1 genesis readback](https://github.com/FS-GG/FS.GG.Coordination/pull/508#issuecomment-5814570932)
is a completed input to this window, not evidence that post-genesis admissions or migration effects can run.

| Slice | Reuse / implementation | Meaningful exit |
|---|---|---|
| Recover existing source | Compare local `routine/gs2-09-7-rehearsal` at `82b4772d…` with current protected main; preserve step-execution, Project/native-relation readers and tests. | Reviewable source PR and relevant canonical-model/adapter checks; local diagnostics are labeled separately. |
| Complete provider capture | Populate every authority required by `GitHubCompleteDiscoveryQualification.expectedAuthorities`: issues, Project items/fields, hierarchy/dependencies, claim/events, review/delivery/release, settings, workflow pins and receivers. | Two complete copy-specific reads with exact identity, revision, raw-byte digest, terminal pagination and stable population. Unknown, partial, contradictory or unsupported observations refuse. |
| Compose exact migration | Feed accepted .1–.6 contracts from those reads; implement only closed typed effects through current adapters, exact journal CAS/generation, intent before dispatch, readback and recovery. | Fresh-process interruption tests cover every effect phase; unknown outcomes remain pending, known-applied replay has no duplicate dispatch. No arbitrary manifest-supplied REST/script executor. |
| Qualify representative copies | Join reviewed executable artifacts, scoped isolated subjects, operation-specific authority and independent controls; migrate, interrupt, retry, archive-verify, rollback before open, and migrate again. | Retained actual provider and journal readback, complete target population, request counts, rollback results, cleanup, observed duration/API headroom and limitations. Only then native .7 acceptance. |

**Then GS2-09.8 and parent closure.** Extend the independent controls already delivered in PR #507.
Prove that exact-manifest rerun changes nothing and added/omitted/duplicate subjects are detected against
the actual provider population. The in-memory rollback-prefix test is useful source evidence, not an
executed provider rollback. Run comprehensive parent closure over the exact accepted child set.

Code must continue to satisfy the canonical Quint protocol and production correspondence. The callable
and hosted-writer Choreo tests supply reusable cases for absent/applied/unknown, lost response, stale retry
and durable recovery, but neither is a substitute for migration-provider evidence. The migration path does
not acquire an actor/Host/PostgreSQL dependency merely because those separate components are qualified.

GS2-09.1–.6 receipts remain immutable contract acceptance. Provider capability gaps are explicit refusals
or reviewed dispositions in the owning scope; they are never normalized into passed rehearsal evidence.

### 9.4 V3–V4: stabilize a deliberately bounded cutover

Use GS2-10's exact candidate: published artifacts, locks, model/compiler identities, tools, workflows,
settings, lifecycle/provider decisions and receiver heads. Choose deferred default changes explicitly.
Do not narrow full-head or full-candidate freshness by editorial interpretation.

The rehearsal must produce an observed duration range, API/rate headroom, temporary restriction inventory,
maximum acceptable closed-write interval, latest safe abort point, recovery time, operator availability
and named backups. Pin these in the existing cutover plan before scheduling. This design supplies no
invented universal outage duration. If the rehearsal does not fit the acceptable window, improve or
reschedule before freezing; do not discover the mismatch after OpenV2.

Prepare dependencies and receiver changes additively before the window. Any changed candidate input
requires the existing complete requalification or deferral. Repeated drift is a reason to stabilize fewer
concurrent changes and revisit candidate inclusion, not to waive the exact-candidate contract.

GS2-11 drains/parks all live operations, stops ingress, verifies restrictions and takes the coherent
snapshot. GS2-12 switches exact artifacts, migrates facts/settings, verifies every receiver, tests wrong
paths and preserves pre-open rollback. Include a routine-profile journey alongside the comprehensive
protocol journey if that profile is claimed by the candidate. They prove different behavior.

### 9.5 V5–V6: finish migration and prove the simplified experience

OpenV2 remains the explicit irreversible decision. Verify the first actual low-risk ordinary change and
the required protocol capabilities, which need not be forced into one artificial PR. Preserve native code
delivery versus required publication, same-PR repair, observer independence and installed policy identity.
Monitor old-writer attempts and retain inert v1 evidence; recovery is roll-forward.

Use identical operational definitions at the immediate baseline and after each of 15 distinct completed v2 work items, as defined by GS2-14.2. Complete Q10 and contraction conditions
before destructive retirement. Delete obsolete callers, policy contexts and artifacts only for the adopted
profile, preserving forensic history and still-needed automatic records. Verify that a clean installation
cannot resolve a v1 production route and that an upgrade does not restore removed ceremony.

R5's independent routine cohort closes its repair-accounting window at the tenth distinct completed
canonical original across its prospectively enrolled BARC-01, SC2C-01, LEARN-01, FOURD-01, TODO-01, TTT-01, SNAKE-01 and HELLO-01 lanes. It
may finish on a different date from the 15-item Q10 gate. Neither completion is inferred from the other.
Claim each outcome at its actual scope; no efficiency-success label is needed to admit a valid repair.

### 9.6 E0–E1: later capability development

The selected single-host actor/execution slice and its installed pilot/recovery and controlled adoption are
accepted in [standalone O0–O3](roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md).
Reuse that exact baseline and the later completed Choreo source/formal evidence; do not reopen either as a
future E1 implementation programme. They do not establish unmeasured comparative value, a general default,
federation or v2 migration completion.

Coordination owns the remaining disposition of the Choreo production fixes with SystemAdmin as installed
operator: compare the selected installed Host artifact's source identity with the C4 fixes; if absent, prepare
and publish the appropriate immutable Host/runner artifact and separately authorize its bounded adoption.
Qualify the affected proven-absence, retry-intent and recovery paths against that installed artifact and its
supported store, retaining native readback and rollback/recovery ownership. If already included, retain the
exact artifact and matching qualification evidence instead of repeating adoption. Until then, report the fixes
as source-qualified only. Historical O3 acceptance and receipts remain unchanged; this is a targeted follow-up,
not a second O3 programme, authorization to operate, or a blanket prerequisite for callable v2. Remaining
E0/E1 extensions follow the conditions below.

[LEARN-01](roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md) is the
bounded learning extension across V0 observation, E0 comparison and this selected E1 foundation.
Its .1–.3 window defines measurement/experiment contracts and integrates fixed planning, execution and
context profiles; .4–.5 qualify installed use and the first evidence-based decision. O2/O3 retain their
existing pilot, recovery and adoption exits. Later community intake on Main is a separate data-receiver
boundary with no job-dispatch capability, not cooperative-agent federation or another executor.

E0 selects one measured hypothesis using section 8. E1 implements only the necessary shared kernel and
executor/host/planner slice. Qualify integrated success, technical failure, repair, budget exhaustion,
process loss, late success and recovery before simulation/shadow/canary promotion. PB and OR consume the
same operation and reservation semantics; no parallel build of competing executors.

Mutation canaries follow the existing H6 default of OperatingV2 unless a separate accepted sequencing
decision authorizes otherwise. Restrict the first canary to one reversible class, exact policy, bounded
population and authorized recovery route. Observation-only success cannot authorize it. Keep the baseline
usable and qualify host loss, pending-effect takeover and receiver routing before any default decision.

Later batching, model routing, multi-agent execution, dashboards, federation and availability expansions
each need their own evidence of incremental benefit and applicable failure qualification. A qualified
executor does not prequalify every plugin or model. Stronger model selection follows qualified capability
and observed work-class risk; escalation cost belongs to the original attempt. Parallel workers require
independent bounded tasks, disjoint or correctly synchronized resources, an integrator and bounded joins.

### 9.7 F0–F5: cooperative orchestrator development

This track implements the client/master feature in section 8.5. It is retained future scope, conditional
on the demonstrated need and qualified foundations; it is not a prerequisite for simplification or v2
cutover. The original F0–F5 identifiers remain the traceability keys, with no competing milestone series.
Reuse the actor supervision, execution identities, bounded admission, durable journal and candidate
transport delivered and qualified by standalone O0–O3 wherever their contracts match. The trusted local
runner is not an enrolled remote contributor: bilateral trust, disclosure, tenant isolation, quarantine
and independent hostile-code verification remain F0–F5 work. Do not count O2/O3 as their completion or
reimplement its shared foundation under a federation label.

| Stage | Entry and development process | Deliverable and exit |
|---|---|---|
| **F0 — Define the cooperative protocol and scope** | Alongside E0 / OR H0–H1 when this is the selected need; bounded research and Quint protocol modeling before stateful implementation | Owner/client responsibilities, admission and disclosure rules, assignment/revocation model, identity and artifact-transport choices, verification isolation and full cost model |
| **F1 — Connect enrolled clients to a master** | After the authenticated session foundations corresponding to H2 are qualified in E1; interface implementation and adversarial session tests | Bilateral enrollment, outbound connection, capacity offers, job offers, reconnect and tenant isolation; read-only sessions with no project execution or delivery |
| **F2 — Execute jobs in a sandbox laboratory** | After bounded execution foundations corresponding to H3; modeled lifecycle implementation and runtime correspondence | Explicit bounded assignments, local agent supervision, synthetic/public fixtures, submissions and quarantine; a fabricated client report cannot self-certify success |
| **F3 — Independently verify contributions in shadow** | Alongside qualified H4–H5 durability/verification foundations; replay, adversarial tests and observational comparison | Master-controlled checks, exact candidate bindings, retained evidence, restart/revocation/reassignment recovery and measured transfer/verification bottlenecks; no production delivery |
| **F4 — Run one cooperative contribution canary** | No earlier than H6 eligibility and OperatingV2 under the existing default; separately authorized protected operation | One enrolled peer, project and work class; immediate revocation, owner-selected candidate, independently verified result and observed ordinary protected delivery |
| **F5 — Adopt measured cooperative contribution** | After the bounded canary and a sufficient comparison; explicit class/default decision | Supported operation and recovery ownership, accepted data/cost/SLO bounds and logging; expand only proven work classes, with additional trust or delegation qualified separately |

Read-only F0 research can precede v2 completion. F1–F3 depend on the specific E1 foundations named above,
not completion of every optional optimizer, dashboard or hosting feature. The graph summarizes those
joins; this table retains their distinct entry conditions. The F4 canary does not gain authorization from
a green F3 shadow run or from a local E1 canary in a different work class.

The first complete demonstration is recognizable to a user: connect the client, receive a job, run local
agents, reconnect after interruption, submit a valid contribution and observe the master's independently
verified delivery. Companion negative cases include a forged result, wrong project/candidate binding,
duplicate submission, revoked assignment and exhausted verification quota. Their outcomes remain refusal,
pending recovery or bounded retry as appropriate; none manufactures success from client claims.

When F0 is selected, assess reuse of the Choreo modeling and trace/replay workflow for assignment,
acknowledgment, reconnect, revocation and duplicate-message semantics. Define this protocol's own bounds,
invariants and production correspondence before claiming evidence. Hosted-writer qualification does not cover
bilateral trust, disclosure, tenant isolation or hostile contribution verification, and does not complete
F0–F3 or authorize a broader Choreo rollout.

The owning implementation selects remaining protocol/library versions, key custody, artifact retention
and isolation through F0 rather than treating this prose as a published schema. Detailed requirements
remain in [OR §8A](coordination/2026-08-31-operations-research-first-agent-orchestration-design.md#8a-federated-cooperative-orchestrators--proposed-extension).
That source supplies protocol depth; this unified roadmap owns the feature's place in the overall sequence.

### 9.8 Feature parts and subroadmap index

For the active clean-start route, plan one bounded part: **clean `.github` V2 activation**, covering C0
through C2 with the existing CLI 0.1.2 and workflow. C3 selects repositories explicitly and may
prepare a bounded wave in parallel after the smoke passes. The migration, candidate-freeze,
controlled-cutover and observation parts in the table below are historical entries and must not be
dispatched for the clean-start route.
The `.github` C0–C2 path is complete. Audio is the first selected C3 repository. Coordination #865
delivered its source profile, `.github` #3919 delivered Audio observation at
`4ac2224cbefa55387ab1273a09ef23daf462628c`, and Audio #326 delivered the disabled receiver at
`08a46576320b0043d43a0ce4eeffb3cd2e736e56`. Its
[receiver plan is durable on `main`](https://github.com/FS-GG/FS.GG.Audio/blob/main/docs/roadmaps/v2-ordinary-adoption.md).
CLI 0.1.3 is published. Dedicated Audio credential custody, protected activation, one settlement and
its normal already-complete replay are complete, with independent Authority journal readback. This
does not activate the fleet.

Rendering's published CLI 0.1.4 and independent settlement are complete as recorded in section 0.
The combined Net, Governance and Game CLI 0.1.5 release and all three protected receiver settlements are complete at the bounded clean-path
boundary documented in [section 9.1](#91-dependencies-and-parallelism). SDD and Templates are the
next completed wave: CLI 0.1.6 is publicly published at its protected source; both SDD and Templates have separately settled with unchanged normal reruns and independent Authority readback, as recorded in section 9.1. Coordination itself completed its selected adoption with immutable CLI 0.1.7, protected activation/observer repair, successful settlement, unchanged normal replay and independent Authority readback as recorded in section 0. The historical migration parts below do not schedule these receivers.

These are the default **parts for Astra planning**, named by deliverable. Select independent
dependency-ready parts concurrently when programme advancement is requested; use a fresh Astra-high
planner when each part needs its first plan. A valid active subroadmap continues with its existing
identity. Within a part, detail only the next useful window and retain later outcomes as an outline.
Source changes, routine repairs and individual PRs do not create new parts or require fresh planning.

A part can span stages where one outcome crosses an operating boundary, and one stage can contain
independently useful parts. The entries below are planning scopes, not a second GS2 queue or completion
ledger. Their order does not override native prerequisites. Split a part only when a separate outcome,
owner or dependency makes it independently executable; record that narrower scope here. Existing bounded
plans remain valid even when they cover only part of a row.
Entry conditions constrain the dependent execution; earlier read-only planning may inspect missing prerequisites.

| Part Astra plans | Stage and bounded outcome | Accountable planning owner and entry | Feature subroadmap |
|---|---|---|---|
| **Audio explicit ordinary V2 adoption — C3-AUDIO-01** | C3: publish immutable Coordination CLI 0.1.3, enroll dedicated Audio credentials, activate the receiver separately, then observe one settlement and one normal rerun | Coordination, `.github` and Audio owners; source profiles [Coordination #865](https://github.com/FS-GG/FS.GG.Coordination/pull/865), [`.github` #3919](https://github.com/FS-GG/.github/pull/3919), [Audio #326](https://github.com/FS-GG/FS.GG.Audio/pull/326), [CLI 0.1.3](https://github.com/FS-GG/FS.GG.Coordination/releases/tag/v0.1.3), dedicated custody and activated Audio main are delivered. [Run `36413290713`](https://github.com/FS-GG/FS.GG.Audio/actions/runs/36413290713) settled once and its normal rerun was already complete; the Authority journal head remained unchanged. | [Audio receiver plan on `main`](https://github.com/FS-GG/FS.GG.Audio/blob/main/docs/roadmaps/v2-ordinary-adoption.md). Selected Audio adoption complete; no fleet-wide result or V1 migration is claimed. |
| **Simplified baseline and v2 policy binding** | V0: accepted selected-profile functional V2; bind adopted routine policy, actual enforcement/receiver wiring and the frozen 15-original R5 population; keep economics separate | `.github`, Coordination, Governance and receivers; the nine selected receivers are accepted at C0–C3, while future operations/defaults retain their own authority. V2-HOST-01 fixed-job source/artifact/hosted diagnostic qualification is closed through Coordination #893/#895 and the owning evidence. Work-main additionally reports one exact installed fixed version/login diagnostic with complete cleanup and retained-runtime preservation; root verified the public report delivery but not the private evidence. No model/resume session, general service activation or upgrade is established | [R0–R5 source plan](2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md), [functional acceptance report](reports/2026-09-29-r5-functional-v2-acceptance.md), [UTEL correctness](roadmaps/utel-01-telemetry-correctness.md), [local store](roadmaps/utel-local-telemetry-store.md), [operational completeness](roadmaps/utel-operational-completeness.md), [dashboard](roadmaps/utel-telemetry-dashboard.md), [release successor](roadmaps/utel-release-successor.md), [Host 0.2.0 successor](roadmaps/utel-host-release-020.md), [V2-HOST-01 owning plan](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/v2-host-execution-boundary.md), [installed result report](https://github.com/FS-GG/.github/blob/0d317ffc72fdcaafce8eb245a960e61124d4445f/MAILBOX.md), and [current audit](reports/2026-09-24-v2-roadmap-code-audit.md). Functional acceptance claims no complete native usage or efficiency benefit |
| **Skill Python to F# conversion — SKILL-FS-01** | Independent source and receiver track: replace the four current distinct Python implementation files across both tracked skill roots with packaged F# commands; preserve telemetry and preflight refusal behavior, then remove Python skill executables | `.github` tool and skill owner, with selected receivers; can proceed beside V2 source work, while the skill flip and deletion require coherent publication and installed parity | [SKILL-FS-01 subroadmap](roadmaps/skill-python-fsharp-conversion.md). Complete at the selected public SDD 2.0.3 clean and retained receiver boundary: coherent 0.94.0 supplies the replacement callers and the retired implementations are absent. Wider materializers use their own adoption path; no removed routine obligation is reopened |
| **Event and queue qualification** | V1, GS2-07.6–07.7: qualify the queue and measure narrow reconciliation, coalescing and audit repair | Coordination; preserve accepted native units and resume only unfinished scope | [GS2-07.7 event-benefit subroadmap](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/gs2-07-7-event-benefit.md), scoped to 07.7; native acceptance is recorded in [PR #329](https://github.com/FS-GG/FS.GG.Coordination/pull/329) |
| **Runtime operations qualification** | V1 / GS2-07.8: accepted selected no-host operation/audit scope | Coordination; preserve the accepted disposition, qualify only newly included runtime behavior | [GS2-07.8 owning plan](https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/docs/roadmaps/gs2-07-8-runtime-operations.md) and its accepted receipt |
| **Universal bridge and receiver fencing** | V1, GS2-08: protected epoch ledger, complete current-writer coverage, published bridge, receiver adoption and old-client refusal | `.github` bridge owner, with Coordination and receiver owners; GS2-08.1–08.9 are accepted, active installed bypasses are retired and Q4 remains unclaimed | [GS2-08.8 receiver adoption horizon](roadmaps/gs2-08-universal-v1-bridge.md#gs2-088-receiver-adoption--window-a), [receiver acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/417) and [residual-writer acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/419) |
| **Unattended CI credential execution** | I1: trusted post-merge secret use, one-attempt installation, reviewer checklist and bounded CI cost observation qualified in an isolated hosted receiver | `.github` policy/secret owner with Coordination installer and selected receiver; 01–06 complete, exact profile selected for GS2-10, production activation pending its candidate and `OpenV2` gates | [V2-CI-I1 subroadmap](roadmaps/v2-ci-i1-unattended-credential-execution.md), [installed qualification](operations/v2-ci-i1-installed-qualification.md), [design](coordination/2026-09-24-v2-unattended-ci-credential-interlude.md) and [ADR-0088](adr/0088-ci-owned-unattended-credential-execution.md) |
| **Callable ordinary v2 execution** | V2 / GS2-09.9: bounded installed source-delivery composition, native isolated recovery and discovery handoff complete | Coordination; preserve exact 0.1.1 artifact, receiver and permission ceiling; no fleet production or migration authority | [Completed V2-CALL-01 plan](https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/docs/roadmaps/callable-ordinary-v2-execution.md), [readiness](https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/gs2-09-9/callable-readiness.json) and [handoff](https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/gs2-09-9/callable-discovery-handoff.json). Historical 0.1.0 bytes remain unchanged; 0.1.1 is the accepted repair identity |
| **Migration tooling and representative rehearsal** | V2 / GS2-09: .1–.6 contracts accepted; .7 provider execution/rehearsal and .8 omission/parent closure remain | Coordination with receiver and protected-operation owners; source lanes and effect join are separated in §9.1 | [GS2-09.7 owner plan](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/gs2-09-7-representative-rehearsal.md), [exact GS2 sequence](github-substrate-v2-roadmap.md#gs2-09--build-migration-archive-and-rollback-tooling), [current implementation window](#93-v1v2-make-v2-usable-before-freezing-it) and [audit](reports/2026-09-24-v2-roadmap-code-audit.md). The published plan retains source work and records the remaining provider/operation gaps; GS2-09.7 is not yet accepted |
| **Coherent candidate and new-workspace qualification** | V3, GS2-10: bind published tools, template/provider pins, guidance, clean/upgrade receiver cases and the rehearsed cutover window | `.github` cutover owner, with Coordination, SDD and Templates; completed candidate inputs | No subroadmap linked yet |
| **Controlled cutover and first ordinary use** | V4–V5, GS2-11–13: freeze and drain, switch while closed, verify rollback, then separately authorize OpenV2 and observe real journeys | `.github` cutover owner with Coordination and receiver owners; qualified candidate and staffed operation window | No subroadmap linked yet; one plan retains the closed-switch and irreversible-open boundaries |
| **Observation, receiver carryover and v1 retirement** | Historical V6/GS2-14 remains superseded; selected-profile R5 functional acceptance uses the fixed first-ten cutoff and all-15 repair accounting, with economics insufficient | `.github` policy owner with Coordination and the nine selected receivers; future populations own their own adoption and economics | [R5 functional V2 acceptance report](reports/2026-09-29-r5-functional-v2-acceptance.md) |
| **One residual execution experiment** | E0: one additional measured unmet need and a bounded comparison against the supported baseline | Coordination, with `.github` policy owner; measured residual need and the section 8.1 investment decision | Conditional additional scope; use the selected [standalone O0–O3](roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md) implementation/evidence as a baseline, not a second actor-runtime selection |
| **Stable-policy orchestration and statistical learning — LEARN-01** | V0 measurement, E0 controlled comparison and selected E1 context/allocation extensions: broad fixed profiles, whole-issue context/token efficiency and robust evidence before finer or adaptive routing | `.github` telemetry/policy/analysis owner with Coordination execution integration; the replacement container owner owns the remaining private collector deployment. .1 is delivered. .2 capture/export, credential-role and native-source binding mechanisms are source-delivered through .github #3916/#3927/#3940/#3986 and Coordination installer-v2 #876; actual native installation and trusted capture remain open. .3 source is complete through executor dispatch and observation integration. .4 W1–W5, W7 and W8 technical preparation are delivered at their recorded scopes. Coordination #901 adds the fixed authenticated capability-diagnostic source and records advertised `gpt-5.6-sol` / `medium` with zero model sessions and complete cleanup; it is not installed collector/capture qualification. Work-main's installed fixed diagnostic remains historical version/login evidence only. Main/work-main is not a future execution prerequisite. Current .4 waits for published `capture/2` successor bytes, the Main-independent collector container, receiver-owned config/2 role/grant/private storage, observed native capability and Main-unavailable clean/capture/restart/recovery/cleanup proof. W6, census/shared costs and enrollment follow; .5 still requires a locked dataset or truthful stopped-window result. After replacement acceptance and record inventory, preserve required records, then retire obsolete Main services/routing/credentials. No experiment, usage denominator or efficiency result is established | [LEARN-01 design and roadmap](roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md), design delivered in [PR #3449](https://github.com/FS-GG/.github/pull/3449), .1 source contract in [PR #3845](https://github.com/FS-GG/.github/pull/3845), bounded .3 source slices in Coordination [#882](https://github.com/FS-GG/FS.GG.Coordination/pull/882), [#885](https://github.com/FS-GG/FS.GG.Coordination/pull/885) and [#886](https://github.com/FS-GG/FS.GG.Coordination/pull/886) at `513acdbddcb52b6f3e219619605de458070e033f`, and the [executor/observation source plan](https://github.com/FS-GG/FS.GG.Coordination/blob/513acdbddcb52b6f3e219619605de458070e033f/docs/roadmaps/learn-01-executor-observation.md). Native PostgreSQL 18.6 passed 40/40 and current formal identities were qualified without changing retained trace behavior. The [bounded owner-input assessment](research/learn-01-observation-contract.md#bounded-pre-admission-owner-assessment), [owning installed-window plan](https://github.com/FS-GG/FS.GG.Coordination/blob/6210dc1612e38acc7f16a6a6ce62bfad9f280c97/docs/roadmaps/learn-01-installed-window.md), [fixed capability source](https://github.com/FS-GG/FS.GG.Coordination/pull/901), [served-candidate evidence](https://github.com/FS-GG/FS.GG.Coordination/blob/92669c866006dfb228f3034b8dd9b20842601c6c/docs/roadmaps/evidence/learn-01.4-served-candidates.md), [installed-boundary owner report](https://github.com/FS-GG/.github/blob/0d317ffc72fdcaafce8eb245a960e61124d4445f/MAILBOX.md), [capture-version gap report](https://github.com/FS-GG/.github/blob/4fdeca1ac6c1c459701d3f62affcb3257ff3ae19/MAILBOX.md), and [replacement-route steering](https://github.com/FS-GG/.github/blob/9dc0a67b1e46adaae12c5c07c1caf935542d33f9/MAILBOX.md) retain the historical and replacement boundaries. Reuse UTEL and O0–O3. Later community contribution, if selected, uses the same Main-independent deployment principle. Adaptive extensions remain conditional |
| **Shared bounded execution** | E1: finite attempts, atomic reservations, effect settlement and qualified CLI/runtime correspondence | Coordination; selected trusted single-host scope is already owned by O0–O3; E0 selects only additional gaps | [Standalone telemetry and orchestration](roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md#o3--controlled-adoption-and-later-options) and the [O3 controlled-adoption evidence](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/o3-controlled-adoption.md) complete the selected shared Akka session core, PostgreSQL journal, capacity-1 subscription budget, provider-neutral executor/Host composition and installed two-project serial qualification. Reuse accepted source; wider execution profiles remain conditional |
| **Authenticated hosting and recovery** | E1, relevant H2–H5: one selected host with sessions, durable recovery and a usable CLI fallback | Coordination; selected O0–O3 is complete, then qualify only additional hosting/cooperative requirements | [Standalone telemetry and orchestration](roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md) owns the completed selected O0–O3 single-host scope. The accepted pilot, physical-reboot same-attempt recovery and non-dispatching installed adoption establish this bounded profile. Akka.NET is selected for this scope; Codex subscription execution is first, while Claude, OpenCode and DeepSeek share the intended adapter contract. Automatic postboot-helper requalification and other hosting scope remain conditional. |
| **Hosted-writer Choreo correspondence and fix adoption** | E1 foundation: C0–C6 source/formal qualification complete; installed inclusion of the two production fixes requires exact artifact evidence | Coordination source/qualification owner and SystemAdmin installed operator; section 9.6 owns targeted publication/adoption disposition without reopening historical O3 acceptance | [Completed Choreo roadmap](https://github.com/FS-GG/FS.GG.Coordination/blob/8135bb68bac07e941897ec556568c52ade6a492c/docs/roadmaps/choreo-akka-fsharp-trace-correspondence.md) and [maintenance guide](https://github.com/FS-GG/FS.GG.Coordination/blob/8135bb68bac07e941897ec556568c52ade6a492c/docs/architecture/choreo-correspondence.md). Reuse existing machinery; callable-v2 coverage remains its own qualification, and federation remains conditional |
| **Scheduling and capacity allocation** | E1, relevant OR/PB scope: one planner over the shared executor, independent feasibility checks and class-specific shadow/canary/adoption | Coordination, with `.github` policy owner; measured scheduling need and required execution foundations | No subroadmap linked yet; conditional, with no second executor |
| **Cooperative enrollment and sessions** | F0–F1: protocol, bilateral enrollment, outbound client connection, capacity/job offers and reconnect without project execution | Coordination; selected cooperative need; F0 research may precede v2, while F1 needs authenticated session foundations | No subroadmap linked yet; conditional |
| **Cooperative contribution and verification** | F2–F3: bounded sandbox assignments, local agents, quarantined submissions and owner-controlled verification through recovery | Coordination; the applicable bounded execution, session and verification foundations from section 9.7 | No subroadmap linked yet; conditional |
| **Cooperative canary and adoption** | F4–F5: one enrolled peer and work class reaches independently verified delivery, then a measured adoption decision | Coordination with project/receiver owners; F3 evidence, OperatingV2 under the existing default and separate canary authority | No subroadmap linked yet; conditional |
| **SVG game engine and Fable workspace completion** | Section 15 independent producer/product track: complete C01–C20, M0–M11, section 13 and Releases A–D through the accepted ordered feature sequence; no V0–V6 completion prerequisite for independent source/qualification work | `.github` planning owner with SDD, Rendering, Game, Audio, Net and Templates implementation owners. S.I.R. is strictly read-only and supplies only an audited disclosed compatibility baseline. Releases A–C and SVG-WORKSPACE-01.1–.6 are complete. Release D is selected for exact public publication, installed qualification and activation; durable hosting is deferred, while the later single-lifecycle default follows the generation-2 clean-start policy and separate SDD receiver proof | [accepted complete programme](2026-09-07-064259-svg-game-engine-template-design-roadmap.md), [SVG-FOUND-01 foundation](roadmaps/svg-game-engine-foundation.md), [SVG-QUAL-01 installed model qualification](roadmaps/svg-game-engine-installed-model-qualification.md), [SVG-SCENE-02 scene/renderer](roadmaps/svg-game-engine-scene-renderer.md), [SVG-PREVIEW-A publication](roadmaps/svg-preview-a.md), [SVG-PREVIEW-B release plan](roadmaps/svg-preview-b.md), [replay](roadmaps/svg-replay-01.md), [network](roadmaps/svg-network-01.md), [scale](roadmaps/svg-scale-01.md), [SVG-PREVIEW-C release plan](roadmaps/svg-preview-c.md), [SVG-WORKSPACE-01](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/roadmaps/svg-workspace-01.md), [SVG-RELEASE-D](roadmaps/svg-release-d.md), and [revision rationale](2026-09-07-121207-svg-game-engine-roadmap-revision-proposal.md) |
| **Fable bindings candidate generation and upstream integration assessment** | Section 15 producer track: optional Xantham candidates, exact tool qualification and skill-load upstream assessment; independent of v2 prerequisites | Templates 0.14.0 contains the public Xantham payload; [PR #635](https://github.com/FS-GG/FS.GG.Templates/pull/635) merged at `f3a7cd6ab6f035d4ba335d03fdc367db6164793f` after clean installed and retained-adoption proof, with the protected composition, kit and materialization gates green. Current live assessment remains `updates-found` / `unqualified` / `investigate`; no upstream update is accepted by that observation. | [Xantham candidate subroadmap](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/roadmaps/fable-bindings-xantham-candidates.md). FBX-05 is complete at its public installed receiver boundary. FBX-07's second-runtime candidate composition merged in [#645](https://github.com/FS-GG/FS.GG.Templates/pull/645), with [exact candidate hosted proof](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36473506070/job/109101353158); public successor adoption remains separate. |
| **Fable SC2 client and custom WASM control** | Section 15 product track: browser tactical client, native SC2 gateway and portable module contract; independent of v2 prerequisites | `FS.GG.SC2.Client`; .1–.4 are delivered for their declared profiles. Bounded .5 realtime/recovery and genuine pointer-commit/keyboard-abort acceptance are complete through [#16](https://github.com/FS-GG/FS.GG.SC2.Client/pull/16) at source `ad50425d`, protected tree `6ea5ef70`, with [verification `36734647781`](https://github.com/FS-GG/FS.GG.SC2.Client/actions/runs/36734647781) passed. Wider race/map and multiplayer coverage, .6 custom-module authoring/recordings and .7 publication, installation and platform qualification remain open | [SC2C-01 design and feature roadmap](2026-09-08-132131-fable-sc2-wasm-client-design-roadmap.md), [bounded .3 plan](https://github.com/FS-GG/FS.GG.SC2.Client/blob/34182a57940493d3083e24413d9c34219876278b/docs/SC2C-01.3-plan.md), [.3 integrated evidence](https://github.com/FS-GG/FS.GG.SC2.Client/blob/34182a57940493d3083e24413d9c34219876278b/docs/SC2C-01.3d-integrated-evidence.md), [tactical .4 plan](https://github.com/FS-GG/FS.GG.SC2.Client/blob/83a770b9c9ec6b595dd173ec0dcf8ce5248b640b/docs/SC2C-01.4-plan.md), [.4e integrated evidence](https://github.com/FS-GG/FS.GG.SC2.Client/blob/83a770b9c9ec6b595dd173ec0dcf8ce5248b640b/docs/SC2C-01.4e-integrated-evidence.md), [.4f receipt](https://github.com/FS-GG/FS.GG.SC2.Client/blob/a40b564f3742a19872712d43137ee9d5457eb303/docs/SC2C-01.4f-representative-receipt.json) and [.5 plan](https://github.com/FS-GG/FS.GG.SC2.Client/blob/ad50425dcdcc526215fce1904e33015369b2ac69/docs/SC2C-01.5-plan.md). Section 0 records exact source/native receipts; no all-race, released-product or backdated R5 claim follows |
| **Fable BAR client and custom WASM control** | Section 15 product track: browser tactical client over FSBarV2/HighBarV3, including the FS.GG Fable game target; independent of v2 prerequisites | FSBarV2 foundation .1a–.1h, correlated native results `.2c`, the full `.3` product/generated preview and the full bounded `.4` live-control outcome are delivered. FSBarV2 #5 at `f1a18c52` and HighBarV3 #2 at `680b6248` carry the paired source; six fresh local/generated pointer, keyboard and custom-guest journeys passed. `.5` useful tactical play is selected: its frozen additive contract enables native, broker and browser lanes in parallel. Factory rally freshness requires a bounded engine API join; source preparation does not close `.5`. `.7` publication, installed defaults and upstream adoption remain pending. | [BARC-01 repository research, design and feature roadmap](2026-09-08-134900-fable-bar-wasm-client-design-roadmap.md), [FSBarV2 foundation plan](https://github.com/EHotwagner/FSBarV2/blob/main/docs/roadmaps/barc-01-foundation.md), [browser/WASM preview](https://github.com/FS-GG/FSBarV2/blob/main/docs/roadmaps/barc-01-browser-preview.md), [live-control plan](https://github.com/FS-GG/FSBarV2/blob/main/docs/roadmaps/barc-01-live-control.md) and [live qualification](https://github.com/FS-GG/FSBarV2/blob/main/docs/roadmaps/evidence/barc-01.4-live-control.md). Section 0 records the source and native evidence; public BAR publication remains separate. |
| **Reusable Unity shim and full Fable replacement client** | Section 15 product track: native Unity dedicated-server bridge, title adapters, complete browser gameplay and custom WASM control; independent of v2 prerequisites | `FS.GG.Unity.Client` owns the source; .1a reference, .1b Nebulous admission, .1c Unity project source, .2a browser contract and .2b/.2c joined source are delivered. .2d opt-in browser-to-managed-reference journey is delivered through [#6](https://github.com/FS-GG/FS.GG.Unity.Client/pull/6) at `d83e4143`, with exact-head run `36682858322`; .1d genuine Unity Editor/Server qualification, publication and installed adoption remain open. No Unity-native play or default workspace effect is claimed | [UNITYC-01 research and design](2026-09-08-144823-unity-native-shim-fable-client-design-roadmap.md); [owning feature plan](https://github.com/FS-GG/FS.GG.Unity.Client/blob/1ff59f3235a433a4a9ca06173bd1258d389a5df1/docs/roadmaps/unityc-01-reference-admission.md) |
| **Four-spatial-dimensional grid tactics** | Section 15 independent product track; compare Commitment and Pressure under the accepted design-v2 amendment, with no V0–V6 prerequisite | `FS.GG.FourD`; .1–.4 source and bounded .5a technical cases remain delivered through #10 at `9109bfe061ece3637cad8e3fcafcfc6643458b43`, verified by run `36473282583`. V2 shared foundation and complete Commitment/Pressure pure encounters are source delivered through [#11](https://github.com/FS-GG/FS.GG.FourD/pull/11) at `b9126588`, verified by run `36525153306`; opt-in browser/save2 and technical comparison are delivered through [#12](https://github.com/FS-GG/FS.GG.FourD/pull/12) at `7739f9bd`, verified by run `36531113993`; the source-ready evaluation pack is delivered through [#13](https://github.com/FS-GG/FS.GG.FourD/pull/13) at `b7afd577`, verified by run `36534310893`, and v1 stays the browser default. Product entry, static archive and retained-reader preparation are delivered through [#14](https://github.com/FS-GG/FS.GG.FourD/pull/14) at `aced62aa`, with 30 existing + 3 isolated-archive + 4 retained-save browser checks. Private immutable distribution and downloaded-only product/save/origin qualification are complete through [#15](https://github.com/FS-GG/FS.GG.FourD/pull/15) at `abe58b3a`, protected-main run `36559749952`, artifact `11029645675` with expiry `2026-12-28T11:07:06Z`; no public host or access grant is added. Bounded automated V2 technical qualification and private handoff closed through [#18](https://github.com/FS-GG/FS.GG.FourD/pull/18) and [#19](https://github.com/FS-GG/FS.GG.FourD/pull/19), exact-main run `36676838988`, downloaded artifact `11080637269`. Original .5 player-derived usability, dependent .6, permanent publication and installed adoption remain open. Prospective R5 admission retains substantive original identities; planning and partial checkpoints do not count | [Algorithm roadmap](2026-09-08-152551-4d-grid-tactics-algorithms-design-roadmap.md), [accepted comparison design](roadmaps/2026-09-28-four-dimensional-skirmish-design-v2.md), [v1 technical boundary](https://github.com/FS-GG/FS.GG.FourD/blob/9109bfe061ece3637cad8e3fcafcfc6643458b43/docs/FOURD-01.5.md) and [owning design-v2 implementation plan](https://github.com/FS-GG/FS.GG.FourD/blob/main/docs/FOURD-01.design-v2.md); [owning .6 preparation window](https://github.com/FS-GG/FS.GG.FourD/blob/aced62aa72bee5509fc70cc96089cab2477cca0e/docs/FOURD-01.6.md); section 0 records source delivery and the remaining external boundaries |
| **Todo app — TODO-01** | Independent opt-in browser sample; whole TODO-01.1 covers add/edit/complete/filter/delete and retained local state | Templates source owner; whole TODO-01.1 delivered through [#648](https://github.com/FS-GG/FS.GG.Templates/pull/648), native checks and protected owning/tree readback verified | [Owning plan](https://github.com/FS-GG/FS.GG.Templates/blob/ba760d7725fe64b9311c20b57a05ef8630533804/docs/roadmaps/todo-app.md); [prospective enrollment](roadmaps/2026-09-29-r5-additional-app-enrollment.md) |
| **Tic-tac-toe — TTT-01** | Independent local two-player game; whole TTT-01.1 covers moves, wins/draws, terminal refusal and restart | Templates source owner; whole TTT-01.1 delivered through [#648](https://github.com/FS-GG/FS.GG.Templates/pull/648), native checks and protected owning/tree readback verified | [Owning plan](https://github.com/FS-GG/FS.GG.Templates/blob/ba760d7725fe64b9311c20b57a05ef8630533804/docs/roadmaps/tic-tac-toe.md); [prospective enrollment](roadmaps/2026-09-29-r5-additional-app-enrollment.md) |
| **Snake — SNAKE-01** | Independent keyboard game; whole SNAKE-01.1 covers food/growth/score, collision, pause and restart | Templates source owner; whole SNAKE-01.1 delivered through [#648](https://github.com/FS-GG/FS.GG.Templates/pull/648), native checks and protected owning/tree readback verified | [Owning plan](https://github.com/FS-GG/FS.GG.Templates/blob/ba760d7725fe64b9311c20b57a05ef8630533804/docs/roadmaps/snake-app.md); [prospective enrollment](roadmaps/2026-09-29-r5-additional-app-enrollment.md) |
| **Hello-world app — HELLO-01** | Independent browser app; whole HELLO-01.1 renders an accessible greeting through its actual entry point | Templates source owner; whole HELLO-01.1 delivered through [#648](https://github.com/FS-GG/FS.GG.Templates/pull/648), native checks and protected owning/tree readback verified | [Owning plan](https://github.com/FS-GG/FS.GG.Templates/blob/ba760d7725fe64b9311c20b57a05ef8630533804/docs/roadmaps/hello-world-app.md); [prospective enrollment](roadmaps/2026-09-29-r5-additional-app-enrollment.md) |
| **Coordination and product V2 boards — COORD-BOARD-V2-01** | Follow-on V2 planning surfaces: organization and product-scoped boards, selective issue carryover, fixed restricted refresh and product-local commands; no new full-acceptance prerequisite | `.github` owns the shared design; SDD/Templates publish product integration and product owners adopt. .1 design is selected; .2–.6 import/projection/adoption and published fresh/retained workspace qualification remain pending. Repository delivery authority stays native; no Home/Main autonomous agent is required | [Design and subroadmap](coordination/2026-09-29-coordination-v2-board-design.md) |
| **Language-independent workspaces and agent integration — V2-LANG-01** | Prospective product integration: portable contracts, reviewed toolchain profiles and cross-language/native product qualification; bounded AG-UI and Agent Framework trials | Coordination owns execution/protocol adapters; `.github` coordinates qualification; SDD/Templates publish profiles and product owners adopt. .1 and .3 are complete. .4 closed through Coordination #899 with a measured rejection of Microsoft Agent Framework 1.22.0 production adoption for this path. .2 v1 contract and P1 utility source are prepared; Coordination #900 delivers qualification-image source. Open #902 passed its six-case native executor gate on the prior candidate; fresh qualification after the protected-base rebase and P2 source delivery still await completion. .5 TypeScript, Rust and Go fixture source remains delivered; Templates #650 adds protected Rust/Go image profiles and a passing strict hosted native run. Full .5 adoption is open. P3 publication, P4 receiver adoption and P5 matrix closure remain open. Language independence is required for supported new routes; the frozen R5 cohort is unchanged | [Amendment and subroadmap](roadmaps/2026-09-29-language-independent-workspaces-and-agent-integration.md), [Coordination .3 source plan](https://github.com/FS-GG/FS.GG.Coordination/blob/5d86daf3898be683bd6720bdd36da479a29a0260/docs/roadmaps/v2-lang-agui-projection.md), [protected .4 trial decision](https://github.com/FS-GG/FS.GG.Coordination/blob/9516006663393709e8f96ecd1f21b9ce16d729bd/docs/roadmaps/v2-lang-agent-framework-trial.md), [merged image source #900](https://github.com/FS-GG/FS.GG.Coordination/pull/900), [open P2 executor #902](https://github.com/FS-GG/FS.GG.Coordination/pull/902), and the [protected .5 language-routes plan](https://github.com/FS-GG/FS.GG.Templates/blob/66ce4faacc122ef4a2d2331a0c10fe388e7c3b69/docs/roadmaps/v2-lang-language-routes.md) advanced through [Templates PR #650](https://github.com/FS-GG/FS.GG.Templates/pull/650) |

Each selected part produces a subroadmap in its owning repository, normally
`docs/roadmaps/<feature-slug>.md`. Link that document in the corresponding row, using a relative link for
`.github` plans and a repository URL for other owners. A draft on an implementation branch can be linked
as a draft; change the link to its durable location after delivery. Keep existing links when extending
or succeeding a plan, and identify their actual scope. “No subroadmap linked yet” means the index has no
plan to follow; check the owner for existing work before creating one. Do not create empty documents or
broken future links for the remaining rows.

The index is navigation. Native GS2 records and each ordinary subroadmap retain their own completion
authority; verify those before reporting status. Link maintenance can accompany planning or an already
needed implementation change and, across repositories, follow asynchronously. It does not become a
second planning-only PR, a per-milestone status update or a delivery prerequisite. Each subroadmap also
links back to this section and names its part so the relationship is navigable in both directions.

### 9.9 When new workspaces change

ADR-0091 does not change generated workspaces or scaffold defaults. Initial continuous V2 activation is
limited to `.github`; every other repository requires an explicit later selection. A repository's clean
epoch changes its operating route, not its generated files. The historical V0–V6 receiver rows below do
not create adoption work for the clean-start route.

For active C3 adoption, a selected receiver must prove its clean pinned installation and ordinary
settlement path. Historical fleet-wide retained-upgrade and old-client gates do not carry over;
receiver-specific compatibility promises still require their own tests. Existing installations without
such promises are repaired forward. Shared CLI publication does not select a scaffold default or
activate any repository by itself; each receiver's checked source and observed operation do that.

**New workspace contents change at the published scaffold/receiver boundary.** A roadmap stage or
producer source merge alone does not change the bytes delivered by an installed tool. Generated files,
available capabilities and enabled behavior can change at different times.

The inspected [SDD materialization path](https://github.com/FS-GG/FS.GG.SDD/blob/1bd80882d78f40b8a3348db3592c3889e1640716/docs/reference/scaffold-driver-materialization.md)
embeds pinned driver bytes when SDD is built and materializes them when the installed tool scaffolds a
workspace. [Templates composition](https://github.com/FS-GG/FS.GG.Templates/blob/8f85300e829fb886adba45dd82d9b3c8f2fa4ef8/docs/design.md)
combines versioned provider templates and overlays with the SDD-owned lifecycle. Thus a driver change
needs its producer publication, SDD pin adoption and publication, and use of that SDD release; a template
or overlay change needs publication and selection through its actual provider/template path. Other
receiver families use their own declared materializer. Inspect exact identities at planning time rather
than assuming every family updates together.

| Roadmap part / stage | What a newly created workspace can receive | Point at which that change becomes effective |
|---|---|---|
| **Baseline binding — V0 and predecessor releases** | Revised routine skills, selected checks, policy/guidance and automatic observation where the receiver supports them | Published producer bytes are adopted into the selected scaffold tool/template and a clean creation proves the resulting behavior. Eligible current-v1 improvements can arrive before v2 cutover |
| **Events and runtime qualification — V1** | Usually no new scaffold files; a later installed event/runtime service may improve reconciliation for enrolled workspaces | Qualification/replay alone has no installed effect. Any service activation or polling change needs its own supported receiver and operating authority |
| **Bridge and receiver fencing — V1** | Updated tool pins, helper preconditions or bridge guidance for affected families; production still follows the current epoch | The bridge is published and adopted by the scaffold/receiver family. A newly generated receiver must obey the same fence as an upgraded one |
| **Callable v2 and migration rehearsal — V2** | Published callable tools and additive preparation artifacts where included; sandbox/rehearsal capabilities can be available | The selected scaffold/receiver adopts those artifacts. Installed v2 capability remains distinct from permission to perform normal production writes |
| **Candidate and new-workspace qualification — V3** | The exact intended combination of tool, provider/template, lifecycle, policy, skills and required checks | GS2-10 explicitly includes or defers each receiver profile and qualifies fresh creation plus upgrade. This is the decisive clean-workspace qualification checkpoint; it does not itself open v2 |
| **Closed switch — V4** | Prepared receiver configuration and verified candidate bytes for the cutover scope | GS2-11–12 govern the closed-write window. Creating a workspace during that window does not escape its restrictions |
| **Open and ordinary use — V5** | Prepared and admitted workspaces can use enabled normal v2 operations and the qualified routine journey | Authoritative OpenV2, effective receiver configuration and current operation permissions. Already prepared files may stay identical while the permitted runtime behavior changes |
| **Carryover and retirement — V6** | Clean scaffolds omit retired v1 routes and obsolete ceremony for the adopted profile; upgrades preserve user-owned content under their supported contract | Published retirement changes reach the selected scaffold path and GS2-14 clean-install/old-client proofs pass. OperatingV2 and R5 efficiency remain separate claims |
| **Residual experiment — E0** | No general scaffold/default change from research | A prototype result only informs whether to fund the selected E1 capability |
| **Stable policy and statistical learning — LEARN-01** | Optional task/context/window observations and fixed execution profiles; later opt-in community export | .1 changes research inputs and .2–.3 change source capability. .4 first changes an enrolled installed route after actual producer publication, Main/runner adoption and selected SDD/Templates materialization. Qualify clean creation and retained upgrade separately; defaults remain unchanged. .6 separately qualifies local preview/consent, isolated Main intake and reviewed public aggregates; neither sharing nor experiments enroll users automatically |
| **Executor, host or scheduler — E1** | Optional tools, configuration or services for the specifically qualified work class | Each enabled capability's subroadmap includes publication, receiver qualification and a separate class/default decision; a source implementation or successful shadow run is insufficient |
| **Cooperative work — F0–F5** | Optional client/master enrollment and contribution capabilities for selected workspace/project families | F0–F3 establish protocol, sessions and lab/shadow behavior; F4 enables the authorized canary population; F5 can support a measured receiver/default decision |
| **SVG game engine foundation — SVG-FOUND-01** | Explicitly selected `fable-game` preview can receive reusable retained SVG scene interaction; existing provider/lifecycle defaults remain as selected | Release A published this explicit path through Rendering 0.29.0 and Templates 0.11.0. Public clean and retained receivers pass; later lifecycle/default activation keeps its own SDD/Templates and applicable epoch prerequisites |
| **SVG installed model qualification — SVG-QUAL-01** | Explicit typed-SDD/profile-2 SVG fixtures can receive exact-tool provisioning and model/reducer correspondence; existing provider/lifecycle defaults remain unchanged | SDD 1.7.0, Rendering 0.29.0 and Templates 0.11.0 are public. Installed public typed clean and retained receivers replay matching 192+192 .NET/Fable transition corpora; later default activation remains a separate boundary |
| **SVG scene/renderer — SVG-SCENE-02** | Opt-in Fable SVG candidates gain identified documents, affine transforms, selected SVG definitions/paint/text, accessible interaction and export | Release A delivers the .7 scene/renderer through public direct, SDD, typed, wizard and retained receiver routes. Selected browser observations pass; heap, presentation timestamps and physical mobile remain unavailable and belong to later milestones |
| **SVG Preview A — SVG-PREVIEW-A** | The public release remains opt-in: all 17 `FS.GG.UI.*` libraries plus BOM and `FS.GG.UI.Template` at Rendering 0.29.0, then `FS.GG.Workspace.Template` 0.11.0. The wizard remains 0.11.1 and uses its two-step `fable-game` then SVG-adopter route; `svg: false` and lifecycle `sdd` remain omitted | Release A is complete. Fresh direct, SDD, typed and wizard receivers consume the public packages; existing projects change only through the bounded adopter from public Templates 0.10.0. No provider activation or default changed. Releases B and C subsequently completed; SVG-WORKSPACE-01 is selected |
| **SVG Preview B — SVG-PREVIEW-B** | Public Templates 0.12.0 composes Rendering 0.30.0, Game 0.15.0 and Audio 0.6.0 for explicitly selected authoring/local-play previews | Public direct, SDD, typed/profile-2, wizard and retained receivers passed; existing workspaces use the bounded adopter. Defaults remain unchanged. |
| **SVG Preview C — SVG-PREVIEW-C** | Published Templates 0.13.0 composes public Rendering 0.31.0, Game 0.16.0 and Net 0.6.0 for explicitly selected replay/network/scale previews | .3 first changes generated source composition; .2/.4 publish producers/Templates and .5 proves public clean creation and separate retained 0.10–0.12 adoption, conflicts, interruption and rollback. Producer and Templates publication, source adoption and installed public receiver qualification are complete. No lifecycle/default activation is implied. |
| **Complete SVG workspace — SVG-WORKSPACE-01** | The complete [Templates plan](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/roadmaps/svg-workspace-01.md) adds owner-published guidance, installed SDD selection, a default SVG player and optional studio/tactical/arcade/complete bundles | .1–.6 are frozen. Clean and retained installed/development proof, local containerized Caddy deployment/rollback and evidence freeze pass. Durable public hosting is deferred. Public Templates/wizard adoption and activation proceed through [Release D](roadmaps/svg-release-d.md). |
| **Unity replacement client — UNITYC-01** | Explicit opt-in Unity bridge, browser gameplay and WASM authoring composition in `fs-gg-fable-game` | .2 enables the selected source product's reference game; .6 publishes/adopts coherent producer and template bytes and proves separate clean/upgrade receiver journeys. Source merge alone has no installed effect |
| **4D grid tactics — FOURD-01** | No general scaffold/default change; the accepted Commitment/Pressure comparison adds explicit opt-in product prototypes | .V2.4 first exposes revised gameplay in product source while v1 stays default. Bounded .6 P1 changes source startup to the fixture-free product entry; P3 qualifies a local static candidate and P2 proves same-origin old-save readers. These are source/local-candidate changes, not installed adoption. Producer publication, clean creation and retained upgrade adoption remain separate. Product ownership is retained; no template extraction is selected without receiver demand |
| **Coordination and product V2 boards — COORD-BOARD-V2-01** | .2 first exposes the organization pilot. GitHub-coordinated SDD/Templates families receive selected V2 board bindings, product-local commands and guidance through .5 publication and .6 adoption | .3 qualifies the shared projection route and .4 switches organization consumers. .5–.6 require pinned producer publication, fresh generation and separate retained adoption with scope isolation. Local-only workspaces and provider/lifecycle defaults remain supported. Design delivery changes no generated bytes. |
| **Language-independent workspaces — V2-LANG-01** | .2 publishes portable tooling and component/toolchain profiles; .5 verifies actual non-.NET and mixed-language product routes. .3–.4 trial optional UI/agent adapters | Qualify exact artifacts through fresh, retained and local-only journeys, native product checks, cancellation and recovery. Product code imports neither Akka nor an agent SDK. Language fixtures are prospective and do not rewrite delivered apps or historical R5 outcomes. This amendment changes no generated bytes or installed defaults. |
| **Enrolled browser apps — TODO-01 / TTT-01 / SNAKE-01 / HELLO-01** | Explicit source samples outside packaged template content; each app first changes behavior when a user runs its source entry point | No generated-family/default change or producer release is selected. Clean source-checkout/browser proof is required; public scaffold adoption and retained generated-workspace upgrade are separate, unselected work. |

For every subroadmap, state **which workspace families change, what the user sees before and after,
and the first milestone that can deliver that change**. Name the producer and scaffold/receiver adoption
steps, exact published identities once known, the default or explicit opt-in choice, and a clean-creation
acceptance example. Treat existing-workspace upgrade separately: publishing new scaffold bytes does not
rewrite existing files, and the inspected SDD backfill preserves already-present owner-sourced skills.
If the part has no workspace effect, say so; if publication or adoption is pending, keep that explicit.
Record this in the feature plan and existing evidence rather than a new fleet registry.

Routine delivery does not silently change the omitted lifecycle from `sdd` to `typed-sdd` or `none`.
The temporary `work-unified-roadmap` coordinator remains repository-owned; it is not automatically
installed in product workspaces. Its eventual shared-driver successor must pass the same publication and
materialization boundaries. The SVG/Fable product work retained in section 15 has its own provider and
default decisions; progress through V0–V6 alone does not select a new product template or game runtime.

## 10. Exact GS2 integration and contract-change boundaries

For ADR-0091, the active boundary is C0–C3 and the ordinary repository protections described in section
9. GS2-09 through GS2-14 require no new integration for the clean-start route. The table below records the
superseded staged design and remains useful only as historical context or for a separately revived route.

| Existing surface | Proposed integration | Required handling |
|---|---|---|
| GS2 execution/evidence rules and 05.6/05.7 | Separate migration acceptance from ordinary profile delivery; preserve actual grants and automatic evidence | Amend owning model/caller/contract where necessary; keep historical receipts unchanged |
| GS2-06.7 | Consume the adopted reduced obligation set with explicit selection guarantee | Publish/qualify policy and aggregate behavior; do not reinterpret the accepted soundness receipt |
| GS2-07.6/07.7 | Burst/coalescing, unrelated subjects, base movement, required-context identity and observer isolation | Reconcile already registered contracts before new acceptance cases are asserted |
| GS2-08.3–08.9 | Include every predecessor-created writer and installed helper route in fencing and old-client tests | Extend the current census and coverage through its owner; disable unsupported writers before freeze |
| GS2-09 and migration runtime wiring | Reuse accepted callable 0.1.1 and .1–.6 contracts; finish complete provider capture and real .7/.8 rehearsal | Keep installed source-delivery permission distinct from migration authority; qualify the exact closed interpreter and representative copy population |
| GS2-10.1/10.5 | Freeze the profile, enforcing components, tool/guidance identities and receiver classes | Enable-and-qualify or explicitly defer; no proposal frozen as implemented behavior |
| GS2-12.7/12.8 | Closed routine journey plus complete protocol journey and negative cases | Preserve no ordinary production writing before OpenV2 |
| GS2-13.3 | Actual enabled ordinary-v2 journey plus required protocol capability coverage | Native/provider evidence from installed tools, not an in-memory fixture |
| GS2-14.1/14.2 | Common routine measurements and attributed repairs | Separate R5 efficiency from existing operational gate authority |
| GS2-14.5–14.10 | Remove superseded caller ceremony and release deliberately deferred changes | Preserve archive meaning and current-epoch admission; later defaults get their own receiver observation |

The adoption sequence is policy/guarantee decision, required governing amendment, producer implementation
and publication, receiver preparation, owning unit/catalog reconciliation and pin refresh, qualification,
then authorized activation. Any change after freeze follows the existing new-candidate/full-rerun rule.

The source roadmap is consumed by exact revision and full-file digest. This document is not that input
and changes none of its bytes or pins. A later accepted roadmap amendment needs the normal reviewed pin
refresh and unit-contract disposition; readers cannot pair old catalog digests with newer document bytes.
Keep one execution owner and existing cross-repository dependency mechanisms instead of creating a parallel
master-plan state service.

## 11. Acceptance examples by changed boundary

These examples qualify a profile/component or migration boundary once at the relevant scope. They are not
a mandatory checklist repeated for every ordinary PR.

| Scenario | Expected result | Stage/process owner |
|---|---|---|
| Two isolated routine code PRs | Independent progress with selected checks, no global board-scan wait or manufactured claim | V0/V3 receiver integration |
| Two routine workers need one exclusive environment | One valid usable grant; only the conflicting action waits | V0 resource adapter and Coordination |
| Partial authorization facts or unreadable policy | Affected effect is refused or pending; source delivery remains routine unless a human selected heavyweight process | V0 policy/Governance handoff |
| Existing strict work continues without a new human heavy instruction | Existing evidence is retained; one-owner routine delivery is used | V0/V1 caller and fence |
| Candidate changes policy or check-selection inputs | Routine source delivery with trusted-base exact-head validation and all applicable native checks | V0/V3 enforcement |
| Ordinary source changes after checks | Same PR, relevant fresh checks and changed-head refusal | V2/V3 delivery |
| Unrelated target movement | Accepted native integration/key refresh; no agent-mediated review/claim restart merely to copy unchanged facts | V1/V3 with existing freshness semantics |
| Current grant revoked after a green check | Stale protected effect rejected with declared in-flight ordering | V1/V4 authority qualification |
| Merge succeeds but response is lost | Native readback identifies delivery without duplicate effect | V2/V4 delivery recovery |
| Provider outcome remains unknown after timeout | Pending settlement retained; no false cancellation or completion | V2/V4, later E1 |
| Provider proves absence; an old response arrives after authorized retry | Absence does not advance completion; retry preserves operation identity with an exact retry discriminator, and stale observations cannot settle the new attempt | E1 hosted-writer evidence; V2-CALL-01.4 checks its own applicable production semantics |
| Process restarts after retry intent is persisted | Recovered intent retains the metadata required by the real store and effect path; no duplicate dispatch or fabricated completion | E1 installed-fix qualification; applicable V2 callable recovery |
| Trace bytes change or a negative mutation is injected | Changed inputs invalidate reuse; invalid traces or mutations fail at the intended boundary with first-divergence diagnostics | Owning modeled component and formal qualification |
| Observer disappears or usage is corrupt | Known delivery survives; measurement remains insufficient | V3/V5 profile |
| Balanced supplied usage omits an attempt | Independent coverage reveals or bounds the omission; no qualified efficiency claim | V3/V6 measurement |
| Scheduled economics producer and delivery observer meet | Correct source/operation identity, schema and whole-unit join; actual artifact retrievable | Predecessor input verified at V0 |
| Shared setup batch fails but isolated partitions pass | Interaction finding remains; no invented green | E1 only if batching enabled |
| Concurrent reservations race or usage arrives twice | Only committed allocation dispatches; no overspend or double release | E1 |
| Budget expires and remote success arrives later | Item and effect statuses remain distinct, settlement owner finishes recovery | E1 |
| Host/database restore resumes an old generation | No stale capability or effect replay can mutate the provider | E1 hosting |
| Clean and upgraded receivers invoke different guidance | Profile qualification fails until effective behavior agrees | V3/V6 and E1 adoption |
| Old helper/client attempts a write after OpenV2 | Refuses before effect for independently observed fencing reason | V5/V6 |
| Optional scheduler unavailable | Supported same-epoch routine path remains usable; unresolved effects stay with recovery owner | E1 canary |
| Cooperative client reconnects after a lost acknowledgment | Resume the same bounded assignment; valid verified contribution can reach one observed delivery | F1–F4 |
| Cooperative client submits forged evidence or a revoked generation | Client claim cannot satisfy independent verification or authorize delivery; preserve the refusal and original attempt cost | F2–F4 |
| Duplicate release identity has different bytes | Refusal; no automatic new version to hide mismatch | Inherited release behavior, V4 applicable journey |

## 12. Risks, stop conditions and operating ownership

| Risk | Early signal | Response and owner |
|---|---|---|
| Simplification leaks into protected semantics | A routine label skips grant, epoch, formal or release evidence | Stop affected class; policy and runtime owners repair the actual boundary |
| Required guarantees reappear as ceremony | Routine receiver demands phase comments, receipt PR or synchronous usage | Repair caller/configuration; do not weaken unrelated kernel semantics |
| Parallel OR/PB build duplicates state | Two reservation, retry, outbox or terminal-state implementations | Stop duplication at E0; Coordination owns one shared component |
| Freeze repeatedly loses validity | Candidate-input churn causes repeated full qualification | Reduce concurrent changes, decide deferrals and reschedule before closure |
| Insufficient operating coverage | No named settlement owner, unavailable backup, unrehearsed rollback duration | Do not schedule protected activation; operational owner supplies the missing capability |
| Qualification proves internal artifacts only | Green adapter suite with no installed callable journey | Close runtime/receiver wiring gap before V3 |
| Efficiency is obtained by exclusions | More refused/deferred work, missing attempts, enlarged tasks or uncharged maintenance | Reject comparison, retain baseline and repair accounting |
| Budget promises exceed provider control | Cancellation does not bound charges or late work | Mark observational/ineligible for strict cap; qualify a supported adapter before enabling |
| Formal state space grows with product detail | Check time explodes before useful implementation feedback | Bound or partition the abstraction with explicit coverage and exclusions; enforce process deadlines, measure preparation separately and retain production correspondence |
| Source qualification is mistaken for installed recovery correctness | A source fix has no selected artifact/readback or affected installed-path evidence | Coordination and installed operator disposition the exact artifact delta; preserve historical acceptance without projecting it onto newer bytes |
| Low volume cannot support tail claims | Wide intervals or too few completed observations | Use insufficient-data and a bounded continuation; do not invent p99 qualification |
| Trusted-writer scope expands accidentally | Untrusted execution inherits routine credentials/check assumptions | Keep class disabled until separate threat model and controls are qualified |
| Peripheral programmes consume migration capacity | UI, federation or default flips become assumed dependencies | Apply section 15 dispositions; only demonstrated accepted blockers join GS2 |

One accountable delivery owner is named when an existing unit/operation starts. Repository ownership in
this proposal is not a claim that staffing or operator availability is already assigned. Before a hosted
or protected class is enabled, its owner records recovery, support, cost and retirement responsibilities
in existing operational sources. New missing capabilities become specific owned gaps, not a universal
bureaucracy checklist.

For the routine comparison retain the predecessor's stop conditions: severe credential/data/authority
incidents stop admission immediately, and two process-attributable rollbacks among the first ten code
items stop expansion for diagnosis. Other defects are judged by impact and recovery cost. For an optional
controller, a hard invariant or enforceable-budget violation stops new admission; current safe effects
still settle. Stopping a prototype or leaving a class unsupported is a valid outcome.

## 13. Completion claims

| Claim | What must be true |
|---|---|
| Current-route simplification handed over | The predecessor's functional current-route outcomes and truthful observation are complete; historical comparison remains explicitly insufficient |
| V2 candidate qualified | Exact candidate and installed receiver evidence meet the accepted GS2 contract, including any profile actually claimed |
| V2 opened | Authoritative OpenV2 and permanent v1 writer fence; normal v2 classes enabled under their actual authority |
| Migration complete | Existing Q10, contraction, clean-install/old-client proofs and OperatingV2; no unowned required follow-up |
| Selected-profile functional V2 accepted | R2 observer-loss behavior, R4 effective routine route, all-15 repair accounting and native settlement/replay/Authority evidence pass for the nine selected receivers |
| Simplification efficiency benefit established | Independently sufficient comparable usage, population, cost and delivery evidence passes the declared economics predicates; the current verdict is insufficient and no benefit is claimed |
| Execution experiment successful | A measured residual need is improved at acceptable total cost; all applicable correctness/budget/recovery claims pass |
| OR/PB capability adopted | One exact operation class and policy has authorized canary, receiver/fallback qualification and an explicit default decision |

No checkbox, merged design or self-consistent telemetry record substitutes for these outcomes. No new
global “all programmes done” gate is introduced. In particular, migration may complete while an optional
optimizer is deliberately rejected or the separate numerical simplification claim remains pending.

## 14. Source-plan consolidation and retained detail

This proposal is the single integrated narrative and recommended future sequence. The sources below
retain accepted contracts, historical evidence and detailed optional requirements. Their future prose is
not a second queue to execute beside this one. When adopted, change the actual owning contracts and use
successor links instead of rewriting old receipts. No source is declared operationally superseded merely
by this documentation merge.

| Source plan | Consolidated disposition |
|---|---|
| [Development master](development-master.md) and [inventory](development-design-inventory.md) | Navigation, census and historical discoverability; this proposal supplies the post-simplification design |
| [R0–R5 radical simplification](2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md) | R0–R4 are predecessor inputs; R5 bindings and proof join V0/V3–V6; retain its accounting and stop rules |
| [V2 governing design](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md), [architecture amendment](coordination/2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md), [roadmap](github-substrate-v2-roadmap.md) | Retain accepted journal/reconciler architecture and all current GS2 gates; V1–V6 group the remaining outcomes |
| [Governance-preserving proposal](coordination/2026-09-07-150716-governance-preserving-ci-simplification-design-proposal.md) | Integrate its independent classification, enforcement ownership, freshness and fallback decisions in V0 and sections 3–6 |
| [OR design H0/H1](coordination/2026-08-31-operations-research-first-agent-orchestration-design.md) | E0 need/baseline and bounded experiment; six graphs and the full planner catalogue are scoped to enabled needs |
| [LEARN-01 stable-policy orchestration and statistical learning](roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md) | Selected design refinement across V0/E0/E1: reuse UTEL and O0–O3, add decision-time task/context/experiment observation and a controlled context comparison under stable broad profiles. Earn finer/adaptive routing through robust repeated evidence. Later opt-in Main intake and reviewed GitHub aggregates extend participation without duplicating execution or changing internal/GS2 authority; source, installation, experiments and community rollout remain future work |
| OR H2–H5 | Selected single-host actor, execution and recovery foundations are owned by standalone O0–O3; reuse their accepted evidence in E1. Additional authenticated hosting, verified planning and federation requirements remain conditional gaps, not a duplicate executor build |
| OR H6/H7 | Class-specific mutation canary and separately decided normal service use after OperatingV2 by default; preserve supported routine path |
| OR H8 | Availability expansion only if measured single-node recovery is insufficient |
| OR F0–F5 federation / cooperative client and master orchestrators | Explicit retained feature in section 8.5 and staged track in section 9.7; later conditional implementation with bilateral admission, generation-bound assignments, hostile-code verification, credential isolation, quotas, revocation and result provenance |
| OR visualization catalogue | Optional question-driven views; retain identity, accessibility, privacy, truthfulness and bounded rendering for every shipped view |
| [PB0](coordination/2026-09-06-performance-bounded-development-flow-design-and-roadmap.md) | Share E0 baseline with the completed predecessor; no second measurement service |
| PB1/PB2 | Reuse standalone O0–O3's matching canonical state, reducer and bounded reservation contracts; E1 adds only unmet verifier/experimental requirements and qualifies correspondence |
| PB3/PB4 | Reuse the selected O0–O3 executor rather than build another; batching/review/routing extensions retain their separate justification. Keep finite repair and durable settlement; defer adaptive modes |
| PB5 | Optional view over canonical state; tables suffice for first qualification |
| PB6/PB7 | E1 replay/simulation/shadow and separately authorized online comparison; preserve sample sufficiency and delayed-defect follow-up |
| PB8 | Later class/default decision and measured cadence improvement; no universal controller adoption |
| [September 4 telemetry automation](reports/2026-09-04-fsharp-roadmap-telemetry-and-projection-automation-design.md) | Reuse useful deterministic collection/projection. Do not implement removed routine phase/receipt obligations merely to automate them; protect remaining GS2 evidence contracts |
| [OR/PB alignment analysis](coordination/2026-09-07-112837-development-flow-proposal-alignment-analysis.md) and [v2 CI integration analysis](coordination/2026-09-07-131445-v2-roadmap-ci-simplification-analysis.md) | Rationale retained; section 10 turns the proposed integration into one explicit owner mapping |
| [Quint-first migration](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md) and [Q1 amendment](coordination/2026-08-26-adr-0077-q1-qualification-amendment.md) | Reuse actual published SDD capabilities; distinguish backend, consumer and default activation; no restart from dated Q2 wording |
| [Completed Choreo correspondence programme](https://github.com/FS-GG/FS.GG.Coordination/blob/8135bb68bac07e941897ec556568c52ade6a492c/docs/roadmaps/choreo-akka-fsharp-trace-correspondence.md) | Reuse the pinned source, bounded model, real Quint traces, production replay and qualified gates as E1 foundation evidence. Section 9.6 owns installed-fix disposition; section 9.3 owns the callable coverage comparison. No automatic model migration, federation completion or installed adoption |
| [Typed SDD/Governance successor](coordination/2026-08-24-174459-typed-sdd-governance-integration-design.md) | Deferred constitutional integration is separate from reuse of already-shipped Governance; changes to defaults respect candidate/epoch rules |

## 15. Wider portfolio: preserved without hidden prerequisites

“All current plans” here includes the master's active development tracks and the dependencies that can
affect them. Further-development proposals remain visible below. Their component work is not cancelled;
their own owner may continue authorized independent work before freeze. Candidate-input changes obey
section 4.3 regardless of whether the originating programme is considered peripheral.

| Programme or lineage | Future place and process | Relationship to this roadmap |
|---|---|---|
| [Accepted SVG game engine and Fable workspace programme](2026-09-07-064259-svg-game-engine-template-design-roadmap.md), with [SVG-FOUND-01 foundation](roadmaps/svg-game-engine-foundation.md), [SVG-QUAL-01 installed model qualification](roadmaps/svg-game-engine-installed-model-qualification.md) and [revision rationale](2026-09-07-121207-svg-game-engine-roadmap-revision-proposal.md) | Product-owned ordered feature sequence through complete C01–C20, M0–M11, section 13 and Releases A–D; modeled game/protocol semantics where relevant, published packages and generated-workspace journeys | Preserve SDD/Game/Rendering/Net/Audio/Templates ownership, capability scope and default-player distinction. S.I.R. is strictly read-only for the entire programme. Independent source/qualification work has no V0–V6 completion prerequisite, while protected releases and later defaults keep their own authority and epoch evidence |
| [Polyglot web architecture](reports/2026-07-27-163509-polyglot-web-product-architecture-and-implementation-design.md) | Provider/contract process and installed consumer qualification | Preserve neutral TypeScript versus selected Fable/game providers; reconcile existing ADR-0071 implementation before creating residual work |
| [Fable bindings/Glutinum proposals](reports/2026-09-03-101829-fable-bindings-glutinum-quint-analysis.md) and [Xantham evaluation](reports/2026-09-08-105454-xantham-fable-bindings-evaluation.md) | Producer-owned converter/adapter slices, public contract and runtime proof; the Xantham feature is indexed in section 9.8 | Analysis completion is not implementation; no new converter is required for v2 unless a demonstrated generic dependency exists |
| [FsQuint reusable Quint–F# integration](roadmaps/2026-09-18-065148-fsquint-design-and-roadmap.md) | Implementation moved to [FsQuint](https://github.com/FS-GG/FsQuint/blob/main/docs/roadmaps/fsquint.md); inspected Coordination and SDD consumers document stable 0.1.0 adoption | Reuse the published generic package and consumer-owned models/replay. Do not reopen extraction from historical unchecked design rows or infer installed Host adoption |
| [S.I.R. Quint handbook](2026-08-27-sir-combat-quint-learning-handbook-design-and-roadmap.md) | S.I.R.-owned learning/publication; executable examples require their actual domain evidence | Learning work can proceed independently; no cross-project index becomes combat authority |
| [Symbology showcase](reports/2026-07-05-starcraft2-unit-symbology-library-design.md) | Optional rendering/product experiment and owner compatibility check | Deferred selection, no implicit current priority |
| [Coordination churn redesign](reports/2026-08-14-090508-coordination-churn-redesign-roadmap.md) and [change amplification](coordination/2026-08-22-coordination-change-risk-mitigation-design.md) | Map surviving release, completeness and recovery defects to the current owner | Reuse delivered work and predecessor simplification; do not restart historical stages |
| [Native collaboration supervision](reports/2026-07-30-150617-native-collaboration-runtime-supervision-design-and-roadmap.md) | Remains historical/on hold; relevant needs enter E0 | No separate competing hosted runtime |
| [Skill context budgets](reports/2026-07-25-223627-skill-context-budget-and-progressive-disclosure-roadmap.md), [rollout](reports/2026-07-26-skill-context-budget-rollout-evidence.md), [vendoring](reports/2026-07-01-skill-vendoring-robustness-roadmap.md), [workBoard](reports/2026-07-21-workboard-single-repo-board-driver-design.md) | Inspect current publisher/materializer and receiver behavior; fix real remaining gaps | Feed V3/V6 receiver qualification; preserve current owner-resolved roots and avoid reviving obsolete copy machinery |
| [Consumer documentation](reports/2026-07-21-consumer-documentation-roadmap.md) | Update actual supported journeys through their owning documentation process | V6 public guidance; no complete rewrite from historical unchecked prose |
| [F# kernel design](coordination/2026-08-24-typed-protocol-kernel-design.md), [P-series roadmap](reports/2026-08-24-094348-typed-protocol-kernel-roadmap.md), [original engine](design/coordination-engine.md), [Phase D](2026-07-15-phase-d-corpus-through-shim-plan.md) | Historical implementation and forensic evidence; successors own new work | Preserve needed compatibility until qualified retirement; use [D.4](2026-07-16-d4-differential-disposition.md) and [payoff dispositions](2026-07-16-phase-d-payoff-disposition.md) before reopening old port work |
| [Earlier executor fencing](reports/2026-08-04-github-native-executor-fencing-design.md) and [Game extraction](reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md) | Design lineage and ownership evidence | Current accepted v2 journals and actual component owners govern; no resurrection of earlier authority mechanisms |

The [full inventory](development-design-inventory.md) retains the June split plans, remaining reports,
ADRs and reference corpus. This review does not certify all of their implementation status. At future
selection, inspect owner evidence, fold duplicate residuals and deliberately defer the rest. Preserve a
successor link rather than turning historical unchecked items into a new backlog.

## 16. Decisions needed to activate this successor

Routine source delivery and the selected O0–O3 foundation are already adopted. They do not need a new
programme-wide approval. Resolve the following decisions only at the boundary that consumes them:

| Boundary | Remaining decision or evidence | Owner |
|---|---|---|
| V0 / GS2-10 | Bind the effective routine profile, retained technical obligations and supported/deferred receiver population. Keep R4/R5 efficiency claims separate from operational acceptance. | `.github` policy, Coordination and actual receivers |
| I1 / before GS2-10 | Select the trusted CI credential topology and exact ordinary operation class; qualify the one-attempt source/effect join or explicitly defer it. Review the current v1 and `OpenV2` human gates separately if either is to change. | `.github` policy owner with Coordination and credential custodian |
| GS2-09.7 | Exact representative copy set, executable candidate, missing-provider dispositions, effect authority and recovery/cleanup ownership. | Coordination and protected-operation owner |
| GS2-10 | Freeze artifact/default identities; finish or defer concurrent candidate changes; accept the measured closed-write window, latest abort point and staffed recovery plan. | Cutover owner with producer/receiver owners |
| GS2-13 / GS2-14 | Irreversible production open, actual observations, contraction and `OperatingV2` evidence. | Existing protected cutover authority |
| Release D.5 | Complete: public SDD 2.0.2, Templates 0.15.0 and wizard 0.12.0 passed clean creation before and after protected pin activation #3955. Repeat 36474756648 and independent default readback establish omitted `typed-sdd`/`quint-specification-v1`. Retained upgrades remain separate. | SDD, Templates and default-policy owner |
| Optional E0/E1 / F0–F5 / LEARN | Select a measured residual need and bounded next window; authorize only its specific installed/canary boundary. | Owning feature and operation owners |

The 10% bureaucracy ceiling and intervention definitions in section 7.4 remain in force; incomplete
measurement does not certify compliance. This document introduces no extra admission, board census,
reporting service or approval cycle. The next bounded implementation window includes I1's
parallel source slices alongside §9.3 and the owner-native contracts, with the joins and
permission limits shown in §9.1.
