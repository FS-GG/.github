"""Fixture tests for scripts/fdev-tmp-cleanup.py (.github#5240). Offline; stdlib only.

Each case builds a fake temp root and a fake procfs in a temp dir and runs the script as a subprocess. The
script is never pointed at the real /tmp here. Every "kept" case has a control that is removed under the same
conditions without the one protecting fact, so a script that deleted nothing, or everything, would fail.
"""
import contextlib
import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "..", "scripts", "fdev-tmp-cleanup.py")
HOUR = 3600
HEX = "0123456789abcdef0123456789abcdef"


def put(path, text):
    with open(path, "w") as handle:
        handle.write(text)


def load_module():
    spec = importlib.util.spec_from_file_location("fdev_tmp_cleanup_under_test", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class Fixture(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.base = os.path.realpath(tmp.name)
        self.root = os.path.join(self.base, "tmp")      # the swept root
        self.proc = os.path.join(self.base, "proc")     # the fake procfs
        self.outside = os.path.join(self.base, "outside")  # something the script must never touch
        for d in (self.root, self.proc, self.outside):
            os.makedirs(d)
        self.now = time.time()

    # --- builders ------------------------------------------------------------------------------------------
    def make(self, name, age_hours=8.0, files=("a/b/c.txt", "d.txt"), mode_dirs=None):
        """A directory tree under the root, every node last modified age_hours ago."""
        top = os.path.join(self.root, name)
        os.makedirs(top)
        for rel in files:
            path = os.path.join(top, rel)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w") as handle:
                handle.write("x" * 100)
        self.age(top, age_hours)
        return top

    def age(self, top, age_hours):
        stamp = self.now - age_hours * HOUR
        for dirpath, dirnames, filenames in os.walk(top, topdown=False):
            for name in filenames + dirnames:
                path = os.path.join(dirpath, name)
                os.utime(path, (stamp, stamp), follow_symlinks=False)
            os.utime(dirpath, (stamp, stamp))

    def process(self, pid, cmdline=(), environ=None, cwd=None, fds=(), maps=""):
        base = os.path.join(self.proc, str(pid))
        os.makedirs(os.path.join(base, "fd"), exist_ok=True)
        with open(os.path.join(base, "cmdline"), "wb") as handle:
            handle.write(b"\0".join(a.encode() for a in cmdline) + b"\0" if cmdline else b"")
        with open(os.path.join(base, "environ"), "wb") as handle:
            handle.write(b"\0".join(("%s=%s" % kv).encode() for kv in (environ or {}).items()))
        with open(os.path.join(base, "maps"), "w") as handle:
            handle.write(maps)
        if cwd:
            os.symlink(cwd, os.path.join(base, "cwd"))
        for number, target in enumerate(fds, 3):
            os.symlink(target, os.path.join(base, "fd", str(number)))

    def write_status(self, pid, uid=None, comm="sleep"):
        uid = os.geteuid() if uid is None else uid
        with open(os.path.join(self.proc, str(pid), "status"), "w") as handle:
            handle.write("Name:\t%s\nUid:\t%d\t%d\t%d\t%d\n" % (comm, uid, uid, uid, uid))
        with open(os.path.join(self.proc, str(pid), "comm"), "w") as handle:
            handle.write(comm + "\n")

    def unix_socket(self, path):
        os.makedirs(os.path.join(self.proc, "net"), exist_ok=True)
        with open(os.path.join(self.proc, "net", "unix"), "a") as handle:
            handle.write("Num RefCount Protocol Flags Type St Inode Path\n0000000000000000: 00000002 00000000 00010000 0001 01 12345 %s\n" % path)

    def run_script(self, *flags, apply=False, root=None, proc=None):
        argv = [sys.executable, "-I", SCRIPT, "--root", root or self.root, "--proc-root", proc or self.proc, "--now", repr(self.now), *flags]
        if apply:
            argv.append("--apply")
        done = subprocess.run(argv, capture_output=True, text=True, check=False)
        lines = done.stdout.splitlines()
        self.assertEqual(len(lines), 1, (done.stdout, done.stderr))
        return done.returncode, json.loads(lines[0]), done.stderr

    def exists(self, name):
        return os.path.lexists(os.path.join(self.root, name))

    def snapshot(self):
        return sorted(os.path.join(d, n) for d, ds, fs in os.walk(self.base) for n in ds + fs)


class DeadRunCleanup(Fixture):
    def test_dry_run_is_the_default_and_changes_nothing(self):
        self.make("fsgg-sdd-tests-4242-" + HEX)
        self.make("playwright_chromiumdev_profile-aB3dE9")
        before = self.snapshot()
        code, out, _ = self.run_script()
        self.assertEqual((code, out["mode"], out["would_remove"], out["removed"]), (0, "dry-run", 2, 0))
        self.assertEqual(before, self.snapshot())
        self.assertGreater(out["would_free_bytes"], 0)
        self.assertEqual({e["owner"] for e in out["removable"]}, {"pid 4242 (dead)", "no live reference"})
        # explicit --dry-run is the same
        self.assertEqual(self.run_script("--dry-run")[1]["removed"], 0)
        self.assertEqual(before, self.snapshot())

    def test_apply_removes_a_dead_pid_run_and_only_it(self):
        self.make("fsgg-sdd-tests-4242-" + HEX)
        self.process(7, ["sleep", "1"])
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((code, out["mode"], out["removed"], out["failed"]), (0, "apply", 1, 0))
        self.assertFalse(self.exists("fsgg-sdd-tests-4242-" + HEX))
        self.assertGreater(out["freed_bytes"], 0)

    def test_a_live_pid_keeps_its_run_and_a_dead_pid_with_the_same_shape_is_removed(self):
        self.make("fsgg-sdd-tests-4242-" + HEX)
        self.make("fsgg-sdd-tests-4243-" + HEX)
        self.process(4243, ["dotnet", "test"])
        code, out, _ = self.run_script(apply=True)
        self.assertEqual(out["kept"], {"pid-alive": 1})
        self.assertFalse(self.exists("fsgg-sdd-tests-4242-" + HEX))
        self.assertTrue(self.exists("fsgg-sdd-tests-4243-" + HEX))
        self.assertTrue(os.path.exists(os.path.join(self.root, "fsgg-sdd-tests-4243-" + HEX, "a/b/c.txt")))

    def test_young_runs_are_kept_and_the_age_threshold_is_exclusive_and_configurable(self):
        self.make("fsgg-sdd-tests-1-" + HEX, age_hours=0.5)
        self.make("fsgg-sdd-tests-2-" + HEX, age_hours=0.99)
        self.make("fsgg-sdd-tests-3-" + HEX, age_hours=1.01)
        code, out, _ = self.run_script(apply=True)
        self.assertEqual(out["kept"], {"young": 2})
        self.assertEqual([self.exists("fsgg-sdd-tests-%d-%s" % (n, HEX)) for n in (1, 2, 3)], [True, True, False])
        code, out, _ = self.run_script("--min-age-hours", "0.25", apply=True)
        self.assertFalse(self.exists("fsgg-sdd-tests-1-" + HEX))

    def test_a_recent_write_deep_in_an_old_tree_keeps_the_whole_tree(self):
        top = self.make("fsgg-sdd-" + HEX)
        fresh = os.path.join(top, "a", "b", "c.txt")
        os.utime(fresh, (self.now - 60, self.now - 60))  # touched a minute ago; every directory above is old
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["kept"], out["removed"]), ({"young": 1}, 0))
        self.assertTrue(os.path.exists(fresh))
        os.utime(fresh, (self.now - 8 * HOUR, self.now - 8 * HOUR))
        self.assertEqual(self.run_script(apply=True)[1]["removed"], 1)

    def test_pidless_run_is_kept_while_any_live_process_references_it_by_any_route(self):
        routes = {
            "cmdline": dict(cmdline=["node", "{p}/driver/run-smoke.mjs"]),
            "cmdline-flag": dict(cmdline=["chrome", "--user-data-dir={p}"]),
            "environ": dict(environ={"TMPDIR": "{p}"}),
            "cwd": dict(cwd="{p}/sub"),
            "fd": dict(fds=["{p}/log.txt"]),
            "fd-deleted": dict(fds=["{p}/log.txt (deleted)"]),
            "maps": dict(maps="7f00-7f01 r--s 00000000 00:2e 99 {p}/mapped.db\n"),
        }
        names = {}
        for index, (route, _) in enumerate(sorted(routes.items())):
            name = "playwright_chromiumdev_profile-r%05d" % index
            self.make(name)
            names[route] = name
        control = "playwright_chromiumdev_profile-ctrl00"
        self.make(control)
        for pid, (route, spec) in enumerate(sorted(routes.items()), 100):
            path = os.path.join(self.root, names[route])
            filled = {k: ([v.format(p=path) for v in val] if isinstance(val, (list, tuple)) else
                          {kk: vv.format(p=path) for kk, vv in val.items()} if isinstance(val, dict) else val.format(p=path))
                      for k, val in spec.items()}
            self.process(pid, **filled)
        self.unix_socket(os.path.join(self.root, "org.chromium.Chromium.sock01", "SingletonSocket"))
        self.make("org.chromium.Chromium.sock01")
        code, out, _ = self.run_script(apply=True)
        self.assertEqual(out["removed"], 1, out)  # only the control
        self.assertEqual(out["kept"], {"referenced-by-live-process": len(routes) + 1})
        self.assertFalse(self.exists(control))
        for route, name in names.items():
            self.assertTrue(self.exists(name), route)
        self.assertTrue(self.exists("org.chromium.Chromium.sock01"))

    def test_a_reference_to_a_different_root_or_a_name_prefix_does_not_protect(self):
        self.make("fsgg-sdd-" + HEX)
        other = os.path.join(self.base, "elsewhere", os.path.basename(self.root), "fsgg-sdd-" + HEX)
        self.process(9, ["cat", other, self.root + "-2/fsgg-sdd-" + HEX, self.root + "/fsgg-sdd-" + HEX + "0"])
        self.assertEqual(self.run_script(apply=True)[1]["removed"], 1)

    def test_only_known_harness_names_are_matched(self):
        keep = ["tmp.abcdefghij", "tmp.abcdefghij2", "fsgg-gate-engine", "fsgg-gate-nightly", "fsgg-sdd-tests-xx-" + HEX, "fsgg-sdd-" + HEX[:-1],
                "fsgg-sdd-" + HEX.upper(), "playwright_chromiumdev_profile-", "playwright-artifacts-abcdef", "org.chromium.Chromium", "work-827",
                "playwright_chromiumdev_profile-abcdef-more", "fsgg-tok-123", "some-user-data", "fsgg-sdd-" + HEX + ".bak"]
        for name in keep:
            self.make(name)
        self.make("fsgg-sdd-" + "a" * 32)  # the one real match, as a control
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["entries_matched"], out["removed"]), (1, 1))
        self.assertFalse(self.exists("fsgg-sdd-" + "a" * 32))
        for name in keep:
            self.assertTrue(self.exists(name), name)

    def test_a_browser_smoke_tree_needs_its_fingerprint(self):
        self.make("tmp.ABCDEFGHIJ", files=("driver/run-smoke.mjs", "js/index.html"))
        self.make("tmp.0123456789", files=("driver/package.json",))          # no run-smoke.mjs
        self.make("tmp.KLMNOPQRST", files=("js/index.html",))                # unrelated mktemp
        os.makedirs(os.path.join(self.outside, "driver"))
        put(os.path.join(self.outside, "driver", "run-smoke.mjs"), "")
        os.makedirs(os.path.join(self.root, "tmp.zzzzzzzzzz"))
        os.symlink(os.path.join(self.outside, "driver"), os.path.join(self.root, "tmp.zzzzzzzzzz", "driver"))  # a symlinked driver is no fingerprint
        self.age(os.path.join(self.root, "tmp.zzzzzzzzzz"), 3)
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["entries_matched"], out["removed"], out["by_pattern"]["browser-smoke"]), (1, 1, {"matched": 1, "removable": 1}))
        self.assertFalse(self.exists("tmp.ABCDEFGHIJ"))
        for name in ("tmp.0123456789", "tmp.KLMNOPQRST", "tmp.zzzzzzzzzz"):
            self.assertTrue(self.exists(name), name)
        self.assertTrue(os.path.exists(os.path.join(self.outside, "driver", "run-smoke.mjs")))

    def test_a_live_browser_smoke_is_kept_through_its_command_line(self):
        self.make("tmp.ABCDEFGHIJ", files=("driver/run-smoke.mjs",))
        self.process(55, ["node", self.root + "/tmp.ABCDEFGHIJ/driver/run-smoke.mjs", "--headless"])
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["removed"], out["kept"]), (0, {"referenced-by-live-process": 1}))

    def test_a_symlink_entry_is_never_followed_or_removed(self):
        target = os.path.join(self.outside, "precious")
        os.makedirs(target)
        put(os.path.join(target, "keep.txt"), "keep")
        os.symlink(target, os.path.join(self.root, "fsgg-sdd-" + HEX))
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["removed"], out["kept"]), (0, {"symlink": 1}))
        self.assertTrue(os.path.islink(os.path.join(self.root, "fsgg-sdd-" + HEX)))
        self.assertTrue(os.path.exists(os.path.join(target, "keep.txt")))

    def test_symlinks_inside_a_removed_tree_are_unlinked_not_followed(self):
        precious = os.path.join(self.outside, "precious")
        os.makedirs(precious)
        put(os.path.join(precious, "keep.txt"), "keep")
        put(os.path.join(self.outside, "file.txt"), "keep")
        top = self.make("fsgg-sdd-tests-4242-" + HEX)
        os.symlink(precious, os.path.join(top, "to-dir"))
        os.symlink(os.path.join(self.outside, "file.txt"), os.path.join(top, "a", "to-file"))
        os.symlink(self.outside, os.path.join(top, "a", "b", "to-outside"))
        os.symlink("/nonexistent/dangling", os.path.join(top, "dangling"))
        self.age(top, 3.0)  # the links and the directories that now hold them are old too
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["removed"], out["failed"]), (1, 0), out)
        self.assertFalse(self.exists("fsgg-sdd-tests-4242-" + HEX))
        self.assertTrue(os.path.exists(os.path.join(precious, "keep.txt")))
        self.assertTrue(os.path.exists(os.path.join(self.outside, "file.txt")))

    def test_read_only_trees_are_removed(self):
        top = self.make("fsgg-ai-tests-" + HEX, files=("objects/ab/cd.pack", "objects/ef.pack"))
        for dirpath, dirnames, filenames in os.walk(top):
            for name in filenames:
                os.chmod(os.path.join(dirpath, name), 0o444)
        for dirpath in (os.path.join(top, "objects", "ab"), os.path.join(top, "objects")):
            os.chmod(dirpath, 0o555)
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["removed"], out["failed"]), (1, 0), out)
        self.assertFalse(self.exists("fsgg-ai-tests-" + HEX))

    def test_files_and_directories_that_are_not_directories_are_kept(self):
        put(os.path.join(self.root, "fsgg-sdd-" + HEX), "a file with a harness name")
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["removed"], out["kept"]), (0, {"not-a-directory": 1}))

    def test_an_entry_owned_by_someone_else_is_kept(self):
        module = load_module()
        self.make("fsgg-sdd-tests-4242-" + HEX)
        out = io.StringIO()
        argv = ["--root", self.root, "--proc-root", self.proc, "--now", repr(self.now), "--apply"]
        with mock.patch.object(module.os, "geteuid", return_value=os.geteuid() + 1), contextlib.redirect_stdout(out):
            self.assertEqual(module.main(argv), 0)
        report = json.loads(out.getvalue())
        self.assertEqual((report["removed"], report["kept"]), (0, {"not-owned": 1}))
        self.assertTrue(self.exists("fsgg-sdd-tests-4242-" + HEX))

    def test_an_entry_a_process_picks_up_after_the_survey_is_kept(self):
        module = load_module()
        top = self.make("fsgg-resolver-" + HEX)
        real = module.live_references
        calls = []

        def racing(proc_root, roots, own_pid):
            calls.append(1)
            names, stats = real(proc_root, roots, own_pid)
            if len(calls) == 2:
                names[roots[0]].add("fsgg-resolver-" + HEX)  # a process started using it after the first read
            return names, stats

        out = io.StringIO()
        argv = ["--root", self.root, "--proc-root", self.proc, "--now", repr(self.now), "--apply"]
        with mock.patch.object(module, "live_references", racing), contextlib.redirect_stdout(out):
            self.assertEqual(module.main(argv), 0)
        report = json.loads(out.getvalue())
        self.assertEqual((len(calls), report["removed"], report["kept"]), (2, 0, {"referenced-by-live-process": 1}))
        self.assertTrue(os.path.exists(top))

    def test_an_entry_modified_after_the_survey_is_kept(self):
        module = load_module()
        top = self.make("fsgg-resolver-" + HEX)
        real = module.live_references
        calls = []

        def racing(proc_root, roots, own_pid):
            calls.append(1)
            if len(calls) == 2:
                os.utime(top, (self.now, self.now))
            return real(proc_root, roots, own_pid)

        out = io.StringIO()
        argv = ["--root", self.root, "--proc-root", self.proc, "--now", repr(self.now), "--apply"]
        with mock.patch.object(module, "live_references", racing), contextlib.redirect_stdout(out):
            module.main(argv)
        report = json.loads(out.getvalue())
        self.assertEqual((report["removed"], report["kept"]), (0, {"changed-since-survey": 1}))

    def test_several_patterns_in_one_sweep_report_by_pattern(self):
        names = ["fsgg-sdd-tests-1-" + HEX, "fsgg-sdd-" + HEX, "fsgg-ai-tr-" + HEX, "fsgg-cli-tests-cache-sandbox-" + HEX,
                 "playwright_chromiumdev_profile-Zz9Zz9", "org.chromium.Chromium.Ab12Cd"]
        for name in names:
            self.make(name)
        code, out, _ = self.run_script("--list-limit", "2")
        self.assertEqual((out["would_remove"], len(out["removable"]), out["removable_listed_of"]), (6, 2, 6))
        self.assertEqual(set(out["by_pattern"]), {"fsgg-sdd-tests", "fsgg-sdd-run", "fsgg-test-root", "playwright-profile", "chromium-temp"})


    def test_pidless_patterns_need_six_hours_and_pid_patterns_one(self):
        for hours, name in ((5.5, "fsgg-sdd-" + "1" * 32), (6.5, "fsgg-sdd-" + "2" * 32), (1.5, "fsgg-sdd-tests-7-" + HEX), (0.5, "fsgg-sdd-tests-8-" + HEX),
                            (5.5, "playwright_chromiumdev_profile-aaaaaa"), (6.5, "playwright_chromiumdev_profile-bbbbbb")):
            self.make(name, age_hours=hours)
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((out["min_age_hours"], out["min_age_hours_pidless"], out["removed"], out["kept"]), (1.0, 6.0, 3, {"young": 3}))
        self.assertEqual([self.exists(n) for n in ("fsgg-sdd-" + "1" * 32, "fsgg-sdd-" + "2" * 32, "fsgg-sdd-tests-7-" + HEX, "fsgg-sdd-tests-8-" + HEX,
                                                  "playwright_chromiumdev_profile-aaaaaa", "playwright_chromiumdev_profile-bbbbbb")],
                         [True, False, False, True, True, False])
        code, out, _ = self.run_script("--min-age-hours-pidless", "5", apply=True)
        self.assertEqual(out["removed"], 2)

    def test_a_relative_word_resolves_against_the_process_cwd_and_a_symlinked_alias_is_a_reference(self):
        self.make("fsgg-sdd-" + "1" * 32)
        self.make("fsgg-sdd-" + "2" * 32)
        self.make("fsgg-sdd-" + "3" * 32)
        self.make("fsgg-sdd-" + "5" * 32)
        elsewhere = os.path.join(self.base, "elsewhere")
        os.makedirs(elsewhere)
        os.symlink(os.path.join(self.root, "fsgg-sdd-" + "2" * 32), os.path.join(elsewhere, "alias"))
        self.process(10, ["node", "fsgg-sdd-" + "1" * 32 + "/driver/run.mjs"], cwd=self.root)           # relative, cwd is the root
        self.process(11, ["tail", "-f", os.path.join(elsewhere, "alias", "a", "b", "c.txt")])             # symlinked alias
        self.process(12, ["node", "../tmp/fsgg-sdd-" + "3" * 32 + "/x.mjs"], cwd=elsewhere)               # relative with ..
        self.process(13, ["cat", "alias/d.txt"], cwd=elsewhere)                                           # alias, relative
        self.process(14, ["cat", "--file=" + os.path.join(elsewhere, "alias") + "/d.txt"])                # flag value through the alias
        code, out, _ = self.run_script(apply=True)
        self.assertEqual(out["removed"], 1, out)  # only the unreferenced one
        self.assertFalse(self.exists("fsgg-sdd-" + "5" * 32))
        for n in "123":
            self.assertTrue(self.exists("fsgg-sdd-" + n * 32), n)

    def test_a_same_user_process_that_cannot_be_inspected_blocks_apply_even_if_its_cmdline_reads(self):
        if os.geteuid() == 0:
            self.skipTest("root can read everything")
        self.make("fsgg-sdd-tests-4242-" + HEX)
        self.process(30, ["sleep", "1"])
        self.write_status(30)
        environ = os.path.join(self.proc, "30", "environ")
        os.chmod(environ, 0)
        self.addCleanup(os.chmod, environ, 0o600)
        before = self.snapshot()
        code, out, err = self.run_script(apply=True)
        self.assertEqual((code, out["mode"]), (3, "refused"))
        self.assertIn("could not be inspected", err)
        self.assertIn("30", err)
        self.assertEqual(before, self.snapshot())
        # a dry run still reports the removable entry and says apply is blocked
        code, out, _ = self.run_script()
        self.assertEqual((code, out["would_remove"], out["apply_blocked"], out["unreadable"]), (0, 1, True, [{"pid": 30, "comm": "sleep"}]))
        # the fd directory is read the same way
        os.chmod(environ, 0o600)
        fd_dir = os.path.join(self.proc, "30", "fd")
        os.chmod(fd_dir, 0)
        self.addCleanup(os.chmod, fd_dir, 0o700)
        self.assertEqual(self.run_script(apply=True)[0], 3)

    def test_the_allowlist_a_vanished_process_and_other_users_processes_do_not_block(self):
        if os.geteuid() == 0:
            self.skipTest("root can read everything")
        self.make("fsgg-sdd-tests-4242-" + HEX)
        for pid, comm, argv in ((31, "(sd-pam)", ["(sd-pam)"]), (32, "sshd-session", ["sshd-session: dev"]), (33, "systemd", ["/usr/lib/systemd/systemd", "--user"])):
            self.process(pid, argv)
            self.write_status(pid, comm=comm)
            os.chmod(os.path.join(self.proc, str(pid), "environ"), 0)
            self.addCleanup(os.chmod, os.path.join(self.proc, str(pid), "environ"), 0o600)
        self.process(34, ["sleep", "1"])
        self.write_status(34, uid=os.geteuid() + 1)  # another user's process: the scan owes nothing about it
        os.chmod(os.path.join(self.proc, "34", "environ"), 0)
        self.addCleanup(os.chmod, os.path.join(self.proc, "34", "environ"), 0o600)
        self.process(35, ["sleep", "1"])
        self.write_status(35)
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((code, out["removed"], out["unreadable"]), (0, 1, []), out)

    def test_a_systemd_that_is_not_the_user_manager_is_not_allowlisted(self):
        if os.geteuid() == 0:
            self.skipTest("root can read everything")
        self.make("fsgg-sdd-tests-4242-" + HEX)
        self.process(40, ["/usr/lib/systemd/systemd", "--system"])
        self.write_status(40, comm="init-ish")
        os.chmod(os.path.join(self.proc, "40", "environ"), 0)
        self.addCleanup(os.chmod, os.path.join(self.proc, "40", "environ"), 0o600)
        self.assertEqual(self.run_script(apply=True)[0], 3)

    def test_a_tree_deeper_than_the_recursion_limit_is_removed(self):
        top = os.path.join(self.root, "fsgg-sdd-tests-4242-" + HEX)
        os.mkdir(top)
        path = top
        for _ in range(1100):
            path = os.path.join(path, "d")
            os.mkdir(path)
        with open(os.path.join(path, "leaf.txt"), "w") as handle:
            handle.write("x")
        os.symlink(self.outside, os.path.join(path, "out"))
        stamp = self.now - 8 * HOUR
        walk = top
        os.utime(top, (stamp, stamp))
        for _ in range(1100):
            walk = os.path.join(walk, "d")
            os.utime(walk, (stamp, stamp))
        os.utime(os.path.join(path, "leaf.txt"), (stamp, stamp))
        os.utime(os.path.join(path, "out"), (stamp, stamp), follow_symlinks=False)
        self.assertGreater(sys.getrecursionlimit(), 0)
        code, out, _ = self.run_script(apply=True)
        self.assertEqual((code, out["removed"], out["failed"]), (0, 1, 0), out)
        self.assertFalse(self.exists("fsgg-sdd-tests-4242-" + HEX))
        self.assertTrue(os.path.isdir(self.outside))

    def test_it_refuses_to_run_as_root(self):
        module = load_module()
        err = io.StringIO()
        with mock.patch.object(module.os, "geteuid", return_value=0), contextlib.redirect_stderr(err):
            self.assertEqual(module.main(["--root", self.root, "--proc-root", self.proc, "--apply"]), 2)
        self.assertIn("not root", err.getvalue())


