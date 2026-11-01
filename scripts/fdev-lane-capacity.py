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
  - cgroup v2 memory headroom (memory.max - memory.current) >= min-mem-gib GiB, when a limit is set
  - free disk at --disk-path (default $HOME) >= min-disk-gb GB (default 20, decimal GB)
  - active heavy processes <= max-heavy, only when --max-heavy is given (default: reported, not graded)

LANE LIMITS. fdev runs lanes in cgroups, so host-wide numbers can overstate what a lane may use. The probe
reads its own cgroup from <proc-root>/self/cgroup (`0::/path`) and walks from that cgroup up to the root
under --cgroup-root (default /sys/fs/cgroup). Any `cpu.max` quota (`<quota> <period>`, or `max`) lowers the
CPU count to min(affinity, ceil(quota/period)); any finite `memory.max` gives a headroom of
memory.max - memory.current (page cache counts, so this is conservative), and the smallest level wins. The
JSON reports host and lane values side by side (nproc_host/nproc_lane/nproc, mem_available_gib/
mem_lane_headroom_gib/mem_effective_gib). No cgroup v2 files means no lane limit, not a hold; a limit file
that is present but malformed is an `unknown:` hold.

A load average that is not finite or is negative (`nan`, `inf`, `-1`) is unreadable and holds as
`unknown: loadavg`; the output is strict JSON (never NaN/Infinity).

A value the probe cannot read is a `hold` reason ("unknown: ..."), never an admit: a missing input does
not become a pass. The probe only reads. It starts nothing, kills nothing and writes no file.

Heavy processes are matched on argv, not on a name substring: `dotnet build|test|publish|pack|restore`,
a `dotnet ... MSBuild.dll` node, `cargo`/`rustc`, and `podman|buildah build`. This process is excluded. An MSBuild node started with `/nodeReuse:true` is a
long-lived idle build server, not an active build: it is counted apart as `heavy.idle_msbuild_nodes` and not in
`heavy.count`.

Tests point `--proc-root` at a fixture tree (loadavg, meminfo, <pid>/cmdline) and pass `--nproc`.
Tests also pass --cgroup-root. Exit codes: 0 admit, 3 hold, 2 usage error (argparse, including --nproc < 1
and non-finite or negative thresholds).
"""
import argparse
import json
import math
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


def node_reuse(args):
    """True when the process was started with /nodeReuse:true (a reusable, idle-between-builds node)."""
    return any(a.lower().lstrip("/-") == "nodereuse:true" for a in args)


def heavy_kind(argv):
    """Classify one process argv as a heavy-build kind, or None."""
    if not argv:
        return None
    exe = os.path.basename(argv[0])
    rest = argv[1:]
    if exe == "dotnet":
        if any(os.path.basename(a).lower() == "msbuild.dll" for a in rest):
            return "msbuild-node-idle" if node_reuse(rest) else "msbuild-node"
        verb = next((a for a in rest if not a.startswith("-")), "")
        if verb in DOTNET_VERBS:
            return "dotnet-" + verb
        return None
    if exe.lower() in ("msbuild", "msbuild.exe"):
        return "msbuild-node-idle" if node_reuse(rest) else "msbuild-node"
    if exe in ("cargo", "rustc"):
        return exe
    if exe in ("podman", "buildah") and "build" in rest:
        return exe + "-build"
    return None


def cgroup_levels(args):
    """Directories of the probe's own cgroup and every ancestor, leaf first; [] when none (no cgroup v2)."""
    try:
        text = read_text(os.path.join(args.proc_root, "self", "cgroup"))
    except OSError:
        return []
    path = None
    for line in text.splitlines():
        parts = line.split(":", 2)
        if len(parts) == 3 and parts[0] == "0" and parts[1] == "":
            path = parts[2]
    if path is None:
        return []
    names = [n for n in path.split("/") if n and n not in (".", "..")]
    return [os.path.join(args.cgroup_root, *names[:i]) for i in range(len(names), -1, -1)]


