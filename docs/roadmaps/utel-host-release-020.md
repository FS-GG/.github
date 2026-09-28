# UTEL-HREL-02 — Native capture and learning export Host 0.2.0

Owner: `.github` Host package and release producer. Main/SystemAdmin owns the protected installation, capture and post-adoption readback.

The immutable public Host 0.1.7 was published from source `5ebaab8f49b3c7fe00b1dce355338704a3a98a70` before protected native capture and bounded same-host learning export merged in #3940 at `76ae9d3ee11dd5dd7624a01b6fac93c80e6ecf55`. The new `collect-native` and `export-learning` command surfaces require a new stable pre-1.0 minor version. Host 0.1.7 runs schema 10; current source runs schema 12 with explicit migrations 11 and 12. Host 0.2.0 therefore declares release schema range 10 through 12. The existing same-schema updater must refuse an unchanged in-place update.

## Source and candidate

- [x] Merge one routine source PR that binds the Host project, backup manifest, candidate workflow, retained-artifact verifier, eight-effect publisher, tests and this roadmap to 0.2.0, `telemetry-host/v0.2.0`, and the unused `fsgg/v2/journal/release/utel-host-rel-07` journal.
- [x] From exact protected `main`, run the read-only candidate workflow once. It must prove the unused tag and both-feed version, run the locked Host/dashboard/browser suites plus native-source and learning-analysis qualification, pack once, validate the installed tool and package boundary, and retain the original archive with its source-bound `fsgg.telemetry.host-release/1` manifest.

Candidate qualification creates no tag, release, journal or feed effect. A moved `main`, a rerun, a pre-existing version, or a changed archive refuses.

## Protected publication

- [x] Freeze `main` after candidate readback and run the publisher's no-effect preflight against the exact candidate and source.
- [x] Publish that retained archive through the existing `release-successor` environment and ordinary-ledger App journal. The ordered effects remain tag, draft release, GitHub Packages, nuget.org, original package asset, manifest asset, publication-journal asset and promotion. Every intent precedes dispatch and every effect needs provider readback; an indeterminate response waits and is never blindly retried.
- [x] Verify the terminal journal, exact tag/source, release assets, both feeds' normalized producer payload, clean anonymous public install, and installed `export-learning` command surface.

The existing Trusted Publishing registration remains scoped to owner `Paradigma11`, package `FS.GG.Telemetry.Host`, repository `FS-GG/.github`, workflow `release-telemetry-host-successor-publish.yml`, and environment `release-successor`. Missing OIDC or feed authority blocks publication before package effects; it does not change source qualification.

## Main adoption

- [x] Give Main the promoted tag, source SHA, original archive SHA-256, manifest digest and schema range 10 through 12. Main verifies the three-asset release shape and exact executable digest before any installation change.
- [ ] For legacy Host state, Main stops the Host, takes and verifies a backup, rehearses the schema-10-to-12 migration and retained-fact readback, then activates only after health and rollback safeguards pass. Provenance absent from schema 10 remains unknown.
- [ ] For prospective native-collector-v2 custody, Main may instead create a separate fresh schema-12 store after qualifying its config-/2 runtime, exact reviewed executable and private writable roots. It must not relabel the legacy schema-10 store as fresh custody.
- [ ] Main separately verifies protected capture, bounded `export-learning` acquisition and trusted analyzer readback. Installed custody, snapshot origin and usage/shared-cost completeness remain unknown until that private operation succeeds.

This release has no fresh-workspace default or generated-workspace effect. It changes only an explicitly installed optional Host; Coordination manager installation and receiver activation remain separate owner operations.

## Verified release result

Source [#3956](https://github.com/FS-GG/.github/pull/3956) merged at `933328fdf76e921a0198625f9ba834ddea0e12ed`. First-attempt [candidate 36482276851](https://github.com/FS-GG/.github/actions/runs/36482276851), [no-effect preflight 36482730487](https://github.com/FS-GG/.github/actions/runs/36482730487) and [publisher 36482933665](https://github.com/FS-GG/.github/actions/runs/36482933665) passed on that source. The [promoted three-asset release](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.2.0) and protected Authority `rel-07` journal generation 17 agree on all eight verified effects. Original package SHA-256 is `d831c05d4b88da5690d86110aa93d5859b73f70f2ec968c648615bb9ba0d855e`; manifest SHA-256 is `504559e9dae11c07a6866225117257dec92b8ce4e534300ffcffdb1fd3aca423`. The protected publisher directly read back both feed payloads, which normalize to `sha256:bd001b8fe1509fd17112dc80b0d1f66ab040455c431d4f15baf4e532e9032cc5`; public NuGet signing changes the archive hash. Anonymous exact-version tool installation passed, and unconfigured `export-learning` returned invalid-configuration/exit 2 without creating state.

Main received this exact artifact packet in telemetry mailbox commit `3cdaec20`. Its reviewed SystemAdmin source is accepted; private operator authentication remains unavailable. Public release is complete, while installed custody, eligible capture, trusted acquisition, usage coverage and efficiency remain unknown. The selected prospective route is a separate fresh schema-12 store; legacy migration is an alternative qualification, not a completed adoption.

The integrator independently downloaded and hashed all three public release assets, verified the exact tag and terminal Authority journal, downloaded the signed public NuGet package and verified its normalized payload, and installed exact 0.2.0 anonymously. Direct integrator access to GitHub Packages returned HTTP 403; that provider observation is supplied by the protected publisher and its source-bound publication journal, rather than a second direct integrator download.