class Refusals(Fixture):
    def test_an_unreadable_process_table_refuses_and_removes_nothing_even_with_apply(self):
        self.make("fsgg-sdd-tests-4242-" + HEX)
        before = self.snapshot()
        code, out, err = self.run_script(apply=True, proc=os.path.join(self.base, "no-such-proc"))
        self.assertEqual((code, out["mode"]), (3, "refused"))
        self.assertIn("nothing was removed", err)
        self.assertEqual(before, self.snapshot())

    def test_roots_that_are_symlinks_home_or_slash_or_missing_are_refused(self):
        link = os.path.join(self.base, "link")
        os.symlink(self.root, link)
        for root in (link, "/", os.path.expanduser("~"), os.path.dirname(os.path.expanduser("~")), os.path.join(self.base, "missing")):
            done = subprocess.run([sys.executable, "-I", SCRIPT, "--root", root, "--proc-root", self.proc, "--apply"], capture_output=True, text=True, check=False)
            self.assertEqual((done.returncode, done.stdout), (2, ""), root)
            self.assertIn("refused", done.stderr)

    def test_apply_and_dry_run_are_mutually_exclusive_and_bad_ages_are_usage_errors(self):
        for flags in (["--apply", "--dry-run"], ["--min-age-hours", "-1"], ["--min-age-hours", "nan"]):
            done = subprocess.run([sys.executable, "-I", SCRIPT, "--root", self.root, "--proc-root", self.proc, *flags], capture_output=True, text=True, check=False)
            self.assertEqual(done.returncode, 2, flags)

    def test_default_roots_are_tmp_tmpdir_and_the_fsgg_cache_and_absent_ones_are_skipped(self):
        module = load_module()
        with mock.patch.dict(os.environ, {"TMPDIR": "/var/tmp/x"}):
            self.assertEqual(module.default_roots("/home/dev"), ["/tmp", "/var/tmp/x", "/home/dev/.cache/fsgg-tmp"])
        with mock.patch.dict(os.environ, {}, clear=True):
            self.assertEqual(module.default_roots("/home/dev"), ["/tmp", "/home/dev/.cache/fsgg-tmp"])
        home = os.path.join(self.base, "home")
        cache = os.path.join(home, ".cache", "fsgg-tmp")
        os.makedirs(cache)
        out = io.StringIO()
        with mock.patch.object(module, "default_roots", lambda h: [os.path.join(home, "absent"), cache]), contextlib.redirect_stdout(out):
            self.assertEqual(module.main(["--proc-root", self.proc, "--now", repr(self.now)]), 0)
        report = json.loads(out.getvalue())
        self.assertEqual((report["roots"], report["skipped_roots"]), ([cache], [{"root": os.path.join(home, "absent"), "reason": "absent"}]))


class LiveHost(unittest.TestCase):
    def test_dry_run_on_the_real_proc_against_an_empty_root_matches_nothing(self):
        with tempfile.TemporaryDirectory() as root:
            done = subprocess.run([sys.executable, "-I", SCRIPT, "--root", os.path.realpath(root)], capture_output=True, text=True, check=False)  # dry run: --apply may be refused by a host process
            self.assertEqual(done.returncode, 0, done.stderr)
            report = json.loads(done.stdout)
            self.assertEqual((report["entries_matched"], report["removed"]), (0, 0))
            self.assertGreater(report["processes_read"], 0)


if __name__ == "__main__":
    unittest.main()
