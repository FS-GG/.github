import hashlib,json,os,subprocess,tempfile,unittest
from pathlib import Path
DLL=os.environ.get("HOST_ATTEMPT_PRODUCER_DLL")
REPO="FS-GG/FS.GG.GitHub.Substrate.Sandbox";WORKFLOW="v2-host-native-private.yml";REF="qualification/v2-host-native-20260930";ENV="v2-host-01-8-native-private"
def base():
 return {"schema":"fsgg.telemetry.host-attempt-state/2","phase":"prepared","sourceGeneration":1,"placementSha":"a"*40,"placementTree":"b"*40,"workflowSha256":"c"*64,"recipeSourceSha":"8ad0da67004d670c6803f34755dfe759a7fc84e7","recipeSourceTree":"df72335c0ddd892d23ee5d573c10b737245845eb","profileSha256":"d"*64,"operationId":"v2-host-01.8a-native-collaboration-v1","bindingSha256":"e"*64,"bindingProducerSha256":"f"*64,"sourcePinsSha256":"1"*64,"producerSha256":"2"*64,"runtimeHostSha256":"3"*64,"mechanismAdapterSha256":"4"*64,"nonce":"attempt-o-0001","destinationId":"destination-o-0001","currentSourceGeneration":1,"currentBindingSha256":"e"*64,"elapsedSeconds":0,"budgetSeconds":2700,"dispatchIntended":False,"dispatchMayHaveEffect":False,"dispatchAcknowledged":False,"effectCheckFresh":False,"secretIntentions":[],"secretAcknowledgments":[],"secretsMayHaveEffect":[],"secretAbsenceObserved":[],"candidateRuns":[],"ownedRunId":None,"cancellationMayHaveEffect":False,"runRetirementObserved":False,"nativeDisposition":"unknown","cleanupDisposition":"unknown","refusal":None}
def observation(kind,**fields):return {"schema":"fsgg.telemetry.host-attempt-observation/1","kind":kind,**fields}
def action(kind,name,argument=None):return observation(kind,action={"name":name,"argument":argument})
def check(state,**change):
 values={"sourceGeneration":1,"bindingSha256":"e"*64,"profileSha256":"d"*64,"workflowSha256":"c"*64,"bindingProducerSha256":"f"*64,"runtimeHostSha256":"3"*64,"mechanismAdapterSha256":"4"*64};values.update(change);return observation("effect-check-observed",**values)
def listing(state,candidates):return observation("runs-observed",repository=REPO,workflow=WORKFLOW,qualificationRef=REF,placementSha=state["placementSha"],priorRunIds=["prior-run"],candidateRunIds=candidates,listingComplete=True)
def owned(state):return observation("owned-run-observed",repository=REPO,workflow=WORKFLOW,qualificationRef=REF,placementSha=state["placementSha"],nonce=state["nonce"],runId="run-1",rootOwnershipObserved=True)
def native(state):return observation("native-evidence-observed",runId="run-1",repository=REPO,workflow=WORKFLOW,qualificationRef=REF,placementSha=state["placementSha"],nonce=state["nonce"],profileSha256=state["profileSha256"],operationId=state["operationId"],bindingSha256=state["bindingSha256"],evidenceComplete=True,nonceJoined=True,profileVerified=True,activityComplete=True,completionCommunicated=True,childAck=True,parentAck=True,writerJoinsHealthy=True)
def absent(role):return observation("secret-absence-observed",role=role,repository=REPO,environment=ENV,httpStatus=200,listingComplete=True,secretPresent=False)
def step(state,obs):
 with tempfile.TemporaryDirectory() as directory:
  root=Path(directory);s=root/'s.json';o=root/'o.json';so=root/'out.json';ao=root/'actions.json';s.write_text(json.dumps(state));o.write_text(json.dumps(obs))
  result=subprocess.run(['/usr/bin/dotnet',DLL,'next','--state',str(s),'--observation',str(o),'--state-output',str(so),'--actions-output',str(ao)],text=True,capture_output=True,timeout=10)
  if result.returncode:raise AssertionError(result.stderr)
  return json.loads(so.read_text()),json.loads(ao.read_text())['actions']
def place():
 s,_=step(base(),observation("placement-observed"));return s
def secrets():
 s=place()
 for role in ["native-auth","effect-admission"]:
  s,_=step(s,check(s));s,_=step(s,action("effect-requested","transfer-secret",role));s,_=step(s,action("effect-acknowledged","transfer-secret",role))
 return s
def dispatch():
 s=secrets();s,_=step(s,check(s));s,_=step(s,action("effect-requested","dispatch-once"));s,_=step(s,action("effect-acknowledged","dispatch-once"));return s
class CliControls(unittest.TestCase):
 def test_first_refusal_stays_sticky_through_late_ack_and_dispatch(self):
  s=place();s,_=step(s,check(s));s,_=step(s,action("effect-requested","transfer-secret","native-auth"));s,_=step(s,action("effect-acknowledged","transfer-secret","native-auth"));s,_=step(s,action("effect-acknowledged","transfer-secret","native-auth"));first=s["refusal"]
  s,_=step(s,action("effect-acknowledged","transfer-secret","effect-admission"));s,actions=step(s,action("effect-requested","dispatch-once"))
  self.assertEqual(first,s["refusal"]);self.assertFalse(s["dispatchIntended"]);self.assertNotIn("dispatch-once",[x["name"] for x in actions])
 def test_admission_before_auth_and_stale_check_refuse_without_effect(self):
  s=place();s,_=step(s,check(s));s,actions=step(s,action("effect-requested","transfer-secret","effect-admission"));self.assertIsNotNone(s["refusal"]);self.assertEqual([],actions)
  s=place();s,_=step(s,check(s,bindingSha256="0"*64));s,actions=step(s,action("effect-requested","transfer-secret","native-auth"));self.assertIsNotNone(s["refusal"]);self.assertFalse(any(x["name"]=="transfer-secret" for x in actions))
 def test_listing_is_not_ownership_and_cannot_cancel(self):
  s=dispatch();s,_=step(s,listing(s,["run-1"]));self.assertIsNone(s["ownedRunId"]);s,actions=step(s,action("effect-requested","cancel-owned-run","run-1"));self.assertIsNotNone(s["refusal"]);self.assertFalse(any(x["name"]=="cancel-owned-run" for x in actions))
 def test_cancel_and_deadline_cannot_be_overwritten_by_native_evidence(self):
  s=dispatch();s,_=step(s,listing(s,["run-1"]));s,_=step(s,owned(s));s,_=step(s,check(s));s,_=step(s,action("effect-requested","cancel-owned-run","run-1"));s,_=step(s,native(s));self.assertEqual("unknown",s["nativeDisposition"])
  s=dispatch();s,_=step(s,listing(s,["run-1"]));s,_=step(s,owned(s));s,_=step(s,observation("time-advanced",seconds=2700));s,_=step(s,native(s));self.assertEqual("unknown",s["nativeDisposition"])
 def test_secret_absence_does_not_retire_unknown_run(self):
  s=secrets();s,_=step(s,check(s));s,_=step(s,action("effect-requested","dispatch-once"));s,_=step(s,action("effect-response-lost","dispatch-once"))
  for role in ["native-auth","effect-admission"]:s,_=step(s,absent(role))
  self.assertEqual("secrets-absent",s["cleanupDisposition"]);self.assertEqual("cleanup-pending",s["phase"]);self.assertFalse(s["runRetirementObserved"])
if __name__=='__main__':unittest.main()
