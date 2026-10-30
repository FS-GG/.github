"""Inert synthetic current-pin/package controls; never publish or execute DLLs."""
import contextlib
import io
import os
import hashlib
import importlib.util
import json
import pathlib
import runpy
import sys
import subprocess
import tempfile
import unittest
import zipfile
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from new_sdd_workspace_successor_execution import CURRENT_0231, CURRENT_023, CURRENT_022, CURRENT_021, CURRENT_020, CURRENT_019, ReleaseBinding
from creator_source_qualification import SOURCE_020

CURRENT = runpy.run_path(str(ROOT / 'scripts/creator-current-coord-dependencies.py'))
PREFIX = CURRENT['PREFIX']


class CurrentDependencies(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.package = self.root / 'FS.GG.NewSddWorkspace.0.19.0.nupkg'
        self.members = {name: b'explicit synthetic unexecutable member ' + name.encode() for name in (
            'FS.GG.Coord.GitHub.dll', 'fsgg-coord-engine.dll', 'fsgg-coord-engine.deps.json',
            'fsgg-coord-engine.runtimeconfig.json', 'FS.GG.Coord.Core.dll', 'runtimes/linux-x64/native/test.so')}
        framework = '.NETCoreApp,Version=v10.0'
        self.producer = {'libraries': {
            'fsgg-coord-engine/0.108.0': {'type': 'project'},
            'FS.GG.Coord.Core/1.0.0': {'type': 'project'},
            'FS.GG.Coord.GitHub/1.0.0': {'type': 'project'}}, 'targets': {framework: {
                'fsgg-coord-engine/0.108.0': {'dependencies': {'FS.GG.Coord.Core': '1.0.0',
                    'FS.GG.Coord.GitHub': '1.0.0'}, 'runtime': {'fsgg-coord-engine.dll': {}}},
                'FS.GG.Coord.Core/1.0.0': {'runtime': {'FS.GG.Coord.Core.dll': {}}},
                'FS.GG.Coord.GitHub/1.0.0': {'runtime': {'FS.GG.Coord.GitHub.dll': {}}}}}}
        self.members['fsgg-coord-engine.deps.json'] = json.dumps(self.producer).encode()
        sha = lambda body: hashlib.sha256(body).hexdigest()
        self.pin = {'schema': 'fsgg.creator-current-coord-dependencies/1', 'version': '0.108.0',
                    'sourceSha': 'b' * 40, 'archiveSha256': 'c' * 64, 'promotedManifestSha256': 'd' * 64,
                    'releaseId': 1, 'assetId': 2, 'promotedManifestAssetId': 3,
                    'downloadUrl': 'https://github.com/FS-GG/.github/releases/download/coherent-set/v0.108.0/FS.GG.Coord.Cli.0.108.0.nupkg',
                    'projects': {'scripts/NewSddWorkspace/NewSddWorkspace.fsproj': 'new-sdd-workspace',
                                 'src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj': 'fsgg-coord-engine',
                                 'src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj': 'FS.GG.Coord.Core',
                                 'src/FS.GG.Coord.GitHub/FS.GG.Coord.GitHub.fsproj': 'FS.GG.Coord.GitHub'},
                    'sourceLeaves': [{'path': 'global.json', 'sha256': 'e' * 64}],
                    'members': {name: sha(body) for name, body in self.members.items()}}
        self.deps = json.loads(json.dumps(self.producer))
        self.deps['libraries']['FS.GG.Coord.Cli/0.108.0'] = self.deps['libraries'].pop('fsgg-coord-engine/0.108.0')
        rows = self.deps['targets'][framework]
        rows['FS.GG.Coord.Cli/0.108.0'] = rows.pop('fsgg-coord-engine/0.108.0')
        rows['new-sdd-workspace/0.19.0'] = {'dependencies': {'FS.GG.Coord.Cli': '0.108.0'}}
        self.write()

    def write(self, members=None, deps=None, extra=None):
        with zipfile.ZipFile(self.package, 'w') as archive:
            for name, body in (self.members if members is None else members).items():
                archive.writestr(PREFIX + name, body)
            archive.writestr(PREFIX + 'new-sdd-workspace.deps.json', json.dumps(self.deps if deps is None else deps))
            for name, body in (extra or {}).items():
                archive.writestr(name, body)

    def evidence(self):
        return CURRENT['evidence'](self.package, 'a' * 40, self.pin, fixtures_passed=True)

    def test_source020_uses_exact108_without_broadening_consumed019(self):
        from creator_source_qualification import SOURCE_020
        from new_sdd_workspace_successor_execution import effects, ReleaseBinding
        from new_sdd_workspace_successor_admission import WizardAdmission
        from release_successor_execution import Refused
        deps = json.loads(json.dumps(self.deps))
        for target in deps['targets'].values():
            target['new-sdd-workspace/0.20.0'] = target.pop('new-sdd-workspace/0.19.0')
        own_metadata = {'FS.GG.NewSddWorkspace.nuspec':
                        b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.20.0</version>'
                        b'<repository commit="' + b'a' * 40 + b'"/></metadata></package>'}
        self.write(deps=deps, extra=own_metadata)
        original_pin = json.dumps(self.pin, sort_keys=True)
        evidence = CURRENT['evidence'](self.package, 'a' * 40, self.pin,
                                      fixtures_passed=True, source_binding=SOURCE_020)
        self.assertEqual(evidence['version'], '0.20.0')
        self.assertEqual(evidence['coherentVersion'], '0.108.0')
        self.assertEqual(evidence['dependencyMembers'], self.pin['members'])
        self.assertEqual(evidence['publishedDependencySourceSha'], self.pin['sourceSha'])
        self.assertEqual(json.dumps(self.pin, sort_keys=True), original_pin)
        self.assertIs(evidence['sourceQualificationOnly'], True)
        self.assertIs(evidence['releaseEligible'], False)
        manifest = {'sourceSha': 'a' * 40, 'version': '0.20.0',
                    'archiveSha256': evidence['archiveSha256']}
        CURRENT['verify_evidence'](evidence, self.package, manifest, self.pin,
                                   source_binding=SOURCE_020)
        with self.assertRaisesRegex(ValueError, 'metadata'):
            CURRENT['verify_evidence'](evidence, self.package, manifest, self.pin)
        with self.assertRaisesRegex(ValueError, 'compiled fixtures'):
            CURRENT['evidence'](self.package, 'a' * 40, self.pin,
                                fixtures_passed=False, source_binding=SOURCE_020)
        self.write(deps=deps, extra={'FS.GG.NewSddWorkspace.nuspec':
                                    own_metadata['FS.GG.NewSddWorkspace.nuspec'].replace(b'0.20.0', b'0.19.0')})
        with self.assertRaisesRegex(ValueError, 'package/source identity'):
            CURRENT['evidence'](self.package, 'a' * 40, self.pin,
                                fixtures_passed=True, source_binding=SOURCE_020)
        self.write(deps=deps, extra=own_metadata)
        with self.assertRaisesRegex(ValueError, 'package/source identity'):
            CURRENT['evidence'](self.package, 'c' * 40, self.pin,
                                fixtures_passed=True, source_binding=SOURCE_020)
        forged = ReleaseBinding(SOURCE_020.package, SOURCE_020.version, SOURCE_020.tag)
        with self.assertRaisesRegex(ValueError, 'unknown Creator source'):
            CURRENT['package_members'](self.package, self.pin, source_binding=forged)
        mutated = dict(self.members)
        mutated['FS.GG.Coord.GitHub.dll'] += b' deliberately changed original dependency'
        self.write(members=mutated, deps=deps, extra=own_metadata)
        with self.assertRaisesRegex(ValueError, 'changed published dependency'):
            CURRENT['package_members'](self.package, self.pin, source_binding=SOURCE_020)
        proposed = {'schema': 'fsgg.new-sdd-workspace-release/1',
                    'packageId': SOURCE_020.package, 'version': SOURCE_020.version,
                    'tag': SOURCE_020.tag, 'sourceSha': 'a' * 40,
                    'archiveSha256': 'b' * 64, 'producerPayloadSha256': 'sha256:' + 'c' * 64}
        class NoNativeReads:
            def get(self, path):
                raise AssertionError('source qualification cannot read effect authority')
        with self.assertRaisesRegex(Refused, 'unknown Wizard release binding'):
            effects(proposed, binding=SOURCE_020)
        with self.assertRaisesRegex(Refused, 'unknown Wizard release binding'):
            WizardAdmission(NoNativeReads(), proposed, 'a' * 40, 1,
                            'EHotwagner', 'refs/heads/main', release_binding=SOURCE_020)
        self.assertEqual(len(effects(proposed, binding=CURRENT_020)[1]), 8)  # descriptors do not accept source-only evidence

    def prepare_release020(self):
        self.package = self.root / 'FS.GG.NewSddWorkspace.0.20.0.nupkg'
        deps = json.loads(json.dumps(self.deps))
        for target in deps['targets'].values():
            target['new-sdd-workspace/0.20.0'] = target.pop('new-sdd-workspace/0.19.0')
        self.write(deps=deps, extra={'FS.GG.NewSddWorkspace.nuspec':
            b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.20.0</version>'
            b'<repository commit="' + b'a' * 40 + b'"/></metadata></package>'})
        evidence = CURRENT['release_evidence'](self.package, 'a' * 40, self.pin, fixtures_passed=True)
        manifest = {'packageId': CURRENT_020.package, 'sourceSha': 'a' * 40, 'version': CURRENT_020.version,
                    'tag': CURRENT_020.tag, 'archiveSha256': evidence['archiveSha256']}
        return evidence, manifest

    def test_release020_recomputes_qualified108_and_never_upgrades_source_receipts(self):
        original_pin = json.dumps(self.pin, sort_keys=True)
        evidence, manifest = self.prepare_release020()
        self.assertEqual(CURRENT['verify_release_evidence'](evidence, self.package, manifest, self.pin), evidence)
        self.assertEqual(evidence['schema'], 'fsgg.creator020-release-qualification/1')
        self.assertEqual((evidence['version'], evidence['coherentVersion']), ('0.20.0', '0.108.0'))
        self.assertEqual(evidence['dependencyMembers'], self.pin['members'])
        self.assertEqual(json.dumps(self.pin, sort_keys=True), original_pin)
        source = CURRENT['evidence'](self.package, 'a' * 40, self.pin, fixtures_passed=True, source_binding=SOURCE_020)
        self.assertFalse(source['releaseEligible'])
        for body in (source, {**source, 'releaseEligible': True},
                     {**source, 'releaseEligible': True, 'sourceQualificationOnly': False},
                     {**evidence, 'fixturesPassed': False}, {**evidence, 'mode': 'source-only'},
                     {**evidence, 'releaseBinding': CURRENT_019.tag},
                     {**evidence, 'publishedDependencySourceSha': 'c' * 40},
                     {**evidence, 'dependencyPinSha256': '0' * 64},
                     {**evidence, 'dependencyMembers': {}}, {**evidence, 'unknown': True}):
            with self.subTest(body=body), self.assertRaisesRegex(ValueError, 'release qualification'):
                CURRENT['verify_release_evidence'](body, self.package, manifest, self.pin)
        for binding in (SOURCE_020, CURRENT_019, ReleaseBinding(CURRENT_020.package, CURRENT_020.version, CURRENT_020.tag)):
            with self.subTest(binding=binding), self.assertRaisesRegex(ValueError, 'release qualification binding'):
                CURRENT['release_evidence'](self.package, 'a' * 40, self.pin, fixtures_passed=True, release_binding=binding)
        with self.assertRaisesRegex(ValueError, 'compiled fixtures'):
            CURRENT['release_evidence'](self.package, 'a' * 40, self.pin, fixtures_passed=False)
        for key, value in (('version', '0.19.0'), ('tag', CURRENT_019.tag), ('archiveSha256', '0' * 64), ('packageId', 'foreign')):
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, 'manifest/package join'):
                CURRENT['verify_release_evidence'](evidence, self.package, {**manifest, key: value}, self.pin)
        with self.assertRaisesRegex(ValueError, 'package/source identity'):
            CURRENT['release_evidence'](self.package, 'b' * 40, self.pin, fixtures_passed=True)

    def test_current020_artifact_refuses_source_only_and_wrong_native_tuple_before_checker(self):
        spec = importlib.util.spec_from_file_location('release020_artifact', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(verifier)
        evidence, manifest = self.prepare_release020()
        source = CURRENT['evidence'](self.package, 'a' * 40, self.pin, fixtures_passed=True, source_binding=SOURCE_020)
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1',
                    'expired': False, 'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                    'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        injected = {**CURRENT, 'protected_pin': lambda source, **kwargs: self.pin}
        real_run_path = runpy.run_path
        def load(path, *args, **kwargs):
            return injected if str(path).endswith('creator-current-coord-dependencies.py') else real_run_path(path, *args, **kwargs)
        cases = [(evidence, run, artifact)]
        cases += [(body, run, artifact) for body in (source, {**source, 'releaseEligible': True, 'sourceQualificationOnly': False}, {})]
        cases += [(evidence, {**run, key: value}, artifact) for key, value in
                  (('run_attempt', 2), ('head_sha', 'b' * 40), ('head_branch', 'other'), ('path', 'foreign.yml'),
                   ('conclusion', 'failure'), ('repository', {'id': 1}))]
        cases += [(evidence, run, {**artifact, 'expired': True})]
        for index, (body, observed_run, observed_artifact) in enumerate(cases):
            archive = self.root / f'release-candidate{index}.zip'
            with zipfile.ZipFile(archive, 'w') as zipped:
                zipped.writestr('manifest.json', json.dumps(manifest))
                zipped.writestr('package-evidence.json', json.dumps(body))
                zipped.writestr(self.package.name, self.package.read_bytes())
            observed_artifact = {**observed_artifact, 'digest': 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()}
            output = self.root / f'release-output{index}'
            with self.subTest(index=index), patch.object(verifier.runpy, 'run_path', side_effect=load), patch.object(verifier.subprocess, 'run') as checker:
                if index == 0:
                    verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_020)
                    checker.assert_called_once()
                else:
                    with self.assertRaises(ValueError):
                        verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_020)
                    checker.assert_not_called()
                    self.assertFalse(output.exists())

    def test_actual_publish_entry_refuses_source_only_before_authority_or_effects(self):
        spec = importlib.util.spec_from_file_location('release020_publisher', ROOT / 'scripts/new-sdd-workspace-successor-publish.py')
        publisher = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(publisher)
        _, manifest = self.prepare_release020()
        source = CURRENT['evidence'](self.package, 'a' * 40, self.pin, fixtures_passed=True, source_binding=SOURCE_020)
        class CandidateAPI:
            def get(self, path):
                if path.endswith('/actions/artifacts/2'):
                    return {'id': 2, 'digest': 'sha256:' + 'd' * 64}
                if path.endswith('/actions/runs/1'):
                    return {'id': 1, 'head_sha': 'a' * 40}
                raise AssertionError('only candidate metadata may be observed before evidence refusal')
        env = {'GITHUB_SHA': 'a' * 40, 'GITHUB_ACTOR': 'EHotwagner', 'GITHUB_RUN_ID': '123',
               'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REPOSITORY': 'FS-GG/.github',
               'GITHUB_REF': 'refs/heads/main', 'GITHUB_RUN_ATTEMPT': '1', 'GH_TOKEN': 'synthetic-gh',
               'ORDINARY_LEDGER_TOKEN': 'synthetic-ledger', 'NUGET_API_KEY': 'synthetic-nuget'}
        original_run_path = runpy.run_path
        injected = {**CURRENT, 'load_pin': lambda **kwargs: self.pin, 'protected_pin': lambda source, **kwargs: self.pin}
        def load(path, *args, **kwargs):
            return injected if str(path).endswith('creator-current-coord-dependencies.py') else original_run_path(path, *args, **kwargs)
        for index, body in enumerate((source, {**source, 'releaseEligible': True, 'sourceQualificationOnly': False})):
            root = self.root / f'entry{index}'
            root.mkdir()
            def fixture(command, **kwargs):
                if command[0] == 'gh':
                    return subprocess.CompletedProcess(command, 0)
                output = pathlib.Path(command[command.index('--output') + 1])
                output.mkdir()
                (output / 'manifest.json').write_text(json.dumps(manifest))
                (output / 'package-evidence.json').write_text(json.dumps(body))
                # Ordinary0231 must reject even a historical020 package renamed to its expected slot.
                (output / 'FS.GG.NewSddWorkspace.0.23.1.nupkg').write_bytes(self.package.read_bytes())
                return subprocess.CompletedProcess(command, 0)
            argv = ['publisher', '--publish', '--candidate-run-id', '1', '--candidate-artifact-id', '2',
                    '--candidate-archive-sha256', 'd' * 64, '--workdir', str(root / 'work')]
            with self.subTest(index=index), patch.object(sys, 'argv', argv), patch.dict(os.environ, env, clear=True), \
                    patch.object(runpy, 'run_path', side_effect=load), \
                    patch.object(publisher, 'GitHubAPI', return_value=CandidateAPI()) as api, \
                    patch.object(publisher.subprocess, 'run', side_effect=fixture), \
                    patch.object(publisher, 'publication_primitives') as authority, \
                    patch.object(publisher, 'WizardProvider') as provider, \
                    patch.object(publisher, 'ProtectedReleaseJournal') as journal, \
                    contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(publisher.main(), 1)
            self.assertEqual(api.call_count, 1)  # Candidate reads, no Authority client.
            authority.assert_not_called()
            provider.assert_not_called()
            journal.assert_not_called()
            summary = json.loads((root / 'creator0231-publication-summary.json').read_text())
            self.assertEqual(summary['firstCause']['kind'], 'ValueError')
            self.assertFalse(summary['journalMutationMayHaveOccurred'])
            self.assertFalse(summary['preflightPassed'])
            self.assertFalse(summary['complete'])

    def test_actual_release020_cli_joins_compiled_output_and_manifest(self):
        # These inert bytes exercise the real CLI path, not a compiled qualification.
        _, manifest = self.prepare_release020()
        creator = self.root / 'creator'
        output = creator / 'scripts/NewSddWorkspace/bin/Release/net10.0'
        output.mkdir(parents=True)
        with zipfile.ZipFile(self.package, 'a') as archive:
            archive.writestr(PREFIX + 'new-sdd-workspace.dll', b'inert own compiled fixture')
            archive.writestr(PREFIX + 'new-sdd-workspace.runtimeconfig.json', b'{}')
        with zipfile.ZipFile(self.package) as archive:
            for name in archive.namelist():
                if name.startswith(PREFIX):
                    target = output / name[len(PREFIX):]
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(archive.read(name))
        checker = runpy.run_path(str(ROOT / 'scripts/new-sdd-workspace-release.py'))
        manifest = checker['build_manifest'](type('Args', (), {'package': str(self.package), 'version': CURRENT_020.version,
            'tag': CURRENT_020.tag, 'source_sha': 'a' * 40})())
        manifest_path = self.root / 'manifest.json'
        original_run_path = runpy.run_path
        injected = {**CURRENT, 'protected_pin': lambda source, **kwargs: self.pin,
                    'old': {**CURRENT['old'], 'layout': lambda *args: {'currentCreatorSourceSha': 'a' * 40}}}
        def load(path, *args, **kwargs):
            return injected if str(path).endswith('creator-current-coord-dependencies.py') else original_run_path(path, *args, **kwargs)
        argv = ['qualification', '--release-qualification-020', '--board-package', str(self.package),
                '--source-sha', 'a' * 40, '--release-manifest', str(manifest_path),
                '--published-coord-source', str(self.root / 'producer'),
                '--published-coord-dependencies', str(self.root / 'dependencies'), '--creator-source', str(creator)]
        own = output / 'new-sdd-workspace.dll'
        original = own.read_bytes()
        for defect in (None, 'missing-output', 'changed-output', 'changed-source', 'archive-manifest', 'payload-manifest'):
            own.write_bytes(original)
            body = dict(manifest)
            injected['old']['layout'] = lambda *args: {'currentCreatorSourceSha': 'b' * 40 if defect == 'changed-source' else 'a' * 40}
            if defect == 'missing-output': own.unlink()
            if defect == 'changed-output': own.write_bytes(b'inert different own output')
            if defect == 'archive-manifest': body['archiveSha256'] = '0' * 64
            if defect == 'payload-manifest': body['producerPayloadSha256'] = 'sha256:' + '0' * 64
            manifest_path.write_text(json.dumps(body))
            result = io.StringIO()
            with self.subTest(defect=defect), patch.object(sys, 'argv', argv), \
                    patch.object(runpy, 'run_path', side_effect=load), contextlib.redirect_stdout(result):
                if defect is None:
                    with self.assertRaises(SystemExit) as exit:
                        original_run_path(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
                    self.assertEqual(exit.exception.code, 0)
                    actual = json.loads(result.getvalue())
                    self.assertEqual(actual['schema'], 'fsgg.creator020-release-qualification/1')
                    self.assertTrue(actual['releaseEligible'])
                    self.assertEqual(actual['coherentVersion'], '0.108.0')
                else:
                    with self.assertRaises(ValueError):
                        original_run_path(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')

    def test_release020_cli_refuses_wrong_graph_before_any_qualification(self):
        command = [sys.executable, '-B', str(ROOT / 'tests/new-sdd-workspace-successor/run.py'),
                   '--release-qualification-020', '--board-package', str(self.root / 'missing.nupkg'),
                   '--source-sha', 'a' * 40]
        complete = ['--published-coord-source', str(self.root / 'producer'),
                    '--published-coord-dependencies', str(self.root / 'dependencies'),
                    '--creator-source', str(self.root / 'creator'), '--release-manifest', str(self.root / 'manifest.json')]
        for extra in ([], ['--build-artifacts', str(self.root)], complete + ['--build-artifacts', str(self.root)],
                      complete + ['--frozen-coord-source', str(self.root)],
                      complete + ['--frozen-coord-dependencies', str(self.root)], ['--source-qualification-020']):
            result = subprocess.run(command + extra, text=True, capture_output=True, timeout=10)
            with self.subTest(extra=extra):
                self.assertEqual(result.returncode, 2)
                self.assertIn('error:', result.stderr)
                self.assertNotIn('Traceback', result.stderr)
        self.assertFalse((self.root / 'missing.nupkg').exists())

    def test_complete_exact_closure_and_mutations(self):
        self.assertEqual(CURRENT['package_members'](self.package, self.pin), self.pin['members'])
        for name in self.members:
            mutated = dict(self.members)
            mutated[name] = bytes([mutated[name][0] ^ 1]) + mutated[name][1:]
            self.write(mutated)
            with self.subTest(member=name), self.assertRaisesRegex(ValueError, 'changed published dependency'):
                CURRENT['package_members'](self.package, self.pin)
            self.write({k: v for k, v in self.members.items() if k != name})
            with self.assertRaisesRegex(ValueError, 'changed published dependency'):
                CURRENT['package_members'](self.package, self.pin)
        self.write(extra={PREFIX + 'alternate/FS.GG.Coord.GitHub.dll': self.members['FS.GG.Coord.GitHub.dll']})
        with self.assertRaisesRegex(ValueError, 'shadow'):
            CURRENT['package_members'](self.package, self.pin)

    def test_source_only_evidence_and_old_pins_refuse(self):
        evidence = self.evidence()
        manifest = {'sourceSha': 'a' * 40, 'version': '0.19.0', 'archiveSha256': evidence['archiveSha256']}
        CURRENT['verify_evidence'](evidence, self.package, manifest, self.pin)
        for field, value in [('mode', 'source-only'), ('sourceSha', 'c' * 40), ('fixturesPassed', False),
                             ('dependencyMembers', {}), ('dependencyPinSha256', '0' * 64), ('unknown', True)]:
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, 'evidence body'):
                CURRENT['verify_evidence']({**evidence, field: value}, self.package, manifest, self.pin)
        for version in ('0.99.0', '0.107.0'):
            with self.assertRaisesRegex(ValueError, '0.108.0 pin'):
                CURRENT['validate_pin']({**self.pin, 'version': version})
        self.write(deps={'libraries': {}, 'targets': {}})
        with self.assertRaisesRegex(ValueError, 'metadata'):
            CURRENT['package_members'](self.package, self.pin)

    def test_original_internal_versions_and_sdk_root_annotation_are_preserved(self):
        # Coherent tool version is not the internal project version. These are
        # explicit synthetic bytes mirroring the genuine producer graph shape.
        self.assertIn('FS.GG.Coord.Core/1.0.0', self.deps['libraries'])
        root = self.deps['targets']['.NETCoreApp,Version=v10.0']['FS.GG.Coord.Cli/0.108.0']
        root['runtime']['fsgg-coord-engine.dll'] = {'fileVersion': '0.0.0.0'}
        self.write()
        self.assertEqual(CURRENT['package_members'](self.package, self.pin), self.pin['members'])
        root['runtime']['fsgg-coord-engine.dll'] = {'fileVersion': '99.0.0.0'}
        self.write()
        with self.assertRaisesRegex(ValueError, 'entry runtime metadata'):
            CURRENT['package_members'](self.package, self.pin)

    def test_original_metadata_versions_edges_assets_and_aliases_are_exact(self):
        framework, core = '.NETCoreApp,Version=v10.0', 'FS.GG.Coord.Core/1.0.0'
        def missing(value):
            del value['libraries'][core]
        def alternate(value):
            value['libraries']['FS.GG.Coord.Core/0.108.0'] = value['libraries'][core]
        def edge(value):
            value['targets'][framework]['FS.GG.Coord.Cli/0.108.0']['dependencies']['FS.GG.Coord.Core'] = '0.108.0'
        def asset(value):
            value['targets'][framework][core]['runtime'] = {'renamed.dll': {}}
        def annotation(value):
            value['targets'][framework][core]['runtime']['FS.GG.Coord.Core.dll'] = {'fileVersion': '99.0.0.0'}
        def alias(value):
            value['targets'][framework]['unrelated/1.0.0'] = {'runtime': {'FS.GG.Coord.Core.dll': {}}}
        for mutate in (missing, alternate, edge, asset, annotation, alias):
            changed = json.loads(json.dumps(self.deps))
            mutate(changed)
            self.write(deps=changed)
            with self.subTest(mutation=mutate.__name__), self.assertRaisesRegex(ValueError, 'metadata'):
                CURRENT['package_members'](self.package, self.pin)

    def test_fixed_pin_absence_and_duplicate_json_refuse(self):
        # Only this disposable root is missing the pin. A later genuine source
        # pin in the production repository must not invalidate this control.
        fixed = self.root / CURRENT['PIN_PATH']
        self.assertFalse(fixed.exists())
        with self.assertRaisesRegex(ValueError, 'pin absent'):
            CURRENT['load_pin'](self.root)
        (self.root / 'arbitrary-pin.json').write_text(json.dumps(self.pin))
        with self.assertRaisesRegex(ValueError, 'pin absent'):
            CURRENT['load_pin'](self.root)
        fixed.parent.mkdir()
        fixed.write_text(json.dumps(self.pin))
        self.assertEqual(CURRENT['load_pin'](self.root), self.pin)
        with self.assertRaisesRegex(ValueError, 'duplicate JSON'):
            CURRENT['read_json'](b'{"mode":"published-dependency","mode":"source-only"}')

    def test_actual_artifact_reader_checks_body_before_extraction_and_checker(self):
        spec = importlib.util.spec_from_file_location('current_artifact_control', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(verifier)
        evidence = self.evidence()
        manifest = {'sourceSha': 'a' * 40, 'version': '0.19.0', 'archiveSha256': evidence['archiveSha256']}
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1',
                    'expired': False, 'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                    'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        injected = {**CURRENT, 'protected_pin': lambda source, **kwargs: self.pin}
        for index, body in enumerate((evidence, {}, {**evidence, 'mode': 'source-only'})):
            archive = self.root / f'candidate{index}.zip'
            with zipfile.ZipFile(archive, 'w') as zipped:
                zipped.writestr('manifest.json', json.dumps(manifest))
                zipped.writestr('package-evidence.json', json.dumps(body))
                zipped.writestr(self.package.name, self.package.read_bytes())
            artifact['digest'] = 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()
            output = self.root / f'output{index}'
            with patch.object(verifier.runpy, 'run_path', return_value=injected), patch.object(verifier.subprocess, 'run') as checker:
                if index == 0:
                    verifier.verify(artifact, run, archive, output, 'a' * 40,
                                    release_binding=CURRENT_019)
                    checker.assert_called_once()
                else:
                    with self.assertRaisesRegex(ValueError, 'evidence body'):
                        verifier.verify(artifact, run, archive, output, 'a' * 40,
                                    release_binding=CURRENT_019)
                    checker.assert_not_called()
                    self.assertFalse(output.exists())

    def test_candidate_has_only_staged_release_mode_and_preflights_before_sdk(self):
        source = (ROOT / '.github/workflows/release-new-sdd-workspace-successor-candidate.yml').read_text()
        def validate(workflow):
            required = ('FSGG_CREATOR_DEPENDENCY_MODE: published-dependency',
                        '--release-qualification-0231 --board-package',
                        'bash "$work/creator/tests/new-sdd-workspace/run.sh"',
                        '-p:FsggCurrentCoordDependencies=true -p:BuildProjectReferences=false',
                        '-p:CompileUsingReferenceAssemblies=false',
                        '-p:FsggFrozenCoordSource="$work/producer" -p:FsggFrozenCoordDependencies="$work/dependencies"',
                        '--published-coord-dependencies "$work/dependencies" --published-coord-source "$work/producer"',
                        '--creator-source "$work/creator"', '-c Release --no-build')
            for token in required:
                if workflow.count(token) != 1:
                    raise ValueError('published candidate path differs: ' + token)
            if ('--artifacts-path' in workflow or '--build-artifacts' in workflow
                    or workflow.count('python3 tests/new-sdd-workspace-successor/run.py\n') != 1
                    or workflow.index('python3 tests/new-sdd-workspace-successor/run.py\n') > workflow.index('Acquire verified promoted originals')
                    or workflow.count('creator-current-coord-dependencies.py preflight') != 1
                    or workflow.index('creator-current-coord-dependencies.py preflight') > workflow.index('actions/setup-dotnet')):
                raise ValueError('published candidate preflight/output route differs')
        validate(source)
        for original, changed in (('--release-qualification-0231', '--source-qualification-0231'),
                                  ('published-dependency', 'source-only'),
                                  ('-p:BuildProjectReferences=false', '-p:BuildProjectReferences=true'),
                                  ('--no-build', '--artifacts-path stale'),
                                  ('creator-current-coord-dependencies.py preflight', ':'),
                                  ('python3 tests/new-sdd-workspace-successor/run.py\n', '# omitted profile controls\n'),
                                  ('--creator-source "$work/creator"', '')):
            with self.subTest(original=original), self.assertRaises(ValueError):
                validate(source.replace(original, changed))
        selftest = (ROOT / '.github/workflows/new-sdd-workspace-selftest.yml').read_text()
        self.assertEqual(selftest.count('"scripts/creator-current-coord-dependencies.*"'), 2)

    def test_real_publisher_entry_refuses_missing_pin_before_any_provider_or_journal(self):
        spec = importlib.util.spec_from_file_location('current_publisher_fence', ROOT / 'scripts/new-sdd-workspace-successor-publish.py')
        publisher = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(publisher)
        argv = ['publisher', '--preflight-only', '--candidate-run-id', '1', '--candidate-artifact-id', '2',
                '--candidate-archive-sha256', 'd' * 64, '--workdir', str(self.root / 'uncreated')]
        injected = {**CURRENT, 'load_pin': lambda **kwargs: CURRENT['load_pin'](self.root, release_binding=CURRENT_0231)}
        with patch.object(sys, 'argv', argv), patch.object(runpy, 'run_path', return_value=injected), \
                patch.object(publisher, 'GitHubAPI') as api, \
                patch.object(publisher, 'WizardProvider') as provider, \
                patch.object(publisher, 'ProtectedReleaseJournal') as journal, \
                patch.object(publisher.subprocess, 'run', side_effect=AssertionError('missing pin may not launch children')), \
                contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(publisher.main(), 1)
        api.assert_not_called()
        provider.assert_not_called()
        journal.assert_not_called()
        self.assertFalse((self.root / 'uncreated').exists())
        summary = json.loads((self.root / 'creator0231-publication-summary.json').read_text())
        self.assertFalse(summary['preflightPassed'])
        self.assertFalse(summary['journalMutationMayHaveOccurred'])
        self.assertEqual(summary['firstCause']['kind'], 'ValueError')

    def test_candidate_and_publisher_source_pin_are_independently_joined(self):
        function = CURRENT['protected_pin']
        with patch.dict(function.__globals__, {'load_pin': lambda **kwargs: self.pin}), \
                patch.object(function.__globals__['subprocess'], 'check_output', return_value=json.dumps(self.pin).encode()) as reads:
            self.assertEqual(function('a' * 40), self.pin)
            self.assertEqual(reads.call_count, 2)
        wrong = {**self.pin, 'sourceSha': 'f' * 40}
        with patch.dict(function.__globals__, {'load_pin': lambda **kwargs: self.pin}), \
                patch.object(function.__globals__['subprocess'], 'check_output', side_effect=[json.dumps(self.pin).encode(), json.dumps(wrong).encode()]):
            with self.assertRaisesRegex(ValueError, 'protected publisher source'):
                function('a' * 40)

    def test_same_size_exact_byte_mutant_breaks_negative_control(self):
        changed = dict(self.members)
        name = 'FS.GG.Coord.GitHub.dll'
        changed[name] = b'X' + changed[name][1:]
        self.write(changed)
        with self.assertRaises(ValueError):
            CURRENT['package_members'](self.package, self.pin)
        real = CURRENT['package_members'].__globals__['digest']
        expected = self.pin['members'][name]
        def relaxed(body):
            return expected if body == changed[name] else real(body)
        with patch.dict(CURRENT['package_members'].__globals__, {'digest': relaxed}):
            # The weakened predicate lets the bad package escape; the same negative assertion fails.
            with self.assertRaises(AssertionError):
                with self.assertRaises(ValueError):
                    CURRENT['package_members'](self.package, self.pin)



class Current021Dependencies(unittest.TestCase):
    """Retained published021 release proof; the current route selects022 below."""
    def setUp(self):
        self.fixture = CurrentDependencies()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        f = self.fixture
        # Explicit synthetic unexecutable110 producer, not a public receipt.
        f.pin = json.loads(json.dumps(f.pin).replace('0.108.0', '0.110.0'))
        f.producer = json.loads(json.dumps(f.producer).replace('0.108.0', '0.110.0'))
        f.members['fsgg-coord-engine.deps.json'] = json.dumps(f.producer).encode()
        f.pin['members'] = {name: hashlib.sha256(body).hexdigest() for name, body in f.members.items()}
        f.deps = json.loads(json.dumps(f.deps).replace('0.108.0', '0.110.0').replace('0.19.0', '0.21.0'))
        f.package = f.root / 'FS.GG.NewSddWorkspace.0.21.0.nupkg'
        self.nuspec = (b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.21.0</version>'
                       b'<repository commit="' + b'a' * 40 + b'"/></metadata></package>')
        self.write()

    def write(self, **kwargs):
        self.fixture.write(extra={'FS.GG.NewSddWorkspace.nuspec': self.nuspec}, **kwargs)

    def proof(self):
        f = self.fixture
        return CURRENT['release_evidence'](f.package, 'a' * 40, f.pin,
                                          fixtures_passed=True, release_binding=CURRENT_021)

    def test021_release_recomputed_and_source_or_prior_evidence_never_upgraded(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_021.package, 'version': CURRENT_021.version,
                    'tag': CURRENT_021.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        self.assertEqual((proof['version'], proof['coherentVersion']), ('0.21.0', '0.110.0'))
        self.assertEqual(proof['schema'], 'fsgg.creator021-release-qualification/1')
        self.assertEqual(proof['dependencyMembers'], f.pin['members'])
        self.assertFalse(proof['sourceQualificationOnly'])
        self.assertTrue(proof['releaseEligible'])
        verify = CURRENT['verify_release_evidence']
        self.assertEqual(verify(proof, f.package, manifest, f.pin, release_binding=CURRENT_021), proof)
        for body in ({**proof, 'schema': 'fsgg.creator020-release-qualification/1'},
                     {**proof, 'sourceQualificationOnly': True}, {**proof, 'releaseEligible': False},
                     {**proof, 'coherentVersion': '0.108.0'}, {**proof, 'fixturesPassed': False},
                     {**proof, 'dependencyMembers': {}}, {**proof, 'dependencyPinSha256': '0' * 64},
                     {**proof, 'publishedDependencySourceSha': 'c' * 40}, {**proof, 'unknown': True}):
            with self.subTest(body=body), self.assertRaises(ValueError):
                verify(body, f.package, manifest, f.pin, release_binding=CURRENT_021)
        from creator_source_qualification import SOURCE_021
        for binding in (SOURCE_020, SOURCE_021, ReleaseBinding(CURRENT_021.package, CURRENT_021.version, CURRENT_021.tag)):
            with self.subTest(binding=binding), self.assertRaises(ValueError):
                CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, release_binding=binding)
        with self.assertRaises(ValueError):
            CURRENT['evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, source_binding=SOURCE_021)
        with self.assertRaises(ValueError):
            CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=False, release_binding=CURRENT_021)
        for key, value in (('tag', CURRENT_020.tag), ('version', CURRENT_020.version), ('packageId', 'foreign'),
                           ('sourceSha', 'b' * 40), ('archiveSha256', '0' * 64)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify(proof, f.package, {**manifest, key: value}, f.pin, release_binding=CURRENT_021)

    def test021_missing_changed_shadow_and_stale_metadata_are_causal(self):
        f = self.fixture
        for defect in ('missing', 'changed', 'shadow', 'old-coherent', 'wrong-own-version'):
            members = dict(f.members); deps = json.loads(json.dumps(f.deps)); extra = {}
            if defect == 'missing': members.pop('FS.GG.Coord.GitHub.dll')
            if defect == 'changed': members['FS.GG.Coord.GitHub.dll'] += b' mutation'
            if defect == 'shadow': extra['foreign/FS.GG.Coord.GitHub.dll'] = b' shadow'
            if defect == 'old-coherent': deps = json.loads(json.dumps(deps).replace('0.110.0', '0.108.0'))
            if defect == 'wrong-own-version': deps = json.loads(json.dumps(deps).replace('0.21.0', '0.20.0'))
            f.write(members=members, deps=deps, extra={**extra, 'FS.GG.NewSddWorkspace.nuspec': self.nuspec})
            with self.subTest(defect=defect), self.assertRaises(ValueError): self.proof()
        self.write()
        self.proof()

    def test021_fixed_pin_cannot_select108_or_arbitrary_input(self):
        f = self.fixture
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_021)
        old = f.root / CURRENT['PIN_PATH']; old.parent.mkdir(); old.write_text(json.dumps(f.pin))
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_021)
        new = f.root / CURRENT['PIN_021_PATH']; new.write_text(json.dumps(f.pin))
        self.assertEqual(CURRENT['load_pin'](f.root, release_binding=CURRENT_021), f.pin)
        for wrong in (CURRENT_020, SOURCE_020, ReleaseBinding(CURRENT_021.package, CURRENT_021.version, CURRENT_021.tag)):
            with self.subTest(binding=wrong), self.assertRaises(ValueError):
                CURRENT['validate_pin'](f.pin, release_binding=wrong)

    def test021_genuine_fixed_coordinates_and_complete_source_census(self):
        pin = CURRENT['load_pin'](release_binding=CURRENT_021)
        self.assertEqual((pin['version'], pin['sourceSha'], pin['releaseId'], pin['assetId'], pin['promotedManifestAssetId']),
                         ('0.110.0', '42cd9e7c8505f7cd72d5a22ad424a27af6d51458', 418406702, 659535832, 659550415))
        self.assertEqual(pin['archiveSha256'], '1a05120c8065ca42348c9feadc9fcfe73bf8d5861ca4b13f1f4891fdad4fdea9')
        self.assertEqual(pin['promotedManifestSha256'], 'bcae24d71571ab7f0dc26abbbf00beb0abf12718b22e7060838990384b86911f')
        self.assertEqual((len(pin['members']), len(pin['projects']), len(pin['sourceLeaves'])), (81, 11, 293))
        # Census the immutable producer, never require current main to remain42.
        producer = self.fixture.root / 'genuine110-producer-source'
        subprocess.run(['git', 'clone', '--shared', '--no-checkout', str(ROOT), str(producer)],
                       check=True, capture_output=True, timeout=15)
        subprocess.run(['git', 'config', 'core.sparseCheckout', 'true'], cwd=producer,
                       check=True, capture_output=True, timeout=10)
        (producer / '.git/info/sparse-checkout').write_text(''.join('/' + row['path'] + '\n' for row in pin['sourceLeaves']))
        subprocess.run(['git', 'checkout', '--detach', pin['sourceSha']], cwd=producer,
                       check=True, capture_output=True, timeout=15)
        self.assertEqual(CURRENT['old']['source_projects'](ROOT, pin, producer), pin['projects'])
        # This fixed historical pin remains a separate genuine108 document.
        historical = CURRENT['load_pin']()
        self.assertEqual(historical['version'], '0.108.0')
        self.assertNotEqual(CURRENT['pin_digest'](historical), CURRENT['pin_digest'](pin))


    def test021_artifact_native_and_evidence_joins_before_package_checker(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_021.package, 'version': CURRENT_021.version,
                    'tag': CURRENT_021.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        spec = importlib.util.spec_from_file_location('current021_artifact', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1',
                    'expired': False, 'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                    'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        def protected(source, *, release_binding):
            self.assertEqual(source, 'a' * 40); self.assertIs(release_binding, CURRENT_021)
            return f.pin
        original = runpy.run_path
        def load(path, *args, **kwargs):
            if str(path).endswith('creator-current-coord-dependencies.py'):
                return {**CURRENT, 'protected_pin': protected}
            return original(path, *args, **kwargs)
        for index, defect in enumerate((None, 'source-only', 'historical-proof', 'rerun', 'source', 'workflow', 'expired')):
            body = dict(proof); observed_run = dict(run); observed_artifact = dict(artifact)
            if defect == 'source-only': body.update(sourceQualificationOnly=True, releaseEligible=False)
            if defect == 'historical-proof': body['schema'] = 'fsgg.creator020-release-qualification/1'
            if defect == 'rerun': observed_run['run_attempt'] = 2
            if defect == 'source': observed_run['head_sha'] = 'b' * 40
            if defect == 'workflow': observed_run['path'] = 'other.yml'
            if defect == 'expired': observed_artifact['expired'] = True
            archive = f.root / f'021-candidate-{index}.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('manifest.json', json.dumps(manifest))
                z.writestr('package-evidence.json', json.dumps(body))
                z.writestr(f.package.name, f.package.read_bytes())
            observed_artifact['digest'] = 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()
            output = f.root / f'021-output-{index}'
            with self.subTest(defect=defect), patch.object(verifier.runpy, 'run_path', side_effect=load), \
                    patch.object(verifier.subprocess, 'run') as checker:
                if defect is None:
                    self.assertEqual(verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_021), manifest)
                    checker.assert_called_once()
                else:
                    with self.assertRaises(ValueError): verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_021)
                    checker.assert_not_called(); self.assertFalse(output.exists())

    def test021_ordinary_authority_excludes020_and_source_bindings(self):
        from new_sdd_workspace_successor_admission import WizardAdmission
        from new_sdd_workspace_successor_execution import effects
        class NoReads:
            def get(self, path): raise AssertionError('historical identity must refuse before native reads')
        historical = {'schema': 'fsgg.new-sdd-workspace-release/1', 'packageId': CURRENT_020.package,
                      'version': CURRENT_020.version, 'tag': CURRENT_020.tag, 'sourceSha': 'a' * 40,
                      'archiveSha256': 'b' * 64, 'producerPayloadSha256': 'sha256:' + 'c' * 64}
        # Retained descriptors/eight-effect readback do not admit ordinary publication.
        self.assertEqual(len(effects(historical, binding=CURRENT_020)[1]), 8)
        admission = WizardAdmission(NoReads(), historical, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=CURRENT_020)
        self.assertFalse(admission.authorize(admission.content_id, 'journal', 'intent', admission.content_id))


    def test021_actual_qualification_cli_and_source_only_boundary(self):
        f = self.fixture
        creator = f.root / 'creator'
        output = creator / 'scripts/NewSddWorkspace/bin/Release/net10.0'
        output.mkdir(parents=True)
        with zipfile.ZipFile(f.package, 'a') as z:
            z.writestr(PREFIX + 'new-sdd-workspace.dll', b'explicit inert creator output')
            z.writestr(PREFIX + 'new-sdd-workspace.runtimeconfig.json', b'{}')
        with zipfile.ZipFile(f.package) as z:
            for name in z.namelist():
                if name.startswith(PREFIX):
                    target = output / name[len(PREFIX):]
                    target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(z.read(name))
        checker = runpy.run_path(str(ROOT / 'scripts/new-sdd-workspace-release.py'))
        manifest = checker['build_manifest'](type('Args', (), {'package': str(f.package),
            'version': CURRENT_021.version, 'tag': CURRENT_021.tag, 'source_sha': 'a' * 40})())
        manifest_path = f.root / 'manifest.json'
        real_run = runpy.run_path
        def protected(source, *, release_binding):
            self.assertIs(release_binding, CURRENT_021); self.assertEqual(source, 'a' * 40)
            return f.pin
        injected = {**CURRENT, 'protected_pin': protected,
                    'old': {**CURRENT['old'], 'layout': lambda *args: {'currentCreatorSourceSha': 'a' * 40}}}
        def load(path, *args, **kwargs):
            return injected if str(path).endswith('creator-current-coord-dependencies.py') else real_run(path, *args, **kwargs)
        argv = ['qualification', '--release-qualification-021', '--board-package', str(f.package),
                '--source-sha', 'a' * 40, '--release-manifest', str(manifest_path),
                '--published-coord-source', str(f.root / 'producer'),
                '--published-coord-dependencies', str(f.root / 'dependencies'), '--creator-source', str(creator)]
        own = output / 'new-sdd-workspace.dll'; original = own.read_bytes()
        for defect in (None, 'missing-output', 'changed-output', 'changed-source', 'archive', 'payload'):
            own.write_bytes(original); body = dict(manifest)
            injected['old']['layout'] = lambda *args: {'currentCreatorSourceSha': 'b' * 40 if defect == 'changed-source' else 'a' * 40}
            if defect == 'missing-output': own.unlink()
            if defect == 'changed-output': own.write_bytes(b'changed inert compiled output')
            if defect == 'archive': body['archiveSha256'] = '0' * 64
            if defect == 'payload': body['producerPayloadSha256'] = 'sha256:' + '0' * 64
            manifest_path.write_text(json.dumps(body)); captured = io.StringIO()
            with self.subTest(defect=defect), patch.object(sys, 'argv', argv), \
                    patch.object(runpy, 'run_path', side_effect=load), contextlib.redirect_stdout(captured):
                if defect is None:
                    with self.assertRaises(SystemExit) as ended:
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
                    self.assertEqual(ended.exception.code, 0)
                    proof = json.loads(captured.getvalue())
                    self.assertEqual((proof['version'], proof['coherentVersion']), ('0.21.0', '0.110.0'))
                    self.assertEqual(proof['schema'], 'fsgg.creator021-release-qualification/1')
                    self.assertTrue(proof['releaseEligible']); self.assertFalse(proof['sourceQualificationOnly'])
                else:
                    with self.assertRaises(ValueError):
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
        # Missing published roots refuse through the actual CLI before any input or compiler read.
        with patch.object(sys, 'argv', argv[:6]), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as ended:
                real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
            self.assertEqual(ended.exception.code, 2)


class Current022Dependencies(unittest.TestCase):
    """Retained published022 release proof over genuine promoted112; the current route selects023 below."""
    def setUp(self):
        self.fixture = CurrentDependencies()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        f = self.fixture
        # Explicit synthetic unexecutable112 producer, not a public receipt.
        f.pin = json.loads(json.dumps(f.pin).replace('0.108.0', '0.112.0'))
        f.producer = json.loads(json.dumps(f.producer).replace('0.108.0', '0.112.0'))
        f.members['fsgg-coord-engine.deps.json'] = json.dumps(f.producer).encode()
        f.pin['members'] = {name: hashlib.sha256(body).hexdigest() for name, body in f.members.items()}
        f.deps = json.loads(json.dumps(f.deps).replace('0.108.0', '0.112.0').replace('0.19.0', '0.22.0'))
        f.package = f.root / 'FS.GG.NewSddWorkspace.0.22.0.nupkg'
        self.nuspec = (b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.22.0</version>'
                       b'<repository commit="' + b'a' * 40 + b'"/></metadata></package>')
        self.write()

    def write(self, **kwargs):
        self.fixture.write(extra={'FS.GG.NewSddWorkspace.nuspec': self.nuspec}, **kwargs)

    def proof(self):
        f = self.fixture
        return CURRENT['release_evidence'](f.package, 'a' * 40, f.pin,
                                          fixtures_passed=True, release_binding=CURRENT_022)

    def test022_release_recomputed_and_source_or_prior_evidence_never_upgraded(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_022.package, 'version': CURRENT_022.version,
                    'tag': CURRENT_022.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        self.assertEqual((proof['version'], proof['coherentVersion']), ('0.22.0', '0.112.0'))
        self.assertEqual(proof['schema'], 'fsgg.creator022-release-qualification/1')
        self.assertEqual(proof['dependencyMembers'], f.pin['members'])
        self.assertFalse(proof['sourceQualificationOnly'])
        self.assertTrue(proof['releaseEligible'])
        verify = CURRENT['verify_release_evidence']
        self.assertEqual(verify(proof, f.package, manifest, f.pin, release_binding=CURRENT_022), proof)
        for body in ({**proof, 'schema': 'fsgg.creator021-release-qualification/1'},
                     {**proof, 'sourceQualificationOnly': True}, {**proof, 'releaseEligible': False},
                     {**proof, 'coherentVersion': '0.110.0'}, {**proof, 'fixturesPassed': False},
                     {**proof, 'dependencyMembers': {}}, {**proof, 'dependencyPinSha256': '0' * 64},
                     {**proof, 'publishedDependencySourceSha': 'c' * 40}, {**proof, 'unknown': True}):
            with self.subTest(body=body), self.assertRaises(ValueError):
                verify(body, f.package, manifest, f.pin, release_binding=CURRENT_022)
        from creator_source_qualification import SOURCE_021, SOURCE_022
        for binding in (SOURCE_020, SOURCE_021, SOURCE_022, ReleaseBinding(CURRENT_022.package, CURRENT_022.version, CURRENT_022.tag)):
            with self.subTest(binding=binding), self.assertRaises(ValueError):
                CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, release_binding=binding)
        with self.assertRaises(ValueError):
            CURRENT['evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, source_binding=SOURCE_022)
        with self.assertRaises(ValueError):
            CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=False, release_binding=CURRENT_022)
        for key, value in (('tag', CURRENT_021.tag), ('version', CURRENT_021.version), ('packageId', 'foreign'),
                           ('sourceSha', 'b' * 40), ('archiveSha256', '0' * 64)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify(proof, f.package, {**manifest, key: value}, f.pin, release_binding=CURRENT_022)

    def test022_missing_changed_shadow_and_stale_metadata_are_causal(self):
        f = self.fixture
        for defect in ('missing', 'changed', 'shadow', 'old-coherent', 'wrong-own-version'):
            members = dict(f.members); deps = json.loads(json.dumps(f.deps)); extra = {}
            if defect == 'missing': members.pop('FS.GG.Coord.GitHub.dll')
            if defect == 'changed': members['FS.GG.Coord.GitHub.dll'] += b' mutation'
            if defect == 'shadow': extra['foreign/FS.GG.Coord.GitHub.dll'] = b' shadow'
            if defect == 'old-coherent': deps = json.loads(json.dumps(deps).replace('0.112.0', '0.110.0'))
            if defect == 'wrong-own-version': deps = json.loads(json.dumps(deps).replace('0.22.0', '0.21.0'))
            f.write(members=members, deps=deps, extra={**extra, 'FS.GG.NewSddWorkspace.nuspec': self.nuspec})
            with self.subTest(defect=defect), self.assertRaises(ValueError): self.proof()
        self.write()
        self.proof()

    def test022_fixed_pin_cannot_select110_or_arbitrary_input(self):
        f = self.fixture
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_022)
        old = f.root / CURRENT['PIN_021_PATH']; old.parent.mkdir(); old.write_text(json.dumps(f.pin))
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_022)
        new = f.root / CURRENT['PIN_022_PATH']; new.write_text(json.dumps(f.pin))
        self.assertEqual(CURRENT['load_pin'](f.root, release_binding=CURRENT_022), f.pin)
        for wrong in (CURRENT_021, CURRENT_020, SOURCE_020, ReleaseBinding(CURRENT_022.package, CURRENT_022.version, CURRENT_022.tag)):
            with self.subTest(binding=wrong), self.assertRaises(ValueError):
                CURRENT['validate_pin'](f.pin, release_binding=wrong)

    def test022_genuine_fixed_coordinates_and_complete_source_census(self):
        pin = CURRENT['load_pin'](release_binding=CURRENT_022)
        self.assertEqual((pin['version'], pin['sourceSha'], pin['releaseId'], pin['assetId'], pin['promotedManifestAssetId']),
                         ('0.112.0', 'd5af6a987e915bd0a1b572bcc09ad0d4b2e798b5', 419781177, 663223337, 663236885))
        self.assertEqual(pin['archiveSha256'], '7f65b2d6ef2bcb1241b1868916d32f168f9937b5f825b93105c556d50c9d7b1f')
        self.assertEqual(pin['promotedManifestSha256'], '0c9efdcc85cc7137c81b456bf8e536dcd45d7017c07f8a6409984e82fe2c4aa5')
        self.assertEqual((len(pin['members']), len(pin['projects']), len(pin['sourceLeaves'])), (81, 11, 293))
        # Census the immutable producer, never require current main to remain d5af.
        producer = self.fixture.root / 'genuine112-producer-source'
        subprocess.run(['git', 'clone', '--shared', '--no-checkout', str(ROOT), str(producer)],
                       check=True, capture_output=True, timeout=15)
        subprocess.run(['git', 'config', 'core.sparseCheckout', 'true'], cwd=producer,
                       check=True, capture_output=True, timeout=10)
        (producer / '.git/info/sparse-checkout').write_text(''.join('/' + row['path'] + '\n' for row in pin['sourceLeaves']))
        subprocess.run(['git', 'checkout', '--detach', pin['sourceSha']], cwd=producer,
                       check=True, capture_output=True, timeout=15)
        self.assertEqual(CURRENT['old']['source_projects'](ROOT, pin, producer), pin['projects'])
        # The fixed retained021 pin remains a separate genuine110 document.
        historical = CURRENT['load_pin'](release_binding=CURRENT_021)
        self.assertEqual(historical['version'], '0.110.0')
        self.assertNotEqual(CURRENT['pin_digest'](historical), CURRENT['pin_digest'](pin))


    def test022_artifact_native_and_evidence_joins_before_package_checker(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_022.package, 'version': CURRENT_022.version,
                    'tag': CURRENT_022.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        spec = importlib.util.spec_from_file_location('current022_artifact', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1',
                    'expired': False, 'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                    'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        def protected(source, *, release_binding):
            self.assertEqual(source, 'a' * 40); self.assertIs(release_binding, CURRENT_022)
            return f.pin
        original = runpy.run_path
        def load(path, *args, **kwargs):
            if str(path).endswith('creator-current-coord-dependencies.py'):
                return {**CURRENT, 'protected_pin': protected}
            return original(path, *args, **kwargs)
        for index, defect in enumerate((None, 'source-only', 'historical-proof', 'rerun', 'source', 'workflow', 'expired')):
            body = dict(proof); observed_run = dict(run); observed_artifact = dict(artifact)
            if defect == 'source-only': body.update(sourceQualificationOnly=True, releaseEligible=False)
            if defect == 'historical-proof': body['schema'] = 'fsgg.creator021-release-qualification/1'
            if defect == 'rerun': observed_run['run_attempt'] = 2
            if defect == 'source': observed_run['head_sha'] = 'b' * 40
            if defect == 'workflow': observed_run['path'] = 'other.yml'
            if defect == 'expired': observed_artifact['expired'] = True
            archive = f.root / f'022-candidate-{index}.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('manifest.json', json.dumps(manifest))
                z.writestr('package-evidence.json', json.dumps(body))
                z.writestr(f.package.name, f.package.read_bytes())
            observed_artifact['digest'] = 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()
            output = f.root / f'022-output-{index}'
            with self.subTest(defect=defect), patch.object(verifier.runpy, 'run_path', side_effect=load), \
                    patch.object(verifier.subprocess, 'run') as checker:
                if defect is None:
                    self.assertEqual(verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_022), manifest)
                    checker.assert_called_once()
                else:
                    with self.assertRaises(ValueError): verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_022)
                    checker.assert_not_called(); self.assertFalse(output.exists())

    def test022_ordinary_authority_excludes_retained021(self):
        from new_sdd_workspace_successor_admission import WizardAdmission
        from new_sdd_workspace_successor_execution import effects
        class NoReads:
            def get(self, path): raise AssertionError('historical identity must refuse before native reads')
        historical = {'schema': 'fsgg.new-sdd-workspace-release/1', 'packageId': CURRENT_021.package,
                      'version': CURRENT_021.version, 'tag': CURRENT_021.tag, 'sourceSha': 'a' * 40,
                      'archiveSha256': 'b' * 64, 'producerPayloadSha256': 'sha256:' + 'c' * 64}
        # Retained descriptors/eight-effect readback do not admit ordinary publication.
        self.assertEqual(len(effects(historical, binding=CURRENT_021)[1]), 8)
        admission = WizardAdmission(NoReads(), historical, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=CURRENT_021)
        self.assertFalse(admission.authorize(admission.content_id, 'journal', 'intent', admission.content_id))


    def test022_actual_qualification_cli_and_source_only_boundary(self):
        f = self.fixture
        creator = f.root / 'creator'
        output = creator / 'scripts/NewSddWorkspace/bin/Release/net10.0'
        output.mkdir(parents=True)
        with zipfile.ZipFile(f.package, 'a') as z:
            z.writestr(PREFIX + 'new-sdd-workspace.dll', b'explicit inert creator output')
            z.writestr(PREFIX + 'new-sdd-workspace.runtimeconfig.json', b'{}')
        with zipfile.ZipFile(f.package) as z:
            for name in z.namelist():
                if name.startswith(PREFIX):
                    target = output / name[len(PREFIX):]
                    target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(z.read(name))
        checker = runpy.run_path(str(ROOT / 'scripts/new-sdd-workspace-release.py'))
        manifest = checker['build_manifest'](type('Args', (), {'package': str(f.package),
            'version': CURRENT_022.version, 'tag': CURRENT_022.tag, 'source_sha': 'a' * 40})())
        manifest_path = f.root / 'manifest.json'
        real_run = runpy.run_path
        def protected(source, *, release_binding):
            self.assertIs(release_binding, CURRENT_022); self.assertEqual(source, 'a' * 40)
            return f.pin
        injected = {**CURRENT, 'protected_pin': protected,
                    'old': {**CURRENT['old'], 'layout': lambda *args: {'currentCreatorSourceSha': 'a' * 40}}}
        def load(path, *args, **kwargs):
            return injected if str(path).endswith('creator-current-coord-dependencies.py') else real_run(path, *args, **kwargs)
        argv = ['qualification', '--release-qualification-022', '--board-package', str(f.package),
                '--source-sha', 'a' * 40, '--release-manifest', str(manifest_path),
                '--published-coord-source', str(f.root / 'producer'),
                '--published-coord-dependencies', str(f.root / 'dependencies'), '--creator-source', str(creator)]
        own = output / 'new-sdd-workspace.dll'; original = own.read_bytes()
        for defect in (None, 'missing-output', 'changed-output', 'changed-source', 'archive', 'payload'):
            own.write_bytes(original); body = dict(manifest)
            injected['old']['layout'] = lambda *args: {'currentCreatorSourceSha': 'b' * 40 if defect == 'changed-source' else 'a' * 40}
            if defect == 'missing-output': own.unlink()
            if defect == 'changed-output': own.write_bytes(b'changed inert compiled output')
            if defect == 'archive': body['archiveSha256'] = '0' * 64
            if defect == 'payload': body['producerPayloadSha256'] = 'sha256:' + '0' * 64
            manifest_path.write_text(json.dumps(body)); captured = io.StringIO()
            with self.subTest(defect=defect), patch.object(sys, 'argv', argv), \
                    patch.object(runpy, 'run_path', side_effect=load), contextlib.redirect_stdout(captured):
                if defect is None:
                    with self.assertRaises(SystemExit) as ended:
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
                    self.assertEqual(ended.exception.code, 0)
                    proof = json.loads(captured.getvalue())
                    self.assertEqual((proof['version'], proof['coherentVersion']), ('0.22.0', '0.112.0'))
                    self.assertEqual(proof['schema'], 'fsgg.creator022-release-qualification/1')
                    self.assertTrue(proof['releaseEligible']); self.assertFalse(proof['sourceQualificationOnly'])
                else:
                    with self.assertRaises(ValueError):
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
        # Missing published roots refuse through the actual CLI before any input or compiler read.
        with patch.object(sys, 'argv', argv[:6]), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as ended:
                real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
            self.assertEqual(ended.exception.code, 2)


