#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import copy
import hashlib
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


def native_snapshot(thread, identifiers, turns, *, complete=True, provider="openai",
                    model="gpt-6-astra", effort="high", root_invocation="root-invocation",
                    invocation="invocation", parent_thread="11111111-1111-4111-8111-111111111111",
                    revision=0):
    observed = {row["turnId"] for row in turns}
    inventory = [{"turnId": turn, "turnSequence": sequence,
                  "status": "completed", "terminal": True,
                  "usageAvailable": turn in observed}
                 for sequence, turn in enumerate(identifiers, 1)]
    roster = [{key: row[key] for key in ("turnId", "turnSequence", "status", "terminal")}
              for row in inventory]
    roster_digest = hashlib.sha256(json.dumps(
        {"threadId": thread, "turnInventory": roster}, sort_keys=True,
        separators=(",", ":"), ensure_ascii=True).encode("ascii")).hexdigest()
    paging = [{"page": 1, "requestCursor": None, "nextCursor": None,
               "rowCount": len(inventory)}]
    captured_at = "2026-09-27T12:00:00Z"
    app_values = [
        {"id": 100, "result": {"data": [{"id": thread, "status": "completed"}], "nextCursor": None}},
        {"id": 200, "result": {"thread": {"id": thread, "modelProvider": provider,
          "model": model, "reasoningEffort": effort}}},
        {"id": 300, "result": {"data": [{"id": row["turnId"], "status": row["status"]}
                                            for row in inventory], "nextCursor": None}},
    ]
    requests = [
        {"id": 100, "method": "thread/list", "params": {"cursor": None, "limit": 100}},
        {"id": 200, "method": "thread/read", "params": {"threadId": thread, "includeTurns": False}},
        {"id": 300, "method": "thread/turns/list",
         "params": {"threadId": thread, "cursor": None, "limit": 100}},
    ]
    request_raw = [json.dumps(value, separators=(",", ":")).encode() + b"\n" for value in requests]
    app_raw = [json.dumps(value, separators=(",", ":")).encode() + b"\n" for value in app_values]
    rollout_raw = [json.dumps({"type": "token_usage_record", "payload": {
        "thread_id": thread, "turn_id": row["turnId"], "response_id": "fixture-response",
        "usage": row["usage"], "turn_token_usage": row["usage"]}},
        sort_keys=True, separators=(",", ":")).encode() + b"\n" for row in turns]
    source = hashlib.sha256()
    binding_document = {
        "schema": "fsgg.telemetry.native-inventory-source-binding/1",
        "producerIdentity": "fsgg-work-roadmap-native-collector/1",
        "capturedAt": captured_at,
        "hostSource": "codex-app-server:thread/turns/list",
        "rootInvocationId": root_invocation,
        "invocationId": invocation,
        "parentThreadId": parent_thread,
        "threadId": thread,
        "orderedTurnIds": list(identifiers),
        "revision": revision,
    }
    binding = json.dumps(binding_document, sort_keys=True, separators=(",", ":")).encode()
    app_chunks = [raw for pair in zip(request_raw, app_raw) for raw in pair]
    for raw in [binding, *app_chunks, *rollout_raw]:
        source.update(len(raw).to_bytes(8, "big"))
        source.update(raw)
    source_digest = source.hexdigest()
    records = lambda rows: [{"sha256": hashlib.sha256(raw).hexdigest(),
                             "bytesBase64": MODULE.base64.b64encode(raw).decode()} for raw in rows]
    app_records = [{"method": request["method"], "threadId": request["params"].get("threadId"),
                    "requestCursor": request["params"].get("cursor"),
                    "requestSha256": hashlib.sha256(request_bytes).hexdigest(),
                    "requestBytesBase64": MODULE.base64.b64encode(request_bytes).decode(),
                    "responseSha256": hashlib.sha256(response_bytes).hexdigest(),
                    "responseBytesBase64": MODULE.base64.b64encode(response_bytes).decode()}
                   for request, request_bytes, response_bytes in zip(requests, request_raw, app_raw)]
    return {"threadId": thread, "allTurnIds": list(identifiers), "turnInventory": inventory,
            "inventoryProvenance": "codex-app-server-thread-turns-list",
            "inventoryHostSource": "codex-app-server:thread/turns/list",
            "inventoryPaging": paging, "inventoryCapturedAt": captured_at,
            "inventoryRosterDigest": roster_digest, "inventorySourceDigest": source_digest,
            "usageProvenance": "codex-native-token-usage-record",
            "collectorProducer": "fsgg-work-roadmap-native-collector/1",
            "sourceBinding": {
                "schema": binding_document["schema"],
                "producerIdentity": binding_document["producerIdentity"],
                "sha256": hashlib.sha256(binding).hexdigest(),
                "bytesBase64": MODULE.base64.b64encode(binding).decode(),
            },
            "appServerResponses": app_records, "rolloutRecords": records(rollout_raw),
            "provider": provider,
            "providerProvenance": ("codex-app-server-thread.modelProvider"
                                   if provider is not None else None),
            "model": model, "effort": effort, "complete": complete, "turns": turns}


