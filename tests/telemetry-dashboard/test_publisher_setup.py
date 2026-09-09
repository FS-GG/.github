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

def host():
    return D.aggregate_host({"schema":"fsgg.telemetry.public-export/1","items":[]},[],[],{"epoch":"e1","distinctBreaches":0,"dirtyItems":0,"intervention":"none"},"2026-09-09T08:00:00Z",[],[],{"status":"ready","schemaVersion":8,"journalMode":"wal","pendingBatches":0})

class PublisherSetupTests(unittest.TestCase):
    def fixture(self,root):
        config=root/"config.json"; config.write_text("{}\n"); config.chmod(0o600)
        labels=root/"labels.json"; labels.write_text(json.dumps({"schema":D.LABELS_SCHEMA,"items":{},"models":{},"efforts":{},"scopes":{}})+"\n"); labels.chmod(0o600)
        args=argparse.Namespace(config=config,labels=labels,repo="FS-GG/.github",branch="telemetry-data",path="host.json",output=root/"host.json",systemd_dir=root/"systemd",install_only=False,activate=False,approve_labels=None,authorize_recurring_publication=False)
        return config,labels,args

    def test_default_preview_has_zero_external_or_systemd_effects(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,args=self.fixture(root)
            with mock.patch.object(D,"config",return_value=(config,{"storeRoot":str(root),"engine":"engine"})),mock.patch.object(D.shutil,"which",return_value=D.sys.executable),mock.patch.object(D,"build_host",return_value=host()),mock.patch.object(D,"github") as github,mock.patch.object(D.subprocess,"run") as run:
                result=D.publisher_setup(args)
            self.assertEqual(result["mode"],"preview"); self.assertEqual(result["effects"],[]); self.assertFalse(args.systemd_dir.exists()); github.assert_not_called(); run.assert_not_called()

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

if __name__=="__main__": unittest.main()
