import importlib.util
import unittest
import tempfile
import json
import subprocess
import sqlite3
from contextlib import closing
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
 def args(self,root):
  return SimpleNamespace(engine='fixture-engine',config='fixture',producer='fixture',binding_digest='fixture',state_root=Path(root)/'shared-custody')
 def io(self,out,snapshot):
  comment=dict(id=17,body=(out/'comment.md').read_text(),html_url='https://example.invalid/comment/17')
  return comment
 def test_record_retry_avoids_duplicate_comment_and_submit(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';snap=self.packet(out);comment=self.io(out,snap);args=self.args(d)
   with patch.object(m,'read_issue',return_value={'body':snap['body']}),patch.object(m,'read_comments',return_value=[comment]),patch.object(m,'read_comment',return_value=comment),patch.object(m,'post_comment') as post,patch.object(m,'submit_telemetry',return_value='fixture receipt') as submit:
    m.record(out,args);m.record(out,args);post.assert_not_called();self.assertEqual(1,submit.call_count)
 def test_record_refuses_changed_body_before_effects(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';self.packet(out)
   with patch.object(m,'read_issue',return_value={'body':'changed'}),patch.object(m,'post_comment') as post,patch.object(m,'submit_telemetry') as submit:
    with self.assertRaisesRegex(ValueError,'changed'):m.record(out,self.args(d))
    post.assert_not_called();submit.assert_not_called()
 def test_uncertain_telemetry_never_resubmits(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';snap=self.packet(out);comment=self.io(out,snap);args=self.args(d)
   with patch.object(m,'read_issue',return_value={'body':snap['body']}),patch.object(m,'read_comments',return_value=[comment]),patch.object(m,'read_comment',return_value=comment),patch.object(m,'submit_telemetry',side_effect=subprocess.CalledProcessError(2,['fixture'])) as submit:
    with self.assertRaises(subprocess.CalledProcessError):m.record(out,args)
    with self.assertRaisesRegex(ValueError,'telemetry possible-send'):m.record(out,args)
    self.assertEqual(1,submit.call_count)
 def test_uncertain_comment_never_reposts_and_late_readback_recovers(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';snap=self.packet(out);comment=self.io(out,snap);args=self.args(d)
   with patch.object(m,'read_issue',return_value={'body':snap['body']}),patch.object(m,'read_comments',return_value=[]) as census,patch.object(m,'post_comment',side_effect=subprocess.CalledProcessError(2,['fixture'])) as post,patch.object(m,'read_comment',return_value=comment),patch.object(m,'submit_telemetry',return_value='receipt') as submit:
    with self.assertRaises(subprocess.CalledProcessError):m.record(out,args)
    with self.assertRaisesRegex(ValueError,'no automatic repost'):m.record(out,args)
    self.assertEqual(1,post.call_count);submit.assert_not_called()
    census.return_value=[comment];m.record(out,args);self.assertEqual(1,post.call_count);self.assertEqual(1,submit.call_count)
 def test_shared_lock_serializes_different_intent_directories(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'one';other=Path(d)/'two';snap=self.packet(out);self.packet(other);comment=self.io(out,snap);args=self.args(d)
   def concurrent(*unused):
    with self.assertRaisesRegex(ValueError,'lock is held'):m.record(other,args)
    return comment
   with patch.object(m,'read_issue',return_value={'body':snap['body']}),patch.object(m,'read_comments',return_value=[]),patch.object(m,'post_comment',side_effect=concurrent) as post,patch.object(m,'read_comment',return_value=comment),patch.object(m,'submit_telemetry',return_value='receipt') as submit:
    m.record(out,args);self.assertEqual(1,post.call_count);self.assertEqual(1,submit.call_count)
 def test_full_batch_tampering_refused_before_any_native_read(self):
  mutations=[lambda b:b.update(ingestId='foreign'),lambda b:b.update(sourceIdentity='foreign'),lambda b:b.update(eventCount=2),lambda b:b['events'].append(dict(b['events'][0])),lambda b:b['events'][0].update(identity='foreign'),lambda b:b['events'][0].update(kind='runtime-start')]
  for mutate in mutations:
   with self.subTest(mutation=mutate),tempfile.TemporaryDirectory() as d:
    out=Path(d)/'intent';self.packet(out);b=json.loads((out/'observation.json').read_text());mutate(b);(out/'observation.json').write_bytes(m.canonical(b))
    with patch.object(m,'read_issue') as read:
     with self.assertRaisesRegex(ValueError,'exact full batch'):m.record(out,self.args(d))
     read.assert_not_called()
 def test_native_readback_mismatch_prevents_telemetry(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';snap=self.packet(out);comment=self.io(out,snap)
   with patch.object(m,'read_issue',return_value={'body':snap['body']}),patch.object(m,'read_comments',return_value=[]),patch.object(m,'post_comment',return_value=comment),patch.object(m,'read_comment',return_value=dict(id=17,body='different')),patch.object(m,'submit_telemetry') as submit:
    with self.assertRaisesRegex(ValueError,'readback mismatch'):m.record(out,self.args(d))
    submit.assert_not_called()
 def test_floor_preserves_delta_review(self):
  with self.assertRaisesRegex(ValueError,'floor'):
   m.classify(self.snapshot('upgrade',chosenProfile=dict(tier='best',criticTier='best',effort='high',reviewDepth='single-source-review'),overrideReason='less review'))
 def test_producer_mismatch_has_no_io(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'intent';self.packet(out)
   with patch.object(m.subprocess,'check_output') as call:
    with self.assertRaisesRegex(ValueError,'producer mismatch'):m.record(out,SimpleNamespace(producer='other'))
    call.assert_not_called()
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
class AttributionJoinTests(unittest.TestCase):
 def fixture(self):
  c=sqlite3.connect(':memory:')
  c.executescript("CREATE TABLE expected_dispatches(item_id,dispatch_id,runtime); CREATE TABLE invocation_lineage(item_id,dispatch_id,invocation_id); CREATE TABLE current_ingest_facts(identity,kind,canonical);")
  c.execute('INSERT INTO expected_dispatches VALUES(?,?,?)',('FS-GG/example#1','dispatch','claude-code-subagent'))
  c.execute('INSERT INTO invocation_lineage VALUES(?,?,?)',('FS-GG/example#1','dispatch','invocation'))
  c.execute('INSERT INTO current_ingest_facts VALUES(?,?,?)',('usage','provider-usage-observation/1',json.dumps(dict(threadId='invocation',total=100))))
  return c
 def query(self,c):
  sql=(Path(__file__).parents[2]/'scripts/learn-calibration-join.sql').read_text()
  return json.loads(c.execute(sql).fetchone()[0])
 def test_duplicate_lineage_does_not_multiply_usage(self):
  with closing(self.fixture()) as c:
   c.execute('INSERT INTO invocation_lineage SELECT * FROM invocation_lineage')
   r=self.query(c);self.assertEqual(2,r['lineageRows']);self.assertEqual(1,r['distinctAssignments']);self.assertEqual(0,r['ambiguousInvocations']);self.assertEqual(100,r['rows'][0]['total']);self.assertEqual(1,r['rows'][0]['responses'])
 def test_conflicting_invocation_refuses_aggregate(self):
  with closing(self.fixture()) as c:
   c.execute('INSERT INTO expected_dispatches VALUES(?,?,?)',('FS-GG/example#2','other','claude-code-subagent'))
   c.execute('INSERT INTO invocation_lineage VALUES(?,?,?)',('FS-GG/example#2','other','invocation'))
   r=self.query(c);self.assertEqual(1,r['ambiguousInvocations']);self.assertIsNone(r['rows'])
if __name__=='__main__':unittest.main()
