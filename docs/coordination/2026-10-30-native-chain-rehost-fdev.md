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

Live read-only facts observed on fdev on 2026-10-30:

- AMD Ryzen 9 7900 guest with 12 vCPUs and 31 GiB RAM, no swap;
- `/dev/kvm` present, nested SVM enabled, QEMU 11.1.2, `passt` and `veritysetup` installed;
- cgroup v2 with `cpuset cpu io memory pids` enabled at the root and in the user manager;
- `/srv/v3-pool` is `vdb`: ext4, 2.0 TiB, 13 inodes used, empty `operations/`;
- `/srv/bulk` is `vdf`: ext4, 4 TB, shared by fsgg-store, the LEARN account home and the SC2 libraries;
- `/home/developer` is `vde`: 492 GB, 420 GB free;
- `/` is regenerated from the image on every upgrade;
- `fsverity`, `mkfs.erofs` and `xorriso` are not installed.

## Recommended design in one paragraph

Root is the trusted execution owner of this route, and the plan says plainly what that
covers. The custody boundary moves from "agents cannot reach Root's files" to four
parts:

- the workload cannot reach fdev;
- inputs and results are content-sealed;
- every digest is anchored outside fdev before the next step uses it;
- no agent tool call writes the domain without a journaled `sudo`.

Each step runs as one systemd transient unit. That unit owns one QEMU guest and runs as
an unprivileged executor uid with no sudo, inside a dedicated slice. The unit's
`RuntimeMaxSec` enforces the step's useful clock and its `TimeoutStopSec` enforces the
retire clock. Disks are fully preallocated on `/srv/v3-pool`. The first selectable
milestone (M1) is one bounded genuine run: it validates the acquired noble inputs
inside a nested guest, then exports the result, commits it and retires. The bake,
registration and first BAR follow as M2.

## 1. Custody and trust boundary

The Home design protected sources, clocks and evidence with Root-owned 0400 files and a
separate Root console. Agents could not cross that UID boundary. Inside fdev, agents
have sudo, so no in-fdev mechanism can stop a deliberate tamperer. The replacement
therefore aims for **structural prevention of accidental interference** plus
**tamper-evidence anchored outside fdev**.

