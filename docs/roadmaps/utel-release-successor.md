# UTEL-REL-01 — Coherent telemetry producer after the GS2-08.9 seal

Owner: `FS-GG/.github` release and telemetry producer. Main/SystemAdmin owns installed-host adoption. This item is a separate, additive publication route; it does not reopen the accepted GS2-08.9 legacy routes or authorize `OpenV2`.

## Trigger and outcome

The native subagent usage and durable remote receipt fixes are merged at source, but the installed host still selects the 0.90.0 coherent set. GS2-08.9 deliberately sealed the old release scripts and disabled five write-capable workflows because their shell publication boundaries do not compose common effect admission. A source build or a package with a new version is not an installed release. Deliver a reviewed successor that publishes one immutable coherent set of `FS.GG.Coord.Cli`, `FS.GG.Kit` and `FS.GG.Drivers` from one source, then allows Main to adopt its verified promoted manifest.

The candidate version is **0.91.0** (minor: new native usage and receipt-settlement behavior). A [read-only release-coherence run](https://github.com/FS-GG/.github/actions/runs/35447314051) on 2026-09-19 observed 0.90.0 as the latest version of all three packages on both the organization feed and nuget.org; no 0.91.0 release tag existed. Recheck this uniqueness immediately before any publication effect. The local token cannot list organization package versions (HTTP 403), so the workflow's scoped `packages: read` credential is the usable feed witness. The source candidate advances the scalar while the five preserved 0.90-bound workflows remain byte-identical and disabled. The retired `kit-auto-publish` manual workflow still explicitly refuses successor versions. The independent bridge receiver verifier continues to check the immutable accepted 0.90 source/receiver identity; it is not a 0.91 candidate gate. No tag, package-feed write, journal mutation or workflow activation follows from a source candidate.

## Release authority and implementation contract

1. **Inventory and preserve the seal.** Keep `release-saga-start`, `release-saga-prepare`, `release-coord-engine`, `release-drivers` and `release-kit` disabled. Keep `release-saga-ci.sh` and `release-saga-promote-release.sh` refusing effects with exit 78. Bind the accepted GS2-08.9 disposition and enumerate current, historical tag/SHA, reusable-workflow, credential, App, OIDC and package-feed entry points. A new route must have a distinct entry point and must not rely on editing the five 0.90-bound workflow identities.
2. **Qualify authority at every effect.** Define one release intent with source SHA, version, three package IDs, exact candidate archive digests, destinations, operation identity and authorized caller. Compose the common effect admission immediately before tag/release creation, authoritative release-asset or journal mutation, GitHub Packages write, nuget.org write, and promotion. A CI artifact retained during read-only candidate qualification is not an admitted release asset: the later writer must independently bind its Actions run identity and artifact digest to the manifest before any effect. Admission must bind the intent and current authority, reject stale or historical source selection, and fail closed on missing, changed or ambiguous evidence. Do not treat a successful check at preparation as authority for a later effect. For this one 0.91.0 cut, the admitted authority is the exact main-branch `release-successor-publish.yml` run by the sole operator `EHotwagner`, reread from the native Actions API and current main ref before each journal or provider effect. Its state is an independent protected release journal under `refs/heads/fsgg/v2/journal/release/utel-rel-01`, written only by the verified ordinary ledger App. This replaces the unavailable v1 operation journal for this scoped release; it does not initialize that v1 journal or broaden ordinary-v2 admission.
3. **Build and prove the candidate.** Advance the coherent scalar and bounded release notes while preserving the five historical workflow hashes. The seal test must continue to prove refusal for both 0.90 and 0.91 effects; the historical receiver verifier remains bound to the accepted 0.90 source. Pack the three packages once from one exact source and preserve exact archives. Require source and payload manifests, package/version uniqueness on both feeds, independent archive and normalized-payload comparison, and an installed CLI test for native child usage, correction, retry and durable receipt settlement. A prior partial publication is resumed from the same exact archives; never repack the same version with different bytes.
4. **Prove recovery against fake providers.** Exercise denied admission, revoked authority, old tag/SHA replay, duplicate dispatch, interruption before and after each remote write, delayed indexing, one-feed partial publication, mismatched archive, journal conflict and promotion retry. The test must show zero provider mutations for denied cases and exactly-once effective publication of byte-identical artifacts for accepted replay. Preserve a truthful incomplete state until both feeds and all three packages pass public readback.
5. **Review and activate the new route.** Review the source, fake-provider evidence, credential scope and administrative dispositions. Activate only the qualified successor entry point through the owning repository/organization administration route; leave the five retired workflows disabled. The first live run must bind a fresh source SHA and version, verify exact feed bytes, create an immutable promoted manifest and receipt, then perform clean public-feed installation/readback. Main may update the side-by-side install only from that promoted manifest and can then qualify the prospective native subagent usage and five-minute drain. Host adoption is a distinct final verification, not a consequence of publication.

## Release result and remaining host boundary

The successor release [coherent-set/v0.91.0](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.91.0) is public and immutable. The [candidate run #35456335400](https://github.com/FS-GG/.github/actions/runs/35456335400) packed all three packages from source `85c356b2c44cfea4b83659b14b6946d64bbd9a22` with content ID `sha256:c8b03938aa6357f696ff9ca257315e02f79e0a23ef8b6d87d37430139f2e600c`. The [publisher recovery run #35457078213](https://github.com/FS-GG/.github/actions/runs/35457078213) completed successfully; the protected release journal marks all 16 effects verified, including both feeds, release assets and promotion. The release manifest records both feeds verified for each package and carries the promotion receipt. Independent public readback verified the three release archives against the candidate SHA-256 values, matched all three nuget.org normalized payloads, and installed CLI 0.91.0 from nuget.org in a clean tool directory.

Main/SystemAdmin owns installed-host adoption. Its updater may select 0.91.0 from the promoted manifest, then verify the installed CLI and five-minute drain capability. Publication does not establish that the running containers have adopted this version or that their local telemetry spools have moved to persistent storage. Keep private host telemetry data and credentials out of Git.

## Implementation and live authority boundary

The first implementation slice, [PR #3570](https://github.com/FS-GG/.github/pull/3570), adds `release-successor-candidate.yml`. It has only read permissions and retains the three archives produced by one pack run together with the runtime qualification of the *same* CLI archive and the source-bound manifest. It cannot publish or promote. This resolves the prior local-versus-CI hash mismatch without treating either unbound archive as a release. `release-saga.py` now checks the qualification file and all three package source commits again on manifest readback.

`scripts/release_successor_execution.py` is the effect-order/recovery policy. Its fake-provider controls require a fresh admission for journal intent, remote dispatch and settlement; a response alone does not mark an effect verified, and a 404 after an uncertain write does not authorize blind replay. The scoped publisher supplies a protected Git commit/ref journal, non-forced compare-and-swap, native Actions and main-head admission rereads, and provider-specific package and release readback. Journal genesis itself requires the admitted exact candidate and verifies the protected ref was previously absent. The writer must fail closed on unreadable or conflicting evidence. The ambient `GITHUB_TOKEN` is a provider credential, not the admission source; the ordinary App has only the authority-repository contents scope. The five retired workflows remain sealed.

`scripts/release-successor-artifact.py` is an offline guard for an artifact, run and archive supplied by a trusted GitHub API reader. It verifies the successful first-attempt exact-main candidate run, repository and artifact identity, the outer artifact SHA-256, exact safe members, the source-bound release manifest, all package archive digests, runtime qualification and stable predecessor before extraction. Reruns are refused because artifact metadata does not bind a run attempt. Its JSON arguments are evidence inputs, not authenticated API responses by themselves; a live writer must fetch them through its own scoped GitHub API client and recheck the current main SHA and version uniqueness before the first effect.

Live observations on 2026-09-19 found the operating-v1 cutover genesis in `FS-GG/FS.GG.Coordination.Authority`, but the canonical `fleet-v1-admission:fs-gg-production` operation journal ref `refs/heads/fsgg/v2/journal/operation/79` returned HTTP 404. The imported registry deliberately has no public initializer, so this scoped release cannot use that operation without a separate authority migration. The `.github` `release-successor` environment is main-only with no required reviewers, matching the user's single-operator decision. The ordinary ledger App `4882140` has active FS-GG installation `160261608`; [read-only proof run #35454913240](https://github.com/FS-GG/.github/actions/runs/35454913240) verified its key, contents permission and a token scoped to the protected authority repository. The nuget.org owner registered Trusted Publishing for the distinct `release-successor-publish.yml` filename, that environment and exactly the three package IDs, with push-only-new-versions scope. These are credential prerequisites, not publication evidence.

The distinct publisher filename is `release-successor-publish.yml`. Its default dispatch is read-only preflight. Permit `publish=true` only after the production adapter and recovery proof, live preflight on final main SHA and exact retained candidate, both-feed uniqueness, and protected-journal readback pass. A fresh journal requires the candidate source to equal the current publisher SHA. After a partial effect, a repaired publisher may run from a newer main descendant only if the protected journal still binds the exact earlier candidate archive and content ID; it must publish those preserved bytes, not repack. A partial or ambiguous write remains pending for readback and never triggers blind retry. The five GS2-08.9 workflows and sealed scripts remain untouched.

## UTEL-REL-04 — 0.91.3 schema-10 successor

The next coherent cut uses the same active successor candidate and publisher
entry points with version `0.91.3`, predecessor `0.91.2` (promoted from
`fc64f351a6c7733c3df6520ad1e1671bfcc1714b`), and a fresh protected
release journal `fsgg/v2/journal/release/utel-rel-04`. The historical
`utel-rel-03` journal remains immutable. Candidate qualification still has no
provider write capability; the publisher performs a separate exact-source
preflight before any effect. The five GS2-08.9 workflows remain disabled.

## UTEL-REL-10 — 0.95.0 Board inspection successor

The selected coherent cut uses the existing `release-successor-candidate.yml` and
`release-successor-publish.yml` entry points with literal version `0.95.0`, predecessor
`0.94.0` from source `337b6a1d53571b07ca8e1417e18e52546ad319a7`, and fresh protected
journal `refs/heads/fsgg/v2/journal/release/utel-rel-10`. The published 0.94 release,
its manifest and completed `utel-rel-09` journal remain historical authority.

Publication is complete: candidate `37070544673`, no-effect preflight `37071412283` and publisher
`37071597534` succeeded at protected `3b5de3367f8647cea0cd9366b8cb11eddf53feae`.
The public coherent release was promoted on 2026-10-02 at 22:44:33 UTC. Root independently
authenticated all seven release assets, source tag, exact candidate descriptor and all 33 canonical
journal generations; head `78eff3ca803517859fa2968fe071d4ceb1fbe1b2` records all 16 effects verified.
All three public signed archives match their original normalized producer payloads; the native
publisher also verified original org-feed archives. Fresh installed adoption and Board qualification
remain pending. The following requirements retain the exact publication/recovery contract.
The candidate must come from the final reviewed repair merge on current main, pack
each coherent member once, and retain the source-bound three archives, standalone qualification
and predecessor receipt. The separate publisher preflight must authenticate that exact
first-attempt candidate on the same main SHA and confirm the unused target before journal
initialization. The existing sole operator, environment, credentials, admission rereads,
16-effect ordering, protected journal and recovery rules apply unchanged.

This route creates only `coherent-set/v0.95.0`; component tags are collision checks and must
not be created to start the retired publishers. The GS2-08.9 legacy routes stay sealed.
Publication requires all 16 effects to be verified through native readback. Fresh public
installation and Board receiver qualification are separate acceptance boundaries; the version
selection alone does not import issues, activate schedulers or complete Board adoption.

## UTEL-REL-11 — 0.96.0 telemetry selection and diagnostic successor

The next source cut selects the existing `release-successor-candidate.yml` and
`release-successor-publish.yml` entry points with literal version `0.96.0`, promoted
predecessor `0.95.0` from `3b5de3367f8647cea0cd9366b8cb11eddf53feae`, and fresh
protected journal `refs/heads/fsgg/v2/journal/release/utel-rel-11`. The completed
0.95.0 release, seven release assets and `utel-rel-10` journal retain their historical
authority. This source selection creates no journal, tag, feed write or installed adoption.

The cut carries the merged CI legacy-default selection repair and first-100 warm
Store diagnostics. The earlier native candidate refused the unchanged 100 ms p95
budget at 119.996 ms; its latency cause remains unknown. Five warmup submit/drain
pairs, all 100 measured admissions, complete arrays, diagnostic hook cost and all
qualification limits remain unchanged. An exact-source native pass is required;
CPU or GC overlap alone does not establish cause or waive a refusal.

After the final source is qualified and merged, hold current main stable through
the successful first-attempt candidate, authenticated seven-file retained archive,
separate no-effect publisher preflight and first publication. Candidate preparation
reads the exact promoted 0.95.0 channel and verifies all six package/feed coordinates
have that predecessor and an unused 0.96.0 target. The publisher repeats current
authority, original-byte, collision and fresh-journal checks before effects.

The existing sole operator, environment, credential identities, admission rereads,
sixteen-effect order, protected journal and forward recovery rules apply unchanged.
Create only `coherent-set/v0.96.0`; preserve all five GS2-08.9 sealed workflows.
Unknown or conflicting effects remain incomplete. After any partial effect, recover
from the original candidate archives and journal without repacking or resetting.
Publication requires native verification of every effect and both feeds; Home
adoption, consumer pins and default configuration remain separate decisions.

## UTEL-REL-13 — 0.97.1 telemetry identity repair

The selected coherent source cut is **0.97.1**, a compatible patch carrying the
merged equal feature/item fact identity repair (PR #4242) and native agent binding
and follow-up guidance (PR #4243). CLI, Kit and Drivers advance together. The
promoted predecessor is **0.97.0**, source
`2ab0c0ff9f37bdec9a11ba3604ebdc230711934f`, content
`sha256:b78fd7bdff1251e32c07ffb181c31b8c2009e3174cfe305b0761f5ca8b4e245d`.
Its release and completed `board-v2-product-coherent-097` journal remain immutable.
The fresh journal is `refs/heads/fsgg/v2/journal/release/utel-rel-13`.

Use the existing candidate and publisher entry points, exact-main first-attempt
candidate, retained seven-file archive, separate no-effect preflight, sole operator,
environment and sixteen-effect admission/recovery contract. Hold final main stable
through candidate qualification, preflight and first publication. Recheck both-feed
uniqueness and journal absence immediately before any first effect. Source selection
creates no journal, tag or publication. Preserve all five sealed legacy workflows.

After native readback establishes both feeds and immutable promotion, adopt only
the promoted manifest side by side and qualify the installed equal-identity begin,
native agent-name binding, terminal drain, retry and follow-up baseline. This does
not replay the already completed original root recovery. Missing parent-thread
relationships and the resumed 0.97.0 duplicate-identity begin refusal retain unknown
usage; never invent a parent token or infer whole-programme coverage from the repair.

### Accepted 0.97.1 release and selected local adoption — 2026-10-05

[PR #4245](https://github.com/FS-GG/.github/pull/4245) delivered source
`99ea75286f5c3cea2a261fef4e5b45cd70378185`. The first-attempt
[candidate 37342478014](https://github.com/FS-GG/.github/actions/runs/37342478014),
[no-effect preflight 37343711905](https://github.com/FS-GG/.github/actions/runs/37343711905)
and [publisher 37344008752](https://github.com/FS-GG/.github/actions/runs/37344008752)
passed. [Coherent set 0.97.1](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.97.1)
is promoted with content ID
`sha256:02dfc44d64e1b807fe20591fc2e0fdb9d02f28435a8b027fddd8ff5631e22714`;
protected `utel-rel-13` generation 33 verifies all 16 effects. The hosted publisher
verified all three packages on both feeds. Root independently matched the nuget.org
payloads and release archives; its direct GitHub Packages reads returned HTTP 403.
That access limitation is retained separately from the hosted verification and does
not establish a byte mismatch. The predecessor and sealed legacy routes are unchanged.

Ordinary user-level side-by-side adoption selected the promoted CLI 0.97.1 and passed
version and existing store-only configuration checks. The first adoption attempt
failed before installation because its cleared environment lacked the existing GitHub
authentication; the retained successor passed after memory-only credential transport.
No Main service or remote Host installation is established. A useful native child
independently matched all 84 extracted installed payload members. The explanation for
the generated NuGet SHA-512 sidecar remains unknown, separately from that payload match.

The fresh prospective local cycle passed equal feature/item root begin, byte-identical
retry, actual short-name child binding, truthful terminal reconciliation, genuine
same-owner follow-up and final local drain. Two distinct source-bound native turn
usage facts were joined; the captured follow-up baseline excluded the prior child
turn. All nine serial stages exited naturally with clean owned retirement within the
original deadline, and final local pending batches were zero. This accepts the selected
installed repair and local collection route, without replaying earlier completed attempts.

The [coverage reference](../reference/local-telemetry-store.md#native-collaboration-observations),
corrected in [PR #4249](https://github.com/FS-GG/.github/pull/4249), distinguishes verified
joined turn facts from the conservative unknown response label. Root usage remains
unsupported; historical and whole-programme usage remain unknown. Independent remote
Host receipts, complete population coverage and the original wider milestones remain
unresolved. The root finish's dashboard publication reported an advisory subprocess
failure: source inspection traces script discovery to the controlled home working
directory, with no script override. Nested stderr was not retained. No publisher retry
was needed for this local-cycle acceptance, and dashboard publication is not established.


## UTEL-REL-15 — 0.98.0 correction and native compact successor

Prepare the existing coherent successor rail for **0.98.0** (minor: additive
schema13 audited local CI attribution correction and native compact/3 report
contract). The promoted predecessor is **0.97.1**, source
`99ea75286f5c3cea2a261fef4e5b45cd70378185`, content ID
`sha256:02dfc44d64e1b807fe20591fc2e0fdb9d02f28435a8b027fddd8ff5631e22714`,
release403934888. Its consumed `utel-rel-13` journal and accepted installed/local
cycle remain immutable. The deferred unpublished0.97.2/`utel-rel-14` source and
all refused attempts remain history; this selection uses distinct
`refs/heads/fsgg/v2/journal/release/utel-rel-15`. Journal absence and target/feed
uniqueness are future live gates, not observations from source preparation.

Root must first integrate and qualify native compact/3 and schema13 correction
source, including the Telemetry Host tests repaired by PR4262. Then qualify this
version and its canonical projections/manifests on the complete final source,
merge through exact-head coherent CI, and hold main stable. The read-only candidate
must pack CLI, Kit and Drivers once, qualify the retained CLI bytes and retain its
original seven-file archive with predecessor and standalone evidence. Authenticate
its successful first-attempt exact-main run, artifact and raw archive digest.

Use separate no-effect `release-successor-publish.yml` preflight on that same SHA,
then the existing root-owned publication route with the same authenticated archive
inputs. Recheck all six feed coordinates, fresh tag/release and journal absence,
sole operator/environment and current authority before the first effect. Preserve
all sixteen effect admissions, protected journal CAS and forward readback recovery;
never repack, reset or replay a consumed operation. Create only `coherent-set/v0.98.0`;
all five sealed GS2-08.9 publishers remain unchanged.

Publication acceptance requires all16 effects and both-feed byte/payload readback.
Root's direct org-feed403 remains an access limitation alongside authenticated
hosted verification, not a payload failure. Adopt only the promoted manifest using
the existing user-level side-by-side updater. Verify actual installed schema13
correction and compact/3 before selecting dashboard migration or a fresh prospective
cycle; preserve original erroneous attribution and unknown historical populations.
No Home/Main service, remote Host adoption, credential expansion or whole-programme
coverage is implied. Old0.97.1 adoption and completed cycles must not be replayed.

### Accepted 0.98 coherent publication — 2026-10-06

Verified publisher `37434982959` promotes CLI, Kit and Drivers 0.98.0 from
`3d4f7e9c6337fb020e269e632653de0faeef6d72` using the original candidate `37433117226`, artifact
`11397568554` and raw archive SHA256 `ac322752d09cfdbc7c9fc209448dba0b9d95c56af979bedd876fa3d97c2cd650`. Protected journal
`utel-rel-15` and both-feed byte verification bind content `sha256:15a3bc4c133216cde77d1fef5b639d1842588a5754802eaa11b098e7b62c2a06`.
Candidate and publisher source, hashes and readbacks are joined without repacking or journal reset.
The prior release and consumed cycles remain unchanged. The existing global installation passed
payload and runtime-closure verification without a global switch. Backed-up canonical migration
to schema 13 preserved all 55 tables. The supported Governance #444 correction joined 204
targets to GOV-423-C3, preserved the original raw-fact hash and all 144 native outcomes, and
verified one audit entry plus an idempotent retry. The correction operation completed in 8.026
seconds with clean custody and no resource or storage failure. The supported drain accepted both
pending batches with no replay or quarantine and verified zero remaining in 2.857 seconds;
the subsequent empty drain also verified zero remaining with no replay or quarantine.
The settled schema 13 sanitized export publishes five eligible approved completed groups at
[feed commit `279f195…`](https://github.com/FS-GG/.github/commit/279f19561559238b49bed73f5956cd9881296f59),
with immutable bytes, public revision and current-branch readback verified. Activation then verified
[feed commit `7a4f81d…`](https://github.com/FS-GG/.github/commit/7a4f81d15a5d1cbb765ce78d8a8e5f4bfc13c2a5)
and authorized event publication. User units are installed inert; timer recurrence is unavailable
without a user bus. [Selector PR #4266](https://github.com/FS-GG/.github/pull/4266) merged at
`d97ad7eb485979afbc4fabfe8282971d8c4261c9`; [Pages run 37447862730](https://github.com/FS-GG/.github/actions/runs/37447862730)
passed test, build and deployment. The HTTP 200 deployed readback joined source, feed commit,
public revision and exact immutable payload with five eligible/published groups, zero dirty and
zero unmapped. Current-feed migration is accepted. CI reconciliation does not trigger the existing
completed-root event refresh; supported CI-only refresh, timer availability and unknown Governance
runtime coverage remain separate open follow-ups.


## UTEL-REL-16 — Prepared 0.99.0 schema-14 successor

[Source PR #4292](https://github.com/FS-GG/.github/pull/4292) delivered schema-14
facts, nullable usage and the Responses assessment path at
`2556144c3eed06ef31a1b7e12a8f528ef4f40b27`. The existing candidate/publisher rail
selects coherent version **0.99.0**, promoted predecessor **0.98.0** and distinct
protected journal `refs/heads/fsgg/v2/journal/release/utel-rel-16`.
The accepted 0.98 archives, `utel-rel-15` effects and installed observations stay
immutable. Source preparation establishes no new candidate archive or publication.

Hold the final main source through first-attempt read-only candidate qualification,
original seven-file archive authentication, separate `publish=false` preflight and
any first publication. The existing native admission rereads the exact current main,
first-attempt Actions identity, sole operator `EHotwagner`, release environment,
content and effect request before every mutation. Recheck all target feed coordinates,
tag/release and fresh journal state. A refused or unknown effect fences its dependent
work; observe the original identity before recovery. Publication requires all sixteen
journal effects and both-feed readback of the preserved packages, without repacking.

Host **0.5.0** is an independently versioned successor with its own read-only
candidate workflow, three-file original archive, publisher and `utel-host-rel-11`
journal. Its promoted predecessor is Host **0.4.0**. Coherent CLI publication does
not publish or install Host. Neither publication establishes schema-14 migration,
receiver enrollment, Manager installation, provider execution or assessment
acceptance; those remain in the [efficiency plan](process-efficiency-telemetry.md).
Retained state and historical unknown usage must remain intact through their separately
admitted installed operations. These instructions select no new host, credential or
service-control authority. Host0.5 publication is now reconciled from exact source
`64e95ebec1a8294e16edaafdb27e6aa96f32c6f7`, successful publisher
[`37567512069`](https://github.com/FS-GG/.github/actions/runs/37567512069)
and immutable release [`telemetry-host/v0.5.0`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.5.0).
Its protected `utel-host-rel-11` journal retains generation17/eight verified effects.
Installed receiver qualification remains pending.

### Accepted 0.99 publication and prepared compatible patch — 2026-10-07

Root accepted publisher `37566865635/a1`, promoted release `405346080` and both-feed
readbacks for coherent0.99.0 at source `64e95ebec1a8294e16edaafdb27e6aa96f32c6f7`.
These immutable receipts supersede the earlier pending-publication checkpoint.

UTEL-REL-17 prepares coherent **0.99.1**, genuine promoted predecessor **0.99.0**
and prospective protected journal `refs/heads/fsgg/v2/journal/release/utel-rel-17`.
The patch integrates qualified local telemetry3e8: one bounded1MiB private-state
reader/writer contract including newline, with refusal preserving full originals.
It changes no public signature, schema or cost counter. Keep the original incomplete
130s window distinct from the passing candidate suite and standalone red control.

Source preparation is not a candidate, publication, installed adoption or receiver
acceptance. Retain all sixteen effect admissions, exact-main/first-attempt/source
and original-byte recovery guards. Root separately admits qualification and any
both-feed operation after fresh coordinate, predecessor/channel and journal checks.
Canonical tool pin and frozen Wizard dependency remain published0.99; current frozen
source guard must refuse the changed compiled leaf before acquisition. Host0.5,
private stores, credentials, grants, defaults and unknown historical usage remain intact.


## Selected breaking coherent successor — 2026-10-08

Current source selects coherent **0.100.0** for Kit, Drivers and CLI with prospective journal `refs/heads/fsgg/v2/journal/release/utel-rel-18`; genuine promoted predecessor remains **0.99.0**. The earlier **0.99.1/utel-rel-17** preparation above is retained historical source work. The new stable0.x minor carries one current receipt ABI, current14 normal init/restore, explicit13 maintenance and scoped14 private dashboard. Historical SQL/digests and all six preservation controls remain; no installed mutation or original-root recovery is established. Keep the same actual candidate archive, first-attempt/exact-main/full qualification, all sixteen durable effect admissions, original-byte recovery and both-feed coherence guards. Coordinate/journal collision absence, candidate qualification, publication, installed adoption and native acceptance remain pending. Host0.5 source/published remains a separate axis; its published frontier was reconciled from the immutable release and exact protected publication evidence. Canonical0.99 pin and frozen Wizard dependency remain historical published bytes.


## 0.100.0 publication accepted; adoption pending — 2026-10-08

[PR #4315](https://github.com/FS-GG/.github/pull/4315) merged the current-only telemetry
contract at `3ed8ad419a64253ca6f665e9779e5e4d110f50a5` after 96 passing checks and four skips.
Candidate run `37773604415`, artifact `11549291800`, and original archive SHA-256
`765e396437933eccf68e7c2c1b21acb7ccd4ec1d9920cbcb3e5e1f8f5e722600`
were carried unchanged through passing preflight `37774706185` and first-attempt
[publisher `37774941181`](https://github.com/FS-GG/.github/actions/runs/37774941181).
Journal `utel-rel-18` generation 33 verifies all sixteen effects. The promoted content identity is
`sha256:7df5886e99d2244dab0c6cb75d5035e4dc68de6a69be42ead6f32ace9dfbe5ca`.
Independent public archive/manifest/channel downloads and public NuGet normalized payloads
match the candidate; the protected publisher verified both feeds. The managed repository tool pin
now selects 0.100.0.

The installed user tool remains 0.99.0. Prepare a fixed 0.100.0 side-by-side adoption with
installed payload verification before changing its selector. Original pending-reader custody,
store migration, fresh canonical collection and activation remain unresolved; do not retry the
held reader or infer native acceptance from publication. Host 0.5 remains its separate published boundary.

## Coherent publisher journal route prepared — 2026-10-10

The coherent publisher explicitly selects the existing main-directory journal route. It
classifies the selected logical release under immutable Authority main and refuses a
surviving legacy ref, incomplete or malformed tree, unreadable object or changing head.
An existing directory is recovery and receives the primitive's full lineage and original
intent validation; legacy-ref absence cannot make consumed journal18 fresh. Unused
preflight performs no journal or provider writes. The journal's default legacy mode,
state machine, sibling preservation, logical history and non-forced CAS remain unchanged.

Each genesis and later intent/dispatch/settlement admission composes the existing exact
publisher run/head/operator checks with the reviewed main and permanent retirement
protections. The reusable predicate retains the importer's exact frozen-ref and coverage
guards. Omitted native bypass actors require the existing stabilized full reviewed binding
and unchanged rule version; omission is not an observed empty roster.

Historical read-only qualification at source `31795a80e706fc4203c27e421f9e3648a1dd1329`
verified31 retained release paths and257 ancestries on imported Authority main
`2e697b07d050ac6d82712550d10235c221746a24`. It does not settle the cancelled original
apply run37984782893 or its unresolved ordinary effects. The accepted cutover retired the
coherent publisher workflow362181999; repairing its source does not enable it.

A bounded authenticated read on 2026-10-09 at protected `.github` main
`de7c37fad562f9f1195fa715b851bce8c1ef044c` found stable Authority main
`54b069a4422b7aaa1a754360c6ab12c9d3d5cd2e`. Main rules24802693/24802698 and permanent
fences24812732/24812733 matched the reviewed fields, versions and visible full actor
rosters. Effective main rules and the prospective19 namespace had the expected repository
origins. Publisher362181999 remained disabled with no queued, in-progress, requested,
waiting or pending runs. These observations are historical prerequisites, not permission
for a later effect; all settings and source bindings must be read freshly at that boundary.

Original read-only feed run [38004777915](https://github.com/FS-GG/.github/actions/runs/38004777915),
attempt1 and source `d210c54cb37f2b05342d1b4f5f03e8a367baf862`, reported newest stable
org/public0.100.0 for CLI, Kit and Drivers. Its component-tag-trio comparand was0.90.0,
separate from the accepted coherent-set/v0.100.0 channel. Prospective0.101.0 and logical
journal19 remain unselected; consumed18 and independent Host11 remain immutable.

Further release use needs exact source/native qualification and separate explicit workflow
activation and effect admission. CI+C1 publication also needs the actual packed receiver
qualification and an approved receiving destination before enabled C2 emission. Analyzer
distribution remains separate; no CLI pack reference establishes its installed delivery.
A sufficient approved local CLI receiver does not require an independent Host successor.
Installed selector, retained store, telemetry inbox/schema coverage gap and original CPU12
custody remain unresolved by this source preparation.