def read_cgroup_limits(args):
    """(cpu_limit, mem_headroom_bytes): min quota CPUs and min memory headroom over the cgroup chain.

    Either is None when no level sets a limit. Raises ValueError on a present-but-malformed file.
    """
    cpus = headroom = None
    for level in cgroup_levels(args):
        try:
            quota = read_text(os.path.join(level, "cpu.max")).split()
        except OSError:
            quota = None
        if quota is not None:
            if len(quota) != 2:
                raise ValueError("cpu.max")
            if quota[0] != "max":
                q, period = int(quota[0]), int(quota[1])
                if q <= 0 or period <= 0:
                    raise ValueError("cpu.max")
                limit = -(-q // period)
                cpus = limit if cpus is None else min(cpus, limit)
        try:
            cap = read_text(os.path.join(level, "memory.max")).strip()
        except OSError:
            cap = None
        if cap is not None and cap != "max":
            cap = int(cap)
            current = int(read_text(os.path.join(level, "memory.current")).strip())
            if cap < 0 or current < 0:
                raise ValueError("memory.max")
            room = max(cap - current, 0)
            headroom = room if headroom is None else min(headroom, room)
    return cpus, headroom


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
    out = {"nproc_host": args.nproc or cpu_count()}
    lane_cpus = lane_mem = None
    try:
        lane_cpus, lane_mem = read_cgroup_limits(args)
    except (OSError, ValueError) as exc:
        reasons.append("unknown: cgroup limits unreadable (%s)" % type(exc).__name__)
    out["nproc_lane"] = lane_cpus
    out["nproc"] = min(out["nproc_host"], lane_cpus) if lane_cpus else out["nproc_host"]

    try:
        fields = read_text(os.path.join(args.proc_root, "loadavg")).split()
        loads = [float(f) for f in fields[:3]]
        if len(loads) != 3 or not all(math.isfinite(v) and v >= 0 for v in loads):
            raise ValueError("non-finite or negative load")
        out["load1"], out["load5"], out["load15"] = loads
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

    out["mem_lane_headroom_gib"] = None if lane_mem is None else round(lane_mem / GIB, 2)
    effective = [b for b in (mem_bytes, lane_mem) if b is not None]
    out["mem_effective_gib"] = round(min(effective) / GIB, 2) if effective else None
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
        idle = kinds.pop("msbuild-node-idle", 0)
        out["heavy"] = {"count": sum(kinds.values()), "kinds": kinds, "idle_msbuild_nodes": idle}
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
            reasons.append("load5 %.2f >= %.2f (%.2f x %d cores%s)" % (
                out["load5"], limit, args.max_load_per_core, out["nproc"], ", cgroup-limited" if lane_cpus and lane_cpus < out["nproc_host"] else ""))
    if mem_bytes is not None and mem_bytes < args.min_mem_gib * GIB:
        reasons.append("MemAvailable %.2f GiB < %.2f GiB" % (out["mem_available_gib"], args.min_mem_gib))
    if lane_mem is not None and lane_mem < args.min_mem_gib * GIB:
        reasons.append("cgroup memory headroom %.2f GiB < %.2f GiB" % (out["mem_lane_headroom_gib"], args.min_mem_gib))
    if disk_bytes is not None and disk_bytes < args.min_disk_gb * GB:
        reasons.append("free disk %.2f GB < %.2f GB at %s" % (out["disk_free_gb"], args.min_disk_gb, args.disk_path))
    if args.max_heavy is not None and out["heavy"] is not None and out["heavy"]["count"] > args.max_heavy:
        reasons.append("heavy processes %d > %d" % (out["heavy"]["count"], args.max_heavy))

    out["verdict"] = "hold" if reasons else "admit"
    out["reasons"] = reasons
    return out


def finite_number(minimum, inclusive):
    def parse(text):
        value = float(text)
        if not math.isfinite(value) or value < minimum or (value == minimum and not inclusive):
            raise argparse.ArgumentTypeError("must be a finite number %s %s, got %r" % (">=" if inclusive else ">", minimum, text))
        return value
    return parse


def cpu_override(text):
    value = int(text)
    if value < 1:
        raise argparse.ArgumentTypeError("must be an integer >= 1, got %r" % text)
    return value


def cpu_override_zero(text):
    value = int(text)
    if value < 0:
        raise argparse.ArgumentTypeError("must be an integer >= 0, got %r" % text)
    return value


def main(argv=None):
    parser = argparse.ArgumentParser(description="Read-only fdev capacity probe: prints admit or hold as one JSON line.")
    parser.add_argument("--max-load-per-core", type=finite_number(0.0, False), default=0.85, help="hold when 5-minute load >= this x nproc (default 0.85)")
    parser.add_argument("--min-mem-gib", type=finite_number(0.0, True), default=6.0, help="hold when MemAvailable is below this many GiB (default 6)")
    parser.add_argument("--min-disk-gb", type=finite_number(0.0, True), default=20.0, help="hold when free disk is below this many decimal GB (default 20)")
    parser.add_argument("--max-heavy", type=cpu_override_zero, default=None, help="optional: hold when more heavy build processes than this (default: report only)")
    parser.add_argument("--disk-path", default=os.path.expanduser("~"), help="path whose filesystem free space is checked (default $HOME)")
    parser.add_argument("--proc-root", default="/proc", help="procfs root; tests point it at a fixture tree")
    parser.add_argument("--cgroup-root", default="/sys/fs/cgroup", help="cgroup v2 mount; tests point it at a fixture tree")
    parser.add_argument("--nproc", type=cpu_override, default=None, help="override the host CPU count, >= 1 (default: what nproc prints)")
    args = parser.parse_args(argv)
    result = probe(args)
    print(json.dumps(result, sort_keys=True, separators=(",", ":"), allow_nan=False))
    return 0 if result["verdict"] == "admit" else 3


if __name__ == "__main__":
    sys.exit(main())
