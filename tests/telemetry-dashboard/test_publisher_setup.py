import argparse
import base64
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest import mock

ROOT=pathlib.Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location("dashboard_setup",ROOT/"tools/telemetry-dashboard.py")
D=importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(D)

def host(observed="2026-09-09T08:00:00Z",breaches=0):
    return D.aggregate_host({"schema":"fsgg.telemetry.public-export/1","items":[]},[],[],{"epoch":"e1","distinctBreaches":breaches,"dirtyItems":0,"intervention":"none"},observed,[],[],{"status":"ready","schemaVersion":8,"journalMode":"wal","pendingBatches":0})

class PublisherSetupTests(unittest.TestCase):
    def fixture(self,root):
        config=root/"config.json"; config.write_text("{}\n"); config.chmod(0o600)
        labels=root/"labels.json"; labels.write_text(json.dumps({"schema":D.LABELS_SCHEMA,"items":{},"models":{},"efforts":{},"scopes":{}})+"\n"); labels.chmod(0o600)
        args=argparse.Namespace(config=config,labels=labels,repo="FS-GG/.github",branch="telemetry-data",path="host.json",output=root/"host.json",systemd_dir=root/"systemd",install_only=False,activate=False,approve_labels=None,authorize_recurring_publication=False,authorize_event_publication=False)
        return config,labels,args

    def test_default_preview_has_zero_external_or_systemd_effects(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,args=self.fixture(root)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"github") as github,mock.patch.object(D.subprocess,"run") as run:
                result=D.publisher_setup(args)
            self.assertEqual(result["mode"],"preview"); self.assertEqual(result["effects"],[]); self.assertFalse(args.systemd_dir.exists()); self.assertFalse((root/D.EVENT_RECEIPT_NAME).exists()); github.assert_not_called(); run.assert_not_called()

    def test_event_authorization_flag_in_preview_remains_zero_effect(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,args=self.fixture(root); args.authorize_event_publication=True
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"github") as github:
                result=D.publisher_setup(args)
            self.assertEqual((result["mode"],result["effects"]),("preview",[])); self.assertFalse((root/D.EVENT_RECEIPT_NAME).exists()); github.assert_not_called()

    def test_publication_token_uses_environment_then_bounded_gh_discovery(self):
        with mock.patch.dict(D.os.environ,{"GITHUB_TOKEN":"environment-token"},clear=True),mock.patch.object(D.subprocess,"run") as run:
            self.assertEqual(D.publication_token(),"environment-token"); run.assert_not_called()
        completed=mock.Mock(returncode=0,stdout="credential-token\n")
        with mock.patch.dict(D.os.environ,{},clear=True),mock.patch.object(D.shutil,"which",return_value="/usr/bin/gh"),mock.patch.object(D.subprocess,"run",return_value=completed) as run:
            self.assertEqual(D.publication_token(),"credential-token"); run.assert_called_once_with(["/usr/bin/gh","auth","token"],capture_output=True,text=True,check=False)

    def test_install_is_inert_idempotent_and_refuses_conflicts(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,args=self.fixture(root); args.install_only=True
            patches=(mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()))
            with patches[0],patches[1],patches[2]: self.assertEqual(D.publisher_setup(args)["installation"],"installed")
            service_text=(args.systemd_dir/"fsgg-telemetry-dashboard.service").read_text()
            self.assertIn(f'"--producer-executable" "{pathlib.Path(D.sys.executable).resolve()}"',service_text); self.assertIn('"--credential-source" "environment-or-gh-auth"',service_text)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()): self.assertEqual(D.publisher_setup(args)["installation"],"unchanged")
            service=args.systemd_dir/"fsgg-telemetry-dashboard.service"; service.write_text("unrelated\n")
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),self.assertRaises(D.HostSourceError): D.publisher_setup(args)

    def test_changed_label_approval_refuses_before_install_or_publish(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,args=self.fixture(root); args.activate=True; args.authorize_recurring_publication=True; args.approve_labels="0"*64
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"install_units") as install,mock.patch.object(D,"publish") as publish,self.assertRaises(D.HostSourceError): D.publisher_setup(args)
            install.assert_not_called(); publish.assert_not_called()

    def test_verified_publication_reports_unavailable_recurrence_without_user_bus(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,labels,args=self.fixture(root); args.activate=True; args.authorize_recurring_publication=True
            args.approve_labels=D.hashlib.sha256(labels.read_bytes()).hexdigest()
            verified={"commit":"a"*40,"immutableBytes":True,"payloadRevision":True,"branchCurrent":True,"verified":True}
            unavailable=D.subprocess.CalledProcessError(1,["systemctl","--user","daemon-reload"])
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"publication_token",return_value="token"),mock.patch.object(D,"publish",return_value="a"*40),mock.patch.object(D,"verify_publication",return_value=verified),mock.patch.object(D.subprocess,"run",side_effect=unavailable):
                result=D.publisher_setup(args)
            self.assertEqual(result["publication"],verified); self.assertEqual(result["recurrence"],"unavailable")
            self.assertEqual(result["effects"],["write-inert-user-units","publish-once"])

    def test_systemd_activation_does_not_authorize_event_mode_without_explicit_flag(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,labels,args=self.fixture(root); args.activate=True; args.authorize_recurring_publication=True
            args.approve_labels=D.hashlib.sha256(labels.read_bytes()).hexdigest()
            verified={"commit":"a"*40,"immutableBytes":True,"payloadRevision":True,"branchCurrent":True,"verified":True}
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"publication_token",return_value="token"),mock.patch.object(D,"publish",return_value="a"*40),mock.patch.object(D,"verify_publication",return_value=verified),mock.patch.object(D.subprocess,"run",return_value=mock.Mock(returncode=0)):
                D.publisher_setup(args)
            self.assertFalse((root/D.EVENT_RECEIPT_NAME).exists())

    def test_explicit_event_activation_writes_closed_private_receipt_after_verification(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,labels,args=self.fixture(root); args.activate=True; args.authorize_recurring_publication=True; args.authorize_event_publication=True
            args.approve_labels=D.hashlib.sha256(labels.read_bytes()).hexdigest()
            verified={"commit":"a"*40,"immutableBytes":True,"payloadRevision":True,"branchCurrent":True,"verified":True}
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"publication_token",return_value="token"),mock.patch.object(D,"publish",return_value="a"*40),mock.patch.object(D,"verify_publication",return_value=verified),mock.patch.object(D.subprocess,"run",return_value=mock.Mock(returncode=0)):
                result=D.publisher_setup(args)
            receipt=root/D.EVENT_RECEIPT_NAME; value=json.loads(receipt.read_text())
            self.assertEqual(receipt.stat().st_mode & 0o777,0o600)
            self.assertEqual(set(value),{"schema","configDigest","engine","labelsPath","labelsDigest","destination","credentialSource"})
            self.assertNotIn("token",json.dumps(value).lower()); self.assertEqual(result["eventPublication"],"active")

    def test_existing_event_receipt_destination_change_refuses_before_effects(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,labels,args=self.fixture(root); args.activate=True; args.authorize_recurring_publication=True; args.authorize_event_publication=True
            args.approve_labels=D.hashlib.sha256(labels.read_bytes()).hexdigest()
            receipt=root/D.EVENT_RECEIPT_NAME; receipt.write_text(json.dumps({"schema":D.EVENT_RECEIPT_SCHEMA,"configDigest":D.hashlib.sha256(config.read_bytes()).hexdigest(),"engine":str(pathlib.Path(D.sys.executable).resolve()),"labelsPath":str(labels.resolve()),"labelsDigest":args.approve_labels,"destination":{"repository":"FS-GG/.github","branch":"other","path":"host.json"},"credentialSource":"environment-or-gh-auth"})); receipt.chmod(0o600)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"install_units") as install,mock.patch.object(D,"publish") as publish,self.assertRaises(D.HostSourceError): D.publisher_setup(args)
            install.assert_not_called(); publish.assert_not_called()

    def test_immutable_verification_rejects_wrong_bytes_revision_and_advanced_branch(self):
        snapshot=host(); commit="a"*40
        good=base64.b64encode(D.dump(snapshot)).decode()
        cases=[base64.b64encode(b"{}\n").decode(),good,good]
        refs=[commit,commit,"b"*40]
        for index in range(3):
            content=cases[index]
            if index==1:
                changed=dict(snapshot); changed["revision"]="0"*64; content=base64.b64encode(D.dump(changed)).decode()
            answers=[({"encoding":"base64","content":content},{}),({"object":{"sha":refs[index]}},{})]
            with mock.patch.object(D,"github",side_effect=answers): result=D.verify_publication("FS-GG/.github","telemetry-data","host.json","token",commit,snapshot)
            self.assertFalse(result["verified"])
        wrapped="\n".join(good[index:index+60] for index in range(0,len(good),60))
        with mock.patch.object(D,"github",side_effect=[({"encoding":"base64","content":wrapped},{}),({"object":{"sha":commit}}, {})]):
            self.assertTrue(D.verify_publication("FS-GG/.github","telemetry-data","host.json","token",commit,snapshot)["verified"])


