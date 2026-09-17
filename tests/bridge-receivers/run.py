#!/usr/bin/env python3
"""Verify one explicit GS2-08.8 receiver against the public coherent bridge."""

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
import xml.etree.ElementTree as ET
import zipfile

EXPECTED_SCHEMA = "fsgg.gs2-08.8-receiver-adoption/1"
EXPECTED_RECEIVER = "FS-GG/.github"
EXPECTED_SOURCE = "3adada5a9738464291088830c47a30a3a8fc9561"
EXPECTED_TREE = "0f075e251d90a2d33efe556df1dac38394b0a388"
EXPECTED_CONTENT = "sha256:52b2774de277855c16a3c0852bc5113deea13d9076b866e6a7de0acc84f4b9c4"
EXPECTED_MANIFEST_SHA = "1bbb77f3de10ba3116f9de2ea1df3f5edee38be0de07fa0eaf7c173da3a8456a"
EXPECTED_ROUTES = {
    "dist/dotnet/.config/dotnet-tools.json": ("bridge-adopted", "31bcff6fe195cb13c6ade2389cbdc8a5f5ebdde590f99b6923399ce445189f5e"),
    "tests/bridge-receivers/run.sh": ("read-only-local-only", "864dbc6a85042b8d0dad62fad9b93dfee75e8f8b8d2ebf9e8613e1e6bc45531d"),
    ".github/workflows/release-saga-tooling.yml": ("read-only-local-only", "4a9d96d5647ab067ae70c5ff22fa159f7235f4e356e8c1dab02802b0e1049463"),
    "docs/coordination/v1-writer-receiver-census.json": ("gs2-08.9-sealing", "3d7de0dee094991e08aed09b7478ea1191d6ee7f50baaad0087975a88e8f90db"),
}
DISPOSITIONS = {"bridge-adopted", "read-only-local-only", "gs2-08.9-sealing"}
SERVER = pathlib.Path(__file__).resolve().parents[1] / "coord-engine-e2e/stateful_server.py"


def fail(message: str) -> "None":
    raise SystemExit(f"bridge-receivers: {message}")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: pathlib.Path) -> str:
    return sha256_bytes(path.read_bytes())


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def run(command: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, text=True, capture_output=True, check=False, **kwargs)


def package_metadata(path: pathlib.Path) -> tuple[str, str, str, str, set[str]]:
    if not path.is_file():
        fail(f"missing public package: {path.name}")
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        nuspecs = [name for name in names if name.endswith(".nuspec") and "/" not in name]
        if len(nuspecs) != 1:
            fail(f"{path.name} must contain exactly one root nuspec")
        root = ET.fromstring(archive.read(nuspecs[0]))
        namespace = root.tag.partition("}")[0].lstrip("{")
        prefix = f"{{{namespace}}}" if namespace else ""
        metadata = root.find(f"{prefix}metadata")
        if metadata is None:
            fail(f"{path.name} has no package metadata")
        package_id = metadata.findtext(f"{prefix}id") or ""
        version = metadata.findtext(f"{prefix}version") or ""
        repository = metadata.find(f"{prefix}repository")
        source = "" if repository is None else repository.attrib.get("commit", "")
        normalized: dict[str, str] = {}
        for name in sorted(names):
            lowered = name.lower()
            if name.endswith("/") or lowered == ".signature.p7s" or lowered.endswith(".psmdcp"):
                continue
            value = archive.read(name)
            if lowered.endswith(".rels") and (lowered.startswith("_rels/") or "/_rels/" in lowered):
                try:
                    relationships = ET.fromstring(value)
                    rows = []
                    for node in relationships:
                        attributes = dict(node.attrib)
                        if node.tag.rsplit("}", 1)[-1] == "Relationship" and attributes.get("Target", "").lower().endswith(".psmdcp"):
                            continue
                        rows.append([node.tag.rsplit("}", 1)[-1], attributes])
                    value = canonical(rows)
                except ET.ParseError:
                    pass
            normalized[name] = sha256_bytes(value)
    return package_id, version, source, "sha256:" + sha256_bytes(canonical(normalized)), set(names)


def load(path: pathlib.Path, label: str) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except Exception as error:
        fail(f"{label} is unreadable: {error}")
    if not isinstance(value, dict):
        fail(f"{label} must be an object")
    return value


