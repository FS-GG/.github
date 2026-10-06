"""Synthetic offline fixtures; no provider, installed collector or native proof."""
import base64
import copy
import contextlib
import io
from unittest.mock import patch
import importlib.util
import json
import pathlib
import os
import tempfile
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('responses_source',ROOT/'tools/learn_01_responses_source.py')
source=importlib.util.module_from_spec(spec);spec.loader.exec_module(source)
def raw(value):return json.dumps(value,separators=(',',':')).encode()
def fixture():
 request={'model':'gpt-6.1-sol','instructions':'Fixed synthetic assessment instructions','input':[{'role':'user','content':'Synthetic evidence only'}],'reasoning':{'effort':'medium'},'text':{'format':{'type':'json_schema','name':'fixture','strict':True,'schema':{'type':'object'}}},'tools':[],'tool_choice':'none','parallel_tool_calls':False,'truncation':'disabled'}
 generation={**request,'max_output_tokens':1500,'store':False,'stream':False,'background':False}
 response={'id':'resp_synthetic','object':'response','model':'gpt-6.1-sol','status':'completed','created_at':1,'error':None,'incomplete_details':None,'output':[{'type':'reasoning'},{'type':'message','role':'assistant','status':'completed','content':[{'type':'output_text','text':'{}'}]}],'usage':{'input_tokens':100,'output_tokens':20,'total_tokens':120,'input_tokens_details':{'cached_tokens':10},'output_tokens_details':{'reasoning_tokens':5}}}
 ref=lambda kind:{'id':'synthetic-'+kind,'kind':kind,'revision':0,'contentDigest':'sha256:'+'a'*64}
 c={'schema':'fsgg.telemetry.responses-owned-capture/1','sourceVariant':source.VARIANT,'operationId':'a'*32,'invocationId':'synthetic-invocation','originalItemId':'synthetic-item','observedAt':'2026-10-06T00:00:00Z','dispatchRef':ref('expected-dispatch'),'claimRef':{'requestId':'synthetic-request','claimId':'synthetic-claim','revision':0,'contentDigest':'sha256:'+'a'*64,'owner':{'producer':'synthetic-queue-owner','stream':'synthetic-queue-stream'},'generation':1},'countRequestBase64':'','generationRequestBase64':'','countResponseBase64':'','generationResponseBase64':'','countStatus':200,'generationStatus':200,'countBodyComplete':True,'generationBodyComplete':True,'stage':'response-captured','elapsedMilliseconds':100,'wholeMilliseconds':60000,'networkMilliseconds':55000,'failures':[],'cleanupFailures':[]}
 for name,value in [('countRequest',request),('generationRequest',generation),('countResponse',{'object':'response.input_tokens','input_tokens':100}),('generationResponse',response)]:c[name+'Base64']=base64.b64encode(raw(value)).decode()
 s={'schema':'fsgg.telemetry.responses-verification-snapshot/1','sourceVariant':source.VARIANT,**{k:c[k] for k in ('operationId','invocationId','originalItemId','dispatchRef','claimRef')},'installedProfileSha256':'b'*64,'instructionsSha256':source.sha(request['instructions'].encode()),'responseSchemaSha256':source.sha(raw(request['text']['format']['schema'])),'responseSchemaName':'fixture','responseSchemaBase64':base64.b64encode(raw(request['text']['format']['schema'])).decode()}
 return c,s
