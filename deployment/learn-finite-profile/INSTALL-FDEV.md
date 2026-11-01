# Installing the LEARN finite pod on fdev

The install route for the LEARN finite executor on fdev. Root decision 7
([#4730](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543)) makes fdev the host wherever that is feasible,
because Home is unavailable. Refs FS-GG/.github#5066 (the Delivery) and FS-GG/.github#4647 (the Outcome).

**This document installs nothing.** Each step marked **Root-executed** is an effect that Root admits and runs separately, and
records on #5066. The other steps are unprivileged readbacks that anyone with a clone can run.

## Where fdev stands (read-only probes, 2026-11-01)

The LEARN qualification account moved into fdev on 2026-10-30 (Atlas `environment/home-subsystems-migrated-into-fdev`). The
Home install journal `home-learn-install-journal/1` is therefore already live on fdev:

- `install-manifest.json` hashes to `c0ff55ff…54e3`, which `BASELINE.json` records.
- `verify_baseline.py --install-journal … --custody-dir …` reports `installedVerified 20`, `journalFiles 20`, `custodyVerified 5`
  and `errors []`.
- The live `user-30034.slice` reads `memory.max 6442450944`, `pids.max 1280` and `cpu.max 200000 100000`. Its drop-in hashes to
  `d6f4f58d…`.
- `/etc/fsgg/learn/seccomp-8bd52f8d.json` hashes to its name. `/usr/bin/python3.14` hashes to `375532f8…`, the same as in the
  journal.
- The user manager for uid 30034 delegates `cpuset cpu io memory pids` and lingers.
- **The retirement acknowledgement is gone.** Neither `/etc/fsgg/learn/retirement-acks` nor the persistent `retirement-acks/`
  exists. The failure record of `a0-20261022t192443z-f1-pre` still hashes to the admitted `3db3a5f3…7210e`. Until step 2 below
  runs, every operator run refuses with `retirement-failure-unacknowledged`.

### How each journal element maps to fdev

| Journal element (`home-learn-install-journal/1`) | On fdev | Status |
|---|---|---|
| `files` (20 entries under `/usr/local/libexec/fsgg-learn`) | the read-only bind of `/srv/bulk/services/usr-local-libexec-fsgg-learn` on the persistent bulk disk | present; 20/20 verified |
| `files`: `/etc/fsgg/learn/seccomp-8bd52f8d.json`, `/etc/systemd/system/user-30034.slice.d/learn-limits.conf` | fdev's root disk, which comes from the fdev image | present and verified. Durable changes are **Home-only** (an image rebuild); a direct write would last only until the next image upgrade. This route changes neither file |
| `journalDir` `/var/lib/fsgg-learn` | absent. `/var/lib` sits on the root disk and would not survive an image upgrade | replaced by `fdev-install-journal/` on the bulk disk (below) |
| `installScriptSha256` and the `adds[].script` installers | Home's installer scripts. One is in custody but embeds private Home paths; four are not in custody | replaced by the Root-executed steps in this document, each recorded in the fdev journal with this document's sha256 |
| `group` `fsgg-learn-evidence` (gid 699) | present; it has one member, the developer account | equivalent |
| `accountDirs`, `newDirs` (account home, `.local/state`, `learn-custody`, `learn-evidence`) | on the bulk disk at the original path, with the journal's owners and modes (`0710`, `0700`, `2710`) | equivalent |
| `createdRootDirs` | `/usr/local/libexec/fsgg-learn/**` is on the bind; `/etc/**` is on the root disk | as above |
| `python3` (path and sha256) | the same bytes | equivalent |
| Hash-pinned sudoers entry (retired) | `developer` has full `NOPASSWD` sudo | authority is procedural: an admission comment plus the admission file (design section 10) |

### Feasibility on fdev

| Need | Probe | Result |
|---|---|---|
| Rootless podman | podman 6.1.3, cgroups v2, overlay driver, `catatonit` and `pasta` present | feasible |
| Transient user units for uid 30034 | `user@30034.service` running and lingering | feasible |
| Slice limits 6G / 1280 / 200% | live cgroup readback | present |
| cgroup delegation | `cpuset cpu io memory pids` | present. `io` is delegated on fdev; on Home it was not |
| Loop volume per op | `CONFIG_BLK_DEV_LOOP=m`, module not loaded, `/dev/loop-control` (root:disk 0660) present, `losetup` and `mkfs.ext4` installed | feasible, root only (module autoload, `losetup`, `mount`) |
| Root-owned copy route | `/srv/bulk/services/usr-local-libexec-fsgg-learn` is root:root 0755 and the bind view is read-only | feasible. Writes go through `/srv/bulk/services/…` |
| Privilege | `sudo -n true` succeeds | available. Every privileged step is Root-executed |
| Disk | bulk disk 3.9 TiB free; the qualification filesystem 154 GiB free; root disk 51 GiB free | sufficient |
| Memory | `MemAvailable` about 27 GiB of 31 GiB | well above the 4 GiB guard |
| PostgreSQL image pull | registry token and manifest fetch worked earlier (design Unknown 6); the pull has not been exercised | open; a Root-admitted acquisition |
| Base image `learn-qualification:v1` (`20a6df20…`) in the account's podman storage | not probed, because it needs uid 30034 | Root readback in step 0 |

## Files to install

All from `main` at `55cf44e651baa5dcc24f9749c9bbb38eee795b6a`. Re-hash each file before you copy it, and refuse a mismatch.
The source path is `/srv/bulk/services/usr-local-libexec-fsgg-learn/`. The view path is `/usr/local/libexec/fsgg-learn/`.

| Repository file | Installed name (under the source and view paths) | Mode | sha256 |
|---|---|---|---|
| `ack/learn-retirement-ack-r2.py` | `learn-retirement-ack-r2.py` | 0755 | `4079753c66e1dcc1a6f713dd4be54330e2f2c6aeeb5e3b55f6814877ddd75cab` |
| `operator/learn-operator-r6.py` | `learn-operator-r6.py` | 0755 | `16b6da1e576222353318a73dc63268533e655995dd9ad5afe0239237e58ce035` |
| `seal/learn_seal_claim.py` | `learn_seal_claim.py` | 0755 | `dc09930149e0bcab194e8e5fea6766d0abd8fcdbbb81612dde19272cabaae1bf` |
| `finalizer/learn-finalizer-r5.py` | `learn-finalizer-r5.py` (the path the pod operator pins) | 0755 | `94300e048bcf8f0abe76feb1420b2e55b3886af10402268459a68b413acbf440` |
| `pod-image/learn-build-pod-app-r1.py` | `learn-build-pod-app-r1.py` | 0755 | `a0bbd9d941f7e351e154c6222a952457aeb948948f499b2d6a74890199916f90` |
| `pod-image/Containerfile` | `pod-image-r1/Containerfile` | 0644 | `02c0c69cbde58c1bc5c8b4fa337e182d8f6616e4789119728144490584ac7fe1` |
| `pod-image/role` | `pod-image-r1/role` | 0644 | `f2d9a062113032a94309dd030df768663bf7e1a9895e8c986e20a5575d3801d9` |
| `pod-image/telemetry-host-tree.json` | `pod-image-r1/telemetry-host-tree.json` | 0644 | `cf91c97abee180ea08accbdf81a53205bd87595ec01c77b16014efaac955b4a6` |
| `pod-image/coord-cli-tree.json` | `pod-image-r1/coord-cli-tree.json` | 0644 | `79267cd19b4fa78f9bad20416497ac7cda2d73ba17e8102307fe29410b255acb` |

The context also needs these artifacts, which are not in this repository. The builder refuses any other bytes.

| Context path under `pod-image-r1/` | sha256 | Where it comes from |
|---|---|---|
| `learn-probe` | `66f8254ec7abf6469a3dd354f3846dbc557fd19762ca8189c484bc03557fb303` | the installed `reader-image-r4/learn-probe` (same bytes) |
| `host/fsgg-coord-orchestration-host` | `b3d6a6e664cf55ad25a110daa3bf1578a6f6210755e22857f5bddff139877c47` | Root's private custody copy of the matched pair |
| `runner/fsgg-coord-orchestration-runner` | `0e07e81306aed69f443d3abe9449a54ef38e05d436146c24a811c903be9f9768` | the same |
| `manifest.json` | `6144181f413c7f9dfe971c9170de92e54a9bc54c21019efd452809d2100256cf` | the same |
| `producer/FS.GG.Coord.Cli.0.113.0.nupkg` | `100f0879436d41df98aa0879aa8ea279dde3e39d3119733875744ac8e2f79789` | release `coherent-set/v0.113.0` of this repository |
| `telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg` | `44273495ed12083b0c9762818ca0ef5688418e6fbc46bde9f2565b400c5aba89` | release `telemetry-host/v0.8.0` of this repository |

Not installed by this route:

- **The pod operator.** `learn-pod-operator-r1.py` (`d34f812d…`) pins no finalizer and no image, so every real run refuses.
  It can start nothing. The pod operator's start is the next source revision (see "What is still owed").
- **Finalizer r5 may change.** Follow-up #5175 changes the finalizer's bytes. Root can install `94300e04…` now and replace it
  after #5175 merges, or wait and install once. Either way the admission names exactly one finalizer sha256.

## The fdev install journal

The journal is `fdev-install-journal/` under the source path: root:root 0755, persistent, and readable through the view. It
holds one file per add set, `<seq>-<set>.json`, with `seq` as a four-digit counter starting at `0001`. Each file is written
`O_EXCL`, root:root 0644, and never edited. A removal is a new entry. The original `install-manifest.json` stays as it is, so
`verify_baseline.py --install-journal` keeps checking the Home baseline.

```json
{
  "schema": "fdev-learn-install-journal/1",
  "seq": 1,
  "set": "ack-r2",
  "utc": "<UTC time of the write>",
  "prevSha256": "<sha256 of the previous entry; entry 0001 uses the install-manifest.json sha256 c0ff55ff…>",
  "admission": "<URL of Root's admission comment>",
  "procedure": {"path": "deployment/learn-finite-profile/INSTALL-FDEV.md", "commit": "<main commit>", "sha256": "<file sha256>"},
  "files": [
    {"view": "/usr/local/libexec/fsgg-learn/<name>", "repoPath": "<path or null>", "repoCommit": "<commit or null>",
     "sha256": "<hex>", "mode": "0o755", "uid": 0, "gid": 0, "priorExists": false, "priorSha256": null, "action": "created"}
  ],
  "dirs": [{"view": "<path>", "mode": "0o755", "created": true}],
  "readback": {"utc": "<time>", "viewSha256Equal": true, "modeEqual": true, "ownerRoot": true}
}
```

`action` is `created`, `replaced` or `removed`. A `replaced` or `removed` entry carries `priorSha256`. The readback block is
filled in from the view, never from the source path.

## Install steps

Run them in this order. A failed readback stops the sequence. Steps 1 to 7 are install steps. Step 8 is per operation.

### Step 0: readbacks before install

1. *Unprivileged.* Check out `main` at the commit above and re-hash every file in the two tables.
2. *Unprivileged.* Run `python3 deployment/learn-finite-profile/verify_baseline.py --install-journal /usr/local/libexec/fsgg-learn/install-manifest.json --custody-dir <custody dir>`.
   Expect `installedVerified 20` and `errors []`.
3. *Unprivileged.* Read the live slice (6442450944 / 1280 / `200000 100000`) and re-hash the slice drop-in and the seccomp file.
4. **Root-executed** (read-only, but as uid 30034). Run `systemctl --user list-units --all 'learn-*'` and `podman ps -a`, and
   expect neither to list anything. Run `podman image exists 20a6df20169ce6721ed91fddd0d24ae7fe8de655008b422df0c3299821b7d4b8`
   and expect exit 0. List every `learn-evidence/*/record.json` with schema `learn-op-retirement-failure/1`, and expect exactly
   one: `a0-20261022t192443z-f1-pre`. Any other unacknowledged failure needs its own admission before step 2.
   *Admission input:* none; this step changes nothing.

### Step 1: ack writer r2, plus the journal directory

**Root-executed.** Create `fdev-install-journal/` (root:root 0755) under the source path. Copy `learn-retirement-ack-r2.py` with
`install -o root -g root -m 0755` to the source path. Read it back through the view (sha256, mode, owner). Write journal entry
`0001-ack-r2`.
*Admission inputs:* the file sha256 `4079753c…5cab`, this document's commit and sha256.

### Step 2: restore the acknowledgement (design section 11, gate 1)

**Root-executed.** Run:

```text
/usr/bin/python3 -I /usr/local/libexec/fsgg-learn/learn-retirement-ack-r2.py --ack --op-id a0-20261022t192443z-f1-pre \
  --record-sha256 3db3a5f38aec1a01d2d5fbbb471bb272c44fc88c633537523245a67620b7210e --record-file <that op's learn-evidence record.json>
```

Then run `--check` with the same op id and digest. Expect exit 0 and the readback to equal. Record the RESULT sha256 in journal
entry `0002-ack`.
*Admission inputs:* the digest admitted in [#4516 6360174387](https://github.com/FS-GG/.github/issues/4516#issuecomment-6360174387)
and the writer sha256 from step 1.

### Step 3: operator r6

**Root-executed.** Install `learn-operator-r6.py` (0755) and read it back. Write journal entry `0003-operator-r6`. Operator r5
stays installed and is the rollback.
*Admission inputs:* `16b6da1e…e035`. Operator r6's re-qualification (A27/A27b, the A0 summary, A29 and an A0 r4 smoke) is a set
of runs that needs its own admission. Home's harness r3b is not in custody; see "What is still owed".

### Step 4: seal and claim tool

**Root-executed.** Install `learn_seal_claim.py` (0755) and read it back. Write journal entry `0004-seal`. Nothing is sealed or
claimed by this step.
*Admission inputs:* `dc099301…e1bf`.

### Step 5: finalizer r5

**Root-executed.** Install `learn-finalizer-r5.py` (0755) and read it back. Write journal entry `0005-finalizer-r5`.
*Admission inputs:* `94300e04…f440`, or the later sha256 after #5175, which Root chooses.

### Step 6: pod image context and builder

**Root-executed.** Create `pod-image-r1/` with the subdirectories `host/`, `runner/`, `producer/` and `telemetry-host/` (all
root:root 0755). Copy the ten context files (root:root 0644), and only those ten. Install `learn-build-pod-app-r1.py` (0755).
Read back every file through the view. Write journal entry `0006-pod-image-context`.
*Admission inputs:* the eleven sha256 values in the two tables above. The two nupkgs are fetched unprivileged from this
repository's releases; the release API reports the same sha256 digests. The matched pair comes from Root's private custody.

### Step 7: acquisitions and the build (effects as uid 30034)

1. **Root-executed.** Pull the PostgreSQL image once as uid 30034, by digest:
   `podman pull docker.io/library/postgres@sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280`. Record
   three identities: the index digest, the `linux/amd64` platform manifest digest (`7341002d…46d7` on 2026-10-30), and the local
   config Id from `podman image inspect`.
   *Admission inputs:* the index digest, plus Root's egress allowance for this one pull. Decision 1 (#5068) governs egress
   during runs, not this acquisition.
2. **Root-executed.** Build the image: `sudo -n -u fsgg-learn-qual /usr/local/libexec/fsgg-learn/learn-build-pod-app-r1.py`.
   Expect exit 0 and a `home-learn-build-pod-app/1` RESULT with the image Id and two `RUNTIME` lines at 10.0.12. Record the
   RESULT sha256 and the image Id in journal entry `0007-images`, together with the pg identities.
   *Admission inputs:* the builder sha256 and the context readback from step 6. The image Id is new. Root admits it after the
   build, for the pod operator's pin.

### Step 8: the loop volume (per operation, not an install step)

**Root-executed** for every pod op. Create a fixed 1 GiB ext4 image on the custody disk. Loop-mount it with
`nosuid,nodev,noexec` at the op's mount point. Create `pgdata/` (428678:428678, 0700) and `recv/` (428679:428679, 0700). After
retirement, remount it read-only for the independent readback. Root unmounts it, and disposal is a separate admission. The mount
point path is still to be confirmed by the pod operator's start (the finalizer r5 README names the expected form).

## Receipts and readback (design section 9)

The install receipt that Root posts on #5066 carries:

- every journal entry's sha256, and the chain from `c0ff55ff…` to the last entry;
- for each installed file, its view path, sha256, mode and owner, read through the view;
- `verify_baseline.py --install-journal` still at `installedVerified 20` and `errors []`;
- the slice, seccomp and ack re-verified after the install (`--check` exit 0);
- the image config Ids, the builder RESULT sha256 and the runtime versions it printed;
- the three PostgreSQL identities;
- the matched-pair hashes (`b3d6a6e6…`, `0e07e813…`, `6144181f…`).

The receipt op (a no-input op whose finalizer record shows in-sandbox Host and Runner usage) needs the pod operator's start, so
it is held with it.

## Rollback

Each step's rollback is a new journal entry with `action: removed`. Every rollback is **Root-executed**.

| Step | Rollback |
|---|---|
| 1, 3, 4, 5, 6 | Remove the installed file, or `pod-image-r1/`, through the source path, then read the view back as absent. Operator r5, finalizer r4 and ack writer r1 stay installed, so the reader profile keeps working |
| 2 | `learn-retirement-ack-r2.py --remove --op-id a0-20261022t192443z-f1-pre`, only on its own admission. Afterwards every operator run refuses again |
| 7 | As uid 30034, `podman rmi <image Id>` for the pod image and for the pg image |
| 8 | Unmount, then dispose of the image on its own admission |

Rollback never touches `install-manifest.json`, the evidence tree or a claim.

## Pod-harness qualification on a synthetic snapshot

**There is no command yet.** The harness, the pod operator's start and a synthetic-snapshot route are all owed source. The
harness PR has to deliver these cases with the expected verdicts below (design sections 3, 6, 7, 8 and 10):

| Case | Expected verdict |
|---|---|
| Pod precheck: only `lo`, external and link-local connects fail, listeners exactly 5432 plus the declared ports | pass |
| Non-loopback interface, non-empty capability set, seccomp set that differs, `EPERM`/`ENOSYS` at start, host uid outside {428678, 428679}, image Id other than the pin | refuse |
| Unacknowledged retirement failure, missing admission, concurrency or headroom over the limit, reader op active | refuse |
| Loop volume not mounted, not fresh, not 1 GiB, missing `nosuid,nodev,noexec`, wrong owners | refuse |
| Snapshot file changed, extra, missing, symlinked or unreadable; second claim; claim naming another op; op id reused | refuse |
| Clean restart (`host`, `pg`, `recv`), each preceded by a checkpoint | fence, backup identity and clocks unchanged; no second launch |
| SIGKILL of `host` while an invocation is non-terminal | restart reconciliation; no second provider call; unproven terminal stays unknown |
| SIGKILL of `pg` during the invocation | Host refuses, then reconciles after `pg` restarts |
| Receiver down while `host` emits | outbox retained, drained exactly once after restart, original timestamps |
| Deadline passes, `/out` over its cap, volume full | `killed-retired`, `rejected:budget` or `enospc`; retirement clean |
| Every op | finalizer record `learn-op-pod-*/1`; no new unacknowledged `learn-op-retirement-failure/1`; units and containers empty; slice back near its baseline |

The first harness run also settles the spikes that the image and finalizer READMEs list: CLI 0.113.0 against Host 0.8.0, a
Generic credential in `host-config/2`, pausing and archiving inside a pod, `pg_controldata` from a one-shot container, and
seccomp applicability.

## What is still owed

| Item | Why it blocks | Owner |
|---|---|---|
| The pod operator's start (r2): finalizer and image pins, the loop-volume and container prechecks, the finalizer and pod units, the containers | No pod can start; the receipt op and the harness need it | source, #5066 |
| The recovery driver and the credential generator | Section 6 cases and per-run TLS | source, #5066 |
| How the Host's section 5 sequence runs inside the pod without `podman exec` (`init`, `verify-installed-adoption`, `serve`, `prepare-main-admission`, resume), and the receiver's `init --root` and `enroll-producer` before `serve` | Each container runs one `role` command, and the finalizer archives only `host`, `recv` and `feed`. The design does not say where the one-shot steps run or how their evidence reaches custody | Root decision on the design, then source |
| A synthetic-snapshot route | `learn_seal_claim.py` pins the two real spool subtrees, so it cannot seal a synthetic source. The harness needs its own source identity that can never collide with the real claim | Root decision, then source |
| A pod harness on fdev | Home's harness r3b source is not in custody (design Unknown 7) | source |
