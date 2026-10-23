#!/usr/bin/env python3
"""Hosted public qualification of the two Wizard fable-bindings creation rows (FS-GG/.github#4642).

The 24-subject matrix adapter (knowledge-public-receivers.py) holds both `fable-bindings-wizard-*`
subjects as deferred supplier gaps; its sealed local manifest, pins and counts are not changed here.
This adapter is the separate, read-only runtime for those two rows on the genuinely published cohort:
Wizard 0.21.0, SDD.Cli 2.4.0 and Workspace.Template 0.19.3, from public feeds, with an isolated HOME
and the Wizard UNPINNED (no --pinned, so its own self-update path is exercised and read back).

`--source-controls` runs pure causal controls and launches no child. `--run` is the hosted operation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import stat
import sys
import tempfile
import types
import unittest
import zipfile

CONSOLE_SHA = 'afc9de40013bc71d430c91bb215a4938ef4ca9bd5e97e389ba53af3007204873'
TEMPLATES_REVISION = 'f5fbd06ed9e6df40f9037c7acddf9eca3c6a14fc'  # frozen receiver probes, hash-pinned in the console helper
# Literal published nupkg bytes (SHA-256). Version text alone establishes none of these identities.
PACKAGES = {
    'wizard': ('FS.GG.NewSddWorkspace', '0.21.0', '4326f57dc41ab3feaf6b7b693dcdc2867e818b0567af27da505b571aace4e190'),
    'producer': ('FS.GG.SDD.Cli', '2.4.0', '1df2e74faebd388f90b813ed9d7497f4247f977718469c9247b37111d791921f'),
    'predecessor': ('FS.GG.SDD.Cli', '2.3.0', '5af05931d7ca0f158d2bfc562047261a38ca098ca6dc3635750eadcc4ed1c3ca'),
    'template': ('FS.GG.Workspace.Template', '0.19.3', '9594ec204a26f238a06e016d1cfe1e796a07fda23c16589eceb19366cd4a72a2'),
}
TEMPLATE_SOURCE = 'FS.GG.Workspace.Template::0.19.3'
EXPLICIT = {'npmPackage': '@babylonjs/core', 'npmVersion': '9.19.0', 'bindingTarget': 'browser'}
TOOLS = {'fable': '5.18.0', 'fantomas': '8.0.5', 'fs.gg.coord.cli': '0.94.0', 'fs.gg.sdd.cli': '2.4.0'}
CI_FILES = ('.github/workflows/project-knowledge.yml', 'scripts/check-project-knowledge.py')
TEMPLATE_PREFIX = 'content/templates/fs-gg-fable-bindings/'
LIMIT = 10485760
ROWS = (
    {'id': 'wizard-typed', 'product': 'KnowledgeBindingsWizardTyped', 'lifecycle': 'typed-sdd'},
    {'id': 'wizard-default', 'product': 'KnowledgeBindingsWizardDefault', 'lifecycle': None},
)
PROBE_FILES = ('candidate.py', 'receiver.py', 'public21.py', 'findings.json')


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for part in iter(lambda: f.read(1024 * 1024), b''):
            h.update(part)
    return h.hexdigest()


def require(ok, message):
    if not ok:
        raise ValueError(message)


def load_console():
    path = Path(__file__).with_name('knowledge-console-public.py').resolve()
    require(sha(path) == CONSOLE_SHA, 'delivered-console-helper-drift')
    module = types.ModuleType('sealed_console_ports')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


C = load_console()


def wizard_argv(wizard, root, row):
    """Unpinned: no --pinned and no --ref, so descriptor `main` and the SDD self-update both apply."""
    require(row['id'] in {r['id'] for r in ROWS}, 'unknown-row')
    argv = [wizard, root, row['product'], '--template', 'fable-bindings']
    if row['lifecycle'] is not None:
        argv += ['--lifecycle', row['lifecycle']]
    return argv + ['--npm-package', EXPLICIT['npmPackage'], '--npm-version', EXPLICIT['npmVersion'],
                   '--binding-target', EXPLICIT['bindingTarget'], '--no-governance', '--no-coordination']


def snapshot(root):
    result = {}
    for p in sorted(Path(root).rglob('*')):
        parts = p.relative_to(root).parts
        if '.git' in parts:
            continue
        info = p.lstat()
        require(not stat.S_ISLNK(info.st_mode), 'subject-symlink')
        if p.is_file():
            result['/'.join(parts)] = sha(p)
    return result


def check_literals(root, product, lifecycle):
    """The literal values must survive Wizard -> SDD -> dotnet new byte for byte under the routed keys."""
    provenance = C.read_json(Path(root) / '.fsgg/scaffold-provenance.json')
    require(provenance['providerName'] == 'fable-bindings' and provenance['outcome'] == 'providerSucceeded'
            and provenance['generator']['version'] == PACKAGES['producer'][1], 'provenance-provider-or-producer')
    effective = {p['key']: p['value'] for p in provenance['effectiveParameters']}
    require(effective == dict(EXPLICIT, lifecycle=lifecycle, productName=product, rootNamespace=product),
            'effective-parameters-not-literal')
    package = C.read_json(Path(root) / 'package.json')
    deps = {**package.get('dependencies', {}), **package.get('devDependencies', {})}
    require(deps.get(EXPLICIT['npmPackage']) == EXPLICIT['npmVersion'], 'package-json-literal')
    return dict(effectiveParameters=effective, packageJsonDependency={EXPLICIT['npmPackage']: deps[EXPLICIT['npmPackage']]})


def check_tools(root):
    value = C.read_json(Path(root) / '.config/dotnet-tools.json')
    require(value['version'] == 1 and value['isRoot'] is True and set(value['tools']) == set(TOOLS), 'tool-set')
    require({k: v['version'] for k, v in value['tools'].items()} == TOOLS, 'tool-pins')
    require('fsgg-sdd' in value['tools']['fs.gg.sdd.cli']['commands'], 'sdd-command')
    return {k: v['version'] for k, v in value['tools'].items()}


def check_descriptor(root):
    text = (Path(root) / '.fsgg/providers.yml').read_text()
    require(any(line.strip() == 'source: ' + TEMPLATE_SOURCE for line in text.splitlines()), 'descriptor-template-source')
    return dict(path='.fsgg/providers.yml', sha256=sha(Path(root) / '.fsgg/providers.yml'), source=TEMPLATE_SOURCE)


def check_typed_initial(root, assets):
    root = Path(root)
    canonical = sorted(p.relative_to(root).as_posix() for p in (root / '.fsgg/knowledge').rglob('*') if p.is_file())
    require('.fsgg/knowledge/schema.json' in canonical and any('/records/' in p for p in canonical), 'canonical-knowledge-missing')
    require((root / '.fsgg/knowledge-guide.md').stat().st_size > 0, 'knowledge-guide-missing')
    for name in CI_FILES:
        require((root / name).read_bytes() == assets[name], 'owner-ci-payload')
    return dict(canonicalFiles=canonical, ownerCi={n: hashlib.sha256(assets[n]).hexdigest() for n in CI_FILES})


def check_default(root):
    root = Path(root)
    require(not (root / '.fsgg/knowledge').exists(), 'default-acquired-canonical-knowledge')
    require(all(not (root / name).exists() for name in CI_FILES), 'default-acquired-owner-ci')
    return dict(canonicalKnowledge=False, ownerCi=False)


def check_refusal(report, root, product):
    """A wrong provider key must refuse at the provider edge and create no provider-owned product."""
    s = report['scaffold']
    invocation = s['providerInvocation']
    require(report['outcome'] == 'blocked' and s['outcome'] == 'providerFailed' and s['producedPathCount'] == 0
            and invocation['exitCode'] != 0 and '--target' in invocation['standardError'], 'wrong-key-not-refused')
    provenance = C.read_json(Path(root) / '.fsgg/scaffold-provenance.json')
    require(provenance['outcome'] == 'providerFailed' and provenance['producedPaths'] == []
            and not (Path(root) / 'package.json').exists() and not (Path(root) / '.fsgg/knowledge').exists()
            and not list(Path(root).glob('*.slnx')), 'wrong-key-produced-product')
    return dict(outcome=report['outcome'], scaffoldOutcome=s['outcome'], providerExit=invocation['exitCode'])


def tool_package(home, key):
    pid, version, _ = PACKAGES[key]
    name = pid.lower()
    return Path(home) / '.dotnet/tools/.store' / name / version / name / version / f'{name}.{version}.nupkg'


def template_package(home):
    meta = json.loads((Path(home) / '.templateengine/packages.json').read_text(encoding='utf-8-sig'))
    rows = [r for r in meta['Packages'] if r.get('Details', {}).get('PackageId', '').lower() == PACKAGES['template'][0].lower()]
    require(len(rows) == 1 and rows[0]['Details']['Version'] == PACKAGES['template'][1]
            and rows[0]['Details']['NuGetSource'] == 'https://api.nuget.org/v3/index.json', 'sole-template-registration')
    return Path(rows[0]['MountPointUri'])


def identity(home, key):
    path = tool_package(home, key) if key != 'template' else template_package(home)
    require(path.is_file() and path.resolve().is_relative_to(Path(home).resolve()), 'package-not-in-isolated-home:' + key)
    return verify_identity(key, sha(path), path.name)


def template_assets(home):
    with zipfile.ZipFile(template_package(home)) as archive:
        return {name: archive.read(TEMPLATE_PREFIX + name) for name in CI_FILES}


def verify_identity(key, observed, name):
    pid, version, expected = PACKAGES[key]
    require(observed == expected, 'package-sha256-mismatch:' + key)
    return dict(package=pid, version=version, file=name, sha256=observed, expectedSha256=expected, match=True)


def check_versions(predecessor, installed, listed):
    require(predecessor == PACKAGES['predecessor'][1], 'predecessor-not-installed')
    require(installed == PACKAGES['producer'][1], 'installed-sdd-not-2.4.0')
    require(listed.get('fs.gg.sdd.cli') == PACKAGES['producer'][1]
            and listed.get('fs.gg.newsddworkspace') == PACKAGES['wizard'][1], 'tool-list-versions')
    return dict(before=predecessor, after=installed, toolList=listed)


def parse_tool_list(text):
    result = {}
    for line in text.splitlines()[2:]:
        parts = line.split()
        if len(parts) >= 2:
            result[parts[0].lower()] = parts[1]
    return result


def environment(work, dotnet):
    home = work / 'home'
    for relative in ('tmp', '.nuget/packages'):
        (home / relative).mkdir(parents=True)
    dotnet_dir = Path(dotnet).resolve().parent
    return home, {
        'HOME': str(home), 'DOTNET_CLI_HOME': str(home), 'TMPDIR': str(home / 'tmp'),
        'NUGET_PACKAGES': str(home / '.nuget/packages'),
        'DOTNET_ROOT': os.environ.get('DOTNET_ROOT') or str(dotnet_dir),
        'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
        'MSBUILDDISABLENODEREUSE': '1', 'TERM': 'dumb', 'NO_COLOR': '1', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8',
        'GIT_CONFIG_NOSYSTEM': '1', 'GIT_CONFIG_GLOBAL': '/dev/null',
        'PATH': os.pathsep.join([str(home / '.dotnet/tools'), str(dotnet_dir), '/usr/local/bin', '/usr/bin', '/bin']),
    }  # No GITHUB_TOKEN/GH_TOKEN/FSGG_PACKAGES_TOKEN: public anonymous feeds only.


def verify_probe_sources(templates):
    templates = Path(templates).resolve()
    for name in PROBE_FILES:
        digest = C.SOURCE_HASHES['tests/knowledge-receiver/' + name]
        require(sha(templates / 'tests/knowledge-receiver' / name) == digest, 'probe-source-drift:' + name)
    return templates


def typed_knowledge(runner, root, sdd, templates, scratch):
    runner.call(['dotnet', 'tool', 'restore', '--no-cache'], cwd=root)
    checked = lambda: json.loads(runner.call([sys.executable, str(root / CI_FILES[1]), '--root', str(root)], cwd=root).stdout)
    before = checked()
    require(before['outcome'] == 'passed' and before['pinnedVersion'] == PACKAGES['producer'][1]
            and before['sourceCandidateOverride'] is False and before['limit'] == LIMIT, 'ordinary-pinned-checker')
    with C.frozen_probes(templates, runner) as (candidate, public):
        probe = candidate.probe([sdd], root, scratch)
        budget = public.ordinary_ci_limits(root)
    require(probe['findings'] == 4 and probe['historyVersions'] == 2 and probe['restoreAuthoredNoClobber'] is True
            and probe['explicitPrivateStoreExcludedFromShared'] is True and probe['size']['Limit'] == LIMIT, 'typed-probe-result')
    require(budget['exactLimit']['bytes'] == LIMIT and budget['oneByteOverExit'] != 0, 'checker-boundary')
    after = checked()
    require(after['outcome'] == 'passed', 'checker-after-restore')
    return dict(checkerBefore=before, probe=probe, checkerBoundary=budget, checkerAfter=after)


def commit_initial(runner, root, assets):
    require(not runner.git(root, 'rev-list', '--all'), 'unexpected-background-commit')
    cache = root / '.fsgg/cache/hosted.json'
    cache.parent.mkdir(parents=True, exist_ok=True)
    cache.write_text('{}')
    runner.git(root, 'add', '.')
    runner.git(root, '-c', 'user.name=Hosted receiver', '-c', 'user.email=receiver@example.invalid', 'commit', '-qm', 'Initial generated Wizard receiver')
    head = runner.git(root, 'rev-parse', 'HEAD')
    tracked = set(runner.git(root, 'ls-files').splitlines())
    require(all(n in tracked for n in CI_FILES) and '.fsgg/knowledge/schema.json' in tracked
            and cache.relative_to(root).as_posix() not in tracked, 'normal-initial-inclusion')
    for name in CI_FILES:
        require(runner.run(['git', '-C', root, 'show', head + ':' + name], text=False, check=True).stdout == assets[name], 'caller-initial-blob')
    return head


def run_row(runner, row, case, wizard, sdd, home, templates):
    root = case / 'subject'
    case.mkdir()
    entry = dict(id=row['id'], argv=[str(a) for a in wizard_argv(wizard, root, row)], state='started')
    runner.call(wizard_argv(wizard, root, row), cwd=case, timeout=180)
    entry['literals'] = check_literals(root, row['product'], row['lifecycle'] or 'sdd')
    entry['descriptor'] = check_descriptor(root)
    entry['tools'] = check_tools(root)
    entry['template'] = identity(home, 'template')
    if row['lifecycle'] == 'typed-sdd':
        assets = template_assets(home)
        entry['initial'] = check_typed_initial(root, assets)
        entry['initialCommit'] = commit_initial(runner, root, assets)
        entry['knowledge'] = typed_knowledge(runner, root, sdd, templates, case / 'probe')
    else:
        entry['boundary'] = check_default(root)
    entry['files'] = snapshot(root)
    entry['state'] = 'passed'
    return entry


def run_control(runner, sdd, work, descriptor):
    root = work / 'control/subject'
    (root / '.fsgg').mkdir(parents=True)
    shutil.copyfile(descriptor, root / '.fsgg/providers.yml')
    argv = [sdd, 'scaffold', '--root', root, '--provider', 'fable-bindings', '--no-update', '--json']
    for key, value in [('npmPackage', EXPLICIT['npmPackage']), ('npmVersion', EXPLICIT['npmVersion']), ('target', EXPLICIT['bindingTarget']),
                       ('lifecycle', 'typed-sdd'), ('productName', 'KnowledgeBindingsWrongKey')]:
        argv += ['--param', f'{key}={value}']
    result = runner.call(argv, cwd=work / 'control', success=False)
    return dict(id='wrong-key-refusal', exitCode=result.returncode, **check_refusal(json.loads(result.stdout), root, 'KnowledgeBindingsWrongKey'))


def qualify(templates, work):
    work = Path(work)
    require(work.is_absolute() and not work.exists(), 'fresh-output-no-replay')
    templates = verify_probe_sources(templates)
    dotnet = shutil.which('dotnet')
    require(dotnet and shutil.which('git'), 'missing-host-tools')
    work.mkdir(mode=0o700)
    evidence = work / 'evidence'
    evidence.mkdir()
    home, env = environment(work, dotnet)
    runner = C.Runner(evidence, env, total_seconds=1500)
    report = dict(schema='fsgg.wizard.bindings-public-acceptance/1', state='started', readOnly=True, wizardPinned=False,
                  host=dict(system=platform.platform(), python=platform.python_version()), rows=[],
                  expected={k: list(v) for k, v in PACKAGES.items()}, templatesRevision=TEMPLATES_REVISION)

    def save():
        (work / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    save()
    try:
        report['host']['dotnet'] = runner.call(['dotnet', '--version']).stdout.strip()
        for key in ('wizard', 'predecessor'):
            runner.call(['dotnet', 'tool', 'install', '--global', PACKAGES[key][0], '--version', PACKAGES[key][1]])
        wizard = shutil.which('new-sdd-workspace', path=env['PATH'])
        sdd = shutil.which('fsgg-sdd', path=env['PATH'])
        require(wizard and sdd and Path(wizard).resolve().is_relative_to(home.resolve())
                and Path(sdd).resolve().is_relative_to(home.resolve()), 'selected-tools-not-isolated')
        predecessor = runner.call([sdd, '--version']).stdout.strip()
        report['packages'] = dict(wizard=identity(home, 'wizard'), predecessor=identity(home, 'predecessor')); save()
        for row in ROWS:
            report['rows'].append(run_row(runner, row, work / row['id'], wizard, sdd, home, templates)); save()
        report['control'] = run_control(runner, sdd, work, work / ROWS[0]['id'] / 'subject/.fsgg/providers.yml'); save()
        installed = runner.call([sdd, '--version']).stdout.strip()
        listed = parse_tool_list(runner.call(['dotnet', 'tool', 'list', '--global']).stdout)
        report['readback'] = check_versions(predecessor, installed, listed)
        report['packages'].update(producer=identity(home, 'producer'), template=identity(home, 'template'))
        report.update(state='passed', commands=runner.sequence); save()
        return report
    except BaseException as error:
        report.update(state='failed', failureType=type(error).__name__, failure=str(error), commands=runner.sequence); save()
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source-controls', action='store_true')
    parser.add_argument('--run', action='store_true')
    parser.add_argument('--templates', type=Path)
    parser.add_argument('--work', type=Path)
    args = parser.parse_args()
    if args.source_controls:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(Controls)
        return 0 if unittest.TextTestRunner(verbosity=1).run(suite).wasSuccessful() else 1
    require(args.run and args.templates and args.work, 'operation-required')
    report = qualify(args.templates, args.work)
    print(json.dumps({k: report[k] for k in ('state', 'commands')}))
    return 0


def provenance_value(lifecycle, product, changes=None):
    effective = dict(EXPLICIT, lifecycle=lifecycle, productName=product, rootNamespace=product)
    effective.update(changes or {})
    return {'generator': {'version': PACKAGES['producer'][1]}, 'providerName': 'fable-bindings', 'outcome': 'providerSucceeded',
            'effectiveParameters': [{'key': k, 'value': v} for k, v in sorted(effective.items()) if v is not None]}


class Controls(unittest.TestCase):
    """Pure causal controls: each invariant is made to fail by the defect it exists to catch."""
    assets = {CI_FILES[0]: b'workflow\n', CI_FILES[1]: b'checker\n'}

    def workspace(self, lifecycle='typed-sdd', product='Product', *, effective=None, version='2.4.0', tools=None, knowledge=None,
                  ci=True, package=EXPLICIT['npmVersion'], dependency=EXPLICIT['npmPackage']):
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR')))
        self.addCleanup(shutil.rmtree, root, True)
        (root / '.fsgg').mkdir(); (root / '.config').mkdir()
        value = provenance_value(lifecycle, product, effective)
        value['generator']['version'] = version
        (root / '.fsgg/scaffold-provenance.json').write_text(json.dumps(value))
        (root / 'package.json').write_text(json.dumps({'dependencies': {dependency: package}}))
        (root / '.config/dotnet-tools.json').write_text(json.dumps({'version': 1, 'isRoot': True, 'tools': {
            k: {'version': v, 'commands': ['fsgg-sdd'] if k == 'fs.gg.sdd.cli' else [k]} for k, v in (tools or TOOLS).items()}}))
        (root / '.fsgg/providers.yml').write_text('providers:\n  - name: fable-bindings\n    source: ' + TEMPLATE_SOURCE + '\n')
        if knowledge if knowledge is not None else lifecycle == 'typed-sdd':
            (root / '.fsgg/knowledge/records').mkdir(parents=True)
            (root / '.fsgg/knowledge/schema.json').write_text('{}')
            (root / '.fsgg/knowledge/records/project-knowledge.json').write_text('{}')
            (root / '.fsgg/knowledge-guide.md').write_text('guide\n')
        if ci:
            for name, data in self.assets.items():
                (root / name).parent.mkdir(parents=True, exist_ok=True); (root / name).write_bytes(data)
        return root

    def refuses(self, reason, function, *args):
        with self.assertRaises(ValueError) as caught:
            function(*args)
        self.assertEqual(str(caught.exception), reason)

    def test_unpinned_argv_carries_every_explicit_literal(self):
        typed = wizard_argv('wiz', 'root', ROWS[0]); default = wizard_argv('wiz', 'root', ROWS[1])
        for argv in (typed, default):
            self.assertNotIn('--pinned', argv); self.assertNotIn('--ref', argv)
            self.assertEqual(argv[argv.index('--npm-package') + 1], '@babylonjs/core')
            self.assertEqual(argv[argv.index('--npm-version') + 1], '9.19.0')
            self.assertEqual(argv[argv.index('--binding-target') + 1], 'browser')
        self.assertEqual(typed[typed.index('--lifecycle') + 1], 'typed-sdd'); self.assertNotIn('--lifecycle', default)
        self.refuses('unknown-row', wizard_argv, 'wiz', 'root', {'id': 'other'})

    def test_literals_pass_and_each_transport_defect_fails(self):
        self.assertEqual(check_literals(self.workspace(), 'Product', 'typed-sdd')['packageJsonDependency'], {'@babylonjs/core': '9.19.0'})
        self.assertEqual(check_literals(self.workspace('sdd'), 'Product', 'sdd')['effectiveParameters']['lifecycle'], 'sdd')
        for defect in (dict(npmPackage='babylonjs/core'), dict(npmVersion='9.19.0 '), dict(bindingTarget=None, target='browser'),
                       dict(lifecycle='sdd')):
            self.refuses('effective-parameters-not-literal', check_literals, self.workspace(effective=defect), 'Product', 'typed-sdd')
        self.refuses('effective-parameters-not-literal', check_literals, self.workspace('typed-sdd'), 'Product', 'sdd')
        self.refuses('provenance-provider-or-producer', check_literals, self.workspace(version='2.3.0'), 'Product', 'typed-sdd')
        self.refuses('package-json-literal', check_literals, self.workspace(package='9.18.0'), 'Product', 'typed-sdd')
        self.refuses('package-json-literal', check_literals, self.workspace(dependency='babylonjs/core'), 'Product', 'typed-sdd')

    def test_root_sdd_pin_is_exactly_the_published_producer(self):
        self.assertEqual(check_tools(self.workspace())['fs.gg.sdd.cli'], '2.4.0')
        self.refuses('tool-pins', check_tools, self.workspace(tools=dict(TOOLS, **{'fs.gg.sdd.cli': '2.3.0'})))
        self.refuses('tool-set', check_tools, self.workspace(tools=dict(TOOLS, extra='1.0.0')))

    def test_typed_row_requires_canonical_knowledge_and_exact_owner_ci(self):
        self.assertEqual(sorted(check_typed_initial(self.workspace(), self.assets)['ownerCi']), sorted(CI_FILES))
        self.refuses('canonical-knowledge-missing', check_typed_initial, self.workspace(knowledge=False), self.assets)
        self.refuses('owner-ci-payload', check_typed_initial, self.workspace(), dict(self.assets, **{CI_FILES[1]: b'other'}))
        root = self.workspace(); (root / CI_FILES[0]).unlink()
        with self.assertRaises(FileNotFoundError): check_typed_initial(root, self.assets)

    def test_default_row_acquires_no_knowledge_and_no_owner_ci(self):
        self.assertEqual(check_default(self.workspace('sdd', ci=False)), dict(canonicalKnowledge=False, ownerCi=False))
        self.refuses('default-acquired-canonical-knowledge', check_default, self.workspace('sdd', knowledge=True, ci=False))
        self.refuses('default-acquired-owner-ci', check_default, self.workspace('sdd', ci=True))

    def test_descriptor_must_select_the_current_template(self):
        root = self.workspace(); check_descriptor(root)
        (root / '.fsgg/providers.yml').write_text('    source: FS.GG.Workspace.Template::0.19.2\n')
        self.refuses('descriptor-template-source', check_descriptor, root)

    def test_wrong_key_must_refuse_without_a_product(self):
        report = {'outcome': 'blocked', 'scaffold': {'outcome': 'providerFailed', 'producedPathCount': 0,
                  'providerInvocation': {'exitCode': 127, 'standardError': "Invalid option(s): --target browser"}}}
        root = self.workspace('typed-sdd', knowledge=False, ci=False); (root / 'package.json').unlink()
        value = json.loads((root / '.fsgg/scaffold-provenance.json').read_text()); value.update(outcome='providerFailed', producedPaths=[])
        (root / '.fsgg/scaffold-provenance.json').write_text(json.dumps(value))
        self.assertEqual(check_refusal(report, root, 'Product')['providerExit'], 127)
        self.refuses('wrong-key-not-refused', check_refusal, dict(report, outcome='succeeded'), root, 'Product')
        (root / 'package.json').write_text('{}')
        self.refuses('wrong-key-produced-product', check_refusal, report, root, 'Product')

    def test_published_identity_and_installed_readback_are_literal(self):
        for key, (pid, version, digest) in PACKAGES.items():
            self.assertRegex(digest, '^[0-9a-f]{64}$'); self.assertTrue(verify_identity(key, digest, 'x')['match'])
            self.refuses('package-sha256-mismatch:' + key, verify_identity, key, '0' * 64, 'x')
        self.assertEqual(PACKAGES['wizard'][2][:8], '4326f57d')  # the Wizard 0.21.0 archive named by the #4642 decision
        listed = {'fs.gg.sdd.cli': '2.4.0', 'fs.gg.newsddworkspace': '0.21.0'}
        self.assertEqual(check_versions('2.3.0', '2.4.0', listed)['after'], '2.4.0')
        self.refuses('installed-sdd-not-2.4.0', check_versions, '2.3.0', '2.3.0', listed)
        self.refuses('predecessor-not-installed', check_versions, '2.4.0', '2.4.0', listed)
        self.refuses('tool-list-versions', check_versions, '2.3.0', '2.4.0', dict(listed, **{'fs.gg.newsddworkspace': '0.20.0'}))
        table = 'Package Id  Version  Commands\n-----\nfs.gg.sdd.cli  2.4.0  fsgg-sdd\nfs.gg.newsddworkspace  0.21.0  new-sdd-workspace\n'
        self.assertEqual(parse_tool_list(table), listed)

    def test_inputs_are_sealed_and_a_run_is_never_replayed(self):
        self.assertEqual(sha(Path(__file__).with_name('knowledge-console-public.py')), CONSOLE_SHA)
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR'))); self.addCleanup(shutil.rmtree, root, True)
        (root / 'tests/knowledge-receiver').mkdir(parents=True)
        for name in PROBE_FILES: (root / 'tests/knowledge-receiver' / name).write_text('drift')
        self.refuses('probe-source-drift:candidate.py', verify_probe_sources, root)
        self.refuses('fresh-output-no-replay', qualify, root, root)


if __name__ == '__main__':
    sys.exit(main())
