# Finalizer r5 (pod profile)

`learn-finalizer-r5.py` is the custody and retirement actor for the LEARN finite executor pod. Design:
`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` sections 6, 8 and 14.1. Refs FS-GG/.github#4647.

**Source only: nothing here is installed, admitted or run.** Root installs it from a root-owned copy, records its sha256 in the install journal and in the pod operator's
admission (`finalizer_sha`), and admits every run.

Finalizer r4 (`baseline/libexec/learn-finalizer-r4.py`, `7f875245…`) archives one container's `/out`. r5 keeps r4's custody pattern and applies it to a pod. The helpers that
carry that pattern are **copied byte for byte** from r4 (and the pinned-descriptor chain from the pod operator), and the test compares them with the originals: the
constants, `drop`, `now`, `mark`, `run`, `open_nofollow`, `chain`, `show`, `archive`, `extract`, `markers`, `evidence_dir`, `evidence_file`, `write_record`, `finish`,
`cgroup_retired`; and `DIR_FLAGS`, `_check_dir_fd`, `open_dir_chain`, `read_pinned`. Everything else is new and has its own tests.

## Invocation and the names it expects

```text
learn-finalizer-r5.py --id <op-id> --source-identity <64 hex>        Environment: FSGG_NONCE=<32 hex>, FSGG_PG_IMAGE=<64 hex image Id>
```

Runs only as uid 30034, as the transient user unit `learn-fin-<id>.service`, started before the pod unit (which `BindsTo` it). The pod operator's start (a later PR) provides:

| What | Name |
|---|---|
| pod, pod unit | `learn-pod-<id>`, `learn-pod-<id>.service` |
| containers | `learn-pod-<id>-host`, `-recv`, `-feed` (each with a tmpfs at `/out/<role>`, see `pod-image/`), `-pg` (PostgreSQL image, no `role`) |
| loop volume, mounted by Root | `/mnt/storage/containers/learn-qualification/learn-pod/<id>.mnt` (path to be confirmed when the volume step is written) |
| per-run credential tmpfs | `/run/user/30034/learn-cred/<id>` |
| pod unit `ExecStopPost` | prints `FSGG <nonce> MEMEV oom_kill=<n>` like the reader op's, so the OOM verdict can be read from the journal |
| `FSGG_PG_IMAGE` | the pg image Id the pod operator pinned; the finalizer checks the running pg container against it |

## The run

1. **Start.** Looks at the custody, checkpoint and evidence directories **before creating any**; if any of the three exists it **exits 70 and writes nothing**, so a second
   finalizer can never replace the first one's record (a custody directory that appears between the check and the creation exits 70 with nothing done; a checkpoint directory that appears there removes the custody directory again and exits 70).
   Then creates `~/.local/state/learn-custody/<id>/` and `~/.local/state/learn-checkpoint/<id>/`. Records the slice's memory and pids. Reads the claim
   (`claims/<source-identity>.claim`) and the sealed manifest through pinned descriptors, and cross-checks the pg image (below); a failure is recorded and later makes the run
   fail (it does not stop the run).
2. **Wait.** Anchors on the pod unit (30 s, else `failed-to-start`). Polls every 2 s. Samples the volume with `statvfs` every 5 s; **90% used triggers `budget`**. Serves checkpoint
   requests. Triggers when every role has `/out/<role>/.done`, at the soft deadline (1800 s, `time`), on the budget, or, if the unit ends first, takes the early path
   (`unrecoverable`). A `systemctl show` that fails or prints no state is **unknown, not "ended"**: the poll is chained once (`unit-state-unknown`) and repeated; only after 5
   consecutive unreadable polls is the unit concluded to have ended, and that conclusion is recorded (`unitStateUnknown`). The pod unit's `ControlGroup` is read with up to 5 tries at the
   anchor (`podControlGroupReadTries`); if it never comes back as a path, the retirement checks ran on a guessed path and the run **cannot be a success**.
