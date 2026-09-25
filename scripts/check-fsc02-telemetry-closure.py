#!/usr/bin/env python3
"""Source/package-boundary characterization for FSC-02, not a Q9 acceptance gate."""

from __future__ import annotations

import argparse
import json
import re
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

HOST = "FS.GG.Telemetry.Host"
CORE = "FS.GG.Coord.Core"
CONTRACTS = "FS.GG.Telemetry.Contracts"
STORE = "FS.GG.Telemetry.Store"
DASHBOARD = "FS.GG.Telemetry.Dashboard"
CLIENT = "FS.GG.Telemetry.Client"
PROJECTS = (HOST, CORE, CONTRACTS, STORE, DASHBOARD, CLIENT)
LEGACY_CANDIDATES = {
    "UsageReceiptStore", "LegacyReceiptProof", "SyntheticCheckpointProof",
    "LifecycleTelemetry", "TelemetrySummary", "CritiqueReceipt", "FeedbackReceipt",
    "RoadmapClosure", "RoadmapProjection",
}
REUSABLE = {"CanonicalJson", "TelemetryStore", "TelemetryReceipt", "TelemetryBudget", "TelemetryCi"}
EXPECTED_STORE_LINKS = {
    "../FS.GG.Coord.Cli/TelemetryStoreApplication.fsi",
    "../FS.GG.Coord.Cli/TelemetryStoreApplication.fs",
}


def project(root: Path, name: str) -> tuple[set[str], list[str]]:
    folder = root / "src" / name
    xml = ET.parse(folder / f"{name}.fsproj").getroot()
    refs = set()
    sources = []
    for entry in xml.iter():
        if entry.tag == "ProjectReference":
            refs.add(Path(entry.attrib["Include"]).stem)
        elif entry.tag == "Compile":
            source = entry.attrib["Include"]
            if not (folder / source).is_file():
                raise ValueError(f"{name}: missing Compile source {source}")
            sources.append(source)
    return refs, sources


def inspect(root: Path, package: Path | None = None) -> dict:
    issues = []
    graph = {}
    sources = {}
    for name in PROJECTS:
        graph[name], sources[name] = project(root, name)
    expected = {HOST, CORE, CONTRACTS, STORE, DASHBOARD}
    closure = set()
    pending = [HOST]
    while pending:
        name = pending.pop()
        if name in closure:
            continue
        closure.add(name)
        pending.extend(graph.get(name, set()))
    if not expected <= closure:
        issues.append(f"Host project closure missing {sorted(expected - closure)}")
    if any(name.startswith("FS.GG.Coord.Cli") for name in closure):
        issues.append("Host project closure includes Coord.Cli assembly")
    if CORE not in graph[CONTRACTS] or CORE not in graph[STORE]:
        issues.append("Contracts and Store must each reference Core")
    if {CONTRACTS, STORE, DASHBOARD} - graph[HOST]:
        issues.append("Host direct telemetry project references changed")
    store_links = {path for path in sources[STORE] if "TelemetryStoreApplication" in path}
    if store_links != EXPECTED_STORE_LINKS:
        issues.append(f"Store historical CLI-path source links changed: {sorted(store_links)}")

    # F# source analysis is lexical by design. It catches new direct host calls
    # into legacy lifecycle modules; it does not infer symbol reachability.
    direct_source = {}
    for name in (HOST, CONTRACTS, STORE):
        folder = root / "src" / name
        text = "\n".join((folder / path).read_text(encoding="utf-8") for path in sources[name])
        direct_source[name] = sorted(module for module in REUSABLE | LEGACY_CANDIDATES
                                     if re.search(rf"\b{module}\b", text))
        suspect = set(direct_source[name]) & LEGACY_CANDIDATES
        if suspect:
            issues.append(f"{name}: direct legacy lifecycle module tokens {sorted(suspect)}")

    mixed_source = (root / "src" / CORE / "Telemetry.fs").read_text(encoding="utf-8")
    mixed_modules = re.findall(r"^module\s+(?:private\s+)?([A-Za-z][A-Za-z0-9]*)\s*=", mixed_source, re.M)
    if "CanonicalJson" not in mixed_modules:
        issues.append("Core Telemetry.fs no longer declares CanonicalJson; requalify source closure")
    unclassified = set(mixed_modules) - REUSABLE - LEGACY_CANDIDATES - {"TelemetryJson", "RuntimeUsage"}
    if unclassified:
        issues.append(f"Core Telemetry.fs has unclassified modules {sorted(unclassified)}")

    allowlist = (root / "tests/standalone-telemetry-host-package/allowed-files.txt").read_text().splitlines()
    required_dlls = {f"{name}.dll" for name in expected}
    listed_dlls = {Path(name).name for name in allowlist if name.endswith(".dll")}
    if not required_dlls <= listed_dlls:
        issues.append(f"Host package allowlist misses {sorted(required_dlls - listed_dlls)}")
    if any(name.startswith("FS.GG.Coord.Cli") for name in listed_dlls):
        issues.append("Host package allowlist includes Coord.Cli assembly")
    package_members = None
    package_deps_libraries = None
    if package is not None:
        with zipfile.ZipFile(package) as archive:
            package_members = archive.namelist()
            deps_names = [name for name in package_members if name.endswith("/FS.GG.Telemetry.Host.deps.json")]
            if len(deps_names) == 1:
                deps = json.loads(archive.read(deps_names[0]))
                package_deps_libraries = sorted((deps.get("libraries") or {}).keys())
            else:
                issues.append("built Host package has no unique Host .deps.json")
        actual_dlls = {Path(name).name for name in package_members if name.endswith(".dll")}
        if not required_dlls <= actual_dlls:
            issues.append(f"built Host package misses {sorted(required_dlls - actual_dlls)}")
        if any(name.startswith("FS.GG.Coord.Cli") for name in actual_dlls):
            issues.append("built Host package includes Coord.Cli assembly")
        if package_deps_libraries is not None and any(
            name.startswith("FS.GG.Coord.Cli") for name in package_deps_libraries
        ):
            issues.append("built Host dependency manifest includes Coord.Cli")

    return {
        "schema": "fsgg.fsc02-telemetry-closure/v1",
        "projectReferences": {name: sorted(refs) for name, refs in graph.items()},
        "hostProjectClosure": sorted(closure),
        "storeLinkedSources": sorted(store_links),
        "directSourceModuleTokens": direct_source,
        "coreMixedTelemetryModules": mixed_modules,
        "reusableModules": sorted(REUSABLE),
        "legacyCandidatesNotDeletionVerdicts": sorted(LEGACY_CANDIDATES),
        "packageAllowlistDlls": sorted(listed_dlls),
        "builtPackageMembers": package_members,
        "builtPackageDependencyLibraries": package_deps_libraries,
        "issues": issues,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--package", type=Path)
    args = parser.parse_args()
    result = inspect(args.root.resolve(), args.package)
    print(json.dumps(result, indent=2, sort_keys=True))
    if result["issues"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
