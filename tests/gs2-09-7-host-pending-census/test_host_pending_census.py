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
JOINT_SPEC = importlib.util.spec_from_file_location(
    "census_joint_signing_fixture",
    ROOT / "tests/gs2-09-7-host-joint-seal/signing_fixture.py")
signing = importlib.util.module_from_spec(JOINT_SPEC)
JOINT_SPEC.loader.exec_module(signing)

ORIGIN = "https://protected.example.invalid"
QUEUE_ID = "pending-queue-test"
QUEUE_ENDPOINT = ORIGIN + "/queue"
JOURNAL_ID = "pending-journal-test"
JOURNAL_ENDPOINT = ORIGIN + "/journal"
FINALIZER_ID = "finalizer-ledger-test"
VAULT_ID = "protected-token-vault-test"
NATIVE_ATTEMPT_RESOURCE = "protected-native-attempt-test"
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


def terminal_receipt(entry):
    return {
        "schema": census.TERMINAL_SCHEMA,
        "sequence": entry["sequence"], "mintId": entry["mintId"],
        "bindingId": entry["bindingId"],
        "tokenSha256": entry["tokenSha256"],
        "installationId": entry["installationId"],
        "journalResourceId": JOURNAL_ID, "vaultId": VAULT_ID,
        "state": "revoked", "nativeObserved": True,
    }


class FakeCensusPort:
    def __init__(self):
        self.calls = []
        self.items = [subject(1), subject(2), subject(3)]
        self.mints = [{"sequence": item["sequence"], "mintId": item["mintId"],
                       "bindingId": item["bindingId"],
                       "tokenSha256": item["tokenSha256"],
                       "installationId": item["installationId"]}
                      for item in self.items]
        self.mint_subjects = {item["mintId"]: item for item in self.mints}
        self.terminal_receipts = {}
        self.native_terminal_states = {
            item["mintId"]: "revoked" for item in self.mints}
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
            "mintCount": 3, "mintSha256": census._digest(self.mints),
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
        signing.attach(census.joint, self, lambda: self.seal)

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

    def read_mint_index(self, seal_id):
        self.calls.append("read-mint-index")
        return {"schema": census.MINT_INDEX_SCHEMA, "sealId": SEAL_ID,
                "highWater": 3, "journalResourceId": JOURNAL_ID,
                "complete": True, "mintSha256": census._digest(self.mints),
                "items": copy.deepcopy(self.mints)}

    def read_mint_subject(self, seal_id, mint_id):
        self.calls.append("read-mint-subject")
        return copy.deepcopy(self.mint_subjects[mint_id])

    def read_terminal_receipt(self, mint_id):
        self.calls.append("read-terminal-receipt")
        return copy.deepcopy(self.terminal_receipts.get(mint_id))

    def read_native_terminal(self, seal_id, mint_id, challenge):
        self.calls.append("read-native-terminal")
        entry = self.mint_subjects[mint_id]
        return {"schema": census.NATIVE_TERMINAL_SCHEMA,
                "sealId": seal_id, "mintId": mint_id,
                "tokenSha256": entry["tokenSha256"],
                "installationId": entry["installationId"],
                "sandboxRepositoryId": census.worker.finalizer.release.host.SANDBOX_ID,
                "appId": census.worker.finalizer.release.host.APP_ID,
                "actor": census.worker.finalizer.release.host.ACTOR,
                "revokerId": census.worker.finalizer.PINNED_REVOKER_ID,
                "challenge": challenge,
                "state": self.native_terminal_states[mint_id]}


