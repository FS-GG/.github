#!/usr/bin/env python3
"""Differential qualification for the typed telemetry readers."""

from __future__ import annotations

import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
PROJECT = HERE / "ReadersProbe.fsproj"
DLL = HERE / "bin/Release/net10.0/ReadersProbe.dll"
FAKE_CODEX = HERE / "fixtures/fake-codex.py"
FAKE_ENGINE = HERE / "fixtures/fake-engine.py"
FAKE_CREDENTIAL_CLIENT = HERE / "fixtures/fdev-telemetry"
PYTHON_READERS = ROOT / ".agents/skills/work-roadmap/scripts"
PARENT = "11111111-1111-1111-1111-111111111111"
TURN_1 = "33333333-3333-3333-3333-333333333333"
TURN_2 = "44444444-4444-4444-4444-444444444444"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def probe(*arguments, environment=None):
    completed = subprocess.run(
        ["dotnet", str(DLL), *map(str, arguments)],
        capture_output=True,
        text=True,
        env=environment,
        check=False,
        timeout=20,
    )
    require(completed.returncode == 0, completed.stdout + completed.stderr)
    return json.loads(completed.stdout)


def write_rollout(path, invalid=False):
    usage_1 = {"input_tokens": 10, "cached_input_tokens": 2, "output_tokens": 5,
               "reasoning_output_tokens": 1, "total_tokens": 15}
    corrected = {"input_tokens": 12, "cached_input_tokens": 2, "output_tokens": 6,
                 "reasoning_output_tokens": 1, "total_tokens": 18}
    usage_2 = {"input_tokens": 7, "cached_input_tokens": 1, "output_tokens": 3,
               "reasoning_output_tokens": 1, "total_tokens": 10}
    if invalid:
        usage_1["cached_input_tokens"] = 11
    rows = [
        {"type": "fixture-metadata", "payload": {"ignored": True}},
        {"type": "token_usage_record", "payload": {"thread_id": "22222222-2222-2222-2222-222222222222",
         "turn_id": TURN_1, "response_id": "response-a", "usage": usage_1, "turn_token_usage": usage_1}},
        {"type": "token_usage_record", "payload": {"thread_id": "22222222-2222-2222-2222-222222222222",
         "turn_id": TURN_1, "response_id": "response-a", "usage": corrected, "turn_token_usage": corrected}},
        {"type": "token_usage_record", "payload": {"thread_id": "22222222-2222-2222-2222-222222222222",
         "turn_id": TURN_2, "response_id": "response-b", "usage": usage_2, "turn_token_usage": usage_2}},
    ]
    path.write_text("".join(json.dumps(row, separators=(",", ":")) + "\n" for row in rows))


def python_projection(value):
    return {
        "threadId": value["threadId"],
        "allTurnIds": value["allTurnIds"],
        "inventory": [{"turnId": row["turnId"], "sequence": row["turnSequence"],
                       "status": row["status"], "terminal": row["terminal"],
                       "usageAvailable": row["usageAvailable"]} for row in value["turnInventory"]],
        "turns": [{"turnId": row["turnId"], "sequence": row["turnSequence"],
                   "provider": value["provider"] or "", "model": value["model"] or "",
                   "effort": value["effort"] or "", "input": row["usage"]["input_tokens"],
                   "cachedInput": row["usage"]["cached_input_tokens"],
                   "output": row["usage"]["output_tokens"],
                   "reasoning": row["usage"]["reasoning_output_tokens"],
                   "total": row["usage"]["total_tokens"]} for row in value["turns"]],
        "complete": value["complete"], "provider": value["provider"],
        "model": value["model"], "effort": value["effort"],
    }


def test_canonical(defaults):
    cases = [
        "git@github.com:FS-GG/.github.git",
        "https://github.com/FS-GG/.github",
        "ssh://git@github.com/FS-GG/.github.git",
        "https://credential@github.com/FS-GG/.github",
        "https://gitlab.com/FS-GG/.github",
        "https://github.com/FS-GG/.github/extra",
    ]
    for value in cases:
        try:
            expected = {"ok": True, "value": defaults.canonical_github_repository(value)}
        except defaults.ConfigurationError as error:
            expected = {"ok": False, "error": str(error)}
        actual = probe("canonical", value)
        require(actual == expected, f"canonical differential mismatch for {value!r}: {actual} != {expected}")
        if not actual["ok"]:
            require("credential" not in actual["error"], "credential-bearing origin leaked")


