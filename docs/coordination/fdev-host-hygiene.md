# fdev host hygiene: browser-run holds, /tmp headroom, disk-backed test temp and dead-run cleanup

Delivery: [#5240](https://github.com/FS-GG/.github/issues/5240), under [#4730](https://github.com/FS-GG/.github/issues/4730);
extends the capacity probe of [#5172](https://github.com/FS-GG/.github/issues/5172).

On 2026-11-02 the quiet-host bench probe never admitted for 60 minutes (load 5 to 9), `/tmp` (a 16 GB tmpfs shared by
every lane) filled and caused 38 spurious SDD test failures, and many browser smokes ran at once. Four pieces, each
source-managed here and installed by Root after merge.

## 1. Two holds in the capacity probe

`scripts/fdev-lane-capacity.py` (read-only, one JSON line, exit 0 `admit` or 3 `hold`) now also holds when:

| Hold | Default | Flag | JSON |
|---|---|---|---|
| At least N headless Chromium **main** processes are running | 2 (`0` disables) | `--browser-hold-at N` | `browsers.count`, `browsers.pids`, `thresholds.browser_hold_at` |
| Free space on `/tmp` is below N GiB | 3 | `--min-tmp-gib N`, `--tmp-path DIR` | `tmp_free_gib`, `tmp_path`, `thresholds.tmp_free_gib_min` |

A browser run is one process whose `argv[0]` is a Chromium binary (`chrome`, `chromium`, `chromium-browser`,
`google-chrome*`, `chrome-headless-shell`, `headless_shell`), that carries `--headless` (or `--headless=...`) and
`--remote-debugging-pipe` or `--remote-debugging-port`, and has no `--type=`. The renderer, GPU, zygote, utility and
crashpad children of one browser therefore count once, and a `node ... chrome ...` or `timeout 600 node ...` launcher
is not a browser. Chromium rewrites its process title, so on fdev `/proc/<pid>/cmdline` is **one space-joined string** with a trailing NUL
(for the main process and every `--type=` child). The probe splits a single field that contains spaces on spaces and, for
that rewritten case, takes `argv[0]`'s basename from `readlink /proc/<pid>/exe` when readable. Fixtures
(`tests/fdev-lane-capacity/fixtures/chromium-*.cmdline`) are captured from a real Playwright `chrome-headless-shell` 1243;
with one such browser running the probe reported `browsers.count 1`, and with two it held (`browser runs 2 >= 2`).
`/tmp` space is reported apart from `disk_free_gb` (which is `$HOME`). An unreadable `/tmp` or
process table is an `unknown:` hold, like every other probe input. Tests: `tests/fdev-lane-capacity/`
(`--proc-root` fixtures, so the host's real processes and `/tmp` never decide a test).

## 2. A short, disk-backed TMPDIR

`/tmp` is RAM. The wrapper `fdev-chromium-env` now runs its command with `TMPDIR=$HOME/.cache/fsgg-tmp` (mode 0700,
created on demand) **unless `TMPDIR` is already set and non-empty**, which is never changed (`TMPDIR=/tmp` opts out).
`FDEV_TMPDIR=<dir>` overrides the default path.

The path is deliberately short. A Unix socket path (`sun_path`) holds at most 107 bytes. The SDD race test
(`GenerationSourceRaceCharacterizationTests.fs`) binds `$TMPDIR/sdd-race-characterization-<32 hex>/inputs/socket`, which is
`TMPDIR` plus 73 bytes, so a `TMPDIR` over 34 bytes fails it; a lane's `/home/developer/.fsgg-scratch/788b/tmp5` (39 bytes)
already does. The wrapper refuses a default longer than 32 bytes (`FDEV_TMPDIR_MAX`), warns, and leaves `TMPDIR` unset;
`/home/developer/.cache/fsgg-tmp` is 31 bytes, and a test models that exact socket path against the budget.

**.NET test runs** do not need the browser libraries on `LD_LIBRARY_PATH`, so take only the directory:

```bash
export TMPDIR="$(fdev-chromium-env --print-tmpdir)"   # prints and creates ~/.cache/fsgg-tmp; exit 1 if it cannot
dotnet test ...
```

or run the whole command through the wrapper (`fdev-chromium-env -- dotnet test ...`), which also puts the Chromium
bundle on `LD_LIBRARY_PATH` for that command. Set it for the whole shell that launches `dotnet test`: `dotnet`,
MSBuild and the test host all read `TMPDIR`. Caveat: MSBuild reuses build nodes (`/nodeReuse`) only between processes
that share a `TMPDIR`, so a mix of lanes with different `TMPDIR` values may start separate node pools instead of
sharing one; move lanes to the disk path together where node reuse matters.

The wrapper had no repository source before this Delivery (it was only `~/.local/bin/fdev-chromium-env`, from #5193).
Its source of truth is now `deployment/fdev-chromium/fdev-chromium-env`; edit that and reinstall. Tests:
`tests/fdev-chromium-env/`. The directory is created with `mkdir -p` and then `chmod 0700`, which also tightens an
existing looser directory of yours (the required shellcheck run forbids `mkdir -p -m`, SC2174).

## 3. Dead-run cleanup

`scripts/fdev-tmp-cleanup.py` removes known harness directories from `/tmp`, `$TMPDIR` and `~/.cache/fsgg-tmp`.
**It is a dry run unless `--apply` is given.**

```bash
python3 -I scripts/fdev-tmp-cleanup.py                 # dry run: one JSON line, nothing changes
python3 -I scripts/fdev-tmp-cleanup.py --apply         # remove
python3 -I scripts/fdev-tmp-cleanup.py --root DIR      # sweep only DIR (repeatable; must exist)
python3 -I scripts/fdev-tmp-cleanup.py --min-age-hours 2
```

Known names (an exact shape directly under a root; anything else is never touched, including a bare `tmp.*`):

| Pattern | Owner test | Idle period |
|---|---|---|
| `fsgg-sdd-tests-<pid>-<32 hex>` | the PID in the name is dead, and no live process references it | 1 h (`--min-age-hours`) |
| `fsgg-sdd-<32 hex>`, `fsgg-ai-tests-<32 hex>`, `fsgg-ai-tr-<32 hex>`, `fsgg-resolver-<32 hex>`, `fsgg-cli-tests-cache-sandbox-<32 hex>` | no live process references it | **6 h** (`--min-age-hours-pidless`) |
| `playwright_chromiumdev_profile-<6>`, `org.chromium.Chromium.<6>` | no live process references it | **6 h** |
| `tmp.<10>` containing `driver/run-smoke.mjs` (a browser-smoke mktemp tree) | no live process references it | **6 h** |

A PID-less entry has no PID to prove its run is dead, so it needs a longer quiet period. Root decided on 6 hours
explicitly, against holders that keep a directory only in memory (a process that never opens, maps or names it again).
Not swept on purpose: `playwright-artifacts-*` (empty on the real host, and a live run's artifacts directory is not
referenced by any process) and `fsgg-gate-*` (the gate-engine build cache is reused; there is no per-run pattern). A
directory is removed only when **all** hold:

1. **The owner is dead.** With a PID in the name, `/proc/<pid>` must not exist. In every case no live process may
   reference the entry: its path, or a path that resolves to it (a relative word resolved against that process's cwd, a
   symlinked alias resolved by `realpath`), as a command-line word or flag value, an environment value, the cwd, an
   open file descriptor, a mapped file, or a bound unix socket (`/proc/net/unix`). This is what
   `--user-data-dir=/tmp/playwright_chromiumdev_profile-X` and `node /tmp/tmp.X/driver/run-smoke.mjs` give. A PID reused by
   an unrelated process keeps the entry.
2. **Idle.** The newest mtime of the entry and everything below it is older than the idle period. A tree with more than
   400000 nodes, or one that crosses a mount point, is kept.
3. **A real directory owned by this user.** A symlink entry is never followed and never removed.
4. **No same-user process is unknown to the scan.** A process of this user whose cwd, fds or environ cannot be read (a
   non-dumpable one, whose `/proc/<pid>` directory belongs to root, so its owner is read from its `status` file) could hold an
   entry invisibly. While one exists, `--apply` refuses to delete anything (exit 3; the pids and names are in
   `unreadable`, and a dry run reports `apply_blocked: true`). Exempt by name: `systemd --user`, `(sd-pam)` and sshd. A
   transient `sudo` started by the same user is enough to block one run; the next timer run retries.

Removal is iterative (no recursion; a 1,100-level tree is tested) and walks with directory file descriptors and
`O_NOFOLLOW`, raising the open-file limit to the hard limit first because it holds one descriptor per level. It unlinks a
symlink inside a tree as a link, does not cross filesystems, and chmods read-only directories it owns so `.git` trees go.
The process table is read again before the first deletion and each entry's inode and mtime must be unchanged since the
survey. If the process table cannot be read it exits 3 and removes nothing; euid 0 is refused (exit 2). Killing a process
is never part of it.

Output keys: `mode`, `roots`, `entries_matched`, `would_remove`, `would_free_bytes`, `removed`, `freed_bytes`, `failed`,
`unreadable`, `apply_blocked`, `by_pattern`, `kept` (by reason: `young`, `pid-alive`, `referenced-by-live-process`,
`symlink`, `not-a-directory`, `not-owned`, `changed-since-survey`, `mount-point`, `tree-too-large`), `removable` (first
`--list-limit` entries). Tests: `tests/fdev-tmp-cleanup/` (fixture tree and fake procfs; live PIDs, live references by each
route, young trees, symlinks, uninspectable same-user processes and unknown names are kept).

### Dry run on the real host, 2026-11-02 (current code)

`python3 -I scripts/fdev-tmp-cleanup.py --list-limit 3` against the real `/tmp` while other lanes were running (exit 0,
default dry run, nothing changed, `removed: 0`):

```
mode dry-run   roots [/tmp]   processes_read 376   unreadable []   apply_blocked false
entries_matched 1077   would_remove 874   would_free_bytes 3569328128 (3.3 GiB)   kept {young: 203}
by_pattern (removable/matched)  browser-smoke 14/14  chromium-temp 444/492  fsgg-sdd-run 124/237
                                fsgg-test-root 219/261  playwright-profile 73/73
```

The kept entries are younger than their idle period (1 h or 6 h), which includes the browser smokes running at that moment.

## 4. The timer

The unit templates live beside the board-tend units in `deployment/fdev-board-tend/` and are installed by the same
`install.py`, as their own effect (`tmp-cleanup`), so the existing `units` effect, its tests and its rollback are
unchanged:

| Unit | When | What |
|---|---|---|
| `fsgg-tmp-cleanup.timer` | hourly at minute 37, `Persistent=true`, up to 120 s random delay | starts the service |
| `fsgg-tmp-cleanup.service` | the timer | `python3 -I ~/.local/libexec/fdev-tmp-cleanup.py --apply`; `Type=oneshot`, `Nice=15`, `IOSchedulingClass=idle`, 600 s cap, no `PrivateTmp` (it must see the lanes' real `/tmp`) |

Minute 37 is away from board tend's :07, the drift observer's :23, the :53 dispatch and the 00:30 daily record.

## Install (Root, after merge; nothing here changes the host by itself)

Each command has `--dry-run`, refuses to run as root and refuses a destination outside `$HOME`. From a clone of
FS-GG/.github at the merged commit:

```bash
# 1. The wrapper (replaces ~/.local/bin/fdev-chromium-env; --force because the installed copy differs)
cp -p ~/.local/bin/fdev-chromium-env ~/.local/bin/fdev-chromium-env.pre-5240   # BACKUP FIRST: the old copy has no other source
python3 -I deployment/fdev-board-tend/install.py chromium-env --dry-run
python3 -I deployment/fdev-board-tend/install.py chromium-env --force

# 2. The cleanup script, service and timer; --enable runs daemon-reload and enable --now for the timer
python3 -I deployment/fdev-board-tend/install.py tmp-cleanup --dry-run
python3 -I deployment/fdev-board-tend/install.py tmp-cleanup --enable

# 3. Before enabling the timer, look at what it would remove right now
python3 -I scripts/fdev-tmp-cleanup.py
```

The probe needs no install: Root runs `scripts/fdev-lane-capacity.py` from the checkout.

Verify: `systemctl --user list-timers fsgg-tmp-cleanup.timer`, then after the first run
`journalctl --user -u fsgg-tmp-cleanup.service -n 5` shows the JSON line; `fdev-chromium-env --print-tmpdir` prints
`~/.cache/fsgg-tmp`.

Rollback: `systemctl --user disable --now fsgg-tmp-cleanup.timer`, delete `~/.config/systemd/user/fsgg-tmp-cleanup.{service,timer}`
and `~/.local/libexec/fdev-tmp-cleanup.py`, then `systemctl --user daemon-reload`. For the wrapper, copy the backup taken in
step 1 back: `install -m 0755 ~/.local/bin/fdev-chromium-env.pre-5240 ~/.local/bin/fdev-chromium-env`. The previous wrapper
exists nowhere else (before this Delivery it was only the installed copy), so keep that backup until the new one has run.

## Known limits

- Lanes are expected to run with `TMPDIR` set to the disk path (section 2). Directories that lanes still create in `/tmp`
  are cleaned by the timer once idle. A PID-less entry held only in memory by a process the scan can inspect (it never
  opens, maps or names the path again) is protected only by the 6-hour idle rule.
- The cleanup does not touch `playwright-artifacts-*`, `fsgg-gate-*`, `tmp.*` trees without the smoke fingerprint, `claude-*`
  scratch, MSBuild or `.NET` runtime files (`clr-debug-pipe-*`, `dotnet-diagnostic-*`), or anything outside the table. New
  harness patterns are added to the table and to `PATTERNS` with a fixture.
- The probe samples; it does not reserve. A lane admitted at one browser can still start the second before the next
  probe, so the browser hold narrows the pile-up rather than serialising it.
