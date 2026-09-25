import hashlib
import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "host_recovery_worker", ROOT / "scripts/gs2-09-7-host-recovery-worker.py")
worker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(worker)
JOINT_SPEC = importlib.util.spec_from_file_location(
    "worker_joint_signing_fixture",
    ROOT / "tests/gs2-09-7-host-joint-seal/signing_fixture.py")
signing = importlib.util.module_from_spec(JOINT_SPEC)
JOINT_SPEC.loader.exec_module(signing)

ORIGIN = "https://protected.example.invalid"
FINALIZER_RESOURCE = "finalizer-ledger-test"
FINALIZER_ENDPOINT = ORIGIN + "/pending"
VAULT = "protected-token-vault-test"
REVOKER = "protected-native-revoker-test"
RECOVERY_RESOURCE = "recovery-journal-test"
RECOVERY_ENDPOINT = ORIGIN + "/recovery"
WORKER_ID = "protected-recovery-worker-test"
SCHEDULER_ID = "protected-recovery-scheduler-test"
SCHEDULER_ENDPOINT = ORIGIN + "/schedule"
QUEUE_ID = "pending-queue-test"
JOURNAL_ID = "pending-journal-test"
MINT_ID = "a" * 64
BINDING_ID = "b" * 64
TOKEN = "fake-recovery-token-never-valid-outside-test"


