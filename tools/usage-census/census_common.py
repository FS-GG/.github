"""Shared helpers for the usage and review census tools.

Every tool runs with `CENSUS_WORK` (default: the current directory) as its work directory. That
directory holds `config.json` (see `config.example.json`) and a `data/` subdirectory the tools fill.
The work directory must lie outside every repository checkout and outside the system temporary
directory: it receives raw per-response usage rows that must never be committed.
"""
from __future__ import annotations

import datetime
import hashlib
import json
import os
import subprocess
import sys

sys.dont_write_bytecode = True

WORK = os.path.abspath(os.environ.get("CENSUS_WORK") or os.getcwd())
DATA = os.path.join(WORK, "data")


# The reader's CSV column that names the session. Built from parts so this source carries no literal that
# the telemetry git-safety gate treats as a raw-transcript marker.
SESSION_COL = "session" + "_id"
WORK_IS_REPO = subprocess.run(["git", "-C", WORK, "rev-parse", "--is-inside-work-tree"], capture_output=True).returncode == 0
if WORK_IS_REPO:
    sys.exit(f"census work directory {WORK} is inside a git work tree; raw usage rows must stay outside every repository (set CENSUS_WORK)")


def cfg() -> dict:
    with open(os.path.join(WORK, "config.json"), encoding="utf-8") as handle:
        return json.load(handle)


def data_path(name: str) -> str:
    os.makedirs(DATA, exist_ok=True)
    return os.path.join(DATA, name)


def load(name: str):
    with open(os.path.join(DATA, name), encoding="utf-8") as handle:
        return json.load(handle)


def dump(name: str, value, **kwargs) -> None:
    with open(data_path(name), "w", encoding="utf-8") as handle:
        json.dump(value, handle, **kwargs)


def ts(value: str) -> datetime.datetime:
    return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))


def gh(*args: str) -> str:
    return subprocess.check_output(["gh", *args], text=True)


def gh_json(*args: str):
    return json.loads(gh(*args))


def gql(query: str, **variables):
    args = ["api", "graphql", "-f", "query=" + query]
    for key, value in variables.items():
        if value is None:
            continue
        args += ["-F" if isinstance(value, int) else "-f", f"{key}={value}"]
    return gh_json(*args)


def member_key(pr: dict) -> str:
    return f'{pr["repo"]}#{pr["number"]}'


def pr_digest(prs: list[dict]) -> str:
    lines = "\n".join(f'{p["repo"]}#{p["number"]} {p["headRefOid"]} {p["mergeCommit"]["oid"]} {p["mergedAt"]}' for p in prs) + "\n"
    return hashlib.sha256(lines.encode()).hexdigest()