3. **Archive.** `host`, `recv`, `feed` in turn: `podman pause`, the container identity sampled (cgroup contains the container id, process start time, `frozen 1`, state `paused`),
   `podman unshare tar` of `/proc/<pid>/root/out/<role>` into custody, the identity sampled again, `podman unpause`. The stream is inventoried (regular files and directories only, depth
   at most 8, no `..`, no absolute name) and given a verdict: `accepted` or `rejected:{budget,truncated,identity,count,size,type,tar}`, in r4's precedence. Accepted streams are extracted
   and the stream deleted. **One budget covers the whole op: 4096 members and 256 MiB across every archive, checkpoints included.** The stream cap of an archive is the content left plus
   1 KiB per member left plus 10 KiB, which is r4's cap for a fresh budget.
4. **pg.** `podman kill --signal INT` (fast shutdown) on the pg container; wait up to 30 s for `exited`; the exit code counts only if the container exited. The pod is then stopped and
   removed. One network-less container from the pinned pg image (`--read-only`, `--cap-drop=all`, the admitted seccomp profile, user 999, `/pgdata` bound read-only, 128 MiB, 32 pids)
   runs `pg_controldata /pgdata/data`. pg is `ok` only if the image matched the pin, it exited 0 without an OOM kill, and the cluster state reads **`shut down`**.
5. **Retire** (design section 8; every item recorded):

   | Check | Meaning |
   |---|---|
   | `podGone`, `containersGone` | `podman pod ps -a` and `podman ps -a` filtered by this op's names (the infra container and the controldata container included) are empty and the listing succeeded |
   | `podCgroupRetired`, `podCgroupProcsEmpty` | the pod unit's cgroup (taken from systemd at the anchor; `podCgroupFromSystemd` records that it was, and a success needs it) is absent or `populated 0`, and `cgroup.procs` is empty |
   | `unitsClean` | this op's units are listed, `reset-failed` is run on each (and on the pod unit), and they are listed again: **none may be anything but `inactive` afterwards** (design section 8: no unit failed, activating or deactivating, `reset-failed` first). The states before the reset are recorded too. A pod unit that the stop left `failed` is normal and passes once the reset clears it; one that stays failed is a retirement failure, because operator r6's sweep refuses a failed unit as busy and would block the next op |
   | `secretsGone`, `credentialTmpfsGone` | no `podman secret` named for the op, and the credential tmpfs path is absent |
   | `volumeOk` | the volume is a mount point, its last `statvfs` is at most 1 GiB (not the host filesystem), it was sampled, and no process is left in the pod cgroup (the finalizer-side proxy for "no open handle"; a successful unmount by Root is authoritative) |
   | `snapshot` | the claim and the sealed manifest are byte-identical to the start, and the sealed snapshot is exactly the manifest: every file's size, sha256, mtime and mode (0644: the seal tool chmods every copy), no extra file, no symlink, read through pinned descriptors |
   | `slice` | memory and pids are back within 256 MiB and 16 of the starting values (up to 10 s of waiting) |

6. **Record.** The evidence tree has `out/<role>/…`, `checkpoints/<n>/<role>/…` and `record.json`.

## Schemas and what the claim tooling reads

