import copy
import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "host_pending_census", ROOT / "scripts/gs2-09-7-host-pending-census.py")
census = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(census)

ORIGIN = "https://protected.example.invalid"
QUEUE_ID = "pending-queue-test"
QUEUE_ENDPOINT = ORIGIN + "/queue"
JOURNAL_ID = "pending-journal-test"
JOURNAL_ENDPOINT = ORIGIN + "/journal"
FINALIZER_ID = "finalizer-ledger-test"
VAULT_ID = "protected-token-vault-test"
RECOVERY_ID = "recovery-journal-test"
WORKER_ID = "protected-recovery-worker-test"
SEAL_ID = "f" * 64


def subject(sequence):
    return {
        "schema": census.SUBJECT_SCHEMA,
        "sequence": sequence,
        "mintId": format(sequence, "064x"),
        "bindingId": format(sequence + 100, "064x"),
        "tokenSha256": format(sequence + 200, "064x"),
        "contextSha256": format(sequence + 300, "064x"),
        "sandboxRepositoryId": census.worker.finalizer.release.host.SANDBOX_ID,
        "appId": census.worker.finalizer.release.host.APP_ID,
        "actor": census.worker.finalizer.release.host.ACTOR,
        "installationId": 123,
        "journalResourceId": JOURNAL_ID,
        "finalizerResourceId": FINALIZER_ID,
        "vaultId": VAULT_ID,
        "recoveryResourceId": RECOVERY_ID,
        "revokeRequired": True,
    }


class FakeCensusPort:
    def __init__(self):
        self.calls = []
        self.items = [subject(1), subject(2), subject(3)]
        self.pages = {
            "0": {"schema": census.PAGE_SCHEMA, "sealId": SEAL_ID,
                  "highWater": 3, "cursor": "0", "items": self.items[:2],
                  "nextCursor": "2"},
            "2": {"schema": census.PAGE_SCHEMA, "sealId": SEAL_ID,
                  "highWater": 3, "cursor": "2", "items": self.items[2:],
                  "nextCursor": None},
        }
        self.seal = {
            "schema": census.SEAL_SCHEMA, "sealId": SEAL_ID,
            "highWater": 3, "pendingCount": 3,
            "pendingSha256": census._digest(self.items),
            "queueResourceId": QUEUE_ID, "journalResourceId": JOURNAL_ID,
            "finalizerResourceId": FINALIZER_ID, "vaultId": VAULT_ID,
            "recoveryResourceId": RECOVERY_ID, "workerId": WORKER_ID,
            "complete": True, "snapshotIsolation": True,
        }
        self.high_water = 3
        self.seal_reads = 0
        self.drift_seal_on_read = None
        self.watermark_reads = 0
        self.drift_watermark_on_read = None
        self.subjects = {item["mintId"]: item for item in self.items}
        self.bindings = {
            item["mintId"]: {
                "schema": census.worker.BINDING_SCHEMA,
                "mintId": item["mintId"],
                "bindingId": item["bindingId"],
                "tokenSha256": item["tokenSha256"],
                "contextSha256": item["contextSha256"],
                "sandboxRepositoryId": item["sandboxRepositoryId"],
                "appId": item["appId"],
                "actor": item["actor"],
                "installationId": item["installationId"],
                "vaultId": VAULT_ID,
                "finalizerResourceId": FINALIZER_ID,
                "verifiedAtCommit": True,
            } for item in self.items
        }
        self.queue_descriptor = {
            "schema": census.QUEUE_SCHEMA, "origin": ORIGIN,
            "resourceId": QUEUE_ID, "endpoint": QUEUE_ENDPOINT,
            "journalResourceId": JOURNAL_ID, "vaultId": VAULT_ID,
            "recoveryResourceId": RECOVERY_ID,
            "credentialScope": "protected-host-only",
            "candidateCanWrite": False, "snapshotIsolation": True,
        }
        self.journal_descriptor = {
            "schema": census.JOURNAL_SCHEMA, "origin": ORIGIN,
            "resourceId": JOURNAL_ID, "endpoint": JOURNAL_ENDPOINT,
            "finalizerResourceId": FINALIZER_ID, "vaultId": VAULT_ID,
            "recoveryResourceId": RECOVERY_ID, "durable": True,
            "atomicSeal": True, "nativeReadback": True,
            "credentialScope": "protected-host-only", "candidateCanWrite": False,
        }

    def describe_queue(self):
        self.calls.append("describe-queue")
        return self.queue_descriptor

    def describe_journal(self):
        self.calls.append("describe-journal")
        return self.journal_descriptor

    def seal_snapshot(self):
        self.calls.append("seal-snapshot")
        return copy.deepcopy(self.seal)

    def read_seal(self, seal_id):
        self.calls.append("read-seal")
        self.seal_reads += 1
        value = copy.deepcopy(self.seal)
        if self.drift_seal_on_read == self.seal_reads:
            value["pendingCount"] -= 1
        return value

    def read_high_water(self):
        self.calls.append("read-high-water")
        self.watermark_reads += 1
        value = self.high_water
        if self.drift_watermark_on_read == self.watermark_reads:
            value += 1
        return {"schema": census.WATERMARK_SCHEMA,
                "journalResourceId": JOURNAL_ID, "highWater": value}

    def read_page(self, seal_id, cursor):
        self.calls.append("read-page:" + cursor)
        return copy.deepcopy(self.pages[cursor])

    def read_subject(self, seal_id, mint_id):
        self.calls.append("read-subject")
        return copy.deepcopy(self.subjects[mint_id])

    def read_binding(self, mint_id):
        self.calls.append("read-binding")
        return copy.deepcopy(self.bindings[mint_id])