class PendingCensusTests(unittest.TestCase):
    def setUp(self):
        self.port = FakeCensusPort()
        signing.pin(self, census.joint)
        pins = {
            (census, "PINNED_QUEUE_ORIGIN"): ORIGIN,
            (census, "PINNED_QUEUE_RESOURCE_ID"): QUEUE_ID,
            (census, "PINNED_QUEUE_ENDPOINT"): QUEUE_ENDPOINT,
            (census, "PINNED_JOURNAL_ORIGIN"): ORIGIN,
            (census, "PINNED_JOURNAL_RESOURCE_ID"): JOURNAL_ID,
            (census, "PINNED_JOURNAL_ENDPOINT"): JOURNAL_ENDPOINT,
            (census.worker, "PINNED_RECOVERY_ORIGIN"): ORIGIN,
            (census.worker, "PINNED_RECOVERY_RESOURCE_ID"): RECOVERY_ID,
            (census.worker, "PINNED_RECOVERY_ENDPOINT"): ORIGIN + "/recovery",
            (census.worker, "PINNED_RECOVERY_WORKER_ID"): WORKER_ID,
            (census.worker.finalizer, "PINNED_FINALIZER_ORIGIN"): ORIGIN,
            (census.worker.finalizer, "PINNED_FINALIZER_RESOURCE_ID"): FINALIZER_ID,
            (census.worker.finalizer, "PINNED_FINALIZER_ENDPOINT"):
                ORIGIN + "/pending",
            (census.worker.finalizer, "PINNED_TOKEN_VAULT_ID"): VAULT_ID,
            (census.worker.finalizer, "PINNED_REVOKER_ID"):
                "protected-native-revoker-test",
            (census.worker.finalizer, "PINNED_NATIVE_ATTEMPT_RESOURCE_ID"):
                NATIVE_ATTEMPT_RESOURCE,
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

    def test_missing_shared_native_attempt_pin_cannot_advertise_subjects(self):
        census.worker.finalizer.PINNED_NATIVE_ATTEMPT_RESOURCE_ID = ""
        with self.assertRaisesRegex(census.Refused, "census-unconfigured"):
            self.scan()
        self.assertNotIn("read-page", self.port.calls)

    def test_boolean_shared_native_attempt_pin_cannot_advertise_subjects(self):
        census.worker.finalizer.PINNED_NATIVE_ATTEMPT_RESOURCE_ID = True
        with self.assertRaisesRegex(census.Refused, "census-unconfigured"):
            self.scan()
        self.assertNotIn("read-page", self.port.calls)

    def test_unsigned_complete_seal_cannot_return_subjects(self):
        self.port.read_joint_seal_envelope = None
        with self.assertRaisesRegex(census.Refused, "joint-seal"):
            self.scan()

    def test_replayed_signed_seal_after_head_advance_returns_no_subjects(self):
        old = self.port.read_joint_seal_envelope(SEAL_ID)
        self.port.joint_generation = 2
        self.port.joint_envelope_override = old
        with self.assertRaisesRegex(census.Refused, "joint-seal-binding"):
            self.scan()
        self.assertNotIn("read-page:0", self.port.calls)

    def test_head_advance_during_complete_scan_refuses_all_subjects(self):
        original = self.port.read_page
        def advance(seal_id, cursor):
            result = original(seal_id, cursor)
            self.port.joint_generation = 2
            return result
        self.port.read_page = advance
        with self.assertRaisesRegex(census.Refused, "joint-seal-drift"):
            self.scan()

    def test_complete_empty_pending_set_with_nonzero_high_water(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = census._digest([])
        self.port.pages = {"0": {"schema": census.PAGE_SCHEMA,
                                 "sealId": SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        self.port.terminal_receipts = {
            item["mintId"]: terminal_receipt(item) for item in self.port.mints}
        result = self.scan()
        self.assertEqual(0, result["subjectCount"])
        self.assertEqual([], result["subjects"])

    def test_minted_before_pending_cannot_disappear_from_empty_pending_scan(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = census._digest([])
        self.port.pages = {"0": {"schema": census.PAGE_SCHEMA,
                                 "sealId": SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        with self.assertRaisesRegex(census.Refused, "mint-unaccounted"):
            self.scan()

    def test_omitted_or_unavailable_mint_index_refuses_entire_scan(self):
        self.port.mints.pop()
        with self.assertRaisesRegex(census.Refused, "mint-index"):
            self.scan()
        self.port = FakeCensusPort()
        def unavailable(_seal_id):
            raise OSError("protected mint index unavailable")
        self.port.read_mint_index = unavailable
        with self.assertRaisesRegex(census.Refused, "census-unknown"):
            self.scan()

    def test_self_consistent_mint_index_drift_refuses_pending_binding(self):
        self.port.mints[1] = {**self.port.mints[1], "mintId": "e" * 64}
        self.port.seal["mintSha256"] = census._digest(self.port.mints)
        self.port.mint_subjects = {
            item["mintId"]: item for item in self.port.mints}
        with self.assertRaisesRegex(census.Refused, "mint-pending-binding"):
            self.scan()

    def test_false_terminal_native_readback_cannot_hide_mint(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = census._digest([])
        self.port.pages = {"0": {"schema": census.PAGE_SCHEMA,
                                 "sealId": SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        self.port.terminal_receipts = {
            item["mintId"]: terminal_receipt(item) for item in self.port.mints}
        self.port.terminal_receipts[self.port.mints[1]["mintId"]][
            "nativeObserved"] = 1
        with self.assertRaisesRegex(census.Refused, "mint-unaccounted"):
            self.scan()

    def test_journal_terminal_receipt_cannot_hide_active_native_token(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = census._digest([])
        self.port.pages = {"0": {"schema": census.PAGE_SCHEMA,
                                 "sealId": SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        self.port.terminal_receipts = {
            item["mintId"]: terminal_receipt(item) for item in self.port.mints}
        self.port.native_terminal_states = {
            item["mintId"]: "revoked" for item in self.port.mints}
        self.port.native_terminal_states[self.port.mints[1]["mintId"]] = "active"
        with self.assertRaisesRegex(census.Refused, "native-terminal"):
            self.scan()

    def test_stale_or_foreign_native_terminal_readback_refuses(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = census._digest([])
        self.port.pages = {"0": {"schema": census.PAGE_SCHEMA,
                                 "sealId": SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        self.port.terminal_receipts = {
            item["mintId"]: terminal_receipt(item) for item in self.port.mints}
        original = self.port.read_native_terminal
        def stale(seal_id, mint_id, challenge):
            return {**original(seal_id, mint_id, challenge),
                    "challenge": "0" * 64}
        self.port.read_native_terminal = stale
        with self.assertRaisesRegex(census.Refused, "native-terminal"):
            self.scan()
        def foreign(seal_id, mint_id, challenge):
            return {**original(seal_id, mint_id, challenge),
                    "tokenSha256": "e" * 64}
        self.port.read_native_terminal = foreign
        with self.assertRaisesRegex(census.Refused, "native-terminal"):
            self.scan()

    def test_unknown_native_terminal_readback_refuses_all_subjects(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = census._digest([])
        self.port.pages = {"0": {"schema": census.PAGE_SCHEMA,
                                 "sealId": SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        self.port.terminal_receipts = {
            item["mintId"]: terminal_receipt(item) for item in self.port.mints}
        def unavailable(_seal_id, _mint_id, _challenge):
            raise OSError("native provider readback unavailable")
        self.port.read_native_terminal = unavailable
        with self.assertRaisesRegex(census.Refused, "census-unknown"):
            self.scan()

    def test_pending_mint_token_digest_drift_refuses_joint_seal(self):
        self.port.mints[1] = {**self.port.mints[1], "tokenSha256": "e" * 64}
        self.port.seal["mintSha256"] = census._digest(self.port.mints)
        self.port.mint_subjects = {
            item["mintId"]: item for item in self.port.mints}
        with self.assertRaisesRegex(census.Refused, "mint-pending-binding"):
            self.scan()

    def test_boolean_sequence_in_independent_mint_readback_refuses(self):
        first = self.port.mints[0]
        self.port.mint_subjects[first["mintId"]] = {
            **first, "sequence": True}
        with self.assertRaisesRegex(census.Refused, "mint-readback"):
            self.scan()

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
