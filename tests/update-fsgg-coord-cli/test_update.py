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
import resource
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
        environment = command["environment"]
        self.assertEqual(environment["HOME"], result["workspace"] + "/sdk-home")
        for name in ("DOTNET_GENERATE_ASPNET_CERTIFICATE", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"):
            self.assertEqual(environment[name], "false")
        self.assertEqual(environment["DOTNET_CLI_USE_MSBUILD_SERVER"], "0")
        self.assertEqual(environment["MSBUILDDISABLENODEREUSE"], "1")
        self.assertEqual(set(environment) - {"LC_CTYPE"}, {
            "HOME", "PATH", "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO", "DOTNET_PROCESSOR_COUNT",
            "DOTNET_GENERATE_ASPNET_CERTIFICATE", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH",
            "DOTNET_CLI_USE_MSBUILD_SERVER", "MSBUILDDISABLENODEREUSE", "DOTNET_CLI_HOME",
            "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "NUGET_PLUGINS_CACHE_PATH", "NUGET_SCRATCH", "TMPDIR"})
        self.assertNotIn("CODEX_THREAD_ID", environment)
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

    def test_fake_installer_inherits_runtime_resource_policy(self):
        # `resource.getrlimit` spells "no limit" as RLIM_INFINITY, and CPython changed that spelling:
        # -1 up to 3.14, 18446744073709551615 from 3.15. The fake installer runs under /usr/bin/python3
        # while this process may be a different minor (CI's setup-python resolves "3.x"), so raw integers
        # from the two interpreters are not comparable (.github#5018). Each side reports a limit as None
        # when it equals ITS OWN interpreter's RLIM_INFINITY, so the comparison is on the semantics the
        # test is about: finite limits are inherited exactly, and an unlimited one stays unlimited.
        fake = self.bin / "dotnet"
        fake.write_text(fake.read_text().replace("import base64,hashlib,json,os,pathlib,shutil,sys,zipfile",
                                                "import base64,hashlib,json,os,pathlib,shutil,sys,zipfile,resource") +
                        "\n(root/'runtime-limits.json').write_text(json.dumps({name:[None if v==resource.RLIM_INFINITY else v for v in resource.getrlimit(getattr(resource,name))] for name in ('RLIMIT_AS','RLIMIT_FSIZE','RLIMIT_CPU')}))\n")
        expected = {name: [None if v == resource.RLIM_INFINITY else v
                           for v in resource.getrlimit(getattr(resource, name))]
                    for name in ("RLIMIT_AS", "RLIMIT_FSIZE", "RLIMIT_CPU")}
        code, result = self.invoke()
        self.assertEqual(code, 0, result)
        self.assertEqual(json.loads((self.root / "runtime-limits.json").read_text()), expected)

    def test_rlimit_projection_is_interpreter_independent_and_not_vacuous(self):
        # Control for the projection above (.github#5018): both interpreters must agree on an unlimited and
        # on a finite limit, and a finite limit must NOT project to None or the test above passes vacuously.
        project = ("import resource,json,sys;i=resource.RLIM_INFINITY;"
                   "resource.setrlimit(resource.RLIMIT_FSIZE,tuple(i if v=='inf' else int(v) for v in sys.argv[1:3]));"
                   "print(json.dumps([None if v==i else v for v in resource.getrlimit(resource.RLIMIT_FSIZE)]))")
        for value, expected in (("inf", [None, None]), (str(1 << 40), [1 << 40, 1 << 40])):
            for interpreter in {sys.executable, "/usr/bin/python3"}:
                with self.subTest(interpreter=interpreter, limit=value):
                    out = subprocess.run([interpreter, "-c", project, value, value],
                                         capture_output=True, text=True, check=True).stdout
                    self.assertEqual(json.loads(out), expected)

    def test_reaped_leader_with_open_pipe_retains_unsupported_group_boundary(self):
        # Finite fake lifecycle: terminal leader plus pipe still held elsewhere.
        # No real descendant is abandoned and no numeric PGID is ever signalled.
        class Reaped:
            returncode = 0
            pid = 123456789
            def __init__(self):
                out_read, out_write = os.pipe()
                err_read, err_write = os.pipe()
                self.stdout, self.stderr = os.fdopen(out_read, "rb"), os.fdopen(err_read, "rb")
                self.writers = (out_write, err_write)
        process = Reaped()
        try:
            with patch.object(update.subprocess, "Popen", return_value=process), \
                 patch.object(update.os, "killpg") as kill, patch.object(update, "WORK_SECONDS", 0.02):
                code, result = self.invoke()
            self.assertEqual(code, 1)
            self.assertEqual(result["firstError"], "install-deadline")
            self.assertEqual(result["commands"][0]["cleanup"], "unsupported-group-custody-after-leader-reap")
            self.assertIn("group-cleanup-unsupported-after-leader-reap", result["additionalErrors"])
            kill.assert_not_called()
        finally:
            for fd in process.writers:
                os.close(fd)

    def test_cleanup_failure_preserves_deadline_and_retained_output(self):
        fake = self.bin / "dotnet"
        fake.write_text("#!/usr/bin/python3\nimport time\nprint('original output',flush=True)\ntime.sleep(10)\n")
        original_kill = update.os.killpg
        def signal_then_fail(pid, sig):
            # Real fixture termination still happens; only its reporting fails.
            original_kill(pid, sig)
            raise OSError("fixture kill reporting failure")
        captured = []
        original_popen = update.subprocess.Popen
        def retain_child(*args, **kwargs):
            value = original_popen(*args, **kwargs)
            captured.append(value)
            return value
        try:
            with patch.object(update.subprocess, "Popen", side_effect=retain_child), \
                 patch.object(update.os, "killpg", side_effect=signal_then_fail), \
                 patch.object(update, "WORK_SECONDS", 1):
                code, result = self.invoke()
            self.assertEqual(code, 1)
            self.assertEqual(result["firstError"], "install-deadline")
            self.assertIn("cleanup:fixture kill reporting failure", result["additionalErrors"])
            self.assertEqual(result["commands"][0]["cleanup"], "unknown")
            self.assertTrue(result["commands"][0]["terminal"])
            self.assertGreater(result["commands"][0]["stdout"]["bytes"], 0)
        finally:
            # Fixture owner retains the actual child object, independently of the
            # failed reporting path, and observes the exact terminal process.
            for child in captured:
                child.wait(timeout=5)

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



class BoardNativeResultsTests(unittest.TestCase):
    """Synthetic native facts through the existing command/result consumer; no native IO."""
    DATE = "2026-10-19T12:00:00Z"

    def setUp(self):
        import runpy
        self.decoder = runpy.run_path(str(ROOT / "scripts/board_v2_native_results.py"))
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def run_original(self, stdout=b"", code=0):
        import time
        outcome = {"commands": [], "additionalErrors": []}
        try:
            record = update.command([sys.executable, "-c", "import sys;sys.stdout.buffer.write(" + repr(stdout) + ");sys.exit(" + str(code) + ")"],
                                    os.environ.copy(), self.root, "native-result", time.monotonic() + 5, outcome)
        except update.Refusal:
            record = outcome["commands"][0]
        return record

    def snapshot(self, option="delivery", name="Delivery"):
        def value(field, kind, **properties):
            pairs = [part for key, value in properties.items() for part in (key, json.dumps(value))]
            return [field, field, kind, json.dumps(pairs)]
        values = value("level", "ProjectV2ItemFieldSingleSelectValue", id="native-level", updatedAt=self.DATE, optionId=option, name=name)
        values += value("roadmap", "ProjectV2ItemFieldTextValue", id="native-roadmap", updatedAt=self.DATE, text="docs/promise.md")
        values += value("repository", "ProjectV2ItemFieldRepositoryValue")
        return ["actor", "owner", "organization", "project", "Board", "3", "True", "True", self.DATE,
                "3", "level", "Work level", "SINGLE_SELECT", "2", "delivery", "Delivery", "BLUE", "", "outcome", "Outcome", "GREEN", "",
                "roadmap", "Roadmap", "TEXT", "0", "repository", "Repository", "REPOSITORY", "0",
                "1", "item", "issue", "Issue", "Synthetic/Repo", "1", self.DATE, "False", "3", *values,
                "1", "view", "All items", "TABLE_LAYOUT", "", "", "2", "level", "roadmap",
                "1", "issue", "Synthetic/Repo", "1", self.DATE, "OPEN"]

    def custody(self):
        prepared_path = self.root / "prepared.json"
        invocation = Path(str(prepared_path) + ".invocation")
        invocation.mkdir()
        scope = {key: "synthetic" for key in "sourceRevision artifactSha256 cliArtifactSha256 principalId principalLogin statusFieldId roadmapFieldId trackFieldId observationFieldId inReviewColor inReviewDescription".split()}
        scope.update(sourceRevision="a" * 40, artifactSha256="a" * 64, cliArtifactSha256="b" * 64, principalId="actor", principalLogin="owner", selectedIssues=[{"id": "issue", "repository": "Synthetic/Repo", "number": 1, "slice": "selected"}], views=[], initialPlanning=[])
        original = dict(schema="fs.gg.board-v2.migration-prepare/1", preparedPath=str(prepared_path), observedAt=self.DATE,
                        scope=scope, snapshot=self.snapshot(), changes=[], ownerActions=[], digest="a" * 64)
        prepared_path.write_text(json.dumps(original))
        (invocation / "prepared.json").write_text(json.dumps(original))
        readback = dict(schema="fs.gg.board-v2.migration-readback/1", observedAt=self.DATE, scope=scope,
                        snapshot=self.snapshot("outcome", "Outcome"), snapshotDigest="b" * 64,
                        admission="readback-only; original outcome and uncertainty retained")
        readback_path = invocation / ("readback-" + "c" * 32 + ".json")
        readback_path.write_text(json.dumps(readback))
        return prepared_path, readback_path

    def test_migration_file_not_stdout_and_complete_reported_preservation(self):
        prepared, readback = self.custody()
        record = self.run_original()
        observed = update.qualify_board_v2_result(record, kind="migration-readback", source_path=readback, prepared_path=prepared)
        self.assertEqual(observed["state"], "Observed", observed)
        facts = observed["value"]
        expected = {"kind": "ProjectV2ItemFieldSingleSelectValue", "fieldId": "level", "optionId": "delivery", "name": "Delivery"}
        desired = {**expected, "optionId": "outcome", "name": "Outcome"}
        proof = self.decoder["verify_field_transition"](facts["before"], facts["after"], "item", "level", expected, desired)
        self.assertEqual((proof["fields"], proof["views"], proof["memberships"]), (3, 1, 1))
        self.assertIn("hidden native fields remain unproved", proof["coverage"])
        self.assertEqual(len(observed["originals"]), 2)
        wrong_stdout = update.qualify_board_v2_result(self.run_original(b"{}"), kind="migration-readback", source_path=readback, prepared_path=prepared)
        self.assertEqual(wrong_stdout["state"], "Unknown")
        self.assertEqual(record["stdout"], b"")

    def test_prepare_snapshot_is_exact_unchanged_readonly_scope_and_file_not_stdout(self):
        prepared, _ = self.custody()
        original = json.loads(prepared.read_text())
        record = self.run_original()
        selected = dict(kind="migration-prepare", source_path=prepared, expected_source_revision="a" * 40, expected_scope=original["scope"])
        observed = update.qualify_board_v2_result(record, **selected)
        self.assertEqual(observed["state"], "Observed", observed)
        self.assertEqual(len(observed["value"]["snapshot"]["items"]), 1)
        self.assertEqual(update.qualify_board_v2_result(self.run_original(prepared.read_bytes()), **selected)["state"], "Unknown")
        for key, changed in (("changes", [["initialize-planning-value/1"]]), ("ownerActions", [7]), ("preparedPath", str(self.root / "another.json"))):
            prepared.write_text(json.dumps({**original, key: changed}))
            self.assertEqual(update.qualify_board_v2_result(record, **selected)["state"], "Unknown")
        prepared.write_text(json.dumps(original))
        self.assertEqual(update.qualify_board_v2_result(record, **{**selected, "expected_source_revision": "b" * 40})["state"], "Unknown")
        self.assertEqual(update.qualify_board_v2_result(record, **{**selected, "expected_scope": {}})["state"], "Unknown")
        prepared.write_text(json.dumps({**original, "snapshot": original["snapshot"][:-1]}))
        self.assertEqual(update.qualify_board_v2_result(record, **selected)["state"], "Unknown")

    def test_wrong_union_changed_preimage_and_unrelated_changes_refuse(self):
        decode = self.decoder["decode_field_value"]
        text_value = {"__typename": "ProjectV2ItemFieldTextValue", "field": {"id": "field"}, "text": "Owning plan"}
        select_value = {"__typename": "ProjectV2ItemFieldSingleSelectValue", "field": {"id": "field"}, "optionId": "outcome", "name": "Outcome"}
        self.assertEqual(decode(text_value, "field")["text"], "Owning plan")
        self.assertEqual(decode({**text_value, "text": ""}, "field")["text"], "")
        self.assertEqual(decode(select_value, "field")["name"], "Outcome")
        wrong = dict(select_value, __typename="ProjectV2ItemFieldTextValue")
        with self.assertRaises(self.decoder["Unknown"]): decode(wrong, "field")
        with self.assertRaises(self.decoder["Unknown"]): self.decoder["require_preimage"](decode(select_value, "field"), decode(text_value, "field"))
        before = self.decoder["decode_snapshot"](self.snapshot())
        import copy
        adjacent = copy.deepcopy(before)
        adjacent["items"]["item"]["values"]["level"]["properties"]["updatedAt"] = "2026-10-19T12:01:00Z"
        with self.assertRaises(self.decoder["Unknown"]): self.decoder["require_preimage"](adjacent, before)
        after = self.decoder["decode_snapshot"](self.snapshot("outcome", "Outcome"))
        after["items"]["item"]["values"]["repository"]["tokens"][3] = "changed opaque field"
        expected = {"kind": "ProjectV2ItemFieldSingleSelectValue", "fieldId": "level", "optionId": "delivery", "name": "Delivery"}
        with self.assertRaises(self.decoder["Unknown"]):
            self.decoder["verify_field_transition"](before, after, "item", "level", expected, {**expected, "optionId": "outcome", "name": "Outcome"})

    def test_missing_partial_invalid_or_lost_original_stays_unknown_without_resend(self):
        prepared, readback = self.custody()
        record = self.run_original()
        for payload in (b"", b"{}", b"{", b'{"schema":"one","schema":"two"}', b'\xff\xfe{}', b'\xef\xbb\xbf{}'):
            readback.write_bytes(payload)
            with patch.object(update.subprocess, "Popen", side_effect=AssertionError("decoder must not resend")):
                result = update.qualify_board_v2_result(record, kind="migration-readback", source_path=readback, prepared_path=prepared)
            self.assertEqual(result["state"], "Unknown")
        readback.unlink()
        self.assertEqual(update.qualify_board_v2_result(record, kind="migration-readback", source_path=readback, prepared_path=prepared)["state"], "Unknown")
        self.assertEqual(update.qualify_board_v2_result({**record, "terminal": False}, kind="migration-readback", source_path=readback, prepared_path=prepared)["state"], "Unknown")
        self.assertEqual(update.qualify_board_v2_result(self.run_original(code=3), kind="migration-readback", source_path=readback, prepared_path=prepared)["state"], "Unknown")
        self.assertEqual(list(self.root.glob("native-result.stdout")), [self.root / "native-result.stdout"])

    def test_missing_maintained_decoder_is_unknown_without_dispatch(self):
        record = self.run_original()
        with patch("importlib.util.spec_from_file_location", side_effect=FileNotFoundError("missing decoder")), \
             patch.object(update.subprocess, "Popen", side_effect=AssertionError("decoder must not resend")):
            result = update.qualify_board_v2_result(record, kind="migration-prepare", source_path=self.root / "original.json")
        self.assertEqual(result["state"], "Unknown")
        self.assertEqual(result["gap"], "maintained-result-decoder-unavailable")
        self.assertEqual(result["originals"][0]["path"], str(self.root / "original.json"))

    def test_partial_snapshot_unknown_union_and_trailing_tokens_refuse(self):
        for tokens in (self.snapshot()[:-1], self.snapshot() + ["extra"], ["bad-count"],
                       [value.replace("ProjectV2ItemFieldTextValue", "FutureValue") for value in self.snapshot()]):
            with self.assertRaises(self.decoder["Unknown"]): self.decoder["decode_snapshot"](tokens)

    def inspection(self, version):
        schema, binding_schema = {3: (1, 1), 5: (3, 3)}[version]
        report = {key: None for key in self.decoder["INSPECTION_COMMON"]}
        report.update(schema=f"fsgg.coord.board-v2-inspection/{schema}", bindingVersion=version, schemaVersion=binding_schema,
                      projectId="project", selectedIssues={"issue": "Synthetic/Repo#1"}, selected=1, attempted=1, mutationAttempts=0)
        item = {key: None for key in self.decoder["INSPECTION_ITEM"]}
        item.update(issue={"owner": "Synthetic", "repository": "Repo", "number": 1}, expectedNodeId="issue", mutationAttempts=0, dependencyObservations=[], dependencyReadComplete=False, sourceCurrentness="Unknown")
        if version == 5:
            report.update({key: None for key in self.decoder["INSPECTION_PROGRAMME"]})
            report["memberships"] = [dict(itemId="item", contentId="issue", kind="Issue", issue=item["issue"], enrolled=True, discrepancy=None)]
            item.update(sourceRevision=None, accountableOwner=None, currentDeliverable=None, nativeIntake=None, workItem=None)
        report["items"] = [item]
        expected = {key: report[key] for key in ("bindingVersion", "schemaVersion", "projectId", "selectedIssues")}
        return report, expected

    def test_state_only_dependency_observation_accepts_actual_producer_shape(self):
        report, expected = self.inspection(5)
        dependency = {"issue": {"owner": "Synthetic", "repository": "Dependency", "number": 2},
                      "nodeId": "dependency", "updatedAt": self.DATE, "state": "OPEN",
                      "url": "https://github.com/Synthetic/Dependency/issues/2", "bodySha256": ""}
        report["items"][0]["dependencyObservations"] = [dependency]
        observed = self.decoder["decode_inspection"](report, expected)
        self.assertEqual(observed["report"]["items"][0]["dependencyObservations"], [dependency])
        self.assertEqual(dependency["bodySha256"], "")  # Never fabricate a body observation.

    def test_issue_and_plan_body_hashes_remain_required(self):
        for role in ("native", "planObservation"):
            report, expected = self.inspection(5)
            report["items"][0][role] = {"issue": {"owner": "Synthetic", "repository": "Repo", "number": 1},
                                       "nodeId": "issue", "updatedAt": self.DATE, "state": "OPEN",
                                       "url": "https://github.com/Synthetic/Repo/issues/1", "bodySha256": ""}
            with self.subTest(role=role), self.assertRaisesRegex(self.decoder["Unknown"], "inspection-native-observation-shape"):
                self.decoder["decode_inspection"](report, expected)

    def test_nonempty_malformed_dependency_body_hash_remains_unknown(self):
        for digest in ("not-a-body-hash", "a" * 63, "a" * 65, "A" * 64):
            report, expected = self.inspection(5)
            report["items"][0]["dependencyObservations"] = [{"issue": {"owner": "Synthetic", "repository": "Dependency", "number": 2},
                "nodeId": "dependency", "updatedAt": self.DATE, "state": "CLOSED",
                "url": "https://github.com/Synthetic/Dependency/issues/2", "bodySha256": digest}]
            with self.subTest(digest=digest), self.assertRaisesRegex(self.decoder["Unknown"], "inspection-native-observation-shape"):
                self.decoder["decode_inspection"](report, expected)

    def test_product3_and_org5_actual_report_families_and_cross_shape_refusal(self):
        record = self.run_original()
        for version in (3, 5):
            report, expected = self.inspection(version)
            path = self.root / "inspection.json"
            path.write_text(json.dumps(report))
            observed = update.qualify_board_v2_result(record, kind="inspection", source_path=path, expected_binding=expected)
            self.assertEqual(observed["state"], "Observed", observed)
            self.assertEqual(observed["value"]["family"], "product3" if version == 3 else "organization5")
            for change in ({"schema": "fsgg.coord.board-v2-inspection/2"}, {"attempted": 0}, {"mutationAttempts": 1}, {"unknown": True}):
                path.write_text(json.dumps({**report, **change}))
                self.assertEqual(update.qualify_board_v2_result(record, kind="inspection", source_path=path, expected_binding=expected)["state"], "Unknown")


if __name__ == "__main__":
    unittest.main()
