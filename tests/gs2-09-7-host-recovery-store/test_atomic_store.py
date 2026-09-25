import copy
from concurrent.futures import ThreadPoolExecutor
import importlib.util
from pathlib import Path
from threading import Barrier, Lock
import unittest


ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


contract = load("host_recovery_store_contract",
                ROOT / "scripts/gs2-09-7-host-recovery-store.py")
fixture = load("host_recovery_worker_fixture",
               ROOT / "tests/gs2-09-7-host-recovery-worker/test_host_recovery_worker.py")


class FakeAtomicStore(contract.AtomicRecoveryStorePort):
    """In-memory model of one protected store, never a production adapter."""

    def __init__(self, source):
        self.lock = Lock()
        self.seal = copy.deepcopy(source.census_seal)
        self.generation = source.joint_generation
        self.floor_resource_id = "protected-floor-test"
        self.batch = None
        self.claims = {}
        self.operations = []

    def append_schedule_batch_once(self, batch, seal_id, high_water,
                                   expected_generation, floor_resource_id):
        with self.lock:
            if self.batch is not None:
                return "duplicate"
            if seal_id != self.seal["sealId"] or \
                    type(high_water) is not int or high_water != self.seal["highWater"] or \
                    batch["sealId"] != seal_id or \
                    batch["highWater"] != high_water or \
                    batch["pendingSha256"] != self.seal["pendingSha256"] or \
                    batch["mintSha256"] != self.seal["mintSha256"] or \
                    batch["jointGeneration"] != self.generation or \
                    expected_generation != self.generation or \
                    floor_resource_id != self.floor_resource_id or \
                    batch["pendingCount"] != self.seal["pendingCount"] or \
                    len(batch["jobs"]) != batch["pendingCount"] or \
                    batch["state"] != "committed":
                return "refused"
            self.batch = copy.deepcopy(batch)
            self.operations.append("append")
            return "committed"

    def read_schedule_batch(self, seal_id):
        with self.lock:
            return copy.deepcopy(self.batch) if self.batch and \
                self.batch["sealId"] == seal_id else None

    def read_schedule(self, mint_id):
        with self.lock:
            if self.batch is None:
                return None
            for job in self.batch["jobs"]:
                if job["mintId"] == mint_id:
                    return copy.deepcopy(job)
            return None

    def withdraw_schedule_batch_once(self, batch_id, seal_id, reason):
        with self.lock:
            if reason not in ("seal-revoked", "operator-hold", "candidate-invalid") \
                    or self.batch is None or self.batch["batchId"] != batch_id \
                    or self.batch["sealId"] != seal_id:
                return "refused"
            if self.batch["state"] == "withdrawn":
                return "duplicate"
            self.batch["state"] = "withdrawn"
            self.operations.append("withdraw")
            return "committed"

    def claim_recovery_once(self, mint_id, binding_id, schedule_id, batch_id,
                            expected_generation, floor_resource_id):
        with self.lock:
            if self.batch is None or self.batch["batchId"] != batch_id \
                    or self.batch["state"] != "committed" \
                    or self.batch["jointGeneration"] != self.generation \
                    or expected_generation != self.generation \
                    or floor_resource_id != self.floor_resource_id:
                return "refused"
            matches = [job for job in self.batch["jobs"]
                       if job["mintId"] == mint_id
                       and job["bindingId"] == binding_id
                       and job["scheduleId"] == schedule_id]
            if len(matches) != 1:
                return "refused"
            if mint_id in self.claims:
                return "duplicate"
            job = matches[0]
            self.claims[mint_id] = {
                "schema": fixture.worker.CLAIM_SCHEMA,
                "mintId": mint_id, "bindingId": binding_id,
                "tokenSha256": job["tokenSha256"],
                "workerId": self.batch["workerId"],
                "scheduleId": schedule_id, "batchId": batch_id,
                "sealId": self.batch["sealId"],
                "jointGeneration": self.batch["jointGeneration"],
                "schedulerResourceId": self.batch["schedulerResourceId"],
                "recoveryResourceId": self.batch["recoveryResourceId"],
                "state": "committed",
            }
            self.operations.append("claim")
            return "committed"

    def read_recovery_claim(self, mint_id):
        with self.lock:
            return copy.deepcopy(self.claims.get(mint_id))


