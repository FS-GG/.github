#!/usr/bin/env python3
"""Hosted public qualification of the external Rendering raw and provider knowledge routes (FS-GG/.github#4822).

Eight rows on the current published tuple: SDD.Cli 2.4.0, FS.GG.UI.Template 0.32.3 (the immutable Rendering
descriptor at Templates 24f3c307) and the Workspace.Template 0.19.3 `fs-gg-project-knowledge` overlay.

- provider  x {app, game} x {typed-sdd, omitted}: `fsgg-sdd scaffold --provider rendering`.
- raw       x {app, game} x {typed-sdd, omitted}: `dotnet new fs-gg-ui` from the original UI.Template archive.

Typed rows compose the overlay without --force (the Rendering payload carries no knowledge CI). Raw typed
rows also need the caller's local SDD tool pin and an explicit `knowledge initialize`, because the raw template
creates no SDD workspace. Each typed row then passes the normal foreground initial commit, the maintained
#4723 typed probe, an ALL-shared export after private capture and the ordinary pinned checker at
10,485,760 / 10,485,761 bytes, preserving every product byte. Omitted rows must stay ordinary `sdd` with no
canonical knowledge and no knowledge CI; the overlay without a typed lifecycle emits nothing. A typed
workspace with an owner-authored knowledge workflow must refuse the overlay unchanged.

Public nuget.org feeds only, an isolated HOME, no token in any child. Helpers come from the accepted R3
adapter's sealed bytes. `--source-controls` runs pure causal controls and launches no child.
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
import zipfile

R3_SHA = '3269ebd7a8dde638184973f0bee5c3e93db66fcd58de80f639d0f937fcaa160f'  # accepted R3 adapter (#4822, 65a71ad2)
RENDERING_DESCRIPTOR = ('FS.GG.UI.Template::0.32.3',)  # the immutable Rendering descriptor's source at Templates 24f3c307
UI = ('FS.GG.UI.Template', '0.32.3', '747f33eb0242783f67b2462d8ca137b14f36bb4133bd0a3d4ca98ca8ad93dfe6')
OVERLAY_ID = 'fs-gg-project-knowledge'
ROWS = tuple(dict(id=f'{route}-{profile}-{"typed" if typed else "default"}', route=route, profile=profile, typed=typed,
                  product=f'Rk{route.title()}{profile.title()}{"Typed" if typed else "Default"}')
             for route in ('provider', 'raw') for profile in ('app', 'game') for typed in (True, False))


def load_r3():
    path = Path(__file__).with_name('knowledge-retained-upgrade-public.py').resolve()
    if hashlib.sha256(path.read_bytes()).hexdigest() != R3_SHA:
        raise ValueError('accepted-r3-adapter-drift')
    module = types.ModuleType('sealed_r3_helpers')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


R = load_r3()
C = R.C
require, sha, snapshot, changed_paths = R.require, R.sha, R.snapshot, R.changed_paths
LIMIT, CI_FILES = R.LIMIT, R.CI_FILES
CURRENT_REVISION = R.CURRENT_REVISION
DESCRIPTOR_SHA = 'ff49732aa5baa4e1b507784c3135ea092b0973c4c4cb3a75197dae0a7a417fc2'  # providers/rendering.providers.yml at Templates 24f3c307
KNOWLEDGE_CANONICAL = ('.fsgg/knowledge-guide.md', '.fsgg/knowledge/schema.json', '.fsgg/knowledge/records/project-knowledge.json')


def check_rendering_descriptor(path):
    lines = [line.split('#', 1)[0].strip() for line in Path(path).read_text().splitlines()]
    require(sha(path) == DESCRIPTOR_SHA and lines.count('source: ' + RENDERING_DESCRIPTOR[0]) == 1
            and lines.count('templateId: fs-gg-ui') == 1, 'rendering-descriptor')
    return dict(revision=CURRENT_REVISION, source=RENDERING_DESCRIPTOR[0], sha256=DESCRIPTOR_SHA)


def ui_package(home):
    meta = json.loads((Path(home) / '.templateengine/packages.json').read_text(encoding='utf-8-sig'))
    rows = [r for r in meta['Packages'] if r.get('Details', {}).get('PackageId', '').lower() == UI[0].lower()]
    require(len(rows) == 1 and rows[0]['Details']['Version'] == UI[1]
            and rows[0]['Details']['NuGetSource'] == 'https://api.nuget.org/v3/index.json', 'sole-ui-template-registration')
    path = Path(rows[0]['MountPointUri'])
    require(path.is_file() and path.resolve().is_relative_to(Path(home).resolve()) and sha(path) == UI[2], 'ui-template-sha256')
    return path


def packed_defaults(archive):
    """The original template's own declared defaults, read from the packed definition."""
    with zipfile.ZipFile(archive) as z:
        symbols = json.loads(z.read('content/.template.config/template.json'))['symbols']
    return dict(lifecycle=symbols['lifecycle']['defaultValue'], profile=symbols['profile']['defaultValue'])


