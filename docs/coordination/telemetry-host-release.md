# Telemetry host package and release boundary

`FS.GG.Telemetry.Host` is an optional, independently versioned .NET tool. The
next candidate is `0.1.6`, carrying the private item timeline projection.
Published `0.1.5` remains immutable; the successor host's failed 0.1.3→0.1.5
attempt remains held with its journal and backup. Its command is `fsgg-telemetry-host`, and
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
and Linux x64 target; dependency lock and UI asset-tree digests; store schema range;
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

The next successor Host candidate is `0.1.6`. Its candidate workflow, archive
verifier, publisher and effect identities bind that version and
`telemetry-host/v0.1.6` together. Its release journal uses the unused
`fsgg/v2/journal/release/utel-host-rel-05` ref. The default publisher dispatch
remains a no-effect preflight. A candidate run and protected publication must
qualify the exact merged source before Main updates the local successor Host.
The installed successor Host remains healthy at 0.1.3/schema 10 until a
separately qualified same-schema update. The historical old-Host migration is separate.

The historical `release-telemetry-host.yml` publisher is sealed under GS2-08.9.
Its current manual workflow qualifies source and package locally and has no
credential, tag, feed, journal, artifact-upload or release effect. Do not use
its old publication instructions for a new Host version.

For Host 0.1.3, the independent
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

The 0.1.3 publication release contains exactly the original
`FS.GG.Telemetry.Host.0.1.3.nupkg`, `manifest.json`, and
`publication-journal.json` required by Main's protected updater. The journal
records independently observed external archive and normalized payload
hashes for both feeds. Main adopts only after the release is promoted and
those three assets are verified.

Host 0.1.3 changes the store schema from 9 to 10 so
`orchestration-delivery` outcomes can be retained alongside existing routine
outcomes. The installed updater refuses a schema-range change. Main must
rehearse and perform a stopped backup and migration for both private Host
scopes, then verify retained receipts and the new schema before activation.
Its protected release journal is `fsgg/v2/journal/release/utel-host-rel-02`;
the 0.1.2 journal remains immutable.

The public dashboard stage uses a separate, closed `fsgg.telemetry.host-config/2`
document with `schema`, `storeRoots`, and `engine` in that order. `storeRoots`
contains the fsharp-dev and orchestration private store roots, each at schema
10. The stage rejects duplicate item IDs and multiple open budget epochs,
projects one public snapshot, and keeps the existing public revision until
both scopes pass. The approved labels file must map a genuinely delivered
orchestration item to a unique public key; without that alias, the item remains
unpublished. Main pins both the new CLI and the dashboard script digest before
enabling the two-scope stage.

The optional `fsgg.telemetry.dashboard-labels/2` file adds a private `members`
map to the existing `items`, `models`, `efforts`, and `scopes` maps. Each member
selector is a canonical private item ID; its value contains only an approved
public `key`, `label`, and FS-GG issue or PR `url`. Version 1 labels remain
valid and produce no subitem pipeline. A version 2 file projects only approved
canonical members from the same engine-owned item-detail snapshot, reporting
unmapped members as coverage rather than exposing their IDs. Stage, CI class,
same-clock invocation time, native tokens, and attribution remain unknown when
their respective evidence is incomplete. Display order follows observed starts;
it does not establish a dependency or parent edge. A changed dashboard script
or labels file requires a new digest-bound publisher activation and Main's
protected cutover before any live subitem claim.

Replacing an already active handoff publisher requires a fresh publisher state
directory and a root-owned `fsgg.telemetry.publisher-cutover-proof/3`. The proof
binds the new config, script, labels, coordinator and unit digests; the current
remote commit, canonical snapshot digest and public revision; and the stopped
incumbent's script, activation and preserved-state digests. Its incumbent timer
and service are checked by the system manager (`managerIdentity=system`) while
`accountUid` names their configured service account. The timer must be disabled
and inactive, the service inactive, and any pending intent
reconciled or absent. `evidenceDigest` is SHA-256 of canonical JSON without that
field. The operator records activation within five minutes of `observedAt`; the
new script rereads the root-owned proof and verifies the exact remote baseline
at activation and again before its first intent. Existing publisher state and
proof remain untouched. The SystemAdmin root coordinator owns the systemd
single-writer check, proof generation, state preservation and rollback.

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
