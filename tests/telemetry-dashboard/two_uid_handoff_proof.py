#!/usr/bin/env python3
"""Exercise the publication handoff with distinct disposable numeric UIDs."""

import hashlib
import importlib.util
import json
import os
import pathlib
import subprocess
import tempfile

ROOT=pathlib.Path(__file__).resolve().parents[2]
TOOL=ROOT/"tools"/"telemetry-dashboard.py"
FIXTURE=pathlib.Path(__file__).with_name("test_publisher_setup.py")
PRODUCER_UID=61001
PUBLISHER_UID=61002
HANDOFF_GID=61003


def run(*args,env=None):
    subprocess.run(args,check=True,env=env)


def load(path,name):
    spec=importlib.util.spec_from_file_location(name,path); module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module


def main():
    dashboard=load(TOOL,"two_uid_dashboard"); fixture=load(FIXTURE,"two_uid_fixture")
    root=pathlib.Path(tempfile.mkdtemp(prefix="fsgg-p1-two-uid."))
    if root.parent!=pathlib.Path("/tmp") or not root.name.startswith("fsgg-p1-two-uid."): raise RuntimeError("unsafe proof root")
    try:
        root.chmod(0o711); outgoing=root/"outgoing"; outgoing.mkdir()
        snapshot=fixture.host(); raw=dashboard.dump(snapshot); digest=hashlib.sha256(raw).hexdigest()
        blob=outgoing/("snapshot-"+digest+".json"); blob.write_bytes(raw)
        manifest={"schema":dashboard.HANDOFF_SCHEMA,"blob":blob.name,"snapshotDigest":digest,"publicRevision":snapshot["revision"],
            "labelsDigest":"1"*64,"stagedAt":"2026-09-10T12:00:00Z"}
        (outgoing/"current.json").write_bytes(dashboard.dump(manifest)); (outgoing/"stage.lock").write_bytes(b"")
        producer_private=root/"producer-private"; publisher_state=root/"publisher-state"
        run("sudo","-n","mkdir",str(producer_private),str(publisher_state))
        for path in outgoing.iterdir(): run("sudo","-n","chown",f"{PRODUCER_UID}:{HANDOFF_GID}",str(path)); run("sudo","-n","chmod","0640",str(path))
        run("sudo","-n","chown",f"{PRODUCER_UID}:{HANDOFF_GID}",str(outgoing)); run("sudo","-n","chmod","2750",str(outgoing))
        run("sudo","-n","chown",f"{PRODUCER_UID}:{PRODUCER_UID}",str(producer_private)); run("sudo","-n","chmod","0700",str(producer_private))
        run("sudo","-n","chown",f"{PUBLISHER_UID}:{PUBLISHER_UID}",str(publisher_state)); run("sudo","-n","chmod","0700",str(publisher_state))
        for path,value,uid in ((producer_private/"store",b"private-store\n",PRODUCER_UID),(producer_private/"labels",b"private-labels\n",PRODUCER_UID),
            (publisher_state/"credential",b"publisher-token\n",PUBLISHER_UID),(publisher_state/"intent",b"publisher-intent\n",PUBLISHER_UID)):
            temporary=root/(path.name+".fixture"); temporary.write_bytes(value); run("sudo","-n","mv",str(temporary),str(path)); run("sudo","-n","chown",f"{uid}:{uid}",str(path)); run("sudo","-n","chmod","0600",str(path))
        publisher_code="""import importlib.util,os,pathlib
tool=pathlib.Path(os.environ['PROOF_TOOL']); spec=importlib.util.spec_from_file_location('d',tool); d=importlib.util.module_from_spec(spec); spec.loader.exec_module(d)
root=pathlib.Path(os.environ['PROOF_ROOT']); manifest,snapshot,raw=d._load_handoff(root/'outgoing',61001,61003)
assert manifest['publicRevision']==snapshot['revision'] and raw==d.dump(snapshot)
for path in (root/'producer-private/store',root/'producer-private/labels'):
    try: path.read_bytes(); raise AssertionError('publisher read producer private input')
    except PermissionError: pass
try: (root/'outgoing/publisher-write').write_bytes(b'x'); raise AssertionError('publisher wrote outgoing')
except PermissionError: pass
assert (root/'publisher-state/credential').read_text().strip()=='publisher-token'
"""
        producer_code="""import os,pathlib
root=pathlib.Path(os.environ['PROOF_ROOT'])
for path in (root/'publisher-state/credential',root/'publisher-state/intent'):
    try: path.read_bytes(); raise AssertionError('producer read publisher state')
    except PermissionError: pass
assert (root/'producer-private/store').read_text().strip()=='private-store'
"""
        run("sudo","-n","setpriv",f"--reuid={PUBLISHER_UID}",f"--regid={PUBLISHER_UID}",f"--groups={HANDOFF_GID}","env",f"PROOF_ROOT={root}",f"PROOF_TOOL={TOOL}","python3","-c",publisher_code)
        run("sudo","-n","setpriv",f"--reuid={PRODUCER_UID}",f"--regid={PRODUCER_UID}",f"--groups={HANDOFF_GID}","env",f"PROOF_ROOT={root}","python3","-c",producer_code)
        result={"schema":"fsgg.telemetry.two-uid-handoff-proof/1","status":"pass","producerUid":PRODUCER_UID,"publisherUid":PUBLISHER_UID,
            "handoffGid":HANDOFF_GID,"candidateToolDigest":hashlib.sha256(TOOL.read_bytes()).hexdigest(),
            "proofScriptDigest":hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
            "checks":["publisher-read-handoff","publisher-write-handoff-denied","publisher-read-store-denied","publisher-read-labels-denied","producer-read-publisher-state-denied","producer-read-credential-denied"]}
        print(json.dumps(result,sort_keys=True,separators=(",",":")))
    finally:
        run("sudo","-n","rm","-rf","--",str(root))


if __name__=="__main__": main()
