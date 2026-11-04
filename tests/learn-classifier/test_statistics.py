import importlib.util,unittest,json
from pathlib import Path
spec=importlib.util.spec_from_file_location('statistics_pilot',Path(__file__).resolve().parents[2]/'scripts/learn-classifier-statistics.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class StatisticsTests(unittest.TestCase):
 def test_content_only_retains_overlength_failure_without_mutation(self):
  from test_pilot import fixture,m as frozen
  packet,value=fixture();value['work_volume']['basis']='x'*181;before=json.dumps(value,sort_keys=True)
  with self.assertRaises(ValueError):frozen.validate(value,packet)
  self.assertTrue(m.validate_content_fields(value,packet));self.assertEqual(json.dumps(value,sort_keys=True),before)
 def test_content_only_rejects_wrong_item_quote_and_time(self):
  from test_pilot import fixture
  for kind in ['item','quote','time']:
   packet,value=fixture()
   if kind=='item':value['item_id']='other'
   elif kind=='quote':value['demand']['evidence'][0]['quote']='invented'
   else:value['worked_time']['p50_minutes']=-1
   with self.assertRaises(ValueError):m.validate_content_fields(value,packet)

 def test_nominal_perfect_agreement(self):
  r=m.agreement(['a','b','a'],['a','b','a'],['a','b']);self.assertEqual(r['exactAgreement'],1);self.assertEqual(r['unweightedKappa'],1);self.assertEqual(r['linearWeightedKappa'],1)
 def test_constant_kappa_unknown(self):
  self.assertIsNone(m.agreement([0,0],[0,0],[0,1])['unweightedKappa'])
 def test_order_reversal_rank(self):
  self.assertAlmostEqual(m.coefficient([0,1,2],[2,1,0],'spearman'),-1)
 def test_research_nonordered_sensitivity(self):
  r={'demand':{'category':'research'}};self.assertEqual(m.ordinal(r),3);self.assertIsNone(m.ordinal(r,True))
 def test_unknown_not_zero(self):self.assertIsNone(m.ordinal({'demand':{'category':'unknown'}}))
 def test_cluster_bootstrap_does_not_invent_groups(self):
  r=m.cluster_interval([1]*100,['one']*100);self.assertIsNone(r['interval95']);self.assertEqual(r['clusters'],1)
 def test_cluster_bootstrap_reproducible(self):
  a=m.cluster_interval([0,1,2,3],['a','b','c','d']);b=m.cluster_interval([0,1,2,3],['a','b','c','d']);self.assertEqual(a,b)
 def test_profile_shift_detected(self):
  ratings={f'item-{i}':{p:{'demand':{'category':'complex' if p.endswith('high') else 'bounded'}} for p in m.PROFILES} for i in range(8)}
  c=m.primary_contrasts(ratings,{i:i for i in ratings});self.assertEqual(c['sol_high_minus_medium']['mean'],1);self.assertFalse(c['sol_high_minus_medium']['conditionalObservedMeanInsideProposedMargin']);self.assertEqual(c['astra_minus_sol_averaged_effort']['mean'],0)
 def test_not_planned_not_completion(self):
  mapping=[{'itemId':'trial-001','historicalItem':'hidden','parent':None,'state':'closed','stateReason':'not_planned','createdAt':'2026-01-01T00:00:00Z','closedAt':'2026-01-01T00:01:00Z'}]
  r=m.historical_rows(mapping,[])['trial-001'];self.assertEqual(r['calendarClosedSeconds'],60);self.assertIsNone(r['calendarCompletedSeconds'])
 def test_training_scaling_no_test_leakage(self):
  train=[{'x':0,'y':1},{'x':1,'y':2},{'x':2,'y':3}];a=m.ridge(train,[{'x':3}],['x'],'y')[0];b=m.ridge(train,[{'x':3},{'x':100000}],['x'],'y')[0];self.assertEqual(a,b)
 def test_unobserved_cells_widen_sensitivity(self):
  ratings={'a':{p:{'demand':{'category':'complex'}} for p in m.PROFILES}};r=m.primary_contrasts(ratings,{'a':'one','b':'two'})['sol_high_minus_medium'];self.assertEqual(r['hypotheticalAllItemBoundIfUnobservedCategoriesLieInZeroToThree'],[-1.5,1.5])
 def test_actual_launch_mismatch_requires_deviation_and_is_excluded(self):
  class C:
   @staticmethod
   def canonical(x):return json.dumps(x,sort_keys=True,separators=(',',':')).encode()
  request={'model':'M','reasoning_effort':'high','fork_turns':'none','message':'right'};actual=dict(request,message='wrong');row={'requestedModel':'M','requestedEffort':'high','taskName':'job'}
  ledger=[{'event':'reserved','request':request,'requestSha256':m.digest(C.canonical(request))},{'event':'accepted_launch','acceptedRequest':actual,'canonicalAgent':'/root/job'},{'event':'collected','canonicalAgent':'/root/job'}]
  with self.assertRaisesRegex(ValueError,'unrecorded'):m.audit_launch(row,ledger,C)
  ledger[1]['protocolDeviation']={'kind':'wrong_packet'};self.assertTrue(m.audit_launch(row,ledger,C))
  ledger[1]['acceptedRequest']=request;ledger[1].pop('protocolDeviation');self.assertFalse(m.audit_launch(row,ledger,C))
if __name__=='__main__':unittest.main()