def provider_argv(sdd, root, row):
    argv = [sdd, 'scaffold', '--root', root, '--provider', 'rendering', '--no-update', '--json',
            '--param', 'productName=' + row['product'], '--param', 'profile=' + row['profile']]
    return argv + (['--param', 'lifecycle=typed-sdd'] if row['typed'] else [])


def raw_argv(root, row):
    argv = ['dotnet', 'new', 'fs-gg-ui', '--profile', row['profile'], '--name', row['product'], '--output', root]
    return argv + (['--lifecycle', 'typed-sdd'] if row['typed'] else [])


def overlay_argv(typed):
    return ['dotnet', 'new', OVERLAY_ID] + (['--lifecycle', 'typed-sdd'] if typed else []) + ['--output', '.']


def check_provenance(provenance, row):
    effective = {p['key']: p['value'] for p in provenance['effectiveParameters']}
    require(provenance['providerName'] == 'rendering' and provenance['outcome'] == 'providerSucceeded'
            and provenance['generator']['version'] == R.PACKAGES['producer'][1], 'provider-provenance')
    require(effective.get('lifecycle') == ('typed-sdd' if row['typed'] else 'sdd') and effective.get('profile') == row['profile']
            and effective.get('productName') == row['product'], 'provider-effective-parameters')
    return effective


def check_knowledge_state(root, *, store, guide, ci):
    """Exact presence of the canonical store, capture guide and knowledge CI in a created product."""
    root = Path(root)
    observed = dict(store=(root / '.fsgg/knowledge/schema.json').is_file(), guide=(root / '.fsgg/knowledge-guide.md').is_file(),
                    ci=all((root / p).is_file() for p in CI_FILES))
    partial_ci = any((root / p).exists() for p in CI_FILES) and not observed['ci']
    require(not partial_ci and observed == dict(store=store, guide=guide, ci=ci), 'knowledge-state:' + json.dumps(observed, sort_keys=True))
    return observed


def check_effect(before, after, created, payload=None):
    """Exactly `created` paths appear (with package bytes when given); no existing byte or mode changes."""
    require(changed_paths(before, after) == sorted(created) and all(p not in before for p in created), 'unexpected-effect')
    for path, data in (payload or {}).items():
        require(after[path][0] == hashlib.sha256(data).hexdigest(), 'payload-mismatch:' + path)
    return sorted(created)


def check_raw_manifest(value):
    require(value.get('isRoot') is True and set(value['tools']) == {'fs.gg.sdd.cli'}
            and value['tools']['fs.gg.sdd.cli']['version'] == R.PACKAGES['producer'][1]
            and 'fsgg-sdd' in value['tools']['fs.gg.sdd.cli']['commands'], 'raw-local-tool-manifest')
    return {k: v['version'] for k, v in value['tools'].items()}


def check_preserved(created, final, gitignore_created, gitignore_final, raw):
    """Every byte and mode of the created product survives knowledge composition and use, except the
    declared knowledge paths; an existing .gitignore keeps its prefix."""
    allowed = set(CI_FILES) | set(KNOWLEDGE_CANONICAL) | {'.gitignore'} | ({'.config/dotnet-tools.json'} if raw else set())
    changes = changed_paths(created, final)
    other = [p for p in changes if p not in allowed
             and not (p.startswith('.fsgg/knowledge/records/') and p.endswith('.json') and p not in created)
             and not (p.startswith('.fsgg/cache/') and p not in created)]
    require(not other, 'product-bytes-changed:' + ','.join(sorted(other)))
    require(all(p in final for p in created), 'product-file-removed')
    require(gitignore_created is None or gitignore_final.startswith(gitignore_created), 'gitignore-prefix-lost')
    return dict(changedPaths=changes, preservedFiles=len([p for p in created if p not in changes]))


