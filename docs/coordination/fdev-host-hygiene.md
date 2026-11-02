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
is not a browser. `/tmp` space is reported apart from `disk_free_gb` (which is `$HOME`). An unreadable `/tmp` or
process table is an `unknown:` hold, like every other probe input. Tests: `tests/fdev-lane-capacity/`
(`--proc-root` fixtures, so the host's real processes and `/tmp` never decide a test).

## 2. A short, disk-backed TMPDIR

`/tmp` is RAM. The wrapper `fdev-chromium-env` now runs its command with `TMPDIR=$HOME/.cache/fsgg-tmp` (mode 0700,
created on demand) **unless `TMPDIR` is already set and non-empty**, which is never changed (`TMPDIR=/tmp` opts out).
`FDEV_TMPDIR=<dir>` overrides the default path.

The path is deliberately short. A Unix socket path (`sun_path`) holds at most 107 bytes and harness trees nest under
`TMPDIR` (`fsgg-sdd-tests-<pid>-<32 hex>/...`, about 70 bytes before the socket's own name), which is why the SDD socket
test failed under a long `TMPDIR`. The wrapper refuses a default longer than 32 bytes (`FDEV_TMPDIR_MAX`), warns, and
leaves `TMPDIR` unset; `/home/developer/.cache/fsgg-tmp` is 31 bytes, and a test asserts the budget plus the longest
harness socket path stays under 107.

**.NET test runs** do not need the browser libraries on `LD_LIBRARY_PATH`, so take only the directory:

```bash
export TMPDIR="$(fdev-chromium-env --print-tmpdir)"   # prints and creates ~/.cache/fsgg-tmp; exit 1 if it cannot
dotnet test ...
```

or run the whole command through the wrapper (`fdev-chromium-env -- dotnet test ...`), which also puts the Chromium
bundle on `LD_LIBRARY_PATH` for that command. Set it for the whole shell that launches `dotnet test`: `dotnet`,
MSBuild and the test host all read `TMPDIR`.

The wrapper had no repository source before this Delivery (it was only `~/.local/bin/fdev-chromium-env`, from #5193).
Its source of truth is now `deployment/fdev-chromium/fdev-chromium-env`; edit that and reinstall. Tests:
`tests/fdev-chromium-env/`.

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

| Pattern | Owner test |
|---|---|
| `fsgg-sdd-tests-<pid>-<32 hex>` | the PID in the name is dead |
| `fsgg-sdd-<32 hex>`, `fsgg-ai-tests-<32 hex>`, `fsgg-ai-tr-<32 hex>`, `fsgg-resolver-<32 hex>`, `fsgg-cli-tests-cache-sandbox-<32 hex>` | no live process references it |
| `fsgg-gate-<name>` except `fsgg-gate-engine` | no live process references it |
| `playwright_chromiumdev_profile-<6>`, `playwright-artifacts-<6>`, `org.chromium.Chromium.<6>` | no live process references it |
| `tmp.<10>` containing `driver/run-smoke.mjs` (a browser-smoke mktemp tree) | no live process references it |

`fsgg-gate-engine` is not swept: it is the shared gate-engine build cache that later runs reuse, not a per-run
directory. A directory is removed only when **all** hold:

1. **The owner is dead.** With a PID in the name, `/proc/<pid>` must not exist. Without one, no live process may
   reference the entry: its path as a command-line word, an environment value, the cwd, an open file descriptor, a
   mapped file, or a bound unix socket (`/proc/net/unix`). This is what `--user-data-dir=/tmp/playwright_chromiumdev_profile-X`
   and `node /tmp/tmp.X/driver/run-smoke.mjs` give. Only this user's entries are removed, and this user's processes are
   readable (a non-dumpable one is not; `processes_unreadable` counts them, and the idle rule below still protects the entry).
   A PID reused by an unrelated process keeps the entry.
2. **Idle for over an hour.** The newest mtime of the entry and everything below it is older than `--min-age-hours`
   (default 1). A tree with more than 400000 nodes, or one that crosses a mount point, is kept.
3. **A real directory owned by this user.** A symlink entry is never followed and never removed.

Removal walks with directory file descriptors and `O_NOFOLLOW`, unlinks a symlink inside a tree as a link, does not cross
filesystems, and chmods read-only directories it owns so `.git` trees go. The process table is read again before the first
deletion and each entry's inode and mtime must be unchanged since the survey. If the process table cannot be read it
exits 3 and removes nothing. Killing a process is never part of it.

Output keys: `mode`, `roots`, `entries_matched`, `would_remove`, `would_free_bytes`, `removed`, `freed_bytes`, `failed`,
`by_pattern`, `kept` (by reason: `young`, `pid-alive`, `referenced-by-live-process`, `symlink`, `not-a-directory`,
`not-owned`, `changed-since-survey`, `mount-point`, `tree-too-large`), `removable` (first `--list-limit` entries).
Tests: `tests/fdev-tmp-cleanup/` (fixture tree and fake procfs; live PIDs, live references by each route, young trees,
symlinks and unknown names are kept).

### Dry run on the real host, 2026-11-02 07:2x UTC

`python3 -I scripts/fdev-tmp-cleanup.py --list-limit 4` against the real `/tmp` while other lanes were running
(exit 0, nothing changed, `removed: 0`):

```
mode dry-run   roots [/tmp]   processes_read 378   processes_unreadable 0
entries_matched 1115   would_remove 1093   would_free_bytes 3755065344 (3.5 GiB)   kept {young: 22}
by_pattern  browser-smoke 14/14  chromium-temp 492/492  fsgg-sdd-run 222/237  fsgg-test-root 254/261
            playwright-artifacts 38/38  playwright-profile 73/73      (removable/matched)
```

The 22 kept entries were younger than an hour, which includes the browser smoke running at that moment.

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
and `~/.local/libexec/fdev-tmp-cleanup.py`, `systemctl --user daemon-reload`. Reinstall the previous wrapper from
`git show <old commit>:deployment/fdev-chromium/fdev-chromium-env` (or re-run `chromium-env --force` from an earlier
checkout; before this Delivery the file was only the installed copy).

## Known limits

- Lanes are expected to run with `TMPDIR` set to the disk path (section 2). Directories that lanes still create in `/tmp`
  are cleaned by the timer once idle; a lane that keeps a directory open only through a path the process table does not
  show (for example a handle held by another user's process) is protected only by the one-hour idle rule.
- The cleanup does not touch `fsgg-gate-engine`, `tmp.*` trees without the smoke fingerprint, `claude-*` scratch, MSBuild
  or `.NET` runtime files (`clr-debug-pipe-*`, `dotnet-diagnostic-*`), or anything outside the table. New harness
  patterns are added to the table and to `PATTERNS` with a fixture.
- The probe samples; it does not reserve. A lane admitted at one browser can still start the second before the next
  probe, so the browser hold narrows the pile-up rather than serialising it.
