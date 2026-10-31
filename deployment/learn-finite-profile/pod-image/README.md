# Pod image `learn-pod-app:r1` and the role entrypoint

Source for the image that the `host`, `recv` and `feed` containers of the LEARN finite executor pod run, and for the builder that makes it. Design:
`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` sections 2.2 and 2.3. Refs FS-GG/.github#4647.

**This directory builds nothing, installs nothing and runs nothing.** No image has been built from it. Root admits the context install, the build and
every later step separately.

| File | What it is |
|---|---|
| `Containerfile` | `FROM` the admitted `learn-qualification:v1` Id (`20a6df20…`), not reader image r4; copies the pinned artifacts; the `RUN` step re-hashes them |
| `role` | the entrypoint script (`/opt/fsgg/pod/role`), the `learn-pod-status/1` contract |
| `learn-build-pod-app-r1.py` | the builder; runs as `fsgg-learn-qual`; schema `home-learn-build-pod-app/1` |
| `telemetry-host-tree.json` | the 55 files of the Telemetry Host 0.8.0 tool tree, with sizes and sha256, extracted from the nupkg at build time |

Tests: `tests/learn-pod-image/run.py`, workflow `learn-pod-image-selftest`.

## What goes into the image

The build context is a root-owned, read-only copy that Root assembles at install time under
`/usr/local/libexec/fsgg-learn/pod-image-r1/` (written through `/srv/bulk/services/usr-local-libexec-fsgg-learn/pod-image-r1/`). Its file set is exactly:

| Context path | sha256 | Source |
|---|---|---|
| `Containerfile`, `role`, `telemetry-host-tree.json` | pinned in the builder; checked against the repository by the tests | this directory |
| `learn-probe` | `66f8254e…b303` | the admitted static socket probe (772,112 B), the same bytes as in reader image r4; built from `baseline/source/learn-probe.c` |
| `host/fsgg-coord-orchestration-host` | `b3d6a6e6…7c47` | matched pair, local build of Coordination `5d525ad2423e733a38ce75fb1f9cac6cf76f214e` (design 2) |
| `runner/fsgg-coord-orchestration-runner` | `0e07e813…9768` | the same build |
| `manifest.json` | `6144181f…56cf` | the bundle manifest of that build |
| `producer/fsgg-coord-engine` | `5e517e8d…f725` | `FS.GG.Coord.Cli` `coherent-set/v0.113.0`, the CLI fdev already uses to drain telemetry |
| `telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg` | `44273495…ba89` | release `telemetry-host/v0.8.0`, the same package the live receiver runs |

The large artifacts are not in this repository. The private custody copy of the matched pair is described in the design document; the builder refuses any
other bytes.

## What the builder does

Run as `fsgg-learn-qual`. It

1. checks the context: every directory from `/` to the context and below is a root-owned directory not writable by group or other; every file is a
   root-owned regular file not writable by group or other; there is no symlink, FIFO or other type; the file set and every sha256 equal its constants;
2. extracts the tool tree from the pinned nupkg into a private staging directory (`~/.local/state/learn-build/pod-app-r1-*`), member by member: only
   members under `tools/net10.0/linux-x64/`, regular files only, the member set, every size and every sha256 equal to `telemetry-host-tree.json`, whose own hash
   and list hash are pinned; it reads the framework minimums from the tree's `runtimeconfig.json` and refuses anything but the two frameworks;
3. builds with `--network=none --pull=never --no-cache --timestamp 0`, passing every pin as a `--build-arg` taken from **its own constants** and the staged tree as
   `--build-context telemetry=<staging>`;
4. checks the image: `Config.User` `1000:1000`; entrypoint `catatonit -- /opt/fsgg/pod/role`; empty `Cmd`; the base's layers are a prefix of the image's; no shim on
   `PATH`; `DOTNET_gcServer=0`. It records the image Id, which is what Root admits.

It starts no container and removes the staging directory on every path.

Inside the build, the `RUN` step (as root) re-hashes what was **copied into the image** against the builder's build arguments, so a context file swapped between
step 1 and the build fails the build. It also fails unless `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` exist under `/usr/share/dotnet/shared` at or above the
tree's minimums (compared with `sort -V`, highest installed version), and prints `RUNTIME <name> <version> (minimum <min>)`, which the builder parses and records. Design
section 2.3 settled that the base ships both at 10.0.12; this is where that is asserted instead of remembered. It adds a `pod` user and group at 1000 if the base has
none, and refuses a `pod` entry at another id.

The base image `20a6df20` is not reader image r4: nothing is inherited of r4's one-shot entrypoint, its single `/out/.status`, its slot helper or its `dotnet` shim.

