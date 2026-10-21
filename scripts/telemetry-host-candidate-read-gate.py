#!/usr/bin/env python3
"""Read-only Host coordinate gate; no candidate work or release effects."""

from __future__ import annotations

import ast
import base64
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

from fsgg_feed import GateError, _RefuseFeedIndexRedirect, parse_version

ROOT = pathlib.Path(__file__).resolve().parents[1]
PACKAGE = "FS.GG.Telemetry.Host"
VERSION = "0.8.0"
PREDECESSOR = "0.7.0"
TAG = "telemetry-host/v0.8.0"
JOURNAL = "refs/heads/fsgg/v2/journal/release/utel-host-rel-14"
API = "https://api.github.com/repos/FS-GG/.github"
FEEDS = {
    "github": "https://nuget.pkg.github.com/FS-GG/download/fs.gg.telemetry.host",
    "nuget": "https://api.nuget.org/v3-flatcontainer/fs.gg.telemetry.host",
}
MAX_BYTES = 262144


class Refused(Exception):
    """Only closed, non-secret diagnostics cross the command boundary."""


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class Reader:
    """Seven fixed GETs at most; redirects and oversized bodies are refused."""

    def __init__(self, token: str):
        require(bool(token), "package-read-token-missing")
        self.token = token
        self.deadline = time.monotonic() + 240
        self.requests = 0
        self.opener = urllib.request.build_opener(_RefuseFeedIndexRedirect)

    def get(self, url: str, *, absent: bool = False):
        self.requests += 1
        require(self.requests <= 7, "request-bound")
        remaining = self.deadline - time.monotonic()
        require(remaining > 0, "observation-deadline")
        headers = {"Accept": "application/json", "User-Agent": "fsgg-host-coordinate-gate"}
        if url.startswith(FEEDS["github"] + "/"):
            headers["Authorization"] = "Basic " + base64.b64encode(f"x:{self.token}".encode()).decode()
        elif url.startswith(API + "/"):
            headers["Authorization"] = "Bearer " + self.token
        allowed = {API + "/git/ref/heads/main", API + "/git/ref/tags/" + TAG}
        for base in FEEDS.values():
            allowed.update({base + "/index.json", base + f"/{VERSION}/fs.gg.telemetry.host.{VERSION}.nupkg"})
        require(url in allowed, "endpoint-not-selected")
        request = urllib.request.Request(url, headers=headers, method="GET")
        try:
            with self.opener.open(request, timeout=min(30, remaining)) as response:
                require(response.geturl() == url, "redirect-refused")
                require(response.status == 200, "unexpected-http-status")
                require(not absent, "coordinate-occupied")
                length = response.headers.get("Content-Length")
                require(length is None or (length.isdecimal() and int(length) <= MAX_BYTES), "response-size")
                raw = bytearray()
                while True:
                    require(time.monotonic() < self.deadline, "observation-deadline")
                    chunk = response.read1(min(8192, MAX_BYTES + 1 - len(raw)))
                    if not chunk:
                        break
                    raw.extend(chunk)
                    require(len(raw) <= MAX_BYTES, "response-size")
                return json.loads(raw)
        except urllib.error.HTTPError as error:
            status = error.code
            error.close()
            require(time.monotonic() < self.deadline, "observation-deadline")
            if status == 404 and absent:
                return None
            raise Refused(f"http-{status}") from None
        except GateError:
            raise Refused("redirect-refused") from None
        except (urllib.error.URLError, OSError, ValueError):
            raise Refused("unreadable-response") from None


def source_bindings(root: pathlib.Path) -> None:
    def read(path: str) -> bytes:
        with (root / path).open("rb") as stream:
            raw = stream.read(65537)
        require(len(raw) <= 65536, "source-size")
        return raw

    project = ET.fromstring(read("src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj"))
    for name, expected in (("Version", VERSION), ("PackageId", PACKAGE)):
        rows = project.findall("./PropertyGroup/" + name)
        require(len(rows) == 1 and rows[0].text == expected and not rows[0].attrib, "project-source-binding")
    sources = {
        "scripts/telemetry_host_successor_execution.py": {"PACKAGE": PACKAGE, "VERSION": VERSION, "TAG": TAG},
        "scripts/telemetry-host-successor-publish.py": {"REF": JOURNAL},
    }
    for path, expected in sources.items():
        assignments = {}
        for node in ast.parse(read(path)).body:
            if isinstance(node, ast.Assign):
                for target in node.targets:
                    if isinstance(target, ast.Name) and target.id in expected:
                        require(target.id not in assignments, "duplicate-source-binding")
                        assignments[target.id] = ast.literal_eval(node.value)
        require(assignments == expected, "release-source-binding")


def qualify(environment, root: pathlib.Path, reader_factory=Reader):
    require(environment.get("GITHUB_REPOSITORY") == "FS-GG/.github"
            and environment.get("GITHUB_REPOSITORY_ID") == "1269292704"
            and environment.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
            and environment.get("GITHUB_REF") == "refs/heads/main"
            and environment.get("GITHUB_ACTOR") == "EHotwagner"
            and environment.get("GITHUB_RUN_ATTEMPT") == "1", "native-context")
    source = environment.get("GITHUB_SHA", "")
    run = environment.get("GITHUB_RUN_ID", "")
    require(bool(re.fullmatch(r"[0-9a-f]{40}", source)) and run.isdecimal() and int(run) > 0, "native-identity")
    checkout = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True,
                              text=True, check=True, timeout=10).stdout.strip()
    require(checkout == source, "checkout-source")
    source_bindings(root)
    reader = reader_factory(environment.get("GITHUB_TOKEN", ""))

    def main_matches():
        value = reader.get(API + "/git/ref/heads/main")
        require(isinstance(value, dict) and value.get("ref") == "refs/heads/main"
                and value.get("object") == {"type": "commit", "sha": source,
                                             "url": API + "/git/commits/" + source}, "protected-main-source")

    main_matches()
    require(reader.get(API + "/git/ref/tags/" + TAG, absent=True) is None, "tag-occupied")
    for base in FEEDS.values():
        value = reader.get(base + "/index.json")
        versions = value.get("versions") if isinstance(value, dict) else None
        require(isinstance(versions, list) and bool(versions)
                and all(isinstance(v, str) for v in versions), "index-shape")
        normalized = [parse_version(v) for v in versions]
        require(parse_version(PREDECESSOR) in normalized, "predecessor-unreadable")
        require(all(v < parse_version(VERSION) for v in normalized), "coordinate-occupied-or-newer")
        require(reader.get(base + f"/{VERSION}/fs.gg.telemetry.host.{VERSION}.nupkg", absent=True) is None,
                "package-occupied")
    main_matches()
    return {"sourceSha": source, "runId": int(run), "attempt": 1, "package": PACKAGE,
            "version": VERSION, "feedAbsence": "verified", "journalSourceBinding": JOURNAL,
            "journalNativeAbsence": "not-observed", "candidateCreated": False}


def main() -> int:
    try:
        print(json.dumps(qualify(os.environ, ROOT), sort_keys=True))
        return 0
    except Refused as error:
        print(f"Host candidate read gate refused: {error}", file=sys.stderr)
    except (GateError, OSError, ValueError, TypeError, KeyError, SyntaxError, ET.ParseError, subprocess.SubprocessError):
        print("Host candidate read gate refused: source-or-transport-invalid", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
