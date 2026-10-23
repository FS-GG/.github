#!/usr/bin/env python3
"""Hosted public qualification of the published Wizard 0.22.0 knowledge rows (FS-GG/.github#4822).

Wizard 0.22.0 (#4811) derives an omitted lifecycle from the provider descriptor's default. This qualifies,
unpinned and on public feeds, with SDD.Cli 2.4.0 / Workspace.Template 0.19.3 / UI.Template 0.32.3:

- Rendering Wizard default, app and game: lifecycle omitted -> the Wizard's own `sdd` default for Rendering
  (only fable-game takes the descriptor default; the Rendering descriptor default is also `sdd`, so these rows
  cannot distinguish the two), no canonical knowledge and no knowledge CI (lane read from the product).
- fable-game PLAYER and COMPLETE, explicit typed-sdd and omitted lifecycle: omitted resolves to the fable-game
  descriptor default `typed-sdd`. Every row must carry the canonical store, guide, exact local pins and the
  package-identical owner CI, then pass the normal initial commit, the maintained #4723 typed probe, the
  ALL-shared export after private capture and the ordinary pinned checker at 10,485,760 / 10,485,761 bytes
  with every generated product byte preserved.
- non-SVG (`svgFoundation=false`) is not a Wizard 0.22.0 route: the parser refuses `--svg-foundation`
  before creating a target. That refusal is a control; it is not a knowledge-ready row.

Helpers come from the accepted Rendering-routes adapter's sealed bytes (which seal R3 and the console
helper). Public nuget.org feeds only, an isolated HOME, no token in any child. `--source-controls` runs pure
causal controls and launches no child.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import sys
import tempfile
import types
import unittest
import unittest.mock
import zipfile

RENDERING_ROUTES_SHA = 'f4eb352ed9ee0fc9c4f701089a767fe22a94bb0bbfd0989e4c37882de26663c9'  # accepted (#4836, 3d45db23)
WIZARD = ('FS.GG.NewSddWorkspace', '0.22.0', 'caa91285e5370edc7a8a14a5a46f6cfef236d1db95f1e186357e6591b9ec7bd5')
# The Wizard fetches descriptors from FS.GG.Templates main at run time (unpinned). They must be byte-identical to
# the descriptors at Templates 24f3c307 that the accepted slices pinned; any later change fails closed.
DESCRIPTOR_SHA = {'rendering': 'ff49732aa5baa4e1b507784c3135ea092b0973c4c4cb3a75197dae0a7a417fc2',
                  'fable-game': '6b3d8f2dcbdcd429e4f622cfd6e1515c439097147bb79f5d5c7969d36c3b5006'}
FABLE_TOOLS = {'fable': '5.18.0', 'fantomas': '8.0.5', 'fs.gg.coord.cli': '0.94.0', 'fs.gg.sdd.cli': '2.4.0'}
ROWS = (
    dict(id='rendering-app-default', template='rendering', profile='app', bundle=None, lifecycle=None, typed=False, product='WzRenderingAppDefault'),
    dict(id='rendering-game-default', template='rendering', profile='game', bundle=None, lifecycle=None, typed=False, product='WzRenderingGameDefault'),
    dict(id='fable-player-typed', template='fable-game', profile=None, bundle=None, lifecycle='typed-sdd', typed=True, product='WzFablePlayerTyped'),
    dict(id='fable-player-default', template='fable-game', profile=None, bundle=None, lifecycle=None, typed=True, product='WzFablePlayerDefault'),
    dict(id='fable-complete-typed', template='fable-game', profile=None, bundle='complete', lifecycle='typed-sdd', typed=True, product='WzFableCompleteTyped'),
    dict(id='fable-complete-default', template='fable-game', profile=None, bundle='complete', lifecycle=None, typed=True, product='WzFableCompleteDefault'),
)


def load_routes():
    path = Path(__file__).with_name('knowledge-rendering-routes-public.py').resolve()
    if hashlib.sha256(path.read_bytes()).hexdigest() != RENDERING_ROUTES_SHA:
        raise ValueError('accepted-rendering-routes-adapter-drift')
    module = types.ModuleType('sealed_rendering_routes')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


V = load_routes()
R, C = V.R, V.C
require, sha, snapshot = R.require, R.sha, R.snapshot
LIMIT, CI_FILES = R.LIMIT, R.CI_FILES


def wizard_argv(wizard, root, row):
    """Unpinned and descriptor `main`, as a public user runs it: no --pinned and no --ref."""
    argv = [wizard, root, row['product'], '--template', row['template']]
    for flag, key in (('--profile', 'profile'), ('--bundle', 'bundle'), ('--lifecycle', 'lifecycle')):
        if row[key] is not None:
            argv += [flag, row[key]]
    return argv + ['--no-governance', '--no-coordination']


def non_svg_argv(wizard, root):
    return [wizard, root, 'WzFableNonSvg', '--template', 'fable-game', '--svg-foundation', 'false', '--no-governance', '--no-coordination']


def check_provenance(provenance, row):
    effective = {p['key']: p['value'] for p in provenance['effectiveParameters']}
    lifecycle = 'typed-sdd' if row['typed'] else 'sdd'
    require(provenance['providerName'] == row['template'] and provenance['outcome'] == 'providerSucceeded'
            and provenance['generator']['version'] == R.PACKAGES['producer'][1], 'wizard-provenance')
    require(effective.get('lifecycle') == lifecycle and effective.get('productName') == row['product'], 'wizard-effective-lifecycle')
    if row['template'] == 'rendering':
        require(effective.get('profile') == row['profile'], 'wizard-effective-profile')
    else:
        require(effective.get('bundle') == (row['bundle'] or 'player'), 'wizard-effective-bundle')
    return effective


def check_descriptor_source(root, row):
    text = (Path(root) / '.fsgg/providers.yml').read_text()
    source = V.RENDERING_DESCRIPTOR[0] if row['template'] == 'rendering' else R.DESCRIPTORS['current'][1]
    require(any(line.split('#', 1)[0].strip() == 'source: ' + source for line in text.splitlines()), 'wizard-descriptor-source')
    digest = sha(Path(root) / '.fsgg/providers.yml')
    require(digest == DESCRIPTOR_SHA[row['template']], 'wizard-descriptor-sha256')
    return dict(source=source, sha256=digest)


def check_tools(root, row):
    tools = {k: v['version'] for k, v in R.tool_pins(root)['tools'].items()} if row['template'] == 'rendering' else None
    if row['template'] == 'rendering':
        require(tools.get('fs.gg.sdd.cli') == R.PACKAGES['producer'][1], 'rendering-sdd-pin')
        return tools
    value = C.read_json(Path(root) / '.config/dotnet-tools.json')
    observed = {k: v['version'] for k, v in value['tools'].items()}
    require(value.get('isRoot') is True and observed == FABLE_TOOLS, 'fable-tool-pins')
    return observed


def check_non_svg_refusal(result, target):
    """The parser itself must reject the option: exit 2, its unknown-argument message, and no target."""
    require(result.returncode == 2 and 'unknown argument: --svg-foundation' in result.stdout + result.stderr
            and not Path(target).exists(), 'non-svg-wizard-route-not-refused')
    return dict(exitCode=result.returncode, reason='unknown argument: --svg-foundation', targetCreated=False)


def check_versions(installed, listed):
    require(installed == R.PACKAGES['producer'][1], 'installed-sdd-not-2.4.0')
    require(listed.get('fs.gg.sdd.cli') == R.PACKAGES['producer'][1] and listed.get('fs.gg.newsddworkspace') == WIZARD[1], 'tool-list-versions')
    return dict(installed=installed, toolList=listed)


def parse_tool_list(text):
    result = {}
    for line in text.splitlines()[2:]:
        parts = line.split()
        if len(parts) >= 2:
            result[parts[0].lower()] = parts[1]
    return result


def wizard_identity(home):
    name = WIZARD[0].lower()
    path = Path(home) / '.dotnet/tools/.store' / name / WIZARD[1] / name / WIZARD[1] / f'{name}.{WIZARD[1]}.nupkg'
    require(path.is_file() and path.resolve().is_relative_to(Path(home).resolve()) and sha(path) == WIZARD[2], 'wizard-sha256')
    return dict(package=WIZARD[0], version=WIZARD[1], sha256=WIZARD[2], match=True)


def run_row(runner, row, case, wizard, sdd, home, templates):
    case.mkdir()
    root = case / 'subject'
    entry = dict(id=row['id'], argv=[str(a) for a in wizard_argv(wizard, root, row)], state='started')
    runner.call(wizard_argv(wizard, root, row), cwd=case, timeout=180)
    entry['effective'] = check_provenance(C.read_json(root / '.fsgg/scaffold-provenance.json'), row)
    entry['descriptor'] = check_descriptor_source(root, row)
    entry['tools'] = check_tools(root, row)
    if row['template'] == 'rendering':
        entry['lane'] = V.check_lane(root, False)
        entry['created'] = V.check_knowledge_state(root, store=False, guide=True, ci=False)
        entry['uiTemplate'] = dict(version=V.UI[1], sha256=sha(V.ui_package(home)))
        entry['state'] = 'passed'
        return entry
    entry['created'] = V.check_knowledge_state(root, store=True, guide=True, ci=True)
    with zipfile.ZipFile(R.template_package(home, 'template')) as archive:
        payload = {n: archive.read('content/templates/fs-gg-fable-game/' + n) for n in CI_FILES}
    for name in CI_FILES:
        require((root / name).read_bytes() == payload[name], 'owner-ci-payload:' + name)
    created = snapshot(root)
    gitignore = (root / '.gitignore').read_bytes() if (root / '.gitignore').is_file() else None
    entry['initialCommit'] = V.initial_commit(runner, root)
    entry['knowledge'] = V.typed_knowledge(runner, sdd, root, templates, case / 'probe')
    entry['preservation'] = V.check_preserved(created, snapshot(root), gitignore, (root / '.gitignore').read_bytes(), False)
    entry['state'] = 'passed'
    return entry


def qualify(templates, current, work):
    work = Path(work)
    require(work.is_absolute() and not work.exists(), 'fresh-output-no-replay')
    templates, current = R.verify_sources(templates, current)
    dotnet = shutil.which('dotnet')
    require(dotnet and shutil.which('git'), 'missing-host-tools')
    work.mkdir(mode=0o700)
    evidence = work / 'evidence'
    evidence.mkdir()
    home, env = R.environment(work, dotnet)
    runner = C.Runner(evidence, env, total_seconds=2700)
    report = dict(schema='fsgg.wizard.default-rows-public-acceptance/1', state='started', readOnly=True, wizardPinned=False,
                  host=dict(system=platform.platform(), python=platform.python_version()), rows=[],
                  expected=dict(wizard=list(WIZARD), ui=list(V.UI), **{k: list(R.PACKAGES[k]) for k in ('producer', 'template', 'coord')}))

    def save():
        (work / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    save()
    try:
        report['host']['dotnet'] = runner.call(['dotnet', '--version']).stdout.strip()
        for package in (WIZARD, R.PACKAGES['producer']):
            runner.call(['dotnet', 'tool', 'install', '--global', package[0], '--version', package[1]])
        wizard = shutil.which('new-sdd-workspace', path=env['PATH'])
        sdd = shutil.which('fsgg-sdd', path=env['PATH'])
        require(wizard and sdd and Path(wizard).resolve().is_relative_to(home.resolve())
                and Path(sdd).resolve().is_relative_to(home.resolve()), 'selected-tools-not-isolated')
        report['packages'] = dict(wizard=wizard_identity(home), producer=R.identity(home, 'producer', R.tool_package(home, 'producer')))
        for row in ROWS:
            report['rows'].append(run_row(runner, row, work / row['id'], wizard, sdd, home, templates)); save()
        control = work / 'non-svg'
        control.mkdir()
        report['nonSvgControl'] = check_non_svg_refusal(runner.call(non_svg_argv(wizard, control / 'subject'), cwd=control, success=False), control / 'subject')
        installed = runner.call([sdd, '--version']).stdout.strip()
        report['readback'] = check_versions(installed, parse_tool_list(runner.call(['dotnet', 'tool', 'list', '--global']).stdout))
        report['packages'].update(producerAfter=R.identity(home, 'producer', R.tool_package(home, 'producer')),
                                  template=R.identity(home, 'template', R.template_package(home, 'template')),
                                  ui=dict(package=V.UI[0], version=V.UI[1], sha256=sha(V.ui_package(home)), match=True),
                                  restoredProducer=R.identity(home, 'producer', R.restored_package(home, 'producer')),
                                  coord=R.identity(home, 'coord', R.restored_package(home, 'coord')))
        report.update(state='passed', commands=runner.sequence); save()
        return report
    except BaseException as error:
        report.update(state='failed', failureType=type(error).__name__, failure=str(error), commands=runner.sequence); save()
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source-controls', action='store_true')
    parser.add_argument('--run', action='store_true')
    parser.add_argument('--templates', type=Path, help=f'Templates checkout at {R.PROBE_REVISION}')
    parser.add_argument('--current', type=Path, help=f'Templates checkout at {R.CURRENT_REVISION}')
    parser.add_argument('--work', type=Path)
    args = parser.parse_args()
    if args.source_controls:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(Controls)
        return 0 if unittest.TextTestRunner(verbosity=1).run(suite).wasSuccessful() else 1
    require(args.run and args.templates and args.current and args.work, 'operation-required')
    report = qualify(args.templates, args.current, args.work)
    print(json.dumps({k: report[k] for k in ('state', 'commands')}))
    return 0


class Controls(unittest.TestCase):
    """Pure causal controls: each invariant is made to fail by the defect it exists to catch."""

    def refuses(self, reason, function, *args, **kwargs):
        with self.assertRaises(ValueError) as caught:
            function(*args, **kwargs)
        self.assertEqual(str(caught.exception), reason)

    def tmp(self):
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR')))
        self.addCleanup(shutil.rmtree, root, True)
        return root

    def test_rows_cover_the_selected_wizard_defaults_and_typed_rows(self):
        self.assertEqual({r['id'] for r in ROWS}, {'rendering-app-default', 'rendering-game-default', 'fable-player-typed',
                                                    'fable-player-default', 'fable-complete-typed', 'fable-complete-default'})
        self.assertTrue(all(r['lifecycle'] is None for r in ROWS if r['id'].endswith('-default')))
        self.assertEqual(len({r['product'] for r in ROWS}), len(ROWS))

    def test_argv_is_unpinned_and_omits_exactly_what_the_row_omits(self):
        for row in ROWS:
            argv = wizard_argv('wiz', 'root', row)
            self.assertNotIn('--pinned', argv); self.assertNotIn('--ref', argv)
            self.assertEqual('--lifecycle' in argv, row['lifecycle'] is not None)
            self.assertEqual('--bundle' in argv, row['bundle'] is not None)
        self.assertIn('--svg-foundation', non_svg_argv('wiz', 'root'))

    def provenance(self, template='fable-game', lifecycle='typed-sdd', product='WzFablePlayerDefault', version='2.4.0', **extra):
        params = dict(lifecycle=lifecycle, productName=product, **extra)
        return {'providerName': template, 'outcome': 'providerSucceeded', 'generator': {'version': version},
                'effectiveParameters': [{'key': k, 'value': v} for k, v in params.items()]}

    def test_omitted_fable_lifecycle_must_resolve_to_typed_and_rendering_to_sdd(self):
        player_default = ROWS[3]
        self.assertEqual(check_provenance(self.provenance(bundle='player'), player_default)['lifecycle'], 'typed-sdd')
        self.refuses('wizard-effective-lifecycle', check_provenance, self.provenance(lifecycle='sdd', bundle='player'), player_default)
        self.refuses('wizard-effective-bundle', check_provenance, self.provenance(bundle='complete'), player_default)
        rendering = ROWS[0]
        ok = self.provenance('rendering', 'sdd', rendering['product'], profile='app')
        self.assertEqual(check_provenance(ok, rendering)['lifecycle'], 'sdd')
        self.refuses('wizard-effective-lifecycle', check_provenance, self.provenance('rendering', 'typed-sdd', rendering['product'], profile='app'), rendering)
        self.refuses('wizard-effective-profile', check_provenance, self.provenance('rendering', 'sdd', rendering['product'], profile='game'), rendering)
        self.refuses('wizard-provenance', check_provenance, self.provenance(version='2.3.0', bundle='player'), player_default)

    def test_fable_tool_pins_are_exact(self):
        root = self.tmp(); (root / '.config').mkdir()
        def write(tools):
            (root / '.config/dotnet-tools.json').write_text(json.dumps({'version': 1, 'isRoot': True, 'tools': {k: {'version': v, 'commands': [k]} for k, v in tools.items()}}))
        write(FABLE_TOOLS); self.assertEqual(check_tools(root, ROWS[2]), FABLE_TOOLS)
        write(dict(FABLE_TOOLS, **{'fs.gg.sdd.cli': '2.3.0'})); self.refuses('fable-tool-pins', check_tools, root, ROWS[2])
        write({k: v for k, v in FABLE_TOOLS.items() if k != 'fantomas'}); self.refuses('fable-tool-pins', check_tools, root, ROWS[2])

    def test_non_svg_wizard_route_must_refuse_without_a_target(self):
        root = self.tmp()
        refused = types.SimpleNamespace(returncode=2, stdout='', stderr='error: unknown argument: --svg-foundation\n')
        self.assertEqual(check_non_svg_refusal(refused, root / 'absent')['exitCode'], 2)
        self.refuses('non-svg-wizard-route-not-refused', check_non_svg_refusal, types.SimpleNamespace(returncode=0, stdout='', stderr=refused.stderr), root / 'absent')
        self.refuses('non-svg-wizard-route-not-refused', check_non_svg_refusal, types.SimpleNamespace(returncode=2, stdout='', stderr='error: missing product name'), root / 'absent')
        (root / 'made').mkdir()
        self.refuses('non-svg-wizard-route-not-refused', check_non_svg_refusal, refused, root / 'made')

    def test_descriptor_source_must_match_the_template_family(self):
        root = self.tmp(); (root / '.fsgg').mkdir()
        (root / '.fsgg/providers.yml').write_text('    source: FS.GG.Workspace.Template::0.19.3\n')
        self.refuses('wizard-descriptor-sha256', check_descriptor_source, root, ROWS[2])  # right source, other bytes
        self.refuses('wizard-descriptor-source', check_descriptor_source, root, ROWS[0])
        with unittest.mock.patch.dict(DESCRIPTOR_SHA, {'fable-game': sha(root / '.fsgg/providers.yml')}):
            self.assertEqual(check_descriptor_source(root, ROWS[2])['source'], 'FS.GG.Workspace.Template::0.19.3')
        (root / '.fsgg/providers.yml').write_text('    source: FS.GG.Workspace.Template::0.19.2\n')
        self.refuses('wizard-descriptor-source', check_descriptor_source, root, ROWS[2])

    def test_installed_readback_is_literal(self):
        listed = {'fs.gg.sdd.cli': '2.4.0', 'fs.gg.newsddworkspace': '0.22.0'}
        self.assertEqual(check_versions('2.4.0', listed)['installed'], '2.4.0')
        self.refuses('installed-sdd-not-2.4.0', check_versions, '2.3.0', listed)
        self.refuses('tool-list-versions', check_versions, '2.4.0', dict(listed, **{'fs.gg.newsddworkspace': '0.21.0'}))
        table = 'Package Id  Version  Commands\n-----\nfs.gg.sdd.cli  2.4.0  fsgg-sdd\nfs.gg.newsddworkspace  0.22.0  new-sdd-workspace\n'
        self.assertEqual(parse_tool_list(table), listed)

    def test_inputs_are_sealed_and_a_run_is_never_replayed(self):
        self.assertEqual(sha(Path(__file__).with_name('knowledge-rendering-routes-public.py')), RENDERING_ROUTES_SHA)
        self.assertRegex(WIZARD[2], '^[0-9a-f]{64}$')
        root = self.tmp()
        self.refuses('fresh-output-no-replay', qualify, root, root, root)


if __name__ == '__main__':
    sys.exit(main())
