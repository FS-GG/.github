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
DEFAULTS = sys.modules["fsgg_telemetry_defaults"]


class RoadmapTelemetryTests(unittest.TestCase):
    def test_repository_environment_precedence_avoids_checkout_discovery(self):
        with mock.patch.dict(os.environ, {
            "FSGG_TELEMETRY_REPOSITORY": "FS-GG/explicit",
            "GITHUB_REPOSITORY": "FS-GG/actions",
        }, clear=True), mock.patch.object(DEFAULTS.subprocess, "run") as invoked:
            self.assertEqual(DEFAULTS.workspace_repository(), "FS-GG/explicit")
            invoked.assert_not_called()
        with mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": "FS-GG/actions"}, clear=True), \
             mock.patch.object(DEFAULTS.subprocess, "run") as invoked:
            self.assertEqual(DEFAULTS.workspace_repository(), "FS-GG/actions")
            invoked.assert_not_called()

    def test_canonical_github_origin_forms_are_discovered_with_bounded_git_call(self):
        cases = (
            "https://github.com/FS-GG/.github.git",
            "git@github.com:FS-GG/.github.git",
            "ssh://git@github.com/FS-GG/.github.git",
        )
        for origin in cases:
            with self.subTest(origin=origin), mock.patch.object(
                DEFAULTS.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, origin + "\n", "")
            ) as invoked:
                self.assertEqual(DEFAULTS.discover_checkout_repository(pathlib.Path("checkout")), "FS-GG/.github")
                self.assertEqual(invoked.call_args.args[0], [
                    "git", "config", "--local", "--get-all", "remote.origin.url"
                ])
                self.assertEqual(invoked.call_args.kwargs, {
                    "cwd": pathlib.Path("checkout"), "capture_output": True, "text": True,
                    "timeout": 5, "check": False,
                })

    def test_detached_linked_worktree_uses_shared_local_origin(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = pathlib.Path(scratch) / "repository"
            linked = pathlib.Path(scratch) / "linked"
            subprocess.run(["git", "init", "-q", str(root)], check=True)
            subprocess.run(["git", "-C", str(root), "config", "user.email", "test@example.invalid"], check=True)
            subprocess.run(["git", "-C", str(root), "config", "user.name", "Telemetry Test"], check=True)
            subprocess.run(["git", "-C", str(root), "remote", "add", "origin", "git@github.com:FS-GG/worktree.git"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "--allow-empty", "-qm", "fixture"], check=True)
            subprocess.run(["git", "-C", str(root), "worktree", "add", "--detach", "-q", str(linked), "HEAD"], check=True)
            self.assertEqual(DEFAULTS.discover_checkout_repository(linked), "FS-GG/worktree")

    def test_malformed_non_github_ambiguous_and_credential_origins_refuse_without_echo(self):
        rejected = (
            "http://github.com/FS-GG/repo.git",
            "https://example.com/FS-GG/repo.git",
            "https://token-value@github.com/FS-GG/repo.git",
            "ssh://root@github.com/FS-GG/repo.git",
            "https://github.com/FS-GG/repo/extra.git",
            "git@github.com:FS-GG/../repo.git",
        )
        for origin in rejected:
            with self.subTest(origin=origin), self.assertRaises(DEFAULTS.ConfigurationError) as refused:
                DEFAULTS.canonical_github_repository(origin)
            self.assertNotIn("token-value", str(refused.exception))
        for output in ("", "https://github.com/FS-GG/one.git\nhttps://github.com/FS-GG/two.git\n"):
            with mock.patch.object(
                DEFAULTS.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, output, "")
            ), self.assertRaisesRegex(DEFAULTS.ConfigurationError, "requires one local origin"):
                DEFAULTS.discover_checkout_repository()

    def test_missing_git_timeout_and_invalid_environment_refuse_without_fallback(self):
        for error in (FileNotFoundError("git"), subprocess.TimeoutExpired(["git"], 5)):
            with mock.patch.object(DEFAULTS.subprocess, "run", side_effect=error), \
                 self.assertRaisesRegex(DEFAULTS.ConfigurationError, "discovery unavailable"):
                DEFAULTS.discover_checkout_repository()
        with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_REPOSITORY": "bad", "GITHUB_REPOSITORY": "FS-GG/good"}, clear=True), \
             mock.patch.object(DEFAULTS, "discover_checkout_repository") as discovery, \
             self.assertRaisesRegex(DEFAULTS.ConfigurationError, "FSGG_TELEMETRY_REPOSITORY"):
            DEFAULTS.workspace_repository()
        discovery.assert_not_called()

    def test_discovered_repository_is_the_only_origin_value_sent_to_workspace_engine(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = pathlib.Path(scratch)
            spool = root / "spool"
            spool.mkdir(mode=0o700)
            config_path = root / "telemetry.json"
            config_path.write_text(json.dumps({
                "schema": "fsgg.telemetry.workspace-config/1", "engine": "engine",
                "associations": [{"workspaceId": "workspace", "producerId": "producer",
                                  "streamId": "runtime", "repositories": ["FS-GG/discovered"],
                                  "destination": {"kind": "remote", "endpoint": "https://example.test/",
                                                  "credentialReference": "main", "spoolRoot": str(spool)}}],
                "retiredAssociations": [],
            }), encoding="utf-8")
            config_path.chmod(0o600)
            binding = {
                "schema": "fsgg.telemetry.workspace-binding/1", "configPath": str(config_path),
                "repository": "FS-GG/discovered", "producerId": "producer", "bindingDigest": "a" * 64,
                "destination": "remote", "privateStateRoot": str(spool),
            }
            calls = []
            def execute(command, **kwargs):
                calls.append((command, kwargs))
                if command[0] == "git":
                    return subprocess.CompletedProcess(command, 0, "https://github.com/FS-GG/discovered.git\n", "")
                return subprocess.CompletedProcess(command, 0, json.dumps(binding), "")
            with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_CONFIG": str(config_path)}, clear=True), \
                 mock.patch.object(DEFAULTS.subprocess, "run", side_effect=execute):
                discovered = MODULE.discover_config()
            self.assertEqual(discovered.repository, "FS-GG/discovered")
            engine = calls[1][0]
            self.assertEqual(engine[engine.index("--repository") + 1], "FS-GG/discovered")
            self.assertNotIn("github.com", " ".join(engine))

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

    def test_native_child_usage_is_joined_once_and_late_correction_revises_it(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            batches = []
            def fake_run(command, **kwargs):
                if "publish" in command:
                    batches.append(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text()))
                return subprocess.CompletedProcess(command, 0, "{}", "")
            def begin(attempt, *extra):
                return MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "F.1", "--attempt", attempt,
                    "--model", "gpt-6-astra", "--effort", "high", *extra]))["token"]
            def start(token, native):
                MODULE.started(config, MODULE.parser().parse_args(["started", "--token", token, "--native-id", native]))
            def finish(token):
                return MODULE.finish(config, MODULE.parser().parse_args([
                    "finish", "--token", token, "--outcome", "completed"]))
            parent_thread = "11111111-1111-4111-8111-111111111111"
            native_thread = "22222222-2222-4222-8222-222222222222"
            turn = "33333333-3333-4333-8333-333333333333"
            usage = {"input_tokens": 100, "cached_input_tokens": 60, "output_tokens": 20,
                     "reasoning_output_tokens": 5, "total_tokens": 120}
            def observation():
                return {"threadId": native_thread, "model": "gpt-6-astra", "effort": "high",
                        "complete": True, "turns": [{"turnId": turn, "turnSequence": 1, "usage": dict(usage)}]}
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": parent_thread}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=lambda *args: observation()) as collector:
                root = begin("root")
                start(root, "root")
                child = begin("child", "--parent-token", root, "--relation", "child")
                start(child, "child_1")
                self.assertEqual(finish(child)["coverage"], "native-collaboration-usage-complete")
                MODULE.usage_reconcile(config, MODULE.parser().parse_args(["usage-reconcile", "--token", child]))
                usage["input_tokens"] = 105
                usage["total_tokens"] = 125
                MODULE.usage_reconcile(config, MODULE.parser().parse_args(["usage-reconcile", "--token", child]))
                self.assertEqual(collector.call_args.args, (parent_thread, "child_1"))
            facts = [fact for batch in batches for fact in batch["events"] if fact["kind"] == "runtime-turn-usage"]
            self.assertEqual(len(facts), 2)
            self.assertEqual([fact["revision"] for fact in facts], [0, 1])
            self.assertEqual([fact["total"] for fact in facts], [120, 125])
            self.assertEqual([fact["input"] for fact in facts], [100, 105])
            self.assertEqual(facts[0]["identity"], facts[1]["identity"])
            self.assertEqual(facts[0]["invocationId"], MODULE.read_state(config, child)["invocationId"])
            self.assertEqual(len([fact for batch in batches for fact in batch["events"]
                                  if fact["kind"] == "runtime-start" and fact["phase"] == "thread"]), 1)
            self.assertNotIn("native-collaboration-usage-unsupported", [fact["code"] for batch in batches
                              for fact in batch["events"] if fact["kind"] == "runtime-gap"
                              and fact["invocationId"] == MODULE.read_state(config, child)["invocationId"]])

    def test_followup_excludes_all_prior_native_turns(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            facts = []
            def fake_run(command, **kwargs):
                if "publish" in command:
                    facts.extend(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text())["events"])
                return subprocess.CompletedProcess(command, 0, "{}", "")
            parent_thread = "11111111-1111-4111-8111-111111111111"
            turns = ["33333333-3333-4333-8333-333333333333", "44444444-4444-4444-8444-444444444444"]
            def usage(*_):
                return {"threadId": "22222222-2222-4222-8222-222222222222", "allTurnIds": turns[:],
                        "model": "gpt-6-astra", "effort": "high", "complete": True,
                        "turns": [{"turnId": turn, "turnSequence": index + 1,
                                   "usage": {"input_tokens": 10, "cached_input_tokens": 5,
                                             "output_tokens": 2, "reasoning_output_tokens": 1,
                                             "total_tokens": 12}}
                                  for index, turn in enumerate(turns)]}
            def begin(attempt, *extra):
                return MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "F.1", "--attempt", attempt,
                    "--model", "gpt-6-astra", "--effort", "high", *extra]))["token"]
            def start(token, native):
                MODULE.started(config, MODULE.parser().parse_args(["started", "--token", token, "--native-id", native]))
            def finish(token):
                MODULE.finish(config, MODULE.parser().parse_args(["finish", "--token", token, "--outcome", "completed"]))
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": parent_thread}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=usage):
                root = begin("root")
                start(root, "root")
                child = begin("child", "--parent-token", root, "--relation", "child")
                start(child, "worker")
                turns.pop()
                finish(child)
                followup = begin("followup", "--parent-token", child, "--relation", "follow-up")
                self.assertEqual(MODULE.read_state(config, followup)["baselineTurnIds"], turns)
                start(followup, "worker")
                turns.append("44444444-4444-4444-8444-444444444444")
                finish(followup)
            usage_facts = [fact for fact in facts if fact["kind"] == "runtime-turn-usage"]
            self.assertEqual([fact["turnId"] for fact in usage_facts], turns)
            self.assertNotEqual(usage_facts[0]["invocationId"], usage_facts[1]["invocationId"])

    def test_rejected_native_usage_does_not_become_a_published_ledger_entry(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            rejected = False
            revisions = []
            def fake_run(command, **kwargs):
                nonlocal rejected
                if "publish" in command:
                    batch = json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text())
                    for fact in batch["events"]:
                        if fact["kind"] == "runtime-turn-usage":
                            revisions.append(fact["revision"])
                            if not rejected:
                                rejected = True
                                return subprocess.CompletedProcess(command, 1, "", "invalid-request: fixture")
                return subprocess.CompletedProcess(command, 0, "{}", "")
            def begin(attempt, *extra):
                return MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "F.1", "--attempt", attempt,
                    "--model", "gpt-6-astra", "--effort", "high", *extra]))["token"]
            def start(token, native):
                MODULE.started(config, MODULE.parser().parse_args(["started", "--token", token, "--native-id", native]))
            native = {"threadId": "22222222-2222-4222-8222-222222222222", "complete": True,
                      "model": "gpt-6-astra", "effort": "high", "turns": [{
                          "turnId": "33333333-3333-4333-8333-333333333333", "turnSequence": 1,
                          "usage": {"input_tokens": 10, "cached_input_tokens": 5,
                                    "output_tokens": 2, "reasoning_output_tokens": 1,
                                    "total_tokens": 12}}]}
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": "11111111-1111-4111-8111-111111111111"}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", return_value=native):
                root = begin("root")
                start(root, "root")
                child = begin("child", "--parent-token", root, "--relation", "child")
                start(child, "worker")
                first = MODULE.finish(config, MODULE.parser().parse_args([
                    "finish", "--token", child, "--outcome", "completed"]))
                self.assertEqual(first["coverage"], "native-collaboration-usage-unknown")
                state = MODULE.read_state(config, child)
                self.assertNotIn("usageIntent", state)
                self.assertEqual(state["usageLedger"], {})
                second = MODULE.usage_reconcile(config, MODULE.parser().parse_args([
                    "usage-reconcile", "--token", child]))
                self.assertEqual(second["coverage"], "native-collaboration-usage-complete")
                self.assertEqual(revisions, [0, 0])

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
                     "associationProducer": config.producer, "associationDigest": config.binding_digest,
                     "token": "a" * 32, "phase": "expected"}
            with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_CREDENTIAL_MAIN": "secret-is-not-observed"}, clear=False), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=lambda command, **_: commands.append(command) or subprocess.CompletedProcess(command, 0, "{}", "")):
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

    def test_terminal_parent_admits_only_exact_same_item_follow_up_and_retry(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))

            def fake_run(command, **_):
                return subprocess.CompletedProcess(command, 0, "{}", "")

            def begin(attempt, *, item="I", parent_token=None, relation="root"):
                arguments = [
                    "begin", "--feature", "F", "--item", item, "--attempt", attempt,
                    "--model", "m", "--effort", "e", "--relation", relation,
                ]
                if parent_token is not None:
                    arguments.extend(["--parent-token", parent_token, "--parent-attempt", "parent"])
                return MODULE.begin(config, MODULE.parser().parse_args(arguments))

            def terminal_parent(attempt="parent", item="I"):
                result = begin(attempt, item=item)
                token = result["token"]
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", token, "--native-id", f"agent-{attempt}",
                ]))
                MODULE.finish(config, MODULE.parser().parse_args([
                    "finish", "--token", token, "--outcome", "completed",
                ]))
                return token

            with mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "refresh_dashboard", return_value={"status": "observed"}):
                parent = terminal_parent()
                follow_up = begin("follow-up-a1", parent_token=parent, relation="follow-up")
                retry = begin("follow-up-a1", parent_token=parent, relation="follow-up")
                self.assertEqual(retry["token"], follow_up["token"])

                state = MODULE.read_state(config, follow_up["token"])
                parent_state = MODULE.read_state(config, parent)
                self.assertEqual(state["relation"], "follow-up")
                self.assertEqual(state["parentDispatchId"], parent_state["dispatchId"])
                self.assertEqual(state["parentInvocationId"], parent_state["invocationId"])
                self.assertEqual(state["rootInvocationId"], parent_state["rootInvocationId"])

                with self.assertRaisesRegex(MODULE.ConfigurationError, "started before a child"):
                    begin("terminal-child", parent_token=parent, relation="child")

                unstarted = begin("unstarted-parent")
                with self.assertRaisesRegex(MODULE.ConfigurationError, "started before a child"):
                    begin("unstarted-follow-up", parent_token=unstarted["token"], relation="follow-up")

                other_item = terminal_parent("other-parent", "OTHER")
                with self.assertRaisesRegex(MODULE.ConfigurationError, "share the item identity"):
                    begin("wrong-item-follow-up", parent_token=other_item, relation="follow-up")

                other_parent = terminal_parent("second-parent")
                with self.assertRaisesRegex(MODULE.ConfigurationError, "retry differs"):
                    begin("follow-up-a1", parent_token=other_parent, relation="follow-up")

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
            config=mock.Mock(workspace=False)
            with mock.patch.object(MODULE,"read_state",return_value=state),mock.patch.object(MODULE,"publish"),mock.patch.object(MODULE,"save_state"),mock.patch.object(MODULE.subprocess,"run",return_value=subprocess.CompletedProcess([],drain,"","")),mock.patch.object(MODULE,"refresh_dashboard",return_value={"status":"observed"}) as refresh:
                result=MODULE.finish(config,args)
            self.assertEqual("dashboardPublication" in result,expected)
            self.assertEqual(refresh.call_count,1 if expected else 0)

    def test_post_terminal_root_correction_drain_hooks_but_preterminal_and_child_do_not(self):
        config=mock.Mock(workspace=False); value={"kind":"complication","identity":"complication-a"}
        with mock.patch.object(MODULE,"publish"),mock.patch.object(MODULE,"save_state"),mock.patch.object(MODULE.subprocess,"run",return_value=subprocess.CompletedProcess([],0,"","")),mock.patch.object(MODULE,"refresh_dashboard",return_value={"status":"observed"}) as refresh:
            terminal=MODULE.record_event(config,{"phase":"terminal","relation":"root"},value)
            started=MODULE.record_event(config,{"phase":"started","relation":"root"},value)
            child=MODULE.record_event(config,{"phase":"terminal","relation":"child"},value)
        self.assertIn("dashboardPublication",terminal); self.assertNotIn("dashboardPublication",started); self.assertNotIn("dashboardPublication",child); self.assertEqual(refresh.call_count,1)

    def test_begin_start_and_finish_replay_the_exact_durable_batch_after_unknown_delivery(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            publications = []
            publish_number = 0

            def unreliable(command, **_):
                nonlocal publish_number
                if "publish" in command:
                    publish_number += 1
                    source = pathlib.Path(command[command.index("--input") + 1])
                    publications.append(source.read_bytes())
                    code = 1 if publish_number in {1, 3, 5} else 0
                    return subprocess.CompletedProcess(command, code, "", "delivery outcome unknown")
                if "drain" in command:
                    return subprocess.CompletedProcess(command, 0, "", "")
                return subprocess.CompletedProcess(command, 0, "{}", "")

            begin_args = MODULE.parser().parse_args([
                "begin", "--feature", "UTEL", "--item", "RECOVERY", "--attempt", "a1",
                "--model", "gpt-5.6-sol", "--effort", "medium",
            ])
            with mock.patch.object(MODULE.subprocess, "run", side_effect=unreliable):
                with self.assertRaisesRegex(MODULE.ConfigurationError, "outcome unknown"):
                    MODULE.begin(config, begin_args)
                paths = list((config.store_root / "orchestrator-dispatches").glob("*.json"))
                self.assertEqual(len(paths), 1)
                token = paths[0].stem
                pending = MODULE.read_state(config, token)
                self.assertEqual((pending["phase"], pending["sequence"], pending["pendingPublication"]["operation"]),
                                 ("begin-pending", 1, "begin"))
                self.assertEqual(MODULE.begin(config, begin_args)["token"], token)
                changed_begin = MODULE.parser().parse_args([
                    "begin", "--feature", "UTEL", "--item", "RECOVERY", "--attempt", "a1",
                    "--model", "gpt-5.6-sol", "--effort", "medium", "--late-after-seconds", "1",
                ])
                with self.assertRaisesRegex(MODULE.ConfigurationError, "retry differs"):
                    MODULE.begin(config, changed_begin)

                started_args = MODULE.parser().parse_args([
                    "started", "--token", token, "--native-id", "root/worker",
                ])
                with self.assertRaisesRegex(MODULE.ConfigurationError, "outcome unknown"):
                    MODULE.started(config, started_args)
                pending = MODULE.read_state(config, token)
                self.assertEqual((pending["phase"], pending["sequence"], pending["nativeId"]),
                                 ("start-pending", 2, "root/worker"))
                MODULE.started(config, started_args)

                finish_args = MODULE.parser().parse_args([
                    "finish", "--token", token, "--outcome", "completed",
                ])
                with self.assertRaisesRegex(MODULE.ConfigurationError, "outcome unknown"):
                    MODULE.finish(config, finish_args)
                pending = MODULE.read_state(config, token)
                self.assertEqual((pending["phase"], pending["sequence"], pending["outcome"], pending["exitCode"]),
                                 ("terminal-pending", 3, "completed", 0))
                changed = MODULE.parser().parse_args([
                    "finish", "--token", token, "--outcome", "failed",
                ])
                with self.assertRaisesRegex(MODULE.ConfigurationError, "differs from the durable terminal intent"):
                    MODULE.finish(config, changed)
                result = MODULE.finish(config, finish_args)
                self.assertEqual((result["status"], result["drain"]), ("terminal", "complete"))
                publish_count = publish_number
                self.assertEqual(MODULE.finish(config, finish_args)["status"], "terminal")
                self.assertEqual(publish_number, publish_count)
                with self.assertRaisesRegex(MODULE.ConfigurationError, "already progressed"):
                    MODULE.begin(config, begin_args)

            self.assertEqual(publications[0], publications[1])
            self.assertEqual(publications[2], publications[3])
            self.assertEqual(publications[4], publications[5])
            settled = MODULE.read_state(config, token)
            self.assertEqual((settled["phase"], settled["sequence"]), ("terminal", 3))
            self.assertNotIn("pendingPublication", settled)

    def test_definitive_invalid_request_releases_only_the_rejected_publication(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            token = "a" * 32
            state = {
                "schema": MODULE.STATE_SCHEMA,
                "token": token,
                "phase": "started",
                "sequence": 2,
                "itemId": "RECOVERY",
                "invocationId": "invocation",
                "producerStream": "roadmap-orchestrator",
                "associationProducer": "producer",
                "associationDigest": "b" * 64,
            }
            MODULE.save_state(config, state)
            rejected = subprocess.CompletedProcess([], 1, "", "telemetry workspace: invalid-request")
            with mock.patch.object(MODULE.subprocess, "run", return_value=rejected), \
                 self.assertRaisesRegex(MODULE.ConfigurationError, "invalid-request"):
                MODULE.publish(config, state, [{"kind": "complication", "identity": "legacy"}])

            recovered = MODULE.read_state(config, token)
            self.assertEqual((recovered["phase"], recovered["sequence"]), ("started", 2))
            self.assertNotIn("pendingPublication", recovered)

            unknown = subprocess.CompletedProcess([], 1, "", "delivery outcome unknown")
            with mock.patch.object(MODULE.subprocess, "run", return_value=unknown), \
                 self.assertRaisesRegex(MODULE.ConfigurationError, "outcome unknown"):
                MODULE.publish(config, recovered, [{"kind": "complication", "identity": "corrected"}])
            retained = MODULE.read_state(config, token)
            self.assertEqual(retained["sequence"], 3)
            self.assertEqual(retained["pendingPublication"]["batch"]["events"][0]["identity"], "corrected")

    def test_workspace_mutations_load_credentials_only_through_owner_controlled_client(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = pathlib.Path(scratch)
            helper = root / "fdev-telemetry"
            helper.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
            helper.chmod(0o755)
            config = DEFAULTS.HostConfig(
                root / "telemetry.json", root / "spool", "engine", "FS-GG/.github", True,
                "producer", "a" * 64, "main",
            )
            command = ["engine", "telemetry", "workspace", "submit", "--input", "private.json"]
            with mock.patch.dict(os.environ, {}, clear=True), \
                 mock.patch.object(DEFAULTS.shutil, "which", return_value=str(helper)):
                wrapped = DEFAULTS.workspace_mutation_command(config, command)
            self.assertEqual(wrapped, [str(helper.resolve()), "exec", *command])
            self.assertNotIn("secret", " ".join(wrapped).lower())
            with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_CREDENTIAL_MAIN": "private-value"}, clear=True), \
                 mock.patch.object(DEFAULTS.shutil, "which") as lookup:
                self.assertIs(DEFAULTS.workspace_mutation_command(config, command), command)
                lookup.assert_not_called()
            helper.chmod(0o775)
            with mock.patch.dict(os.environ, {}, clear=True), \
                 mock.patch.object(DEFAULTS.shutil, "which", return_value=str(helper)), \
                 self.assertRaisesRegex(DEFAULTS.ConfigurationError, "owner-controlled"):
                DEFAULTS.workspace_mutation_command(config, command)

    def test_workspace_credential_binding_must_be_exact_and_unambiguous(self):
        binding = {"producerId": "producer"}
        destination = {"credentialReference": "main"}
        association = {"producerId": "producer", "repositories": ["FS-GG/.github"], "destination": destination}
        self.assertEqual(
            DEFAULTS.workspace_credential_reference({"associations": [association]}, binding, "FS-GG/.github"),
            "main",
        )
        with self.assertRaisesRegex(DEFAULTS.ConfigurationError, "missing or ambiguous"):
            DEFAULTS.workspace_credential_reference({"associations": [association, association]}, binding, "FS-GG/.github")

    def test_legacy_terminal_state_replays_safely_only_with_its_derived_exit_code(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            state = {
                "schema": MODULE.STATE_SCHEMA, "token": "a" * 32, "phase": "terminal", "sequence": 3,
                "itemId": "ITEM", "invocationId": "invocation", "nativeId": "worker", "outcome": "completed",
                "relation": "root", "associationProducer": None, "associationDigest": None,
            }
            MODULE.save_state(config, state)
            args = MODULE.parser().parse_args(["finish", "--token", "a" * 32, "--outcome", "completed"])
            with mock.patch.object(MODULE.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "", "")), \
                 mock.patch.object(MODULE, "refresh_dashboard", return_value={"status": "observed"}):
                self.assertEqual(MODULE.finish(config, args)["status"], "terminal")
            self.assertEqual(MODULE.read_state(config, "a" * 32)["exitCode"], 0)
            explicit = MODULE.parser().parse_args([
                "finish", "--token", "a" * 32, "--outcome", "completed", "--exit-code", "7",
            ])
            with self.assertRaisesRegex(MODULE.ConfigurationError, "differs from the durable terminal intent"):
                MODULE.finish(config, explicit)


if __name__ == "__main__":
    unittest.main()
