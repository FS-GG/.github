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
from new_sdd_workspace_successor_execution import CURRENT_020, CURRENT_019, ReleaseBinding
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
        self.assertEqual(len(effects(proposed)[1]), 8)  # descriptors do not accept source-only evidence

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
        injected = {**CURRENT, 'protected_pin': lambda source: self.pin}
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
                    verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40)
                    checker.assert_called_once()
                else:
                    with self.assertRaises(ValueError):
                        verifier.verify(observed_artifact, observed_run, archive, output, 'a' * 40)
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
        injected = {**CURRENT, 'load_pin': lambda: self.pin, 'protected_pin': lambda source: self.pin}
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
                (output / self.package.name).write_bytes(self.package.read_bytes())
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
            summary = json.loads((root / 'creator020-publication-summary.json').read_text())
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
        injected = {**CURRENT, 'protected_pin': lambda source: self.pin,
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
        injected = {**CURRENT, 'protected_pin': lambda source: self.pin}
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
                        '--release-qualification-020 --board-package',
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
        for original, changed in (('--release-qualification-020', '--source-qualification-020'),
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
        injected = {**CURRENT, 'load_pin': lambda: CURRENT['load_pin'](self.root)}
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
        summary = json.loads((self.root / 'creator020-publication-summary.json').read_text())
        self.assertFalse(summary['preflightPassed'])
        self.assertFalse(summary['journalMutationMayHaveOccurred'])
        self.assertEqual(summary['firstCause']['kind'], 'ValueError')

    def test_candidate_and_publisher_source_pin_are_independently_joined(self):
        function = CURRENT['protected_pin']
        with patch.dict(function.__globals__, {'load_pin': lambda: self.pin}), \
                patch.object(function.__globals__['subprocess'], 'check_output', return_value=json.dumps(self.pin).encode()) as reads:
            self.assertEqual(function('a' * 40), self.pin)
            self.assertEqual(reads.call_count, 2)
        wrong = {**self.pin, 'sourceSha': 'f' * 40}
        with patch.dict(function.__globals__, {'load_pin': lambda: self.pin}), \
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
