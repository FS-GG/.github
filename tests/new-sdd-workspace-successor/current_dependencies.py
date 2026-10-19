"""Inert synthetic current-pin/package controls; never publish or execute DLLs."""
import contextlib
import io
import hashlib
import importlib.util
import json
import pathlib
import runpy
import sys
import tempfile
import unittest
import zipfile
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
CURRENT = runpy.run_path(str(ROOT / 'scripts/creator-current-coord-dependencies.py'))
PREFIX = CURRENT['PREFIX']


class CurrentDependencies(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.package = self.root / 'FS.GG.NewSddWorkspace.0.18.0.nupkg'
        self.members = {name: b'explicit synthetic unexecutable member ' + name.encode() for name in (
            'FS.GG.Coord.GitHub.dll', 'fsgg-coord-engine.dll', 'fsgg-coord-engine.deps.json',
            'fsgg-coord-engine.runtimeconfig.json', 'FS.GG.Coord.Core.dll', 'runtimes/linux-x64/native/test.so')}
        sha = lambda body: hashlib.sha256(body).hexdigest()
        self.pin = {'schema': 'fsgg.creator-current-coord-dependencies/1', 'version': '0.108.0',
                    'sourceSha': 'b' * 40, 'archiveSha256': 'c' * 64, 'promotedManifestSha256': 'd' * 64,
                    'releaseId': 1, 'assetId': 2, 'promotedManifestAssetId': 3,
                    'downloadUrl': 'https://github.com/FS-GG/.github/releases/download/coherent-set/v0.108.0/FS.GG.Coord.Cli.0.108.0.nupkg',
                    'projects': {'scripts/NewSddWorkspace/NewSddWorkspace.fsproj': 'new-sdd-workspace',
                                 'src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj': 'fsgg-coord-engine'},
                    'sourceLeaves': [{'path': 'global.json', 'sha256': 'e' * 64}],
                    'members': {name: sha(body) for name, body in self.members.items()}}
        self.deps = {'libraries': {'FS.GG.Coord.Cli/0.108.0': {'type': 'project'}}, 'targets': {
            '.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.18.0': {'dependencies': {'FS.GG.Coord.Cli': '0.108.0'}},
                                       'FS.GG.Coord.Cli/0.108.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}}
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
        manifest = {'sourceSha': 'a' * 40, 'version': '0.18.0', 'archiveSha256': evidence['archiveSha256']}
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
        manifest = {'sourceSha': 'a' * 40, 'version': '0.18.0', 'archiveSha256': evidence['archiveSha256']}
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
                    verifier.verify(artifact, run, archive, output, 'a' * 40)
                    checker.assert_called_once()
                else:
                    with self.assertRaisesRegex(ValueError, 'evidence body'):
                        verifier.verify(artifact, run, archive, output, 'a' * 40)
                    checker.assert_not_called()
                    self.assertFalse(output.exists())

    def test_candidate_has_only_staged_release_mode_and_preflights_before_sdk(self):
        source = (ROOT / '.github/workflows/release-new-sdd-workspace-successor-candidate.yml').read_text()
        def validate(workflow):
            required = ('FSGG_CREATOR_DEPENDENCY_MODE: published-dependency',
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
                    or workflow.count('creator-current-coord-dependencies.py preflight') != 1
                    or workflow.index('creator-current-coord-dependencies.py preflight') > workflow.index('actions/setup-dotnet')):
                raise ValueError('published candidate preflight/output route differs')
        validate(source)
        for original, changed in (('published-dependency', 'source-only'),
                                  ('-p:BuildProjectReferences=false', '-p:BuildProjectReferences=true'),
                                  ('--no-build', '--artifacts-path stale'),
                                  ('creator-current-coord-dependencies.py preflight', ':'),
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
        summary = json.loads((self.root / 'creator018-publication-summary.json').read_text())
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
