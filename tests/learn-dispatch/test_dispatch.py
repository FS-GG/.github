import importlib.util
import unittest
import tempfile
import json
import subprocess
from types import SimpleNamespace
from unittest.mock import patch
from pathlib import Path
s=importlib.util.spec_from_file_location('dispatch',Path(__file__).parents[2]/'scripts/learn-dispatch.py')
m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
class DispatchTests(unittest.TestCase):
 def snapshot(self,body,**kwargs):
  return dict(itemId='example/repo#1',body=body,capturedAt='2026-01-01T00:00:00Z',sourceRevision='fixture:original',dispatchBoundary='fixture:before',producer='fixture',**kwargs)
 def test_safety_floor_refuses_override(self):
  with self.assertRaisesRegex(ValueError,'floor'):
   m.classify(self.snapshot('upgrade',chosenProfile=dict(tier='normal',criticTier='normal',effort='medium',reviewDepth='single-source-review'),overrideReason='cheaper'))
 def test_unknown_never_routes_low(self):
  r=m.classify(self.snapshot('edit prose',unknownDimensions=['D4']));self.assertIsNone(r['hardness']);self.assertEqual('best',r['chosenProfile']['tier'])
 def test_determinism_and_input_binding(self):
  a=m.classify(self.snapshot('add deterministic algorithm'));b=m.classify(self.snapshot('add deterministic algorithm'))
  self.assertEqual(m.canonical(a),m.canonical(b));self.assertNotEqual(a['inputDigest'],m.classify(self.snapshot('other'))['inputDigest'])
 def test_no_fictional_exploration(self):
  with self.assertRaisesRegex(ValueError,'not enabled'):m.classify(self.snapshot('test',propensity=.5))
 def test_high_invariant_and_low_routes(self):
  self.assertEqual('best',m.classify(self.snapshot('atomic state machine'))['chosenProfile']['tier'])
  self.assertEqual('normal',m.classify(self.snapshot('correct spelling'))['chosenProfile']['tier'])
 def test_override_requires_reason(self):
  with self.assertRaisesRegex(ValueError,'overrideReason'):m.classify(self.snapshot('spelling',chosenProfile=dict(tier='best',criticTier='best',effort='high',reviewDepth='source-and-delta')))
 def packet(self,path):
  s=self.snapshot('upgrade',runtime='fixture',model='fixture-best')
  m.prepare(s,path)
  return s
 def test_prepare_binds_comment_and_observation(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';self.packet(out)
   r=json.loads((out/'snapshot.json').read_text());b=json.loads((out/'observation.json').read_text())
   self.assertEqual(__import__('hashlib').sha256(m.canonical(r)).hexdigest(),b['events'][0]['snapshotDigest'])
   self.assertEqual(r['itemId'],b['events'][0]['itemId'])
   with self.assertRaises(FileExistsError):self.packet(out)
 def test_record_retry_avoids_duplicate_comment(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';s=self.packet(out)
   args=SimpleNamespace(engine='fixture-engine',config='fixture',producer='fixture',binding_digest='fixture')
   replies=[json.dumps({'body':s['body']}),json.dumps([[{'body':(out/'comment.md').read_text()}]]),'{}']
   with patch.object(m.subprocess,'check_output',side_effect=replies) as call,patch.object(m.subprocess,'run') as run:
    m.record(out,args);run.assert_not_called();self.assertEqual(3,call.call_count)
 def test_record_refuses_changed_body_before_effects(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';self.packet(out)
   with patch.object(m.subprocess,'check_output',return_value=json.dumps({'body':'changed'})) as call,patch.object(m.subprocess,'run') as run:
    with self.assertRaisesRegex(ValueError,'changed'):m.record(out,None)
    run.assert_not_called();self.assertEqual(1,call.call_count)
 def test_partial_telemetry_failure_preserves_intent(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';s=self.packet(out);original=(out/'observation.json').read_bytes()
   args=SimpleNamespace(engine='fixture-engine',config='fixture',producer='fixture',binding_digest='fixture')
   replies=[json.dumps({'body':s['body']}),'[]',subprocess.CalledProcessError(2,['fixture-engine'])]
   with patch.object(m.subprocess,'check_output',side_effect=replies),patch.object(m.subprocess,'run') as run:
    with self.assertRaises(subprocess.CalledProcessError):m.record(out,args)
    self.assertEqual(1,run.call_count);self.assertEqual(original,(out/'observation.json').read_bytes())
class CalibrationTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  spec=importlib.util.spec_from_file_location('calibration',Path(__file__).parents[2]/'scripts/learn-calibration.py')
  cls.mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(cls.mod)
 def test_missing_does_not_become_zero(self):
  r=self.mod.aggregate({'deliveries':[dict(hardness=0,observedTokens=None,reviewRounds=[],blockingFindings=None)]})
  self.assertEqual(0,r['tokenJoined']);self.assertIsNone(r['logTokenPearson']);self.assertEqual(0,r['knownBlockingCounts']);self.assertIsNone(r['blockingPearson'])
 def test_known_inverse_and_dependent_prs(self):
  rows=[dict(hardness=i,observedTokens=2**(4-i),reviewRounds=[i+1,i+1],blockingFindings=3-i) for i in range(3)]
  r=self.mod.aggregate({'deliveries':rows})
  self.assertAlmostEqual(-1,r['logTokenPearson']);self.assertAlmostEqual(1,r['reviewPearson']);self.assertEqual(3,r['uniqueDeliveries']);self.assertEqual(6,r['reviewExamples']);self.assertEqual(12,r['reviewRoundSum'])
if __name__=='__main__':unittest.main()