class AtomicStoreTests(unittest.TestCase):
    def setUp(self):
        self.source = fixture.FakePort()
        self.batch = copy.deepcopy(self.source.schedule_batch)
        self.job = self.batch["jobs"][0]
        self.store = FakeAtomicStore(self.source)

    def append(self):
        return self.store.append_schedule_batch_once(
            self.batch, self.batch["sealId"], self.batch["highWater"],
            self.batch["jointGeneration"], self.store.floor_resource_id)

    def claim(self):
        return self.store.claim_recovery_once(
            self.job["mintId"], self.job["bindingId"],
            self.job["scheduleId"], self.batch["batchId"],
            self.batch["jointGeneration"], self.store.floor_resource_id)

    def withdraw(self):
        return self.store.withdraw_schedule_batch_once(
            self.batch["batchId"], self.batch["sealId"], "operator-hold")

    def test_full_batch_append_is_all_or_none_and_idempotent(self):
        incomplete = copy.deepcopy(self.batch)
        incomplete["jobs"] = []
        self.assertEqual("refused", self.store.append_schedule_batch_once(
            incomplete, incomplete["sealId"], incomplete["highWater"],
            incomplete["jointGeneration"], self.store.floor_resource_id))
        self.assertIsNone(self.store.read_schedule_batch(self.batch["sealId"]))
        self.assertEqual("committed", self.append())
        self.assertEqual("duplicate", self.append())
        self.assertEqual(self.batch, self.store.read_schedule_batch(self.batch["sealId"]))

    def test_append_rejects_stale_generation_or_foreign_floor_at_commit(self):
        self.assertEqual("refused", self.store.append_schedule_batch_once(
            self.batch, self.batch["sealId"], self.batch["highWater"],
            self.batch["jointGeneration"] + 1, self.store.floor_resource_id))
        self.assertEqual("refused", self.store.append_schedule_batch_once(
            self.batch, self.batch["sealId"], self.batch["highWater"],
            self.batch["jointGeneration"], "foreign-floor"))
        self.assertIsNone(self.store.read_schedule_batch(self.batch["sealId"]))

    def test_concurrent_full_batch_appends_commit_once(self):
        start = Barrier(3)
        def append_after_start():
            start.wait()
            return self.append()
        with ThreadPoolExecutor(max_workers=2) as pool:
            left = pool.submit(append_after_start)
            right = pool.submit(append_after_start)
            start.wait()
            results = (left.result(), right.result())
        self.assertEqual(["committed", "duplicate"], sorted(results))
        self.assertEqual(["append"], self.store.operations)
        self.assertEqual(self.batch, self.store.read_schedule_batch(self.batch["sealId"]))

    def test_withdrawal_wins_before_claim_and_cannot_be_reversed(self):
        self.assertEqual("committed", self.append())
        self.assertEqual("committed", self.withdraw())
        self.assertEqual("refused", self.claim())
        self.assertIsNone(self.store.read_recovery_claim(self.job["mintId"]))
        self.assertEqual(["append", "withdraw"], self.store.operations)

    def test_claim_wins_then_withdrawal_preserves_durable_claim(self):
        self.assertEqual("committed", self.append())
        self.assertEqual("committed", self.claim())
        claim = self.store.read_recovery_claim(self.job["mintId"])
        self.assertEqual(self.batch["batchId"], claim["batchId"])
        self.assertEqual(self.job["scheduleId"], claim["scheduleId"])
        self.assertEqual("committed", self.withdraw())
        self.assertEqual(claim, self.store.read_recovery_claim(self.job["mintId"]))
        self.assertEqual("refused", self.claim())
        self.assertEqual(["append", "claim", "withdraw"], self.store.operations)

    def test_claim_rejects_head_advance_or_foreign_floor_at_commit(self):
        self.assertEqual("committed", self.append())
        self.store.generation += 1
        self.assertEqual("refused", self.claim())
        self.store.generation -= 1
        self.assertEqual("refused", self.store.claim_recovery_once(
            self.job["mintId"], self.job["bindingId"],
            self.job["scheduleId"], self.batch["batchId"],
            self.batch["jointGeneration"], "foreign-floor"))
        self.assertIsNone(self.store.read_recovery_claim(self.job["mintId"]))

    def test_concurrent_claim_and_withdrawal_have_one_order(self):
        for _ in range(12):
            self.store = FakeAtomicStore(self.source)
            self.assertEqual("committed", self.append())
            start = Barrier(3)
            def run(action):
                start.wait()
                return action()
            with ThreadPoolExecutor(max_workers=2) as pool:
                claim = pool.submit(run, self.claim)
                withdraw = pool.submit(run, self.withdraw)
                start.wait()
                claim_result = claim.result()
                withdraw_result = withdraw.result()
            self.assertEqual("committed", withdraw_result)
            self.assertIn(claim_result, ("committed", "refused"))
            self.assertEqual(claim_result == "committed",
                             self.store.read_recovery_claim(self.job["mintId"]) is not None)
            self.assertEqual(
                ["append", "claim", "withdraw"] if claim_result == "committed"
                else ["append", "withdraw"], self.store.operations)

    def test_lost_claim_response_has_exact_readback_but_no_second_fresh_claim(self):
        self.assertEqual("committed", self.append())
        def response_lost():
            self.assertEqual("committed", self.claim())
            raise OSError("response lost after durable claim")
        with self.assertRaises(OSError):
            response_lost()
        self.assertEqual(self.batch["batchId"],
                         self.store.read_recovery_claim(self.job["mintId"])["batchId"])
        self.assertEqual("duplicate", self.claim())
        self.assertEqual(1, self.store.operations.count("claim"))

    def test_old_batch_after_protected_head_restart_cannot_claim(self):
        self.assertEqual("committed", self.append())
        self.store.generation += 1
        self.assertEqual("refused", self.claim())
        self.assertIsNone(self.store.read_recovery_claim(self.job["mintId"]))


if __name__ == "__main__":
    unittest.main()