class FakePort:
    def __init__(self):
        self.calls = []
        self.token = TOKEN
        self.token_sha256 = hashlib.sha256(TOKEN.encode()).hexdigest()
        self.claim = None
        self.native_attempt = None
        self.schedule_state = "committed"
        self.batch_state = "committed"
        self.revoke_schedule_after_read = False
        self.receipt = None
        self.claim_response_lost = False
        self.false_claim_commit = False
        self.receipt_response_lost = False
        self.revoke_response_lost = False
        self.native_attempt_response_lost = False
        self.stale_native_attempt_commit = False
        self.observations = ["active", "revoked"]
        self.finalizer_descriptor = {
            "schema": worker.finalizer.FINALIZER_SCHEMA,
            "origin": ORIGIN, "resourceId": FINALIZER_RESOURCE,
            "endpoint": FINALIZER_ENDPOINT, "vaultId": VAULT,
            "durable": True, "atomicCas": True, "nativeReadback": True,
            "escrowEncrypted": True, "credentialScope": "protected-host-only",
            "candidateCanWrite": False, "apiOrigin": "https://api.github.com",
            "nativeRevocation": True,
        }
        self.vault_descriptor = {
            "schema": worker.finalizer.VAULT_SCHEMA, "vaultId": VAULT,
            "credentialScope": "protected-host-only",
            "candidateCanRead": False, "candidateCanWrite": False,
            "encrypted": True, "durable": True,
        }
        self.revoker_descriptor = {
            "schema": worker.finalizer.REVOKER_SCHEMA, "resourceId": REVOKER,
            "apiOrigin": "https://api.github.com",
            "credentialScope": "protected-host-only", "candidateCanWrite": False,
            "nativeObservation": True,
        }
        self.recovery_descriptor = {
            "schema": worker.WORKER_SCHEMA, "origin": ORIGIN,
            "resourceId": RECOVERY_RESOURCE, "endpoint": RECOVERY_ENDPOINT,
            "workerId": WORKER_ID, "durable": True, "atomicCas": True,
            "nativeReadback": True, "credentialScope": "protected-host-only",
            "candidateCanWrite": False,
        }
        self.mint = {
            "schema": worker.finalizer.MINT_SCHEMA, "mintId": MINT_ID,
            "tokenSha256": self.token_sha256, "contextSha256": "c" * 64,
            "sandboxRepositoryId": worker.finalizer.release.host.SANDBOX_ID,
            "appId": worker.finalizer.release.host.APP_ID,
            "actor": worker.finalizer.release.host.ACTOR,
            "installationId": 123, "vaultId": VAULT, "escrowed": True,
        }
        self.binding = {
            "schema": worker.BINDING_SCHEMA, "mintId": MINT_ID,
            "bindingId": BINDING_ID, "tokenSha256": self.token_sha256,
            "contextSha256": self.mint["contextSha256"],
            "sandboxRepositoryId": worker.finalizer.release.host.SANDBOX_ID,
            "appId": worker.finalizer.release.host.APP_ID,
            "actor": worker.finalizer.release.host.ACTOR,
            "installationId": self.mint["installationId"],
            "vaultId": VAULT, "finalizerResourceId": FINALIZER_RESOURCE,
            "verifiedAtCommit": True,
        }
        self.pending = {
            "schema": worker.finalizer.PENDING_SCHEMA, "mintId": MINT_ID,
            "tokenSha256": self.token_sha256, "revokeRequired": True,
        }
        self.scheduler_descriptor = {
            "schema": worker.SCHEDULER_SCHEMA, "origin": ORIGIN,
            "resourceId": SCHEDULER_ID, "endpoint": SCHEDULER_ENDPOINT,
            "queueResourceId": QUEUE_ID, "journalResourceId": JOURNAL_ID,
            "recoveryResourceId": RECOVERY_RESOURCE,
            "durable": True, "atomicCas": True,
            "atomicWithdrawClaim": True, "nativeReadback": True,
            "credentialScope": "protected-host-only", "candidateCanWrite": False,
        }
        self.scheduled = {
            "schema": worker.SCHEDULE_SCHEMA, "scheduleId": "d" * 64,
            "sealId": "f" * 64, "jointGeneration": 1,
            "highWater": 3, "sequence": 1,
            "pendingSha256": "1" * 64, "mintSha256": "2" * 64,
            "mintId": MINT_ID, "bindingId": BINDING_ID,
            "tokenSha256": self.token_sha256,
            "contextSha256": self.mint["contextSha256"],
            "sandboxRepositoryId": worker.finalizer.release.host.SANDBOX_ID,
            "appId": worker.finalizer.release.host.APP_ID,
            "actor": worker.finalizer.release.host.ACTOR,
            "installationId": self.mint["installationId"],
            "vaultId": VAULT, "finalizerResourceId": FINALIZER_RESOURCE,
            "recoveryResourceId": RECOVERY_RESOURCE,
            "schedulerResourceId": SCHEDULER_ID,
            "queueResourceId": QUEUE_ID, "journalResourceId": JOURNAL_ID,
            "censusComplete": True, "state": "committed",
        }
        self.census_seal = {
            "schema": worker.CENSUS_SEAL_SCHEMA,
            "sealId": self.scheduled["sealId"], "highWater": 3,
            "pendingCount": 1,
            "pendingSha256": self.scheduled["pendingSha256"],
            "mintCount": 3, "mintSha256": self.scheduled["mintSha256"],
            "queueResourceId": QUEUE_ID, "journalResourceId": JOURNAL_ID,
            "finalizerResourceId": FINALIZER_RESOURCE,
            "vaultId": VAULT, "recoveryResourceId": RECOVERY_RESOURCE,
            "workerId": WORKER_ID, "complete": True,
            "snapshotIsolation": True,
        }
        self.census_subject = {
            "schema": worker.CENSUS_SUBJECT_SCHEMA,
            "sequence": 1, "mintId": MINT_ID, "bindingId": BINDING_ID,
            "tokenSha256": self.token_sha256,
            "contextSha256": self.mint["contextSha256"],
            "sandboxRepositoryId": worker.finalizer.release.host.SANDBOX_ID,
            "appId": worker.finalizer.release.host.APP_ID,
            "actor": worker.finalizer.release.host.ACTOR,
            "installationId": self.mint["installationId"],
            "journalResourceId": JOURNAL_ID,
            "finalizerResourceId": FINALIZER_RESOURCE,
            "vaultId": VAULT, "recoveryResourceId": RECOVERY_RESOURCE,
            "revokeRequired": True,
        }
        self.census_seal["pendingSha256"] = worker._digest([self.census_subject])
        self.scheduled["pendingSha256"] = self.census_seal["pendingSha256"]
        self.schedule_batch = {
            "schema": worker.BATCH_SCHEMA, "batchId": "9" * 64,
            "sealId": self.census_seal["sealId"],
            "jointGeneration": 1,
            "highWater": self.census_seal["highWater"], "pendingCount": 1,
            "pendingSha256": self.census_seal["pendingSha256"],
            "mintSha256": self.census_seal["mintSha256"],
            "schedulerResourceId": SCHEDULER_ID,
            "queueResourceId": QUEUE_ID, "journalResourceId": JOURNAL_ID,
            "recoveryResourceId": RECOVERY_RESOURCE, "workerId": WORKER_ID,
            "jobs": [self.scheduled], "state": "committed",
        }
        signing.attach(worker.joint, self, lambda: self.census_seal)

    def describe(self):
        return self.finalizer_descriptor

    def describe_vault(self):
        return self.vault_descriptor

    def describe_revoker(self):
        return self.revoker_descriptor

    def describe_recovery(self):
        return self.recovery_descriptor

    def describe_scheduler(self):
        self.calls.append("describe-scheduler")
        return self.scheduler_descriptor

    def read_schedule(self, mint_id):
        self.calls.append("read-schedule")
        if self.revoke_schedule_after_read:
            self.schedule_state = "revoked"
        return self.scheduled

    def read_census_seal(self, seal_id):
        self.calls.append("read-census-seal")
        return self.census_seal

    def read_census_subject(self, seal_id, mint_id):
        self.calls.append("read-census-subject")
        return self.census_subject

    def read_schedule_batch(self, seal_id):
        self.calls.append("read-schedule-batch")
        return self.schedule_batch

    def withdraw_schedule_batch_once(self, batch_id, seal_id, reason):
        self.calls.append("withdraw-batch")
        if batch_id != self.schedule_batch["batchId"] or \
                seal_id != self.schedule_batch["sealId"]:
            return "refused"
        self.batch_state = "withdrawn"
        self.schedule_batch["state"] = "withdrawn"
        return "committed"

    def load_mint(self, mint_id):
        self.calls.append("load-mint")
        return self.mint

    def recover_token(self, mint_id):
        self.calls.append("recover-token")
        return self.token

    def append_pending(self, mint_id, token_sha256):
        raise AssertionError("recovery must not create a new release intent")

    def read_pending(self, mint_id):
        self.calls.append("read-pending")
        return self.pending

    def revoke(self, token):
        self.calls.append("native-revoke")
        if self.revoke_response_lost:
            raise OSError("response lost after native revoke")
        return "accepted"

    def observe(self, token):
        self.calls.append("native-observe")
        return self.observations.pop(0) if self.observations else "unknown"

    def append_revoked(self, mint_id, token_sha256):
        raise AssertionError("recovery must use its bound receipt")

    def read_revoked(self, mint_id):
        return None

    def read_binding(self, mint_id):
        self.calls.append("read-binding")
        return self.binding

    def claim_recovery_once(self, mint_id, binding_id, schedule_id, batch_id,
                            expected_generation, floor_resource_id):
        self.calls.append("claim-recovery")
        if self.schedule_state != "committed" or self.batch_state != "committed" \
                or self.scheduled is None or self.schedule_batch is None \
                or schedule_id != self.scheduled["scheduleId"] \
                or batch_id != self.schedule_batch["batchId"] \
                or self.schedule_batch["jointGeneration"] != self.joint_generation \
                or expected_generation != self.joint_generation \
                or floor_resource_id != worker.joint.floor.PINNED_FLOOR_RESOURCE_ID:
            return "refused"
        if self.claim is not None:
            return "duplicate"
        if self.false_claim_commit:
            return "committed"
        self.claim = {
            "schema": worker.CLAIM_SCHEMA, "mintId": mint_id,
            "bindingId": binding_id, "tokenSha256": self.token_sha256,
            "workerId": WORKER_ID,
            "scheduleId": schedule_id, "batchId": batch_id,
            "sealId": self.scheduled["sealId"],
            "jointGeneration": self.schedule_batch["jointGeneration"],
            "schedulerResourceId": SCHEDULER_ID,
            "recoveryResourceId": RECOVERY_RESOURCE,
            "state": "committed",
        }
        if self.claim_response_lost:
            raise OSError("response lost after recovery claim")
        return "committed"

    def read_recovery_claim(self, mint_id):
        self.calls.append("read-claim")
        return self.claim

    def claim_native_attempt_once(self, mint_id, token_sha256, attempt_id):
        self.calls.append("claim-native-attempt")
        if self.native_attempt is not None:
            return "duplicate"
        self.native_attempt = worker.finalizer.native_attempt_record(
            mint_id, token_sha256, self.mint["contextSha256"],
            "e" * 64 if self.stale_native_attempt_commit else attempt_id)
        if not self.stale_native_attempt_commit and self.native_attempt["attemptId"] != attempt_id:
            raise AssertionError("foreign native attempt")
        if self.native_attempt_response_lost:
            raise OSError("lost after native-attempt claim")
        return "committed"

    def claim_recovery_native_attempt_once(self, mint_id, token_sha256,
                                           attempt_id, expected_generation,
                                           floor_resource_id):
        self.calls.append("claim-recovery-native-attempt")
        if type(expected_generation) is not int \
                or expected_generation != self.joint_generation \
                or floor_resource_id != worker.joint.floor.PINNED_FLOOR_RESOURCE_ID \
                or type(self.claim) is not dict \
                or self.claim.get("mintId") != mint_id \
                or self.claim.get("tokenSha256") != token_sha256 \
                or self.claim.get("jointGeneration") != expected_generation \
                or self.claim.get("state") != "committed" \
                or type(self.schedule_batch) is not dict \
                or self.claim.get("batchId") != self.schedule_batch["batchId"]:
            return "refused"
        return self.claim_native_attempt_once(mint_id, token_sha256, attempt_id)

    def read_native_attempt(self, mint_id):
        self.calls.append("read-native-attempt")
        return self.native_attempt

    def append_recovery_receipt(self, mint_id, binding_id, token_sha256):
        self.calls.append("append-receipt")
        self.receipt = {
            "schema": worker.RECEIPT_SCHEMA, "mintId": mint_id,
            "bindingId": binding_id, "tokenSha256": token_sha256,
            "workerId": WORKER_ID, "providerState": "revoked",
        }
        if self.receipt_response_lost:
            raise OSError("response lost after receipt commit")
        return "committed"

    def read_recovery_receipt(self, mint_id):
        self.calls.append("read-receipt")
        return self.receipt


