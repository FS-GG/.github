#!/usr/bin/env python3
"""Normalize retained receiver source captures and validate the census offline."""

from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


CAPTURE_SCHEMA = "fsgg.v1-receiver-source-capture/1"
CENSUS_SCHEMA = "fsgg.v1-writer-receiver-census/1"
EXPECTED = (
    ("sdd", "FS-GG/FS.GG.SDD", "8d648c8deaf1edc16b942d0cfccee722c3a0a24c", "9d1486b51dc3b0b62c6a3ef0272b00f0a1421829", "0.87.0"),
    ("rendering", "FS-GG/FS.GG.Rendering", "86102999e7f60a494bed74e825e36b284fef6d62", "41299fe02be434366da6d876af1955765cbeee2a", "0.75.4"),
    ("governance", "FS-GG/FS.GG.Governance", "e0580e402c07f1c0576183eb8c6433bf6b1185bd", "78fee3a3c372dbeb13be34b3e130d3f2dfd67f54", "0.58.0"),
    ("templates", "FS-GG/FS.GG.Templates", "61091078337689c6ab1aac139bc03f6a07ca8f99", "2fd555ff979b1200a0e03f2f8417b59c953bdc31", "0.75.4"),
    ("game", "FS-GG/FS.GG.Game", "24f79084fdd289f34387f91b1d4398c78fde16eb", "e3dec5593102975031fc4d4a4bf053659db2e2b3", "0.75.4"),
    ("audio", "FS-GG/FS.GG.Audio", "04ca17810c2a00efa20a689405f943a20c06769a", "e24658403dca6a0ac29bc3f5ae854953be88bd66", "0.75.4"),
    ("net", "FS-GG/FS.GG.Net", "e66d38d7ed7fb5cc36984f4d33434bd9c6eaa802", "670a54df53405c8e025f1f0cf1ba0a2e3ba87b48", "0.75.4"),
)
EXPECTED_POPULATIONS = {
    "sdd": (2715, 142), "rendering": (4801, 296), "governance": (3612, 246),
    "templates": (505, 136), "game": (496, 131), "audio": (233, 71), "net": (158, 56),
}
EXPECTED_EFFECT_COUNTS = {"conditional": 197, "inert": 353, "protected-admin": 27, "read-only": 38}
ROUTE_PATTERNS = (
    ("merge", re.compile(r"\bgh\s+pr\s+merge\b")),
    ("git-push", re.compile(r"\bgit\s+push\b")),
    ("package-publish", re.compile(r"\bdotnet\s+nuget\s+push\b|\bgh\s+release\s+(?:create|upload|edit|delete)\b")),
    ("rest-api", re.compile(r"\bgh\s+api\b")),
    ("coordination-engine", re.compile(r"\bfsgg-coord(?:-engine)?\b")),
    ("dispatch", re.compile(r"\brepository_dispatch\b|dispatch-sender\.yml@")),
    ("delegated-materializer", re.compile(r"\bkit-materialize\b")),
)
EXTERNAL_USE = re.compile(r"^\s*(?:-\s*)?uses:\s*([^\s#]+)@([^\s#]+)", re.M)
LOCAL_USE = re.compile(r"^\s*(?:-\s*)?uses:\s*(\./[^\s#]+)", re.M)
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA64 = re.compile(r"^[0-9a-f]{64}$")


class CensusError(RuntimeError):
    pass