## The role entrypoint and `learn-pod-status/1`

```text
catatonit -- /opt/fsgg/pod/role <host|recv|feed> [--ready-tcp PORT | --ready-file PATH] [--ready-timeout S] [--volume PATH] -- <command> [args...]
```

Each container has its own tmpfs at `/out/<role>/` and a writable `/work` (the pod operator provides both). `role`:

1. refuses an unknown role (exit 64), a missing or symlinked `/out/<role>`, or an existing `.status` or `.done` (exit 70); pre-allocates `.status` (exactly 4096 B, fsync)
   and writes `{"schema":"learn-pod-status/1","role":…,"phase":"starting"}`; creates `/work/home` and `/work/tmp` (exit 71, status stays `starting`);
2. starts the command with stdin from `/dev/null`; when the readiness probe passes it overwrites the status **in place** (`dd conv=notrunc`, same inode, 4096 B) with
   `phase: ready`. `--ready-tcp` means a socket in LISTEN state on the port in `/proc/net/tcp` or `tcp6`; `--ready-file` means a regular file, not a symlink. No probe means ready
   when the command is started (`feed` runs to completion and has no listener). A probe that has not passed after `--ready-timeout` (default 120 s) stops the command and ends
   `error` with detail `ready-timeout`;
3. the first TERM or INT stops the command (TERM, 20 s, then KILL) and ends `signalled`;
4. a command that exits ends `ok` (exit 0), `enospc` (non-zero while `/out` or the `--volume` path has under 1 MiB free) or `error`;
5. the terminal status is `{"phase":"terminal","exit":N,"reason":…,"detail":…}`, then `.done` (0 B) is created, then `role` **blocks** so the finalizer can archive a live
   container. A second TERM exits 0;
6. a bad option, a missing `--` or an empty command is a terminal `gate-refused` (exit 64) and runs nothing.

`detail` is built from fixed words and integers, so the JSON is always valid. The reader profile's cooperative caps (32 processes, 10 CLR slots) are not applied: the enforced bounds are
the cgroup limits in design section 3. The `pg` container uses the PostgreSQL image's own entrypoint and has no `role`; its terminal evidence belongs to finalizer r5.

## What the pod operator has to provide (contract for the pod start)

Not implemented here; the pod operator's start is a later PR. The image assumes: `--read-only`, `--read-only-tmpfs=false`, a per-container tmpfs at `/out/<role>` and at `/work`
(sizes set by the operator), the loop volume mounted at `/recv` for `recv`, `--user 1000:1000`, and `DOTNET_GCHeapHardLimit` set per container (the image sets
`DOTNET_gcServer=0` because the Telemetry Host's runtimeconfig turns server GC on).

## What is NOT established

These are open and stay open until a pod harness runs; none is claimed by this source.

- **CLI 0.113.0 against Host 0.8.0.** Whether `fsgg-coord-engine telemetry workspace drain` speaks the wire contract the Telemetry Host 0.8.0 receiver accepts. The versions are
  pinned to the live pair; compatibility is a harness spike.
- **A Generic credential in `host-config/2`** for the Host outbox. Design 2.3 chose `native-collector` for the replay principal; whether the Host's outbox accepts a Generic credential is a spike.
- **Single-file bundle extraction.** The Host and Runner payloads are self-contained single-file executables. Whether they extract anything at start, and whether `/work` may be mounted
  `noexec`, is untested. The image sets `HOME` and `TMPDIR` under `/work` and does not set `DOTNET_BUNDLE_EXTRACT_BASE_DIR`.
- **Seccomp applicability** of the admitted profile to the .NET applications. The pod precheck (later PR) fails closed on `EPERM` and `ENOSYS`.
- **The build tools in the base.** The `RUN` step uses `find`, `sort -V`, `sha256sum`, `cut`, `ls` and `grep`. They are present in any coreutils and findutils base but were not
  inspected in `20a6df20`; the first build is the check, and a missing tool fails the build.
- **`COPY --chmod` and `--build-context` on the installed podman** (6.1.3 on fdev). Not exercised; the first build is the check.
- **`/bin/sh` of the base.** `role` uses only POSIX `sh`. The tests run it under the runner's `/bin/sh` (dash in CI, bash on fdev); the base's own `sh` is exercised only by the first harness run.
- **The PostgreSQL image.** Pulled, not built, by one separately admitted acquisition (design 2.1). Not part of this source.

The tests execute `role` and the `RUN` step for real (with their absolute paths replaced by temporary directories), and drive the builder against a fake podman. They do not
build an image.