def test_repository_precedence(defaults):
    with tempfile.TemporaryDirectory() as directory:
        environment = dict(os.environ)
        environment.update(FSGG_TELEMETRY_REPOSITORY="Owner/Repo", GITHUB_REPOSITORY="Ignored/Repo")
        with mock.patch.dict(os.environ, environment, clear=True):
            expected = defaults.workspace_repository()
        actual = probe("discover-repository", directory, environment=environment)
        require(actual == {"ok": True, "value": expected}, "repository environment precedence changed")


def test_config_precedence(defaults):
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        explicit = root / "explicit.json"
        selected = root / "selected.json"
        default = root / "fs-gg/telemetry.json"
        for path, store in ((explicit, root / "explicit-store"), (selected, root / "selected-store"),
                            (default, root / "default-store")):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps({"schema": "fsgg.telemetry.host-config/1",
                                        "storeRoot": str(store), "engine": "fixture-engine"}))
            path.chmod(0o600)
        environment = dict(os.environ)
        environment.update(FSGG_TELEMETRY_CONFIG=str(selected), XDG_CONFIG_HOME=str(root))
        actual_explicit = probe("discover", explicit, environment=environment)
        require(actual_explicit["storeRoot"] == str(root / "explicit-store"), "explicit config lost precedence")
        actual_environment = probe("discover", "-", environment=environment)
        require(actual_environment["storeRoot"] == str(root / "selected-store"), "config environment lost precedence")
        environment.pop("FSGG_TELEMETRY_CONFIG")
        actual_default = probe("discover", "-", environment=environment)
        require(actual_default["storeRoot"] == str(root / "default-store"), "XDG default config changed")
        python = defaults.discover_config(str(explicit))
        require((actual_explicit["path"], actual_explicit["storeRoot"], actual_explicit["engine"], actual_explicit["workspace"]) ==
                (str(python.path), str(python.store_root), python.engine, python.workspace), "host config differential mismatch")

        duplicate = root / "duplicate.json"
        duplicate.write_text('{"schema":"fsgg.telemetry.host-config/1","storeRoot":"/tmp/a","engine":"a","engine":"b"}')
        duplicate.chmod(0o600)
        actual = probe("discover", duplicate, environment=environment)
        require(not actual["ok"] and "JSON is malformed" in actual["error"], "duplicate config property did not refuse")


def test_workspace_credentials_and_assignment():
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        state = root / "private-state"
        config = root / "workspace.json"
        config.write_text(json.dumps({
            "schema": "fsgg.telemetry.workspace-config/1",
            "engine": FAKE_ENGINE.name,
            "associations": [{
                "producerId": "fixture-producer",
                "repositories": ["FS-GG/.github"],
                "destination": {"credentialReference": "fixture-ref"},
            }],
            "retiredAssociations": [],
        }))
        config.chmod(0o600)
        environment = dict(os.environ)
        environment.update(
            FSGG_TELEMETRY_REPOSITORY="FS-GG/.github",
            SKILL_FS_01_STATE_ROOT=str(state),
            FSGG_TELEMETRY_CREDENTIAL_FIXTURE_REF="synthetic-loaded-value",
            PATH=str(FAKE_ENGINE.parent) + os.pathsep + environment.get("PATH", ""),
        )
        discovered = probe("discover", config, environment=environment)
        require(discovered["ok"] and discovered["workspace"] and discovered["repository"] == "FS-GG/.github",
                "workspace binding was not discovered")
        require("synthetic-loaded-value" not in json.dumps(discovered), "credential material escaped discovery")
        mutation = probe("mutation", config, environment=environment)
        require(mutation == {"ok": True, "command": ["fixture-engine", "submit"]},
                "loaded credential should not alter mutation arguments")
        environment.pop("FSGG_TELEMETRY_CREDENTIAL_FIXTURE_REF")
        mutation = probe("mutation", config, environment=environment)
        require(mutation == {"ok": True, "command": [str(FAKE_CREDENTIAL_CLIENT.resolve()), "exec",
                                                       "fixture-engine", "submit"]},
                "unloaded credential did not use the owner-controlled client")
        FAKE_CREDENTIAL_CLIENT.chmod(0o777)
        refused = probe("mutation", config, environment=environment)
        require(not refused["ok"] and "owner-controlled" in refused["error"],
                "group-writable credential client was accepted")
        FAKE_CREDENTIAL_CLIENT.chmod(0o755)
        environment["FSGG_TELEMETRY_CREDENTIAL_FIXTURE_REF"] = "synthetic-loaded-value"
        assignment = probe("ci", config, "SKILL-FS-01", "SKILL-FS-01.2", "fixture-attempt",
                           environment=environment)
        require(assignment["ok"], "CI assignment was not created")
        assignment_path = Path(assignment["assignment"])
        require(assignment_path.is_file() and (assignment_path.stat().st_mode & 0o777) == 0o600,
                "CI assignment is not a private regular file")
        payload = json.loads(assignment_path.read_text())
        require(payload == {"schema": "fsgg.telemetry.ci-assignment/1", "featureId": "SKILL-FS-01",
                            "itemId": "SKILL-FS-01.2", "attemptId": "fixture-attempt",
                            "parentAttemptId": None, "producerStream": "fixture-producer"},
                "CI assignment payload changed")

        ambiguous = json.loads(config.read_text())
        ambiguous["associations"].append(ambiguous["associations"][0])
        config.write_text(json.dumps(ambiguous))
        refused = probe("discover", config, environment=environment)
        require(not refused["ok"] and "missing or ambiguous" in refused["error"],
                "ambiguous credential association did not refuse")


