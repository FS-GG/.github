# SVG-RELEASE-D — complete workspace publication and activation

Status: complete through .5 on 2026-09-28 after public clean-creation acceptance, protected pin activation
and independent default readback under [ADR-0091](../adr/0091-speed-first-clean-v2-start.md). Route: protected operation
for immutable publication; routine source delivery with required checks for default adoption. Durable public
hosting is deferred and does not gate the compatible release.

This is the executable Release D plan for the
[accepted SVG game-engine programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md). It
publishes the frozen complete workspace, proves the public bytes through installed consumers, and activates
the SVG product default within every currently supported lifecycle. The later single-lifecycle default remains
a distinct effect gated by ADR-0091's specific clean-start authority and public receiver acceptance.

## Frozen release inputs

- `FS.GG.Workspace.Template` **0.14.0** is frozen at Templates commit
  `2d8802d527e01afe4755ae7015a5627be88e318f`, tree
  `058b964b75f1b8daa672ee21b4f33155bcff2ed0`, template subtree
  `90eefebdeb982c3270b2bacde462d0f11c11e42f`.
- The retained original `FS.GG.Workspace.Template.0.14.0.nupkg` has SHA-256
  `340250f942efef30702bf8c3f1f9a9096ca246c1bb4b244a2c5806ac49398380` from candidate release run
  [34965730359](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34965730359), artifact `10395590118`.
- `FS.GG.NewSddWorkspace` **0.11.2** is frozen at `.github` commit
  `3935b6bb81dc242635bbb8b400537d07de3b954b`; its package source has not changed on `main` since that merge.
- Rendering **0.31.0**, Game **0.16.0**, Net **0.6.0**, Audio **0.6.0**, SDD **1.8.0**, Drivers
  **0.89.0**, and the owner skill packages recorded by `SVG-WORKSPACE-01` are already public prerequisites.
