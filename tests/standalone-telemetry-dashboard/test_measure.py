import importlib.util,json,pathlib,tempfile,unittest
spec=importlib.util.spec_from_file_location('measure',pathlib.Path(__file__).with_name('measure.py')); measure=importlib.util.module_from_spec(spec); spec.loader.exec_module(measure)
class RuntimeMeasureTests(unittest.TestCase):
 def test_percentile_is_nearest_rank(self): self.assertEqual(19,measure.p95(list(range(1,21))))
 def test_batch_is_valid_bounded_and_representative(self):
  value=measure.batch(7); self.assertGreaterEqual(len(value),60*1024); self.assertLessEqual(len(value),64*1024); self.assertIn(b'"ingestId":"qualification-007"',value)
 def test_empty_samples_fail(self):
  with self.assertRaisesRegex(ValueError,'empty'): measure.p95([])
 def test_public_readback_binds_normalized_and_raw_identity(self):
  binding={'version':'0.88.0','sourceSha':'1'*40,'preparedArchiveSha256':'a'*64,'packageSha256':'b'*64,'payloadSha256':'sha256:'+'c'*64}
  evidence={'acquisition':'nuget.org-public-readback','packageId':'FS.GG.Coord.Cli',**binding,'externalArchiveSha256':binding['packageSha256']}
  evidence.pop('packageSha256')
  with tempfile.TemporaryDirectory() as root:
   path=pathlib.Path(root)/'public.json'; path.write_text(json.dumps(evidence)); measure.verify_public_readback(path,binding)
   evidence['payloadSha256']='sha256:'+'d'*64; path.write_text(json.dumps(evidence))
   with self.assertRaisesRegex(ValueError,'differs'): measure.verify_public_readback(path,binding)
if __name__=='__main__': unittest.main()
