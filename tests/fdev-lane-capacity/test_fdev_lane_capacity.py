"""Fixture tests for scripts/fdev-lane-capacity.py (.github#5172). Offline; stdlib only.

Each case builds a fake procfs (loadavg, meminfo, <pid>/cmdline) in a temp dir, runs the probe as a
subprocess with --proc-root/--nproc, and checks the verdict, the exit code and the reasons. Every
threshold has a control that flips admit to hold, so a probe that always admitted would fail here.
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
PROBE = os.path.join(HERE, "..", "..", "scripts", "fdev-lane-capacity.py")


class Proc:
    def __init__(self, root, load="1.00 1.00 1.00", mem_kib=32 * 1024 * 1024):
        self.root = root
        for name in ("loadavg", "meminfo"):  # a None input means the file is absent, not stale
            if os.path.exists(os.path.join(root, name)):
                os.remove(os.path.join(root, name))
        if load is not None:
            self.write("loadavg", load + " 1/100 4242\n")
        if mem_kib is not None:
            self.write("meminfo", "MemTotal:       65536000 kB\nMemFree:         1000 kB\nMemAvailable:   %d kB\n" % mem_kib)

    def write(self, name, text):
        path = os.path.join(self.root, name)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(text)

    def cgroup(self, path="/lane.slice/job.scope", **files):
        """Point <proc>/self/cgroup at `path` and write cgroup v2 files under <proc>/cg (the --cgroup-root)."""
        self.write("self/cgroup", "0::%s\n" % path)
        for rel, text in files.items():  # keys are "<level>/<file>" with "/" spelled "__", e.g. "lane.slice__cpu.max"
            self.write("cg/" + rel.replace("__", "/"), text)

    def process(self, pid, *argv):
        self.write("%d/cmdline" % pid, "\0".join(argv) + "\0")


def run(proc_root, *flags, nproc="12", disk="0"):
    args = [sys.executable, "-I", PROBE, "--proc-root", proc_root, "--cgroup-root", os.path.join(proc_root, "cg"),
            "--nproc", nproc, "--disk-path", proc_root, "--min-disk-gb", disk, *flags]
    done = subprocess.run(args, capture_output=True, text=True, check=False)
    lines = done.stdout.splitlines()
    assert len(lines) == 1, "expected exactly one JSON line, got %r (stderr %r)" % (done.stdout, done.stderr)
    return done.returncode, json.loads(lines[0])


class CapacityProbe(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = self._tmp.name

    def test_idle_host_admits_with_exit_0(self):
        Proc(self.root)
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"], out["reasons"]), (0, "admit", []))
        self.assertEqual((out["nproc"], out["load1"], out["load5"], out["load15"]), (12, 1.0, 1.0, 1.0))
        self.assertEqual(out["mem_available_gib"], 32.0)
        self.assertEqual(out["heavy"], {"count": 0, "kinds": {}, "idle_msbuild_nodes": 0})
        self.assertEqual((out["nproc_host"], out["nproc_lane"], out["mem_lane_headroom_gib"], out["mem_effective_gib"]), (12, None, None, 32.0))

    def test_load_ceiling_is_five_minute_load_below_085_times_nproc(self):
        # 0.85 x 12 = 10.2: 10.19 admits, 10.20 holds. The 1-minute figure never decides.
        Proc(self.root, load="30.00 10.19 30.00")
        self.assertEqual(run(self.root)[0], 0)
        Proc(self.root, load="0.10 10.20 0.10")
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertTrue(any(r.startswith("load5 10.20 >= 10.20") for r in out["reasons"]), out["reasons"])
        self.assertEqual(out["thresholds"]["load5_below"], 10.2)

    def test_load_scales_with_nproc_and_flag(self):
        Proc(self.root, load="0.1 5.00 0.1")
        self.assertEqual(run(self.root, nproc="12")[0], 0)
        self.assertEqual(run(self.root, nproc="4")[0], 3)  # 0.85 x 4 = 3.4
        self.assertEqual(run(self.root, "--max-load-per-core", "0.4", nproc="12")[0], 3)

    def test_memory_floor_is_6_gib(self):
        Proc(self.root, mem_kib=6 * 1024 * 1024)
        self.assertEqual(run(self.root)[0], 0)
        Proc(self.root, mem_kib=6 * 1024 * 1024 - 1)
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertTrue(any(r.startswith("MemAvailable") for r in out["reasons"]), out["reasons"])
        Proc(self.root)
        self.assertEqual(run(self.root, "--min-mem-gib", "64")[0], 3)

    def test_disk_floor_flag_flips_the_verdict(self):
        Proc(self.root)
        self.assertEqual(run(self.root, disk="0")[0], 0)
        code, out = run(self.root, disk="1000000000")
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertTrue(any(r.startswith("free disk") for r in out["reasons"]), out["reasons"])
        self.assertIsInstance(out["disk_free_gb"], float)

    def test_default_thresholds_are_stated(self):
        Proc(self.root)
        done = subprocess.run([sys.executable, "-I", PROBE, "--proc-root", self.root, "--cgroup-root", self.root + "/cg", "--nproc", "12", "--disk-path", self.root],
                              capture_output=True, text=True, check=False)
        t = json.loads(done.stdout)["thresholds"]
        self.assertEqual((t["load5_below"], t["mem_available_gib_min"], t["disk_free_gb_min"], t["heavy_max"]), (10.2, 6.0, 20.0, None))

    def test_heavy_processes_are_counted_by_kind_and_graded_only_on_request(self):
        proc = Proc(self.root)
        proc.process(10, "dotnet", "build", "x.sln")
        proc.process(11, "/usr/bin/dotnet", "test", "--no-build")
        proc.process(12, "dotnet", "/usr/share/dotnet/sdk/MSBuild.dll", "/nodemode:1", "/nodeReuse:false")
        proc.process(13, "/home/u/.cargo/bin/cargo", "build")
        proc.process(14, "podman", "build", "-t", "img", ".")
        proc.process(15, "podman", "ps")           # not a build
        proc.process(16, "dotnet", "--info")       # not a build
        proc.process(17, "vim", "build.md")        # name substring is not a match
        code, out = run(self.root)
        self.assertEqual(code, 0)
        self.assertEqual(out["heavy"]["count"], 5)
        self.assertEqual(out["heavy"]["kinds"], {"dotnet-build": 1, "dotnet-test": 1, "msbuild-node": 1, "cargo": 1, "podman-build": 1})
        self.assertEqual(run(self.root, "--max-heavy", "5")[0], 0)
        code, out = run(self.root, "--max-heavy", "4")
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertIn("heavy processes 5 > 4", out["reasons"])

    def test_all_reasons_are_reported_together(self):
        Proc(self.root, load="1 20.00 1", mem_kib=1024)
        code, out = run(self.root, disk="1000000000")
        self.assertEqual(code, 3)
        self.assertEqual(len(out["reasons"]), 3, out["reasons"])

    def test_unreadable_input_holds_instead_of_admitting(self):
        Proc(self.root, load=None)
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertTrue(any(r.startswith("unknown: loadavg") for r in out["reasons"]), out["reasons"])
        Proc(self.root, mem_kib=None)  # restores loadavg, still no meminfo
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertTrue(any(r.startswith("unknown: MemAvailable") for r in out["reasons"]), out["reasons"])

    def test_malformed_meminfo_holds(self):
        proc = Proc(self.root)
        proc.write("meminfo", "MemTotal: 1 kB\n")
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"]), (3, "hold"))

    def test_probe_is_read_only(self):
        Proc(self.root).process(10, "dotnet", "build")
        before = sorted(os.path.join(d, f) for d, _, fs in os.walk(self.root) for f in fs)
        run(self.root)
        after = sorted(os.path.join(d, f) for d, _, fs in os.walk(self.root) for f in fs)
        self.assertEqual(before, after)

    def test_msbuild_reuse_nodes_are_idle_not_active(self):
        proc = Proc(self.root)
        for pid in (20, 21, 22):
            proc.process(pid, "dotnet", "/usr/share/dotnet/sdk/MSBuild.dll", "/nodemode:1", "/nodeReuse:true", "/low:false")
        proc.process(23, "dotnet", "/usr/share/dotnet/sdk/MSBuild.dll", "-nodemode:1", "-nodeReuse:TRUE")
        code, out = run(self.root, "--max-heavy", "0")
        self.assertEqual((code, out["verdict"]), (0, "admit"), out["reasons"])
        self.assertEqual(out["heavy"], {"count": 0, "kinds": {}, "idle_msbuild_nodes": 4})
        proc.process(24, "dotnet", "/usr/share/dotnet/sdk/MSBuild.dll", "/nodemode:1", "/nodeReuse:false")  # an active node
        code, out = run(self.root, "--max-heavy", "0")
        self.assertEqual((code, out["heavy"]["count"], out["heavy"]["idle_msbuild_nodes"]), (3, 1, 4))

    def test_nan_inf_and_negative_load_hold_as_unknown_loadavg(self):
        for bad in ("nan 1.00 1.00", "1.00 nan 1.00", "1.00 1.00 nan", "inf inf inf", "0.1 -inf 0.1", "1.00 -1.00 1.00", "-0.5 1.00 1.00", "1.00 1.00"):
            with self.subTest(loadavg=bad):
                Proc(self.root, load=bad)
                code, out = run(self.root)
                self.assertEqual((code, out["verdict"]), (3, "hold"))
                self.assertTrue(any(r.startswith("unknown: loadavg") for r in out["reasons"]), out["reasons"])
                self.assertIsNone(out["load5"])

    def test_output_is_strict_json_with_no_nan_or_infinity(self):
        Proc(self.root, load="nan inf -inf")
        done = subprocess.run([sys.executable, "-I", PROBE, "--proc-root", self.root, "--cgroup-root", self.root + "/cg", "--nproc", "12",
                               "--disk-path", self.root, "--min-disk-gb", "0"], capture_output=True, text=True, check=False)
        def refuse(token):
            raise ValueError("non-strict JSON token " + token)
        json.loads(done.stdout, parse_constant=refuse)
        self.assertEqual(done.returncode, 3)

    def test_the_serializer_itself_refuses_nan(self):
        # Defence in depth: even if a non-finite value ever reached the result, printing must fail, not emit `NaN`.
        import importlib.util
        spec = importlib.util.spec_from_file_location("fdev_lane_capacity", PROBE)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.probe = lambda args: {"verdict": "admit", "reasons": [], "load5": float("nan")}
        with self.assertRaises(ValueError):
            module.main(["--proc-root", self.root])

    def test_nonfinite_threshold_flags_are_usage_errors(self):
        Proc(self.root)
        for flag, value in (("--min-mem-gib", "nan"), ("--min-disk-gb", "inf"), ("--max-load-per-core", "nan"), ("--max-load-per-core", "0"),
                            ("--min-mem-gib", "-1"), ("--max-heavy", "-1")):
            with self.subTest(flag=flag, value=value):
                done = subprocess.run([sys.executable, "-I", PROBE, "--proc-root", self.root, flag, value], capture_output=True, text=True, check=False)
                self.assertEqual(done.returncode, 2, done.stdout)
                self.assertEqual(done.stdout, "")

    def test_nproc_below_one_or_not_an_integer_is_a_usage_error(self):
        Proc(self.root)
        for value in ("0", "-3", "x", "1.5"):
            with self.subTest(nproc=value):
                done = subprocess.run([sys.executable, "-I", PROBE, "--proc-root", self.root, "--nproc", value], capture_output=True, text=True, check=False)
                self.assertEqual(done.returncode, 2, done.stdout)
                self.assertEqual(done.stdout, "")
        Proc(self.root, load="0.1 0.50 0.1")
        self.assertEqual(run(self.root, nproc="1")[0], 0)  # 0.50 < 0.85 x 1

    # --- cgroup v2 lane limits: quota present, `max`, absent -------------------------------------------------
    def test_cpu_quota_lowers_nproc_and_the_load_ceiling(self):
        # 4 CPUs of quota on a 12-core host: ceiling 0.85 x 4 = 3.4, so load5 5.00 holds although 5.00 < 10.2.
        proc = Proc(self.root, load="0.1 5.00 0.1")
        proc.cgroup(**{"lane.slice__job.scope__cpu.max": "400000 100000\n"})
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertEqual((out["nproc_host"], out["nproc_lane"], out["nproc"], out["thresholds"]["load5_below"]), (12, 4, 4, 3.4))
        self.assertTrue(any("cgroup-limited" in r for r in out["reasons"]), out["reasons"])
        Proc(self.root, load="0.1 3.00 0.1").cgroup(**{"lane.slice__job.scope__cpu.max": "400000 100000\n"})
        self.assertEqual(run(self.root)[0], 0)

    def test_cpu_quota_rounds_up_and_never_raises_above_the_affinity_count(self):
        Proc(self.root).cgroup(**{"lane.slice__job.scope__cpu.max": "150000 100000\n"})
        self.assertEqual(run(self.root)[1]["nproc"], 2)  # ceil(1.5)
        Proc(self.root).cgroup(**{"lane.slice__job.scope__cpu.max": "9600000 100000\n"})  # 96 CPUs of quota
        out = run(self.root, nproc="12")[1]
        self.assertEqual((out["nproc_lane"], out["nproc"]), (96, 12))

    def test_tightest_ancestor_wins(self):
        proc = Proc(self.root, load="0.1 2.00 0.1")
        proc.cgroup(**{"lane.slice__job.scope__cpu.max": "max 100000\n", "lane.slice__cpu.max": "200000 100000\n",
                       "lane.slice__job.scope__memory.max": "max\n", "lane.slice__memory.max": str(8 * 1024 ** 3) + "\n",
                       "lane.slice__memory.current": str(1 * 1024 ** 3) + "\n"})
        out = run(self.root)[1]
        self.assertEqual((out["nproc_lane"], out["nproc"], out["mem_lane_headroom_gib"]), (2, 2, 7.0))

    def test_cpu_max_max_and_absent_files_mean_no_lane_limit(self):
        proc = Proc(self.root, load="0.1 9.00 0.1")
        proc.cgroup(**{"lane.slice__job.scope__cpu.max": "max 100000\n", "lane.slice__job.scope__memory.max": "max\n"})
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"], out["nproc"], out["nproc_lane"], out["mem_lane_headroom_gib"]), (0, "admit", 12, None, None))
        Proc(self.root, load="0.1 9.00 0.1").cgroup()  # cgroup path named, no limit files at all
        out = run(self.root)[1]
        self.assertEqual((out["verdict"], out["nproc_lane"], out["mem_lane_headroom_gib"]), ("admit", None, None))
        # no <proc>/self/cgroup at all (cgroup v1 or a bare container): host-wide, not a hold
        os.remove(os.path.join(self.root, "self", "cgroup"))
        out = run(self.root)[1]
        self.assertEqual((out["verdict"], out["nproc_lane"]), ("admit", None))

    def test_cgroup_memory_headroom_below_the_floor_holds_even_when_the_host_has_plenty(self):
        gib = 1024 ** 3
        proc = Proc(self.root)  # host MemAvailable 32 GiB
        proc.cgroup(**{"lane.slice__job.scope__memory.max": str(10 * gib) + "\n", "lane.slice__job.scope__memory.current": str(4 * gib) + "\n"})
        code, out = run(self.root)  # headroom exactly 6 GiB: admits
        self.assertEqual((code, out["mem_available_gib"], out["mem_lane_headroom_gib"], out["mem_effective_gib"]), (0, 32.0, 6.0, 6.0))
        proc.cgroup(**{"lane.slice__job.scope__memory.current": str(4 * gib + 1) + "\n"})
        code, out = run(self.root)
        self.assertEqual((code, out["verdict"]), (3, "hold"))
        self.assertTrue(any(r.startswith("cgroup memory headroom") for r in out["reasons"]), out["reasons"])
        proc.cgroup(**{"lane.slice__job.scope__memory.current": str(20 * gib) + "\n"})  # over its own cap: headroom 0, never negative
        self.assertEqual(run(self.root)[1]["mem_lane_headroom_gib"], 0.0)

    def test_malformed_cgroup_limit_holds_as_unknown(self):
        for name, text in (("cpu.max", "garbage\n"), ("cpu.max", "0 100000\n"), ("cpu.max", "100000\n"), ("memory.max", "lots\n")):
            with self.subTest(file=name, text=text):
                Proc(self.root).cgroup(**{"lane.slice__job.scope__" + name: text})
                code, out = run(self.root)
                self.assertEqual((code, out["verdict"]), (3, "hold"))
                self.assertTrue(any(r.startswith("unknown: cgroup limits") for r in out["reasons"]), out["reasons"])
        # a finite memory.max with no memory.current cannot be graded: hold
        Proc(self.root).cgroup(**{"lane.slice__job.scope__memory.max": "1073741824\n"})
        self.assertEqual(run(self.root)[0], 3)


class LiveHost(unittest.TestCase):
    def test_runs_on_the_real_proc_and_prints_one_line(self):
        done = subprocess.run([sys.executable, "-I", PROBE, "--min-disk-gb", "0"], capture_output=True, text=True, check=False)
        self.assertIn(done.returncode, (0, 3))
        out = json.loads(done.stdout)
        self.assertEqual(out["verdict"], "admit" if done.returncode == 0 else "hold")
        self.assertGreaterEqual(out["nproc"], 1)


if __name__ == "__main__":
    unittest.main()
