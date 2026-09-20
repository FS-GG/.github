"""Guard the one-writer label activation rotation at its durable boundaries."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import pathlib
import tempfile
import unittest
from types import SimpleNamespace
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[2]


def load(name: str, path: pathlib.Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


D = load("telemetry_dashboard_rotation_test", ROOT / "tools/telemetry-dashboard.py")
R = load("telemetry_label_rotation_test", ROOT / "tools/telemetry-handoff-label-rotation.py")


class RotationTests(unittest.TestCase):
    def fixture(self, root: pathlib.Path):
        state, candidate, outgoing = (root / name for name in ("state", "candidate", "outgoing"))
        for directory in (state, candidate, outgoing):
            directory.mkdir(mode=0o700)
        old_label, new_label = "1" * 64, "2" * 64
        config = {"outgoingDir": str(outgoing), "producerUid": 10, "handoffGid": 20,
                  "labelsDigest": old_label, "destination": {"repository": "FS-GG/.github", "branch": "telemetry-data", "path": "host.json"},
                  "credentialSource": "environment-only"}
        candidate_digest = hashlib.sha256((ROOT / "tools/telemetry-dashboard.py").read_bytes()).hexdigest()
        old = {"config": config, "candidateDigest": candidate_digest}
        new = {"config": {**config, "labelsDigest": new_label}, "candidateDigest": candidate_digest}
        D.atomic_private(state / D.HANDOFF_ACTIVATION_NAME, old)
        D.atomic_private(candidate / D.HANDOFF_ACTIVATION_NAME, new)
        served = {"revision": "4" * 64}
        raw = D.dump(served)
        digest = hashlib.sha256(raw).hexdigest()
        D.atomic_private(state / ("snapshot-" + digest + ".json"), served)
        success = {"schema": D.HANDOFF_SUCCESS_SCHEMA, "status": "published", "intentSnapshotDigest": "5" * 64,
                   "publishedSnapshotDigest": digest, "publishedSnapshotFile": "snapshot-" + digest + ".json",
                   "publicRevision": served["revision"], "commit": "6" * 40, "completedAt": "2026-09-20T01:50:00Z"}
        D.atomic_private(state / D.HANDOFF_SUCCESS_NAME, success)
        fake = SimpleNamespace(
            _validate_owned_directory=D._validate_owned_directory, _acquire_owned_lock=D._acquire_owned_lock,
            _load_handoff_activation=lambda path: json.loads(D.read_private_bytes(path / D.HANDOFF_ACTIVATION_NAME, 16384, "INVALID")),
            _load_publisher_intent=lambda path: None,
            _load_handoff=lambda *_: ({"labelsDigest": new_label, "snapshotDigest": "7" * 64}, served, raw),
            _candidate_digest=lambda: candidate_digest, read_private_bytes=D.read_private_bytes,
            exact=D.exact, validate_host=lambda _: None, dump=D.dump, atomic_private=D.atomic_private,
            publication_token=lambda _: "token", current_publication=lambda *_: (served, "6" * 40),
            verify_publication=lambda *_: {"verified": True},
            HANDOFF_LOCK_NAME=D.HANDOFF_LOCK_NAME, HANDOFF_ACTIVATION_NAME=D.HANDOFF_ACTIVATION_NAME,
            HANDOFF_SUCCESS_NAME=D.HANDOFF_SUCCESS_NAME, HANDOFF_SUCCESS_SCHEMA=D.HANDOFF_SUCCESS_SCHEMA,
            MAX_JSON=D.MAX_JSON)
        args = argparse.Namespace(publisher_script=ROOT / "tools/telemetry-dashboard.py", state_dir=state,
                                  candidate_state_dir=candidate, from_labels=old_label, to_labels=new_label,
                                  expected_remote_commit="6" * 40, authorize_label_rotation=True)
        return fake, args, old, new

    def test_rotates_same_state_after_exact_remote_readback(self):
        with tempfile.TemporaryDirectory() as temp:
            fake, args, old, new = self.fixture(pathlib.Path(temp))
            with mock.patch.object(R, "publisher", return_value=fake):
                result = R.rotate(args)
            self.assertEqual((result["status"], result["remoteBaseline"]), ("rotated", "6" * 40))
            self.assertEqual(fake._load_handoff_activation(args.state_dir), new)
            archive = args.state_dir / ("activation-before-label-rotation-" + hashlib.sha256(D.dump(old)).hexdigest() + ".json")
            self.assertEqual(json.loads(archive.read_bytes()), old)

    def test_refuses_pending_or_changed_remote_without_touching_activation(self):
        with tempfile.TemporaryDirectory() as temp:
            fake, args, old, _ = self.fixture(pathlib.Path(temp))
            with mock.patch.object(R, "publisher", return_value=fake):
                fake._load_publisher_intent = lambda _: {"pending": True}
                with self.assertRaisesRegex(ValueError, "ROTATION_INTENT_PENDING"):
                    R.rotate(args)
                fake._load_publisher_intent = lambda _: None
                fake.current_publication = lambda *_: ({"revision": "9" * 64}, "8" * 40)
                with self.assertRaisesRegex(ValueError, "ROTATION_REMOTE_DRIFT"):
                    R.rotate(args)
            self.assertEqual(fake._load_handoff_activation(args.state_dir), old)

    def test_refuses_unapproved_digest_or_stage_mismatch(self):
        with tempfile.TemporaryDirectory() as temp:
            fake, args, old, _ = self.fixture(pathlib.Path(temp))
            with mock.patch.object(R, "publisher", return_value=fake):
                args.authorize_label_rotation = False
                with self.assertRaisesRegex(ValueError, "ROTATION_NOT_AUTHORIZED"):
                    R.rotate(args)
                args.authorize_label_rotation = True
                fake._load_handoff = lambda *_: ({"labelsDigest": "9" * 64}, {}, b"")
                with self.assertRaisesRegex(ValueError, "ROTATION_STAGE_MISMATCH"):
                    R.rotate(args)
            self.assertEqual(fake._load_handoff_activation(args.state_dir), old)


if __name__ == "__main__":
    unittest.main()
