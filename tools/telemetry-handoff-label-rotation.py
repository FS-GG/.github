#!/usr/bin/env python3
"""Rotate one handoff publisher's approved labels without creating another writer.

The candidate activation is first produced by the installed publisher's
``handoff-setup`` in a fresh private state directory. This helper only replaces
the active receipt after reconciling the last publication and staged snapshot.
It never stages data, publishes, or changes a service/timer.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import pathlib
import re
import stat
import sys


def publisher(path: pathlib.Path):
    script = path.resolve(strict=True)
    spec = importlib.util.spec_from_file_location("telemetry_dashboard_installed", script)
    if spec is None or spec.loader is None:
        raise ValueError("ROTATION_PUBLISHER_INVALID")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ValueError(code)


def rotate(args: argparse.Namespace) -> dict[str, str]:
    require(args.authorize_label_rotation, "ROTATION_NOT_AUTHORIZED")
    for digest in (args.from_labels, args.to_labels):
        require(bool(re.fullmatch(r"[0-9a-f]{64}", digest)), "ROTATION_LABEL_DIGEST_INVALID")
    require(args.from_labels != args.to_labels, "ROTATION_LABELS_UNCHANGED")
    require(bool(re.fullmatch(r"[0-9a-f]{40}", args.expected_remote_commit)), "ROTATION_BASELINE_INVALID")

    state = args.state_dir.resolve(strict=True)
    candidate_state = args.candidate_state_dir.resolve(strict=True)
    uid, gid = os.getuid(), os.getgid()
    require(state != candidate_state, "ROTATION_STATE_COLLISION")
    state_info = state.lstat()
    require(stat.S_ISDIR(state_info.st_mode) and stat.S_IMODE(state_info.st_mode) == 0o700
            and state_info.st_uid == uid and state_info.st_gid == gid, "ROTATION_STATE_UNSAFE")
    script = args.publisher_script
    script_info = script.lstat()
    require(not script.is_symlink() and stat.S_ISREG(script_info.st_mode)
            and script_info.st_uid in {0, uid} and not script_info.st_mode & 0o022,
            "ROTATION_PUBLISHER_UNSAFE")
    activation_file = state / "activation.json"
    activation_info = activation_file.lstat()
    require(not activation_file.is_symlink() and stat.S_ISREG(activation_info.st_mode)
            and stat.S_IMODE(activation_info.st_mode) == 0o600
            and activation_info.st_uid == uid and activation_info.st_gid == gid,
            "ROTATION_ACTIVATION_UNSAFE")
    original = json.loads(activation_file.read_bytes())
    require(isinstance(original, dict) and hashlib.sha256(script.read_bytes()).hexdigest()
            == original.get("candidateDigest"), "ROTATION_PUBLISHER_MISMATCH")
    d = publisher(script)
    d._validate_owned_directory(state, 0o700, uid, gid, "ROTATION_STATE_UNSAFE")
    d._validate_owned_directory(candidate_state, 0o700, uid, gid, "ROTATION_CANDIDATE_UNSAFE")
    lock = d._acquire_owned_lock(state, d.HANDOFF_LOCK_NAME, 0o600, uid, gid, "ROTATION_LOCK_UNSAFE")
    try:
        old = d._load_handoff_activation(state)
        new = d._load_handoff_activation(candidate_state)
        old_config, new_config = old["config"], new["config"]
        require(old_config["labelsDigest"] == args.from_labels, "ROTATION_OLD_LABEL_MISMATCH")
        require(new_config["labelsDigest"] == args.to_labels, "ROTATION_NEW_LABEL_MISMATCH")
        require(old["candidateDigest"] == new["candidateDigest"] == d._candidate_digest(), "ROTATION_CANDIDATE_MISMATCH")
        require(
            {key: value for key, value in old_config.items() if key != "labelsDigest"}
            == {key: value for key, value in new_config.items() if key != "labelsDigest"},
            "ROTATION_CONFIG_DRIFT",
        )
        require(d._load_publisher_intent(state) is None, "ROTATION_INTENT_PENDING")

        manifest, _, _ = d._load_handoff(
            pathlib.Path(old_config["outgoingDir"]), old_config["producerUid"], old_config["handoffGid"]
        )
        require(manifest["labelsDigest"] == args.to_labels, "ROTATION_STAGE_MISMATCH")

        success = json.loads(d.read_private_bytes(state / d.HANDOFF_SUCCESS_NAME, 8192, "ROTATION_SUCCESS_MISSING"))
        d.exact(success, {"schema", "status", "intentSnapshotDigest", "publishedSnapshotDigest", "publishedSnapshotFile",
                          "publicRevision", "commit", "completedAt"}, "rotation last success")
        require(success["schema"] == d.HANDOFF_SUCCESS_SCHEMA, "ROTATION_SUCCESS_INVALID")
        require(success["commit"] == args.expected_remote_commit, "ROTATION_BASELINE_MISMATCH")
        digest = success["publishedSnapshotDigest"]
        require(bool(re.fullmatch(r"[0-9a-f]{64}", digest)), "ROTATION_SUCCESS_INVALID")
        require(success["publishedSnapshotFile"] == "snapshot-" + digest + ".json", "ROTATION_SUCCESS_INVALID")
        raw = d.read_private_bytes(state / success["publishedSnapshotFile"], d.MAX_JSON, "ROTATION_SUCCESS_INVALID")
        require(hashlib.sha256(raw).hexdigest() == digest, "ROTATION_SUCCESS_INVALID")
        served = json.loads(raw)
        d.validate_host(served)
        require(d.dump(served) == raw and served["revision"] == success["publicRevision"], "ROTATION_SUCCESS_INVALID")

        destination = old_config["destination"]
        token = d.publication_token(old_config["credentialSource"])
        current, commit = d.current_publication(destination["repository"], destination["branch"], destination["path"], token)
        require(commit == args.expected_remote_commit and current is not None and d.dump(current) == raw,
                "ROTATION_REMOTE_DRIFT")
        check = d.verify_publication(destination["repository"], destination["branch"], destination["path"],
                                     token, commit, served)
        require(check["verified"], "ROTATION_REMOTE_UNVERIFIED")

        old_digest = hashlib.sha256(d.dump(old)).hexdigest()
        archive = state / ("activation-before-label-rotation-" + old_digest + ".json")
        if archive.exists() or archive.is_symlink():
            require(d.read_private_bytes(archive, 16384, "ROTATION_ARCHIVE_INVALID") == d.dump(old),
                    "ROTATION_ARCHIVE_CONFLICT")
        else:
            d.atomic_private(archive, old)
        d.atomic_private(state / d.HANDOFF_ACTIVATION_NAME, new)
        try:
            require(d._load_handoff_activation(state) == new, "ROTATION_READBACK_FAILED")
        except Exception:
            d.atomic_private(state / d.HANDOFF_ACTIVATION_NAME, old)
            raise
        return {"schema": "fsgg.telemetry.handoff-label-rotation/1", "status": "rotated",
                "fromLabelsDigest": args.from_labels, "toLabelsDigest": args.to_labels,
                "stagedSnapshotDigest": manifest["snapshotDigest"], "remoteBaseline": commit,
                "oldActivationDigest": old_digest, "newActivationDigest": hashlib.sha256(d.dump(new)).hexdigest()}
    finally:
        os.close(lock)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publisher-script", type=pathlib.Path, required=True)
    parser.add_argument("--state-dir", type=pathlib.Path, required=True)
    parser.add_argument("--candidate-state-dir", type=pathlib.Path, required=True)
    parser.add_argument("--from-labels", required=True)
    parser.add_argument("--to-labels", required=True)
    parser.add_argument("--expected-remote-commit", required=True)
    parser.add_argument("--authorize-label-rotation", action="store_true")
    args = parser.parse_args()
    try:
        print(json.dumps(rotate(args), sort_keys=True, separators=(",", ":")))
        return 0
    except (ValueError, OSError, RuntimeError) as error:
        print(f"telemetry handoff label rotation: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
