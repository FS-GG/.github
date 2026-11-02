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
  runs, every operator run refuses with `retirement-failure-unacknowledged`. The harness uses that state once, for A27 (step H2).

### How each journal element maps to fdev

| Journal element (`home-learn-install-journal/1`) | On fdev | Status |
|---|---|---|
| `files` (20 entries under `/usr/local/libexec/fsgg-learn`) | the read-only bind of `/srv/bulk/services/usr-local-libexec-fsgg-learn` on the persistent bulk disk | present; 20/20 verified |
| `files`: `/etc/fsgg/learn/seccomp-8bd52f8d.json`, `/etc/systemd/system/user-30034.slice.d/learn-limits.conf` | fdev's root disk, which comes from the fdev image | present and verified. Durable changes are **Home-only** (an image rebuild); a direct write would last only until the next image upgrade. This route changes neither file |
| `journalDir` `/var/lib/fsgg-learn` | absent. `/var/lib` sits on the root disk and would not survive an image upgrade | replaced by `fdev-install-journal/` on the bulk disk (below) |
| `installScriptSha256` and the `adds[].script` installers | Home's installer scripts. One is in custody but embeds private Home paths; four are not in custody | replaced by the Root-executed steps in this document, each recorded in the fdev journal with this document's sha256 |
| `group` `fsgg-learn-evidence` (gid 699) | present; it has one member, the Root operator account | equivalent |
| `accountDirs`, `newDirs` (account home, `.local/state`, `learn-custody`, `learn-evidence`) | on the bulk disk at the original path, with the journal's owners and modes (`0710`, `0700`, `2710`) | equivalent |
| `createdRootDirs` | `/usr/local/libexec/fsgg-learn/**` is on the bind; `/etc/**` is on the root disk | as above |
| `python3` (path and sha256) | the same bytes | equivalent |
| Hash-pinned per-operator entry (retired) | Root-executed steps use the host's administrative route | authority is procedural (admission comment plus admission file, design §10) |

### Feasibility on fdev

| Need | Probe | Result |
|---|---|---|
| Rootless podman | podman 6.1.3, cgroups v2, overlay driver, `catatonit` and `pasta` present | feasible |
| Transient user units for uid 30034 | `user@30034.service` running and lingering | feasible |
| Slice limits 6G / 1280 / 200% | live cgroup readback | present |
| cgroup delegation | `cpuset cpu io memory pids` | present. `io` is delegated on fdev; on Home it was not |
| Loop volume per op | `CONFIG_BLK_DEV_LOOP=m`, module not loaded, `/dev/loop-control` (root:disk 0660) present, `losetup` and `mkfs.ext4` installed | feasible, root only (module autoload, `losetup`, `mount`) |
| Root-owned copy route | `/srv/bulk/services/usr-local-libexec-fsgg-learn` is root:root 0755 and the bind view is read-only | feasible. Writes go through `/srv/bulk/services/…` |
| Privilege | the host's administrative route | available to Root. Every privileged step is Root-executed |
| Disk | bulk disk 3.9 TiB free; the qualification filesystem 154 GiB free; root disk 51 GiB free | sufficient |
| Memory | `MemAvailable` about 27 GiB of 31 GiB | well above the 4 GiB guard |
| PostgreSQL image pull | registry token and manifest fetch worked earlier (design Unknown 6); the pull has not been exercised | open; a Root-admitted acquisition |
| Base image `learn-qualification:v1` (`20a6df20…`) in the account's podman storage | not probed, because it needs uid 30034 | Root readback in step 0 |

## Files to install

