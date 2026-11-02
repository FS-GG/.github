#!/usr/bin/python3 -I
"""Private fdev Done-retention adapter (#5170). Never enables itself or retries an unresolved run.

The existing archiver owns native reads, per-card writer exclusion, intent/send/readback and policy.
This adapter pins its activation inputs and preserves a cross-run latch before invoking it.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import subprocess
import sys
import tempfile

PROJECT = "PVT_kwDOEYAWY84Bldpa"
SCHEMA = "fsgg.board-archive-config/1"
FIELDS = {"schema", "enabled", "maxArchive", "script", "gh", "binding", "commitment", "readerVersion",
          "readerFiles", "acceptedReceipt", "stateDir", "outDir", "workdir"}


class Refused(Exception):
    pass


def private_file(path: Path) -> bytes:
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != 0o600:
        raise Refused("private input must be an owner-owned regular 0600 file")
    return path.read_bytes()


def absolute(text: object) -> Path:
    if not isinstance(text, str) or not Path(text).is_absolute():
        raise Refused("absolute path required")
    return Path(text)


def pin(value: object) -> tuple[Path, str]:
    if not isinstance(value, dict) or set(value) != {"path", "sha256"}:
        raise Refused("invalid file pin")
    path = absolute(value["path"])
    digest = value["sha256"]
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise Refused("invalid file digest")
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o022:
        raise Refused("pinned file must be owner-owned, regular and not group/world writable")
    if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise Refused("pinned file changed; Root must refresh activation")
    return path, digest


def private_dir(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    info = path.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != 0o700:
        raise Refused("output/state directory must be owner-owned, regular and 0700")
    return path


def sync_dir(path: Path) -> None:
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def exclusive_json(path: Path, data: dict) -> None:
    fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w") as handle:
        json.dump(data, handle, sort_keys=True)
        handle.write("\n")
        handle.flush()
        os.fsync(handle.fileno())
    sync_dir(path.parent)


def configuration(path: Path) -> tuple[dict, dict[str, Path]]:
    c = json.loads(private_file(path))
    if not isinstance(c, dict) or set(c) != FIELDS or c["schema"] != SCHEMA or c["enabled"] is not True:
        raise Refused("missing, disabled or invalid archive activation")
    if type(c["maxArchive"]) is not int or not 1 <= c["maxArchive"] <= 500:
        raise Refused("Root must supply a positive archive bound at most 500")
    version = c["readerVersion"]
    if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+", version) or tuple(map(int, version.split("."))) < (0, 114, 1):
        raise Refused("qualified archived-membership reader required")
    paths = {name: pin(c[name])[0] for name in ("script", "gh", "binding", "commitment", "acceptedReceipt")}
    private_file(paths["binding"])
    binding = json.loads(paths["binding"].read_bytes())
    selected = binding.get("selectedIssues") if isinstance(binding, dict) else None
    if not isinstance(selected, dict) or not selected:
        raise Refused("invalid enrollment binding")
    if not isinstance(c["readerFiles"], list) or not c["readerFiles"]:
        raise Refused("qualified installed reader file pins required")
    for value in c["readerFiles"]:
        pin(value)
    receipt = json.loads(private_file(paths["acceptedReceipt"]))
    expected = {"projectId": PROJECT, "keep": 200, "maxArchive": c["maxArchive"], "readerVersion": version,
                "readerFiles": c["readerFiles"], "scriptSha256": c["script"]["sha256"],
                "bindingSha256": c["binding"]["sha256"], "commitmentSha256": c["commitment"]["sha256"]}
    if (not isinstance(receipt, dict) or receipt.get("schema") != "fsgg.board-archive-accepted/1"
            or receipt.get("accepted") is not True or receipt.get("inputs") != expected
            or not re.fullmatch(r"https://github\.com/FS-GG/\.github/issues/5170#issuecomment-\d+", str(receipt.get("evidenceUrl")))):
        raise Refused("Root's accepted first-real execution must match activation inputs")
    for name in ("stateDir", "outDir", "workdir"):
        paths[name] = absolute(c[name])
    if Path.cwd().resolve() != paths["workdir"].resolve() or (paths["workdir"] / ".git").exists():
        raise Refused("fixed non-checkout working directory required")
    runtime = os.environ.get("XDG_RUNTIME_DIR", "")
    if not runtime or not Path(runtime).is_absolute():
        raise Refused("shared XDG_RUNTIME_DIR required")
    return c, paths


def settled(run: Path) -> bool:
    custody = run / "custody" / "custody.jsonl"
    if not custody.exists():
        return (run / "stdout").read_text().rstrip().endswith("nothing to archive.")
    rows = [json.loads(line) for line in custody.read_text().splitlines()]
    pending = set()
    for row in rows:
        if not isinstance(row, dict):
            return False
        if row.get("step") == "intent":
            pending.add(row.get("itemId"))
        elif row.get("step") == "archived":
            pending.discard(row.get("itemId"))
        elif row.get("step") in ("uncertain", "stopped"):
            return False
    return bool(rows) and rows[0].get("step") == "run" and rows[-1].get("step") == "finished" and not pending


def run(config: Path, credentials: Path, timeout: float = 720) -> int:
    try:
        c, p = configuration(config)
        secrets = json.loads(private_file(credentials))
        token = secrets.get("GH_TOKEN") if isinstance(secrets, dict) else None
        if not isinstance(token, str) or not token.strip():
            raise Refused("credential file has no GH_TOKEN")
        state = private_dir(p["stateDir"])
        latch = state / "in-progress.json"
        if latch.exists() or latch.is_symlink():
            raise Refused("unresolved prior run; Root readback required before another invocation")
        out = private_dir(p["outDir"])
        stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ-")
        directory = Path(tempfile.mkdtemp(prefix=stamp, dir=out))
        exclusive_json(latch, {"run": str(directory), "configSha256": hashlib.sha256(config.read_bytes()).hexdigest()})
        env = {key: os.environ[key] for key in ("HOME", "LANG", "LC_ALL", "XDG_RUNTIME_DIR", "DOTNET_ROOT") if key in os.environ}
        env.update(PATH="/usr/local/bin:/usr/bin:/bin", GH_TOKEN=token.strip(), COORD_ARCHIVE_GH=str(p["gh"]))
        command = ["/usr/bin/python3", "-I", str(p["script"]), "--owner", "FS-GG", "--project", "3",
                   "--expect-project-id", PROJECT, "--keep", "200", "--max-archive", str(c["maxArchive"]),
                   "--enrollment-binding", str(p["binding"]), "--reader-version", c["readerVersion"],
                   "--custody-dir", str(directory / "custody"), "--execute"]
        with os.fdopen(os.open(directory / "stdout", os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as stdout, \
                os.fdopen(os.open(directory / "stderr", os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as stderr:
            process = subprocess.Popen(command, env=env, stdout=stdout, stderr=stderr, start_new_session=True, umask=0o077)
            try:
                code = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
                code = 124
        if code != 0 or not settled(directory):
            print("board-archive: stopped; private custody retained, Root readback required", file=sys.stderr)
            return 3
        exclusive_json(directory / "completed.json", {"exitCode": code, "settled": True})
        latch.unlink()
        sync_dir(state)
        print("board-archive: completed; private custody retained")
        return 0
    except (Refused, OSError, ValueError, KeyError, TypeError):
        # Never emit paths, card identities, tokens or exception text to the journal.
        print("board-archive: refused; check private activation inputs or unresolved custody", file=sys.stderr)
        return 2


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--credentials", type=Path, required=True)
    args = parser.parse_args()
    return run(args.config, args.credentials)


if __name__ == "__main__":
    sys.exit(main())