class RecoveryWorkerTests(unittest.TestCase):
    def setUp(self):
        self.port = FakePort()
        signing.pin(self, worker.joint)
        pins = {
            (worker, "PINNED_RECOVERY_ORIGIN"): ORIGIN,
            (worker, "PINNED_RECOVERY_RESOURCE_ID"): RECOVERY_RESOURCE,
            (worker, "PINNED_RECOVERY_ENDPOINT"): RECOVERY_ENDPOINT,
            (worker, "PINNED_RECOVERY_WORKER_ID"): WORKER_ID,
            (worker, "PINNED_SCHEDULER_ORIGIN"): ORIGIN,
            (worker, "PINNED_SCHEDULER_RESOURCE_ID"): SCHEDULER_ID,
            (worker, "PINNED_SCHEDULER_ENDPOINT"): SCHEDULER_ENDPOINT,
            (worker, "PINNED_CENSUS_QUEUE_RESOURCE_ID"): QUEUE_ID,
            (worker, "PINNED_CENSUS_JOURNAL_RESOURCE_ID"): JOURNAL_ID,
            (worker.finalizer, "PINNED_FINALIZER_ORIGIN"): ORIGIN,
            (worker.finalizer, "PINNED_FINALIZER_RESOURCE_ID"): FINALIZER_RESOURCE,
            (worker.finalizer, "PINNED_FINALIZER_ENDPOINT"): FINALIZER_ENDPOINT,
            (worker.finalizer, "PINNED_TOKEN_VAULT_ID"): VAULT,
            (worker.finalizer, "PINNED_REVOKER_ID"): REVOKER,
        }
        for (module, name), value in pins.items():
            original = getattr(module, name)
            setattr(module, name, value)
            self.addCleanup(setattr, module, name, original)

    def run_worker(self):
        return worker.recover_one(self.port, MINT_ID, BINDING_ID)

    def exact_claim(self):
        return {"schema": worker.CLAIM_SCHEMA, "mintId": MINT_ID,
                "bindingId": BINDING_ID,
                "tokenSha256": self.port.token_sha256,
                "workerId": WORKER_ID,
                "scheduleId": self.port.scheduled["scheduleId"],
                "batchId": self.port.schedule_batch["batchId"],
                "sealId": self.port.scheduled["sealId"],
                "jointGeneration": self.port.schedule_batch["jointGeneration"],
                "schedulerResourceId": SCHEDULER_ID,
                "recoveryResourceId": RECOVERY_RESOURCE,
                "state": "committed"}

    def test_fresh_exact_claim_revokes_once_and_requires_native_and_receipt_readback(self):
        result = self.run_worker()
        self.assertEqual("fresh", result["claim"])
        self.assertEqual("revoked", result["revocation"])
        self.assertEqual("pending", result["disposition"])
        self.assertEqual(1, self.port.calls.count("native-revoke"))
        self.assertEqual(2, self.port.calls.count("native-observe"))
        self.assertIn("read-receipt", self.port.calls)
        self.assertNotIn(TOKEN, json.dumps(result))

    def test_unsigned_seal_cannot_authorize_native_recovery(self):
        self.port.read_joint_seal_envelope = None
        with self.assertRaisesRegex(worker.Refused, "joint-seal"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_replayed_signed_seal_after_head_advance_cannot_claim(self):
        old = self.port.read_joint_seal_envelope(self.port.census_seal["sealId"])
        self.port.joint_generation = 2
        self.port.joint_envelope_override = old
        with self.assertRaisesRegex(worker.Refused, "joint-seal-binding"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_old_batch_with_new_valid_signed_generation_cannot_claim(self):
        self.port.joint_generation = 2
        with self.assertRaisesRegex(worker.Refused, "joint-generation"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_head_advance_after_claim_blocks_native_revoke(self):
        original = self.port.claim_recovery_once
        def advance(mint_id, binding_id, schedule_id, batch_id, generation, floor_id):
            result = original(mint_id, binding_id, schedule_id, batch_id,
                              generation, floor_id)
            self.port.joint_generation = 2
            return result
        self.port.claim_recovery_once = advance
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_head_advance_after_final_read_before_attempt_blocks_revoke(self):
        original = self.port.read_native_attempt
        def advance(mint_id):
            result = original(mint_id)
            self.port.joint_generation = 2
            return result
        self.port.read_native_attempt = advance
        result = self.run_worker()
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_recovery_native_attempt_claim_refuses_foreign_floor_or_generation(self):
        attempt_id = "a" * 64
        for generation, floor_id in (
            (2, worker.joint.floor.PINNED_FLOOR_RESOURCE_ID),
            (1, "foreign-floor"),
            (True, worker.joint.floor.PINNED_FLOOR_RESOURCE_ID),
        ):
            with self.subTest(generation=generation, floor_id=floor_id):
                self.assertEqual("refused", self.port.claim_recovery_native_attempt_once(
                    MINT_ID, self.port.token_sha256, attempt_id,
                    generation, floor_id))
                self.assertIsNone(self.port.native_attempt)

    def test_native_attempt_claim_without_durable_recovery_claim_refuses(self):
        self.assertEqual("refused", self.port.claim_recovery_native_attempt_once(
            MINT_ID, self.port.token_sha256, "a" * 64, 1,
            worker.joint.floor.PINNED_FLOOR_RESOURCE_ID))
        self.assertIsNone(self.port.native_attempt)

    def test_missing_recovery_native_attempt_port_refuses_before_token_load(self):
        self.port.claim_recovery_native_attempt_once = None
        with self.assertRaisesRegex(worker.Refused, "recovery-unconfigured"):
            self.run_worker()
        self.assertNotIn("recover-token", self.port.calls)

    def test_committed_claim_without_batch_and_schedule_identity_cannot_revoke(self):
        original = self.port.claim_recovery_once
        def wrong_store_claim(mint_id, binding_id, schedule_id, batch_id,
                              generation, floor_id):
            result = original(mint_id, binding_id, schedule_id, batch_id,
                              generation, floor_id)
            self.port.claim["scheduleId"] = "e" * 64
            self.port.claim["batchId"] = "8" * 64
            return result
        self.port.claim_recovery_once = wrong_store_claim
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_foreign_claim_batch_schedule_or_seal_readback_blocks_native_effect(self):
        for field in ("batchId", "scheduleId", "sealId", "schedulerResourceId"):
            with self.subTest(field=field):
                self.port = FakePort()
                original = self.port.claim_recovery_once
                def foreign_claim(mint_id, binding_id, schedule_id, batch_id,
                                  generation, floor_id):
                    result = original(mint_id, binding_id, schedule_id,
                                      batch_id, generation, floor_id)
                    self.port.claim[field] = "foreign" if field.endswith("ResourceId") \
                        else "e" * 64
                    return result
                self.port.claim_recovery_once = foreign_claim
                result = self.run_worker()
                self.assertEqual("unknown", result["claim"])
                self.assertEqual("pending", result["revocation"])
                self.assertNotIn("native-revoke", self.port.calls)

    def test_unscheduled_exact_mint_cannot_invoke_recovery(self):
        self.port.scheduled = None
        with self.assertRaisesRegex(worker.Refused, "recovery-schedule"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_single_committed_job_without_full_durable_batch_refuses(self):
        self.port.schedule_batch = None
        with self.assertRaisesRegex(worker.Refused, "recovery-batch"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_omitted_duplicate_or_withdrawn_batch_blocks_claim(self):
        for mutate in (
            lambda batch: batch["jobs"].clear(),
            lambda batch: batch["jobs"].append(batch["jobs"][0].copy()),
            lambda batch: batch.update(state="withdrawn"),
        ):
            with self.subTest(mutate=mutate):
                self.port = FakePort()
                mutate(self.port.schedule_batch)
                with self.assertRaisesRegex(worker.Refused, "recovery-batch"):
                    self.run_worker()
                self.assertNotIn("claim-recovery", self.port.calls)
                self.assertNotIn("native-revoke", self.port.calls)

    def test_batch_withdrawn_after_readback_blocks_atomic_claim(self):
        self.port.schedule_state = "committed"
        original = self.port.read_schedule_batch
        def withdraw(seal_id):
            result = original(seal_id)
            self.port.batch_state = "withdrawn"
            return result
        self.port.read_schedule_batch = withdraw
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_revoked_schedule_between_readback_and_claim_blocks_native_effect(self):
        self.port.revoke_schedule_after_read = True
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_foreign_or_uncommitted_schedule_refuses_before_claim(self):
        for change in ({"bindingId": "e" * 64}, {"sealId": "not-a-seal"},
                       {"censusComplete": 1}, {"state": "revoked"},
                       {"queueResourceId": "candidate-queue"}):
            with self.subTest(change=change):
                self.port = FakePort()
                self.port.scheduled = {**self.port.scheduled, **change}
                with self.assertRaisesRegex(worker.Refused, "recovery-schedule"):
                    self.run_worker()
                self.assertNotIn("claim-recovery", self.port.calls)
                self.assertNotIn("native-revoke", self.port.calls)

    def test_unavailable_or_candidate_writable_scheduler_refuses(self):
        def unavailable(_mint_id):
            raise OSError("protected scheduler unavailable")
        self.port.read_schedule = unavailable
        with self.assertRaisesRegex(worker.Refused, "recovery-schedule-unknown"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.port = FakePort()
        self.port.scheduler_descriptor["candidateCanWrite"] = True
        with self.assertRaisesRegex(worker.Refused, "scheduler-authority"):
            self.run_worker()
        self.assertNotIn("recover-token", self.port.calls)

    def test_scheduler_without_atomic_withdraw_claim_port_refuses(self):
        self.port.scheduler_descriptor["atomicWithdrawClaim"] = False
        with self.assertRaisesRegex(worker.Refused, "scheduler-authority"):
            self.run_worker()
        self.assertNotIn("recover-token", self.port.calls)
        self.port = FakePort()
        self.port.withdraw_schedule_batch_once = None
        with self.assertRaisesRegex(worker.Refused, "scheduler-unconfigured"):
            self.run_worker()
        self.assertNotIn("recover-token", self.port.calls)

    def test_self_asserted_complete_schedule_without_census_seal_refuses(self):
        self.port.census_seal = None
        with self.assertRaisesRegex(worker.Refused, "recovery-census-seal"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_foreign_seal_or_sealed_subject_refuses_before_claim(self):
        self.port.census_seal["pendingSha256"] = "e" * 64
        with self.assertRaisesRegex(worker.Refused, "recovery-census-seal"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.port = FakePort()
        self.port.census_subject["bindingId"] = "e" * 64
        with self.assertRaisesRegex(worker.Refused, "recovery-census-subject"):
            self.run_worker()
        self.assertNotIn("native-revoke", self.port.calls)

    def test_seal_drift_during_schedule_readback_refuses(self):
        reads = 0
        def moving(_seal_id):
            nonlocal reads
            reads += 1
            return {**self.port.census_seal,
                    "pendingCount": 2 if reads == 2 else 1}
        self.port.read_census_seal = moving
        with self.assertRaisesRegex(worker.Refused, "recovery-census-seal"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)

    def test_boolean_high_water_alias_cannot_authorize_schedule(self):
        self.port.scheduled["highWater"] = 1
        self.port.census_seal["highWater"] = True
        self.port.census_seal["mintCount"] = True
        with self.assertRaisesRegex(worker.Refused, "recovery-census-seal"):
            self.run_worker()
        self.assertNotIn("native-revoke", self.port.calls)

    def test_duplicate_active_claim_never_repeats_native_effect(self):
        self.port.claim = self.exact_claim()
        self.port.observations = ["active"]
        result = self.run_worker()
        self.assertEqual("duplicate", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_prior_finalizer_native_attempt_blocks_recovery_revoke(self):
        self.port.native_attempt = worker.finalizer.native_attempt_record(
            MINT_ID, self.port.token_sha256, self.port.mint["contextSha256"],
            "d" * 64)
        self.port.observations = ["active"]
        result = self.run_worker()
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_foreign_native_attempt_blocks_receipt_even_if_provider_revoked(self):
        self.port.native_attempt = {
            **worker.finalizer.native_attempt_record(
                MINT_ID, self.port.token_sha256, self.port.mint["contextSha256"],
                "d" * 64),
            "revokerId": "foreign-revoker",
        }
        self.port.observations = ["revoked"]
        result = self.run_worker()
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertNotIn("append-receipt", self.port.calls)

    def test_lost_shared_attempt_claim_never_retries_native_effect(self):
        self.port.native_attempt_response_lost = True
        self.port.observations = ["active"]
        first = self.run_worker()
        self.assertEqual("pending", first["revocation"])
        self.assertIsNotNone(self.port.native_attempt)
        self.assertNotIn("native-revoke", self.port.calls)
        self.port.native_attempt_response_lost = False
        self.port.observations = ["active"]
        self.port.calls.clear()
        second = self.run_worker()
        self.assertEqual("duplicate", second["claim"])
        self.assertEqual("pending", second["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_stale_committed_native_attempt_response_cannot_revoke(self):
        self.port.stale_native_attempt_commit = True
        self.port.observations = ["active"]
        result = self.run_worker()
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_unavailable_shared_attempt_readback_blocks_revoke_and_receipt(self):
        def unavailable(_mint_id):
            raise OSError("protected native-attempt journal unavailable")
        self.port.read_native_attempt = unavailable
        self.port.observations = ["revoked"]
        result = self.run_worker()
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertNotIn("append-receipt", self.port.calls)

    def test_lost_claim_response_never_authorizes_native_effect(self):
        self.port.claim_response_lost = True
        self.port.observations = ["active"]
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertIsNotNone(self.port.claim)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_false_committed_claim_without_readback_refuses_native_effect(self):
        self.port.false_claim_commit = True
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertNotIn("native-observe", self.port.calls)

    def test_duplicate_revoked_claim_can_finish_receipt_without_new_effect(self):
        self.port.claim = self.exact_claim()
        self.port.observations = ["revoked"]
        result = self.run_worker()
        self.assertEqual("revoked", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertIn("append-receipt", self.port.calls)

    def test_lost_claim_response_can_finish_only_preobserved_revocation(self):
        self.port.claim_response_lost = True
        self.port.observations = ["revoked"]
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("revoked", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_unknown_native_observation_refuses_mutation(self):
        self.port.observations = ["unknown"]
        result = self.run_worker()
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_lost_native_response_needs_provider_readback(self):
        self.port.revoke_response_lost = True
        self.port.observations = ["active", "revoked"]
        result = self.run_worker()
        self.assertEqual("revoked", result["revocation"])
        self.port = FakePort()
        self.port.revoke_response_lost = True
        self.port.observations = ["active", "active"]
        second = self.run_worker()
        self.assertEqual("pending", second["revocation"])

    def test_lost_receipt_response_requires_exact_durable_readback(self):
        self.port.receipt_response_lost = True
        result = self.run_worker()
        self.assertEqual("revoked", result["revocation"])
        self.port = FakePort()
        self.port.read_recovery_receipt = lambda _mint_id: None
        second = self.run_worker()
        self.assertEqual("pending", second["revocation"])

    def test_wrong_binding_or_mint_identity_refuses_before_claim(self):
        self.port.binding["bindingId"] = "d" * 64
        with self.assertRaisesRegex(worker.Refused, "recovery-binding"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.port.binding["bindingId"] = BINDING_ID
        self.port.mint["vaultId"] = "foreign-vault"
        with self.assertRaises(worker.finalizer.Refused):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)

    def test_missing_pending_intent_or_foreign_store_refuses_before_claim(self):
        self.port.pending = None
        with self.assertRaisesRegex(worker.Refused, "pending-intent"):
            self.run_worker()
        self.port = FakePort()
        self.port.finalizer_descriptor["candidateCanWrite"] = True
        with self.assertRaises(worker.finalizer.Refused):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)

    def test_absent_pin_or_candidate_writable_worker_refuses_before_vault(self):
        worker.PINNED_RECOVERY_RESOURCE_ID = ""
        with self.assertRaisesRegex(worker.Refused, "recovery-unconfigured"):
            self.run_worker()
        self.assertEqual([], self.port.calls)
        worker.PINNED_RECOVERY_RESOURCE_ID = RECOVERY_RESOURCE
        self.port.recovery_descriptor["candidateCanWrite"] = True
        with self.assertRaisesRegex(worker.Refused, "recovery-authority"):
            self.run_worker()
        self.assertEqual([], self.port.calls)


if __name__ == "__main__":
    unittest.main()
