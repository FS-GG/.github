#!/usr/bin/env python3
"""Two public019 raw COMPLETE knowledge subjects; pure controls launch no child.

Standalone knowledge only. Product/browser/WASM/provider execution needs its own
acceptance and is never performed by this source-only control invocation.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
import types
import unittest
from unittest.mock import Mock, patch
import zipfile

PLAYER_SHA = '24468f761482764d8d05c722dd9974b0daa3159a40c5e36a89c86274d67661be'

def require(ok, reason):
    if not ok: raise ValueError(reason)

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def load_ports():
    path = Path(__file__).with_name('knowledge-fable-game-public.py').resolve()
    require(digest(path) == PLAYER_SHA, 'accepted-player-ports-drift')
    module = types.ModuleType('sealed_complete_knowledge_ports'); module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module

P = load_ports()
M, C = P.M, P.C
PREFIX, PACKAGES, TOP_KEYS = P.PREFIX, P.PACKAGES, P.TOP_KEYS
ROWS = ({'id': 'raw-fable-complete-explicit-typed', 'product': 'RawCompleteExplicit', 'explicit': True},
        {'id': 'raw-fable-complete-omitted-lifecycle', 'product': 'RawCompleteOmitted', 'explicit': False})
COMPLETE_FILES = ('Domain/ArenaRules.fs', 'SvgFoundation/Studio/Studio.fsproj',
                  'models/svg-arena/arena-rules.qnt', 'Conformance/GeneratedArenaRuleModel.fs',
                  'SvgFoundation/Examples/Tactical/scene.json', 'SvgFoundation/Examples/Arcade/scene.json',
                  'SvgFoundation/Examples/FourD/reference.json',
                  'SvgFoundation/Examples/ExternalAuthority/reference.json',
                  'SvgFoundation/Examples/Wasm/WasmPolicyExample.fs',
                  'SvgFoundation/Examples/SoldierReference/reference.json')
REFERENCE_FILES = ('SvgFoundation/Examples/FourD/reference.json',
                   'SvgFoundation/Examples/ExternalAuthority/reference.json')
initialize, restore_tools, knowledge = P.initialize, P.restore_tools, P.knowledge
injected_runner = P.injected_runner


def packed_contract(archive):
    tools = P.packed_contract(archive)
    with zipfile.ZipFile(archive) as z:
        config = json.loads(z.read(PREFIX + '.template.config/template.json'))
    require('complete' in {v['choice'] for v in config['symbols']['bundle']['choices']}, 'packed-complete-choice')
    modifiers = config['sources'][0]['modifiers']
    complete = [m for m in modifiers if m.get('condition') == '(bundle != "complete")']
    require(len(complete) == 1 and all(name in complete[0]['exclude'] for name in
            ('SvgFoundation/Examples/FourD/**', 'SvgFoundation/Examples/ExternalAuthority/**',
             'SvgFoundation/Examples/Wasm/**', 'SvgFoundation/Examples/SoldierReference/**')),
            'packed-complete-composition')
    return tools


def command(m, row, root):
    require(row in ROWS, 'unknown-complete-subject')
    argv = [m['dotnetExecutable'], 'new', 'fs-gg-fable-game', '--name', row['product'],
            '--output', root, '--bundle', 'complete']
    return argv + (['--lifecycle', 'typed-sdd'] if row['explicit'] else [])


def raw_boundary(root, archive):
    require(not (root / '.fsgg/knowledge').exists() and not (root / '.fsgg/knowledge-guide.md').exists(),
            'raw-complete-auto-knowledge')
    require(all(not (root / p).exists() for p in P.LIFECYCLE_PATHS), 'raw-complete-auto-lifecycle')
    require(all((root / p).is_file() for p in COMPLETE_FILES), 'raw-complete-composition')
    require(not (root / 'PortalExample').exists() and not (root / 'SvgFoundation/PreviewFont.fs').exists()
            and not (root / 'SvgFoundation/PreviewDocument.fs').exists(), 'raw-complete-excluded-content')
    tools = packed_contract(archive)
    with zipfile.ZipFile(archive) as z:
        for name in REFERENCE_FILES:
            require((root / name).read_bytes() == z.read(PREFIX + name), 'complete-reference-bytes:' + name)
    M.assets(root, archive, PREFIX)
    return P.game_manifest(root, original_tools=tools)


def initial(runner, root, archive):
    result = P.initial(runner, root, archive)
    tracked = set(runner.git(root, 'ls-files').splitlines())
    require(set(COMPLETE_FILES) <= tracked, 'complete-owner-files-untracked')
    blobs = {}
    for name in COMPLETE_FILES:
        raw = runner.run(['git', '-C', root, 'show', result['commit'] + ':' + name], text=False, check=True).stdout
        require(raw == (root / name).read_bytes(), 'complete-initial-owner-blob:' + name)
        blobs[name] = hashlib.sha256(raw).hexdigest()
    result['completeOwnerCommittedAssets'] = blobs
    return result


def validate(path, expected_sha, after=False):
    require(expected_sha and digest(path) == expected_sha, 'manifest-digest')
    m = C.read_json(path)
    require(set(m) == TOP_KEYS and m['schema'] == 'fsgg.raw-fable-complete-knowledge/1', 'closed-game-manifest')
    require(set(m['packages']) == set(PACKAGES), 'closed-public-game-packages')
    for name in m['inputs']:
        C.regular(name, m['inputs'])
    C.regular(m['runnerPort'], m['inputs'])
    for name in (__file__, P.__file__, M.__file__, C.__file__):
        C.regular(Path(name).resolve(), m['inputs'])
    for relative, sha in C.SOURCE_HASHES.items():
        if relative.startswith('tests/knowledge-receiver/'):
            require(digest(C.regular(Path(m['sourceRoot']) / relative, m['inputs'])) == sha, 'frozen-game-probe-source')
    for key, identity in PACKAGES.items():
        binding = m['packages'][key]
        require(set(binding) == ({'archive', 'publicationReceipts', 'closure'} if key == 'producer'
                                else {'archive', 'publicationReceipts'}), 'closed-game-package-binding')
        members = M.public_package(key, binding, m['inputs'], identity)
        if key == 'producer':
            C.tool_closure(binding, m['sddExecutable'], m['inputs'], members, 'FS.GG.SDD.Cli.dll')
    packed_contract(m['packages']['workspace']['archive'])
    env = m['childEnvironment']
    require(set(env) == {'PATH', *M.SERIAL_ENV, *M.HOST_ENV}
            and all(env[k] == v for k, v in {**M.SERIAL_ENV, **M.HOST_ENV}.items()), 'closed-game-host-environment')
    require(m['actualHome'] == os.environ.get('HOME'), 'actual-home')
    for name, expected in (('dotnet', m['dotnetExecutable']), ('fsgg-sdd', m['sddExecutable'])):
        selected = shutil.which(name, path=env['PATH'])
        require(selected and Path(selected).resolve() == Path(expected).resolve(), 'selected-game-executable')
        C.regular(Path(expected), m['inputs'])
    git = shutil.which('git', path=env['PATH'])
    require(git, 'missing-bound-git')
    for executable in (Path(git).resolve(), Path(sys.executable).resolve()):
        C.regular(executable, m['inputs'])
    root = Path(m['workRoot'])
    require(root.is_absolute() and root == root.resolve() and not root.is_symlink()
            and Path(m['report']).parent == root and (after or not root.exists()), 'fresh-game-output-no-replay')
    return m


def qualify(path, expected_sha, *, runner_factory=None):
    # The frozen console runner is intentionally never a runtime fallback. Root
    # must bind and qualify the supplied finite collector/retirement adapter first.
    require(callable(runner_factory), 'qualified-runner-factory-required')
    started = time.monotonic(); deadline = started + 600
    m = validate(path, expected_sha)
    require(time.monotonic() < deadline - 9, 'setup-exhausted-original-window')
    base = Path(m['workRoot']); base.mkdir(mode=0o700)
    for name in ('tmp', 'dotnet-home', 'packages', 'http-cache'): (base / name).mkdir()
    feed = base / 'feed'; feed.mkdir()
    for key, identity in PACKAGES.items():
        shutil.copyfile(m['packages'][key]['archive'], feed / (identity[0] + '.' + identity[1] + '.nupkg'))
    require(not any(c in str(feed) for c in '<>&"'), 'unsafe-local-feed-path')
    config = base / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="sealed-public" value="' + str(feed) + '"/></packageSources></configuration>')
    runner = injected_runner(runner_factory, m, base, deadline - 5)
    report = dict(schema='fsgg.raw-fable-complete-knowledge-result/1', state='started', manifestSha256=expected_sha,
                  outerSeconds=600, perCommandMaxSeconds=180, rows=[], runtimeAdmission='external-required',
                  unfulfilled=['independent result acceptance', 'other parent4642 variants/routes', 'product/browser/provider/Wizard acceptance'])
    def save(): Path(m['report']).write_text(json.dumps(report, indent=2) + '\n')
    save()
    try:
        require(runner.call([m['sddExecutable'], '--version']).stdout.strip() == '2.2.0', 'actual-public-game-producer')
        for row in ROWS:
            case = base / row['id']; case.mkdir()
            for name in ('tmp', 'dotnet-home', 'packages', 'http-cache'): (case / name).mkdir()
            runner.env = M.env_for(m, case)
            root = case / 'receiver'
            entry = dict(id=row['id'], state='started'); report['rows'].append(entry); save()
            runner.call([m['dotnetExecutable'], 'new', 'install', m['packages']['workspace']['archive']], cwd=case)
            runner.call(command(m, row, root), cwd=case)
            entry['registration'] = C.registration(case, '0.19.0', PACKAGES['workspace'][3])
            raw_boundary(root, m['packages']['workspace']['archive'])
            entry['initialization'] = initialize(runner, m, root, config)
            entry['initial'] = initial(runner, root, m['packages']['workspace']['archive']); save()
            restore_tools(runner, m, root, config)
            entry['knowledge'] = knowledge(runner, m, root, case)
            require(entry['knowledge']['probe']['probeSetupCommit'] != entry['initial']['commit'], 'game-probe-not-caller-initial')
            entry['state'] = 'passed'; save()
        require(validate(path, expected_sha, after=True) == m, 'post-game-custody-drift')
        require(time.monotonic() < deadline, 'original-game-deadline')
        report.update(state='passed', commands=runner.sequence, elapsedSeconds=time.monotonic() - started)
        save(); return report
    except BaseException as error:
        if report['rows'] and report['rows'][-1]['state'] == 'started': report['rows'][-1]['state'] = 'failed'
        report.update(state='failed', commands=runner.sequence, failureType=type(error).__name__, failure=str(error))
        try:
            save()
        except BaseException as custody_error:
            # Retain the actual command/probe exception; failed evidence writing
            # remains separately Unknown rather than replacing the primary cause.
            error.add_note('failure-report-custody-Unknown:' + type(custody_error).__name__)
        raise


def source_controls():
    class Controls(unittest.TestCase):
        @staticmethod
        def tools():
            return {'version': 1, 'isRoot': True, 'tools': {
                'fable': {'version': '5.18.0', 'commands': ['fable']},
                'fantomas': {'version': '8.0.5', 'commands': ['fantomas'], 'rollForward': False}}}

        def fixture(self, directory):
            root = Path(directory) / 'complete'; root.mkdir()
            for name in COMPLETE_FILES:
                target = root / name; target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(('owned complete ' + name).encode())
            (root / '.config').mkdir()
            (root / '.config/dotnet-tools.json').write_text(json.dumps(self.tools(), indent=2) + '\n')
            (root / '.gitignore').write_text('owner-prefix\n')
            config = {'identity': 'FS.GG.Workspace.Template.FableGame', 'precedence': 100,
                      'shortName': 'fs-gg-fable-game', 'symbols': {
                          'lifecycle': {'defaultValue': 'typed-sdd'},
                          'bundle': {'defaultValue': 'player', 'choices': [{'choice': 'player'}, {'choice': 'complete'}]}},
                      'sources': [{'modifiers': [{'condition': '(bundle != "complete")', 'exclude': [
                          'SvgFoundation/Examples/FourD/**', 'SvgFoundation/Examples/ExternalAuthority/**',
                          'SvgFoundation/Examples/Wasm/**', 'SvgFoundation/Examples/SoldierReference/**']}]}]}
            archive = Path(directory) / 'workspace.nupkg'
            def write():
                raw = json.dumps(config).encode()
                with zipfile.ZipFile(archive, 'w') as z:
                    z.writestr(PREFIX + '.template.config/template.json', raw)
                    z.writestr(PREFIX + '.config/dotnet-tools.json', json.dumps(self.tools()))
                    for name in REFERENCE_FILES: z.writestr(PREFIX + name, (root / name).read_bytes())
                    for name in M.CI_FILES:
                        asset = root / name; asset.parent.mkdir(parents=True, exist_ok=True)
                        asset.write_bytes(('knowledge CI ' + name).encode()); z.writestr(PREFIX + name, asset.read_bytes())
                return hashlib.sha256(raw).hexdigest()
            return root, archive, config, write

        def test_two_fixed_complete_rows_and_lifecycle_only_omission(self):
            argv = [command({'dotnetExecutable': '/dotnet'}, row, Path('/fresh')) for row in ROWS]
            for args in argv:
                self.assertEqual(args[args.index('--bundle') + 1], 'complete')
                self.assertEqual(args.count('--bundle'), 1)
            self.assertEqual(argv[0][-2:], ['--lifecycle', 'typed-sdd'])
            self.assertNotIn('--lifecycle', argv[1])
            with self.assertRaisesRegex(ValueError, 'unknown-complete'): command({'dotnetExecutable': '/dotnet'}, P.ROWS[0], Path('/fresh'))

        def test_packed_complete_choice_defaults_and_reference_modifiers_are_causal(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, config, write = self.fixture(td)
                with patch.object(P, 'CONFIG_SHA', write()): self.assertEqual(packed_contract(archive), self.tools())
                config['symbols']['bundle']['choices'] = [{'choice': 'player'}]
                with patch.object(P, 'CONFIG_SHA', write()):
                    with self.assertRaisesRegex(ValueError, 'complete-choice'): packed_contract(archive)
                config['symbols']['bundle']['choices'].append({'choice': 'complete'})
                config['sources'][0]['modifiers'][0]['exclude'].remove('SvgFoundation/Examples/ExternalAuthority/**')
                with patch.object(P, 'CONFIG_SHA', write()):
                    with self.assertRaisesRegex(ValueError, 'complete-composition'): packed_contract(archive)
                config['symbols']['lifecycle']['defaultValue'] = 'sdd'
                with patch.object(P, 'CONFIG_SHA', write()):
                    with self.assertRaisesRegex(ValueError, 'default-contract'): packed_contract(archive)

        def test_complete_assets_required_and_external_reference_bytes_exact(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, config, write = self.fixture(td)
                with patch.object(P, 'CONFIG_SHA', write()):
                    raw_boundary(root, archive)
                    for name in COMPLETE_FILES:
                        target = root / name; original = target.read_bytes(); target.unlink()
                        with self.assertRaisesRegex(ValueError, 'complete-composition'): raw_boundary(root, archive)
                        target.write_bytes(original)
                    target = root / REFERENCE_FILES[1]; original = target.read_bytes(); target.write_bytes(original + b'changed')
                    with self.assertRaisesRegex(ValueError, 'complete-reference-bytes'): raw_boundary(root, archive)

        def test_raw_complete_refuses_automatic_store_lifecycle_and_default_exclusions(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, config, write = self.fixture(td)
                with patch.object(P, 'CONFIG_SHA', write()):
                    for name, reason in [('.fsgg/knowledge', 'auto-knowledge'), ('sdd', 'auto-lifecycle'), ('PortalExample', 'excluded-content')]:
                        path = root / name; path.mkdir(parents=True)
                        with self.assertRaisesRegex(ValueError, reason): raw_boundary(root, archive)
                        path.rmdir()

        def test_complete_owner_bytes_modes_and_ignore_prefix_preserved(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, config, write = self.fixture(td); write(); before = M.snapshot(root)
                target = root / REFERENCE_FILES[1]; original = target.read_bytes(); target.write_bytes(original + b'changed')
                with self.assertRaisesRegex(ValueError, 'owner-game-content-or-mode'): P.preserved(before, root)
                target.write_bytes(original); target.chmod(0o600)
                with self.assertRaisesRegex(ValueError, 'owner-game-content-or-mode'): P.preserved(before, root)
                target.chmod(before[REFERENCE_FILES[1]]['mode'])
                ignore = root / '.gitignore'; ignore.write_bytes(b'owner-prefix\nknowledge-excludes\n')
                P.preserved(before, root, ignore_prefix=b'owner-prefix\n')
                ignore.write_bytes(b'changed-prefix\n')
                with self.assertRaisesRegex(ValueError, 'ignore-prefix'): P.preserved(before, root, ignore_prefix=b'owner-prefix\n')

        def test_genuine_scratch_port_preserves_complete_owners_before_standalone(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, config, write = self.fixture(td); write(); before = M.snapshot(root)
                runner = Mock(); runner.env = {'NUGET_PACKAGES': str(Path(td) / 'packages')}; events = []
                def install(argv, **kwargs):
                    self.assertFalse(Path(kwargs['cwd']).is_relative_to(root)); P.preserved(before, root)
                    scratch = Path(argv[argv.index('--tool-manifest') + 1]); self.assertFalse(scratch.is_relative_to(root))
                    scratch.write_text(json.dumps({'version': 1, 'isRoot': True, 'tools': {'fs.gg.sdd.cli': {
                        'version': '2.2.0', 'commands': ['fsgg-sdd'], 'rollForward': False}}}, indent=2)); events.append('scratch-install')
                runner.call.side_effect = install
                runner.knowledge.side_effect = lambda *a: events.append(a[1])
                with patch.object(M, 'package_payload', return_value={}):
                    result = initialize(runner, {'dotnetExecutable': '/dotnet', 'sddExecutable': '/sdd',
                                        'packages': {'producer': {'archive': '/sealed'}}}, root, '/config')
                self.assertEqual(events, ['scratch-install', 'initialize'])
                self.assertTrue(result['toolAddition']['ownerByteSpansPreserved'])
                self.assertNotIn('rollForward', P.game_manifest(root, True)['tools']['fable'])
                P.preserved(before, root, tool_change=True)

        def test_complete_initial_commit_tracks_and_retains_variant_owner_blobs(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, config, write = self.fixture(td); write(); runner = Mock()
                runner.git.return_value = '\n'.join(COMPLETE_FILES)
                runner.run.side_effect = lambda argv, **kw: types.SimpleNamespace(stdout=(root / argv[-1].split(':', 1)[1]).read_bytes())
                with patch.object(P, 'initial', return_value={'commit': 'caller'}):
                    result = initial(runner, root, archive); self.assertEqual(len(result['completeOwnerCommittedAssets']), len(COMPLETE_FILES))
                    runner.git.return_value = '\n'.join(COMPLETE_FILES[:-1])
                    with self.assertRaisesRegex(ValueError, 'untracked'): initial(runner, root, archive)
                    runner.git.return_value = '\n'.join(COMPLETE_FILES); runner.run.return_value.stdout = b'wrong'; runner.run.side_effect = None
                    with self.assertRaisesRegex(ValueError, 'owner-blob'): initial(runner, root, archive)

        def test_driver_runs_only_complete_rows_commit_before_probe_and_keeps_primary(self):
            with tempfile.TemporaryDirectory() as td:
                base = Path(td) / 'fresh'; archive = Path(td) / 'original'; archive.write_bytes(b'fixture')
                m = {'workRoot': str(base), 'report': str(base / 'report.json'), 'sddExecutable': '/sdd', 'dotnetExecutable': '/dotnet',
                     'packages': {k: {'archive': str(archive)} for k in PACKAGES}, 'runnerPort': __file__,
                     'inputs': {str(Path(__file__).resolve()): digest(__file__)}}
                runner = Mock(); runner.sequence = 0; runner.call.return_value.stdout = '2.2.0\n'; events = []
                def factory(base, env, *, deadline): runner.deadline = deadline; runner.env = env; return runner
                def commit(*a): events.append('caller-initial'); return {'commit': 'caller'}
                def probe(*a):
                    events.append('probe'); self.assertEqual(events[-2:], ['caller-initial', 'probe'])
                    return {'probe': {'probeSetupCommit': 'separate'}}
                with patch(__name__ + '.validate', return_value=m), patch.object(M, 'env_for', return_value={}), patch.object(C, 'registration', return_value={}), \
                     patch(__name__ + '.raw_boundary'), patch(__name__ + '.initialize', return_value={}), patch(__name__ + '.restore_tools'), \
                     patch(__name__ + '.initial', side_effect=commit), patch(__name__ + '.knowledge', side_effect=probe):
                    result = qualify('/sealed', 'digest', runner_factory=factory)
                    self.assertEqual([r['id'] for r in result['rows']], [r['id'] for r in ROWS])
                    self.assertEqual(result['schema'], 'fsgg.raw-fable-complete-knowledge-result/1')
                creates = [call.args[0] for call in runner.call.call_args_list if call.args[0][1:3] == ['new', 'fs-gg-fable-game']]
                self.assertEqual(len(creates), 2); self.assertTrue(all('--bundle' in a and 'complete' in a for a in creates))
                self.assertNotIn('--lifecycle', creates[1])
                fail = Path(td) / 'failed'; m.update(workRoot=str(fail), report=str(fail / 'report.json'))
                primary = ValueError('complete-command-primary'); runner.call.side_effect = primary
                original_write = Path.write_text
                def custody_fault(path, data, *args, **kwargs):
                    if path.name == 'report.json' and json.loads(data)['state'] == 'failed': raise OSError('secondary')
                    return original_write(path, data, *args, **kwargs)
                with patch(__name__ + '.validate', return_value=m), patch.object(M, 'env_for', return_value={}), patch.object(Path, 'write_text', custody_fault):
                    with self.assertRaises(ValueError) as caught: qualify('/sealed', 'digest', runner_factory=factory)
                self.assertIs(caught.exception, primary); self.assertIn('failure-report-custody-Unknown:OSError', primary.__notes__)

        def test_player_schema_and_missing_runner_port_refuse_before_runtime(self):
            with tempfile.TemporaryDirectory() as td:
                path = Path(td) / 'manifest'; value = dict.fromkeys(TOP_KEYS); value['schema'] = 'fsgg.raw-fable-player-knowledge/1'
                path.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError, 'closed-game-manifest'): validate(path, digest(path))
            with patch(__name__ + '.validate', side_effect=AssertionError('must refuse before manifest read')):
                with self.assertRaisesRegex(ValueError, 'qualified-runner-factory-required'): qualify('/sealed', 'digest')

    with patch.object(C.subprocess, 'Popen', side_effect=AssertionError('complete-source-controls-launched-child')):
        result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Controls))
    require(result.wasSuccessful(), 'complete-source-controls-failed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-controls', action='store_true')
    args = parser.parse_args()
    require(args.source_controls, 'root-qualified-runner-factory-and-runtime-admission-required')
    source_controls()


if __name__ == '__main__': main()
