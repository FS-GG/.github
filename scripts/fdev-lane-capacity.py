#!/usr/bin/env python3
"""Read-only fdev capacity probe: admit or hold the next heavy lane (.github#5172).

fdev load reached 21-25 on 12 cores with two Ready lanes waiting for capacity (2026-11-01 progress
report). Root checks this probe before each heavy launch (dotnet build/test, SDK re-qualification,
native workload). It reads the host, prints ONE JSON line, and exits 0 for `admit` or 3 for `hold`.

  scripts/fdev-lane-capacity.py                       # default thresholds
  scripts/fdev-lane-capacity.py --min-mem-gib 10      # tighten one threshold

Verdict (every condition must hold to admit):
  - 5-minute load average < max-load-per-core x nproc          (default 0.85)
  - MemAvailable in /proc/meminfo >= min-mem-gib GiB           (default 6)
  - free disk at --disk-path (default $HOME) >= min-disk-gb GB (default 20, decimal GB)
  - active heavy processes <= max-heavy, only when --max-heavy is given (default: reported, not graded)

A value the probe cannot read is a `hold` reason ("unknown: ..."), never an admit: a missing input does
not become a pass. The probe only reads. It starts nothing, kills nothing and writes no file.

Heavy processes are matched on argv, not on a name substring: `dotnet build|test|publish|pack|restore`,
a `dotnet ... MSBuild.dll` node, `cargo`/`rustc`, and `podman|buildah build`. This process is excluded.

Tests point `--proc-root` at a fixture tree (loadavg, meminfo, <pid>/cmdline) and pass `--nproc`.
Exit codes: 0 admit, 3 hold, 2 usage error (argparse).
"""
import argparse
import json
import os
import sys

GIB = 1024 ** 3
GB = 1000 ** 3
DOTNET_VERBS = {"build", "test", "publish", "pack", "restore", "msbuild"}


def cpu_count():
    """CPUs this process may run on (what `nproc` prints), else the machine's."""
    try:
        return len(os.sched_getaffinity(0))
    except (AttributeError, OSError):
        return os.cpu_count()


def read_text(path):
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        return handle.read()


def heavy_kind(argv):
    """Classify one process argv as a heavy-build kind, or None."""
    if not argv:
        return None
    exe = os.path.basename(argv[0])
    rest = argv[1:]
    if exe == "dotnet":
        if any(os.path.basename(a).lower() == "msbuild.dll" for a in rest):
            return "msbuild-node"
        verb = next((a for a in rest if not a.startswith("-")), "")
        if verb in DOTNET_VERBS:
            return "dotnet-" + verb
        return None
    if exe.lower() in ("msbuild", "msbuild.exe"):
        return "msbuild-node"
    if exe in ("cargo", "rustc"):
        return exe
    if exe in ("podman", "buildah") and "build" in rest:
        return exe + "-build"
    return None


def count_heavy(proc_root, own_pid):
    kinds = {}
    for entry in os.listdir(proc_root):
        if not entry.isdigit() or int(entry) == own_pid:
            continue
        try:
            raw = read_text(os.path.join(proc_root, entry, "cmdline"))
        except OSError:
            continue  # the process exited between listdir and read
        kind = heavy_kind([a for a in raw.split("\0") if a])
        if kind:
            kinds[kind] = kinds.get(kind, 0) + 1
    return kinds