class Current023Dependencies(unittest.TestCase):
    """Separate current023 release proof over genuine promoted113; retained022 and 021 stay historical."""
    def setUp(self):
        self.fixture = CurrentDependencies()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        f = self.fixture
        # Explicit synthetic unexecutable113 producer, not a public receipt.
        f.pin = json.loads(json.dumps(f.pin).replace('0.108.0', '0.113.0'))
        f.producer = json.loads(json.dumps(f.producer).replace('0.108.0', '0.113.0'))
        f.members['fsgg-coord-engine.deps.json'] = json.dumps(f.producer).encode()
        f.pin['members'] = {name: hashlib.sha256(body).hexdigest() for name, body in f.members.items()}
        f.deps = json.loads(json.dumps(f.deps).replace('0.108.0', '0.113.0').replace('0.19.0', '0.23.0'))
        f.package = f.root / 'FS.GG.NewSddWorkspace.0.23.0.nupkg'
        self.nuspec = (b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.23.0</version>'
                       b'<repository commit="' + b'a' * 40 + b'"/></metadata></package>')
        self.write()

    def write(self, **kwargs):
        self.fixture.write(extra={'FS.GG.NewSddWorkspace.nuspec': self.nuspec}, **kwargs)

    def proof(self):
        f = self.fixture
        return CURRENT['release_evidence'](f.package, 'a' * 40, f.pin,
                                          fixtures_passed=True, release_binding=CURRENT_023)

    def test023_release_recomputed_and_source_or_prior_evidence_never_upgraded(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_023.package, 'version': CURRENT_023.version,
                    'tag': CURRENT_023.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        self.assertEqual((proof['version'], proof['coherentVersion']), ('0.23.0', '0.113.0'))
        self.assertEqual(proof['schema'], 'fsgg.creator023-release-qualification/1')
        self.assertEqual(proof['dependencyMembers'], f.pin['members'])
        self.assertFalse(proof['sourceQualificationOnly'])
        self.assertTrue(proof['releaseEligible'])
        verify = CURRENT['verify_release_evidence']
        self.assertEqual(verify(proof, f.package, manifest, f.pin, release_binding=CURRENT_023), proof)
        for body in ({**proof, 'schema': 'fsgg.creator022-release-qualification/1'}, {**proof, 'schema': 'fsgg.creator021-release-qualification/1'},
                     {**proof, 'sourceQualificationOnly': True}, {**proof, 'releaseEligible': False},
                     {**proof, 'coherentVersion': '0.112.0'}, {**proof, 'coherentVersion': '0.110.0'}, {**proof, 'fixturesPassed': False},
                     {**proof, 'dependencyMembers': {}}, {**proof, 'dependencyPinSha256': '0' * 64},
                     {**proof, 'publishedDependencySourceSha': 'c' * 40}, {**proof, 'unknown': True}):
            with self.subTest(body=body), self.assertRaises(ValueError):
                verify(body, f.package, manifest, f.pin, release_binding=CURRENT_023)
        from creator_source_qualification import SOURCE_021, SOURCE_022, SOURCE_023
        for binding in (SOURCE_020, SOURCE_021, SOURCE_022, SOURCE_023, ReleaseBinding(CURRENT_023.package, CURRENT_023.version, CURRENT_023.tag)):
            with self.subTest(binding=binding), self.assertRaises(ValueError):
                CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, release_binding=binding)
        with self.assertRaises(ValueError):
            CURRENT['evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, source_binding=SOURCE_023)
        with self.assertRaises(ValueError):
            CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=False, release_binding=CURRENT_023)
        for key, value in (('tag', CURRENT_022.tag), ('version', CURRENT_022.version), ('tag', CURRENT_021.tag), ('version', CURRENT_021.version), ('packageId', 'foreign'),
                           ('sourceSha', 'b' * 40), ('archiveSha256', '0' * 64)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify(proof, f.package, {**manifest, key: value}, f.pin, release_binding=CURRENT_023)

    def test023_missing_changed_shadow_and_stale_metadata_are_causal(self):
        f = self.fixture
        for defect in ('missing', 'changed', 'shadow', 'old-coherent', 'wrong-own-version'):
            members = dict(f.members); deps = json.loads(json.dumps(f.deps)); extra = {}
            if defect == 'missing': members.pop('FS.GG.Coord.GitHub.dll')
            if defect == 'changed': members['FS.GG.Coord.GitHub.dll'] += b' mutation'
            if defect == 'shadow': extra['foreign/FS.GG.Coord.GitHub.dll'] = b' shadow'
            if defect == 'old-coherent': deps = json.loads(json.dumps(deps).replace('0.113.0', '0.112.0'))
            if defect == 'wrong-own-version': deps = json.loads(json.dumps(deps).replace('0.23.0', '0.22.0'))
            f.write(members=members, deps=deps, extra={**extra, 'FS.GG.NewSddWorkspace.nuspec': self.nuspec})
            with self.subTest(defect=defect), self.assertRaises(ValueError): self.proof()
        self.write()
        self.proof()

    def test023_fixed_pin_cannot_select112_110_or_arbitrary_input(self):
        f = self.fixture
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_023)
        old = f.root / CURRENT['PIN_021_PATH']; old.parent.mkdir(); old.write_text(json.dumps(f.pin))
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_023)
        prior = f.root / CURRENT['PIN_022_PATH']; prior.write_text(json.dumps(f.pin))
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_023)
        new = f.root / CURRENT['PIN_023_PATH']; new.write_text(json.dumps(f.pin))
        self.assertEqual(CURRENT['load_pin'](f.root, release_binding=CURRENT_023), f.pin)
        for wrong in (CURRENT_022, CURRENT_021, CURRENT_020, SOURCE_020, ReleaseBinding(CURRENT_023.package, CURRENT_023.version, CURRENT_023.tag)):
            with self.subTest(binding=wrong), self.assertRaises(ValueError):
                CURRENT['validate_pin'](f.pin, release_binding=wrong)

    def test023_genuine_fixed_coordinates_and_complete_source_census(self):
        pin = CURRENT['load_pin'](release_binding=CURRENT_023)
        self.assertEqual((pin['version'], pin['sourceSha'], pin['releaseId'], pin['assetId'], pin['promotedManifestAssetId']),
                         ('0.113.0', '79db622ff4a51498e22ae5820eeb2b470fc743c5', 421880935, 669319653, 669346850))
        self.assertEqual(pin['archiveSha256'], '100f0879436d41df98aa0879aa8ea279dde3e39d3119733875744ac8e2f79789')
        self.assertEqual(pin['promotedManifestSha256'], '5daf312bf999a393ba77b82508eddd50380a3f059e17ec90f433760e01f9b8f7')
        self.assertEqual((len(pin['members']), len(pin['projects']), len(pin['sourceLeaves'])), (81, 11, 293))
        # Census the immutable producer, never require current main to remain 79db.
        producer = self.fixture.root / 'genuine113-producer-source'
        subprocess.run(['git', 'clone', '--shared', '--no-checkout', str(ROOT), str(producer)],
                       check=True, capture_output=True, timeout=15)
        subprocess.run(['git', 'config', 'core.sparseCheckout', 'true'], cwd=producer,
                       check=True, capture_output=True, timeout=10)
        (producer / '.git/info/sparse-checkout').write_text(''.join('/' + row['path'] + '\n' for row in pin['sourceLeaves']))
        subprocess.run(['git', 'checkout', '--detach', pin['sourceSha']], cwd=producer,
                       check=True, capture_output=True, timeout=15)
        self.assertEqual(CURRENT['old']['source_projects'](ROOT, pin, producer), pin['projects'])
        # The fixed retained022 and 021 pins remain separate genuine112 and genuine110 documents.
        for binding, version in ((CURRENT_022, '0.112.0'), (CURRENT_021, '0.110.0')):
            historical = CURRENT['load_pin'](release_binding=binding)
            self.assertEqual(historical['version'], version)
            self.assertNotEqual(CURRENT['pin_digest'](historical), CURRENT['pin_digest'](pin))
        # 0.113.0 is the newest promoted set; the unpublished 0.114.0 source scalar is never a pin.
        self.assertEqual(CURRENT['release_pin'](CURRENT_023)[2], '0.113.0')
        self.assertEqual({row[0].version: row[2] for row in CURRENT['RELEASE_PINS']},
                         {'0.20.0': '0.108.0', '0.21.0': '0.110.0', '0.22.0': '0.112.0', '0.23.0': '0.113.0', '0.23.1': '0.113.0'})


    def test023_artifact_native_and_evidence_joins_before_package_checker(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_023.package, 'version': CURRENT_023.version,
                    'tag': CURRENT_023.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        spec = importlib.util.spec_from_file_location('current023_artifact', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1',
                    'expired': False, 'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                    'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        def protected(source, *, release_binding):
            self.assertEqual(source, 'a' * 40); self.assertIs(release_binding, CURRENT_023)
            return f.pin
        original = runpy.run_path
        def load(path, *args, **kwargs):
            if str(path).endswith('creator-current-coord-dependencies.py'):
                return {**CURRENT, 'protected_pin': protected}
            return original(path, *args, **kwargs)
        for index, defect in enumerate((None, 'source-only', 'historical-proof', 'rerun', 'source', 'workflow', 'expired')):
            body = dict(proof); observed_run = dict(run); observed_artifact = dict(artifact)
            if defect == 'source-only': body.update(sourceQualificationOnly=True, releaseEligible=False)
            if defect == 'historical-proof': body['schema'] = 'fsgg.creator022-release-qualification/1'
            if defect == 'rerun': observed_run['run_attempt'] = 2
            if defect == 'source': observed_run['head_sha'] = 'b' * 40
            if defect == 'workflow': observed_run['path'] = 'other.yml'
            if defect == 'expired': observed_artifact['expired'] = True
            archive = f.root / f'023-candidate-{index}.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('manifest.json', json.dumps(manifest))
                z.writestr('package-evidence.json', json.dumps(body))
                z.writestr(f.package.name, f.package.read_bytes())
            observed_artifact['digest'] = 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()
            output = f.root / f'023-output-{index}'
            with self.subTest(defect=defect), patch.object(verifier.runpy, 'run_path', side_effect=load), \
                    patch.object(verifier.subprocess, 'run') as checker:
                if defect is None:
                    self.assertEqual(verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_023), manifest)
                    checker.assert_called_once()
                else:
                    with self.assertRaises(ValueError): verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40, release_binding=CURRENT_023)
                    checker.assert_not_called(); self.assertFalse(output.exists())

    def test023_ordinary_authority_excludes_retained022_021_and_020(self):
        from new_sdd_workspace_successor_admission import WizardAdmission
        from new_sdd_workspace_successor_execution import effects
        class NoReads:
            def get(self, path): raise AssertionError('historical identity must refuse before native reads')
        for retained in (CURRENT_022, CURRENT_021, CURRENT_020):
            with self.subTest(binding=retained.version):
                historical = {'schema': 'fsgg.new-sdd-workspace-release/1', 'packageId': retained.package,
                              'version': retained.version, 'tag': retained.tag, 'sourceSha': 'a' * 40,
                              'archiveSha256': 'b' * 64, 'producerPayloadSha256': 'sha256:' + 'c' * 64}
                # Retained descriptors/eight-effect readback do not admit ordinary publication.
                self.assertEqual(len(effects(historical, binding=retained)[1]), 8)
                admission = WizardAdmission(NoReads(), historical, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=retained)
                self.assertFalse(admission.authorize(admission.content_id, 'journal', 'intent', admission.content_id))

    def test023_refuses_a_022_candidate_on_the_023_route(self):
        """A complete, internally consistent 0.22.0 candidate is refused at every 023 join."""
        from new_sdd_workspace_successor_admission import WizardAdmission
        from new_sdd_workspace_successor_execution import effects, Refused as EffectRefused
        f = self.fixture
        prior = Current022Dependencies('test022_release_recomputed_and_source_or_prior_evidence_never_upgraded')
        prior.setUp(); self.addCleanup(prior.doCleanups)
        old = prior.fixture
        proof = prior.proof()
        manifest = {'schema': 'fsgg.new-sdd-workspace-release/1', 'packageId': CURRENT_022.package,
                    'version': CURRENT_022.version, 'tag': CURRENT_022.tag, 'sourceSha': 'a' * 40,
                    'archiveSha256': proof['archiveSha256'], 'producerPayloadSha256': 'sha256:' + 'c' * 64}
        # The 022 candidate really is valid on its own retained route, so each refusal below is causal.
        CURRENT['verify_release_evidence'](proof, old.package, manifest, old.pin, release_binding=CURRENT_022)
        # 1. Effect descriptors select 0.23.1 by default; the retained 0.23.0 descriptor still refuses 0.22.0.
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.1'): effects(manifest)
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.0'): effects(manifest, binding=CURRENT_023)
        class NoReads:
            def get(self, path): raise AssertionError('a 0.22 candidate must refuse before native reads')
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.0'):
            WizardAdmission(NoReads(), manifest, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=CURRENT_023)
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.1'):
            WizardAdmission(NoReads(), manifest, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main')
        # The 0.22 candidate still admits only on the retained 022 descriptor, which grants no ordinary authority.
        retained = WizardAdmission(NoReads(), manifest, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=CURRENT_022)
        self.assertFalse(retained.authorize(retained.content_id, 'journal', 'intent', retained.content_id))
        # 2. The 0.113.0 pin and the 0.23.0 binding refuse the 0.22.0 package and its evidence.
        with self.assertRaises(ValueError):
            CURRENT['release_evidence'](old.package, 'a' * 40, f.pin, fixtures_passed=True, release_binding=CURRENT_023)
        with self.assertRaises(ValueError):
            CURRENT['verify_release_evidence'](proof, old.package, manifest, f.pin, release_binding=CURRENT_023)
        with self.assertRaises(ValueError):
            CURRENT['verify_release_evidence'](proof, old.package, manifest, old.pin, release_binding=CURRENT_023)
        with self.assertRaises(ValueError):
            CURRENT['validate_pin'](old.pin, release_binding=CURRENT_023)
        # 3. The candidate artifact reader refuses it before any package checker runs.
        spec = importlib.util.spec_from_file_location('current023_prior_candidate', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1', 'expired': False,
                    'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                                     'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        def protected(source, *, release_binding):
            self.assertIs(release_binding, CURRENT_023)
            return f.pin
        original = runpy.run_path
        def load(path, *args, **kwargs):
            return {**CURRENT, 'protected_pin': protected} if str(path).endswith('creator-current-coord-dependencies.py') else original(path, *args, **kwargs)
        for index, (member_name, label) in enumerate(((old.package.name, '0.22 member names'),
                                                      (f.package.name, '0.22 bytes in the 0.23 slot'))):
            archive = f.root / f'prior-candidate-{index}.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('manifest.json', json.dumps(manifest))
                z.writestr('package-evidence.json', json.dumps(proof))
                z.writestr(member_name, old.package.read_bytes())
            observed = {**artifact, 'digest': 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()}
            output = f.root / f'prior-output-{index}'
            with self.subTest(candidate=label), patch.object(verifier.runpy, 'run_path', side_effect=load), \
                    patch.object(verifier.subprocess, 'run') as checker:
                with self.assertRaises(ValueError): verifier.verify(observed, run, archive, output, 'a' * 40, release_binding=CURRENT_023)
                checker.assert_not_called(); self.assertFalse(output.exists())

    def test023_actual_qualification_cli_and_source_only_boundary(self):
        f = self.fixture
        creator = f.root / 'creator'
        output = creator / 'scripts/NewSddWorkspace/bin/Release/net10.0'
        output.mkdir(parents=True)
        with zipfile.ZipFile(f.package, 'a') as z:
            z.writestr(PREFIX + 'new-sdd-workspace.dll', b'explicit inert creator output')
            z.writestr(PREFIX + 'new-sdd-workspace.runtimeconfig.json', b'{}')
        with zipfile.ZipFile(f.package) as z:
            for name in z.namelist():
                if name.startswith(PREFIX):
                    target = output / name[len(PREFIX):]
                    target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(z.read(name))
        checker = runpy.run_path(str(ROOT / 'scripts/new-sdd-workspace-release.py'))
        manifest = checker['build_manifest'](type('Args', (), {'package': str(f.package),
            'version': CURRENT_023.version, 'tag': CURRENT_023.tag, 'source_sha': 'a' * 40})())
        manifest_path = f.root / 'manifest.json'
        real_run = runpy.run_path
        def protected(source, *, release_binding):
            self.assertIs(release_binding, CURRENT_023); self.assertEqual(source, 'a' * 40)
            return f.pin
        injected = {**CURRENT, 'protected_pin': protected,
                    'old': {**CURRENT['old'], 'layout': lambda *args: {'currentCreatorSourceSha': 'a' * 40}}}
        def load(path, *args, **kwargs):
            return injected if str(path).endswith('creator-current-coord-dependencies.py') else real_run(path, *args, **kwargs)
        argv = ['qualification', '--release-qualification-023', '--board-package', str(f.package),
                '--source-sha', 'a' * 40, '--release-manifest', str(manifest_path),
                '--published-coord-source', str(f.root / 'producer'),
                '--published-coord-dependencies', str(f.root / 'dependencies'), '--creator-source', str(creator)]
        own = output / 'new-sdd-workspace.dll'; original = own.read_bytes()
        for defect in (None, 'missing-output', 'changed-output', 'changed-source', 'archive', 'payload'):
            own.write_bytes(original); body = dict(manifest)
            injected['old']['layout'] = lambda *args: {'currentCreatorSourceSha': 'b' * 40 if defect == 'changed-source' else 'a' * 40}
            if defect == 'missing-output': own.unlink()
            if defect == 'changed-output': own.write_bytes(b'changed inert compiled output')
            if defect == 'archive': body['archiveSha256'] = '0' * 64
            if defect == 'payload': body['producerPayloadSha256'] = 'sha256:' + '0' * 64
            manifest_path.write_text(json.dumps(body)); captured = io.StringIO()
            with self.subTest(defect=defect), patch.object(sys, 'argv', argv), \
                    patch.object(runpy, 'run_path', side_effect=load), contextlib.redirect_stdout(captured):
                if defect is None:
                    with self.assertRaises(SystemExit) as ended:
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
                    self.assertEqual(ended.exception.code, 0)
                    proof = json.loads(captured.getvalue())
                    self.assertEqual((proof['version'], proof['coherentVersion']), ('0.23.0', '0.113.0'))
                    self.assertEqual(proof['schema'], 'fsgg.creator023-release-qualification/1')
                    self.assertTrue(proof['releaseEligible']); self.assertFalse(proof['sourceQualificationOnly'])
                else:
                    with self.assertRaises(ValueError):
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
        # Missing published roots refuse through the actual CLI before any input or compiler read.
        with patch.object(sys, 'argv', argv[:6]), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as ended:
                real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
            self.assertEqual(ended.exception.code, 2)

    def test023_compiled_product_fixture_joins_same_current_pin(self):
        text = (ROOT / 'tests/new-sdd-workspace/product-board.fsx').read_text()
        published = text.split('| Some "published-dependency" ->', 1)[1].split('| None | Some "source-only" ->', 1)[0]
        self.assertIn('"scripts/creator023-coord-dependencies.json"', published)
        self.assertNotIn('"scripts/creator022-coord-dependencies.json"', published)
        self.assertNotIn('"scripts/creator021-coord-dependencies.json"', published)
        self.assertNotIn('"scripts/creator-current-coord-dependencies.json"', published)
        historical = text.split('| None | Some "source-only" ->', 1)[1].split('| Some _ ->', 1)[0]
        self.assertIn('"scripts/creator-frozen-coord-dependencies.json"', historical)


class Current0231Dependencies(unittest.TestCase):
    """Separate current 0.23.1 patch release proof over the same genuine promoted113 pin; retained 0.23.0 and 022 stay historical."""
    def setUp(self):
        self.fixture = CurrentDependencies()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        f = self.fixture
        # Explicit synthetic unexecutable113 producer, not a public receipt.
        f.pin = json.loads(json.dumps(f.pin).replace('0.108.0', '0.113.0'))
        f.producer = json.loads(json.dumps(f.producer).replace('0.108.0', '0.113.0'))
        f.members['fsgg-coord-engine.deps.json'] = json.dumps(f.producer).encode()
        f.pin['members'] = {name: hashlib.sha256(body).hexdigest() for name, body in f.members.items()}
        f.deps = json.loads(json.dumps(f.deps).replace('0.108.0', '0.113.0').replace('0.19.0', '0.23.1'))
        f.package = f.root / 'FS.GG.NewSddWorkspace.0.23.1.nupkg'
        self.nuspec = (b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.23.1</version>'
                       b'<repository commit="' + b'a' * 40 + b'"/></metadata></package>')
        self.write()

    def write(self, **kwargs):
        self.fixture.write(extra={'FS.GG.NewSddWorkspace.nuspec': self.nuspec}, **kwargs)

    def proof(self):
        f = self.fixture
        return CURRENT['release_evidence'](f.package, 'a' * 40, f.pin,
                                          fixtures_passed=True, release_binding=CURRENT_0231)

    def test0231_release_recomputed_and_source_or_prior_evidence_never_upgraded(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_0231.package, 'version': CURRENT_0231.version,
                    'tag': CURRENT_0231.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        self.assertEqual((proof['version'], proof['coherentVersion']), ('0.23.1', '0.113.0'))
        self.assertEqual(proof['schema'], 'fsgg.creator0231-release-qualification/1')
        self.assertEqual(proof['dependencyMembers'], f.pin['members'])
        self.assertFalse(proof['sourceQualificationOnly'])
        self.assertTrue(proof['releaseEligible'])
        verify = CURRENT['verify_release_evidence']
        self.assertEqual(verify(proof, f.package, manifest, f.pin, release_binding=CURRENT_0231), proof)
        for body in ({**proof, 'schema': 'fsgg.creator023-release-qualification/1'}, {**proof, 'schema': 'fsgg.creator022-release-qualification/1'}, {**proof, 'schema': 'fsgg.creator021-release-qualification/1'},
                     {**proof, 'sourceQualificationOnly': True}, {**proof, 'releaseEligible': False},
                     {**proof, 'coherentVersion': '0.112.0'}, {**proof, 'coherentVersion': '0.110.0'}, {**proof, 'fixturesPassed': False},
                     {**proof, 'dependencyMembers': {}}, {**proof, 'dependencyPinSha256': '0' * 64},
                     {**proof, 'publishedDependencySourceSha': 'c' * 40}, {**proof, 'unknown': True}):
            with self.subTest(body=body), self.assertRaises(ValueError):
                verify(body, f.package, manifest, f.pin, release_binding=CURRENT_0231)
        from creator_source_qualification import SOURCE_021, SOURCE_022, SOURCE_023, SOURCE_0231
        for binding in (SOURCE_020, SOURCE_021, SOURCE_022, SOURCE_023, SOURCE_0231, ReleaseBinding(CURRENT_0231.package, CURRENT_0231.version, CURRENT_0231.tag)):
            with self.subTest(binding=binding), self.assertRaises(ValueError):
                CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, release_binding=binding)
        with self.assertRaises(ValueError):
            CURRENT['evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=True, source_binding=SOURCE_0231)
        with self.assertRaises(ValueError):
            CURRENT['release_evidence'](f.package, 'a' * 40, f.pin, fixtures_passed=False, release_binding=CURRENT_0231)
        for key, value in (('tag', CURRENT_023.tag), ('version', CURRENT_023.version), ('tag', CURRENT_022.tag), ('version', CURRENT_022.version), ('tag', CURRENT_021.tag), ('version', CURRENT_021.version), ('packageId', 'foreign'),
                           ('sourceSha', 'b' * 40), ('archiveSha256', '0' * 64)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify(proof, f.package, {**manifest, key: value}, f.pin, release_binding=CURRENT_0231)

    def test0231_missing_changed_shadow_and_stale_metadata_are_causal(self):
        f = self.fixture
        for defect in ('missing', 'changed', 'shadow', 'old-coherent', 'wrong-own-version'):
            members = dict(f.members); deps = json.loads(json.dumps(f.deps)); extra = {}
            if defect == 'missing': members.pop('FS.GG.Coord.GitHub.dll')
            if defect == 'changed': members['FS.GG.Coord.GitHub.dll'] += b' mutation'
            if defect == 'shadow': extra['foreign/FS.GG.Coord.GitHub.dll'] = b' shadow'
            if defect == 'old-coherent': deps = json.loads(json.dumps(deps).replace('0.113.0', '0.112.0'))
            if defect == 'wrong-own-version': deps = json.loads(json.dumps(deps).replace('0.23.1', '0.23.0'))
            f.write(members=members, deps=deps, extra={**extra, 'FS.GG.NewSddWorkspace.nuspec': self.nuspec})
            with self.subTest(defect=defect), self.assertRaises(ValueError): self.proof()
        self.write()
        self.proof()

    def test0231_fixed_pin_cannot_select112_110_or_arbitrary_input(self):
        f = self.fixture
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_0231)
        old = f.root / CURRENT['PIN_021_PATH']; old.parent.mkdir(); old.write_text(json.dumps(f.pin))
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_0231)
        prior = f.root / CURRENT['PIN_022_PATH']; prior.write_text(json.dumps(f.pin))
        with self.assertRaises(ValueError): CURRENT['load_pin'](f.root, release_binding=CURRENT_0231)
        new = f.root / CURRENT['PIN_023_PATH']; new.write_text(json.dumps(f.pin))
        self.assertEqual(CURRENT['load_pin'](f.root, release_binding=CURRENT_0231), f.pin)
        for wrong in (CURRENT_022, CURRENT_021, CURRENT_020, SOURCE_020, ReleaseBinding(CURRENT_0231.package, CURRENT_0231.version, CURRENT_0231.tag)):
            with self.subTest(binding=wrong), self.assertRaises(ValueError):
                CURRENT['validate_pin'](f.pin, release_binding=wrong)

    def test0231_genuine_fixed_coordinates_and_complete_source_census(self):
        pin = CURRENT['load_pin'](release_binding=CURRENT_0231)
        self.assertEqual((pin['version'], pin['sourceSha'], pin['releaseId'], pin['assetId'], pin['promotedManifestAssetId']),
                         ('0.113.0', '79db622ff4a51498e22ae5820eeb2b470fc743c5', 421880935, 669319653, 669346850))
        self.assertEqual(pin['archiveSha256'], '100f0879436d41df98aa0879aa8ea279dde3e39d3119733875744ac8e2f79789')
        self.assertEqual(pin['promotedManifestSha256'], '5daf312bf999a393ba77b82508eddd50380a3f059e17ec90f433760e01f9b8f7')
        self.assertEqual((len(pin['members']), len(pin['projects']), len(pin['sourceLeaves'])), (81, 11, 293))
        # Census the immutable producer, never require current main to remain 79db.
        producer = self.fixture.root / 'genuine113-producer-source'
        subprocess.run(['git', 'clone', '--shared', '--no-checkout', str(ROOT), str(producer)],
                       check=True, capture_output=True, timeout=15)
        subprocess.run(['git', 'config', 'core.sparseCheckout', 'true'], cwd=producer,
                       check=True, capture_output=True, timeout=10)
        (producer / '.git/info/sparse-checkout').write_text(''.join('/' + row['path'] + '\n' for row in pin['sourceLeaves']))
        subprocess.run(['git', 'checkout', '--detach', pin['sourceSha']], cwd=producer,
                       check=True, capture_output=True, timeout=15)
        self.assertEqual(CURRENT['old']['source_projects'](ROOT, pin, producer), pin['projects'])
        # The fixed retained022 and 021 pins remain separate genuine112 and genuine110 documents.
        for binding, version in ((CURRENT_022, '0.112.0'), (CURRENT_021, '0.110.0')):
            historical = CURRENT['load_pin'](release_binding=binding)
            self.assertEqual(historical['version'], version)
            self.assertNotEqual(CURRENT['pin_digest'](historical), CURRENT['pin_digest'](pin))
        # The 0.23.1 patch keeps the coherent 0.113.0 pin: the published 0.23.0 binding reads the very same
        # genuine document, so a pin change would have to be an explicit second pin file, never an in-place edit.
        self.assertEqual(CURRENT['binding_pin_path'](CURRENT_0231), CURRENT['binding_pin_path'](CURRENT_023))
        self.assertEqual(CURRENT['load_pin'](release_binding=CURRENT_023), pin)
        # 0.113.0 is the newest promoted set; the unpublished 0.114.0 source scalar is never a pin.
        self.assertEqual(CURRENT['release_pin'](CURRENT_0231)[2], '0.113.0')
        self.assertEqual({row[0].version: row[2] for row in CURRENT['RELEASE_PINS']},
                         {'0.20.0': '0.108.0', '0.21.0': '0.110.0', '0.22.0': '0.112.0', '0.23.0': '0.113.0', '0.23.1': '0.113.0'})


    def test0231_artifact_native_and_evidence_joins_before_package_checker(self):
        f = self.fixture
        proof = self.proof()
        manifest = {'packageId': CURRENT_0231.package, 'version': CURRENT_0231.version,
                    'tag': CURRENT_0231.tag, 'sourceSha': 'a' * 40, 'archiveSha256': proof['archiveSha256']}
        spec = importlib.util.spec_from_file_location('current0231_artifact', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1',
                    'expired': False, 'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                    'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        def protected(source, *, release_binding):
            self.assertEqual(source, 'a' * 40); self.assertIs(release_binding, CURRENT_0231)
            return f.pin
        original = runpy.run_path
        def load(path, *args, **kwargs):
            if str(path).endswith('creator-current-coord-dependencies.py'):
                return {**CURRENT, 'protected_pin': protected}
            return original(path, *args, **kwargs)
        for index, defect in enumerate((None, 'source-only', 'historical-proof', 'rerun', 'source', 'workflow', 'expired')):
            body = dict(proof); observed_run = dict(run); observed_artifact = dict(artifact)
            if defect == 'source-only': body.update(sourceQualificationOnly=True, releaseEligible=False)
            if defect == 'historical-proof': body['schema'] = 'fsgg.creator022-release-qualification/1'
            if defect == 'rerun': observed_run['run_attempt'] = 2
            if defect == 'source': observed_run['head_sha'] = 'b' * 40
            if defect == 'workflow': observed_run['path'] = 'other.yml'
            if defect == 'expired': observed_artifact['expired'] = True
            archive = f.root / f'0231-candidate-{index}.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('manifest.json', json.dumps(manifest))
                z.writestr('package-evidence.json', json.dumps(body))
                z.writestr(f.package.name, f.package.read_bytes())
            observed_artifact['digest'] = 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()
            output = f.root / f'0231-output-{index}'
            with self.subTest(defect=defect), patch.object(verifier.runpy, 'run_path', side_effect=load), \
                    patch.object(verifier.subprocess, 'run') as checker:
                if defect is None:
                    self.assertEqual(verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40), manifest)
                    checker.assert_called_once()
                else:
                    with self.assertRaises(ValueError): verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40)
                    checker.assert_not_called(); self.assertFalse(output.exists())

    def test0231_ordinary_authority_excludes_retained023_022_021_and_020(self):
        from new_sdd_workspace_successor_admission import WizardAdmission
        from new_sdd_workspace_successor_execution import effects
        class NoReads:
            def get(self, path): raise AssertionError('historical identity must refuse before native reads')
        for retained in (CURRENT_023, CURRENT_022, CURRENT_021, CURRENT_020):
            with self.subTest(binding=retained.version):
                historical = {'schema': 'fsgg.new-sdd-workspace-release/1', 'packageId': retained.package,
                              'version': retained.version, 'tag': retained.tag, 'sourceSha': 'a' * 40,
                              'archiveSha256': 'b' * 64, 'producerPayloadSha256': 'sha256:' + 'c' * 64}
                # Retained descriptors/eight-effect readback do not admit ordinary publication.
                self.assertEqual(len(effects(historical, binding=retained)[1]), 8)
                admission = WizardAdmission(NoReads(), historical, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=retained)
                self.assertFalse(admission.authorize(admission.content_id, 'journal', 'intent', admission.content_id))

    def test0231_refuses_a_published_023_candidate_on_the_0231_route(self):
        """A complete, internally consistent 0.23.0 candidate is refused at every 0.23.1 join, although both
        releases share one genuine coherent 0.113.0 pin: version, tag and package identity are the boundary."""
        from new_sdd_workspace_successor_admission import WizardAdmission
        from new_sdd_workspace_successor_execution import effects, Refused as EffectRefused
        f = self.fixture
        prior = Current023Dependencies('test023_release_recomputed_and_source_or_prior_evidence_never_upgraded')
        prior.setUp(); self.addCleanup(prior.doCleanups)
        old = prior.fixture
        proof = prior.proof()
        manifest = {'schema': 'fsgg.new-sdd-workspace-release/1', 'packageId': CURRENT_023.package,
                    'version': CURRENT_023.version, 'tag': CURRENT_023.tag, 'sourceSha': 'a' * 40,
                    'archiveSha256': proof['archiveSha256'], 'producerPayloadSha256': 'sha256:' + 'c' * 64}
        # The 0.23.0 candidate really is valid on its own retained route, so each refusal below is causal.
        CURRENT['verify_release_evidence'](proof, old.package, manifest, old.pin, release_binding=CURRENT_023)
        self.assertEqual(old.pin, f.pin)  # one genuine coherent113 pin serves both bindings
        # 1. Effect descriptors and ordinary admission select only 0.23.1.
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.1'): effects(manifest)
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.1'): effects(manifest, binding=CURRENT_0231)
        class NoReads:
            def get(self, path): raise AssertionError('a 0.23.0 candidate must refuse before native reads')
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.1'):
            WizardAdmission(NoReads(), manifest, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=CURRENT_0231)
        with self.assertRaisesRegex(EffectRefused, 'exact Wizard 0.23.1'):
            WizardAdmission(NoReads(), manifest, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main')
        # The 0.23.0 candidate still admits only on the retained 023 descriptor, which grants no ordinary authority.
        retained = WizardAdmission(NoReads(), manifest, 'a' * 40, 1, 'EHotwagner', 'refs/heads/main', release_binding=CURRENT_023)
        self.assertFalse(retained.authorize(retained.content_id, 'journal', 'intent', retained.content_id))
        # 2. The 0.23.1 binding refuses the 0.23.0 package and its evidence even with the shared pin.
        with self.assertRaises(ValueError):
            CURRENT['release_evidence'](old.package, 'a' * 40, f.pin, fixtures_passed=True, release_binding=CURRENT_0231)
        with self.assertRaises(ValueError):
            CURRENT['verify_release_evidence'](proof, old.package, manifest, f.pin, release_binding=CURRENT_0231)
        with self.assertRaises(ValueError):
            CURRENT['verify_release_evidence'](proof, old.package, manifest, old.pin, release_binding=CURRENT_0231)
        # 3. The candidate artifact reader refuses it before any package checker runs.
        spec = importlib.util.spec_from_file_location('current0231_prior_candidate', ROOT / 'scripts/new-sdd-workspace-successor-artifact.py')
        verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
        run = {'id': 1, 'path': verifier.WORKFLOW, 'head_sha': 'a' * 40, 'head_branch': 'main',
               'event': 'workflow_dispatch', 'conclusion': 'success', 'run_attempt': 1,
               'repository': {'id': verifier.REPOSITORY_ID}}
        artifact = {'id': 2, 'name': 'new-sdd-workspace-successor-candidate-' + 'a' * 40 + '-1', 'expired': False,
                    'workflow_run': {'id': 1, 'head_sha': 'a' * 40, 'head_branch': 'main',
                                     'repository_id': verifier.REPOSITORY_ID, 'head_repository_id': verifier.REPOSITORY_ID}}
        def protected(source, *, release_binding):
            self.assertIs(release_binding, CURRENT_0231)
            return f.pin
        original = runpy.run_path
        def load(path, *args, **kwargs):
            return {**CURRENT, 'protected_pin': protected} if str(path).endswith('creator-current-coord-dependencies.py') else original(path, *args, **kwargs)
        for index, (member_name, label) in enumerate(((old.package.name, '0.23.0 member names'),
                                                      (f.package.name, '0.23.0 bytes in the 0.23.1 slot'))):
            archive = f.root / f'prior-candidate-{index}.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('manifest.json', json.dumps(manifest))
                z.writestr('package-evidence.json', json.dumps(proof))
                z.writestr(member_name, old.package.read_bytes())
            observed = {**artifact, 'digest': 'sha256:' + hashlib.sha256(archive.read_bytes()).hexdigest()}
            output = f.root / f'prior-output-{index}'
            with self.subTest(candidate=label), patch.object(verifier.runpy, 'run_path', side_effect=load), \
                    patch.object(verifier.subprocess, 'run') as checker:
                with self.assertRaises(ValueError): verifier.verify(observed, run, archive, output, 'a' * 40)
                checker.assert_not_called(); self.assertFalse(output.exists())

    def test0231_actual_qualification_cli_and_source_only_boundary(self):
        f = self.fixture
        creator = f.root / 'creator'
        output = creator / 'scripts/NewSddWorkspace/bin/Release/net10.0'
        output.mkdir(parents=True)
        with zipfile.ZipFile(f.package, 'a') as z:
            z.writestr(PREFIX + 'new-sdd-workspace.dll', b'explicit inert creator output')
            z.writestr(PREFIX + 'new-sdd-workspace.runtimeconfig.json', b'{}')
        with zipfile.ZipFile(f.package) as z:
            for name in z.namelist():
                if name.startswith(PREFIX):
                    target = output / name[len(PREFIX):]
                    target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(z.read(name))
        checker = runpy.run_path(str(ROOT / 'scripts/new-sdd-workspace-release.py'))
        manifest = checker['build_manifest'](type('Args', (), {'package': str(f.package),
            'version': CURRENT_0231.version, 'tag': CURRENT_0231.tag, 'source_sha': 'a' * 40})())
        manifest_path = f.root / 'manifest.json'
        real_run = runpy.run_path
        def protected(source, *, release_binding):
            self.assertIs(release_binding, CURRENT_0231); self.assertEqual(source, 'a' * 40)
            return f.pin
        injected = {**CURRENT, 'protected_pin': protected,
                    'old': {**CURRENT['old'], 'layout': lambda *args: {'currentCreatorSourceSha': 'a' * 40}}}
        def load(path, *args, **kwargs):
            return injected if str(path).endswith('creator-current-coord-dependencies.py') else real_run(path, *args, **kwargs)
        argv = ['qualification', '--release-qualification-0231', '--board-package', str(f.package),
                '--source-sha', 'a' * 40, '--release-manifest', str(manifest_path),
                '--published-coord-source', str(f.root / 'producer'),
                '--published-coord-dependencies', str(f.root / 'dependencies'), '--creator-source', str(creator)]
        own = output / 'new-sdd-workspace.dll'; original = own.read_bytes()
        for defect in (None, 'missing-output', 'changed-output', 'changed-source', 'archive', 'payload'):
            own.write_bytes(original); body = dict(manifest)
            injected['old']['layout'] = lambda *args: {'currentCreatorSourceSha': 'b' * 40 if defect == 'changed-source' else 'a' * 40}
            if defect == 'missing-output': own.unlink()
            if defect == 'changed-output': own.write_bytes(b'changed inert compiled output')
            if defect == 'archive': body['archiveSha256'] = '0' * 64
            if defect == 'payload': body['producerPayloadSha256'] = 'sha256:' + '0' * 64
            manifest_path.write_text(json.dumps(body)); captured = io.StringIO()
            with self.subTest(defect=defect), patch.object(sys, 'argv', argv), \
                    patch.object(runpy, 'run_path', side_effect=load), contextlib.redirect_stdout(captured):
                if defect is None:
                    with self.assertRaises(SystemExit) as ended:
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
                    self.assertEqual(ended.exception.code, 0)
                    proof = json.loads(captured.getvalue())
                    self.assertEqual((proof['version'], proof['coherentVersion']), ('0.23.1', '0.113.0'))
                    self.assertEqual(proof['schema'], 'fsgg.creator0231-release-qualification/1')
                    self.assertTrue(proof['releaseEligible']); self.assertFalse(proof['sourceQualificationOnly'])
                else:
                    with self.assertRaises(ValueError):
                        real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
        # Missing published roots refuse through the actual CLI before any input or compiler read.
        with patch.object(sys, 'argv', argv[:6]), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as ended:
                real_run(str(ROOT / 'tests/new-sdd-workspace-successor/run.py'), run_name='__main__')
            self.assertEqual(ended.exception.code, 2)

    def test0231_compiled_product_fixture_joins_same_current_pin(self):
        text = (ROOT / 'tests/new-sdd-workspace/product-board.fsx').read_text()
        published = text.split('| Some "published-dependency" ->', 1)[1].split('| None | Some "source-only" ->', 1)[0]
        self.assertIn('"scripts/creator023-coord-dependencies.json"', published)
        self.assertNotIn('"scripts/creator022-coord-dependencies.json"', published)
        self.assertNotIn('"scripts/creator021-coord-dependencies.json"', published)
        self.assertNotIn('"scripts/creator-current-coord-dependencies.json"', published)
        historical = text.split('| None | Some "source-only" ->', 1)[1].split('| Some _ ->', 1)[0]
        self.assertIn('"scripts/creator-frozen-coord-dependencies.json"', historical)
