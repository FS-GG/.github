# LEARN C2 persistent receiver v3

Owner: `.github` owns the inactive preparation boundary and its source qualification. Root owns
protected integration, publication, container qualification, receiver custody, and Unified section 0.

## Before P1

The retained persistent receiver is v2. Published Host 0.4.0 adds the inactive preparation
reader; immutable Host 0.3.0 and 0.2.1 releases remain historical. There is no v3 image, installation,
grant, activation, capture, custody, or native acceptance. The protected Coordination manager can emit
v3 installation records, but that alone does not establish a receiver.

## P1 — inactive preparation and native CI

- [x] Merge the reviewed stateless F# constructor and CLI. They accept closed, pinned public or
  explicitly synthetic declarations and emit an inactive result whose installation, capture, custody,
  activation, and native-acceptance flags remain false.
- [x] Require the constructor console in `telemetry-host-package`: acquire the public Coordination
  manager at exact revision `e6f6631a2166f9d73639b1cbb96953ee3a768530` and tree
  `9059c91396cc2963d15037867cde9a5a3cf1c9ba`, restore locked inputs, build sequentially, and execute
  with both manager and preparation executable paths present.
- [x] Require the joined actual Host installed-origin tests with those same executable paths. The
  preparation output supplies exact source-reference/runtime-manifest bytes and manager arguments;
  the actual manager output supplies the Host sidecar and receipt.
- [x] Root reads the protected workflow result for the exact merged source. P1 becomes Closed only
  after that native result passes. A local or pull-request result is source evidence and does not close P1.

