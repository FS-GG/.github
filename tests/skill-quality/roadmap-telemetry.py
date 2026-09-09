#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / ".claude/skills/work-roadmap/scripts"))
SPEC = importlib.util.spec_from_file_location(
    "roadmap_telemetry", ROOT / ".claude/skills/work-roadmap/scripts/roadmap-telemetry.py"
)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class RoadmapTelemetryTests(unittest.TestCase):
    def config(self, root: pathlib.Path) -> MODULE.HostConfig:
        config_path = root / "telemetry.json"
        config_path.write_text("{}", encoding="utf-8")
        config_path.chmod(0o600)
        store = root / "store"
        store.mkdir(mode=0o700)
        return MODULE.HostConfig(config_path, store, "engine")

    def test_native_dispatch_is_expected_started_terminal_and_usage_stays_unsupported(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            batches = []
            original_run = MODULE.subprocess.run

            def fake_run(command, **kwargs):
                if "publish" in command:
                    batches.append(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text(encoding="utf-8")))
                    return subprocess.CompletedProcess(command, 0, "{}", "")
                return subprocess.CompletedProcess(command, 0, "{}", "")

            begin_args = MODULE.parser().parse_args([
                "begin", "--feature", "GS2-08", "--item", "GS2-08.3", "--attempt", "a1",
                "--model", "gpt-5.6-sol", "--effort", "medium",
            ])
            try:
                MODULE.subprocess.run = fake_run
                expected = MODULE.begin(config, begin_args)
                token = expected["token"]
                MODULE.started(config, MODULE.parser().parse_args(["started", "--token", token, "--native-id", "agent-1"]))
                terminal = MODULE.finish(config, MODULE.parser().parse_args([
                    "finish", "--token", token, "--outcome", "completed",
                ]))
            finally:
                MODULE.subprocess.run = original_run

            kinds = [event["kind"] for batch in batches for event in batch["events"]]
            self.assertIn("expected-dispatch", kinds)
            self.assertIn("runtime-admission", kinds)
            self.assertIn("runtime-start", kinds)
            self.assertIn("runtime-terminal", kinds)
            gaps = [event["code"] for batch in batches for event in batch["events"] if event["kind"] == "runtime-gap"]
            self.assertIn("native-collaboration-usage-unsupported", gaps)
            admission = next(event for batch in batches for event in batch["events"] if event["kind"] == "runtime-admission")
            self.assertEqual((admission["featureId"], admission["itemId"], admission["attemptId"], admission["requestedModel"], admission["requestedEffort"]),
                             ("GS2-08", "GS2-08.3", "a1", "gpt-5.6-sol", "medium"))
            self.assertEqual((terminal["status"], terminal["coverage"], terminal["drain"]),
                             ("terminal", "native-collaboration-usage-unsupported", "complete"))

    def test_private_host_config_is_discovered_without_embedding_an_instance_in_source(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = pathlib.Path(scratch)
            store = root / "store"
            store.mkdir(mode=0o700)
            config = root / "telemetry.json"
            config.write_text(json.dumps({
                "schema": "fsgg.telemetry.host-config/1", "storeRoot": str(store), "engine": "engine",
            }), encoding="utf-8")
            config.chmod(0o600)
            previous = os.environ.get("FSGG_TELEMETRY_CONFIG")
            try:
                os.environ["FSGG_TELEMETRY_CONFIG"] = str(config)
                discovered = MODULE.discover_config()
                self.assertEqual((discovered.store_root, discovered.engine), (store, "engine"))
                config.chmod(0o644)
                with self.assertRaises(MODULE.ConfigurationError):
                    MODULE.discover_config()
            finally:
                if previous is None:
                    os.environ.pop("FSGG_TELEMETRY_CONFIG", None)
                else:
                    os.environ["FSGG_TELEMETRY_CONFIG"] = previous

    def test_child_requires_and_records_parent_lineage(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            batches = []
            original_run = MODULE.subprocess.run
            MODULE.subprocess.run = lambda command, **kwargs: (batches.append(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text(encoding="utf-8"))) or subprocess.CompletedProcess(command, 0, "{}", "")) if "publish" in command else subprocess.CompletedProcess(command, 0, "{}", "")
            try:
                parent = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "I", "--attempt", "parent", "--model", "m", "--effort", "e",
                ]))
                MODULE.started(config, MODULE.parser().parse_args(["started", "--token", parent["token"], "--native-id", "parent-agent"]))
                child = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "I", "--attempt", "child", "--parent-attempt", "parent",
                    "--parent-token", parent["token"], "--relation", "child", "--model", "m", "--effort", "e",
                ]))
            finally:
                MODULE.subprocess.run = original_run
            expected = [event for batch in batches for event in batch["events"] if event["kind"] == "expected-dispatch"][-1]
            relation = [event for batch in batches for event in batch["events"] if event["kind"] == "parent-child"][-1]
            self.assertEqual(expected["relation"], "child")
            self.assertIsNotNone(expected["parentDispatchId"])
            self.assertEqual((relation["parentId"], relation["childId"]), ("parent", "child"))
            self.assertEqual(child["status"], "expected")

    def test_dispatch_token_cannot_escape_private_state_directory(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            with self.assertRaises(MODULE.ConfigurationError):
                MODULE.read_state(config, "../outside")


if __name__ == "__main__":
    unittest.main()
