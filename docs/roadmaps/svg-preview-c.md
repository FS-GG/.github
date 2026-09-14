# SVG-PREVIEW-C — installed replay, network and scale preview

Status: complete — Release C published and installed public receivers qualified on 2026-09-14. Route: routine for release preparation and installed qualification;
protected operation for publication.

This is the executable Release C plan for the
[accepted SVG game-engine programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md). It
publishes the completed replay/rules, authoritative network and SVG-scale set, then proves those exact public
bytes through clean and upgraded installed receivers. It does not inspect or modify S.I.R.; tactical evidence
continues to use the already disclosed Templates-owned external characterization.

## Stable public target

- Rendering publishes its coherent 19-package `FS.GG.UI.*` set at **0.31.0**, adding the bounded spatial
  working-set surface and M9 qualification assets over public 0.30.0.
- Game publishes `FS.GG.Game.Core`, `FS.GG.Game.Render` and `FS.GG.Game.Harness` at **0.16.0**, adding the
  replay/rules and authoritative-session capabilities over public 0.15.0.
- Net publishes its coherent six-package set at **0.6.0**, adding the accepted ordered reconnect/backpressure
  reducer over public 0.5.0.
- Audio remains at public **0.6.0** and SDD remains at public **1.7.0** because Release C changes neither
  artifact family.
- Templates publishes `FS.GG.Workspace.Template` at **0.13.0** and pins the exact public Rendering, Game,
  Net and existing Audio/SDD versions used by the installed matrix.
- SVG and its Replay, Network and scale examples remain explicit preview selections. No provider, lifecycle,
  coordination epoch or general default changes are part of Release C.

The versions follow the current public lines reviewed at selection: additive public APIs advance each changed
producer and Templates by one minor version. API compatibility gates remain authoritative and may require a
new plan if they classify any change as breaking.

## Custody and recovery

Each changed producer packs once and retains authenticated original archives before any feed mutation. Both
feeds receive those originals, and readback compares archive payload members while allowing only nuget.org's
explained signature addition. Existing tags and packages are immutable. Recovery replays only retained source
artifacts and never retargets a tag, overwrites a package or rebuilds part of a cut.

Producer readback is a prerequisite for Templates adoption. Source merge, one-feed availability and metadata
listing remain insufficient. Public package availability, installed workspace compatibility and lifecycle
activation are reported independently.

## Milestones

