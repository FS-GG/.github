#!/usr/bin/env python3
"""Remove dead harness temp directories from fdev's /tmp (.github#5240). Dry-run unless --apply.

fdev's /tmp is a 16 GB tmpfs shared by every lane. Test and browser-smoke harnesses leave directories behind
when a run dies, and a full /tmp fails unrelated SDD tests (38 spurious failures on 2026-11-02). This removes
only KNOWN harness directories whose owning run is dead and which have been quiet long enough.

  scripts/fdev-tmp-cleanup.py                 # DRY RUN (the default): list what would be removed, remove nothing
  scripts/fdev-tmp-cleanup.py --apply         # remove it (the user timer passes this)
  scripts/fdev-tmp-cleanup.py --root DIR ...  # roots to sweep (default: /tmp, $TMPDIR if set, ~/.cache/fsgg-tmp)

It prints ONE JSON line (counts by pattern and by keep reason, the removable entries and their bytes). It exits
0 when it finished, 2 on a usage error, an unsafe root or euid 0, and 3 when it refused to delete: the process
table could not be read, or a same-user process could not be inspected (below). A missing input is a refusal to
delete, never a pass. It never runs as root (euid 0 is refused) and never kills a process.

KNOWN PATTERNS (an entry directly under a root, matched by exact name shape; nothing else is ever touched):

  owner PID in the name        fsgg-sdd-tests-<pid>-<32 hex>          (FS.GG.SDD tests/Shared/TestShared.fs)
                               idle for over --min-age-hours (default 1)
  no PID in the name           fsgg-sdd-<32 hex>
                               fsgg-ai-tests-<32 hex>, fsgg-ai-tr-<32 hex>, fsgg-resolver-<32 hex>,
                               fsgg-cli-tests-cache-sandbox-<32 hex>
                               playwright_chromiumdev_profile-<6>, org.chromium.Chromium.<6>
                               tmp.<10 alphanumerics> that contains driver/run-smoke.mjs (a browser-smoke
                               mktemp tree; a bare tmp.* is NEVER matched)
                               idle for over --min-age-hours-pidless (default 6)

A PID-less entry has no PID to prove the run is dead, so it needs a longer quiet period: 6 hours is an explicit
decision against holders that keep a directory only in memory (a process that never opens, maps or names it again).
Not swept on purpose: playwright-artifacts-* (empty on the real host and a live run's artifacts directory is not
referenced by any process), fsgg-gate-* (the gate-engine build cache is reused, and there is no per-run pattern).

AN ENTRY IS REMOVED ONLY WHEN ALL OF THESE HOLD
  1. The owner is dead. With a PID in the name, <proc-root>/<pid> must not exist. Always, no live process may
     reference the entry: its path, or a path that resolves to it (a relative word resolved against that
     process's cwd, a symlinked alias resolved by realpath), as a command-line word or flag value, an environment
     value, the cwd, an open file descriptor, a mapped file, or a bound unix socket (net/unix). A PID reused by an
     unrelated process keeps the entry (safe).
  2. Nothing under it changed for the idle period above: the newest mtime of the entry and every node below it.
     A tree of more than 400000 nodes, or one that crosses a mount point, is kept.
  3. It is a real directory owned by this user. A symlink at the entry is never followed and never removed.
  4. No same-user process is unknown to the scan. A process of this user whose cwd, fds or environ cannot be read
     (a non-dumpable process, whose /proc entry belongs to root) could hold an entry invisibly, so while one
     exists --apply refuses to delete anything (exit 3, reported in `unreadable`). Only a fixed allowlist is
     exempt, by name: `systemd --user`, `(sd-pam)` and sshd. The scan decides a process's owner from the Uid line
     of its status file, because the /proc directory of such a process is owned by root.

Removal never follows a symlink: the tree is walked iteratively (no recursion, so depth is unbounded) with
directory file descriptors and O_NOFOLLOW, a symlink inside a tree is unlinked as a link, and the walk does not
cross filesystems. The open-file limit is raised to the hard limit first, because the walk holds one descriptor
per level. Before the first deletion the process table is read again, and each entry's inode and mtime must be
unchanged since it was surveyed.

Tests point --proc-root at a fixture tree (<pid>/cmdline|environ|cwd|fd/N|maps|status, net/unix) and pass --root
and --now.
"""
import argparse
import errno
import json
import math
import os
import re
import resource
import stat
import sys
import time

HEX32 = r"[0-9a-f]{32}"
RAND6 = r"[A-Za-z0-9]{6}"

