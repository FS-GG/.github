# Telemetry host package and release boundary

`FS.GG.Telemetry.Host` is an optional, independently versioned .NET tool. Its
current source version is `0.1.2`, its command is `fsgg-telemetry-host`, and
its tag namespace is `telemetry-host/v*`. It is not a fourth member of the
`FS.GG.Kit`/`FS.GG.Drivers`/`FS.GG.Coord.Cli` coherent release set.

The supported production profile is Linux x64 with the .NET 10 and ASP.NET Core
10 runtimes. The standard package remains installable with `dotnet tool install`
when an SDK is present. A runtime-only operator installation may extract the
verified standard tool payload and invoke `FS.GG.Telemetry.Host.dll` with
`dotnet`; it must first validate the release manifest, the exact
`DotnetToolSettings.xml` command and entry point, archive paths, and immutable
release placement.

The package contains the Host executable, the Dashboard projection and embedded
fixed assets, the Store and Contracts assemblies, and their ordinary runtime
dependency closure. The package gate rejects source, tests, fixture data,
configuration, and secrets. Browser keys, producer credentials, TLS private
material, state, receipts, backups, and Main-specific configuration never enter
the package.

## Manifest

The release workflow packs once and emits
`fsgg.telemetry.host-release/1`. It binds the exact source commit, package ID,
version and tag; prepared archive SHA-256; producer payload SHA-256; framework
and Linux x64 target; dependency lock and UI asset-tree digests; store schema 9;
runtime prerequisites; and creation time.

`producerPayloadSha256` is calculated by the established
`scripts/release-saga.py` `payload()` and `payload_id()` functions. Those
functions bind every producer entry while excluding a registry-added signature
and package-service core properties and normalizing the relationship entry that
points at those core properties. Their canonical JSON has sorted keys, compact
separators, UTF-8, and no trailing newline. The shared fixed vector is
`tests/telemetry-host-release/payload-vector.json`.

External archive hashes belong in the separate publication journal because a
registry may sign an archive. The workflow requires equal producer payloads and
records each feed's actual archive SHA-256; it does not claim downloaded archive
bytes are equal when signing changes them.

## Protected release

Source acceptance does not publish the package. An operator runs the dedicated
`release-telemetry-host.yml` workflow with an exact commit already contained by
`main`, the source's exact independent Host version, and the explicit
confirmation phrase. The workflow
restores locked dependencies, runs the host, projection, browser, package, and
release fixtures, and prepares one package and manifest.

The nuget.org OIDC login happens before either feed push. If the new package ID
and workflow are not admitted by Trusted Publishing, the run stops without a
feed effect. After authority succeeds, the same prepared package is published
to GitHub Packages first and nuget.org second. Existing versions are accepted
only after their producer payload verifies against the prepared manifest. The
immutable tag and GitHub release are created only after both feeds are observed
and their external hashes are journaled.

Host 0.1.1 was published from source
`431d69d38d71da3b2c293bee8cc05448795ea38f` by
[run 34446544380](https://github.com/FS-GG/.github/actions/runs/34446544380)
and is available as the immutable
[`telemetry-host/v0.1.1`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.1.1)
release. Its prepared archive SHA-256 is
`da004f32f2293539d043e03ac86e33293b57ab1dddd1a258e2b7401fc066acb7`,
its manifest SHA-256 is
`940c9c5572b32da1747d1ba06bce147c38cafbf8bd1bdddc0a0826a467e5c5e2`,
and its normalized producer payload is
`sha256:2bf8d0c3d0be3df545be66ff7ba67eb98ad5d2df157cff5417ceb2176d36dc2a`.
The nuget.org-signed outer archive has the distinct SHA-256
`92357f9457ba9ce3f9beedb7eda8256d9c7cee419b6363cabc6244aaa7fc577e`.

The registry's `telemetry-host` row records the independent source surface.
The immutable release manifest and release tag carry package publication
identity; the Host remains outside the registry's three-member GitHub coherent
set. Main installation, storage qualification, private identity provisioning,
listener activation, backup/restore, restart and rollback remain separate
SystemAdmin and H3 acceptance facts.

## Backup boundary

`fsgg-telemetry-host backup` creates a new data-only backup set atomically. Its
closed root manifest binds the supported store schema range, logical workspace
set, each workspace manifest, and a secret-free logical configuration digest.
Each workspace manifest binds the SQLite backup and every pending receipt file.
The command does not copy configuration, TLS keys, producer secrets, browser
access keys, or browser key-hash files.

Recovering after configuration loss therefore depends on the companion
[SystemAdmin procedure](https://github.com/EHotwagner/SystemAdmin/pull/6). Before activation, the operator must place the canonical
config template and all referenced private files in a separately selected,
encrypted or equivalently access-controlled backup, and verify its digest and
permissions. Restore first recovers or reprovisions those files, renders a
reviewed config whose store roots are `<fresh-state-root>/<workspaceId>`, runs
the product restore into that non-existing root, and runs preflight before any
cutover. The logical digest excludes machine-specific store paths so this fresh
root is possible, while retaining workspace enrollment, producer scope,
browser authorization, session policy, and listener identity. A browser access
key cannot be recovered from its hash and must be retained separately or
reprovisioned and distributed through the selected private channel.

## Historical continuity boundary

Host 0.1.2 adds an offline, read-only historical export. It accepts a schema 8
or 9 source and a schema 9 target, establishes one SQLite read transaction per
store, validates the engine-owned canonical fact rows, and writes deterministic
payloads within `TelemetryStore.MaxEvents`, `MaxEventBytes`, and
`MaxBatchBytes`. There is no cross-database atomic snapshot claim.

The export does not modify, copy, restore, or replace either store and does not
read configuration or credentials. Its payloads are imported through the
existing authenticated receipt endpoint. Existing receipt application owns
replay, correction, and terminal `semantic-conflict` behavior if the target
moves after preview. Operators retain the source store and pre-import Host
backup and verify a final zero-payload preview; the resulting continuity claim
covers current canonical facts, not superseded correction history or original
batch grouping.
