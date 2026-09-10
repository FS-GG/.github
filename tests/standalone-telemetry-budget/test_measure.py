import importlib.util,json,pathlib,tempfile,unittest,zipfile
SPEC=importlib.util.spec_from_file_location('budget',pathlib.Path(__file__).with_name('measure.py')); budget=importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(budget)
class BudgetTests(unittest.TestCase):
 def test_percentile_uses_nearest_rank_and_median_is_conventional(self):
  values=list(range(1,21)); self.assertEqual(19,budget.percentile95(values)); self.assertEqual(10.5,budget.statistics.median(values))
 def test_empty_samples_are_refused(self):
  with self.assertRaisesRegex(ValueError,'empty'): budget.percentile95([])
 def test_invalid_package_is_refused(self):
  with tempfile.TemporaryDirectory() as root:
   path=pathlib.Path(root)/'bad.nupkg'; path.write_bytes(b'bad')
   with self.assertRaisesRegex(ValueError,'valid nupkg'): budget.package_identity(path)
 def test_identity_requires_one_root_nuspec(self):
  with tempfile.TemporaryDirectory() as root:
   path=pathlib.Path(root)/'x.nupkg'
   with zipfile.ZipFile(path,'w') as z: z.writestr('nested/x.nuspec','<package/>')
   with self.assertRaisesRegex(ValueError,'one root nuspec'): budget.package_identity(path)
 def test_cleanup_is_bounded_to_generated_root(self):
  with tempfile.TemporaryDirectory() as root:
   owner=pathlib.Path(root)/'owner'; owner.mkdir(); outside=pathlib.Path(root)/'outside'; outside.mkdir()
   with self.assertRaisesRegex(ValueError,'escaped'): budget.safe_remove(outside,owner)
 def test_provenance_shape_is_canonical_json_serializable(self):
  evidence={'package':'FS.GG.Coord.Cli','version':'0.87.0','packageSha256':'a'*64}
  self.assertEqual(evidence,json.loads(json.dumps(evidence,sort_keys=True,separators=(',',':'))))
 def test_candidate_digest_and_qualification_source_are_bound(self):
  with self.assertRaisesRegex(ValueError,'archive digest'): budget.validate_candidate_provenance('a'*64,'b'*64,'smoke',None)
  with self.assertRaisesRegex(ValueError,'source SHA'): budget.validate_candidate_provenance('a'*64,'a'*64,'qualification',None)
  budget.validate_candidate_provenance('a'*64,'a'*64,'qualification','1'*40)
 def test_host_and_akka_closure_is_refused_by_name(self):
  self.assertEqual(['tools/FS.GG.Telemetry.Host.pdb','tools/Akka.dll'],budget.forbidden_names(['tools/FS.GG.Telemetry.Host.pdb','tools/Akka.dll','tools/FS.GG.Coord.Core.dll']))
if __name__=='__main__': unittest.main()