# (name, compiled regex, owner-pid group present, needs the smoke fingerprint)
PATTERNS = [
    ("fsgg-sdd-tests", re.compile(r"fsgg-sdd-tests-(?P<pid>\d{1,9})-" + HEX32), True, False),
    ("fsgg-sdd-run", re.compile(r"fsgg-sdd-" + HEX32), False, False),
    ("fsgg-test-root", re.compile(r"fsgg-(?:ai-tests|ai-tr|resolver|cli-tests-cache-sandbox)-" + HEX32), False, False),
    ("playwright-profile", re.compile(r"playwright_chromiumdev_profile-" + RAND6), False, False),
    ("chromium-temp", re.compile(r"org\.chromium\.Chromium\." + RAND6), False, False),
    ("browser-smoke", re.compile(r"tmp\.[A-Za-z0-9]{10}"), False, True),
]
SMOKE_FINGERPRINT = ("driver", "run-smoke.mjs")
MAX_NODES = 400000
# Same-user processes the scan may not be able to read, and that can be ignored: systemd --user, (sd-pam), sshd.
ALLOWED_UNREADABLE_COMM = {"(sd-pam)", "sshd", "sshd-session"}
NAME_CHARS = r"[^/\s\x00:=,\"'\\]+"
TOKEN_SPLIT = re.compile(r"[\s\x00=,:;\"']+")


def finite_hours(text):
    value = float(text)
    if not math.isfinite(value) or value < 0:
        raise argparse.ArgumentTypeError("must be a finite number >= 0, got %r" % text)
    return value


def classify(name):
    """(pattern, owner pid or None, needs fingerprint) for a known harness name, else None."""
    for label, regex, has_pid, fingerprint in PATTERNS:
        match = regex.fullmatch(name)
        if match:
            return label, int(match.group("pid")) if has_pid else None, fingerprint
    return None


def has_fingerprint(path):
    try:
        inner = os.lstat(os.path.join(path, SMOKE_FINGERPRINT[0]))
        if not stat.S_ISDIR(inner.st_mode):
            return False
        return stat.S_ISREG(os.lstat(os.path.join(path, *SMOKE_FINGERPRINT)).st_mode)
    except OSError:
        return False


def read_bytes(path, limit=8 * 1024 * 1024):
    try:
        with open(path, "rb") as handle:
            return handle.read(limit)
    except OSError:
        return b""


def vanished(exc):
    return isinstance(exc, (FileNotFoundError, ProcessLookupError, NotADirectoryError))


def process_uid(base):
    """Real uid of a process: the Uid line of its status file (its /proc directory belongs to root when it is
    non-dumpable), else the directory's owner."""
    try:
        for line in read_bytes(os.path.join(base, "status")).decode("utf-8", "replace").splitlines():
            if line.startswith("Uid:"):
                return int(line.split()[1])
        return os.lstat(base).st_uid
    except (OSError, ValueError, IndexError):
        return None


def allowed_unreadable(base, argv):
    """True for the fixed allowlist of same-user processes that may stay uninspected, by name."""
    comm = read_bytes(os.path.join(base, "comm")).decode("utf-8", "replace").strip()
    if comm in ALLOWED_UNREADABLE_COMM:
        return True
    first = os.path.basename(argv[0]) if argv else ""
    return (first == "systemd" or comm == "systemd") and "--user" in argv


