#!/usr/bin/env python3
"""Executable, private-data-free SKILL-FS-01.1 oracle contract."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
FIXTURES = HERE / "fixtures"
TELEMETRY_DIR = ROOT / ".agents/skills/work-roadmap/scripts"
TELEMETRY = TELEMETRY_DIR / "roadmap-telemetry.py"
PREFLIGHT = ROOT / ".agents/skills/pipeline-preflight/scripts/preflight.py"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def test_inventory(corpus: dict[str, object]) -> None:
    implementations = corpus["implementations"]
    assert isinstance(implementations, dict)
    for relative, expected in implementations.items():
        for root in (ROOT / ".agents/skills", ROOT / ".claude/skills"):
            source = root / relative
            require(source.is_file(), f"missing implementation copy: {source}")
            actual = hashlib.sha256(source.read_bytes()).hexdigest()
            require(actual == expected, f"implementation drift: {source}: {actual}")


def test_not_configured() -> None:
    with tempfile.TemporaryDirectory() as directory:
        environment = dict(os.environ)
        for name in ("FSGG_TELEMETRY_CONFIG", "FSGG_TELEMETRY_REPOSITORY", "GITHUB_REPOSITORY"):
            environment.pop(name, None)
        environment.update(HOME=directory, XDG_CONFIG_HOME=directory)
        completed = subprocess.run(
            [sys.executable, str(TELEMETRY), "status"],
            capture_output=True,
            env=environment,
            check=False,
        )
    require(completed.returncode == 2, "not-configured must exit 2")
    require(completed.stdout == b'{"schema":"fsgg.telemetry.host-status/1","status":"not-configured"}\n',
            "not-configured stdout bytes changed")
    require(completed.stderr == b"", "not-configured must not write stderr")


def test_configuration(defaults) -> None:
    require(defaults.canonical_github_repository("git@github.com:FS-GG/.github.git") == "FS-GG/.github",
            "SCP repository canonicalization changed")
    require(defaults.canonical_github_repository("https://github.com/FS-GG/.github") == "FS-GG/.github",
            "HTTPS repository canonicalization changed")
    try:
        defaults.canonical_github_repository("https://secret@github.com/FS-GG/.github")
    except defaults.ConfigurationError as error:
        require("canonical GitHub" in str(error), "credential origin refusal category changed")
        require("secret" not in str(error), "credential-bearing origin leaked")
    else:
        raise AssertionError("credential-bearing origin was accepted")


def test_state_replay(telemetry, defaults) -> None:
    fixture = json.loads((FIXTURES / "state-replay.json").read_text(encoding="utf-8"))
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        store = root / "store"
        state_dir = store / "orchestrator-dispatches"
        state_dir.mkdir(parents=True, mode=0o700)
        state_path = state_dir / f'{fixture["token"]}.json'
        state_path.write_text(json.dumps(fixture, separators=(",", ":")) + "\n", encoding="utf-8")
        state_path.chmod(0o600)
        config_path = root / "telemetry.json"
        config_path.write_text("{}\n", encoding="utf-8")
        config_path.chmod(0o600)
        config = defaults.HostConfig(config_path, store, "fixture-engine")
        submitted: list[bytes] = []

        def result(returncode: int, stderr: str = ""):
            def invoke(command, **_kwargs):
                source = Path(command[command.index("--input") + 1])
                submitted.append(source.read_bytes())
                return subprocess.CompletedProcess(command, returncode, "", stderr)
            return invoke

        retained = telemetry.read_state(config, fixture["token"])
        with mock.patch.object(telemetry.subprocess, "run", side_effect=result(1, "ambiguous fixture outcome")):
            try:
                telemetry.publish_pending(config, retained)
            except defaults.ConfigurationError:
                pass
            else:
                raise AssertionError("unknown publication outcome did not refuse")
        retained = telemetry.read_state(config, fixture["token"])
        require("pendingPublication" in retained and retained["sequence"] == 1,
                "unknown outcome discarded pending state")
        with mock.patch.object(telemetry.subprocess, "run", side_effect=result(0)):
            telemetry.publish_pending(config, retained)
        require(submitted[0] == submitted[1], "retry did not replay exact batch bytes")
        settled = telemetry.read_state(config, fixture["token"])
        require(settled["phase"] == "expected" and "pendingPublication" not in settled,
                "applied replay did not settle state")


def test_protected_original_refusal(telemetry, defaults) -> None:
    feature, item = "SKILL-FS-01", "SKILL-FS-01.2"
    token = hashlib.sha256(f"population-only\x1f{feature}\x1f{item}".encode()).hexdigest()[:32]
    state = json.loads((FIXTURES / "original-binding-replay.json").read_text(encoding="utf-8"))
    state["token"] = token
    state["invocationId"] = "original-binding-" + token
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        store = root / "store"
        binding_dir = store / "orchestrator-original-bindings"
        binding_dir.mkdir(parents=True, mode=0o700)
        path = binding_dir / f"{token}.json"
        path.write_text(json.dumps(state, separators=(",", ":")) + "\n", encoding="utf-8")
        path.chmod(0o600)
        config_path = root / "telemetry.json"
        config_path.write_text("{}\n", encoding="utf-8")
        config_path.chmod(0o600)
        config = defaults.HostConfig(config_path, store, "fixture-engine", "FS-GG/.github", True,
                                     "fixture-association", "fixture-binding-digest", "fixture-reference")
        args = SimpleNamespace(feature=feature, item=item, original_item="DIFFERENT-ORIGINAL",
                               producer="roadmap-orchestrator")
        with mock.patch.object(telemetry, "authorized_original") as network:
            try:
                telemetry.population_only(config, args)
            except defaults.ConfigurationError as error:
                require("protected identity" in str(error), "protected mismatch refusal changed")
            else:
                raise AssertionError("protected original mismatch was accepted")
            network.assert_not_called()


def test_observation_inputs(telemetry, defaults, corpus: dict[str, object]) -> None:
    values = json.loads((FIXTURES / "observation-inputs.json").read_text(encoding="utf-8"))
    contracts = corpus["observationInputs"]
    assert isinstance(contracts, dict)
    with tempfile.TemporaryDirectory() as directory:
        for command, contract in contracts.items():
            value = values[command]
            source = Path(directory) / f"{command}.json"
            source.write_text(json.dumps(value), encoding="utf-8")
            fields = set(contract["fields"])
            require(telemetry.read_contract(str(source), contract["schema"], fields) == value,
                    f"{command} positive input changed")
            value["unexpected"] = True
            source.write_text(json.dumps(value), encoding="utf-8")
            try:
                telemetry.read_contract(str(source), contract["schema"], fields)
            except defaults.ConfigurationError as error:
                require("exact" in str(error), f"{command} closed-shape refusal changed")
            else:
                raise AssertionError(f"{command} accepted an extra property")


def rendered(value: dict[str, object]) -> bytes:
    return (json.dumps(value, indent=2, allow_nan=False) + "\n").encode()


def test_preflight(preflight) -> None:
    positive_path = FIXTURES / "assess-positive.json"
    positive = subprocess.run([sys.executable, str(PREFLIGHT), "assess", str(positive_path)],
                              capture_output=True, check=False)
    expected_positive = preflight.assess(json.loads(positive_path.read_text(encoding="utf-8")))
    require(positive.returncode == 0 and positive.stdout == rendered(expected_positive) and not positive.stderr,
            "assess positive process bytes changed")

    insufficient_path = FIXTURES / "assess-insufficient.json"
    insufficient = subprocess.run([sys.executable, str(PREFLIGHT), "assess", str(insufficient_path)],
                                  capture_output=True, check=False)
    expected_insufficient = preflight.assess(json.loads(insufficient_path.read_text(encoding="utf-8")))
    require(insufficient.returncode == 0 and insufficient.stdout == rendered(expected_insufficient),
            "assess insufficient-data bytes changed")
    require(expected_insufficient["missing"] == ["setup_cost"], "unknown estimate became zero")

    passed = json.loads((FIXTURES / "graph-passed.yml").read_text(encoding="utf-8"))
    blocked = json.loads((FIXTURES / "graph-blocked.yml").read_text(encoding="utf-8"))
    require(preflight.graph(passed, ["report:shard_a,shard_b,build"]) == {
        "decision": "passed", "scope": "dependency-order-only", "jobs": 4,
        "requirements": ["report:shard_a,shard_b,build"]}, "graph pass changed")
    require(preflight.graph(blocked, ["report:shard_a,shard_b"]) == {
        "decision": "blocked", "scope": "dependency-order-only", "target": "report",
        "missing_ancestors": ["shard_b"]}, "graph blocked result changed")

    mutations = []
    expression = json.loads(json.dumps(passed)); expression["jobs"]["report"]["needs"] = "${{ inputs.needs }}"; mutations.append(expression)
    cycle = json.loads(json.dumps(passed)); cycle["jobs"]["build"]["needs"] = "report"; mutations.append(cycle)
    unknown = json.loads(json.dumps(passed)); unknown["jobs"]["report"]["needs"] = "missing"; mutations.append(unknown)
    duplicate = json.loads(json.dumps(passed)); duplicate["jobs"]["report"]["needs"] = ["shard_a", "shard_a"]; mutations.append(duplicate)
    bounded = {"jobs": {f"job_{number}": {} for number in range(257)}}; mutations.append(bounded)
    for mutation in mutations:
        try:
            preflight.graph(mutation, ["report:shard_a"] if mutation is not bounded else ["job_1:job_0"])
        except ValueError:
            pass
        else:
            raise AssertionError("graph refusal mutation passed")

    with tempfile.TemporaryDirectory() as directory:
        oversize = Path(directory) / "oversize.yml"
        with oversize.open("wb") as stream:
            stream.seek(2 * 1024 * 1024)
            stream.write(b"x")
        refused = subprocess.run([sys.executable, str(PREFLIGHT), "graph", str(oversize),
                                  "--requires", "report:build"], capture_output=True, check=False)
        require(refused.returncode == 2 and json.loads(refused.stderr)["decision"] == "error",
                "oversize graph input did not refuse")

    missing_parser = subprocess.run([sys.executable, "-S", str(PREFLIGHT), "graph",
                                     str(FIXTURES / "graph-passed.yml"), "--requires", "report:build"],
                                    capture_output=True, check=False)
    require(missing_parser.returncode == 2 and "requires PyYAML" in json.loads(missing_parser.stderr)["message"],
            "missing parser did not refuse")


def main() -> int:
    corpus = json.loads((HERE / "acceptance-corpus.json").read_text(encoding="utf-8"))
    require(corpus["schema"] == "fsgg.skill-python-fsharp-contract/1", "corpus schema changed")
    require(len({case["id"] for case in corpus["cases"]}) == len(corpus["cases"]), "duplicate case id")
    test_inventory(corpus)
    test_not_configured()
    sys.path.insert(0, str(TELEMETRY_DIR))
    defaults = load("fsgg_telemetry_defaults", TELEMETRY_DIR / "fsgg_telemetry_defaults.py")
    load("native_collaboration_usage", TELEMETRY_DIR / "native_collaboration_usage.py")
    telemetry = load("skill_fs_01_telemetry", TELEMETRY)
    preflight = load("skill_fs_01_preflight", PREFLIGHT)
    test_configuration(defaults)
    test_state_replay(telemetry, defaults)
    test_protected_original_refusal(telemetry, defaults)
    test_observation_inputs(telemetry, defaults, corpus)
    test_preflight(preflight)
    print("SKILL-FS-01.1 contract: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