class PendingCensusTests(unittest.TestCase):
    def setUp(self):
        self.port = FakeCensusPort()
        pins = {
            (census, "PINNED_QUEUE_ORIGIN"): ORIGIN,
            (census, "PINNED_QUEUE_RESOURCE_ID"): QUEUE_ID,
            (census, "PINNED_QUEUE_ENDPOINT"): QUEUE_ENDPOINT,
            (census, "PINNED_JOURNAL_ORIGIN"): ORIGIN,
            (census, "PINNED_JOURNAL_RESOURCE_ID"): JOURNAL_ID,
            (census, "PINNED_JOURNAL_ENDPOINT"): JOURNAL_ENDPOINT,
            (census.worker, "PINNED_RECOVERY_RESOURCE_ID"): RECOVERY_ID,
            (census.worker, "PINNED_RECOVERY_WORKER_ID"): WORKER_ID,
            (census.worker.finalizer, "PINNED_FINALIZER_RESOURCE_ID"): FINALIZER_ID,
            (census.worker.finalizer, "PINNED_TOKEN_VAULT_ID"): VAULT_ID,
        }
        for (module, name), value in pins.items():
            original = getattr(module, name)
            setattr(module, name, value)
            self.addCleanup(setattr, module, name, original)

    def scan(self):
        return census.census_pending(self.port)

    def test_complete_sealed_scan_returns_only_exact_worker_subjects(self):
        result = self.scan()
        self.assertEqual(3, result["subjectCount"])
        self.assertEqual([{"sequence": item["sequence"],
                           "mintId": item["mintId"],
                           "bindingId": item["bindingId"]}
                          for item in self.port.items], result["subjects"])
        self.assertEqual("pending", result["disposition"])
        self.assertEqual(2, self.port.calls.count("read-seal"))
        self.assertEqual(2, self.port.calls.count("read-high-water"))
        self.assertNotIn("tokenSha256", json.dumps(result))

    def test_complete_empty_pending_set_with_nonzero_high_water(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = census._digest([])
        self.port.pages = {"0": {"schema": census.PAGE_SCHEMA,
                                 "sealId": SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        result = self.scan()
        self.assertEqual(0, result["subjectCount"])
        self.assertEqual([], result["subjects"])

    def test_omitted_last_page_or_wrong_digest_refuses_all_subjects(self):
        self.port.pages["0"]["nextCursor"] = None
        with self.assertRaisesRegex(census.Refused, "census-omission"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.seal["pendingSha256"] = "0" * 64
        with self.assertRaisesRegex(census.Refused, "census-omission"):
            self.scan()

    def test_unknown_or_partial_page_refuses(self):
        self.port.pages.pop("2")
        with self.assertRaisesRegex(census.Refused, "census-unknown"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.pages["2"]["items"] = []
        with self.assertRaisesRegex(census.Refused, "partial-page"):
            self.scan()

    def test_duplicate_mint_binding_or_order_refuses(self):
        self.port.pages["2"]["items"] = [self.port.items[0]]
        with self.assertRaisesRegex(census.Refused, "duplicate-or-order"):
            self.scan()
        self.port = FakeCensusPort()
        duplicate_binding = {**self.port.items[2],
                             "bindingId": self.port.items[0]["bindingId"]}
        self.port.pages["2"]["items"] = [duplicate_binding]
        with self.assertRaisesRegex(census.Refused, "duplicate-or-order"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.pages["0"]["items"] = list(reversed(self.port.items[:2]))
        with self.assertRaisesRegex(census.Refused, "duplicate-or-order"):
            self.scan()

    def test_repeated_or_skipped_cursor_refuses(self):
        self.port.pages["0"]["nextCursor"] = "0"
        with self.assertRaisesRegex(census.Refused, "cursor"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.pages["0"]["nextCursor"] = "3"
        with self.assertRaisesRegex(census.Refused, "cursor"):
            self.scan()

    def test_stale_or_moving_high_water_refuses(self):
        self.port.high_water = 4
        with self.assertRaisesRegex(census.Refused, "high-water-drift"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.drift_watermark_on_read = 2
        with self.assertRaisesRegex(census.Refused, "high-water-drift"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.pages["2"]["highWater"] = 4
        with self.assertRaisesRegex(census.Refused, "page"):
            self.scan()

    def test_seal_mismatch_before_or_after_scan_refuses(self):
        self.port.drift_seal_on_read = 1
        with self.assertRaises(census.Refused):
            self.scan()
        self.port = FakeCensusPort()
        self.port.drift_seal_on_read = 2
        with self.assertRaises(census.Refused):
            self.scan()

    def test_subject_native_readback_or_target_mismatch_refuses(self):
        self.port.subjects[self.port.items[1]["mintId"]] = {
            **self.port.items[1], "vaultId": "foreign-vault"}
        with self.assertRaisesRegex(census.Refused, "subject-identity"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.pages["2"]["items"][0]["recoveryResourceId"] = "foreign-recovery"
        with self.assertRaisesRegex(census.Refused, "subject-identity"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.pages["2"]["items"][0]["sandboxRepositoryId"] = 1
        with self.assertRaisesRegex(census.Refused, "subject-identity"):
            self.scan()

    def test_protected_binding_readback_mismatch_refuses_sealed_queue(self):
        self.port.bindings[self.port.items[1]["mintId"]]["bindingId"] = "d" * 64
        with self.assertRaisesRegex(census.Refused, "binding-readback"):
            self.scan()
        self.assertIn("read-binding", self.port.calls)

    def test_false_complete_seal_or_candidate_writable_queue_refuses(self):
        self.port.seal["complete"] = 1
        with self.assertRaisesRegex(census.Refused, "seal-identity"):
            self.scan()
        self.port = FakeCensusPort()
        self.port.queue_descriptor["candidateCanWrite"] = True
        with self.assertRaisesRegex(census.Refused, "queue-authority"):
            self.scan()
        self.assertNotIn("seal-snapshot", self.port.calls)

    def test_unconfigured_pin_or_foreign_journal_refuses_before_seal(self):
        census.PINNED_JOURNAL_RESOURCE_ID = ""
        with self.assertRaisesRegex(census.Refused, "census-unconfigured"):
            self.scan()
        self.assertEqual([], self.port.calls)
        census.PINNED_JOURNAL_RESOURCE_ID = JOURNAL_ID
        self.port.journal_descriptor["vaultId"] = "foreign-vault"
        with self.assertRaisesRegex(census.Refused, "journal-authority"):
            self.scan()
        self.assertNotIn("seal-snapshot", self.port.calls)


if __name__ == "__main__":
    unittest.main()
