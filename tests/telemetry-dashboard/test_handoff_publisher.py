import argparse
import hashlib
import importlib.util
import json
import os
import pathlib
import tempfile
import unittest
from unittest import mock

ROOT=pathlib.Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location("handoff_dashboard",ROOT/"tools"/"telemetry-dashboard.py")
D=importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(D)
FIXTURE_SPEC=importlib.util.spec_from_file_location("publisher_fixture",pathlib.Path(__file__).with_name("test_publisher_setup.py"))
F=importlib.util.module_from_spec(FIXTURE_SPEC); FIXTURE_SPEC.loader.exec_module(F)


def cutover(config_digest,candidate_digest):
    value={"schema":D.HANDOFF_CUTOVER_SCHEMA,"configDigest":config_digest,"candidateDigest":candidate_digest,"incumbentAccountUid":1001,
        "managerIdentity":"user@1001.service","timerUnit":"fsgg-telemetry-dashboard.timer","timerEnabledState":"disabled","timerActiveState":"inactive",
        "serviceUnit":"fsgg-telemetry-dashboard.service","serviceActiveState":"inactive","eventActivationPathDigest":"4"*64,
        "eventActivationPriorState":"removed-by-cutover","eventActivationReceiptDigest":"3"*64,
        "eventActivationState":"absent","observedAt":"2026-09-10T12:00:00Z"}
    value["evidenceDigest"]=hashlib.sha256(D.dump(value)).hexdigest()
    return value


