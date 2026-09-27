#!/usr/bin/env python3
"""Process-level CLI checks with public synthetic inputs only."""
import base64
import json
import os
from pathlib import Path
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PROJECT = HERE / "CliHarness.fsproj"
ASSEMBLY = HERE / "bin/Debug/net10.0/CliHarness.dll"
FIXTURES = ROOT / "tests/skill-fsharp/contracts/fixtures"
ENGINE = HERE / "fake-engine.py"


def run(*args, env=None):
    return subprocess.run(["dotnet", str(ASSEMBLY), *args],
                          cwd=ROOT, env=env, capture_output=True, check=False)


def expect(actual, code, stdout=None, stderr=None):
    assert actual.returncode == code, (actual.returncode, actual.stdout, actual.stderr)
    if stdout is not None:
        assert actual.stdout == stdout, actual.stdout
    if stderr is not None:
        assert actual.stderr == stderr, actual.stderr


def configured_host(root):
    bin_dir = root / "bin"
    bin_dir.mkdir()
    engine = bin_dir / "fixture-skill-engine"
    engine.write_bytes(ENGINE.read_bytes())
    engine.chmod(0o700)
    store = root / "store"
    store.mkdir(mode=0o700)
    config = root / "telemetry.json"
    config.write_text(json.dumps({"schema": "fsgg.telemetry.host-config/1",
                                  "storeRoot": str(store), "engine": engine.name}), encoding="utf-8")
    config.chmod(0o600)
    env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"],
               SKILL_FS_01_CLI_LOG=str(root / "published.log"))
    return config, store, env


def telemetry(config, env, command, *arguments):
    return run("skill", "roadmap-telemetry", "--config", str(config), command, *arguments, env=env)


def configured_commands(root):
    config, store, env = configured_host(root)
    begin_args = ("--feature", "SKILL-FS-01", "--item", "SKILL-FS-01.3",
                  "--attempt", "attempt-a", "--model", "gpt-6-sol", "--effort", "medium")
    begin = telemetry(config, env, "begin", *begin_args)
    expect(begin, 0, stderr=b"")
    expected = json.loads(begin.stdout)
    assert expected["schema"] == "fsgg.telemetry.roadmap-dispatch/1"
    assert expected["status"] == "expected"
    token = expected["token"]
    assert len(token) == 32 and all(c in "0123456789abcdef" for c in token)
    assert expected["coverage"] == "native-collaboration-usage-unsupported"

    started = telemetry(config, env, "started", "--token", token, "--native-id", "native-agent-a")
    expect(started, 0, stderr=b"")
    assert json.loads(started.stdout)["status"] == "started"
    finished = telemetry(config, env, "finish", "--token", token, "--outcome", "completed")
    expect(finished, 0, stderr=b"")
    terminal = json.loads(finished.stdout)
    assert terminal["status"] == "terminal" and terminal["outcome"] == "completed"
    assert terminal["coverage"] == "native-collaboration-usage-unsupported"
    expect(telemetry(config, env, "finish", "--token", token, "--outcome", "completed"),
           0, finished.stdout, b"")
    changed = telemetry(config, env, "finish", "--token", token, "--outcome", "failed")
    expect(changed, 1, b"", b"fsgg roadmap telemetry: terminal retry differs from the durable terminal intent\n")

    assignment = telemetry(config, env, "ci-assignment", "--feature", "SKILL-FS-01",
                           "--item", "SKILL-FS-01.3", "--attempt", "attempt-ci")
    expect(assignment, 0, stderr=b"")
    result = json.loads(assignment.stdout)
    assert result["schema"] == "fsgg.telemetry.assignment-result/1"
    assert result["status"] == "ready"
    assignment_path = Path(result["assignment"])
    assert assignment_path.is_file() and assignment_path.is_relative_to(store)
    assert json.loads(assignment_path.read_text(encoding="utf-8"))["schema"] == "fsgg.telemetry.ci-assignment/1"

    invalid = telemetry(config, env, "started", "--token", "bad", "--native-id", "x")
    expect(invalid, 1, b"", b"fsgg roadmap telemetry: token must be the opaque 32-hex dispatch token\n")
    trailing_token = telemetry(config, env, "started", "--token", token + "\n", "--native-id", "x")
    expect(trailing_token, 1, b"", b"fsgg roadmap telemetry: token must be the opaque 32-hex dispatch token\n")
    trailing_identity = telemetry(config, env, "begin", "--feature", "SKILL-FS-01",
                                  "--item", "SKILL-FS-01.3", "--attempt", "attempt\n",
                                  "--model", "gpt-6-sol", "--effort", "medium")
    assert trailing_identity.returncode == 1 and b"attempt" in trailing_identity.stderr, trailing_identity

    publications = (root / "published.log").read_text(encoding="ascii").splitlines()
    assert len(publications) >= 3 and all(base64.b64decode(row).startswith(b'{"schema":"fsgg.telemetry.' + b'ingest/1"')
                                          for row in publications)


