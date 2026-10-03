import copy,importlib.util,json,pathlib,tempfile,unittest,zipfile
spec=importlib.util.spec_from_file_location('measure',pathlib.Path(__file__).with_name('measure.py')); measure=importlib.util.module_from_spec(spec); spec.loader.exec_module(measure)
class RuntimeMeasureTests(unittest.TestCase):
 def phase_fixture(self,root,values=None):
  assembly=root/'FS.GG.Telemetry.Store.dll'; assembly.write_bytes(b'packaged-store')
  core=root/'FS.GG.Coord.Core.dll'; core.write_bytes(b'packaged-core')
  native=root/'libe_sqlite3.so'; native.write_bytes(b'packaged-sqlite')
  package=root/'candidate.nupkg'
  with zipfile.ZipFile(package,'w') as archive:
   for binary in [assembly,core,native]: archive.write(binary,'tools/net10.0/any/'+binary.name)
  values=values or [1.0]*100; rows=[]; cursor=0
  for index,value in enumerate(values):
   start=cursor+1000; end=start+round(value*1000); cursor=end
   rows.append({'index':index,'startTimestamp':start,'endTimestamp':end,'elapsedMilliseconds':value,'processCpuMilliseconds':value+1,'gcCollections':[0,0,0],'phasesTruncated':False,
    'phases':[{'name':name,'timestamp':start+offset} for name,offset in [('before-file-sync',1),('after-file-sync',2),('after-rename',3),('after-directory-sync',4),('index-committed',5)]]})
  evidence={'schema':'fsgg.telemetry.packaged-store-performance/1','qualified':measure.p95(values)<=100,'storeAssemblySha256':measure.digest(assembly),'assessment':'approved-local-durable',
   'samples':{'warmInProcessAdmissionMilliseconds':values,'applicationDrainMilliseconds':[1.0]*100,'accumulatingPendingAdmissionMilliseconds':[1.0]*100},
   'summary':{'warmInProcessAdmission':measure.distribution(values)},'limits':{'warmInProcessAdmissionP95Milliseconds':100},
   'diagnostics':{'schema':'fsgg.telemetry.store-phase-diagnostics/1','warmupPairs':5,'firstSample':0,'sampleCount':100,'timestampFrequency':1000000,'admissions':rows,
    'context':{'runtimeVersion':'10.0.0','runtimeAssemblySha256':'1'*64,'fsiHostVersion':'14.0.0.0','fsiHostSha256':'2'*64,'framework':'.NET 10.0.0','processArchitecture':'X64','osArchitecture':'X64','processorCount':2,'cpuModel':None,'cpuQuota':None,'operatingSystem':'Linux','isServerGC':False,'gcLatencyMode':'Interactive','storeAssemblySha256':measure.digest(assembly),'coreAssemblySha256':measure.digest(core),'nativeSqliteSha256':measure.digest(native),
     'observedEnvironment':{prefix+name:None for prefix in ['DOTNET_','COMPlus_'] for name in ['TieredCompilation','TieredPGO','ReadyToRun','gcServer','gcConcurrent']}}}}
  return evidence,assembly,package
 def test_phase_diagnostics_bind_first_hundred_samples_and_package_bytes(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=pathlib.Path(temporary); evidence,assembly,package=self.phase_fixture(root)
   path=root/'probe.json'; path.write_text(json.dumps(evidence))
   self.assertTrue(measure.verify_store_probe(path,assembly,package,True)['qualified'])
   # Process-wide CPU can exceed wall time; it is an observation, not a timing filter.
   self.assertGreater(evidence['diagnostics']['admissions'][0]['processCpuMilliseconds'],1.0)
   with zipfile.ZipFile(package,'w') as archive:
    archive.writestr('tools/net10.0/any/FS.GG.Telemetry.Store.dll',b'wrong-payload')
   with self.assertRaisesRegex(ValueError,'package payload differs'): measure.verify_store_probe(path,assembly,package,True)
 def test_phase_diagnostics_cannot_turn_the_native_slow_cluster_into_a_pass(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=pathlib.Path(temporary); values=[12.0]*100
   values[15:21]=[119.996,150.868,320.481,269.847,619.272,377.055]
   evidence,assembly,package=self.phase_fixture(root,values); path=root/'probe.json'; path.write_text(json.dumps(evidence))
   result=measure.verify_store_probe(path,assembly,package,True)
   self.assertFalse(result['qualified']); self.assertEqual(119.996,result['summary']['warmInProcessAdmission']['p95Milliseconds'])
   evidence['qualified']=True; path.write_text(json.dumps(evidence))
   with self.assertRaisesRegex(ValueError,'verdict contradicts'): measure.verify_store_probe(path,assembly,package,True)
 def test_phase_diagnostics_reject_order_clock_boundaries_and_unbounded_context(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=pathlib.Path(temporary); original,assembly,package=self.phase_fixture(root); path=root/'probe.json'
   mutations=[
    ('sample order',lambda d:d['admissions'][1].update(index=0)),
    ('wall time',lambda d:d['admissions'][0].update(endTimestamp=3000)),
    ('phase order',lambda d:d['admissions'][0]['phases'][1].update(timestamp=0)),
    ('durability boundaries',lambda d:d['admissions'][0]['phases'].pop(0)),
    ('durability boundaries',lambda d:d['admissions'][0]['phases'].pop()),
    ('context allowlist',lambda d:d['context'].update(unrelated='must-not-be-retained')),
    ('runtime identity',lambda d:d['context'].update(fsiHostSha256='not-a-digest')),
    ('runtime context',lambda d:d['context'].update(processorCount=True)),
    ('GC counts',lambda d:d['admissions'][0].update(gcCollections=[-1,0,0])),
    ('truncated',lambda d:d['admissions'][0].update(phasesTruncated=True)),
    ('configuration',lambda d:d['context']['observedEnvironment'].update(UNRELATED='must-not-be-retained')),
    ('assembly',lambda d:d['context'].update(coreAssemblySha256='0'*64)),
    ('sampling',lambda d:d.update(firstSample=1)),
    ('sampling',lambda d:d.update(warmupPairs=50)),
    ('count',lambda d:d['admissions'].pop()),
   ]
   for label,mutate in mutations:
    with self.subTest(label=label):
     evidence=copy.deepcopy(original); mutate(evidence['diagnostics']); path.write_text(json.dumps(evidence))
     with self.assertRaisesRegex(ValueError,label): measure.verify_store_probe(path,assembly,package,True)
 def test_missing_diagnostics_stay_historical_and_unknown_context_is_nullable(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=pathlib.Path(temporary); evidence,assembly,package=self.phase_fixture(root); path=root/'probe.json'
   evidence['diagnostics']['context'].update(coreAssemblySha256=None,nativeSqliteSha256=None)
   path.write_text(json.dumps(evidence)); measure.verify_store_probe(path,assembly,package,True)
   evidence.pop('diagnostics'); path.write_text(json.dumps(evidence))
   self.assertTrue(measure.verify_store_probe(path,assembly)['qualified'])
   with self.assertRaisesRegex(ValueError,'diagnostics are missing'): measure.verify_store_probe(path,assembly,package,True)
 def test_percentile_is_nearest_rank(self): self.assertEqual(19,measure.p95(list(range(1,21))))
 def test_batch_is_valid_bounded_and_representative(self):
  value=measure.batch(7); self.assertGreaterEqual(len(value),60*1024); self.assertLessEqual(len(value),64*1024); self.assertIn(b'"ingestId":"qualification-007"',value)
 def test_empty_samples_fail(self):
  with self.assertRaisesRegex(ValueError,'empty'): measure.p95([])
 def test_distribution_reports_median_p95_and_maximum(self):
  self.assertEqual({'medianMilliseconds':10.5,'p95Milliseconds':19,'maximumMilliseconds':20},measure.distribution(list(range(1,21))))
 def test_packaged_store_evidence_binds_assembly_and_sample_floors(self):
  with tempfile.TemporaryDirectory() as root:
   assembly=pathlib.Path(root)/'FS.GG.Telemetry.Store.dll'; assembly.write_bytes(b'packaged-store')
   evidence={
    'schema':'fsgg.telemetry.packaged-store-performance/1','qualified':True,
    'storeAssemblySha256':measure.digest(assembly),'assessment':'approved-local-durable',
    'samples':{name:[1.0]*100 for name in ['warmInProcessAdmissionMilliseconds','applicationDrainMilliseconds','accumulatingPendingAdmissionMilliseconds']},
    'summary':{'warmInProcessAdmission':{'p95Milliseconds':1.0}},
    'limits':{'warmInProcessAdmissionP95Milliseconds':100},
   }
   path=pathlib.Path(root)/'probe.json'; path.write_text(json.dumps(evidence))
   self.assertTrue(measure.verify_store_probe(path,assembly)['qualified'])
   evidence['storeAssemblySha256']='0'*64; path.write_text(json.dumps(evidence))
   with self.assertRaisesRegex(ValueError,'assembly differs'): measure.verify_store_probe(path,assembly)
   evidence['storeAssemblySha256']=measure.digest(assembly); evidence['samples']['warmInProcessAdmissionMilliseconds'][-1]=float('nan'); path.write_text(json.dumps(evidence))
   with self.assertRaisesRegex(ValueError,'samples are invalid'): measure.verify_store_probe(path,assembly)
   evidence['samples']['warmInProcessAdmissionMilliseconds']=[101.0]*100; evidence['summary']['warmInProcessAdmission']['p95Milliseconds']=101.0; path.write_text(json.dumps(evidence))
   with self.assertRaisesRegex(ValueError,'verdict contradicts'): measure.verify_store_probe(path,assembly)
 def test_public_readback_binds_normalized_and_raw_identity(self):
  binding={'version':'0.88.0','sourceSha':'1'*40,'preparedArchiveSha256':'a'*64,'packageSha256':'b'*64,'payloadSha256':'sha256:'+'c'*64}
  evidence={'acquisition':'nuget.org-public-readback','packageId':'FS.GG.Coord.Cli',**binding,'externalArchiveSha256':binding['packageSha256']}
  evidence.pop('packageSha256')
  with tempfile.TemporaryDirectory() as root:
   path=pathlib.Path(root)/'public.json'; path.write_text(json.dumps(evidence)); measure.verify_public_readback(path,binding)
   evidence['payloadSha256']='sha256:'+'d'*64; path.write_text(json.dumps(evidence))
   with self.assertRaisesRegex(ValueError,'differs'): measure.verify_public_readback(path,binding)
if __name__=='__main__': unittest.main()
