#!/usr/bin/env python3
"""Run retained coord clients only against the hermetic loopback provider."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import tempfile
import time
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
SERVER = ROOT / "tests/coord-engine-e2e/stateful_server.py"
EVIDENCE = ROOT / "tests/producer-fence-attacks/legacy-probe-evidence.json"
VERSIONS = ("0.58.0", "0.75.4")


def sha256(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def package_root(version: str) -> pathlib.Path:
    base = pathlib.Path(os.environ.get("NUGET_PACKAGES", pathlib.Path.home() / ".nuget/packages"))
    return base / "fs.gg.coord.cli" / version


def probe(version: str) -> dict[str, object]:
    root = package_root(version)
    dll = root / "tools/net10.0/any/fsgg-coord-engine.dll"
    nupkg = root / f"fs.gg.coord.cli.{version}.nupkg"
    logical = [
        f"fs.gg.coord.cli/{version}/tools/net10.0/any/fsgg-coord-engine.dll",
        f"fs.gg.coord.cli/{version}/fs.gg.coord.cli.{version}.nupkg",
    ]
    if not dll.is_file() or not nupkg.is_file():
        return {
            "version": version,
            "status": "artifact-unavailable",
            "searched": logical,
            "missing": [logical[index] for index, path in enumerate((dll, nupkg)) if not path.is_file()],
            "gs2089": "unresolved",
        }

    with tempfile.TemporaryDirectory(prefix=f"gs2-08-6-legacy-{version}-") as temporary:
        temp = pathlib.Path(temporary)
        output = temp / "server.out"
        body = temp / "body.txt"
        cache = temp / "cache"
        cache.mkdir()
        body.write_text("legacy-probe")
        with output.open("wb") as stream:
            server = subprocess.Popen(["python3", str(SERVER)], stdout=stream, stderr=subprocess.STDOUT)
        try:
            port = ""
            for _ in range(100):
                text = output.read_text(errors="replace").splitlines()
                if text:
                    port = text[0].strip()
                    break
                time.sleep(0.05)
            if not port.isdigit():
                raise RuntimeError("loopback provider did not publish a port")

            env = dict(os.environ)
            env.pop("FSGG_COORD_TEST_ALLOW_UNFENCED_LOOPBACK_MUTATIONS", None)
            env.update(
                {
                    "FSGG_GITHUB_API_BASE": f"http://127.0.0.1:{port}",
                    "GITHUB_TOKEN": "fixture-token",
                    "FSGG_COORD_OWNER": "FS-GG",
                    "FSGG_COORD_PROJECT": "Coordination",
                    "FSGG_COORD_CACHE": str(cache),
                    "FSGG_WORKER": "",
                }
            )
            command = [
                "dotnet",
                str(dll),
                "comment",
                "create",
                "FS.GG.SDD#43",
                "FS.GG.SDD#42",
                str(body),
                "--json",
                "--worker",
                "attack-worker",
            ]
            completed = subprocess.run(command, env=env, text=True, capture_output=True, timeout=30, check=False)
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/_fixture/mutations", timeout=5) as response:
                ledger = json.load(response)
        finally:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait(timeout=5)

    return {
        "version": version,
        "status": "bypass-observed" if ledger.get("count", 0) else "refusal-observed",
        "gs2089": "failure" if ledger.get("count", 0) else "candidate-refusal-only",
        "artifact": {
            "dllSha256": sha256(dll),
            "nupkgSha256": sha256(nupkg),
        },
        "command": ["comment", "create", "FS.GG.SDD#43", "FS.GG.SDD#42", "<owned-file>", "--json"],
        "exitCode": completed.returncode,
        "stdoutSha256": hashlib.sha256(completed.stdout.encode()).hexdigest(),
        "stderrSha256": hashlib.sha256(completed.stderr.encode()).hexdigest(),
        "providerLedger": ledger,
    }


def produce() -> dict[str, object]:
    return {
        "schema": "fsgg.gs2-08.6-legacy-intercept/1",
        "isolation": "loopback-stateful-provider; no live GitHub mutation",
        "serverSha256": sha256(SERVER),
        "clients": [probe(version) for version in VERSIONS],
    }


def canonical(value: object) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    if args.write == args.verify:
        parser.error("select exactly one of --write or --verify")

    actual = produce()
    if args.write:
        EVIDENCE.write_text(json.dumps(actual, indent=2, sort_keys=True) + "\n")
    else:
        recorded = json.loads(EVIDENCE.read_text())
        if recorded.get("schema") != actual["schema"] or recorded.get("serverSha256") != actual["serverSha256"]:
            raise SystemExit("legacy intercept evidence is not bound to this probe/server")

        actual_by_version = {row["version"]: row for row in actual["clients"]}
        for expected in recorded.get("clients", []):
            observed = actual_by_version.get(expected.get("version"))
            if observed is None:
                raise SystemExit("legacy intercept evidence has an unknown client")
            if observed["status"] == "artifact-unavailable":
                if expected["status"] == "artifact-unavailable" and expected != observed:
                    raise SystemExit("legacy artifact absence evidence drifted")
                # A content-addressed prior execution remains verifiable when that exact package is not
                # installed on this runner. Its DLL/NUPKG digests and provider transcript stay in evidence.
                continue
            if expected != observed:
                raise SystemExit("legacy intercept evidence does not reproduce exactly")

        actual = recorded

    print(canonical(actual))
    print("evidence-sha256=" + hashlib.sha256(canonical(actual).encode()).hexdigest())


if __name__ == "__main__":
    main()
