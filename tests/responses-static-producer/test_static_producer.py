"""Synthetic pure codec controls, never installed/static qualification receipts."""
import base64
import copy
import importlib.util
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('static_producer',ROOT/'tools/process-efficiency-responses-static.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
b64=lambda value:base64.b64encode(value).decode()

def fixture():
 names={'host':'FS.GG.Telemetry.Host.dll','client':'FS.GG.Telemetry.Client.dll','core':'FS.GG.Coord.Core.dll','store':'FS.GG.Telemetry.Store.dll'}
 files=sorted([{'path':'/installed/'+name,'bytes':1,'sha256':'a'*64,'components':[role]} for role,name in names.items()],key=lambda x:x['path'])
 p={'schema':'fsgg.telemetry.responses-capability-profile/1','sourceVariant':'openai-responses/1','provider':'openai','model':'gpt-6.1-sol','effort':'medium','countEndpoint':'https://api.openai.com/v1/responses/input_tokens','generationEndpoint':'https://api.openai.com/v1/responses','inputTokenLimit':8000,'outputTokenLimit':1500,'wholeMilliseconds':60000,'networkMilliseconds':55000,'requestPolicySha256':m.POLICY,'instructionsSha256':'b'*64,'responseSchemaSha256':'c'*64,'responseSchemaName':'fixture','verifierModuleSha256':m.VERIFIER,'verifierRuntimeManifestSha256':'e'*64,'installedRoots':['/installed'],'installedFiles':files}
 raw=m.encode(p); file_raw=m.property_bytes(raw,'installedFiles'); attempt='synthetic_static_attempt'
 kinds={n:('trx+metadata' if n in ('current-installed-closure','credential-role-separation') else 'trx' if n in m.METHODS else 'verifier') for n in m.SCENARIOS}
 selection={'schema':'fsgg.telemetry.responses-static-selection/1','attemptId':attempt,'profileSha256':m.sha(raw),'installedFilesSha256':m.sha(file_raw),'sourceHead':'f'*40,'consumerSha256':'1'*64,'scenarioKinds':kinds,'originalWholeMilliseconds':60000}
 terminal={'qualified':True,'ownedCustodyClean':True,'resourceFailed':False,'storageFailed':False,'actualRuntimeEvidenceFailed':False,'cleanupFailure':None,'stopCause':None,'attemptId':attempt,'sourceHead':'f'*40,'originalWholeMilliseconds':60000,'operationSpecificCaptureProduced':False,'elapsedSeconds':12.5,'installedFilesBeforeSha256':m.sha(file_raw),'installedFilesAfterSha256':m.sha(file_raw)}
 receipts={}
 for name in m.SCENARIOS:
  metadata=None
  if name in m.METHODS:
   cls,method,count=m.METHODS[name]; cases=[(cls,method,str(i)) for i in range(count)]
   if name=='request-policy-caps':cases.append((cls,'count boundary and exact retained byte joins are accepted purely','boundary'))
   artifact=trx(cases)
   if kinds[name]=='trx+metadata':
    checks=['physicalInventoryExact','immutableFilesVerified','assemblyNamesVerified','loadedLocationsVerified'] if name=='current-installed-closure' else ['distinctReferences','providerNotIngestionFile','positiveRoles','aliasRefused']
    metadata=b64(m.encode({'schema':'fsgg.telemetry.responses-static-metadata-evidence/1','attemptId':attempt,'scenario':name,'profileSha256':m.sha(raw),'installedFilesSha256':m.sha(file_raw),'loadedComponents':{role:row['path'] for row in files for role in row['components']},'managedInstalledFilesBase64':b64(file_raw),'checks':{key:True for key in checks}}))
  else:
   capture=m.encode({'invocationId':'synthetic-invocation','originalItemId':'synthetic-item'});snapshot=m.encode({'installedProfileSha256':m.sha(raw)})
   accepted=name=='verifier-valid-capture';code='request-policy-mismatch' if name=='verifier-mutated-capture-refused' else 'binding-mismatch'
   result=m.encode({'schema':'fsgg.telemetry.responses-verification/1','captureSha256':m.sha(capture),'snapshotSha256':m.sha(snapshot),'profileSha256':m.sha(raw),'accepted':accepted,'observationVerified':accepted,'errors':[] if accepted else [code]})
   artifact=m.encode({'schema':'fsgg.telemetry.responses-static-verifier-evidence/1','attemptId':attempt,'scenario':name,'profileSha256':m.sha(raw),'verifierModuleSha256':m.VERIFIER,'captureBase64':b64(capture),'snapshotBase64':b64(snapshot),'resultBase64':b64(result)})
  receipts[name]=m.encode({'schema':'fsgg.telemetry.responses-static-scenario-receipt/1','attemptId':attempt,'scenario':name,'profileSha256':m.sha(raw),'consumerSha256':'1'*64,'artifactBase64':b64(artifact),'metadataBase64':metadata})
 return raw,file_raw,selection,terminal,receipts

def trx(cases):
 ns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010';tag=lambda n:'{'+ns+'}'+n
 root=ET.Element(tag('TestRun')); defs=ET.SubElement(root,tag('TestDefinitions'));results=ET.SubElement(root,tag('Results'))
 for i,(cls,method,label) in enumerate(cases):
  identity=str(i);execution='execution-'+str(i);unit=ET.SubElement(defs,tag('UnitTest'),id=identity)
  ET.SubElement(unit,tag('TestMethod'),className=cls,name=method);ET.SubElement(unit,tag('Execution'),id=execution)
  ET.SubElement(results,tag('UnitTestResult'),testId=identity,executionId=execution,testName=cls+'.'+method+'('+label+')',outcome='Passed')
 summary=ET.SubElement(root,tag('ResultSummary'));ET.SubElement(summary,tag('Counters'),total=str(len(cases)),executed=str(len(cases)),passed=str(len(cases)),failed='0')
 return ET.tostring(root)

def metadata(profile_raw,files_raw,scenario="current-installed-closure"):
 profile=json.loads(profile_raw)
 checks=("physicalInventoryExact","immutableFilesVerified","assemblyNamesVerified","loadedLocationsVerified") if scenario=="current-installed-closure" else ("distinctReferences","providerNotIngestionFile","positiveRoles","aliasRefused")
 return m.encode({'schema':'fsgg.telemetry.responses-static-metadata-evidence/1','attemptId':'synthetic_static_attempt','scenario':scenario,'profileSha256':m.sha(profile_raw),'installedFilesSha256':m.sha(files_raw),'loadedComponents':{role:row['path']for row in profile['installedFiles']for role in row['components']},'managedInstalledFilesBase64':b64(files_raw),'checks':{key:True for key in checks}})

def full_inventory():
 profile=json.loads(fixture()[0]);prefix='/installed/static-qualification/prospective-product/h'
 files=[{**row,'path':row['path'].replace('/installed',prefix)}for row in profile['installedFiles']]
 files += [{'path':prefix+('/dependency-%03d-ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.dll'%i),'bytes':1,'sha256':'a'*64,'components':[]}for i in range(281)]
 files.sort(key=lambda row:row['path']);fragment=m.encode(files)
 raw=m.build_profile('b'*64,'c'*64,'fixture',m.VERIFIER,'e'*64,[prefix],fragment)
 return raw,fragment

class PureProducerTests(unittest.TestCase):
 def run_fixture(self,parts):
  p,f,s,t,r=parts;return m.assemble(p,f,m.encode(s),m.encode(t),r)
 def test_closed_nine_result_from_matching_synthetic_receipts(self):
  p,f,s,t,r=fixture();result=json.loads(self.run_fixture((p,f,s,t,r)))
  self.assertEqual([x['name'] for x in result['scenarioResults']],list(m.SCENARIOS));self.assertEqual(len(result),12)
  self.assertEqual(result['installedFilesSha256'],m.sha(f));self.assertFalse(result['operationSpecificCaptureProduced'])
  for row in result['scenarioResults']:self.assertEqual(row['evidenceSha256'],m.sha(r[row['name']]))
 def test_managed_fragment_is_retained_not_python_reencoded(self):
  p,f,s,t,r=fixture();profile=json.loads(p)
  profile['installedRoots']=['/A&BÅ'];profile['installedFiles']=[{**row,'path':row['path'].replace('/installed','/A&BÅ')} for row in profile['installedFiles']]
  managed=m.encode(profile).replace(b'&',b'\\u0026').replace(b'\\u00c5',b'\\u00C5')
  files=m.property_bytes(managed,'installedFiles');_,actual=m.profile_artifact(managed,files)
  self.assertEqual(actual,m.sha(files));self.assertNotEqual(files,m.encode(json.loads(files)))
  with self.assertRaises(m.Refusal):m.profile_artifact(managed,m.encode(json.loads(files)))
 def test_profile_builder_keeps_managed_inventory_fragment(self):
  p,f,*_=fixture();profile=json.loads(p)
  raw=m.build_profile('b'*64,'c'*64,'fixture',m.VERIFIER,'e'*64,profile['installedRoots'],f)
  self.assertEqual(json.loads(raw),profile);self.assertEqual(m.property_bytes(raw,'installedFiles'),f)
  with self.assertRaises(m.Refusal):m.build_profile('b'*64,'c'*64,'fixture',m.VERIFIER,'e'*64,['/other'],f)
 def test_original_deadline_and_rounded_boundary(self):
  for elapsed,whole in [(60,60000),(59.9999,60000),(0,60001),(float('inf'),60000),(-1,60000)]:
   p,f,s,t,r=fixture();s['originalWholeMilliseconds']=whole;t['originalWholeMilliseconds']=whole;t['elapsedSeconds']=elapsed
   with self.subTest(elapsed=elapsed),self.assertRaises((m.Refusal,ValueError)):self.run_fixture((p,f,s,t,r))
 def test_old_long_or_foreign_attempt_cannot_be_relabelled(self):
  for field,value in [('originalWholeMilliseconds',1080000),('attemptId','old'),('sourceHead','2'*40),('operationSpecificCaptureProduced',True)]:
   p,f,s,t,r=fixture();t[field]=value
   with self.subTest(field=field),self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_every_custody_failure_refuses(self):
  for field,value in [('qualified',False),('ownedCustodyClean',False),('resourceFailed',True),('storageFailed',True),('actualRuntimeEvidenceFailed',True),('cleanupFailure','unknown'),('stopCause','timeout')]:
   p,f,s,t,r=fixture();t[field]=value
   with self.subTest(field=field),self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_unobserved_or_changed_final_closure_refuses(self):
  for key in ('cleanupFailure','installedFilesAfterSha256'):
   p,f,s,t,r=fixture();t.pop(key)
   with self.subTest(key=key),self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
  p,f,s,t,r=fixture();t['installedFilesAfterSha256']='2'*64
  with self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_missing_or_extra_scenario_refuses(self):
  for extra in (False,True):
   p,f,s,t,r=fixture()
   if extra:r['unexpected']=next(iter(r.values()))
   else:r.pop(next(iter(r)))
   with self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_foreign_receipt_consumer_and_profile_refuse(self):
  for field in ('attemptId','consumerSha256','profileSha256'):
   p,f,s,t,r=fixture();key=next(iter(r));row=json.loads(r[key]);row[field]='wrong';r[key]=m.encode(row)
   with self.subTest(field=field),self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_trx_duplicates_failed_case_or_partial_roster_refuse(self):
  cls,method,count=m.METHODS['denied-count-no-generation'];raw=trx([(cls,method,'same')]*count)
  with self.assertRaises(m.Refusal):m.trx_cases(raw)
  raw=trx([(cls,method,str(i)) for i in range(count)])
  with self.assertRaises(m.Refusal):m.trx_cases(raw.replace(b'outcome="Passed"',b'outcome="Failed"',1))
  p,f,s,t,r=fixture();key='denied-count-no-generation';row=json.loads(r[key]);row['artifactBase64']=b64(trx([(cls,method,'one')]));r[key]=m.encode(row)
  with self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_metadata_is_required_even_when_the_method_passes(self):
  p,f,s,t,r=fixture();key='current-installed-closure';row=json.loads(r[key]);e=json.loads(base64.b64decode(row['metadataBase64']));e['checks']['loadedLocationsVerified']=False;row['metadataBase64']=b64(m.encode(e));r[key]=m.encode(row)
  with self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_stale_or_mutated_verifier_cannot_claim_pass(self):
  for key in ('verifier-stale-snapshot-refused','verifier-mutated-capture-refused','verifier-valid-capture'):
   p,f,s,t,r=fixture();row=json.loads(r[key]);e=json.loads(base64.b64decode(row['artifactBase64']));reply=json.loads(base64.b64decode(e['resultBase64']));reply['captureSha256']='0'*64;e['resultBase64']=b64(m.encode(reply));row['artifactBase64']=b64(m.encode(e));r[key]=m.encode(row)
   with self.subTest(key=key),self.assertRaises(m.Refusal):self.run_fixture((p,f,s,t,r))
 def test_profile_bounds_and_closed_shape(self):
  for key,value in [('installedRoots',['/installed','/installed/nested']),('outputTokenLimit',1501),('wholeMilliseconds',True),('verifierModuleSha256','0'*64),('extra',1)]:
   p,f,s,t,r=fixture();profile=json.loads(p);profile[key]=value
   with self.subTest(key=key),self.assertRaises(m.Refusal):m.validate_profile(m.encode(profile))
 def test_profile_duplicate_or_overflow_file_refuses(self):
  p,*_=fixture();profile=json.loads(p);profile['installedFiles'].append(copy.deepcopy(profile['installedFiles'][0]))
  with self.assertRaises(m.Refusal):m.validate_profile(m.encode(profile))
  profile=json.loads(p);profile['installedFiles'][0]['bytes']=201*1024*1024
  with self.assertRaises(m.Refusal):m.validate_profile(m.encode(profile))
 def test_full_285_inventory_metadata_expansion_is_admitted(self):
  profile,files=full_inventory();self.assertEqual(len(json.loads(files)),285);self.assertLessEqual(len(profile),65536)
  for scenario in ('current-installed-closure','credential-role-separation'):
   raw=metadata(profile,files,scenario);self.assertGreater(len(raw),65536);self.assertLessEqual(len(raw),131072)
   m.metadata_evidence(raw,profile,'synthetic_static_attempt',scenario)
 def test_metadata_inclusive_boundary_and_profile_bound_stay_distinct(self):
  profile,files=full_inventory();raw=metadata(profile,files);at_bound=raw+b' '*(131072-len(raw))
  m.metadata_evidence(at_bound,profile,'synthetic_static_attempt','current-installed-closure')
  with self.assertRaises(m.Refusal):m.metadata_evidence(at_bound+b' ',profile,'synthetic_static_attempt','current-installed-closure')
  profile_at_bound=profile+b' '*(65536-len(profile));m.validate_profile(profile_at_bound)
  with self.assertRaises(m.Refusal):m.validate_profile(profile_at_bound+b' ')
  m.parse(b'{}'+b' '*(65536-2))
  with self.assertRaises(m.Refusal):m.parse(b'{}'+b' '*(65536-1))
 def test_full_inventory_metadata_mutations_still_refuse(self):
  profile,files=full_inventory();row=json.loads(metadata(profile,files))
  for field,value in [('installedFilesSha256','b'*64),('loadedComponents',{}),('checks',{}),('profileSha256','c'*64)]:
   bad=copy.deepcopy(row);bad[field]=value
   with self.subTest(field=field),self.assertRaises(m.Refusal):m.metadata_evidence(m.encode(bad),profile,'synthetic_static_attempt','current-installed-closure')
  changed=json.loads(files);changed[-1]['sha256']='b'*64;bad=copy.deepcopy(row);bad['managedInstalledFilesBase64']=b64(m.encode(changed));bad['installedFilesSha256']=m.sha(m.encode(changed))
  with self.assertRaises(m.Refusal):m.metadata_evidence(m.encode(bad),profile,'synthetic_static_attempt','current-installed-closure')
  changed=json.loads(files);changed[-1]['bytes']=2;bad=copy.deepcopy(row);bad['managedInstalledFilesBase64']=b64(m.encode(changed))
  with self.assertRaises(m.Refusal):m.metadata_evidence(m.encode(bad),profile,'synthetic_static_attempt','current-installed-closure')
 def test_xml_entities_and_duplicate_json_refuse(self):
  with self.assertRaises(m.Refusal):m.trx_cases(b'<!DOCTYPE x []><x/>')
  with self.assertRaises(m.Refusal):m.parse(b'{"a":1,"a":2}')

if __name__=='__main__':unittest.main()