def live_references(proc_root, roots, own_pid):
    """Top-level names under each root that a live process references, and the processes the scan could not fully read.

    Returns (names {root: set}, stats {processes, unreadable: [{pid, comm}]}). Raises OSError when the process
    table itself cannot be listed.
    """
    patterns = [re.compile(r"(?<![A-Za-z0-9_.\-])" + re.escape(root) + r"/(" + NAME_CHARS + r")") for root in roots]
    names = {root: set() for root in roots}
    real_cache = {}
    me = os.geteuid()
    unreadable = []
    processes = 0

    def note(path):
        for root in roots:
            if path.startswith(root + "/"):
                names[root].add(path[len(root) + 1:].split("/", 1)[0])

    def resolve(path):
        if path not in real_cache:
            try:
                real_cache[path] = os.path.realpath(path)
            except (OSError, ValueError, RuntimeError):
                real_cache[path] = path
        return real_cache[path]

    def harvest(blob, cwd):
        text = blob.decode("utf-8", errors="replace").replace("\x00", " ")
        for root, pattern in zip(roots, patterns):
            for match in pattern.finditer(text):
                names[root].add(match.group(1))
        # Relative words resolve against the process's cwd; every candidate is also compared by realpath, so a
        # symlinked alias of an entry is a reference to it.
        for token in TOKEN_SPLIT.split(text)[:20000]:
            if not token or len(token) > 4096:
                continue
            if token.startswith("/"):
                candidate = token
            elif cwd and ("/" in token or cwd_in_root(cwd)):
                candidate = os.path.normpath(os.path.join(cwd, token))
            else:
                continue
            if "/" not in candidate[1:]:
                continue
            note(candidate)
            if token.startswith("/") or "/" in token:
                note(resolve(candidate))

    def cwd_in_root(cwd):
        return any(cwd == root or cwd.startswith(root + "/") for root in roots)

    for entry in os.listdir(proc_root):
        if not entry.isdigit() or int(entry) == own_pid:
            continue
        base = os.path.join(proc_root, entry)
        processes += 1
        mine = process_uid(base) == me
        full = True
        cwd = None
        try:
            cwd = os.readlink(os.path.join(base, "cwd"))
            note(cwd)
            note(resolve(cwd))
        except OSError as exc:
            full = full and (vanished(exc) or not mine)
        argv = [a for a in read_bytes(os.path.join(base, "cmdline")).decode("utf-8", "replace").split("\x00") if a]
        for leaf in ("cmdline", "environ", "maps"):
            path = os.path.join(base, leaf)
            try:
                with open(path, "rb") as handle:
                    harvest(handle.read(8 * 1024 * 1024), cwd)
            except OSError as exc:
                if leaf == "environ":
                    full = full and (vanished(exc) or not mine)
        try:
            for fd in os.listdir(os.path.join(base, "fd")):
                try:
                    target = os.readlink(os.path.join(base, "fd", fd))
                    harvest(target.encode(), None)
                    note(resolve(target)) if target.startswith("/") else None
                except OSError:
                    pass
        except OSError as exc:
            full = full and (vanished(exc) or not mine)
        if mine and not full and not allowed_unreadable(base, argv):
            comm = read_bytes(os.path.join(base, "comm")).decode("utf-8", "replace").strip()
            unreadable.append({"pid": int(entry), "comm": comm or (os.path.basename(argv[0]) if argv else "")})
    for line in read_bytes(os.path.join(proc_root, "net", "unix")).splitlines():
        fields = line.split(None, 7)  # Num RefCount Protocol Flags Type St Inode Path
        if len(fields) == 8:
            harvest(fields[7], None)
    return names, {"processes": processes, "unreadable": unreadable}


def survey(path, top, cutoff):
    """Walk one entry without following symlinks: (newest mtime, bytes, nodes, verdict).

    verdict is None, or why the entry must be kept ("young", "mount-point", "tree-too-large", "unreadable").
    Stops at the first node newer than cutoff.
    """
    newest = top.st_mtime
    total = top.st_blocks * 512
    nodes = 1
    if newest > cutoff:
        return newest, total, nodes, "young"
    stack = [path]
    while stack:
        current = stack.pop()
        try:
            with os.scandir(current) as entries:
                for entry in entries:
                    info = entry.stat(follow_symlinks=False)
                    nodes += 1
                    total += info.st_blocks * 512
                    newest = max(newest, info.st_mtime)
                    if info.st_mtime > cutoff:
                        return newest, total, nodes, "young"
                    if nodes > MAX_NODES:
                        return newest, total, nodes, "tree-too-large"
                    if stat.S_ISDIR(info.st_mode):
                        if info.st_dev != top.st_dev:
                            return newest, total, nodes, "mount-point"
                        stack.append(entry.path)
        except OSError:
            return newest, total, nodes, "unreadable"
    return newest, total, nodes, None


def remove_tree(parent_fd, name, dev):
    """Delete parent_fd/name without following any symlink and without crossing filesystems.

    Iterative: one open directory descriptor per level of depth, no recursion.
    """
    def open_dir(pfd, child):
        info = os.stat(child, dir_fd=pfd, follow_symlinks=False)
        if info.st_dev != dev:
            raise OSError(errno.EXDEV, "mount point inside the tree", child)
        fd = os.open(child, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=pfd)
        try:
            now = os.fstat(fd)
            if (now.st_dev, now.st_ino) != (info.st_dev, info.st_ino):
                raise OSError(errno.ESTALE, "entry replaced while opening", child)
            if not info.st_mode & stat.S_IWUSR or not info.st_mode & stat.S_IXUSR:
                os.fchmod(fd, 0o700)
            return [fd, child, pfd, os.listdir(fd)]
        except BaseException:
            os.close(fd)
            raise

    top = os.stat(name, dir_fd=parent_fd, follow_symlinks=False)
    if not stat.S_ISDIR(top.st_mode):
        os.unlink(name, dir_fd=parent_fd)
        return
    stack = [open_dir(parent_fd, name)]
    try:
        while stack:
            frame = stack[-1]
            if frame[3]:
                child = frame[3].pop()
                info = os.stat(child, dir_fd=frame[0], follow_symlinks=False)
                if stat.S_ISDIR(info.st_mode):
                    stack.append(open_dir(frame[0], child))
                else:
                    os.unlink(child, dir_fd=frame[0])
            else:
                stack.pop()
                os.close(frame[0])
                frame[0] = -1
                os.rmdir(frame[1], dir_fd=frame[2])
    finally:
        for frame in stack:
            if frame[0] >= 0:
                os.close(frame[0])


