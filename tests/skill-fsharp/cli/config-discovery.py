#!/usr/bin/env python3
"""Focused process checks for read-only telemetry configuration discovery."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PROJECT = HERE / "CliHarness.fsproj"
ASSEMBLY = HERE / "bin/Debug/net10.0/CliHarness.dll"
FIXTURES = HERE / "config-discovery-fixtures"
SCHEMA = "fsgg.telemetry.config-discovery/1"
REFUSAL = b"fsgg skill telemetry-config: configuration discovery failed\n"


def invoke(*arguments, environment):
    return subprocess.run(
        ["dotnet", str(ASSEMBLY), "skill", "telemetry-config", "discover", *arguments],
        cwd=ROOT,
        env=environment,
        capture_output=True,
        check=False,
        timeout=30,
    )


def expect(result, code, stdout=b"", stderr=b""):
    assert result.returncode == code, (result.returncode, result.stdout, result.stderr)
    assert result.stdout == stdout, result.stdout
    assert result.stderr == stderr, result.stderr


def write_host(path, store_root):
    value = (FIXTURES / "host-config.json").read_text(encoding="utf-8")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value.replace("__STORE_ROOT__", str(store_root)), encoding="utf-8")
    path.chmod(0o600)


def configured(result, path, store, engine="fixture-host-engine", repository=None, workspace=False):
    assert result.returncode == 0 and result.stderr == b"", (result.stdout, result.stderr)
    assert len(result.stdout) <= 16 * 1024 and result.stdout.endswith(b"\n")
    value = json.loads(result.stdout)
    assert list(value) == ["schema", "status", "configPath", "storeRoot", "engine", "repository", "workspace"]
    assert value == {
        "schema": SCHEMA,
        "status": "configured",
        "configPath": str(path.resolve()),
        "storeRoot": str(store),
        "engine": engine,
        "repository": repository,
        "workspace": workspace,
    }


def main():
    subprocess.run(["dotnet", "build", str(PROJECT), "--nologo"], cwd=ROOT, check=True)

    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        environment = dict(os.environ, HOME=str(root / "home"), XDG_CONFIG_HOME=str(root / "xdg"))
        environment.pop("FSGG_TELEMETRY_CONFIG", None)
        environment.pop("FSGG_TELEMETRY_REPOSITORY", None)
        environment.pop("GITHUB_REPOSITORY", None)

        expect(
            invoke(environment=environment),
            2,
            b'{"schema":"fsgg.telemetry.config-discovery/1","status":"not-configured"}\n',
        )
        expect(invoke("--config", str(root / "missing-secret-name.json"), environment=environment), 1,
               stderr=REFUSAL)

        default_config = root / "xdg/fs-gg/telemetry.json"
        environment_config = root / "environment.json"
        explicit_config = root / "explicit.json"
        write_host(default_config, root / "default-store")
        write_host(environment_config, root / "environment-store")
        write_host(explicit_config, root / "explicit-store")

        configured(invoke(environment=environment), default_config, root / "default-store")
        selected = dict(environment, FSGG_TELEMETRY_CONFIG=str(environment_config))
        configured(invoke(environment=selected), environment_config, root / "environment-store")
        configured(invoke("--config", str(explicit_config), environment=selected),
                   explicit_config, root / "explicit-store")

        malformed = root / "malformed.json"
        malformed.write_text('{"schema":"fsgg.telemetry.host-config/1","secret":"must-not-escape"}',
                             encoding="utf-8")
        malformed.chmod(0o600)
        expect(invoke("--config", str(malformed), environment=environment), 1, stderr=REFUSAL)

        oversized = root / "oversized.json"
        oversized.write_bytes(b" " * 65537)
        oversized.chmod(0o600)
        expect(invoke("--config", str(oversized), environment=environment), 1, stderr=REFUSAL)

        insecure = root / "insecure.json"
        write_host(insecure, root / "insecure-store")
        insecure.chmod(0o644)
        expect(invoke("--config", str(insecure), environment=environment), 1, stderr=REFUSAL)

        symlink = root / "linked.json"
        symlink.symlink_to(explicit_config)
        expect(invoke("--config", str(symlink), environment=environment), 1, stderr=REFUSAL)

        huge = root / "huge-response.json"
        write_host(huge, "/" + "s" * (17 * 1024))
        expect(invoke("--config", str(huge), environment=environment), 1, stderr=REFUSAL)

        syntax = invoke("--unknown", "arbitrary-secret-value", environment=environment)
        assert syntax.returncode == 2 and syntax.stdout == b""
        assert len(syntax.stderr) < 256 and b"arbitrary-secret-value" not in syntax.stderr

        bin_directory = root / "bin"
        bin_directory.mkdir()
        engine = bin_directory / "fixture-binding-engine"
        wrapper = bin_directory / "fdev-telemetry"
        shutil.copyfile(FIXTURES / "fake-binding-engine.py", engine)
        shutil.copyfile(FIXTURES / "fdev-telemetry", wrapper)
        engine.chmod(0o700)
        wrapper.chmod(0o700)
        workspace = root / "workspace.json"
        shutil.copyfile(FIXTURES / "workspace-config.json", workspace)
        workspace.chmod(0o600)
        state_root = root / "private-state"
        marker = root / "wrapper-was-invoked"
        workspace_environment = dict(
            environment,
            PATH=str(bin_directory) + os.pathsep + environment["PATH"],
            FSGG_TELEMETRY_REPOSITORY="FS-GG/.github",
            CONFIG_DISCOVERY_STATE_ROOT=str(state_root),
            CONFIG_DISCOVERY_WRAPPER_MARKER=str(marker),
        )
        original_config = workspace.read_bytes()
        configured(invoke("--config", str(workspace), environment=workspace_environment), workspace,
                   state_root, "fixture-binding-engine", "FS-GG/.github", True)
        assert workspace.read_bytes() == original_config
        assert not state_root.exists() and not marker.exists(), "discovery performed a mutation or loaded credentials"

        failed_binding = dict(workspace_environment, CONFIG_DISCOVERY_BINDING_FAILURE="1")
        refusal = invoke("--config", str(workspace), environment=failed_binding)
        expect(refusal, 1, stderr=REFUSAL)
        assert b"synthetic-secret-value" not in refusal.stderr

    print("skill telemetry config discovery checks passed")


if __name__ == "__main__":
    main()
