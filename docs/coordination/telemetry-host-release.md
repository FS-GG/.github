# Telemetry host package and release boundary

`FS.GG.Telemetry.Host` is an optional, independently versioned .NET tool. Its
current released version is `0.1.2`, its command is `fsgg-telemetry-host`, and
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

The historical `release-telemetry-host.yml` publisher is sealed under GS2-08.9.
Its current manual workflow qualifies source and package locally and has no
credential, tag, feed, journal, artifact-upload or release effect. Do not use
its old publication instructions for a new Host version.

For Host 0.1.2, the independent
`release-telemetry-host-successor-candidate.yml` qualifies an exact current
`main` commit, the package, installed tool, manifest, and Host/browser tests.
It retains one unpromoted Actions artifact and has read-only repository and
package permissions. `release-telemetry-host-successor-publish.yml` separately
authenticates that candidate's first-attempt run and archive digest, then
uses a protected release journal to admit and read back each tag, feed,
release-asset and promotion effect. Its default dispatch is read-only
preflight. A fresh publication requires the candidate source to equal current
`main`; recovery from a newer publisher must reuse the same retained archive
and protected intent. The new workflow needs its own nuget.org Trusted
Publishing registration before `publish=true` can obtain a feed credential.

The publication release must contain exactly the original
`FS.GG.Telemetry.Host.0.1.2.nupkg`, `manifest.json`, and
`publication-journal.json` required by Main's protected updater. The journal
records independently observed external archive and normalized payload
hashes for both feeds. Main adopts only after the release is promoted and
those three assets are verified.

Host 0.1.2 was published from source `88ab88c3240a5e3075129b087a95b691088eaf5d` by [run 35479412309](https://github.com/FS-GG/.github/actions/runs/35479412309) as the immutable [`telemetry-host/v0.1.2`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.1.2) release. The original package archive SHA-256 is `52cf4c895a90bc84918f95bce8fd0c1585bc289cf815c4e7645d96bada26d2d0`, its manifest content ID is `sha256:99f63a1bb24835b53e99d6baa6c26d3bf64cda94923190eb3cce4f617f0781b5`, and both feeds expose normalized producer payload `sha256:4cef79134fd0a074f14a591c02ccf6589e604464ade9cfdd651895f38656f263`. All eight protected journal effects, including promotion, are verified. Protected Host installation and receipt-triggered completion re-projection are runtime steps owned by Main.

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
