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
 def test_public_command_has_no_local_source_and_prepared_command_has_one(self):
  public=budget.install_command('id','1',pathlib.Path('/tools'),pathlib.Path('/cfg'),None)
  prepared=budget.install_command('id','1',pathlib.Path('/tools'),pathlib.Path('/cfg'),pathlib.Path('/private/source'))
  self.assertNotIn('--add-source',public); self.assertEqual('/private/source',prepared[prepared.index('--add-source')+1])
 def test_candidate_digest_and_qualification_source_are_bound(self):
  with self.assertRaisesRegex(ValueError,'archive digest'): budget.validate_candidate_provenance('a'*64,'b'*64,'smoke',None)
  with self.assertRaisesRegex(ValueError,'source SHA'): budget.validate_candidate_provenance('a'*64,'a'*64,'qualification',None)
  budget.validate_candidate_provenance('a'*64,'a'*64,'qualification','1'*40)
 def test_host_and_akka_closure_is_refused_by_name(self):
  self.assertEqual(['tools/FS.GG.Telemetry.Host.pdb','tools/Akka.dll'],budget.forbidden_names(['tools/FS.GG.Telemetry.Host.pdb','tools/Akka.dll','tools/FS.GG.Coord.Core.dll']))
 def test_acquired_archive_mismatch_is_refused(self):
  with tempfile.TemporaryDirectory() as root:
   def package(name,version,extra):
    path=pathlib.Path(root)/extra
    with zipfile.ZipFile(path,'w') as z: z.writestr(f'{name}.nuspec',f'<package><metadata><id>{name}</id><version>{version}</version></metadata></package>'); z.writestr('payload',extra)
    return path
   expected=package('FS.GG.Coord.Cli','1.0.0','a.nupkg'); acquired=package('FS.GG.Coord.Cli','1.0.0','b.nupkg')
   with self.assertRaisesRegex(ValueError,'bound readback'): budget.acquired_artifact(expected,acquired,'FS.GG.Coord.Cli','1.0.0','nuget.org')
 def test_prepared_smoke_passes_without_qualifying_and_local_qualification_is_refused(self):
  passed,qualified=budget.outcome('smoke','prepared-local',2,[],{'compressedDeltaBytes':True,'installedDeltaBytes':True,'publicColdRestoreP95Milliseconds':None})
  self.assertTrue(passed); self.assertFalse(qualified)
  with self.assertRaisesRegex(ValueError,'public-only'): budget.validate_run_mode('qualification','prepared-local',20,'1'*40)
if __name__=='__main__': unittest.main()
