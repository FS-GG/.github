#!/usr/bin/env python3
"""Public019 RAW legacy non-SVG: typed knowledge and genuine omitted sdd preservation.

Pure controls launch no child. Actual receivers need a separately admitted sealed
manifest and the existing qualified finite runner factory; no product is built.
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
PREFIX = 'content/templates/fs-gg-fable-game-legacy/'
CONFIG_SHA = 'dd8feae9693b7b7dae3bda1b3cf7ea61c9b6cc5a568e8efeed5021c56d086e34'
CURRENT_CONFIG_SHA = 'f0f81f93b923084a56a42e2f8d008eb6fd7c5078afbc84b3b3781cfcfc2ffcff'
ROWS = ({'id': 'raw-fable-legacy-explicit-typed', 'product': 'RawLegacyTyped', 'explicit': True},
        {'id': 'raw-fable-legacy-omitted-sdd', 'product': 'RawLegacyOmitted', 'explicit': False})
OWNER_FILES = ('Client/Client.fsproj', 'Client/App.fs', 'Server/Server.fsproj', 'Server/Program.fs',
               'Domain/Domain.fsproj', 'Domain/ArenaRules.fs', 'Protocol/Protocol.fsproj',
               'Client/packages.lock.json', 'Server/packages.lock.json', 'Domain/packages.lock.json',
               'Protocol/packages.lock.json', 'Directory.Build.props', 'NuGet.config',
               '.config/dotnet-tools.json', '.gitignore')
EXCLUDED = ('SvgFoundation', 'models', 'Conformance/GeneratedArenaRuleModel.fs',
            'Conformance/GeneratedTacticalRuleModel.fs', 'PortalExample')


def require(ok, reason):
    if not ok: raise ValueError(reason)


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_ports():
    path = Path(__file__).with_name('knowledge-fable-game-public.py').resolve()
    require(digest(path) == PLAYER_SHA, 'accepted-player-ports-drift')
    module = types.ModuleType('sealed_legacy_knowledge_ports'); module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


P = load_ports()
M, C = P.M, P.C
PACKAGES, TOP_KEYS = P.PACKAGES, P.TOP_KEYS
initialize, restore_tools, knowledge, injected_runner = P.initialize, P.restore_tools, P.knowledge, P.injected_runner


def packed_contract(archive):
    with zipfile.ZipFile(archive) as z:
        raw = z.read(PREFIX + '.template.config/template.json')
        require(hashlib.sha256(raw).hexdigest() == CONFIG_SHA, 'public-legacy-config-drift')
        value = json.loads(raw)
        require(value['identity'] == 'FS.GG.Workspace.Template.FableGame.LegacySvgFoundation'
                and value['groupIdentity'] == 'FS.GG.Workspace.Template.FableGame'
                and value['precedence'] == 0 and value['shortName'] == 'fs-gg-fable-game'
                and value['symbols']['svgFoundation']['isRequired'] is True
                and {'true', 'false'} == {v['choice'] for v in value['symbols']['svgFoundation']['choices']}
                and value['symbols']['lifecycle']['defaultValue'] == 'sdd'
                and 'bundle' not in value['symbols'] and not value.get('postActions'), 'public-legacy-default-contract')
        modifiers = value['sources'][0]['modifiers']
        non_typed = [v for v in modifiers if v.get('condition') == '(lifecycle != "typed-sdd")']
        non_svg = [v for v in modifiers if v.get('condition') == '(svgFoundation == false)']
        require(len(non_typed) == 1 and set(non_typed[0]['exclude']) == set(M.CI_FILES), 'legacy-conditional-knowledge-assets')
        require(len(non_svg) == 1 and set(non_svg[0]['exclude']) == {
            'SvgFoundation/**', 'models/**', 'Conformance/GeneratedArenaRuleModel.fs',
            'Conformance/GeneratedTacticalRuleModel.fs'}, 'legacy-non-svg-modifier')
        tools_raw = z.read(PREFIX + '.config/dotnet-tools.json')
        tools = json.loads(tools_raw)
        require({k: v['version'] for k, v in tools['tools'].items()} == P.RAW_PINS, 'legacy-packed-tool-pins')
        # Authenticate the competing higher-precedence selector separately. It
        # cannot supply the legacy omission semantics; this join permits reuse
        # of the existing preserving initial-commit port's identical CI bytes.
        current_raw = z.read(P.PREFIX + '.template.config/template.json')
        require(hashlib.sha256(current_raw).hexdigest() == CURRENT_CONFIG_SHA, 'current-selector-config-drift')
        current = json.loads(current_raw)
        require(current['identity'] == 'FS.GG.Workspace.Template.FableGame' and current['precedence'] == 100
                and current['symbols']['lifecycle']['defaultValue'] == 'typed-sdd'
                and current['symbols']['bundle']['defaultValue'] == 'player', 'current-selector-distinction')
        require(tools_raw == z.read(P.PREFIX + '.config/dotnet-tools.json')
                and all(z.read(PREFIX + name) == z.read(P.PREFIX + name) for name in M.CI_FILES),
                'legacy-preserving-port-original-byte-join')
        return tools


def command(m, row, root):
    require(row in ROWS, 'unknown-legacy-subject')
    argv = [m['dotnetExecutable'], 'new', 'fs-gg-fable-game', '--name', row['product'],
            '--output', root, '--svgFoundation', 'false']
    return argv + (['--lifecycle', 'typed-sdd'] if row['explicit'] else [])


def raw_boundary(root, archive, row):
    require(row in ROWS, 'unknown-legacy-subject')
    tools = packed_contract(archive)
    require(not (root / '.fsgg/knowledge').exists() and not (root / '.fsgg/knowledge-guide.md').exists(),
            'raw-legacy-auto-knowledge')
    require(all(not (root / p).exists() for p in P.LIFECYCLE_PATHS), 'raw-legacy-auto-lifecycle')
    require(all((root / p).is_file() for p in OWNER_FILES), 'raw-legacy-positive-owner-assets')
    require(all(not (root / p).exists() for p in EXCLUDED), 'raw-legacy-excluded-svg-model-content')
    with zipfile.ZipFile(archive) as z:
        for name in ('.config/dotnet-tools.json', '.gitignore'):
            require((root / name).read_bytes() == z.read(PREFIX + name), 'legacy-raw-owner-bytes:' + name)
    P.game_manifest(root, original_tools=tools)
    if row['explicit']:
        M.assets(root, archive, PREFIX)
    else:
        require(all(not (root / p).exists() for p in M.CI_FILES), 'omitted-sdd-knowledge-assets-present')
    return dict(effectiveLifecycle='typed-sdd' if row['explicit'] else 'sdd',
                svgFoundation=False, bundleParameter=False, lifecycleOmitted=not row['explicit'],
                rawCreationInitializesStore=False, knowledgeReady=False)


def initial(runner, root, archive, row):
    packed_contract(archive)
    if row['explicit']:
        # packed_contract proves the exact legacy/current CI-byte join used by
        # this accepted port; no default/composition helper is borrowed.
        result = P.initial(runner, root, archive)
    else:
        raw_boundary(root, archive, row)
        M.ensure_git(runner, root)
        require(not runner.git(root, 'rev-list', '--all'), 'unexpected-legacy-background-commit')
        before = M.snapshot(root)
        cache = root / 'Client/obj/legacy-control.json'
        cache.parent.mkdir(parents=True, exist_ok=True); cache.write_text('{}')
        relative_cache = cache.relative_to(root).as_posix()
        require(runner.git(root, 'check-ignore', '--', relative_cache).strip() == relative_cache, 'legacy-cache-not-ignored')
        result = dict(commit=M.commit(runner, root, 'Initial raw legacy non-SVG Standard owner files'))
        P.preserved(before, root)
        require(cache.relative_to(root).as_posix() not in runner.git(root, 'ls-files').splitlines(), 'legacy-cache-in-initial-commit')
    tracked = set(runner.git(root, 'ls-files').splitlines())
    require(set(OWNER_FILES) <= tracked, 'legacy-owner-files-untracked')
    blobs = {}
    for name in OWNER_FILES:
        raw = runner.run(['git', '-C', root, 'show', result['commit'] + ':' + name], text=False, check=True).stdout
        require(raw == (root / name).read_bytes(), 'legacy-initial-owner-blob:' + name)
        blobs[name] = hashlib.sha256(raw).hexdigest()
    result['legacyOwnerCommittedAssets'] = blobs
    return result


def validate(path, expected_sha, after=False):
    require(expected_sha and digest(path) == expected_sha, 'manifest-digest')
    m = C.read_json(path)
    require(set(m) == TOP_KEYS and m['schema'] == 'fsgg.raw-fable-legacy-knowledge/1', 'closed-legacy-manifest')
    require(set(m['packages']) == set(PACKAGES), 'closed-public-legacy-packages')
    for name in m['inputs']: C.regular(name, m['inputs'])
    C.regular(m['runnerPort'], m['inputs'])
    for name in (__file__, P.__file__, M.__file__, C.__file__): C.regular(Path(name).resolve(), m['inputs'])
    for relative, sha in C.SOURCE_HASHES.items():
        if relative.startswith('tests/knowledge-receiver/'):
            require(digest(C.regular(Path(m['sourceRoot']) / relative, m['inputs'])) == sha, 'frozen-legacy-probe-source')
    for key, identity in PACKAGES.items():
        binding = m['packages'][key]
        require(set(binding) == ({'archive', 'publicationReceipts', 'closure'} if key == 'producer'
                                else {'archive', 'publicationReceipts'}), 'closed-legacy-package-binding')
        members = M.public_package(key, binding, m['inputs'], identity)
        if key == 'producer': C.tool_closure(binding, m['sddExecutable'], m['inputs'], members, 'FS.GG.SDD.Cli.dll')
    packed_contract(m['packages']['workspace']['archive'])
    env = m['childEnvironment']
    require(set(env) == {'PATH', *M.SERIAL_ENV, *M.HOST_ENV}
            and all(env[k] == v for k, v in {**M.SERIAL_ENV, **M.HOST_ENV}.items()), 'closed-legacy-host-environment')
    require(m['actualHome'] == os.environ.get('HOME'), 'actual-home')
    for name, expected in (('dotnet', m['dotnetExecutable']), ('fsgg-sdd', m['sddExecutable'])):
        selected = shutil.which(name, path=env['PATH'])
        require(selected and Path(selected).resolve() == Path(expected).resolve(), 'selected-legacy-executable')
        C.regular(Path(expected), m['inputs'])
    git = shutil.which('git', path=env['PATH']); require(git, 'missing-bound-git')
    for executable in (Path(git).resolve(), Path(sys.executable).resolve()): C.regular(executable, m['inputs'])
    root = Path(m['workRoot'])
    require(root.is_absolute() and root == root.resolve() and not root.is_symlink()
            and Path(m['report']).parent == root and (after or not root.exists()), 'fresh-legacy-output-no-replay')
    return m


def qualify(path, expected_sha, *, runner_factory=None):
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
    report = dict(schema='fsgg.raw-fable-legacy-knowledge-result/1', state='started', manifestSha256=expected_sha,
                  outerSeconds=600, perCommandMaxSeconds=180, rows=[], runtimeAdmission='external-required',
                  unfulfilled=['independent result acceptance', 'other parent4642 variants/routes', 'product/browser/provider/Wizard acceptance'])
    def save(): Path(m['report']).write_text(json.dumps(report, indent=2) + '\n')
    save()
    try:
        require(runner.call([m['sddExecutable'], '--version']).stdout.strip() == '2.2.0', 'actual-public-legacy-producer')
        for row in ROWS:
            case = base / row['id']; case.mkdir()
            for name in ('tmp', 'dotnet-home', 'packages', 'http-cache'): (case / name).mkdir()
            runner.env = M.env_for(m, case); root = case / 'receiver'
            entry = dict(id=row['id'], state='started'); report['rows'].append(entry); save()
            runner.call([m['dotnetExecutable'], 'new', 'install', m['packages']['workspace']['archive']], cwd=case)
            runner.call(command(m, row, root), cwd=case)
            entry['registration'] = C.registration(case, '0.19.0', PACKAGES['workspace'][3])
            entry['raw'] = raw_boundary(root, m['packages']['workspace']['archive'], row)
            before = M.snapshot(root)
            ignore_prefix = (root / '.gitignore').read_bytes()
            if row['explicit']:
                entry['initialization'] = initialize(runner, m, root, config)
                entry['initial'] = initial(runner, root, m['packages']['workspace']['archive'], row); save()
                restore_tools(runner, m, root, config)
                entry['knowledge'] = knowledge(runner, m, root, case)
                require(entry['knowledge']['probe']['probeSetupCommit'] != entry['initial']['commit'], 'legacy-probe-not-caller-initial')
                # Accepted probe changes only guide/ignore/cache/canonical data;
                # the shared port checks all other legacy owner bytes/modes.
                P.preserved(before, root, tool_change=True, ignore_prefix=ignore_prefix)
                P.game_manifest(root, True, packed_contract(m['packages']['workspace']['archive']))
                entry['knowledgeReady'] = True
            else:
                entry['initial'] = initial(runner, root, m['packages']['workspace']['archive'], row)
                raw_boundary(root, m['packages']['workspace']['archive'], row)
                P.preserved(before, root)
                entry.update(knowledgeReady=False, standaloneInitializeInvoked=False, ordinaryKnowledgeCheckerInvoked=False)
            entry['state'] = 'passed'; save()
        require(validate(path, expected_sha, after=True) == m, 'post-legacy-custody-drift')
        require(time.monotonic() < deadline, 'original-legacy-deadline')
        report.update(state='passed', commands=runner.sequence, elapsedSeconds=time.monotonic() - started)
        save(); return report
    except BaseException as error:
        if report['rows'] and report['rows'][-1]['state'] == 'started': report['rows'][-1]['state'] = 'failed'
        report.update(state='failed', commands=runner.sequence, failureType=type(error).__name__, failure=str(error))
        try: save()
        except BaseException as custody_error: error.add_note('failure-report-custody-Unknown:' + type(custody_error).__name__)
        raise


def source_controls():
    class Controls(unittest.TestCase):
        def fixture(self, directory, typed=True):
            root = Path(directory) / 'legacy'; root.mkdir()
            tools = {'version': 1, 'isRoot': True, 'tools': {
                'fable': {'version': '5.18.0', 'commands': ['fable']},
                'fantomas': {'version': '8.0.5', 'commands': ['fantomas'], 'rollForward': False}}}
            for name in OWNER_FILES:
                path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(('owner ' + name).encode())
            (root / '.config/dotnet-tools.json').write_text(json.dumps(tools, indent=2) + '\n')
            (root / '.gitignore').write_bytes(b'original-owner-prefix\nbin/\nobj/\n')
            config = {'identity': 'FS.GG.Workspace.Template.FableGame.LegacySvgFoundation',
                      'groupIdentity': 'FS.GG.Workspace.Template.FableGame', 'precedence': 0,
                      'shortName': 'fs-gg-fable-game', 'symbols': {'svgFoundation': {
                          'isRequired': True, 'choices': [{'choice': 'true'}, {'choice': 'false'}]},
                          'lifecycle': {'defaultValue': 'sdd'}}, 'sources': [{'modifiers': [
                              {'condition': '(lifecycle != "typed-sdd")', 'exclude': list(M.CI_FILES)},
                              {'condition': '(svgFoundation == false)', 'exclude': ['SvgFoundation/**', 'models/**',
                               'Conformance/GeneratedArenaRuleModel.fs', 'Conformance/GeneratedTacticalRuleModel.fs']}]}]}
            current = {'identity': 'FS.GG.Workspace.Template.FableGame', 'precedence': 100,
                       'symbols': {'lifecycle': {'defaultValue': 'typed-sdd'}, 'bundle': {'defaultValue': 'player'}}}
            archive = Path(directory) / 'workspace.nupkg'
            original_tools = (root / '.config/dotnet-tools.json').read_bytes()
            original_ignore = (root / '.gitignore').read_bytes()
            def write():
                with zipfile.ZipFile(archive, 'w') as z:
                    for prefix, cfg in ((PREFIX, config), (P.PREFIX, current)):
                        z.writestr(prefix + '.template.config/template.json', json.dumps(cfg).encode())
                        z.writestr(prefix + '.config/dotnet-tools.json', original_tools)
                        z.writestr(prefix + '.gitignore', original_ignore)
                        for name in M.CI_FILES: z.writestr(prefix + name, ('knowledge CI ' + name).encode())
                return dict(CONFIG_SHA=hashlib.sha256(json.dumps(config).encode()).hexdigest(),
                            CURRENT_CONFIG_SHA=hashlib.sha256(json.dumps(current).encode()).hexdigest())
            if typed:
                for name in M.CI_FILES:
                    path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(('knowledge CI ' + name).encode())
            return root, archive, config, current, write

        def test_fixed_required_selection_and_real_omission(self):
            args = [command({'dotnetExecutable': '/dotnet'}, row, Path('/fresh')) for row in ROWS]
            for argv in args:
                self.assertEqual(argv[argv.index('--svgFoundation') + 1], 'false'); self.assertNotIn('--bundle', argv)
            self.assertEqual(args[0][-2:], ['--lifecycle', 'typed-sdd']); self.assertNotIn('--lifecycle', args[1])
            changed = dict(ROWS[1], explicit=True)
            with self.assertRaisesRegex(ValueError, 'unknown-legacy'): command({'dotnetExecutable': '/dotnet'}, changed, Path('/fresh'))

        def test_packed_required_default_and_competing_selector_are_causal(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, cfg, current, write = self.fixture(td)
                with patch.multiple(__name__, **write()): packed_contract(archive)
                for key, wrong in [('precedence', 100), ('identity', 'FS.GG.Workspace.Template.FableGame')]:
                    original = cfg[key]; cfg[key] = wrong
                    with patch.multiple(__name__, **write()):
                        with self.assertRaisesRegex(ValueError, 'legacy-default'): packed_contract(archive)
                    cfg[key] = original
                cfg['symbols']['lifecycle']['defaultValue'] = 'typed-sdd'
                with patch.multiple(__name__, **write()):
                    with self.assertRaisesRegex(ValueError, 'legacy-default'): packed_contract(archive)
                cfg['symbols']['lifecycle']['defaultValue'] = 'sdd'; cfg['symbols']['svgFoundation']['isRequired'] = False
                with patch.multiple(__name__, **write()):
                    with self.assertRaisesRegex(ValueError, 'legacy-default'): packed_contract(archive)
                cfg['symbols']['svgFoundation']['isRequired'] = True; current['precedence'] = 0
                with patch.multiple(__name__, **write()):
                    with self.assertRaisesRegex(ValueError, 'current-selector'): packed_contract(archive)

        def test_conditional_ci_and_non_svg_exclusions_cannot_drift(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, cfg, current, write = self.fixture(td)
                cfg['sources'][0]['modifiers'][0]['exclude'].pop()
                with patch.multiple(__name__, **write()):
                    with self.assertRaisesRegex(ValueError, 'conditional-knowledge'): packed_contract(archive)
                cfg['sources'][0]['modifiers'][0]['exclude'] = list(M.CI_FILES)
                cfg['sources'][0]['modifiers'][1]['exclude'].pop()
                with patch.multiple(__name__, **write()):
                    with self.assertRaisesRegex(ValueError, 'non-svg-modifier'): packed_contract(archive)

        def test_both_raw_rows_need_positive_owners_and_no_svg_models(self):
            for row in ROWS:
                with tempfile.TemporaryDirectory() as td:
                    root, archive, cfg, current, write = self.fixture(td, row['explicit'])
                    with patch.multiple(__name__, **write()):
                        result = raw_boundary(root, archive, row)
                        self.assertEqual(result['effectiveLifecycle'], 'typed-sdd' if row['explicit'] else 'sdd')
                        for name in OWNER_FILES:
                            path = root / name; data = path.read_bytes(); path.unlink()
                            with self.assertRaisesRegex(ValueError, 'positive-owner'): raw_boundary(root, archive, row)
                            path.write_bytes(data)
                        for name in EXCLUDED:
                            path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.mkdir()
                            with self.assertRaisesRegex(ValueError, 'excluded-svg'): raw_boundary(root, archive, row)
                            path.rmdir()

        def test_real_omitted_row_refuses_knowledge_lifecycle_checker_and_sdd_pin(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, cfg, current, write = self.fixture(td, False)
                with patch.multiple(__name__, **write()):
                    for name in ('.fsgg/knowledge', '.fsgg/knowledge-guide.md', 'sdd', *M.CI_FILES):
                        path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('unexpected')
                        with self.assertRaises(ValueError): raw_boundary(root, archive, ROWS[1])
                        path.unlink()
                    path = root / '.config/dotnet-tools.json'; original = path.read_bytes()
                    changed = json.loads(original); changed['tools']['fs.gg.sdd.cli'] = {'version': '2.2.0', 'commands': ['fsgg-sdd']}
                    path.write_text(json.dumps(changed))
                    with self.assertRaisesRegex(ValueError, 'raw-owner-bytes'): raw_boundary(root, archive, ROWS[1])

        def test_owner_bytes_pins_modes_and_ignore_prefix_preservation(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, cfg, current, write = self.fixture(td); write(); before = M.snapshot(root)
                path = root / 'Domain/ArenaRules.fs'; original = path.read_bytes(); path.write_bytes(b'changed')
                with self.assertRaisesRegex(ValueError, 'content-or-mode'): P.preserved(before, root)
                path.write_bytes(original); path.chmod(0o600)
                with self.assertRaisesRegex(ValueError, 'content-or-mode'): P.preserved(before, root)
                path.chmod(before['Domain/ArenaRules.fs']['mode']); path = root / '.config/dotnet-tools.json'; original = path.read_bytes()
                changed = json.loads(original); changed['tools']['fable']['rollForward'] = False; path.write_text(json.dumps(changed))
                with self.assertRaisesRegex(ValueError, 'content-or-mode'): P.preserved(before, root)
                path.write_bytes(original); ignore = root / '.gitignore'; prefix = ignore.read_bytes(); ignore.write_bytes(prefix + b'new-exclusion\n')
                P.preserved(before, root, ignore_prefix=prefix); ignore.write_bytes(b'wrong-prefix')
                with self.assertRaisesRegex(ValueError, 'ignore-prefix'): P.preserved(before, root, ignore_prefix=prefix)

        def test_initial_owner_blobs_and_typed_initial_port_join(self):
            for row in ROWS:
                with tempfile.TemporaryDirectory() as td:
                    root, archive, cfg, current, write = self.fixture(td, row['explicit']); runner = Mock(); committed = []
                    runner.git.side_effect = lambda root, *args: ('' if args[0]=='rev-list' else args[-1] if args[0]=='check-ignore' else '\n'.join(OWNER_FILES))
                    runner.run.side_effect = lambda argv, **kw: types.SimpleNamespace(stdout=(root / argv[-1].split(':', 1)[1]).read_bytes())
                    with patch.multiple(__name__, **write()), patch.object(P, 'initial', return_value={'commit':'caller'}) as typed, \
                         patch.object(M, 'ensure_git'), patch.object(M, 'commit', side_effect=lambda *a: committed.append('caller') or 'caller'):
                        result = initial(runner, root, archive, row); self.assertEqual(len(result['legacyOwnerCommittedAssets']), len(OWNER_FILES))
                        self.assertEqual(typed.call_count, int(row['explicit']))
                        if not row['explicit']:
                            positive_git = runner.git.side_effect
                            runner.git.side_effect = lambda root, *args: '' if args[0]=='check-ignore' else positive_git(root,*args)
                            with self.assertRaisesRegex(ValueError, 'cache-not-ignored'): initial(runner, root, archive, row)
                            runner.git.side_effect = positive_git
                        runner.run.side_effect = None; runner.run.return_value.stdout = b'wrong'
                        with self.assertRaisesRegex(ValueError, 'owner-blob'): initial(runner, root, archive, row)

        def test_preserving_port_join_refuses_different_current_owner_bytes(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, cfg, current, write = self.fixture(td)
                hashes = write()
                with zipfile.ZipFile(archive) as z: members = {n:z.read(n) for n in z.namelist()}
                members[P.PREFIX + M.CI_FILES[0]] += b'wrong-current-CI'
                with zipfile.ZipFile(archive, 'w') as z:
                    for name, data in members.items(): z.writestr(name, data)
                with patch.multiple(__name__, **hashes):
                    with self.assertRaisesRegex(ValueError, 'original-byte-join'): packed_contract(archive)

        def test_actual_injected_factory_refuses_late_changed_environment_and_frozen_runner(self):
            m = {'runnerPort':__file__, 'inputs':{str(Path(__file__).resolve()):digest(__file__)}}
            runner = Mock(); runner.sequence=0; mode=['valid']
            def factory(base, env, *, deadline):
                runner.env = env if mode[0]!='environment' else {'wrong':'environment'}
                runner.deadline = deadline if mode[0]!='late' else deadline+1
                return runner
            with patch.object(M, 'env_for', return_value={}):
                self.assertIs(injected_runner(factory,m,Path('/private'),100),runner)
                for wrong in ('late','environment'):
                    mode[0]=wrong
                    with self.assertRaisesRegex(ValueError,'original-deadline-and-environment'):
                        injected_runner(factory,m,Path('/private'),100)
                class Frozen:
                    run=C.Runner.run
                    call=git=knowledge=lambda *a:None
                old=Frozen();old.sequence=0;old.deadline=100;old.env={}
                def frozen_factory(base,env,*,deadline):return old
                with self.assertRaisesRegex(ValueError,'qualified-runner-ports'):
                    injected_runner(frozen_factory,m,Path('/private'),100)

        def test_genuine_scratch_initialize_keeps_legacy_owner_bytes(self):
            with tempfile.TemporaryDirectory() as td:
                root, archive, cfg, current, write = self.fixture(td); before = M.snapshot(root); runner = Mock(); events=[]
                runner.env={'NUGET_PACKAGES':str(Path(td)/'packages')}
                def install(argv, **kw):
                    self.assertFalse(Path(kw['cwd']).is_relative_to(root)); P.preserved(before,root)
                    target=Path(argv[argv.index('--tool-manifest')+1]);self.assertFalse(target.is_relative_to(root))
                    target.write_text(json.dumps({'version':1,'isRoot':True,'tools':{'fs.gg.sdd.cli':{'version':'2.2.0','commands':['fsgg-sdd'],'rollForward':False}}}));events.append('scratch')
                runner.call.side_effect=install;runner.knowledge.side_effect=lambda *a:events.append(a[1])
                with patch.object(M,'package_payload',return_value={}): result=initialize(runner,{'dotnetExecutable':'/dotnet','sddExecutable':'/sdd','packages':{'producer':{'archive':'sealed'}}},root,'/config')
                self.assertEqual(events,['scratch','initialize']);self.assertTrue(result['toolAddition']['ownerByteSpansPreserved']);P.preserved(before,root,tool_change=True)

        def test_actual_driver_is_asymmetric_commit_before_probe_and_preserves_primary(self):
            with tempfile.TemporaryDirectory() as td:
                base=Path(td)/'fresh';archive=Path(td)/'archive';archive.write_bytes(b'fixture')
                m={'workRoot':str(base),'report':str(base/'report.json'),'sddExecutable':'/sdd','dotnetExecutable':'/dotnet','packages':{k:{'archive':str(archive)} for k in PACKAGES},
                   'runnerPort':__file__,'inputs':{str(Path(__file__).resolve()):digest(__file__)}}
                runner=Mock();runner.sequence=0;runner.call.return_value.stdout='2.2.0\n';events=[];original_read=Path.read_bytes
                def factory(base, env, *, deadline):
                    runner.deadline=deadline;runner.env=env;return runner
                def commit(r,root,archive,row):events.append('initial:'+row['id']);return {'commit':'caller'}
                def probe(*a):self.assertEqual(events[-1],'initial:'+ROWS[0]['id']);events.append('probe');return {'probe':{'probeSetupCommit':'separate'}}
                with patch(__name__+'.validate',return_value=m),patch.object(M,'env_for',return_value={}),patch.object(C,'registration',return_value={}),patch(__name__+'.raw_boundary',return_value={}),patch.object(M,'snapshot',return_value={}),patch.object(Path,'read_bytes',autospec=True,side_effect=lambda f: b'owner-ignore' if f.name=='.gitignore' else original_read(f)),patch.object(P,'preserved'),patch(__name__+'.initialize',return_value={}) as init,patch(__name__+'.restore_tools') as restore,patch(__name__+'.initial',side_effect=commit),patch(__name__+'.knowledge',side_effect=probe),patch(__name__+'.packed_contract',return_value={}),patch.object(P,'game_manifest'):
                    result=qualify('/sealed','sha',runner_factory=factory)
                    self.assertEqual(init.call_count,1);self.assertEqual(restore.call_count,1)
                    self.assertEqual([x['knowledgeReady'] for x in result['rows']],[True,False]);self.assertFalse(result['rows'][1]['ordinaryKnowledgeCheckerInvoked'])
                    self.assertEqual(events,['initial:'+ROWS[0]['id'],'probe','initial:'+ROWS[1]['id']])
                creates=[x.args[0] for x in runner.call.call_args_list if x.args[0][1:3]==['new','fs-gg-fable-game']]
                self.assertEqual(len(creates),2);self.assertNotIn('--lifecycle',creates[1]);self.assertTrue(all('--bundle' not in x for x in creates))
                fail=Path(td)/'failed';m.update(workRoot=str(fail),report=str(fail/'report.json'));primary=ValueError('legacy-command-primary');runner.call.side_effect=primary;original_write=Path.write_text
                def fail_save(path,data,*args,**kw):
                    if path.name=='report.json' and json.loads(data)['state']=='failed':raise OSError('secondary')
                    return original_write(path,data,*args,**kw)
                with patch(__name__+'.validate',return_value=m),patch.object(M,'env_for',return_value={}),patch.object(Path,'write_text',fail_save):
                    with self.assertRaises(ValueError) as caught:qualify('/sealed','sha',runner_factory=factory)
                self.assertIs(caught.exception,primary);self.assertIn('failure-report-custody-Unknown:OSError',primary.__notes__)

        def test_missing_qualified_factory_and_wrong_schema_refuse_before_runtime(self):
            with patch(__name__+'.validate',side_effect=AssertionError('no manifest before factory')):
                with self.assertRaisesRegex(ValueError,'qualified-runner-factory-required'):qualify('/sealed','sha')
            with tempfile.TemporaryDirectory() as td:
                f=Path(td)/'m';value=dict.fromkeys(TOP_KEYS);value['schema']='fsgg.raw-fable-complete-knowledge/1';f.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError,'closed-legacy-manifest'):validate(f,digest(f))

    with patch.object(C.subprocess, 'Popen', side_effect=AssertionError('legacy-source-controls-launched-child')):
        result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Controls))
    require(result.wasSuccessful(),'legacy-source-controls-failed')


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source-controls',action='store_true');args=parser.parse_args()
    require(args.source_controls,'root-qualified-runner-factory-and-runtime-admission-required');source_controls()


if __name__=='__main__':main()
