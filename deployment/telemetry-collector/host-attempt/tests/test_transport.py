import importlib.util
import json
import os
import subprocess
import tempfile
import unittest
from unittest import mock
from pathlib import Path

MODULE_PATH = Path(__file__).parents[1] / "host_attempt_transport.py"
SPEC = importlib.util.spec_from_file_location("host_attempt_transport", MODULE_PATH)
transport = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(transport)


class Completed:
    def __init__(self, returncode=0, stdout="", stderr=""):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr


def context(root, owned="run-7"):
    return {
        "schema": "fsgg.telemetry.host-attempt-transport-context/2",
        "placementSha": "a" * 40,
        "nonce": "attempt-o-0001",
        "hostBindingDll": str(root / "HostBinding.dll"),
        "recipeRoot": str(root / "recipe"),
        "recipeSha": transport.RECIPE_SHA,
        "profilePath": str(root / "profile.json"),
        "sourcePinsPath": str(root / "pins.json"),
        "bindingSha256": "b" * 64,
        "ownedRunId": owned,
        "artifactOutput": str(root / "artifacts"),
        "discoveryStart": "2026-10-02T00:00:00Z",
        "remainingSeconds": 2700,
    }


def actions(*items):
    return {"schema": transport.SCHEMA, "actions": list(items)}


def action(name, argument=None):
    return {"name": name, "argument": argument}