`learn-op-pod-success/1`, `learn-op-pod-killed-retired/1`, `learn-op-pod-unrecoverable/1`, `learn-op-pod-failed-to-start/1` and `learn-op-retirement-failure/1`. The last keeps the name
the acknowledgement gate keys on, and the first four start with `learn-op-` as the claim tooling requires. Every record, whatever its schema **and whatever path wrote it, the exception path included** (the evidence is taken in a `try` that cannot lose the fields), carries `opId`,
`runNonceSha256` (sha256 of the nonce), `finalizerSha256`, **`inputRead`** (the pod operator's `input-read` marker exists) and **`containersStartedWithInput`**: the pod's
containers that were *started* with a mount at `/in`. It is a list, or **`null` when it cannot be determined**: the container census failed, an inspect failed, or the marker exists
and no container remains to ask. The claim tooling treats `null` as "not empty", so a void is refused. **A census that succeeds and lists no container with the marker absent
gives `[]`**: the pod operator creates the marker before it first reads or mounts the input (design section 4), so with no marker no container can have started with `/in`. That
rule matters: without it a pod that never started, or whose containers were already gone, left a claim that could be neither voided nor superseded.

**The pg image is cross-checked against the admission.** `FSGG_PG_IMAGE` comes from the pod operator's unit; the finalizer reads `admissions/<id>.admit` through pinned descriptors and
requires the same op id and source identity, **this finalizer's own sha256** as the admitted finalizer, and the pg image Id among the admitted `imageIds`. The result is recorded
(`admission`) and a run whose admission does not match cannot be a success. The admission's lifetime is not checked here (the operator did that when the run started).

**What the pg `shut down` evidence is worth.** `pg_controldata` reads `global/pg_control` in `/pgdata`, a file the pod's own pg process writes, so the state line is
*attested by the pod*, not independently measured; what the finalizer adds is the podman exit code of the container (the pg process exiting 0 after SIGINT), the image Id check
and the fact that the one-shot reads the cluster after the pod is gone. Together they show a fast shutdown completed, to the extent a cooperative profile can: a compromised pg could
write the file and exit 0. Root's independent readback of the volume (mounted read-only after retirement) is the strong check.

**Success** needs all of: the admission cross-check ok; trigger `done`; for each role an accepted archive and a valid terminal status whose reason the role may end with (`host` and `recv`: `ok` or `signalled`,
because the driver stops them with TERM; `feed`: `ok`); every unpause succeeded; pg `ok`; no OOM kill (`MEMEV` read exactly once) and no `RuntimeMaxSec` expiry; the input evidence known; the volume under its
90% budget; every checkpoint acknowledged with accepted archives; and retirement clean. Late custody (after 1890 s) or any failed retirement check is a retirement failure. Anything else
is `killed-retired`.

## Checkpoints

The recovery driver (same account) writes `~/.local/state/learn-checkpoint/<id>/req-<n>` (n = 1, 2, ..., consecutive, at most 8; JSON `{"n": n, "label": "<slug>"}`, at most 512 bytes)
and waits for `ack-<n>`. The finalizer archives every role as it is **now**, extracts the accepted streams into custody, and writes `ack-<n>` (`O_EXCL`, 0600: the request's sha256 and each
role's verdict). Only then does the driver act, so what a restart would wipe from a role's tmpfs is already in custody. A request that cannot be read is acknowledged with `ok: false`
and the run cannot then succeed. Other files and out-of-sequence numbers are ignored; a pre-existing `ack-<n>` is never overwritten (the run fails closed into a retirement failure).
The driver must make its last request before it stops the roles: the finalizer stops serving once the trigger fires.

## What is NOT established

None of this runs against a real pod; the tests use fakes for podman, systemctl and journalctl and the real `tar` on a fake `/proc`. These are pod-harness questions and stay open:

- **Pausing and archiving a container inside a pod** with `podman pause` and `podman unshare tar` over `/proc/<pid>/root`. r4 does this for a standalone container.
- **`pg_controldata` from a one-shot container** through a read-only bind of the loop volume's `/pgdata` (owner 428678, mode 0700), and whether the admitted seccomp profile allows it.
- **The loop volume's mount point path** and the mount options Root uses; the finalizer only needs `statvfs` and a mount point.
- **The pod unit's `ExecStopPost` `MEMEV` line and the unit ordering** (`BindsTo`, `RuntimeMaxSec` 1920 against the finalizer's 1980) belong to the pod operator's start.
- **`podman secret ls`** naming: the pod operator has to name per-run secrets `learn-pod-<id>-…` for the check to see them.
- **The sealed snapshot is re-hashed in full** at retirement (about 200 KB). An extra empty directory in the snapshot is not detected; files, symlinks and special files are.
- **A truncated pg_controldata output** is judged only by the cluster state line; the full output is retained by hash.
