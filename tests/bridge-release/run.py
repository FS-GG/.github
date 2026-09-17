#!/usr/bin/env python3
"""Offline GS2-08.7 P3 release-binding and recovery controls."""

import argparse
import copy
import hashlib
import importlib.util
import json
import pathlib
import subprocess
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("release_saga", ROOT / "scripts/release-saga.py")
saga = importlib.util.module_from_spec(spec)
assert spec.loader
spec.loader.exec_module(saga)

SOURCE = "a" * 40
TREE = "b" * 40
PACKAGES = ("FS.GG.Coord.Cli", "FS.GG.Drivers", "FS.GG.Kit")


def package(path: pathlib.Path, package_id: str, body: str = "qualified") -> None:
    nuspec = f'<package><metadata><id>{package_id}</id><version>0.90.0</version><authors>FS-GG</authors><description>fixture</description><releaseNotes>fixture</releaseNotes></metadata></package>'
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)
        archive.writestr("content/payload.txt", body)


def expect_failure(label: str, action, contains: str) -> None:
    try:
        action()
    except (ValueError, OSError, KeyError, json.JSONDecodeError, zipfile.BadZipFile) as error:
        if contains not in str(error):
            raise AssertionError(f"{label}: wrong refusal: {error}") from error
    else:
        raise AssertionError(f"{label}: unexpectedly passed")