def ambiguous_retry(root):
    config, store, env = configured_host(root)
    marker = root / "failed-once"
    env["SKILL_FS_01_CLI_FAIL_ONCE"] = str(marker)
    args = ("--feature", "SKILL-FS-01", "--item", "RETRY",
            "--attempt", "attempt-r", "--model", "gpt-6-sol", "--effort", "medium")
    first = telemetry(config, env, "begin", *args)
    expect(first, 1, b"", b"fsgg roadmap telemetry: delivery outcome unknown\n")
    state_dir = store / "orchestrator-dispatches"
    states = list(state_dir.glob("*.json"))
    assert len(states) == 1
    pending = json.loads(states[0].read_text(encoding="utf-8"))
    assert pending["phase"] == "begin-pending" and pending["pendingPublication"]["operation"] == "begin"
    second = telemetry(config, env, "begin", *args)
    expect(second, 0, stderr=b"")
    assert json.loads(second.stdout)["status"] == "expected"
    rows = (root / "published.log").read_text(encoding="ascii").splitlines()
    assert len(rows) == 2 and rows[0] == rows[1], "retry changed publication bytes"


def main():
    subprocess.run(["dotnet", "build", str(PROJECT), "--nologo"], cwd=ROOT, check=True)
    with tempfile.TemporaryDirectory() as directory:
        env = dict(os.environ, HOME=directory, XDG_CONFIG_HOME=directory)
        env.pop("FSGG_TELEMETRY_CONFIG", None)
        expect(run("skill", "roadmap-telemetry", "status", env=env), 2,
               b'{"schema":"fsgg.telemetry.host-status/1","status":"not-configured"}\n', b"")
        expect(run("skill", "roadmap-telemetry", "begin", "--feature", "SKILL-FS-01",
                   "--item", "SKILL-FS-01.2", "--attempt", "a1", "--model", "test",
                   "--effort", "low", env=env), 2,
               b'{"schema":"fsgg.telemetry.host-status/1","status":"not-configured"}\n', b"")
        invalid = run("skill", "roadmap-telemetry", "finish", "--token", "abc", env=env)
        assert invalid.returncode == 2 and b"--outcome" in invalid.stderr
        expect(run("unrelated", env=env), 99, b"", b"")

        positive = run("skill", "preflight", "assess", str(FIXTURES / "assess-positive.json"), env=env)
        expect(positive, 0, stderr=b"")
        assert json.loads(positive.stdout)["decision"] == "pilot-candidate"
        blocked = run("skill", "preflight", "graph", str(FIXTURES / "graph-blocked.yml"),
                      "--requires", "report:shard_b", env=env)
        expect(blocked, 1,
               b'{\n  "decision": "blocked",\n  "scope": "dependency-order-only",\n'
               b'  "target": "report",\n  "missing_ancestors": [\n    "shard_b"\n  ]\n}\n', b"")
        passed = run("skill", "preflight", "graph", str(FIXTURES / "graph-passed.yml"),
                     "--requires", "report:shard_b", env=env)
        expect(passed, 0,
               b'{\n  "decision": "passed",\n  "scope": "dependency-order-only",\n'
               b'  "jobs": 4,\n  "requirements": [\n    "report:shard_b"\n  ]\n}\n', b"")

        oversized = Path(directory) / "oversized.json"
        oversized.write_bytes(b" " * (2 * 1024 * 1024 + 1))
        expect(run("skill", "preflight", "assess", str(oversized), env=env), 2,
               b"", b'{"decision": "error", "message": "Input exceeds 2 MiB"}\n')
    with tempfile.TemporaryDirectory() as directory:
        configured_commands(Path(directory))
    with tempfile.TemporaryDirectory() as directory:
        ambiguous_retry(Path(directory))
    print("skill CLI process checks passed")


if __name__ == "__main__":
    main()
