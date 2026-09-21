#!/usr/bin/env python3
"""Receiver and public-package checks for callable Coordination opt-in."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


HERE = Path(__file__).resolve().parent
FIXTURES = HERE / "fixtures"
SHA = "a" * 40


def run(command: list[str], *, env: dict[str, str] | None = None, expected: int = 0) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(command, text=True, capture_output=True, env=env)
    if result.returncode != expected:
        raise SystemExit(
            f"callable-adoption: exit {result.returncode}, expected {expected}: {' '.join(command)}\n"
            f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}"
        )
    return result


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--receiver-root", type=Path, required=True)
    args = parser.parse_args()
    adopter = args.receiver_root / "scripts/adopt-callable-coordination.py"

    with tempfile.TemporaryDirectory(prefix="fsgg-callable-adoption-") as temporary:
        work = Path(temporary)
        lifecycle = work / "lifecycle.json"
        operations = work / "operations.json"
        lifecycle.write_text('{"state":"retained"}\n', encoding="utf-8")
        operations.write_text('{"operations":["retained"]}\n', encoding="utf-8")
        sentinel = (lifecycle.read_bytes(), operations.read_bytes())

        clean = work / "clean/.config/dotnet-tools.json"
        run(["python3", str(adopter), "install", "--manifest", str(clean)])
        if load(clean) != load(FIXTURES / "callable-clean-expected.json"):
            raise SystemExit("callable-adoption: clean manifest mismatch")

        retained = work / "retained/.config/dotnet-tools.json"
        retained.parent.mkdir(parents=True)
        shutil.copyfile(FIXTURES / "callable-retained-input.json", retained)
        run(["python3", str(adopter), "install", "--manifest", str(retained)])
        if load(retained) != load(FIXTURES / "callable-retained-expected.json"):
            raise SystemExit("callable-adoption: retained upgrade changed peer state")
        installed_bytes = retained.read_bytes()
        run(["python3", str(adopter), "install", "--manifest", str(retained)])
        if retained.read_bytes() != installed_bytes:
            raise SystemExit("callable-adoption: idempotent install rewrote manifest")
        run(["python3", str(adopter), "uninstall", "--manifest", str(retained)])
        if load(retained) != load(FIXTURES / "callable-retained-input.json"):
            raise SystemExit("callable-adoption: uninstall did not restore retained peers")
        uninstalled_bytes = retained.read_bytes()
        run(["python3", str(adopter), "uninstall", "--manifest", str(retained)])
        if retained.read_bytes() != uninstalled_bytes:
            raise SystemExit("callable-adoption: idempotent uninstall rewrote manifest")

        previous = work / "previous/.config/dotnet-tools.json"
        previous.parent.mkdir(parents=True)
        previous_value = load(FIXTURES / "callable-retained-input.json")
        previous_value["tools"]["fs.gg.coordination.cli"] = {
            "version": "0.1.0", "commands": ["fsgg-coordination"], "rollForward": False
        }
        previous.write_text(json.dumps(previous_value) + "\n", encoding="utf-8")
        upgraded = run(["python3", str(adopter), "install", "--manifest", str(previous)])
        if "upgraded" not in upgraded.stdout or load(previous) != load(FIXTURES / "callable-retained-expected.json"):
            raise SystemExit("callable-adoption: exact previous pin did not upgrade without changing peers")

        conflict = work / "conflict/.config/dotnet-tools.json"
        conflict.parent.mkdir(parents=True)
        shutil.copyfile(FIXTURES / "callable-conflict.json", conflict)
        before = conflict.read_bytes()
        refusal = run(
            ["python3", str(adopter), "install", "--manifest", str(conflict)], expected=3
        )
        if "conflicting fs.gg.coordination.cli entry" not in refusal.stderr or conflict.read_bytes() != before:
            raise SystemExit("callable-adoption: conflict did not refuse without writing")
        if (lifecycle.read_bytes(), operations.read_bytes()) != sentinel:
            raise SystemExit("callable-adoption: lifecycle or operation state changed")

        nuget = work / "NuGet.Config"
        nuget.write_text(
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<configuration><packageSources><clear/>'
            '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />'
            '</packageSources></configuration>\n',
            encoding="utf-8",
        )
        environment = dict(os.environ)
        environment.update({"DOTNET_CLI_HOME": str(work / "dotnet-home"), "NUGET_PACKAGES": str(work / "packages")})
        tool = work / "public-tool"
        run([
            "dotnet", "tool", "install", "--tool-path", str(tool), "--configfile", str(nuget),
            "--version", "0.1.1", "FS.GG.Coordination.Cli"
        ], env=environment)
        executable = tool / "fsgg-coordination"
        invocation = run([str(executable), "delivery"], env=environment, expected=2)
        if "delivery <inspect|plan|advance>" not in invocation.stderr:
            raise SystemExit("callable-adoption: installed public command did not expose delivery boundary")

        observation = work / "observation.json"
        observation.write_text(json.dumps({
            "repository": "fs-gg/example", "repositoryId": 101, "pullRequestNumber": 7,
            "pullRequestNodeId": "PR_callable_receiver", "baseRef": "main", "baseSha": SHA,
            "headSha": "b" * 40, "policyRevision": "c" * 40,
            "checks": [{"identity": "required", "appId": 10, "conclusion": "passed"}],
            "epoch": "OperatingV1", "epochGeneration": 3, "epochCommit": "d" * 40,
            "journalGeneration": 7, "journalHead": "e" * 40, "sourceComplete": True,
            "checksComplete": True, "authorized": True, "supported": True,
        }) + "\n", encoding="utf-8")
        plan = work / "plan.json"
        with plan.open("w", encoding="utf-8") as output:
            planned = subprocess.run(
                [str(executable), "delivery", "plan", "--observation", str(observation)],
                text=True, stdout=output, stderr=subprocess.PIPE, env=environment,
            )
        if planned.returncode != 0:
            raise SystemExit(f"callable-adoption: installed plan failed: {planned.stderr}")
        provider = work / "provider.json"
        provider.write_text('{"effect":"absent"}\n', encoding="utf-8")
        refused = run([
            str(executable), "delivery", "advance", "--observation", str(observation),
            "--provider-response", str(provider), "--plan", str(plan),
        ], env=environment, expected=3)
        if "PreOpenV2Refusal" not in refused.stderr:
            raise SystemExit("callable-adoption: installed pre-OpenV2 command did not fail closed")

    canonical = load(args.receiver_root / "dist/dotnet/.config/dotnet-tools.json")
    if canonical["tools"].get("fs.gg.coord.cli", {}).get("version") != "0.90.0":
        raise SystemExit("callable-adoption: legacy bridge was replaced")
    if canonical["tools"].get("fs.gg.coordination.cli") != {
        "version": "0.1.1", "commands": ["fsgg-coordination"], "rollForward": False
    }:
        raise SystemExit("callable-adoption: canonical callable pin mismatch")
    print("callable-adoption: PASS (clean, retained, exact upgrade, idempotent, conflict, uninstall, public refusal)")


if __name__ == "__main__":
    main()