def bound_collector(snapshot_or_factory):
    def collect(parent_thread, _native_id, *, root_invocation_id, invocation_id, revision):
        source = snapshot_or_factory() if callable(snapshot_or_factory) else snapshot_or_factory
        snapshot = copy.deepcopy(source)
        document = {
            "schema": "fsgg.telemetry.native-inventory-source-binding/1",
            "producerIdentity": snapshot["collectorProducer"],
            "capturedAt": snapshot["inventoryCapturedAt"],
            "hostSource": snapshot["inventoryHostSource"],
            "rootInvocationId": root_invocation_id,
            "invocationId": invocation_id,
            "parentThreadId": parent_thread,
            "threadId": snapshot["threadId"],
            "orderedTurnIds": snapshot["allTurnIds"],
            "revision": revision,
        }
        binding = json.dumps(document, sort_keys=True, separators=(",", ":")).encode()
        snapshot["sourceBinding"] = {
            "schema": document["schema"], "producerIdentity": document["producerIdentity"],
            "sha256": hashlib.sha256(binding).hexdigest(),
            "bytesBase64": MODULE.base64.b64encode(binding).decode(),
        }
        digest = hashlib.sha256()
        chunks = [binding, *[MODULE.base64.b64decode(record[field])
                              for record in snapshot["appServerResponses"]
                              for field in ("requestBytesBase64", "responseBytesBase64")],
                  *[MODULE.base64.b64decode(record["bytesBase64"])
                    for record in snapshot["rolloutRecords"]]]
        for value in chunks:
            digest.update(len(value).to_bytes(8, "big")); digest.update(value)
        snapshot["inventorySourceDigest"] = digest.hexdigest()
        return snapshot
    return collect


def rewrite_source_binding(snapshot, **changes):
    document = json.loads(MODULE.base64.b64decode(snapshot["sourceBinding"]["bytesBase64"]))
    document.update(changes)
    binding = json.dumps(document, sort_keys=True, separators=(",", ":")).encode()
    snapshot["sourceBinding"]["sha256"] = hashlib.sha256(binding).hexdigest()
    snapshot["sourceBinding"]["bytesBase64"] = MODULE.base64.b64encode(binding).decode()
    digest = hashlib.sha256()
    chunks = [binding, *[MODULE.base64.b64decode(record[field])
                          for record in snapshot["appServerResponses"]
                          for field in ("requestBytesBase64", "responseBytesBase64")],
              *[MODULE.base64.b64decode(record["bytesBase64"])
                for record in snapshot["rolloutRecords"]]]
    for value in chunks:
        digest.update(len(value).to_bytes(8, "big")); digest.update(value)
    snapshot["inventorySourceDigest"] = digest.hexdigest()


