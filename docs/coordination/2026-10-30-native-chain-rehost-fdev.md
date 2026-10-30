# Re-hosting the native chain inside fdev

Status: proposed owning plan for Root decision. No native run, VM, provisioner action,
clock, lease, claim or Original is started, selected or authorized by this document.
Owning issue: [.github#4778](https://github.com/FS-GG/.github/issues/4778); parent
[#4650](https://github.com/FS-GG/.github/issues/4650) (V3-ENV-01); first consumer
[FS.GG.Coordination#1032](https://github.com/FS-GG/FS.GG.Coordination/issues/1032).

## Context

On 2026-10-30 Root withdrew native selection `d2f2171d7c5d55aacdc560a09e19b46a`, which
never started ([#4778 decision](https://github.com/FS-GG/.github/issues/4778#issuecomment-6518659982),
[#4517 decision](https://github.com/FS-GG/.github/issues/4517#issuecomment-6518659566)).
It bound Home-side facts: boot id, Root PIDs, tmux pane `%2`, MemAvailable and the B0
backing. The fdev migration changed those facts
(Atlas `environment/fdev-kvm-vm`, `environment/home-subsystems-migrated-into-fdev`).
Root directed that the shared chain move inside fdev:
runner → export → genuine bake → registration → first BAR.

**Security zone.** The repository owner ruled on 2026-10-30 that protection inside fdev
after the migration is cooperative only, intentionally, and that the fdev VM is the only
relevant security zone. Home enforces that boundary through the VMM container and the
egress filter. Root records the ruling as Atlas
`environment/fdev-vm-is-the-only-security-zone` (.github PR #4942, pending review).
This plan therefore designs no in-fdev replacement for Home's Root-owned 0400 files and
separate Root console.

Live read-only facts observed on fdev on 2026-10-30:

- AMD Ryzen 9 7900 guest with 12 vCPUs and 31 GiB RAM, no swap;
- `/dev/kvm` present (mode 0666), nested SVM enabled, QEMU 11.1.2 and `passt` installed;
- cgroup v2 with `cpuset cpu io memory pids` enabled at the root;
- `/srv/v3-pool` is `vdb`: ext4, 2.0 TiB, 13 inodes used, empty `operations/`;
- `/srv/bulk` is `vdf`: ext4, 4 TB, shared by fsgg-store, the LEARN account home and the SC2 libraries;
- `/home/developer` is `vde`: 492 GB, 420 GB free;
- `/` is regenerated from the image on every upgrade.

## Recommended design in one paragraph

Root, or an agent Root selects, runs each step as one systemd transient unit in a
dedicated top-level slice. The unit runs as `developer` and owns one disposable QEMU
guest. `RuntimeMaxSec` enforces the step's useful clock and `TimeoutStopSec` enforces
the retire clock. Disks are fully preallocated on `/srv/v3-pool`. Custody is
cooperative: content hashes, one claim per operation, no replay, original clocks and an
independent readback. The first selectable milestone (M1) is the genuine bake with
export, which already covers runner → export → bake. Registration and the first BAR
follow as M2.

## 1. Custody under the cooperative ruling

Inside fdev, file modes, `sudo` and process separation are cooperative hygiene, not a
security boundary. The plan keeps the parts of custody that make evidence usable and
operations unrepeatable:

| Cooperative control | Mechanism |
|---|---|
| Input identity | The input set is built into one ext4 image with `mke2fs -d`. Its SHA-256 is recorded in the selection packet. The guest gets it read-only, so the guest cannot change it by accident. |
| Result identity | After QEMU exit is observed, the collector hashes the export. Root posts the closed packet digest on the owning issue before any later step uses it. In M2, the BAR job's own log on GitHub also carries the job's result digests. |
| Single claim, no replay | See [Execution](#single-claim-and-no-replay). |
| Original clocks | See [Clocks](#clocks). |
| Independent readback | An actor other than the one who executed checks four things: the packet against the posted digests, unit and cgroup absence, disk disposition, and runner absence through the API (M2). |

**What this plan no longer claims.**

- It claims no tamper-proofing against agents or fdev root. Hashes, claims and clocks
  detect accidents and keep the record coherent. Anyone with root in fdev can defeat
  them.
- It requires no nested VM for custody, no separate no-sudo executor uid, and no
  dm-verity, fs-verity, vTPM or guest-side input measurement.
- It does not re-create Home's 0400 source custody, Root console, pane or PID guards,
  or the human operator gate.
- The evidence shows what a cooperative fdev observed. It is not independent of fdev or
  Home.

Nested guests stay in the design for other reasons. The source contract requires them:
an offline image-build VM for the bake, and one disposable privileged guest per job for
the runner, with passwordless root inside the guest (`runner.sudoers`). They provide
disposability and a clean, reproducible environment, not custody.

## 2. Execution

### Placement and helper reuse

| Chain step | Runs where | Carried over unchanged | New or changed |
|---|---|---|---|
| Input acquisition | An ordinary fdev process into a fresh directory. It uses public URLs and no credentials (ADR-0096 A). | `noble-inputs.py acquire/verify`, `noble/acquired-input-lock.json` | none |
| Genuine bake, with export | An offline nested image-build guest in unit `fsgg-native-<op>-bake` | `provision-image.sh --apply` (which runs `validate-inputs`), `verify-guest --phase image`, the noble APT/SDK/Go/runner locks; the [#4820](https://github.com/FS-GG/.github/issues/4820) journal producer for Commit (Coordination PR1059 lineage, whose runtime is fdev's installed .NET 10.0.12) | the launcher supplies the root-only `/run/fsgg-image-build-selection.json` inside the guest |
| Registration | (a) A reviewed Coordination PR records the baked image identity. (b) One JIT runner registration on `FS.GG.Native.Qualification`. | `guest-boot.sh`, `run-jit-runner.sh`, `binding_from_request` | `portable_workspace_sc2.py` sets `DEDICATED_IMAGE_SHA256` and `DEDICATED_PROVIDER_IMAGE_ID`. The README provider row becomes `fdev-kvm-qualification-v1`. |
| First BAR | A fresh runner guest from the qualified image as a throwaway overlay. Root dispatches `bar-hosted-image-qualification.yml` with `purpose=full-image`. | `bar_hosted_operation.py`, `bar_hosted_domain.py`, the caller workflow and its 90-minute job budget | none |

The **fdev launcher** is new source in FS.GG.Coordination under
`deployment/qualification-runner/fdev/`. It has three parts:

- a claim front end, adapted from `home-d2f2-run.py`. It keeps the one-time claim,
  closed result packaging and observe-only resume. It drops the tmux paste, the Root
  PID, pane and boot guards, the hash-pinned loader and the human TTY gate.
- a pure builder for the unit, slice and QEMU argv;
- a collector and retirement helper, adapted from `home-d2f2-inspect.py` and
  `home-d2f2-retirement.py`.

The Home B0 cohort is retained as history and not carried over. That cohort is
`final_operator.py`, the assembler, the receiving static, the 41 Root-0400 runtime files
and the 454 payload pins. It existed to cross Home's ordinary-to-Root boundary, and
fdev has no such boundary. These lessons carry over:

- systemd manager-state retention (`ExitType=cgroup`, `RemainAfterExit`,
  `CollectMode=inactive`) from the
  [28 Oct decision](2026-10-24-trusted-home-execution-decisions.md#administrative-preparation-boundary--28-october);
- `OOMPolicy=continue` (Atlas `platform/systemd-oom-policy-public-contract`);
- bounded stream drain and first-failure preservation;
- terminal state is not EOF, and absence is not success.

### Clocks

The clock structure of the d2f2 selection stays: a pre-start window, then one Original
per step made of useful time plus retire time, then a collector with its own Original
inside an enclosing bound. No clock is renewed, extended, combined or reset.

| Clock | Enforcement | M1 bake | M2 BAR guest |
|---|---|---|---|
| Pre-start (claim → unit active) | the launcher refuses past the cut | 300 s | 300 s |
| Original useful | `RuntimeMaxSec`. On expiry, `ExecStop` sends QMP `system_powerdown`. | 4 800 s | 6 600 s (90-min job + boot/registration) |
| Original retire | `TimeoutStopSec`, then SIGKILL with `KillMode=control-group` | 600 s | 900 s |
| Collector useful + retire | its own transient unit | 900 + 60 s | 1 800 + 120 s |
| Enclosing (all of the above + 120 s tail) | the launcher refuses dependent steps past it | 6 780 s | 9 840 s |

Each clock starts at the manager's `ActiveEnterTimestampMonotonic` and is
cross-checked against UTC and the GitHub selection-comment timestamp. If they diverge by
more than 5 s, the step stops as Unknown. This covers Home pausing the fdev VM, which
fdev cannot observe. An fdev reboot or upgrade during a step ends it as FAILED or
Unknown. It is never resumed. The values above are proposals for Root to bind in each
selection.

### Single claim and no replay

- Before any effect, the launcher creates the claim
  `/srv/v3-pool/native/claims/<op>` with `O_CREAT|O_EXCL|O_NOFOLLOW`. An existing claim
  refuses forever.
- The launcher refuses the retired identities 83a08, 69db, 2ae4 and d2f2 (full IDs in
  #4778) and any operation ID that is absent from the Root selection comment.
- Each step's unit name is unique. `CollectMode=inactive` retains a failed unit's name
  and metadata until collection.
- After the first failure, an Unknown, determining-input drift or an exhausted bound,
  dependent steps stop. There is no retry, re-dispatch or replacement-ID replay. Any
  continuation needs a new explicit Root selection with fresh clocks.

### Retirement, cleanup and independent readback

Retirement runs in this order, inside the step's retire and collector clocks:

1. Observe QEMU exit: unit inactive, `ExecMainStatus`, and `cgroup.events populated 0`.
2. The collector hashes the output, writes the closed packet and makes the journal
   Commit.
3. Root posts the packet digest on the owning issue.
4. Delete the scratch disk and overlay. On failure, keep them as a *named held residue*
   of that operation instead.
5. Check runner absence through the API (M2).
6. Revert the window's runtime-only slice properties.
7. Another actor reads back every item above.

## 3. Write domains and capacity

| Domain | Use | Reason |
|---|---|---|
| `/srv/v3-pool` (`vdb`) | The only native write domain: `native/{claims,inputs,images,runs/<op>,evidence}` | A dedicated block device inside fdev, with no current data writers. `provision.py create_pool` fully allocated `pool.img` on Home. |
| `/srv/bulk` (`vdf`) | none | shared with fsgg-store, the LEARN home and the SC2 libraries |
| `/home/developer` (`vde`) | source checkouts and small evidence copies only | agent worktrees and caches compete for it |
| `/` (`vdd`) | transient unit state only | regenerated on upgrade |
| `/srv/sc2-reserved` (`vdc`) and LEARN `vda` | excluded | SC2-only reserved operation e995b57e, which #4778 forbids borrowing; LEARN qualification |

**Mapping to #4778 Done when, option (b): an independently provisioned resource domain.**
Capacity enforcement here is kernel accounting. Write exclusion is cooperative, as the
ruling states.

- *Bytes:* each run's guest disks are fully preallocated with `fallocate` before start.
  That is 256 GiB scratch, a 64 GiB output disk and the RO input image. Other writers on
  `vdb` can consume only free space that was never allocated. They cannot take the
  reserved blocks.
- *Inodes:* the guest filesystems are independent. On the host side each run uses a
  handful of inodes on `vdb`, which has 134 M free.
- *Writes:* old writers have no path into the run directory. QEMU holds the image locks.
  Both are cooperative.
- *Backing identity:* the evidence records the filesystem UUID and label
  (`systemadmin-v3`), the device, and each file's device, inode, size and allocated
  blocks.
- *Lifecycle:* see the retirement order above.
- *Rollback:* stop the unit and delete the run directory. The pool and other domains are
  untouched.
- *Refusal:* the launcher refuses if free space on `vdb` is below the run's need plus
  64 GiB headroom, or if a v3 pilot unit (`v3-storage@*`) is active. Root admits no v3
  pilot start while a native window is open.

**CPU, memory and I/O admission.** fdev is shared with agent lanes, fsgg-store
(uid 30033) and LEARN (uid 30034).

- *Guest and slice:* the guest uses 4 vCPUs and 16 GiB, preallocated with
  `-mem-prealloc`. The unit runs in a top-level system slice, `fsgg-native.slice`, so
  that its memory protection takes effect. A slice under `user.slice` would inherit a
  zero effective `memory.min`. The slice sets:
  - `AllowedCPUs=8-11`;
  - `MemoryMin=18G` and `MemoryMax=19G` (the guest plus a 2 GiB VMM and collector margin);
  - `TasksMax=512`;
  - `memory.oom.group`.
- *Pre-start admission:*
  - `MemAvailable` ≥ 24 GiB (26 GiB was observed during this planning read);
  - free space on `vdb` as above;
  - no other `fsgg-native-*` unit active.
- *Agent lanes:* for the window, Root sets runtime-only properties on `user-1000.slice`:
  `AllowedCPUs=0-7` and `MemoryHigh=10G`. Root also limits lanes to one heavy build.
- *Cleanup:* retirement reverts every runtime property.
- *I/O:* the `io` controller allows `IOWeight` between slices. Physical contention on
  Home's HDD, which holds the `vdb`, `vdf`, `vdc` and `vda` images, cannot be controlled
  from fdev. It affects timing, not correctness.

The 4 vCPU / 16 GiB / 256 GiB profile is the one the source already selects
(Coordination `deployment/qualification-runner/README.md`). The BAR gate still requires
14 GiB of actually available guest memory and 80 GiB of free backing, and those
observations are made inside the guest. fdev's virtio-gpu and PipeWire devices
(Atlas `environment/fdev-gpu-and-audio`, PR #4942) are not used by this headless chain
and are not passed into nested guests.

**Holds.**

- Nothing is released or replayed. The consumed originals 83a08, 69db and 2ae4 and the
  retired d2f2 stay as recorded. Home's `/var/lib/b0` and recovery parents stay
  Home-held.
- The five old v3 lease records name Home's former mount path. They are not modified
  and keep their own Unknown scope.
- The new domain is demonstrably isolated in four ways: a different filesystem, fresh
  identities, fresh clocks and no shared engine store. Under ADR-0096 B the 439 + 46
  holds therefore do not fence it. This plan observed no data writer on `vdb`. Any
  writer found at admission refuses the run.

## 4. First-BAR consumer and acceptance

| Owning record | This path can establish | This path cannot establish |
|---|---|---|
| #4778 | The option (b) decision with actual capacity enforcement observed in a run: identity, bytes, inodes, CPU and memory correspondence, refusal controls, lifecycle and independent retirement. Write exclusion is cooperative under the ruling. | Home physical capacity and I/O correspondence; recovery of the historical B0 originals. |
| #4650 (V3-ENV-01) | For the fdev nested profile: enforcing execution, committed export, independent cleanup, a qualified launcher source and its first genuine use. | Unattended allocation without a human or host agent, worker-loss recovery, reboot persistence, publication or installed adoption, benefit disposition. |
| Coordination#1032 | The complete pinned FSBar headless-integration image from current accepted source, if M2 succeeds. That covers the manifests, image identity, exported archive, static ELF correspondence, bounded outputs, readback and retirement. | Loaded engine/plugin ABI, Count1 and the useful-play journeys ([#4842](https://github.com/FS-GG/.github/issues/4842)), product publication, installed B2 adoption. |
| [#4821](https://github.com/FS-GG/.github/issues/4821) | Nothing. | Two independent allocations. Guests on one fdev share a kernel and disks, which the 2026-10-24 ruling excludes. This stays separate. |

## 5. Milestones

**M0. Launcher source.** This is source only, plus ordinary verification under ADR-0096 A.
It boots no guest.

- *Scope:* one FS.GG.Coordination PR adding `deployment/qualification-runner/fdev/`
  (about 500–800 lines of Python plus pure tests), with these contents:
  - the claim front end;
  - the unit, slice and QEMU argv builder;
  - the collector and retirement helper;
  - a README amendment adding provider `fdev-kvm-qualification-v1`.
- *Controls:* claim races, a retired-ID refusal, clock arithmetic, argv golden files,
  and refusal on missing free space or memory.
- *Also confirmed in M0:* that the 4820 producer closure runs on fdev's installed
  .NET 10.0.12.
- *Review:* one independent source review and exact-head CI.

**M1. First selectable milestone: genuine bake with export.** This is one Root
selection with one Original.

- *Prerequisites:*
  - M0 merged;
  - noble inputs acquired and verified in a fresh directory, with the lock digests
    matching;
  - Root decisions 1–3 below.
- *Journey:*
  1. Build the input image and record its digest.
  2. Claim the operation.
  3. Preallocate the disks.
  4. Start the unit.
  5. Boot the signed noble cloud image (lock-pinned) offline, with 4 vCPUs and 16 GiB.
  6. Run `provision-image.sh --apply` and `verify-guest --phase image` inside the
     guest.
  7. Power off and observe the exit.
  8. Collect the qualified image and receipts, make the Commit and post the digest.
  9. Retire, then run the independent readback.
- *Size:* about 2 h wall, 4 vCPU and 18 GiB for up to about 90 min, and about 350 GiB
  on `vdb`. The scratch space is freed at retirement unless held. The qualified image
  is retained as M2's input.
- *Establishes:* runner → export → genuine bake on a real nested run, with clocks,
  admission and the #4778 capacity domain observed. It does not establish registration
  or BAR.

**M2. Registration → first BAR.** This is one later Root selection with two ordered
steps:

1. the registration PR in Coordination (normal review and CI), then one JIT
   registration under a separately admitted credential use;
2. one runner guest, one dispatch of `full-image` at a pinned `coordination_sha`, export
   and teardown, with runner-absence readback.

Its native clock is about 2.7 h, plus registration-PR review time. It reuses the
launcher unchanged, unless M1 exposes a determining defect.

## Decisions only Root or the user can make

1. **Shared-machine clause.** The runner README says "the shared Codex machine is
   forbidden". Decide whether a disposable nested guest on fdev is permitted, with fdev
   as host.
2. **Memory.** Decide between throttling agent lanes during native windows (about 10 GiB
   for lanes while the guest holds 18 GiB, with no swap) and asking Home to enlarge the
   fdev VM. VM lifecycle is Home's.
3. **Write domain.** Decide whether `/srv/v3-pool` becomes the native domain, which
   pauses v3 pilot starts during windows, or Home attaches a dedicated native disk, as it
   did with `fdev-sc2`.

## Unknowns

- How Home physically backs fdev's 32 GiB, and HDD contention between the attached
  images.
- Guest clock behaviour while Home pauses fdev.
- Whether nested-SVM performance fits the bake clock and the BAR job's 90-minute budget.
  A timeout would be a retained first failure, not a reason to extend the clock.
- Whether a JIT runner self-updates.
- Whether `bar_hosted_domain.py` accepts the fdev provider facts without a further
  source change.

## Not selected by this plan

This plan selects none of the following: an operation, clock, claim, credential use,
provisioner or lease action, Home action, slice change, retirement, or release of any
hold. Source merge of this plan or of M0 establishes no native, installed or consumer
acceptance.
