#!/usr/bin/env python3
"""Public019 raw Fable-player knowledge adapter; --source-controls launches no child.

Runtime needs separate root admission and sealed genuine public inputs. This adapter
never builds a product, invokes a provider/Wizard, or initializes the SDD lifecycle.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import sys
import time
import types
import unittest
from unittest.mock import Mock, patch
import zipfile

MATRIX_SHA = '20c4ccaa37046fa92778e2f82576c3c7faedb4ba62da7764d310746a47eaa8ab'
PREFIX = 'content/templates/fs-gg-fable-game/'
CONFIG_SHA = 'f0f81f93b923084a56a42e2f8d008eb6fd7c5078afbc84b3b3781cfcfc2ffcff'
RAW_PINS = {'fable': '5.18.0', 'fantomas': '8.0.5'}
PINS = dict(RAW_PINS, **{'fs.gg.sdd.cli': '2.2.0'})
ROWS = ({'id': 'raw-fable-player-explicit-typed', 'product': 'RawFableExplicit', 'explicit': True},
        {'id': 'raw-fable-player-omitted', 'product': 'RawFableOmitted', 'explicit': False})
TOP_KEYS = {'schema', 'inputs', 'packages', 'sourceRoot', 'sddExecutable', 'dotnetExecutable',
            'actualHome', 'childEnvironment', 'workRoot', 'report', 'runnerPort'}
LIFECYCLE_PATHS = ('sdd', '.fsgg/typed-sdd', '.fsgg/typed-sdd.json', '.fsgg/scaffold-provenance.json')


def require(ok, message):
    if not ok:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_ports():
    path = Path(__file__).with_name('knowledge-public-receivers.py').resolve()
    require(digest(path) == MATRIX_SHA, 'delivered-matrix-ports-drift')
    module = types.ModuleType('sealed_raw_game_ports')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


M = load_ports()
C = M.C
PACKAGES = {key: M.PACKAGES[key] for key in ('workspace', 'producer', 'fable', 'fantomas')}


def packed_contract(archive):
    with zipfile.ZipFile(archive) as z:
        raw = z.read(PREFIX + '.template.config/template.json')
        require(hashlib.sha256(raw).hexdigest() == CONFIG_SHA, 'public-game-config-drift')
        value = json.loads(raw)
        require(value['identity'] == 'FS.GG.Workspace.Template.FableGame'
                and value['precedence'] == 100 and value['shortName'] == 'fs-gg-fable-game'
                and value['symbols']['lifecycle']['defaultValue'] == 'typed-sdd'
                and value['symbols']['bundle']['defaultValue'] == 'player'
                and not value.get('postActions'), 'public-game-default-contract')
        tools = json.loads(z.read(PREFIX + '.config/dotnet-tools.json'))
        require({k: v['version'] for k, v in tools['tools'].items()} == RAW_PINS, 'packed-game-tool-pins')
        return tools


def validate(path, expected_sha, after=False):
    require(expected_sha and digest(path) == expected_sha, 'manifest-digest')
    m = C.read_json(path)
    require(set(m) == TOP_KEYS and m['schema'] == 'fsgg.raw-fable-player-knowledge/1', 'closed-game-manifest')
    require(set(m['packages']) == set(PACKAGES), 'closed-public-game-packages')
    for name in m['inputs']:
        C.regular(name, m['inputs'])
    C.regular(m['runnerPort'], m['inputs'])
    for name in (__file__, M.__file__, C.__file__):
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


def command(m, row, root):
    require(row in ROWS, 'unknown-game-subject')
    argv = [m['dotnetExecutable'], 'new', 'fs-gg-fable-game', '--name', row['product'], '--output', root]
    return argv + (['--lifecycle', 'typed-sdd'] if row['explicit'] else [])


def game_manifest(root, initialized=False, original_tools=None):
    value = M.manifest(root, PINS if initialized else RAW_PINS)
    if original_tools is not None:
        require(all(value['tools'].get(k) == v for k, v in original_tools['tools'].items()), 'owner-game-tool-entry-changed')
        require({k: v for k, v in value.items() if k != 'tools'} ==
                {k: v for k, v in original_tools.items() if k != 'tools'}, 'owner-game-tool-metadata-changed')
    return value


def preserved(before, root, *, tool_change=False, ignore_prefix=None):
    after = M.snapshot(root)
    for name, value in before.items():
        if name == '.gitignore' and ignore_prefix is not None:
            require((root / name).read_bytes().startswith(ignore_prefix)
                    and after[name]['mode'] == value['mode'], 'owner-game-ignore-prefix')
        elif name == '.config/dotnet-tools.json' and tool_change:
            require(after[name]['mode'] == value['mode'], 'owner-game-tool-mode')
        else:
            require(after.get(name) == value, 'owner-game-content-or-mode:' + name)
    return after


def raw_boundary(root, archive):
    require(not (root / '.fsgg/knowledge').exists()
            and not (root / '.fsgg/knowledge-guide.md').exists(), 'raw-game-auto-knowledge-initialization')
    require(all(not (root / p).exists() for p in LIFECYCLE_PATHS), 'raw-game-auto-lifecycle-initialization')
    require((root / 'Domain/ArenaRules.fs').is_file(), 'raw-game-player-positive-control')
    require(not (root / 'SvgFoundation/Studio').exists() and not (root / 'models').exists(), 'raw-game-not-player')
    M.assets(root, archive, PREFIX)
    return game_manifest(root, original_tools=packed_contract(archive))


def initialize(runner, m, root, config):
    before = M.snapshot(root)
    original = game_manifest(root)
    ignore = (root / '.gitignore').read_bytes()
    target = root / '.config/dotnet-tools.json'
    runner.call([m['dotnetExecutable'], 'tool', 'install', 'FS.GG.SDD.Cli', '--version', '2.2.0',
                 '--local', '--tool-manifest', target, '--configfile', config], cwd=root)
    after_tool = preserved(before, root, tool_change=True)
    require(set(after_tool) == set(before), 'tool-install-added-owner-files')
    game_manifest(root, True, original)
    runner.knowledge(m['sddExecutable'], 'initialize', '--root', str(root))
    preserved(after_tool, root, ignore_prefix=ignore)
    require(all(not (root / p).exists() for p in LIFECYCLE_PATHS), 'standalone-created-lifecycle')
    return dict(rawCreationInitializesStore=False, callerStandaloneInitialize=True,
                ownerBytesAndModesPreserved=True, ignorePrefixSha256=hashlib.sha256(ignore).hexdigest())


def restore_tools(runner, m, root, config):
    game_manifest(root, True)
    runner.call([m['dotnetExecutable'], 'tool', 'restore', '--configfile', config, '--no-cache'], cwd=root)
    packages = Path(runner.env['NUGET_PACKAGES'])
    for key in ('producer', 'fable', 'fantomas'):
        identity = PACKAGES[key]
        for name, sha in M.package_payload(key, m['packages'][key]['archive']).items():
            if name.startswith('tools/net10.0/any/'):
                require(digest(packages / identity[0].lower() / identity[1] / name) == sha, 'restored-game-tool-payload')


def initial(runner, root, archive):
    M.ensure_git(runner, root)
    require(not runner.git(root, 'rev-list', '--all'), 'unexpected-game-background-commit')
    M.assets(root, archive, PREFIX)
    game_manifest(root, True)
    canonical = sorted(p.relative_to(root).as_posix() for p in (root / '.fsgg/knowledge').rglob('*') if p.is_file())
    require('.fsgg/knowledge/schema.json' in canonical and any('/records/' in p for p in canonical), 'canonical-game-initial-store')
    required = canonical + ['.fsgg/knowledge-guide.md', '.config/dotnet-tools.json', *M.CI_FILES]
    require((root / '.fsgg/knowledge-guide.md').stat().st_size > 0, 'game-initial-guide')
    cache = root / '.fsgg/cache/game-control.json'
    cache.parent.mkdir(parents=True, exist_ok=True); cache.write_text('{}')
    head = M.commit(runner, root, 'Initial raw Fable-player with standalone knowledge')
    tracked = set(runner.git(root, 'ls-files').splitlines())
    require(set(required) <= tracked and cache.relative_to(root).as_posix() not in tracked, 'normal-game-initial-inclusion')
    blobs = {}
    for name in required:
        raw = runner.run(['git', '-C', root, 'show', head + ':' + name], text=False, check=True).stdout
        require(raw == (root / name).read_bytes(), 'game-initial-blob')
        blobs[name] = hashlib.sha256(raw).hexdigest()
    return dict(commit=head, committedAssets=blobs)


def knowledge(runner, m, root, case):
    before = M.snapshot(root)
    result = dict(ordinaryChecker=C.checked(runner, root))
    with C.frozen_probes(m['sourceRoot'], runner) as (candidate, public):
        result['probe'] = candidate.probe([m['sddExecutable']], root, case / 'probe')
        result['probe']['probeSetupCommit'] = result['probe'].pop('initialCommit')
        result['postPrivateExport'] = C.post_private_export(runner, m['sddExecutable'], root, case / 'probe')
        result['gitBundle'] = M.bundle_recovery(runner, m['sddExecutable'], root, case / 'probe', result['probe']['findingsCommit'])
        result['ordinaryBudget'] = public.ordinary_ci_limits(root)
    result['ordinaryCheckerAfterRestore'] = C.checked(runner, root)
    # The frozen probe intentionally authors guide/ignore; all other owner files retain modes too.
    after = M.snapshot(root)
    for name, value in before.items():
        if name.startswith('.fsgg/knowledge/') or name.startswith('.fsgg/cache/'):
            continue
        if name in ('.gitignore', '.fsgg/knowledge-guide.md'):
            require(after[name]['mode'] == value['mode'], 'probe-owner-mode')
        else:
            require(after.get(name) == value, 'probe-game-owner-changed:' + name)
    return result


def injected_runner(factory, m, base, deadline):
    require(callable(factory), 'qualified-runner-factory-required')
    code = getattr(factory, '__code__', None)
    require(code is not None and Path(code.co_filename).resolve() == C.regular(m['runnerPort'], m['inputs']),
            'exact-qualified-runner-source-required')
    runner = factory(base, M.env_for(m, base), deadline=deadline)
    require(all(callable(getattr(runner, name, None)) for name in ('run', 'call', 'git', 'knowledge'))
            and getattr(type(runner), 'run', None) is not C.Runner.run, 'qualified-runner-ports-required')
    require(isinstance(runner.deadline, (int, float)) and 0 < runner.deadline <= deadline
            and runner.env == M.env_for(m, base) and runner.sequence == 0, 'runner-original-deadline-and-environment')
    return runner


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
    report = dict(schema='fsgg.raw-fable-player-knowledge-result/1', state='started', manifestSha256=expected_sha,
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
    import tempfile
    class Controls(unittest.TestCase):
        def test_true_omission_and_explicit_typed_player(self):
            m = {'dotnetExecutable': '/sealed/dotnet'}
            omitted = command(m, ROWS[1], Path('/fresh/omitted'))
            self.assertNotIn('--lifecycle', omitted); self.assertNotIn('--bundle', omitted)
            explicit = command(m, ROWS[0], Path('/fresh/typed'))
            self.assertEqual(explicit[-2:], ['--lifecycle', 'typed-sdd'])
            self.assertNotIn('--bundle', explicit)
            with self.assertRaisesRegex(ValueError, 'unknown-game-subject'): command(m, {}, '/root')

        def test_packed_defaults_pins_and_postactions_refuse_drift(self):
            with tempfile.TemporaryDirectory() as td:
                archive = Path(td) / 'synthetic.zip'
                config = {'identity': 'FS.GG.Workspace.Template.FableGame', 'precedence': 100,
                          'shortName': 'fs-gg-fable-game', 'symbols': {'lifecycle': {'defaultValue': 'typed-sdd'}, 'bundle': {'defaultValue': 'player'}}}
                tools = {'tools': {k: {'version': v} for k, v in RAW_PINS.items()}}
                def write():
                    raw = json.dumps(config).encode()
                    with zipfile.ZipFile(archive, 'w') as z:
                        z.writestr(PREFIX + '.template.config/template.json', raw)
                        z.writestr(PREFIX + '.config/dotnet-tools.json', json.dumps(tools))
                    return hashlib.sha256(raw).hexdigest()
                with patch(__name__ + '.CONFIG_SHA', write()):
                    self.assertEqual(packed_contract(archive), tools)
                    with zipfile.ZipFile(archive, 'a') as z:
                        z.writestr('unrelated', 'positive-control')
                    self.assertEqual(packed_contract(archive), tools)
                for field, bad in [('precedence', 0), ('postActions', [{'actionId': 'restore'}])]:
                    old = config.get(field); config[field] = bad
                    with patch(__name__ + '.CONFIG_SHA', write()):
                        with self.assertRaisesRegex(ValueError, 'default-contract'): packed_contract(archive)
                    if old is None: config.pop(field)
                    else: config[field] = old
                config['symbols']['lifecycle']['defaultValue'] = 'sdd'
                with patch(__name__ + '.CONFIG_SHA', write()):
                    with self.assertRaisesRegex(ValueError, 'default-contract'): packed_contract(archive)
                config['symbols']['lifecycle']['defaultValue'] = 'typed-sdd'
                tools['tools']['fable']['version'] = '0.0.0'
                with patch(__name__ + '.CONFIG_SHA', write()):
                    with self.assertRaisesRegex(ValueError, 'tool-pins'): packed_contract(archive)
                with self.assertRaisesRegex(ValueError, 'config-drift'): packed_contract(archive)

        def test_knowledge_order_retains_private_export_bundle_and_budget(self):
            from contextlib import contextmanager
            events = []
            candidate, public = Mock(), Mock()
            candidate.probe.side_effect = lambda *a: events.append('capture-private') or {'initialCommit': 'setup', 'findingsCommit': 'findings'}
            public.ordinary_ci_limits.side_effect = lambda *a: events.append('exact-plus-one-restored') or {}
            @contextmanager
            def probes(*args): yield candidate, public
            with patch.object(M, 'snapshot', return_value={}), patch.object(C, 'checked', side_effect=lambda *a: events.append('ordinary') or {}), \
                 patch.object(C, 'frozen_probes', probes), patch.object(C, 'post_private_export', side_effect=lambda *a: events.append('all-shared-after-private') or {}), \
                 patch.object(M, 'bundle_recovery', side_effect=lambda *a: events.append('distinct-git-bundle') or {}):
                result = knowledge(Mock(), {'sourceRoot': '/sealed', 'sddExecutable': '/sdd'}, Path('/receiver'), Path('/case'))
            self.assertEqual(events, ['ordinary', 'capture-private', 'all-shared-after-private', 'distinct-git-bundle', 'exact-plus-one-restored', 'ordinary'])
            self.assertEqual(result['probe']['probeSetupCommit'], 'setup')
            self.assertNotIn('initialCommit', result['probe'])

        def test_driver_normal_commit_precedes_probe_and_failure_is_retained(self):
            with tempfile.TemporaryDirectory() as td:
                base = Path(td) / 'fresh'
                archive = Path(td) / 'original'; archive.write_bytes(b'synthetic')
                m = {'workRoot': str(base), 'report': str(base / 'report.json'), 'sddExecutable': '/sdd', 'dotnetExecutable': '/dotnet',
                     'packages': {k: {'archive': str(archive)} for k in PACKAGES},
                     'runnerPort': str(Path(__file__).resolve()), 'inputs': {str(Path(__file__).resolve()): digest(__file__)}}
                runner = Mock(); runner.sequence = 0; runner.call.return_value.stdout = '2.2.0\n'
                def factory(base, env, *, deadline):
                    self.assertTrue((base / 'tmp').is_dir())
                    runner.deadline = deadline; runner.env = env
                    return runner
                events = []
                def caller(*a): events.append('caller-initial'); return {'commit': 'caller'}
                def probe(*a):
                    events.append('probe')
                    self.assertEqual(events[-2:], ['caller-initial', 'probe'])
                    return {'probe': {'probeSetupCommit': 'separate'}}
                with patch(__name__ + '.validate', return_value=m), patch.object(M, 'env_for', return_value={}), \
                     patch.object(C, 'registration', return_value={}), \
                     patch(__name__ + '.raw_boundary'), patch(__name__ + '.initialize', return_value={}), \
                     patch(__name__ + '.initial', side_effect=caller), patch(__name__ + '.restore_tools'), \
                     patch(__name__ + '.knowledge', side_effect=probe):
                    result = qualify('/sealed', 'digest', runner_factory=factory)
                    self.assertEqual(result['state'], 'passed'); self.assertEqual(len(result['rows']), 2)
                self.assertEqual(events, ['caller-initial', 'probe', 'caller-initial', 'probe'])
                # A failed actual command retains its fresh root/report rather than a cleanup/retry.
                failed = Path(td) / 'failed'; m['workRoot'] = str(failed); m['report'] = str(failed / 'report.json')
                runner.call.side_effect = ValueError('original-command-failure')
                with patch(__name__ + '.validate', return_value=m), patch.object(M, 'env_for', return_value={}), patch.object(C, 'Runner', side_effect=AssertionError('unbounded-fallback')):
                    with self.assertRaisesRegex(ValueError, 'original-command-failure'): qualify('/sealed', 'digest', runner_factory=factory)
                saved = C.read_json(m['report'])
                self.assertEqual(saved['state'], 'failed'); self.assertEqual(saved['failure'], 'original-command-failure')
                self.assertTrue(failed.is_dir())
                dual = Path(td) / 'dual-fault'; m['workRoot'] = str(dual); m['report'] = str(dual / 'report.json')
                primary = ValueError('same-primary-command-failure'); runner.call.side_effect = primary
                write_text = Path.write_text
                def broken_failure_report(path, data, *args, **kwargs):
                    if path.name == 'report.json' and json.loads(data)['state'] == 'failed':
                        raise OSError('secondary-custody-write-failure')
                    return write_text(path, data, *args, **kwargs)
                with patch(__name__ + '.validate', return_value=m), patch.object(M, 'env_for', return_value={}), \
                     patch.object(Path, 'write_text', broken_failure_report):
                    with self.assertRaises(ValueError) as caught: qualify('/sealed', 'digest', runner_factory=factory)
                self.assertIs(caught.exception, primary)
                self.assertIn('failure-report-custody-Unknown:OSError', caught.exception.__notes__)
                self.assertEqual(C.read_json(m['report'])['state'], 'started')

        def test_missing_or_unbound_runner_refuses_before_runtime(self):
            with patch(__name__ + '.validate', side_effect=AssertionError('manifest-read-before-port-refusal')):
                with self.assertRaisesRegex(ValueError, 'qualified-runner-factory-required'): qualify('/sealed', 'digest')
            def factory(*args, **kwargs): raise AssertionError('unqualified-factory-invoked')
            with tempfile.TemporaryDirectory() as td:
                p = Path(td) / 'other-source'; p.write_text('not-the-factory')
                m = {'runnerPort': str(p), 'inputs': {str(p): digest(p)}}
                with self.assertRaisesRegex(ValueError, 'exact-qualified-runner-source-required'):
                    injected_runner(factory, m, Path(td), time.monotonic() + 10)

        def test_raw_boundary_rejects_automatic_store_and_skeleton(self):
            with tempfile.TemporaryDirectory() as td:
                root = Path(td)
                positive = root / 'Domain/ArenaRules.fs'
                positive.parent.mkdir(parents=True); positive.write_text('player')
                with patch.object(M, 'assets'), patch(__name__ + '.game_manifest', return_value={}), patch(__name__ + '.packed_contract', return_value={}):
                    raw_boundary(root, '/synthetic')
                    for relative in ('.fsgg/knowledge', '.fsgg/knowledge-guide.md', *LIFECYCLE_PATHS, 'SvgFoundation/Studio', 'models'):
                        p = root / relative; p.parent.mkdir(parents=True, exist_ok=True); p.mkdir()
                        with self.assertRaises(ValueError): raw_boundary(root, '/synthetic')
                        p.rmdir()
                    positive.unlink()
                    with self.assertRaisesRegex(ValueError, 'player-positive-control'): raw_boundary(root, '/synthetic')

        def test_owner_bytes_modes_and_ignore_prefix_causal_controls(self):
            with tempfile.TemporaryDirectory() as td:
                root = Path(td); owner = root / 'owner'; owner.write_text('owned'); owner.chmod(0o640)
                ignore = root / '.gitignore'; ignore.write_bytes(b'prefix\n')
                before = M.snapshot(root)
                ignore.write_bytes(b'prefix\nextra\n'); preserved(before, root, ignore_prefix=b'prefix\n')
                ignore.write_bytes(b'replaced\n')
                with self.assertRaisesRegex(ValueError, 'ignore-prefix'): preserved(before, root, ignore_prefix=b'prefix\n')
                ignore.write_bytes(b'prefix\n'); owner.chmod(0o600)
                with self.assertRaisesRegex(ValueError, 'content-or-mode'): preserved(before, root)
                owner.chmod(0o640); owner.write_text('changed')
                with self.assertRaisesRegex(ValueError, 'content-or-mode'): preserved(before, root)

        def test_game_tool_entries_must_preserve_full_owner_metadata(self):
            original = {'version': 1, 'isRoot': True, 'tools': {k: {'version': v, 'commands': [k], 'rollForward': False} for k, v in RAW_PINS.items()}}
            value = json.loads(json.dumps(original)); value['tools']['fs.gg.sdd.cli'] = {'version': '2.2.0', 'commands': ['fsgg-sdd']}
            with patch.object(M, 'manifest', return_value=value):
                game_manifest(Path('/synthetic'), True, original)
                value['tools']['fantomas']['rollForward'] = True
                with self.assertRaisesRegex(ValueError, 'tool-entry-changed'): game_manifest(Path('/synthetic'), True, original)
                value['tools']['fantomas']['rollForward'] = False; value['isRoot'] = False
                with self.assertRaisesRegex(ValueError, 'tool-metadata-changed'): game_manifest(Path('/synthetic'), True, original)

        def test_standalone_install_then_initialize_and_owner_preservation(self):
            with tempfile.TemporaryDirectory() as td:
                root = Path(td); (root / '.gitignore').write_text('prefix\n')
                runner = Mock(); events = []
                runner.call.side_effect = lambda *a, **k: events.append(a[0])
                runner.knowledge.side_effect = lambda *a: events.append(a)
                with patch(__name__ + '.game_manifest', return_value={}), patch(__name__ + '.preserved', return_value=M.snapshot(root)):
                    initialize(runner, {'dotnetExecutable': '/dotnet', 'sddExecutable': '/sdd'}, root, '/config')
                self.assertEqual(events[0][1:5], ['tool', 'install', 'FS.GG.SDD.Cli', '--version'])
                self.assertEqual(events[1][1:3], ('initialize', '--root'))
                self.assertNotIn('--force', events[0]); self.assertNotIn('init', events[0])

    # A source-control regression must fail before any process can be launched.
    with patch.object(C.subprocess, 'Popen', side_effect=AssertionError('source-controls-launched-child')):
        result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Controls))
    require(result.wasSuccessful(), 'raw-game-source-controls-failed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-controls', action='store_true')
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--approved-manifest-sha256')
    args = parser.parse_args()
    if args.source_controls:
        source_controls(); return
    require(args.manifest is not None and args.approved_manifest_sha256, 'sealed-game-manifest-required')
    print(json.dumps(qualify(args.manifest, args.approved_manifest_sha256), indent=2))


if __name__ == '__main__':
    main()