| Layer | Mechanism | Reuses |
|---|---|---|
| Executor identity | Root starts each step with `systemd-run` as a system transient unit. The unit sets a fixed unprivileged numeric uid/gid (proposal: 64100), which has no passwd entry, sudo, login or SSH key. It gets the `kvm` group only through `SupplementaryGroups`. Hardening: `NoNewPrivileges`, `ProtectSystem=strict`, `ProtectHome=yes`, `PrivateTmp`, and `ReadWritePaths` limited to the run directory. | systemd manager-state retention from the [28 Oct decision](2026-10-24-trusted-home-execution-decisions.md#administrative-preparation-boundary--28-october) (`ExitType=cgroup`, `RemainAfterExit`, `CollectMode=inactive`), plus `OOMPolicy=continue` (Atlas `platform/systemd-oom-policy-public-contract`) |
| Sealed inputs | The input set is built into one ext4 image with `mke2fs -d`. Its SHA-256 is posted on the owning issue before the unit starts. The guest gets it as `readonly=on` virtio-blk with `file.locking=on`. At boot, the guest hashes the whole device and refuses on a mismatch with the expected digest, which arrives as a SMBIOS OEM string. Later hardening: dm-verity (`veritysetup` is present on fdev; the guest package set would change) or fs-verity (needs a root probe of the ext4 feature and `fsverity-utils`). | Coordination `noble-inputs.py verify` and `dedicated_qualification_runner.py validate-inputs` |
| Execution boundary | A nested KVM guest per step. It gets only the RO input disk, a fresh preallocated scratch disk, a fresh output disk and, where the step needs it, `passt` user-mode egress with `--no-map-gw`. Outbound traffic then meets Home's fail-closed nftables filter. The bake runs with no network. | Coordination `guest-boot.sh`, `run-jit-runner.sh`, `provision-image.sh` |
| Measured export | The guest writes the export archive to its output disk, hashes it and prints the digest on the serial console. In M2 the digest also goes into the GitHub Actions job log. After QEMU exit is observed, the collector re-hashes and must match. Root posts the closed packet digest on the owning issue before anything consumes it. | d2f2 closed result packaging; the [#4820](https://github.com/FS-GG/.github/issues/4820) journal producer for Commit (Coordination PR1059 lineage, whose runtime is fdev's installed .NET 10.0.12) |
| Independent readback | A separate read-only actor verifies five things: the packet against the GitHub anchors, unit and cgroup absence, disk disposition, runner absence through the API, and the `sudo` journal entries in the operation window. | d2f2 inspect and retirement helpers, adapted |

**What this proves.**

- The workload ran in a fresh guest. Its inputs were byte-identical to a digest published
  before start.
- The guest cannot have written outside its output and scratch disks.
- The result bytes match a digest timestamped by GitHub before any dependent step. In M2
  they also match a digest in a job log that fdev cannot edit afterwards.
- No ordinary agent tool call can modify the domain. A `sudo` intervention leaves a
  journal record, unless the person using `sudo` also edits the journal.

**What it does not prove.**

- There is no protection against a deliberate fdev root. Such an actor can alter QEMU,
  the image before hashing, guest memory or the journal.
- There is no protection against a compromised Home, which hosts fdev's disks and VMM.
- There is no attestation. A vTPM (swtpm) or measured boot would run under the same fdev
  root, so this plan rejects it as false assurance.

This is the same trust posture the user accepted for Home in the
[2026-10-24 trusted-Home ruling](https://github.com/FS-GG/.github/issues/4820#issuecomment-6380220325),
with fdev root in Home's place. One stronger option stays available later: Home or the
user could hash the result disk from outside fdev, but that needs Home participation.

## 2. Execution

### Placement and helper reuse

| Chain step | Runs where | Carried over unchanged | New or changed |
|---|---|---|---|
| Input acquisition | Ordinary fdev process into a fresh directory. It uses public URLs and no credentials (ADR-0096 A). | `noble-inputs.py acquire/verify`, `noble/acquired-input-lock.json` | none |
| Runner (bounded workload) and export | Nested guest in unit `fsgg-native-<op>-run` | `dedicated_qualification_runner.py validate-inputs`, the journal producer Commit | fdev launcher (below) |
| Genuine bake | Offline nested image-build guest in unit `fsgg-native-<op>-bake` | `provision-image.sh --apply`, `verify-guest --phase image`, the noble APT/SDK/Go/runner locks | the launcher supplies the root-only `/run/fsgg-image-build-selection.json` |
| Registration | (a) A reviewed Coordination PR records the baked image identity. (b) One JIT runner registration on `FS.GG.Native.Qualification`. | `guest-boot.sh`, `run-jit-runner.sh`, `binding_from_request` | `portable_workspace_sc2.py` sets `DEDICATED_IMAGE_SHA256` and `DEDICATED_PROVIDER_IMAGE_ID`. The README provider row becomes `fdev-kvm-qualification-v1`. |
| First BAR | A fresh runner guest from the qualified image as a throwaway overlay. Root dispatches `bar-hosted-image-qualification.yml` with `purpose=full-image`. | `bar_hosted_operation.py`, `bar_hosted_domain.py`, the caller workflow and its 90-minute job budget | none |

The **fdev launcher** is new source in FS.GG.Coordination under
`deployment/qualification-runner/fdev/`. It has these parts:

- a claim and loader front end, adapted from `home-d2f2-run.py`. It keeps the one-time
  claim, the hash-pinned helper loader, signal-safe dispatch, closed result packaging and
  observe-only resume. It drops the tmux paste, the Root PID, pane and boot guards, and
  the human TTY gate.
- a pure builder for the unit, slice and QEMU argv;
- a collector;
- a retirement and readback helper adapted from `home-d2f2-inspect.py` and
  `home-d2f2-retirement.py`.

The Home B0 cohort is retained as history and not carried over. That cohort is
`final_operator.py`, the assembler, the receiving static, the 41 Root-0400 runtime files
and the 454 payload pins. It existed to cross Home's ordinary-to-Root boundary, and that
boundary no longer exists. These lessons carry over:

- one named document bound with a ≥75% fit-report control;
- bounded stream drain;
- first-failure preservation;
- terminal state is not EOF;
- absence is not success.

### Clocks

The clock structure of the d2f2 selection stays: a pre-start window, then one Original
per step made of useful time plus retire time, then a collector with its own Original
inside an enclosing bound. No clock is renewed, extended, combined or reset.

| Clock | Enforcement | M1 | M2 bake | M2 BAR guest |
|---|---|---|---|---|
| Pre-start (claim → unit active) | the launcher refuses past the cut | 300 s | 300 s | 300 s |
| Original useful | `RuntimeMaxSec`. On expiry, `ExecStop` sends QMP `system_powerdown`. | 2 700 s | 4 800 s | 6 600 s (90-min job + boot/registration) |
| Original retire | `TimeoutStopSec`, then SIGKILL with `KillMode=control-group` | 600 s | 600 s | 900 s |
| Collector useful + retire | its own transient unit | 600 + 60 s | 900 + 60 s | 1 800 + 120 s |
| Enclosing (all of the above + 120 s tail) | the launcher refuses dependent steps past it | 4 380 s | 6 780 s | 9 840 s |

Each clock starts at the manager's `ActiveEnterTimestampMonotonic` and is
cross-checked against UTC and the GitHub selection-comment timestamp. If they diverge by
more than 5 s, the step stops as Unknown. This covers Home pausing the fdev VM, which
fdev cannot observe. An fdev reboot or upgrade during a step ends it as FAILED or
Unknown. It is never resumed. The values above are proposals for Root to bind in each
selection.

### Single claim and no replay

- Before any effect, the executor creates the claim
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
2. The collector hashes the output, compares it with the guest's serial digest, writes
   the closed packet and makes the journal Commit.
3. Root posts the packet digest on the owning issue.
4. Delete the scratch disk and overlay. On failure, keep them as a *named held residue*
   of that operation instead.
5. Check runner absence through the API (M2).
6. Revert the window's runtime-only slice properties.
7. An independent actor reads back every item above.

## 3. Write domains and capacity

| Domain | Use | Reason |
|---|---|---|
| `/srv/v3-pool` (`vdb`) | The only native write domain: `native/{claims,inputs,images,runs/<op>,evidence}`. The directory is root-owned 0711. Per-run subdirectories belong to the executor uid. | A dedicated block device inside fdev, with no current data writers. `provision.py create_pool` fully allocated `pool.img` on Home. |
| `/srv/bulk` (`vdf`) | none | shared with fsgg-store, the LEARN home and the SC2 libraries |
| `/home/developer` (`vde`) | source checkouts and small evidence copies only | agent worktrees and caches compete for it |
| `/` (`vdd`) | transient unit state only | regenerated on upgrade |
| `/srv/sc2-reserved` (`vdc`) and LEARN `vda` | excluded | SC2-only reserved operation e995b57e, which #4778 forbids borrowing; LEARN qualification |

**Mapping to #4778 Done when, option (b): an independently provisioned resource domain.**

- *Bytes:* each run's guest disks are fully preallocated with `fallocate` before
  start: 256 GiB scratch, a 64 GiB output disk and the RO input image. Other writers on
  `vdb` can consume only free space that was never allocated. They cannot take the
  reserved blocks.
- *Inodes:* the guest filesystems are independent. On the host side each run uses a
  handful of inodes on `vdb`, which has 134 M free.
- *Writes:* the files belong to the executor uid with mode 0600. QEMU holds the image
  locks. The guest writes only its own disks.
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
  `-mem-prealloc`. The slice `fsgg-native.slice` sets:
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
observations are made inside the guest.

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
| #4778 | The option (b) decision with actual enforcement observed in a run: identity, bytes, inodes, CPU and memory correspondence, refusal controls, lifecycle and independent retirement, all within fdev's cooperative trust. | Home physical capacity and I/O correspondence; protection against fdev root; recovery of the historical B0 originals. |
| #4650 (V3-ENV-01) | For the fdev nested profile: enforcing execution, committed export, independent cleanup, a qualified launcher source and its first genuine use. | Unattended allocation without a human or host agent, worker-loss recovery, reboot persistence, publication or installed adoption, benefit disposition. |
| Coordination#1032 | The complete pinned FSBar headless-integration image from current accepted source, if M2 succeeds. That covers the manifests, image identity, exported archive, static ELF correspondence, bounded outputs, readback and retirement. | Loaded engine/plugin ABI, Count1 and the useful-play journeys ([#4842](https://github.com/FS-GG/.github/issues/4842)), product publication, installed B2 adoption. |
| [#4821](https://github.com/FS-GG/.github/issues/4821) | Nothing. | Two independent allocations. Guests on one fdev share a kernel and disks, which the 2026-10-24 ruling excludes. This stays separate. |

## 5. Milestones

**M0. Launcher source.** This is source only, plus ordinary verification under ADR-0096 A.
It boots no guest.

- *Scope:* one FS.GG.Coordination PR adding `deployment/qualification-runner/fdev/`
  (about 1–1.5 k lines of Python plus pure tests), with these contents:
  - the claim and loader front end;
  - the unit, slice and QEMU argv builder;
  - the collector;
  - retirement and readback;
  - a README amendment adding provider `fdev-kvm-qualification-v1`.
- *Controls:* claim races, a retired-ID refusal, clock arithmetic, argv golden files,
  refusal on missing free space or memory, and no `sudo` in the executor path.
- *Also confirmed in M0:* that fdev's systemd accepts a numeric `User=`, and that the
  4820 producer closure runs on fdev's installed .NET 10.0.12.
- *Review:* one independent source review and exact-head CI.

**M1. First selectable milestone: one bounded genuine run → export.** This is one Root
selection, run as a single-step ADR-0096 C envelope.

- *Prerequisites:*
  - M0 merged;
  - noble inputs acquired and verified in a fresh directory, with the lock digests
    matching;
  - Root decisions 1–4 below.
- *Journey:*
  1. Seal the inputs and post the digest.
  2. Claim the operation.
  3. Preallocate the disks.
  4. Start the unit.
  5. Boot the signed noble cloud image (lock-pinned) with a 4 vCPU / 16 GiB guest and no
     network.
  6. The guest hashes its input device, runs `validate-inputs` and writes the receipt.
  7. Power off and observe the exit.
  8. Collect, Commit and post.
  9. Retire, then run the independent readback.
- *Size:* about 1 h wall, 4 vCPU and 18 GiB for about 75 min, and about 330 GiB on
  `vdb`, freed at retirement unless held.
- *Establishes:* the custody boundary, the clocks, admission and the #4778 domain
  enforcement on a real nested run. It does not establish a bake or BAR.

**M2. Genuine bake → registration → first BAR.** This is one later Root selection with
three ordered steps, each with its own Original:

1. the offline bake and `verify-guest --phase image`;
2. the registration PR in Coordination (normal review and CI), then one JIT
   registration under a separately admitted credential use;
3. one runner guest, one dispatch of `full-image` at a pinned `coordination_sha`, export
   and teardown, with runner-absence readback.

Its native clocks total about 4.6 h, plus registration-PR review time. Root may split
it into two selections at the registration boundary. It reuses M1's launcher unchanged,
unless M1 exposes a
determining defect.

## Decisions only Root or the user can make

1. **Trust posture.** Accept fdev root as the trusted execution owner, with
   tamper-evidence and external anchors instead of the Home UID separation. This changes
   what the evidence claims. The user approved the Home equivalent, so Root may want the
   user's explicit assent even under the [#3010](https://github.com/FS-GG/.github/issues/3010) delegation.
2. **Shared-machine clause.** The runner README says "the shared Codex machine is
   forbidden". Decide whether a disposable nested guest on fdev is permitted, with fdev
   as host.
3. **Memory.** Decide between throttling agent lanes during native windows (about 10 GiB
   for lanes while the guest holds 18 GiB, with no swap) and asking Home to enlarge the
   fdev VM. VM lifecycle is Home's.
4. **Write domain.** Decide whether `/srv/v3-pool` becomes the native domain, which
   pauses v3 pilot starts during windows, or Home attaches a dedicated native disk, as it
   did with `fdev-sc2`.
5. **Executor identity.** Decide between a transient numeric uid (no image change) and a
   persistent account added to fdev's image source in Home's SystemAdmin repository.
6. **Human step.** Decide whether any human operator step remains. d2f2 required one
   only because of Home's safety limit on Home activity.

## Unknowns

- How Home physically backs fdev's 32 GiB, and HDD contention between the attached
  images.
- Guest clock behaviour while Home pauses fdev.
- Whether nested-SVM performance fits the BAR job's 90-minute budget. A timeout would be
  a retained first failure, not a reason to extend the clock.
- Whether the ext4 `verity` feature is enabled on `vdb`, which needs a root probe.
- Whether `passt --no-map-gw` fully blocks guest access to fdev loopback services. M1
  includes a negative probe.
- Whether a JIT runner self-updates.
- Whether `bar_hosted_domain.py` accepts the fdev provider facts without a further
  source change.

## Not selected by this plan

This plan selects none of the following: an operation, clock, claim, credential use,
provisioner or lease action, Home action, slice change, retirement, or release of any
hold. Source merge of this plan or of M0 establishes no native, installed or consumer
acceptance.
