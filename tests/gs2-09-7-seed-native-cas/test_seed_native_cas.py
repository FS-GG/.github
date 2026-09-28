import base64
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("seed_native_cas", ROOT / "scripts/gs2-09-7-seed-native-cas.py")
cas = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cas)
PLAN = b'{"seed":"retained"}\n'


def declaration(extra="first"):
    candidate = "b" * 40
    nonce = "12345-2-" + candidate
    ref = cas.nonce_ref(12345, 2, candidate)
    value = {
        "schema": "fsgg.github-substrate-v2.sandbox-seed-execution-binding/2",
        "status": "bound-no-write-authority", "activation": False,
        "source": {"workflowSha": "a" * 40, "candidateSha": candidate,
                   "runId": 12345, "runAttempt": 2, "runNonce": nonce,
                   "seedJournalRef": ref},
        "journal": {"profile": {"ref": ref, "object": {"path": "state.json"}}},
        "artifacts": {"seedPlan": {"sha256": cas.sha256(PLAN)}},
        "fixture": extra,
    }
    value["fingerprint"] = cas.sha256(cas.canonical(value))
    return cas.canonical(value)


def proposal(declaration_digest):
    candidate = "b" * 40
    workflow = "a" * 40
    nonce = "12345-2-" + candidate
    state = json.dumps({
        "schema": "fsgg.gs2-09-7.sandbox-seed-execution/1",
        "stateGeneration": 0, "mode": "forward", "activeIndex": 0,
        "binding": {"runNonce": nonce, "workflowSha": workflow,
                    "protectedHostReceiptSha256": declaration_digest,
                    "seedPlanSha256": cas.sha256(PLAN)},
        "effects": [{"kind": "create-nonce-issue", "stage": "planned",
                     "originalEffectId": None, "ownership": None},
                    {"kind": "add-project-membership", "stage": "planned",
                     "originalEffectId": None, "ownership": None}],
    }, separators=(",", ":")).encode()
    state_sha = cas.sha256(state)
    blob = cas.git_oid("blob", state)
    tree = b"100644 state.json\0" + bytes.fromhex(blob)
    tree_oid = cas.git_oid("tree", tree)
    commit = (f"tree {tree_oid}\n"
              "author FS.GG Q4 Seed Journal <q4-seed-journal@fs.gg> 0 +0000\n"
              "committer FS.GG Q4 Seed Journal <q4-seed-journal@fs.gg> 0 +0000\n\n"
              "fsgg Q4 seed journal generation 0\n"
              f"state-sha256 {state_sha}\n").encode()
    encode = lambda raw: base64.b64encode(raw).decode()
    return {"runId": 12345, "runAttempt": 2, "candidateSha": candidate,
            "workflowSha": workflow, "refName": cas.nonce_ref(12345, 2, candidate),
            "runNonce": nonce, "journalGeneration": 0, "stateGeneration": 0,
            "expectedParent": None, "stateSha256": state_sha,
            "stateBytesBase64": encode(state), "blobOid": blob,
            "treeBytesBase64": encode(tree), "treeOid": tree_oid,
            "commitBytesBase64": encode(commit), "commitOid": cas.git_oid("commit", commit),
            "s2DeclarationSha256": declaration_digest,
            "seedPlanSha256": cas.sha256(PLAN)}


class NativeCasTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.remote = Path(self.temp.name) / "remote.git"
        self.remote.mkdir()
        self.port = cas.GitPort(str(self.remote))
        self.port.new_repo(self.remote)
        self.declaration = declaration()
        self.value = proposal(cas.sha256(self.declaration))

    def test_exact_raw_object_genesis_and_independent_readback(self):
        with mock.patch.object(cas, "INSTALLATION_STATUS", "installed-protected-host"):
            receipt = cas.apply(self.value, self.declaration, PLAN, self.port,
                                protected_grant_verified=True)
        self.assertTrue(receipt["complete"])
        self.assertEqual(cas.SCHEMA, receipt["schema"])
        self.assertEqual(self.value["commitOid"], receipt["observedRefOid"])
        self.assertEqual(self.value["s2DeclarationSha256"], receipt["s2DeclarationSha256"])
        fresh = self.port.fresh(self.value["refName"])
        self.assertEqual(base64.b64decode(self.value["stateBytesBase64"]), fresh["state"])
        self.assertEqual(base64.b64decode(self.value["treeBytesBase64"]), fresh["tree"])
        self.assertEqual(base64.b64decode(self.value["commitBytesBase64"]), fresh["commit"])

    def test_source_only_and_missing_grant_refuse_before_object_write(self):
        with self.assertRaisesRegex(cas.Refused, "uninstalled"):
            cas.apply(self.value, self.declaration, PLAN, self.port, protected_grant_verified=True)
        with mock.patch.object(cas, "INSTALLATION_STATUS", "installed-protected-host"):
            with self.assertRaisesRegex(cas.Refused, "uninstalled"):
                cas.apply(self.value, self.declaration, PLAN, self.port, protected_grant_verified=False)
        self.assertIsNone(self.port.ref_oid(self.remote, self.value["refName"]))

    def test_expected_absence_lease_refuses_second_different_genesis(self):
        with mock.patch.object(cas, "INSTALLATION_STATUS", "installed-protected-host"):
            cas.apply(self.value, self.declaration, PLAN, self.port, protected_grant_verified=True)
            second_declaration = declaration("second")
            second = proposal(cas.sha256(second_declaration))
            result = cas.apply(second, second_declaration, PLAN, self.port,
                               protected_grant_verified=True)
            self.assertFalse(result["complete"])
            self.assertEqual("object-mismatch", result["reason"])
        self.assertEqual(self.value["commitOid"], self.port.ref_oid(self.remote, self.value["refName"]))

    def test_exact_oid_state_nonce_generation_and_path_refusals(self):
        changes = [
            {**self.value, "journalGeneration": 1},
            {**self.value, "stateGeneration": 1},
            {**self.value, "expectedParent": "f" * 40},
            {**self.value, "refName": "refs/heads/main"},
            {**self.value, "blobOid": "f" * 40},
            {**self.value, "treeOid": "f" * 40},
            {**self.value, "commitOid": "f" * 40},
        ]
        for changed in changes:
            with self.subTest(changed=changed), self.assertRaises(cas.Refused):
                cas.inspect_proposal(changed)

    def test_changed_declaration_refuses_before_any_push(self):
        with mock.patch.object(cas, "INSTALLATION_STATUS", "installed-protected-host"):
            with self.assertRaisesRegex(cas.Refused, "s2-declaration-bytes"):
                cas.apply(self.value, declaration("changed"), PLAN, self.port,
                          protected_grant_verified=True)
        self.assertIsNone(self.port.ref_oid(self.remote, self.value["refName"]))

    def test_fresh_ref_movement_and_object_drift_refuse(self):
        item = cas.inspect_proposal(self.value)
        with mock.patch.object(self.port, "fresh", return_value={
            "refOid": "f" * 40, "commit": item["commit"], "tree": item["tree"],
            "state": item["state"], "treeOid": self.value["treeOid"],
            "blobOid": self.value["blobOid"]}):
            with self.assertRaisesRegex(cas.Refused, "native-cas-mismatch"):
                cas.readback(item, self.port)

    def test_lost_or_unavailable_readback_stays_explicitly_pending(self):
        with mock.patch.object(cas, "INSTALLATION_STATUS", "installed-protected-host"), \
             mock.patch.object(self.port, "fresh", side_effect=cas.Refused("native-ref-missing")):
            report = cas.apply(self.value, self.declaration, PLAN, self.port,
                               protected_grant_verified=True)
        self.assertFalse(report["complete"])
        self.assertEqual("pending", report["status"])
        self.assertEqual("readback-unavailable", report["reason"])


if __name__ == "__main__":
    unittest.main()