def test_native(native):
    require(probe("coverage", "none") == {"ok": True, "coverage": "Unsupported"},
            "missing parent did not remain explicitly unsupported")
    require(probe("coverage", PARENT) == {"ok": True, "coverage": "Unknown"},
            "present parent was promoted before native evidence")
    with tempfile.TemporaryDirectory() as directory:
        home = Path(directory)
        sessions = home / "sessions/2026/09/27"
        sessions.mkdir(parents=True)
        rollout = sessions / "fixture.jsonl"
        write_rollout(rollout)
        environment = dict(os.environ)
        environment.update(SKILL_FS_01_ROLLOUT=str(rollout), SKILL_FS_01_MODE="complete")
        with mock.patch.dict(os.environ, environment, clear=True):
            expected = native.collect(PARENT, "child_1", root_invocation_id="root-1",
                                      invocation_id="invocation-1", revision=0,
                                      command=str(FAKE_CODEX), codex_home=home)
        actual = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1", "invocation-1", 0,
                       environment=environment)
        projection = python_projection(expected)
        comparable = {key: actual[key] for key in projection}
        require(comparable == projection, f"native differential mismatch: {comparable} != {projection}")
        require(actual["turns"][0]["total"] == 18, "duplicate response correction was added instead of replaced")

        original_rollout = rollout.read_bytes()
        for wire_mode in ("crlf", "eof-final"):
            exact_rollout = sessions / f"{wire_mode}.jsonl"
            raw = original_rollout.replace(b"\n", b"\r\n") if wire_mode == "crlf" else original_rollout.rstrip(b"\n")
            exact_rollout.write_bytes(raw)
            environment.update(SKILL_FS_01_ROLLOUT=str(exact_rollout),
                               SKILL_FS_01_WIRE_MODE=wire_mode)
            retained = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1",
                             f"invocation-{wire_mode}", 0, environment=environment)
            require(retained["ok"], f"{wire_mode} native collection refused: {retained}")
            responses = [base64.b64decode(row["responseBytesBase64"])
                         for row in retained["appServerResponses"]]
            if wire_mode == "crlf":
                require(all(response.endswith(b"\r\n") for response in responses),
                        "CRLF App Server response was normalized")
            else:
                require(responses[-1].endswith(b"}") and not responses[-1].endswith(b"\n") and
                        all(response.endswith(b"\n") for response in responses[:-1]),
                        "unterminated App Server response was given a terminator")
            expected_rollout = [line for line in raw.splitlines(keepends=True)
                                if b'"token_usage_record"' in line]
            retained_rollout = [base64.b64decode(row["bytesBase64"])
                                for row in retained["rolloutRecords"]]
            require(retained_rollout == expected_rollout,
                    f"{wire_mode} rollout bytes changed during retention")
            chunks = [base64.b64decode(retained["sourceBinding"]["bytesBase64"])]
            for record in retained["appServerResponses"]:
                chunks.extend((base64.b64decode(record["requestBytesBase64"]),
                               base64.b64decode(record["responseBytesBase64"])))
            chunks.extend(retained_rollout)
            source = hashlib.sha256()
            for chunk in chunks:
                source.update(len(chunk).to_bytes(8, "big"))
                source.update(chunk)
            require(source.hexdigest() == retained["sourceDigest"],
                    f"{wire_mode} source digest did not bind the retained bytes")
        environment.pop("SKILL_FS_01_WIRE_MODE")
        environment["SKILL_FS_01_ROLLOUT"] = str(rollout)
        environment["SKILL_FS_01_WIRE_MODE"] = "oversize"
        refused = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1",
                        "invocation-oversize-response", 0, environment=environment)
        require(not refused["ok"] and "1 MiB" in refused["error"],
                "oversized App Server byte line was accepted")
        environment.pop("SKILL_FS_01_WIRE_MODE")
        oversized_rollout = sessions / "oversize.jsonl"
        oversized_rollout.write_bytes(b'{"type":"token_usage_record"}' + b" " * (1024 * 1024) + b"\n")
        environment["SKILL_FS_01_ROLLOUT"] = str(oversized_rollout)
        refused = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1",
                        "invocation-oversize-rollout", 0, environment=environment)
        require(not refused["ok"] and "1 MiB" in refused["error"],
                "oversized rollout byte line was accepted")
        environment["SKILL_FS_01_ROLLOUT"] = str(rollout)

        environment["SKILL_FS_01_MODE"] = "missing-profile"
        incomplete = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1", "invocation-2", 0,
                           environment=environment)
        require(incomplete["ok"] and not incomplete["complete"] and incomplete["provider"] is None,
                "missing profile became complete usage")

        environment["SKILL_FS_01_MODE"] = "cycle"
        refused = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1", "invocation-3", 0,
                        environment=environment)
        require(not refused["ok"] and "pagination" in refused["error"], "paging cycle did not refuse")

        outside = home / "outside.jsonl"
        write_rollout(outside)
        environment.update(SKILL_FS_01_MODE="complete", SKILL_FS_01_ROLLOUT=str(outside))
        refused = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1", "invocation-4", 0,
                        environment=environment)
        require(not refused["ok"] and "outside the private host" in refused["error"], "outside rollout did not refuse")

        invalid = sessions / "invalid.jsonl"
        write_rollout(invalid, invalid=True)
        environment["SKILL_FS_01_ROLLOUT"] = str(invalid)
        refused = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1", "invocation-5", 0,
                        environment=environment)
        require(not refused["ok"] and "counters" in refused["error"], "invalid counters did not refuse")

        environment.update(SKILL_FS_01_MODE="complete", SKILL_FS_01_ROLLOUT=str(rollout))
        refused = probe("native", FAKE_CODEX, home, PARENT, "different-agent", "root-1", "invocation-6", 0,
                        environment=environment)
        require(not refused["ok"] and "missing or ambiguous" in refused["error"], "lineage mismatch did not refuse")

        outside_directory = home / "outside-directory"
        outside_directory.mkdir()
        linked = home / "sessions/linked"
        linked.symlink_to(outside_directory, target_is_directory=True)
        linked_rollout = outside_directory / "linked.jsonl"
        write_rollout(linked_rollout)
        environment["SKILL_FS_01_ROLLOUT"] = str(linked / "linked.jsonl")
        refused = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1", "invocation-7", 0,
                        environment=environment)
        require(not refused["ok"] and "outside the private host" in refused["error"], "symlinked rollout did not refuse")

        environment.update(SKILL_FS_01_MODE="timeout", SKILL_FS_01_ROLLOUT=str(rollout))
        refused = probe("native", FAKE_CODEX, home, PARENT, "child_1", "root-1", "invocation-8", 0,
                        environment=environment)
        require(not refused["ok"] and "timed out" in refused["error"], "bounded App Server timeout did not refuse")

        refused = probe("native", home / "missing-codex", home, PARENT, "child_1", "root-1", "invocation-9", 0,
                        environment=environment)
        require(not refused["ok"] and "unavailable" in refused["error"], "missing App Server did not stay unavailable")


def main():
    subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release", "--nologo"], check=True)
    sys.path.insert(0, str(PYTHON_READERS))
    defaults = load("fsgg_telemetry_defaults", PYTHON_READERS / "fsgg_telemetry_defaults.py")
    native = load("native_collaboration_usage", PYTHON_READERS / "native_collaboration_usage.py")
    test_canonical(defaults)
    test_repository_precedence(defaults)
    test_config_precedence(defaults)
    test_workspace_credentials_and_assignment()
    test_native(native)
    print("SKILL-FS-01.2 readers: PASS")


if __name__ == "__main__":
    main()
