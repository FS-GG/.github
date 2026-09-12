# SVG Preview A publication and installed qualification

Feature identity: **SVG-PREVIEW-A**.

Status: **In progress; SVG-PREVIEW-A.1 complete; SVG-PREVIEW-A.2 is next**.

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

- [ ] **SVG-PREVIEW-A.2 — Publish and verify Rendering 0.29.0 — route: routine, protected operation**

  Obtain exact operation authority and preflight. Pack the 19 archives once, retain and authenticate them,
  publish GitHub Packages first and then the identical original files to nuget.org. Download from both feeds
  and compare every payload entry except the explained NuGet `.signature.p7s`. Partial availability is
  incomplete and blocks Templates adoption; recover only by original-byte replay under the contract above.

- [ ] **SVG-PREVIEW-A.3 — Adopt public producer pins and prepare Templates 0.11.0 — route: routine**

  Use only public Rendering 0.29.0 inputs. Package bounded adopter, baseline and rollback support bytes and
  qualify staged adoption from public Templates 0.10.0 plus the retained foundation lineage. Preserve authored
  files, lifecycle/skills, backups, symlinks and path/refusal behavior across interruption and rollback. Prove
  the direct, SDD-provider and wizard two-step routes. The exact packed candidate must restore from public-only
  sources and caches; do not activate a provider or change a default.

- [ ] **SVG-PREVIEW-A.4 — Publish and verify Templates 0.11.0 — route: routine, protected operation**

  Obtain exact operation authority and preflight. Pack once, checksum and retain the original archive; use that
  same archive for both feeds. Verify all five template identities and both-feed payloads. Recovery is
  original-byte replay only. Reconcile registry/provider pin facts without provider activation or a default
  change.

- [ ] **SVG-PREVIEW-A.5 — Qualify installed public receivers and close Release A — route: routine**

  Use isolated public-only sources and caches. Qualify direct `dotnet new` SVG with lifecycle `none`; SDD 1.7.0
  with `none` and typed/profile 2; the wizard 0.11.1 two-step route; retained Templates 0.10.0 and foundation
  upgrades. Require build, test, Fable, serve, selection, definitions, affine transforms, text, export and
  tactical behavior, plus 192+192 typed offline transitions and their mutants. Bind the result to existing
  browser, assistive-technology and performance evidence, rerunning any dimension affected by released-byte
  drift. Keep unavailable dimensions explicit and separate. Only then mark Release A complete and select
  **SVG-AUTHOR-01**.

## Workspace impact and projection

Source preparation and producer publication alone do not alter generated workspaces. After .4, a fresh public
receiver can explicitly choose the released SVG composition: `svg: false` remains omitted and lifecycle `sdd`
remains omitted. Existing projects change only through the .3 adopter/upgrade contract. Installed behavior is
not qualified until .5.

After every completed milestone, update this plan, the accepted SVG programme, and Unified Roadmap sections 0,
9.8 and 9.9 with exact PR, merge, checks, publication/readback and unavailable-dimension evidence. These
projections do not replace owner-native merge or protected-operation authority.