- [x] **SVG-PREVIEW-C.1 — Prepare coherent producer releases — route: routine**

  Prepare Rendering 0.31.0, Game 0.16.0 and Net 0.6.0 from their accepted heads. Reconcile package rosters,
  dependency ranges, locks, Fable sources, API baselines, candidate-only omission ledgers and release custody
  contracts. Run each repository's ordinary and release-configuration gates without creating tags or
  publishing archives.

  Rendering [PR #1322](https://github.com/FS-GG/FS.GG.Rendering/pull/1322), merge
  `96810c3c66ba888ecd03a0e10fe6374fd88917f9`, prepares all 19 UI/template archives at 0.31.0, adds the
  spatial working-set API mirror and retires its candidate-only omissions. Exact-candidate pack/custody,
  API compatibility (17 passes, one BOM not applicable, zero breaks), Build.Tests and the release contract
  passed. The native deterministic, generated-product and strict documentation gates passed after correcting
  a stale 0.30.0 sentence in the pinned API ledger; no technical gate was weakened.

  Game [PR #635](https://github.com/FS-GG/FS.GG.Game/pull/635), merge
  `996832a6ebb5c893199627b0ecc46f7c1da848cd`, prepares all three 0.16.0 packages against public 0.15.0,
  updates internal lock ranges, and checks the packed replay/planning/rules/network Fable sources. Locked
  restore, Release build, 928 .NET tests, package compatibility/custody and clean consumers passed, followed
  by native Linux, Windows, Fable and browser checks. Its existing public Rendering API dependencies remain
  sufficient; preparation does not require unpublished Rendering 0.31.0.

  Net [PR #87](https://github.com/FS-GG/FS.GG.Net/pull/87), merge
  `ada81df43ab344fccbab701493b5d0c585627a32`, prepares Core, Elmish, Grpc, Protobuf, WebSocket and
  WebSocket.Server at 0.6.0 against public 0.5.0. It corrects the obsolete five-package release roster,
  updates internal locks and adds retained source-run custody, exact-tag recovery, checksum verification
  and dual-feed payload readback. Locked restore, Release build, 29 tests, package metadata, API compatibility
  and the release-contract gate passed. Net intentionally ships no Fable source payload.

  All three source preparations are merged. Local candidate archives are preparatory evidence only;
  .2 must create and retain authoritative source-run custody from the accepted merged commits through each
  producer's release workflow before any feed mutation. No Release-C tag or archive was published by .1.

- [x] **SVG-PREVIEW-C.2 — Publish and verify producer releases — route: protected operation**

  Publish Rendering first, then Game and Net against their exact public dependencies. Verify every expected
  archive from GitHub Packages and nuget.org, including payload members, dependency ranges, Fable sources,
  repository commit metadata and immutable tags. Preserve custody and readback manifests; recover only by
  authenticated original-byte replay.

  Rendering run [34806339482](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/34806339482)
  published/read back all 19 archives at source `96810c3c66ba888ecd03a0e10fe6374fd88917f9` and the three
  exact 0.31.0 tags. Custody artifact `10333198495` retains the original bytes and manifest.
  Game run [34815287187](https://github.com/FS-GG/FS.GG.Game/actions/runs/34815287187) published/read back
  all three 0.16.0 archives at `996832a6ebb5c893199627b0ecc46f7c1da848cd`, tag `v0.16.0`, with custody
  artifact `10336430350` and readback artifact `10336470968`.
  Net run [34815521271](https://github.com/FS-GG/FS.GG.Net/actions/runs/34815521271) published/read back
  all six 0.6.0 archives at `ada81df43ab344fccbab701493b5d0c585627a32`, tag `v0.6.0`, with custody
  artifact `10335663605` and readback artifact `10335649243`. Both feeds match the retained payloads;
  only nuget.org's signature addition changes archive bytes. Templates remains public at 0.12.0 pending .3–.5.

- [x] **SVG-PREVIEW-C.3 — Adopt public pins and prepare Templates 0.13.0 — route: routine**

  Replace replay/network/scale candidate seams with exact public producer pins while retaining explicit
  preview selection. Regenerate the public API mirror, retire candidate omission entries and update the
  bounded adopter. Qualify direct, SDD, typed/profile-2, wizard, retained 0.10–0.12, interruption, conflict
  and rollback routes. The ordinary Player closure must exclude optional Studio and analysis tooling.

  Templates [PR #480](https://github.com/FS-GG/FS.GG.Templates/pull/480) merged as
  `6acdfc5f5da41156db0aeaffcd54885b3b9e66be`, from qualified head
  `89f35c393529efe316fff61e5c24728101251383`. The generated preview pins public Rendering 0.31.0,
  Game 0.16.0, Net 0.6.0 and Audio 0.6.0, with zero public-API mirror omissions. Source qualification
  run [34818598332](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34818598332), artifact
  `10337705570`, passed direct, SDD, wizard/adopter and retained 0.10–0.12 routes, preservation,
  interruption and rollback, plus presentation, authoring, input, replay, scale/accessibility and network
  authority/reconnect/review in Chromium, Firefox and WebKit. Typed/profile-2 receivers passed run
  [34818598319](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34818598319); composition/coherence
  passed [34818598873](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34818598873), and pretag
  pack/checksum/custody validation passed [34818598496](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34818598496).
  A generated FSharp.Core lock mismatch was repaired to the shipped SDK 10.0.400 before acceptance.
  This is source adoption only: public Templates remains 0.12.0, SVG stays opt-in, and .5 must separately
  establish public-package receiver and performance evidence.

- [x] **SVG-PREVIEW-C.4 — Publish and verify Templates 0.13.0 — route: protected operation**

  Pack once from the accepted source, retain and authenticate the original archive, publish those same bytes
  to both feeds, create the immutable tag/release and read back every template payload member and producer
  pin. Recovery uses only the retained source-run artifact.

  Templates release run [34820243790](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34820243790)
  succeeded from exact source `6acdfc5f5da41156db0aeaffcd54885b3b9e66be`. The immutable annotated
  `fs-gg-templates/v0.13.0` tag peels to that source. Artifact `10337733099` retains the original archive
  and checksum; its nupkg SHA-256 is `d93b122cb50fd27bed0cc34d21ce20835c7f6a0c7769c811e3fc30720417b42d`.
  The workflow tested that retained archive before pushing it to both feeds. Readback artifact
  `10339235374` proves all **205 payload entries** match on each feed; GitHub Packages retains the original
  archive hash, while nuget.org's added signature produces
  `ab72f74a76d59ad4be6f11f367bbdf1b27f8bdedae7a2e3afecbe0e4b3fac10c`.
  The [GitHub Release](https://github.com/FS-GG/FS.GG.Templates/releases/tag/fs-gg-templates/v0.13.0)
  retains the original package asset. Publication is complete; .5 remains the separate installed-public
  replay/network/scale and retained-upgrade acceptance boundary.

- [x] **SVG-PREVIEW-C.5 — Qualify installed public receivers and close Release C — route: routine**

  From clean public-only caches, install Templates 0.13.0 through direct, SDD, typed/profile-2, current wizard
  and retained-upgrade routes. Run replay equality/divergence/rules, two-browser authority/reconnect/review,
  Chromium scale measurements and the Chromium/Firefox/WebKit accessibility matrix against downloaded public
  bytes. Bind evidence to exact package hashes. Close Release C and select `SVG-WORKSPACE-01` only after every
  public receiver passes without weakening a threshold or treating activation as implied.

  Public receiver run [34822927445](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34822927445)
  passed at Templates candidate `aad05bf840afe4a5292e785aee5adf33c9c37a5f`.
  Artifact `10339327277` retains qualification JSON SHA-256
  `d335c04623604367ef267ecb95fee7607fceae3cb8ba8d3e0792cb516dc7fa65`, the signed public Templates
  archive identity and hashes for all seven consumed producer archives. Direct, SDD none/default/typed,
  wizard/adopter and retained 0.10–0.12 routes passed; collisions refused without writes, interruptions
  rolled back and authored files survived. Chromium, Firefox and WebKit passed presentation, authoring,
  input, replay/rules, dense/extent/responsive/accessibility and two-client authority/reconnect/review.

  A fresh exact-release-source Rendering reference run replaced a stale source-mismatched timing receipt.
  Its retained receipt SHA-256 is `98ba3cf0a2a1db103ca7ac1711017062bf2c3697d59a0880e376c3629c5953b6`.
  The frozen v2 reference-host criteria passed: ordinary p95 66.861–66.881 ms against 100 ms, dense p95
  100.263–108.469 ms against 150 ms, extent cost ratio 1.0, zero live-node growth and zero missed-frame ratio.
  Before reuse, the public receiver verifies every released Rendering Fable source member against that
  receipt and the exact release commit. Reference Chromium 151.0.7922.34 measurements remain separate
  from installed shared-runner Chromium 140.0.7339.16 diagnostics; no physical-device or retained-heap
  result is inferred. Templates [PR #481](https://github.com/FS-GG/FS.GG.Templates/pull/481) merged as
  `7b202c9a6053e017e9ffc17fec0c53aa9bb15ac0`; full composition run
  [34822927819](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34822927819) and release-route
  validation [34822927433](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34822927433) also passed.

## Completion boundary

Release C establishes supported installed replay/network/scale preview capability through M7–M9 and C14–C19.
It does not establish complete C20 composition, Release D or lifecycle/default activation. Those remain owned by
`SVG-WORKSPACE-01`, `SVG-RELEASE-D` and their explicit activation boundary.


## Separate skills publication boundary

Rendering's symbology recipe pin and its manifest digest advance with the UI template. The independently
versioned `FS.GG.Rendering.Skills` package remains public at 0.1.1 and is outside this 19-package UI release
roster. These source changes do not establish updated installed product skills; reconcile that separate
package and its installed consumers under `SVG-WORKSPACE-01`/C20.

## Next feature

[SVG-WORKSPACE-01](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/roadmaps/svg-workspace-01.md) is selected after this closure and the unified section 0 update. Its
first window is .1–.3: owner-published guidance, installed SDD delivery, and generated default SVG/bundle
composition. Complete C20, actual generated-development/deployment journeys and Release D remain required.
The later lifecycle-default effect retains its actual common-base, migration and OperatingV2 prerequisites;
independent product and explicit typed-profile work can continue.
