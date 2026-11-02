"""Fixture tests for the TMPDIR default in deployment/fdev-chromium/fdev-chromium-env (.github#5240). Offline."""
import os
import stat
import subprocess
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
WRAPPER = os.path.join(HERE, "..", "..", "deployment", "fdev-chromium", "fdev-chromium-env")
# Linux sun_path is 108 bytes including the NUL; a harness socket nests under TMPDIR (fsgg-sdd-tests-<pid>-<32 hex>/x/y.sock).
SOCKET_BUDGET = 107
HARNESS_SUFFIX = len("/fsgg-sdd-tests-1234567-" + "0" * 32 + "/run/host.sock")


class Wrapper(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.home = os.path.realpath(tmp.name)
        self.bundle = os.path.join(self.home, "bundle")
        os.makedirs(os.path.join(self.bundle, "root", "usr", "lib"))
        for rel in ("manifest.json", "fonts.conf", "root/usr/lib/libnss3.so"):
            with open(os.path.join(self.bundle, rel), "w") as handle:
                handle.write("{}")

    def run_wrapper(self, *args, env=None, tmpdir=None):
        full = {"PATH": os.environ["PATH"], "HOME": self.home, "FDEV_TMPDIR_MAX": "200"}  # the fixture HOME is long
        if tmpdir is not None:
            full["TMPDIR"] = tmpdir
        full.update(env or {})
        return subprocess.run(["bash", WRAPPER, "--bundle", self.bundle, *args], capture_output=True, text=True, check=False, env=full)

    def printed(self, done):
        return dict(line.split("=", 1) for line in done.stdout.splitlines() if "=" in line)

    def test_unset_tmpdir_defaults_to_a_private_disk_directory_under_home(self):
        done = self.run_wrapper("--print-env")
        want = os.path.join(self.home, ".cache", "fsgg-tmp")
        self.assertEqual((done.returncode, self.printed(done)["TMPDIR"]), (0, want), done.stderr)
        info = os.lstat(want)
        self.assertTrue(stat.S_ISDIR(info.st_mode))
        self.assertEqual(stat.S_IMODE(info.st_mode), 0o700)

    def test_the_command_runs_with_that_tmpdir(self):
        done = self.run_wrapper("--", "sh", "-c", "printf %s \"$TMPDIR\"")
        self.assertEqual((done.returncode, done.stdout), (0, os.path.join(self.home, ".cache", "fsgg-tmp")), done.stderr)

    def test_a_tmpdir_that_is_already_set_is_never_changed_and_nothing_is_created(self):
        for value in ("/tmp", "/somewhere/else"):
            done = self.run_wrapper("--", "sh", "-c", "printf %s \"$TMPDIR\"", tmpdir=value)
            self.assertEqual((done.returncode, done.stdout), (0, value))
        self.assertFalse(os.path.exists(os.path.join(self.home, ".cache")))

    def test_an_empty_tmpdir_counts_as_unset(self):
        done = self.run_wrapper("--print-env", tmpdir="")
        self.assertEqual(self.printed(done)["TMPDIR"], os.path.join(self.home, ".cache", "fsgg-tmp"))

    def test_fdev_tmpdir_overrides_the_default_path(self):
        want = os.path.join(self.home, "t")
        done = self.run_wrapper("--print-env", env={"FDEV_TMPDIR": want})
        self.assertEqual(self.printed(done)["TMPDIR"], want)

    def test_a_default_that_is_too_long_for_a_unix_socket_is_refused_with_a_warning(self):
        done = self.run_wrapper("--print-env", env={"FDEV_TMPDIR_MAX": "20"})  # the fixture HOME path is longer than 20
        self.assertEqual((done.returncode, self.printed(done)["TMPDIR"]), (0, ""))
        self.assertIn("budget for Unix socket paths", done.stderr)
        self.assertFalse(os.path.exists(os.path.join(self.home, ".cache")))

    def test_the_shipped_default_leaves_room_for_a_harness_socket_path(self):
        # fdev's HOME is /home/developer; the default budget must keep the longest harness socket under sun_path.
        default = "/home/developer/.cache/fsgg-tmp"
        done = subprocess.run(["bash", "-c", 'grep -o "FDEV_TMPDIR_MAX:-[0-9]*" "$0"', WRAPPER], capture_output=True, text=True, check=True)
        budget = int(done.stdout.split("-")[-1])
        self.assertLessEqual(len(default), budget)
        self.assertLessEqual(budget + HARNESS_SUFFIX, SOCKET_BUDGET)

    def test_a_symlink_or_foreign_default_is_refused(self):
        os.makedirs(os.path.join(self.home, ".cache"))
        os.symlink("/tmp", os.path.join(self.home, ".cache", "fsgg-tmp"))
        done = self.run_wrapper("--print-env")
        self.assertEqual(self.printed(done)["TMPDIR"], "")
        self.assertIn("is a symlink", done.stderr)

    def test_print_tmpdir_needs_no_bundle_and_fails_when_nothing_resolves(self):
        full = {"PATH": os.environ["PATH"], "HOME": self.home, "FDEV_TMPDIR_MAX": "200"}
        done = subprocess.run(["bash", WRAPPER, "--print-tmpdir"], capture_output=True, text=True, check=False, env=full)
        self.assertEqual((done.returncode, done.stdout.strip()), (0, os.path.join(self.home, ".cache", "fsgg-tmp")), done.stderr)
        full["FDEV_TMPDIR_MAX"] = "5"
        done = subprocess.run(["bash", WRAPPER, "--print-tmpdir"], capture_output=True, text=True, check=False, env=full)
        self.assertEqual((done.returncode, done.stdout.strip()), (1, ""))

    def test_side_effect_free_actions_do_not_create_the_directory(self):
        for action in ("--print-args", "--print-args-json"):
            self.assertEqual(self.run_wrapper(action).returncode, 0)
        self.assertFalse(os.path.exists(os.path.join(self.home, ".cache")))

    def test_the_bundle_environment_is_unchanged(self):
        done = self.run_wrapper("--gpu", "--print-env")
        printed = self.printed(done)
        self.assertTrue(printed["LD_LIBRARY_PATH"].startswith(os.path.join(self.bundle, "root", "usr", "lib")))
        self.assertEqual(printed["FDEV_CHROMIUM_MODE"], "gpu")
        self.assertEqual(self.run_wrapper("--", "true").returncode, 0)
        self.assertEqual(self.run_wrapper("--bundle", "/nonexistent", "--print-env").returncode, 66)


if __name__ == "__main__":
    unittest.main()
