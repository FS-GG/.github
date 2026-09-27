#!/usr/bin/env python3
"""Process-level CLI checks with public synthetic inputs only."""
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


def run(*args, env=None):
    return subprocess.run(["dotnet", str(ASSEMBLY), *args],
                          cwd=ROOT, env=env, capture_output=True, check=False)


def expect(actual, code, stdout=None, stderr=None):
    assert actual.returncode == code, (actual.returncode, actual.stdout, actual.stderr)
    if stdout is not None:
        assert actual.stdout == stdout, actual.stdout
    if stderr is not None:
        assert actual.stderr == stderr, actual.stderr


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
    print("skill CLI process checks passed")


if __name__ == "__main__":
    main()