def initial_commit(runner, root):
    require(not runner.git(root, 'rev-list', '--all'), 'unexpected-background-commit')
    cache = Path(root) / '.fsgg/cache/rendering.json'
    cache.parent.mkdir(parents=True, exist_ok=True)
    cache.write_text('{}')
    head = R.commit(runner, root, 'Initial Rendering product with project knowledge')
    tracked = set(runner.git(root, 'ls-files').splitlines())
    required = set(KNOWLEDGE_CANONICAL) | set(CI_FILES) | {'.config/dotnet-tools.json'}
    require(required <= tracked and '.fsgg/cache/rendering.json' not in tracked, 'initial-commit-inclusion')
    blobs = {}
    for relative in sorted(required):
        blob = runner.run(['git', '-C', root, 'show', head + ':' + relative], text=False, check=True).stdout
        require(blob == (Path(root) / relative).read_bytes(), 'initial-commit-blob')
        blobs[relative] = hashlib.sha256(blob).hexdigest()
    return dict(commit=head, committed=blobs)


def typed_knowledge(runner, sdd, root, templates, scratch):
    runner.call(['dotnet', 'tool', 'restore', '--no-cache'], cwd=root)
    before = R.checker(runner, root, R.PACKAGES['producer'][1])
    with C.frozen_probes(templates, runner) as (candidate, public):
        probe = candidate.probe([sdd], root, scratch)
        shared = C.post_private_export(runner, sdd, root, scratch)
        stable = snapshot(root)
        boundary = public.ordinary_ci_limits(root)
    require(snapshot(root) == stable, 'checker-boundary-residue')
    require(probe['findings'] == 4 and probe['historyVersions'] == 2 and probe['restoreAuthoredNoClobber'] is True
            and probe['explicitPrivateStoreExcludedFromShared'] is True and probe['size']['Limit'] == LIMIT, 'typed-probe')
    require(boundary['exactLimit']['bytes'] == LIMIT and boundary['oneByteOverExit'] != 0, 'checker-boundary')
    after = R.checker(runner, root, R.PACKAGES['producer'][1])
    return dict(checkerBefore=before, probe=probe, allShared=shared, checkerBoundary=boundary, checkerAfter=after)


