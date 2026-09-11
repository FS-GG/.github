# SVG game engine installed model qualification

Feature identity: **SVG-QUAL-01**.

Status: **SVG-QUAL-01.1 complete; SVG-QUAL-01.2 is ready**.

Programme: [SVG game engine and Fable template](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md).
Unified part: **SVG game engine and Fable workspace completion**, in the
[section 9.8 feature index](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).
Depends on: completed [SVG-FOUND-01 foundation window](svg-game-engine-foundation.md).

Accountable planning owner: `.github`. Implementation owners are SDD for installed tool provisioning,
Rendering for the canonical retained reducer model/correspondence, and Templates for installed receiver
qualification. S.I.R. is strictly read-only: this feature makes no S.I.R. source, configuration, branch,
pull-request, deployment or product-state write.

## Outcome and boundaries

Qualify one installed, content-addressed profile-2 model route before further stateful engine semantics are
added. A clean isolated consumer can provision the exact supported Quint, literate extractor and Fable tool
objects, author and inspect a retained scene reducer offline, and refuse missing, modified or wrong-profile
inputs. Rendering then binds that model to its real .NET and Fable reducer. Templates proves the same authority
in clean and retained workspaces.

This feature may use local Rendering and Templates candidate packages. It does not publish those packages,
complete Release A, activate a provider/registry entry, change a lifecycle/default or claim S.I.R. adoption.
Generic fixtures may compare disclosed tactical characteristics already captured by SVG-FOUND-01. The user,
as S.I.R. copyright owner, authorizes reuse of their first-party implementation logic without AGPL obligations;
record its provenance and keep S.I.R.-specific public types and tactical rules out of generic APIs. Inventory
third-party contributions, fonts, images, assets and dependencies separately under their own terms. The S.I.R.
repository remains strictly read-only.

The [foundation audit](../reports/2026-09-11-svg-game-engine-extraction-boundary.md) observed installed
`fsgg-sdd 1.6.0` and profile `fsgg-quint-profile/2`. Against an empty isolated cache, `author` refused
`lmt-binary` and `quint-binary` with `typedSdd.v2.cacheInvalid`. The expected Quint 0.32.0 archive SHA256
is `939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f`; the accepted lmt binary SHA256 is
`37e0b0365c2641edce40b48605471f61fa12e97c3e2376152f0e849abdc31f10`. Reproduce that exact observation
before changing SDD and try its existing supported provisioning route first. Implement only a demonstrated
producer-owned gap.

## Ready qualification window