- The [workspace freeze](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/reports/2026-09-15-svg-workspace-freeze.md)
  accepts the local Podman/Docker Compose plus Caddy deployment boundary. It claims served-byte identity,
  SignalR WebSocket proxy/reconnect and rollback, but not public DNS/TLS, host reboot survival or external
  availability. Those remain [Templates issue #491](https://github.com/FS-GG/FS.GG.Templates/issues/491).

Tags and packages are immutable. Recovery replays only authenticated retained archives; it never moves a tag,
overwrites a package or rebuilds a partial release. Publication, installed qualification, compatible product
activation and lifecycle-default activation are separate claims.

## Milestones

- [x] **SVG-RELEASE-D.1 — Publish and verify Templates 0.14.0 — route: protected operation**

  Create `fs-gg-templates/v0.14.0` on the exact frozen Templates source. Publish the one retained package to
  GitHub Packages and nuget.org and read both feeds back. Verify package identity, every payload member,
  dependency locks, repository commit metadata and release asset custody. Nuget.org's added signature may
  change the archive hash, but all non-signature payload members must equal the retained original.

  Completed by immutable tag `fs-gg-templates/v0.14.0` and release run
  [34969670370](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34969670370): both feeds returned
  all 402 normalized payload entries exactly. The nuget.org signed archive is
  `sha256:a5f218d10bbac42b11afcfb401806c8f0f56261cf79876cc76710471d3666563`.

- [x] **SVG-RELEASE-D.2 — Publish and verify wizard 0.11.2 — route: protected operation**

  After Templates public readback, create `new-sdd-workspace/v0.11.2` on the exact frozen wizard source.
  Publish the same archive to both feeds, retain its checksum and verify payload equality, evaluated version,
  repository commit metadata and the Templates 0.14.0 selection. No later `.github` documentation commit is
  substituted for the frozen tool source.

  Completed by immutable tag `new-sdd-workspace/v0.11.2` and release run
  [34972041225](https://github.com/FS-GG/.github/actions/runs/34972041225). Independent reads from both
  feeds contain the same 25 normalized payload entries and bind repository commit
  `3935b6bb81dc242635bbb8b400537d07de3b954b`; the public signed archive is
  `sha256:c3f6d1333c34511dfef2c1ce3518191749f622689ee57d822a4f083fee35118d`.

- [x] **SVG-RELEASE-D.3 — Qualify public-only installed receivers — route: routine**

  Use empty package caches and no sibling source repositories. Install Templates 0.14.0 directly and through
  public wizard 0.11.2. Cover omitted/default Player and explicit Studio, Tactical, Arcade and Complete bundles
  with lifecycle `none`, `sdd`, `typed-sdd` and retained supported Spec Kit compatibility where applicable.
  Verify exact owner guidance, SDD 1.8 transport, clean generation, retained 0.10–0.13 adoption, collision and
  rollback controls, locked builds, three-browser journeys, Orca observation, two-client authority/reconnect,
  and the local containerized Caddy deployment/rollback boundary. Bind the result to downloaded package hashes.

  Completed by [Templates PR #493](https://github.com/FS-GG/FS.GG.Templates/pull/493), public qualification
  run [34973748082](https://github.com/FS-GG/FS.GG.Templates/actions/runs/34973748082). The 11m38s gate used
  nuget.org-only product inputs and passed every listed bundle/lifecycle/adoption/browser/accessibility/network
  claim plus the Caddy reverse-proxy edge.

- [x] **SVG-RELEASE-D.4 — Activate the compatible SVG product default — route: routine**

  Update the public provider/registry pins to Templates 0.14.0 and wizard 0.11.2 after .3 passes. The omitted
  bundle must create the SVG Player and explicit bundle selections must retain their documented contents under
  every currently supported lifecycle. Verify a fresh installed receiver from the effective public registry.
  This is product-default activation only; it neither changes the coordination epoch nor selects one lifecycle.

  Completed by [registry activation PR #3488](https://github.com/FS-GG/.github/pull/3488), which advances both
  public package rows to Templates 0.14.0 and wizard 0.11.2, regenerates every version projection, validates
  the typed registry, and checks both effective pins against the newest archives on GitHub Packages and
  nuget.org. The installed receiver evidence is milestone .3.

- [x] **SVG-RELEASE-D.5 — Activate the single workspace lifecycle — route: routine source, gated default effect**

  The owning SDD capability is public: SDD [#927](https://github.com/FS-GG/FS.GG.SDD/issues/927),
  [#934](https://github.com/FS-GG/FS.GG.SDD/issues/934), and release
  [2.0.0](https://github.com/FS-GG/FS.GG.SDD/releases/tag/v2.0.0) deliver the Quint-backed Typed SDD
  backend and explicit F# compatibility. The 2.0.2 [tagged release](https://github.com/FS-GG/FS.GG.SDD/actions/runs/35334648281)
  and [read-only recovery](https://github.com/FS-GG/FS.GG.SDD/actions/runs/35336067623)
  establish public package publication and payload readback; the [SDD release record](https://github.com/FS-GG/FS.GG.SDD/blob/83790aedc228e2158c9da7a9ac8e30195bdf9fbe/docs/release/fsquint-migration.md)
  documents FsQuint stable adoption and replay qualification. These do not change a provider lifecycle token or a scaffold default.
  The shared generation 2 [`OpenV2` append](https://github.com/FS-GG/FS.GG.Coordination.Authority/commit/26d1882af9293b264df17a1fa98515e108313fe5)
  and `.github` ordinary settlement/rerun satisfy the specific clean-start epoch prerequisite selected by
  [ADR-0091](../adr/0091-speed-first-clean-v2-start.md). No `OperatingV2` claim is made.

  The public SDD 2.0.2 receiver preparation merged in [Templates #641](https://github.com/FS-GG/FS.GG.Templates/pull/641)
  at `848fe5a1fb581707be67ecb6bda326ecd43a2fe8`. Its focused installed clean receiver and
  bounded promised retained-adoption check passed on the reviewed head, along with the native required
  checks. The [exact-main public receiver run](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36454677283)
  also passed at the protected merge. This earlier receiver evidence preceded the successor publication and default activation recorded below.

  The SDD, Templates and wizard owners must deliver and publish the exact package composition that makes
  omitted SVG creation select `typed-sdd` with its default `quint-specification-v1` backend. Qualify that
  public candidate before changing the effective registry/default policy. From empty caches and public
  feeds only, use raw `dotnet new fs-gg-fable-game` to prove omitted SVG Player product files. Use installed
  `fsgg-sdd scaffold` and the wizard with lifecycle omitted to prove the root `typed-sdd` lifecycle and
  refusal path; check retained explicit lifecycle tokens and SVG bundles separately. Then update the effective
  public pins under current required checks and repeat clean creation from those pins. Bind both proofs
  to package hashes and independently read back the activated default. Treat promised retained-workspace
  upgrades as a separate qualification.

  Templates 0.15.0 was published from `b86c841a1c4c4bf7f157a3ae4dc61b0356d6576d` by
  [run 36465599265](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36465599265). Wizard 0.12.0
  was published from `4889c446de0a431d1168a61a89ab660fc2062314`; its journal-aware
  [recovery 36471023672](https://github.com/FS-GG/.github/actions/runs/36471023672) verified all eight effects.
  The separate public receiver [Templates #644](https://github.com/FS-GG/FS.GG.Templates/pull/644)
  merged at `59d0c521446a3d9d362d81a978b25434ce4d2d6f`.
  [Public-only run 36472328832](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36472328832)
  passed the omitted and explicit lifecycle routes, named bundles, 18 locked builds, authored authority,
  wrong-profile refusal and six sampled Quint invariants. Receipt SHA-256 is
  `fd89d991f1966517ae56aebdc4b2e5000ee94e6f8ce7eb6aa6c85b932cd16c64`; all 63 evidence hashes and
  the identical direct/wizard authority hash were independently verified.

  [Activation #3955](https://github.com/FS-GG/.github/pull/3955) merged as
  `2574f02aa8cf835ab1e0b5ff6c62504c33098183`, selecting public Templates 0.15.0 and wizard 0.12.0.
  The fresh [public-only repeat 36474756648](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36474756648)
  passed the complete clean-creation boundary after activation. Its qualification receipt SHA-256 is
  `42c882fab211f15bf05745c98ddaaa870b3f923040c7aa0ee59a734420d1a9fb`; all 63 evidence hashes
  and both authored authorities were independently verified. Remote protected registry and generated
  projection SHA-256 values are `aa6ad10f8ef718b572b7c954b24e4d71697a5e40f3c3aca3f879f92b6faa6680`
  and `ef14e4daa3a508ef18ffe9bff317c6538098173aaf09f2f7e28a9e33493162d7`. Both select the exact
  public source/package pairs. Milestone .5 is complete. Retained upgrades remain a separate qualification;
  the sampled invariant result does not establish exhaustive SDD verification readiness.

## Completion boundary

Milestones .1–.4 establish the public complete SVG workspace and its product default across supported lifecycle
choices. Release D is fully complete only when .5 also establishes the authorized lifecycle default. Durable
public game hosting is deliberately outside this release boundary and may be resumed through issue #491 without
changing the frozen package contents.

Current state: Release D is complete through .5. Public SDD 2.0.2, Templates 0.15.0 and wizard 0.12.0
passed clean receiver acceptance before and after protected pin activation, with independent default
readback. Retained-workspace upgrades and durable public hosting remain separate. The shared generation 2
`OpenV2` remains observed at its separate operating boundary; this release makes no fleet `OperatingV2` claim.