class EventPublisherTests(unittest.TestCase):
    def fixture(self,root):
        config=root/"telemetry.json"; config.write_text("{}\n"); config.chmod(0o600)
        labels=root/"labels.json"; labels.write_text(json.dumps({"schema":D.LABELS_SCHEMA,"items":{},"models":{},"efforts":{},"scopes":{}})+"\n"); labels.chmod(0o600)
        engine=pathlib.Path(D.sys.executable).resolve()
        receipt={"schema":D.EVENT_RECEIPT_SCHEMA,"configDigest":D.hashlib.sha256(config.read_bytes()).hexdigest(),"engine":str(engine),"labelsPath":str(labels.resolve()),"labelsDigest":D.hashlib.sha256(labels.read_bytes()).hexdigest(),"destination":{"repository":"FS-GG/.github","branch":"telemetry-data","path":"host.json"},"credentialSource":"environment-or-gh-auth"}
        receipt_path=root/D.EVENT_RECEIPT_NAME; D.atomic_private(receipt_path,receipt)
        return config,labels,receipt_path,receipt,argparse.Namespace(config=config)

    def common(self,config):
        return mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(config.parent/"store"),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"publication_token",return_value="fake-token")

    def test_no_receipt_has_zero_effects(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config=root/"telemetry.json"; config.write_text("{}\n"); config.chmod(0o600); args=argparse.Namespace(config=config)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D,"build_host") as build,mock.patch.object(D,"publication_token") as token,mock.patch.object(D,"write_event_health") as write:
                result=D.publisher_event(args)
            self.assertEqual((result["status"],result["reason"]),("skipped","EVENT_ACTIVATION_RECEIPT_MISSING")); build.assert_not_called(); token.assert_not_called(); write.assert_not_called()
        with mock.patch.object(D,"config",side_effect=RuntimeError("private/config/path")):
            result=D.publisher_event(argparse.Namespace(config=pathlib.Path("/private/config/path")))
        self.assertEqual((result["status"],result["reason"]),("skipped","EVENT_CONFIG_UNAVAILABLE")); self.assertNotIn("private",json.dumps(result))

    def test_receipt_mode_symlink_schema_extra_field_and_digest_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,receipt_path,receipt,args=self.fixture(root)
            receipt_path.chmod(0o644)
            with self.common(config)[0],self.common(config)[1],self.common(config)[2]: self.assertEqual(D.publisher_event(args)["status"],"failed")
            receipt_path.unlink(); target=root/"target"; D.atomic_private(target,receipt); receipt_path.symlink_to(target)
            with self.common(config)[0],self.common(config)[1],self.common(config)[2]: self.assertEqual(D.publisher_event(args)["status"],"failed")
            receipt_path.unlink()
            for mutation in ({**receipt,"schema":"wrong"},{**receipt,"extra":1},{**receipt,"configDigest":"0"*64}):
                D.atomic_private(receipt_path,mutation)
                with self.common(config)[0],self.common(config)[1],self.common(config)[2]: self.assertEqual(D.publisher_event(args)["status"],"failed")

    def test_private_parent_with_group_access_refuses_before_snapshot_or_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,_,_,args=self.fixture(root); root.chmod(0o750)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D,"build_host") as build,mock.patch.object(D,"publication_token") as token:
                result=D.publisher_event(args)
            self.assertEqual(result["status"],"failed"); build.assert_not_called(); token.assert_not_called()

    def test_unchanged_later_observed_at_performs_no_github_write(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,_,_,args=self.fixture(root); current=host(); candidate=host("2026-09-09T09:00:00Z"); p=self.common(config)
            with p[0],p[1],p[2],mock.patch.object(D,"build_host",return_value=candidate) as build,mock.patch.object(D,"current_publication",return_value=(current,"a"*40)),mock.patch.object(D,"publish") as publish,mock.patch.object(D,"verify_publication") as verify:
                result=D.publisher_event(args)
            self.assertEqual((result["status"],result["publicRevision"],result["commit"]),("unchanged",current["revision"],"a"*40)); build.assert_called_once(); publish.assert_not_called(); verify.assert_not_called()

    def test_meaningful_change_publishes_one_verified_commit(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,_,_,args=self.fixture(root); current=host(); candidate=host("2026-09-09T09:00:00Z",1); verified={"commit":"b"*40,"immutableBytes":True,"payloadRevision":True,"branchCurrent":True,"verified":True}; p=self.common(config)
            with p[0],p[1],p[2],mock.patch.object(D,"build_host",return_value=candidate),mock.patch.object(D,"current_publication",return_value=(current,"a"*40)),mock.patch.object(D,"publish",return_value="b"*40) as publish,mock.patch.object(D,"verify_publication",return_value=verified) as verify:
                result=D.publisher_event(args)
            self.assertEqual((result["status"],result["commit"]),("published","b"*40)); publish.assert_called_once(); verify.assert_called_once()

    def test_lock_contention_and_ref_conflict_are_bounded_health(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,_,_,args=self.fixture(root)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D,"acquire_event_lock",return_value=None),mock.patch.object(D,"build_host") as build:
                result=D.publisher_event(args)
            self.assertEqual((result["status"],result["reason"]),("skipped","EVENT_LOCK_CONTENDED")); build.assert_not_called(); p=self.common(config)
            with p[0],p[1],p[2],mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"current_publication",return_value=(host(),"a"*40)),mock.patch.object(D,"unchanged_with_preserved_observation",return_value=False),mock.patch.object(D,"publish",side_effect=D.RefConflict("private sentinel")):
                result=D.publisher_event(args)
            self.assertEqual((result["status"],result["reason"]),("failed","PUBLISH_REF_CONFLICT")); self.assertNotIn("sentinel",json.dumps(result))

    def test_private_failure_text_never_reaches_health(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,_,_,args=self.fixture(root); p=self.common(config)
            with p[0],p[1],p[2],mock.patch.object(D,"build_host",side_effect=ValueError("secret-user/private/path")): result=D.publisher_event(args)
            saved=json.loads((root/D.EVENT_HEALTH_NAME).read_text())
            self.assertEqual(result["reason"],"EVENT_REFRESH_FAILED"); self.assertEqual(set(saved),{"schema","status","reason","observedAt","publicRevision","commit"}); self.assertNotIn("secret-user",json.dumps(saved)); self.assertNotIn("/private/path",json.dumps(saved))

    def test_receipt_is_reloaded_after_lock_before_any_snapshot_or_network_effect(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,receipt_path,_,args=self.fixture(root); real_load=D.load_event_receipt; calls=0
            def replaced(path):
                nonlocal calls
                calls+=1
                if calls==1: return real_load(path)
                receipt_path.unlink(); receipt_path.symlink_to(root/"missing"); return real_load(path)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D,"load_event_receipt",side_effect=replaced),mock.patch.object(D,"build_host") as build,mock.patch.object(D,"publication_token") as token: result=D.publisher_event(args)
            self.assertEqual(calls,2); self.assertEqual(result["status"],"failed"); build.assert_not_called(); token.assert_not_called()

    def test_no_credentials_and_failed_verification_are_advisory_fixed_health(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,_,_,args=self.fixture(root); p=self.common(config)
            with p[0],p[1],mock.patch.object(D,"publication_token",side_effect=D.HostSourceError("PUBLISHER_TOKEN_UNAVAILABLE")),mock.patch.object(D,"build_host") as build: unavailable=D.publisher_event(args)
            self.assertEqual((unavailable["status"],unavailable["reason"]),("failed","PUBLISHER_TOKEN_UNAVAILABLE")); build.assert_not_called(); p=self.common(config)
            verification={"commit":"b"*40,"immutableBytes":False,"payloadRevision":False,"branchCurrent":False,"verified":False}
            with p[0],p[1],p[2],mock.patch.object(D,"build_host",return_value=host("2026-09-09T09:00:00Z",1)),mock.patch.object(D,"current_publication",return_value=(host(),"a"*40)),mock.patch.object(D,"publish",return_value="b"*40),mock.patch.object(D,"verify_publication",return_value=verification): failed=D.publisher_event(args)
            self.assertEqual((failed["status"],failed["reason"],failed["commit"]),("failed","EVENT_PUBLICATION_VERIFICATION_FAILED","b"*40))

if __name__=="__main__": unittest.main()
