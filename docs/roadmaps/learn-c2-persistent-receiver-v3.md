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
qualification remain open. The current C4 runner stops with qualification Unknown and needs its
separate C5 adapter to execute the exact published Host bytes. This declaration correction creates
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

This closes Host successor publication. The image constructor still pins historical
Host 0.3.0 and requires a separately qualified source rebinding to these served facts.
Served repaired Manager distribution, genuine native/runtime/profile acquisition,
two cold OCI builds, C5 retirement and actual served Manager-to-Host inactive qualification
remain open. Installation, grants, capture, restart/recovery, usage completeness and
activation are unchanged; no receiver acceptance follows from publication.