def probe(args):
    reasons = []
    mem_bytes = disk_bytes = None  # graded unrounded; the printed figures are rounded
    out = {"nproc": args.nproc or cpu_count()}

    try:
        fields = read_text(os.path.join(args.proc_root, "loadavg")).split()
        out["load1"], out["load5"], out["load15"] = (float(fields[0]), float(fields[1]), float(fields[2]))
    except (OSError, ValueError, IndexError) as exc:
        out["load1"] = out["load5"] = out["load15"] = None
        reasons.append("unknown: loadavg unreadable (%s)" % type(exc).__name__)

    try:
        for line in read_text(os.path.join(args.proc_root, "meminfo")).splitlines():
            if line.startswith("MemAvailable:"):
                mem_bytes = int(line.split()[1]) * 1024
                out["mem_available_gib"] = round(mem_bytes / GIB, 2)
                break
        else:
            raise ValueError("no MemAvailable")
    except (OSError, ValueError, IndexError) as exc:
        out["mem_available_gib"] = None
        reasons.append("unknown: MemAvailable unreadable (%s)" % type(exc).__name__)

    out["disk_path"] = args.disk_path
    try:
        stat = os.statvfs(args.disk_path)
        disk_bytes = stat.f_bavail * stat.f_frsize
        out["disk_free_gb"] = round(disk_bytes / GB, 2)
    except OSError as exc:
        out["disk_free_gb"] = None
        reasons.append("unknown: free disk unreadable (%s)" % type(exc).__name__)

    try:
        kinds = count_heavy(args.proc_root, os.getpid())
        out["heavy"] = {"count": sum(kinds.values()), "kinds": kinds}
    except OSError as exc:
        out["heavy"] = None
        reasons.append("unknown: process table unreadable (%s)" % type(exc).__name__)

    limit = args.max_load_per_core * out["nproc"] if out["nproc"] else None
    out["thresholds"] = {
        "load5_below": limit,
        "mem_available_gib_min": args.min_mem_gib,
        "disk_free_gb_min": args.min_disk_gb,
        "heavy_max": args.max_heavy,
    }
    if out["load5"] is not None:
        if limit is None:
            reasons.append("unknown: nproc unavailable")
        elif out["load5"] >= limit:
            reasons.append("load5 %.2f >= %.2f (%.2f x %d cores)" % (out["load5"], limit, args.max_load_per_core, out["nproc"]))
    if mem_bytes is not None and mem_bytes < args.min_mem_gib * GIB:
        reasons.append("MemAvailable %.2f GiB < %.2f GiB" % (out["mem_available_gib"], args.min_mem_gib))
    if disk_bytes is not None and disk_bytes < args.min_disk_gb * GB:
        reasons.append("free disk %.2f GB < %.2f GB at %s" % (out["disk_free_gb"], args.min_disk_gb, args.disk_path))
    if args.max_heavy is not None and out["heavy"] is not None and out["heavy"]["count"] > args.max_heavy:
        reasons.append("heavy processes %d > %d" % (out["heavy"]["count"], args.max_heavy))

    out["verdict"] = "hold" if reasons else "admit"
    out["reasons"] = reasons
    return out


def main(argv=None):
    parser = argparse.ArgumentParser(description="Read-only fdev capacity probe: prints admit or hold as one JSON line.")
    parser.add_argument("--max-load-per-core", type=float, default=0.85, help="hold when 5-minute load >= this x nproc (default 0.85)")
    parser.add_argument("--min-mem-gib", type=float, default=6.0, help="hold when MemAvailable is below this many GiB (default 6)")
    parser.add_argument("--min-disk-gb", type=float, default=20.0, help="hold when free disk is below this many decimal GB (default 20)")
    parser.add_argument("--max-heavy", type=int, default=None, help="optional: hold when more heavy build processes than this (default: report only)")
    parser.add_argument("--disk-path", default=os.path.expanduser("~"), help="path whose filesystem free space is checked (default $HOME)")
    parser.add_argument("--proc-root", default="/proc", help="procfs root; tests point it at a fixture tree")
    parser.add_argument("--nproc", type=int, default=None, help="override the CPU count (default: what nproc prints)")
    args = parser.parse_args(argv)
    result = probe(args)
    print(json.dumps(result, sort_keys=True, separators=(",", ":")))
    return 0 if result["verdict"] == "admit" else 3


if __name__ == "__main__":
    sys.exit(main())
