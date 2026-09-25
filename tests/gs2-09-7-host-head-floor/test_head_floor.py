from concurrent.futures import ThreadPoolExecutor
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


joint = load("head_floor_joint_source", ROOT / "scripts/gs2-09-7-host-joint-seal.py")
fixture = load("head_floor_signing_fixture",
               ROOT / "tests/gs2-09-7-host-joint-seal/signing_fixture.py")
floor = joint.floor


class FakePort:
    pass


class HeadFloorTests(unittest.TestCase):
    def setUp(self):
        self.seal = {"sealId": "f" * 64, "highWater": 3,
                     "pendingSha256": "1" * 64, "mintSha256": "2" * 64}
        self.port = FakePort()
        fixture.pin(self, joint)
        fixture.attach(joint, self.port, lambda: self.seal)

    def head(self):
        return self.port.read_joint_seal_head("a" * 64)["record"]["head"]

    def observe(self, head=None):
        floor.require_monotonic(
            self.port, self.head() if head is None else head,
            fixture.STORE_ID, fixture.SIGNER_ID, fixture.POLICY_SHA256)

    def test_exact_floor_advance_and_repeated_read_do_not_write_twice(self):
        self.observe()
        self.assertEqual(1, self.port.joint_floor_record["generation"])
        self.assertEqual(self.seal["sealId"], self.port.joint_floor_record["sealId"])
        self.observe()
        self.assertEqual(1, self.port.joint_floor_calls.count("advance"))

    def test_lower_freshly_signed_head_after_restart_refuses(self):
        self.port.joint_generation = 2
        self.observe()
        persisted = copy.deepcopy(self.port.joint_floor_record)
        recovered = FakePort()
        fixture.attach(joint, recovered, lambda: self.seal)
        recovered.joint_floor_record = persisted
        self.port = recovered
        self.port.joint_generation = 1
        with self.assertRaisesRegex(floor.Refused, "joint-floor-rollback"):
            self.observe()
        self.assertNotIn("advance", self.port.joint_floor_calls)

    def test_same_generation_different_seal_refuses_fork(self):
        self.observe()
        changed = {**self.head(), "sealId": "e" * 64}
        with self.assertRaisesRegex(floor.Refused, "joint-floor-fork"):
            self.observe(changed)

    def test_missing_pin_or_candidate_writable_floor_refuses(self):
        floor.PINNED_FLOOR_RESOURCE_ID = ""
        with self.assertRaisesRegex(floor.Refused, "joint-floor-unconfigured"):
            self.observe()
        floor.PINNED_FLOOR_RESOURCE_ID = fixture.FLOOR_ID
        descriptor = self.port.describe_head_floor
        self.port.describe_head_floor = lambda: {
            **descriptor(), "candidateCanWrite": True}
        with self.assertRaisesRegex(floor.Refused, "joint-floor-authority"):
            self.observe()
        self.assertEqual([], self.port.joint_floor_calls)

    def test_false_commit_without_durable_floor_readback_refuses(self):
        self.port.joint_floor_false_commit = True
        with self.assertRaisesRegex(floor.Refused, "joint-floor-readback"):
            self.observe()
        self.assertEqual(0, self.port.joint_floor_record["generation"])
        self.assertEqual(1, self.port.joint_floor_calls.count("advance"))

    def test_lost_cas_response_refuses_without_retry(self):
        self.port.joint_floor_lost_response = True
        with self.assertRaisesRegex(floor.Refused, "joint-floor-advance-unknown"):
            self.observe()
        self.assertEqual(1, self.port.joint_floor_record["generation"])
        self.assertEqual(1, self.port.joint_floor_calls.count("advance"))

    def test_stale_read_after_cas_refuses(self):
        self.port.joint_floor_stale_after = 2
        with self.assertRaisesRegex(floor.Refused, "joint-floor-readback"):
            self.observe()
        self.assertEqual(1, self.port.joint_floor_record["generation"])

    def test_stale_pre_read_cannot_hide_higher_durable_floor(self):
        self.port.joint_floor_record["generation"] = 2
        self.port.joint_floor_record["sealId"] = self.seal["sealId"]
        self.port.joint_floor_stale_after = 1
        with self.assertRaisesRegex(floor.Refused, "joint-floor-readback"):
            self.observe()
        self.assertNotIn("advance", self.port.joint_floor_calls)

    def test_foreign_floor_record_refuses_before_advance(self):
        self.port.joint_floor_record["storeResourceId"] = "foreign-store"
        with self.assertRaisesRegex(floor.Refused, "joint-floor-record"):
            self.observe()
        self.assertNotIn("advance", self.port.joint_floor_calls)

    def test_concurrent_same_head_observers_commit_one_floor(self):
        with ThreadPoolExecutor(max_workers=2) as pool:
            left = pool.submit(self.observe)
            right = pool.submit(self.observe)
            left.result()
            right.result()
        self.assertEqual(1, self.port.joint_floor_record["generation"])
        self.assertEqual(1, self.port.joint_floor_calls.count("advance"))


if __name__ == "__main__":
    unittest.main()
