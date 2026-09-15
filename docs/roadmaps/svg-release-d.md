# SVG-RELEASE-D — complete workspace publication and activation

Status: complete through .4 on 2026-09-15; .5 is waiting on OperatingV2. Route: protected operation for immutable
publication; routine for installed qualification and documentation. Durable public hosting is deferred and does
not gate the compatible release.

This is the executable Release D plan for the
[accepted SVG game-engine programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md). It
publishes the frozen complete workspace, proves the public bytes through installed consumers, and activates
the SVG product default within every currently supported lifecycle. The later single-lifecycle default remains
a distinct effect gated by the actual OperatingV2/SDD authority transition.

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

- [ ] **SVG-RELEASE-D.5 — Activate the single workspace lifecycle — route: protected effect**

  The owning SDD capability is complete: SDD [#927](https://github.com/FS-GG/FS.GG.SDD/issues/927),
  [#934](https://github.com/FS-GG/FS.GG.SDD/issues/934), and release
  [2.0.0](https://github.com/FS-GG/FS.GG.SDD/releases/tag/v2.0.0) deliver the Quint-backed default,
  explicit F# compatibility, implementation correspondence, and public package/readback qualification.
  Proceed only after actual OperatingV2 evidence satisfies the remaining accepted lifecycle/default prerequisite.
  Then update the lifecycle default through its authority sources, qualify clean and retained public receivers,
  and preserve explicit compatible legacy selections. Until then, report .1–.4 as the published compatible
  complete SVG workspace, not as full Release D closure.

## Completion boundary

Milestones .1–.4 establish the public complete SVG workspace and its product default across supported lifecycle
choices. Release D is fully complete only when .5 also establishes the authorized lifecycle default. Durable
public game hosting is deliberately outside this release boundary and may be resumed through issue #491 without
changing the frozen package contents.

Current state: the compatible complete SVG workspace is published and active through .4, and the SDD producer
half of .5 is public as 2.0.0. Full Release D remains open only for the authoritative OperatingV2 transition and
the receiver/default activation it authorizes; successful producer publication does not manufacture that epoch.