class TransportTests(unittest.TestCase):
    def test_effect_intention_exists_before_callback(self):
        with tempfile.TemporaryDirectory() as value:
            root = Path(value)
            lease = root / "lease"
            observed = []

            def runner(command, **kwargs):
                intention = Path(str(lease) + ".0.json")
                self.assertTrue(intention.is_file())
                record = json.loads(intention.read_text())
                self.assertTrue(record["mayHaveEffect"])
                observed.append(command)
                return Completed()

            result = transport.run_actions(actions(action("dispatch-once")), context(root), lease, runner)
            self.assertEqual("returned", result["results"][0]["outcome"])
            self.assertEqual(["gh", "workflow", "run"], observed[0][:3])
            self.assertIn("placement_sha=" + "a" * 40, observed[0])

    def test_lost_dispatch_response_preserves_intention_and_reports_unknown(self):
        with tempfile.TemporaryDirectory() as value:
            root = Path(value)
            lease = root / "lease"

            def timeout(*args, **kwargs):
                raise subprocess.TimeoutExpired(args[0], kwargs["timeout"])

            result = transport.run_actions(actions(action("dispatch-once")), context(root), lease, timeout)
            self.assertEqual("unknown", result["results"][0]["outcome"])
            self.assertTrue(Path(str(lease) + ".0.json").is_file())

    def test_partial_secret_mutation_before_response_is_retained(self):
        with tempfile.TemporaryDirectory() as value:
            root = Path(value)
            lease = root / "lease"
            old = os.environ.get(transport.SECRETS["native-auth"])
            os.environ[transport.SECRETS["native-auth"]] = "synthetic-secret"
            try:
                seen = []

                def timeout(command, **kwargs):
                    seen.append((command, kwargs["input"]))
                    raise subprocess.TimeoutExpired(command, kwargs["timeout"])

                result = transport.run_actions(actions(action("transfer-secret", "native-auth")), context(root), lease, timeout)
                self.assertEqual("unknown", result["results"][0]["outcome"])
                self.assertEqual("synthetic-secret", seen[0][1])
                self.assertNotIn("--body", seen[0][0])
                self.assertNotIn("-", seen[0][0])
                self.assertNotIn("synthetic-secret", seen[0][0])
                self.assertTrue(Path(str(lease) + ".0.json").is_file())
            finally:
                if old is None:
                    os.environ.pop(transport.SECRETS["native-auth"], None)
                else:
                    os.environ[transport.SECRETS["native-auth"]] = old

    def test_only_exact_owned_run_can_be_cancelled(self):
        with tempfile.TemporaryDirectory() as value:
            root = Path(value)
            with self.assertRaisesRegex(transport.Refusal, "owned-run-refused"):
                transport.run_actions(actions(action("cancel-owned-run", "run-8")), context(root), root / "lease", lambda *a, **k: Completed())

    def test_delete_and_readback_are_distinct_fixed_commands(self):
        with tempfile.TemporaryDirectory() as value:
            root = Path(value)
            commands = []

            def runner(command, **kwargs):
                commands.append(command)
                return Completed(stdout="[]")

            result = transport.run_actions(
                actions(action("delete-secret", "effect-admission"), action("read-secret-absence", "effect-admission")),
                context(root), root / "lease", runner, allow_batch=True)
            self.assertEqual("delete", commands[0][2])
            self.assertEqual(["gh", "api", "--include"], commands[1][:3])
            self.assertEqual(["returned", "returned"], [item["outcome"] for item in result["results"]])

    def test_duplicate_json_and_unknown_action_refuse_closed(self):
        with tempfile.TemporaryDirectory() as value:
            path = Path(value) / "input.json"
            path.write_text('{"schema":"a","schema":"b"}')
            with self.assertRaisesRegex(transport.Refusal, "json-duplicate-key-refused"):
                transport.load(path, 1024)
            with self.assertRaisesRegex(transport.Refusal, "action-refused"):
                transport.run_actions(actions(action("arbitrary-shell")), context(Path(value)), Path(value) / "lease", lambda *a, **k: Completed())

    def test_transport_never_classifies_native_or_cleanup(self):
        with tempfile.TemporaryDirectory() as value:
            root = Path(value)
            with self.assertRaisesRegex(transport.Refusal, "readback-owned-by-fsharp"):
                transport.run_actions(actions(action("emit-root-readback")), context(root), root / "lease", lambda *a, **k: Completed())

    def test_emergency_cleanup_requests_both_deletions_and_readbacks(self):
        with tempfile.TemporaryDirectory() as value:
            root = Path(value)
            commands = []

            def runner(command, **kwargs):
                commands.append(command)
                return Completed(stdout="[]")

            result = transport.emergency_cleanup(context(root), root / "emergency", runner)
            self.assertEqual(4, len(result["results"]))
            self.assertEqual(["delete", "delete", "--include", "--include"], [command[2] for command in commands])
            self.assertTrue(Path(str(root / "emergency") + ".0.0.json").is_file())
            self.assertTrue(Path(str(root / "emergency") + ".1.0.json").is_file())

    def test_batch_cannot_continue_after_failed_binding(self):
        with tempfile.TemporaryDirectory() as value:
            root=Path(value)
            with self.assertRaisesRegex(transport.Refusal,"actions-refused"):
                transport.run_actions(actions(action("invoke-binding"),action("dispatch-once")),context(root),root/"lease",lambda *a,**k:Completed())

    def test_binding_retry_accepts_only_fresh_success_after_exact_cleanup_refusal(self):
        with tempfile.TemporaryDirectory() as value:
            root=Path(value);calls=[]
            def succeeds(command,**kwargs):
                calls.append(kwargs["timeout"])
                return Completed(2,"","host-binding-refused:process-cleanup-unknown\n") if len(calls)<3 else Completed(0,"authoritative","")
            result=transport.run_actions(actions(action("invoke-binding")),context(root),root/"lease",succeeds)
            self.assertEqual("authoritative",result["results"][0]["stdout"]);self.assertEqual(3,len(calls));self.assertGreater(calls[0],calls[-1])
            rejected=[]
            def refuses(command,**kwargs):rejected.append(command);return Completed(2,"","host-binding-refused:source-head-drift\n")
            result=transport.run_actions(actions(action("invoke-binding")),context(root),root/"lease-2",refuses)
            self.assertEqual(2,result["results"][0]["exitCode"]);self.assertEqual(1,len(rejected))

    def test_remaining_budget_clamps_fixed_timeout(self):
        with tempfile.TemporaryDirectory() as value:
            root=Path(value);value_context=context(root);value_context["remainingSeconds"]=3;timeouts=[]
            def runner(command,**kwargs):timeouts.append(kwargs["timeout"]);return Completed()
            transport.run_actions(actions(action("dispatch-once")),value_context,root/"lease",runner)
            self.assertEqual([3],timeouts)

    def test_supported_run_getter_excludes_unsupported_artifacts_field(self):
        with tempfile.TemporaryDirectory() as value:
            command,_=transport.action_command(action("get-run","run-7"),context(Path(value)))
            self.assertNotIn("artifacts",command[-1])

    def test_default_runner_caps_stream_before_full_accumulation(self):
        with self.assertRaisesRegex(transport.Refusal,"transport-output-limit"):
            transport.bounded_run(["/usr/bin/python3","-c","import sys;sys.stdout.write('x'*1100000)"],timeout=5)

    def test_default_runner_times_out_and_settles_process_group(self):
        started=__import__("time").monotonic()
        with self.assertRaises(subprocess.TimeoutExpired):
            transport.bounded_run(["/usr/bin/python3","-c","import time;time.sleep(30)"],timeout=1)
        self.assertLess(__import__("time").monotonic()-started,6)

    def test_oversize_stdin_is_refused_before_spawn(self):
        with mock.patch.object(transport.subprocess,"Popen") as spawn:
            with self.assertRaisesRegex(transport.Refusal,"transport-input-limit"):
                transport.bounded_run(["/usr/bin/false"],input="x"*1048577,timeout=1)
            spawn.assert_not_called()

    def test_stalled_stdin_shares_deadline_and_owned_process_settlement(self):
        import time
        started=time.monotonic()
        with self.assertRaises(subprocess.TimeoutExpired):
            transport.bounded_run(["/usr/bin/python3","-c","import time;time.sleep(30)"],input="x"*65536,timeout=0.2)
        self.assertLess(time.monotonic()-started,1.0)


if __name__ == "__main__":
    unittest.main()
