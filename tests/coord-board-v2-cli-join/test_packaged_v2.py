#!/usr/bin/env python3
"""Pure mocks of the packaged qualifier. These are not native qualification."""
import importlib.util
import json
import io
import os
from unittest.mock import patch
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("packaged_v2", Path(__file__).with_name("packaged_v2.py"))
qualifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(qualifier)
SOURCE = "a" * 40
DIAGNOSTICS = (
    b"could not read refresh input", b"unknown, missing or duplicate properties",
    b"read-only V2 inspection token", b"integrator stdin is restricted to organization binding 4 inspection",
    b"selected binding does not match the loaded adapter assembly SHA-256",
    b"binding and schema versions disagree", b"repository binding cannot select the organization",
    b"integrator observations are read-only organization inspect input", b"",
)


class PackagedControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        tools = self.root / "tools"
        tools.mkdir()
        self.engine = tools / "fsgg-coord-engine"
        self.engine.write_bytes(b"inert-shim-never-executed")
        self.engine.chmod(0o700)
        self.package = self.root / "FS.GG.Coord.Cli.0.103.0.nupkg"
        with zipfile.ZipFile(self.package, "w") as archive:
            archive.writestr("FS.GG.Coord.Cli.nuspec", '<package><metadata><id>FS.GG.Coord.Cli</id><version>0.103.0</version><repository commit="' + SOURCE + '"/></metadata></package>')
            for name in qualifier.MEMBERS:
                payload = ("inert-" + name).encode()
                archive.writestr("tools/net10.0/any/" + name, payload)
                (tools / name).write_bytes(payload)
        self.calls = []

    def run_fake(self, mutate=None):
        def runner(engine, args, home, capture):
            index = len(self.calls)
            self.calls.append(args)
            result = (1, b"", DIAGNOSTICS[index])
            return mutate(index, result, home) if mutate else result
        return qualifier.qualify(self.engine, self.package, SOURCE, "123", "1", runner)

    def test_all_distinct_controls_and_evidence_are_closed_and_repeatable(self):
        result = self.run_fake()
        self.assertTrue(result["passed"])
        self.assertEqual(len(result["outcomes"]), 9)
        self.assertEqual(len({row["name"] for row in result["outcomes"]}), 9)
        self.assertEqual(result["packageSha256"], qualifier.sha(self.package))
        self.assertFalse(result["activeDeveloperInstallQualified"])
        self.assertFalse(result["nativePositiveQualified"])
        self.assertFalse(result["migrationScopeValidationQualified"])
        self.calls.clear()
        self.assertEqual(self.run_fake(), result)
        self.assertEqual(set(result["outcomes"][0]), {"name", "exitCode", "stdoutBytes", "stdoutSha256", "stderrBytes", "stderrSha256", "expectedRefusal", "controlTreeUnchanged"})

    def test_old_cli_generic_failure_is_not_qualification(self):
        self.assertFalse(self.run_fake(lambda index, result, home: (1, b"", b"unknown command"))["passed"])

    def test_ignoring_adapter_or_stdin_or_old_schema_refusal_fails(self):
        for target in (3, 4, 5, 6, 7, 8):
            with self.subTest(target=target):
                self.calls.clear()
                self.assertFalse(self.run_fake(lambda index, result, home: (1, b"", b"wrong-stage") if index == target else result)["passed"])

    def test_zero_exit_or_help_stdout_or_changed_control_tree_fails(self):
        for replacement in ((0, b"", DIAGNOSTICS[0]), (1, b"usage", DIAGNOSTICS[0])):
            self.calls.clear()
            self.assertFalse(self.run_fake(lambda index, result, home: replacement if index == 0 else result)["passed"])
        self.calls.clear()
        def mutation(index, result, home):
            if index == 0:
                (home / "unexpected-output").write_text("inert")
            return result
        self.assertFalse(self.run_fake(mutation)["passed"])

    def test_process_boundary_scrubs_credentials_and_keeps_stdin_open(self):
        home, capture = self.root / "home", self.root / "capture"
        home.mkdir()
        capture.mkdir()
        stdin = io.BytesIO()
        class FakeChild:
            returncode = 1
            def poll(self):
                return 1
        child = FakeChild()
        child.stdin = stdin
        def start(argv, **kwargs):
            self.assertEqual(argv, [str(self.engine), "board-v2", "inspect"])
            self.assertFalse(stdin.closed)
            self.assertEqual(kwargs["stdin"], qualifier.subprocess.PIPE)
            self.assertTrue(kwargs["start_new_session"])
            self.assertEqual(kwargs["env"]["HOME"], str(home))
            for key in ("GITHUB_TOKEN", "GH_TOKEN", "NUGET_AUTH_TOKEN", "AWS_SECRET_ACCESS_KEY"):
                self.assertNotIn(key, kwargs["env"])
            kwargs["stderr"].write(b"bounded diagnostic")
            return child
        with patch.dict(os.environ, {"GITHUB_TOKEN": "private-never-forwarded", "GH_TOKEN": "private-never-forwarded"}), patch.object(qualifier.subprocess, "Popen", side_effect=start):
            self.assertEqual(qualifier.process(self.engine, ["inspect"], home, capture), (1, b"", b"bounded diagnostic"))
        self.assertTrue(stdin.closed)

    def test_stdin_deadline_and_output_overflow_kill_once_without_retry(self):
        from unittest.mock import Mock
        for overflow in (False, True):
            with self.subTest(overflow=overflow):
                home, capture = self.root / ("home-" + str(overflow)), self.root / ("capture-" + str(overflow))
                home.mkdir()
                capture.mkdir()
                child = Mock(pid=4242, stdin=io.BytesIO())
                child.poll.return_value = None
                def start(argv, **kwargs):
                    if overflow:
                        kwargs["stdout"].write(b"x" * (qualifier.MAX_OUTPUT + 1))
                        kwargs["stdout"].flush()
                    return child
                with patch.object(qualifier.subprocess, "Popen", side_effect=start) as launch, patch.object(qualifier.os, "killpg") as kill, patch.object(qualifier.time, "monotonic", side_effect=([0, 0] if overflow else [0, 21])):
                    with self.assertRaises(ValueError):
                        qualifier.process(self.engine, ["inspect"], home, capture)
                    launch.assert_called_once()
                    kill.assert_called_once_with(4242, qualifier.signal.SIGKILL)
                    child.wait.assert_called_once_with(timeout=5)

    def test_failed_evidence_retains_only_bounded_error_class(self):
        output = self.root / "failure.json"
        arguments = ["packaged_v2.py", "--engine", str(self.engine), "--package", str(self.package), "--output", str(output),
                     "--source-sha", SOURCE, "--run-id", "123", "--run-attempt", "1"]
        with patch.object(qualifier, "qualify", side_effect=ValueError("private raw diagnostic must not escape")), patch("sys.argv", arguments):
            self.assertEqual(qualifier.main(), 1)
        result = json.loads(output.read_text())
        self.assertFalse(result["passed"])
        self.assertEqual(result["failureClass"], "ValueError")
        self.assertNotIn("private", output.read_text())

    def test_wrong_source_attempt_and_installed_bytes_refuse_before_process(self):
        for source, attempt in (("b" * 40, "1"), (SOURCE, "2")):
            with self.assertRaises(ValueError):
                qualifier.qualify(self.engine, self.package, source, "123", attempt, lambda *args: self.fail("must not launch"))
        (self.engine.parent / qualifier.MEMBERS[1]).write_bytes(b"changed")
        with self.assertRaises(ValueError):
            self.run_fake()
        self.assertEqual(self.calls, [])


if __name__ == "__main__":
    unittest.main()
