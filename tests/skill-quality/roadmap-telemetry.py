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
from unittest import mock


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
    def test_every_closed_unified_item_updates_the_only_profile_roadmap(self):
        agent_skill = (ROOT / ".agents/skills/work-unified-roadmap/SKILL.md").read_text(encoding="utf-8")
        claude_skill = (ROOT / ".claude/skills/work-unified-roadmap/SKILL.md").read_text(encoding="utf-8")
        roadmap = (ROOT / "docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md").read_text(encoding="utf-8")
        profile = (ROOT / "profile/README.md").read_text(encoding="utf-8")

        self.assertEqual(agent_skill, claude_skill)
        for text in (agent_skill, roadmap):
            self.assertIn("every Unified Roadmap item", text)
            self.assertIn("Closed", text)
            self.assertIn("Done", text)
        self.assertIn("do not\nselect the next roadmap item", agent_skill)
        self.assertIn("## 0. Current progress report", roadmap)

        current_note = profile.split("> [!NOTE]", 1)[1].split("\n\n", 1)[0]
        self.assertIn("FS-GG Unified Development Roadmap", current_note)
        self.assertIn("#0-current-progress-report", current_note)
        self.assertNotIn("development-master.md", current_note)
        self.assertNotIn("github-substrate-v2-roadmap.md", current_note)

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

    def test_workspace_association_uses_workspace_submit_and_private_spool_state(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = pathlib.Path(scratch)
            spool = root / "spool"
            spool.mkdir(mode=0o700)
            config_path = root / "telemetry.json"
            config_path.write_text(json.dumps({
                "schema": "fsgg.telemetry.workspace-config/1", "engine": "engine",
                "associations": [{"workspaceId": "workspace-a", "producerId": "producer-a",
                                  "streamId": "runtime", "repositories": ["FS-GG/.github"],
                                  "destination": {"kind": "remote", "endpoint": "https://example.test/",
                                                  "credentialReference": "main", "spoolRoot": str(spool)}}],
                "retiredAssociations": [],
            }), encoding="utf-8")
            config_path.chmod(0o600)
            binding = {"schema":"fsgg.telemetry.workspace-binding/1","configPath":str(config_path),
                       "repository":"FS-GG/.github","producerId":"producer-a","bindingDigest":"a"*64,
                       "destination":"remote","privateStateRoot":str(spool)}
            with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_CONFIG": str(config_path),
                                              "FSGG_TELEMETRY_REPOSITORY": "FS-GG/.github"}, clear=False), \
                 mock.patch("fsgg_telemetry_defaults.subprocess.run", return_value=subprocess.CompletedProcess([],0,json.dumps(binding),"")):
                    config = MODULE.discover_config()
            self.assertTrue(config.workspace)
            self.assertEqual((config.store_root, config.repository), (spool, "FS-GG/.github"))
            commands = []
            state = {"sequence": 0, "invocationId": "invocation-a", "producerStream": "roadmap",
                     "associationProducer": config.producer, "associationDigest": config.binding_digest}
            with mock.patch.object(MODULE.subprocess, "run", side_effect=lambda command, **_: commands.append(command) or subprocess.CompletedProcess(command, 0, "{}", "")):
                MODULE.publish(config, state, [])
            self.assertEqual(commands[0][:4], ["engine", "telemetry", "workspace", "status"])
            self.assertEqual(commands[1][:4], ["engine", "telemetry", "workspace", "submit"])
            self.assertEqual(commands[1][commands[1].index("--repository") + 1], "FS-GG/.github")
            self.assertEqual(commands[1][commands[1].index("--producer") + 1], "producer-a")

    def test_workspace_token_is_fenced_after_prospective_cutover(self):
        with tempfile.TemporaryDirectory() as scratch:
            root=pathlib.Path(scratch)
            store=root/"state"; store.mkdir(mode=0o700)
            token="a"*32
            config=MODULE.HostConfig(root/"telemetry.json",store,"engine","FS-GG/.github",True,"producer-b","b"*64)
            directory=store/"orchestrator-dispatches"; directory.mkdir(mode=0o700)
            path=directory/f"{token}.json"
            path.write_text(json.dumps({"schema":MODULE.STATE_SCHEMA,"token":token,"associationProducer":"producer-a","associationDigest":"a"*64}),encoding="utf-8")
            path.chmod(0o600)
            with self.assertRaisesRegex(MODULE.ConfigurationError,"retired workspace association"):
                MODULE.read_state(config,token)

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

    def test_private_post_terminal_review_and_activity_hooks_are_closed(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = pathlib.Path(scratch)
            config = self.config(root)
            batches = []
            original_run = MODULE.subprocess.run

            def fake_run(command, **kwargs):
                if "publish" in command:
                    batches.append(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text(encoding="utf-8")))
                return subprocess.CompletedProcess(command, 0, "{}", "")

            review_path = root / "review.json"
            review_path.write_text(json.dumps({
                "schema": MODULE.REVIEW_SCHEMA, "revision": 1, "outcomeSynopsis": "Delivered",
                "wentWell": ["Focused tests"], "problems": [], "avoidableDelayOrRework": [],
                "processObservations": ["Routine route held"], "remainingRisks": [],
                "concreteImprovements": ["Keep the focused gate"], "evidence": [],
                "evidenceCoverage": "unknown", "populationCoverage": "complete", "confidence": "medium",
                "reviewerModel": "gpt-5", "reviewerEffort": "medium", "reviewedAt": "2026-09-09T09:00:00Z",
                "durationSeconds": 20,
            }), encoding="utf-8")
            review_path.chmod(0o600)
            try:
                MODULE.subprocess.run = fake_run
                result = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "I", "--attempt", "a1", "--model", "m", "--effort", "e",
                ]))
                token = result["token"]
                MODULE.started(config, MODULE.parser().parse_args(["started", "--token", token, "--native-id", "agent-1"]))
                with self.assertRaisesRegex(MODULE.ConfigurationError, "terminal"):
                    MODULE.review(config, MODULE.parser().parse_args(["review", "--token", token, "--scope", "attempt", "--input", str(review_path)]))
                MODULE.finish(config, MODULE.parser().parse_args(["finish", "--token", token, "--outcome", "completed"]))
                recorded = MODULE.review(config, MODULE.parser().parse_args(["review", "--token", token, "--scope", "attempt", "--input", str(review_path)]))
            finally:
                MODULE.subprocess.run = original_run
            review = [event for batch in batches for event in batch["events"] if event["kind"] == "process-review"]
            self.assertEqual(len(review), 1)
            self.assertEqual((review[0]["scope"], review[0]["attemptId"], review[0]["revision"]), ("attempt", "a1", 1))
            self.assertNotIn("schema", review[0])
            self.assertEqual(recorded["status"], "recorded")
            self.assertIn("dashboardPublication", recorded)

            review_path.write_text(json.dumps({"schema": MODULE.REVIEW_SCHEMA, "revision": 1}), encoding="utf-8")
            with self.assertRaisesRegex(MODULE.ConfigurationError, "exact"):
                MODULE.review(config, MODULE.parser().parse_args(["review", "--token", token, "--scope", "attempt", "--input", str(review_path)]))

    def test_dashboard_hook_requires_successful_completed_root_drain(self):
        base={"schema":MODULE.STATE_SCHEMA,"token":"a"*32,"phase":"started","sequence":0,"itemId":"I","invocationId":"v","nativeId":"agent","relation":"root"}
        args=MODULE.parser().parse_args(["finish","--token","a"*32,"--outcome","completed"])
        for relation,outcome,drain,expected in (("root","completed",0,True),("root","failed",0,False),("child","completed",0,False),("root","completed",1,False)):
            state={**base,"relation":relation}
            args.outcome=outcome
            with mock.patch.object(MODULE,"read_state",return_value=state),mock.patch.object(MODULE,"publish"),mock.patch.object(MODULE,"save_state"),mock.patch.object(MODULE.subprocess,"run",return_value=subprocess.CompletedProcess([],drain,"","")),mock.patch.object(MODULE,"refresh_dashboard",return_value={"status":"observed"}) as refresh:
                result=MODULE.finish(mock.Mock(),args)
            self.assertEqual("dashboardPublication" in result,expected)
            self.assertEqual(refresh.call_count,1 if expected else 0)

    def test_post_terminal_root_correction_drain_hooks_but_preterminal_and_child_do_not(self):
        config=mock.Mock(); value={"kind":"complication"}
        with mock.patch.object(MODULE,"publish"),mock.patch.object(MODULE,"save_state"),mock.patch.object(MODULE.subprocess,"run",return_value=subprocess.CompletedProcess([],0,"","")),mock.patch.object(MODULE,"refresh_dashboard",return_value={"status":"observed"}) as refresh:
            terminal=MODULE.record_event(config,{"phase":"terminal","relation":"root"},value)
            started=MODULE.record_event(config,{"phase":"started","relation":"root"},value)
            child=MODULE.record_event(config,{"phase":"terminal","relation":"child"},value)
        self.assertIn("dashboardPublication",terminal); self.assertNotIn("dashboardPublication",started); self.assertNotIn("dashboardPublication",child); self.assertEqual(refresh.call_count,1)


if __name__ == "__main__":
    unittest.main()
