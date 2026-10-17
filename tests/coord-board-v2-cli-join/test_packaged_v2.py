#!/usr/bin/env python3
"""Pure mocks of the packaged qualifier. These are not native qualification."""
import importlib.util
import base64
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


def outcome(code=1, out=b"", err=b"", failure=None, cleanup="owned-group-absent"):
    return {"exitCode": code, "stdout": out, "stderr": err, "firstFailure": failure,
            "cleanup": {"status": cleanup, "failures": []}, "outputComplete": True, "observedBytes": {"stdout": len(out), "stderr": len(err)}}


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
            selected = mutate(index, result, home) if mutate else result
            return selected if isinstance(selected, dict) else outcome(*selected)
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
        self.assertEqual(set(result["outcomes"][0]), {"name", "exitCode", "stdoutObservedBytes", "stdoutRetainedBytes", "stdoutOmittedObservedBytes", "stdoutRetainedSha256", "stdoutRetainedBase64", "stderrObservedBytes", "stderrRetainedBytes", "stderrOmittedObservedBytes", "stderrRetainedSha256", "stderrRetainedBase64", "expectedRefusal", "controlTreeUnchanged", "firstFailure", "cleanup", "outputComplete"})

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

    def test_process_boundary_scrubs_credentials_and_cleans_exited_root_group(self):
        from unittest.mock import Mock
        home, capture = self.root / "home", self.root / "capture"
        home.mkdir()
        capture.mkdir()
        stdin = io.BytesIO()
        child = Mock(pid=4242, stdin=stdin)
        events = []
        child.wait.side_effect = lambda **kwargs: events.append("reap") or 1
        def start(argv, **kwargs):
            self.assertEqual(argv, [str(self.engine), "board-v2", "inspect"])
            self.assertFalse(stdin.closed)
            self.assertEqual(kwargs["stdin"], qualifier.subprocess.PIPE)
            self.assertTrue(kwargs["start_new_session"])
            self.assertEqual(kwargs["env"]["HOME"], str(home))
            for key in ("GITHUB_TOKEN", "GH_TOKEN", "NUGET_AUTH_TOKEN", "AWS_SECRET_ACCESS_KEY"):
                self.assertNotIn(key, kwargs["env"])
            kwargs["stderr"].write(b"bounded diagnostic")
            kwargs["stderr"].flush()
            return child
        def signal_group(pid, sig):
            events.append("kill" if sig else "probe")
            if sig == 0:
                raise ProcessLookupError()
        with patch.dict(os.environ, {"GITHUB_TOKEN": "private-never-forwarded", "GH_TOKEN": "private-never-forwarded"}), patch.object(qualifier.subprocess, "Popen", side_effect=start), patch.object(qualifier.os, "waitid", return_value=object()) as observe, patch.object(qualifier.os, "killpg", side_effect=signal_group):
            result = qualifier.process(self.engine, ["inspect"], home, capture)
        self.assertEqual((result["exitCode"], result["stdout"], result["stderr"]), (1, b"", b"bounded diagnostic"))
        self.assertEqual(result["cleanup"]["status"], "owned-group-absent")
        self.assertEqual(events, ["kill", "reap", "probe"])
        self.assertTrue(result["outputComplete"])
        self.assertTrue(observe.call_args.args[2] & qualifier.os.WNOWAIT)
        child.poll.assert_not_called()
        self.assertTrue(stdin.closed)

    def test_stdin_deadline_and_output_overflow_preserve_bytes_and_cleanup(self):
        from unittest.mock import Mock
        for overflow in (False, True):
            with self.subTest(overflow=overflow):
                home, capture = self.root / ("home-" + str(overflow)), self.root / ("capture-" + str(overflow))
                home.mkdir()
                capture.mkdir()
                child = Mock(pid=4242, stdin=io.BytesIO())
                child.wait.return_value = -9
                def start(argv, **kwargs):
                    kwargs["stdout"].write(b"x" * (qualifier.MAX_OUTPUT + 1) if overflow else b"original-before-timeout")
                    kwargs["stdout"].flush()
                    return child
                def signal_group(pid, sig):
                    if sig == 0:
                        raise ProcessLookupError()
                ticks = [0, 0, 0, 0, 0] if overflow else [0, 21, 22, 22, 22]
                with patch.object(qualifier.subprocess, "Popen", side_effect=start) as launch, patch.object(qualifier.os, "killpg", side_effect=signal_group) as kill, patch.object(qualifier.time, "monotonic", side_effect=ticks):
                    result = qualifier.process(self.engine, ["inspect"], home, capture)
                self.assertEqual(result["firstFailure"], "packaged output bound" if overflow else "packaged control deadline or stdin read")
                self.assertEqual(result["stdout"], b"x" * qualifier.MAX_OUTPUT if overflow else b"original-before-timeout")
                self.assertEqual(result["cleanup"]["status"], "owned-group-absent")
                self.assertEqual(result["observedBytes"]["stdout"], qualifier.MAX_OUTPUT + 1 if overflow else len(b"original-before-timeout"))
                self.assertEqual(result["outputComplete"], not overflow)
                launch.assert_called_once()
                self.assertEqual([call.args[1] for call in kill.call_args_list], [qualifier.signal.SIGKILL, 0])
                child.wait.assert_called_once()
                child.poll.assert_not_called()

    def test_remaining_descendant_or_lost_custody_is_unknown_and_never_resignalled(self):
        from unittest.mock import Mock
        for lost in (False, True):
            home, capture = self.root / ("home-" + str(lost)), self.root / ("capture-" + str(lost))
            home.mkdir()
            capture.mkdir()
            child = Mock(pid=4242, stdin=io.BytesIO())
            child.wait.return_value = 1
            ticks = [0, 0, 0, 0, 0, 6] if not lost else [0, 0, 0, 0]
            with patch.object(qualifier.subprocess, "Popen", return_value=child), patch.object(qualifier.os, "waitid", side_effect=ChildProcessError() if lost else [object()]), patch.object(qualifier.os, "killpg") as kill, patch.object(qualifier.time, "monotonic", side_effect=ticks), patch.object(qualifier.time, "sleep"):
                result = qualifier.process(self.engine, ["inspect"], home, capture)
            self.assertEqual(result["cleanup"]["status"], "Unknown")
            if lost:
                kill.assert_not_called()
                self.assertEqual(result["firstFailure"], "process-acquisition-unavailable")
            else:
                self.assertEqual([call.args[1] for call in kill.call_args_list], [qualifier.signal.SIGKILL, 0])
            child.poll.assert_not_called()

    def test_acquisition_failure_keeps_partial_cases_identity_and_original_outputs(self):
        def fail(index, result, home):
            if index == 2:
                return outcome(None, b"partial original", b"native original", "acquisition-failed", "Unknown")
            return result
        result = self.run_fake(fail)
        self.assertFalse(result["passed"])
        self.assertEqual(len(result["outcomes"]), 3)
        self.assertIn("installedIdentity", result)
        self.assertEqual(result["firstFailure"], {"case": "product-missing-credential", "reason": "acquisition-failed"})
        self.assertEqual(base64.b64decode(result["outcomes"][2]["stdoutRetainedBase64"]), b"partial original")
        self.assertEqual(base64.b64decode(result["outcomes"][2]["stderrRetainedBase64"]), b"native original")
        self.assertEqual(result["outcomes"][2]["cleanup"]["status"], "Unknown")

    def test_unexpected_later_exception_does_not_drop_prior_evidence_or_first_failure(self):
        def fail(index, result, home):
            if index == 2:
                raise RuntimeError("not retained raw exception")
            return (1, b"", b"first unexpected refusal") if index == 0 else result
        result = self.run_fake(fail)
        self.assertFalse(result["passed"])
        self.assertEqual(len(result["outcomes"]), 3)
        self.assertEqual(result["firstFailure"]["case"], "missing-binding")
        self.assertEqual(base64.b64decode(result["outcomes"][0]["stderrRetainedBase64"]), b"first unexpected refusal")
        self.assertIn("installedIdentity", result)
        self.assertNotIn("not retained raw exception", json.dumps(result))

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

    def test_control_tree_collection_error_retains_current_output(self):
        def mutation(index, result, home):
            if index == 1:
                (home / "unexpected-link").symlink_to("missing-target")
            return result
        result = self.run_fake(mutation)
        self.assertFalse(result["passed"])
        self.assertEqual(len(result["outcomes"]), 2)
        self.assertEqual(result["firstFailure"], {"case": "malformed-binding", "reason": "control-tree-readback-unavailable"})
        self.assertEqual(base64.b64decode(result["outcomes"][1]["stderrRetainedBase64"]), DIAGNOSTICS[1])

    def test_reap_failure_keeps_first_timeout_and_separate_cleanup_uncertainty(self):
        from unittest.mock import Mock
        home, capture = self.root / "home", self.root / "capture"
        home.mkdir()
        capture.mkdir()
        child = Mock(pid=4242, stdin=io.BytesIO())
        child.wait.side_effect = qualifier.subprocess.TimeoutExpired("inert", 5)
        with patch.object(qualifier.subprocess, "Popen", return_value=child), patch.object(qualifier.os, "killpg") as kill, patch.object(qualifier.time, "monotonic", side_effect=[0, 21, 22, 22]):
            result = qualifier.process(self.engine, ["inspect"], home, capture)
        self.assertEqual(result["firstFailure"], "packaged control deadline or stdin read")
        self.assertEqual(result["cleanup"]["status"], "Unknown")
        self.assertIn("root-reap-unconfirmed", result["cleanup"]["failures"])
        kill.assert_called_once_with(4242, qualifier.signal.SIGKILL)

    def test_capture_close_failure_retains_original_first_cause_and_bytes(self):
        from unittest.mock import Mock
        home, capture = self.root / "home", self.root / "capture"
        home.mkdir()
        capture.mkdir()
        child = Mock(pid=4242, stdin=io.BytesIO())
        child.wait.return_value = -9
        original_open = Path.open
        class BadClose:
            def __init__(self, stream):
                self.stream = stream
            def __getattr__(self, name):
                return getattr(self.stream, name)
            def close(self):
                self.stream.close()
                raise OSError("not retained raw close error")
        def open_capture(path, *args, **kwargs):
            stream = original_open(path, *args, **kwargs)
            return BadClose(stream) if path.name == "stderr" else stream
        def start(argv, **kwargs):
            kwargs["stderr"].write(b"original before failed close")
            kwargs["stderr"].flush()
            return child
        def signal_group(pid, sig):
            if sig == 0:
                raise ProcessLookupError()
        with patch.object(Path, "open", open_capture), patch.object(qualifier.subprocess, "Popen", side_effect=start), patch.object(qualifier.os, "killpg", side_effect=signal_group), patch.object(qualifier.time, "monotonic", side_effect=[0, 21, 22, 22, 22]):
            result = qualifier.process(self.engine, ["inspect"], home, capture)
        self.assertEqual(result["firstFailure"], "packaged control deadline or stdin read")
        self.assertEqual(result["stderr"], b"original before failed close")
        self.assertEqual(result["cleanup"]["status"], "Unknown")
        self.assertIn("capture-close-unavailable", result["cleanup"]["failures"])

    def test_evidence_write_failure_cannot_replace_first_cause_or_pass(self):
        from contextlib import redirect_stderr
        output = self.root / "failure.json"
        arguments = ["packaged_v2.py", "--engine", str(self.engine), "--package", str(self.package), "--output", str(output),
                     "--source-sha", SOURCE, "--run-id", "123", "--run-attempt", "1"]
        retained = {"passed": False, "firstFailure": {"case": "missing-binding", "reason": "original-first-cause"}}
        original = json.dumps(retained)
        diagnostic = io.StringIO()
        with patch.object(qualifier, "qualify", return_value=retained), patch("sys.argv", arguments), patch.object(Path, "open", side_effect=PermissionError("private IO error")) as write, redirect_stderr(diagnostic):
            self.assertEqual(qualifier.main(), 1)
        write.assert_called_once()
        self.assertEqual(json.dumps(retained), original)
        self.assertNotIn("private", diagnostic.getvalue())

    def test_wrong_source_attempt_and_installed_bytes_refuse_before_process(self):
        for source, attempt in (("b" * 40, "1"), (SOURCE, "2")):
            result = qualifier.qualify(self.engine, self.package, source, "123", attempt, lambda *args: self.fail("must not launch"))
            self.assertFalse(result["passed"])
            self.assertEqual(result["outcomes"], [])
        (self.engine.parent / qualifier.MEMBERS[1]).write_bytes(b"changed")
        self.assertFalse(self.run_fake()["passed"])
        self.assertEqual(self.calls, [])


if __name__ == "__main__":
    unittest.main()