- [x] **SVG-QUAL-01.1 — Provision and qualify the exact installed profile-2 toolchain — route: routine**

  Depends on: SVG-FOUND-01.5.

  Scope: SDD reproduces the empty-cache refusal and attempts its current documented provisioning route. If no
  supported route can acquire and validate the accepted objects, add only the narrow producer-owned
  provisioning surface needed by installed consumers. Preserve content-addressed verification and the existing
  `author`/`inspect` authority.

  Acceptance: an isolated installation records the installed SDD package/platform identity, Quint 0.32.0,
  accepted lmt source/build identity, Fable 5.13.0, exact object hashes and cache provenance. With network access
  disabled after provisioning, profile 2 authors and inspects a small literate reducer. Missing, modified and
  wrong-profile objects are refused with stable diagnostics; a profile-1 workspace remains readable and is not
  silently upgraded. Tests use installed package/CLI bytes without a sibling SDD source or implicit ambient
  executable. Acquisition failures leave no accepted partial object.

  Evidence: SDD source [PR #981](https://github.com/FS-GG/FS.GG.SDD/pull/981), head
  `0c219cf237fb6a6d74997972e9e0f5057a81f788`, merged as
  `2e3a68bdde232e229952ba5fd4c52bc0661df996`, added the installed `typed-sdd provision` operation after
  reproducing the 1.6.0 empty-cache refusal and confirming that no supported acquisition operation existed.
  Release [PR #982](https://github.com/FS-GG/FS.GG.SDD/pull/982), head
  `5630d508777cb3d3dbb985bc40deebffb600ae91`, merged as
  `b1a3bc1c46dfc28d7e8a02696f0e7bf4b026df50` and published the additive coherent SDD 1.7.0 set. The exact
  [no-push candidate run](https://github.com/FS-GG/FS.GG.SDD/actions/runs/34623117701) and serialized
  [dual-feed public readback](https://github.com/FS-GG/FS.GG.SDD/actions/runs/34623411200) passed. Receipt artifact
  `10273841192` binds Artifacts package SHA256
  `7563b15fe2cd5454303910ab4d5a8897a37c47c2b646aed84632eee952709432` and CLI package SHA256
  `ba944a469c83eaea3f790a310631e9d4728211e10caaa5534ef5c3ff632173cc` to byte-identical GitHub Packages and
  NuGet.org payloads. Public-package Q2 passed 13/13 and Q3 passed 22/22, including offline author/inspect,
  exact cache identity, missing/modified/wrong-profile refusal, profile-1 preservation, concurrent provision
  and failure cleanup. Toolchain evidence records Linux/amd64, profile 2, Quint 0.32.0
  `939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f`, lmt source
  `driusan/lmt@62fe18f2f6a6e11c158ff2b2209e1082a4fcd59c` built with Go 1.24.1 and `CGO_ENABLED=1` to
  `37e0b0365c2641edce40b48605471f61fa12e97c3e2376152f0e849abdc31f10`, and Fable 5.13.0. The toolchain
  JSON SHA256 is `6e63ff4a9d17d4d8ff1baf2afda662962344b562308f8b6bc40caddd1b68e688`; Q2 and Q3 JUnit SHA256 values
  are `6661ae90cc412542102601d2e699296cbb15cb53e83a564aad8762e47f5d7fdf` and
  `778d66af0665bdbd43985877cb7ac7428877dc99bce8ffda55f6735131902e04`. The first tag-triggered
  [run 34623410870](https://github.com/FS-GG/FS.GG.SDD/actions/runs/34623410870) published and passed dual-feed
  byte readback but its immediate clean public install met NuGet V3 propagation lag; the paired serialized run
  qualified the same package bytes without repacking. S.I.R. was not accessed or changed.

- [ ] **SVG-QUAL-01.2 — Bind the retained scene reducer model to real runtimes — route: routine**

  Depends on: SVG-QUAL-01.1.

  Scope: Rendering owns a canonical literate retained reducer model and correspondence adapters for its actual
  .NET and curated Fable reducers. The model is the semantic source for behavior added by this slice; it does not
  absorb rendering mechanics or product rules.

  Acceptance: bounded model witnesses and real reducer correspondence cover current and stale replacement,
  selection, focus, camera changes and pointer capture/release. .NET and Fable consume the same public contract.
  Targeted mutants for stale acceptance, identity retention, focus, camera anchoring and capture release fail.
  The checked evidence binds the installed SDD/profile/tool identities from SVG-QUAL-01.1 and refuses stale
  generated bindings.

- [ ] **SVG-QUAL-01.3 — Qualify clean and retained typed-SDD receivers — route: routine**

  Depends on: SVG-QUAL-01.2.

  Scope: Templates adds isolated clean-creation and retained-workspace journeys using explicit profile 2/backend
  selection and the candidate Rendering surface. It preserves the current default arena and lifecycle choices.

  Acceptance: both receivers invoke the actually installed author and inspect commands, compile the resulting
  .NET/Fable correspondence and distinguish an implementation-only repair from a semantic change. The semantic
  change refreshes its model/bindings and passes real correspondence; the repair reuses current semantics through
  its applicable checks. Wrong tools, wrong profile and stale bindings refuse. The retained journey preserves
  authored files and reports conflicts without destructive scaffold rerun. Effective owner guidance and
  lifecycle selection are observed; installed skills are not presumed refreshed by backfill.

  Rendering and Templates packages may remain local. Their publication, installed receiver qualification and
  Release A remain pending and separately authorized; the SDD 1.7.0 producer toolchain used here is public.

## Generated-workspace impact

Affected family: explicitly selected `fs-gg-fable-game` / `fable-game` typed-SDD profile-2 qualification
fixtures. SVG-QUAL-01.1 and .2 change producer/source qualification only. SVG-QUAL-01.3 first changes candidate
clean and retained receiver fixtures; no installed public scaffold changes until Rendering and Templates
publish artifacts compatible with SDD 1.7.0 and an actual receiver consumes them.

Before qualification, an empty installed profile-2 cache refuses without a supported provisioning operation.
After this feature, the explicit candidate receiver can provision exact verified objects, author/inspect offline
and bind generated evidence to real reducers. Existing omitted, `none`, `sdd` and profile-1 selections retain
their behavior. A later preview-A release owns public publication and installed qualification.

## Stop condition

Stop after SVG-QUAL-01.3 with installed model qualification complete and Release A pending. Extend the programme
through the next feature in the accepted sequence; do not collapse publication or default activation into this
feature. Pause only the affected milestone on a real producer/tool/authority block, preserving exact failure
evidence and the strict S.I.R. read-only boundary.
