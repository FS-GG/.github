# LEARN finite executor: pod profile

Status: proposed design for review. It authorises no install, image build, pull or run. Root admits each
effect separately. Refs [#4647](https://github.com/FS-GG/.github/issues/4647).

Root's decisions that bind this design: the local build of the accepted Coordination 1041 tree is the matched pair
and the finite run is a new executor profile ([6533215654](https://github.com/FS-GG/.github/issues/4647#issuecomment-6533215654));
a fixture is not genuine capture, the spool boundary is the two `spool/` subtrees, and the claim is bound to source
identity ([6533900826](https://github.com/FS-GG/.github/issues/4647#issuecomment-6533900826)).

The sections are grouped by concern: what the run claims (1), what it runs (2-3), what it reads (4), what the Host
does (5), how it survives and fails (6-8), what it leaves behind (9), how it is authorised (10), and the order and
open items (11-14). Gates come first in the order of work (section 11).

## 1. What the run can and cannot establish

This is the single statement of the run's limits. Every later section is bound by it.

| Assertion | Status after this run | Basis |
|---|---|---|
| Installed Host and Runner bytes (payloads `b3d6a6e6…7c47`, `0e07e813…9768`) run `serve` against PostgreSQL 18.6, including stop/restart recovery | **Established if the run passes** | Sections 5, 6 |
| Causal receiver acceptance: the receiver admits `execution-causal-admission/1`, replay is idempotent, changed content is refused as `semantic-conflict` | **Established if the run passes**, against a disposable receiver | Sections 5, 7 |
| Once-only receipt of the retained spool facts at the receiver, with original clocks | **Established if the run passes** | Sections 4, 5 |
| Host-side actor capture driven by fixture input | **Fixture.** Labelled fixture in every record. Never genuine capture | Section 5 |
| Genuine usage | **Stays open.** No egress, fixture provider. A later run with a scoped provider egress allowance is a separate future decision | Section 5 |
| Selected adoption (the real receiver, the v3 receiver) | **Stays open** | Section 5 |
| A link from the production input (the spool) to Host actor capture | **Stays open.** The spool feeds the receiver; the Host is driven by fixture input | Section 5 |
| Owning-plan [§1.1](../roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md): root/child/retry joins, lost-telemetry and unavailable-provider injection | **Stays open** | Section 5 |
| The O3-04 installed-adoption cases | **Stays open** | Section 5 |
| Persistence across a reboot or loss of the user manager | **Stays open.** Recovery here is a stop/restart of containers inside one op | Section 6 |
| Statistical or economic conclusions | **Not claimed** | Owning plan |

A record that carries fixture-driven results says so in a `provenance: fixture` field. A report that omits a row of
this table is incomplete.

## 2. The pod: topology and images

One rootless Podman pod per op, created by the pod operator as `fsgg-learn-qual` (uid 30034). The pod has no
interface except its own loopback. Containers talk over `127.0.0.1` inside the pod. There is no Podman socket, no
`podman exec`, no host PID or IPC namespace.

| Container | Image | Role |
|---|---|---|
| `pg` | `postgres:18.6` (digest below) | Host store |
| `host` | `learn-pod-app:r1` | Host `serve` and its bundled Runner child |
| `recv` | `learn-pod-app:r1` | Disposable Telemetry Host receiver |
| `feed` | `learn-pod-app:r1` | Telemetry producer CLI that replays the spool copy |

Matched pair (local build of Coordination `5d525ad2423e733a38ce75fb1f9cac6cf76f214e`, tree `2bdc6fba…`): Host payload
`b3d6a6e6…7c47`, Runner payload `0e07e813…9768`, bundle manifest `6144181f…56cf`, bundle zip `bcb7f833…dae0`. SDK 10.0.400,
runtime 10.0.11, fsc.dll `3e82a7fb…`, one recorded deviation (the preparers ignore `*.xml`).

### 2.1 PostgreSQL image

It is the digest Coordination's PostgreSQL tests already pin (`tests/FS.GG.Coordination.Orchestration.PostgreSql.Tests/QUALIFICATION.md`
at `5d525ad2`): `postgres:18.6@sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280`. That is the
OCI **index** digest. The `linux/amd64` **platform manifest** digest read from the registry on 2026-10-30 is
`sha256:7341002d2b8c7c5bdd7542a671a95b36196c0b5b888daf454ae4fc33ba5346d7`. The acquisition record holds three identities:
index digest, platform manifest digest, and the local image config Id from `podman image inspect`. They are different
values and none is derived from another's tail ([trusted Home decisions](2026-10-24-trusted-home-execution-decisions.md)).

It is pulled, not built: a local build would need package acquisition and would not be the qualified artifact. The pull
is one separately admitted acquisition by `fsgg-learn-qual` over the VM's public-internet egress. The run never pulls.
Every container uses `--pull=never`, and the operator pins the local config Id.

### 2.2 `learn-pod-app:r1`: base and entrypoint

**Base.** Built `FROM` the admitted `learn-qualification:v1` Id (`20a6df20…`), **not** from reader image r4. Reader r4 sets
`ENTRYPOINT catatonit -- /usr/local/libexec/learn/entrypoint`, a one-shot wrapper that pre-allocates `/out/.status`,
runs a single command through `slot proc 32 10`, writes `/out/.done` and blocks. It also puts a `dotnet` shim on
`PATH` that passes every `dotnet` call through 10 cooperative CLR slots. Long-running `host`, `recv` and `feed`
containers would collide on the one `.status`, and three CLR processes plus children would share a 10-slot pool and a
32-process cap. None of that is inherited from the base `20a6df20`.

**Entrypoint.** `ENTRYPOINT ["/usr/bin/catatonit","--","/opt/fsgg/pod/role"]`. `role` is a root-owned, mode 0555,
hash-pinned shell script in the build context, with the role name and command passed as arguments. The pinned socket
probe (`learn-probe`, `66f8254e…`) is copied to `/opt/fsgg/pod/probe` for prechecks. The image adds an unprivileged
`pod` entry at uid/gid 1000 to `/etc/passwd` and `/etc/group`, as r4 does for `learn`. `PATH` does not include the shim.

**Per-container status contract (`learn-pod-status/1`).** Each container has its own tmpfs `/out/<role>/`:

1. Before any work, `role` pre-allocates `/out/<role>/.status` (exactly 4096 B, fsync) and refuses to start if that
   fails. It writes `{"schema":"learn-pod-status/1","role":…,"phase":"starting"}`.
2. After the readiness probe (listener up, file present), it overwrites in place (`dd conv=notrunc`) with `phase: ready`.
3. The first SIGTERM (`podman kill --signal TERM`) stops the service: TERM to the child, 20 s bound, then KILL. `role`
   writes `phase: terminal` with `exit`, and `reason` in `ok | error | gate-refused | enospc | signalled`, then creates
   `/out/<role>/.done`.
4. `role` then blocks, like reader C5, so the finalizer can archive a live container's tmpfs. A second SIGTERM
   (`podman stop -t 3`) exits.
5. A service that exits on its own goes straight to step 3.

**Caps.** The reader profile's cooperative caps (32 processes, 10 CLR slots) are bypassable by direct fork/exec. They do
not apply here and are not re-implemented. The enforced bounds are the cgroup limits in section 3 (`--pids-limit`,
`--memory`, pod `TasksMax`/`MemoryMax`). These are stronger but coarser: pids count threads. The Telemetry Host's
runtimeconfig sets `System.GC.Server: true`, so `recv` and `host` run with `DOTNET_gcServer=0` and a
`DOTNET_GCHeapHardLimit` below their container limit.

### 2.3 Runtimes for the Telemetry Host and the producer CLI

The Host and Runner payloads are self-contained single-file applications and need no installed runtime. The Telemetry
Host 0.8.0 (`FS.GG.Telemetry.Host.0.8.0.nupkg`, sha `44273495…ba89`, release `telemetry-host/v0.8.0`, store schema 14) is
framework-dependent. Its `runtimeconfig.json` requires `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` 10.0.0
with `rollForward: Major`, and its release manifest lists both as prerequisites. The build runs with `--network=none`, so
no runtime is downloaded at build time.

**Settled (read-only check as `fsgg-learn-qual`, 2026-10-30).** The base image `20a6df20` ships, under
`/usr/share/dotnet/shared`, `Microsoft.NETCore.App` **10.0.12** and `Microsoft.AspNetCore.App` **10.0.12**, with SDK
10.0.401. Both satisfy the 10.0.0 requirement, so the runtimes come from the base image and no runtime archive is acquired.
The builder still asserts, as a build step that fails the build, that
`shared/Microsoft.NETCore.App/10.*` and `shared/Microsoft.AspNetCore.App/10.*` exist with a version at or above the
`runtimeconfig.json` minimum, and records the versions it found. The self-contained Host and Runner bundle runtime
10.0.11; that is independent of the shared runtimes.

**Producer CLI.** The producer is `fsgg-coord-engine` from NuGet `FS.GG.Coord.Cli`, promoted as `coherent-set/v0.113.0`: a
self-contained single-file ELF of about 130 MB, sha256 `5e517e8d…ff725`, which needs no runtime. The build context pins the
package version, the release tag and the binary sha256. It is the CLI fdev already uses to drain telemetry.

**Receiver version.** The live fsgg-store runs the same Telemetry Host 0.8.0 payload: its `installed-payload.json` records
the nupkg sha256 `44273495…ba89` and source `74cbd6f0…`, equal to the release asset and manifest. The pod receiver and the
live receiver therefore match by package identity.

**How `feed` replays** (design, qualified later inside the pod harness). `feed` creates a fresh per-run remote association
with `telemetry workspace activate-remote` and replays with `telemetry workspace drain`. `drain` sends the `*.ready` files at
the top of the spool directory (at most 129) in name order after `drain-cursor`, with a 30 s deadline, and counts a batch as
processed only when its receipt is `applied`, `rejected` or `expired`. Only `passive-prospective-0106-20261018/spool` holds
drainable batches: three `fsgg.telemetry.envelope/1` files, all workspace `utel-persistent-20261018`, producer
`codex-persistent-20261018`, stream `native-persistent-20261018`. `passive-home-canonical-v1/spool` has no `*.ready` file
(two earlier-send receipts under `outcomes/`, a `producer.json` and a `drain-cursor`); it is sealed and hashed as
**read-only comparison evidence and never replayed**.

**Per-run credentials and TLS** (design). The maintained template is
`deployment/telemetry-collector/qualify_host_state.py`. A generator step creates, in tmpfs and for this run only:

- an ephemeral server certificate (PFX and password file) authenticating `localhost`, and its CA;
- a CSPRNG secret for the one principal the replay needs, enrolled in a `fsgg.telemetry.host-config/2` with
  `Role: native-collector` for that single workspace, producer and stream identity (Root's decision);
- the store initialised with `init --root` and the principal enrolled with `enroll-producer --secret-file`.

The client endpoint is `https://localhost:<port>` (the contract only requires an absolute `https` URI). `feed` and `host`
trust the per-run CA through `SSL_CERT_FILE`, and read the credential from the environment variable
`FSGG_TELEMETRY_CREDENTIAL_<REF>` filled from a tmpfs file. Retirement checks that the credential tmpfs is gone.

The Host, Runner, receiver and producer CLI are all pinned by sha256 in the build context.

## 3. Isolation and limits

**Network.** `podman pod create --network=none`. The pod precheck asserts from inside each container that
`/proc/net/dev` lists only `lo`, that a connect to a public address and to `169.254.1.2` fails, and that the listeners are
exactly PostgreSQL 5432 and the declared Host and receiver ports. The VMM egress filter is a second layer, not relied on.

**Users.** `pg` runs `--user 999:999` (no root entrypoint, no `gosu`), host uid 428678. The other containers run
`1000:1000`, host uid 428679, as the reader profile. The operator's host-uid gate accepts {428678, 428679} and refuses
0 and 30034. Payloads are mode 0555 root-owned in the image.

**Capabilities.** `--cap-drop=all`, `no-new-privileges`, `--read-only`, `--read-only-tmpfs=false`.

**Seccomp.** The admitted profile `/etc/fsgg/learn/seccomp-8bd52f8d.json` applies to every container and the
per-container rule-set equality check (effective OCI `linux.seccomp` equals the set computed from the source) is
unchanged. Applicability is unproven. PostgreSQL needs SysV shared memory and semaphores and runs with
`io_method=worker`; the .NET apps need the usual `mmap`/`memfd` family. The precheck starts each container with a
no-effect command and **fails closed on both `EPERM` (1) and `ENOSYS` (38)**, since a seccomp filter can return either.

**Limits.** The slice is `user-30034.slice`: 6 GiB, 1280 pids, 200% CPU. The idle user manager uses about 1 GiB and 128
tasks. The pod profile counts as the only active op (section 10).

| Unit | RuntimeMaxSec | MemoryMax | MemorySwapMax | TasksMax | CPUQuota |
|---|---|---|---|---|---|
| `learn-pod-<id>` (pod cgroup) | 1920 | 4G | 0 | 900 | 150% |
| `learn-fin-<id>` (finalizer) | 1980 | 512M | | 64 | |

Per-container limits: `pg` 1024M / 200 pids, `host` 1024M / 300 (the Runner child is inside `host`), `recv` 768M / 150,
`feed` 256M / 50. They sum to **3072 MiB (3 GiB) and 700 pids**. The remaining **1 GiB and 200 pids** of the pod cgroup
cover conmon, the infra container and page cache. Worst case against the slice: 4G + 0.5G + about 1G = 5.5 GiB under
6 GiB, and 900 + 64 + 128 = 1092 pids under 1280. The 1920/1980 timing is the reader profile's, so the finalizer's
timing constants and the worst-case hard kill (32:00 + 24 s) are reused. Headroom gate: slice `memory.max − memory.current
≥ 4.5 GiB` and `pids.max − pids.current ≥ 1000`, plus the existing `MemAvailable ≥ 4 GiB` and custody-disk guards.
PostgreSQL runs `shared_buffers=128MB`, `max_connections=20`, `io_method=worker`, `fsync=on`,
`max_wal_size=256MB`, `temp_file_limit=128MB`.

## 4. Inputs: the spool snapshot and the claim

**Boundary (settled).** The two `spool/` subtrees under `~developer/.local/share/fs-gg-private/telemetry-spool/`:
`passive-home-canonical-v1/spool` and `passive-prospective-0106-20261018/spool`. Nothing else in those directories
(`ca.crt`, `selection.json`, `telemetry.json`, `config/`, `evidence/`) is read. Observed on 2026-10-30, apparent sizes
(not allocated blocks):

| Subtree | Files | Apparent bytes |
|---|---|---|
| `passive-home-canonical-v1/spool` | 5 (`drain-cursor` 70, `orchestrator-passive/producer.json` 54,820, `producer.lock` 0, two `outcomes/*.json` of 210 each) | 55,310 |
| `passive-prospective-0106-20261018/spool` | 5 (three `*.ready` of about 23,500 each, `orchestrator-passive/producer.json` 81,856, `producer.lock` 0) | 152,459 |

The exact file list is the one recorded by manifest M1 at seal; any file that is not in M1 is not read.

**Quiesce proof.** Sealing happens only if all of these hold:

1. Root stops `fdev-telemetry-drain.timer` and waits until `fdev-telemetry-drain.service` is inactive.
2. No process has a file open under either subtree (`fuser -v` and `/proc/*/fd` scan return none).
3. Manifest M1 records, per file, path, type, apparent size, sha256, `mtime_ns`, `ctime_ns`, inode and mode. After at
   least one timer period (5 min) with the timer stopped, M2 is taken and must equal M1 in every field.
4. A third manifest M3 is taken immediately after the copy and must equal M1, so the source did not change during
   the copy.

Any difference aborts sealing, and the timer is restarted only on Root's admission.

**Seal.** Root copies the files with mtimes and modes preserved into
`/srv/bulk/services/usr-local-libexec-fsgg-learn/inputs/<snapshot-id>/` (in the VM,
`/usr/local/libexec/fsgg-learn/inputs/<snapshot-id>/`), root:root, directories 0755, files 0644. That path passes the
operator's root-owned-chain check, which anything under the developer-owned `/mnt/storage` would not. The sealed
manifest `learn-input-snapshot/1` lists, per file, relative path, type, size, sha256, `mtime_ns` and mode. The operator
re-hashes every file at launch and after the run.

**Source identity and the claim.** The claim is bound to the **source identity**, not to the sealed manifest hash. The
source identity is the sha256 of the canonical list of the two source subtree paths and, for each file in M1, relative
path, size, sha256 and `mtime_ns`. The same retained source therefore cannot be claimed twice by re-sealing it.

The claim is written by a root-only writer (`learn-claim.py`, `O_CREAT|O_EXCL|O_NOFOLLOW`) through the writable source
path `/srv/bulk/services/usr-local-libexec-fsgg-learn/claims/<source-identity>.claim`, root:root 0644, containing the op
id, the source identity, the sealed manifest sha256 and the UTC time. The in-VM bind is read-only, so the operator and
the pod containers can only read it.

**Timing: after the non-input prechecks.** The claim is taken in two phases driven by Root:

1. `--precheck-only` runs every gate that reads no input: operator and image pins, ack gate, admission file, slice,
   sweep, headroom, pod precheck (network, caps, uid map, seccomp, tmpfs owners). It never mounts or hashes the snapshot.
2. Root reads that result, writes the claim, then starts `--run`. `--run` re-evaluates the gates, requires a claim that
   names its own op id, and only then creates the `input-read` marker and verifies and mounts the snapshot.

**Who owns what on disk.** The **custody directory** `~/.local/state/learn-custody/<op-id>/` is owned by the **finalizer**,
which creates it with `exist_ok=False`; the operator refuses a run whose custody directory already exists, so nothing else
creates inside it. The **`input-read` marker** is owned by the **pod operator**: it creates
`~/.local/state/learn-input/<op-id>/input-read` (directory 0700, `O_EXCL`) immediately before the first read or mount of the
input and only after verifying the claim. The marker is deliberately outside the custody directory. The finalizer records
whether it exists; the claim tooling only reads it.

**Contract the claim tooling relies on** (for the pod operator and finalizer r5 PRs). The pod operator creates the marker
before the first read or mount of the input; sets `gates.inputMounted` in its RESULT before it mounts, so a run that dies
during the mount cannot show `false`; and emits an integer `exitCode`, `runNonceSha256`, `operatorSha256`, `mode`, `opId`,
schema `home-learn-pod-operator/1` and a `containers` list with `mounts[].destination`. The finalizer records `opId`,
`inputRead`, `containersStartedWithInput`, the same `runNonceSha256` and its own `finalizerSha256`. Void is fail-closed (a
missing field blocks it). Supersede needs positive evidence of consumption (the marker, `inputRead` true, or a non-empty
`containersStartedWithInput`), and a `--run` that created the marker or mounted `/in` and failed early can only be superseded,
on a Root decision. Root may add `--operator-exit-code`, its own capture of the operator's exit status, which must match the
RESULT.

**Void and abort.**

- *Voided:* the claim exists, the op failed or was aborted, and all of these hold: the `input-read` marker does not exist;
  the operator RESULT is from a `--run` (a `--precheck-only` result is not evidence about a run) with a non-zero exit code,
  the input unmounted and no container with `/in`; the finalizer record says `inputRead` false and no container started with
  the input, carries the RESULT's run nonce, and carries the same operator and finalizer pins as the admission; and the call
  carries a Root corroboration reference. Root runs `learn_seal_claim.py void`, which renames the claim to
  `<source-identity>.claim.voided-<op-id>` (kept, never deleted). The source can then be claimed again, and the next claim
  continues the generation.
- *Consumed:* the `input-read` marker exists. The claim stands whatever happens next, including a failed run. A later run
  on the same source needs a new explicit Root decision and a new claim after the consumed one is recorded as superseded.
- A second claim attempt on the same source fails with `EEXIST`. A second op with a new id finds a claim naming another
  op and refuses.

**The claim is cooperative.** `developer` has full sudo and can edit or delete any root-owned file, including claims,
acks and the sealed snapshot. The claim detects mistakes; it does not defend against Root. A void is **attested by artifacts
the operator account writes** (the RESULT, the finalizer record and the marker): the tool makes them consistent with each
other and with the admission but does not authenticate them, so only Root's independent corroboration strengthens a void, and
the claim tooling does not detect a compromised `fsgg-learn-qual`. Decision and corroboration references are GitHub comment
URLs that the tool format-checks and does not verify; Root verifies the comment. The install journal and evidence record the
claim's hash so a change is visible afterwards.

**No replay, original clocks.** The `feed` container gets the snapshot at `/in` (read-only, `nosuid,nodev,noexec`) and
copies it into `/work/spool` preserving mtimes. The copy is verified against the sealed manifest, and only the copy is
replayed, so cursor files move in the copy, never in the snapshot. Facts keep their recorded timestamps; nothing is
re-stamped. `RuntimeMaxSec` is the original budget and is never renewed.

## 5. What the Host does

Commands, in order, inside the pod against the pinned payloads:

1. `host init` against `pg`: records the store's backup identity and generation fence.
2. `host verify-installed-adoption` with a per-run request: the existing offline verifier for the installed store
   ([O3-04 preview](https://github.com/FS-GG/FS.GG.Coordination/blob/5d525ad2423e733a38ce75fb1f9cac6cf76f214e/docs/operations/o3-04-installed-adoption-preview.md)).
3. `recv` starts (receiver schema 14, collector grants generated for this run only).
4. `host serve`, paused, with local runner roots under `/work`, an executor binding for a fixture provider, and
   telemetry options pointing at the in-pod receiver outbox.
5. One ordinary assignment through `prepare-main-admission` from the repository's pinned fixture assignment, then
   resume, one controlled invocation, terminal state.
6. `feed` replays the spool copy to `recv`.

**What "installed actor and usage capture, and causal receiver acceptance" means here.** It follows
[§1.1 of the owning plan](../roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md) and the
[causal receiver window](../roadmaps/learn-01-causal-receiver.md), cut down to what a pod with no egress can show. The
table in section 1 is the authority for what each result counts as.

- *Actor capture (fixture).* The installed Host and Runner persist item, attempt, invocation and parent identity before
  launch, observe start and terminal states, and reconcile expected against observed across the restarts in section 6.
  The input is the repository's fixture assignment, so these results are labelled fixture.
- *Causal receiver acceptance.* The receiver admits the Host's `execution-causal-admission/1`, retains the exact decoded
  bytes with the route and launch-intent references, and the private analyzer joins them. Exact replay is idempotent;
  changed content is refused as `semantic-conflict`.
- *Usage.* The only provider is a fixture. Its records exercise the capture path but do not support a complete-usage claim.
- *Spool.* The replay shows the installed receiver accepting the retained producer's facts once, with receipts and
  original clocks.

**Relation to the persistent receiver plan.** The receiver here is disposable and uses the published Telemetry Host
payload. It is not the persistent receiver of the
[LEARN C2 persistent receiver v3 plan](../roadmaps/learn-c2-persistent-receiver-v3.md), whose v3 image, installation,
grants, capture, custody and activation stay open there. Accepting facts in the pod shows that the installed Host/Runner
output and the receiver contract fit together on pinned bytes. Receiver-first adoption into the real receiver, with its
backup and restore evidence, remains that plan's gate.

## 6. Persistence and recovery

**Where data lives.** PostgreSQL and the receiver store use a dedicated volume. Root creates a fixed-size 1 GiB ext4
image `…/learn-pod/<id>.img` on the custody disk, loop-mounts it for the op, and after retirement mounts it read-only for
readback. Inside it: `/pgdata` (owner 428678:428678, mode 0700) with `PGDATA=/pgdata/data` set explicitly, so `initdb`
never meets `lost+found`, and `/recv` (owner 428679:428679, mode 0700). The size is **enforced** by the filesystem: a
full volume returns `ENOSPC`, PostgreSQL fails closed and the status contract reports `enospc`. If Root declines a
root-owned mount per op, the fallback is a watchdog (below). Root alone mounts and unmounts; disposal is a separate
admission.

**How the 30034 finalizer checks size and open handles.** The files are owned by subuids and mode 0700, so
`fsgg-learn-qual` cannot read inside them from the host side. The finalizer does not need to:

- *Size:* `statvfs` of the mount point (readable) gives used bytes for the whole volume. It samples every 5 s and, at
  90% used, stops the pod with a `budget` verdict.
- *Open handles:* after the stop, `cgroup.procs` of the pod cgroup (user-owned, readable) must be empty. A process that
  held a handle is a process in that cgroup. No `/proc/<pid>/fd` access is needed.
- *Fallback watchdog (no loop mount):* `podman unshare du -s --block-size=1` enters the account's user namespace, where
  container root has `CAP_DAC_OVERRIDE` over the mapped uids and can read both trees. The finalizer samples every 5 s
  and stops the pod at 1 GiB combined. Overshoot between samples is bounded only by the PostgreSQL settings in section 3,
  so the filesystem quota is the preferred option.

**Recovery cases** (all within one op, none across a reboot):

- *Clean restart.* SIGTERM `host`, then `pg`, then `recv`. Start `pg`, `recv`, `host` from the same directories.
  Verify: generation fence and backup identity unchanged; admission and both clocks unchanged; the invocation reconciled
  without a second launch; receiver facts and receipts unchanged; an exact replay of one fact returns the same terminal
  state.
- *SIGKILL of `host` and its Runner child while an invocation is non-terminal.* `podman kill --signal KILL` on `host`,
  which takes the Runner child with it, while the invocation has a persisted launch intent and no terminal state.
  Verify: the pod cgroup shows no surviving Runner process; the restarted `host` reconciles the existing attempt, starts a
  clean child, and does not invoke the provider a second time; the original clocks are unchanged; an unproven terminal
  stays unknown rather than success.
- *SIGKILL of `pg` during the invocation.* The Host refuses to proceed and reconciles after `pg` restarts.
- *Receiver down.* Stop `recv` while `host` emits facts. Verify: the outbox on disk retains every fact (count and
  hashes); `host` continues safely; after `recv` restarts the outbox drains exactly once, with no loss and no
  duplicate, and original timestamps are kept. A restart of `host` while `recv` is down must not drop the outbox.

## 7. Refusal and conflict cases

Each case has an expected verdict that is checked; a failure is retained, not retried.

- **Snapshot:** a file changed after sealing (hash mismatch), an extra file, a missing file, a symlink, an unreadable
  file, a second claim for the same source identity, a claim naming another op, an op id reuse, an `input-read` marker
  present when a void is requested.
- **Pod:** any non-loopback interface, a reachable external address, a non-empty capability set, a seccomp rule set that
  differs, a start that returns `EPERM` or `ENOSYS`, a host uid outside {428678, 428679}, an image Id that differs from the
  pin, an unacknowledged retirement failure, a missing admission file, an exceeded concurrency or headroom gate.
- **Host:** wrong executable hash for `verify-installed-adoption`, a stale or expired request, an unsupported launch
  obligation (refuses before invocation), duplicate launch (returns the original), cancellation after terminal.
- **Receiver:** exact replay (idempotent), changed content for an existing causal identity (`semantic-conflict`), an unknown
  causal kind (rejected before a durable receipt), a credential for another scope.
- **Budget:** the unit deadline passes (killed and retired), `/out` over its cap, the volume full (`enospc`).

## 8. Retirement

A new finalizer r5 keeps the r4 pattern: it starts first, the pod unit `BindsTo` it, and it is the only custody actor.
The reader finalizer archives one container's `/out`. Finalizer r5 archives each role's `/out/<role>/` from its live
container in turn, using the same bounded O_PATH/O_NOFOLLOW chain, into one custody directory with per-role members.
The total stays under the 4096-member and 256 MiB caps. At retirement it checks that:

- the pod and all containers are gone (`podman pod ps -a`, `podman ps -a` filtered by the op id);
- the pod cgroup is absent or `populated 0`, and no process remains under the op unit;
- no `learn-pod-*` or `learn-fin-*` unit is failed, activating or deactivating (`reset-failed` afterwards);
- the pod's secrets are removed and the per-run credential tmpfs is gone;
- the volume's last `statvfs` is within its cap and the pod cgroup is empty (no open handle);
- the sealed manifest still hashes to its identity and the claim file is unchanged;
- slice `memory.current` and `pids.current` return near their pre-run values.

Failure writes `learn-op-retirement-failure/1`, which blocks the next op until acknowledged by exact record hash
(the ack gate, section 10).

## 9. Evidence and acceptance

| Acceptance item | Evidence |
|---|---|
| Install receipts | Matched-pair hashes (section 2); every installed file's sha256 and mode (context, builder, pod operator, finalizer r5, claim writer, admission writer, operator r6, ack writer r2); image config Ids and builder RESULT hashes for `learn-pod-app:r1`; the PostgreSQL acquisition (index digest, platform manifest digest, config Id); the runtime source and versions chosen in section 2.3; the install journal entry; re-verified slice, seccomp and ack at install time; a no-input receipt op whose finalizer record shows the in-sandbox Host and Runner usage and production readbacks equal the build's (`d625b9b1…`, `390d4c94…`, `690773fc…`, `81f44360…`). |
| Finite run against the spool only | Source identity and sealed manifest before and after (equal); the claim file and its hash; the op id; the admission file; operator RESULT with all gates and precheck assertions per container; the `verify-installed-adoption` result object; Host and receiver outputs; receipts and counts for each spool item; the recovery readbacks; refusal-case verdicts; the finalizer's hash-chained record with `runNonceSha256`. Original clocks: unit `RuntimeMaxSec` from `systemctl show`, preserved mtimes in the manifest and the receiver's recorded timestamps. Every fixture-driven result carries `provenance: fixture`. |
| Independent retirement | The retirement block and checks in section 8, an independent Root re-hash of the evidence tree, `systemctl --user list-units` and `podman ps -a` empty of the op, slice usage back near baseline, and no new retirement-failure record. |

Root reads back every item independently and records the result on #4647. This design does not close #4647.

## 10. Operator r6, authorisation and exclusion

**Authorisation.** Home's hash-pinned sudoers entry (`fsgg-learn-operator`) is retired. Today the operator checks only
that it runs as euid 30034; the caller is `developer`, which has full sudo, so authority is procedural. The pod operator
adds a mechanical, but still cooperative, gate: it refuses unless a root-written admission file
`/usr/local/libexec/fsgg-learn/admissions/<op-id>.admit` exists on a root-owned chain and its content matches the op:
sha256 of the argv, the source identity, the operator, finalizer and image pins, and an expiry of at most ten minutes. A
root-only writer creates it with `O_EXCL`, and the admission's sha256 is recorded on #4647 at admission. `fsgg-learn-qual`
cannot forge it; Root can, so it binds a run to what Root admitted, not to a second person.

**Operator r6** is operator r5 with these changes, for independent review against `318edb4e…`:

1. `ACK_DIR` becomes `/usr/local/libexec/fsgg-learn/retirement-acks` (root-chain-checked in the read-only bind view).
2. The sweep also lists `learn-pod-*` units and treats an active pod as an active op.

Change 2 enforces mutual exclusion. Without it the exclusion is **de facto only**: a pod needs 4.5 GiB of slice headroom
and a reader op needs 2.5 GiB, so each refuses while the other runs, but only because of the headroom arithmetic. The pod
operator takes the same `learn-operator.lock` flock and refuses if any `learn-op-*` or `learn-pod-*` unit is active.
Change 2 goes beyond the scope Root recorded (ack move only), so it needs Root's confirmation.

No change to `IMAGE_ID`, `FIN_SHA`, limits, seccomp, units or custody.

**Ack writer r2** (`learn-retirement-ack.py`). The bind is read-only in the VM, so the writer writes through
`/srv/bulk/services/usr-local-libexec-fsgg-learn/retirement-acks`. Its chain check starts at `/srv/bulk/services`
(`/srv/bulk` is developer-owned), and it also requires `st_dev`/`st_ino` of the source directory to equal those of the
mounted `/usr/local/libexec/fsgg-learn`, so a directory swapped after the mount is refused. The ack content stays exactly
the admitted `3db3a5f3…7210e` for `a0-20261022t192443z-f1-pre`
([#4516 6360174387](https://github.com/FS-GG/.github/issues/4516#issuecomment-6360174387)). A stricter option is
`chattr +i /srv/bulk/services` so it cannot be renamed.

**The restored ack must exist before any operator run.** That includes the receipt op and `--precheck-only`: the
operator's retirement gate refuses with `retirement-failure-unacknowledged` while the f1-pre record has no ack. Section
11 makes it step 1.

**Gates to re-qualify for r6 and ack writer r2:**

| Gate | Positive | Negative |
|---|---|---|
| A27 / A27b (ack gate) | f1-pre acked, op proceeds | unacked record refuses; wrong hash refuses |
| Ack writer, path | ack created through the source path; mounted view shows it | **swapped path** (`services` or `retirement-acks` replaced by a symlink or another directory), **world-writable or non-root-owned** ack directory, existing ack (`EEXIST`) |
| Ack writer `--remove` | removes a regular root-owned ack | refuses a symlink, a non-root-owned file, a missing file |
| A0 summary | `failures == acked`, `unacked == []` | |
| Exclusion (change 2) | a pod unit active makes a reader op refuse | a reader unit active makes a pod op refuse |
| A29 (cap) | still refuses the third op | |

A smoke of A0 r4 9/9 is recommended because the operator hash changes. A1-A26, A28/A28b, A30 and A31 do not touch the
changed code. The harness r3b pins the operator sha and its source is Home's (Unknown 7).

The pod operator (`learn-pod-operator-r1.py`), finalizer r5, claim writer and admission writer are new admitted bytes,
not r6. Their gates are the new items in sections 3, 4, 6, 7 and 8, qualified by a pod harness before the genuine run:
A0-style fail-closed kill cases for the pod, the retirement cases, the one-op concurrency and headroom gates, the
snapshot, claim, void and admission refusals, and the recovery cases of section 6 on a synthetic snapshot. They need
their own independent review.

## 11. Order of work

Gates and preconditions come first. Nothing after step 1 runs until step 1 has passed.

1. **Gate: the ack.** Restore `a0-20261022t192443z-f1-pre.retirement-ack` (content `3db3a5f3…7210e`) and verify it
   against the record's sha256 before any operator run. The location is the one the operator in use reads: `/etc/fsgg/learn/retirement-acks`
   for operator r5 (writer r1; lost at the next image rebuild), or the persistent `retirement-acks` for r6 (writer r2), which
   must therefore be installed and reviewed before the first run if the ack is to persist.
2. **Gate: live profile.** Re-verify the slice drop-in, seccomp file, operator and finalizer pins, live slice limits and
   an empty `learn-*` unit and container population (read-only).
3. Review this plan; resolve the Unknowns marked *build* in section 14.
4. Root admits the PostgreSQL acquisition (and, if needed, the runtime archive) and records the identities.
5. Source-only pieces, unadmitted and reviewed: pod operator, finalizer r5, claim, admission and void writers, builder
   and context, operator r6, ack writer r2.
6. Root installs them and records receipts; the receipt op runs.
7. Pod harness qualification on a synthetic snapshot.
8. Quiesce, seal, `--precheck-only`, claim, `--run` once (section 4).
9. Retirement, readback, and the Root record on #4647.

## 12. Home dependencies

None required. The slice limits and seccomp file are already in Home's image
([6528087192](https://github.com/FS-GG/.github/issues/4647#issuecomment-6528087192)). Everything new lives on the
persistent bulk disk under `/usr/local/libexec/fsgg-learn`, in the account's podman storage on `/mnt/storage`, or is a
transient user unit. Needed from the VM rather than Home: outbound access to the container registry once for the
PostgreSQL pull, and Root's per-op loop mount for the volume. If the pull route is closed, Home would have to supply the
image, which would be a Home dependency.

## 13. Out of scope

Any real provider call or egress, the live fsgg-store receiver, production usage or economics, the Home SystemAdmin
Main deployment, and the telemetry drain schedule.

## 14. Unknowns, by what they block

Updated 2026-10-30 after the build-blocker investigation and Root's decisions on
[#4647](https://github.com/FS-GG/.github/issues/4647#issuecomment-6534175822) and on #4988
([6534030404](https://github.com/FS-GG/.github/pull/4988#issuecomment-6534030404)).

| # | Unknown | Status | Blocks |
|---|---|---|---|
| 1 | Exact file list of the two `spool/` subtrees (the boundary is settled; the list comes from M1 at seal) | open | seal and claim |
| 2 | Producer CLI package and version; receiver version | **designed** (section 2.3): `fsgg-coord-engine` 0.113.0, receiver identical to live fsgg-store. Wire compatibility of CLI 0.113.0 with Host 0.8.0 is a pod-harness spike | spike in the pod harness |
| 3 | Seccomp applicability to PostgreSQL and the .NET apps (untested; the precheck fails closed) | open | run |
| 4 | Finalizer r5 archiving one `/out/<role>/` per container from live containers | **designed**: finalizer r4 already pauses a container and archives `/proc/<pid>/root/out` with `podman unshare tar`; r5 repeats it per role, and a checkpoint handshake (the recovery driver requests, the finalizer archives and acknowledges, then the driver acts) preserves pre-restart evidence. Pausing and archiving inside a pod is a pod-harness spike | spike in the pod harness |
| 5 | Fixture assignment as Host input | **settled**: results are labelled fixture | settled |
| 6 | First pull from the registry (the registry token and manifest fetch worked from fdev; the pull itself is not exercised) | open | run |
| 7 | Home harness r3b source and the install scripts | **partly resolved**: the C1 r8b installer (`8d16d02a…`) is in custody and recorded by hash; the journal creator `b9792a07…` and the three earlier add-set scripts are not in custody; the harness r3b source is still absent | run |
| 8 | Per-run credentials and mTLS | **designed** (section 2.3), following `qualify_host_state.py`. Whether a Generic credential works in host-config/2 for the Host outbox is a pod-harness spike | spike in the pod harness |
| 9 | Whether the quiesce proof in section 4 is sufficient, and whether the drain timer touches these subtrees at all | open | seal and claim |
| 10 | Whether the base image `20a6df20` carries both shared .NET runtimes | **settled**: both are 10.0.12 (section 2.3) | settled |
| 11 | Loop-mounted volume or watchdog fallback | **settled** by Root: the fixed 1 GiB ext4 loop image is the enforced quota for `/pgdata` and `/recv`, root-mounted in the VM (cooperative under the VM-only zone ruling), checked by the finalizer with `statvfs`; the `podman unshare` watchdog is dropped | settled |
| 12 | r6 includes the exclusion change | **settled** by Root: r6 adds `learn-pod-*` to its sweep, so pod and reader exclusion is enforced; it is part of r6's scoped re-qualification | settled |

### 14.1 Decisions that bind the source PRs

From Root's [comment 6534175822](https://github.com/FS-GG/.github/issues/4647#issuecomment-6534175822) and the carry-forward
list on [6534100763](https://github.com/FS-GG/.github/issues/4647#issuecomment-6534100763):

- The grant role is `native-collector`, for the single identity of the three drainable batches.
- The code lives in `FS-GG/.github` under `deployment/learn-finite-profile/`, tests under `tests/learn-finite-profile/`. The
  admitted installed bytes are published first as a verified baseline, after a secrets and identifier scan.
- PR sequence: baseline, ack writer r2, operator r6, seal and claim (parallel to the ack writer and operator), image and role
  entrypoint, finalizer r5, pod operator with recovery driver and credential generator.
- The recovery driver is named with its authority and readback custody, and pre-restart evidence is captured before any
  restart wipes a role's tmpfs.
- The `pg` container's terminal evidence is specified, and it is stopped with SIGINT (fast shutdown), not TERM.
- The pod operator has a loop-volume precheck (mounted, fresh, 1 GiB, `nosuid,nodev,noexec`, correct ownership), with a
  matching refusal in section 7 and a per-op create and mount step in section 11.
- The admission file is written through `/srv/bulk/services/...`, not the read-only bind.
- The SIGKILL case is described as restart reconciliation, not orphan detection, because the containers share no PID
  namespace.
- Voiding a claim needs corroboration beyond the `input-read` marker (an operator RESULT showing no container started with
  `/in`, plus finalizer evidence); it proves "never mounted", not "never read". A consumed claim is superseded by a
  generation+1 claim, because a fresh claim at the same name hits `EEXIST`.
- A successful remount read-only or unmount of the loop volume is authoritative for disposal; `cgroup.procs` stays the
  finalizer-side proxy.

Blocks *build* (open): none; the remaining items are pod-harness spikes. Blocks *seal and claim*: 1, 9. Blocks *run*: 3, 6, 7.
