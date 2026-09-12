# SVG Preview A publication and installed qualification

Feature identity: **SVG-PREVIEW-A**.

Status: **Release A complete through SVG-PREVIEW-A.5; SVG-AUTHOR-01 selected next**.

This is the executable feature plan for the first stable installed SVG preview in the
[accepted SVG game-engine programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md) and
the [Unified Roadmap feature index](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).
It starts from the completed [SVG-SCENE-02](svg-game-engine-scene-renderer.md) local-candidate handoff.
Preview A is a capability label, not a SemVer prerelease label.

## Stable public target

- Rendering publishes one coherent **0.29.0** set of 19 archives: all 17 `FS.GG.UI.*` libraries,
  the `FS.GG.UI` BOM, and `FS.GG.UI.Template`.
- The Rendering tags are `fs-gg-ui/v0.29.0`, `fs-gg-ui-template/v0.29.0`, and `v0.29.0`.
- Templates publishes `FS.GG.Workspace.Template` **0.11.0** under
  `fs-gg-templates/v0.11.0`.
- The workspace wizard remains public **0.11.1**. Release A qualifies its two-step path: the wizard
  creates `fable-game`, then the released Templates adopter enables SVG. There is no wizard 0.12.0.
- SVG remains opt-in. No provider activation, lifecycle default, or general default changes in this
  feature.

The Rendering roster is exactly:

1. `FS.GG.UI.Build`
2. `FS.GG.UI.Canvas`
3. `FS.GG.UI.Controls`
4. `FS.GG.UI.Controls.Elmish`
5. `FS.GG.UI.DesignSystem`
6. `FS.GG.UI.Diagnostics`
7. `FS.GG.UI.Elmish`
8. `FS.GG.UI.KeyboardInput`
9. `FS.GG.UI.Layout`
10. `FS.GG.UI.Scene`
11. `FS.GG.UI.Scene.SvgBrowser`
12. `FS.GG.UI.SkiaViewer`
13. `FS.GG.UI.Symbology`
14. `FS.GG.UI.Symbology.Render`
15. `FS.GG.UI.Testing`
16. `FS.GG.UI.Themes.AntDesign`
17. `FS.GG.UI.Themes.Default`
18. `FS.GG.UI` (BOM)
19. `FS.GG.UI.Template`

## Authority and recovery contract

Publication milestones are protected operations. Their exact operation authority, credentials and
preflight are independent of routine source delivery and fail closed. A prepared release packs once and
retains authenticated original archives before the first feed mutation. Validators, both feed pushes and
readback consume that same retained set.

After any package is published, do not delete, move or retarget a release tag and do not repack a partial
cut. Resume by reading both feeds and replaying only authenticated missing original bytes. Compare every
downloaded payload entry, allowing only NuGet's explained `.signature.p7s` addition. If custody of any
original archive is lost, choose a new coherent version; never reconstruct or overwrite the old one.
Templates publication failure does not invalidate an already verified Rendering publication, but Release A
remains incomplete.

## Milestones