P1 Closed on 2026-10-02: [PR #4084](https://github.com/FS-GG/.github/pull/4084) merged at
`d9a142de6b0b615604b02779d5eae963010acc15`, tree
`34ecbaf027646f0454f5ce7f1d30aac61c0f8cf0`, equal qualified source
`1898c28ee98b8f4c0146eb011a022cb5c460f02a`. Root authenticated the merge and protected
[native Host package run](https://github.com/FS-GG/.github/actions/runs/36953495898), which
passed for that exact merged source. The thin shell entry executes the typed constructor console;
the existing Python selector and fixture remain unchanged. Installation and activation remain false.

The CI change is a linear restore, build, console, and focused-test sequence inside the existing Host
job. Static YAML/shell checks and the actual focused tests are proportionate preflight. A new model
would duplicate these direct controls without exercising retry, fan-out, shared artifacts, or a new
state machine, so P1 adds no custom model.

## P2 — publication and root container after P1 Closed

P2 started after root recorded P1 Closed from the protected exact-head workflow. Host successor source
and publication are now Closed. Root still owns the separate Coordination manager distribution,
complete runtime/image closure, reproducible OCI construction, and protected container qualification.
P2 preserves the published Host 0.2.1 package, tag, release, manifest, and journal as immutable history.

### P2-A — Host successor source

The additive installed-origin reader selects source version `0.3.0`, stable tag
`telemetry-host/v0.3.0`, and CanonicalAuthority ledger release `utel-host-rel-09`.
The candidate, artifact, admission and publisher selectors agree; the existing publisher
state machine and default `publish=false` remain unchanged. Historical 0.2.1 and older
refusal fixtures remain.

The P2-A source window is Closed on 2026-10-02: [PR #4086](https://github.com/FS-GG/.github/pull/4086)
merged at `c10885ead065f6724a843ea9c10bd51793e1a120`, tree
`c55d71b5cabdd61d49724434c796bd438e91e278`, equal qualified
`a494e3cdd0be95b9cc72f597cf5af7cd5f355dc1`. Root authenticated the merge,
tree and protected main, then verified the exact protected
[Host package workflow](https://github.com/FS-GG/.github/actions/runs/36959036061)
passed. Native CI repaired the generated publishing skill manifest in the same
PR; all eight independently reviewed implementation blobs remained unchanged.
This closes successor source preparation. Publication and later adoption remain separate gates.

### P2-B — served artifacts

The served-artifact gate required root to acquire a first-attempt candidate, verify its exact
package/manifest/source bytes, qualify the existing publication authority, publish one retained
payload to both feeds and read back their normalized equality.
Publication does not follow from a source version bump. The Coordination manager requires its
own read-only Actions archive route with source and complete published-directory hashes;
the existing two-component orchestration archive does not contain the manager. Artifact
expiration and actual acquisition remain explicit. No new manager package or durable release
destination is selected here.

Host publication is Closed on 2026-10-02. The exact protected
[publisher run](https://github.com/FS-GG/.github/actions/runs/36964135216) succeeded for source
`f43e0a1f94448aa8f7668b1ed72f3169a7cf925e`. Release
[`telemetry-host/v0.3.0`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.3.0)
is public as release `401538149`; its GitHub archive SHA-256 is
`c7cbaa474fdcd0ba577f8577ec92bb381f032db7842f86ddb1b3fb050d8a97ad`. The signed nuget.org
archive SHA-256 is `7ad2c30894cb3eafbf498e5d247034bc3167ee30dd07c8b09c6b4915657c598a`;
both feeds expose normalized producer payload SHA-256
`5572aa61f284abc5a37a12aa88f99b5169c4379be2aa9959e46c9934532f2836`. The release manifest and
publication journal SHA-256 values are respectively
`7d61b4888d08299b5578df2e3e283dee88b5acabafa53490b4ba8d00e6676704` and
`ec63822c627cfad490f9eea731257ac531c9ac3bd681f4790b9489dcfc8470ce`.

Coordination manager source is also Closed: [Coordination PR #923](https://github.com/FS-GG/FS.GG.Coordination/pull/923)
merged at `bd4de1e999289c7d2a73bcaaf58177f903adc797`, tree
`b2e06caa53db83ace4d0c44448aea20c0f82747e`, from qualified source
`0591497fa05e2cd12ab80311125fc421a8124701`; protected native-coherence
[run `36961953645`](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36961953645) and its
bootstrap passed. Manager distribution is now served by successful run `36967367250`: artifact
`11209647998` expires `2026-12-31`, contains three entries and 19 manager payloads, and has archive
SHA-256 `0387deaeddcdce45a3664c2a207b1ec003c7b9fdf467d4a18dd117ed7508620f`; the raw ZIP SHA-256 is
`ce868989d1b78535d2db6000721d765e4a10a03443aa139b3fc38f12f087d78d`.

The actual local compiled verifier exited 2 for that hosted archive. All 189 Core payload bytes match,
but hosted dotnet and hostfxr content differ from the required target and all 191 runtime file modes
are `0777` rather than the required target modes. The refusal preserves the boundary between served
distribution and a qualified inactive image. Image qualification, grant, capture, recovery and C3
activation remain open. Main has no action.

### P2-C — inactive v3 container

A new v3 recipe must consume the served Host payload, qualified manager directory, unchanged
canonical verifier, selected native executable/profile, and complete pinned .NET/Python/native
runtime and search closure. Preserve the active v2 and HOST qualification recipes. Require two
isolated builds from identical inputs and truthful OCI digest comparison, then disposable,
credential-free manager-to-Host qualification. Keep final image receipts outside the image to
avoid a circular digest. Neither an image nor a synthetic fixture establishes installation,
enrollment, native capture or real grant custody.

Generated-workspace impact: none. Source, served distribution and inert image are separate
milestones; the first enabled runtime change remains the later scoped receiver admission.
Existing v2 receivers need separate stopped migration and backup/restore evidence. C3 remains
disabled. Main/work-main is not a destination or prerequisite.

### P2-C.3 source readiness

- [x] The inactive image-closure constructor and typed qualification-runner source are ready for
  protected integration. Candidate `ca86607ee55705ca9ded42e35803ac335fa0b5b3` binds the complete
  physical native closure to a separately supplied root-acquisition selection, preserves explicit
  production unavailability while that genuine selection is absent, and carries the selected trust
  input into the prepared context. The retained independent review is READY with SHA-256
  `302773c8bf5bee44a7d58c50e0860b99f13339d717bea3feea172378eb967964`.
- [x] The joined protected Host workflow passed for exact merged source
  `907bbd6abe6da735ddca409b6ec827fe4b2ab7b5`, tree
  `e5a8a7eea4a5206b42e98da66e0d97d6840ece5c`, in
  [run 37008208065](https://github.com/FS-GG/.github/actions/runs/37008208065).
  [PR #4105](https://github.com/FS-GG/.github/pull/4105) delivered the reviewed source and official
  NuGet lock correction. This closes P2-C.3 source; it does not install or activate a receiver.
- [ ] P2-C.4 production mechanism source: separate trusted-input CLI, bounded rootless process mechanism, modeled C4Ready and verified OCI evidence. Source qualification alone does not establish actual image builds.
- [ ] P2-C.4 genuine full Python/native/search/reader acquisition and independently selected input custody; the acquisition-required placeholder remains unavailable.
- [ ] P2-C.4 two-build OCI comparison, P2-C.5 actual served Manager-to-Host inactive qualification,
  and P2-C.6 protected readback remain open. Genuine native acquisition, image construction,
  installation, grants, capture, restart/recovery, enrollment, and activation also remain open.

### P2-C.4 production mechanism source closure

[PR #4118](https://github.com/FS-GG/.github/pull/4118) delivered protected
`238e4f3c4160f69f32b1f924ff639a4deda0cf3c`, tree
`9144aae81142d186c9f8c34fc80739c0193047e9`. The exact native
[Host package workflow](https://github.com/FS-GG/.github/actions/runs/37037046999)
and all required source checks passed. The production rootless process mechanism, OCI validation
and canonical C4Only mode have source qualification through sixteen scenarios/334 ordered
correspondence transitions, actual synthetic process controls and causal mutants.
The production capacity reserve, process retirement, fixed executable/argument and refusal
contracts remain enforced. The positive HOST fixture setup repair preserves the operation deadline.

This closes the mechanism **source** milestone only. C4Ready still reports qualification Unknown;
C5/C6 remain false. Genuine native input acquisition, complete runtime closure, two isolated
OCI builds and native container comparison/qualification remain required. No image, native run,
installation, collector grant, capture or activation is accepted by this source merge.

Receiver installation, real grants, private custody, capability/capture evidence, restart/export/backup,
and any activation remain later protected effects. C3 stays disabled until those authorities close.


### P2-C runtime declaration and acquisition frontier, 2026-10-03

The inactive production declaration now names the same canonical runtime inventory as the
[typed constructor](../../deployment/telemetry-collector/persistent/v3/image-closure/ImageClosure.fs):
`ead4ece42719198be9607d18415e428e3a6fcaadf50b88dc6e93894c47bec4c2`.
Independent readback hashed the retained actual 337-file runtime inventory to that digest.
Fresh authenticated downloads of manager artifact `11216418410` and Host release asset
`604796015` matched the selected artifact and archive digests. The manager artifact expires
`2026-12-31T08:19:03Z`. This corrects a stale declaration; `acquisition-required` and all native
inventory placeholders remain unchanged.

Complete native acquisition remains open. The bounded retained acquisition packet lacks reader
capability profile bytes, raw protocol/session-flag inputs, and full Python loader, import,
library and OS-data closure. Its selectable Python `_tkinter` extension has unresolved Tcl/Tk
libraries. Cached candidates and hash fields do not establish acquired input custody.

The existing Coordination Host `diagnose-fixed-native-capability` command can produce a new
genuine result from an independently selected, unexpired profile. Its closed profile pins the
Host process and native executable, bounds runtime and output, and selects a fresh disposable
workspace. It checks current-account authentication before bounded model discovery for the
frozen `gpt-5.6-sol` / `medium` selection; it permits no executor candidate. The profile is an
owner input, and the actual diagnostic emits the result. Missing retained bytes therefore remain
an acquisition frontier, not proof that a new diagnostic is impossible. An admitted account/profile
and actual producer execution are still required; neither source nor a synthetic fixture supplies
them.

Two independent cold OCI builds, complete runtime probes, and served Manager-to-Host inactive
qualification remain open. `run-c4` deliberately stops with qualification Unknown;
`run-inactive` already runs the actual selected Manager and published Host on both cold-reloaded
bundles through `InactiveQualification.run`. Its disposable synthetic fixture supplies no
native or installed authority. This declaration correction creates
no image, grant, installation, capture, enrollment or activation authority.

### P2-C empty native dependency admission repair, 2026-10-03

The immutable Python 3.14.0 image selected for acquisition contains genuine zero-byte regular
initializer files, including `compression/__init__.py`, `compression/_common/__init__.py`,
`email/mime/__init__.py`, `pydoc_data/__init__.py` and `urllib/__init__.py`. Rejecting every
zero-byte row prevents a complete native import census without changing or dropping those bytes.

The [image constructor](../../deployment/telemetry-collector/persistent/v3/image-closure/ImageClosure.fs)
accepts an empty native authority row only with exact empty SHA-256
`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` and mode `0444`.
Its existing regular-file, owner, path, link, physical-byte, digest and complete-census checks remain.
Host archives, Manager payloads, CLR runtime files and executable native rows remain positive.
The aggregate inventory remains finite, positive and bounded by 1 GiB and 8,192 entries.
The [preparation constructor](../../deployment/telemetry-collector/persistent/v3/Preparation.fs)
preserves the same exact empty dependency tuple in the verifier runtime manifest; runtime executable,
verifier module and manifest paths remain ineligible for empty admission, with the existing 512 MiB
and 4,096-entry bounds.

Focused consoles replay these actual immutable Python initializer bytes when
`PERSISTENT_V3_IMMUTABLE_PYTHON_ROOT` names the acquired standard library. They verify exact emitted
manifest bytes and refusal of wrong or malformed empty digests, negative or malformed sizes,
executable modes and empty required payloads. Existing preparation CLI custody checks and actual
Manager positive-fixture qualification still pass. These are source checks, not image qualification.

The [protected Manager baseline](https://github.com/FS-GG/FS.GG.Coordination/blob/2309964321a895100d12ededb1c9c00c8342b509/eng/telemetry-host-manager/Program.fs#L376)
rejected zero dependency bytes. That consumer source repair is now Closed through
[Coordination #927](https://github.com/FS-GG/FS.GG.Coordination/pull/927), merged at
`1e6afd8ef38a5fef01e2a2d3f2a2acf4ec1127f7` from exact source
`5f1af3f3263e307975d8227333be4e9e39b16e2f`. Its two-file change updates the Manager
validator and V3 test harness. Empty dependency rows require the exact empty SHA-256 and
mode `0444`; every row binds a regular, single-link, no-follow descriptor to its declared extent,
EOF, stable metadata and final filename. Missing or empty required runtime, module and manifest
payloads still refuse; the 4,096-entry and 512 MiB limits remain unchanged.

All 28 extracted-production-source controls passed, including the baseline causal rejection and
admission of fresh readonly copies of the five retained initializer files. Their acquired source
copies remain unchanged at mode `0600`; this source test does not qualify installed custody.
The actual compiled/native [optimistic qualification](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/37138060201)
and [ordinary source checks](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/37138060190)
passed. The [Host required-payload reader](https://github.com/FS-GG/FS.GG.Coordination/blob/2309964321a895100d12ededb1c9c00c8342b509/src/FS.GG.Coordination.Orchestration.Host/LearningInstalledReadinessSource.fs#L249)
remained unchanged: its callers read required payloads, never Python dependency rows, so its
positive-size check is not this frontier.

Distribution of the repaired Manager, complete OS/Tcl/Tk and native Python closure, independently
selected reader capability/profile and image inputs, two cold OCI builds, C4/C5 runtime readiness,
and actual served Manager-to-Host qualification remain Pending/Unknown. The inactive
`acquisition-required` selection and all installation, capture and activation boundaries remain.
Main has no action; publication, installed adoption and live receiver evidence remain separate.

The exact-byte inactive CLI guard also binds the canonical declaration after the prior runtime-pin
correction: SHA-256 `63a6bd47f0e5def4d0690df6ed83c479629ed2461ab26b9fc6017415d52da7b8`.
An actual CLI control requires unavailable status for those exact bytes and refusal after appending
one whitespace byte. The deliberate input-revalidation mutant remains unchanged and must still
fail its safety property; that expected failure is distinct from later placeholder checks.


### Host 0.4.0 source and publication closure, 2026-10-04

[PR #4207](https://github.com/FS-GG/.github/pull/4207) merged as
`09f06f5cc5278cbfbf7c02b0a7a9e72104aa4e3f`, tree
`264b1f717fa3786f928cf5a3ad59ea8dcebac8a7`, after exact-source checks passed.
Host 0.4.0 publishes the read-only `read-inactive-preparation` command. Root accepted
first-attempt [candidate 37219926637](https://github.com/FS-GG/.github/actions/runs/37219926637),
artifact `11309323841`, candidate archive SHA-256
`e23611083c392cbe4a5332bba6a1b8628ea513884088b8b40ba4fc1c3ada2afd`,
then the [publish=false preflight](https://github.com/FS-GG/.github/actions/runs/37220253239)
and first-success [publisher 37220419362](https://github.com/FS-GG/.github/actions/runs/37220419362).

Release [`telemetry-host/v0.4.0`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.4.0),
ID `403127331`, serves the original package and manifest byte-identically. GitHub archive
SHA-256 is `150f3e697e088a10170db4f2404fd4e73be856ccefc8d776c53076f55adce45a`;
signed nuget.org archive SHA-256 is
`293e2ccdbc7a4aed2c56d72cf82a7f806d7671b549439463cd93164dad574f77`.
Both normalize to producer payload
`d7e747cebd6f425d775177bc844d0b3d187a911ae4032e269b008c875a872240`.
Raw release manifest and publication-journal asset SHA-256 values are respectively
`144e5aa3d382b8bfc6ddcac76ed583c23ebe8fbb6744f844a94fa5347819d3f0` and
`c2409342cd3448325fb1dc3dff53b6c2e3783c7634b75101834049b86aa11731`.
Root accepted all 17 protected journal generations at
`203ba60f7df146743d4a7373a52f55fc6c22858e`, with eight verified effects.

This closes Host successor publication. The image constructor's Host 0.4.0 pin rebinding
has passed the focused local source qualification described below; protected source
integration and native checks remain separate. Served repaired Manager distribution,
genuine native/runtime/profile acquisition,
two cold OCI builds, C5 retirement and actual served Manager-to-Host inactive qualification
remain open. Installation, grants, capture, restart/recovery, usage completeness and
activation are unchanged; no receiver acceptance follows from publication.


### Host 0.4.0 image pin source qualification, 2026-10-04

Source candidate `90b0fbd0c9b020b0a74cf940be88820adbb31559` binds the accepted
Host 0.4.0 source, publisher/release IDs, both archive hashes, normalized payload,
raw manifest and publication-journal asset, and exact 6,134,924-byte GitHub package.
The production declaration remains `acquisition-required`; its actual byte digest
`dd7b2ec5b91b56f085c9492577b188b115ae4b99dafb29905b2e4c3b57317165`
is bound by the CLI. Historical Host 0.3.0 C5 refusal remains unchanged.

Root accepted a bounded local source qualification: three locked restore/build/console
steps returned natural exit 0 in 10.488 seconds, every owned process settled and final
pins stayed unchanged. The actual staged CLI preserved canonical-placeholder
unavailability and changed-byte refusal. Twelve focused Host release cases include
current production identity reaching missing-trusted-native unavailability and eight
historical-pin refusals. Existing path-helper controls retain 29 genuine names and
unsafe-path/extent checks; closed inactive-result controls also passed.
The named focused mode leaves the full constructor/OCI/runner console as the default
for native CI; no full OCI or installed-receiver acceptance is claimed.

Local qualification does not replace protected source integration or exact-head native
checks. Repaired
served Manager distribution, owner-selected native/profile/Python/Tcl/Tk/search/runtime
closure, two cold builds/reloads and C5 on each reload remain pending. Grants, capture,
restart/recovery, usage completeness and activation remain open.

### Repaired served Manager acquisition, 2026-10-04

Root accepted Manager bundle run `37224476786`, first attempt, artifact `11311024262`,
from protected Coordination source `5468ce210ef4886806d70c4e333496a358efbdf1`
and tree `a299eeea6aecc73d6a820ddf38701419c7ae22e3`. Served artifact SHA-256
`00965f7bb4de96bfdfff4ff44a8f04c82173abf819591e1f4012e8c3233151b9`
contains the exact manifest and 19 payload files with accepted sizes, hashes and modes.
Hosted focused tests and served verification passed. This closes repaired Manager
distribution; native and installed receiver qualification remain pending.

The next receiver source candidate binds this Manager tuple alongside published Host
0.4.0. Its declaration remains `acquisition-required`, with reader profile and native
inventory unset. Eight historical Manager-field refusals supplement the existing
Host release cases. Root accepted the exact source candidate
`9c22a393a6d8669bdd52df20ef0fd6f994dd2490`: three restore/build/focused console
steps returned natural exit 0, all owned groups settled, and all 20 focused release-pin
cases passed. Local qualification does not replace protected integration or exact-head
checks; full OCI and installed receiver acceptance remain false.

A retained served diagnostic-capable Coordination executable can be reused with a
fresh owner-selected profile, after byte and provenance checks. Its Oct 3 profile
and capability evidence are expired. Retained Python, ASP.NET, signed Tcl/Tk, native
schema and flags are proposed acquisition evidence; trusted inventory/aliases and
a fresh actual reader result remain required. No two cold OCI builds/reloads, C5,
installed receiver, capture, recovery, usage or activation acceptance follows.


### P2-C rootless command custody source window, 2026-10-04

The accepted narrow custody amendment preserves the existing cold OCI comparison and C5
sequence. Source now requires a root-admitted inherited command channel for `run-c4` and
`run-inactive`; command identity comes from the operation lease, and retirement is observed
by the caller through retained pidfds and exclusive recursive cgroup handles. The original
absolute deadline and custody identity join the selected input/evidence identity. Command
streams and retained command/output evidence share one operation output allowance.

Source preparation does not establish compiled or native custody. Affected compilation and
console/correspondence checks use the existing safe source supervisor; controlled native
custody fixtures require a separate admission in a qualified context. Current local UID/GID
maps lack the required singleton non-host-root UID0 mapping, and the cgroup mount is read-only. Delegation, workload control denial, foreign
insertion exclusion, mapped-user runtime visibility, namespace authority and conservative
32-thread/process capacity remain actual feasibility gates. No fallback or raised cap is
admitted. The former 18:50 image-closure executable cannot be reused for the changed backend.

Fresh native diagnostic evidence and a successor acquisition admission are required before
actual two-image/C5 qualification; the accepted 19:20 diagnostic is historical after its
19:24:09 UTC expiry. Installed receiver adoption, grants, capture, restart/recovery, usage
completeness and activation remain pending. Source controls do not close those gates.


Root accepted the amended source compilation at `bd06cce1d50d42934377dfd9c3676b8a331a5c1e`.
Private operation `learn-rootless-compile-root-operation-20261004-2106` completed restore,
build and the actual default source console with three natural exit-zero steps in 16.815s;
original/source pins settled and the final CLR census contained only the two reviewed
infrastructure generations. The source console retained the unavailable placeholder,
release-pin refusals, synthetic OCI/runner checks and five pure custody binding controls.
Its in-process command fixture does not establish actual descendant custody.

The source landing joins protected `2ab0c0ff9f37bdec9a11ba3604ebdc230711934f`; all 24
compilation leaves remain byte-equal to the accepted candidate. This byte join preserves
compilation provenance and does not call the joined revision newly compiled. Protected
integration and exact-head checks retain their normal authority. The initial overload-failed
operation and its consumed admission remain retained as failed evidence.

Before native use, the fixed inherited-namespace C helper must join an independently pinned
compiler, headers/linker and freshly compiled body. A bounded helper compilation
can resolve that source dependency independently; it cannot supply cgroup delegation,
required UID/GID mappings, private namespace/setns authority, actual mapped-user/guest runtime
visibility, foreign-insertion exclusion, workload control denial or retirement qualification.
Current native custody and all actual cold-image, installed receiver and activation gates
remain open. Source delivery follows the Board publication fence.


### P2-C custody acknowledgment source preparation, 2026-10-11

The current source review found that action-specific acknowledgment checks ran after
transport validation returned. A malformed input binding, retention charge, start lease
or process observation could therefore refuse one action while leaving its channel reusable.
Observation also lacked enforcement of the selected command's output ceiling, and cached
`Wait` could return a retired result after another action poisoned the shared channel.

The prepared correction validates every acknowledgment payload inside the existing
poison-on-failure boundary. Process observations require a closed field set, exact lease,
boolean retirement, integer exit and non-null output. The combined UTF-8 bytes of stdout
and stderr must fit the selected one-command ceiling, including unretired observations.
Retirement is recorded only after the whole acknowledgment passes; poisoned channels
cannot return cached successful waits or successful cancellation no-ops. Existing cancellation, original deadline, output
charging, command selection and disposal retain their conservative custody behavior.
The caller still owns the shared operation allowance and actual descendant retirement.

The existing image-closure source console contains focused controls for the exact combined
ceiling, multibyte output, null or malformed fields, foreign leases, duplicate/extra fields
and unretired observations. Actual inherited AF_UNIX channel fixtures cover malformed
acknowledgments across all five action paths, subsequent-operation refusal, and cached
wait/cancel refusal after another action poisons a retired lease. The peer stays open to
distinguish poison rejection from transport failure; bounded test-owned readers are joined.
`PERSISTENT_V3_CUSTODY_CONTROLS_ONLY=1` selects parser and local-socket controls before
image fixtures or child execution. Source compilation and these controls
remain unrun at preparation; protected delivery and runtime qualification remain separate.

The refreshed inventory has not located the owning inherited-namespace C helper or a
compiler in the named system tool directories. The source-selected `/usr/sbin/podman`
executable is absent; the observed `setsid` bytes match their selected source pin.
The separate Coordination bootstrap source
is retained as an unjoined candidate. Current UID/GID maps have a single 65,536-entry range;
they do not meet the required singleton UID/GID-0 mapping. The cgroup mount is read-only
and delegation/foreign-insertion exclusion remain unqualified. Bounded search does not
prove global source absence. No compiler, native command, container, provider, installation
or activation was invoked. Complete acquired inputs, two cold images, C5 and live custody
remain open; consumed or unknown historical operations retain their original reservations.
### LEARN-01.4 fresh maintained internal custody core source, 2026-10-11

The accepted source window prepares `learn-custody-core-fresh-v1` independently
of missing historical helper/private-volume/SSH evidence. The internal C core
has no production entry point or effects. It decodes the existing closed F# wire
grammar, joins immutable identity/admission/input/ordinal/deadline values, charges
cumulative combined output and retained evidence against a supplied aggregate
allowance, and poisons reuse/cached retirement after failure. Acknowledgments and
retired state require explicit adapter results; syntax and synthetic results do
not authenticate command authority or descendant custody.

One focused C test translation unit prepares meaningful semantic controls against
the actual component. Compilation, execution and genuine C/F# interoperability
remain unrun. Qualification can select the existing hosted source fixture and its
pinned runner toolchain, or separately authenticate/acquire/close the proposed
local compiler. Neither route is admitted by source preparation; Podman acquisition
is independent of core compilation. Existing source supervision, exact source/tool
pins, immutable resource/deadline/output bounds and complete cleanup remain required.

The prepared existing Host workflow now binds its early C toolchain census to
the root-selected GitHub-hosted Ubuntu 24.04 image `20261004.327.1` and official
source `e3fe113a581eb9a44ca43f479b69f9c93f36df34`. It produces the existing
fixture's required compiler pins before dependent qualification; fresh native
image metadata, compilation, C controls and F# interop acceptance remain pending.
This source preparation does not admit hosted execution or native helper effects.

The next production dependency is the owner-selected root operation lease and
selected-command binding, immutable shared allowance, retained namespace/cgroup/
pidfd capabilities and authenticated retirement result interface. Historical body
and compilation provenance remain unknown. Host feasibility, cold builds/reloads,
C5, publication/installed adoption, capture/recovery/usage and activation stay open;
LEARN is not complete.

### Fixed production custody adapter source successor

PR4431's accepted core/source bridge is protected at `db9b91a576039af36044dfc45fcc25ccefbe587b`; its 518 C controls and actual C/F# bridge establish source fixtures, not native process custody. The accepted production-adapter window adds a callable C boundary, frozen exact command slots and retained namespace/cgroup/pidfd controls, continuous shared output charging, joined descendant retirement, and a fixed test-only native controller using the existing F# channel. Source compilation/regressions and native qualification are pending; no current run or acceptance is claimed.

The existing hosted route prepares a default-off protected-main native opt-in with separate original root admission/profile and actual delegated capability requirements. The bounded native fixture may report BLOCKED on provider limitations; it cannot substitute simulated retirement, elevate an ordinary PR, provision privileges or install a helper. Its setup/CLR/controller overhead belongs inside the admitted 32-task/512MiB/one-CPU parent boundary and original 120-second deadline. Actual cleanup, source qualification and protected source delivery will be recorded separately after their genuine operations.

Production root-caller integration, an exact dynamic command producer, actual Podman/runtime/image closure, installed receiver/grants/capture/recovery/usage/activation and historical private-host provenance remain open. Whole LEARN remains incomplete; missing historical artifacts are not reconstructed by this maintained source successor.


### Fixed native component diagnosis, 2026-10-12

[PR #4436](https://github.com/FS-GG/.github/pull/4436) delivered the joined
controller source with four C builds, 105 owner controls, 103 adapter controls,
518 core controls and the C/F# ABI, wire and model checks passing. The admitted
[native run 38204892063](https://github.com/FS-GG/.github/actions/runs/38204892063)
used the unchanged four source files at protected `e95ea340`. Its actual hard
inner enrollment joined, then namespace/map/mount setup blocked before workload
birth. All eight cases were not run and capture count was zero. The controller
returned 43 with cleanup UNKNOWN; the later outer channel-lost symptom does not
identify the first failing setup syscall. Provider job failure and completion do
not prove native empty-cgroup cleanup or whole-wrapper retirement. Retained
remote identities remain reserved and this consumed invocation is never replayed.

The diagnostic source successor records the actual failing setup or cleanup
stage, distinguishes a captured syscall errno from an unknown clock/indirect
errno, and preserves each process's first cause through later cleanup findings.
Injected source controls cover failure ordering, original-cause retention and
bounded formatting. This adds observability to the existing fixed test
controller; resource limits, original deadlines, setup predicates and custody
acceptance remain intact. Ordinary source qualification does not establish a new
native result. A subsequent fixture requires fresh root admission, and production
caller/adoption and whole-wrapper retirement remain open.


### Preserve native read-only mount restrictions, 2026-10-12

[PR #4440](https://github.com/FS-GG/.github/pull/4440) delivered diagnostics after
114 adapter controls, 105 owner controls and the full hosted source package passed.
The independently admitted [native run 38209809730](https://github.com/FS-GG/.github/actions/runs/38209809730)
joined its hard inner boundary, then the keeper recorded `setup/readonly-remount`
with actual errno 1 (`EPERM`) at `/usr/sbin/podman`. The prepared handshake then
failed indirectly. No workload was born; eight cases remained not run, capture
was zero, native exit was 43 and cleanup remained UNKNOWN. Both failed native
invocations and their distinct remote resources remain retained.

The old bind remount supplied a replacement flag set containing only read-only.
Dropping locked inherited restrictions is a hypothesis: this failed run did not
retain its flag mask or establish the kernel's exact rejection branch. The
[Linux mount attribute contract](https://kernel.googlesource.com/pub/scm/docs/man-pages/man-pages/+/99891e9f3c184347ce1616361e8b32fb04cffd1f/man/man2/mount_setattr.2)
permits an additive read-only request with no cleared attributes. The fixed
keeper now uses that request on each new bind, with no recursive change,
propagation or ownership change, privilege fallback or remount fallback.
Bounded flag readbacks require read-only and preservation of every prior flag;
injected source controls reject lost restrictions. The production population
reader also had a separate one-byte offset error: it rejected genuine
`populated 0` and `populated 1` kernel records. Its shared parser now requires one
complete Boolean population line, refuses duplicates and truncated records, and
preserves the output value on refusal. Direct production-parser controls cover
these cases. Existing descriptor/capability validation, original clocks and
quotas remain unchanged. Source qualification cannot establish native success or cleanup;
a new fixture still requires separate root admission.


### Held-process census retirement source repair, 2026-10-12

The separately admitted run `38213947377` observed all three additive read-only
binds preserving their prior restrictions, then two cases and their two drains.
It stopped at a retained-process observation with `ENOENT` for `/proc/12046`.
The exact caller stack was not retained. Native exit, captured-byte total,
remaining six cases, final controller drain and self-restoration remain UNKNOWN;
this partial evidence does not establish eight-case success. Its distinct runner
and known remote names remain held, alongside the two earlier failed native runs.

The source repair permits census-only retirement of an already-held exact birth
when the original retained pidfd has an accepted terminal poll event and a fresh
independent read from the exact known cgroup excludes that PID. The event does
not establish exit status or cause of termination. PID reuse, unknown first
birth, live or invalid pidfd events, missing or denied descriptors, malformed
process data and unavailable membership remain fatal. JOIN, CASE and sender
admission retain their strict live-process gates. Actual owner wait and cleanup
still require their separate witnesses. Bounded primitive, phase, role and birth
diagnostics preserve the original exception and errno. Recovery requires an
explicit process-directory or held process-stat disappearance primitive; an
ENOENT from pidfd, cgroup, control or close observations cannot be reconciled
as process retirement.

After the original operation DRAIN has been validated and its acknowledgement
sent, disappearance of that exact operation name during topology observation
permits one reread. The retained parent and home identities must still match,
and the fresh parent census must contain only home. An already-observed foreign
directory cannot be discarded by the reread. Pre-DRAIN disappearance,
foreign children, permission failures and failures reading an opened cgroup
remain fatal. Name absence is never group-retirement evidence. The receipt
records actual aggregate-drain observation separately from the contractual
self-restoration description. Original deadlines, task/memory/CPU limits and
capability ownership remain unchanged. The existing owner source-control block
passes 186 injected checks; these mocked inputs establish source behavior only.
Protected hosted qualification and any new native admission remain separate.


### Assembled native loader input-bound repair, 2026-10-12

Run `38222624305` refused the maintained owner input before executing the owner
module, creating the native evidence directory or starting the native TEST
controller. The loader still read at most 37,929 bytes while its selected guard
required 48,966 bytes. This missed dependent bound deterministically truncated
the selected input. All eight native cases remained not run. The original was
consumed and will not be replayed; its runner and retained source fixture remain
uncertain, alongside the three earlier native UNKNOWN holds. Provider terminal
status and its orphan-process cleanup do not establish native group retirement.

The exact declared owner size now drives both its read limit and length check.
The read allows one extra byte to reject oversized inputs, and an independent
64 KiB ceiling rejects invalid declarations before opening the file. The
dormant privileged-observation manifest also binds the current owner size and
digest. This updates input correspondence without enabling that route or
changing its authority. Existing clocks, quotas, custody gates and cleanup
remain unchanged.

The existing owner-control block now passes 213 checks. Its additional controls
execute the actual assembled native loader prefix and dormant preflight input
selection against the selected source files, stopping before module execution,
sealing, output creation or privileged work. They reject stale declarations,
invalid ceilings, short or oversized input, digest drift, missing files and
permission failures. The workflow selection tests corrupt each of its three
inputs independently. These are nonprivileged source controls; exact hosted
qualification, protected source correspondence and another root native admission
remain separate.


### Native case and caller failure diagnostics, 2026-10-12

Run `38230597573` passed the normal and descendant client cases, then the denial
client failed the combined zero-exit and retirement assertion. The controller
observed three case drains, 43 captured bytes and actual self-restoration, but
aggregate drain was not observed and cleanup remained UNKNOWN. The retained
controller line did not expose the denial workload's exit code or signal, so
its exact failed primitive remains unknown. The remaining five cases did not
run. This original and its distinct runner, cgroup, storage, seal and source
fixture remain held alongside the four earlier remote uncertainties. No
provider terminal status releases those resources or admits a replay.

Diagnostics now identify failed workload UID/GID, control-open, inherited-FD
and write predicates without changing their return codes or order. Failed
syscalls retain their actual errno; unexpected successful opens or live FDs
are predicate failures with errno explicitly unknown. These failure-only
messages enter the existing bounded stderr capture. Controller evidence also
prints its already-measured leader exit code, signal and original client wait
code/status. An exit code is interpreted only with its accompanying measured
leader-exit flag; an observer terminal poll alone remains insufficient.

The F# fixture preserves its ExitCode-first short circuit, evaluates HasExited
at most once, and reports either that actual result or NOT_EVALUATED. On refusal,
it retains up to 512 characters of the actually captured stderr as escaped JSON,
with an explicit truncation flag, so workload-stage diagnostics survive the
client assertion. Its
nonprivileged controls reject live custody, retain the original read exception,
and refuse to evaluate retirement after a nonzero exit. Direct C source
controls exercise the same descriptor primitive with a real retained pipe FD
and a missing FD. All eight case selections, their original budgets and
allowances, capability ownership, client wait and cleanup predicates remain
unchanged. These diagnostics do not establish the cause of the held failure.
Hosted compiler/full source qualification remains required. Because the caller
changed, the next exact protected-source correspondence must include
`OciTests.fs` as a sixth leaf alongside the previous five leaves; any native
successor still requires its own root review and effect admission.

### Descriptor metadata source qualification, 2026-10-14

Acquisition, revalidation and rootless context/evidence ownership checks previously
started `/usr/bin/stat` outside the inherited command-custody channel. The maintained
image-closure implementation now reads owner/type metadata through one retained
`O_PATH|O_NOFOLLOW|O_CLOEXEC` descriptor and Linux `statx`. It requires returned
UID/type/mode fields and the declared Linux x64 process profile, refuses final
symlinks and special files, and closes the descriptor on success and refusal.
Parent-directory custody checks remain applicable; this change does not turn a
metadata observation into custody of a later path-based read.

Ordinary SDK 10.0.401 source qualification at
`276d13694bafe68692099064b6203466b91e461b` passed locked restore and compilation with
zero warnings/errors. Actual descriptor controls passed for owner UID, regular and
empty files, directories, UTF-8 paths, final file/directory/dangling links, a FIFO,
missing and NUL-containing paths, replacement refusal and repeated descriptor
cleanup. The existing pure path/inactive-result controls and actual CLI placeholder
and sixteen historical Host/Manager release-pin refusals also passed. The ordinary
container exited zero and its exact removal and empty selected store were observed.
The required hosted source checks passed and PR #4499 delivered this source slice.
This acceptance is limited to descriptor metadata and its source controls.

The production command producer/root caller, authentic provider profile, native
custody, two cold-image builds/reloads, installed receiver, grants/capture/recovery,
usage completeness and activation remain open. Existing unknown historical effects
retain their original holds; no native replay or activation occurred.


### Production caller command recipe: source window 1, 2026-10-14

The fixed native fixture cannot bind the real production command stream at open:
production previously selected bundle nonces after `BindInput`, first created private
bundle directories after that binding, and selected cleanup removal operands from
later captured census results. A production caller therefore needs a finite trusted
command program, delayed directory capabilities and captured-result cleanup binding.
The accepted implementation sequence separates the shared source recipe from native
capability and mapped-engine execution changes.

The first source window supplies `CommandRecipe`, a pure producer shared with the
actual rootless mechanism and inactive qualification. Its independently selected
recipe binds the target, custody identity, selection/trusted/prepared digests, parent
archive, work/evidence roots, two distinct bundle nonces and bounded cleanup allowance.
The production CLI requires a root-owned private `--command-recipe` file before
`BindInput`; it does not select that file's nonces itself. A closed, canonical parser
rejects duplicate, missing, extra, null and noncanonical input fields. Exact command
recipes include argv, environment, working directory and output bounds. Revalidation
compares the selected recipe with the actual runtime inputs and includes its digest
in the existing input identity and evidence.

The same command producer now supplies actual build, reload, probe, inactive-manager,
inactive-host and fixed cleanup commands. C4 selects 20 normal and 12 fixed cleanup
commands; the full inactive target selects 26 normal and 12 fixed cleanup commands.
Up to 32 removals are separately bounded across four owned stores. Each removal has
an exact storage/environment prefix and a 64-character lowercase hexadecimal ID
selected from the adapter's captured census result. The source parser and removal
shape do not authenticate a native operation or establish result custody.

Integrated source qualification at `066f34ca4231dd2069c2ea40452a2c0640195de6`
passed locked restore and compilation with zero warnings/errors. Actual C4 and full
inactive Runner/RootlessMechanism/InactiveQualification control flow matched all
selected commands. Tests exercised closed-parser refusals, target/input/nonce and
command drift, package/receipt checks, the genuine pinned verifier bytes and four
captured-result removal IDs. External Podman effects and Manager/Host responses were
inert substitutions; the ordinary SDK container's exact removal and empty store were
observed. Descriptor and existing CLI/release-pin controls also passed. These source
results establish neither authentic provider isolation nor native acceptance.

The fixed native API retains its host-UID0 directory check and UID32768 child.
The second source window prepares a separate production entry for the trusted native
selector, delayed selected-slot capabilities and mapped engine profile, described
below. The authentic provisioner remains required, with supplying owner unresolved.
No phase-2/phase-3 effects, native replay, cold-image build, receiver installation or
activation are accepted by either source window.


The first hosted source run compiled successfully but refused the full inactive
fixture at `inactive-fixture-owner`: the ordinary runner could not create the real
UID-32768 fixture required by the maintained implementation. Preserve that failure;
changing the owner predicate would invalidate this test. The maintained source
launcher now executes its focused recipe controls in a disposable SDK 10.0.401
container selected by immutable manifest and config digest. The checkout is read-only,
network access is disabled during the fixture, and root UID has only CHOWN,
DAC_OVERRIDE and FOWNER within the container. The actual ownership, package, receipt
and full inactive checks execute there; the ordinary nonroot source suite retains
its remaining coverage. This test namespace supplies no production provider profile.

The additional acquisition, fixture and independently authenticated exact-container
cleanup are bounded within the unchanged 20-minute hosted job. Acquisition and
fixture outputs cap at 16 MiB, inspection output at 64 KiB; cleanup refuses an
unreadable or mismatched object instead of removing an unproven CID. Measured prior
source-job duration was 607 seconds. The tightened new maximum is 385 seconds,
leaving 208 seconds to the original job deadline; cold acquisition and stage
variation remain unmeasured. Missing Docker refuses before expensive source work.
Static adversarial cleanup controls refuse identity, mount, namespace, capability,
resource and command drift before removal. Required hosted qualification of this
fixture repair subsequently passed the required hosted source qualification.


The reviewed hosted variant passed the full source job in 590 seconds, including
actual full inactive controls and authenticated exact-container removal. A connected
cleanup review additionally requires nonprivileged execution, no host devices,
private mount propagation and recursively read-only source, fixed namespaces/runtime,
no init/health/restart worker, and no extra mounts or network publication. Fifty-two
actual helper adversarial controls reject scope/identity/inspection drift before
removal. The intentional cap-plus-one pipeline is annotated because truncation
refuses overflow and `pipefail` retains writer failure; its repository fixture passes
all 25 controls. Final exact-source hosted qualification retains the original failure
and these stronger safeguards.


The final first-window source candidate passed required hosted qualification at
`071e00e121376bc925abfe332d55edc3a539b2ea` and landed as
`0ba575b150549bd2390d886e44d744e3dabdf8b3`. The original ownership failure and
intermediate successful fixture evidence remain retained. Source completion still
supplies no production controller or native acceptance.

### Prepared second source window: finite native policy and deferred capabilities

`native/custody_root` now supplies a bounded copied recipe and finite normal/abort
cleanup policy. The private production `lc_open_root` composes it with the existing
adapter validation, original peer/pidfd and namespace/cgroup joins, capture, birth
and retirement. Root selects a finite pool of unique immutable command leases and
empty command cgroups. A request cannot register a command or capability. START
consumes its pool slot before matching or attempted birth, then freezes one exact
selected command. Only the adapter's own successful, fully captured retired census
can supply removal IDs; each retains its original operation/store/lease origin and
is consumed once in observed order. The root controller must independently derive
and validate the typed recipe from the shared producer. Digest shape alone supplies
no authority, and an authentic production controller does not yet call this API.

Already-existing work/evidence parent anchors are retained before BindInput; no
bundle precreation changes Runner ownership. Selected roots must initially be absent.
The production path derives exact parent and private-directory host UID/GID from the
retained keeper's verified namespace-UID0 mappings. It authenticates mapped parent
metadata, selected namespace paths and mount identities, then resolves the selected
bundle through retained parents without symlinks, mount crossing or path escape.
Mode0700, original inode/device/mount and the exact regular owner nonce remain
required. Namespace visibility checks the next selected and live slots, so legitimately
deleted historical bundle paths need not remain visible. Control cgroups, executables
and supervisor retain host-root ownership checks. The fixed fixture retains its
existing identity; a supplying production profile must independently prove mapped
engine isolation from control paths and inherited descriptors.

Production output buffers allocate only for selected slots and remain retained.
Before opening, bounded arithmetic accounts for worst-case output for every possible
slot, copied recipe, vectors, argv/environment, workspace and lease tables, plus a
nonzero independently admitted requester/controller/runtime overhead reservation.
The sum must fit the existing operation memory ceiling. Original captured-output
charging, task/CPU limits and deadline remain authoritative; this adds no capacity.

Production EOF requires a finite cleaned state, genuine retirement of every consumed
slot, complete framing/acknowledgements and actual selected bundle absence under
original directory identities. An unused operation can settle only an abort custody
state after actual absence. The observational outcome explicitly distinguishes abort
custody completion; only actual Runner terminal evidence establishes C4Ready or
InactiveQualified. Unknown program or directory retirement holds the original handles.

Strict C11 executable and shared-library compilation passed, with 378 ordinary source
controls and zero failures. Controls use the actual parser and directory helpers for
nonce, owner, mount, symlink, escape, replacement and terminal-absence refusals; pure
policy fixtures exercise both targets and bounded cleanup without claiming kernel
custody. The source launcher links the new module into its maintained controls.
No privileged native fixture, namespace/cgroup operation, Podman, CLR invocation or
production effects ran for this window. Source safeguard review accepted the finite policy, exact cwd/copy bounds and
unchanged caller cleanup ordering. Required hosted source qualification remains pending.

The next source window joins the actual production caller, finite selector and real
wire/core through an explicitly inert test transport boundary. The authenticated
production controller/profile, native8, cold image closure, native C5 and receiver
acceptance remain separate unresolved outcomes. Source success cannot settle a
historical unknown operation or establish publication, installation or activation.

### Joined production caller source qualification, 2026-10-14

`ProductionCaller.run` now owns the actual root caller orchestration used by the
CLI. `RootRecipeBinding` validates and copies the shared command program into the
bounded native recipe ABI. A distinct linked-source test assembly exercises that
caller through `CustodyChannel`, the actual C wire/core, finite selector, capture,
retirement and cleanup. Its separate C factory substitutes namespace, cgroup,
command birth, wait and signal facts using only local socketpair, eventfd, memfd
and private directory fixtures. It supplies no production controller authority.

Ordinary SDK 10.0.401 qualification of the source at `9f874da2` passed locked
restore and compilation with zero warnings/errors. The joined C4 and full inactive
flows completed 36 and 42 selected slots; failed first build completed abort custody
through 17 slots without accepting qualification. Input drift refused with zero
consumed slots and births; command drift consumed its first slot and refused before
birth. Neither refusal manufactured cleanup or qualification. The callback remains
rooted until peer retirement is observed; the managed GC control verifies retention
through an unknown peer and release after retirement. Production PE metadata and an
ordinary production ELF contain no test boundary or source hook.

The maintained source launcher builds and executes the joined suite inside its
existing SDK fixture, binding the two exact assemblies and library directory in
its authenticated command. Shell/Python preparation and 52 existing adversarial
cleanup controls passed. The original compiler syntax and linked-source name
collision failures remain retained. The final source qualification still requires
the exact hosted check. Its original 20-minute job, image acquisition, namespace,
resource and cleanup limits remain applicable; no new image acquisition was added.

These results establish inert caller/native-selector composition. An authentic
supplying controller and provider profile, mapped engine isolation, native8,
cold image closure, native C5, receiver acceptance and activation remain open.
Historical unknown operations retain their original custody holds.

### Hosted image candidate rebinding, 2026-10-15

The original [PR #4560 source check, run `38786251522/a1`](https://github.com/FS-GG/.github/actions/runs/38786251522)
refused before the toolchain census and source qualification: the hosted provider
reported Ubuntu 24.04 image `20261012.334.1`, while the guard still selected
`20261004.327.1` / `e3fe113a581eb9a44ca43f479b69f9c93f36df34`.
The failed attempt retained no toolchain artifact. Its image drift is a failed
prerequisite and supplies no replacement custody or source acceptance.

Root selected `20261012.334.1` and official runner-images tag commit
[`7e7dc1bcc0ef464e5e315a2b4c33609590c6cffe`](https://github.com/actions/runner-images/tree/7e7dc1bcc0ef464e5e315a2b4c33609590c6cffe)
as a candidate for bounded requalification. The direct immutable image manifests
retain GCC 13.3.0, system Python 3.12.3, binutils 2.42-4ubuntu2.10,
coreutils 9.4-3ubuntu6.3 and the selected .NET SDK 10.0.401. Listed sudo changes
from `1.9.15p5-3ubuntu5.24.04.3` to `1.9.15p5-3ubuntu5.24.04.4`;
the candidate release is marked prerelease. These declared versions do not
establish actual transitive package/file custody, Python/PAM/ELF search closure
or post-setup SDK placement.

The source candidate changes only the selected image/source tuple and its
independent source equality. It retains the actual provider/OS/architecture/image
checks, pinned census, compiler and complete toolchain binding, SDK custody
normalization, source tests and separate default-off protected native admission.
Preflight uses static input consistency and inert wrong-tuple refusal against
the actual workflow guard; this literal binding change needs no new model.
The existing 20-minute hosted job budget and all stage, output and cleanup bounds
remain authoritative. Full required hosted source qualification and fresh
toolchain evidence on the candidate image remain pending. No native fixture,
installed custody, publication, receiver acceptance, activation or historical
retirement is established by this candidate. The first failure and original
unknown operations retain their identities and holds.

### Hosted source image successor candidate, 2026-10-20

[Run39383364443/a1](https://github.com/FS-GG/.github/actions/runs/39383364443)
passed the passive Host HTTP stage, then refused before the LEARN census and
dependent source compilation: actual image20261018.339.1 differed from selected
20261012.334.1. Its failed prerequisite and prior unknown operations stay retained.

Root selects image20261018.339.1 and official runner-images source
[`b4067e48bd9bd803e958bfe1503ea0a06a3a127f`](https://github.com/actions/runner-images/tree/b4067e48bd9bd803e958bfe1503ea0a06a3a127f)
for ordinary bounded source requalification. The official release remains marked
prerelease; its manifest lists SDK10.0.402 while setup-dotnet still selects10.0.401.
Declared versions supply no actual post-setup custody or framework acceptance.

Only the three selected tuple scalars change. Provider/platform/image predicates,
pinned census, SDK normalization, complete compiler/toolchain binding, all source
gates and default-off protected native inputs remain required. Static preflight
uses the extracted actual guard with a successful inert census sentinel and
wrong-tuple refusal. The unchanged execution graph needs no custom model; the
preflight effort cap is ten minutes against the existing twenty-minute hosted job.
Fresh successful census and full required source CI on the exact candidate remain
pending. No native fixture, whole-wrapper retirement, C5, installed receiver,
publication, activation or old-operation replay is established by this selection.


### Closed source candidates during the hosted rollout, 2026-10-20

[Run39384280477/a1](https://github.com/FS-GG/.github/actions/runs/39384280477)
received the older20261012.334.1 image after the preceding attempt received
20261018.339.1. The single-newer-image guard correctly refused before the census.
Neither failed prerequisite is retried or qualified by changing a literal again.

Root admits exactly two ordinary-source candidate tuples:20261012.334.1 with
7e7dc1bcc0ef464e5e315a2b4c33609590c6cffe, and20261018.339.1 with
b4067e48bd9bd803e958bfe1503ea0a06a3a127f. An actual version must match a literal
case; its independent source pin must match before the pinned census runs.
Unlisted images refuse. This is a closed selection map, with no wildcard/latest
acceptance. Each actual selected image still requires fresh complete census,
SDK normalization and all exact-source gates; success on one image does not
qualify another. Protected native inputs, authority, budgets and cleanup remain
unchanged. Static preflight exercises both good tuples and wrong/missing/unknown
inputs against the actual guard with an inert census. Full hosted qualification,
native/receiver/installed acceptance and all prior unknown effects remain separate.

The first closed-map source attempt,
[run39386130481/a1](https://github.com/FS-GG/.github/actions/runs/39386130481),
advanced through image selection, compilation and the 41 HostAttempt controls.
The later image-closure step then refused because shell-local exports did not
cross the step boundary. That independent failure remains retained. After all
tuple/provider checks, the workflow now writes only the validated literal image
version and source to the job environment file, alongside its existing native
metadata handling. Static preflight must launch a fresh second environment and
prove that both admitted tuples reach the actual downstream required-variable
read. It must also show that local exports alone do not establish the handoff.
Full exact-source hosted qualification remains required after this repair.

### Third closed source candidate tuple, 2026-10-24

The non-required `telemetry-host-package` source check refused before the toolchain
census on the newer hosted Ubuntu 24.04 image `20261023.334.3`
([run 40121481919](https://github.com/FS-GG/.github/actions/runs/40121481919),
[run 40116351123](https://github.com/FS-GG/.github/actions/runs/40116351123)):
`Unadmitted hosted image`. The closed selection map above correctly refused an unlisted
image; those failed prerequisites stay retained and are not retried by editing a literal.

Admission followed the existing procedure for a closed candidate: the actual image version
and its official runner-images source must both be named, and the version and source are
pinned independently. Read from the official repository, not from the failing run:
tag `ubuntu24/20261023.334` and commit
[`bf62c713fd9c48247e2a4d3ce99a9f0554cd745e`](https://github.com/actions/runner-images/tree/bf62c713fd9c48247e2a4d3ce99a9f0554cd745e)
("Updating readme file for ubuntu24 version 20261023.334.3"); the release is marked
prerelease. Its `Ubuntu2404-Readme.md` differs from the already admitted `20261012.334.1`
manifest (`7e7dc1bc`) only in the image version and Bazel (9.3.0 to 9.3.1): kernel
`6.17.0-1022-azure`, GCC 13.3.0, Python 3.12.3, binutils `2.42-4ubuntu2.10`, coreutils
`9.4-3ubuntu6.3`, sudo `1.9.15p5-3ubuntu5.24.04.4` and .NET SDK `10.0.401` are unchanged.
These declared versions are not custody evidence.

Only a third literal tuple is added: `20261023.334.3` with `bf62c713...`. The guard remains
a closed map with no wildcard or latest acceptance, the actual provider/OS/architecture/image
predicates, the pinned census, SDK normalization, complete toolchain binding, all source
gates and the default-off protected native inputs are unchanged. Success on one image does
not qualify another: this image needs its own fresh complete census and full required
source CI on the exact candidate, which this change does not claim. No native fixture,
installed custody, publication, receiver acceptance or activation is established.

### Fourth closed source candidate tuple, 2026-10-30

The non-required `telemetry-host-package` source check again refused before the toolchain
census, now on the newer hosted Ubuntu 24.04 image `20261026.354.1`
([run 41035557653](https://github.com/FS-GG/.github/actions/runs/41035557653) on PR #4948,
[run 41035865253](https://github.com/FS-GG/.github/actions/runs/41035865253) on PR #4950):
`Unadmitted hosted image: 20261026.354.1`. The closed selection map correctly refused an
unlisted image; those failed prerequisites stay retained and are not retried by editing a
literal.

Admission follows the same procedure as the earlier tuples: the actual image version and its
official runner-images source are named, and the version and source are pinned independently.
Read from the official repository (GitHub API), not from the failing run: tag
`ubuntu24/20261026.354` (release "Ubuntu 24.04 (20261026) Image Update", published
2026-10-26, not draft, not prerelease) and commit
[`ac85c71971895cd6234578d6b11f49f7c27ff0c4`](https://github.com/actions/runner-images/tree/ac85c71971895cd6234578d6b11f49f7c27ff0c4)
("Updating readme file for ubuntu24 version 20261026.354.1"). The release's
`target_commitish` is that commit, and its `Ubuntu2404-Readme.md` at that commit and the
release notes both state `Image Version: 20261026.354.1`.

Its manifest differs from the already admitted `20261023.334.3` (`bf62c713`) in many
unrelated entries. Relevant to the census: kernel `6.17.0-1022-azure` becomes
`7.0.0-1017-azure` and the preinstalled .NET SDK `10.0.401` becomes `10.0.403`; the
non-census-relevant remainder is routine tool and package churn. Bazel 9.3.1, GCC
(`4:13.2.0-7ubuntu1`, 13.3.0 toolchain), Python 3.12.3,
binutils `2.42-4ubuntu2.10`, coreutils `9.4-3ubuntu6.3` and sudo
`1.9.15p5-3ubuntu5.24.04.4` are unchanged. The kernel change is the first across the closed
candidates and is not assumed harmless: whether the fresh complete census accepts it is
decided only by that census. These declared versions are not custody evidence.

Only a fourth literal tuple is added: `20261026.354.1` with `ac85c719...`, as an env constant
`FSGG_LEARN_CUSTODY_CORE_IMAGE_SOURCE_20261026` plus a `case` arm with an independent `test`
equality on the source. The guard remains a closed map with no wildcard or latest acceptance,
the actual provider/OS/architecture/image predicates, the pinned census, SDK normalization,
complete toolchain binding, all source gates and the default-off protected native inputs are
unchanged. Success on one image does not qualify another: this image needs its own fresh
complete census and full required source CI on the exact candidate, which this change does
not claim. No native fixture, installed custody, publication, receiver acceptance or
activation is established.
