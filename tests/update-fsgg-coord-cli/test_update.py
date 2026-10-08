"""Exercise the source main entry with disposable packages and fake fixed commands.

Only tests replace archive/manifest hashes for their synthetic fixture. The
production entry has no hash override. Optional public-pin coverage uses the
actual shell entry and retained public bytes, without executing any package DLL.
"""
import base64
from contextlib import redirect_stdout, redirect_stderr
import fcntl
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ENTRY = ROOT / "scripts/update-fsgg-coord-cli"
HELPER = ROOT / "scripts/update_fsgg_coord_cli.py"
spec = importlib.util.spec_from_file_location("host_update", HELPER)
update = importlib.util.module_from_spec(spec)
spec.loader.exec_module(update)


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="utel-host-source-test-")
        self.root = Path(self.temporary.name)
        self.home = self.root / "home"
        self.home.mkdir()
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.archive = self.root / "package.nupkg"
        with zipfile.ZipFile(self.archive, "w") as package:
            package.writestr(update.PREFIX + "fsgg-coord-engine.dll", b"inert, never executed")
            package.writestr(update.PREFIX + "DotnetToolSettings.xml", b"inert fixture")
            package.writestr("README.md", b"fixture")
        sha = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.manifest = self.root / "manifest.json"
        self.manifest.write_text(json.dumps({
            "schema": "fsgg.release-saga/1", "contentId": "fixture-content",
            "descriptor": {"sourceSha": update.SOURCE, "version": update.VERSION, "channel": "stable",
                           "packages": [{"id": name, "version": update.VERSION,
                                         "artifact": {"payloadSha256": update.PAYLOAD_ID}}
                                        for name in ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")]},
            "state": {"channelPromotion": {"state": "promoted", "receipt": {
                "sourceSha": update.SOURCE, "version": update.VERSION, "contentId": "fixture-content"}},
                "feeds": {name: {"packages": {"FS.GG.Coord.Cli": {"state": "verified",
                         "externalPayloadSha256": update.PAYLOAD_ID, "externalSha256": sha}}}
                          for name in ("github", "nuget")}}}))
        self.hashes = patch.multiple(update, ARCHIVE_SHA=sha,
                                    MANIFEST_SHA=hashlib.sha256(self.manifest.read_bytes()).hexdigest())
        self.hashes.start()
        self.environment = patch.dict(os.environ, HOME=str(self.home), PATH=str(self.bin) + ":/usr/bin:/bin")
        self.environment.start()
        self.link = self.home / ".local/bin/fsgg-coord-engine"
        self.link.parent.mkdir(parents=True)
        self.link.symlink_to("/disposable/old-command")
        self.before = update.selector_snapshot(self.link)
        self.fake_installer()

    def tearDown(self):
        self.environment.stop()
        self.hashes.stop()
        self.temporary.cleanup()

    def fake_installer(self, failure=None, version_exit=0, extra=None):
        # Paths and behavior are baked into an owned fixture; no arbitrary command
        # override or TEST permit is offered by the product.
        script = '''#!/usr/bin/python3
import base64,hashlib,json,os,pathlib,shutil,sys,zipfile
archive=pathlib.Path(ARCHIVE)
root=pathlib.Path(ROOT)
argv=sys.argv[1:]
(root/'invocation.json').write_text(json.dumps({'argv':argv,'environment':dict(os.environ)}))
assert argv[:3]==['tool','install','FS.GG.Coord.Cli']
assert argv[argv.index('--version')+1]=='0.100.0'
config=pathlib.Path(argv[argv.index('--configfile')+1])
assert config.read_bytes()==CONFIG
path=pathlib.Path(argv[argv.index('--tool-path')+1]);path.mkdir()
if FAILURE:
 (path/'partial').write_text('original partial');print('install original failure',file=sys.stderr);sys.exit(7)
store=path/STORE;store.mkdir(parents=True)
with zipfile.ZipFile(archive) as package:
 for member in package.namelist():
  if member.startswith('tools/net10.0/any/') or member in ('README.md','FS.GG.Coord.Cli.nuspec','.signature.p7s'):
   destination=store/member;destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(package.read(member))
shutil.copyfile(archive,store/'FS.GG.Coord.Cli.nupkg')
(store/'.nupkg.metadata').write_text('{}')
(store/'fs.gg.coord.cli.0.100.0.nupkg.sha512').write_bytes(base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()))
(path/'.store/fs.gg.coord.cli/0.100.0/project.assets.json').write_text('{}')
shim=path/'fsgg-coord-engine'
shim.write_text('#!/usr/bin/python3\\nimport sys\\nassert sys.argv[1:]==["--version"]\\nprint("0.100.0.0")\\nsys.exit(VERSION_EXIT)\\n'.replace('VERSION_EXIT',str(VERSION_EXIT)))
shim.chmod(0o700)
if EXTRA:
 extra=path/EXTRA;extra.write_text('unexpected');extra.chmod(0o700)
'''
        for key, value in {"ARCHIVE": str(self.archive), "ROOT": str(self.root), "CONFIG": update.CONFIG,
                           "FAILURE": failure, "VERSION_EXIT": version_exit, "STORE": update.STORE,
                           "EXTRA": extra}.items():
            # Assignment lines leave the body and byte literals intact.
            script = script.replace("import base64,hashlib,json,os,pathlib,shutil,sys,zipfile\n",
                                    "import base64,hashlib,json,os,pathlib,shutil,sys,zipfile\n" + key + "=" + repr(value) + "\n")
        (self.bin / "dotnet").write_text(script)
        (self.bin / "dotnet").chmod(0o700)

    def invoke(self):
        stdout = io.StringIO()
        with redirect_stdout(stdout):
            code = update.main(["--version", "0.100.0", "--install-only", "--manifest", str(self.manifest),
                                "--archive", str(self.archive)])
        return code, json.loads(stdout.getvalue())

    def test_success_exact_command_and_unchanged_selector(self):
        code, result = self.invoke()
        self.assertEqual(code, 0, result)
        self.assertEqual(result["state"], "version-verified-inactive")
        self.assertTrue(result["selectorUnchanged"])
        self.assertFalse(result["nativeAccepted"])
        self.assertEqual(update.selector_snapshot(self.link), self.before)
        command = json.loads((self.root / "invocation.json").read_text())
        self.assertEqual(command["argv"][:5], ["tool", "install", "FS.GG.Coord.Cli", "--version", "0.100.0"])
        self.assertEqual(command["argv"][-1], "--no-cache")
        self.assertNotIn("--add-source", command["argv"])
        for key in ("NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "DOTNET_CLI_HOME", "TMPDIR"):
            self.assertTrue(command["environment"][key].startswith(result["workspace"] + "/"))
        self.assertNotIn("CODEX_THREAD_ID", command["environment"])
        self.assertTrue(all(c["terminal"] and c["exit"] == 0 for c in result["commands"]))
        self.assertTrue(all(g["origin"] == "observed-sdk-output-not-independent-provenance" for g in result["generated"]))

    def test_existing_destination_never_invokes_version_or_installer(self):
        destination = self.home / ".local/share/fs-gg/tools/coord-0.100.0"
        destination.mkdir(parents=True)
        (destination / "keep").write_text("preserved")
        code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "existing-install-needs-reconciliation")
        self.assertEqual(result["commands"], [])
        self.assertFalse((self.root / "invocation.json").exists())
        self.assertEqual((destination / "keep").read_text(), "preserved")

    def test_dangling_selector_cannot_activate_new_install(self):
        destination = self.home / ".local/share/fs-gg/tools/coord-0.100.0"
        alias = self.root / "alias"
        alias.symlink_to(destination)
        for target in (destination / "fsgg-coord-engine", alias / "subdirectory/other-command"):
            with self.subTest(target=target):
                self.link.unlink()
                self.link.symlink_to(target)
                code, result = self.invoke()
                self.assertEqual(code, 1)
                self.assertEqual(result["firstError"], "selector-already-targets-destination")
                self.assertEqual(result["commands"], [])

    def test_changed_selector_refuses_without_rollback_or_version(self):
        fake = self.bin / "dotnet"
        fake.write_text(fake.read_text() + "\nlink=pathlib.Path(" + repr(str(self.link)) +
                        ");link.unlink();link.symlink_to('/fixture/changed-selector')\n")
        code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "selector-changed-during-install-only")
        self.assertFalse(result["selectorUnchanged"])
        self.assertEqual(os.readlink(self.link), "/fixture/changed-selector")
        self.assertEqual(len(result["commands"]), 1)

    def test_lock_contention_and_inode_preserved(self):
        lock = self.home / ".local/state/fs-gg/coord-tool-update.lock"
        lock.parent.mkdir(parents=True)
        lock.write_text("retained lock bytes")
        lock.chmod(0o600)
        before = lock.stat().st_ino
        with lock.open("r+") as stream:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
            code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "update-lock-busy")
        self.assertEqual(lock.stat().st_ino, before)
        self.assertEqual(lock.read_text(), "retained lock bytes")
        self.assertEqual(result["commands"], [])

    def test_invalid_manifest_or_archive_fences_commands(self):
        for path, error in ((self.manifest, "release-manifest-pin"), (self.archive, "public-archive-pin")):
            with self.subTest(path=path):
                original = path.read_bytes()
                path.write_bytes(original + b"bad")
                code, result = self.invoke()
                self.assertEqual(code, 1)
                self.assertEqual(result["firstError"], error)
                self.assertEqual(result["commands"], [])
                path.write_bytes(original)

    def test_install_failure_retains_partial_directory_and_cause(self):
        self.fake_installer(failure="exit")
        code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "install-exit:7")
        self.assertEqual(len(result["commands"]), 1)
        self.assertTrue(result["commands"][0]["terminal"])
        self.assertTrue((self.home / ".local/share/fs-gg/tools/coord-0.100.0/partial").exists())
        self.assertEqual(update.selector_snapshot(self.link), self.before)

    def test_version_failure_keeps_verified_inactive_install(self):
        self.fake_installer(version_exit=9)
        code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "version-exit:9")
        self.assertEqual(result["state"], "payload-verified-inactive")
        self.assertEqual(update.selector_snapshot(self.link), self.before)
        self.assertTrue(result["commands"][1]["terminal"])

    def test_unexpected_executable_fences_version(self):
        self.fake_installer(extra="unrelated-executable")
        code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "unexpected-installed-file:unrelated-executable")
        self.assertEqual(len(result["commands"]), 1)

    def test_tampered_tool_and_symlink_fence_version(self):
        for action, expected in (
            ("(store/'tools/net10.0/any/fsgg-coord-engine.dll').write_bytes(b'changed')", "installed-payload-diff:"),
            ("(store/'tools/net10.0/any/fsgg-coord-engine.dll').unlink(); (store/'tools/net10.0/any/fsgg-coord-engine.dll').symlink_to('/outside')", "installed-link-or-special")):
            with self.subTest(action=action):
                self.fake_installer()
                fake = self.bin / "dotnet"
                fake.write_text(fake.read_text() + action + "\n")
                code, result = self.invoke()
                self.assertEqual(code, 1)
                self.assertTrue(result["firstError"].startswith(expected), result)
                self.assertEqual(len(result["commands"]), 1)
                # Disposal is fixture-owned, never product cleanup of an install.
                import shutil
                shutil.rmtree(self.home / ".local/share/fs-gg/tools/coord-0.100.0")

    def test_output_bound_reaps_fake_installer(self):
        fake = self.bin / "dotnet"
        fake.write_text("#!/usr/bin/python3\nimport os,time\nos.write(1,b'x'*100000)\ntime.sleep(10)\n")
        code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "install-output-bound")
        command = result["commands"][0]
        self.assertTrue(command["terminal"])
        self.assertEqual(command["stdout"]["bytes"], update.MAX_OUTPUT)
        self.assertEqual(command["cleanup"], "direct-child-reaped-group-termination-requested")

    def test_original_deadline_reaps_fake_installer(self):
        fake = self.bin / "dotnet"
        fake.write_text("#!/usr/bin/python3\nimport time\ntime.sleep(10)\n")
        with patch.object(update, "WORK_SECONDS", 1):
            code, result = self.invoke()
        self.assertEqual(code, 1)
        self.assertEqual(result["firstError"], "install-deadline")
        self.assertTrue(result["commands"][0]["terminal"])
        self.assertEqual(result["commands"][0]["cleanup"], "direct-child-reaped-group-termination-requested")

    def test_reporting_failure_preserves_install_first_cause(self):
        self.fake_installer(failure="exit")
        args = type("Args", (), {"manifest": self.manifest, "archive": self.archive})()
        result = update.execute(args)
        class Broken:
            def write(self, value):
                raise BrokenPipeError("fixture reporting")
        self.assertEqual(update.emit(result, Broken()), 1)
        self.assertEqual(result["firstError"], "install-exit:7")
        self.assertEqual(result["reporting"], "failed")
        self.assertIn("reporting-failed", result["additionalErrors"][0])

    def test_shell_rejects_invalid_and_conflicting_args_before_filesystem(self):
        for arguments in (["--version", "latest", "--install-only"], ["--version", "0.100.0", "--activate"],
                          ["--ver", "0.100.0"], ["--version", "0.100.0", "--version", "0.100.0"], []):
            with self.subTest(arguments=arguments):
                process = subprocess.run([str(ENTRY), *arguments], capture_output=True, timeout=5)
                self.assertEqual(process.returncode, 2)
                self.assertFalse((self.home / ".local/share/fs-gg").exists())

    @unittest.skipUnless(os.environ.get("UTEL_TEST_PUBLIC_ARCHIVE") and os.environ.get("UTEL_TEST_RELEASE_MANIFEST"),
                         "optional retained public bytes are not available on this runner")
    def test_actual_shell_with_public_pins_and_fake_installer(self):
        self.archive = Path(os.environ["UTEL_TEST_PUBLIC_ARCHIVE"])
        self.manifest = Path(os.environ["UTEL_TEST_RELEASE_MANIFEST"])
        self.fake_installer()
        process = subprocess.run([str(ENTRY), "--version", "0.100.0", "--install-only",
                                  "--archive", str(self.archive), "--manifest", str(self.manifest)],
                                 capture_output=True, timeout=20)
        result = json.loads(process.stdout)
        self.assertEqual(process.returncode, 0, result)
        self.assertEqual(result["state"], "version-verified-inactive")
        self.assertEqual(update.selector_snapshot(self.link), self.before)


if __name__ == "__main__":
    unittest.main()
