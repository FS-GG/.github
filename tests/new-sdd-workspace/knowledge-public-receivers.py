#!/usr/bin/env python3
"""Closed 24-subject public knowledge qualification. Source controls launch no child.

This adapter needs separately reviewed source, sealed inputs and fresh runtime admission.
It reuses the delivered console command/custody ports without changing that source.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
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
import xml.etree.ElementTree as ET

CONSOLE_SHA = 'afc9de40013bc71d430c91bb215a4938ef4ca9bd5e97e389ba53af3007204873'
PINNED_FILES = {
    'caseInventory': '0fec0241b854f8c113d4da474252aae6ba274b401ff8116054b2de726880fbe3',
    'extraInventory': '9b509e630d452b0037c5080f6d0b5ad8bb9039130da8fad040d6baa013fb8653',
    'sourceClosure': '3a80b2c6dfbe873b5d8901a27fbe542b507d6cf131c9c2edb1501877a0d1d9b2',
    'fsiPreflight': 'fe3d942c38f9cf446fe5e799a2551c15519b6b18c2e65bf7f45621c4d5fe26f0',
    'fsiClosure': '2446186e34e2599b66c785863af9ef247357b688bdfd40a87474019f4dbdaf0b',
}
SUPPORTED_SPEC_SHA = '7f7c796770678d8941e6865c6c161b07a65c881faf0dbca774521b574aa2b685'
ACCEPTED_APP_HEAD = 'e77bdf1442a91009c22411d3cb0917b825928587'
CI_FILES = ('.github/workflows/project-knowledge.yml', 'scripts/check-project-knowledge.py')
SERIAL_ENV = {'DOTNET_PROCESSOR_COUNT': '1', 'DOTNET_GCHeapHardLimit': '0x60000000',
              'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'MSBUILDDISABLENODEREUSE': '1'}
HOST_ENV = {'DOTNET_ROOT': '/usr/share/dotnet', 'DOTNET_ROOT_X64': '/usr/share/dotnet',
            'DOTNET_HOST_PATH': '/usr/share/dotnet/dotnet'}
TOP_KEYS = {'schema', 'inputs', 'packages', 'workspaceOriginal', 'sourceRoot', 'caseInventory',
            'extraInventory', 'sourceClosure', 'fsiPreflight', 'fsiClosure', 'wizard', 'sddExecutable',
            'dotnetExecutable', 'actualHome', 'childEnvironment', 'workRoot', 'report', 'acceptedApp'}


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
PACKAGES = dict(C.PACKAGES, **{
    'rendering': ('FS.GG.UI.Template', '0.32.1', '6c9f766fdd91483c2de6f061e75589e94852a265', 'c1f143a1e24c1393939de48b49ab86aa1ee20822da242eba6c701633eefbea3a'),
    'fable': ('Fable', '5.18.0', '6ccad2d4f874427a0743f432561e5b67a14c2ae8', 'cda6bb5099e7e4833b7e1194066a204540deff6ae91fbf5447e93a7729bb9264'),
    'fantomas': ('fantomas', '8.0.5', '972f7d0133de540ab2c582decddb0313ae617b53', '93e0de98bd4674b2122cd13074f46880e993f4f2d7b4531a7dcc16d873a4f63d'),
})



def fable_members(archive):
    """Read original names/bytes; normalize only a separately recorded extraction locator."""
    entries = archive.infolist()
    require(len(entries) <= 4096 and sum(e.file_size for e in entries) <= 128 * 1024 * 1024, 'archive-bound')
    raw_names = set(); extraction = {}; payload = {}
    for entry in entries:
        name = entry.filename
        require(name and entry.orig_filename == name and '\x00' not in name
                and not name.startswith('/') and '\\' not in name and ':' not in name
                and not name.endswith('//'), 'unsafe-fable-archive-member')
        body = name[:-1] if name.endswith('/') else name
        parts = body.split('/')
        require(parts[0] and parts[-1] and all(p not in ('.', '..') for p in parts), 'unsafe-fable-archive-member')
        kind = stat.S_IFMT(entry.external_attr >> 16)
        require(kind in (0, stat.S_IFREG, stat.S_IFDIR)
                and (kind != stat.S_IFDIR or entry.is_dir())
                and (not entry.is_dir() or kind != stat.S_IFREG), 'unsafe-fable-archive-member-type')
        normalized = '/'.join(p for p in parts if p)
        require(name not in raw_names and normalized not in extraction.values(), 'fable-normalized-member-collision')
        raw_names.add(name); extraction[name] = normalized
        if not entry.is_dir() and name != '.signature.p7s':
            payload[name] = hashlib.sha256(archive.read(entry)).hexdigest()
    files = {extraction[e.filename] for e in entries if not e.is_dir()}
    require(not any('/'.join(path.split('/')[:i]) in files
                    for path in extraction.values() for i in range(1, len(path.split('/')))), 'fable-normalized-parent-collision')
    return payload, extraction


def fable_archive(path):
    require(sha(path) == PACKAGES['fable'][3], 'literal-public-fable-archive')
    with zipfile.ZipFile(path) as archive:
        return fable_members(archive)


def public_package(key, binding, inputs, identity):
    if key != 'fable':
        return C.package(binding, inputs, identity)
    require(identity == PACKAGES['fable'], 'exact-public-fable-tuple')
    path = C.regular(binding['archive'], inputs)
    members, _ = fable_archive(path)
    require(binding['publicationReceipts'] and isinstance(binding['publicationReceipts'], list), 'publication-custody-required')
    for receipt in binding['publicationReceipts']: C.regular(receipt, inputs)
    names = [n for n in members if n.endswith('.nuspec')]
    require(len(names) == 1, 'package-metadata')
    with zipfile.ZipFile(path) as archive: metadata = ET.fromstring(archive.read(names[0]))
    values = lambda name: [n for n in metadata.iter() if n.tag.split('}')[-1] == name]
    require(all(len(values(n)) == 1 for n in ('id', 'version', 'repository')), 'package-metadata')
    require((values('id')[0].text, values('version')[0].text, values('repository')[0].get('commit')) == identity[:3], 'package-source-identity')
    return members


def package_payload(key, path):
    return fable_archive(path)[0] if key == 'fable' else C.archive_payload(path)


def rows():
    result = []
    for family in ('console', 'web', 'python', 'fable-bindings'):
        for route in ('wizard', 'provider', 'raw'):
            if family == 'fable-bindings' and route == 'wizard':
                continue  # Both positive Wizard bindings assertions remain a supplier gap.
            for selection in ('typed', 'default'):
                if (family, route, selection) == ('console', 'wizard', 'typed'):
                    continue  # Accepted4720 creation is not replayed.
                result.append(dict(id=f'{family}-{route}-{selection}', family=family, route=route,
                                   selection=selection, product='Knowledge' + ''.join(x.title() for x in family.split('-')) + route.title() + selection.title()))
    result += [dict(id='rendering-wizard-game-typed', family='rendering', route='wizard', selection='typed', product='KnowledgeRenderingGame'),
               dict(id='rendering-app-accepted4708-clone-capture', family='rendering', route='accepted-clone', selection='typed'),
               dict(id='retained-standard-explicit-fsharp-preview-apply-rollback', family='console', route='retained', selection='retained')]
    return result


ROWS = rows()


def assert_rows(inventory):
    actual = inventory['rows']
    require(len(actual) == 24 and len({r['id'] for r in actual}) == 24, 'matrix-population')
    require({r['id'] for r in actual} == {r['id'] for r in ROWS}, 'closed-matrix-routes')
    require(inventory['counts'] == {'typed': 12, 'default': 11, 'retained': 1}, 'matrix-assertion-counts')
    deferred = inventory['deferredSourceGaps']
    require({r['id'] for r in deferred} == {'fable-bindings-wizard-typed', 'fable-bindings-wizard-default'}
            and all(r['execute'] is False for r in deferred), 'deferred-positive-boundary')
    # creationArgv in the historical proposal is evidence only, never executable input.


def source_files(m):
    closure = C.read_json(m['sourceClosure'])
    for item in closure['files']:
        require(sha(C.regular(item['frozenFile'], m['inputs'])) == item['sha256'], 'source-closure-member')
    for item in closure['reusedFrozenProbeInputs']:
        require(sha(C.regular(item['path'], m['inputs'])) == item['sha256'], 'frozen-probe-member')
    for relative, digest in C.SOURCE_HASHES.items():
        if relative.startswith('tests/knowledge-receiver/'):
            require(sha(C.regular(Path(m['sourceRoot']) / relative, m['inputs'])) == digest, 'frozen-probe-source')
    return closure


def descriptor(m, family):
    candidates = [r for r in C.read_json(m['sourceClosure'])['files']
                  if r['repository'] == 'FS-GG/FS.GG.Templates' and r['revision'] == C.REVISION
                  and r['sourcePath'] == f'providers/{family}.providers.yml']
    require(len(candidates) == 1, 'exact-family-descriptor')
    return Path(candidates[0]['frozenFile'])


def sdk_snapshot(m):
    """Hash literal content AND enumerate layouts; existence is not loaded-framework proof."""
    preflight = C.read_json(m['fsiPreflight'])
    closure = C.read_json(m['fsiClosure'])
    require(closure['fileCount'] == len(closure['files']) == 4079 and closure['totalBytes'] == 589711886, 'fsi-roster-bound')
    expected = set()
    for row in closure['files']:
        path = Path(row['path']); info = path.lstat()
        require(path == path.resolve() and stat.S_ISREG(info.st_mode) and row['kind'] == 'regular'
                and stat.S_IMODE(info.st_mode) == row['mode'] and info.st_size == row['size']
                and sha(path) == row['sha256'], 'fsi-content-mode-drift')
        expected.add(str(path))
    # Include files outside selected roots (the literal public SDD closure) in its own exact tree.
    roots = [Path(p) for p in closure['roots']] + [Path(m['packages']['producer']['closure'])]
    actual = {str(p) for root in roots for p in root.rglob('*') if p.is_file() or p.is_symlink()}
    actual.add(str(Path(m['dotnetExecutable'])))
    require(actual == expected, 'fsi-content-population-drift')
    host = Path(m['dotnetExecutable']).parent
    def layout(p):
        return sorted(x.name for x in p.iterdir() if x.is_dir())
    require(layout(host / 'sdk') == preflight['sdkLayout'] and layout(host / 'host/fxr') == preflight['hostFxrLayout'], 'sdk-host-layout-drift')
    require({p.name: layout(p) for p in (host / 'shared').iterdir() if p.is_dir()} == preflight['sharedFrameworkLayout'], 'framework-layout-drift')
    require(sha(m['dotnetExecutable']) == preflight['dotnetSha256'], 'physical-dotnet-host')
    return dict(files=len(expected), bytes=closure['totalBytes'], rosterSha256=PINNED_FILES['fsiClosure'],
                loadedFramework='not inferred from this content snapshot')


def validate(path, expected_sha, after=False):
    require(expected_sha and sha(path) == expected_sha, 'matrix-manifest-digest')
    m = C.read_json(path)
    require(set(m) == TOP_KEYS and m['schema'] == 'fsgg.wizard.public-knowledge-matrix/1', 'closed-matrix-manifest')
    require(set(m['packages']) == set(PACKAGES), 'closed-public-package-tuple')
    for name in m['inputs']:
        C.regular(name, m['inputs'])
    C.regular(Path(__file__).resolve(), m['inputs'])
    C.regular(Path(__file__).with_name('knowledge-console-public.py').resolve(), m['inputs'])
    for key, digest in PINNED_FILES.items():
        require(sha(C.regular(m[key], m['inputs'])) == digest, 'sealed-input:' + key)
    assert_rows(C.read_json(m['caseInventory']))
    source_files(m)
    extra = C.read_json(m['extraInventory'])
    for name, digest in extra['inputs'].items():
        require(sha(C.regular(name, m['inputs'])) == digest, 'extra-tool-source-custody')
    for key, identity in PACKAGES.items():
        binding = m['packages'][key]
        require(set(binding) == ({'archive', 'publicationReceipts', 'closure'} if key in ('wizard', 'producer') else {'archive', 'publicationReceipts'}), 'closed-public-binding')
        members = public_package(key, binding, m['inputs'], identity)
        if key in ('wizard', 'producer'):
            C.tool_closure(binding, m['wizard' if key == 'wizard' else 'sddExecutable'], m['inputs'], members,
                           'new-sdd-workspace.dll' if key == 'wizard' else 'FS.GG.SDD.Cli.dll')
    original = C.regular(m['workspaceOriginal'], m['inputs'])
    require(sha(original) == C.ORIGINAL_WORKSPACE_SHA and C.archive_payload(original) == C.archive_payload(m['packages']['workspace']['archive']), 'public-workspace-original-join')
    for p in (m['wizard'], m['sddExecutable'], m['dotnetExecutable'], Path(sys.executable).resolve()):
        C.regular(p, m['inputs'])
    env = m['childEnvironment']
    require(set(env) == {'PATH', *SERIAL_ENV, *HOST_ENV}
            and all(env[k] == v for k, v in {**SERIAL_ENV, **HOST_ENV}.items()), 'closed-host-environment')
    require(m['actualHome'] == os.environ.get('HOME'), 'actual-home')
    for name, expected in [('dotnet', m['dotnetExecutable']), ('fsgg-sdd', m['sddExecutable'])]:
        selected = shutil.which(name, path=env['PATH'])
        require(selected and Path(selected).resolve() == Path(expected).resolve(), 'selected-host-executable')
    git = shutil.which('git', path=env['PATH']); require(git, 'missing-bound-git')
    C.regular(Path(git).resolve(), m['inputs'])
    require(set(m['acceptedApp']) == {'root', 'head', 'tree', 'files'}
            and m['acceptedApp']['head'] == ACCEPTED_APP_HEAD and m['acceptedApp']['files'], 'accepted-app-origin')
    root = Path(m['workRoot'])
    require(root.is_absolute() and root == root.resolve() and not root.is_symlink()
            and Path(m['report']).parent == root and (after or not root.exists()), 'fresh-output-no-replay')
    return m


def snapshot(root):
    result = {}
    for p in sorted(Path(root).rglob('*')):
        if '.git' in p.relative_to(root).parts:
            continue
        info = p.lstat()
        require(not stat.S_ISLNK(info.st_mode), 'subject-symlink')
        if p.is_file():
            result[p.relative_to(root).as_posix()] = dict(sha256=sha(p), mode=stat.S_IMODE(info.st_mode))
    return result


def env_for(m, case):
    env = {k: os.environ[k] for k in ('HOME', 'LANG', 'LC_ALL') if k in os.environ}
    env.update(m['childEnvironment'])
    env.update(DOTNET_CLI_HOME=str(case / 'dotnet-home'), NUGET_PACKAGES=str(case / 'packages'),
               NUGET_HTTP_CACHE_PATH=str(case / 'http-cache'), TMPDIR=str(case / 'tmp'), GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL='/dev/null')
    return env


class MatrixRunner(C.Runner):
    @contextmanager
    def subject(self, case, env):
        old_env, deadline = self.env, self.deadline
        self.env = env
        self.deadline = min(deadline, time.monotonic() + 180)
        try:
            yield self
        finally:
            self.env, self.deadline = old_env, deadline  # Restore the ORIGINAL absolute deadline.


def command(m, row, root):
    family, route, selected = row['family'], row['route'], row['selection']
    require(row['id'] in {r['id'] for r in ROWS}, 'unknown-row')
    if route == 'wizard':
        args = [m['wizard'], root, row['product'], '--template', family, '--pinned', '--no-governance', '--no-coordination', '--ref', C.REVISION]
        if family == 'rendering': args += ['--profile', 'game']
        return args + (['--lifecycle', 'typed-sdd'] if selected == 'typed' else [])
    if route == 'provider':
        return [m['sddExecutable'], 'scaffold', '--root', root, '--provider', family, '--no-update', '--json',
                '--param', 'productName=' + row['product']] + (['--param', 'lifecycle=typed-sdd'] if selected == 'typed' else [])
    if route == 'raw':
        return [m['dotnetExecutable'], 'new', 'fs-gg-' + family, '--name', row['product'], '--output', root] + (['--lifecycle', 'typed-sdd'] if selected == 'typed' else [])
    raise ValueError('clone-and-retained-dispatch-is-separate')


def pins(row):
    result = {} if row['route'] == 'raw' else dict(C.TOOL_PINS)
    if row['family'] == 'fable-bindings':
        result.update(fable='5.18.0', fantomas='8.0.5')
    if row['selection'] != 'default': result['fs.gg.sdd.cli'] = '2.2.0'
    return result


def manifest(root, expected):
    path = root / '.config/dotnet-tools.json'
    if not expected:
        require(not path.exists(), 'unexpected-raw-tool-manifest'); return None
    value = C.read_json(path)
    require(type(value['version']) is int and value['version'] == 1 and value['isRoot'] is True
            and set(value['tools']) == set(expected), 'route-tool-membership')
    require(all(value['tools'][k]['version'] == v for k, v in expected.items()), 'route-tool-pin')
    if 'fs.gg.sdd.cli' in expected:
        require('fsgg-sdd' in value['tools']['fs.gg.sdd.cli']['commands'], 'route-sdd-command')
    return value


def ensure_git(runner, root):
    if not (root / '.git').exists(): runner.git(root, 'init', '-q')


def commit(runner, root, message):
    runner.git(root, 'add', '.')
    runner.git(root, '-c', 'user.name=Knowledge matrix', '-c', 'user.email=receiver@example.invalid', 'commit', '-qm', message)
    return runner.git(root, 'rev-parse', 'HEAD')


def assets(root, archive, prefix):
    with zipfile.ZipFile(archive) as z:
        for name in CI_FILES:
            require((root / name).read_bytes() == z.read(prefix + name), 'owner-ci-payload')


def initial(runner, root, row, archive, prefix):
    ensure_git(runner, root)
    require(not runner.git(root, 'rev-list', '--all'), 'unexpected-background-commit')
    assets(root, archive, prefix)
    manifest(root, pins(row))
    canonical = sorted(p.relative_to(root).as_posix() for p in (root / '.fsgg/knowledge').rglob('*') if p.is_file())
    require('.fsgg/knowledge/schema.json' in canonical and any('/records/' in p for p in canonical), 'canonical-initial-store')
    required = canonical + ['.fsgg/knowledge-guide.md', '.config/dotnet-tools.json', *CI_FILES]
    cache = root / '.fsgg/cache/matrix.json'; cache.parent.mkdir(parents=True, exist_ok=True); cache.write_text('{}')
    head = commit(runner, root, 'Initial generated knowledge receiver')
    tracked = set(runner.git(root, 'ls-files').splitlines())
    require(set(required) <= tracked and cache.relative_to(root).as_posix() not in tracked, 'normal-initial-inclusion')
    blobs = {}
    for name in required:
        raw = runner.run(['git', '-C', root, 'show', head + ':' + name], text=False, check=True).stdout
        require(raw == (root / name).read_bytes(), 'caller-initial-blob')
        blobs[name] = hashlib.sha256(raw).hexdigest()
    return dict(commit=head, committedAssets=blobs)


def default_boundary(runner, root, row):
    require(not (root / '.fsgg/knowledge').exists() and all(not (root / p).exists() for p in CI_FILES), 'default-acquired-typed-knowledge')
    manifest(root, pins(row))
    if row['route'] != 'raw':
        provenance = C.read_json(root / '.fsgg/scaffold-provenance.json')
        effective = {p['key']: p['value'] for p in provenance['effectiveParameters']}
        require(provenance['providerName'] == row['family'] and effective['lifecycle'] == 'sdd', 'actual-default-selection')
    ensure_git(runner, root)
    head = commit(runner, root, 'Initial omitted Standard receiver')
    return dict(outcome='passed', effectiveLifecycle='sdd', knowledgeReady=False, baselineCommit=head, subject=snapshot(root))


def verify_bindings(root, m):
    value = C.read_json(root / 'package.json')
    deps = {**value.get('dependencies', {}), **value.get('devDependencies', {})}
    require(deps.get('@babylonjs/core') == '9.19.0', 'actual-bindings-default-package')
    with zipfile.ZipFile(m['packages']['workspace']['archive']) as archive:
        prefix = 'content/templates/fs-gg-fable-bindings/'
        contract = json.loads(archive.read(prefix + '.template.config/template.json'))
        require(contract['symbols']['bindingTarget']['defaultValue'] == 'browser', 'bindings-target-default-contract')
        require(value['scripts']['test:browser'] == json.loads(archive.read(prefix + 'package.json'))['scripts']['test:browser']
                and (root / 'runtime/browser/index.html').read_bytes() == archive.read(prefix + 'runtime/browser/index.html'), 'bindings-browser-owned-assets')
    return dict(package='@babylonjs/core', version=deps['@babylonjs/core'], defaultTargetContract='browser', browserAssetsVerified=True, packageJsonSha256=sha(root / 'package.json'))


def raw_knowledge_initialize(runner, m, root, row):
    before = snapshot(root)
    ignore = root / '.gitignore'
    ignore_bytes = ignore.read_bytes() if ignore.exists() else None
    require(not (root / '.fsgg/knowledge').exists(), 'raw-flag-impersonated-initializer')
    manifest(root, pins(row))
    runner.knowledge(m['sddExecutable'], 'initialize', '--root', str(root))
    after = snapshot(root)
    if ignore_bytes is not None:
        require(ignore.is_file() and ignore.read_bytes().startswith(ignore_bytes)
                and after['.gitignore']['mode'] == before['.gitignore']['mode'], 'raw-ignore-owner-prefix')
    for name, value in before.items():
        if name in ('.gitignore', '.config/dotnet-tools.json'): continue
        require(after.get(name) == value, 'raw-owner-content-changed')
    return dict(rawFlagInitializesStore=False, explicitCallerInitialize=True)



def raw_initialize(runner, m, root, row, config):
    before_setup = snapshot(root)
    require(not (root / '.fsgg/knowledge').exists(), 'raw-flag-impersonated-initializer')
    target = root / '.config/dotnet-tools.json'
    if not target.exists():
        runner.call([m['dotnetExecutable'], 'new', 'tool-manifest', '--output', root / '.config'], cwd=root)
    runner.call([m['dotnetExecutable'], 'tool', 'install', 'FS.GG.SDD.Cli', '--version', '2.2.0', '--local', '--tool-manifest', target, '--configfile', config], cwd=root)
    after_setup = snapshot(root)
    require(all(after_setup.get(p) == value for p, value in before_setup.items() if p != '.config/dotnet-tools.json'), 'raw-tool-setup-owner-content-changed')
    return raw_knowledge_initialize(runner, m, root, row)


def restore_tools(runner, m, root, row, config):
    observed = manifest(root, pins(row))
    # Every actual generated tool is checked against the already sealed feed before restore.
    available = {identity[0].lower(): identity[1] for identity in PACKAGES.values()}
    require(all(available.get(k) == v['version'] for k, v in observed['tools'].items()), 'unsealed-manifest-dependency')
    runner.call([m['dotnetExecutable'], 'tool', 'restore', '--configfile', config, '--no-cache'], cwd=root)
    package_root = Path(runner.env['NUGET_PACKAGES'])
    for key, identity in PACKAGES.items():
        if identity[0].lower() not in observed['tools']: continue
        for name, digest in package_payload(key, m['packages'][key]['archive']).items():
            if name.startswith('tools/net10.0/any/'):
                require(sha(package_root / identity[0].lower() / identity[1] / name) == digest, 'restored-tool-payload')


def current_shared(runner, executable, root):
    return runner.knowledge(executable, 'search', '--root', str(root))


def bundle_recovery(runner, executable, root, scratch, findings_commit):
    shared = current_shared(runner, executable, root)
    bundle, clone = scratch / 'history.bundle', scratch / 'bundle-clone'
    runner.git(root, 'bundle', 'create', bundle, '--all')
    runner.git(root, 'bundle', 'verify', bundle)
    runner.call(['git', 'clone', '-q', bundle, clone])
    require(not (clone / '.fsgg/cache').exists(), 'bundle-clone-cache')
    require(current_shared(runner, executable, clone) == shared, 'bundle-record-recovery')
    for row in shared:
        identity = row['Record']['Id']
        require(runner.knowledge(executable, 'get', '--root', str(clone), '--id', identity) == row, 'bundle-version-recovery')
    history = runner.knowledge(executable, 'history', '--root', str(clone), '--id', 'architecture-decision')
    require(len(history) == 2, 'bundle-history-recovery')
    old = runner.knowledge(executable, 'get-version', '--root', str(clone), '--id', 'architecture-decision', '--commit', findings_commit)
    original_old = runner.knowledge(executable, 'get-version', '--root', str(root), '--id', 'architecture-decision', '--commit', findings_commit)
    require(old == original_old and old['Revision'] != next(x['Revision'] for x in shared if x['Record']['Id'] == 'architecture-decision'), 'bundle-selected-version-recovery')
    return dict(bundleSha256=sha(bundle), sharedIds=sorted(r['Record']['Id'] for r in shared), historyVersions=2, selectedVersionRecovered=True)


def full_knowledge(runner, m, root, row, case, config):
    restore_tools(runner, m, root, row, config)
    evidence = dict(ordinaryChecker=C.checked(runner, root))
    with C.frozen_probes(m['sourceRoot'], runner) as (candidate, public):
        evidence['probe'] = candidate.probe([m['sddExecutable']], root, case / 'probe')
        evidence['probe']['probeSetupCommit'] = evidence['probe'].pop('initialCommit')
        evidence['postPrivateExport'] = C.post_private_export(runner, m['sddExecutable'], root, case / 'probe')
        evidence['gitBundle'] = bundle_recovery(runner, m['sddExecutable'], root, case / 'probe', evidence['probe']['findingsCommit'])
        evidence['ordinaryBudget'] = public.ordinary_ci_limits(root)
    evidence['ordinaryCheckerAfterRestore'] = C.checked(runner, root)
    return evidence


def ci_role(runner, m, root, case):
    require(all(not (root / p).exists() for p in CI_FILES), 'caller-ci-assets-already-exist')
    before = snapshot(root)
    runner.call([m['dotnetExecutable'], 'new', 'install', m['packages']['workspace']['archive']], cwd=root)
    runner.call([m['dotnetExecutable'], 'new', 'fs-gg-project-knowledge', '--lifecycle', 'typed-sdd', '--output', root], cwd=root)
    after = snapshot(root)
    require(set(after) - set(before) == set(CI_FILES) and all(after[p] == v for p, v in before.items()), 'ci-role-not-exactly-two-preserving-assets')
    assets(root, m['packages']['workspace']['archive'], 'content/templates/fs-gg-project-knowledge/')
    collision = case / 'ci-owner-collision'; collision.mkdir()
    owned = collision / CI_FILES[1]; owned.parent.mkdir(parents=True); owned.write_text('Authored checker must survive.\n')
    original = snapshot(collision)
    result = runner.call([m['dotnetExecutable'], 'new', 'fs-gg-project-knowledge', '--lifecycle', 'typed-sdd', '--output', collision], cwd=root, success=False)
    require(snapshot(collision) == original, 'ci-collision-overwrote-owner')
    return dict(added=list(CI_FILES), ownerCollisionExit=result.returncode, existingBytesAndModesPreserved=True)


def app_donor(runner, m):
    donor = m['acceptedApp']; root = Path(donor['root'])
    require(root == root.resolve() and root.is_dir(), 'unsafe-accepted-donor')
    require(runner.git(root, 'rev-parse', 'HEAD') == donor['head']
            and runner.git(root, 'rev-parse', 'HEAD^{tree}') == donor['tree'], 'accepted-donor-head-tree')
    require(snapshot(root) == donor['files'], 'accepted-donor-owner-preimage')
    return root


def inherited_initial(runner, m, root):
    donor = m['acceptedApp']
    require(runner.git(root, 'rev-parse', 'HEAD') == donor['head']
            and runner.git(root, 'rev-parse', 'HEAD^{tree}') == donor['tree'], 'cloned-origin-head-tree')
    manifest(root, C.TOOL_PINS)
    required = ['.fsgg/knowledge/schema.json', '.fsgg/knowledge/records/project-knowledge.json', '.fsgg/knowledge-guide.md', '.config/dotnet-tools.json', *CI_FILES]
    for name in required:
        raw = runner.run(['git', '-C', root, 'show', donor['head'] + ':' + name], text=False, check=True).stdout
        require(hashlib.sha256(raw).hexdigest() == donor['files'][name]['sha256'], 'accepted-initial-assets')
    return dict(acceptedCreationReused=True, originalCallerCommit=donor['head'], tree=donor['tree'])


def supported_spec(m):
    extra = C.read_json(m['extraInventory'])
    files = [Path(p) for p, digest in extra['inputs'].items() if p.endswith('/supported-spec.md') and digest == SUPPORTED_SPEC_SHA]
    require(len(files) == 1, 'supported-nonempty-standard-fixture')
    return files[0]


def migration_command(m, root, operation='migrate', accept=False):
    result = [m['sddExecutable'], 'typed-sdd', operation, '--root', root, '--work', 'demo']
    if operation == 'migrate': result += ['--source', 'work/demo/spec.md', '--backend', 'fsharp-specification-v1']
    return result + (['--accept'] if accept else [])


def fsi_observation(runner, m, root, case, label):
    preflight = C.read_json(m['fsiPreflight'])
    require(sha(root / 'global.json') == preflight['retainedConsoleGlobalJson']['sha256'], 'retained-sdk-global-json')
    sdk = runner.call([m['dotnetExecutable'], '--version'], cwd=root).stdout.strip()
    require(sdk == '10.0.401', 'actual-retained-sdk')
    artifact = preflight['publicSddArtifacts']
    script = case / (label + '-fsi-observation.fsx')
    path = artifact['path'].replace('\\', '\\\\').replace('"', '\\"')
    script.write_text('#r "' + path + '"\nopen System\nopen System.Text.Json\nopen System.Runtime.InteropServices\nopen FS.GG.SDD.Artifacts.TypedSpecifications\nprintfn "%s" (JsonSerializer.Serialize {| framework = RuntimeInformation.FrameworkDescription; coreLib = typeof<obj>.Assembly.Location; fsharpCore = typeof<list<int>>.Assembly.Location; artifacts = typeof<SpecificationId>.Assembly.Location |})\n')
    observed = json.loads(runner.call([m['dotnetExecutable'], 'fsi', '--exec', script], cwd=root, timeout=30).stdout)
    files = {r['path']: r for r in C.read_json(m['fsiClosure'])['files']}
    for key in ('coreLib', 'fsharpCore', 'artifacts'):
        require(observed[key] in files and sha(observed[key]) == files[observed[key]]['sha256'], 'observed-fsi-loaded-content')
    require(observed['artifacts'] == artifact['path'] and sha(observed['artifacts']) == artifact['sha256'], 'observed-public-artifacts-location')
    return dict(sdk=sdk, observation=observed, cwdGlobalSha256=sha(root / 'global.json'),
                internalMigrationChildLoadedPaths='not independently observed; this is an actual same-cwd/environment FSI observation')


def rollback_preservation(before, after):
    changed = {'work/demo/spec.md', 'work/demo/specification.fsx',
               'readiness/demo/specification.normalized.json', 'readiness/demo/typed-authority.json'}
    require({p: v for p, v in before.items() if p not in changed}
            == {p: v for p, v in after.items() if p not in changed}, 'rollback-owner-product-tool-preservation')
    require('work/demo/spec.md' in after
            and all(p in before and p not in after for p in changed - {'work/demo/spec.md'}), 'rollback-explicit-authority-removal')


def retained(runner, m, root, case, donor, config, row):
    runner.call(['git', 'clone', '--no-local', donor, root])
    require(not (root / '.fsgg/knowledge').exists(), 'retained-store-not-absent')
    spec = root / 'work/demo/spec.md'; require(not spec.exists(), 'retained-source-collision')
    spec.parent.mkdir(parents=True, exist_ok=True); spec.write_bytes(supported_spec(m).read_bytes())
    note = root / 'docs/retained-owner-note.md'; require(not note.exists(), 'retained-owner-note-collision')
    note.parent.mkdir(exist_ok=True); note.write_text('Owner finding: retain the Standard source and its explicit references during compatibility migration. Evidence: work/demo/spec.md.\n')
    guide = root / '.fsgg/knowledge-guide.md'; guide.write_text((guide.read_text() if guide.exists() else '') + '\nAuthored retained knowledge guidance.\n')
    baseline = commit(runner, root, 'Retained nonempty Standard specification and owner note')
    before = snapshot(root)
    ignore_bytes = (root / '.gitignore').read_bytes()
    preview = json.loads(runner.call(migration_command(m, root), cwd=root).stdout)
    require(preview['outcome'] == 'noChange' and preview['classification'] == 'Migrated'
            and snapshot(root) == before and not (root / '.fsgg/knowledge').exists(), 'migration-preview-write-or-classification')
    observations = dict(before=fsi_observation(runner, m, root, case, 'before'))
    applied = json.loads(runner.call(migration_command(m, root, accept=True), cwd=root).stdout)
    require(applied['outcome'] == 'succeeded' and applied['classification'] == 'Migrated', 'actual-fsharp-migration')
    require((root / '.fsgg/knowledge/records/project-knowledge.json').is_file()
            and (root / 'work/demo/spec.standard-sdd.rollback.md').read_bytes() == supported_spec(m).read_bytes(), 'migration-source-or-bootstrap')
    after = snapshot(root)
    for name, old in before.items():
        if name in ('work/demo/spec.md', '.gitignore'): continue
        require(after.get(name) == old, 'migration-owner-content-changed')
    require((root / '.gitignore').read_bytes().startswith(ignore_bytes), 'retained-ignore-owner-prefix')
    migration_outputs(before, after, root)
    return retained_post_apply(runner, m, root, case, config, row, baseline, preview, applied, observations)


AUTHORITY_LOCK = '.fsgg/typed-sdd-transactions/authority.lock'


def migration_outputs(before, after, root):
    allowed_new = {'work/demo/spec.standard-sdd.rollback.md', 'work/demo/specification.fsx',
                   'readiness/demo/specification.normalized.json', 'readiness/demo/typed-authority.json',
                   '.fsgg/knowledge/schema.json', '.fsgg/knowledge/records/project-knowledge.json', AUTHORITY_LOCK}
    require(set(after) - set(before) <= allowed_new, 'migration-unexpected-output')
    lock = root / AUTHORITY_LOCK
    require(lock.is_file() and not lock.is_symlink() and lock.read_bytes() == b'', 'migration-authority-lock-not-empty-regular-file')


def retained_post_apply(runner, m, root, case, config, row, baseline, preview, applied, observations):
    """Remaining effects only: caller must bind the already-applied subject and original evidence.

    Never clones, previews, migrates/applies or initializes the receiver again.
    """
    spec = root / 'work/demo/spec.md'
    require(preview['outcome'] == 'noChange' and applied['outcome'] == 'succeeded'
            and applied['classification'] == 'Migrated', 'retained-post-apply-original-proof')
    require((root / '.fsgg/knowledge/records/project-knowledge.json').is_file()
            and (root / 'work/demo/spec.standard-sdd.rollback.md').read_bytes() == supported_spec(m).read_bytes(), 'retained-post-apply-source-or-bootstrap')
    #The fresh caller validated before/after; continuation validates committed baseline names.
    #Recheck the persistent lock here without pretending this is a before-image.
    migration_outputs(snapshot(root), snapshot(root), root)
    role = ci_role(runner, m, root, case)
    migration_commit = commit(runner, root, 'Accept explicit FSharp compatibility and preserving CI role')
    evidence = full_knowledge(runner, m, root, row, case, config)
    require(evidence['probe']['probeSetupCommit'] != migration_commit, 'probe-commit-relabeled-as-migration')
    history = runner.knowledge(m['sddExecutable'], 'history', '--root', str(root), '--id', 'architecture-decision')
    before_rollback = snapshot(root)
    rolled = json.loads(runner.call(migration_command(m, root, operation='rollback', accept=True), cwd=root).stdout)
    require(rolled['outcome'] == 'succeeded' and spec.read_bytes() == supported_spec(m).read_bytes()
            and not (root / 'readiness/demo/typed-authority.json').exists()
            and not (root / 'work/demo/specification.fsx').exists(), 'actual-rollback-source-or-authority')
    rollback_preservation(before_rollback, snapshot(root))
    require(runner.knowledge(m['sddExecutable'], 'history', '--root', str(root), '--id', 'architecture-decision') == history, 'rollback-lost-knowledge-history')
    observations['after'] = fsi_observation(runner, m, root, case, 'after')
    require(observations['before'] == observations['after'], 'fsi-selection-drift')
    return dict(baselineCommit=baseline, migrationCommit=migration_commit, rollbackCommit=commit(runner, root, 'Restore original Standard specification'),
                preview=preview, applied=applied, rollback=rolled, ciRole=role, fsi=observations, knowledge=evidence)


def create(runner, m, row, root, case):
    if row['route'] == 'provider':
        path = root / '.fsgg/providers.yml'; path.parent.mkdir(parents=True)
        path.write_bytes(descriptor(m, row['family']).read_bytes())
    if row['route'] == 'wizard' and row['selection'] == 'typed':
        runner.call([m['dotnetExecutable'], 'new', 'install', m['packages']['overlay']['archive']], cwd=case)
        C.registration(case, '0.18.2', PACKAGES['overlay'][3])
    if row['route'] == 'raw':
        with zipfile.ZipFile(m['packages']['workspace']['archive']) as archive:
            template = json.loads(archive.read('content/templates/fs-gg-' + row['family'] + '/.template.config/template.json'))
            require(template['symbols']['lifecycle']['defaultValue'] == 'sdd', 'raw-package-default-contract')
        runner.call([m['dotnetExecutable'], 'new', 'install', m['packages']['workspace']['archive']], cwd=case)
    result = runner.call(command(m, row, root), cwd=case)
    if row['route'] != 'raw':
        provenance = C.read_json(root / '.fsgg/scaffold-provenance.json')
        effective = {p['key']: p['value'] for p in provenance['effectiveParameters']}
        require(provenance['providerName'] == row['family'] and effective['lifecycle'] == ('typed-sdd' if row['selection'] == 'typed' else 'sdd'), 'provider-effective-selection')
        if row['family'] == 'rendering': require(effective['profile'] == 'game', 'actual-rendering-game-profile')
    if row['family'] == 'fable-bindings': verify_bindings(root, m)
    version = '0.18.2' if row['family'] == 'rendering' else '0.19.0'
    C.registration(case, version, PACKAGES['overlay' if version == '0.18.2' else 'workspace'][3])
    return result


ORIGINAL_POSTTERMINAL_CUSTODY_SHA = 'f8e4693bada60e3c2954f2961118e1d4ee57080e8f2a6c08ab571c83509b95ea'
ORIGINAL_DEADLINE_UTC = '2026-10-20T06:10:37.893628+00:00'


def complete_snapshot(root):
    result = {}
    for path in Path(root).rglob('*'):
        info = path.lstat()
        require(not stat.S_ISLNK(info.st_mode), 'continuation-owner-symlink')
        if path.is_file():
            result[path.relative_to(root).as_posix()] = dict(sha256=sha(path), mode=stat.S_IMODE(info.st_mode), size=info.st_size)
    return result


def continuation_deadline(checkpoint):
    from datetime import datetime, timezone
    end = datetime.fromisoformat(checkpoint['deadlineUtc'])
    observed = datetime.fromisoformat(checkpoint['clockObservedUtc'])
    require(checkpoint['deadlineUtc'] == ORIGINAL_DEADLINE_UTC, 'original-deadline-not-renewed')
    deadline = checkpoint['deadlineMonotonic']
    require(abs(deadline - checkpoint['clockObservedMonotonic'] - (end - observed).total_seconds()) < .01
            and abs(time.monotonic() - checkpoint['clockObservedMonotonic']
                    - (datetime.now(timezone.utc) - observed).total_seconds()) < 1, 'original-wall-monotonic-clock-binding')
    require(time.monotonic() < deadline - 10 and datetime.now(timezone.utc) < end, 'original-window-expired')
    return deadline


def relocate_raw_manifest(root):
    source = root / 'dotnet-tools.json'; target = root / '.config/dotnet-tools.json'
    require(source.is_file() and not source.is_symlink() and not target.exists()
            and list(root.rglob('dotnet-tools.json')) == [source], 'retained-raw-manifest-location-conflict')
    value = C.read_json(source)
    require(value['version'] == 1 and value['isRoot'] is True
            and set(value['tools']) == {'fs.gg.sdd.cli'}
            and value['tools']['fs.gg.sdd.cli']['version'] == '2.2.0'
            and value['tools']['fs.gg.sdd.cli']['commands'] == ['fsgg-sdd'], 'retained-raw-capable-pin')
    before = complete_snapshot(root); payload = source.read_bytes(); mode = stat.S_IMODE(source.stat().st_mode)
    target.parent.mkdir(exist_ok=True)
    require(not target.parent.is_symlink(), 'retained-raw-config-symlink')
    # link is exclusive: a raced destination refuses rather than replacing owner bytes.
    os.link(source, target); source.unlink()
    after = complete_snapshot(root)
    expected = dict(before); expected['.config/dotnet-tools.json'] = expected.pop('dotnet-tools.json')
    require(after == expected and target.read_bytes() == payload and stat.S_IMODE(target.stat().st_mode) == mode, 'retained-raw-relocation-owner-preservation')
    return dict(originalSha256=hashlib.sha256(payload).hexdigest(), mode=mode, fromPath='dotnet-tools.json', toPath='.config/dotnet-tools.json')


def resume_raw(runner, m, root, row, tail):
    before = complete_snapshot(root)
    checkpoint = dict(semantics='Actual retained post-successful-tool-install baseline; no lost preinstall snapshot claimed',
                      files=before, sourceInputSha256=sha(__file__), beforeAnyReceiverMutation=True)
    with (tail / 'raw-postinstall-checkpoint.json').open('x') as out: json.dump(checkpoint, out, indent=2)
    relocation = relocate_raw_manifest(root)
    result = raw_knowledge_initialize(runner, m, root, row)
    result.update(originalToolInstallReused=True, creationReplayed=False, relocation=relocation,
                  preservationBaseline='retained postinstall before explicit relocation/knowledge initialization')
    return result


def continue_qualification(path, digest):
    require(sha(path) == digest, 'continuation-checkpoint-digest')
    checkpoint = C.read_json(path)
    require(set(checkpoint) == {'schema','admittedBy','operation','manifest','manifestSha256','originalCustody','originalCustodySha256',
                               'originalTerminalSha256','deadlineUtc','clockObservedUtc','clockObservedMonotonic','deadlineMonotonic'}
            and checkpoint['schema'] == 'fsgg.matrix4723.original-tail/1' and checkpoint['admittedBy'] == 'Programme/root'
            and checkpoint['operation'] == 'matrix4723-runtime20261020-01', 'closed-continuation-authority')
    deadline = continuation_deadline(checkpoint)  # Before validation/hashing; never now+600.
    m = validate(checkpoint['manifest'], checkpoint['manifestSha256'], after=True)
    base = Path(m['workRoot']); tail = base / 'continuation01'
    require(base.name == 'matrix4723-runtime01' and base.is_dir() and not tail.exists(), 'retained-tail-no-replay')
    require(checkpoint['originalCustodySha256'] == ORIGINAL_POSTTERMINAL_CUSTODY_SHA
            and sha(checkpoint['originalCustody']) == ORIGINAL_POSTTERMINAL_CUSTODY_SHA, 'original-postterminal-custody')
    custody = C.read_json(checkpoint['originalCustody'])
    original_report = base / 'report.json'; journal = base / 'commands.jsonl'
    require(sha(original_report) == custody['originalReportSha256'] and sha(journal) == custody['originalJournalSha256'], 'original-failure-report-journal')
    original = C.read_json(original_report)
    require(original['state'] == 'failed' and original['commands'] == 88 and len(original['rows']) == 4
            and [r['id'] for r in original['rows']] == [r['id'] for r in ROWS[:4]]
            and all(r['state'] == 'passed' for r in original['rows'][:3]) and original['rows'][3]['state'] == 'failed', 'exact-original-accepted-prefix')
    terminal = base.parent / 'matrix4723-runtime-terminal01.json'
    require(sha(terminal) == checkpoint['originalTerminalSha256'] and C.read_json(terminal)['actualExitCode'] == 1, 'original-failed-terminal')
    originals = {str(original_report): sha(original_report), str(journal): sha(journal), str(terminal): sha(terminal)}
    terminals = [json.loads(line) for line in journal.read_text().splitlines() if json.loads(line).get('phase') == 'terminal']
    require([r['sequence'] for r in terminals] == list(range(1,89)) and all(r['groupObservedAbsent'] for r in terminals), 'original88-terminal-cleanup')
    for command in terminals:
        for stream in ('stdout','stderr'):
            file = base / command[stream+'Path']; require(sha(file) == command[stream+'Sha256'], 'original88-output-drift')
            originals[str(file)] = sha(file)
    for row in ROWS[:4]:
        require(complete_snapshot(base / row['id'] / 'subject') == custody['rows'][row['id']]['files'], 'retained-prefix-or-raw-owner-drift')
    for key, identity in PACKAGES.items():
        if key != 'wizard':
            require(sha(base / 'feed' / (identity[0] + '.' + identity[1] + '.nupkg')) == identity[3], 'retained-feed-package-drift')
    expected_config = '<configuration><packageSources><clear/><add key="sealed-public-originals" value="' + str(base / 'feed') + '"/></packageSources></configuration>'
    require((base / 'NuGet.Config').read_text() == expected_config, 'retained-feed-config-drift')
    before_sdk = sdk_snapshot(m)
    continuation_deadline(checkpoint)
    tail.mkdir(mode=0o700)
    runner = MatrixRunner(tail, env_for(m, base), deadline=deadline-5); runner.sequence = 88
    report = dict(schema='fsgg.wizard.public-knowledge-matrix-tail-result/1', state='started', manifestSha256=checkpoint['manifestSha256'],
                  originalFailureReportSha256=originals[str(original_report)], originalJournalSha256=originals[str(journal)],
                  originalCustodySha256=ORIGINAL_POSTTERMINAL_CUSTODY_SHA, originalDeadlineUtc=ORIGINAL_DEADLINE_UTC,
                  first3AcceptedOriginalRowsReused=True, failedFourthRowRetained=True, automaticRetry=False,
                  rows=original['rows'][:3], sdkBefore=before_sdk)
    def save(): (tail / 'report.json').write_text(json.dumps(report,indent=2)+'\n')
    save()
    try:
        for entry in original['rows'][:3]:
            root = base / entry['id'] / 'subject'
            expected = entry['boundary']['baselineCommit'] if 'boundary' in entry else entry['knowledge']['probe']['updatedCommit']
            require(runner.git(root,'rev-parse','HEAD') == expected, 'accepted-prefix-reported-git-head')
        donor = app_donor(runner,m)
        execute_rows(runner,m,base,report,save,donor,base/'console-provider-default/subject',3,True)
        require(len(report['rows']) == 24 and all(r['state']=='passed' for r in report['rows']), 'incomplete-original-prefix-tail')
        require(all(sha(p)==h for p,h in originals.items()), 'original-custody-overwritten')
        for row in ROWS[:3]:
            require(complete_snapshot(base/row['id']/'subject') == custody['rows'][row['id']]['files'], 'accepted-prefix-changed-by-tail')
        require(validate(checkpoint['manifest'],checkpoint['manifestSha256'],after=True)==m and sdk_snapshot(m)==before_sdk, 'tail-post-input-drift')
        continuation_deadline(checkpoint)
        report.update(state='passed',commands=runner.sequence,tailCommands=runner.sequence-88);save();return report
    except BaseException as error:
        if report['rows'][-1]['state']=='started':report['rows'][-1].update(state='failed',failureType=type(error).__name__,failure=str(error))
        report.update(state='failed',failureType=type(error).__name__,failure=str(error),commands=runner.sequence);save();raise


PYTHON_WIZARD_HOLDS = {'python-wizard-typed', 'python-wizard-default'}
TAIL01_CUSTODY_SHA = '45446c83ade42813cd815e5c3f380283bf7c1af81b63a6a66606662f2e0015c9'


def wizard_capabilities(m):
    import re
    candidates = [r for r in C.read_json(m['sourceClosure'])['files'] if r['repository']=='FS-GG/.github'
                  and r['revision']==PACKAGES['wizard'][2] and r['sourcePath']=='scripts/NewSddWorkspace/Program.fs']
    require(len(candidates)==1, 'published-wizard-capability-source')
    source = C.regular(candidates[0]['frozenFile'],m['inputs']).read_text()
    block = re.search(r'^let private templates =\s*\[(.*?)^\s*\]',source,re.M|re.S)
    require(block is not None and 'let knownTemplates = templates |> List.map fst' in source
            and 'if List.contains value knownTemplates then' in source, 'wizard-parser-capability-contract')
    families = re.findall(r'^\s*"([a-z-]+)",',block.group(1),re.M)
    require(families==['rendering','console','web','fable-game','fable-bindings'], 'exact-published019-wizard-families')
    return families


def held_python(row, capabilities, original_refusal=False):
    if row['route']!='wizard' or row['family'] in capabilities: return None
    require(row['id'] in PYTHON_WIZARD_HOLDS, 'unreviewed-unsupported-wizard-route')
    return dict(id=row['id'],route='wizard',selection=row['selection'],state='blocked',
                owner='FS-GG/.github Wizard producer',reason='Published Wizard0.19.0 excludes Python from its parser; compatible published supplier required',
                positiveAcceptance=False,thisContinuationCreationInvoked=False,
                originalAttemptInvoked=bool(original_refusal and row['selection']=='typed'),
                originalRefusalEvidence=(dict(sequence=414,actualExitCode=2,journal='continuation01/commands.jsonl',stdout='continuation01/414.stdout')
                                         if original_refusal and row['selection']=='typed' else None))


def matrix_state(rows):
    require(len(rows)==24 and [r['id'] for r in rows]==[r['id'] for r in ROWS], 'incomplete-24-subject-accounting')
    blocked={r['id'] for r in rows if r['state']=='blocked'}
    require(blocked==PYTHON_WIZARD_HOLDS and all(r['state']=='passed' for r in rows if r['id'] not in blocked), 'incomplete-supported-tail')
    return 'partial'  #22passed/2held never establishes full24 or Delivery closure.


def verify_output_journal(directory, first, last):
    journal=directory/'commands.jsonl'
    terminals=[json.loads(line) for line in journal.read_text().splitlines() if json.loads(line).get('phase')=='terminal']
    require([r['sequence'] for r in terminals]==list(range(first,last+1)) and all(r['groupObservedAbsent'] for r in terminals), 'retained-tail-terminal-cleanup')
    originals={str(journal):sha(journal)}
    for command in terminals:
        for stream in ('stdout','stderr'):
            file=directory/command[stream+'Path'];require(sha(file)==command[stream+'Sha256'],'retained-tail-output-drift')
            originals[str(file)]=sha(file)
    return originals



def recovery_deadline(checkpoint, *, now_utc=None, now_monotonic=None):
    from datetime import datetime, timezone
    now_utc = now_utc or datetime.now(timezone.utc)
    now_monotonic = time.monotonic() if now_monotonic is None else now_monotonic
    original = datetime.fromisoformat(ORIGINAL_DEADLINE_UTC)
    require(checkpoint['originalDeadlineUtc']==ORIGINAL_DEADLINE_UTC and checkpoint['originalWindowExpired'] is True
            and now_utc>=original, 'original-window-must-already-be-expired')
    require(checkpoint['operation']=='matrix4723-supported-tail-recovery20261020-01', 'separate-supported-recovery-operation')
    observed = datetime.fromisoformat(checkpoint['recoveryClockObservedUtc'])
    end = datetime.fromisoformat(checkpoint['recoveryDeadlineUtc'])
    seconds = (end-observed).total_seconds()
    require(observed>=original and 0<seconds<=600, 'recovery-profile-maximum600')
    deadline = checkpoint['recoveryDeadlineMonotonic']
    require(abs(deadline-checkpoint['recoveryClockObservedMonotonic']-seconds)<.01
            and abs(now_monotonic-checkpoint['recoveryClockObservedMonotonic']-(now_utc-observed).total_seconds())<1,
            'recovery-wall-monotonic-clock-binding')
    require(observed<=now_utc<end and now_monotonic<deadline-10, 'fresh-recovery-window-expired-or-insufficient')
    return deadline


def continue_supported_tail(path,digest,recovery=False):
    require(sha(path)==digest,'continuation02-checkpoint-digest'); checkpoint=C.read_json(path)
    common={'schema','admittedBy','operation','manifest','manifestSha256','originalCustody','originalCustodySha256','tailCustody','tailCustodySha256'}
    clocks=({'originalDeadlineUtc','originalWindowExpired','recoveryClockObservedUtc','recoveryClockObservedMonotonic','recoveryDeadlineUtc','recoveryDeadlineMonotonic'}
            if recovery else {'deadlineUtc','clockObservedUtc','clockObservedMonotonic','deadlineMonotonic'})
    schema='fsgg.matrix4723.supported-tail-recovery/3' if recovery else 'fsgg.matrix4723.supported-tail/2'
    operation='matrix4723-supported-tail-recovery20261020-01' if recovery else 'matrix4723-runtime20261020-01'
    require(set(checkpoint)==common|clocks and checkpoint['schema']==schema and checkpoint['admittedBy']=='Programme/root'
            and checkpoint['operation']==operation,'closed-supported-tail-authority')
    check_clock=lambda: recovery_deadline(checkpoint) if recovery else continuation_deadline(checkpoint)
    deadline=check_clock()  #Before validation/hashing; fixed original or separately admitted recovery.
    m=validate(checkpoint['manifest'],checkpoint['manifestSha256'],after=True);capabilities=wizard_capabilities(m)
    base=Path(m['workRoot']);tail=base/('supported-tail-recovery01' if recovery else 'continuation02');prior=base/'continuation01'
    require(base.name=='matrix4723-runtime01' and not tail.exists(),'continuation02-no-replay')
    require(checkpoint['originalCustodySha256']==ORIGINAL_POSTTERMINAL_CUSTODY_SHA and sha(checkpoint['originalCustody'])==ORIGINAL_POSTTERMINAL_CUSTODY_SHA
            and checkpoint['tailCustodySha256']==TAIL01_CUSTODY_SHA and sha(checkpoint['tailCustody'])==TAIL01_CUSTODY_SHA,'original-and-tail01-custody')
    original_custody=C.read_json(checkpoint['originalCustody']);custody=C.read_json(checkpoint['tailCustody'])
    require(sha(base/'report.json')==original_custody['originalReportSha256'] and sha(base/'commands.jsonl')==original_custody['originalJournalSha256']
            and sha(prior/'report.json')==custody['tailReportSha256'] and sha(prior/'commands.jsonl')==custody['tailJournalSha256'],'both-original-failures-preserved')
    terminal=base.parent/'matrix4723-original-tail-terminal01.json'
    require(sha(terminal)==custody['tailTerminalSha256'],'original-tail01-terminal')
    old=C.read_json(prior/'report.json')
    require(old['state']=='failed' and old['commands']==414 and len(old['rows'])==12 and all(r['state']=='passed' for r in old['rows'][:11])
            and [r['id'] for r in old['rows']]==[r['id'] for r in ROWS[:12]] and old['rows'][11]['state']=='failed','exact-eleven-accepted-prefix')
    originals=verify_output_journal(base,1,88);originals.update(verify_output_journal(prior,89,414))
    originals.update({str(base/'report.json'):sha(base/'report.json'),str(prior/'report.json'):sha(prior/'report.json'),str(terminal):sha(terminal)})
    for row in ROWS[:12]:
        subject=base/row['id']/'subject';saved=custody['rows'][row['id']]
        require(subject.exists()==saved['exists'] and complete_snapshot(subject)==saved['files'],'tail01-postterminal-subject-drift')
    require(not (base/'python-wizard-typed/subject').exists() and not (base/'python-wizard-default').exists(),'held-python-target-boundary')
    for key,identity in PACKAGES.items():
        if key!='wizard':require(sha(base/'feed'/(identity[0]+'.'+identity[1]+'.nupkg'))==identity[3],'retained-feed-package-drift')
    expected='<configuration><packageSources><clear/><add key="sealed-public-originals" value="'+str(base/'feed')+'"/></packageSources></configuration>'
    require((base/'NuGet.Config').read_text()==expected,'retained-feed-config-drift')
    before_sdk=sdk_snapshot(m);check_clock();tail.mkdir(mode=0o700)
    runner=MatrixRunner(tail,env_for(m,base),deadline=deadline-5);runner.sequence=414
    report=dict(schema=('fsgg.wizard.public-knowledge-matrix-supported-tail-recovery-result/3' if recovery else 'fsgg.wizard.public-knowledge-matrix-supported-tail-result/2'),state='started',manifestSha256=checkpoint['manifestSha256'],
                originalFailureReportSha256=sha(base/'report.json'),tail01FailureReportSha256=sha(prior/'report.json'),
                tail01CustodySha256=TAIL01_CUSTODY_SHA,originalDeadlineUtc=ORIGINAL_DEADLINE_UTC,full24Accepted=False,
                runtimeOperation=operation,originalWindowExpired=recovery,newRecoveryOperation=recovery,
                scope=[row['id'] for row in ROWS[13:]],
                recoveryDeadlineUtc=checkpoint.get('recoveryDeadlineUtc'),profileOuterMaximumSeconds=600,profileCommandAndCaseMaximumSeconds=180,
                elevenAcceptedSubjectsReused=True,wizardCapabilities=capabilities,rows=old['rows'][:11],automaticRetry=False)
    def save():(tail/'report.json').write_text(json.dumps(report,indent=2)+'\n')
    save()
    try:
        for entry in old['rows'][:11]:
            expected=entry['boundary']['baselineCommit'] if 'boundary' in entry else entry['knowledge']['probe']['updatedCommit']
            require(runner.git(base/entry['id']/'subject','rev-parse','HEAD')==expected,'eleven-prefix-reported-git-head')
        donor=app_donor(runner,m)
        execute_rows(runner,m,base,report,save,donor,base/'console-provider-default/subject',start_index=11,original_python_refusal=True)
        state=matrix_state(report['rows'])
        require(all(sha(p)==h for p,h in originals.items()),'original-and-tail01-output-overwrite')
        for row in ROWS[:11]:require(complete_snapshot(base/row['id']/'subject')==custody['rows'][row['id']]['files'],'eleven-accepted-prefix-modified')
        require(validate(checkpoint['manifest'],checkpoint['manifestSha256'],after=True)==m and sdk_snapshot(m)==before_sdk,'continuation02-post-input-drift')
        check_clock()
        report.update(state=state,passed=22,blocked=2,full24Accepted=False,commands=runner.sequence,tailCommands=runner.sequence-414);save();return report
    except BaseException as error:
        if report['rows'][-1]['state']=='started':report['rows'][-1].update(state='failed',failureType=type(error).__name__,failure=str(error))
        report.update(state='failed',failureType=type(error).__name__,failure=str(error),commands=runner.sequence);save();raise


def execute_rows(runner, m, base, report, save, original_app, donor_default=None, start_index=0, raw_resume=False, original_python_refusal=False):
    config = base / 'NuGet.Config'
    capabilities = wizard_capabilities(m)
    for row in ROWS[start_index:]:
        held = held_python(row, capabilities, original_python_refusal)
        if held is not None:
            report['rows'].append(held);save();continue
        case = base / row['id']
        if not (raw_resume and row['id'] == 'console-raw-typed'):
            case.mkdir(); (case / 'tmp').mkdir()
        root = case / 'subject'
        entry = dict(id=row['id'], route=row['route'], selection=row['selection'], state='started', firstCommand=runner.sequence + 1)
        if raw_resume and row['id'] == 'console-raw-typed': entry['recoveredOriginalFailedRow'] = True
        report['rows'].append(entry); save()
        with runner.subject(case, env_for(m, case)):
            if row['route'] == 'accepted-clone':
                runner.call(['git', 'clone', '--no-local', original_app, root])
                entry['initial'] = inherited_initial(runner, m, root)
                entry['knowledge'] = full_knowledge(runner, m, root, row, case, config)
            elif row['route'] == 'retained':
                require(donor_default is not None, 'retained-default-supplier-not-accepted')
                entry['retained'] = retained(runner, m, root, case, donor_default, config, row)
            else:
                if raw_resume and row['id'] == 'console-raw-typed':
                    entry['rawCaller'] = resume_raw(runner, m, root, row, runner.base)
                else:
                    create(runner, m, row, root, case)
                if row['selection'] == 'default':
                    entry['boundary'] = default_boundary(runner, root, row)
                    if row['id'] == 'console-provider-default': donor_default = root
                else:
                    if row['route'] == 'raw' and not (raw_resume and row['id'] == 'console-raw-typed'): entry['rawCaller'] = raw_initialize(runner, m, root, row, config)
                    key = 'overlay' if row['family'] == 'rendering' else 'workspace'
                    prefix = 'content/templates/fs-gg-project-knowledge/' if key == 'overlay' else 'content/templates/fs-gg-' + row['family'] + '/'
                    entry['initial'] = initial(runner, root, row, m['packages'][key]['archive'], prefix)
                    entry['knowledge'] = full_knowledge(runner, m, root, row, case, config)
            if 'knowledge' in entry:
                original = entry['initial'].get('commit', entry['initial'].get('originalCallerCommit'))
                require(entry['knowledge']['probe']['probeSetupCommit'] != original, 'probe-commit-relabeled-as-caller-initial')
            entry.update(state='passed', lastCommand=runner.sequence)
            save()



def continue_retained_post_apply(path, digest):
    """Resume only unfinished effects of command900, inside its unchanged recovery clock."""
    require(sha(path) == digest, 'retained-checkpoint-digest')
    checkpoint = C.read_json(path)
    require(set(checkpoint) == {'schema','admittedBy','operation','manifest','manifestSha256','originalRecoveryCheckpoint','originalRecoveryCheckpointSha256','custody','custodySha256'}
            and checkpoint['schema'] == 'fsgg.matrix4723.retained-post-apply/4'
            and checkpoint['admittedBy'] == 'Programme/root'
            and checkpoint['operation'] == 'matrix4723-supported-tail-recovery20261020-01', 'closed-retained-post-apply-authority')
    original = Path(checkpoint['originalRecoveryCheckpoint'])
    require(sha(original) == checkpoint['originalRecoveryCheckpointSha256'], 'original-recovery-clock-custody')
    clock = C.read_json(original)
    deadline = recovery_deadline(clock)  #No new clock or profile, including validation time.
    require(sha(checkpoint['custody']) == checkpoint['custodySha256'], 'retained-postterminal-custody')
    custody = C.read_json(checkpoint['custody'])
    require(custody['schema'] == 'fsgg.matrix4723.recovery-postterminal-custody/1'
            and custody['operation'] == checkpoint['operation'] and custody['lastCommand'] == 900
            and custody['passed'] == 21 and custody['blocked'] == 2 and custody['failed'] == 1
            and custody['originalDeadlineUtc'] == clock['recoveryDeadlineUtc'], 'exact-post-apply-prefix')
    m = validate(checkpoint['manifest'], checkpoint['manifestSha256'], after=True)
    base = Path(m['workRoot']); prior = base/'supported-tail-recovery01'; tail = base/'retained-post-apply01'
    require(not tail.exists() and str(prior/'report.json') == custody['report']
            and sha(prior/'report.json') == custody['reportSha256'], 'retained-no-replay-or-report-drift')
    old = C.read_json(prior/'report.json')
    require([r['id'] for r in old['rows']] == [r['id'] for r in ROWS]
            and old['state'] == 'failed' and old['commands'] == 900
            and old['failure'] == 'migration-unexpected-output'
            and old['rows'][-1]['state'] == 'failed'
            and all(r['state'] == ('blocked' if r['id'] in PYTHON_WIZARD_HOLDS else 'passed') for r in old['rows'][:-1]), 'exact-retained-failure-and21-passes')
    originals = dict(custody['originals'])
    originals.update(verify_output_journal(base,1,88))
    originals.update(verify_output_journal(base/'continuation01',89,414))
    originals.update(verify_output_journal(prior,415,900))
    for file, expected in originals.items():require(sha(file) == expected, 'retained-original-output-drift')
    for row in ROWS:
        case = base/row['id']; saved = custody['rows'][row['id']]
        require((case/'subject').exists() == saved['exists'] and complete_snapshot(case) == saved['caseFiles'], 'all21-and-retained-subject-custody')
    root = base/ROWS[-1]['id']/'subject'; case = root.parent
    terminal = [json.loads(line) for line in (prior/'commands.jsonl').read_text().splitlines() if json.loads(line).get('phase') == 'terminal'][-1]
    require(terminal['sequence'] == 900 and terminal['actualExitCode'] == 0
            and terminal['argv'] == list(map(str,migration_command(m,root,accept=True)))
            and terminal['cwd'] == str(root), 'actual900-applied-command')
    preview = C.read_json(prior/'897.stdout'); applied = C.read_json(prior/'900.stdout')
    baseline = (prior/'896.stdout').read_text().strip()
    require((prior/'898.stdout').read_text().strip() == '10.0.401', 'recorded-retained-sdk')
    observations = {'before':dict(sdk='10.0.401',observation=C.read_json(prior/'899.stdout'),
        cwdGlobalSha256=sha(root/'global.json'),internalMigrationChildLoadedPaths='not independently observed; this is an actual same-cwd/environment FSI observation')}
    before_sdk = sdk_snapshot(m); recovery_deadline(clock); tail.mkdir(mode=0o700)
    runner = MatrixRunner(tail,env_for(m,base),deadline=deadline-5); runner.sequence = 900
    report = dict(schema='fsgg.wizard.retained-post-apply-result/4',state='started',runtimeOperation=checkpoint['operation'],
                  originalFailureReportSha256=custody['reportSha256'],custodySha256=checkpoint['custodySha256'],
                  manifestSha256=checkpoint['manifestSha256'],recoveryDeadlineUtc=clock['recoveryDeadlineUtc'],
                  full24Accepted=False,automaticRetry=False,rows=[dict(r) for r in old['rows']])
    report['rows'][-1] = dict(id=ROWS[-1]['id'],route='retained',selection='explicit-fsharp',state='started',firstCommand=901,
        originalFailedApplyReused=True,receiverCreationReplayed=False,migrationReplayed=False)
    def save():(tail/'report.json').write_text(json.dumps(report,indent=2)+'\n')
    save()
    try:
        with runner.subject(root,env_for(m,case)):
            require(runner.git(root,'rev-parse','HEAD') == baseline,'retained-baseline-head')
            names = runner.git(root,'ls-tree','-r','--name-only',baseline).splitlines()
            require({'docs/retained-owner-note.md','.fsgg/knowledge-guide.md','work/demo/spec.md'} <= set(names),'retained-setup-must-be-committed-baseline')
            for name in ('docs/retained-owner-note.md','.fsgg/knowledge-guide.md'):
                recorded = runner.run(['git','-C',root,'show',baseline+':'+name],text=False,check=True).stdout
                require(recorded == (root/name).read_bytes(),'retained-committed-owner-setup-drift')
            migration_outputs(dict.fromkeys(names),snapshot(root),root)
            result = retained_post_apply(runner,m,root,case,base/'NuGet.Config',ROWS[-1],baseline,preview,applied,observations)
        report['rows'][-1].update(state='passed',retained=result,lastCommand=runner.sequence)
        require(matrix_state(report['rows']) == 'partial','retained-partial22-held2')
        for row in ROWS[:-1]:require(complete_snapshot(base/row['id']) == custody['rows'][row['id']]['caseFiles'],'21-passed-subjects-modified')
        require(all(sha(file) == value for file,value in originals.items()),'retained-originals-overwritten')
        require(validate(checkpoint['manifest'],checkpoint['manifestSha256'],after=True) == m and sdk_snapshot(m) == before_sdk,'retained-post-custody-drift')
        recovery_deadline(clock)
        report.update(state='partial',passed=22,blocked=2,commands=runner.sequence);save();return report
    except BaseException as error:
        report['rows'][-1].update(state='failed',failureType=type(error).__name__,failure=str(error))
        report.update(state='failed',failureType=type(error).__name__,failure=str(error),commands=runner.sequence);save();raise


def qualify(path, expected_sha):
    started = time.monotonic(); whole_deadline = started + 600
    m = validate(path, expected_sha)
    before_sdk = sdk_snapshot(m)
    require(time.monotonic() < whole_deadline - 9, 'setup-exhausted-original-window')
    base = Path(m['workRoot']); base.mkdir(mode=0o700)
    (base / 'tmp').mkdir()
    feed = base / 'feed'; feed.mkdir()
    for key, identity in PACKAGES.items():
        if key != 'wizard': shutil.copyfile(m['packages'][key]['archive'], feed / (identity[0] + '.' + identity[1] + '.nupkg'))
    require(not any(c in str(feed) for c in '<>&"'), 'unsafe-local-feed-path')
    config = base / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="sealed-public-originals" value="' + str(feed) + '"/></packageSources></configuration>')
    runner = MatrixRunner(base, env_for(m, base), deadline=whole_deadline - 5)
    report = dict(schema='fsgg.wizard.public-knowledge-matrix-result/1', state='started', manifestSha256=expected_sha,
                  outerSeconds=600, caseMaxSeconds=180, rows=[], deferred=['fable-bindings-wizard-typed', 'fable-bindings-wizard-default'], sdkBefore=before_sdk, fableRawToNormalizedExtraction=fable_archive(m['packages']['fable']['archive'])[1])
    def save(): Path(m['report']).write_text(json.dumps(report, indent=2) + '\n')
    save()
    donor_default = None
    try:
        require(runner.call([m['sddExecutable'], '--version']).stdout.strip() == '2.2.0', 'actual-public-producer-version')
        original_app = app_donor(runner, m)
        execute_rows(runner, m, base, report, save, original_app)
        require(len(report['rows']) == 24 and all(r['state'] == 'passed' for r in report['rows']), 'incomplete-matrix')
        app_donor(runner, m)
        require(validate(path, expected_sha, after=True) == m and sdk_snapshot(m) == before_sdk, 'post-custody-drift')
        require(time.monotonic() < whole_deadline, 'overall-deadline-during-postcheck')
        report.update(state='passed', elapsedSeconds=time.monotonic() - started, commands=runner.sequence)
        save(); return report
    except BaseException as error:
        if report['rows'] and report['rows'][-1]['state'] == 'started':
            report['rows'][-1].update(state='failed', failureType=type(error).__name__, failure=str(error))
        report.update(state='failed', failureType=type(error).__name__, failure=str(error), commands=runner.sequence)
        save(); raise


def source_controls():
    """Synthetic source controls only. No SDK/Git/producer/FSI/network process is launched."""
    import tempfile

    class Controls(unittest.TestCase):
        def test_exact_empty_authority_lock_and_foreign_output_refusal(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);lock=root/AUTHORITY_LOCK;lock.parent.mkdir(parents=True);lock.write_bytes(b'')
                migration_outputs({},snapshot(root),root)
                (root/'foreign').write_text('unadmitted')
                with self.assertRaisesRegex(ValueError,'unexpected-output'):migration_outputs({},snapshot(root),root)
                (root/'foreign').unlink();lock.write_bytes(b'not empty')
                with self.assertRaisesRegex(ValueError,'lock-not-empty'):migration_outputs({},snapshot(root),root)

        def test_continuation_baseline_names_include_verified_owner_setup(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td)
                setup={'docs/retained-owner-note.md':'owner finding','.fsgg/knowledge-guide.md':'owner guidance',
                       'work/demo/spec.md':'Standard source','src/Owner.fs':'owner product'}
                for name,value in setup.items():p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(value)
                baseline_names=list(snapshot(root))
                (root/'work/demo/spec.md').write_text('typed projection')
                for name in (AUTHORITY_LOCK,'work/demo/spec.standard-sdd.rollback.md','work/demo/specification.fsx',
                             'readiness/demo/specification.normalized.json','readiness/demo/typed-authority.json',
                             '.fsgg/knowledge/schema.json','.fsgg/knowledge/records/project-knowledge.json'):
                    p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('' if name==AUTHORITY_LOCK else 'maintained output')
                migration_outputs(dict.fromkeys(baseline_names),snapshot(root),root)
                self.assertTrue(set(setup)<=set(baseline_names))
                (root/'foreign').write_text('not an admitted output')
                with self.assertRaisesRegex(ValueError,'unexpected-output'):migration_outputs(dict.fromkeys(baseline_names),snapshot(root),root)

        def test_post_apply_port_only_executes_remaining_rollback_not_creation_or_migration(self):
            with tempfile.TemporaryDirectory() as td:
                case=Path(td);root=case/'subject';root.mkdir();standard=case/'standard.md';standard.write_text('retained standard')
                files={'work/demo/spec.md':'typed projection','work/demo/spec.standard-sdd.rollback.md':'retained standard',
                       'work/demo/specification.fsx':'canonical','readiness/demo/specification.normalized.json':'normalized',
                       'readiness/demo/typed-authority.json':'authority','.fsgg/knowledge/records/project-knowledge.json':'{}',
                       AUTHORITY_LOCK:'','src/Owner.fs':'original owner'}
                for name,value in files.items():p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(value)
                def rollback(argv,**kwargs):
                    self.assertIn('rollback',argv);self.assertNotIn('migrate',argv);self.assertNotIn('clone',argv)
                    (root/'work/demo/spec.md').write_bytes(standard.read_bytes())
                    for name in ('work/demo/specification.fsx','readiness/demo/specification.normalized.json','readiness/demo/typed-authority.json'):(root/name).unlink()
                    return types.SimpleNamespace(stdout=json.dumps({'outcome':'succeeded'}))
                runner=Mock();runner.call.side_effect=rollback;runner.knowledge.return_value={'history':'same'}
                observation={'observed':'recorded original899'}
                with patch.dict(globals(),supported_spec=lambda *a:standard,ci_role=lambda *a:{},commit=Mock(side_effect=['migration','rollback']),
                                full_knowledge=lambda *a:{'probe':{'probeSetupCommit':'distinct'}},fsi_observation=lambda *a:observation):
                    result=retained_post_apply(runner,{'sddExecutable':'/sdd'},root,case,None,ROWS[-1],'original-baseline',
                        {'outcome':'noChange'},{'outcome':'succeeded','classification':'Migrated'},{'before':observation})
                self.assertEqual(runner.call.call_count,1);self.assertEqual(result['baselineCommit'],'original-baseline')
                self.assertEqual((root/'src/Owner.fs').read_text(),'original owner');self.assertEqual((root/AUTHORITY_LOCK).read_bytes(),b'')

        def test_retained_continuation_bad_custody_cannot_launch_or_reset(self):
            with tempfile.TemporaryDirectory() as td:
                path=Path(td)/'checkpoint';path.write_text('{}')
                with patch('subprocess.Popen',side_effect=AssertionError('child launched')):
                    with self.assertRaisesRegex(ValueError,'retained-checkpoint-digest'):continue_retained_post_apply(path,'0'*64)

        def test_exact_population_and_deferred_routes(self):
            self.assertEqual(len(ROWS), 24)
            self.assertEqual(sum(r['selection'] == 'typed' for r in ROWS), 12)
            self.assertEqual(sum(r['selection'] == 'default' for r in ROWS), 11)
            self.assertFalse(any(r['family'] == 'fable-game' or (r['family'] == 'fable-bindings' and r['route'] == 'wizard') for r in ROWS))
            value = dict(rows=[dict(id=r['id']) for r in ROWS], counts={'typed': 12, 'default': 11, 'retained': 1}, deferredSourceGaps=[dict(id='fable-bindings-wizard-'+s, execute=False) for s in ('typed','default')])
            assert_rows(value)
            value['rows'][-1] = value['rows'][0]
            with self.assertRaisesRegex(ValueError, 'population'): assert_rows(value)

        def test_route_dispatch_cannot_take_proposal_argv_or_wrong_lifecycle(self):
            m = dict(wizard='/wizard', sddExecutable='/sdd', dotnetExecutable='/dotnet')
            for row in ROWS:
                if row['route'] not in ('wizard','provider','raw'): continue
                argv = [str(a) for a in command(m, row, Path('/fresh'))]
                self.assertEqual('typed-sdd' in argv or 'lifecycle=typed-sdd' in argv, row['selection'] == 'typed')
                self.assertNotIn('--force', argv); self.assertNotIn('--npm-package', argv)
                if row['route'] == 'provider': self.assertIn('--no-update', argv)
                if row['family'] != 'rendering': self.assertNotIn('--profile', argv)
            with self.assertRaisesRegex(ValueError, 'unknown-row'):
                command(m, dict(id='new-row', family='console', route='raw', selection='typed'), Path('/fresh'))

        def test_legitimate_tool_sets_are_route_specific(self):
            def row(family, route, selection='typed'): return dict(family=family, route=route, selection=selection)
            self.assertEqual(pins(row('console','raw')), {'fs.gg.sdd.cli':'2.2.0'})
            self.assertEqual(pins(row('fable-bindings','raw')), {'fs.gg.sdd.cli':'2.2.0','fable':'5.18.0','fantomas':'8.0.5'})
            self.assertEqual(pins(row('fable-bindings','provider')), dict(C.TOOL_PINS, fable='5.18.0',fantomas='8.0.5'))
            self.assertEqual(pins(row('fable-bindings','raw','default')), {'fable':'5.18.0','fantomas':'8.0.5'})

        def test_case_clock_restores_original_not_new_budget(self):
            with tempfile.TemporaryDirectory() as td:
                runner = MatrixRunner(td, {}, deadline=500)
                with patch('time.monotonic', return_value=400):
                    with runner.subject(Path(td), {'PATH':'/bound'}): self.assertEqual(runner.deadline, 500)
                self.assertEqual(runner.deadline, 500)
                with patch('time.monotonic', return_value=100):
                    with runner.subject(Path(td), {}): self.assertEqual(runner.deadline, 280)
                self.assertEqual(runner.deadline, 500)

        def test_manifest_missing_or_unsealed_source_refuses_before_launch(self):
            with tempfile.TemporaryDirectory() as td:
                p = Path(td)/'manifest';p.write_text('{}')
                with patch('subprocess.Popen', side_effect=AssertionError('child launched')):
                    with self.assertRaisesRegex(ValueError,'manifest-digest'): qualify(p,'0'*64)
                    with self.assertRaisesRegex(ValueError,'closed-matrix-manifest'): qualify(p,sha(p))

        def test_retained_commands_are_explicit_fsharp_and_no_generic_scaffold(self):
            m={'sddExecutable':'/sdd'};root=Path('/retained')
            self.assertIn('fsharp-specification-v1', migration_command(m,root))
            self.assertNotIn('--accept', migration_command(m,root))
            self.assertIn('--accept', migration_command(m,root,accept=True))
            self.assertNotIn('--backend', migration_command(m,root,operation='rollback',accept=True))

        def test_generated_manifest_refuses_extra_or_wrong_dependency_before_restore(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td); (root/'.config').mkdir()
                row=next(r for r in ROWS if r['id']=='console-provider-typed')
                value={'version':1,'isRoot':True,'tools':{k:{'version':v,'commands':['fsgg-sdd']} for k,v in pins(row).items()}}
                path=root/'.config/dotnet-tools.json';path.write_text(json.dumps(value))
                manifest(root,pins(row))
                runner=Mock()
                value['tools']['unsealed']={'version':'1'};path.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError,'membership'): restore_tools(runner,{},root,row,None)
                runner.call.assert_not_called()
                del value['tools']['unsealed'];value['tools']['fs.gg.sdd.cli']['version']='2.1.0';path.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError,'route-tool-pin'): restore_tools(runner,{},root,row,None)
                runner.call.assert_not_called()

        def test_full_capture_orders_private_supplement_bundle_and_budget(self):
            events=[];runner=Mock();m={'sourceRoot':'sealed','sddExecutable':'producer'}
            candidate=Mock();candidate.probe.side_effect=lambda *a: events.append('probe-private-capture') or {'initialCommit':'setup','findingsCommit':'findings'}
            public=Mock();public.ordinary_ci_limits.side_effect=lambda *a: events.append('ordinary-exact-boundary') or {}
            @contextmanager
            def probes(*args): yield candidate,public
            with patch.object(C,'frozen_probes',probes), patch.object(C,'checked',side_effect=lambda *a:events.append('ordinary-check')), patch.object(C,'post_private_export',side_effect=lambda *a:events.append('post-private-all-shared')), patch.dict(globals(),restore_tools=lambda *a:events.append('restore-actual-pins'),bundle_recovery=lambda *a:events.append('bundle-history')):
                result=full_knowledge(runner,m,Path('/subject'),ROWS[0],Path('/case'),None)
            self.assertEqual(events,['restore-actual-pins','ordinary-check','probe-private-capture','post-private-all-shared','bundle-history','ordinary-exact-boundary','ordinary-check'])
            self.assertEqual(result['probe']['probeSetupCommit'],'setup');self.assertNotIn('initialCommit',result['probe'])

        def test_donor_preimage_mismatch_refuses_before_clone(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);(root/'owner').write_text('retained')
                runner=Mock();runner.git.side_effect=[ACCEPTED_APP_HEAD,'tree']
                m={'acceptedApp':{'root':str(root),'head':ACCEPTED_APP_HEAD,'tree':'tree','files':{}}}
                with self.assertRaisesRegex(ValueError,'owner-preimage'):app_donor(runner,m)
                runner.call.assert_not_called()

        def test_fsi_observation_uses_actual_clone_cwd_and_loaded_path_custody(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);(root/'global.json').write_text('owned sdk')
                files={name:root/name for name in ('core','fsharp','artifacts')}
                for name,p in files.items():p.write_text(name)
                pre={'retainedConsoleGlobalJson':{'sha256':sha(root/'global.json')},'publicSddArtifacts':{'path':str(files['artifacts']),'sha256':sha(files['artifacts'])}}
                closure={'files':[{'path':str(p),'sha256':sha(p)} for p in files.values()]}
                observed={'framework':'observed framework','coreLib':str(files['core']),'fsharpCore':str(files['fsharp']),'artifacts':str(files['artifacts'])}
                runner=Mock();runner.call.side_effect=[types.SimpleNamespace(stdout='10.0.401\n'),types.SimpleNamespace(stdout=json.dumps(observed))]
                m={'fsiPreflight':'pre','fsiClosure':'closure','dotnetExecutable':'/bound/dotnet'}
                with patch.object(C,'read_json',side_effect=lambda p:pre if p=='pre' else closure):
                    result=fsi_observation(runner,m,root,root,'before')
                    self.assertEqual(result['observation']['framework'],'observed framework')
                    self.assertTrue(all(c.kwargs['cwd']==root for c in runner.call.call_args_list))
                    self.assertEqual(runner.call.call_args_list[1].kwargs['timeout'],30)
                    observed['coreLib']='/unsealed/framework';runner.call.side_effect=[types.SimpleNamespace(stdout='10.0.401'),types.SimpleNamespace(stdout=json.dumps(observed))]
                    with self.assertRaisesRegex(ValueError,'loaded-content'):fsi_observation(runner,m,root,root,'after')

        def test_ci_role_exact_two_assets_and_owner_collision(self):
            with tempfile.TemporaryDirectory() as td:
                case=Path(td);root=case/'subject';root.mkdir();(root/'owner').write_text('owner bytes')
                archive=case/'role.zip'
                with zipfile.ZipFile(archive,'w') as z:
                    for name in CI_FILES:z.writestr('content/templates/fs-gg-project-knowledge/'+name,'owned '+name)
                m={'dotnetExecutable':'/dotnet','packages':{'workspace':{'archive':str(archive)}}}
                def call(argv,**kwargs):
                    if argv[1:3]==['new','fs-gg-project-knowledge']:
                        target=Path(argv[-1])
                        if target==root:
                            for name in CI_FILES:
                                p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('owned '+name)
                        return types.SimpleNamespace(returncode=0 if target==root else 73)
                    return types.SimpleNamespace(returncode=0)
                runner=Mock();runner.call.side_effect=call
                result=ci_role(runner,m,root,case)
                self.assertEqual(result['ownerCollisionExit'],73)
                self.assertTrue(all('--force' not in c.args[0] for c in runner.call.call_args_list))
                self.assertEqual((root/'owner').read_text(),'owner bytes')

        def test_failed_first_creation_persists_partial_report_and_never_replays(self):
            with tempfile.TemporaryDirectory() as td:
                base=Path(td)/'fresh';m={'sddExecutable':'/bound/producer','workRoot':str(base),'report':str(base/'report.json'),'packages':{k:{'archive':'sealed'} for k in PACKAGES},'actualHome':'/home','childEnvironment':{}}
                events=[]
                def invalid_create(*args):events.append('first-creation');raise ValueError('original-first-failure')
                with patch.dict(globals(),validate=lambda *a,**k:m,sdk_snapshot=lambda *a:{},app_donor=lambda *a:Path('/donor'),create=invalid_create,fable_archive=lambda *a:({},{}),wizard_capabilities=lambda *a:['rendering','console','web','fable-game','fable-bindings']), patch.object(MatrixRunner,'call',return_value=types.SimpleNamespace(stdout='2.2.0')), patch('shutil.copyfile'), patch('subprocess.Popen',side_effect=AssertionError('native child')):
                    with self.assertRaisesRegex(ValueError,'original-first-failure'):qualify('sealed','digest')
                report=json.loads((base/'report.json').read_text())
                self.assertEqual(report['state'],'failed');self.assertEqual(len(report['rows']),1)
                self.assertEqual(report['rows'][0]['state'],'failed');self.assertEqual(events,['first-creation'])
                self.assertEqual(report['commands'],0)

        def test_transitive_command_port_refuses_observed_surviving_group(self):
            with tempfile.TemporaryDirectory() as td:
                runner=MatrixRunner(td,{},deadline=time.monotonic()+50)
                process=Mock(pid=701,returncode=0);process.communicate.return_value=(b'original',b'')
                with patch.object(C.subprocess,'Popen',return_value=process),patch.object(C.os,'killpg',return_value=None):
                    with self.assertRaisesRegex(ValueError,'group-cleanup-incomplete'):runner.run(['git','clone','donor','target'])
                journal=[json.loads(line) for line in (Path(td)/'commands.jsonl').read_text().splitlines()]
                self.assertFalse(journal[-1]['groupObservedAbsent']);self.assertFalse(journal[-1]['cleanupObservedAbsentAfterRequest'])
                self.assertEqual((Path(td)/'001.stdout').read_bytes(),b'original')

        def test_closed_tuple_rejects_missing_supplier_before_any_input_read(self):
            with tempfile.TemporaryDirectory() as td:
                p=Path(td)/'manifest';value={key:None for key in TOP_KEYS}
                value.update(schema='fsgg.wizard.public-knowledge-matrix/1',packages={})
                p.write_text(json.dumps(value))
                with patch('subprocess.Popen',side_effect=AssertionError('native child')):
                    with self.assertRaisesRegex(ValueError,'closed-public-package-tuple'):validate(p,sha(p))

        def test_literal_public_archive_cannot_be_replaced_by_hash_updated_source(self):
            with tempfile.TemporaryDirectory() as td:
                p=Path(td)/'substitute.nupkg';p.write_bytes(b'source substitute')
                with self.assertRaisesRegex(ValueError,'literal-public-package'):
                    C.package({'archive':str(p),'publicationReceipts':[]},{str(p):sha(p)},PACKAGES['producer'])

        def test_payload_closure_rejects_extra_file_even_when_each_file_is_sealed(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);(root/'producer.dll').write_bytes(b'payload');(root/'extra.dll').write_bytes(b'extra')
                inputs={str(p):sha(p) for p in root.iterdir()}
                with self.assertRaisesRegex(ValueError,'exclusive-tool-closure'):
                    C.tool_closure({'closure':str(root)},root/'producer.dll',inputs,{'tools/net10.0/any/producer.dll':sha(root/'producer.dll')},'producer.dll')

        def test_private_input_bytes_and_links_cannot_be_resealed_implicitly(self):
            with tempfile.TemporaryDirectory() as td:
                p=Path(td)/'input';p.write_bytes(b'original');inputs={str(p):sha(p)}
                C.regular(p,inputs);p.write_bytes(b'changed')
                with self.assertRaisesRegex(ValueError,'input-drift'):C.regular(p,inputs)
                link=Path(td)/'alias';link.symlink_to(p)
                with self.assertRaisesRegex(ValueError,'unsafe-input'):C.regular(link,{str(link):sha(p)})

        def test_rollback_preserves_all_product_provenance_tool_bytes_and_modes(self):
            old={'sha256':'old','mode':420};new={'sha256':'restored','mode':420}
            before={p:dict(old) for p in ('work/demo/spec.md','work/demo/specification.fsx','readiness/demo/specification.normalized.json','readiness/demo/typed-authority.json','src/Program.fs','.config/dotnet-tools.json','.fsgg/scaffold-provenance.json','.fsgg/knowledge/records/finding.json','work/demo/spec.standard-sdd.rollback.md')}
            after={p:dict(v) for p,v in before.items() if p not in ('work/demo/specification.fsx','readiness/demo/specification.normalized.json','readiness/demo/typed-authority.json')}
            after['work/demo/spec.md']=new;rollback_preservation(before,after)
            for path in ('src/Program.fs','.config/dotnet-tools.json','.fsgg/scaffold-provenance.json'):
                for field,value in (('sha256','changed'),('mode',493)):
                    changed={p:dict(v) for p,v in after.items()};changed[path][field]=value
                    with self.assertRaisesRegex(ValueError,'owner-product-tool-preservation'):rollback_preservation(before,changed)
            after['unexpected-product']={'sha256':'new','mode':420}
            with self.assertRaisesRegex(ValueError,'owner-product-tool-preservation'):rollback_preservation(before,after)

        def test_raw_initialize_preserves_owner_ignore_prefix(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);ignore=root/'.gitignore';original=b'# Owner rule\nproduct-cache/\n';ignore.write_bytes(original)
                (root/'.config').mkdir();(root/'.config/dotnet-tools.json').write_text('{}')
                runner=Mock();m={'dotnetExecutable':'/dotnet','sddExecutable':'/sdd'}
                runner.knowledge.side_effect=lambda *a:ignore.write_bytes(original+b'\n.fsgg/cache/\n')
                with patch.dict(globals(),manifest=lambda *a:None):
                    raw_initialize(runner,m,root,{'family':'console','route':'raw','selection':'typed'},None)
                    ignore.write_bytes(original)
                    runner.knowledge.side_effect=lambda *a:ignore.write_bytes(b'# Replacement owner rules\n.fsgg/cache/\n')
                    with self.assertRaisesRegex(ValueError,'raw-ignore-owner-prefix'):
                        raw_initialize(runner,m,root,{'family':'console','route':'raw','selection':'typed'},None)

        def fable_fixture(self, entries):
            import io
            data=io.BytesIO()
            with zipfile.ZipFile(data,'w') as archive:
                for name,body,mode in entries:
                    entry=zipfile.ZipInfo(name);entry.external_attr=mode << 16
                    archive.writestr(entry,body)
            data.seek(0);return data

        def test_fable_redundant_internal_separator_preserves_original_bytes_and_names(self):
            entries=[('fable-library-js//Array.js',b'original library',stat.S_IFREG|420),('tools/net10.0/any/fable.dll',b'original tool',stat.S_IFREG|493)]
            data=self.fable_fixture(entries);original=data.getvalue()
            with zipfile.ZipFile(data) as archive:
                payload,mapping=fable_members(archive)
                self.assertEqual(archive.namelist(),[e[0] for e in entries])
                self.assertEqual(archive.read(entries[0][0]),entries[0][1])
            self.assertEqual(data.getvalue(),original)
            self.assertEqual(payload[entries[0][0]],hashlib.sha256(entries[0][1]).hexdigest())
            self.assertEqual(mapping[entries[0][0]],'fable-library-js/Array.js')
            self.assertNotIn('fable-library-js/Array.js',payload)

        def test_fable_normalized_and_file_parent_collisions_refuse(self):
            for names in (('a//b','a/b'),('a','a//b'),('a/','a'),('a//b','a//b')):
                with self.subTest(names=names):
                    # Duplicate raw names use a fake roster to avoid ZipFile's warning.
                    entries=[zipfile.ZipInfo(n) for n in names]
                    fake=Mock();fake.infolist.return_value=entries;fake.read.return_value=b'bytes'
                    with self.assertRaisesRegex(ValueError,'collision'):fable_members(fake)

        def test_fable_only_internal_forward_separator_relaxation(self):
            for name in ('/absolute','a/../b','a/./b','a\\b','C:drive','a//','//a','a\x00hidden'):
                entry=zipfile.ZipInfo(name);fake=Mock();fake.infolist.return_value=[entry];fake.read.return_value=b'bytes'
                with self.subTest(name=name),self.assertRaisesRegex(ValueError,'unsafe-fable'):fable_members(fake)

        def test_fable_link_device_and_type_mismatch_refuse(self):
            for mode,name in ((stat.S_IFLNK|511,'link'),(stat.S_IFCHR|420,'device'),(stat.S_IFDIR|493,'not-directory'),(stat.S_IFREG|420,'directory/')):
                entry=zipfile.ZipInfo(name);entry.external_attr=mode<<16
                fake=Mock();fake.infolist.return_value=[entry];fake.read.return_value=b'bytes'
                with self.subTest(mode=mode,name=name),self.assertRaisesRegex(ValueError,'member-type'):fable_members(fake)

        def test_fable_bounds_and_exact_archive_scope_refuse(self):
            entry=zipfile.ZipInfo('tool');entry.file_size=128*1024*1024+1;fake=Mock();fake.infolist.return_value=[entry]
            with self.assertRaisesRegex(ValueError,'archive-bound'):fable_members(fake)
            fake.infolist.return_value=[zipfile.ZipInfo('tool')]*4097
            with self.assertRaisesRegex(ValueError,'archive-bound'):fable_members(fake)
            with tempfile.TemporaryDirectory() as td:
                path=Path(td)/'substitute';path.write_bytes(b'unpublished substitute')
                with self.assertRaisesRegex(ValueError,'literal-public-fable'):fable_archive(path)

        def test_other_packages_keep_strict_archive_port(self):
            sentinel=object()
            with patch.object(C,'package',return_value=sentinel) as strict,patch.dict(globals(),fable_archive=Mock(side_effect=AssertionError('wrong relaxed port'))):
                self.assertIs(public_package('fantomas',{}, {},PACKAGES['fantomas']),sentinel)
                strict.assert_called_once_with({}, {},PACKAGES['fantomas'])

        def test_raw_sdk10_location_is_explicit_and_tool_install_cannot_search_shadow(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);runner=Mock();m={'dotnetExecutable':'/dotnet','sddExecutable':'/sdd'}
                def sdk(argv,**kwargs):
                    if argv[1:3]==['new','tool-manifest']:
                        output=Path(argv[argv.index('--output')+1]) if '--output' in argv else root
                        output.mkdir(exist_ok=True);(output/'dotnet-tools.json').write_text(json.dumps({'version':1,'isRoot':True,'tools':{}}))
                    elif argv[1:3]==['tool','install']:
                        self.assertIn('--tool-manifest',argv)
                        target=Path(argv[argv.index('--tool-manifest')+1]);self.assertEqual(target,root/'.config/dotnet-tools.json')
                        value=json.loads(target.read_text());value['tools']['fs.gg.sdd.cli']={'version':'2.2.0','commands':['fsgg-sdd']};target.write_text(json.dumps(value))
                runner.call.side_effect=sdk
                raw_initialize(runner,m,root,{'family':'console','route':'raw','selection':'typed'},None)
                self.assertFalse((root/'dotnet-tools.json').exists());manifest(root,{'fs.gg.sdd.cli':'2.2.0'})

        def test_retained_root_manifest_relocation_preserves_bytes_mode_and_owner(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);source=root/'dotnet-tools.json'
                payload=json.dumps({'version':1,'isRoot':True,'tools':{'fs.gg.sdd.cli':{'version':'2.2.0','commands':['fsgg-sdd'],'rollForward':False}}}).encode()
                source.write_bytes(payload);source.chmod(0o640);(root/'owner').write_text('untouched')
                relocate_raw_manifest(root)
                self.assertFalse(source.exists());self.assertEqual((root/'.config/dotnet-tools.json').read_bytes(),payload)
                self.assertEqual(stat.S_IMODE((root/'.config/dotnet-tools.json').stat().st_mode),0o640)
                self.assertEqual((root/'owner').read_text(),'untouched')
                with self.assertRaisesRegex(ValueError,'location-conflict'):relocate_raw_manifest(root)

        def test_retained_manifest_conflict_refuses_without_changes(self):
            with tempfile.TemporaryDirectory() as td:
                root=Path(td);(root/'dotnet-tools.json').write_text('{}');(root/'.config').mkdir();(root/'.config/dotnet-tools.json').write_text('owner')
                before=complete_snapshot(root)
                with self.assertRaisesRegex(ValueError,'location-conflict'):relocate_raw_manifest(root)
                self.assertEqual(complete_snapshot(root),before)

        def test_continuation_cannot_renew_deadline_and_checkpoint_precedes_mutation(self):
            with self.assertRaisesRegex(ValueError,'deadline-not-renewed'):
                continuation_deadline({'deadlineUtc':'2099-01-01T00:00:00+00:00','clockObservedUtc':'2026-10-20T06:00:00+00:00'})
            with tempfile.TemporaryDirectory() as td:
                tail=Path(td);root=tail/'subject';root.mkdir();(root/'owner').write_text('retained')
                def move(actual):
                    self.assertTrue((tail/'raw-postinstall-checkpoint.json').exists())
                    recorded=json.loads((tail/'raw-postinstall-checkpoint.json').read_text())
                    self.assertEqual(recorded['files'],complete_snapshot(root));return {}
                runner=Mock()
                with patch.dict(globals(),relocate_raw_manifest=move,raw_knowledge_initialize=lambda *a:{}):
                    result=resume_raw(runner,{},root,{},tail)
                self.assertFalse(result['creationReplayed']);runner.call.assert_not_called()

        def test_exact_published_wizard_capabilities_refuse_invented_python_support(self):
            with tempfile.TemporaryDirectory() as td:
                path=Path(td)/'Program.fs'
                source='let private templates =\n    [\n'+''.join('        "'+name+'", "description"\n' for name in ('rendering','console','web','fable-game','fable-bindings'))+'    ]\nlet knownTemplates = templates |> List.map fst\nif List.contains value knownTemplates then\n'
                path.write_text(source)
                closure={'files':[{'repository':'FS-GG/.github','revision':PACKAGES['wizard'][2],'sourcePath':'scripts/NewSddWorkspace/Program.fs','frozenFile':str(path)}]}
                m={'sourceClosure':'closure','inputs':{str(path):sha(path)}}
                with patch.object(C,'read_json',return_value=closure):
                    self.assertNotIn('python',wizard_capabilities(m))
                    path.write_text(source.replace('"web",','"python",'));m['inputs'][str(path)]=sha(path)
                    with self.assertRaisesRegex(ValueError,'exact-published019'):wizard_capabilities(m)

        def test_python_wizard_holds_do_not_create_or_execute_and_supported_tail_remains(self):
            with tempfile.TemporaryDirectory() as td:
                base=Path(td);report={'rows':[]};runner=MatrixRunner(base,{},deadline=time.monotonic()+60);runner.call=Mock()
                with patch.dict(globals(),wizard_capabilities=lambda *a:['console','web','rendering','fable-game','fable-bindings'],create=Mock(side_effect=ValueError('supported-provider-reached'))):
                    with self.assertRaisesRegex(ValueError,'supported-provider-reached'):
                        execute_rows(runner,{'childEnvironment':{}},base,report,lambda:None,None,start_index=11)
                self.assertEqual([r['state'] for r in report['rows'][:2]],['blocked','blocked'])
                self.assertEqual(report['rows'][2]['id'],'python-provider-typed')
                self.assertFalse((base/'python-wizard-typed').exists());self.assertFalse((base/'python-wizard-default').exists())
                runner.call.assert_not_called()

        def test_22_passed_two_held_is_partial_and_any_supported_failure_refuses(self):
            rows=[dict(id=row['id'],state='blocked' if row['id'] in PYTHON_WIZARD_HOLDS else 'passed') for row in ROWS]
            self.assertEqual(matrix_state(rows),'partial')
            self.assertEqual(sum(r['state']=='passed' for r in rows),22)
            rows[-1]['state']='failed'
            with self.assertRaisesRegex(ValueError,'incomplete-supported-tail'):matrix_state(rows)

        def recovery_fixture(self):
            return dict(operation='matrix4723-supported-tail-recovery20261020-01',originalDeadlineUtc=ORIGINAL_DEADLINE_UTC,originalWindowExpired=True,
                        recoveryClockObservedUtc='2026-10-20T07:00:00+00:00',recoveryClockObservedMonotonic=1000,
                        recoveryDeadlineUtc='2026-10-20T07:10:00+00:00',recoveryDeadlineMonotonic=1600)

        def test_separate_recovery_admits_only_expired_original_with_own_clock(self):
            from datetime import datetime
            value=self.recovery_fixture()
            self.assertEqual(recovery_deadline(value,now_utc=datetime.fromisoformat('2026-10-20T07:00:05+00:00'),now_monotonic=1005),1600)
            value['operation']='matrix4723-runtime20261020-01'
            with self.assertRaisesRegex(ValueError,'separate-supported-recovery-operation'):
                recovery_deadline(value,now_utc=datetime.fromisoformat('2026-10-20T07:00:05+00:00'),now_monotonic=1005)

        def test_recovery_refuses_unexpired_original_despite_expired_claim(self):
            from datetime import datetime
            with self.assertRaisesRegex(ValueError,'must-already-be-expired'):
                recovery_deadline(self.recovery_fixture(),now_utc=datetime.fromisoformat('2026-10-20T06:09:00+00:00'),now_monotonic=1005)
            value=self.recovery_fixture();value['originalDeadlineUtc']='2026-10-20T08:00:00+00:00'
            with self.assertRaisesRegex(ValueError,'must-already-be-expired'):
                recovery_deadline(value,now_utc=datetime.fromisoformat('2026-10-20T07:00:05+00:00'),now_monotonic=1005)

        def test_recovery_profile_cannot_exceed600_or_reset_elapsed_clock(self):
            from datetime import datetime
            value=self.recovery_fixture();value['recoveryDeadlineUtc']='2026-10-20T07:10:01+00:00';value['recoveryDeadlineMonotonic']=1601
            with self.assertRaisesRegex(ValueError,'profile-maximum600'):
                recovery_deadline(value,now_utc=datetime.fromisoformat('2026-10-20T07:00:05+00:00'),now_monotonic=1005)
            value=self.recovery_fixture();value['recoveryDeadlineMonotonic']=1700
            with self.assertRaisesRegex(ValueError,'wall-monotonic-clock-binding'):
                recovery_deadline(value,now_utc=datetime.fromisoformat('2026-10-20T07:00:05+00:00'),now_monotonic=1005)
            value=self.recovery_fixture()
            with self.assertRaisesRegex(ValueError,'expired-or-insufficient'):
                recovery_deadline(value,now_utc=datetime.fromisoformat('2026-10-20T07:10:01+00:00'),now_monotonic=1601)

        def test_held_rows_keep_actual414_invocation_distinct_from_this_recovery(self):
            capabilities=['rendering','console','web','fable-game','fable-bindings']
            typed=held_python(next(r for r in ROWS if r['id']=='python-wizard-typed'),capabilities,True)
            default=held_python(next(r for r in ROWS if r['id']=='python-wizard-default'),capabilities,True)
            self.assertTrue(typed['originalAttemptInvoked']);self.assertEqual(typed['originalRefusalEvidence']['sequence'],414)
            self.assertEqual(typed['originalRefusalEvidence']['actualExitCode'],2)
            self.assertFalse(default['originalAttemptInvoked']);self.assertIsNone(default['originalRefusalEvidence'])
            self.assertFalse(typed['thisContinuationCreationInvoked']);self.assertFalse(default['thisContinuationCreationInvoked'])

        def test_console_source_stays_byte_identical_and_workflow_reaches_controls(self):
            self.assertEqual(sha(Path(__file__).with_name('knowledge-console-public.py')), CONSOLE_SHA)
            workflow=Path(__file__).resolve().parents[2]/'.github/workflows/new-sdd-workspace-selftest.yml'
            calls=[line.strip() for line in workflow.read_text().splitlines() if 'knowledge-public-receivers.py' in line]
            self.assertEqual(calls,['python3 -B tests/new-sdd-workspace/knowledge-public-receivers.py --source-controls'])

    result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Controls))
    require(result.wasSuccessful(),'matrix-source-controls-failed')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-controls',action='store_true')
    parser.add_argument('--continuation',type=Path);parser.add_argument('--continuation-sha256')
    parser.add_argument('--manifest',type=Path);parser.add_argument('--manifest-sha256')
    args=parser.parse_args()
    if args.source_controls: source_controls();return
    require((args.continuation and args.continuation_sha256) or (args.manifest and args.manifest_sha256),'sealed-matrix-input-required')
    import signal
    def cancelled(signum,frame): raise TimeoutError('original-outer-window-cancelled')
    previous=signal.signal(signal.SIGTERM,cancelled)
    try:
        if args.continuation:
            schema=C.read_json(args.continuation).get('schema')
            if schema=='fsgg.matrix4723.retained-post-apply/4': continue_retained_post_apply(args.continuation,args.continuation_sha256)
            elif schema=='fsgg.matrix4723.supported-tail-recovery/3': continue_supported_tail(args.continuation,args.continuation_sha256,recovery=True)
            elif schema=='fsgg.matrix4723.supported-tail/2': continue_supported_tail(args.continuation,args.continuation_sha256)
            else: continue_qualification(args.continuation,args.continuation_sha256)
        else: qualify(args.manifest,args.manifest_sha256)
    finally: signal.signal(signal.SIGTERM,previous)


if __name__=='__main__': main()
