# Unified Roadmap handoff — 2026-10-13 09:52:58 UTC

Programme execution is **paused and incomplete** after `$stop-with-handoff`.
This timestamp is the first observed save-preparation time, not an invented request
arrival time. All current workers were terminal (five completed, four capacity
errors); none was restarted. Root performs only terminal cleanup and this handoff's
native delivery. No runtime goal exists and no monitoring continues after shutdown.

The objective remains completing the [Unified Roadmap v3](../roadmaps/2026-10-07-unified-development-roadmap-v3.md)
and [games roadmap](../roadmaps/2026-10-07-games-development-roadmap.md), through
[continue-from-handoff](../../.agents/skills/continue-from-handoff/SKILL.md) and
[work-programme](../../.agents/skills/work-programme/SKILL.md). Campaign:
`unified-roadmap-20261003`. The [previous handoff](2026-10-12-192900-unified-roadmap-handoff.md)
and its predecessors retain every obligation not explicitly superseded here;
v2 is archive lineage. [Stop-with-handoff](../../.agents/skills/stop-with-handoff/SKILL.md),
[agent-handoff](../../.agents/skills/agent-handoff/SKILL.md) and
[technical-writer](../../.agents/skills/technical-writer/SKILL.md) govern this save.

Latest priorities: advance Podman first to support LEARN/PROC; prefer **Arch**;
seek useful independent parallel lanes aggressively; report every 30 minutes with
lane stages, completed phases, dependencies and estimates. **This machine is the
selected target. Its external host/operator will amend its Containerfile through
the user. No local package installation occurred or is selected.** No message was
sent to that external operator. The rejection of another CLI remains binding.

## Accepted source and verification

