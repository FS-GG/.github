# Telemetry host package and release boundary

## Prospective Host 0.9 source — Outcome4640, 2026-10-23

[PR4802](https://github.com/FS-GG/.github/pull/4802) taught the shared store to admit
passive `provider-usage-observation/1` facts with provenance
`claude-local-transcript/1`, each in its own native identity namespace
(`claude-transcript/1`), beside `codex-local-rollout/1`. Every other provenance is still
refused. Published Host0.8 was built from `74cbd6f0` and predates that change, so it
answers a Claude transcript batch with `invalid-request`. The Host takes the parser
through its Store and Coord.Core project references. It therefore needs a new
independent package rather than any Host logic change. The packaged closure also carries
other coherent113 source compiled through those references (the Claude transcript reader
in `SkillTelemetryReaders.fs` and `Coord.Core/Telemetry.fs`); none of it is reachable from
Host entry points. Root re-diffs the Host closure against `74cbd6f0` at the dispatch SHA.
The bump also re-pins `telemetry-host-release.py` in the read-only org observer
(`scripts/telemetry-host-published-org-read.py`), whose test now runs in the package
workflow so a stale pin fails at PR time.

This source prepares **0.9.0**, `telemetry-host/v0.9.0` and logical journal15 at
`state/releases/utel-host-rel-15`. The predecessor is the accepted original Host0.8
publication: [publisher39580298871](https://github.com/FS-GG/.github/actions/runs/39580298871)
with independent readonly [39591087147](https://github.com/FS-GG/.github/actions/runs/39591087147),
nonlatest release419394647 at `74cbd6f0b1b598d48556eb854c4ba6a3c3abda54`, release archive
`44273495…5aba89` and common normalized payload `f036808546…1802ee`, with journal14.
The read-first gate now requires the known0.8 predecessor and normalized0.9 absence.
The other gate, artifact, publisher and journal mechanics are unchanged from Host0.8.
These are prospective coordinates, not an availability claim. Root still verifies
tag/release occupancy, Authority journal15 destination/protection and exact accepted
source before any invocation. Publication needs its own fresh effect admission, and
the publisher stays default-false.

Store14 and the receipt ABI are unchanged. Since `74cbd6f0`, `TelemetryStore.fs`
differs only in the provenance-to-namespace match. `TelemetryStoreApplication`
(`currentSchemaVersion = 14`, DDL and passive query) is byte-identical, and the release
manifest still selects only schema14. Claude observations use the existing
`current_ingest_facts` rows and corrections path, so a Home Store14 workspace can move
from0.8 to0.9 without migration. The focused passive Host HTTP preflight now requires
five tests. The new test proves that the actual Host receipt endpoint applies a Claude
transcript observation and refuses an unknown provenance or a Codex-namespaced identity.
Home cutover, credential custody and installed acceptance remain separate.
Published0.8/journal14, 0.7/journal13 and held0.6/journal12 remain unchanged.

## Prospective Host 0.8 source — Delivery4707, 2026-10-21

[Delivery4707](https://github.com/FS-GG/.github/issues/4707) owns the first installed
facts-and-coverage browser journey. Its accepted source
[PR4685](https://github.com/FS-GG/.github/pull/4685) postdates the published Host0.7
source below: the independent Host package must advance to include the bounded
unassigned-usage projection and browser assets. Advancing the coordination coherent
set does not update this package.

This source prepares **0.8.0**, `telemetry-host/v0.8.0` and logical journal14 at
`state/releases/utel-host-rel-14`, with unchanged Store14 and receipt ABI. These
are prospective coordinates, not an availability claim. Root's native4707 ruling
permits this reversible source preparation despite the current org-feed REST and
maintained NuGet v3 reads returning HTTP403. Org-feed occupancy remains **Unknown**.
The later [root read-first decision](https://github.com/FS-GG/.github/issues/4707)
permits a separately admitted first-attempt candidate invocation to establish feed
absence in its mandatory first gate. That gate precedes SDK setup, tests, build,
pack and archive creation. Both authenticated/public indexes must demonstrate the
known0.7 predecessor and normalized0.8 absence; exact package reads must also return404.
Unreadable, redirected, oversized, occupied, contradictory or stale observations
stop the original invocation before candidate work. Failed reads grant no retry.
Root still verifies tag/release occupancy, Authority journal14 destination/protection
and exact accepted source before any invocation; repository-token Authority or
private draft visibility is not assumed. Source journal literals are not native
absence. Publication repeats fresh effect admission and remains separate. If a
coordinate exists, change the prospective source profile through review. No dispatch
or publication follows from this source decision; the publisher stays default-false.
Published0.7/journal13 and the held0.6/journal12 originals remain unchanged.

Package qualification now requires the actual installed Host to serve corrected
unassigned observations to its browser before shutdown and after isolated restore.
It verifies one current revision, exact nullable counters, partial population,
unknown attribution/cost and exclusion of private native identities. The existing
canonical-store checks keep these observations outside item totals. The candidate
also runs the maintained Dashboard JavaScript browser controls already required by
normal Host package CI. Synthetic package acceptance remains distinct from Home's
real receiver cutover, credential custody, durable receipt4705 and browser journey4707.

Pipeline preflight reuses the existing closed-coordinate, artifact, schema-profile
and fake-provider controls before costly builds. This bounded composition change
does not introduce a new effect machine, so no custom model is selected. Initial
composition/static review is capped at30minutes; required source, coherent-run,
authority, reproducible-package and installed-runtime checks remain applicable.
The read-first helper has seven fixed GETs at most, bounded response bodies and
request/deadline checks within a five-minute Actions step. Its focused preflight is
`python3 tests/telemetry-host-successor/run.py CandidateReadFirstTests EarlyQualificationTests`:
fake transport failures must prevent dependent work, and workflow mutation checks
reject moving, skipping or bypassing the gate. These controls do not read live feeds.
The release candidate must be a genuine first-attempt exact-main three-member
artifact; source qualification artifacts and earlier candidates cannot substitute.
Each later preflight/publication/adoption effect needs its own existing admission.

## Accepted Host 0.7 publication — 2026-10-18

Original [candidate 39147197464](https://github.com/FS-GG/.github/actions/runs/39147197464) and
[preflight 39148123795](https://github.com/FS-GG/.github/actions/runs/39148123795) qualified protected
source `2e60210be1e04d52fe0735a56333b3e3aef42284`. Original
[publisher 39148437514](https://github.com/FS-GG/.github/actions/runs/39148437514), attempt 1,
completed journal 13 at generation 17 with all 8 effects verified. Root independently accepted original
custody, native successful steps, exact release and feed correspondence. The promoted
[`telemetry-host/v0.7.0` release](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.7.0)
(ID 416586994) contains exactly the package, manifest and publication journal. It is nonlatest;
coherent-set/v0.106.0 remains latest. No coherent 107 or Creator 017 publication follows.

Candidate/release package SHA256 is `d59ecac7ef4d1cdc75cebdcb08624b73960309821def1cd83d2909dbda5de253`;
public NuGet signed archive SHA256 is `c24af6af3e35f67dc0eca1f0f7c82657a8c82adf3c62ea11a986b1deb6bb4460`.
Both actual hosted downloaded feeds share normalized producer payload
`sha256:f8ec077387cfbcefbae89f74781ba7bbcfce1ebc2e97f7c7dd43d7bdcddeda1b`.
Independent public NuGet and release downloads matched through the maintained reader; a separate
org-feed observer remained 403, so authenticated hosted evidence supplies that feed's proof.
Manifest content ID is `sha256:dcee8e039276d805d2190632e2adb783dcd17c7ee9ae3a8ba635790303d97c95`.
Archive signing differences are retained, not asserted byte equality across feeds.

Old 0.6 journal 12 remains generation 2/tag intent Unknown with original candidate and failed attempts
unchanged; publication 0.7 neither settles nor resends it. The temporary main reservation for this
completed original operation is released; all 439+46 historical holds remain. App post cleanup logged
revocation; independent credential invalidity and feed-key expiry remain unproved. Accepted programme binding 4/schema 2/inspection 2 remains. A later Root read observed the global
CLI selector targeting 0.106, but its pre-entry value and change timing are Unknown; a pointer is not
whole runtime or instruction-discovery qualification. Home endpoint/trust, installation, enrollment,
restart/restore and receiver activation
remain separate acceptance. No local observation queues or Home state were changed by publication.

## Delivered Host 0.7 source profile — 2026-10-18

Independently versioned Host **0.7.0** uses the current Store14 implementation
and unchanged authenticated passive-receipt ABI. It is a new source, package, candidate and
release profile, with `telemetry-host/v0.7.0` and logical journal13 at
`state/releases/utel-host-rel-13`. It requires a newly built genuine candidate on the exact
current publisher source. It cannot consume or relabel the retained0.6 candidate. The current
publisher explicitly refuses the retired `continue_failed_original` flag and source inequality.

The [Host project](../../src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj),
[closed candidate verifier](../../scripts/telemetry-host-successor-artifact.py),
[effect identities](../../scripts/telemetry_host_successor_execution.py) and
[publisher](../../scripts/telemetry-host-successor-publish.py) bind the new coordinates together.
Passive observations retain authenticated owner checks, corrections, partial population and
canonical-only attribution; qualified item totals exclude them. The generic historical manifest
reader retains0.4 schema10..12 and0.5/0.6 schema14 profiles; the current candidate/publisher accepts0.7 only.

Before genesis and each effect, the existing native predicate checks Authority main-writer,
integrity and retired journal fencing. The selected directory is classified through bounded
canonical Git trees; inaccessible, malformed, missing-parent and changed-main states cannot mean
fresh absence. Existing protected-main nonforced compare/exchange preserves unrelated directories,
including old journal12. Native caller admission requires the current main source, first attempt,
expected operator and an in-progress dispatch; a terminal old caller cannot authorize future effects.
The unchanged workflow concurrency group serializes this route. These gates do not claim zero other writers.

The eight ordered effects remain tag, draft release, two feeds, three release assets and promotion.
Promotion keeps `make_latest=false` and touches no coherent CLI channel. A late0.6 tag cannot satisfy
0.7 coordinates or restart the terminal old caller's later stages. An uncertain0.7 send retains its
intent and polls readback without resend. A new coordinate does not reconcile an earlier Unknown.
Future native admission must freshly qualify source/currentness, competing executions, current
protection/writer compatibility, absence of both journal locations and all tag/release/feed destinations,
and its actual operating window. Source observations do not reserve destinations or prove write success.

Dispatch failure reporting now reuses the common closed safe projection for status, request ID,
rate metadata and allowlisted response classes. It excludes raw HTTP bodies, arguments and credentials,
clears stale observation diagnostics and emits each captured dispatch diagnostic once. Reporting failure
cannot change the effect outcome. This improves future diagnostics; it cannot recover an earlier cause.

The existing pure runner exercises the actual current publisher with inert providers and a disposable
Git Authority: new journal13/eight effects, old12 byte preservation, terminal-caller refusal, old candidate
and continuation refusal, late old-tag irrelevance, policy drift and Unknown polling with one send.
Secret and malformed-diagnostic controls exercise the actual provider and caller. Existing early HTTP,
schema/native-output assertions and full package/installed-browser qualification remain required with
unchanged job caps. Static/caller preflight reuses those controls; no workflow, permission, clock budget
or custom model is added. Host0.7 full hosted/package and original candidate/publication gates subsequently passed as recorded above; receiver adoption remains pending.

### Retained0.6 evidence

[Source PR4675](https://github.com/FS-GG/.github/pull/4675) delivered Store14 passive ingestion;
its full hosted/installed-browser qualification and genuine [candidate39135954294](https://github.com/FS-GG/.github/actions/runs/39135954294)
passed. [Publisher39137010941](https://github.com/FS-GG/.github/actions/runs/39137010941)
failed HTTP422 creating a retired legacy ref. Its precise response body and created immutable-object
identities remain Unknown. [Corrective source PR4676](https://github.com/FS-GG/.github/pull/4676)
selected the existing protected-main route while retaining that exact candidate.

Corrected [preflight39139925555](https://github.com/FS-GG/.github/actions/runs/39139925555) passed.
The separately admitted [publisher39140168695](https://github.com/FS-GG/.github/actions/runs/39140168695)
then exhausted136 waiting transitions. Journal12 retains generation2/tag intent and the original0.6
source/content/archive tuple. The underlying tag HTTP cause was not retained; a workflow-permission
explanation is an inference, not a recovered response. Other seven stages were not reached by source
ordering. Current404 visibility is not a no-send or zero-effects proof. No retry, reset or new tag
transport follows from0.7 preparation. App cleanup logged revocation; independent invalidity and NuGet
key revocation/expiry remain unproved. Original state, candidate and package bytes remain unchanged.

Published [Host0.5.0](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.5.0) and all historical
artifacts/journals remain immutable. Coherent published CLI0.106 is accepted; actual selected-caller/runtime and installed discovery proof remain separately scoped.
No service installation, TLS/trust change, enrollment, native provider capture or Home durability follows
from this source. Home owns final endpoint/reference, service custody and receiver adoption. Existing
TLS/bearer capability does not establish readiness. Historical sections below retain their original scopes.

`FS.GG.Telemetry.Host` is an optional, independently versioned .NET tool. The
next candidate is `0.2.1`, carrying the protected native capture provenance
hardening merged after the immutable `0.2.0` release. Published `0.2.0` remains
immutable; the successor host's failed 0.1.3→0.1.5 attempt remains held with
its journal and backup. Its command is `fsgg-telemetry-host`, and
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

The next successor Host candidate is `0.2.1`. Its candidate workflow, archive
verifier, publisher and effect identities bind that version and
`telemetry-host/v0.2.1` together. Its release journal uses the unused
`fsgg/v2/journal/release/utel-host-rel-08` ref. The 0.2.0 release journal
remains at `fsgg/v2/journal/release/utel-host-rel-07`. The default publisher dispatch
remains a no-effect preflight. A candidate run and protected publication must
qualify the exact merged source before Main updates the local successor Host.
The installed successor Host remains on its last qualified schema-10 release
until a separately qualified update. Host 0.2.1 declares release schema range
10 through 12 and migrates an accepted schema-10 store through migrations 11
and 12. The existing same-schema updater must therefore refuse an unchanged
in-place update. Main must use a stopped backup and qualified migration path,
or create separate fresh schema-12 custody for the prospective native collector.
The historical old-Host migration is separate.

The historical `release-telemetry-host.yml` publisher is sealed under GS2-08.9.
Its current manual workflow qualifies source and package locally and has no
credential, tag, feed, journal, artifact-upload or release effect. Do not use
its old publication instructions for a new Host version.

For Host 0.2.1, the independent
`release-telemetry-host-successor-candidate.yml` qualifies an exact current
`main` commit, the package, installed tool, manifest, and Host/browser tests.
It retains one unpromoted Actions artifact and has read-only repository and
package permissions. `release-telemetry-host-successor-publish.yml` separately
authenticates that candidate's first-attempt run and archive digest, then
uses a protected release journal to admit and read back each tag, feed,
release-asset and promotion effect. Its default dispatch is read-only
preflight. A fresh publication requires the candidate source to equal current
`main`; recovery from a newer publisher must reuse the same retained archive
and protected intent. The existing nuget.org Trusted Publishing policy must
remain bound to this workflow and `release-successor` environment before
`publish=true` can obtain a feed credential.

The 0.2.1 publication release contains exactly the original
`FS.GG.Telemetry.Host.0.2.1.nupkg`, `manifest.json`, and
`publication-journal.json` required by Main's protected updater. The journal
records independently observed external archive and normalized payload
hashes for both feeds. Main adopts only after the release is promoted and
those three assets are verified.

Host 0.1.7 was published from source
`5ebaab8f49b3c7fe00b1dce355338704a3a98a70` on 23 September 2026. It predates
the protected native capture and same-host export merge
`76ae9d3ee11dd5dd7624a01b6fac93c80e6ecf55`, so its immutable package cannot
provide those commands and must not be replaced in place. Its runtime and
release manifest are schema 10. Host 0.2.0's schema-10 migration retains
existing facts and their durable order; provenance that schema 10 never stored
remains unknown rather than being invented.

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
