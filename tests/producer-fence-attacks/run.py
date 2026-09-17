#!/usr/bin/env python3
"""Offline structural verifier for the independently authored GS2-08.6 oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys
import xml.etree.ElementTree as ET


ROOT = pathlib.Path(__file__).resolve().parents[2]
ORACLE_PATH = ROOT / "tests/producer-fence-attacks/oracle.json"
CENSUS_PATH = ROOT / "docs/coordination/v1-writer-census.json"
RECEIVER_PATH = ROOT / "docs/coordination/v1-writer-receiver-census.json"
ATTACKS_PATH = ROOT / "tests/FS.GG.Coord.GitHub.Tests/ProducerFenceAttackTests.fs"
CLIENT_PATH = ROOT / "src/FS.GG.Coord.Cli/Client.fs"


def fail(message: str) -> None:
    raise SystemExit(f"producer-fence-attacks: {message}")


def canonical_digest(value: object) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def main(trx_path: pathlib.Path | None) -> None:
    oracle = json.loads(ORACLE_PATH.read_text())
    census = json.loads(CENSUS_PATH.read_text())
    receivers = json.loads(RECEIVER_PATH.read_text())

    if oracle.get("schema") != "fsgg.gs2-08.6-producer-attack-oracle/1":
        fail("unknown oracle schema")

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
    actual_epochs = {row["name"]: row["ordinaryEffect"] for row in oracle["epochs"]}
    if actual_epochs != expected_epochs:
        fail("epoch oracle is incomplete or changed")

    expected_commands = {
        row["name"]: row["writes"]
        for row in census["commandRoots"]
        if row["writes"] in {"always", "conditional"}
    }
    actual_commands = {row["name"]: row["writes"] for row in oracle["typedWriteCommands"]}
    if actual_commands != expected_commands:
        fail("typed write command oracle does not equal the accepted closed census")
    if {row["entry"] for row in oracle["typedWriteCommands"]} != {"rest", "graphql"}:
        fail("both REST and GraphQL mutation entries must be exercised")

    writer_dispositions = {
        "conditional-remote-writer", "protected-admin-writer", "publish-writer", "remote-writer"
    }
    expected_sources = {
        row["path"] for row in census["sources"] if row["disposition"] in writer_dispositions
    }
    actual_sources = set(oracle["externalWriterSources"])
    if actual_sources != expected_sources:
        missing = sorted(expected_sources - actual_sources)
        extra = sorted(actual_sources - expected_sources)
        fail(f"external writer population drifted; missing={missing}, extra={extra}")
    if oracle["externalWriterDisposition"] != "unresolved-gs2-08.9-blocker":
        fail("external writer sources must remain explicit GS2-08.9 blockers")

    installed = {row["id"]: row["installedCoordCliVersion"] for row in receivers["receivers"]}
    expected_legacy = {
        "0.58.0": sorted(name for name, version in installed.items() if version == "0.58.0"),
        "0.75.4": sorted(name for name, version in installed.items() if version == "0.75.4"),
    }
    actual_legacy = {
        row["version"]: sorted(row["receivers"])
        for row in oracle["legacyClients"]
        if row["disposition"] == "unresolved-gs2-08.9-blocker"
    }
    if actual_legacy != expected_legacy:
        fail("legacy client blockers do not match the accepted receiver census")

    if oracle["qualityClaims"].get("q4RealProvider") != "unclaimed":
        fail("offline attacks cannot claim Q4 real-provider evidence")

    attack_source = ATTACKS_PATH.read_text()
    attack_markers = {
        "all-epochs": "all eleven epochs",
        "stale-cache": "stale cache and ledger rewind",
        "lost-response": "lost response concurrent owner",
        "ledger-rewind": "stale cache and ledger rewind",
        "missing-tag": "missing tag wrong manifest",
        "wrong-manifest": "missing tag wrong manifest",
        "permission-loss": "missing tag wrong manifest",
        "crash-before-send": "crash before send and crash after send",
        "crash-after-send": "crash before send and crash after send",
        "concurrent-ownership": "lost response concurrent owner",
        "changed-operation-generation": "changed generations refuse replay",
        "changed-claim-generation": "changed generations refuse replay",
        "same-semantic-different-bytes": "same semantic effect with different request bytes",
        "delayed-old-sha": "delayed old SHA",
        "mutation-disguised-as-read": "disguised mutations",
        "retry-proven-absent-fresh-fence": "retry requires ProvenAbsent and obtains a fresh epoch fence",
        "canonical-mutation-identity": "canonical mutation identity binds exact request bytes",
        "rest-entry": "real durable REST and GraphQL entry paths",
        "graphql-entry": "real durable REST and GraphQL entry paths",
    }
    required = set(oracle["requiredAttacks"])
    exempt = {"old-clients", "fail-closed-live-composition"}
    if set(attack_markers) | exempt != required:
        fail("required attack inventory and structural mappings disagree")
    missing_markers = sorted(name for name, marker in attack_markers.items() if marker not in attack_source)
    if missing_markers:
        fail(f"compiled attack cases are missing: {missing_markers}")

    client_source = CLIENT_PATH.read_text()
    live_markers = [
        "new Transport.FencedTransport(",
        "UnavailableProductionMutationFence()",
        'env "FSGG_COORD_TEST_ALLOW_UNFENCED_LOOPBACK_MUTATIONS" "" = "1"',
        "Uri.TryCreate(apiBase, UriKind.Absolute, &uri)",
        "uri.IsLoopback",
    ]
    if any(marker not in client_source for marker in live_markers):
        fail("live composition or loopback-only escape is no longer fail closed")

    evidence = {
        "schema": "fsgg.gs2-08.6-offline-structural-evidence/1",
        "oracleSha256": hashlib.sha256(ORACLE_PATH.read_bytes()).hexdigest(),
        "canonicalOracleSha256": canonical_digest(oracle),
        "writerCensusSha256": hashlib.sha256(CENSUS_PATH.read_bytes()).hexdigest(),
        "receiverCensusSha256": hashlib.sha256(RECEIVER_PATH.read_bytes()).hexdigest(),
        "compiledAttackHarnessSha256": hashlib.sha256(ATTACKS_PATH.read_bytes()).hexdigest(),
        "liveCompositionSha256": hashlib.sha256(CLIENT_PATH.read_bytes()).hexdigest(),
        "epochs": len(actual_epochs),
        "typedWriteCommands": len(actual_commands),
        "externalWriterBlockers": len(actual_sources),
        "legacyReceiverBlockers": sum(len(value) for value in expected_legacy.values()),
        "q4": "unclaimed",
        "status": "pass",
    }
    if trx_path is not None:
        root = ET.parse(trx_path).getroot()
        counters = next((item for item in root.iter() if item.tag.endswith("Counters")), None)
        if counters is None:
            fail("TRX execution evidence has no counters")
        total = int(counters.attrib.get("total", "0"))
        passed = int(counters.attrib.get("passed", "0"))
        failed = int(counters.attrib.get("failed", "0"))
        if failed != 0 or passed != total or total < 21:
            fail(f"compiled attacks did not pass non-vacuously; total={total}, passed={passed}, failed={failed}")
        evidence["schema"] = "fsgg.gs2-08.6-offline-execution-evidence/1"
        evidence["compiledTests"] = total
        evidence["compiledFailures"] = failed
    print(json.dumps(evidence, sort_keys=True, separators=(",", ":")))
    print(f"evidence-sha256={canonical_digest(evidence)}")


if __name__ == "__main__":
    try:
        parser = argparse.ArgumentParser()
        parser.add_argument("--trx", type=pathlib.Path)
        args = parser.parse_args()
        main(args.trx)
    except (KeyError, TypeError, ValueError, json.JSONDecodeError, ET.ParseError) as error:
        fail(f"malformed input: {error}")
