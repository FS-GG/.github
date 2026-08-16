#!/usr/bin/env python3
"""Content-addressed, resumable dual-feed release saga.

The manifest is both the immutable release descriptor and the durable journal.  It never
publishes by itself: feed credentials stay in the package-owning workflows.  Those workflows
must publish the manifest-bound file and then record the externally served package here.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import pathlib
import tempfile
import xml.etree.ElementTree as ET
import zipfile

SCHEMA = "fsgg.release-saga/1"
FEEDS = ("github", "nuget")


def now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def sha256(path: pathlib.Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def write_atomic(path: pathlib.Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=path.name + ".", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def nuspec(path: pathlib.Path) -> tuple[str, str, str, list[dict[str, str]]]:
    with zipfile.ZipFile(path) as archive:
        names = [name for name in archive.namelist() if name.lower().endswith(".nuspec")]
        if len(names) != 1:
            raise ValueError(f"{path}: expected exactly one nuspec, found {len(names)}")
        raw = archive.read(names[0])
    root = ET.fromstring(raw)
    metadata = next((node for node in root.iter() if node.tag.rsplit("}", 1)[-1] == "metadata"), None)
    if metadata is None:
        raise ValueError(f"{path}: nuspec has no metadata")
    scalar = {node.tag.rsplit("}", 1)[-1]: (node.text or "") for node in metadata}
    dependencies = []
    for node in metadata.iter():
        if node.tag.rsplit("}", 1)[-1] == "dependency":
            dependencies.append({"id": node.attrib.get("id", ""), "version": node.attrib.get("version", "")})
    dependencies.sort(key=lambda row: (row["id"].lower(), row["version"]))
    return scalar.get("id", ""), scalar.get("version", ""), scalar.get("releaseNotes", ""), dependencies


def is_core_properties(name: str) -> bool:
    return name.lower().endswith(".psmdcp")


def is_relationship_part(name: str) -> bool:
    # OPC relationship parts: `_rels/.rels` at the package root, `<folder>/_rels/<part>.rels` below it.
    lowered = name.lower()
    return lowered.endswith(".rels") and (lowered.startswith("_rels/") or "/_rels/" in lowered)


def normalized_relationships(raw: bytes) -> bytes:
    """Drop the relationship that names the per-invocation core-properties part.

    `payload()` has always excluded the `.psmdcp` core-properties entry itself, because `dotnet pack`
    mints a fresh one — new GUID filename — on every invocation (.github#2240). It did not exclude the
    relationship POINTING at it, and `_rels/.rels` carries that GUID in `Target` plus an `Id` derived
    from it. So `payloadSha256`, the field that exists specifically to be re-pack stable, was not
    (.github#2664): two `dotnet pack` runs over one clean tree differed in exactly this one entry.

    The exclusion is the narrowest that removes the measured instability: only the relationship to the
    part already outside the payload is dropped. The manifest relationship — Type, Target and `Id`
    alike — stays under the hash, and was measured stable across those same two runs, so a genuinely
    different nuspec target still fails closed. Unparseable bytes fall back to a verbatim comparison,
    which cannot pass on a difference this normalization was never asked to explain.

    This removes the OPC-envelope instability, which was guaranteed on every re-pack; it does NOT make
    `payloadSha256` re-pack stable in general, and no caller may assume it does. `FS.GG.Coord.Cli` has
    a residual, rare per-compile instability inside `tools/net10.0/any/fsgg-coord-engine.dll` — the F#
    compiler disambiguates same-named generated closures with a `-N` suffix drawn from a shared
    counter, and which of two `contains@1` closures gets `-1` is decided by a race. That is a different
    root cause, and deliberately NOT normalized away here: it is a real difference in packed content,
    and hiding it would be the fail-open .github#2240 and .github#2428 exist to prevent.

    .github#2688 CLOSED THAT ROW BY MEASURING IT, AND THE ANSWER CHANGES WHAT THIS PARAGRAPH MAY
    PROMISE RATHER THAN WHAT IT DOES. Three things are now known instead of suspected:

      * The race is NOT fixable from build configuration on SDK 10.0.302. `ParallelCompilation` is
        inert (the F# SDK passes it to the Fsc task as an item reference to a property, and fsc refuses
        the flag as test-only anyway), `test:ParallelOff` does not help (7 of 40 compiles diverged
        against a 9-of-40 baseline), and turning cross-module optimization off makes it worse. So no
        future edit here should be written as if the compiler side were about to be repaired.
      * `contains@1` is one family of roughly eighty carrying a `-N` disambiguator, so the instability
        is not scoped to that symbol and a normalization keyed to it would be false comfort.
      * The divergence is BOUNDED: across 144 compiles in five configurations the disambiguated-symbol
        multiset was identical every time and only the ORDER of two of them moved. A payload difference
        outside that shape is a genuinely different tree, which is the property this fail-closed
        comparison exists to keep.

    The OTHER half of .github#2688 — every compiled entry embedding the absolute directory it was built
    in — WAS fixed, by `DeterministicSourcePaths` on the three coord projects, and is held there by
    scripts/check-engine-build-determinism.py. Until that landed, `payloadSha256` was not comparable
    across machines at all, so a re-prepare on a different checkout path differed for a reason that had
    nothing to do with the compiler.
    """
    try:
        root = ET.fromstring(raw)
    except ET.ParseError:
        return raw
    rows = []
    for node in root:
        local = node.tag.rsplit("}", 1)[-1]
        attributes = dict(node.attrib)
        if local == "Relationship" and is_core_properties(attributes.get("Target", "")):
            continue
        rows.append([local, attributes])
    return canonical(rows)


def entry_payload(archive: zipfile.ZipFile, name: str) -> bytes:
    raw = archive.read(name)
    return normalized_relationships(raw) if is_relationship_part(name) else raw


def payload(path: pathlib.Path) -> dict[str, str]:
    with zipfile.ZipFile(path) as archive:
        return {
            name: hashlib.sha256(entry_payload(archive, name)).hexdigest()
            for name in sorted(archive.namelist())
            # Registries may append a signature and regenerate the package-services metadata.
            # Neither is producer payload; every other entry remains byte-bound to the manifest.
            if name.lower() != ".signature.p7s" and not is_core_properties(name) and not name.endswith("/")
        }


def payload_id(path: pathlib.Path) -> str:
    return "sha256:" + hashlib.sha256(canonical(payload(path))).hexdigest()


def load(path: pathlib.Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema") != SCHEMA:
        raise ValueError(f"{path}: expected schema {SCHEMA}")
    return data


def artifact_path(manifest_path: pathlib.Path, package: dict) -> pathlib.Path:
    path = pathlib.Path(package["artifact"]["path"])
    return path if path.is_absolute() else (manifest_path.parent / path).resolve()


def assert_bound(manifest_path: pathlib.Path, data: dict, selected: set[str] | None = None) -> None:
    descriptor = data["descriptor"]
    expected_content = "sha256:" + hashlib.sha256(canonical(descriptor)).hexdigest()
    if data.get("contentId") != expected_content:
        raise ValueError("manifest descriptor drift: contentId does not match immutable descriptor")
    known = {row["id"] for row in descriptor["packages"]}
    if selected and not selected <= known:
        raise ValueError(f"unknown package(s): {', '.join(sorted(selected - known))}")
    for package in descriptor["packages"]:
        if selected and package["id"] not in selected:
            continue
        path = artifact_path(manifest_path, package)
        if not path.is_file():
            raise ValueError(f"manifest-bound artifact is missing: {path}")
        observed = sha256(path)
        if observed != package["artifact"]["sha256"]:
            raise ValueError(f"byte drift for {package['id']}: expected {package['artifact']['sha256']}, got {observed}")


def package_map(data: dict) -> dict[str, dict]:
    return {row["id"]: row for row in data["descriptor"]["packages"]}


def command_prepare(args: argparse.Namespace) -> None:
    root = pathlib.Path(args.artifact_dir).resolve()
    expected = set(args.expected_package)
    rows = []
    for path in sorted(root.glob("*.nupkg")):
        package_id, version, release_notes, dependencies = nuspec(path)
        if package_id not in expected:
            continue
        if version != args.version:
            raise ValueError(f"{path}: package version {version} != release version {args.version}")
        rows.append({
            "id": package_id,
            "version": version,
            "dependencies": dependencies,
            "releaseNotesCharacters": len(release_notes),
            "artifact": {
                "path": os.path.relpath(path, pathlib.Path(args.output).resolve().parent),
                "sha256": sha256(path),
                "size": path.stat().st_size,
                "payloadSha256": payload_id(path),
            },
        })
    found = {row["id"] for row in rows}
    if found != expected:
        raise ValueError(f"coherent set mismatch: expected {sorted(expected)}, found {sorted(found)}")
    rows.sort(key=lambda row: row["id"].lower())
    descriptor = {
        "releaseId": args.release_id,
        "version": args.version,
        "sourceSha": args.source_sha,
        "policyVersion": args.policy_version,
        "previousStableVersion": args.previous_version,
        "channel": "stable",
        "feedOrder": list(FEEDS),
        "packages": rows,
    }
    content_id = "sha256:" + hashlib.sha256(canonical(descriptor)).hexdigest()
    state = {
        "phase": "prepared",
        "createdAt": now(),
        "updatedAt": now(),
        "preflight": {feed: {"state": "pending", "checkedAt": None, "constraints": {}} for feed in FEEDS},
        "feeds": {
            feed: {
                "state": "pending",
                "packages": {row["id"]: {"state": "pending", "inputSha256": row["artifact"]["sha256"], "externalSha256": None, "externalPayloadSha256": None, "observedAt": None} for row in rows},
                "attempts": [],
            } for feed in FEEDS
        },
        "recovery": {"resumptions": 0, "lastFailure": None},
        "channelPromotion": {"state": "pending", "promotedAt": None, "receipt": None},
    }
    output = pathlib.Path(args.output).resolve()
    write_atomic(output, {"schema": SCHEMA, "contentId": content_id, "descriptor": descriptor, "state": state})
    print(content_id)


def command_preflight(args: argparse.Namespace) -> None:
    path = pathlib.Path(args.manifest).resolve()
    data = load(path)
    assert_bound(path, data)
    targets = FEEDS if args.feed == "both" else (args.feed,)
    for feed in targets:
        current = data["state"]["preflight"][feed]["state"]
        if current == "passed":
            continue
        for package in data["descriptor"]["packages"]:
            if package["artifact"]["size"] > args.max_package_bytes:
                raise ValueError(f"{package['id']}: {feed} package size limit exceeded")
            if package["releaseNotesCharacters"] > args.max_release_notes_characters:
                raise ValueError(f"{package['id']}: {feed} PackageReleaseNotes limit exceeded")
        data["state"]["preflight"][feed] = {
            "state": "passed", "checkedAt": now(),
            "constraints": {"maxPackageBytes": args.max_package_bytes, "maxReleaseNotesCharacters": args.max_release_notes_characters},
        }
    data["state"]["updatedAt"] = now()
    write_atomic(path, data)
    print("preflight passed: " + ",".join(targets))


def command_assert(args: argparse.Namespace) -> None:
    path = pathlib.Path(args.manifest).resolve()
    data = load(path)
    assert_bound(path, data, set(args.package) if args.package else None)
    print("manifest-bound artifact hashes verified")


def command_reusable(args: argparse.Namespace) -> None:
    """Decide whether an already-stored coherent set can be resumed against freshly packed inputs.

    Preparation is re-run whenever an earlier run created the draft release and something after it
    did not finish — a rejected tag push, a failed token mint, a runner fault. The stored bytes are
    the ones every release workflow downloads and, once published, are immutable, so the only two
    honest outcomes are "reuse them" and "say exactly what differs".

    What may NOT decide that: the raw archive hash, `artifact.size`, or the `contentId` computed over
    them. `dotnet pack` is not reproducible (.github#2240), so all three differ on every re-pack of an
    identical tree — an assertion over them can never hold, and the branch it guarded could only fail
    (.github#2664). Payload is recomputed from BOTH archive sets here rather than read out of the two
    manifests' `payloadSha256` fields, so the verdict is a statement about bytes on disk and does not
    depend on which version of this script wrote the stored manifest.
    """
    stored_path = pathlib.Path(args.stored).resolve()
    candidate_path = pathlib.Path(args.candidate).resolve()
    stored, candidate = load(stored_path), load(candidate_path)
    # Each side must first be internally coherent: a half-uploaded draft is not a reusable one.
    assert_bound(stored_path, stored)
    assert_bound(candidate_path, candidate)
    problems: list[str] = []
    for key in ("releaseId", "version", "sourceSha", "policyVersion", "previousStableVersion", "channel", "feedOrder"):
        if stored["descriptor"].get(key) != candidate["descriptor"].get(key):
            problems.append(f"descriptor.{key}: stored {stored['descriptor'].get(key)!r} != candidate {candidate['descriptor'].get(key)!r}")
    stored_packages, candidate_packages = package_map(stored), package_map(candidate)
    for label, ids in (("missing from the re-packed inputs", set(stored_packages) - set(candidate_packages)),
                       ("absent from the stored release", set(candidate_packages) - set(stored_packages))):
        if ids:
            problems.append(f"package(s) {label}: {', '.join(sorted(ids))}")
    for package_id in sorted(set(stored_packages) & set(candidate_packages)):
        stored_row, candidate_row = stored_packages[package_id], candidate_packages[package_id]
        for key in ("version", "dependencies", "releaseNotesCharacters"):
            if stored_row.get(key) != candidate_row.get(key):
                problems.append(f"{package_id}.{key}: stored {stored_row.get(key)!r} != candidate {candidate_row.get(key)!r}")
        stored_payload = payload(artifact_path(stored_path, stored_row))
        candidate_payload = payload(artifact_path(candidate_path, candidate_row))
        differing = sorted(
            (set(stored_payload) ^ set(candidate_payload))
            | {name for name in set(stored_payload) & set(candidate_payload) if stored_payload[name] != candidate_payload[name]}
        )
        if differing:
            problems.append(f"{package_id}: payload differs in {len(differing)} archive entr{'y' if len(differing) == 1 else 'ies'}: {', '.join(differing)}")
    if problems:
        raise ValueError("stored release cannot be reused for these inputs:\n  - " + "\n  - ".join(problems))
    print(f"stored release is reusable: {len(stored_packages)} package(s) are payload-identical to the re-packed inputs")


def command_identity(args: argparse.Namespace) -> None:
    path = pathlib.Path(args.manifest).resolve()
    data = load(path)
    assert_bound(path, data)
    expected = {
        "releaseId": args.release_id,
        "version": args.version,
        "sourceSha": args.source_sha,
        "policyVersion": args.policy_version,
    }
    mismatches = [f"{key}: {data['descriptor'].get(key)!r} != {value!r}" for key, value in expected.items() if data["descriptor"].get(key) != value]
    if data["descriptor"].get("channel") != "stable":
        mismatches.append("channel is not stable")
    if mismatches:
        raise ValueError("manifest identity mismatch: " + "; ".join(mismatches))
    print("manifest release/version/source/policy identity verified")


def command_merge(args: argparse.Namespace) -> None:
    path = pathlib.Path(args.manifest).resolve()
    data = load(path)
    assert_bound(path, data)
    for journal_path_value in args.journal:
        journal_path = pathlib.Path(journal_path_value).resolve()
        journal = load(journal_path)
        if journal.get("contentId") != data.get("contentId") or journal.get("descriptor") != data.get("descriptor"):
            raise ValueError(f"{journal_path}: journal belongs to different release bytes")
        for feed_name in FEEDS:
            target_feed = data["state"]["feeds"][feed_name]
            source_feed = journal["state"]["feeds"][feed_name]
            for package_id, source_row in source_feed["packages"].items():
                if source_row["state"] != "verified":
                    continue
                target_row = target_feed["packages"][package_id]
                if target_row["state"] == "verified":
                    identity_fields = ("inputSha256", "externalSha256", "externalPayloadSha256")
                    if any(target_row[field] != source_row[field] for field in identity_fields):
                        raise ValueError(f"{journal_path}: conflicting observation for {feed_name}/{package_id}")
                    # Independent package workflows observe the same immutable bytes at different
                    # times. Timestamp drift is recovery evidence, not byte drift; retain the latest.
                    if (source_row.get("observedAt") or "") > (target_row.get("observedAt") or ""):
                        target_feed["packages"][package_id] = source_row
                else:
                    target_feed["packages"][package_id] = source_row
            for attempt in source_feed["attempts"]:
                if attempt not in target_feed["attempts"]:
                    target_feed["attempts"].append(attempt)
            target_feed["state"] = "verified" if all(row["state"] == "verified" for row in target_feed["packages"].values()) else ("partial" if any(row["state"] == "verified" for row in target_feed["packages"].values()) else "pending")
        failure = journal["state"]["recovery"].get("lastFailure")
        if failure and (data["state"]["recovery"].get("lastFailure") is None or failure["at"] > data["state"]["recovery"]["lastFailure"]["at"]):
            data["state"]["recovery"]["lastFailure"] = failure
        data["state"]["recovery"]["resumptions"] = max(data["state"]["recovery"]["resumptions"], journal["state"]["recovery"]["resumptions"])
    if all(data["state"]["feeds"][feed]["state"] == "verified" for feed in FEEDS):
        data["state"]["phase"] = "feeds-complete"
    elif data["state"]["feeds"]["github"]["state"] == "verified":
        data["state"]["phase"] = "org-complete"
    data["state"]["updatedAt"] = now()
    write_atomic(path, data)
    print(f"merged {len(args.journal)} durable journal(s)")


def command_failure(args: argparse.Namespace) -> None:
    path = pathlib.Path(args.manifest).resolve()
    data = load(path)
    assert_bound(path, data)
    feed = data["state"]["feeds"][args.feed]
    if feed["state"] == "verified":
        raise ValueError(f"{args.feed}: cannot record failure after verified completion")
    stamp = now()
    feed["attempts"].append({"at": stamp, "outcome": "failure", "package": args.package, "detail": args.detail})
    feed["state"] = "partial" if any(row["state"] == "verified" for row in feed["packages"].values()) else "pending"
    data["state"]["recovery"]["lastFailure"] = {"feed": args.feed, "package": args.package, "at": stamp, "detail": args.detail}
    data["state"]["updatedAt"] = stamp
    write_atomic(path, data)
    print(f"failure journaled for {args.feed}/{args.package}")


def command_record(args: argparse.Namespace) -> None:
    path = pathlib.Path(args.manifest).resolve()
    data = load(path)
    assert_bound(path, data)
    if data["state"]["preflight"][args.feed]["state"] != "passed":
        raise ValueError(f"{args.feed}: preflight has not passed")
    if args.feed == "nuget" and data["state"]["feeds"]["github"]["state"] != "verified":
        raise ValueError("nuget: org-first policy requires the complete GitHub Packages set")
    packages = package_map(data)
    observations: dict[str, pathlib.Path] = {}
    for item in args.observed:
        package_id, separator, value = item.partition("=")
        if not separator:
            raise ValueError("--observed must be PACKAGE=PATH")
        observations[package_id] = pathlib.Path(value).resolve()
    feed = data["state"]["feeds"][args.feed]
    previously_partial = feed["state"] == "partial" or data["state"]["recovery"]["lastFailure"] is not None
    stamp = now()
    for package_id, observed in observations.items():
        if package_id not in packages:
            raise ValueError(f"unknown observed package: {package_id}")
        if not observed.is_file():
            raise ValueError(f"observed package does not exist: {observed}")
        original = artifact_path(path, packages[package_id])
        if payload(original) != payload(observed):
            raise ValueError(f"externally observed payload differs for {package_id} on {args.feed}")
        row = feed["packages"][package_id]
        external_hash = sha256(observed)
        external_payload_hash = payload_id(observed)
        if row["state"] == "verified" and (row["externalSha256"] != external_hash or row["externalPayloadSha256"] != external_payload_hash):
            raise ValueError(f"non-monotonic external observation for {package_id} on {args.feed}")
        row.update({"state": "verified", "externalSha256": external_hash, "externalPayloadSha256": external_payload_hash, "observedAt": stamp})
        feed["attempts"].append({"at": stamp, "outcome": "verified", "package": package_id, "detail": args.detail})
    feed["state"] = "verified" if all(row["state"] == "verified" for row in feed["packages"].values()) else "partial"
    if previously_partial:
        data["state"]["recovery"]["resumptions"] += 1
    if all(data["state"]["feeds"][feed_name]["state"] == "verified" for feed_name in FEEDS):
        data["state"]["phase"] = "feeds-complete"
    elif data["state"]["feeds"]["github"]["state"] == "verified":
        data["state"]["phase"] = "org-complete"
    data["state"]["updatedAt"] = stamp
    write_atomic(path, data)
    print(f"recorded {len(observations)} observation(s); {args.feed}={feed['state']}")


def command_promote(args: argparse.Namespace) -> None:
    path = pathlib.Path(args.manifest).resolve()
    data = load(path)
    assert_bound(path, data)
    incomplete = [feed for feed in FEEDS if data["state"]["feeds"][feed]["state"] != "verified"]
    if incomplete:
        raise ValueError("stable promotion refused; incomplete feeds: " + ", ".join(incomplete))
    def stable_parts(value: str) -> tuple[int, ...]:
        if "-" in value:
            raise ValueError(f"stable channel version is prerelease: {value}")
        try:
            return tuple(int(part) for part in value.split("."))
        except ValueError as error:
            raise ValueError(f"stable channel version is malformed: {value}") from error
    existing = data["state"]["channelPromotion"]
    if existing["state"] == "promoted":
        if existing["receipt"]["contentId"] != data["contentId"]:
            raise ValueError("stable channel already promoted to different bytes")
        if args.previous_channel:
            live = json.loads(pathlib.Path(args.previous_channel).read_text(encoding="utf-8"))
            if live.get("contentId") != data["contentId"]:
                raise ValueError("live stable channel names different content; refusing older replay")
        if args.channel_output:
            write_atomic(pathlib.Path(args.channel_output).resolve(), existing["receipt"])
        print("stable channel already promoted to these bytes")
        return
    previous_version = data["descriptor"].get("previousStableVersion")
    if args.previous_channel:
        previous = json.loads(pathlib.Path(args.previous_channel).read_text(encoding="utf-8"))
        if previous.get("contentId") == data["contentId"]:
            raise ValueError("previous stable channel already names this content")
        if previous.get("version") != previous_version:
            raise ValueError("previous channel receipt does not match manifest baseline")
    if not previous_version:
        raise ValueError("manifest has no previous stable channel baseline")
    if stable_parts(data["descriptor"]["version"]) <= stable_parts(str(previous_version)):
        raise ValueError("stable channel version must advance monotonically")
    stamp = now()
    receipt = {"contentId": data["contentId"], "version": data["descriptor"]["version"], "sourceSha": data["descriptor"]["sourceSha"], "promotedAt": stamp}
    data["state"]["channelPromotion"] = {"state": "promoted", "promotedAt": stamp, "receipt": receipt}
    data["state"]["phase"] = "promoted"
    data["state"]["updatedAt"] = stamp
    write_atomic(path, data)
    if args.channel_output:
        write_atomic(pathlib.Path(args.channel_output).resolve(), receipt)
    print("stable channel promoted")


def parser() -> argparse.ArgumentParser:
    top = argparse.ArgumentParser()
    commands = top.add_subparsers(dest="command", required=True)
    prepare = commands.add_parser("prepare")
    prepare.add_argument("--release-id", required=True); prepare.add_argument("--version", required=True)
    prepare.add_argument("--source-sha", required=True); prepare.add_argument("--policy-version", required=True)
    prepare.add_argument("--previous-version", required=True)
    prepare.add_argument("--artifact-dir", required=True); prepare.add_argument("--expected-package", action="append", required=True)
    prepare.add_argument("--output", required=True); prepare.set_defaults(run=command_prepare)
    preflight = commands.add_parser("preflight")
    preflight.add_argument("--manifest", required=True); preflight.add_argument("--feed", choices=("both",) + FEEDS, required=True)
    preflight.add_argument("--max-package-bytes", type=int, default=250_000_000)
    preflight.add_argument("--max-release-notes-characters", type=int, default=35_000); preflight.set_defaults(run=command_preflight)
    assertion = commands.add_parser("assert-artifacts")
    assertion.add_argument("--manifest", required=True); assertion.add_argument("--package", action="append"); assertion.set_defaults(run=command_assert)
    reusable = commands.add_parser("assert-reusable")
    reusable.add_argument("--stored", required=True); reusable.add_argument("--candidate", required=True)
    reusable.set_defaults(run=command_reusable)
    identity = commands.add_parser("assert-identity")
    identity.add_argument("--manifest", required=True); identity.add_argument("--release-id", required=True)
    identity.add_argument("--version", required=True); identity.add_argument("--source-sha", required=True)
    identity.add_argument("--policy-version", required=True); identity.set_defaults(run=command_identity)
    merge = commands.add_parser("merge-journals")
    merge.add_argument("--manifest", required=True); merge.add_argument("--journal", action="append", required=True); merge.set_defaults(run=command_merge)
    failure = commands.add_parser("record-failure")
    failure.add_argument("--manifest", required=True); failure.add_argument("--feed", choices=FEEDS, required=True)
    failure.add_argument("--package", required=True); failure.add_argument("--detail", required=True); failure.set_defaults(run=command_failure)
    record = commands.add_parser("record-observed")
    record.add_argument("--manifest", required=True); record.add_argument("--feed", choices=FEEDS, required=True)
    record.add_argument("--observed", action="append", required=True); record.add_argument("--detail", required=True); record.set_defaults(run=command_record)
    promote = commands.add_parser("promote")
    promote.add_argument("--manifest", required=True); promote.add_argument("--channel-output")
    promote.add_argument("--previous-channel"); promote.set_defaults(run=command_promote)
    return top


def main() -> int:
    try:
        args = parser().parse_args()
        args.run(args)
        return 0
    except (OSError, ValueError, KeyError, json.JSONDecodeError, zipfile.BadZipFile, ET.ParseError) as error:
        print(f"release-saga: {error}", file=os.sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