def raise_open_file_limit():
    soft, hard = resource.getrlimit(resource.RLIMIT_NOFILE)
    want = hard if hard != resource.RLIM_INFINITY else 1 << 20
    if soft != resource.RLIM_INFINITY and soft < want:
        try:
            resource.setrlimit(resource.RLIMIT_NOFILE, (want, hard))
        except (ValueError, OSError):
            pass


def safe_root(text, home):
    """Resolve a root to a directory that is not a symlink, not / and not $HOME or an ancestor of it."""
    path = os.path.abspath(os.path.expanduser(text))
    if os.path.islink(path):
        raise ValueError("root %s is a symlink; roots are never followed" % path)
    if not os.path.isdir(path):
        raise FileNotFoundError(path)
    real = os.path.realpath(path)
    if real == "/" or real == home or home.startswith(real.rstrip("/") + "/"):
        raise ValueError("root %s is / or $HOME or an ancestor of it" % path)
    return real


def default_roots(home):
    roots = ["/tmp"]
    if os.environ.get("TMPDIR"):
        roots.append(os.environ["TMPDIR"])
    roots.append(os.path.join(home, ".cache", "fsgg-tmp"))
    return roots


def sweep(args):
    home = os.path.realpath(os.path.expanduser("~"))
    requested = list(args.root) or default_roots(home)
    roots, skipped_roots = [], []
    for text in requested:
        try:
            path = safe_root(text, home)
        except FileNotFoundError:
            if args.root:  # a root the caller named must exist; a default one may be absent
                raise ValueError("root %s does not exist" % text)
            skipped_roots.append({"root": text, "reason": "absent"})
            continue
        if path not in roots:
            roots.append(path)
    if not roots:
        raise ValueError("no root to sweep")

    now = args.now if args.now is not None else time.time()
    cutoff_pid = now - args.min_age_hours * 3600
    cutoff_pidless = now - args.min_age_hours_pidless * 3600
    uid = os.geteuid()
    try:
        refs, proc_stats = live_references(args.proc_root, roots, os.getpid())
    except OSError as exc:
        return None, "process table unreadable (%s)" % type(exc).__name__

    summary = {"mode": "apply" if args.apply else "dry-run", "roots": roots, "skipped_roots": skipped_roots,
               "min_age_hours": args.min_age_hours, "min_age_hours_pidless": args.min_age_hours_pidless,
               "processes_read": proc_stats["processes"], "unreadable": proc_stats["unreadable"],
               "apply_blocked": bool(proc_stats["unreadable"]), "entries_matched": 0, "would_remove": 0,
               "would_free_bytes": 0, "removed": 0, "freed_bytes": 0, "failed": 0,
               "by_pattern": {}, "kept": {}, "removable": [], "failures": []}
    candidates = []
    for root in roots:
        try:
            names = sorted(os.listdir(root))
        except OSError:
            summary["skipped_roots"].append({"root": root, "reason": "unreadable"})
            continue
        for name in names:
            known = classify(name)
            if known is None:
                continue
            label, owner_pid, needs_fingerprint = known
            path = os.path.join(root, name)
            try:
                top = os.lstat(path)
            except OSError:
                continue
            keep = None
            if stat.S_ISLNK(top.st_mode):
                keep = "symlink"
            elif not stat.S_ISDIR(top.st_mode):
                keep = "not-a-directory"
            elif needs_fingerprint and not has_fingerprint(path):
                continue  # a bare tmp.* is not a harness directory: not matched at all
            elif top.st_uid != uid:
                keep = "not-owned"
            summary["entries_matched"] += 1
            bucket = summary["by_pattern"].setdefault(label, {"matched": 0, "removable": 0})
            bucket["matched"] += 1
            if keep is None and owner_pid is not None and os.path.isdir(os.path.join(args.proc_root, str(owner_pid))):
                keep = "pid-alive"
            if keep is None and name in refs[root]:
                keep = "referenced-by-live-process"
            newest = size = None
            if keep is None:
                newest, size, _, verdict = survey(path, top, cutoff_pid if owner_pid is not None else cutoff_pidless)
                keep = verdict
            if keep is not None:
                summary["kept"][keep] = summary["kept"].get(keep, 0) + 1
                continue
            bucket["removable"] += 1
            summary["would_remove"] += 1
            summary["would_free_bytes"] += size
            candidates.append((root, name, path, label, owner_pid, top, size, newest))
            summary["removable"].append({"path": path, "pattern": label, "owner": ("pid %d (dead)" % owner_pid) if owner_pid else "no live reference",
                                         "bytes": size, "idle_hours": round((now - newest) / 3600, 2)})

    if args.apply and proc_stats["unreadable"]:
        who = ", ".join("%d (%s)" % (u["pid"], u["comm"]) for u in proc_stats["unreadable"][:5])
        return None, "%d same-user process(es) could not be inspected (%s); an entry could be held invisibly" % (len(proc_stats["unreadable"]), who)
    if args.apply and candidates:
        raise_open_file_limit()
        try:  # the table is read again; an entry a process picked up since the survey is kept
            fresh, fresh_stats = live_references(args.proc_root, roots, os.getpid())
        except OSError as exc:
            return None, "process table unreadable on the second read (%s)" % type(exc).__name__
        if fresh_stats["unreadable"]:
            return None, "a same-user process could not be inspected on the second read; nothing was removed"
        for root, name, path, label, owner_pid, top, size, newest in candidates:
            if name in fresh[root]:
                summary["kept"]["referenced-by-live-process"] = summary["kept"].get("referenced-by-live-process", 0) + 1
                continue
            try:
                again = os.lstat(path)
                if (again.st_ino, again.st_dev, again.st_mtime) != (top.st_ino, top.st_dev, top.st_mtime):
                    summary["kept"]["changed-since-survey"] = summary["kept"].get("changed-since-survey", 0) + 1
                    continue
                root_fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                try:
                    remove_tree(root_fd, name, top.st_dev)
                finally:
                    os.close(root_fd)
                summary["removed"] += 1
                summary["freed_bytes"] += size
            except FileNotFoundError:
                continue
            except OSError as exc:
                summary["failed"] += 1
                summary["failures"].append({"path": path, "error": type(exc).__name__ + ": " + (exc.strerror or "")})
    summary["removable"] = summary["removable"][:args.list_limit]
    summary["removable_listed_of"] = summary["would_remove"]
    return summary, None


