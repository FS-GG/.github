# Pod operator: gate layer r1 and start r2

r2 (`learn-pod-operator-r2.py`, below) adds the start. r1 stays here as the reviewed gate layer that r2 copies verbatim.

## r1: the gate layer

`learn-pod-operator-r1.py` is the **gate layer** of the pod operator described in
`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` (sections 3, 4, 10 and 14.1). Refs FS-GG/.github#4647. Source only: it
installs and starts nothing, and **this revision cannot start a pod**. Root admits any install and every run.

### What this revision is, and is not

It decides whether a pod op may start and records the decision in a `home-learn-pod-operator/1` RESULT. It does not contain the images, the
role entrypoint, finalizer r5, the loop-volume and container prechecks, or the pod start; those are later PRs, and their pins are empty here:

- `image_ids` and `finalizer_sha` are empty in the production configuration, so every real run refuses (`no image ids are pinned`);
- `--precheck-only` never exits 0 while `pod_precheck_implemented` is false, so Root cannot take a claim from it by mistake;
- `--run` stops after the gates and the claim verification, **before the `input-read` marker**, so a run of this revision consumes no claim.

The ordering contract below is implemented in `begin_input_access` and tested through an injected start function.

### Gates, in order (the first refusal wins)

1. **Identity.** Runs only as uid 30034; exact arguments (`--precheck-only|--run --id --argv-json --source-identity`); image ids and the
   finalizer pin are pinned in the file.
2. **Pins.** Every pinned image id is present locally by Id; the finalizer hashes to its pin through a pinned descriptor chain.
3. **Slice.** `user-30034.slice` is exactly 6 GiB and 1280 pids; a missing drop-in fails closed.
4. **Shared lock.** `~/.local/state/learn-operator.lock`, the **same file and the same flock semantics as operator r6**
   (`O_RDWR|O_CREAT|O_NOFOLLOW|O_CLOEXEC` 0600, exclusive non-blocking `flock` retried every 0.5 s for 120 s, then `operator lock busy`),
   held to process exit. The test holds it with r6's own code and requires the refusal, and the reverse.
5. **Symmetric exclusion.** Refuse while **any `learn-op-*` reader op** (a precheck op included) **or any `learn-pod-*` unit** is active or
   transitioning (`failed`, `activating`, `deactivating`, `reloading`), a failed or transitioning `learn-fin-*` unit, or a leftover
   `learn-pod-*` pod, or a `learn-op-*` container whose op unit is not active (the same orphan sweep as r6). A failed census (units, pods or
   containers) refuses. This is the other direction of operator r6's exclusion.
6. **Custody.** An existing custody or evidence directory for `<id>` or `<id>-pre`, or a unit named for the id, refuses.
7. **Acknowledgements.** Operator r6's `retirement_gate` and `read_ack_pinned`, **verbatim**, against the persistent ack directory.
8. **Evidence size.** Operator r6's `evidence_usage`, verbatim, with its 32 GiB cap.
9. **Admission.** The admission reader of `learn_seal_claim.py`, **verbatim**, with its pinned chain (`open_dir_chain`, `_check_dir_fd`). The
   admission Root wrote must match the argv hash, the source identity, **this file's own sha256**, the finalizer pin and the image ids; it must
   be unexpired and live at most 600 s.
10. **Headroom.** Slice memory headroom at least 4.5 GiB, pids headroom at least 1000, MemAvailable at least 4 GiB, custody disk at least 1 GiB.

`--run` adds: **claim** (the live claim names this op and source identity, and its sealed manifest on disk has the sha256 the claim records;
only metadata is read, the sealed copy of the input is never opened); **nonce** (`runNonceSha256` = sha256 of a fresh nonce is recorded in the
RESULT before anything can start); **input** (`begin_input_access`).

### The ordering contract

`begin_input_access` does, in this order, and a refusal at a step stops there:

