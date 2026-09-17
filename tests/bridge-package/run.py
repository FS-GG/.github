#!/usr/bin/env python3
"""Bind and execute one local GS2-08.7 bridge package candidate."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import tempfile
import time
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
EXPECTATIONS_PATH = HERE / "expectations.json"
SERVER = ROOT / "tests/coord-engine-e2e/stateful_server.py"


def fail(message: str) -> "None":
    raise SystemExit(f"bridge-package: {message}")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: pathlib.Path) -> str:
    return sha256_bytes(path.read_bytes())


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def run(command: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, text=True, capture_output=True, check=False, **kwargs)


def git(*args: str) -> str:
    result = run(["git", "-C", str(ROOT), *args])
    if result.returncode:
        fail(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def expectations() -> dict[str, object]:
    value = json.loads(EXPECTATIONS_PATH.read_text(encoding="utf-8"))
    if value.get("schema") != "fsgg.gs2-08.7-bridge-expectations/1":
        fail("independent expectations have an unsupported schema")
    return value


def package_metadata(package: pathlib.Path) -> tuple[str, str, str, dict[str, dict[str, object]], str]:
    if not package.is_absolute() or not package.is_file():
        fail("--package must name an existing absolute local candidate")
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        nuspecs = [name for name in names if name.endswith(".nuspec") and "/" not in name]
        if len(nuspecs) != 1:
            fail("candidate must contain exactly one root nuspec")
        root = ET.fromstring(archive.read(nuspecs[0]))
        ns = {"n": "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"}
        package_id = root.findtext("n:metadata/n:id", namespaces=ns) or ""
        version = root.findtext("n:metadata/n:version", namespaces=ns) or ""
        repository = root.find("n:metadata/n:repository", namespaces=ns)
        repository_commit = "" if repository is None else repository.attrib.get("commit", "")
        payload: dict[str, dict[str, object]] = {}
        for name in sorted(name for name in names if name.startswith("tools/net10.0/any/") and not name.endswith("/")):
            value = archive.read(name)
            payload[name] = {"sha256": sha256_bytes(value), "size": len(value)}
    return package_id, version, repository_commit, payload, sha256_bytes(canonical(payload))


def validate_source(commit: str, tree: str, expected: dict[str, object]) -> None:
    if len(commit) != 40 or len(tree) != 40:
        fail("source commit and tree must be full Git object identities")
    if git("rev-parse", f"{commit}^{{commit}}") != commit:
        fail("source commit is unavailable or abbreviated")
    if git("rev-parse", f"{commit}^{{tree}}") != tree:
        fail("wrong source tree binding")
    producer = expected["producerSource"]
    if git("rev-parse", f"{producer['commit']}^{{tree}}") != producer["tree"]:
        fail("producer source commit/tree expectation is invalid")
    ancestor = run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", producer["commit"], commit])
    if ancestor.returncode:
        fail("candidate source does not descend from the qualified producer source")


def validate_repository_identities(expected: dict[str, object]) -> None:
    for relative, digest in expected["componentIdentities"].items():
        path = ROOT / relative
        if not path.is_file() or sha256_file(path) != digest:
            fail(f"component identity changed: {relative}")


def describe(package: pathlib.Path, commit: str, tree: str) -> dict[str, object]:
    expected = expectations()
    validate_source(commit, tree, expected)
    validate_repository_identities(expected)
    package_id, version, repository_commit, payload, payload_digest = package_metadata(package)
    if package_id != expected["package"]["id"] or version != expected["package"]["version"]:
        fail("candidate package id/version does not match the independent expectation")
    if repository_commit != commit:
        fail("candidate nuspec repository commit does not match the source binding")
    assemblies: dict[str, str] = {}
    for name in expected["requiredAssemblies"]:
        key = f"tools/net10.0/any/{name}"
        if key not in payload:
            fail(f"candidate is missing bridge assembly: {name}")
        assemblies[name] = payload[key]["sha256"]
    return {
        "schema": "fsgg.gs2-08.7-bridge-candidate/1",
        "package": {"id": package_id, "version": version},
        "archiveSha256": sha256_file(package),
        "payloadSha256": payload_digest,
        "payloadEntries": len(payload),
        "source": {"commit": commit, "tree": tree},
        "producerSource": expected["producerSource"],
        "installedAssemblyDigests": assemblies,
        "acceptedReceipts": expected["acceptedReceipts"],
        "componentIdentities": expected["componentIdentities"],
        "coherentSet": expected["coherentSet"],
        "expectationsSha256": sha256_file(EXPECTATIONS_PATH),
    }


def require_complete(binding: dict[str, object]) -> None:
    required = {
        "schema", "package", "archiveSha256", "payloadSha256", "payloadEntries", "source",
        "producerSource", "installedAssemblyDigests", "acceptedReceipts", "componentIdentities",
        "coherentSet", "expectationsSha256",
    }
    if set(binding) != required:
        fail("incomplete or unknown candidate evidence")


def verify_identity(package: pathlib.Path, binding: dict[str, object]) -> dict[str, object]:
    require_complete(binding)
    expected = expectations()
    if binding["schema"] != "fsgg.gs2-08.7-bridge-candidate/1":
        fail("candidate evidence schema changed")
    if binding["expectationsSha256"] != sha256_file(EXPECTATIONS_PATH):
        fail("candidate evidence names the wrong independent expectations")
    validate_repository_identities(expected)
    validate_source(binding["source"]["commit"], binding["source"]["tree"], expected)
    if binding["producerSource"] != expected["producerSource"]:
        fail("wrong producer source binding")
    if binding["acceptedReceipts"] != expected["acceptedReceipts"]:
        fail("wrong accepted receipt binding")
    if binding["componentIdentities"] != expected["componentIdentities"]:
        fail("wrong workflow/Kit/Drivers identity binding")
    if binding["coherentSet"] != expected["coherentSet"]:
        fail("wrong coherent-set identity binding")
    if binding["archiveSha256"] != sha256_file(package):
        fail("substituted same-version package archive")
    package_id, version, repository_commit, payload, payload_digest = package_metadata(package)
    if binding["package"] != {"id": package_id, "version": version} or binding["package"] != {
        "id": expected["package"]["id"], "version": expected["package"]["version"]
    }:
        fail("wrong package identity")
    if repository_commit != binding["source"]["commit"]:
        fail("package/source commit mismatch")
    if binding["payloadSha256"] != payload_digest or binding["payloadEntries"] != len(payload):
        fail("changed or incomplete package payload")
    actual_assemblies = {}
    for name in expected["requiredAssemblies"]:
        key = f"tools/net10.0/any/{name}"
        if key not in payload:
            fail(f"missing bridge assembly: {name}")
        actual_assemblies[name] = payload[key]["sha256"]
    if binding["installedAssemblyDigests"] != actual_assemblies:
        fail("changed bridge assembly digest")
    return expected


def private_environment(root: pathlib.Path) -> dict[str, str]:
    env = dict(os.environ)
    env.update({
        "DOTNET_CLI_HOME": str(root / "dotnet-home"),
        "NUGET_PACKAGES": str(root / "nuget-packages"),
        "NUGET_HTTP_CACHE_PATH": str(root / "nuget-http"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    })
    for name in ("NUGET_AUTH_TOKEN", "GITHUB_TOKEN", "GH_TOKEN"):
        env.pop(name, None)
    return env


def install_candidate(package: pathlib.Path, binding: dict[str, object], expected: dict[str, object], root: pathlib.Path) -> tuple[pathlib.Path, pathlib.Path, dict[str, str]]:
    feed, tool_dir = root / "feed", root / "tool"
    feed.mkdir(); tool_dir.mkdir()
    shutil.copy2(package, feed / package.name)
    config = root / "NuGet.config"
    config.write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<configuration><packageSources><clear />'
        f'<add key="candidate" value="{feed}" /></packageSources></configuration>\n', encoding="utf-8")
    env = private_environment(root)
    command = [
        "dotnet", "tool", "install", expected["package"]["id"], "--version", expected["package"]["version"],
        "--tool-path", str(tool_dir), "--configfile", str(config), "--no-cache",
    ]
    result = run(command, env=env, timeout=120)
    if result.returncode:
        fail(f"isolated local tool install failed: {result.stdout}{result.stderr}")
    payload = tool_dir / ".store" / expected["package"]["id"].lower() / expected["package"]["version"]
    candidates = list(payload.glob("**/tools/net10.0/any"))
    if len(candidates) != 1:
        fail("installed package payload location is ambiguous")
    payload = candidates[0].resolve()
    for name, digest in binding["installedAssemblyDigests"].items():
        installed = payload / name
        if not installed.is_file() or sha256_file(installed) != digest:
            fail(f"installed assembly digest mismatch: {name}")
    return tool_dir, payload, env


def run_installed_command(tool_dir: pathlib.Path, env: dict[str, str]) -> None:
    command = tool_dir / "fsgg-coord-engine"
    result = run([str(command), "--help"], env=env, timeout=30)
    if result.returncode or "fsgg-coord" not in (result.stdout + result.stderr):
        fail("installed command did not execute its packaged entry point")


def production_refusal(tool_dir: pathlib.Path, env: dict[str, str], root: pathlib.Path) -> None:
    output, body, cache = root / "server.out", root / "body.txt", root / "coord-cache"
    body.write_text("bridge-package-refusal", encoding="utf-8"); cache.mkdir()
    with output.open("wb") as stream:
        server = subprocess.Popen(["python3", str(SERVER)], stdout=stream, stderr=subprocess.STDOUT)
    try:
        port = ""
        for _ in range(100):
            lines = output.read_text(errors="replace").splitlines()
            if lines:
                port = lines[0].strip(); break
            time.sleep(0.05)
        if not port.isdigit():
            fail("loopback refusal fixture did not bind a port")
        probe_env = dict(env)
        probe_env.pop("FSGG_COORD_TEST_ALLOW_UNFENCED_LOOPBACK_MUTATIONS", None)
        probe_env.update({
            "FSGG_GITHUB_API_BASE": f"http://127.0.0.1:{port}", "GITHUB_TOKEN": "fixture-token",
            "FSGG_COORD_OWNER": "FS-GG", "FSGG_COORD_PROJECT": "Coordination",
            "FSGG_COORD_CACHE": str(cache), "FSGG_WORKER": "",
        })
        result = run([
            str(tool_dir / "fsgg-coord-engine"), "comment", "create", "FS.GG.SDD#43", "FS.GG.SDD#42",
            str(body), "--json", "--worker", "bridge-probe",
        ], env=probe_env, timeout=30)
        text = result.stdout + result.stderr
        if result.returncode == 0 or "production v1 admission is not installed" not in text:
            fail("installed production composition did not fail closed without admission")
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/_fixture/mutations", timeout=5) as response:
            ledger = json.load(response)
        if ledger.get("count") != 0:
            fail("production refusal reached the loopback provider")
    finally:
        server.terminate()
        try:
            server.wait(timeout=5)
        except subprocess.TimeoutExpired:
            server.kill(); server.wait(timeout=5)


def packed_fence_probe(payload: pathlib.Path, env: dict[str, str]) -> None:
    references = [payload / "FS.GG.Coord.Core.dll", payload / "FS.GG.Coord.GitHub.dll"]
    command = ["dotnet", "fsi", "--exec", *(f"--reference:{path}" for path in references), str(HERE / "probe.fsx"), str(payload)]
    result = run(command, env=env, timeout=90)
    if result.returncode:
        fail(f"packed fence probe failed: {result.stdout}{result.stderr}")
    if "packed-fence: PASS" not in result.stdout:
        fail("packed fence probe produced no completion marker")
    print(result.stdout.strip())


def verify(package: pathlib.Path, binding_path: pathlib.Path, identity_only: bool) -> None:
    try:
        binding = json.loads(binding_path.read_text(encoding="utf-8"))
    except Exception as error:
        fail(f"candidate evidence is unreadable: {error}")
    expected = verify_identity(package, binding)
    if identity_only:
        return
    with tempfile.TemporaryDirectory(prefix="fsgg-bridge-execute-") as temporary:
        root = pathlib.Path(temporary)
        tool_dir, payload, env = install_candidate(package, binding, expected, root)
        run_installed_command(tool_dir, env)
        production_refusal(tool_dir, env, root)
        packed_fence_probe(payload, env)
    print(json.dumps(binding, sort_keys=True, separators=(",", ":")))


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    describe_parser = sub.add_parser("describe")
    describe_parser.add_argument("--package", type=pathlib.Path, required=True)
    describe_parser.add_argument("--source-commit", required=True)
    describe_parser.add_argument("--source-tree", required=True)
    verify_parser = sub.add_parser("verify")
    verify_parser.add_argument("--package", type=pathlib.Path, required=True)
    verify_parser.add_argument("--binding", type=pathlib.Path, required=True)
    verify_parser.add_argument("--identity-only", action="store_true")
    args = parser.parse_args()
    package = args.package.resolve()
    if args.command == "describe":
        print(json.dumps(describe(package, args.source_commit, args.source_tree), indent=2, sort_keys=True))
    else:
        verify(package, args.binding.resolve(), args.identity_only)


if __name__ == "__main__":
    main()