class HandoffPublisherTests(unittest.TestCase):
    def fixture(self):
        temporary=tempfile.TemporaryDirectory(); root=pathlib.Path(temporary.name)
        outgoing=root/"outgoing"; outgoing.mkdir(); outgoing.chmod(0o2750)
        state=root/"publisher"; state.mkdir(); state.chmod(0o700)
        labels=root/"labels.json"; labels.write_text(json.dumps({"schema":D.LABELS_SCHEMA,"items":{},"models":{},"efforts":{},"scopes":{}})+"\n"); labels.chmod(0o600)
        config=root/"telemetry.json"; config.write_text("{}\n"); config.chmod(0o600)
        digest=hashlib.sha256(labels.read_bytes()).hexdigest()
        stage=argparse.Namespace(outgoing=outgoing,handoff_gid=os.getgid(),labels=labels,approve_labels=digest,config=config,producer_executable=None)
        return temporary,root,outgoing,state,labels,digest,stage

    def stage(self, args, snapshot=None):
        args.config.write_text(json.dumps({"schema":"fsgg.telemetry.host-config/1","storeRoot":str(args.config.parent/"store"),"engine":"fsgg-coord-engine"})+"\n")
        with mock.patch.object(D,"build_host",return_value=snapshot or F.host()): return D.handoff_stage(args)

    def test_explicit_producer_config_is_self_contained_outside_checkout(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary); copied=root/"telemetry-dashboard.py"
            copied.write_bytes((ROOT/"tools"/"telemetry-dashboard.py").read_bytes())
            spec=importlib.util.spec_from_file_location("isolated_handoff_dashboard",copied)
            isolated=importlib.util.module_from_spec(spec); spec.loader.exec_module(isolated)
            config=root/"telemetry.json"
            config.write_text(json.dumps({"schema":"fsgg.telemetry.host-config/1","storeRoot":str(root/"store"),"engine":"fsgg-coord-engine"})+"\n")
            config.chmod(0o600)
            path,value=isolated._explicit_producer_config(config)
            self.assertEqual(path,config)
            self.assertEqual(value,{"storeRoot":str(root/"store"),"engine":"fsgg-coord-engine"})
            self.assertFalse((root/".claude").exists())

    def test_explicit_producer_config_rejects_workspace_and_unsafe_shapes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=pathlib.Path(temporary); config=root/"telemetry.json"
            for value in (
                {"schema":"fsgg.telemetry.workspace-config/1","engine":"fsgg-coord-engine","associations":[],"retiredAssociations":[]},
                {"schema":"fsgg.telemetry.host-config/1","storeRoot":"relative","engine":"fsgg-coord-engine"},
                {"schema":"fsgg.telemetry.host-config/1","storeRoot":str(root/"store"),"engine":"/tmp/engine"},
            ):
                config.write_text(json.dumps(value)+"\n"); config.chmod(0o600)
                with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_CONFIG_INVALID"):
                    D._explicit_producer_config(config)

    def test_checked_in_two_uid_proof_is_bound_to_current_tool_and_provider(self):
        result=json.loads(pathlib.Path(__file__).with_name("two_uid_handoff_proof.result.json").read_text())
        script=pathlib.Path(__file__).with_name("two_uid_handoff_proof.py")
        self.assertEqual(result["status"],"pass")
        self.assertEqual(result["candidateToolDigest"],hashlib.sha256((ROOT/"tools"/"telemetry-dashboard.py").read_bytes()).hexdigest())
        self.assertEqual(result["proofScriptDigest"],hashlib.sha256(script.read_bytes()).hexdigest())

    def activate(self,outgoing,state,digest):
        args=argparse.Namespace(outgoing=outgoing,state_dir=state,producer_uid=os.getuid(),handoff_gid=os.getgid(),approve_labels=digest,
            repo="FS-GG/.github",branch="telemetry-data",path="host.json",candidate_digest=D._candidate_digest(),cutover_proof_dir=state,
            operator_uid=os.getuid()+10000,cutover_gid=os.getgid(),
            record_activation=True,authorize_single_publisher_cutover=True)
        with mock.patch.object(D,"_load_cutover_proof",side_effect=lambda directory,uid,gid,producer_uid,config_digest,candidate_digest: cutover(config_digest,candidate_digest)): return D.handoff_setup(args)

    def test_stage_is_digest_bound_group_read_only_and_never_requests_credentials(self):
        temporary,_,outgoing,_,_,digest,args=self.fixture()
        with temporary,mock.patch.object(D,"publication_token") as token:
            result=self.stage(args); manifest=json.loads((outgoing/D.HANDOFF_CURRENT_NAME).read_text()); blob=outgoing/manifest["blob"]
            self.assertEqual((result["status"],manifest["labelsDigest"]),("staged",digest)); self.assertEqual(blob.stat().st_mode&0o777,0o640)
            self.assertEqual(outgoing.stat().st_mode&0o7777,0o2750); self.assertEqual(blob.stat().st_gid,outgoing.stat().st_gid)
            self.assertFalse(blob.stat().st_mode&0o020); token.assert_not_called()

    def test_approval_change_and_corrupt_blob_fail_closed(self):
        temporary,_,outgoing,state,_,digest,args=self.fixture()
        with temporary:
            args.approve_labels="0"*64
            with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_LABEL_APPROVAL_MISMATCH"): self.stage(args)
            args.approve_labels=digest; self.stage(args); manifest=json.loads((outgoing/D.HANDOFF_CURRENT_NAME).read_text())
            (outgoing/manifest["blob"]).write_bytes(b"{}\n"); (outgoing/manifest["blob"]).chmod(0o640)
            with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_BLOB_INVALID"): D._load_handoff(outgoing,os.getuid(),os.getgid())

    def test_pointer_rename_failure_preserves_prior_pending_blob_and_pointer(self):
        temporary,_,outgoing,_,_,_,args=self.fixture()
        with temporary:
            first=self.stage(args,F.host()); before=(outgoing/D.HANDOFF_CURRENT_NAME).read_bytes()
            real_replace=D.os.replace
            def fail_pointer(source,target):
                if pathlib.Path(target).name==D.HANDOFF_CURRENT_NAME: raise OSError("synthetic rename failure")
                return real_replace(source,target)
            with mock.patch.object(D.os,"replace",side_effect=fail_pointer),self.assertRaisesRegex(D.HostSourceError,"HANDOFF_DURABILITY_UNKNOWN"): self.stage(args,F.host("2026-09-10T13:00:00Z",1))
            # A pre-rename failure leaves the old pointer here. A post-rename
            # directory-fsync failure is instead reported as unknown.
            self.assertEqual((outgoing/D.HANDOFF_CURRENT_NAME).read_bytes(),before)
            self.assertTrue((outgoing/("snapshot-"+first["snapshotDigest"]+".json")).exists())

    def test_setup_binds_candidate_and_real_cutover_provider_without_publishing(self):
        temporary,_,outgoing,state,_,digest,args=self.fixture()
        with temporary:
            self.stage(args); setup=argparse.Namespace(outgoing=outgoing,state_dir=state,producer_uid=os.getuid(),handoff_gid=os.getgid(),approve_labels=digest,
                repo="FS-GG/.github",branch="telemetry-data",path="host.json",candidate_digest=D._candidate_digest(),cutover_proof_dir=state,
                operator_uid=os.getuid()+10000,cutover_gid=os.getgid(),
                record_activation=True,authorize_single_publisher_cutover=True)
            provider=lambda directory,uid,gid,producer_uid,config_digest,candidate_digest: cutover(config_digest,candidate_digest)
            with mock.patch.object(D,"_load_cutover_proof",side_effect=provider) as load_proof,mock.patch.object(D,"publish") as publish:
                result=D.handoff_setup(setup)
            self.assertEqual(result["effects"],["record-digest-bound-cutover-activation"]); load_proof.assert_called_once(); publish.assert_not_called()
            self.assertEqual((state/D.HANDOFF_ACTIVATION_NAME).stat().st_mode&0o777,0o600)
            setup.candidate_digest="0"*64
            with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_CANDIDATE_MISMATCH"): D.handoff_setup(setup)

    def test_candidate_preview_does_not_require_or_read_cutover_proof(self):
        temporary,_,outgoing,state,_,digest,args=self.fixture()
        with temporary:
            self.stage(args); setup=argparse.Namespace(outgoing=outgoing,state_dir=state,producer_uid=os.getuid(),handoff_gid=os.getgid(),approve_labels=digest,
                repo="FS-GG/.github",branch="telemetry-data",path="host.json",candidate_digest=D._candidate_digest(),cutover_proof_dir=None,
                operator_uid=None,cutover_gid=None,record_activation=False,authorize_single_publisher_cutover=False)
            with mock.patch.object(D,"_load_cutover_proof") as load_proof:
                result=D.handoff_setup(setup)
            self.assertEqual((result["mode"],result["readiness"],result["effects"]),("preview","candidate-ready",[])); load_proof.assert_not_called()

    def test_operator_proof_is_closed_digest_bound_and_not_publisher_owned(self):
        digest="1"*64; candidate="2"*64; proof=cutover(digest,candidate)
        self.assertEqual(D._validate_cutover_proof(proof,digest,candidate),proof)
        changed=dict(proof); changed["eventActivationState"]="present"
        with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_CUTOVER_PROOF_INVALID"): D._validate_cutover_proof(changed,digest,candidate)
        absent=dict(proof); absent["eventActivationPriorState"]="already-absent"; absent["eventActivationReceiptDigest"]=None; absent.pop("evidenceDigest")
        absent["evidenceDigest"]=hashlib.sha256(D.dump(absent)).hexdigest()
        self.assertEqual(D._validate_cutover_proof(absent,digest,candidate),absent)
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory); path.chmod(0o2750)
            with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_CUTOVER_PROOF_INVALID"): D._load_cutover_proof(path,os.getuid(),os.getgid(),61001,digest,candidate)
            with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_CUTOVER_PROOF_INVALID"): D._load_cutover_proof(path,61001,os.getgid(),61001,digest,candidate)

    def test_shared_stage_lock_contention_is_bounded_and_never_changes_mode(self):
        import fcntl
        temporary,_,outgoing,_,_,_,args=self.fixture()
        with temporary:
            self.stage(args); lock_path=outgoing/"stage.lock"; fd=os.open(lock_path,os.O_RDONLY); fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
            try:
                with self.assertRaisesRegex(D.HostSourceError,"HANDOFF_LOCK_CONTENDED"): D._load_handoff(outgoing,os.getuid(),os.getgid())
            finally: os.close(fd)
            self.assertEqual(lock_path.stat().st_mode&0o777,0o640)

    def test_unresolved_restart_readback_retains_old_intent_and_refuses_new_push(self):
        temporary,_,outgoing,state,_,digest,args=self.fixture()
        with temporary:
            old=self.stage(args,F.host()); self.activate(outgoing,state,digest)
            with mock.patch.object(D,"publication_token",return_value="publisher-token"),mock.patch.object(D,"current_publication",side_effect=RuntimeError("offline")),mock.patch.object(D,"publish") as publish:
                first=D.handoff_publish(argparse.Namespace(state_dir=state))
            self.assertEqual(first["reason"],"REMOTE_RECONCILIATION_UNAVAILABLE"); publish.assert_not_called()
            intent_before=(state/D.HANDOFF_INTENT_NAME).read_bytes()
            self.stage(args,F.host("2026-09-10T13:00:00Z",1))
            with mock.patch.object(D,"publication_token",return_value="publisher-token"),mock.patch.object(D,"current_publication",side_effect=RuntimeError("still offline")),mock.patch.object(D,"publish") as publish:
                second=D.handoff_publish(argparse.Namespace(state_dir=state))
            self.assertEqual(second["snapshotDigest"],old["snapshotDigest"]); self.assertEqual((state/D.HANDOFF_INTENT_NAME).read_bytes(),intent_before); publish.assert_not_called()

    def test_ambiguous_push_is_reconciled_on_remote_bytes_without_duplicate(self):
        temporary,_,outgoing,state,_,digest,args=self.fixture()
        with temporary:
            self.stage(args,F.host()); self.activate(outgoing,state,digest); candidate=F.host(); calls=[(None,None),(candidate,"b"*40)]
            with mock.patch.object(D,"publication_token",return_value="publisher-token"),mock.patch.object(D,"current_publication",side_effect=calls),mock.patch.object(D,"publish",side_effect=RuntimeError("lost response")) as publish,mock.patch.object(D,"verify_publication",return_value={"verified":True}):
                result=D.handoff_publish(argparse.Namespace(state_dir=state))
            self.assertEqual((result["status"],result["commit"]),("reconciled","b"*40)); publish.assert_called_once(); self.assertFalse((state/D.HANDOFF_INTENT_NAME).exists())

    def test_failed_exact_readback_keeps_intent_for_current_ref_reconciliation(self):
        temporary,_,outgoing,state,_,digest,args=self.fixture()
        with temporary:
            staged=self.stage(args,F.host()); self.activate(outgoing,state,digest)
            verification={"commit":"b"*40,"immutableBytes":True,"payloadRevision":True,"branchCurrent":False,"verified":False}
            with mock.patch.object(D,"publication_token",return_value="publisher-token"),mock.patch.object(D,"current_publication",return_value=(F.host("2026-09-09T11:00:00Z",1),"a"*40)),mock.patch.object(D,"publish",return_value="b"*40),mock.patch.object(D,"verify_publication",return_value=verification):
                result=D.handoff_publish(argparse.Namespace(state_dir=state))
            self.assertEqual((result["status"],result["reason"]),("pending","PUBLICATION_READBACK_FAILED")); self.assertTrue((state/D.HANDOFF_INTENT_NAME).exists())
            self.assertEqual(json.loads((state/D.HANDOFF_INTENT_NAME).read_text())["snapshotDigest"],staged["snapshotDigest"])

    def test_readback_exceptions_after_normal_and_ambiguous_push_stay_pending(self):
        for ambiguous in (False,True):
            temporary,_,outgoing,state,_,digest,args=self.fixture()
            with temporary:
                self.stage(args,F.host()); self.activate(outgoing,state,digest); current_calls=[(None,None),(F.host(),"b"*40)]
                publish_patch=(mock.patch.object(D,"publish",side_effect=RuntimeError("lost response")) if ambiguous else mock.patch.object(D,"publish",return_value="b"*40))
                with mock.patch.object(D,"publication_token",return_value="publisher-token"),mock.patch.object(D,"current_publication",side_effect=current_calls if ambiguous else [(None,None)]),publish_patch,mock.patch.object(D,"verify_publication",side_effect=RuntimeError("readback unavailable")):
                    result=D.handoff_publish(argparse.Namespace(state_dir=state))
                expected="AMBIGUOUS_PUBLICATION_UNRESOLVED" if ambiguous else "PUBLICATION_READBACK_FAILED"
                self.assertEqual((result["status"],result["reason"]),("pending",expected)); self.assertTrue((state/D.HANDOFF_INTENT_NAME).exists())

    def test_semantic_noop_keeps_remote_observation_and_records_last_success(self):
        temporary,_,outgoing,state,_,digest,args=self.fixture()
        with temporary:
            candidate=F.host("2026-09-10T13:00:00Z"); current=F.host("2026-09-09T08:00:00Z")
            self.stage(args,candidate); self.activate(outgoing,state,digest)
            with mock.patch.object(D,"publication_token",return_value="publisher-token"),mock.patch.object(D,"current_publication",return_value=(current,"a"*40)),mock.patch.object(D,"verify_publication",return_value={"verified":True}),mock.patch.object(D,"publish") as publish:
                result=D.handoff_publish(argparse.Namespace(state_dir=state))
            self.assertEqual((result["status"],result["publicRevision"]),("unchanged",current["revision"])); publish.assert_not_called()
            success=json.loads((state/D.HANDOFF_SUCCESS_NAME).read_text()); served=D.dump(current); served_digest=hashlib.sha256(served).hexdigest()
            self.assertEqual(success["intentSnapshotDigest"],hashlib.sha256(D.dump(candidate)).hexdigest())
            self.assertEqual((success["publishedSnapshotDigest"],success["publicRevision"]),(served_digest,current["revision"]))
            self.assertEqual((state/success["publishedSnapshotFile"]).read_bytes(),served)


if __name__=="__main__": unittest.main()
