import importlib.util,json,pathlib,tempfile,unittest
spec=importlib.util.spec_from_file_location('measure',pathlib.Path(__file__).with_name('measure.py')); measure=importlib.util.module_from_spec(spec); spec.loader.exec_module(measure)
class RuntimeMeasureTests(unittest.TestCase):
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