with tempfile.TemporaryDirectory(prefix="gs2-08-7-p3.") as temporary:
    work = pathlib.Path(temporary)
    artifacts = work / "artifacts"
    artifacts.mkdir()
    for package_id in PACKAGES:
        package(artifacts / f"{package_id}.0.90.0.nupkg", package_id)
    previous = work / "stable.json"
    previous.write_text(json.dumps({
        "contentId": "sha256:" + "1" * 64, "version": "0.89.0", "sourceSha": "2" * 40,
        "promotedAt": "2026-09-14T00:00:00Z",
    }))
    expectations = json.loads((ROOT / "tests/bridge-package/expectations.json").read_text())
    cli = artifacts / "FS.GG.Coord.Cli.0.90.0.nupkg"
    qualification = artifacts / "gs2-08-7-bridge-qualification.json"
    qualification.write_text(json.dumps({
        "schema": saga.BRIDGE_CANDIDATE_SCHEMA,
        "package": {"id": "FS.GG.Coord.Cli", "version": "0.90.0"},
        "archiveSha256": saga.sha256(cli), "payloadSha256": saga.payload_id(cli).removeprefix("sha256:"),
        "payloadEntries": 2, "source": {"commit": SOURCE, "tree": TREE},
        "producerSource": expectations["producerSource"],
        "installedAssemblyDigests": {name: "3" * 64 for name in expectations["requiredAssemblies"]},
        "acceptedReceipts": expectations["acceptedReceipts"],
        "componentIdentities": expectations["componentIdentities"],
        "coherentSet": expectations["coherentSet"],
        "expectationsSha256": saga.sha256(ROOT / "tests/bridge-package/expectations.json"),
    }, sort_keys=True))
    binding = ROOT / "evidence/gs2-08-7/release-binding.json"
    manifest = artifacts / "release-manifest.json"

    def args(**changes):
        values = dict(
            release_id="github:0.90.0", version="0.90.0", source_sha=SOURCE,
            source_tree=TREE, policy_version="release-saga/1", previous_channel=str(previous),
            artifact_dir=str(artifacts), expected_package=list(PACKAGES), dashboard_assets=None,
            standalone_qualification=None, bridge_binding=str(binding),
            bridge_qualification=str(qualification), output=str(manifest),
        )
        values.update(changes)
        return argparse.Namespace(**values)

    saga.command_prepare(args())
    saga.command_preflight(argparse.Namespace(manifest=str(manifest), feed="both", max_package_bytes=250_000_000, max_release_notes_characters=35_000))

    expect_failure("bypassed qualification", lambda: saga.command_prepare(args(bridge_binding=None, bridge_qualification=None)), "qualification is required")
    wrong_source = json.loads(qualification.read_text())
    wrong_source["source"]["commit"] = "f" * 40
    wrong_source_path = work / "wrong-source.json"
    wrong_source_path.write_text(json.dumps(wrong_source))
    expect_failure("wrong source", lambda: saga.command_prepare(args(bridge_qualification=str(wrong_source_path))), "does not bind")
    wrong_payload = json.loads(qualification.read_text())
    wrong_payload["payloadSha256"] = "f" * 64
    wrong_payload_path = work / "wrong-payload.json"
    wrong_payload_path.write_text(json.dumps(wrong_payload))
    expect_failure("payload substitution", lambda: saga.command_prepare(args(bridge_qualification=str(wrong_payload_path))), "does not bind")
    wrong_workflow = json.loads(qualification.read_text())
    wrong_workflow["componentIdentities"][".github/workflows/release-saga-prepare.yml"] = "f" * 64
    wrong_workflow_path = work / "wrong-workflow.json"
    wrong_workflow_path.write_text(json.dumps(wrong_workflow))
    expect_failure("workflow substitution", lambda: saga.command_prepare(args(bridge_qualification=str(wrong_workflow_path))), "does not bind")
    stripped_assembly = json.loads(qualification.read_text())
    stripped_assembly["installedAssemblyDigests"].pop("FS.GG.Coord.Core.dll")
    stripped_assembly_path = work / "stripped-assembly.json"
    stripped_assembly_path.write_text(json.dumps(stripped_assembly))
    expect_failure("stripped installed assembly", lambda: saga.command_prepare(args(bridge_qualification=str(stripped_assembly_path))), "does not bind")
    stripped_receipt = json.loads(qualification.read_text())
    stripped_receipt["acceptedReceipts"].pop("gs2-08.6")
    stripped_receipt_path = work / "stripped-receipt.json"
    stripped_receipt_path.write_text(json.dumps(stripped_receipt))
    expect_failure("stripped accepted receipt", lambda: saga.command_prepare(args(bridge_qualification=str(stripped_receipt_path))), "does not bind")
    wrong_binding = json.loads(binding.read_text())
    wrong_binding["coordination"]["p2Merge"] = "f" * 40
    wrong_binding_path = work / "wrong-binding.json"
    wrong_binding_path.write_text(json.dumps(wrong_binding))
    expect_failure("P2 evidence substitution", lambda: saga.command_prepare(args(bridge_binding=str(wrong_binding_path))), "P2 registration")

    original_cli = cli.read_bytes()
    package(cli, "FS.GG.Coord.Cli", "substituted")
    expect_failure("same-version package substitution", lambda: saga.assert_bound(manifest, saga.load(manifest)), "byte drift")
    cli.write_bytes(original_cli)

    data = saga.load(manifest)
    for feed in saga.FEEDS:
        data["state"]["feeds"][feed]["state"] = "verified"
        for row in data["state"]["feeds"][feed]["packages"].values():
            row["state"] = "verified"
    saga.write_atomic(manifest, data)
    bundle = work / "bundle.json"
    bundle.write_text('{"signed":"fixture"}')
    verification = work / "verification.json"
    cli_sha = saga.sha256(cli)
    verification.write_text(json.dumps([{
        "subject": {"digest": {"sha256": cli_sha}}, "source": SOURCE,
        "signer": ".github/workflows/release-coord-engine.yml",
    }]))
    saga.command_provenance(argparse.Namespace(
        manifest=str(manifest), package="FS.GG.Coord.Cli", artifact=str(cli), bundle=str(bundle),
        verification=str(verification), signer_workflow=".github/workflows/release-coord-engine.yml",
    ))
    expect_failure("partial provenance recovery", lambda: saga.command_promote(argparse.Namespace(manifest=str(manifest), previous_channel=str(previous), channel_output=None)), "evidence is incomplete")

    stored = work / "stored"
    candidate = work / "candidate"
    stored.mkdir(); candidate.mkdir()
    for target in (stored, candidate):
        for source in artifacts.iterdir():
            target.joinpath(source.name).write_bytes(source.read_bytes())
    saga.command_reusable(argparse.Namespace(stored=str(stored / manifest.name), candidate=str(candidate / manifest.name)))
    candidate.joinpath(qualification.name).write_text('{}')
    expect_failure("changed stored qualification bytes", lambda: saga.command_reusable(argparse.Namespace(stored=str(stored / manifest.name), candidate=str(candidate / manifest.name))), "qualification is missing or digest-mismatched")

    facts = work / "auto-publish-facts.json"
    facts.write_text(json.dumps({
        "version": "0.90.0",
        "provenance": {"mergedReachable": True, "introducedVersion": "0.90.0", "prArm": "pass"},
        "orgFeed": "absent", "nugetFeed": "absent", "orgLatest": "0.89.0", "nugetLatest": "0.89.0",
        "tagExists": False,
    }))
    decision = subprocess.run(
        ["python3", str(ROOT / "scripts/kit-auto-publish.py"), "--facts", str(facts), "--json"],
        check=True, text=True, capture_output=True,
    )
    verdict = json.loads(decision.stdout)
    assert verdict["action"] == "expectedRefusal" and verdict["reason"] == "candidate-not-next-patch", verdict

print("GS2-08.7 P3 bridge release: qualification, provenance, recovery, and substitution controls passed")
