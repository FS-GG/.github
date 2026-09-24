#!/usr/bin/env python3
"""Independent structural and executed evidence validator for GS2-08.6."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
ORACLE_PATH = ROOT / "tests/producer-fence-attacks/oracle.json"
CENSUS_PATH = ROOT / "docs/coordination/v1-writer-census.json"
RECEIVER_PATH = ROOT / "docs/coordination/v1-writer-receiver-census.json"
LEGACY_PATH = ROOT / "tests/producer-fence-attacks/legacy-probe-evidence.json"
EXTERNAL_PATH = ROOT / "tests/producer-fence-attacks/external-route-evidence.json"
READ_ONLY_SUCCESSORS_PATH = ROOT / "tests/producer-fence-attacks/read-only-route-successors.json"
GS2089_RELEASE_PATH = ROOT / "docs/reports/gs2-08-9-release-route-dispositions.json"
CLIENT_PATH = ROOT / "src/FS.GG.Coord.Cli/Client.fs"
WRITE_FIXTURE_PATH = ROOT / "tests/coord-engine-e2e/writes.sh"
GLOBAL_JSON = ROOT / "global.json"


def fail(message: str) -> None:
    raise SystemExit(f"producer-fence-attacks: {message}")


def file_sha(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_kit_read_only_successor(historical: dict[str, object], successor: dict[str, object], source: bytes) -> None:
    """Admit a changed Kit route only while its current bytes retain the GS2-08.9 capability loss."""
    path = ".github/workflows/kit-materialize.yml"
    expected_proof = (
        "contents-read-only; no job permission override, App token, secret read, "
        "provider write command, or materialize job"
    )
    if successor.get("path") != path or successor.get("predecessorSha256") != historical.get("sha256"):
        fail("Kit successor does not bind the accepted historical route")
    if successor.get("sha256") != hashlib.sha256(source).hexdigest():
        fail("Kit successor does not bind the current route bytes")
    if successor.get("disposition") != "current-revision-read-only" or successor.get("proof") != expected_proof:
        fail("Kit successor does not attest the retained read-only disposition")

    # Parse the live indentation structure. This workflow is fixed to a small YAML shape, so an
    # unknown job, permission form, or action is a refusal. Comment-only historical prose is not
    # authority; the accepted old evidence and candidate census cannot make a new writer safe.
    lines = [line.rstrip() for line in source.decode("utf-8").splitlines()
             if line.strip() and not line.lstrip().startswith("#")]
    def block(key: str) -> list[str]:
        indices = [i for i, line in enumerate(lines) if line == key]
        if len(indices) != 1:
            fail(f"Kit successor has no unique top-level {key}")
        start = indices[0] + 1
        end = next((i for i in range(start, len(lines)) if not lines[i][0].isspace()), len(lines))
        return lines[start:end]

    if block("permissions:") != ["  contents: read"]:
        fail("Kit successor can obtain a token beyond contents:read")
    jobs = block("jobs:")
    job_names = re.findall(r"(?m)^  ([a-z][a-z0-9-]*):\s*$", "\n".join(jobs))
    if job_names != ["bump-shape", "bump-mechanical", "receiver-validate"]:
        fail("Kit successor changed the three read-only job identities")
    if any(re.match(r"^    (?:permissions|uses):", line) for line in jobs):
        fail("Kit successor adds a job permission override or reusable callee")
    active = "\n".join(lines)
    if "actions/create-github-app-token" in active or re.search(r"\$\{\{\s*secrets(?:\.|\[)", active):
        fail("Kit successor can read an App or repository secret")
    uses = [match.group(1) or match.group(2) for match in re.finditer(
        r"(?m)^        uses:\s*(\S+)\s*$|^      - uses:\s*(\S+)\s*$", "\n".join(jobs))]
    if sorted(uses) != sorted(["actions/checkout@v7", "actions/checkout@v7",
                             "actions/setup-dotnet@v6", "actions/setup-python@v7"]):
        fail("Kit successor introduces an unreviewed action")
    forbidden = (
        r"\bgh\s+api\s+-X\s+(?:POST|PATCH|PUT|DELETE)\b",
        r"\bgh\s+pr\s+(?:create|edit|merge|close)\b",
        r"\bgh\s+release\s+(?:create|edit|upload|delete)\b",
        r"\bgit\s+push\b",
        r"\bdotnet\s+nuget\s+push\b",
        r"\bcurl\b[^\n]*\s-X\s+(?:POST|PATCH|PUT|DELETE)\b",
        r"\brepos/[^\s]+/dispatches\b",
    )
    if any(re.search(pattern, active, re.I) for pattern in forbidden):
        fail("Kit successor introduces a provider write command")


def canonical_digest(value: object) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def expected_test_names(oracle: dict[str, object]) -> set[str]:
    test_oracle = oracle["testOracle"]
    names = list(test_oracle["facts"])
    for family in test_oracle["theories"]:
        names.extend(family["identity"] + case for case in family["cases"])
    if len(names) != len(set(names)):
        fail("independent test oracle contains duplicate identities")
    return set(names)


def validate_trx(path: pathlib.Path, expected: set[str]) -> tuple[int, int]:
    root = ET.parse(path).getroot()
    results = [node for node in root.iter() if node.tag.endswith("UnitTestResult")]
    names = [node.attrib.get("testName", "") for node in results]
    execution_ids = [node.attrib.get("executionId", "") for node in results]
    if len(names) != len(set(names)):
        fail("TRX contains duplicate test names")
    if "" in execution_ids or len(execution_ids) != len(set(execution_ids)):
        fail("TRX contains missing or duplicate execution identities")
    actual = set(names)
    if actual != expected:
        fail(f"TRX identity closure failed; missing={sorted(expected-actual)}, extra={sorted(actual-expected)}")
    failed = [node for node in results if node.attrib.get("outcome") != "Passed"]
    if failed:
        fail("TRX contains a non-passing exact attack identity")

    counters = next((node for node in root.iter() if node.tag.endswith("Counters")), None)
    if counters is None:
        fail("TRX execution evidence has no counters")
    total = int(counters.attrib.get("total", "0"))
    passed = int(counters.attrib.get("passed", "0"))
    if total != len(expected) or passed != total:
        fail(f"TRX counters disagree with exact identities: total={total}, passed={passed}")
    return total, len(failed)


def validate_writer_callsites(oracle: dict[str, object]) -> None:
    rows = oracle["writerCallsites"]
    keys = [(row["path"], row["id"]) for row in rows]
    if len(rows) != 16 or len(keys) != len(set(keys)):
        fail("writer callsite oracle must contain 16 unique concrete callsites")
    boundaries = {row["id"] for row in oracle["boundaryExecutions"]}
    if boundaries != {row["boundary"] for row in rows}:
        fail("every writer callsite must map to one executed boundary and no synthetic boundary")

    for row in rows:
        path = ROOT / row["path"]
        source = path.read_text()
        if source.count(row["anchor"]) != 1:
            fail(f"writer callsite anchor is absent or ambiguous: {row['path']}::{row['anchor']}")

    # Independent source census: these are the only production SendMutation/sendRestMutation sinks.
    actual = []
    for relative in (
        "src/FS.GG.Coord.GitHub/Writes.fs",
        "src/FS.GG.Coord.GitHub/Done.fs",
        "src/FS.GG.Coord.GitHub/Board.fs",
        "src/FS.GG.Coord.GitHub/OperationalGraphQl.fs",
    ):
        text = (ROOT / relative).read_text()
        actual.extend((relative, value) for value in re.findall(r'sendRestMutation "([^"]+)"', text))
        actual.extend(
            (relative, value.removesuffix(":"))
            for value in re.findall(r'EffectId\s*=\s*mutationEffectId \$?"(board-(?:add-item|set-field|set-field-batch|archive-items):)', text)
        )
    expected = [(row["path"], row["id"]) for row in rows]
    if sorted(actual) != sorted(expected):
        fail(f"real writer callsite population drifted; actual={sorted(actual)}, expected={sorted(expected)}")


def validate_external_and_legacy(oracle: dict[str, object]) -> tuple[dict[str, object], dict[str, object]]:
    census = json.loads(CENSUS_PATH.read_text())
    receivers = json.loads(RECEIVER_PATH.read_text())
    external = json.loads(EXTERNAL_PATH.read_text())
    read_only_successors = json.loads(READ_ONLY_SUCCESSORS_PATH.read_text())
    legacy = json.loads(LEGACY_PATH.read_text())

    dispositions = {"conditional-remote-writer", "protected-admin-writer", "publish-writer", "remote-writer"}
    expected_sources = {row["path"] for row in census["sources"] if row["disposition"] in dispositions}
    oracle_sources = set(oracle["externalWriterSources"])
    route_sources = {row["path"] for row in external["routes"]}
    unresolved_sources = {row["path"] for row in external["routes"] if row["gs2089"] == "unresolved"}
    successor = json.loads(GS2089_RELEASE_PATH.read_text()) if GS2089_RELEASE_PATH.is_file() else None
    successor_routes = {row["path"]: row for row in successor["routes"]} if successor else {}
    kit_path = ".github/workflows/kit-materialize.yml"
    if (read_only_successors.get("schema") != "fsgg.gs2-08.6-read-only-route-successors/1"
            or len(read_only_successors.get("routes", [])) != 1
            or read_only_successors["routes"][0].get("path") != kit_path):
        fail("read-only successor attestation must cover only the Kit workflow")
    kit_successor = read_only_successors["routes"][0]
    removed = unresolved_sources - expected_sources
    declared_removed = set(successor.get("censusAdjustment", {}).get("removedWriterRows", [])) if successor else set()
    if route_sources != oracle_sources or unresolved_sources - declared_removed != expected_sources:
        fail("external writer evidence does not close the accepted census population")
    if removed != declared_removed:
        fail("GS2-08.9 successor does not account for every removed accepted writer")
    for row in external["routes"]:
        current = file_sha(ROOT / row["path"])
        if row["sha256"] != current:
            if row["path"] == kit_path:
                if row.get("sha256") != "c665d20100ec37068c2eae8fbadb22ab22cd9f571a539174c1a85dc917b59575":
                    fail("accepted Kit predecessor identity changed")
                if row.get("status") != "current-revision-read-only" or row.get("gs2089") != "resolved":
                    fail("accepted Kit route was not a resolved read-only route")
                validate_kit_read_only_successor(row, kit_successor, (ROOT / kit_path).read_bytes())
            elif successor_routes.get(row["path"], {}).get("sha256") != current:
                fail(f"external route source hash drifted without GS2-08.9 successor: {row['path']}")
        if row["gs2089"] not in {"unresolved", "resolved"} or row["status"] in {"pass", "refusal-observed"}:
            fail(f"unexecuted external route was overstated: {row['path']}")

    installed = {row["id"]: row["installedCoordCliVersion"] for row in receivers["receivers"]}
    expected_legacy = {
        "0.58.0": sorted(name for name, version in installed.items() if version == "0.58.0"),
        "0.75.4": sorted(name for name, version in installed.items() if version == "0.75.4"),
    }
    oracle_legacy = {row["version"]: sorted(row["receivers"]) for row in oracle["legacyClients"]}
    if oracle_legacy != expected_legacy:
        fail("legacy receiver population drifted")
    clients = {row["version"]: row for row in legacy["clients"]}
    if clients.get("0.58.0", {}).get("status") != "artifact-unavailable":
        fail("0.58.0 must remain precisely unresolved until its artifact is retained")
    old = clients.get("0.75.4", {})
    if old.get("status") != "bypass-observed" or old.get("providerLedger", {}).get("count") != 1:
        fail("0.75.4 isolated provider bypass evidence is missing")
    if legacy["serverSha256"] != file_sha(ROOT / "tests/coord-engine-e2e/stateful_server.py"):
        fail("legacy intercept server hash drifted")
    return external, legacy


def validate_assertion_floors(oracle: dict[str, object], root: pathlib.Path = ROOT) -> None:
    for relative, minimum in oracle["testOracle"]["sourceAssertionMinimums"].items():
        count = (root / relative).read_text().count("Assert.")
        if count < minimum:
            fail(f"attack assertions were removed from {relative}: {count} < {minimum}")


def main(args: argparse.Namespace) -> None:
    oracle = json.loads(ORACLE_PATH.read_text())
    if oracle.get("schema") != "fsgg.gs2-08.6-producer-attack-oracle/2":
        fail("unknown oracle schema")
    if oracle["qualityClaims"].get("q4RealProvider") != "unclaimed":
        fail("offline attacks cannot claim Q4 real-provider evidence")

    expected_epochs = {
        "OperatingV1": "allow",
        "Preparing": "allow-eligible-incumbent",
        "FreezeRequested": "refuse",
        "Frozen": "refuse",
        "SwitchedV2": "refuse",
        "VerifiedV2": "refuse",
        "OpenV2": "refuse",
        "ObservingV2": "refuse",
        "ContractingV1": "refuse",
        "OperatingV2": "refuse",
        "RollingBack": "refuse",
    }
    if {row["name"]: row["ordinaryEffect"] for row in oracle["epochs"]} != expected_epochs:
        fail("epoch oracle is incomplete or changed")

    validate_writer_callsites(oracle)
    external, legacy = validate_external_and_legacy(oracle)
    validate_assertion_floors(oracle)

    client_source = CLIENT_PATH.read_text()
    for marker in (
        "new Transport.FencedTransport(",
        "UnavailableProductionMutationFence()",
        'env "FSGG_COORD_TEST_ALLOW_UNFENCED_LOOPBACK_MUTATIONS" "" = "1"',
        "uri.IsLoopback",
    ):
        if marker not in client_source:
            fail("live composition or loopback-only escape is no longer fail closed")
    fixture = WRITE_FIXTURE_PATH.read_text()
    if 'FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT"' not in fixture or "export FSGG_COORD_TEST_ALLOW_UNFENCED_LOOPBACK_MUTATIONS=1" not in fixture:
        fail("hermetic write fixture no longer binds the explicit loopback escape")

    git_head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True, capture_output=True, check=True).stdout.strip()
    dotnet = subprocess.run(["dotnet", "--version"], cwd=ROOT, text=True, capture_output=True, check=True).stdout.strip()
    evidence = {
        "schema": "fsgg.gs2-08.6-offline-structural-evidence/2",
        "oracleSha256": file_sha(ORACLE_PATH),
        "canonicalOracleSha256": canonical_digest(oracle),
        "writerCensusSha256": file_sha(CENSUS_PATH),
        "receiverCensusSha256": file_sha(RECEIVER_PATH),
        "legacyEvidenceSha256": file_sha(LEGACY_PATH),
        "externalRouteEvidenceSha256": file_sha(EXTERNAL_PATH),
        "readOnlySuccessorsSha256": file_sha(READ_ONLY_SUCCESSORS_PATH),
        "liveCompositionSha256": file_sha(CLIENT_PATH),
        "loopbackWriteFixtureSha256": file_sha(WRITE_FIXTURE_PATH),
        "checkoutHead": git_head,
        "sourceHead": args.source_head or git_head,
        "dotnetVersion": dotnet,
        "globalJsonSha256": file_sha(GLOBAL_JSON),
        "epochs": 11,
        "writerCallsites": 16,
        "executedBoundaries": 6,
        "externalWriterResiduals": len(external["routes"]),
        "legacyBypasses": sum(row.get("status") == "bypass-observed" for row in legacy["clients"]),
        "legacyUnavailable": sum(row.get("status") == "artifact-unavailable" for row in legacy["clients"]),
        "q4": "unclaimed",
        "status": "pass",
    }

    if args.trx is not None:
        if args.test_assembly is None or args.product_assembly is None or args.source_head is None:
            fail("execution evidence requires --test-assembly, --product-assembly, and --source-head")
        for path in (args.trx, args.test_assembly, args.product_assembly):
            if not path.is_file():
                fail(f"execution artifact is missing: {path}")
        if not re.fullmatch(r"[0-9a-f]{40}", args.source_head):
            fail("source head must be an exact 40-hex commit")
        total, failures = validate_trx(args.trx, expected_test_names(oracle))
        evidence.update(
            {
                "schema": "fsgg.gs2-08.6-offline-execution-evidence/2",
                "trxSha256": file_sha(args.trx),
                "testAssemblySha256": file_sha(args.test_assembly),
                "productAssemblySha256": file_sha(args.product_assembly),
                "compiledTests": total,
                "compiledFailures": failures,
            }
        )

    print(json.dumps(evidence, sort_keys=True, separators=(",", ":")))
    print(f"evidence-sha256={canonical_digest(evidence)}")


if __name__ == "__main__":
    try:
        parser = argparse.ArgumentParser()
        parser.add_argument("--trx", type=pathlib.Path)
        parser.add_argument("--test-assembly", type=pathlib.Path)
        parser.add_argument("--product-assembly", type=pathlib.Path)
        parser.add_argument("--source-head")
        main(parser.parse_args())
    except (KeyError, TypeError, ValueError, json.JSONDecodeError, ET.ParseError, subprocess.CalledProcessError) as error:
        fail(f"malformed input: {error}")