Pinned to `main` at `7cd6f84f82c7e3c6df5f860a90010783f733ef65` (the harness merge, #5204). Re-hash each file before you copy it, and refuse a
mismatch. The source path is `/srv/bulk/services/usr-local-libexec-fsgg-learn/`. The view path is `/usr/local/libexec/fsgg-learn/`. The last
commit that changed each file is given so a reviewer can find the reviewed bytes.

| Repository file | Installed name (under the source and view paths) | Mode | sha256 | Last changed in |
|---|---|---|---|---|
| `ack/learn-retirement-ack-r2.py` | `learn-retirement-ack-r2.py` | 0755 | `4079753c66e1dcc1a6f713dd4be54330e2f2c6aeeb5e3b55f6814877ddd75cab` | `4b26764b445571e22693bf8af4747fbc53cfd142` |
| `operator/learn-operator-r6.py` | `learn-operator-r6.py` (the harness pins this sha256) | 0755 | `16b6da1e576222353318a73dc63268533e655995dd9ad5afe0239237e58ce035` | `e88df005d256c12b93c95a5fedbc6e8e9c67a849` |
| `pod-harness/learn-pod-harness-r1.py` | `learn-pod-harness-r1.py` | 0755 | `b0f5700e20465cdf30a78925ddf29ceb735ba0e26d4e23558ee1cbcff7955489` | `7cd6f84f82c7e3c6df5f860a90010783f733ef65` |
| `seal/learn_seal_claim.py` | `learn_seal_claim.py` | 0755 | `3db55d803007b33102e3028d01a12dab47f64925a5e9c74dcce58847941f40d0` | `7cd6f84f82c7e3c6df5f860a90010783f733ef65` |
| `finalizer/learn-finalizer-r5.py` | `learn-finalizer-r5.py` (pod operator r2 pins this sha256) | 0755 | `c1de5c03598e6f1e315de7f5d55ce394f35b0880991c28467410420a042556d7` | `7cd6f84f82c7e3c6df5f860a90010783f733ef65` |
| `pod-image/learn-build-pod-app-r1.py` | `learn-build-pod-app-r1.py` | 0755 | `a0bbd9d941f7e351e154c6222a952457aeb948948f499b2d6a74890199916f90` | `18d2fb7ab3e7d7d75ee63a8c6556bf52846226c2` |
| `pod-image/Containerfile` | `pod-image-r1/Containerfile` | 0644 | `02c0c69cbde58c1bc5c8b4fa337e182d8f6616e4789119728144490584ac7fe1` | `18d2fb7ab3e7d7d75ee63a8c6556bf52846226c2` |
| `pod-image/role` | `pod-image-r1/role` | 0644 | `f2d9a062113032a94309dd030df768663bf7e1a9895e8c986e20a5575d3801d9` | `18d2fb7ab3e7d7d75ee63a8c6556bf52846226c2` |
| `pod-image/telemetry-host-tree.json` | `pod-image-r1/telemetry-host-tree.json` | 0644 | `cf91c97abee180ea08accbdf81a53205bd87595ec01c77b16014efaac955b4a6` | `18d2fb7ab3e7d7d75ee63a8c6556bf52846226c2` |
| `pod-image/coord-cli-tree.json` | `pod-image-r1/coord-cli-tree.json` | 0644 | `79267cd19b4fa78f9bad20416497ac7cda2d73ba17e8102307fe29410b255acb` | `18d2fb7ab3e7d7d75ee63a8c6556bf52846226c2` |
| `pod-operator/learn-pod-operator-r2.py` | **not installed as merged** (see below) | | `78633c4a4da83d30191d1522df441b5c48ae9759a68dac832d7801cc95b16106` | `7cd6f84f82c7e3c6df5f860a90010783f733ef65` |

The context also needs these artifacts, which are not in this repository. The builder refuses any other bytes.

| Context path under `pod-image-r1/` | sha256 | Where it comes from |
|---|---|---|
| `learn-probe` | `66f8254ec7abf6469a3dd354f3846dbc557fd19762ca8189c484bc03557fb303` | the installed `reader-image-r4/learn-probe` (same bytes) |
| `host/fsgg-coord-orchestration-host` | `b3d6a6e664cf55ad25a110daa3bf1578a6f6210755e22857f5bddff139877c47` | Root's private custody copy of the matched pair (Coordination `5d525ad2423e733a38ce75fb1f9cac6cf76f214e`) |
| `runner/fsgg-coord-orchestration-runner` | `0e07e81306aed69f443d3abe9449a54ef38e05d436146c24a811c903be9f9768` | the same |
| `manifest.json` | `6144181f413c7f9dfe971c9170de92e54a9bc54c21019efd452809d2100256cf` | the same |
| `producer/FS.GG.Coord.Cli.0.113.0.nupkg` | `100f0879436d41df98aa0879aa8ea279dde3e39d3119733875744ac8e2f79789` | release `coherent-set/v0.113.0` of this repository |
| `telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg` | `44273495ed12083b0c9762818ca0ef5688418e6fbc46bde9f2565b400c5aba89` | release `telemetry-host/v0.8.0` of this repository |

Two pins change after this document:

- **The pod operator.** Pod operator r2 as merged has `app_image` and `pg_image` empty, so every real run refuses. Its pin-only revision
  fills those two values with the Ids Root admits in step 7. It is a separate reviewed source PR whose diff against `78633c4a…` is those
  two values. Step 10 installs that revision at `learn-pod-operator-r2.py`, the path its `self` value and the harness name. The admission
  names the revision's own sha256.
- **Finalizer r5.** Follow-up #5175 also changes the finalizer. Whichever of #5175 and its rebase merges last re-pins `finalizer_sha` in the
  pod operator. Install the finalizer whose sha256 the installed pod operator pins, and nothing else.

## The fdev install journal

The journal is `fdev-install-journal/` under the source path: root:root 0755, persistent, and readable through the view. It
holds one file per add set, `<seq>-<set>.json`, with `seq` as a four-digit counter starting at `0001`. Each file is written
`O_EXCL`, root:root 0644, and never edited. A removal is a new entry. The original `install-manifest.json` stays as it is, so
`verify_baseline.py --install-journal` keeps checking the Home baseline.

```json
{
  "schema": "fdev-learn-install-journal/1",
  "seq": 1,
  "set": "operator-r6",
  "utc": "<UTC time of the write>",
  "prevSha256": "<sha256 of the previous entry; entry 0001 uses the install-manifest.json sha256 c0ff55ff…>",
  "admission": "<URL of Root's admission comment>",
  "procedure": {"path": "deployment/learn-finite-profile/INSTALL-FDEV.md", "commit": "<main commit>", "sha256": "<file sha256>"},
  "files": [
    {"view": "/usr/local/libexec/fsgg-learn/<name>", "repoPath": "<path or null>", "repoCommit": "<commit or null>",
     "sha256": "<hex>", "mode": "0o755", "uid": 0, "gid": 0, "priorExists": false, "priorSha256": null, "action": "created"}
  ],
  "dirs": [{"view": "<path>", "mode": "0o755", "created": true}],
  "results": [{"what": "<command or case>", "exitCode": 0, "resultSha256": "<hex>"}],
  "readback": {"utc": "<time>", "viewSha256Equal": true, "modeEqual": true, "ownerRoot": true}
}
```

`action` is `created`, `replaced` or `removed`. A `replaced` or `removed` entry carries `priorSha256`. The readback block is
filled in from the view, never from the source path. `results` records the RESULT sha256 of each command or harness case that the set ran.

## The ordered sequence

Run the rows in this order. A failed readback or an unexpected verdict stops the sequence; nothing is retried. The step numbers are
stable identifiers (the harness README cites steps 1, 2, 3 and 7), so the order is given here and not by the numbers.

| # | Step | Who | Journal entry |
|---|---|---|---|
| 1 | Step 0: readbacks before install | unprivileged, then Root (read-only, as uid 30034) | none |
| 2 | Step 3: the journal directory and operator r6 | **Root-executed** | `0001-operator-r6` |
| 3 | Step 9: the pod harness | **Root-executed** | `0002-pod-harness` |
| 4 | Step H1: harness `plan` | **Root-executed** (as uid 30034, no effect) | none; its RESULT sha256 goes in Root's admission |
| 5 | Step H2: cases `census`, `a27-unacked` | **Root-executed** (as uid 30034) | `0003-harness-a` |
| 6 | Step 1: ack writer r2 | **Root-executed** | `0004-ack-r2` |
| 7 | Step 2: the f1-pre acknowledgement | **Root-executed** | `0005-ack` |
| 8 | Step H3: cases `a0-summary`, `exclusion-reader`, `a29-cap`, `n5b-cgroups`, then `summary` | **Root-executed** (as uid 30034) | `0006-harness-ab` |
| 9 | Step 4: seal and claim tool | **Root-executed** | `0007-seal` |
| 10 | Step 5: finalizer r5 | **Root-executed** | `0008-finalizer-r5` |
| 11 | Step 6: pod image context and builder | **Root-executed** | `0009-pod-image-context` |
| 12 | Step 7: the Host build check, the pg pull and the image build | unprivileged check, then **Root-executed** (as uid 30034) | `0010-images` |
| 13 | Step 10: the pin-only pod operator revision (after its source PR merges) | **Root-executed** | `0011-pod-operator` |
| 14 | Step 8: the volume root, then the loop volume for the synthetic op | **Root-executed** | `0012-volume-root`; the per-op volume goes in `0013` |
| 15 | Step H4: phase C, the synthetic receipt op | **Root-executed** (Root and uid 30034) | `0013-harness-c` |

Every step that runs as uid 30034 goes through the host's administrative route and is Root-executed: it is an effect in another account.

## Install steps

### Step 0: readbacks before install

1. *Unprivileged.* Check out `main` at the commit above and re-hash every file in the two tables.
2. *Unprivileged.* Run `python3 deployment/learn-finite-profile/verify_baseline.py --install-journal /usr/local/libexec/fsgg-learn/install-manifest.json --custody-dir <custody dir>`.
   Expect `installedVerified 20` and `errors []`.
3. *Unprivileged.* Read the live slice (6442450944 / 1280 / `200000 100000`) and re-hash the slice drop-in and the seccomp file.
4. **Root-executed** (read-only, as uid 30034). Run `systemctl --user list-units --all 'learn-*'` and `podman ps -a`, and expect
   neither to list anything. Run `podman image exists` for the base image `20a6df20169ce6721ed91fddd0d24ae7fe8de655008b422df0c3299821b7d4b8`
   and for reader image r4 `6625dfb774feabc256d94d15d112cf3f554d3cd7d388e1d717d74ada97c1de06`, and expect exit 0 for both. List every
   `learn-evidence/*/record.json` with schema `learn-op-retirement-failure/1`, and expect exactly one: `a0-20261022t192443z-f1-pre`. Any other
   unacknowledged failure needs its own admission before the sequence continues.
   *Admission input:* none; this step changes nothing.

### Step 3: operator r6, plus the journal directory

**Root-executed.** Create `fdev-install-journal/` (root:root 0755) under the source path. Copy `learn-operator-r6.py` with
`install -o root -g root -m 0755` to the source path. Read it back through the view (sha256, mode, owner). Write journal entry
`0001-operator-r6`. Operator r5 stays installed and is the rollback.
It is installed **before** the acknowledgement, so the harness can run A27 against the real f1-pre record.
*Admission inputs:* `16b6da1e…e035` and this document's commit and sha256.

### Step 9: the pod harness

**Root-executed.** Install `learn-pod-harness-r1.py` (root:root 0755) and read it back. Write journal entry `0002-pod-harness`. The harness
runs only from that root-owned copy.
*Admission inputs:* `b0f5700e…5489`.

### Step H1: the admission request

**Root-executed** (as uid 30034; no effect). Choose a run id (4 to 12 lowercase letters and digits) and run
`learn-pod-harness-r1.py plan --run-id <id>`. Post the RESULT sha256 on #5066 as the admitted case list. Every later `case` names that
comment with `--admission-ref`.

### Step H2: phase A before the acknowledgement

**Root-executed** (as uid 30034). Run `case --case census`, then `case --case a27-unacked`, each with `--run-id <id> --admission-ref <URL>`.
Expected: `census` clean, both images present and operator r6 hashing to `16b6da1e…`; `a27-unacked` exits 5 with exactly
`retirement-failure-unacknowledged: a0-20261022t192443z-f1-pre` and starts no precheck op. Record both RESULT sha256 values in `0003-harness-a`.

### Step 1: ack writer r2

**Root-executed.** Install `learn-retirement-ack-r2.py` (0755) and read it back. Write journal entry `0004-ack-r2`.
*Admission inputs:* `4079753c…5cab`.

### Step 2: restore the acknowledgement (design section 11, gate 1)

**Root-executed.** Run:

```text
/usr/bin/python3 -I /usr/local/libexec/fsgg-learn/learn-retirement-ack-r2.py --ack --op-id a0-20261022t192443z-f1-pre \
  --record-sha256 3db3a5f38aec1a01d2d5fbbb471bb272c44fc88c633537523245a67620b7210e --record-file <that op's learn-evidence record.json>
```

Then run `--check` with the same op id and digest. Expect exit 0, with the mounted view showing the same digest. Record the RESULT sha256
in journal entry `0005-ack`.
*Admission inputs:* the digest admitted in [#4516 6360174387](https://github.com/FS-GG/.github/issues/4516#issuecomment-6360174387)
and the writer sha256 from step 1.

### Step H3: phase A after the acknowledgement, and phase B

**Root-executed** (as uid 30034). Run these cases in order, one invocation each, then `summary --run-id <id>`:

| Case | Expected |
|---|---|
| `a0-summary` | exit 0; `failures == acked == [f1-pre]`, `unacked == []`; every precheck assertion true; the precheck record `learn-op-success/1` bound to the nonce |
| `exclusion-reader` | exit 5, `exclusion refused: a pod op is active: learn-pod-hq-<id>-x` |
| `a29-cap` | exit 5, `concurrency refused: 2 ops already active (max 2)` |
| `n5b-cgroups` | the spike removes its pod and reads it back gone; verdict `members-and-infra-under-unit`. `not-under-unit` or `split-refused` is a finding that blocks phase C until the pod start changes |

The first failed case writes `STOP` and every later case refuses. Record each RESULT sha256 and the summary in `0006-harness-ab`. The case
`a0-r4-smoke` stays held (see "What is still owed").

### Step 4: seal and claim tool

**Root-executed.** Install `learn_seal_claim.py` (0755) and read it back. Write journal entry `0007-seal`. Nothing is sealed or
claimed by this step.
*Admission inputs:* `3db55d80…40d0`.

### Step 5: finalizer r5

**Root-executed.** Install `learn-finalizer-r5.py` (0755) and read it back. Write journal entry `0008-finalizer-r5`.
*Admission inputs:* `c1de5c03…56d7`, the value pod operator r2 pins (or the re-pinned value after #5175).

### Step 6: pod image context and builder

**Root-executed.** Create `pod-image-r1/` with the subdirectories `host/`, `runner/`, `producer/` and `telemetry-host/` (all
root:root 0755). Copy the ten context files (root:root 0644), and only those ten. Install `learn-build-pod-app-r1.py` (0755).
Read back every file through the view. Write journal entry `0009-pod-image-context`.
*Admission inputs:* the eleven sha256 values of the builder and the context. The two nupkgs are fetched unprivileged from this
repository's releases; the release API reports the same sha256 digests. The matched pair comes from Root's private custody.

### Step 7: the Host build check, acquisitions and the build

1. *Unprivileged, before the build.* Confirm that the Host payload `b3d6a6e6…` embeds the assembly metadata `FsggSourceRevision` equal to
   `5d525ad2423e733a38ce75fb1f9cac6cf76f214e` (#5204 review N5). `verify-installed-adoption` compares it with the request's
   `expectedSourceRevision`, so a mismatch fails phase C. A missing or different value stops the sequence here, before any image is built.
2. **Root-executed** (as uid 30034). Pull the PostgreSQL image once, by digest:
   `podman pull docker.io/library/postgres@sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280`. Record
   three identities: the index digest, the `linux/amd64` platform manifest digest (`7341002d…46d7` on 2026-10-30), and the local
   config Id from `podman image inspect`.
   *Admission inputs:* the index digest, plus Root's egress allowance for this one pull. Decision 1 (#5068) governs egress
   during runs, not this acquisition.
3. **Root-executed** (as uid 30034). Build the image by running `/usr/local/libexec/fsgg-learn/learn-build-pod-app-r1.py`.
   Expect exit 0 and a `home-learn-build-pod-app/1` RESULT with the image Id and two `RUNTIME` lines at 10.0.12. Record the
   RESULT sha256, the image Id and the pg identities in journal entry `0010-images`.
   *Admission inputs:* the builder sha256 and the context readback from step 6. Root then admits the two config Ids for the pod
   operator's pin-only revision.

### Step 10: the pin-only pod operator revision

**Root-executed**, after the pin-only source PR merges. Install its bytes at `learn-pod-operator-r2.py` (0755) and read them back. Expect the
sha256 that PR reports, `finalizer_sha` equal to the installed finalizer and `app_image`/`pg_image` equal to step 7's Ids. Write journal
entry `0011-pod-operator`.
*Admission inputs:* the revision's sha256 and merge commit.

### Step 8: the loop volume

**Root-executed.** Once: create the volume root `/mnt/storage/containers/learn-qualification/learn-pod/` (root:root 0755) and journal it in
`0012-volume-root`. Then for every pod op: create a fixed 1 GiB ext4 image on the custody disk (`mkfs.ext4`), loop-mount it with
`rw,nosuid,nodev,noexec` at `<volume root>/<op id>.mnt`, and create `pgdata/` (428678:428678, 0700) and `recv/` (428679:428679, 0700) in a
root-owned top directory closed to group and other writers. The pod operator's precheck refuses anything else: not exactly one ext4 mount from
`/dev/loopN`, a missing option, a size outside 900 MiB to 1 GiB, more than 16 inodes or 64 MiB in use, other entries than `pgdata`, `recv`
and `lost+found`, or the wrong owners. After retirement, remount it read-only for the independent readback. Root unmounts it, and disposal is
a separate admission.

### Step H4: phase C, the synthetic receipt op

Phase C is the install receipt op and runs last. The synthetic op id is the harness's (`plan` prints it), and its argv-json is
`["learn-pod-plan/1","<plan sha256>","input:synthetic"]` (`plan` prints the exact string).

1. *Any uid.* `learn-pod-harness-r1.py synth-tree --out <new dir> --nonce <nonce>`: a synthetic spool with no real data.
2. **Root-executed.** `learn_seal_claim.py manifest --mode synthetic --synthetic-base <dir> --out <M1>`, then
   `learn_seal_claim.py seal --mode synthetic --synthetic-base <dir> --m1 <M1> --snapshot-id <id>`. Record the synthetic source identity and the
   sealed record's sha256.
3. **Root-executed.** `learn_seal_claim.py admit --op-id <syn op> --argv-json-sha256 <sha256 of the synthetic argv-json> --source-identity <syn identity>
   --operator-sha256 <step 10> --finalizer-sha256 <step 5> --image-id <app> --image-id <pg> --ttl-seconds 600`. The argv hash binds the
   synthetic kind. Mount the loop volume (step 8).
4. **Root-executed** (as uid 30034). `case --case syn-precheck --argv-json <…> --source-identity <…>`. Expected: exit 0,
   `inputProvenance synthetic`, `inputMounted false`.
5. **Root-executed.** `learn_seal_claim.py claim --mode synthetic` (with the source identity, op id, snapshot id, sealed-record sha256 and that precheck RESULT). The subcommand comes first; `--mode` is an option.
6. **Root-executed** (as uid 30034). `case --case syn-run`. Expected: exit 0; the finalizer record is `learn-op-pod-success/1` with
   `inputProvenance synthetic`, `inputRead true`, the feed container the only one started with `/in`, five accepted checkpoints and the pod
   unit `ExecMainStatus` 0; `drive.jsonl` ends complete; census after clean.
7. **Root-executed** (as uid 30034). `case --case syn-reuse`. Expected: exit 5 with `op id already used`. Then `summary`.
8. **Root-executed.** Remount the volume read-only, read it back, unmount. Record every RESULT sha256, the finalizer record sha256 and the
   `drive.jsonl` chain head in `0013-harness-c`.

A real admission cannot start a synthetic op and the reverse, so phase C consumes no real claim.

## Receipts and readback (design section 9)

The install receipt that Root posts on #5066 carries:

- every journal entry's sha256, and the chain from `c0ff55ff…` to the last entry;
- for each installed file, its view path, sha256, mode and owner, read through the view;
- `verify_baseline.py --install-journal` still at `installedVerified 20` and `errors []`;
- the slice, seccomp and ack re-verified after the install (`--check` exit 0);
- the image config Ids, the builder RESULT sha256 and the runtime versions it printed;
- the three PostgreSQL identities and the `FsggSourceRevision` readback;
- the matched-pair hashes (`b3d6a6e6…`, `0e07e813…`, `6144181f…`);
- the harness case RESULT sha256 values and its `summary`, phases A, B and C.

## Rollback

Each step's rollback is a new journal entry with `action: removed`. Every rollback is **Root-executed**.

| Step | Rollback |
|---|---|
| 1, 3, 4, 5, 6, 9, 10 | Remove the installed file, or `pod-image-r1/`, through the source path, then read the view back as absent. Operator r5, finalizer r4 and ack writer r1 stay installed, so the reader profile keeps working |
| 2 | `learn-retirement-ack-r2.py --remove --op-id a0-20261022t192443z-f1-pre`, only on its own admission. Afterwards every operator run refuses again |
| 7 | As uid 30034, `podman rmi <image Id>` for the pod image and for the pg image |
| 8 | Unmount, then dispose of the image on its own admission |
| H1 to H4 | Nothing to remove: the harness writes only its own state directory and the evidence of the ops it ran. A consumed synthetic claim stays |

Rollback never touches `install-manifest.json`, the evidence tree or a real claim.

## What is still owed

| Item | Why it blocks | Owner |
|---|---|---|
| The pin-only pod operator revision | Every real pod run refuses until `app_image` and `pg_image` are pinned | source, after step 7 |
| `a0-r4-smoke` | Home's A0 r4 harness (`12ec5ebf…`) is not in this repository. Re-pinning it to operator r6 needs that source, which is **Home-only** unless Root holds a copy | Home, or a Root decision to accept r6 without it |
| #5175 | It changes finalizer r5; the pod operator re-pins it | source |