def load(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise CensusError(f"{path} is unreadable or malformed: {error}") from error
    if not isinstance(value, dict):
        raise CensusError(f"{path} must contain one JSON object")
    return value


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def stable(*values: str) -> str:
    return hashlib.sha256("\0".join(values).encode()).hexdigest()[:24]


def decode_source(row: dict[str, Any]) -> bytes:
    try:
        data = base64.b64decode(row["bytesBase64"], validate=True)
    except (KeyError, ValueError) as error:
        raise CensusError(f"source bytes are malformed for {row.get('path')}") from error
    if digest(data) != row.get("sha256"):
        raise CensusError(f"source byte digest differs for {row.get('path')}")
    return data


def effect(kind: str, line: str, text: str) -> str:
    if kind in {"merge", "package-publish"}:
        return "protected-admin"
    if kind == "rest-api":
        return "conditional" if re.search(r"(?:--method|-X)\s+(?:POST|PUT|PATCH|DELETE)|(?:^|\s)-[fF]\s", text, re.I) else "read-only"
    if kind in {"git-push", "coordination-engine", "dispatch", "delegated-materializer"}:
        return "conditional"
    if "telemetry" in text.lower():
        return "local-only"
    return "inert"


def normalize(capture: dict[str, Any]) -> dict[str, Any]:
    if capture.get("schema") != CAPTURE_SCHEMA or capture.get("terminal") is not True:
        raise CensusError("capture is not complete terminal receiver source evidence")
    receivers: list[dict[str, Any]] = []
    sources: list[dict[str, Any]] = []
    routes: list[dict[str, Any]] = []
    dependencies: list[dict[str, Any]] = []
    blobs: dict[str, str] = {}
    observed_callees = {(row.get("repository"), row.get("path"), row.get("revision")): row for row in capture.get("reviewedCallees", [])}
    for receiver in capture.get("receivers", []):
        receiver_id = receiver.get("id")
        summary = {key: receiver[key] for key in ("id", "repository", "revision", "tree", "installedCoordCliVersion", "sourceManifestSha256")}
        summary.update({"trackedEntryCount": len(receiver.get("manifest", [])), "relevantCandidateCount": len(receiver.get("relevantSources", []))})
        receivers.append(summary)
        for source in receiver.get("relevantSources", []):
            data = decode_source(source)
            text = data.decode("utf-8", errors="replace")
            matches: list[tuple[str, int, str]] = []
            for number, line in enumerate(text.splitlines(), 1):
                for kind, pattern in ROUTE_PATTERNS:
                    if pattern.search(line):
                        matches.append((kind, number, line.strip()))
            uses = [(match.group(1), match.group(2)) for match in EXTERNAL_USE.finditer(text)]
            local_uses = [match.group(1) for match in LOCAL_USE.finditer(text)]
            pin_subject = source["path"].endswith(("dotnet-tools.json", ".props", ".targets", ".fsproj"))
            if not matches and not uses and not local_uses and not pin_subject:
                continue
            source_id = f"{receiver_id}:{source['path']}:{source['sha256'][:16]}"
            blobs[source["sha256"]] = base64.b64encode(data).decode()
            sources.append({"id": source_id, "receiver": receiver_id, "path": source["path"], "blobSha1": source["blobSha1"], "sha256": source["sha256"]})
            credential = "workflow-token-or-declared-secret" if re.search(r"GH_TOKEN|github\.token|secrets\.", text) else "none-observed"
            grouped: dict[tuple[str, str], list[str]] = {}
            for kind, number, line in matches:
                route_effect = effect(kind, line, text)
                grouped.setdefault((kind, route_effect), []).append(f"{source['path']}:{number}")
            for (kind, route_effect), callsites in sorted(grouped.items()):
                routes.append({
                    "id": stable(str(receiver_id), source["path"], kind), "receiver": receiver_id,
                    "sourceId": source_id, "entrypoint": source["path"], "callsites": callsites,
                    "effectClass": route_effect, "condition": "source-controlled-or-operator-invoked",
                    "remoteEffects": [kind] if route_effect in {"always", "conditional", "protected-admin"} else [],
                    "credentialBoundary": credential, "q0Mapping": "legacy-v1-route",
                    "laterDisposition": "GS2-08.4-fence" if route_effect in {"always", "conditional", "protected-admin"} else "GS2-08.9-retain-or-retire",
                })
            if not matches and ("telemetry" in source["path"].lower() or pin_subject):
                route_effect = "local-only" if "telemetry" in source["path"].lower() else "inert"
                routes.append({
                    "id": stable(str(receiver_id), source["path"], "summary", route_effect), "receiver": receiver_id,
                    "sourceId": source_id, "entrypoint": source["path"], "callsites": [f"{source['path']}:source"],
                    "effectClass": route_effect, "condition": "installed-source-presence", "remoteEffects": [],
                    "credentialBoundary": credential, "q0Mapping": "post-q0-source-identity",
                    "laterDisposition": "GS2-08.9-retain-or-retire",
                })
            for target, reference in uses:
                if target.startswith(("FS-GG/", "./")):
                    dependency = {"receiver": receiver_id, "sourceId": source_id, "target": target, "reference": reference, "resolution": "immutable" if SHA40.fullmatch(reference) else "mutable-source-reference"}
                    if target.startswith("FS-GG/.github/"):
                        callee_path = target.removeprefix("FS-GG/.github/")
                        revision = reference if SHA40.fullmatch(reference) else "c1ad41e059c18376685b5c71256149c35c341652"
                        callee = observed_callees.get(("FS-GG/.github", callee_path, revision))
                        if callee:
                            dependency.update({"resolution": "immutable" if SHA40.fullmatch(reference) else "observed-mutable", "resolvedRevision": revision, "calleeSha256": callee["sha256"]})
                    dependencies.append(dependency)
            for target in local_uses:
                dependencies.append({"receiver": receiver_id, "sourceId": source_id, "target": target, "reference": receiver["revision"], "resolution": "receiver-tree"})
    tool_sources = []
    for row in capture.get("toolSources", []):
        options = row.get("options", {})
        project = row.get("project", {})
        decode_source(options); decode_source(project)
        blobs[options["sha256"]] = options["bytesBase64"]
        blobs[project["sha256"]] = project["bytesBase64"]
        tool_sources.append({"version": row["version"], "repository": row["repository"], "revision": row["revision"], "tree": row["tree"], "optionsPath": options["path"], "optionsSha256": options["sha256"], "projectPath": project["path"], "projectSha256": project["sha256"]})
    callees = []
    for row in capture.get("reviewedCallees", []):
        decode_source(row); blobs[row["sha256"]] = row["bytesBase64"]
        callees.append({key: row[key] for key in ("repository", "revision", "path", "blobSha1", "sha256")})
    sources.sort(key=lambda row: (row["receiver"], row["path"]))
    routes.sort(key=lambda row: (row["receiver"], row["entrypoint"], row["id"]))
    dependencies.sort(key=lambda row: (row["receiver"], row["sourceId"], row["target"], row["reference"]))
    return {
        "schema": CENSUS_SCHEMA, "captureSource": capture.get("source"), "terminal": True,
        "claims": {"installed": False, "fenced": False, "accepted": False},
        "telemetryBoundary": {"classification": "local-only", "sourceRead": False, "privateCorpusRead": False},
        "receivers": receivers, "sourceIdentities": sources, "sourceBlobDigests": sorted(blobs),
        "writerRoutes": routes, "callableDependencies": dependencies, "installedTools": tool_sources,
        "reviewedCallees": callees,
    }


def source_blob_bundle(capture: dict[str, Any]) -> dict[str, Any]:
    normalized = normalize(capture)
    required = set(normalized["sourceBlobDigests"])
    blobs: dict[str, str] = {}
    for receiver in capture.get("receivers", []):
        for source in receiver.get("relevantSources", []):
            if source.get("sha256") in required:
                blobs[source["sha256"]] = source["bytesBase64"]
    for tool in capture.get("toolSources", []):
        for name in ("options", "project"):
            row = tool[name]; blobs[row["sha256"]] = row["bytesBase64"]
    for row in capture.get("reviewedCallees", []):
        blobs[row["sha256"]] = row["bytesBase64"]
    return {"schema": "fsgg.v1-receiver-source-blobs/1", "blobs": [{"sha256": sha, "bytesBase64": blobs[sha]} for sha in sorted(blobs)]}


def retained_capture(capture: dict[str, Any]) -> dict[str, Any]:
    value = json.loads(json.dumps(capture))
    for receiver in value.get("receivers", []):
        for source in receiver.get("relevantSources", []):
            source.pop("bytesBase64", None)
    for tool in value.get("toolSources", []):
        tool.get("options", {}).pop("bytesBase64", None)
        tool.get("project", {}).pop("bytesBase64", None)
    for callee in value.get("reviewedCallees", []):
        callee.pop("bytesBase64", None)
    value["schema"] = "fsgg.v1-receiver-source-manifests/1"
    value["relevantBytesRetainedIn"] = "docs/coordination/v1-writer-receiver-census.json#sourceBlobs"
    return value


def write_gzip_json(path: Path, value: dict[str, Any]) -> None:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    with path.open("wb") as output:
        with gzip.GzipFile(filename="", mode="wb", fileobj=output, mtime=0) as compressed:
            compressed.write(encoded)


def unique(rows: list[dict[str, Any]], key: str, label: str) -> None:
    values = [row.get(key) for row in rows]
    if any(not isinstance(value, str) or not value for value in values) or len(values) != len(set(values)):
        raise CensusError(f"{label} identity is missing or duplicated")


def validate(census: dict[str, Any], evidence: dict[str, Any] | None = None, blob_bundle: dict[str, Any] | None = None) -> tuple[int, int, int]:
    if census.get("schema") != CENSUS_SCHEMA or census.get("terminal") is not True:
        raise CensusError("receiver census schema or terminal completeness differs")
    if census.get("claims") != {"installed": False, "fenced": False, "accepted": False}:
        raise CensusError("source snapshot must not claim installation, fencing, or acceptance")
    if census.get("telemetryBoundary") != {"classification": "local-only", "sourceRead": False, "privateCorpusRead": False}:
        raise CensusError("telemetry must remain explicitly local-only without source or private-corpus inspection")
    receivers = census.get("receivers")
    if not isinstance(receivers, list) or [(r.get("id"), r.get("repository"), r.get("revision"), r.get("tree"), r.get("installedCoordCliVersion")) for r in receivers] != list(EXPECTED):
        raise CensusError("receiver roster, order, revision, tree, or installed tool pin differs")
    for row in receivers:
        if not SHA64.fullmatch(str(row.get("sourceManifestSha256"))):
            raise CensusError(f"receiver source manifest is incomplete: {row.get('id')}")
        if (row.get("trackedEntryCount"), row.get("relevantCandidateCount")) != EXPECTED_POPULATIONS[row["id"]]:
            raise CensusError(f"receiver tracked or relevant source population differs: {row.get('id')}")
    if evidence is not None:
        if evidence.get("schema") != "fsgg.v1-receiver-source-manifests/1" or evidence.get("terminal") is not True:
            raise CensusError("retained source-manifest evidence is malformed or nonterminal")
        evidence_receivers = evidence.get("receivers")
        if not isinstance(evidence_receivers, list) or len(evidence_receivers) != len(EXPECTED):
            raise CensusError("retained source-manifest evidence omits a receiver")
        for summary, observed in zip(receivers, evidence_receivers, strict=True):
            manifest = observed.get("manifest")
            if not isinstance(manifest, list) or not manifest or observed.get("id") != summary.get("id"):
                raise CensusError(f"retained manifest is absent or crossed: {summary.get('id')}")
            actual = digest(json.dumps(manifest, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode())
            if actual != summary.get("sourceManifestSha256"):
                raise CensusError(f"retained full source manifest digest differs: {summary.get('id')}")
    blob_digests = census.get("sourceBlobDigests")
    sources = census.get("sourceIdentities")
    routes = census.get("writerRoutes")
    dependencies = census.get("callableDependencies")
    if not all(isinstance(value, list) for value in (blob_digests, sources, routes, dependencies)):
        raise CensusError("census arrays are malformed")
    if (len(sources), len(blob_digests), len(routes), len(dependencies)) != (555, 460, 615, 95):
        raise CensusError("complete source/blob/route/dependency population differs")
    unique(sources, "id", "source"); unique(routes, "id", "writer route")
    if sources != sorted(sources, key=lambda row: (row["receiver"], row["path"])) or routes != sorted(routes, key=lambda row: (row["receiver"], row["entrypoint"], row["id"])):
        raise CensusError("receiver source or route rows are reordered")
    blob_map: dict[str, bytes] = {}
    if not isinstance(blob_bundle, dict) or blob_bundle.get("schema") != "fsgg.v1-receiver-source-blobs/1":
        raise CensusError("retained source-byte bundle is missing or malformed")
    for row in blob_bundle.get("blobs", []):
        sha = row.get("sha256")
        try: data = base64.b64decode(row.get("bytesBase64", ""), validate=True)
        except ValueError as error: raise CensusError("retained source blob is malformed") from error
        if not isinstance(sha, str) or digest(data) != sha or sha in blob_map:
            raise CensusError("retained source blob identity differs or duplicates")
        blob_map[sha] = data
    if sorted(blob_map) != blob_digests:
        raise CensusError("retained source-byte bundle is incomplete, extra, or reordered")
    source_ids = {row["id"] for row in sources}
    for row in sources:
        if row.get("sha256") not in blob_map or row.get("receiver") not in {value[0] for value in EXPECTED}:
            raise CensusError(f"source bytes or receiver binding missing: {row.get('id')}")
    allowed_effects = {"always", "conditional", "read-only", "protected-admin", "local-only", "inert"}
    for row in routes:
        if row.get("sourceId") not in source_ids or row.get("effectClass") not in allowed_effects:
            raise CensusError(f"writer route source/effect is malformed: {row.get('id')}")
        if row.get("remoteEffects") and row.get("effectClass") in {"read-only", "local-only", "inert"}:
            raise CensusError(f"remote writer was laundered as non-writer: {row.get('id')}")
        if not isinstance(row.get("callsites"), list) or not row["callsites"] or len(row["callsites"]) != len(set(row["callsites"])):
            raise CensusError(f"writer route callsites are missing, duplicated, or reordered: {row.get('id')}")
    effect_counts = {effect: sum(1 for row in routes if row.get("effectClass") == effect) for effect in allowed_effects}
    if {key: value for key, value in effect_counts.items() if value} != EXPECTED_EFFECT_COUNTS:
        raise CensusError("exact writer effect classification population differs")
    if not any(row.get("receiver") == "sdd" and row.get("effectClass") == "conditional" and "kit-materialize" in row.get("entrypoint", "") for row in routes):
        raise CensusError("SDD read-only caller does not retain its delegated materializer writer route")
    if not any(row.get("receiver") == "rendering" and row.get("target") == "FS-GG/.github/.github/workflows/dispatch-sender.yml" and row.get("reference") == "5fed2838f9ed085ffca09f4cc18b4f7bc59c1294" for row in dependencies):
        raise CensusError("Rendering historical dispatch callee is missing")
    tools = census.get("installedTools")
    if not isinstance(tools, list) or [row.get("version") for row in tools] != ["0.58.0", "0.75.4", "0.87.0"]:
        raise CensusError("exact legacy/current tool source correspondence is missing")
    if any(row.get("resolution") == "mutable-source-reference" and str(row.get("target", "")).startswith("FS-GG/") for row in dependencies):
        raise CensusError("FS-GG callable dependency retains an unresolved mutable ref")
    for row in dependencies:
        if row.get("resolution") == "observed-mutable" and (row.get("resolvedRevision") != "c1ad41e059c18376685b5c71256149c35c341652" or not SHA64.fullmatch(str(row.get("calleeSha256")))):
            raise CensusError("mutable FS-GG callee lacks exact observed source correspondence")
    return len(receivers), len(sources), len(routes)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--capture", type=Path)
    parser.add_argument("--normalize", type=Path)
    parser.add_argument("--evidence", type=Path, default=Path(__file__).resolve().parents[1] / "tests/v1-receiver-census/evidence/source-manifests.json.gz")
    parser.add_argument("--blobs", type=Path, default=Path(__file__).resolve().parents[1] / "tests/v1-receiver-census/evidence/source-blobs.json.gz")
    parser.add_argument("--census", type=Path, default=Path(__file__).resolve().parents[1] / "docs/coordination/v1-writer-receiver-census.json")
    args = parser.parse_args()
    try:
        if args.capture:
            if not args.normalize:
                raise CensusError("--capture requires --normalize OUTPUT")
            value = normalize(load(args.capture))
            args.normalize.parent.mkdir(parents=True, exist_ok=True)
            args.normalize.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
            args.evidence.parent.mkdir(parents=True, exist_ok=True)
            write_gzip_json(args.evidence, retained_capture(load(args.capture)))
            write_gzip_json(args.blobs, source_blob_bundle(load(args.capture)))
        else:
            value = load(args.census)
        with gzip.open(args.evidence, "rt", encoding="utf-8") as stream: evidence = json.load(stream)
        with gzip.open(args.blobs, "rt", encoding="utf-8") as stream: blob_bundle = json.load(stream)
        receiver_count, source_count, route_count = validate(value, evidence, blob_bundle)
    except (CensusError, OSError, UnicodeError) as error:
        print(f"v1 receiver census refused: {error}", file=sys.stderr)
        return 1
    print(f"V1_RECEIVER_CENSUS_OK receivers={receiver_count} sources={source_count} routes={route_count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
