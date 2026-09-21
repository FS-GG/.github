"""Public selectors resolve privately and fail closed."""

import base64
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("materializer", ROOT / "tools/telemetry-public-item-registry.py")
M = importlib.util.module_from_spec(spec)
spec.loader.exec_module(M)


class MaterializerTests(unittest.TestCase):
    def fixture(self):
        registry = {"schema": "fsgg.telemetry.public-item-registry/1", "items": {
            "public-work": {"label": "Public work", "url": "https://github.com/FS-GG/.github/issues/1",
                            "repositories": ["FS-GG/.github"], "deliveries": [
                                {"repository": "FS-GG/.github", "pullRequest": 2}], "notes": []}}}
        snapshot = {"selection": {"mode": "all", "complete": True},
                    "store": {"schemaVersion": 10, "journalMode": "wal"},
                    "populations": [{"item_id": "private-attempt", "original_item_id": "private-root", "state": "completed"}],
                    "outcomes": [{"item_id": "private-attempt", "repository": "FS-GG/.github", "pr_number": 2,
                                  "outcome": "delivered", "code_delivery": "delivered"}]}
        raw = json.dumps(snapshot, sort_keys=True, separators=(",", ":")).encode()
        envelope = {"schema": "fsgg.telemetry.item-detail/2", "observedAt": "2026-09-21T00:00:00Z",
                    "revision": hashlib.sha256(raw).hexdigest(),
                    "canonicalSnapshotGzip": base64.b64encode(gzip.compress(raw)).decode(),
                    "operational": {"pendingBatches": 0}}
        base = {"schema": "fsgg.telemetry.dashboard-labels/1", "items": {}, "models": {}, "efforts": {}, "scopes": {}}
        return registry, snapshot, envelope, base

    def test_resolves_public_delivery_without_public_private_id(self):
        registry, snapshot, _, base = self.fixture()
        M.validate_registry(registry)
        result = M.materialize(registry, snapshot, base)
        self.assertNotIn("private-root", json.dumps(registry))
        self.assertEqual(result["items"]["private-root"]["key"], "public-work")

    def test_requires_exactly_one_completed_delivered_match(self):
        registry, snapshot, _, base = self.fixture()
        snapshot["outcomes"] = []
        with self.assertRaisesRegex(M.Refusal, "exactly once"):
            M.materialize(registry, snapshot, base)

    def test_rejects_duplicate_selector_and_private_notes(self):
        registry, _, _, _ = self.fixture()
        registry["items"]["other"] = json.loads(json.dumps(registry["items"]["public-work"]))
        with self.assertRaisesRegex(M.Refusal, "duplicate"):
            M.validate_registry(registry)
        registry.pop("items")
        with self.assertRaisesRegex(M.Refusal, "public registry"):
            M.validate_registry(registry)

    def test_accepts_complete_digest_bound_snapshot(self):
        _, _, envelope, _ = self.fixture()
        snapshot = M.load_snapshot(envelope)
        self.assertEqual(snapshot["store"]["schemaVersion"], 10)


if __name__ == "__main__":
    unittest.main()