def run_row(runner, row, case, sdd, home, templates, current, overlay_payload):
    case.mkdir()
    root = case / 'subject'
    entry = dict(id=row['id'], state='started')
    if row['route'] == 'provider':
        (root / '.fsgg').mkdir(parents=True)
        shutil.copyfile(current / 'providers/rendering.providers.yml', root / '.fsgg/providers.yml')
        entry['descriptor'] = check_rendering_descriptor(root / '.fsgg/providers.yml')
        created = json.loads(runner.call(provider_argv(sdd, root, row), cwd=case, timeout=180).stdout)
        require(created['outcome'] == 'succeeded', 'provider-scaffold')
        entry['effective'] = check_provenance(C.read_json(root / '.fsgg/scaffold-provenance.json'), row)
        entry['tools'] = {k: v['version'] for k, v in R.tool_pins(root)['tools'].items()}
        require(entry['tools']['fs.gg.sdd.cli'] == R.PACKAGES['producer'][1], 'provider-sdd-pin')
        entry['created'] = check_knowledge_state(root, store=row['typed'], guide=True, ci=False)
    else:
        runner.call(raw_argv(root, row), cwd=case, timeout=180)
        require(not (root / '.fsgg').exists() and not (root / '.config').exists(), 'raw-template-created-sdd-workspace')
        entry['created'] = check_knowledge_state(root, store=False, guide=False, ci=False)
    entry['uiTemplate'] = dict(version=UI[1], sha256=sha(ui_package(home)))
    created_tree = snapshot(root)
    gitignore = (root / '.gitignore').read_bytes() if (root / '.gitignore').is_file() else None
    if not row['typed']:
        before = snapshot(root)
        runner.call(overlay_argv(False), cwd=root)
        entry['overlayWithoutTypedLifecycle'] = check_effect(before, snapshot(root), [])
        entry['final'] = check_knowledge_state(root, store=False, guide=row['route'] == 'provider', ci=False)
        entry['state'] = 'passed'
        return entry
    if row['id'] == 'provider-app-typed':
        collision = case / 'collision'
        shutil.copytree(root, collision, symlinks=True)
        (collision / CI_FILES[0]).parent.mkdir(parents=True, exist_ok=True)
        (collision / CI_FILES[0]).write_text('name: owner-authored-knowledge\non: workflow_dispatch\njobs: {}\n')
        before = snapshot(collision)
        entry['overlayCollision'] = R.check_overlay_refusal(runner.call(overlay_argv(True), cwd=collision, success=False), before, snapshot(collision))
    before = snapshot(root)
    runner.call(overlay_argv(True), cwd=root)
    entry['overlay'] = check_effect(before, snapshot(root), list(CI_FILES), overlay_payload)
    if row['route'] == 'raw':
        runner.call(['git', 'init', '-q'], cwd=root)
        before = snapshot(root)
        runner.call(['dotnet', 'new', 'tool-manifest', '--output', root / '.config'], cwd=root)
        runner.call(['dotnet', 'tool', 'install', '--local', R.PACKAGES['producer'][0], '--version', R.PACKAGES['producer'][1]], cwd=root)
        entry['localTool'] = check_raw_manifest(C.read_json(root / '.config/dotnet-tools.json'))
        check_effect(before, snapshot(root), ['.config/dotnet-tools.json'])
        before = snapshot(root)
        runner.call(checker_argv(root), cwd=root, success=False)
        require(snapshot(root) == before, 'checker-pre-init-changed-bytes')
        initialized = json.loads(runner.call([sdd, 'knowledge', 'initialize', '--root', root], cwd=root).stdout)
        require(initialized['Limit'] == LIMIT, 'initialize-size')
        once = snapshot(root)
        runner.call([sdd, 'knowledge', 'initialize', '--root', root], cwd=root)
        require(snapshot(root) == once, 'initialize-not-idempotent')
        entry['initialized'] = initialized
    entry['beforeCommit'] = check_knowledge_state(root, store=True, guide=True, ci=True)
    entry['initialCommit'] = initial_commit(runner, root)
    entry['knowledge'] = typed_knowledge(runner, sdd, root, templates, case / 'probe')
    gitignore_final = (root / '.gitignore').read_bytes()
    entry['preservation'] = check_preserved(created_tree, snapshot(root), gitignore, gitignore_final, row['route'] == 'raw')
    entry['state'] = 'passed'
    return entry


def checker_argv(root):
    return [sys.executable, str(Path(root) / CI_FILES[1]), '--root', str(root)]


