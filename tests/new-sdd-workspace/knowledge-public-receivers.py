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
        members = C.package(binding, m['inputs'], identity)
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


def raw_initialize(runner, m, root, row, config):
    before = snapshot(root)
    ignore = root / '.gitignore'
    ignore_bytes = ignore.read_bytes() if ignore.exists() else None
    require(not (root / '.fsgg/knowledge').exists(), 'raw-flag-impersonated-initializer')
    if not (root / '.config/dotnet-tools.json').exists():
        runner.call([m['dotnetExecutable'], 'new', 'tool-manifest'], cwd=root)
    runner.call([m['dotnetExecutable'], 'tool', 'install', 'FS.GG.SDD.Cli', '--version', '2.2.0', '--local', '--configfile', config], cwd=root)
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


def restore_tools(runner, m, root, row, config):
    observed = manifest(root, pins(row))
    # Every actual generated tool is checked against the already sealed feed before restore.
    available = {identity[0].lower(): identity[1] for identity in PACKAGES.values()}
    require(all(available.get(k) == v['version'] for k, v in observed['tools'].items()), 'unsealed-manifest-dependency')
    runner.call([m['dotnetExecutable'], 'tool', 'restore', '--configfile', config, '--no-cache'], cwd=root)
    package_root = Path(runner.env['NUGET_PACKAGES'])
    for key, identity in PACKAGES.items():
        if identity[0].lower() not in observed['tools']: continue
        for name, digest in C.archive_payload(m['packages'][key]['archive']).items():
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
    allowed_new = {'work/demo/spec.standard-sdd.rollback.md', 'work/demo/specification.fsx', 'readiness/demo/specification.normalized.json',
                   'readiness/demo/typed-authority.json', '.fsgg/knowledge/schema.json', '.fsgg/knowledge/records/project-knowledge.json'}
    require(set(after) - set(before) <= allowed_new, 'migration-unexpected-output')
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
                  outerSeconds=600, caseMaxSeconds=180, rows=[], deferred=['fable-bindings-wizard-typed', 'fable-bindings-wizard-default'], sdkBefore=before_sdk)
    def save(): Path(m['report']).write_text(json.dumps(report, indent=2) + '\n')
    save()
    donor_default = None
    try:
        require(runner.call([m['sddExecutable'], '--version']).stdout.strip() == '2.2.0', 'actual-public-producer-version')
        original_app = app_donor(runner, m)
        for row in ROWS:
            case = base / row['id']; case.mkdir(); (case / 'tmp').mkdir()
            root = case / 'subject'
            entry = dict(id=row['id'], route=row['route'], selection=row['selection'], state='started', firstCommand=runner.sequence + 1)
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
                    create(runner, m, row, root, case)
                    if row['selection'] == 'default':
                        entry['boundary'] = default_boundary(runner, root, row)
                        if row['id'] == 'console-provider-default': donor_default = root
                    else:
                        if row['route'] == 'raw': entry['rawCaller'] = raw_initialize(runner, m, root, row, config)
                        key = 'overlay' if row['family'] == 'rendering' else 'workspace'
                        prefix = 'content/templates/fs-gg-project-knowledge/' if key == 'overlay' else 'content/templates/fs-gg-' + row['family'] + '/'
                        entry['initial'] = initial(runner, root, row, m['packages'][key]['archive'], prefix)
                        entry['knowledge'] = full_knowledge(runner, m, root, row, case, config)
                if 'knowledge' in entry:
                    original = entry['initial'].get('commit', entry['initial'].get('originalCallerCommit'))
                    require(entry['knowledge']['probe']['probeSetupCommit'] != original, 'probe-commit-relabeled-as-caller-initial')
                entry.update(state='passed', lastCommand=runner.sequence)
                save()
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
                with patch.dict(globals(),validate=lambda *a,**k:m,sdk_snapshot=lambda *a:{},app_donor=lambda *a:Path('/donor'),create=invalid_create), patch.object(MatrixRunner,'call',return_value=types.SimpleNamespace(stdout='2.2.0')), patch('shutil.copyfile'), patch('subprocess.Popen',side_effect=AssertionError('native child')):
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
    parser.add_argument('--manifest',type=Path);parser.add_argument('--manifest-sha256')
    args=parser.parse_args()
    if args.source_controls: source_controls();return
    require(args.manifest and args.manifest_sha256,'sealed-matrix-input-required')
    import signal
    def cancelled(signum,frame): raise TimeoutError('original-outer-window-cancelled')
    previous=signal.signal(signal.SIGTERM,cancelled)
    try: qualify(args.manifest,args.manifest_sha256)
    finally: signal.signal(signal.SIGTERM,previous)


if __name__=='__main__': main()
