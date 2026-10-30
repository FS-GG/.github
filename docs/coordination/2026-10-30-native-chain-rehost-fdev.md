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

**Security zone and owner.** The repository owner ruled that protection inside fdev is
cooperative only, intentionally, and that the fdev VM is the only relevant security zone
([durable record](https://github.com/FS-GG/.github/issues/4778#issuecomment-6519522997);
Atlas `environment/fdev-vm-is-the-only-security-zone`, merged in .github `37028dfd`).
Home enforces that boundary through the VMM container and the egress filter. Under that
ruling, [Root decision 1](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725)
makes **fdev the trusted owner for this route**. The 2026-10-24 Home-only export and
trusted-owner exception does not carry over automatically. This plan designs no in-fdev
replacement for Home's Root-owned 0400 files and separate Root console.

Live read-only facts observed on fdev on 2026-10-30:

- AMD Ryzen 9 7900 guest with 12 vCPUs and 31 GiB RAM, no swap;
- `/dev/kvm` present (mode 0666), nested SVM enabled, QEMU 11.1.2 with the built-in
  `vvfat` block driver, `qemu-img` and `passt`;
- `mkfs.fat`, `mcopy`, `xorriso` and `cloud-localds` are not installed;
- cgroup v2 with `cpuset cpu io memory pids` enabled at the root;
- `/srv/v3-pool` is `vdb`: ext4, 2.0 TiB, 13 inodes used, empty `operations/`;
- `/srv/bulk` is `vdf`: ext4, 4 TB, shared by fsgg-store, the LEARN account home and the
  SC2 libraries;
- `/home/developer` is `vde`: 492 GB, 408 GB free;
- `/` is regenerated from the image on every upgrade;
- .NET runtimes 10.0.11 and 10.0.12 are installed.

## Recommended design in one paragraph

Root, or an agent Root selects, runs each step as one systemd transient system unit in
a dedicated top-level slice. The unit runs as `developer` and owns one disposable QEMU
guest. Every guest disk is a raw file preallocated in full on `/srv/v3-pool`. No disk
is a growing overlay, and guest TRIM cannot deallocate blocks. Custody is cooperative:
content hashes, one claim per operation, no replay, original clocks and an independent
readback. Durable exports and the Commit journal go to `/srv/bulk/native-chain/`. The
first selectable milestone (M1) acquires the inputs, runs the genuine offline bake and
exports the qualified image. That covers runner → export → bake. Registration and the
first BAR follow as M2, a separate later selection.

## 1. Custody under the cooperative ruling

Inside fdev, file modes, `sudo` and process separation are cooperative hygiene, not a
security boundary.

| Cooperative control | Mechanism |
|---|---|
| Input identity | The input set is built into one ext4 image with `mke2fs -d`. Its SHA-256 is recorded in the selection packet and checked before the unit starts. The guest gets it read-only. |
| Result identity | After QEMU exit is observed, the collector hashes the export. Root posts the closed packet digest on the owning issue before any later step uses it. In M2, the BAR job's log on GitHub also carries the job's result digests. |
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

Nested guests stay in the design because the source contract requires them, not for
custody. The contract asks for an offline image-build VM for the bake. It also asks for
one disposable privileged guest per job for the runner, with passwordless root inside
the guest (`runner.sudoers`).

## 2. Execution

### Placement and helper reuse

| Chain step | Runs where | Carried over unchanged | New or changed |
|---|---|---|---|
| Input acquisition | The M1 selection's first Original, into a fresh directory on `vdb` ([Root decision 4](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725)) | `noble-inputs.py acquire/verify`, `noble/acquired-input-lock.json` | none |
| Genuine bake, with export | An offline nested image-build guest. See [M1 bake layout](#m1-bake-layout). | `provision-image.sh --apply` (which runs `verify-image-target` and `validate-inputs`), `verify-guest --phase image`, the noble APT/SDK/Go/runner locks; the [#4820](https://github.com/FS-GG/.github/issues/4820) journal producer for Commit (Coordination PR1059 lineage) | the launcher, the input and control disks, and the NoCloud seed |
| Registration | (a) A reviewed Coordination PR records the baked image identity, before M2 is selected. (b) Inside M2, one JIT runner registration on `FS.GG.Native.Qualification`. | `guest-boot.sh`, `run-jit-runner.sh`, `binding_from_request` | `portable_workspace_sc2.py` sets `DEDICATED_IMAGE_SHA256` and `DEDICATED_PROVIDER_IMAGE_ID`. The README provider row becomes `fdev-kvm-qualification-v1`. |
| First BAR | A fresh runner guest on a preallocated copy of the qualified image. Root dispatches `bar-hosted-image-qualification.yml` once. | `bar_hosted_operation.py`, `bar_hosted_domain.py`, the caller workflow | none |

The **fdev launcher** is new source in FS.GG.Coordination under
`deployment/qualification-runner/fdev/`. It has three parts:

- a claim front end, adapted from `home-d2f2-run.py`. It keeps the one-time claim,
  closed result packaging and observe-only resume. It drops the tmux paste, the Root
  PID, pane and boot guards, the hash-pinned loader and the human TTY gate.
- a pure builder for the units, slices, disks and QEMU argv;
- a collector and retirement helper, adapted from `home-d2f2-inspect.py` and
  `home-d2f2-retirement.py`.

The adapted helpers come from private custody and name Home paths, accounts and PIDs.
They need a privacy review before they enter the public repository.

The Home B0 cohort is retained as history and not carried over. That cohort is
`final_operator.py`, the assembler, the receiving static, the 41 Root-0400 runtime files
and the 454 payload pins. It existed to cross Home's ordinary-to-Root boundary, and
fdev has no such boundary. These lessons carry over:

- systemd manager-state retention from the
  [28 Oct decision](2026-10-24-trusted-home-execution-decisions.md#administrative-preparation-boundary--28-october):
  `Type=exec`, `ExitType=cgroup`, `RemainAfterExit=yes`, `Restart=no` and
  `CollectMode=inactive`. The unit keeps its metadata in active/exited or failed state
  for external collection.
- OOM handling only through the public `OOMPolicy` property (Atlas
  `platform/systemd-oom-policy-public-contract`). There is no transient
  `MemoryOOMGroup` or `memory.oom.group` setter, and the 69db refusal came from exactly
  that mistake.
- bounded stream drain and first-failure preservation;
- terminal state is not EOF, and absence is not success.

### Units, OOM and the collector

- *Guest unit.* `fsgg-native-<op>-<step>.service` runs in `fsgg-native.slice`. Its
  `ExecStart` is QEMU. Its `ExecStop` sends QMP `system_powerdown` and then waits for
  the QEMU main PID to exit. The unit's `TimeoutStopSec` bounds that wait. After
  `TimeoutStopSec`, systemd sends SIGKILL to the whole control group
  (`KillMode=control-group`).
- *OOM.* The guest unit sets `OOMPolicy=kill`. If the kernel OOM-kills any process in
  the unit, systemd kills the whole unit, including QEMU and `passt`. The unit then
  enters failed state with result `oom-kill`, and `CollectMode=inactive` retains it. The
  kernel's `memory.oom.group` value is read back as a typed observation only.
- *Collector.* The collector is a separate unit in its own slice,
  `fsgg-native-collect.slice`, with `MemoryMax=1G`. It starts only after the guest unit
  is observed inactive. An OOM in the guest slice therefore cannot take the collector
  with QEMU.

### Clocks

The clock structure of the d2f2 selection stays: a pre-start window, then one Original
per step made of useful time plus retire time. The collector has its own Original. All
of them sit inside one enclosing bound. No clock is renewed, extended, combined or
reset.

| Clock | In-run enforcement | M1 acquisition | M1 bake | M1 collector | M2 BAR guest | M2 collector |
|---|---|---|---|---|---|---|
| Pre-start (claim → unit active) | the launcher refuses past the cut | 300 s | 300 s | — | 300 s | — |
| Original useful | `RuntimeMaxSec` | 1 800 s | 4 800 s | 900 s | 6 600 s | 1 800 s |
| Original retire | `TimeoutStopSec`, then SIGKILL | 60 s | 600 s | 60 s | 900 s | 120 s |
| Enclosing, from the start witness | refused at each step boundary | 8 940 s for M1 as a whole | | | 9 840 s for M2's guest and collector | |

The two clock sources are kept separate:

- **fdev-observed time.** `RuntimeMaxSec` and `TimeoutStopSec` run on fdev's
  `CLOCK_MONOTONIC`, from the manager's `ActiveEnterTimestampMonotonic`. They bound how
  long a unit runs *as fdev observes it*.
- **External real time.** GitHub timestamps serve as the real-time witness:
  - *Start witness:* when the claim is made, the launcher posts a short claim comment on
    the owning issue. That comment's `created_at` is the start witness.
  - *Boundary checks:* at each step boundary, before the next unit starts, the launcher
    reads the `Date` header of a GitHub API response. It refuses the next step if the
    enclosing bound measured from the start witness has passed.
  - *End witness:* the `created_at` of Root's result post.

**What bounds real time.** Nothing inside fdev bounds real time if Home pauses or
suspends the fdev VM. Whether fdev's monotonic clock advances during such a pause is
Unknown. The bound is applied after the fact. If the end witness minus the start witness
exceeds the enclosing bound plus 300 s for posting, the operation is recorded as having
exceeded its real-time bound. A result that exceeds it is not accepted as
qualification, whatever the monotonic outcome. An fdev reboot or upgrade during a step
ends it as FAILED or Unknown. It is never resumed. The values above are proposals for
Root to bind in each selection.

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

1. Observe QEMU exit: unit inactive or failed, `ExecMainStatus`, `Result`, and
   `cgroup.events populated 0`.
2. The collector hashes the output, writes the closed packet and makes the journal
   Commit under `/srv/bulk/native-chain/`.
3. Root posts the packet digest on the owning issue. This is also the end witness.
4. Delete the run disks on `vdb`. On failure, keep them as a *named held residue* of
   that operation instead.
5. Check runner absence through the API (M2).
6. Another actor reads back every item above.

The launcher changes no properties on `user-1000.slice`. Root drains other lanes for
the window instead (see [capacity](#3-write-domains-and-capacity)). The launcher front
end, the readback actor and the Root session keep running unthrottled.

## M1 bake layout

| Disk | Contents | How it is made | Guest access |
|---|---|---|---|
| `vda`, root and output | the signed noble cloud image, which becomes the qualified image | `fallocate -l 256G root.raw`, then `qemu-img convert -n --target-is-zero -f qcow2 -O raw <verified noble .img> root.raw`. `-n` writes into the preallocated file and `--target-is-zero` skips zero writes, so no extent is deallocated. cloud-init `growpart` expands the partition at first boot. | read-write |
| `vdb`, inputs | the acquired input tree, plus a Coordination checkout at the pinned source SHA | `mke2fs -d`. Its digest is checked before start. The guest mounts it at `/mnt/fsgg-qualification-inputs`, the path `provision-image.sh` requires. | read-only |
| `vdc`, control | `TARGET.json` (`fsgg.dedicated-qualification-target/1`), `fsgg-image-build-selection.json` (`fsgg.dedicated-image-build-selection/1`) and `bake.sh` | `mke2fs -d`. Its digest is checked before start. | read-only |
| NoCloud seed | `meta-data` (a fresh instance id) and a minimal `user-data` | QEMU's built-in `vvfat` driver, `label=CIDATA`, `read-only=on`. No host tool or guest package is needed. | read-only |

- *Inventory.* `user-data` installs no packages and sets no `package_update`, so the
  dpkg inventory stays exactly the 717 locked packages. A `bootcmd` masks `apt-daily`,
  `apt-daily-upgrade` and `unattended-upgrades` before their timers can fire. A
  `runcmd` executes `/mnt/control/bake.sh`. The guest has no network device
  (`-nic none`).
- *Selection.* `bake.sh` copies the selection to `/run/fsgg-image-build-selection.json`
  as root, mode 0600, on tmpfs. It then runs `provision-image.sh --apply TARGET.json
  <lock> /mnt/fsgg-qualification-inputs`, unmounts and powers off. The selection never
  reaches the root filesystem.
- *DMI UUID.* The launcher chooses a fresh UUID and passes it with `-uuid`, so it
  appears as `/sys/class/dmi/id/product_uuid`. The same UUID is written into both
  `TARGET.json` and the selection, which `verify-image-target` cross-checks.
- *Disk options.* Every QEMU disk uses `cache=none`, `aio=native`, `discard=ignore` and
  `detect-zeroes=off`.
- *Image identity.* After a clean poweroff, `DEDICATED_IMAGE_SHA256` is the SHA-256 of
  the complete 256 GiB raw `root.raw` byte stream. Unwritten extents read back as zeros
  without device I/O.
- *Export.* The collector copies `root.raw` with `cp --sparse=always` to
  `/srv/bulk/native-chain/<op>/qualified-root.raw` and re-hashes the copy. Both digests
  must match. `DEDICATED_PROVIDER_IMAGE_ID` is `fdev-kvm-qualification-v1/<op>`.
- *Target field.* `TARGET.json` must state `sharedCodexHost` truthfully. See open
  decision 1.

## 3. Write domains and capacity

| Domain | Use | Reason |
|---|---|---|
| `/srv/v3-pool` (`vdb`) | Run working state only: `native/{claims,inputs,runs/<op>}` and the preallocated guest disks | A dedicated block device inside fdev, with no current data writers. `provision.py create_pool` fully allocated `pool.img` on Home. |
| `/srv/bulk/native-chain/` (`vdf`), mode 0700, owner `developer` | The durable Commit and journal destination, closed result packets and the exported qualified image ([Root decision 1](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725)) | A persistent disk. Exports are sparse copies of about 10–20 GiB. |
| Rest of `/srv/bulk` | none | shared with fsgg-store, the LEARN home and the SC2 libraries |
| `/home/developer` (`vde`) | source checkouts and small evidence copies only | agent worktrees and caches compete for it |
| `/` (`vdd`) | transient unit state only | regenerated on upgrade |
| `/srv/sc2-reserved` (`vdc`) and LEARN `vda` | excluded | SC2-only reserved operation e995b57e, which #4778 forbids borrowing; LEARN qualification |

**Mapping to #4778 Done when, option (b): an independently provisioned resource domain.**
Capacity enforcement here is kernel accounting. Write exclusion is cooperative, as the
ruling states.

- *Bytes:* every guest disk is a raw file preallocated in full before start: 256 GiB
  for the root, plus the read-only input and control images. With `discard=ignore` and
  `--target-is-zero`, nothing deallocates those blocks during the run. Other writers on
  `vdb` can consume only free space that was never allocated.
- *Inodes:* the guest filesystems are independent. On the host side each run uses a
  handful of inodes on `vdb`, which has 134 M free.
- *Writes:* old writers have no path into the run directory. QEMU holds the image locks.
  Both are cooperative.
- *Backing identity:* the evidence records the filesystem UUID and label
  (`systemadmin-v3`), the device, and each file's device, inode, size and allocated
  blocks.
- *Lifecycle and rollback:* see the retirement order above. Rollback means stopping the
  unit and deleting the run directory.
- *Refusal:* the launcher refuses if free space on `vdb` is below the run's need plus
  64 GiB headroom, or if a v3 pilot unit (`v3-storage@*`) is active. Root admits no v3
  pilot start while a native window is open.
- *I/O correspondence stays open.* Physical I/O contention on Home's HDD, which holds
  the `vdb`, `vdf`, `vdc` and `vda` images, cannot be observed or controlled from fdev.
  Per [Root decision 2](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725),
  this path does not claim I/O correspondence. #4778 closes only with genuine evidence
  from Home or later, or by an explicit later ruling.

**CPU and memory admission** ([Root decision 3](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725)):

- *Guest and slice:* the guest uses 4 vCPUs and 16 GiB, preallocated with
  `-mem-prealloc`. `fsgg-native.slice` is a top-level system slice. A slice under
  `user.slice` would inherit a zero effective `memory.min`. The slice sets
  `AllowedCPUs=8-11`, `MemoryMin=18G`, `MemoryMax=19G` and `TasksMax=512`.
- *Admission:* `MemAvailable` ≥ **26 GiB** at pre-start: a 16 GiB guest, a 2 GiB margin
  and 8 GiB kept for the host. This follows the reused source contract.
- *Lanes:* a native window therefore requires the other fdev agent lanes to be paused
  or drained. Root schedules that.
- *Other checks:* free space on `vdb` as above, and no other `fsgg-native-*` unit
  active.

The 4 vCPU / 16 GiB / 256 GiB profile is the one the source already selects. The target
schema enforces it as floors. The BAR gate's 14 GiB of available guest memory and 80 GiB
of free backing are observed inside the guest. fdev's virtio-gpu and HDA/PipeWire audio
(Atlas `environment/fdev-gpu-and-audio`) are not used by this headless chain and are not
passed into nested guests. They do widen fdev's host-facing surface; see prerequisites.

**Holds.**

- Nothing is released or replayed. The consumed originals 83a08, 69db and 2ae4 and the
  retired d2f2 stay as recorded. Home's `/var/lib/b0` and recovery parents stay
  Home-held.
- The five old v3 lease records name Home's former mount path. They are not modified
  and keep their own Unknown scope.
- The new domains are demonstrably isolated in four ways: different filesystems, fresh
  identities, fresh clocks and no shared engine store. Under ADR-0096 B the 439 + 46
  holds therefore do not fence them. This plan observed no data writer on `vdb`. Any
  writer found at admission refuses the run.

## 4. First-BAR consumer and acceptance

| Owning record | This path can establish | This path cannot establish |
|---|---|---|
| #4778 | The option (b) decision with capacity enforcement observed in a real run: identity, bytes, inodes, CPU and memory correspondence, refusal controls, lifecycle and independent retirement. Write exclusion is cooperative. | I/O correspondence, which stays open (decision 2); Home physical capacity; recovery of the historical B0 originals. |
| #4650 (V3-ENV-01) | For the fdev nested profile: enforcing execution, committed export, independent cleanup, a qualified launcher source and its first genuine use. | Unattended allocation without a human or host agent, worker-loss recovery, reboot persistence, publication or installed adoption, benefit disposition. |
| [#4820](https://github.com/FS-GG/.github/issues/4820) | Real use of the journal producer for Commit on fdev. | LEARN finite-profile .1–.3 acceptance. The Home-only export exception does not transfer. |
| Coordination#1032 | The complete pinned FSBar headless-integration image from current accepted source, if M2 succeeds: manifests, image identity, the encrypted exported candidate, static ELF correspondence, bounded outputs, readback and retirement. | Loaded engine/plugin ABI, Count1 and the useful-play journeys ([#4842](https://github.com/FS-GG/.github/issues/4842)), product publication, installed B2 adoption. |
| [#4815](https://github.com/FS-GG/.github/issues/4815) | At most a qualified BAR image as a later input to the BAR published replacement. | Receiver installation and retirement, the published replacements themselves, the migration inventory. |
| [Coordination#1051](https://github.com/FS-GG/FS.GG.Coordination/issues/1051) | The same qualified image and launcher could host the FourD target row later, under FourD's own selection. | Any FourD lifecycle or native qualification. Nothing here selects it. |
| [#4821](https://github.com/FS-GG/.github/issues/4821) | Nothing. | Two independent allocations. Guests on one fdev share a kernel and disks, which the 2026-10-24 ruling excludes. This stays separate. |

## 5. Milestones

**M0. Launcher source and early real preflight.** This is source work, ordinary
verification under ADR-0096 A, and read-only preflight. It boots no guest.

- *Scope:* one FS.GG.Coordination PR adding `deployment/qualification-runner/fdev/`
  (about 600–900 lines of Python plus pure tests), with these contents:
  - the claim front end;
  - the unit, slice, disk and seed builder;
  - the collector and retirement helper;
  - a README amendment adding provider `fdev-kvm-qualification-v1`.
- *Privacy:* a privacy review of the helpers adapted from private custody.
- *Controls:* claim races, a retired-ID refusal, clock arithmetic including the GitHub
  witness checks, golden files for the argv and unit properties (including
  `OOMPolicy=kill` and no `MemoryOOMGroup`), and refusal on missing memory or free
  space.
- *Early real preflight (ADR-0096), read-only:*
  - `FS.GG.Native.Qualification` visibility, collaborators and Actions policy;
  - that its workflow bytes equal the template at the pinned Coordination SHA;
  - that Root's credential has the repository-administration scope that JIT
    configuration needs, without generating one;
  - a hosted run of `bar-hosted-domain-preflight.yml`.

  Real refusals are kept as findings.
- *Review:* one independent source review and exact-head CI.

**M1. First selectable milestone: acquisition → genuine bake → export.** This is one
Root selection under an ADR-0096 C envelope, with three ordered Originals: acquisition,
bake and collector. The acquired inputs reach the bake only through the reviewed
derivation: the input-image digest is checked before use.

- *Prerequisites:*
  - M0 merged;
  - open decision 1 resolved;
  - Home's confirmation of the current VMM device set
    ([Root decision 6](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725),
    requested on #4517);
  - lanes drained and `MemAvailable` ≥ 26 GiB;
  - the installed .NET 10.0.12 runtime rechecked at admission.
- *Journey:*
  1. Claim the operation and post the start witness.
  2. Acquire and verify the noble inputs.
  3. Build the input and control images and check their digests.
  4. Preallocate `root.raw` and write the verified cloud image into it.
  5. Run the bake guest, offline, per the [M1 bake layout](#m1-bake-layout).
  6. Observe the exit.
  7. Collect, export, make the Commit and post the result.
  8. Retire, then run the independent readback.
- *Size:* about 2.5 h wall, 4 vCPU and 18 GiB for up to 90 min, about 280 GiB on `vdb`
  (freed at retirement unless held), and about 20 GiB on `/srv/bulk/native-chain/`.
- *Establishes:* runner → export → genuine bake on a real nested run, with clocks,
  admission and the #4778 capacity domain observed. It does not establish registration
  or BAR.

**Between M1 and M2: the registration PR.** A Coordination PR sets
`DEDICATED_IMAGE_SHA256` and `DEDICATED_PROVIDER_IMAGE_ID` from M1's accepted export.
It goes through normal independent review and CI. ADR-0096 C forbids an unreviewed
input inside an envelope, so this PR cannot sit inside the M2 selection.

**M2. JIT registration → first BAR.** This is a separate, later Root selection, made
only after the registration PR is merged and reviewed
([Root decision 5](https://github.com/FS-GG/.github/issues/4778#issuecomment-6522829725)).

1. Make a preallocated 256 GiB raw copy of the exported qualified image.
2. Boot one runner guest with `passt --no-map-gw` egress.
3. `guest-boot.sh` writes the binding, and Root supplies one JIT configuration through
   a root-only tmpfs file.
4. Once the runner listens, Root sets `selected_utc` and dispatches
   `bar-hosted-image-qualification.yml` once.
5. Export, teardown, runner-absence readback and retirement follow.

The workflow enforces these constraints itself:

- `purpose=full-image` with `full_image=true`;
- `main`, attempt 1;
- the first step within 300 s of `selected_utc`;
- `observer_deadline_utc` exactly 5 700 s after selection;
- a 90-minute job limit;
- only recipient-encrypted candidate bytes leave the runner.

Root holds the candidate decryption key and the JIT credential in fdev private custody
(0700). M2's native clocks total about 2.7 h.

## Open decision

1. **Shared-host field.** The target schema requires `sharedCodexHost: false`
   (`dedicated_qualification_runner.py validate_target_data`), and the runner README
   forbids the shared Codex machine. A disposable nested guest on fdev is dedicated, but
   its host is the shared agent machine. Root must rule whether that is permitted. If it
   is, M0 must change the schema field's meaning, or add a host field, so the target
   stays truthful.

Root has decided the other earlier questions: trusted owner and Commit destination,
I/O correspondence, memory, acquisition, credential custody and the device-set
prerequisite. The write domain follows decision 1's bulk destination, with working
state on `vdb`.

## Unknowns

- How Home physically backs fdev's 32 GiB, and HDD contention between the attached
  images.
- Whether fdev's monotonic clock advances while Home pauses fdev.
- Whether nested-SVM performance fits the bake clock and the BAR job's 90-minute budget.
  A timeout would be a retained first failure, not a reason to extend the clock.
- Whether `passt --no-map-gw` fully blocks the M2 guest from fdev loopback services.
- Whether a JIT runner self-updates.
- Whether `bar_hosted_domain.py` accepts the fdev provider facts without a further
  source change.
- The current Home VMM device set, until Home confirms it.

## Not selected by this plan

This plan selects none of the following: an operation, clock, claim, credential use,
provisioner or lease action, Home action, slice change, retirement, or release of any
hold. Source merge of this plan or of M0 establishes no native, installed or consumer
acceptance.
