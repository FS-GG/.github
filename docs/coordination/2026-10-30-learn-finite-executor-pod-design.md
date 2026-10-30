# LEARN finite executor: pod profile

Status: proposed design for review. It authorises no install, image build, pull or run. Root admits each
effect separately. Refs [#4647](https://github.com/FS-GG/.github/issues/4647).

Decisions this design implements are in Root's 2026-10-30 comment
[6533215654](https://github.com/FS-GG/.github/issues/4647#issuecomment-6533215654): the local build of the
accepted Coordination 1041 tree is the matched pair, the finite run is a new executor profile, operator r6
moves `ACK_DIR`, and the snapshot covers only the two `passive-*` spool directories.

## 1. Why a new profile

The #4745 reader profile (operator r5, finalizer r4, reader image r4) runs one command in one container with
`--network=none`, a read-only root, only tmpfs and no bind mounts
([#4745 closure 6362357832](https://github.com/FS-GG/.github/issues/4745#issuecomment-6362357832)). It cannot
host PostgreSQL, the Host `serve` loop or a receiver, and it has no input path. The Done-when of #4647 asks for
installed actor and usage capture, causal receiver acceptance, persistent storage and recovery, and
refusal/conflict cases. That needs a multi-process pod. The reader profile stays unchanged and qualified.

Matched pair, from the local build recorded on #4647: Coordination `5d525ad2423e733a38ce75fb1f9cac6cf76f214e`,
tree `2bdc6fbac95a34735f1e866b99d3f77fc1a845fd`. Host payload `b3d6a6e6…7c47`, Runner payload
`0e07e813…9768`, bundle manifest `6144181f…56cf`, bundle zip `bcb7f833…dae0`. Built with SDK 10.0.400 and runtime
10.0.11, fsc.dll `3e82a7fb…`, one recorded deviation (the preparers ignore `*.xml`).

## 2. Pod

One rootless Podman pod per op, created by the pod operator as `fsgg-learn-qual` (uid 30034). The pod has no
network interface except its own loopback. Containers talk over `127.0.0.1` inside the pod and over files in
declared shared volumes. No Podman socket, no `podman exec`, no host PID or IPC.

| Container | Image | Role | Mounts |
|---|---|---|---|
| `pg` | `docker.io/library/postgres:18.6@sha256:4ef4dbc9…2280` | Host store, PostgreSQL 18.6 | `/pgdata` rw (host dir under the op's custody directory), tmpfs `/run/postgresql`, `/tmp` |
| `host` | `learn-pod-app:r1` | Host `serve` and its bundled Runner child (`host-supervises-local-runner-child`) | tmpfs `/work`, shared `/out` volume, per-run credentials tmpfs |
| `recv` | `learn-pod-app:r1` | Disposable telemetry receiver (Telemetry Host) | tmpfs `/work`, `/recv` rw (custody dir), shared `/out` |
| `feed` | `learn-pod-app:r1` | Telemetry producer CLI that replays the spool copy | `/in` read-only snapshot, tmpfs `/work`, shared `/out` |

Images:

- **PostgreSQL.** This is the digest Coordination's PostgreSQL tests already pin
  (`tests/FS.GG.Coordination.Orchestration.PostgreSql.Tests/QUALIFICATION.md` at `5d525ad2`). It is not built
  locally, because a local build would need package acquisition and would not be the qualified artifact. It is
  pulled once, as a separately admitted acquisition by `fsgg-learn-qual`, with the digest verified. The run never
  pulls: every container uses `--pull=never` and the operator pins the local image config Id, not the tag.
  The manifest digest and the image config Id are different identities, so the acquisition records both from
  `podman image inspect` ([trusted Home decisions](2026-10-24-trusted-home-execution-decisions.md)).
- **`learn-pod-app:r1`.** Built locally with `--network=none --pull=never --no-cache --timestamp 0` from a root-owned,
  hash-pinned context, the same pattern as reader image r4. It is `FROM` the r4 image Id and adds: the bundle's
  `host/` and `runner/` payloads and `manifest.json` under `/opt/fsgg/orchestration/`; the Telemetry Host payload
  extracted from `FS.GG.Telemetry.Host.0.8.0.nupkg` (sha `44273495…ba89`, release `telemetry-host/v0.8.0`); and the
  pinned producer CLI. The context file hashes are listed in a builder constant map and re-checked before the build.
  Which Telemetry Host version fsgg-store runs and which CLI package fdev drains with are Unknowns (section 14).

Containers run with `--cap-drop=all`, `no-new-privileges`, `--read-only`, `--read-only-tmpfs=false`, and the same
`--security-opt seccomp=/etc/fsgg/learn/seccomp-8bd52f8d.json` as the reader profile.

## 3. Isolation and limits

**Networking.** `podman pod create --network=none`. The pod precheck asserts from inside each container that
`/proc/net/dev` lists only `lo`, that a connect to a public address and to `169.254.1.2` fails, and that the
loopback listeners are exactly PostgreSQL 5432 and the Host and receiver ports. The VMM's nftables egress filter
remains a second layer and is not relied on.

**Users.** `pg` runs `--user 999:999` (no root entrypoint, no `gosu`), which maps to host uid 428678. The other
containers run `1000:1000`, host uid 428679, as the reader profile does. The operator extends the reader profile's
host-uid gate to the set {428678, 428679} and still refuses 0 and 30034. Payload files are mode 0555 root-owned
in the image, and no container can write its own executables.

**Seccomp.** The same admitted profile applies to all containers, and the per-container rule-set equality check
(effective OCI `linux.seccomp` equals the set computed from the source) is unchanged. Applicability is not yet
proven. PostgreSQL needs SysV shared memory and semaphores and `io_method=worker` (not `io_uring`); the single-file
.NET apps need the usual `mmap`/`memfd` family. The pod precheck starts each container with a no-effect command and
fails closed on `EPERM`. Section 14 lists this as an Unknown to close before any run.

**Limits inside `user-30034.slice` (6 GiB, 1280 pids, 200% CPU).** The idle user manager uses about 1 GiB and 128
tasks. The pod profile counts as the only active op:

| Unit | RuntimeMaxSec | MemoryMax | MemorySwapMax | TasksMax | CPUQuota |
|---|---|---|---|---|---|
| `learn-pod-<id>` (pod cgroup) | 1920 | 4G | 0 | 900 | 150% |
| `learn-fin-<id>` (finalizer) | 1980 | 512M | | 64 | |

Per-container `--memory`/`--pids-limit` sum under the pod: `pg` 1G/200, `host` 1G/300, `recv` 768M/150, `feed` 256M/50,
with 768M and 200 pids left for the Runner child and burst. Worst case: 4G + 0.5G + 1G = 5.5 GiB under 6 GiB, and
900 + 64 + 128 = 1092 pids under 1280. The 1920/1980 timing is the reader profile's, so finalizer timing
constants and the worst-case hard kill (32:00 + 24 s) are reused. Concurrency is one (a pod op excludes a reader
op, and vice versa). Headroom gate: slice `memory.max − memory.current ≥ 4.5 GiB` and `pids.max − pids.current ≥ 1000`, plus the
existing `MemAvailable ≥ 4 GiB` and custody-disk guards. PostgreSQL runs `shared_buffers=128MB`,
`max_connections=20`, `io_method=worker`, `fsync=on`.

## 4. Inputs: snapshot sealing and the single claim

**Scope.** Only the two spool subtrees `passive-home-canonical-v1/spool` (5 files, 80 KiB) and
`passive-prospective-0106-20261018/spool` (5 files, 160 KiB) under
`~developer/.local/share/fs-gg-private/telemetry-spool/`. The rest of that directory is out of bounds. Whether
"spool" means these `spool/` subtrees or the whole `passive-*` directories (which add `ca.crt`, `selection.json`,
`telemetry.json`, `config/`, `evidence/`) needs Root's confirmation; this design takes the subtrees because the
other files carry configuration and prior evidence, not spool data.

**Quiesce.** Root stops `fdev-telemetry-drain.timer`, waits for the service to be inactive, takes manifest M1,
waits at least one timer period (5 min), takes M2, and requires M1 = M2. Anything else aborts sealing.

**Seal.** Root copies the files with mtimes and modes preserved into
`/srv/bulk/services/usr-local-libexec-fsgg-learn/inputs/<snapshot-id>/` (seen in the VM as
`/usr/local/libexec/fsgg-learn/inputs/<snapshot-id>/`), root:root, dirs 0755, files 0644. That path passes the
operator's root-owned-chain check, unlike anything under `/mnt/storage`, which is developer-owned. The manifest
`learn-input-snapshot/1` lists, per file: relative path, type, size, sha256, `mtime_ns` and mode. Its own sha256 is the
snapshot identity, and the operator takes it as `--input-manifest-sha256`. The operator re-hashes every file at
launch and again after the run; any difference refuses or fails the run.

**Single claim.** Root runs a small claim writer (`learn-claim.py`, root-only, same style as the ack writer). It
creates `/usr/local/libexec/fsgg-learn/claims/<manifest-sha256>.claim` with `O_CREAT|O_EXCL|O_NOFOLLOW`, root:root
0644, containing the op id, the manifest sha256 and the UTC time. The pod operator refuses unless the claim exists
and names its own op id. A second claim writer call fails with `EEXIST`. A second op, even with a new id, finds a
claim naming another op and refuses. The claim is permanent, as acks are, and `fsgg-learn-qual` cannot modify it.

**No replay, original clocks.** The `feed` container gets the snapshot at `/in` (read-only, `nosuid,nodev,noexec`).
It copies it to `/work/spool` preserving mtimes, verifies the copy against the manifest, and replays only that copy,
so the cursor files move in the copy, never in the snapshot. Facts keep their recorded timestamps; nothing is
re-stamped. The op's `RuntimeMaxSec` is the original budget and is never renewed.

## 5. What the Host does

Commands, in order, all inside the pod and all against the pinned payloads:

1. `host init` against `pg`: records the store's backup identity and generation fence.
2. `host verify-installed-adoption` with a per-run request: the existing offline verifier for the installed store
   ([O3-04 preview](https://github.com/FS-GG/FS.GG.Coordination/blob/5d525ad2423e733a38ce75fb1f9cac6cf76f214e/docs/operations/o3-04-installed-adoption-preview.md)).
   Pass means the installed executable hash and store behaviour match.
3. `recv` starts (receiver schema 14, collector grants generated for this run only).
4. `host serve`, paused, with local runner roots under `/work`, an executor binding for a fixture provider, and
   telemetry options pointing at the in-pod receiver outbox.
5. One ordinary assignment through `prepare-main-admission` with the repository's pinned fixture assignment (its
   hashes are in the install receipt), then resume, one controlled invocation, terminal state.
6. `feed` replays the spool copy to `recv`.

**What "installed actor and usage capture and causal receiver acceptance" means here.** It follows
[§1.1 of the owning plan](../roadmaps/2026-09-12-095059-stable-policy-orchestration-and-statistical-learning.md)
and the [causal receiver window](../roadmaps/learn-01-causal-receiver.md), and is limited to what a pod with no
egress can show:

- *Actor capture.* The installed Host and Runner persist item, attempt, invocation and parent identity before launch,
  observe start and terminal states, and reconcile expected against observed across the restart in section 8. The
  observations reach the receiver as facts.
- *Causal receiver acceptance.* The receiver admits the Host's `execution-causal-admission/1` for the assignment,
  retains the exact decoded bytes with the route and launch-intent references, and the private analyzer joins
  them. Exact replay is idempotent; changed content is refused as `semantic-conflict`.
- *Usage.* The only provider in the pod is a fixture. Its usage records exercise the capture path but are not a
  supported route, so this run cannot claim complete usage. Per §1.1, missing host support stays a named
  capability gap. Real provider usage needs egress, which Root excluded.
- *Spool.* The spool replay shows the installed receiver accepting the retained producer's facts once, with
  receipts and original clocks.

Not established by this run: production usage, economics, statistical conclusions, adoption into fsgg-store, or
any fresh-workspace default. The Host-side work is fixture work, not a second production input, and the spool is
the only production input.

## 6. Persistence and recovery

PostgreSQL data lives in `/pgdata`, a host directory under the op's custody directory
(`~/.local/state/learn-custody/<id>/pgdata`, owner 30034, mode 0700 with the subuid mapping applied by `:U`), on the
bulk-backed custody disk. The receiver store lives in `/recv` beside it. Both are bounded: the finalizer fails the
op if `/pgdata` + `/recv` exceed 1 GiB, and the custody-free-space gate is unchanged. After the run they are
retained read-only for Root's readback and disposed only on Root's admission.

Recovery step, run by the controller mid-op: SIGTERM `host`, then `pg`, then `recv` (clean stop); start `pg`, `recv`,
`host` again from the same directories; verify store generation fence and backup identity unchanged, the
admission and both clocks unchanged, the invocation reconciled without a second launch, receiver facts and receipts
unchanged, and an exact replay of one fact returning the same terminal state. A second variant kills `pg` with
SIGKILL during the invocation and checks the Host refuses to proceed and reconciles after restart.

## 7. Refusal and conflict cases

Each case has an expected verdict that is checked, and a failure is retained, not retried.

- Snapshot: a file changed after sealing (hash mismatch), an extra file, a missing file, a symlink, an unreadable
  file, a second claim, a claim naming another op, an op id reuse.
- Pod: any non-loopback interface, a reachable external address, a capability set that is not empty, a seccomp rule
  set that differs, a host uid outside {428678, 428679}, an image Id that differs from the pin, an unacknowledged
  retirement failure, an exceeded concurrency or headroom gate.
- Host: wrong executable hash for `verify-installed-adoption`, stale or expired request, an unsupported launch
  obligation (refuses before invocation), duplicate launch (returns the original), cancellation after terminal.
- Receiver: exact replay (idempotent), changed content for an existing causal identity (`semantic-conflict`), an
  unknown causal kind (rejected before a durable receipt), a credential for another scope.
- Budget: the unit deadline passes (killed and retired), `/out` or `/pgdata` over their caps (budget verdict).

## 8. Retirement

A new finalizer r5 keeps the r4 pattern: it starts first, the pod unit `BindsTo` it, and it is the only custody
actor. It archives `/out` through the bounded O_PATH/O_NOFOLLOW chain, writes the hash-chained record
(`learn-op-*/1` schemas plus the pod fields), and at retirement checks that:

- the pod and all its containers are gone (`podman pod ps -a`, `podman ps -a` filtered by the op id);
- the pod cgroup is absent or has `populated 0`, and no process remains under the op unit;
- no `learn-pod-*` or `learn-fin-*` unit is failed, activating or deactivating (`reset-failed` after);
- the pod's volumes and secrets are removed, and the per-run credential tmpfs is gone;
- `/pgdata` and `/recv` are closed (no open handle), within the size cap and unchanged since the last write;
- the snapshot manifest still hashes to its identity and the claim is unchanged;
- the slice's `memory.current` and `pids.current` return near their pre-run values.

Failure writes `learn-op-retirement-failure/1`, which blocks the next op until acknowledged by exact record hash
(the ack gate, section 10).

## 9. Evidence and acceptance

| Acceptance item | Evidence |
|---|---|
| Install receipts | Matched-pair hashes (above); every installed file's sha256 and mode, including the context, builder, pod operator, finalizer r5, claim writer and operator r6; the image config Ids and builder RESULT hashes for `learn-pod-app:r1` and the PostgreSQL pull; the install journal entry; re-verified slice, seccomp and ack at install time; a no-input receipt op whose finalizer record shows the in-sandbox Host and Runner usage and production readbacks equal the build's (`d625b9b1…`, `390d4c94…`, `690773fc…`, `81f44360…`). |
| Finite run against the spool only | Sealed manifest before and after (equal), the claim file, the op id, operator RESULT with all gates and precheck assertions per container, `verify-installed-adoption` result object, Host and receiver outputs, receipts and counts for each spool item, recovery readbacks, refusal-case verdicts, and the finalizer's hash-chained record with `runNonceSha256`. Original clocks: unit `RuntimeMaxSec` from `systemctl show`, preserved mtimes in the manifest and the receiver's recorded timestamps. |
| Independent retirement | The retirement block and checks in section 8, an independent Root re-hash of the evidence tree, `systemctl --user list-units` and `podman ps -a` empty of the op, slice usage back near baseline, and no new retirement-failure record. |

Root reads back every item independently and records the result on #4647. This design does not close #4647. Statistical
or economic conclusions still need the owning plan's independent experiment evidence.

## 10. Operator r6

r6 is r5 with the smallest change Root decided on, and it keeps the reader profile's pins.

- `ACK_DIR` moves from `/etc/fsgg/learn/retirement-acks` to `/usr/local/libexec/fsgg-learn/retirement-acks`. The
  existing `root_chain_ok` walk applies unchanged to the new path.
- The ack writer (`learn-retirement-ack.py`, r2) changes with it. The path is a read-only bind in the VM, so the
  writer writes through the persistent source `/srv/bulk/services/usr-local-libexec-fsgg-learn/retirement-acks`.
  Its chain check must then start at `/srv/bulk/services` because `/srv/bulk` is developer-owned. Root's accepted
  persistence risk covers this; a stricter option is `chattr +i /srv/bulk/services` so the directory cannot be
  renamed away.
- The ack content stays exactly the admitted `3db3a5f3…7210e` for `a0-20261022t192443z-f1-pre`
  ([#4516 6360174387](https://github.com/FS-GG/.github/issues/4516#issuecomment-6360174387)).
- No change to `IMAGE_ID`, `FIN_SHA`, limits, seccomp, units or custody. Independent review of the diff against r5
  (`318edb4e…`) is required because it changes admitted bytes.

Gates to re-qualify for r6: A27 and A27b (ack gate, positive and negative, including a world-writable or symlinked
ack directory) and the A0 summary that `failures == acked` with no unacked. A smoke of A0 r4 9/9 is recommended
because the operator hash changes. A1–A26, A28/A28b, A29, A30 and A31 do not touch the changed code. The harness
r3b pins the operator sha and its source is Home's, so re-pinning it needs that source (an Unknown).

The pod operator (`learn-pod-operator-r1.py`) and finalizer r5 are new admitted bytes, not r6. Their gates are
the new items in sections 3, 4, 7 and 8, to be qualified by a pod harness before the genuine run: A0-style
fail-closed kill cases for the pod, the retirement cases above, the 1-op concurrency and headroom gates, and the
new snapshot and claim refusals. They need their own independent review.

## 11. Home dependencies

None are required for this design. Slice limits and the seccomp file are already in Home's image
(restored 2026-10-30, [6528087192](https://github.com/FS-GG/.github/issues/4647#issuecomment-6528087192)). Everything new
lives on the persistent bulk disk under `/usr/local/libexec/fsgg-learn` and the account's podman storage on
`/mnt/storage`, or is a transient user unit. Needed from the existing VM, not from Home: outbound access to
Docker Hub once, for the PostgreSQL pull. If that route is closed, the PostgreSQL image would need Home to supply
it as a disk image, which would be a Home dependency.

## 12. Order of work

1. Review this plan. 2. Root admits the PostgreSQL acquisition and records both digests. 3. Source-only pieces:
pod operator, finalizer r5, claim writer, builder and context, operator r6 and ack writer r2, all unadmitted and
reviewed. 4. Root installs them and records receipts. 5. Pod harness qualification with a synthetic snapshot.
6. Root seals the real snapshot, claims it, and runs once. 7. Retirement and readback.

## 13. Out of scope

Any real provider call or egress, the live fsgg-store receiver, production usage or economics, the Home
SystemAdmin Main deployment, and the telemetry drain schedule.

## 14. Unknowns

1. **Spool boundary.** `spool/` subtrees only, or whole `passive-*` directories. The drain timer's effect on these
   directories is unverified.
2. **Producer CLI and receiver version.** Which package drains the spool, and whether Telemetry Host 0.8.0 matches the
   receiver fsgg-store runs.
3. **Seccomp applicability** to PostgreSQL and the single-file .NET apps. Untested.
4. **Shared `/out` across pod containers** under the finalizer's custody chain, which today archives one container.
   Needs a spike.
5. **Fixture assignment.** The Host-side stimulus is repository fixture data. Whether Root accepts that as "installed
   actor capture" without a real provider is a Root decision.
6. **Docker Hub pull** is allowed by the VMM filter per Atlas `environment/fdev-kvm-vm`; not yet exercised.
7. **Home harness and C1 install script source** (`b9792a07…`) are not in fdev, so r6 harness re-pinning and journal
   writing are manual.
8. **Per-run credentials and mTLS** for `host serve` and the receiver, generated inside the pod, are not yet specified.
9. **Quiesce proof.** Two equal manifests five minutes apart is the proposed bar. Root may want a stronger one.
