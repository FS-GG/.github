"""Private host configuration and assignment helpers for FS-GG telemetry adapters."""

from __future__ import annotations

import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass


CONFIG_SCHEMA = "fsgg.telemetry.host-config/1"
WORKSPACE_CONFIG_SCHEMA = "fsgg.telemetry.workspace-config/1"
RUNTIME_ASSIGNMENT_SCHEMA = "fsgg.telemetry.codex-assignment/1"
CI_ASSIGNMENT_SCHEMA = "fsgg.telemetry.ci-assignment/1"
IDENTITY_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,199}$")
sys.dont_write_bytecode = True


class ConfigurationError(RuntimeError):
    pass


@dataclass(frozen=True)
class HostConfig:
    path: pathlib.Path
    store_root: pathlib.Path
    engine: str
    repository: str | None = None
    workspace: bool = False
    producer: str | None = None
    binding_digest: str | None = None


def candidate_config_paths(explicit: str | None = None) -> list[pathlib.Path]:
    if explicit:
        return [pathlib.Path(explicit)]
    configured = os.environ.get("FSGG_TELEMETRY_CONFIG")
    if configured:
        return [pathlib.Path(configured)]
    xdg = os.environ.get("XDG_CONFIG_HOME")
    base = pathlib.Path(xdg) if xdg else pathlib.Path.home() / ".config"
    return [base / "fs-gg" / "telemetry.json"]


def discover_config(explicit: str | None = None) -> HostConfig | None:
    path = candidate_config_paths(explicit)[0]
    if not path.exists() and explicit is None and not os.environ.get("FSGG_TELEMETRY_CONFIG"):
        return None
    try:
        if not path.is_absolute():
            raise ConfigurationError("telemetry config path must be absolute")
        info = path.lstat()
        if path.is_symlink() or not path.is_file():
            raise ConfigurationError("telemetry config must be a regular non-symlink file")
        if os.name != "nt" and (info.st_mode & 0o777) != 0o600:
            raise ConfigurationError("telemetry config permissions must be 0600")
        if info.st_size > 65536:
            raise ConfigurationError("telemetry config exceeds 64 KiB")
        def reject_duplicates(pairs):
            if len(pairs) != len({name for name, _ in pairs}):
                raise ConfigurationError("telemetry config contains duplicate properties")
            return dict(pairs)
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=reject_duplicates)
        if not isinstance(value, dict):
            raise ConfigurationError("telemetry config has an invalid closed shape")
        if value.get("schema") == WORKSPACE_CONFIG_SCHEMA:
            if set(value) != {"schema", "engine", "associations", "retiredAssociations"}:
                raise ConfigurationError("telemetry workspace config has an invalid closed shape")
            repository = os.environ.get("FSGG_TELEMETRY_REPOSITORY") or os.environ.get("GITHUB_REPOSITORY")
            if not repository:
                raise ConfigurationError("telemetry workspace repository association is required")
            engine = value["engine"]
            if not isinstance(engine, str) or not engine or "/" in engine or "\\" in engine:
                raise ConfigurationError("telemetry engine must be an executable name")
            completed = subprocess.run([engine, "telemetry", "workspace", "binding", "--config", str(path),
                                        "--repository", repository], capture_output=True, text=True, timeout=20, check=False)
            if completed.returncode != 0 or len(completed.stdout.encode()) > 4096:
                raise ConfigurationError(completed.stderr.strip() or "telemetry workspace binding failed")
            binding = json.loads(completed.stdout, object_pairs_hook=reject_duplicates)
            fields = {"schema", "configPath", "repository", "producerId", "bindingDigest", "destination", "privateStateRoot"}
            if not isinstance(binding, dict) or set(binding) != fields or binding.get("schema") != "fsgg.telemetry.workspace-binding/1":
                raise ConfigurationError("telemetry workspace binding result is invalid")
            state_root = pathlib.Path(binding["privateStateRoot"])
            return HostConfig(path.resolve(), state_root, engine, binding["repository"], True,
                              binding["producerId"], binding["bindingDigest"])
        if list(value) != ["schema", "storeRoot", "engine"]:
            raise ConfigurationError("telemetry config has an invalid closed shape")
        if value.get("schema") != CONFIG_SCHEMA:
            raise ConfigurationError("telemetry config schema is unsupported")
        store_root = pathlib.Path(value["storeRoot"])
        if not store_root.is_absolute():
            raise ConfigurationError("telemetry store root must be absolute")
        engine = value["engine"]
        if not isinstance(engine, str) or not engine or os.path.sep in engine:
            raise ConfigurationError("telemetry engine must be an executable name")
        return HostConfig(path.resolve(), store_root, engine)
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError, subprocess.SubprocessError) as error:
        if isinstance(error, ConfigurationError):
            raise
        raise ConfigurationError(f"telemetry config is unreadable: {error}") from error


def validate_identity(name: str, value: str | None, *, optional: bool = False) -> str | None:
    if value is None and optional:
        return None
    if not isinstance(value, str) or not IDENTITY_RE.fullmatch(value):
        raise ConfigurationError(f"{name} must be a bounded stable identity")
    return value


def validate_workspace(config: HostConfig) -> None:
    if not config.workspace:
        return
    try:
        completed = subprocess.run(
            [config.engine, "telemetry", "workspace", "status", "--config", str(config.path),
             "--repository", str(config.repository)], capture_output=True, text=True, timeout=20, check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        raise ConfigurationError(f"telemetry workspace validation unavailable: {error}") from error
    if completed.returncode != 0:
        raise ConfigurationError(completed.stderr.strip() or "telemetry workspace validation failed")


def assignment_payload(
    schema: str,
    *,
    feature: str,
    item: str,
    attempt: str,
    parent_attempt: str | None,
    producer: str,
) -> dict[str, object]:
    if schema not in {RUNTIME_ASSIGNMENT_SCHEMA, CI_ASSIGNMENT_SCHEMA}:
        raise ConfigurationError("assignment schema is unsupported")
    return {
        "schema": schema,
        "featureId": validate_identity("feature", feature),
        "itemId": validate_identity("item", item),
        "attemptId": validate_identity("attempt", attempt),
        "parentAttemptId": validate_identity("parent attempt", parent_attempt, optional=True),
        "producerStream": validate_identity("producer", producer),
    }


def write_private_json(directory: pathlib.Path, prefix: str, payload: dict[str, object]) -> pathlib.Path:
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    if os.name != "nt" and (directory.stat().st_mode & 0o077):
        raise ConfigurationError("private telemetry directory permissions must exclude group and other")
    descriptor, temporary = tempfile.mkstemp(prefix=prefix + ".", suffix=".tmp", dir=directory)
    target = directory / f"{prefix}.json"
    try:
        os.fchmod(descriptor, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, separators=(",", ":"), ensure_ascii=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, target)
        directory_fd = os.open(directory, os.O_RDONLY)
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
        return target
    except Exception:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass
        raise


def create_assignment(
    config: HostConfig,
    schema: str,
    *,
    feature: str,
    item: str,
    attempt: str,
    parent_attempt: str | None,
    producer: str,
) -> pathlib.Path:
    validate_workspace(config)
    payload = assignment_payload(
        schema,
        feature=feature,
        item=item,
        attempt=attempt,
        parent_attempt=parent_attempt,
        producer=producer,
    )
    safe_name = re.sub(r"[^A-Za-z0-9._-]", "_", f"{producer}-{item}-{attempt}")[:180]
    return write_private_json(config.store_root / "assignments", safe_name, payload)