def validate_receiver(root: pathlib.Path, revision: str, tree: str) -> None:
    if len(revision) != 40 or len(tree) != 40:
        fail("receiver revision and tree must be full Git identities")
    actual = run(["git", "-C", str(root), "rev-parse", f"{revision}^{{tree}}"])
    if actual.returncode or actual.stdout.strip() != tree:
        fail("receiver revision/tree mismatch")


def git_file(root: pathlib.Path, revision: str, path: str) -> bytes:
    result = subprocess.run(["git", "-C", str(root), "show", f"{revision}:{path}"], capture_output=True)
    if result.returncode:
        fail(f"receiver revision is missing selected path: {path}")
    return result.stdout


def validate(evidence_path: pathlib.Path, manifest_path: pathlib.Path, packages: pathlib.Path,
             root: pathlib.Path, revision: str, tree: str, bind_tree: bool = False) -> dict[str, pathlib.Path]:
    validate_receiver(root, revision, tree)
    evidence = load(evidence_path, "receiver evidence")
    required = {"schema", "receiver", "release", "packages", "installedCommand", "routes", "callableRoutes", "qualification", "remainingBoundary"}
    if set(evidence) != required or evidence.get("schema") != EXPECTED_SCHEMA:
        fail("receiver evidence has an incomplete or unsupported schema")
    if evidence.get("receiver") != EXPECTED_RECEIVER:
        fail("missing or wrong receiver identity")
    if bind_tree and git_file(root, revision, "docs/reports/gs2-08-8-bridge-adoption.json") != evidence_path.read_bytes():
        fail("receiver evidence does not match the supplied revision/tree")

    manifest = load(manifest_path, "release manifest")
    release = evidence["release"]
    if sha256_file(manifest_path) != EXPECTED_MANIFEST_SHA or release.get("manifestSha256") != EXPECTED_MANIFEST_SHA:
        fail("release manifest digest mismatch")
    descriptor = manifest.get("descriptor", {})
    if (manifest.get("contentId") != EXPECTED_CONTENT or release.get("contentId") != EXPECTED_CONTENT or
            descriptor.get("sourceSha") != EXPECTED_SOURCE or release.get("sourceCommit") != EXPECTED_SOURCE or
            descriptor.get("bridgeQualification", {}).get("sourceTree") != EXPECTED_TREE or release.get("sourceTree") != EXPECTED_TREE or
            descriptor.get("version") != "0.90.0" or release.get("version") != "0.90.0"):
        fail("release identity does not match the accepted public bridge")

    manifest_packages = {row["id"]: row for row in descriptor.get("packages", [])}
    evidence_packages = {row["id"]: row for row in evidence["packages"]}
    coherent_ids = {"FS.GG.Coord.Cli", "FS.GG.Drivers", "FS.GG.Kit"}
    expected_ids = {"FS.GG.Coord.Cli", "FS.GG.Kit"}
    if set(manifest_packages) != coherent_ids or set(evidence_packages) != expected_ids:
        fail("coherent package set is incomplete")
    public_paths: dict[str, pathlib.Path] = {}
    for package_id in sorted(expected_ids):
        row = evidence_packages[package_id]
        published = manifest["state"]["feeds"]["nuget"]["packages"][package_id]
        if (row.get("version") != "0.90.0" or row.get("feed") != "https://api.nuget.org/v3/index.json" or
                row.get("payloadSha256") != manifest_packages[package_id]["artifact"]["payloadSha256"]):
            fail(f"wrong coherent identity for {package_id}")
        if row.get("publicArchiveSha256") != published.get("externalSha256") or published.get("state") != "verified":
            fail(f"wrong public archive identity for {package_id}")
        path = packages / f"{package_id.lower()}.0.90.0.nupkg"
        if sha256_file(path) != row["publicArchiveSha256"]:
            fail(f"substituted public package: {package_id}")
        actual_id, version, source, payload, names = package_metadata(path)
        if actual_id != package_id or version != "0.90.0" or source != EXPECTED_SOURCE or payload != row["payloadSha256"]:
            fail(f"public package metadata or payload mismatch: {package_id}")
        if package_id == "FS.GG.Kit" and "kit/kit-manifest.tsv" not in names:
            fail("public Kit package is missing its manifest")
        public_paths[package_id] = path

    if evidence_packages["FS.GG.Coord.Cli"].get("pinPath") != "dist/dotnet/.config/dotnet-tools.json" or evidence_packages["FS.GG.Kit"].get("pinPath") != "Directory.Build.props":
        fail("selected receiver pin is missing, old, or ambiguous")
    cli_pin = evidence_packages["FS.GG.Coord.Cli"]["pinPath"]
    if bind_tree:
        try:
            tool_manifest = json.loads(git_file(root, revision, cli_pin))
        except Exception as error:
            fail(f"selected tool manifest is unreadable in receiver revision: {error}")
    else:
        tool_manifest = load(root / cli_pin, "selected tool manifest")
    actual_pin = tool_manifest.get("tools", {}).get("fs.gg.coord.cli", {}).get("version")
    if actual_pin != "0.90.0":
        fail("selected receiver still has an old CLI")
    kit_pin = evidence_packages["FS.GG.Kit"]["pinPath"]
    kit_bytes = git_file(root, revision, kit_pin) if bind_tree else (root / kit_pin).read_bytes()
    if b"<FsggCoherentSetVersion>0.90.0</FsggCoherentSetVersion>" not in kit_bytes:
        fail("selected receiver still has an old Kit")

    routes = evidence["routes"]
    if len(routes) != len(EXPECTED_ROUTES) or {row.get("entrypoint") for row in routes} != set(EXPECTED_ROUTES):
        fail("receiver has a missing, duplicate, or unaccounted route")
    for row in routes:
        entrypoint = row["entrypoint"]
        disposition, digest = EXPECTED_ROUTES[entrypoint]
        if row.get("disposition") != disposition or row.get("disposition") not in DISPOSITIONS or row.get("sha256") != digest:
            fail(f"invalid route disposition or identity: {entrypoint}")
        path_bytes = git_file(root, revision, entrypoint) if bind_tree else (root / entrypoint).read_bytes()
        if sha256_bytes(path_bytes) != digest:
            fail(f"route bytes do not match the receiver manifest: {entrypoint}")

    callable_routes = evidence["callableRoutes"]
    if len(callable_routes) != 1:
        fail("receiver has a missing, duplicate, or unaccounted callable route")
    callable_route = callable_routes[0]
    if (callable_route.get("caller") != ".github/workflows/release-saga-tooling.yml" or
            callable_route.get("target") != "FS-GG/.github/tests/bridge-receivers/run.sh" or
            callable_route.get("ref") != "receiver-revision" or
            callable_route.get("observedRevision") != "qualification-input" or
            callable_route.get("disposition") != "read-only-local-only" or
            callable_route.get("callerSha256") != EXPECTED_ROUTES[".github/workflows/release-saga-tooling.yml"][1] or
            callable_route.get("workflowSha256") != EXPECTED_ROUTES["tests/bridge-receivers/run.sh"][1]):
        fail("mutable callable dependency is missing its receiver revision binding")

    if evidence["installedCommand"] != {
        "command": "${TOOL_PATH}/fsgg-coord-engine --version",
        "versionOutput": "0.90.0.0",
        "executablePath": "${TOOL_PATH}/.store/fs.gg.coord.cli/0.90.0/fs.gg.coord.cli/0.90.0/tools/net10.0/any/fsgg-coord-engine.dll",
        "executableSha256": "c37d76fa42483e76b91275d8764bdcb04881cb9f3643c6e9fe9b0df3914d3512",
        "gitHubAssemblySha256": "a11ef52027b9f4485ffc0f959ebf3592002d7b5338723a90c3aaa1240ea9ff49",
        "resolution": "The isolated public restore supplies the wrapper only under TOOL_PATH; its exact packaged engine and GitHub assemblies are verified before execution.",
    }:
        fail("installed command identity or location is incomplete")

    qualification = evidence["qualification"]
    if (qualification.get("publicRestore") is not True or qualification.get("installedCommand") is not True or
            qualification.get("productionMutation") != "refused-unavailable-fence" or
            qualification.get("network") != "loopback-only" or qualification.get("credentials") != "fake" or
            qualification.get("mutationRequests") != 0 or not qualification.get("probe")):
        fail("receiver qualification outcomes are incomplete")
    if "GS2-08.9" not in evidence["remainingBoundary"]:
        fail("remaining receiver boundary does not retain GS2-08.9 sealing")
    return public_paths


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