1. verify the claim (a refusal creates nothing);
2. create the `input-read` marker `~/.local/state/learn-input/<op-id>/input-read` (directory 0700, file `O_EXCL` 0600, never removed; the file, its directory and that directory's parent are fsynced before the next step);
3. set `gates.inputMounted` to true;
4. mount the input.

So the marker exists before the first read or mount, `inputMounted` is already true when the mount runs, a run that dies during the mount
cannot show `false`, and a second attempt for the same op id refuses at the marker. The marker is owned by this operator; the custody
directory is owned by the finalizer (see the claim tooling's README).

### Contract with the claim tooling

The RESULT has `schema` `home-learn-pod-operator/1`, `mode`, `opId`, `operatorSha256`, an integer `exitCode`, `gates.inputMounted`, a
`containers` list and, for `--run`, `runNonceSha256`. The tests feed it to the claim tooling's own `_precheck_ok` and `_evidence_contract`.

### Verbatim blocks

The blocks marked `VERBATIM` are copied byte for byte from `learn_seal_claim.py` and operator r6. The test compares their source with the
originals, so they cannot drift. The pod operator is a separate single-file script run with `-I` and cannot import them.

### Tests

`python3 tests/learn-pod-operator/run.py` drives `main()` with injected dependencies (a scripted `run`, the effective uid, the clock, the nonce
and the pod start) against temporary fixtures. It runs the admission vectors A01 to A18 against **both** the original reader and the copy and
requires the same answers. A mutation run of 58 mutants is recorded in the PR.

## r2: the start, the recovery driver and the credential generator

`learn-pod-operator-r2.py` is r1's gate layer plus the start: pins, the loop-volume and container prechecks, the finalizer and pod units, the
containers, the recovery driver (`--drive`) and the per-run credential generator. Refs FS-GG/.github#5183. **Source only:** it installs nothing
and nothing here has run against a real pod. Root admits the install and every run.

```text
learn-pod-operator-r2.py --precheck-only|--run --id <op-id> --argv-json '["learn-pod-plan/1","<plan sha256>"]' --source-identity <64 hex>
learn-pod-operator-r2.py --drive --id <op-id> --source-identity <64 hex>     (internal: the main process of learn-pod-<id>.service)
```

`--argv-json` names the plan by its digest (`plan_argv(PLAN)`), so the admission binds a run to the exact plan in this file.

### One-shot commands without `podman exec`

The Host's section 5 one-shots (`init`, `verify-installed-adoption`, `prepare-main-admission`), the receiver's `init --root` and `enroll-producer`,
and the producer's `activate-remote` and `drain` run as **step lists in the role containers**. Each role container keeps the image's entrypoint
(`catatonit -- role <role> ... -- <command>`) and gets one command: `STEP_RUNNER`, a POSIX `sh` program pinned in this file. It runs an ordered
list of argv vectors and writes each step's `stdout`, `stderr` and `rc` under `/out/<role>/steps/<k>-<name>/`, stops at the first failure with
its exit code, writes `steps/complete`, and may end by `exec`'ing the role's service. Values are passed as positional parameters; `eval` only
ever sees `"${N}"` references built from integers. Two built-ins: `wait-tcp PORT SECONDS` (a LISTEN socket in the pod's network namespace)
and `env-file FSGG_TELEMETRY_CREDENTIAL_<REF> PATH` (exports a credential from a tmpfs file to later steps, never printed).

`/out/<role>` is what finalizer r5 archives, so one-shot evidence reaches custody by the finalizer's own route. Neither the image, `role` nor
the finalizer changes. A container whose steps are done is replaced only after a checkpoint has archived it (below).

Alternatives considered:

| Option | Effect on custody | Changes | Why not chosen |
|---|---|---|---|
| Podman init containers (`--init-ctr`) | their `/out` is never archived: finalizer r5 archives only `host`, `recv`, `feed` from live containers | finalizer r5 (a fourth source), and a writable shared path for their output | breaks "the finalizer is the only custody actor" or loses the evidence |
| A `--steps` mode in `role` | same as chosen | `role`, the image (its sha256 is pinned by the builder and by the install route) | a rebuild-time change for what an argv can carry; reasonable if Root prefers the contract in the image |
| A fourth archived slot (`init`) | archived | finalizer r5 `ROLES`, `role`'s role list, the image | three reviewed artifacts change; one-shots still need ordering against the services |
| One container per one-shot, replaced through checkpoints | archived | none | consumes up to 8 checkpoints for one-shots alone; `/work` state cannot pass between one-shots (the feed copy, verify and drain need it) |

### Gates and the precheck

1 to 10 are r1's (identity, pins, slice, lock, exclusion, custody, acks, evidence, admission, headroom). Changes: the pins are the app and pg image
Ids (empty here: they are admitted after the build and the pull, so every real run refuses), finalizer r5's sha256 (`94300e04…`) and the seccomp
profile's; exclusion also refuses a leftover `learn-pre-*` precheck pod; the op-id check also covers the checkpoint, drive, marker and credential
directories. New:

- **11 plan** (checked with 1): `validate_plan` simulates the plan and refuses one that is malformed or breaks an invariant the finalizer depends on
  (at most 8 checkpoints; all three role containers running at every checkpoint; no role container killed or removed unless a checkpoint since its
  last change holds its state; never all three roles done before the last action; exactly one generation with `/in`; every role done and pg running
  at the end). A plan with a `HELD` value refuses.
- **12 loop volume** (carry-forward item 3): `<volume_root>/<id>.mnt` is exactly one `ext4` mount from `/dev/loopN` with `rw,nosuid,nodev,noexec`;
  900 MiB to 1 GiB; fresh (at most 16 inodes and 64 MiB used, so an `initdb`'d volume is refused without reading inside it); a root-owned top
  directory closed to group and other writers holding exactly `pgdata` (428678:428678, 0700) and `recv` (428679:428679, 0700) on the same
  filesystem, and at most `lost+found`.
- **13 container precheck**: a precheck pod `learn-pre-<id>` (`--network=none`, net shared), its four containers built by the same `ctr_argv` as
  the run (no credentials, volume or input; default cgroups because the operator is not in the pod unit) with a probe command. From each it asserts
  the start (EPERM and ENOSYS named), uid/gid, the five capability sets 0, NoNewPrivs 1, tmpfs owners, only `lo` and no route, the socket probe
  (AF_VSOCK errno from the profile, app containers), the payload's host uid (pg 428678, apps 428679, never 0 or 30034), the image Id, CapAdd empty,
  read-only root, memory and pids limits, the seccomp option, and OCI seccomp rule-set equality (operator r6's `expected_set`/`effective_set`,
  verbatim). The pod is removed and the removal read back, on every path.

`--precheck-only` **exits 0 when all of this holds**, with `inputMounted` false and no container with `/in`; the claim tooling's `_precheck_ok`
accepts that RESULT. `--run` runs the same gates and precheck, takes the census again, verifies the claim, records `runNonceSha256`, then
`begin_input_access` (r1, verbatim): the `input-read` marker, `gates.inputMounted`, and only then the start. The RESULT's `containers` lists the
containers the driver will create; only `feed-replay` mounts `/in` (read-only).

### Units

| Unit | Started | Properties |
|---|---|---|
| `learn-fin-<id>` | first | finalizer r5; `RuntimeMaxSec=1980`, `MemoryMax=512M`, `TasksMax=64`, `FSGG_NONCE`, `FSGG_PG_IMAGE`. No pod removal on stop: a finalizer that loses the custody race must not touch the winner's pod (#5029 B1) |
| `learn-pod-<id>` | only if the finalizer started | `BindsTo`/`After` the finalizer, `RuntimeMaxSec=1920`, `MemoryMax=4G`, `MemorySwapMax=0`, `TasksMax=900`, `CPUQuota=150%`, `Delegate=yes`, `KillMode=control-group`; operator r6's bounded `ExecStop` (pod unpause, pod stop) and MEMEV dump (verbatim); `ExecStopPost` removes the pod and the credential tmpfs. Main process: this file with `--drive` |

Containers (`ctr_argv`): `--cgroups=split`, `--pull=never`, `--cap-drop=all`, `no-new-privileges`, the admitted seccomp profile, `--read-only`,
`k8s-file` logs capped at 16 MiB, tmpfs `/tmp` and `/dev/shm`; pg `999:999` with `/pgdata` from the volume and `postgres -c ...` (design section 3
settings, `listen_addresses=127.0.0.1`); apps `1000:1000` with tmpfs `/out/<role>` (64 MiB) and `/work`, `/cred` read-only, `DOTNET_GCHeapHardLimit`
below each container limit; `recv` gets `/recv` from the volume; the replay gets `/in` read-only. Limits are design section 3's.

### The recovery driver (`--drive`)

**Authority.** It runs only as uid 30034 inside `learn-pod-<id>.service` (its own cgroup says so), only while `learn-fin-<id>.service` is active, only
if the admission for `<id>` names this file's sha256 and the source identity, only if the `input-read` marker exists (so only a `--run` that passed
the claim starts it) and the live claim names `<id>`. It acts only on this op's pod and containers, through a closed set of actions:
`start`, `wait-ready`, `wait-listen`, `wait-done`, `assert-listeners`, `assert-cgroups`, `checkpoint`, `stop` (TERM; pg: INT, fast shutdown),
`kill` (KILL), `reconcile`, `remove`, `restart` (pg only), `sleep`. Its budget is 1500 s, inside finalizer r5's 1800 s soft deadline.

**Readback custody.** Before it removes or kills a role container, a checkpoint acknowledged since that container's last change must hold its state:
the driver writes `req-<n>` (renamed into place), finalizer r5 archives every role and writes `ack-<n>`, and the driver requires the ack to name its
request's sha256, `ok`, every role `accepted`, and then reads the finalizer's extracted copy back from custody (the role's `learn-pod-status/1`
status, its `steps/complete` evidence, and a digest of the file manifest). The actions refuse a destructive act without that, whatever the plan says.
Every action goes to the hash-chained `~/.local/state/learn-drive/<id>/drive.jsonl` and to the journal as `FSGG <nonce> DRIVE ...` (the
finalizer's marker reader ignores the pod unit's main process, so these lines cannot forge its MEMEV).

**The plan** (`PLAN`): pg; `recv-init` (`init --root`, `enroll-producer`, then `serve`); `host-init` (wait for pg, `init`, `verify-installed-adoption`,
then `serve`); `feed-admin` (`prepare-main-admission`, resume, then hold); checkpoint `admitted`. Clean restart: TERM host, INT pg, TERM recv,
checkpoint `clean-restart`, remove host and recv, restart pg, start `recv-restart` and `host-restart`. **SIGKILL of host** (carry-forward item 5):
checkpoint `pre-kill-host`, KILL, `reconcile` (the killed container's own cgroup holds no process, so nothing of it, the Runner child included,
survives into the restart; there is no shared PID namespace, so this is restart reconciliation, not orphan detection), remove, start `host-restart`.
SIGKILL of pg, reconcile, restart, host must be ready again. Receiver down: TERM recv, 30 s, checkpoint `recv-down`, remove, start `recv-restart`.
Replay: checkpoint `pre-replay`, remove `feed-admin`, start `feed-replay` (copy `/in` to `/work/spool`, check it against the sealed manifest's
digests, export the credential, `activate-remote`, `drain`), wait until done. End: TERM host and recv, so all three roles are done and the
finalizer triggers. Five checkpoints of eight.

**Fail closed.** On any failure the driver aborts: it never removes evidence, fills an empty or dead role slot with a hold container, and TERMs every
role, so the finalizer archives each `/out` and retires; feed does not end `ok`, so the run cannot be a success. Its wait for the finalizer is
bounded by the finalizer's `RuntimeMaxSec`. It then removes the credential directory. Exit 0 complete, 3 aborted, 5 refused before acting.

### The credential generator

`gen_credentials`, called by the driver, following `deployment/telemetry-collector/qualify_host_state.py`: in `<cred_root>/<id>/` (refused unless
`<cred_root>` is on a tmpfs, a 0700 directory of this account, and `<id>` is new), CSPRNG values from `secrets.token_urlsafe` (pg password,
operator and runner tokens, the collector grant, the PFX password), written `O_EXCL` 0600 and never printed, logged or passed in an argv; an
ephemeral P-256 CA and a `localhost` server certificate signed for one day, exported to a PFX with `-passout file:`; the keys, request and work
directory removed. One directory per slot holds exactly the files its generations name (host never holds the collector grant), handed to the
container uid with `podman unshare chown` (999 for pg, 1000 for apps) and checked. `host-config.json` is `fsgg.telemetry.host-config/2` with one
`native-collector` grant for the replay identity. No podman secret is used: podman's file driver stores secrets on disk, not in tmpfs.

### Held in this revision

- **Image Ids** (`app_image`, `pg_image`): admitted after the build and the pull; every real run refuses until a pin-only revision.
- **Eight plan values** marked `HELD` (`validate_plan` lists them): the Host's `verify-installed-adoption` request and `serve` flags (fixture executor,
  identities, the chaining of `init`'s backup identity), `prepare-main-admission` and the resume control, and the producer's `activate-remote`
  flags and the `drain` binding digest. They belong to the pod harness (#5184); a plan holding one refuses before any container starts.

### Tests

`python3 tests/learn-pod-operator/run_r2.py` (and `run.py` for r1). Fake podman/systemctl worlds as in the r1 and finalizer suites; the step runner
and probe run for real under `/bin/sh`; the credential generator runs the real `openssl`. `tests/learn-finalizer-r5/run.py` runs r2's unit argv,
names, checkpoint requests, ack check and custody readback against the real finalizer r5. `tests/learn-finite-profile/run.py` scans r2 and this
README with the baseline's identifier classes.

### What is NOT established

- Whether `--cgroups=split` places pod members (and the infra container) under the pod unit's cgroup. The driver asserts it (`assert-cgroups`) and
  aborts if not; the first harness run is the check.
- Seccomp applicability to PostgreSQL and the .NET applications, the CLI and Host wire compatibility, a Generic credential for the Host outbox,
  `cp --preserve` and `sha256sum` in the base image, `openssl` on the host: pod-harness items.
- That `docker-entrypoint.sh` of the pinned pg image honours `POSTGRES_PASSWORD_FILE` with a read-only root and `PGDATA` on the volume.
- Finalizer r5's success does not read the pod unit's exit status, so a driver that aborts after the replay is visible only in the drive log and the
  unit's `ExecMainStatus` (recorded by the finalizer as `opUnit`). Root's readback checks both.