class RoadmapTelemetryTests(unittest.TestCase):
    def test_native_source_binding_refuses_order_and_durable_identity_substitution(self):
        thread = "22222222-2222-4222-8222-222222222222"
        turn = "33333333-3333-4333-8333-333333333333"
        reordered = native_snapshot(thread, [turn], [], complete=False)
        rewrite_source_binding(reordered, orderedTurnIds=[])
        with self.assertRaisesRegex(MODULE.ConfigurationError, "source binding is malformed"):
            MODULE.native_snapshot(reordered)

        state = {
            "rootInvocationId": "root-invocation", "invocationId": "invocation",
            "hostParentThreadId": "11111111-1111-4111-8111-111111111111",
        }
        for field, changed in (("rootInvocationId", "foreign-root"),
                               ("invocationId", "foreign-invocation")):
            candidate = native_snapshot(thread, [turn], [], complete=False)
            rewrite_source_binding(candidate, **{field: changed})
            candidate = MODULE.native_snapshot(candidate)
            with self.subTest(field=field), self.assertRaisesRegex(
                    MODULE.ConfigurationError, "differs from durable dispatch identity"):
                MODULE._publish_turn_roster(None, state, candidate, [turn])

        revised = native_snapshot(thread, [turn], [], complete=False, revision=1)
        revised = MODULE.native_snapshot(revised)
        with self.assertRaisesRegex(MODULE.ConfigurationError, "differs from durable dispatch identity"):
            MODULE._publish_turn_roster(None, state, revised, [turn])

    def test_thread_list_status_is_not_a_turn_and_exact_terminal_cursor_is_required(self):
        thread = "22222222-2222-4222-8222-222222222222"
        turn = "33333333-3333-4333-8333-333333333333"
        snapshot = native_snapshot(thread, [turn], [], complete=False)
        # The fixture's thread/list row deliberately has the real Thread.status
        # shape; only the method-bound thread/turns/list row belongs to the roster.
        self.assertEqual(MODULE.native_snapshot(snapshot)["allTurnIds"], [turn])

        response = json.loads(MODULE.base64.b64decode(
            snapshot["appServerResponses"][2]["responseBytesBase64"]))
        response["result"]["nextCursor"] = "missing-page"
        raw = json.dumps(response, separators=(",", ":")).encode() + b"\n"
        snapshot["appServerResponses"][2]["responseSha256"] = hashlib.sha256(raw).hexdigest()
        snapshot["appServerResponses"][2]["responseBytesBase64"] = MODULE.base64.b64encode(raw).decode()
        binding = MODULE.base64.b64decode(snapshot["sourceBinding"]["bytesBase64"])
        digest = hashlib.sha256()
        for value in [binding, *[MODULE.base64.b64decode(record[field])
                                  for record in snapshot["appServerResponses"]
                                  for field in ("requestBytesBase64", "responseBytesBase64")]]:
            digest.update(len(value).to_bytes(8, "big"))
            digest.update(value)
        snapshot["inventorySourceDigest"] = digest.hexdigest()
        with self.assertRaisesRegex(MODULE.ConfigurationError, "paging chain is incomplete"):
            MODULE.native_snapshot(snapshot)

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

    def test_distinct_members_publish_one_original_and_children_inherit_it(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            events = []

            def fake_run(command, **kwargs):
                if "publish" in command:
                    events.extend(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text())["events"])
                return subprocess.CompletedProcess(command, 0, "{}", "")

            def begin(item, attempt, *extra):
                return MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "UTEL", "--item", item, "--attempt", attempt,
                    "--model", "gpt-5.6-sol", "--effort", "medium", *extra,
                ]))["token"]

            with mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "authorized_original", return_value="assignment-digest") as authorized:
                first = begin("UTEL.1", "root-a", "--original-item", "UTEL")
                second = begin("UTEL.2", "root-b", "--original-item", "UTEL")
                self.assertEqual(authorized.call_count, 2)
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", first, "--native-id", "root-a",
                ]))
                child = begin("UTEL.1", "child-a", "--parent-token", first, "--relation", "child")
                self.assertEqual(MODULE.read_state(config, child)["originalItemId"], "UTEL")
                with self.assertRaisesRegex(MODULE.ConfigurationError, "original item identity"):
                    begin("UTEL.1", "child-b", "--parent-token", first, "--relation", "child",
                          "--original-item", "OTHER")
                with self.assertRaisesRegex(MODULE.ConfigurationError, "durable identity"):
                    begin("UTEL.2", "root-b", "--original-item", "OTHER")

            populations = [event for event in events if event["kind"] == "budget-population"]
            self.assertEqual({event["itemId"] for event in populations}, {"UTEL.1", "UTEL.2"})
            self.assertEqual({event["originalItemId"] for event in populations}, {"UTEL"})
            self.assertEqual({event["state"] for event in populations}, {"open"})

    def test_nonself_original_requires_one_protected_assignment(self):
        source = json.dumps({"schema": MODULE.ORIGINAL_ASSIGNMENTS_SCHEMA,
                             "assignments": [{"featureId": "F", "itemId": "F.1", "originalItemId": "F"}]})
        revision = "a" * 40
        ref = subprocess.CompletedProcess([], 0, json.dumps({"ref": "refs/heads/main",
                             "object": {"type": "commit", "sha": revision}}), "")
        def content(value):
            return subprocess.CompletedProcess([], 0, json.dumps({"type": "file",
                "path": MODULE.ORIGINAL_ASSIGNMENTS, "encoding": "base64",
                "content": MODULE.base64.b64encode(value.encode()).decode()}), "")
        with mock.patch.object(MODULE.subprocess, "run", side_effect=[ref, content(source), ref, content(source)]) as read_protected:
            self.assertEqual(MODULE.authorized_original("F", "F.1", "F"),
                             revision + ":" + MODULE.hashlib.sha256(source.encode()).hexdigest())
            self.assertEqual(read_protected.call_args_list[0].args[0],
                             ["gh", "api", "repos/FS-GG/.github/git/ref/heads/main"])
            self.assertEqual(read_protected.call_args_list[1].args[0],
                             ["gh", "api", f"repos/FS-GG/.github/contents/{MODULE.ORIGINAL_ASSIGNMENTS}?ref={revision}"])
            with self.assertRaisesRegex(MODULE.ConfigurationError, "not authorized"):
                MODULE.authorized_original("F", "F.2", "F")
        duplicate = json.dumps({"schema": MODULE.ORIGINAL_ASSIGNMENTS_SCHEMA,
                                "assignments": [{"featureId": "F", "itemId": "F.1", "originalItemId": "F"}] * 2})
        with mock.patch.object(MODULE.subprocess, "run", side_effect=[ref, content(duplicate)]), \
             self.assertRaisesRegex(MODULE.ConfigurationError, "not authorized"):
            MODULE.authorized_original("F", "F.1", "F")

    def test_observed_root_population_only_emits_no_second_dispatch(self):
        with tempfile.TemporaryDirectory() as scratch:
            local = self.config(pathlib.Path(scratch))
            config = MODULE.HostConfig(local.path, local.store_root, local.engine,
                                       "FS-GG/FS.GG.Coordination", True, "producer", "binding")
            batches = []
            args = MODULE.parser().parse_args([
                "population-only", "--feature", "F", "--item", "F.1",
                "--original-item", "F",
            ])

            def fake_run(command, **kwargs):
                if "submit" in command:
                    batches.append(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text()))
                    return subprocess.CompletedProcess(command, 0,
                                                       "durably-received" if len(batches) == 1 else "applied", "")
                return subprocess.CompletedProcess(command, 0, "{}", "")

            with mock.patch.object(MODULE, "authorized_original", return_value="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), \
                 mock.patch.object(MODULE, "validate_workspace"), \
                 mock.patch.object(MODULE, "workspace_mutation_command", side_effect=lambda _config, command: command), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run):
                with self.assertRaisesRegex(MODULE.ConfigurationError, "not applied"):
                    MODULE.population_only(config, args)
                self.assertEqual(MODULE.population_only(config, args)["status"], "applied")
                self.assertEqual(MODULE.population_only(config, args)["status"], "applied")
                self.assertEqual(len(batches), 2)
                self.assertEqual(batches[0], batches[1])
                key = MODULE.digest("", "F.1", "F")
                self.assertEqual(batches[0]["events"], [
                    MODULE.event("feature", "F", None, name="F"),
                    MODULE.event("item", "F.1", "F.1", featureId="F"),
                    MODULE.event("budget-population", "budget-population-" + key, "F.1",
                                 originalItemId="F", state="open", sourceKind="native-item",
                                 sourceRef="roadmap-dispatch:" + key),
                ])
                other = MODULE.parser().parse_args([
                    "population-only", "--feature", "F", "--item", "F.2",
                    "--original-item", "F",
                ])
                self.assertEqual(MODULE.population_only(config, other)["status"], "applied")
                self.assertEqual(batches[-1]["events"][-1]["originalItemId"], "F")
                self.assertEqual(batches[-1]["events"][-1]["itemId"], "F.2")
            conflicting = MODULE.parser().parse_args([
                "population-only", "--feature", "F", "--item", "F.1",
                "--original-item", "OTHER",
            ])
            with mock.patch.object(MODULE, "authorized_original", side_effect=AssertionError("unexpected recheck")), \
                 self.assertRaisesRegex(MODULE.ConfigurationError, "protected identity"):
                MODULE.population_only(config, conflicting)

    def test_population_only_replays_exact_bytes_after_lost_response(self):
        with tempfile.TemporaryDirectory() as scratch:
            local = self.config(pathlib.Path(scratch))
            config = MODULE.HostConfig(local.path, local.store_root, local.engine,
                                       "FS-GG/FS.GG.Coordination", True, "producer", "binding")
            args = MODULE.parser().parse_args([
                "population-only", "--feature", "F", "--item", "F.1", "--original-item", "F",
            ])
            batches = []

            def fake_run(command, **kwargs):
                if "submit" in command:
                    batches.append(pathlib.Path(command[command.index("--input") + 1]).read_bytes())
                    return subprocess.CompletedProcess(command, 1 if len(batches) == 1 else 0,
                                                       "" if len(batches) == 1 else "applied",
                                                       "unacknowledged-lossy" if len(batches) == 1 else "")
                return subprocess.CompletedProcess(command, 0, "{}", "")

            with mock.patch.object(MODULE, "authorized_original", return_value="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") as authorized, \
                 mock.patch.object(MODULE, "validate_workspace"), \
                 mock.patch.object(MODULE, "workspace_mutation_command", side_effect=lambda _config, command: command), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run):
                with self.assertRaisesRegex(MODULE.ConfigurationError, "unacknowledged-lossy"):
                    MODULE.population_only(config, args)
                self.assertEqual(MODULE.population_only(config, args)["status"], "applied")
                self.assertEqual(batches[0], batches[1])
                authorized.assert_called_once()

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
                return native_snapshot(native_thread, [turn], [
                    {"turnId": turn, "turnSequence": 1, "usage": dict(usage)}])
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": parent_thread}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=bound_collector(observation)) as collector:
                root = begin("root")
                start(root, "root")
                child = begin("child", "--parent-token", root, "--relation", "child")
                start(child, "child_1")
                self.assertEqual(finish(child)["coverage"], "native-collaboration-usage-unknown")
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
            integration = MODULE.read_state(config, child)["nativeInventoryIntegration"]
            self.assertEqual(integration["status"], "published")
            self.assertEqual(integration["fact"]["kind"], "runtime-native-inventory/1")
            self.assertEqual(integration["sourceFact"]["kind"], "runtime-native-inventory-source/1")
            self.assertEqual(integration["fact"]["expectedProvider"], "openai")
            self.assertEqual(integration["fact"]["expectedTurnIds"], [turn])
            self.assertEqual(integration["sourceBinding"]["sha256"],
                             MODULE.read_state(config, child)["nativeInventoryBindingDigest"])
            authority = [fact for batch in batches for fact in batch["events"]
                         if fact["kind"] in {"runtime-native-inventory/1",
                                             "runtime-native-inventory-source/1"}]
            self.assertEqual([fact["kind"] for fact in authority],
                             ["runtime-native-inventory/1", "runtime-native-inventory-source/1"])
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
            def usage():
                return native_snapshot("22222222-2222-4222-8222-222222222222", turns[:],
                        [{"turnId": turn, "turnSequence": index + 1,
                                   "usage": {"input_tokens": 10, "cached_input_tokens": 5,
                                             "output_tokens": 2, "reasoning_output_tokens": 1,
                                             "total_tokens": 12}}
                                  for index, turn in enumerate(turns)])
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
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=bound_collector(usage)):
                root = begin("root")
                start(root, "root")
                child = begin("child", "--parent-token", root, "--relation", "child")
                start(child, "worker")
                turns.pop()
                finish(child)
                followup = begin("followup", "--parent-token", child, "--relation", "follow-up")
                followup_state = MODULE.read_state(config, followup)
                self.assertEqual(followup_state["baselineTurnIds"], turns)
                self.assertEqual(followup_state["baselineHostSource"],
                                 "codex-app-server:thread/turns/list")
                self.assertRegex(followup_state["baselineSourceDigest"], r"^[0-9a-f]{64}$")
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
            native = native_snapshot("22222222-2222-4222-8222-222222222222",
                      ["33333333-3333-4333-8333-333333333333"], [{
                          "turnId": "33333333-3333-4333-8333-333333333333", "turnSequence": 1,
                          "usage": {"input_tokens": 10, "cached_input_tokens": 5,
                                    "output_tokens": 2, "reasoning_output_tokens": 1,
                                    "total_tokens": 12}}])
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": "11111111-1111-4111-8111-111111111111"}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=bound_collector(native)):
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
                self.assertEqual(second["coverage"], "native-collaboration-usage-unknown")
                self.assertEqual(revisions, [0, 0])

    def test_partial_inventory_publishes_exact_roster_but_never_claims_complete(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            batches = []
            def fake_run(command, **_):
                if "publish" in command:
                    batches.append(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text()))
                return subprocess.CompletedProcess(command, 0, "{}", "")
            parent = "11111111-1111-4111-8111-111111111111"
            thread = "22222222-2222-4222-8222-222222222222"
            turns = ["33333333-3333-4333-8333-333333333333",
                     "44444444-4444-4444-8444-444444444444"]
            usage = {"input_tokens": 10, "cached_input_tokens": 5, "output_tokens": 2,
                     "reasoning_output_tokens": 1, "total_tokens": 12}
            partial = native_snapshot(thread, turns, [
                {"turnId": turns[0], "turnSequence": 1, "usage": usage}], complete=False)
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": parent}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=bound_collector(partial)):
                root = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "F.1", "--attempt", "root",
                    "--model", "gpt-6-astra", "--effort", "high"]))["token"]
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", root, "--native-id", "root"]))
                child = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "F.1", "--attempt", "child",
                    "--parent-token", root, "--relation", "child",
                    "--model", "gpt-6-astra", "--effort", "high"]))["token"]
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", child, "--native-id", "worker"]))
                result = MODULE.finish(config, MODULE.parser().parse_args([
                    "finish", "--token", child, "--outcome", "completed"]))
            self.assertEqual(result["coverage"], "native-collaboration-usage-unknown")
            facts = [fact for batch in batches for fact in batch["events"]]
            roster = [fact for fact in facts if fact["kind"] == "runtime-start" and fact["phase"] == "turn"]
            observed = [fact for fact in facts if fact["kind"] == "runtime-turn-usage"]
            self.assertEqual([fact["turnId"] for fact in roster], turns)
            self.assertEqual([fact["turnId"] for fact in observed], turns[:1])
            self.assertEqual(observed[0]["provider"], "openai")
            state = MODULE.read_state(config, child)
            self.assertEqual(state["expectedTurnRoster"], turns)
            self.assertEqual(state["turnRosterPublishedCount"], len(turns))
            self.assertEqual(state["nativeInventoryProducerStream"], "roadmap-orchestrator")
            self.assertEqual(state["nativeInventoryHostSource"],
                             "codex-app-server:thread/turns/list")
            self.assertRegex(state["nativeInventoryBindingDigest"], r"^[0-9a-f]{64}$")

    def test_roster_publication_is_bounded_to_store_batch_limit(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            batches = []
            def fake_run(command, **_):
                if "publish" in command:
                    batches.append(json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text()))
                return subprocess.CompletedProcess(command, 0, "{}", "")
            parent = "11111111-1111-4111-8111-111111111111"
            thread = "22222222-2222-4222-8222-222222222222"
            turns = [f"00000000-0000-4000-8000-{number:012x}" for number in range(1, 66)]
            partial = native_snapshot(thread, turns, [], complete=False)
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": parent}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=bound_collector(partial)):
                root = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "I", "--attempt", "root",
                    "--model", "m", "--effort", "e"]))["token"]
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", root, "--native-id", "root"]))
                child = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "I", "--attempt", "child",
                    "--parent-token", root, "--relation", "child", "--model", "m", "--effort", "e"]))["token"]
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", child, "--native-id", "worker"]))
                result = MODULE.finish(config, MODULE.parser().parse_args([
                    "finish", "--token", child, "--outcome", "completed"]))
            roster_batches = [batch for batch in batches
                              if any(event.get("phase") == "turn" for event in batch["events"])]
            self.assertEqual(result["coverage"], "native-collaboration-usage-unknown")
            self.assertEqual([batch["eventCount"] for batch in roster_batches], [64, 1])
            state_path = MODULE.state_path(config, child)
            self.assertLessEqual(state_path.stat().st_size, 262144)
            self.assertEqual(MODULE.read_state(config, child)["turnRosterPublishedCount"], 65)

    def test_interrupted_roster_publication_replays_exact_batch(self):
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            roster_batches = []
            refused = False
            def fake_run(command, **_):
                nonlocal refused
                if "publish" in command:
                    batch = json.loads(pathlib.Path(command[command.index("--input") + 1]).read_text())
                    if any(fact.get("phase") == "turn" for fact in batch["events"]):
                        roster_batches.append(batch)
                        if not refused:
                            refused = True
                            return subprocess.CompletedProcess(command, 1, "", "unacknowledged publication")
                return subprocess.CompletedProcess(command, 0, "{}", "")
            parent = "11111111-1111-4111-8111-111111111111"
            thread = "22222222-2222-4222-8222-222222222222"
            turn = "33333333-3333-4333-8333-333333333333"
            usage = {"input_tokens": 10, "cached_input_tokens": 5, "output_tokens": 2,
                     "reasoning_output_tokens": 1, "total_tokens": 12}
            complete = native_snapshot(thread, [turn], [
                {"turnId": turn, "turnSequence": 1, "usage": usage}], model="m", effort="e")
            with mock.patch.dict(os.environ, {"CODEX_THREAD_ID": parent}), \
                 mock.patch.object(MODULE.subprocess, "run", side_effect=fake_run), \
                 mock.patch.object(MODULE, "collect_native_usage", side_effect=bound_collector(complete)):
                root = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "I", "--attempt", "root",
                    "--model", "m", "--effort", "e"]))["token"]
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", root, "--native-id", "root"]))
                child = MODULE.begin(config, MODULE.parser().parse_args([
                    "begin", "--feature", "F", "--item", "I", "--attempt", "child",
                    "--parent-token", root, "--relation", "child", "--model", "m", "--effort", "e"]))["token"]
                MODULE.started(config, MODULE.parser().parse_args([
                    "started", "--token", child, "--native-id", "worker"]))
                first = MODULE.finish(config, MODULE.parser().parse_args([
                    "finish", "--token", child, "--outcome", "completed"]))
                self.assertEqual(first["coverage"], "native-collaboration-usage-unknown")
                state = MODULE.read_state(config, child)
                self.assertTrue(state.get("rosterIntent"))
                self.assertTrue(state.get("pendingPublication"))
                self.assertEqual(state["nativeInventoryIntegration"]["status"], "published")
                retained_digest = state["nativeInventorySourceDigest"]
                retained_responses = state["nativeInventoryAppServerResponses"]
                second = MODULE.usage_reconcile(config, MODULE.parser().parse_args([
                    "usage-reconcile", "--token", child]))
            self.assertEqual(second["coverage"], "native-collaboration-usage-unknown")
            self.assertEqual(len(roster_batches), 2)
            self.assertEqual(roster_batches[0], roster_batches[1])
            replayed = MODULE.read_state(config, child)
            self.assertEqual(replayed["nativeInventorySourceDigest"], retained_digest)
            self.assertEqual(replayed["nativeInventoryAppServerResponses"], retained_responses)

    def test_followup_duplicate_or_nonprefix_baseline_stays_unknown(self):
        thread = "22222222-2222-4222-8222-222222222222"
        one = "33333333-3333-4333-8333-333333333333"
        two = "44444444-4444-4444-8444-444444444444"
        duplicate = native_snapshot(thread, [one], [], complete=False)
        duplicate["allTurnIds"] = [one, one]
        duplicate["turnInventory"] = duplicate["turnInventory"] * 2
        with self.assertRaisesRegex(MODULE.ConfigurationError, "snapshot is malformed"):
            MODULE.native_snapshot(duplicate)

        malformed_evidence = native_snapshot(thread, [one], [], complete=False)
        malformed_evidence["inventoryPaging"][0]["rowCount"] = 0
        with self.assertRaisesRegex(MODULE.ConfigurationError, "paging evidence disagrees"):
            MODULE.native_snapshot(malformed_evidence)

        wrong_digest = native_snapshot(thread, [one], [], complete=False)
        wrong_digest["inventorySourceDigest"] = "0" * 64
        with self.assertRaisesRegex(MODULE.ConfigurationError, "source digest disagrees"):
            MODULE.native_snapshot(wrong_digest)

        def replace_record(snapshot, collection, index, document):
            raw = json.dumps(document, separators=(",", ":")).encode() + b"\n"
            if collection == "appServerResponses":
                snapshot[collection][index]["responseSha256"] = hashlib.sha256(raw).hexdigest()
                snapshot[collection][index]["responseBytesBase64"] = MODULE.base64.b64encode(raw).decode()
            else:
                snapshot[collection][index] = {
                    "sha256": hashlib.sha256(raw).hexdigest(),
                    "bytesBase64": MODULE.base64.b64encode(raw).decode(),
                }
            digest = hashlib.sha256()
            binding = MODULE.base64.b64decode(snapshot["sourceBinding"]["bytesBase64"])
            digest.update(len(binding).to_bytes(8, "big"))
            digest.update(binding)
            for record in snapshot["appServerResponses"]:
                for field in ("requestBytesBase64", "responseBytesBase64"):
                    value = MODULE.base64.b64decode(record[field])
                    digest.update(len(value).to_bytes(8, "big"))
                    digest.update(value)
            for record in snapshot["rolloutRecords"]:
                value = MODULE.base64.b64decode(record["bytesBase64"])
                digest.update(len(value).to_bytes(8, "big"))
                digest.update(value)
            snapshot["inventorySourceDigest"] = digest.hexdigest()

        forged_provider = native_snapshot(thread, [one], [], complete=False)
        response = json.loads(MODULE.base64.b64decode(
            forged_provider["appServerResponses"][1]["responseBytesBase64"]))
        response["result"]["thread"]["modelProvider"] = "foreign"
        replace_record(forged_provider, "appServerResponses", 1, response)
        with self.assertRaisesRegex(MODULE.ConfigurationError, "provider/profile"):
            MODULE.native_snapshot(forged_provider)

        nonterminal_page = native_snapshot(thread, [one], [], complete=False)
        response = json.loads(MODULE.base64.b64decode(
            nonterminal_page["appServerResponses"][2]["responseBytesBase64"]))
        response["result"]["nextCursor"] = "unretained-next-page"
        replace_record(nonterminal_page, "appServerResponses", 2, response)
        with self.assertRaisesRegex(MODULE.ConfigurationError, "paging chain is incomplete"):
            MODULE.native_snapshot(nonterminal_page)

        forged_usage = native_snapshot(thread, [one], [{
            "turnId": one, "turnSequence": 1,
            "usage": {"input_tokens": 10, "cached_input_tokens": 5, "output_tokens": 2,
                      "reasoning_output_tokens": 1, "total_tokens": 12}}])
        record = json.loads(MODULE.base64.b64decode(forged_usage["rolloutRecords"][0]["bytesBase64"]))
        record["payload"]["usage"]["input_tokens"] = 11
        record["payload"]["usage"]["total_tokens"] = 13
        record["payload"]["turn_token_usage"] = dict(record["payload"]["usage"])
        replace_record(forged_usage, "rolloutRecords", 0, record)
        with self.assertRaisesRegex(MODULE.ConfigurationError, "does not match retained rollout bytes"):
            MODULE.native_snapshot(forged_usage)

        malformed_usage = native_snapshot(thread, [one], [{
            "turnId": one, "turnSequence": 1,
            "usage": {"input_tokens": 2, "cached_input_tokens": 0, "output_tokens": 1,
                      "reasoning_output_tokens": 0, "total_tokens": 99}}])
        with self.assertRaisesRegex(MODULE.ConfigurationError, "counters are malformed"):
            MODULE.native_snapshot(malformed_usage)

        current = native_snapshot(thread, [one, two], [], complete=False)
        state = {"phase": "terminal", "hostParentThreadId": "11111111-1111-4111-8111-111111111111",
                 "nativeId": "worker", "usageBaselineKnown": True, "baselineTurnIds": [two],
                 "baselineThreadId": thread, "pendingPublication": None}
        with tempfile.TemporaryDirectory() as scratch:
            config = self.config(pathlib.Path(scratch))
            state["rootInvocationId"] = "root-invocation"
            with mock.patch.object(MODULE, "collect_native_usage", side_effect=bound_collector(current)), \
                 mock.patch.object(MODULE, "_resume_native_publication"), \
                 mock.patch.object(MODULE, "publish"):
                # Thread publication precedes the overlap check in ordinary state;
                # pin it as already published for this focused boundary probe.
                state.update({"nativeThreadId": thread, "nativeThreadPublished": True,
                              "usageLedger": {}, "invocationId": "inv", "itemId": "I",
                              "model": "m", "effort": "e", "sequence": 0,
                              "producerStream": "p", "associationProducer": None,
                              "associationDigest": None})
                self.assertEqual(MODULE.reconcile_usage(config, state),
                                 "native-collaboration-usage-unknown")
                self.assertNotIn("expectedTurnRoster", state)

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