def execute(cli: pathlib.Path) -> None:
    with tempfile.TemporaryDirectory(prefix="fsgg-bridge-receiver-execute-") as temporary:
        root = pathlib.Path(temporary)
        tool = root / "tool"
        tool.mkdir()
        config = root / "NuGet.config"
        config.write_text('<?xml version="1.0" encoding="utf-8"?>\n<configuration><packageSources><clear />'
                          '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
                          '</packageSources></configuration>\n')
        env = private_environment(root)
        installed = run(["dotnet", "tool", "install", "FS.GG.Coord.Cli", "--version", "0.90.0",
                         "--tool-path", str(tool), "--configfile", str(config), "--no-cache"], env=env, timeout=120)
        if installed.returncode:
            fail(f"public CLI restore failed: {installed.stdout}{installed.stderr}")
        command = (tool / "fsgg-coord-engine").resolve()
        if command.parent != tool.resolve() or not command.is_file():
            fail("installed executable escaped or is absent from the selected tool directory")
        payloads = list((tool / ".store/fs.gg.coord.cli/0.90.0").glob("**/tools/net10.0/any"))
        if len(payloads) != 1:
            fail("installed CLI payload location is missing or ambiguous")
        with zipfile.ZipFile(cli) as archive:
            for assembly in ("FS.GG.Coord.Core.dll", "FS.GG.Coord.GitHub.dll", "fsgg-coord-engine.dll"):
                installed_assembly = payloads[0] / assembly
                packed = archive.read(f"tools/net10.0/any/{assembly}")
                if not installed_assembly.is_file() or sha256_bytes(installed_assembly.read_bytes()) != sha256_bytes(packed):
                    fail(f"installed CLI assembly does not match the public package: {assembly}")
        version_result = run([str(command), "--version"], env=env, timeout=30)
        if version_result.returncode or version_result.stdout.strip() != "0.90.0.0":
            fail("installed public command did not execute the exact expected version")

        output, body, cache = root / "server.out", root / "body.txt", root / "coord-cache"
        body.write_text("receiver-refusal", encoding="utf-8"); cache.mkdir()
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
                fail("loopback fixture did not bind")
            probe_env = dict(env)
            probe_env.update({
                "FSGG_GITHUB_API_BASE": f"http://127.0.0.1:{port}", "GITHUB_TOKEN": "fake-receiver-token",
                "FSGG_COORD_OWNER": "FS-GG", "FSGG_COORD_PROJECT": "Coordination",
                "FSGG_COORD_CACHE": str(cache), "FSGG_WORKER": "",
            })
            probe_env.pop("FSGG_COORD_TEST_ALLOW_UNFENCED_LOOPBACK_MUTATIONS", None)
            refusal = run([str(command), "comment", "create", "FS.GG.SDD#43", "FS.GG.SDD#42", str(body),
                           "--json", "--worker", "bridge-receiver"], env=probe_env, timeout=30)
            if refusal.returncode == 0 or "production v1 admission is not installed" not in refusal.stdout + refusal.stderr:
                fail("UnavailableProductionMutationFence did not refuse fake receiver credentials")
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/_fixture/mutations", timeout=5) as response:
                if json.load(response).get("count") != 0:
                    fail("refused receiver command reached the loopback provider")
        finally:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill(); server.wait(timeout=5)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("validate", "qualify"))
    parser.add_argument("--receiver-root", type=pathlib.Path, required=True)
    parser.add_argument("--receiver-revision", required=True)
    parser.add_argument("--receiver-tree", required=True)
    parser.add_argument("--evidence", type=pathlib.Path, required=True)
    parser.add_argument("--release-manifest", type=pathlib.Path, required=True)
    parser.add_argument("--packages", type=pathlib.Path, required=True)
    args = parser.parse_args()
    paths = validate(args.evidence, args.release_manifest, args.packages, args.receiver_root,
                     args.receiver_revision, args.receiver_tree, bind_tree=args.command == "qualify")
    if args.command == "qualify":
        execute(paths["FS.GG.Coord.Cli"])
    print(f"bridge-receivers: {args.command} passed for {args.receiver_revision}/{args.receiver_tree}")


if __name__ == "__main__":
    main()