Protected `.github/main` before this save is **M4**
`6b863af8c5ae7c040675203efc69b8c315a37dc8`. Root independently fetched and checked its
Git object. [PR4461](https://github.com/FS-GG/.github/pull/4461) merged the Podman
design, prior-art report, owning roadmap and v3 entry. Candidate
`36b7d8995c3cb60a855b2f3fa2e9750196b17589`, tree
`25e90dccc6b440a12dccb6dac5296a1be7283552`, parent M3
`a030c05ebd4e2a2c7e6fd704405e073e17db957d`; merge time 08:34:43 UTC.
Normal CI: 37 checks, 33 success/4 skipped; coherent workflow303 run38368243924
and routine38368243346 passed. Original ordinary settlement
workflow366182296/run38370210822/a1, push/main/M4, **succeeded**. Preflight job
115205038337 ended08:35:10; settlement115205184091 ended08:36:56, within its
original cutoff08:45:14. Artifact11798553527 (1137 bytes) and independent logs
were accepted; receipt digest
`6531c41ae59c621eea1c8023218f54d0ba27120ac188b9e08d03e8b07f7e3e94`.
This establishes documentation delivery and that original settlement, not a built
image, installed engine or whole-roadmap completion.

Earlier prepared [showProgress PR4459](https://github.com/FS-GG/.github/pull/4459)
merged at M2; [registry repair PR4460](https://github.com/FS-GG/.github/pull/4460)
merged at M3 above. M3's full engine run38358820790/a1 passed all44 exposed steps,
ending07:18:40. Its freshness is historical after M4 and this save; do not use it as
a new publisher grant. Current v3 has **18 rows: 16 open, 2 Done**; games has ten
open rows. No whole outcome closed in this window. Board/managed Portal remain
closed and stopped SVG profiling stays excluded. Binary rows do not measure
engineering percentage.

| Lane | State at stop | Next action after authorized restart | Acceptance/dependency |
|---|---|---|---|
| Root / integration | Stopped except handoff delivery | Reconcile current protected main, reservations, route and private evidence | Fresh native prerequisites; no old grant reuse |
| `podman_docs` / Arch inventory | Completed source repair; stopped | Reuse accepted 37 tests; admit only retained-layer reparse plus small-layer GET | Both compressed hashes and diffIDs, ordered overlay, actual donor configuration/keyrings; inventory remains unaccepted |
| `arch_repositories` | Completed source-only signature carrier; stopped | Root review/admit one fixed extra.db.sig acquisition; consume bounded selected descriptors | Actual response/signature disposition; DB metadata is not package trust or dependency closure |
| `podman_profile` | Stopped with staged Arch draft | Join authentic donor inventory, maintained resolver output and trusted package archives; create genuine checksum manifest | Source checks and real build pending; no fabricated manifest or packet joins |
| `podman_window` | Completed planning; no action | Reconcile the externally rebuilt machine, then bounded namespace/container/limit/teardown acceptance | User-mediated host rebuild and valid mapping/launch contract |
| `utel_plan` / consumers | Completed seam decisions; no action | Select an actual provisioner caller/ownership seam before production changes | Allocation/launch/teardown and durable state ownership contract |
| UTEL publication | Deferred, not cancelled; no action | Bind original0.103 candidate to final protected source and fresh publisher prerequisites | Current FULL/ordinary correspondence, PUBLIC4 and separate effect admission |
| PROC/CTX/LEARN and older lanes | Preserved holds; no action | Reopen only independently ready owning work or authentic missing evidence | Prior handoff gates remain; no capability inferred from Podman preparation |

`context_resume`, `process_resume`, `recover`, and `utel_recovery` failed at model
capacity; no completed work or cleanup is inferred from those errors. Native
collaboration carried current coordination; archived worker mailboxes are not
revived. On restart, inventory and repository metadata can proceed in parallel in
separate private roots within fresh capacity. Provisioner acceptance waits for the
external rebuild. Routine PROC adoption can remain independent of contained Linux
acceptance; no production-code gap was justified merely to fill agent slots.

## This machine: host-facing provisioning request

**Installed during this session: none.** Only immutable metadata, repository DBs
and one retained Arch layer were downloaded under private `/tmp` roots. The observed
machine already runs Arch, kernel7.2.4-arch1-2, developer UID/GID1000, pacman,
newuidmap/newgidmap and sudo. Podman/crun and `/dev/fuse` were absent. Directory
observations show SDK6.0.428/10.0.400/10.0.401 and runtime9.0.20/6.0.36/10.0.11/10.0.12;
these are not qualified CLI runtime results.

The host-facing proposal requests package roots **podman, crun, shadow,
ca-certificates, glibc, icu, krb5, libgcc, libstdc++, libunwind, openssl, zlib**, with
pacman resolving engine/network helpers. Keep SDK10.0.401/runtime10.0.12. Use a
consistent frozen repository and maintained full-system upgrade/resolution;
selected October12 metadata says Podman6.1.3-1/crun1.30.1-1. The source sketch
proposes VFS/crun, so no FUSE device/package is required by that selected route.
This is a proposal, not an executed install command or authenticated package set.

The external owner must provide a nested-rootless launch contract: permitted
user-namespace/mount operations, valid subordinate UID/GID mappings, effective
CPU/memory/PID enforcement with its owner/scope, private writable graph/run/runtime/
temp/cache/export roots, disk limits and reliable teardown. Current uid_map/gid_map
are `0 1 65536` (inner IDs0..65535), but `/etc/subuid` and `/etc/subgid` contain
`developer:100000:65536`, outside that parent range. Expand the outer mapping or
allocate a valid nonoverlapping inner range; retaining those entries alone does
not establish nesting. Enabled user namespaces, visible cpu/memory/pids controllers,
Seccomp2 and zero effective capabilities do not prove namespace creation or cgroup
delegation. No blanket privileged/unconfined fallback was selected.

After rebuild, old platform observations must be invalidated. First reconcile
Containerfile/image identity, tool/helper ownership and modes, maps/subordinate
allocations, seccomp, cgroups and storage ownership. Then one freshly admitted
private VFS/crun namespace probe, followed by one offline authenticated digest-pinned
container run. Observe finite effective CPU/memory/PID limits, execution identity,
normal exit, one bounded cancellation and full owned cleanup. No implicit pull,
host-rootfs substitution, privileged fallback or LEARN qualification claim.

## Private evidence, drafts and truthful limits

Private roots: **R** `/tmp/fsgg-recovery-20261012-kjgb6lgr`; **P**
`/tmp/fsgg-parent-resume-20261012-pk6x1mf5`; **PD**
`/tmp/fsgg-podman-profile-0d3oasp1`. They remain outside committed payloads.
The paths/digests below locate evidence for an authorized successor; they grant
no operations. Use bounded context views and complete mechanical reuse.

| Evidence/location | Accepted or reported frontier |
|---|---|
| PD/readback-source-preparation/root-protected-docs-merge-and-original-ordinary-acceptance.json | Root accepted PR4461/M4 and original ordinary result |
| `/tmp/fsgg-arch-donor-metadata-successor-20261013/root-immutable-donor-metadata-acceptance.json` | Root accepted native45128, five metadata GETs, index/manifest/config only |
| `/tmp/fsgg-arch-repository-db-offline-20261013-q38mit09/root-offline-outcome-and-cleanup-acceptance.json` | Native64614 exit0, zeroGET,15340 package descriptors; metadata only |
| `/tmp/fsgg-arch-inventory-path-diagnostic-20261013/root-offline-outcome-and-cleanup-acceptance.json` | Native70384 exit0; unrelated regular literal-backslash member diagnosed without extraction/raw path disclosure |
| `/tmp/fsgg-arch-inventory-successor-offline-20261013/root-offline37-acceptance.json` | Native chunkb7e935,37/37 exit0; fresh cleanup09:53:52; SHA25619d815d797efa149ea1a65724924ee9bb1122902f8f20e7ce572778da68cd875 |
| `/tmp/fsgg-arch-inventory-successor-source-20261013` | Root reviewed all changed code; final network collector SHA256b513e9a2c6561ebcf3b84ba68dd9a870a01db29ac4af10bf9dda100b74f9d1a3; **network NOT_RUN** |
| `/tmp/fsgg-arch-extra-db-signature-source-20261013-ce499ab5` | Owner-reported frozen source-only carrier; return SHA256b60710db91fd1d6def6ae7eeefe1358f5bb35e4745d7afddceb678ed2089904d; root source review/admission pending |
| `/tmp/fsgg-podman-arch-source-decision-20261013.md` | SHA256d30b70f6c7196065ec911b66a707473bffb6b7dc4e1d1a0c07eb0d6961c1b571; unvalidated sketch, not delivery |
| R/utel-material-plan/podman-consumer-joins-20261013/next-coordination-seam-decision.md | SHA2566eae1927b99b83431de1ec8577cd18436c51eace3aa97a1de76b3722108c2c9c; no independently justified production repair |

Authentic Arch donor tag `base-20261011.0.609700`, linux/amd64 manifest
`sha256:363129b67b2bc6c5a634f8fb4961a7e2eba213a2ec075d159127b1260290a722`;
index `sha256:3c64b538588416593f69a1aebd2f2ec0f2541b29159f32556fa7def2167147a0`;
config `sha256:5a30b4acc83ff841682eb21795e00f7ff030796f5e6fd2a127decd7a45ba7cef`.
Its source label is not an independently accepted source-publication Git join.
Layer0 compressed SHA256d2cc54e7adee435a81c8b997d4346ca05a3a9639d71e1169d6cd9fb959bedaac,
134054341 bytes, retained unchanged at
`/tmp/fsgg-arch-inventory-20261013/archives/layer-0.tar.gz`.
Layer1 SHA25655924c9eac0db11dd02b15ee1743734f402b3b22b794909b72b93f7afb7d64b6,
8724 bytes, **not acquired**. Expected diffIDs respectively
8beafa1582622f514d7271539dc9037c6ad015404f7ba4bd2c6d157f6ecb7b26 and
205bcf789d3f2506be63c097af493729123265ac8c3b50a71afef17d2a09fa8d.

Original metadata39965 failed on config redirect; successor45128 succeeded with
an explicit credential-free allowed CDN hop. Original inventory21501 failed on
`safe-tar-path` after three GETs; compressed layer0 is retained but its full diffID
and inventory were not accepted. Diagnostic70384 established the offending entry
was an unrelated regular nonsparse filename containing a literal backslash. The
successor ignores only that narrow unrelated class, retains unsafe selected-path,
ancestor, traversal, absolute, link and device refusals, and added ten regressions
to the original27. **All37 passed in the actual runtime.** Its next network operation
reads/reverifies retained layer0 first and downloads only layer1, at most three
GETs including token/registry/optional safehop, with a fresh64MiB output root.
Do not redownload the large blob, replay consumed grants or rewrite original evidence.
The intended new output `/tmp/fsgg-arch-inventory-successor-20261013` was not launched.

Original repository65808 failed only its8MiB encoded-index bound, after core.db200,
core.db.sig404 and extra.db200; extra.db.sig was never requested. Offline successor64614
raised only the justified encoded-index cap and passed without network:
core299 descriptors/665600 decoded bytes; extra15041/36567040 decoded bytes;
combined encoded11628107 bytes. Original retained raw core130338 bytes SHA256
015d34bc1196dd13f98bd58e301d566f45b16f656bbb8be3def91b3725496ecd;
extra8893798 bytes SHA2566257fcc963adfd914f299884eae839f3ef139daf7c9c4248257d9c959029e849.
DB TLS/hash metadata does not authenticate packages. The frozen extra.sig carrier
allows only its fixed October12 URL, one safehop and64KiB/max2GETs; no request occurred.
Actual donor SigLevel, trusted package verification and dependency closure remain pending.

Arch worktree `/tmp/fsgg-podman-arch-source-20261013`, branch
`routine/provisioned-podman-arch-source-20261013`, starts at M4. Five staged files:
Containerfile, README.md, sdk.sha512, pacman.conf under deployment/provisioned-podman,
and tests/provisioned-podman/test_source.py. **package-checksums.sha256 is absent**;
eight prepared source tests were not run and root has not reviewed all five draft
files. No source PR or image build exists. Current source-only returns must not be
invented into packet/item/attempt/dispatch joins. The recipe must join actual base
package state with a maintained full-Syu resolver, replacements/removals/conflicts/
hooks and trusted archives. A printed addition list alone is insufficient.
Exact pacman7.1 implementation privilege/NOLOCK/return-before-commit proof remains
unavailable; current native pacman/libalpm are outside the canonical runtime pins.
No pacman execution or package archive/signature validation occurred.

Noble alternatives are deliberately paused and preserved at
`/tmp/fsgg-podman-profile-source-20261013` and
`/tmp/fsgg-podman-sdk-source-20261013`, source commit
8e704e09d8762e23d4ca7a35ca58a0436d37a101. Actual five source controls passed before
Arch steering; SDK draft has four dirty/new files and eight unrun tests. SDK10.0.401
linux-x64 archive is **not acquired**; expected runtime10.0.12 and official SHA512
51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b.
Existing host SDK directories are not that portable archive contract.

## Publication, original holds and cleanup

UTEL0.103 original candidate remains source
4d136d5450a7032d0b87c4b8a37f69f020144792, run38290966724/a1,
artifact11766710761,32744064-byte ZIP SHA256
d8b22728b9e36932bf8d31e1ab486b312f2d1aedddf96660c872fc69f97f25ba.
Journal `utel-rel-21`, three coherent0.103.0 packages. Actual true-mode source
qualification **31/31 passed** (native13220, zeroGET/noCLR/noeffect); reuse it when
source is unchanged. Its root record resides under
R/utel-source/hosted-preflight-preparation/original103-publishtrue-route-preparation/
true-mode-copy-preparation/true31-root-qualification-preparation. False41 is
prepared only. **No TRUE/FALSE POST, PUBLIC4, publisher effect grant or new intent
started.** Publication is deferred until the selected Arch source landing and final
main correspondence; this save also moves main. Current FULL, current ordinary,
fresh account/entitlement/uniqueness evidence and PUBLIC4 within its300-second
window remain required; never repack the original candidate or renew expired clocks.

PROC installed-A/CTX source milestones in the previous handoff remain accepted;
PUBB namespace/write/Trusted Publishing entitlement remains UNKNOWN/disabled.
Routine process adoption and contained Linux acceptance remain separate. LEARN's
fixed Python image/ENV contract is separate from this outer development worker;
its historical Podman6.1.2, exact ELF/controller/provisioner provenance, C4/C5 fresh
stores/coldbuild/reload/custody and original teardown holds are not replaced by this
Arch proposal. Python journal/enrollment state remains durable under its existing
contract. Rendering original tag effect and SC2 original candidate/counterexample
custody remain UNKNOWN/missing, as in previous reports; no retries were admitted.

All current owned native attempts are terminal. At09:53:52, fresh full `/proc`
reconciliation found offline37 controller2838996/start55040820 and child2838997/
start55040857 absent, both owned process groups2838995/2838997 empty, no live
singletonCPU4 process and no census errors. Own CPU4 was released; prior ownedCPU3
was released after64614 cleanup09:42:49. No signal was necessary. Earlier failed
inventory21501, repository65808 and successful diagnostic70384 also retain root
fresh cleanup records. **No global process or external job clearance is claimed.**

Preserve all439 inherited reservations, including unknown CPU0/2/6/8 and later
CPU/GPU holds, plus the original PROC unadmitted FSI hold. CPU13 survivor chain,
CPU6 dbus custody, five historical LEARN teardown runs and prior Rendering jobs
remain unresolved. Preserve188 immutable pending telemetry intents, historical
832 dispatch states, inbox2/quarantine2 and exact-store schema14 identity from the
previous handoff. No begin/submit/drain/live-store mutation occurred. Tokens,
whole-family costs and baseline/context savings remain UNKNOWN; native logs are
not fabricated authenticated dispatch telemetry. Capacity errors do not release
holds. Known consumed operations stay single-use and expired observations stay
historical.

The unchanged canonical runtime prerequisite artifact is
R/utel-source/walltime-repair-preparation/root-canonical-source-delivery-admission.json,
SHA2563f58169267a38b438a6e83e9cbdf980fb45cc7f1c890c5ddd4908c7f87c84465,
2866 file pins/46 aliases. Fresh equality is required, never execution authority.
Root-owned operations use the existing guard/control functions and original
entry deadline before imports:120 work+30 cleanup,1GiB sampledRSS,1MiB logs,
controller plus at most16 known birth generations, explicit sampled disk ceiling
and owned cleanup. Sampling does not prove hard quotas or whole transient custody.
Anonymous public acquisitions exclude caller credentials/proxies; scoped anonymous
registry bearer/presigned URLs stay memory-only. A host rebuild invalidates relevant
runtime/closure observations and requires fresh qualification.

Handoff delivery preflight at10:00 UTC observed zero open PRs, one historical
queued schedule run37655507851 and no in-progress `.github` runs. That queued job
was preserved, not cancelled or claimed retired. The first handoff carrier failed
Python parsing before module/child execution; its original evidence is retained at
`/tmp/fsgg-stop-handoff-20261013-095258/collector-parse-original-failure.json`.
The corrected distinct preflight passed all five native reads and fresh cleanup.
Isolated worktree creation returned Git exit0 but its enclosing64MiB profile
failed the60MiB ordinary sampled allocated-byte threshold. The complete checkout
uses65,916,928 allocated bytes. The failed profile and fresh empty-group/birth
cleanup are retained; a distinct handoff-only128MiB disk profile is selected for
validation/delivery. It does not renew that failed operation or any roadmap grant.

The shared session checkout retains untracked `docs/reports/progressReports/` and
was not reset or cleaned. PD/actual-M3-delivery-worktree and all Arch/Noble/older
worktrees remain preserved. Handoff delivery uses a separate isolated worktree;
its final exact head, PR, resulting main and checks belong to the terminal response
and retained delivery record, not a predicted merge in this report.

## Ordered restart

1. Recover this handoff and previous unresolved lineage; inspect protected main,
   actual reservations, terminal owned cleanup and private evidence. Use existing
   owners and genuine packet/return joins; do not revive unknown operations.
2. Confirm whether the external host rebuilt this selected machine. Reconcile all
   changed tool/platform/mapping facts before native admission; keep provisioning
   separate from source acquisition. Obtain actual Containerfile/launch identity.
3. Independently finish Arch inventory using retained layer0 and only8724-byte
   layer1, and the frozen extra.sig metadata carrier. Reuse37 controls unchanged.
   Join actual base configuration/public keyrings with maintained resolver and
   authentic trusted package archives; qualify a coherent pinned Arch/SDK recipe.
4. Land only the completed source through ordinary PR/check/merge/settlement controls.
   After external rebuild, prove namespace, offline container execution, effective
   finite limits and normal/cancel cleanup. Then select real LEARN/PROC consumer
   joins with fresh owner contracts; source preparation alone is insufficient.
5. Resume original0.103 publication on the final source with fresh gates; preserve
   original candidate bytes and consume each operation once. Accept installed
   operational receivers/provider collection and owning projections separately.
6. Advance genuinely independent PROC/CTX and other v3/games owning work. Reopen
   LEARN/Rendering/SC2 only on authentic missing evidence. Mark Done only for whole
   accepted outcomes and land their owning closure plus compact index projection.

Last structured report was09:28 UTC (11:28 CEST); the next active report would
have been09:58 UTC. Reporting is now paused. No completion estimate or watcher
continues after this save. Programme execution remains paused.