def main(argv=None):
    parser = argparse.ArgumentParser(description="Remove dead, idle harness temp directories (dry-run unless --apply).")
    parser.add_argument("--apply", action="store_true", help="actually remove; without it nothing is changed")
    parser.add_argument("--dry-run", action="store_true", help="the default; accepted so a caller can state it")
    parser.add_argument("--root", action="append", default=[], help="a directory to sweep (repeatable; default /tmp, $TMPDIR, ~/.cache/fsgg-tmp)")
    parser.add_argument("--min-age-hours", type=finite_hours, default=1.0, help="PID-named patterns: keep anything modified within this many hours (default 1)")
    parser.add_argument("--min-age-hours-pidless", type=finite_hours, default=6.0, help="the idle period for patterns with no PID in the name (default 6)")
    parser.add_argument("--proc-root", default="/proc", help="procfs root; tests point it at a fixture tree")
    parser.add_argument("--now", type=float, default=None, help="epoch seconds to age against (tests)")
    parser.add_argument("--list-limit", type=int, default=25, help="how many removable entries to list (default 25)")
    args = parser.parse_args(argv)
    if os.geteuid() == 0:
        print("fdev-tmp-cleanup: refused: run as the fdev user, not root", file=sys.stderr)
        return 2
    if args.apply and args.dry_run:
        parser.error("--apply and --dry-run are exclusive")
    try:
        summary, refusal = sweep(args)
    except ValueError as exc:
        print("fdev-tmp-cleanup: refused: %s" % exc, file=sys.stderr)
        return 2
    if summary is None:
        print(json.dumps({"mode": "refused", "reason": refusal}, sort_keys=True, separators=(",", ":")))
        print("fdev-tmp-cleanup: %s; nothing was removed" % refusal, file=sys.stderr)
        return 3
    print(json.dumps(summary, sort_keys=True, separators=(",", ":"), allow_nan=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