def qualify(templates, current, work):
    work = Path(work)
    require(work.is_absolute() and not work.exists(), 'fresh-output-no-replay')
    templates, current = R.verify_sources(templates, current)
    check_rendering_descriptor(current / 'providers/rendering.providers.yml')
    dotnet = shutil.which('dotnet')
    require(dotnet and shutil.which('git'), 'missing-host-tools')
    work.mkdir(mode=0o700)
    evidence = work / 'evidence'
    evidence.mkdir()
    home, env = R.environment(work, dotnet)
    runner = C.Runner(evidence, env, total_seconds=2700)
    report = dict(schema='fsgg.wizard.rendering-routes-public-acceptance/1', state='started', readOnly=True,
                  host=dict(system=platform.platform(), python=platform.python_version()), rows=[],
                  expected=dict(ui=list(UI), **{k: list(R.PACKAGES[k]) for k in ('producer', 'template', 'coord')}),
                  descriptor=[CURRENT_REVISION, RENDERING_DESCRIPTOR[0], DESCRIPTOR_SHA])

    def save():
        (work / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    save()
    try:
        report['host']['dotnet'] = runner.call(['dotnet', '--version']).stdout.strip()
        runner.call(['dotnet', 'tool', 'install', '--global', R.PACKAGES['producer'][0], '--version', R.PACKAGES['producer'][1]])
        sdd = shutil.which('fsgg-sdd', path=env['PATH'])
        require(sdd and Path(sdd).resolve().is_relative_to(home.resolve()), 'selected-tool-not-isolated')
        require(runner.call([sdd, '--version']).stdout.strip() == R.PACKAGES['producer'][1], 'producer-version')
        report['packages'] = {'producer': R.identity(home, 'producer', R.tool_package(home, 'producer'))}
        runner.call(['dotnet', 'new', 'install', R.DESCRIPTORS['current'][1]], cwd=work)
        runner.call(['dotnet', 'new', 'install', RENDERING_DESCRIPTOR[0]], cwd=work)
        report['packages']['template'] = R.identity(home, 'template', R.template_package(home, 'template'))
        report['packages']['ui'] = dict(package=UI[0], version=UI[1], sha256=sha(ui_package(home)), match=True)
        report['packedDefaults'] = packed_defaults(ui_package(home))
        require(report['packedDefaults']['lifecycle'] == 'sdd', 'raw-lifecycle-default')
        with zipfile.ZipFile(R.template_package(home, 'template')) as archive:
            overlay_payload = {n: archive.read(f'content/templates/{OVERLAY_ID}/' + n) for n in CI_FILES}
        for row in ROWS:
            report['rows'].append(run_row(runner, row, work / row['id'], sdd, home, templates, current, overlay_payload)); save()
        report['packages']['restored-producer'] = R.identity(home, 'producer', R.restored_package(home, 'producer'))
        report['packages']['coord'] = R.identity(home, 'coord', R.restored_package(home, 'coord'))
        require(ui_package(home) and R.template_package(home, 'template'), 'final-registrations')
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
    parser.add_argument('--current', type=Path, help=f'Templates checkout at {CURRENT_REVISION}')
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

    def test_eight_rows_cover_both_routes_profiles_and_lifecycles(self):
        self.assertEqual(len(ROWS), 8)
        self.assertEqual({(r['route'], r['profile'], r['typed']) for r in ROWS},
                         {(a, b, c) for a in ('provider', 'raw') for b in ('app', 'game') for c in (True, False)})
        self.assertEqual(len({r['product'] for r in ROWS}), 8)

    def test_argv_selects_route_profile_and_lifecycle_explicitly(self):
        typed, default = ROWS[0], ROWS[1]
        self.assertEqual(provider_argv('sdd', 'r', typed)[-2:], ['--param', 'lifecycle=typed-sdd'])
        self.assertNotIn('lifecycle=typed-sdd', ' '.join(provider_argv('sdd', 'r', default)))
        self.assertIn('profile=app', provider_argv('sdd', 'r', typed))
        raw = [r for r in ROWS if r['route'] == 'raw']
        self.assertEqual(raw_argv('r', raw[0])[-2:], ['--lifecycle', 'typed-sdd'])
        self.assertNotIn('--lifecycle', raw_argv('r', raw[1]))
        self.assertNotIn('--force', overlay_argv(True) + overlay_argv(False))
        self.assertNotIn('--lifecycle', overlay_argv(False))

    def test_provenance_must_record_route_profile_and_effective_lifecycle(self):
        def value(lifecycle, profile='app', version='2.4.0', provider='rendering'):
            return {'providerName': provider, 'outcome': 'providerSucceeded', 'generator': {'version': version},
                    'effectiveParameters': [{'key': 'lifecycle', 'value': lifecycle}, {'key': 'profile', 'value': profile},
                                            {'key': 'productName', 'value': ROWS[0]['product']}]}
        self.assertEqual(check_provenance(value('typed-sdd'), ROWS[0])['lifecycle'], 'typed-sdd')
        default = dict(ROWS[1], product=ROWS[0]['product'])
        self.assertEqual(check_provenance(value('sdd'), default)['lifecycle'], 'sdd')
        self.refuses('provider-effective-parameters', check_provenance, value('sdd'), ROWS[0])
        self.refuses('provider-effective-parameters', check_provenance, value('typed-sdd', profile='game'), ROWS[0])
        self.refuses('provider-provenance', check_provenance, value('typed-sdd', version='2.3.0'), ROWS[0])
        self.refuses('provider-provenance', check_provenance, value('typed-sdd', provider='console'), ROWS[0])

    def test_knowledge_state_is_exact(self):
        root = self.tmp()
        self.assertEqual(check_knowledge_state(root, store=False, guide=False, ci=False), dict(store=False, guide=False, ci=False))
        (root / '.fsgg/knowledge').mkdir(parents=True); (root / '.fsgg/knowledge/schema.json').write_text('{}')
        self.refuses('knowledge-state:{"ci": false, "guide": false, "store": true}', check_knowledge_state, root, store=False, guide=False, ci=False)
        (root / 'scripts').mkdir(); (root / CI_FILES[1]).write_text('x')
        self.refuses('knowledge-state:{"ci": false, "guide": false, "store": true}', check_knowledge_state, root, store=True, guide=False, ci=False)

    def test_effects_are_exact_and_payload_bound(self):
        payload = {CI_FILES[0]: b'w', CI_FILES[1]: b'c'}
        before = {'a': ['x', 0o644]}
        after = dict(before, **{n: [hashlib.sha256(d).hexdigest(), 0o644] for n, d in payload.items()})
        self.assertEqual(check_effect(before, after, list(CI_FILES), payload), sorted(CI_FILES))
        self.assertEqual(check_effect(before, before, []), [])
        self.refuses('unexpected-effect', check_effect, before, after, [])
        self.refuses('unexpected-effect', check_effect, before, dict(after, a=['y', 0o644]), list(CI_FILES), payload)
        self.refuses('payload-mismatch:' + CI_FILES[0], check_effect, before, after, list(CI_FILES), dict(payload, **{CI_FILES[0]: b'x'}))

    def test_raw_manifest_pins_exactly_the_published_producer(self):
        ok = {'isRoot': True, 'tools': {'fs.gg.sdd.cli': {'version': '2.4.0', 'commands': ['fsgg-sdd']}}}
        self.assertEqual(check_raw_manifest(ok), {'fs.gg.sdd.cli': '2.4.0'})
        self.refuses('raw-local-tool-manifest', check_raw_manifest, {'isRoot': True, 'tools': {'fs.gg.sdd.cli': {'version': '2.3.0', 'commands': ['fsgg-sdd']}}})
        self.refuses('raw-local-tool-manifest', check_raw_manifest, dict(ok, tools=dict(ok['tools'], extra={'version': '1', 'commands': []})))

    def test_preservation_admits_only_knowledge_paths(self):
        created = {'src/A.fs': ['a', 0o644], '.gitignore': ['g', 0o644], 'README.md': ['r', 0o644]}
        final = dict(created, **{'.gitignore': ['g2', 0o644], CI_FILES[0]: ['w', 0o644], KNOWLEDGE_CANONICAL[1]: ['s', 0o644],
                                 '.fsgg/knowledge/records/bug-cause-fix.json': ['b', 0o644], '.fsgg/cache/rendering.json': ['z', 0o644]})
        self.assertEqual(check_preserved(created, final, b'g\n', b'g\nblock\n', False)['preservedFiles'], 2)
        self.refuses('product-bytes-changed:src/A.fs', check_preserved, created, dict(final, **{'src/A.fs': ['a', 0o755]}), b'g', b'g', False)
        self.refuses('product-bytes-changed:.config/dotnet-tools.json', check_preserved, created,
                     dict(final, **{'.config/dotnet-tools.json': ['m', 0o644]}), b'g', b'g', False)
        self.assertIn('.config/dotnet-tools.json', check_preserved(created, dict(final, **{'.config/dotnet-tools.json': ['m', 0o644]}), b'g', b'g', True)['changedPaths'])
        gone = dict(final); del gone['README.md']
        self.refuses('product-bytes-changed:README.md', check_preserved, created, gone, b'g', b'g', False)
        self.refuses('gitignore-prefix-lost', check_preserved, created, final, b'owner\n', b'replaced\n', False)

    def test_inputs_are_sealed_and_a_run_is_never_replayed(self):
        self.assertEqual(sha(Path(__file__).with_name('knowledge-retained-upgrade-public.py')), R3_SHA)
        self.assertRegex(UI[2], '^[0-9a-f]{64}$')
        self.assertRegex(DESCRIPTOR_SHA, '^[0-9a-f]{64}$')
        root = self.tmp(); (root / 'providers').mkdir(); (root / 'providers/rendering.providers.yml').write_text('source: FS.GG.UI.Template::0.32.3\n')
        self.refuses('rendering-descriptor', check_rendering_descriptor, root / 'providers/rendering.providers.yml')
        self.refuses('fresh-output-no-replay', qualify, root, root, root)


if __name__ == '__main__':
    sys.exit(main())