- [x] **SVG-PREVIEW-A.1 — Prepare a recoverable coherent Rendering release — route: routine**

  Rendering [PR #1287](https://github.com/FS-GG/FS.GG.Rendering/pull/1287), merge
  `142a27aee552c104cf9594ca778bc7b6a9e32f83`, prepares the inert 0.29.0 plan without changing a publication-triggering version
  axis. The release workflow retains a source/version/roster/graph/archive/payload-hash manifest before any
  push, validates and pushes the same 19 archives, reads back existing entries, and resumes interrupted
  dual-feed publication by replaying only missing original bytes. Tag deletion and repacking recovery are
  removed. Bootstrap [PR #1288](https://github.com/FS-GG/FS.GG.Rendering/pull/1288), merge
  `f0b4fc92063ab4f4e31739e675edcd0122b7d18f`, first installed the trusted-base routine eligibility boundary;
  #1287 then passed the real exact-base/exact-head `pull_request_target` check.

  Acceptance evidence: 531 package tests; seven custody/replay/mutation tests; the ApiCompat status fixture;
  a live public 0.28.0 baseline result of **Pass 16 / legitimate first publication 1
  (`FS.GG.UI.Scene.SvgBrowser`) / NotApplicable 1 BOM / Unavailable 0**; an exact-head Release pack of 19
  archives; custody prepare/verify; and packed API checks for all 18 packable projects. Custody validation
  also checks the 17-member BOM graph, Fable interfaces/profile, SVG public markers, source/repository commit,
  MIT license, and generated template entries. No package was published and no release tag was created.

- [x] **SVG-PREVIEW-A.2 — Publish and verify Rendering 0.29.0 — route: routine, protected operation**

  Obtain exact operation authority and preflight. Pack the 19 archives once, retain and authenticate them,
  publish GitHub Packages first and then the identical original files to nuget.org. Download from both feeds
  and compare every payload entry except the explained NuGet `.signature.p7s`. Partial availability is
  incomplete and blocks Templates adoption; recover only by original-byte replay under the contract above.

  **Blocked protected-operation checkpoint, 2026-09-12.** Rendering safeguard
  [PR #1289](https://github.com/FS-GG/FS.GG.Rendering/pull/1289), merge
  `c942d4db5d2a55ad4b7fffdffcf4771d25a9789f`, and follow-up PRs
  [#1290](https://github.com/FS-GG/FS.GG.Rendering/pull/1290) (`28b3d645009300375e330b087b6cf8f46142e709`),
  [#1291](https://github.com/FS-GG/FS.GG.Rendering/pull/1291) (`4fdab477101d43ebe2aaa1bac210e7a005eaf9b6`),
  [#1292](https://github.com/FS-GG/FS.GG.Rendering/pull/1292) (`fcfafdf299984f8718501b6f029ebea378518414`),
  [#1293](https://github.com/FS-GG/FS.GG.Rendering/pull/1293) (`03ac951b4ed0b0d8d9cf2d8bbc024a09405376f2`)
  and [#1294](https://github.com/FS-GG/FS.GG.Rendering/pull/1294)
  (`fcccd6358a56f6b8693659ac7c59d60271a6ff90`) installed fail-closed, trusted-main collision and
  credential diagnostics without changing a version axis. Historical 0.28.0 publication run
  [32786664161](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/32786664161) bound the established
  publisher to source `6f0c46f5229a647fd045aee1913096e6774da872` and the repository `GITHUB_TOKEN`
  with `packages:write`, but that workflow never performed collision proof or archive readback.

  Exact-candidate trusted-main run
  [34677990563](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/34677990563) bound source
  `c1095290983a428f74a248c278df5f148b4ad1d7`, workflow
  `fcccd6358a56f6b8693659ac7c59d60271a6ff90`, version 0.29.0, the 19-member plan and NuGet OIDC.
  The historical repository token could read the GitHub NuGet service, registration and version indexes
  (`200`, `200`, `200`), listing 0.28.0 and not 0.29.0 for the anchor, but the known
  `FS.GG.UI.Scene` 0.28.0 archive returned `403`. The established org App installation `143110413` with
  `packages:read` also returned `403` for that archive. Earlier attempts proved both Bearer and Basic App
  variants fail. Therefore authenticated archive collision/readback authority is unavailable and the
  preflight correctly refused before pack, tag or publication. Nuget.org reports all 19 target identities
  absent; all three target tags are absent. ApiCompat remains **Pass 16 / legitimate first publication 1 /
  BOM NotApplicable 1 / Unavailable 0**. No archive was published and no tag was created.

  Resume requires an organization package administrator to grant the `FS-GG/FS.GG.Rendering` Actions
  repository principal read/write access under **Manage Actions access** for the 18 existing packages in
  the roster (all except `FS.GG.UI.Scene.SvgBrowser`), preserve `packages:write` for the trusted release job,
  and permit that repository principal to create, link and subsequently read the new
  `FS.GG.UI.Scene.SvgBrowser` package. The repaired credential must return `200` for the known 0.28.0 archive
  and authoritative `404` for every 0.29.0 archive before this milestone resumes. Metadata-only access is
  insufficient. Do not begin .3 while .2 is blocked.

  **Completed recovery, 2026-09-12.** After the package-access repair, the immutable tags
  `fs-gg-ui/v0.29.0`, `fs-gg-ui-template/v0.29.0` and `v0.29.0` all peel to
  `c654a33bb206c6f3aa0a3adb310231a0d54aec63`. Release run
  [34690467974](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/34690467974) published the retained
  19-archive set to GitHub Packages and nuget.org and read both feeds back with every payload entry exact
  except nuget.org's expected `.signature.p7s`. Recovery hardening PRs
  [#1298–#1301](https://github.com/FS-GG/FS.GG.Rendering/pull/1301), ending at merge
  `7d60c37641c650759f0c2c761023c89bdfcbbee1`, preserve exact-tag recovery without repacking or retargeting.

- [x] **SVG-PREVIEW-A.3 — Adopt public producer pins and prepare Templates 0.11.0 — route: routine**

  Use only public Rendering 0.29.0 inputs. Package bounded adopter, baseline and rollback support bytes and
  qualify staged adoption from public Templates 0.10.0 plus the retained foundation lineage. Preserve authored
  files, lifecycle/skills, backups, symlinks and path/refusal behavior across interruption and rollback. Prove
  the direct, SDD-provider and wizard two-step routes. The exact packed candidate must restore from public-only
  sources and caches; do not activate a provider or change a default.

  Templates [PR #464](https://github.com/FS-GG/FS.GG.Templates/pull/464), merge
  `6a66e0a31c33feab4c8f650709b585df6ac3d4c4`, prepared the exact 0.11.0 candidate with public exact
  Rendering `[0.29.0]` pins, SDK `10.0.400`, bounded adoption and rollback, and clean, retained, direct,
  SDD and typed/profile-2 qualification. Candidate browser/retained-packet and typed receiver workflows
  passed; SVG and lifecycle defaults remained unchanged.

- [x] **SVG-PREVIEW-A.4 — Publish and verify Templates 0.11.0 — route: routine, protected operation**

  Obtain exact operation authority and preflight. Pack once, checksum and retain the original archive; use that
  same archive for both feeds. Verify all five template identities and both-feed payloads. Recovery is
  original-byte replay only. Reconcile registry/provider pin facts without provider activation or a default
  change.

  Tag `fs-gg-templates/v0.11.0` peels to `6a66e0a31c33feab4c8f650709b585df6ac3d4c4`.
  Initial run [34696163461](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34696163461) retained the
  original archive at SHA-256 `560421d4eafc54b5ac47b7e493531bdfa4faf13f2b46528bc932f0d82d08f490`;
  SDK repair [PR #465](https://github.com/FS-GG/FS.GG.Templates/pull/465), merge
  `0a968664bcb94e2f40db071906a7e026e524626c`, then recovery run
  [34697652000](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34697652000) published those unchanged
  retained bytes to both feeds and created the GitHub release. Readback repair
  [PR #466](https://github.com/FS-GG/FS.GG.Templates/pull/466), merge
  `8e9ae550a6a06310770881961360a2cda00df6f9`, and output-propagation repair
  [PR #468](https://github.com/FS-GG/FS.GG.Templates/pull/468), merge
  `ccfb9e08261f4fbf309ad7099adbac72c7b7ce0a`, enabled final trusted-feed recovery run
  [34703775170](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34703775170), which verified all 187 payload
  entries from GitHub Packages and nuget.org. Nuget.org's signed archive SHA-256 is
  `41fa91ba1674a4c1140c4054d4e76cff00cd514462dcdb3d9b3e3cdfa22ba4d9`; all five template identities install.

- [x] **SVG-PREVIEW-A.5 — Qualify installed public receivers and close Release A — route: routine**

  Use isolated public-only sources and caches. Qualify direct `dotnet new` SVG with lifecycle `none`; SDD 1.7.0
  with `none` and typed/profile 2; the wizard 0.11.1 two-step route; retained Templates 0.10.0 and foundation
  upgrades. Require build, test, Fable, serve, selection, definitions, affine transforms, text, export and
  tactical behavior, plus 192+192 typed offline transitions and their mutants. Bind the result to existing
  browser, assistive-technology and performance evidence, rerunning any dimension affected by released-byte
  drift. Keep unavailable dimensions explicit and separate. Only then mark Release A complete and select
  **SVG-AUTHOR-01**.

  Templates [PR #467](https://github.com/FS-GG/FS.GG.Templates/pull/467), merge
  `23298d54c3f44a5a5cd476c999fc473a2ea7e4b1`, and exact-head receiver run
  [34700795032](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34700795032) passed public direct-none,
  SDD-none, typed/profile-2, wizard 0.11.1 two-step and retained public 0.10.0 upgrade routes. All routes
  passed build/test/Fable/serve and the selected browser document, definitions, affine, text, export and
  tactical behavior. The typed clean/retained receivers replayed separate 192+192 transition corpora through
  .NET and Fable and killed the action, ordering, stale, reference, capture and atomicity mutants. Public-byte
  Chromium was rerun; unchanged payload identity binds the accepted three-browser, Orca/AT-SPI and frozen
  performance receipts. Heap, presentation timestamps and physical mobile remain explicitly unavailable.
  SVG stays opt-in, no provider or lifecycle default changed, Release A is complete, and **SVG-AUTHOR-01 is
  the selected next feature**.

## Workspace impact and projection

Fresh public receivers can explicitly choose the released SVG composition: `svg: false` remains omitted and
lifecycle `sdd` remains omitted. Existing projects change only through the bounded adopter/upgrade contract;
the installed public direct, SDD, typed and wizard routes are qualified by .5.

After every completed milestone, update this plan, the accepted SVG programme, and Unified Roadmap sections 0,
9.8 and 9.9 with exact PR, merge, checks, publication/readback and unavailable-dimension evidence. These
projections do not replace owner-native merge or protected-operation authority.
