# UTEL-REL-01 — Coherent telemetry producer after the GS2-08.9 seal

Owner: `FS-GG/.github` release and telemetry producer. Main/SystemAdmin owns installed-host adoption. This item is a separate, additive publication route; it does not reopen the accepted GS2-08.9 legacy routes or authorize `OpenV2`.

## Selected current source scope — 2026-10-16

The [user direction](https://github.com/FS-GG/.github/issues/4517#issuecomment-6218097145)
requires no backwards compatibility for active programme source and consumer work. Qualify one
coherent selected current composition; obsolete variants, layouts, serialized bytes, fixtures and
test populations need not be retained solely for compatibility. Replace or remove such scaffolding
where it serves no selected consumer, without an unrelated mass cleanup or rewrite. Preserve current
security, identity, ownership, boundedness, custody and failure/Unknown assertions in the resulting
tests; record removed or replaced coverage honestly. Immutable historical source, test and failure
evidence and original-operation identities remain intact. This changes no admission, publication,
installed-adoption, credential or replay gate and admits no managed/native cohort or uncertain original.

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

The distinct publisher filename is `release-successor-publish.yml`. Its default dispatch is read-only preflight. Permit `publish=true` only after the production adapter and recovery proof, live preflight on final main SHA and exact retained candidate, both-feed uniqueness, and protected-journal readback pass. A fresh journal ordinarily requires the candidate source to equal the current publisher SHA. One explicit fixed original-0.103 selection also permits the accepted candidate run `38290966724`, attempt 1, artifact `11766710761`, source `4d136d5450a7032d0b87c4b8a37f69f020144792`, raw archive SHA-256 `d8b22728b9e36932bf8d31e1ab486b312f2d1aedddf96660c872fc69f97f25ba` and content ID `sha256:0615d0d776f06b824b98193233c749f0ae6983e811d900cc5a166ede5e7e5007` to use fresh logical journal `utel-rel-21` from a reviewed compatible descendant publisher. The selection binds the complete original manifest/package/source/predecessor tuple and verifies native ancestry plus unchanged supporting publisher dependencies. It cannot select another version, candidate or journal. Each journal and provider effect still requires the authenticated first-attempt publisher run and current main to match the immutable publisher revision. Candidate provenance stays at the original source. After a partial effect, a repaired publisher may run from a newer main descendant only while the protected journal still binds the same original archives and content ID; publish those preserved bytes without repacking. An existing journal never becomes a fresh preflight or genesis through this selection. Partial or ambiguous writes remain pending for readback and never trigger blind retry. Historical .101/.102 recovery profiles and the five sealed workflows remain unchanged. The five GS2-08.9 workflows and sealed scripts remain untouched.

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

## CI and causal receiver successor source prepared — 2026-10-10

The next source window selects coherent `0.101.0` and logical `utel-rel-19` for
preparation only. Its protected source includes the exact CI classification/native
association controls and C1 additive immutable `execution-causal-admission/1`
receiver, delivered in [PR4353](https://github.com/FS-GG/.github/pull/4353).
The source selection follows protected main `cf7a28b254962042b0eb978d3d6abc48609a756a`.
Published package versions and tags remain `0.100.0`; consumed journal18 and its
original candidate bytes remain immutable.

Original read-only feed run [38008672236](https://github.com/FS-GG/.github/actions/runs/38008672236),
attempt1 at source `94f4cd7ff880f59748f8dbb162a7d807055d5983`, reported newest stable
`0.100.0` on all six CLI/Kit/Drivers org and public coordinates at00:21:37Z. A bounded
source-selection census later read unchanged `.github` main `cf7a28b`, stable Authority
main `bb424f3a71526e82057f4095a242b6844bd21089`, complete immutable parent trees with
selected19 absent, and absent legacy19. Native protections matched their reviewed
versions and visible actor rosters. Publisher362181999 remained disabled; candidate and
publisher groups were empty. Public target indices had no0.101.0. Local org-feed403
remains unknown; the hosted six-coordinate read is historical and must be repeated
freshly at candidate/publication admission, alongside genuine full predecessor/channel
freshness and unused tag/release/journal checks.

The existing candidate gate requires an exact packed receiver qualifier. It uses a
fresh assessor-approved disposable destination, generic receipts for the four fixed
synthetic declaration cases, full format2/schema14 export and exact declaration byte
readback alongside old admission, lineage and dispatch facts. A second process reopens
and replays the original receipts without changing the canonical revision. Unknown-kind
and immutable-declaration conflicts must refuse without changing that revision. Loaded
Store/Core hashes bind the proof to the packed assemblies. The proof travels inside the
existing runtime qualification member; the seven-member immutable candidate retention
contract and performance timings remain unchanged. This synthetic contract qualification
is distinct from genuine producer lineage, analyzer consumption and installed adoption.

Source preparation does not activate workflows, build a native candidate, publish,
install, migrate a database or change retained telemetry custody. C1 uses current
schema14 without migration. The old receiver rejects the new kind, so receiver publication
and approved destination adoption must precede enabled C2 production emission. A local
receiver suffices without a Host successor; analyzer distribution remains separate.
The installed selector, original held reader/store, full observer inbox/schema gap,
unknown waits, unevaluated critical path and token coverage remain unresolved.


## Original 0.101 partial publication and bounded journal repair — 2026-10-10

Original candidate [38026714312](https://github.com/FS-GG/.github/actions/runs/38026714312),
attempt1, retained artifact11660242226 and archive SHA-256
`d5d7d68ab0f73ce020e36f6ae10321b632eb177162d925cd9faabc071891bd64` from
source `79051e56b025dadfc6fcaabd58b79e33f2854928`. Its packed causal receiver,
Chromium receipt and durability proofs were accepted independently. Original
[publish=false preflight38029100526](https://github.com/FS-GG/.github/actions/runs/38029100526)
passed with all six coordinates at0.100.0 and0.101.0 absent.

Original [publisher38029395937](https://github.com/FS-GG/.github/actions/runs/38029395937),
attempt1, failed on an Authority immutable-blob HTTP403 with core rate remaining0.
Authenticated readback bound logical19 head `30edfbd8aee914492ce359f4dd34454701a41d5a`,
generation14, to the original source, archive and content
`sha256:e8ed439047f663dfcb34aba1152ffd4c6966a0c3a27be0ebb48eebcf47e26996`.
Tag, draft, all three org packages and public CLI are verified; public Kit has an
intent, and nine effects remain pending. Current local org reads are403/unknown;
local draft enumeration did not settle the recorded draft's identity. Publication,
public coherence and installed adoption remain unaccepted. No journal reset,
replacement draft, repack or blind retry follows from this partial state.

The source repair bounds per-instance physical-history memoization to2048 immutable
Authority commit/tree/blob responses,16MiB total serialized bytes and1MiB per object.
Only complete successful matching-OID objects enter it; returned values are independent
copies. Refs, selected path-history pages, protections and admission/provider reads
remain fresh. Logical-state reads remain direct, including the current canonical blob
recheck and legacy default behavior. This narrower physical-history cache preserves the
existing same-head corruption refusal while removing repeated historical object reads.
Existing offline fixtures cover generations14 and128, cache limits, incomplete/failed
responses, independent copies and fresh history/head reads; qualification and native
source admission remain separate from any original-byte descendant recovery.

## Four-advance recovery checkpoint qualified offline — 2026-10-10

The [publisher](../../scripts/release-successor-publish.py) has an explicit
`--recovery-checkpoint` profile for the retained candidate above and existing
main-directory journal19. It requires publish mode, the exact original candidate
run/artifact/archive/source/content tuple, original intent validation and current
descendant-publisher admission. A fresh destination refuses before initialization.
Starting generations14 through33 must retain the known sixteen-effect prefix.
Each invocation makes at most four actual advances, retains five-second spacing
and the existing45-minute observation deadline, and never redispatches an absent
effect whose original intent exists. No automatic next invocation follows.

One fixed adapter counts every Authority GET/POST/PATCH application entry,
including quota admission, constructor, classification, logical reads, native
protections and CAS writes/readback. The counted initial core-quota observation
must contain coherent integer limit/remaining/reset/used fields, a future reset,
and at least4500 remaining requests. Entry4201 refuses before sending; neither
the4200 ceiling nor the4500 floor is an input. These bounds do not reserve quota
against other consumers or bound transport redirects and request duration.

The [workflow](../../.github/workflows/release-successor-publish.yml) retains a
result artifact on success or failure. A bounded checkpoint can exit successfully
with `disposition=checkpoint` and `publicationComplete=false`; only an actual
complete advance reports completion. The16KiB result records the candidate and
execution identities, starting and last successfully observed journal bindings,
request counters, quota, iterations and dispatch uncertainty. It performs no
extra final journal read. Primary errors remain distinct from reporting errors;
a missing, malformed or truncated result does not establish acceptance.

Independent exact-source review and one admitted run of the
[offline controls](../../tests/release-successor-recovery/run.py) passed14controls
at protected base `c7dda5ca89e60f65e06ba5bfecba587181bdb7bd`. The controls use
in-memory Authority objects with actual journal lineage/read/CAS and composite
native admission. They cover quota refusal, original intent absence, admission,
CAS and observation failures, count exhaustion, dispatch uncertainty and result
failure. Their object IDs are synthetic fixture identities. Measured application
entries include the quota observation; fixture setup1632 entries is separate.

| Starting generation | GET | POST | PATCH | Total |
| --- | --- | --- | --- | --- |
| 14 | 1129 | 28 | 4 | 1161 |
| 18 | 1385 | 28 | 4 | 1417 |
| 22 | 1641 | 28 | 4 | 1673 |
| 26 | 1897 | 28 | 4 | 1929 |
| 30 | 1922 | 21 | 3 | 1946 |
| 33 | 751 | 0 | 0 | 751 |

All six measured slices stayed below the conservative4080-call bound. The
original supervisor receipt records exit0, observed owned custody and an empty
process group, with sampled peak59,424,768 bytes. Independent postinspection
verified all14 source/tool pins and exact original process births/group. The
first prelaunch census classification failure remains retained; correcting its
own observer exclusion preceded the single actual test attempt. These sampled
observations do not establish hard containment, reserve live quota or qualify
the original live journal, native source, provider or publication effects.

The original generation14 still retains `nuget:FS.GG.Kit=intent`. An absent
package observation does not prove that no dispatch occurred. The unchanged
advance policy returns waiting without redispatch for intent plus absence, so
the quota checkpoint alone cannot resolve that stranded intent. Source/log
ordering supports an inference of intent-CAS readback failure before the Kit
push, but no independent native provider non-entry receipt or publication audit
establishes that inference. Separate original-intent reconciliation and an
explicit forward-recovery decision remain required for an actual repeat. The
checkpoint supplies no intent-reset or generic-resend permission; the distinct
source-selected repeat profile below has its own admission boundary.

Current native source acceptance, fresh quota and original-operation
reconciliation remain prerequisites to any recovery invocation. The accepted
read-only [hosted observer38036069257](https://github.com/FS-GG/.github/actions/runs/38036069257),
attempt1 at protected `c7dda5ca89e60f65e06ba5bfecba587181bdb7bd`, authenticated
current draft408709919 with tag `coherent-set/v0.101.0`, target source
`79051e56b025dadfc6fcaabd58b79e33f2854928` and the exact original content marker.
The historical create-response identity and first local reader remain unknown;
current draft readback does not reconstruct either. Its fresh Authority-scoped
credential observed4985→4984 remaining of5000, without reserving quota or
reconstructing the original exhausted token. Partial feed state, public coherence
and installed receiver acceptance remain unresolved. Existing journal, provider
and admission algorithms and original candidate bytes remain unchanged.

## Explicit original Kit intent repeat qualified offline — 2026-10-10

The selected `--repeat-original-nuget-kit-intent` profile permits consideration of
one further request for the original `FS.GG.Kit`0.101.0 archive on nuget.org under
the retained intent's uncertainty. It is mutually exclusive with the four-advance
profile, requires publish mode and defaults to false. Its candidate tuple,
logical head `30edfbd8aee914492ce359f4dd34454701a41d5a`, canonical generation14
bytes, six verified predecessors and sole Kit intent are fixed. No package,
feed, version, replacement bytes or retry-count input is available.

One ordinary advance freshly observes the predecessors and Kit. A matched
package can settle14→15 through existing admission/CAS and then immediately
checkpoint without sending. An unreadable or conflicting observation refuses.
An absent Kit observation leaves the original intent unchanged; only the
explicit repeat profile supplies additional request permission. The publisher
then performs one extra canonical journal read and requires unchanged logical
and physical heads/fullstate, revalidates the original manifest, nupkg hash,
normalized payload and package identity in owned candidate storage, and applies
fresh original dispatch admission and native protections.

Before delegation, a bounded exclusive fsynced local record binds the new
execution, original intent, digests, route and uncertainty. Persistence failure
stops before sending. A port caps delegation at one existing provider dispatch;
the publisher immediately returns an incomplete checkpoint on an applied
response, or failed-or-unknown on an unconfirmed result/exception. It performs
no later advance, readback settlement or second send in that invocation. The
record is a new local attempt record, not a historical non-entry proof, global
lease or assertion that no earlier send occurred. A later repeated request
requires another explicit root selection and separately admitted operation.

The route is limited to nuget.org: its [publish contract](https://learn.microsoft.com/en-us/nuget/api/package-publish-resource)
rejects an existing package ID/version with409; other feeds may replace packages.
No skip-duplicate option is selected. A409 or successful client response does
not prove payload equality or settle the intent. Later ordinary processing
must freshly observe exact matching payload before protected settlement; an
absent observation continues waiting and conflicting payload refuses.

The4200 Authority entry ceiling and4500 observed quota floor remain fixed.
The original journal transition rules, generic sixteen-effect intent-plus-
absence policy and provider dispatch remain unchanged. Independent exact-source
review closed without findings, and one separately admitted offline operation
passed26 controls: the14 checkpoint controls plus12 repeat-profile controls.
The latter cover exact original-state refusal, unchanged-journal fencing, local
candidate and attempt-record failures, fresh admission/protection refusal,
single-delegation uncertainty, matched settlement and incompatible profiles.
Process and network guards precede project imports; provider delegation uses an
inert fixture port. No native package tool, Git process or provider ran.

The absent repeat branch measured430 Authority GET entries and no POST/PATCH;
the matched ordinary settlement measured443 GET,7 POST and1 PATCH, total451,
without provider dispatch. Quota observation is included;1632 fixture setup
entries are separate for each branch. The six checkpoint counts above remained
unchanged. These synthetic counts stay below4080 and do not establish a live
quota reservation or original-journal runtime bound.

The original supervisor receipt records exit0, no first cause, observed owned
custody, empty process group and sampled peak63,504,384 bytes. Independent
postinspection rehashed all15 input pins and11 retained artifacts and verified
the exact recorded process births, group and selected session absent. The
original receipt records PID/birth/PGID, not a separate session identity; session
absence is a later census observation. The100 ms sampled supervisor/group
observations leave unobserved transient descendants unknown and do not establish
hard containment. The old checkpoint commit and14-control evidence remain
historical. Offline acceptance does not admit an actual repeat, publication,
overwrite, deletion, replacement draft or receiver adoption.


## Fresh physical-head journal settlement — 2026-10-10

The explicit original Kit repeat in [run 38050387273](https://github.com/FS-GG/.github/actions/runs/38050387273)
returned an applied request and an incomplete checkpoint. Later ordinary recovery in
[run 38052044954](https://github.com/FS-GG/.github/actions/runs/38052044954) observed matching Kit
bytes, then its non-force Authority main-reference update returned HTTP 422. That invocation
made no provider dispatch. The exact 422 cause remains unknown; seven created Git objects may
remain unreferenced. A subsequent original-identity observation found logical journal generation
14 and the same Kit intent, with a stable physical main head during that observation. This does
not prove that no earlier mutation occurred or authorize another package send.

The shared journal now refreshes the physical main head immediately before constructing a
selected-path transition. It validates the selected logical pointer and canonical state against
the originally observed state, checks that main stayed fixed during that proof, and preserves
unrelated paths by constructing the new tree from the fresh physical parent. An additional
reference check immediately before the single non-force update refuses intervening movement.
The logical history retains its original parent. A final race or uncertain response remains a
failure requiring observation; the helper does not retry the update or provider dispatch.

Nine affected offline suites passed at source `2d60ec39b8301b8a9bef638b532dd80c44352053`:
370 unittest cases plus imperative real-Git, frozen-import and journal controls. They cover sibling
writes, selected-state changes, movement during proof/object creation, a final reference race and
an applied update with a lost response. The same run qualified both UTEL and Creator callers.
Counted fixture maxima were 2,605 Authority calls for a four-advance UTEL slice and 3,488 for all
eight Creator effects, below the unchanged 4,200 ceiling. These counts do not reserve live quota.
Two earlier private-launcher failures remain retained separately; the corrected launcher passed
nine pure controls and three actual declared local helper launches before this source run.

The original 180-second work limit and 30-second cleanup reserve applied to the nine-suite run.
All 5,473 pinned inputs and 14 aliases reconciled, with observed empty owned custody and no
retained residuals. Sampled process-group custody does not establish hard whole-tree containment.
Source qualification does not complete publication, install a collector, settle the original intent
or qualify the causal receiver. Fresh protected-source admission and original-identity readback
remain necessary before native recovery.


## Original recovery reaches generation 16 — 2026-10-10

[Recovery run 38057876080](https://github.com/FS-GG/.github/actions/runs/38057876080)
passed on protected source `6c1775cfbc61d6571e84d181b412fb1a1c23fdd5`. Its authenticated
checkpoint settled the matching original Kit publication, then recorded and sent the original
Drivers request once. The remaining two advances waited without resending. Logical generation
16 contains seven verified effects and the Drivers intent; publication is incomplete.
The result used 1,067 counted Authority calls: 1,051 GET attempts, fourteen Git-object POSTs
and two successful non-force reference updates. This actual count does not reserve future quota.

A later bounded observation on protected source `003ec3e7d5c60cffb4622197b58f2bf0f195f8b4`
matched the exact generation-16 canonical state and logical pointer
`1b35d40b3b7fca6c9848b34c0886cd822f28d7ea`. All 49 GET responses were retained, with no
writes and empty observed owned custody. Drivers appeared in the public index, but this read
neither acquired its package bytes nor established current writer quota. At that observation, its original intent
was still pending matching-payload settlement. Eight later ordered effects remained:
three archive assets, two qualification assets, the channel asset, manifest asset and promotion.
Coherent publication, installed collection and causal-receiver acceptance remain open.

The earlier HTTP 422 cause, possible unreferenced objects, expired operation deadlines and
incomplete telemetry collection remain preserved. Successful recovery does not resolve them.


## Original recovery reaches generation 20 — 2026-10-10

[Recovery run 38060161251](https://github.com/FS-GG/.github/actions/runs/38060161251)
passed on protected source `003ec3e7d5c60cffb4622197b58f2bf0f195f8b4`. Its retained,
source-bound checkpoint has nine verified effects and the Kit archive intent at logical
pointer `1966fc517ef413cd3f1c3035fd13da8836394aec`. The original candidate is unchanged.
The starting and final states, four successful journal updates and fixed advance order imply
four steps: settle the matching Drivers payload, send the CLI archive, settle that archive,
then send the Kit archive. The receipt explicitly records the final Kit archive request as
applied; it does not enumerate intermediate steps separately.

The operation used 1,731 counted Authority API entries: 1,699 GET attempts, 28 Git-object
POSTs and four successful non-force reference updates. The single expected legacy-reference
404 is retained. The quota observation belongs to this run and grants no later capacity.
The original result archive was read within its five-minute terminal retention window;
its exact source, candidate, run, artifact and receipt bindings passed, and owned cleanup
was observed empty.

Publication remains incomplete. Next is matching-payload settlement of the existing Kit
archive intent, without resending it. Six later effects remain: Drivers archive, qualification
evidence, qualification runtime, channel asset, manifest asset and promotion. Installed
collection and causal-receiver acceptance remain open.


## Original recovery reaches generation 23 — 2026-10-10

[Recovery run 38061751378](https://github.com/FS-GG/.github/actions/runs/38061751378)
failed on protected source `003ec3e7d5c60cffb4622197b58f2bf0f195f8b4` after three
successful advances. Its authenticated last-observed checkpoint has eleven verified effects
and no pending intent at logical pointer `654fd6fe4106036e48a533b8b40cd8485d26260d`.
The original candidate is unchanged. The source-defined advance order and retained states
imply settlement of the Kit archive, one Drivers archive request, then settlement of that
archive. The receipt explicitly records the Drivers request as applied.

The fourth advance refused with `main journal moved during CAS freshness read`. The
qualified source places this paired-reference check before object creation, the reference
update and provider dispatch. Thus this invocation did not send the qualification-evidence
request. This is a concurrency refusal; it does not justify weakening the check or retrying
the consumed operation. The physical journal head after the refusal remains unknown and
requires fresh observation of the original logical pointer and canonical state.

The operation used 1,913 counted Authority API entries: 1,889 GET attempts, 21 successful
Git-object POSTs and three successful non-force reference updates. The expected legacy-ref
404 and historical quota observation remain retained. The original 1,405-byte result archive
was read within the terminal retention window, all receipt bindings reconciled, and both
watch and readback custody were observed empty.

Publication remains incomplete. Five later effects remain: qualification evidence,
qualification runtime, channel asset, manifest asset and promotion. Installed collection
and causal-receiver acceptance remain open. This checkpoint does not establish current
journal state, fresh quota, or permission to resend an earlier effect.

## Original runtime intent and scoped recovery — 2026-10-10

[Recovery run 38063768172](https://github.com/FS-GG/.github/actions/runs/38063768172)
failed after recording generation 26, twelve verified effects and the runtime qualification
intent at logical pointer `e25521bff7f24dc0c454327c854f0d912fc6fa2c`. Its authenticated
receipt records the evidence upload as applied, its later settlement, and then
`qualification-asset:runtime: dispatch admission denied`. The source places that refusal
after the runtime intent update and before the runtime provider call. The exact admission
predicate that refused remains unknown. The operation used 1,831 counted Authority entries:
1,807 GET attempts, 21 successful object POSTs and three successful non-force updates.

The original five-minute result-read grant expired. A separately admitted read-only grant
authenticated the retained original artifact without extending that grant. A later 49-GET
observation on protected source `332acd8841f8b0497dccf32c4e27b2d37197e992` matched the
same generation-26 canonical state and pointer. Neither observation authorizes a send or
proves that all other invocations withheld the runtime request.

The fixed `--recover-original-runtime-intent` profile authenticates that original refusal
artifact, the unchanged candidate and exact generation-26 intent. It requires complete
bounded release and asset censuses plus fresh source, caller, protection, payload, journal
and quota checks. Matching runtime bytes select ordinary settlement to generation 27 with
zero sends. A stable absence witness can select one explicit original-byte runtime upload
after an exclusive retained attempt record; the result immediately remains an incomplete
generation-26 checkpoint. Unknown responses stop without retry. Ordinary absent intents
still wait without resending, and the generation-14 Kit profile remains separate.

Nine affected source suites passed 390 tests, including 20 new recovery controls. The actual
CLI fixtures counted 767 Authority API entries for the upload branch and 947 for settlement;
the sibling-preservation case counted 949, below the 4,080 qualification limit. Deadline expiry
and payload drift refused before mutation. These are offline fixture results; native admission
for this profile remains pending. Every later recovery send needs root reconciliation of earlier
known or unknown invocations; the local attempt record is not a global lease. Publication still requires runtime settlement, channel,
manifest and promotion, followed by independent installed collection and receiver acceptance.
The earlier HTTP 422 uncertainty, seven possible orphan objects and expired deadlines remain.


[Runtime recovery run 38075382153](https://github.com/FS-GG/.github/actions/runs/38075382153)
failed with HTTP 415 while downloading the original-refusal ZIP, after candidate
authentication and before Authority construction or runtime upload. Its authenticated
receipt records zero advances and no dispatch for this invocation; current journal state
and historical intent outcomes remain unobserved. A distinct read-only grant authenticated
the retained failure after the original five-minute read grant expired, without extending
that grant. The fixed Actions artifact request now omits `Accept`, matching the previously
accepted artifact transport; JSON and release-asset media headers, read and redirect bounds
and cross-host credential stripping remain unchanged. Nine affected offline suites passed
394 tests, including four new actual-request controls. Recovery fixtures still counted at
most 949 Authority entries, below the 4,080 qualification limit. This is source qualification;
native recovery and publication remain pending, with immutable payload and deadline fences
and no-second-send guarantees unchanged.

[Runtime recovery run 38079998431](https://github.com/FS-GG/.github/actions/runs/38079998431)
authenticated the original-refusal ZIP, then refused the first release-census response at
its content-length bound. Its retained receipt records generation 26, twelve verified
effects and the runtime intent, with zero Authority writes and no runtime upload in that
invocation. The original response header was not retained. A separate one-GET diagnostic
observed HTTP 200 and a 1,203,971-byte content length for the 100-release page using a local
credential; that observation does not establish the original App response. The selected
runtime census now requests 25 releases per page across at most 40 pages, retaining the
1 MiB response cap and the same maximum of 999 releases followed by a terminating partial
page; 40 full pages still refuse. Asset pagination remains 100 rows across four pages.
Nine offline suites passed 400 tests, including six new actual-transport controls and
both actual CLI branches with a dense 999-release population. Authority entries remained
at most 949, below 4,080; publisher entries reached 110, below 256. Immutable payload,
deadline, complete-census and no-second-send fences remain. Fresh protected-source, state,
quota and admission checks are required before another scoped native recovery; publication
and the four later effects remain open.


## Accepted coherent publication — 2026-10-10

[Original recovery run 38094091013](https://github.com/FS-GG/.github/actions/runs/38094091013)
completed the original CLI/Kit/Drivers 0.101.0 candidate without rebuilding or advancing its
version. Its authenticated receipt records generation 33 with all sixteen effects verified
and `publicationComplete: true`. Starting from generation 31, the ordinary route recorded
promotion intent, applied one release promotion, verified it at generation 33, and confirmed
completion on its third advance. It uploaded no package or asset again. The original result
was read within its five-minute retention window.

The receipt counted 1,950 Authority GET attempts, fourteen successful object POSTs and two
successful non-force updates: 1,966 application API entries, below the 4,200 limit. The
missing legacy journal ref accounted for the expected failed GET; it did not prevent
main-directory journal completion. These counts do not measure wire requests or reserve
future quota. Historical failed invocations, the unretained HTTP 422 cause and possible
orphan objects remain separate.

Independent readback authenticated the promoted manifest and stable channel for this original
candidate, then verified CLI, Kit and Drivers 0.101.0 package bytes from both feeds. Each
feed's raw package hash matches its promoted publication receipt, and all three normalized
payload hashes match the original candidate across both feeds. Public NuGet registry signing
changes the raw archive bytes; it does not change the authenticated producer payload. These
are bounded observations of the accepted coherent set, not a claim that the channel or
repository head remains current.

Clean public-feed side-by-side installation, installed receiver acceptance, local correction
and retained-store migration remain open. Existing installed effects, shim provenance and
historical unknowns are preserved. Independent feed verification does not establish installed
collection or complete the operational milestone.


## Installed engine pairing source repair — 2026-10-11

The accepted public 0.101.0 installation exposed an adapter identity gap: the SDK tool
shim has no adjacent engine assembly. When the configured executable resolves to the
running executable, the adapter now binds the actual entry assembly independently.
A different executable retains the adjacent-assembly route. Both paths preserve the
existing before-and-after hashes and exact typed rejection proof; they add no Store,
schema or wire-protocol authority.

The complete adapter source harness passed locked restore, build and execution in
33.57 seconds with no build warnings or errors. It covers executable aliases, foreign
paths and assembly movement, alongside the existing adapter cases. The source-free
package fixture also requires genuine SDK-installed parent/child rejection proofs,
including the foreign executable case. That installed-package regression remains
unrun and is required on a new coherent candidate before publication. The existing
0.101.0 archives remain unchanged; original pending recovery, live adoption and C2
acceptance remain open.


### SDK-installed engine pairing successor: 0.102.0 source preparation

The delivered adapter fix pairs the resolved current SDK apphost with its running
entry assembly. The [source CI run](https://github.com/FS-GG/.github/actions/runs/38118205528)
qualified all four SDK-installed cases: same executable and alias restore the
exact retained state after a typed parser rejection; a shadowed basename and a
selected foreign executable retain it. The package fixture passed 45 checks.
These candidate test bytes do not replace the immutable published 0.101.0 set.

The next coherent source version is **0.102.0**, with the accepted 0.101.0
channel and source `79051e56b025dadfc6fcaabd58b79e33f2854928` as predecessor.
Read-only observations on 2026-10-11 found 0.101.0 newest for all three packages
on both feeds, and no 0.102.0 package version or tag in the four release namespaces.
The proposed independent journal is `refs/heads/fsgg/v2/journal/release/utel-rel-20`;
its absence still requires authenticated readback. Source selection authorizes
no journal genesis, candidate dispatch or publication. Immediate collision and
current-authority gates remain mandatory before effects. The completed journal19
and all fixed original 0.101.0 recovery profiles preserve their original bindings.

A genuine source-bound 0.102.0 candidate must pack the three archives once and
qualify the four installed parent/child cases before publication. Both-feed
readback, isolated installation and supported original pending recovery remain
separate downstream obligations; the milestone is not complete.


### Original 0.102.0 publication and bounded continuation — 2026-10-11

[Candidate run 38128101680](https://github.com/FS-GG/.github/actions/runs/38128101680)
packed one coherent set from protected source `6e9be17bacd288ece103b67ecd5842d37a9c8e50`
and qualified all four genuine SDK-installed parent/child cases. Its original
archives and content ID remain the continuation inputs. [Publisher run 38133108945](https://github.com/FS-GG/.github/actions/runs/38133108945)
failed when the Authority installation token exhausted its core quota. The timely
terminal read retained the log only; a separately admitted post-expiry read
subsequently authenticated the original result archive. That ordinary result's
zero iteration and mutation defaults were uninstrumented and do not prove zero
writes. A later bounded journal observation established generation 21, ten
verified effects and no intent at that point; it did not verify provider bytes
or establish current quota. Publication and downstream 0.102.0 adoption remain open.

The selected source repair caches SHA-addressed immutable logical and physical
Git objects while rereading live Authority refs and selected-path history. It
records ordinary journal observations, attempted advances, dispatch uncertainty
and counted Authority API entries. Ordinary waiting returns an incomplete
checkpoint immediately instead of repeatedly polling the same prefix. The
proposed ordinary budget is 1,500 API entries with an observed 1,800-entry quota
floor; qualification and fresh native quota acceptance remain required. These
bounds were not enforced by the failed original run and do not reserve shared quota.

One fixed original-candidate continuation accepts an exact admitted generation,
logical head and canonical state digest, validates the ordered effect prefix,
and attempts at most four advances. A fresh automated bounded observation and
continuation can use the next checkpoint without another source profile or an
operator approval requirement. Existing no-resend, current authority, immutable
payload and first-cause rules remain in force; no new genesis or rebuild follows.
The historical 0.101.0 profiles retain their bindings and limits.

The shared terminal acquisition composition collects the job log and available
result archive for every terminal conclusion in the same bounded caller operation.
It authenticates original identity, archive digest and member set; missing or
unreadable evidence stays explicitly partial. Caller-owned original deadlines,
retention and custody are unchanged. After first publication reaches a terminal
outcome, an initialized journal with authenticated unchanged intent permits a
compatible descendant publisher source, so independent documentation merges
need not remain held. Every later effect still needs fresh admission.

### Accepted 0.102.0 publication and live schema adoption — 2026-10-11

[Continuation run 38156868958](https://github.com/FS-GG/.github/actions/runs/38156868958)
completed the fixed original candidate from run 38128101680. Its authenticated
terminal result records generation 33, all 16 effects verified and
`publicationComplete: true`. The final publisher source is compatible descendant
`9c31cb723bc9bc08f9c42deca5f03aba90b72fe5`; the candidate source, archives and
content ID remain unchanged. Independent provider observations verified the
promoted manifest, stable channel and all three package payloads on both feeds.
The retained promotion intent was observed and verified without another promotion
dispatch. Earlier failed and partial runs retain their original outcomes.

The configured live telemetry store was separately preserved and upgraded from
schema 13 to 14 through the supported 0.101.0 CLI. The complete snapshot was taken
under the store lock; migration validated historical checksums under its own
writer lock and preserved existing tables, rows and pending state. Two separately
bounded drains processed the 256 ready files: 215 committed and 41 were quarantined
as permanent conflicts. Read-only classification of every quarantined batch found
one stale same-candidate outcome event, with a strictly newer revision already
retained: 36 ready-to-delivered, four refused-to-delivered and one delivered-to-delivered
observation. No batch contained collateral events. All 41 original batches remain
retained; this does not claim that the current projection preserves every historical
field. The separate malformed historical pending intent was recovered through the
supported 0.102.0 Activity route after disposable qualification. The exact original
input produced an authenticated parser rejection before publication I/O; the CLI
retained native exit 1 and its original cause, removed only the pending intent and
rolled sequence 3 back to 2. Every other dispatch field, including its started phase
and native root identity, remained unchanged. This did not deliver a corrected
event, finish the historical attempt or establish usage coverage. The original
input, state and refusal receipts remain retained; the quarantines were not replayed.
A separate current validation activity then passed through the published 0.102.0
CLI: its recorded response matched one durable activity row, and the original
root state advanced from sequence 2 to 3 without a pending intent. Its timestamps
cover only the current evidence inspection. This does not complete the historical
attempt or establish native usage coverage.
Prospective telemetry begins and finishes now succeed through the explicitly configured,
pinned 0.101.0 engine. Native collaboration usage remains unknown where its
supported collector is unavailable; no additional Codex CLI was launched.

Independent public NuGet acquisition authenticated the 0.102.0 CLI archive.
Its raw bytes match the archive retained in the externally observed installation,
and its normalized package payload matches the fixed original candidate. The
complete population of 91 installed files remained unchanged; its package files
correspond to that public archive. Generated SDK metadata and the shim retain
unknown original provenance; the external installation owner remains unknown.
A separately admitted read-only installed 0.102.0 store-status operation returned
schema 14, ready status, WAL mode and zero pending inbox files. It observed the
three pinned hostfxr, hostpolicy and coreclr runtime anchors, with the complete
installed population and selector unchanged. The earlier short version operation
returned the exact version but lacked two runtime observations; that consumed
qualification refusal remains retained. These results accept the selected status
operation and runtime evidence, not every installed operation or selector custody.

The existing CLI ingress and local correction driver passed 38 inert refusal
controls after its reviewed currentness and private-configuration repairs. Its
separately admitted 15-stage native run then passed against two fresh disposable
stores. Every stage retained successful original receipts, process retirement and
all three selected runtime anchors. Ingress reopen and replay preserved the
canonical revision; one correction applied to ten targets produced one audit entry,
preserved all ten raw facts and changed effective delivery summaries from zero to
one. This accepts that disposable CLI path only. It does not recover the historical
pending attempt, change the live store or enable C2 emission.

The next operational milestone is the existing C2 receiver-and-producer acceptance:
bind the published receiver to the already configured receiving destination and
verify the owning contract before enabling genuine producer emission. Synthetic receiver
and disposable correction proofs do not establish genuine producer lineage,
analyzer consumption or native usage coverage. Unknown installation custody,
unavailable native collaboration usage and genuine operational producer acceptance
remain separate obligations. Publication, schema adoption and these selected
installed operations therefore advance the plan without completing the operational milestone.

### Current CI collection and budget repair qualification — 2026-10-12

The genuine LEARN adapter source was delivered through PR #4433. Its current-head
observer retained the admission, binding and delivered outcome, plus 41 terminal
runs and 69 terminal jobs. Actions, checks and jobs coverage remains partial;
continuation is pending. These facts do not establish complete CI health or
production adapter adoption.

Source diagnosis found that a multi-batch drain evaluates budgets in the first
committed batch but can leave later committed facts dirty. The proposed repair
preserves that first transaction and performs one final existing budget transaction
after multiple successful batches. Initial qualification passed 107 of 108
maintained cases but exposed an extra 32-item allowance in that final transaction.
The source successor shares one 32-item allowance across committed assessments;
rolled-back batch work does not consume it. Inbox bounds, replay, quarantine and
failure handling remain unchanged. The corrected successor passed all 109 maintained
cases, including six focused multi-batch, rollback and replay controls, after
locked restore and build succeeded. Source delivery and installed acceptance
remain required before using the repair against the configured live store.

Whole-head provider discovery currently restarts its bounded inventory on each
call; the retained continuation flag is not a resource checkpoint. A separate
continuation contract must bind the exact admitted candidate and base, retained
resource progress, complete inventory and final stability checks within explicit
request and time bounds. No larger cap, new cursor or provider replay follows
from this diagnosis.


### C3 receiver-first coherent successor source — 2026-10-12

Source selects coherent **0.103.0** and fresh logical `utel-rel-21`, with published
0.102.0 as predecessor. The source includes PR #4446's authenticated CI suite graph,
durable pending recovery and aggregate bounded provider continuation. Candidate and
publisher identities are selected only after exact source qualification and fresh
both-feed, tag, release and journal collision checks; this source selection creates
no release effect. The existing candidate/preflight/publisher rail remains in force.

The explicit original-successor recovery profile remains pinned to the unchanged
0.102.0 candidate and consumed `utel-rel-20`; advancing ordinary source cannot redirect
that recovery into `utel-rel-21`. Original 0.101.0 profiles, consumed journals, archived
package bytes and the five sealed historical workflows remain unchanged.

Publish and verify the coherent CLI/Kit/Drivers bytes before receiver adoption.
Install the capable local CLI receiver before enabling the new producer route; an
independent Host successor is unnecessary for that selected destination. Verify the
installed suite contract and exact payload, schema-14 migration journal and historical
rows, then qualify packaged receiver composition and the genuinely admitted native
more-than-40-request provider journey. These publication, installation and native
acceptance outcomes remain pending; version-only output and source tests do not
qualify them. Whole UTEL remains open.

The original successful `.103` candidate remains fixed at run `38290966724` attempt 1 and artifact `11766710761`. Independent acceptance on 2026-10-12 authenticated the raw archive, exact seven members, three package archives and payloads, manifest/source/predecessor bindings and packed synthetic receiver qualification. This accepts candidate preparation only; publication and receiver operation remain pending.

The fresh-genesis source-equality assumption was invalidated when unrelated documentation and guidance advanced main after packing. The implementation's explicit fixed-original selection separates original candidate provenance from the immutable compatible publisher revision. Its fresh native descendant and dependency checks supplement existing journal classification, uniqueness and per-effect authority; they never authorize historical publisher dispatch, another candidate build, original-effect replay or a journal reset. Before the protected operation, authenticate the retained original artifact again and independently admit the workflow's credential exchanges, runtime/custody and observation budget. Even `publish=false` performs the App token and NuGet OIDC exchanges. Source qualification is a routine delivery outcome; all native release and adoption boundaries below remain separately pending.

The source repair has no fresh-workspace, installed-runtime or lifecycle-default impact. Existing C3 acceptance continues with original-byte coherent publication, public readback, receiver installation and genuinely admitted native provider qualification; whole UTEL remains open.

A later original preflight stopped before dispatch when the compatible publisher's
ordinary post-merge check found a missing routine-development marker on its source
PR. Repair the canonical body producer and delivery consumer through a new nonempty,
normally qualified PR; retain the original failed attempts and full source evidence.
A successful replacement main preflight can release the installed ordinary-v2
credential job, so admit that exact automatic settlement path before merging the
replacement publisher. Qualify its full exact-main workflow and all native gates,
then select the new compatible immutable publisher while preserving the accepted
original .103 candidate.

The ordinary observer correction also covers a native merge whose target advanced
after the associated PR base was selected. It authenticates the actual first parent
as that base or a strict descendant, retains the exact PR head as second parent,
and independently proves fetched ancestry and complete clean three-way source-tree
composition. Original failed observation `38571428924`, attempt 1, remains evidence;
source controls do not qualify replacement hosted ordinary settlement, the full
exact-main workflow or publication. The accepted original `.103` archive and all
release-effect boundaries remain fixed.

### Fresh-profile resource containment — 2026-10-16

[ADR-0095](../adr/0095-utel-resource-containment-for-fresh-profiles.md) selects enforced
resource containment for a separately authorized fresh UTEL profile. The acceptance
result is finite installed budgets and tested refusal/Unknown behavior within explicit
boundaries, not a universal physical-copy bound or guaranteed availability. The initial
scope is one operational construction and one retained original. Actual production budgets,
provider/runtime admission and the fresh original identity remain unset.

The next acceptance target is one integrated maintained parent→Python owner→typed sink→
owned provider→exact readback→checked source receipt journey, including the same original's
pre-extraction diagnostic reservation and owned recipient persistence. Producer preparation
factoring, coherent source closure/vector selection and matching consumer pins must land
together with their source controls. A controlled backend pass alone does not qualify the
actual provider; the separate provider's source candidate alone does not qualify the parent.
Public CI remains independent of private source and credentials.

| Owner | Required contribution before runtime admission |
| --- | --- |
| Python producer and Coordination | Exact delivered construction/transport, canonical original/checkpoint/receipt joins, coherent pins and actual integrated success/fault evidence. |
| SystemAdmin | Exact provider/runtime/storage/supervision and recipient boundaries; finite numeric budgets with justified headroom; independently observed enforcement, persistence/fencing/recovery and original-clock mapping. |
| Authority owner and independent reviewer | Exact delivered authority-code acceptance; retained-evidence and principal-substitution controls. |
| Credential custodians and Programme | Separate provisioning/enrollment/readback, explicit coverage/exclusion acceptance and authorization of a distinct fresh profile/original. |

Source acceptance remains separate from runtime acceptance. The integrated matrix must
cover resource pressure, storage/inode exhaustion, interrupted commit/readback/delivery,
owner loss, duplicate/concurrent attempts, clock/retention boundaries and alternate egress.
Supplier measurements inform finite budgets; neither a sampled peak nor a source-test
ceiling establishes a production maximum. Existing source/job admissions and native holds
remain unchanged until their own requirements are met.

The following original `.103` section remains the legacy policy. Its 65,536-byte universal
physical-copy requirement is unproved, physical bound null and admission false. `.103`
remains attempted-unknown/unreplayed; all 439 reservations and `.104` holds survive. The
physical-proof outcome must not be marked proved because this future objective was selected.

### Original .103 authentication domain policy — 2026-10-15


The opt-in `fsgg.utel-closed-source/1` construction profile now has a
[closed source contract](../../tests/release-successor-authentication/closed-source-contract.md)
and an owned Python constructor that composes the maintained decoder, callback owner and
typed slot sink through bounded owned endpoints. It excludes generic stream/backend/path
injection and raw evidence export from this selected route while preserving child,
callback /1 and domain /3 wire formats. Focused source controls exercise thirteen serialized
exchanges, six exact retained phases, fixed-status egress and two recipient roles with
three cumulatively reserved purposes each. Four unsigned controlled diagnostic deliveries
can retain the existing maximum fixture; both expiry purposes refuse while post-expiry
custody remains null. Lost responses consume before extraction and never renew an attempt.
This is a source slice of [#4600](https://github.com/FS-GG/.github/issues/4600), whose complete
finite-cap acceptance remains open; it is not a production recipient/physical-fit claim.

The next public SOURCE successor factors only sink preparation and shared closed owner/
decoder/buffer construction. The maintained callback, observation, run and diagnostic
algorithms remain shared; public legacy preparation retains the exact controlled-backend
guard and production `prepare(profile)` still refuses before effects. The coupled
`inert-slot-callback-pair/2` source selection carries coherent bundle/provenance and
regenerated vectors, refusing `/1` against the new closure. Historical `/1` remains available
at its immutable producer/consumer pins as evidence; the current selected successor need not maintain
legacy callback/factory variants or compatibility-only scaffolding under the current-scope direction above.
The unchanged lifetime/allocation observers qualify the selected controlled-source journey,
including raw aliases and the sixth readback, without rewriting the historical joint ledger.

The closed Python core now shares endpoint initialization below its existing original,
sealed-input and exact-sink guards. The existing buffered path constructs the same two
`BytesIO` objects; a fixed source-owned composer can initialize the one maintained engine
with owned nonseekable endpoints. Initialization performs no endpoint I/O or discovery.
Public source controls exercise the thirteen-exchange/six-slot path and first/terminal
output failures without seek, EOF or a fourteenth read. The run/diagnostic algorithms,
closed semantic profile and sixteen-role `/2` slot manifest/bundle/vector bytes stay
unchanged; only the nine-file closed source closure changes. Coherent consumer repinning
and actual concurrent process/provider qualification remain separate dependent work.
Public production preparation still refuses before effects; no endpoint selector or
public activation is added.

This preparation is a dependency for the real managed → Python → owned CAP03 store →
recipient journey, not acceptance of that join. After independent CAP03 source review,
SystemAdmin owns the separately reviewed fixed private adapter and exact observation
commit extension: explicit synthetic original/descriptor/checkpoint/source mapping,
owned reservation/proof identity, both barriers and fresh new-descriptor readback,
checked translation to existing source receipts, observation ordinal mapping and
cumulative constructor/recipient attempts. Private qualification must use the maintained
public decoder/owner/sink and no monkeypatch, subclass impersonation, second callback
engine or public raw egress. Coordination then adopts the exact protected `/2` source,
manifest/vector/bundle pins and matching managed selected journey. Public CI uses only
the controlled backend, with no private code, endpoint or credential dependency. Actual
authenticated provider/checkpoint/clock/enrollment, native qualification, physical bound,
fit and admission remain unresolved; original unknown attempts are never replayed.

Coordination owns matching parent integration against the protected producer manifest;
SystemAdmin supplies actual provider/process/transport/storage/clock/proof/recovery and
enrolled-recipient containment. Production fields remain null and construction refuses
before effects. Complete recipient source/runtime/storage/transport and alternate-egress
populations still require qualified accounting/enforcement. The upper-bound trust registry
is unchanged: cap 65,536, physical bound null, fit unproved, admission false. This source
delivery proves no installed/native acceptance or authority-service private review.
Original `.103` remains attempted-unknown/unreplayed and `.104` remains held.

The sponsor-delegated [policy](https://github.com/FS-GG/.github/issues/4517#issuecomment-6199911781)
and subsequent [accounting/ACK decision](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200107860)
are recorded in [ADR-0093](../adr/0093-original-103-authentication-domain-policy.md).
Programme owns policy/change control; EHotwagner retains separate credential/App custody.
SystemAdmin owns the separate supervised ACK-service design and later operational custody,
outside the key-free facts store and original job/runner/VM. Actual signer custody and
independent enrollment remain unqualified. Preserve original-operation descriptor and
issuer/ACK separation, one-use durable gates, same-token HTTP 204 revocation and original
270/285/300-second cutoffs with authenticated finite clock uncertainty.

The cap is six **logical phase ordinals** and **65,536 total retained bytes**, counting
the complete signed/schema population, simultaneous physical copies, peer control,
write/replacement overlap and old/temp/candidate uncertainty evidence. No required fact
may be dropped or evicted to fit; unproved fit/overflow refuses source/runtime admission.
Retention ends 90 days after the original total cutoff without read-based renewal;
RetentionExpired/Unknown preserves the original-effect hold.

The former unavailable-packet status is historical: SystemAdmin [reported restored/read-back-equal
private v1/v2](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200035172) and
[delivered v3](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200247987).
Programme independently verified the received v3 digest and accepted its 64,000-byte slot
arithmetic and separate ACK topology only as a [planning subset](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200287829).
Receipt, prospective slot limits and reported readiness v10 do not prove complete fit or
native qualification; v1's 128-frame/131,072-byte design is historical.

Programme/fdev1 owns the reserved source-only slice for an isolated versioned inert
contract, complete maximum encoded vectors and exhaustive slot/copy accounting.
SystemAdmin independently reviews host layout/backend/trust against its exact digest;
Programme decides the join. This permits pure contract/vectors/tests only, with no
production provider/workflow change or native/credential/release effect.

[PR #4565](https://github.com/FS-GG/.github/pull/4565) and the [late-cause correction #4567](https://github.com/FS-GG/.github/pull/4567),
protected at `a8384157c5622c7e0629b6d9129a6fbe8a96428a`, deliver an accepted **inert serialized
construction subset**: [closed schemas/vectors and model](https://github.com/FS-GG/.github/blob/a8384157c5622c7e0629b6d9129a6fbe8a96428a/tests/release-successor-authentication/domain-contract.md)
count 57,562 proposed encoded bytes, leaving 7,974 under the 65,536-byte cap.
The authentication entry passed 68 pure tests and hosted qualifications passed.
Original pins and publisher provenance remain distinct from executing authentication;
the external descriptor commits the exact child-binding and full ready-frame digests.
Ready/start structural maxima including framing are 1,291/1,273 bytes. Child ACK
`b,h,k,n,v` remains separate from the 510-byte framed external ACK. The 546-byte framed
diagnostic response is distinct from the existing 512-byte callback retain response and
cannot become a replacement gate. Authenticated typed readback and existing
`TakeAck`/`ConfirmAckSent` one-use/full-write/cutoff checks remain integration obligations.
Placeholder signatures provide no authenticated positive evidence.

The model counts host/peer control overlap, uncertainty and independent same-token 204
observations; frozen core uncertainty permits only separately reserved append-once loss
and expiry observations, with no retry of an uncertain observation write. It preserves
first failure and original-effect holds. Signed loss/expiry observations now retain optional
closed `lateCause` phase/code after core freeze without rewriting core `firstCause`,
including when the earlier cause is absent. The maximum observation is 497 bytes within
its 512-byte slot; four reserved copies add 280 bytes. An observation remains immutable
and append-once after its first possible write, with no retry after loss/restart/ambiguity.
This does not prove the actual population fit.
[Coordination PR #999](https://github.com/FS-GG/FS.GG.Coordination/pull/999), protected at
`16c28c0419612a358aa572ca5cbc1e3540bdd8d0`,
narrows the inert source profile to one immutable exact frame per decoded snapshot,
with a checked payload view and no separate retained payload array. Callback correlation
drops pending base64 after encoding, and internal encoding reads the owned frame;
public `Frame`/`Payload` accessors still create caller-owned copies. Existing legacy
start/phase/ACK/callback bytes and one-use/full-write checks remain unchanged.
The protected [ownership inventory](https://github.com/FS-GG/FS.GG.Coordination/blob/16c28c0419612a358aa572ca5cbc1e3540bdd8d0/tests/engineering/phase-result-protocol/README.md)
and [managed source](https://github.com/FS-GG/FS.GG.Coordination/blob/16c28c0419612a358aa572ca5cbc1e3540bdd8d0/src/FS.GG.Coordination.Orchestration.Execution/PhaseResultProtocol.fs)
passed hosted compiler, required suites and coherent qualification (48 success, six
skipped). The [qualifier pin](https://github.com/FS-GG/FS.GG.Coordination/blob/16c28c0419612a358aa572ca5cbc1e3540bdd8d0/eng/utel-callback-source.json)
selects the separate inert callback pair at `.github` `71227e17` and exact 23-case census;
its corpus and actual paired Python callback journey do not qualify factory/native
compatibility, the new slot sink, credentials or original-operation effects.

The six × 2,048-byte snapshot-array ceiling is a partial inventory only. The opt-in
managed ownership profile explicitly refuses closed physical-copy admission until
endpoint capture/release is qualified; legacy inert preparation asserts no fit.

The remaining source-owner blocker is the actual simultaneous-copy/lifetime census
and slot mapping across binding/start/ACK arrays, parser/encoder/base64 temporaries,
callback/read buffers, async requests/tasks, arbitrary stream captures/caller copies,
and actual host/sink/backend/peer storage. Aliases need truthful identity and independent
buffers remain counted. Terminal/timeout/cancellation/task completion/restart do not
prove physical release or permit reuse of unresolved slots. The serialized 57,562/7,974
model is unchanged; no joined fit follows. Seven possible extra maximum-ready caller
copies exceed headroom (9,037 > 7,974), without establishing a current allocation count.
Memory, filesystem metadata/allocated blocks and native enforcement remain unproved.
Protected [PR #4570](https://github.com/FS-GG/.github/pull/4570) at
`966e3216713f2bc2959af5f01ebe9bb41be4b73b` adds the [actual-sink constructed witness](https://github.com/FS-GG/.github/blob/966e3216713f2bc2959af5f01ebe9bb41be4b73b/tests/release-successor-authentication/sink-copy-contract.md):
a permitted padded report yields 66,170 serialized bytes at the pre-replace cut,
634 over policy. Old/temp file stores, prior/body memory and incoming frame are five
distinct backing stores, while each report remains within 16 KiB. Five focused controls,
77 authentication tests and required hosted checks passed. This is a source-permitted
counterexample using an inert filesystem double, not an observed deployed allocation
or complete census. Additional objects/copies and filesystem blocks remain uncounted;
the report limit does not prove the sink's joined fit or supply durable ACK/barrier,
clock, recovery or retry authority. The actual host/sink/backend/peer map stays held.
Protected [PR #4571](https://github.com/FS-GG/.github/pull/4571) at
`df803eb90f1d3f1d6175319d06dcb30478df5093` adds the [inert closed-report slot sink](https://github.com/FS-GG/.github/blob/df803eb90f1d3f1d6175319d06dcb30478df5093/tests/release-successor-authentication/slot-sink-contract.md)
through the actual callback owner. Twelve focused/89 full authentication controls and
hosted checks passed. It rejects padding before reservation, preserves early-terminal
phase order and models one-attempt core/append-once observational writes with Unknown
and first/late-cause retention. Its explicit 23,552-byte store reservation excludes
caller/callback/managed/runtime/serialization and native backend populations. It neither
replaces the publisher nor supplies cold restart/cross-process custody, real barriers
or authenticated receipts. It cannot be combined with or replace the 57,562-byte model
to infer joined fit. The reusable finding resolves the source-contract capture gap through Atlas
`github/original-103-authentication-domain-policy`.
Protected [PR #4573](https://github.com/FS-GG/.github/pull/4573) at
`d3dd657b23fc1ac2f3aeb6cba06b6ca455406bdf` adds the [inert framed Python journey](https://github.com/FS-GG/.github/blob/d3dd657b23fc1ac2f3aeb6cba06b6ca455406bdf/tests/release-successor-authentication/slot-sink-contract.md#framed-injected-stream-fixture)
through actual decoder/owner/typed sink, with original scalar guard before copying
and complete write plus flush before confirmation. Failure permanently closes while
retaining existing stores/attempts and any existing Unknown; malformed input before
a phase does not create Unknown, and no retry follows. Thirteen focused/103 full tests
and hosted checks passed, including exact 13 exchanges/six retained frames. This
accepts injected Python integration only: source pins and managed-profile selection
remain unchanged; no whole-fit, production transport, durability, host-clock or native
ACK claim follows. Atlas also captures this additional reusable source finding.
The [PR #4572 scalar guard](https://github.com/FS-GG/.github/blob/11cecded9486cd74dfb0ff51946799a21f4469db/tests/release-successor-authentication/inert_phase_slot_sink.py),
protected at `11cecded9486cd74dfb0ff51946799a21f4469db`, closes the literal-equality
counterexample by checking exact built-in two-key dict/string types before equality.
Thirteen focused/90 full tests and hosted checks passed. Custom equality and subclasses
refuse without custom equality execution or reservation/backend/phase changes; valid
subsequent frames succeed. This is an inert input-boundary correction, with no deployed
host, whole-fit or native claim.

Protected [PR #4576](https://github.com/FS-GG/.github/pull/4576) at
`fca016e1db30a279a5489223d19408f086eb18b5` extends exact-type preparation checks to
identity/window/population/role declarations before set/equality and snapshots admitted
literals before caller hooks. Spoofed scalars/mappings refuse without custom comparison
or reservation; later caller mutation cannot alter admitted identity/path/generation.
Sixteen focused/106 full tests and hosted checks passed (32 success, two skipped).
This closes an inert declaration boundary, with no whole-fit, installed-host or native
claim; producer integration remains separate.
Actual signer custody/enrollment, authenticated barrier/fresh exact readback, original-clock
continuity/uncertainty and independent loss proof remain SystemAdmin/consumer obligations.
Interface freeze and installed/native admission stay held; production provider/workflow
remain unchanged.

All six native selections and all admission/effect flags remain false. Preserve the
accepted loss matrix, original `.103` attempted-unknown tag and no replay, unchanged
original candidate/journal, and `.104` release hold. No credential issuance,
installation, dispatch, journal, tag or release effect follows from this policy.

### Proposed .104 selection on the maintained successor rail — 2026-10-15

**Draft for release-owner review; no operating authority.** This proposes an
extension of the existing candidate, artifact verifier and publisher. It activates
no workflow, journal or credential operation. ADR-0012's same-archive,
organization-feed-first rule and ADR-0013's producer-local Trusted Publishing
remain binding. No generic v1 admission genesis follows (ADR-0087).

The local source-only proposal `638a0e1c` selects coherent 0.104.0 and a distinct
`release-improvement-candidate.yml`. Reconcile it with current protected source
before qualification. Its 0.102.0 predecessor is an assumption, not a decision
about unfinished .103 effects. The current publisher selects .103; its artifact
verifier accepts only `release-successor-candidate.yml` and the corresponding
artifact name. It deliberately cannot consume the new .104 archive.

#### Original .103 disposition comes first

Preserve the fixed candidate run `38290966724`, attempt 1, artifact `11766710761`,
source `4d136d5450a7032d0b87c4b8a37f69f020144792`, archive
`d8b22728b9e36932bf8d31e1ab486b312f2d1aedddf96660c872fc69f97f25ba`
and `utel-rel-21`. The source-pinned checkpoint from run `38580684077` records
generation 2, `tag:intent`, and a tag dispatch with unknown outcome. This is
historical evidence required by recovery, not a fresh journal observation. A
missing public tag does not establish non-dispatch.

Before any .104 publisher preflight, credential exchange or genesis, the programme
root and accountable release authority must select a supported disposition:

- Reconcile .103 through its admitted original-byte route. If it is promoted,
  reselect the .104 predecessor and requalify the complete candidate; the current
  0.102.0 assumption must refuse.
- An explicit suspension or supersession of .103 would need a separately reviewed
  installed authority route: durable receipt, exclusion of competing .103
  continuation and a reader contract. None is established here. Unknown effects
  stay unknown; no receipt may mark them verified or absent without evidence.

Until a route is accepted, .104 publication remains blocked. Candidate source and
byte qualification may advance under their separate admission. Neither resets
`utel-rel-21`, replaces original archives, renews a consumed effect or enables the
unavailable original-tag credential factory.

#### Smallest source extension after the decision

1. Keep the publisher filename, environment, sole operator, concurrency group,
   protected journal adapter, admission rereads and 16-effect order. Add one
   explicit, mutually exclusive, literal .104 profile. Preserve all original and
   historical selections and refusals. The authority owner must allocate and
   verify an unused protected journal identity; `utel-rel-22` is a proposal only.
   No caller-supplied version, journal, workflow or artifact-name override.
2. Extend the existing artifact verifier with a closed profile pairing exactly
   `release-improvement-candidate.yml` and
   `release-improvement-candidate-<source>-<run>`. Keep the old profile default.
   Preserve repository IDs, successful first attempt, exact main, seven safe
   members, raw archive digest, manifest, three packages, qualification and
   predecessor checks. The publisher's verified selection chooses the profile;
   untrusted artifact fields cannot choose it.
3. Require candidate source to equal the exact current publisher source for fresh
   .104 genesis. Hold main stable through qualification and first publication.
   Changed source requires reviewed requalification; add no generic descendant
   exception. Recheck both feeds, tags, release and selected journal before genesis.
   Preserve the original archives for forward recovery.
4. Preserve .103 dependency-pin refusal. Editing the verifier or feed reader
   changes a pinned supporting dependency; ancestry or green .104 tests cannot
   approve it. Review compatibility separately before allowing original selection
   against changed dependencies. Do not silently refresh pins.

The eventual touch set is the existing publisher workflow/script, artifact
verifier, focused inert controls and this owning plan. The candidate scalar,
projections and generated driver manifest belong to the separate source candidate;
reconcile them after concurrent Atlas integration lands. This draft changes no
executable path.

#### Controls and live gates

Inert controls must prove closed .104 selection, conflict with every historical
flag, rejection of crossed workflow/name/version/journal bindings and reruns,
changed-main refusal, unchanged .103 refusals, and zero journal/provider writes
when selection, authority, predecessor or archive identity is denied. Reuse the
existing protected-journal/provider controls for intent, unknown outcome, CAS
conflict, feed partial publication and forward recovery. Preserve first failure
and independent cleanup/reporting under original bounds.

Before live work, separately admit the exact source-qualification dispatch and
authenticate its original seven-file Actions archive and three package bytes.
Refresh the selected .103 disposition, .104 journal/protection, six feed
coordinates and exact-main facts. Then independently admit publisher runtime,
custody, observation budget and credential route. Even `publish=false` mints the
ordinary ledger App token and exchanges NuGet OIDC. Source metadata or historical
proof does not establish current issuance, scope, expiry or effect authority.
Publication needs all 16 durable effects and both-feed readback. SDD pin updates,
materialization and installed receiver qualification follow normal owner routes;
no candidate, package staging or version output establishes them.
