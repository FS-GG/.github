import copy
import importlib.util
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


scheduler = load("host_recovery_scheduler", ROOT / "scripts/gs2-09-7-host-recovery-scheduler.py")
fixture = load("host_pending_census_fixture", ROOT / "tests/gs2-09-7-host-pending-census/test_host_pending_census.py")


class FakeDurableStore(fixture.FakeCensusPort):
    def __init__(self):
        super().__init__()
        self.batch = None
        self.jobs = {}
        self.store_fault = None
        self.scheduler_descriptor = {
            "schema": scheduler.worker.SCHEDULER_SCHEMA,
            "origin": fixture.ORIGIN,
            "resourceId": "scheduler-test",
            "endpoint": fixture.ORIGIN + "/schedule",
            "queueResourceId": fixture.QUEUE_ID,
            "journalResourceId": fixture.JOURNAL_ID,
            "recoveryResourceId": fixture.RECOVERY_ID,
            "durable": True, "atomicCas": True, "nativeReadback": True,
            "credentialScope": "protected-host-only", "candidateCanWrite": False,
        }

    def describe_scheduler(self):
        self.calls.append("describe-scheduler")
        return copy.deepcopy(self.scheduler_descriptor)

    def read_census_seal(self, seal_id):
        return self.read_seal(seal_id)

    def read_census_subject(self, seal_id, mint_id):
        return self.read_subject(seal_id, mint_id)

    def append_schedule_batch_once(self, batch, seal_id, high_water):
        self.calls.append("append-batch")
        if self.batch is not None or seal_id != self.seal["sealId"] \
                or high_water != self.high_water:
            return "duplicate"
        if self.store_fault == "mutate-argument-state":
            batch["state"] = "withdrawn"
        self.batch = copy.deepcopy(batch)
        if self.store_fault == "omit":
            self.batch["jobs"].pop()
        if self.store_fault == "duplicate":
            self.batch["jobs"].append(copy.deepcopy(self.batch["jobs"][0]))
        if self.store_fault == "withdraw":
            self.batch["state"] = "withdrawn"
        self.jobs = {job["mintId"]: job for job in self.batch["jobs"]}
        return "committed"

    def read_schedule_batch(self, seal_id):
        self.calls.append("read-batch")
        return copy.deepcopy(self.batch)

    def read_schedule(self, mint_id):
        self.calls.append("read-schedule")
        return copy.deepcopy(self.jobs.get(mint_id))


class SchedulerTests(unittest.TestCase):
    def setUp(self):
        self.port = FakeDurableStore()
        census = scheduler.census
        worker = scheduler.worker
        pins = {
            (census, "PINNED_QUEUE_ORIGIN"): fixture.ORIGIN,
            (census, "PINNED_QUEUE_RESOURCE_ID"): fixture.QUEUE_ID,
            (census, "PINNED_QUEUE_ENDPOINT"): fixture.QUEUE_ENDPOINT,
            (census, "PINNED_JOURNAL_ORIGIN"): fixture.ORIGIN,
            (census, "PINNED_JOURNAL_RESOURCE_ID"): fixture.JOURNAL_ID,
            (census, "PINNED_JOURNAL_ENDPOINT"): fixture.JOURNAL_ENDPOINT,
            (worker, "PINNED_RECOVERY_ORIGIN"): fixture.ORIGIN,
            (worker, "PINNED_RECOVERY_RESOURCE_ID"): fixture.RECOVERY_ID,
            (worker, "PINNED_RECOVERY_ENDPOINT"): fixture.ORIGIN + "/recovery",
            (worker, "PINNED_RECOVERY_WORKER_ID"): fixture.WORKER_ID,
            (worker, "PINNED_SCHEDULER_ORIGIN"): fixture.ORIGIN,
            (worker, "PINNED_SCHEDULER_RESOURCE_ID"): "scheduler-test",
            (worker, "PINNED_SCHEDULER_ENDPOINT"): fixture.ORIGIN + "/schedule",
            (worker, "PINNED_CENSUS_QUEUE_RESOURCE_ID"): fixture.QUEUE_ID,
            (worker, "PINNED_CENSUS_JOURNAL_RESOURCE_ID"): fixture.JOURNAL_ID,
            (worker.finalizer, "PINNED_FINALIZER_ORIGIN"): fixture.ORIGIN,
            (worker.finalizer, "PINNED_FINALIZER_RESOURCE_ID"): fixture.FINALIZER_ID,
            (worker.finalizer, "PINNED_FINALIZER_ENDPOINT"): fixture.ORIGIN + "/pending",
            (worker.finalizer, "PINNED_TOKEN_VAULT_ID"): fixture.VAULT_ID,
            (worker.finalizer, "PINNED_REVOKER_ID"): "protected-native-revoker-test",
            (fixture.census.worker.finalizer, "PINNED_REVOKER_ID"):
                "protected-native-revoker-test",
        }
        for (module, name), value in pins.items():
            original = getattr(module, name)
            setattr(module, name, value)
            self.addCleanup(setattr, module, name, original)

    def test_complete_sealed_census_appends_one_atomic_batch(self):
        verdict = scheduler.schedule_pending(self.port)
        self.assertEqual(3, verdict["jobCount"])
        self.assertEqual("pending", verdict["disposition"])
        self.assertEqual(1, self.port.calls.count("append-batch"))
        self.assertEqual(3, len(self.port.batch["jobs"]))

    def test_store_omission_duplicate_or_withdrawal_refuses(self):
        for fault in ("omit", "duplicate", "withdraw", "mutate-argument-state"):
            with self.subTest(fault=fault):
                self.port = FakeDurableStore()
                self.port.store_fault = fault
                with self.assertRaisesRegex(scheduler.Refused, "schedule-batch-readback"):
                    scheduler.schedule_pending(self.port)
                self.assertEqual(1, self.port.calls.count("append-batch"))

    def test_pre_pending_mint_refuses_before_any_job_append(self):
        self.port.seal["pendingCount"] = 0
        self.port.seal["pendingSha256"] = scheduler.census._digest([])
        self.port.pages = {"0": {"schema": scheduler.census.PAGE_SCHEMA,
                                 "sealId": fixture.SEAL_ID, "highWater": 3,
                                 "cursor": "0", "items": [],
                                 "nextCursor": None}}
        with self.assertRaisesRegex(scheduler.census.Refused, "mint-unaccounted"):
            scheduler.schedule_pending(self.port)
        self.assertNotIn("append-batch", self.port.calls)

    def test_lost_append_response_does_not_retry_or_authorize(self):
        original = self.port.append_schedule_batch_once
        def lost(batch, seal_id, high_water):
            original(batch, seal_id, high_water)
            raise OSError("response lost after commit")
        self.port.append_schedule_batch_once = lost
        with self.assertRaisesRegex(scheduler.Refused, "schedule-unknown"):
            scheduler.schedule_pending(self.port)
        self.assertEqual(1, self.port.calls.count("append-batch"))


if __name__ == "__main__":
    unittest.main()