class ReplayTests(unittest.TestCase):
 def result(self,c,s):return source.verify(raw(c),raw(s))
 def body(self,c,key,change):
  value=json.loads(base64.b64decode(c[key+'Base64']));change(value);c[key+'Base64']=base64.b64encode(raw(value)).decode()
 def test_owned_input_file_guards(self):
  with tempfile.TemporaryDirectory() as directory:
   p=pathlib.Path(directory)/'capture.json';p.write_bytes(b'{}');p.chmod(0o600);self.assertEqual(source.read_owned(p,10),b'{}')
   p.chmod(0o644)
   with self.assertRaises(source.Refusal):source.read_owned(p,10)
   p.chmod(0o600);alias=p.parent/'alias';alias.symlink_to(p)
   with self.assertRaises((source.Refusal,OSError)):source.read_owned(alias,10)
   alias.unlink();os.link(p,alias)
   with self.assertRaises(source.Refusal):source.read_owned(p,10)
 def test_malformed_stage_and_status_do_not_escape(self):
  c,s=fixture();c['stage']=[];self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();self.body(c,'generationResponse',lambda v:v.update(status={}));self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();s['claimRef']['revision']=0.0;self.assertFalse(self.result(c,s)['accepted'])
 def test_positive_and_exact_raw_hashes(self):
  c,s=fixture();r=self.result(c,s);self.assertTrue(r['accepted']);self.assertTrue(r['observationVerified']);self.assertEqual(r['captureSha256'],source.sha(raw(c)));self.assertEqual(r['profileSha256'],'b'*64);self.assertEqual(r['usageState'],'complete')
 def test_claim_owner_generation_and_response_id(self):
  for field,value in [('generation',True),('generation',0),('revision',0.0),('owner',{'producer':'p','stream':'s','extra':True})]:
   c,s=fixture();c['claimRef']=copy.deepcopy(c['claimRef']);c['claimRef'][field]=value;self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();s['claimRef']=copy.deepcopy(s['claimRef']);s['claimRef']['owner']['producer']='other';self.assertFalse(self.result(c,s)['accepted'])
  for rid in ['resp bad','resp\ninvalid','x'*257]:
   c,s=fixture();self.body(c,'generationResponse',lambda v:v.update(id=rid));self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();self.body(c,'generationResponse',lambda v:v.update(created_at=None));self.assertTrue(self.result(c,s)['accepted'])
 def test_actual_safe_failure_code_preserves_observation(self):
  c,s=fixture();c['failures']=['input_tokens-invalid'];r=self.result(c,s);self.assertFalse(r['accepted']);self.assertEqual(r['responseId'],'resp_synthetic');self.assertEqual(r['usageState'],'complete');self.assertEqual(r['errors'],['operation-failed'])
 def test_schema_and_instruction_pins(self):
  c,s=fixture();s['instructionsSha256']='c'*64;self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();s['responseSchemaSha256']='c'*64;self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();self.body(c,'generationRequest',lambda v:v['text']['format']['schema'].update(type=1));self.assertFalse(self.result(c,s)['accepted'])
  self.assertFalse(source.same(True,1));self.assertFalse(source.same(1,1.0));self.assertFalse(source.same({'a':True},{'a':1}))
 def test_original_deadline_and_cleanup(self):
  for name,value in [('elapsedMilliseconds',60000),('elapsedMilliseconds',60001),('cleanupFailures',['retirement-unknown']),('generationBodyComplete',False),('stage','generation-sent')]:
   with self.subTest(name=name,value=value):
    c,s=fixture();c[name]=value;self.assertFalse(self.result(c,s)['accepted'])
 def test_every_shared_field_mutation_refused(self):
  for name in source.SHARED:
   with self.subTest(name=name):
    c,s=fixture();self.body(c,'generationRequest',lambda v:v.update({name:None}));self.assertFalse(self.result(c,s)['accepted'])
 def test_generation_cap_and_tool_injection(self):
  for name,value in [('max_output_tokens',1501),('max_output_tokens',True),('tools',[{'type':'function'}]),('store',True),('previous_response_id','resp_prior')]:
   with self.subTest(name=name):
    c,s=fixture();self.body(c,'generationRequest',lambda v:v.update({name:value}));self.assertFalse(self.result(c,s)['accepted'])
 def test_count_invalid_never_accepted(self):
  for count in [0,8001,100.0,True,-1]:
   c,s=fixture();self.body(c,'countResponse',lambda v:v.update(input_tokens=count));self.assertFalse(self.result(c,s)['accepted'])
 def test_count_duplicate_refused(self):
  c,s=fixture();c['countResponseBase64']=base64.b64encode(b'{"object":"response.input_tokens","input_tokens":100,"input_tokens":100}').decode();self.assertFalse(self.result(c,s)['accepted'])
 def test_response_usage_limits_and_partial_preserved(self):
  for field,value in [('input_tokens',101),('output_tokens',1501),('total_tokens',119),('output_tokens',True)]:
   c,s=fixture();self.body(c,'generationResponse',lambda v:v['usage'].update({field:value}));self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();self.body(c,'generationResponse',lambda v:v['usage'].pop('output_tokens'));r=self.result(c,s);self.assertFalse(r['accepted']);self.assertEqual(r['usageState'],'partial');self.assertEqual(r['responseId'],'resp_synthetic')
 def test_breakouts_and_provider_created_time(self):
  c,s=fixture();self.body(c,'generationResponse',lambda v:v['usage']['output_tokens_details'].update(reasoning_tokens=21));self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();self.body(c,'generationResponse',lambda v:v.update(created_at=-1));self.assertFalse(self.result(c,s)['accepted'])
 def test_refusal_and_nonterminal(self):
  for change in [lambda v:v.update(status='incomplete'),lambda v:v['output'][1]['content'][0].update(type='refusal'),lambda v:v.update(tools=[{'type':'web_search'}])]:
   c,s=fixture();self.body(c,'generationResponse',change);self.assertFalse(self.result(c,s)['accepted'])
 def test_binding_identity_and_revision(self):
  c,s=fixture();s['claimRef']=copy.deepcopy(s['claimRef']);s['claimRef']['revision']=1;self.assertFalse(self.result(c,s)['accepted'])
  c,s=fixture();s['invocationId']='other';self.assertFalse(self.result(c,s)['accepted'])
 def test_unknown_capture_field_refused(self):
  c,s=fixture();c['accepted']=True;self.assertFalse(self.result(c,s)['accepted'])
 def test_failed_and_incomplete_owned_observation_retains_numeric_cost(self):
  for status,http,failures in [('failed',500,['responses-http-status-500']),('incomplete',200,['provider-not-completed','provider-incomplete-details-present'])]:
   c,s=fixture();c['generationStatus']=http;c['failures']=failures
   self.body(c,'generationResponse',lambda v:v.update(status=status,output=[],incomplete_details={'reason':'max_output_tokens'} if status=='incomplete' else None,error={'code':'server_error'} if status=='failed' else None))
   r=self.result(c,s);self.assertTrue(r['observationVerified']);self.assertFalse(r['accepted']);self.assertEqual(r['usageState'],'complete');self.assertEqual(r['responseId'],'resp_synthetic')
   observed=json.loads(base64.b64decode(c['generationResponseBase64']))
   self.assertEqual(observed['usage']['total_tokens'],120);self.assertEqual(r['responseSha256'],source.sha(raw(observed)))
 def test_nullable_cost_remains_partial_or_unknown(self):
  for usage,state,failures in [({'input_tokens':100,'output_tokens':None,'total_tokens':None},'partial',['output_tokens-unavailable','total_tokens-unavailable','usage-not-complete','inclusive-total-mismatch','inclusive-output-not-admitted']), (None,'unknown',['usage-unavailable','usage-not-complete','count-input-not-corresponding','inclusive-total-mismatch','inclusive-output-not-admitted'])]:
   c,s=fixture();c['failures']=failures;self.body(c,'generationResponse',lambda v:v.update(usage=usage))
   r=self.result(c,s);self.assertTrue(r['observationVerified']);self.assertFalse(r['accepted']);self.assertEqual(r['usageState'],state)
 def test_observation_contradictions_refuse_even_semantic_failure_codes(self):
  for field,value in [('total_tokens',119),('output_tokens',True),('input_tokens',-1)]:
   c,s=fixture();c['failures']=['provider-not-completed'];self.body(c,'generationResponse',lambda v:v.update(status='failed'));self.body(c,'generationResponse',lambda v:v['usage'].update({field:value}));self.assertFalse(self.result(c,s)['observationVerified'])
  c,s=fixture();self.body(c,'generationResponse',lambda v:v['usage']['output_tokens_details'].update(reasoning_tokens=21));self.assertFalse(self.result(c,s)['observationVerified'])
 def test_partial_inclusive_total_cannot_be_below_known_operand(self):
  for usage in [{'input_tokens':100,'output_tokens':None,'total_tokens':1},{'input_tokens':None,'output_tokens':20,'total_tokens':19}]:
   c,s=fixture();c['failures']=['provider-not-completed'];self.body(c,'generationResponse',lambda v:v.update(status='failed',usage=usage));r=self.result(c,s);self.assertFalse(r['observationVerified']);self.assertFalse(r['accepted'])
 def test_nullable_breakouts_obey_all_structurally_known_bounds(self):
  invalid=[{'input_tokens':None,'output_tokens':1,'total_tokens':1,'input_tokens_details':{'cached_tokens':10}}, {'input_tokens':1,'output_tokens':None,'total_tokens':1,'output_tokens_details':{'reasoning_tokens':10}}, {'input_tokens':None,'output_tokens':1,'total_tokens':10,'input_tokens_details':{'cached_tokens':10}}, {'input_tokens':None,'output_tokens':None,'total_tokens':None,'input_tokens_details':{'cache_write_tokens':2**63-1},'output_tokens_details':{'reasoning_tokens':1}}]
  for usage in invalid:
   c,s=fixture();c['failures']=['provider-not-completed'];self.body(c,'generationResponse',lambda v:v.update(status='failed',usage=usage));self.assertFalse(self.result(c,s)['observationVerified'])
  c,s=fixture();self.body(c,'generationResponse',lambda v:v.update(status='failed',usage={'input_tokens':None,'output_tokens':None,'total_tokens':None,'input_tokens_details':{'cached_tokens':101},'output_tokens_details':{'reasoning_tokens':1501}}));r=self.result(c,s);self.assertTrue(r['observationVerified']);self.assertFalse(r['accepted']);self.assertEqual(r['usageState'],'partial')
 def test_consistent_provider_policy_breach_preserves_actual_observation(self):
  for i,o in [(8001,20),(100,1501),(8001,1501),(101,20),(0,0)]:
   c,s=fixture();c['failures']=['count-input-not-corresponding','inclusive-output-not-admitted'];self.body(c,'generationResponse',lambda v:v.update(usage={'input_tokens':i,'output_tokens':o,'total_tokens':i+o}))
   r=self.result(c,s);self.assertTrue(r['observationVerified']);self.assertFalse(r['accepted']);self.assertEqual(r['usageState'],'complete')
   actual=json.loads(base64.b64decode(c['generationResponseBase64']))['usage'];self.assertEqual(actual['input_tokens'],i);self.assertEqual(actual['output_tokens'],o)
   if i!=100:self.assertIn('count-input-not-corresponding',r['errors'])
   if i>8000 or o>1500:self.assertIn('usage-policy-exceeded',r['errors'])
 def test_unproved_transport_and_custody_never_verify_observation(self):
  for field,value in [('failures',['responses-transport-unknown']),('failures',['responses-local-failure']),('failures',['made-up-completion']),('failures',['responses-http-status-500']),('cleanupFailures',['retirement-unknown']),('elapsedMilliseconds',60000),('countStatus',500),('generationBodyComplete',False),('stage','generation-sent'),('generationStatus',None)]:
   c,s=fixture();c[field]=value;self.assertFalse(self.result(c,s)['observationVerified'])
 def test_forged_observation_flag_and_stale_source_cannot_grant_proof(self):
  c,s=fixture();c['observationVerified']=True;r=self.result(c,s);self.assertFalse(r['observationVerified']);self.assertIn('malformed-capture',r['errors'])
  c,s=fixture();s['dispatchRef']=copy.deepcopy(s['dispatchRef']);s['dispatchRef']['contentDigest']='sha256:'+'c'*64;self.assertFalse(self.result(c,s)['observationVerified'])
 def test_cli_exit_success_is_observation_not_assessment(self):
  with tempfile.TemporaryDirectory() as directory:
   capture=pathlib.Path(directory)/'capture.json';snapshot=pathlib.Path(directory)/'snapshot.json'
   for failure,expected in [(['provider-not-completed'],0),(['responses-transport-unknown'],1)]:
    c,s=fixture();c['failures']=failure;self.body(c,'generationResponse',lambda v:v.update(status='failed',output=[]))
    capture.write_bytes(raw(c));snapshot.write_bytes(raw(s));capture.chmod(0o600);snapshot.chmod(0o600)
    output=io.StringIO()
    with patch('sys.argv',['verifier','verify-responses','--capture',str(capture),'--telemetry-snapshot',str(snapshot)]),contextlib.redirect_stdout(output):
     self.assertEqual(source.main(),expected)
    self.assertFalse(json.loads(output.getvalue())['accepted'])
 def test_independent_failures_collected(self):
  c,s=fixture();self.body(c,'generationRequest',lambda v:v.update(max_output_tokens=1501));self.body(c,'countResponse',lambda v:v.update(input_tokens=8001));c['elapsedMilliseconds']=60001;r=self.result(c,s);self.assertEqual(set(r['errors']),{'request-policy-mismatch','count-not-admitted','usage-not-complete','count-input-not-corresponding','deadline-exhausted'})
if __name__=='__main__':unittest.main()
