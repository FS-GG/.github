#!/usr/bin/env python3
"""Selected public console knowledge qualification; --source-controls never launches a child.

Runtime requires separate source/resource admission. Frozen Templates probes retain their bytes;
only their command ports are adapted to this operation's bounded journal. No production behavior
or command output is substituted. A failed operation retains its original receiver and report.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import signal
import stat
import subprocess
import sys
import time
import types
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

REVISION = 'f5fbd06ed9e6df40f9037c7acddf9eca3c6a14fc'
INVENTORY_SHA = '8ceb12eaebd4bc96045791072312bdb96cb1a986bf17c0b1047f2bdb399cb8dd'
SOURCE_HASHES = {
    'providers/console.providers.yml': '3911afe2d54876f735074c2dc4947a59b1ab6e5f367f40bea7231ea0e606329f',
    'tests/knowledge-receiver/candidate.py': 'f7a3d7ba62b5253e059077bd318c069b5cea63cfdcd1504e4f8263de4a01dc77',
    'tests/knowledge-receiver/receiver.py': '9ee12394c906d5669d9d457699bc96452c7e5d9a48f230f5641b2ac49a7b57de',
    'tests/knowledge-receiver/findings.json': '6efe8f0245c1fa0988e38cf2171829e08219aeb8b3b7d7518e85aeb4bf60a375',
    'tests/knowledge-receiver/public21.py': '20ef736fb5bfe57c042c5f8c4a9232e91b0e85bec768be26a244461f476c77e4',
}
# Literal genuinely published inputs. Version text alone establishes none of these identities.
PACKAGES = {
    'wizard': ('FS.GG.NewSddWorkspace', '0.19.0', 'cc1d3c12c1ed7e7128012063d7ab478c7198b523', '3c8150734814c3d5c4416535ff56f91a0570595bcc38cea6586012678a4e92b6'),
    'producer': ('FS.GG.SDD.Cli', '2.2.0', 'cf2f046a10497a336d6243c9a314c3f91fb15771', '46b4b172eac65df00db43f813bd3d3fbafac5c6376da62a6d3c08fb05750cf28'),
    'overlay': ('FS.GG.Workspace.Template', '0.18.2', '20c009bc6728302f9ed06fdf49cb2e8bd19e417d', '496aad0e89496719bc543edc6ffbf829a82307862e630ba4e174e374ac11cc4e'),
    'workspace': ('FS.GG.Workspace.Template', '0.19.0', '8952e1c5753b0abc972154c7596ddc8da772b271', '187c995806bb2f22db67c8a9a67c251f46f339bf6350b8051035935d308b497f'),
    'coord': ('FS.GG.Coord.Cli', '0.94.0', '337b6a1d53571b07ca8e1417e18e52546ad319a7', 'e68a2134242c1e59611a41f98aa5b7d26a545fd6411a0de01af610f4862cc332'),
}
ORIGINAL_WORKSPACE_SHA = 'a30bf66afe7a226ff618bb112ed44f20b502fd5dd5c9999e7c8d29feae10631a'
TOOL_PINS = {'fs.gg.sdd.cli': '2.2.0', 'fs.gg.coord.cli': '0.94.0'}
LIMIT = 10485760
TOP_KEYS = {'schema', 'inputs', 'packages', 'workspaceOriginal', 'inventory', 'sourceRoot', 'wizard',
            'sddExecutable', 'dotnetExecutable', 'actualHome', 'childEnvironment', 'workRoot', 'report'}
ENV_KEYS = {'PATH', 'DOTNET_PROCESSOR_COUNT', 'DOTNET_GCHeapHardLimit', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'MSBUILDDISABLENODEREUSE'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate-json-key:' + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(Path(path).read_text(), object_pairs_hook=unique)


def regular(path, inputs):
    p = Path(path)
    require(p.is_absolute() and p == p.resolve() and p.is_file() and not p.is_symlink(), 'unsafe-input:' + str(p))
    require(str(p) in inputs and sha(p) == inputs[str(p)], 'input-drift:' + str(p))
    return p


def archive_payload(path):
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        require(len(entries) <= 4096 and sum(e.file_size for e in entries) <= 128 * 1024 * 1024, 'archive-bound')
        require(len(entries) == len({e.filename for e in entries}), 'duplicate-archive-member')
        for e in entries:
            name = e.filename
            require(name and not name.startswith('/') and '\\' not in name and ':' not in name
                    and all(p not in ('', '.', '..') for p in name.rstrip('/').split('/'))
                    and not stat.S_ISLNK(e.external_attr >> 16), 'unsafe-archive-member')
        return {e.filename: hashlib.sha256(archive.read(e)).hexdigest() for e in entries
                if not e.is_dir() and e.filename != '.signature.p7s'}


def package(binding, inputs, identity):
    archive = regular(binding['archive'], inputs)
    require(sha(archive) == identity[3], 'literal-public-package')
    require(binding['publicationReceipts'] and isinstance(binding['publicationReceipts'], list), 'publication-custody-required')
    for receipt in binding['publicationReceipts']:
        regular(receipt, inputs)
    members = archive_payload(archive)
    names = [n for n in members if n.endswith('.nuspec')]
    require(len(names) == 1, 'package-metadata')
    with zipfile.ZipFile(archive) as a:
        metadata = ET.fromstring(a.read(names[0]))
    def values(name):
        return [n for n in metadata.iter() if n.tag.split('}')[-1] == name]
    require(all(len(values(n)) == 1 for n in ('id', 'version', 'repository')), 'package-metadata')
    require((values('id')[0].text, values('version')[0].text, values('repository')[0].get('commit')) == identity[:3], 'package-source-identity')
    return members


def tool_closure(binding, executable, inputs, members, entry):
    root = Path(binding['closure'])
    require(root.is_absolute() and root == root.resolve() and root.is_dir(), 'unsafe-tool-closure')
    prefix = 'tools/net10.0/any/'
    expected = {n.removeprefix(prefix): digest for n, digest in members.items() if n.startswith(prefix)}
    actual = {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file() or p.is_symlink()}
    require(entry in expected and actual == set(expected), 'exclusive-tool-closure')
    for name, digest in expected.items():
        require(sha(regular(root / name, inputs)) == digest, 'literal-tool-payload')
    exe = regular(executable, inputs)
    target = os.path.relpath(root / entry, exe.parent)
    raw = exe.read_bytes()
    require(raw.startswith(b'\x7fELF') and b'\x00' + target.encode() + b'\x00' in raw, 'apphost-target')


def validate(manifest_path, expected_sha, after=False):
    require(expected_sha and sha(manifest_path) == expected_sha, 'manifest-digest')
    m = read_json(manifest_path)
    require(set(m) == TOP_KEYS and m['schema'] == 'fsgg.wizard.public-console-knowledge/1', 'closed-manifest')
    require(set(m['packages']) == set(PACKAGES), 'closed-package-tuple')
    for path in m['inputs']:
        regular(path, m['inputs'])
    regular(Path(__file__).resolve(), m['inputs'])
    inventory = regular(m['inventory'], m['inputs'])
    require(sha(inventory) == INVENTORY_SHA, 'original-source-inventory')
    inv = read_json(inventory)
    require(inv['sourceRevision'] == REVISION, 'source-revision')
    source = Path(m['sourceRoot'])
    for relative, digest in SOURCE_HASHES.items():
        p = regular(source / relative, m['inputs'])
        require(sha(p) == digest and inv['inputs'].get(str(p)) == digest, 'frozen-probe-source:' + relative)
    for key, identity in PACKAGES.items():
        binding = m['packages'][key]
        require(set(binding) == ({'archive', 'publicationReceipts', 'closure'} if key in ('wizard', 'producer') else {'archive', 'publicationReceipts'}), 'closed-package-binding')
        members = package(binding, m['inputs'], identity)
        if key in ('wizard', 'producer'):
            tool_closure(binding, m['wizard' if key == 'wizard' else 'sddExecutable'], m['inputs'], members,
                         'new-sdd-workspace.dll' if key == 'wizard' else 'FS.GG.SDD.Cli.dll')
    original = regular(m['workspaceOriginal'], m['inputs'])
    require(sha(original) == ORIGINAL_WORKSPACE_SHA and archive_payload(original) == archive_payload(m['packages']['workspace']['archive']), 'original-public-workspace-payload')
    regular(m['dotnetExecutable'], m['inputs'])
    regular(Path(sys.executable).resolve(), m['inputs'])
    import shutil
    git = shutil.which('git', path=m['childEnvironment'].get('PATH', ''))
    require(git, 'selected-git-missing')
    regular(Path(git).resolve(), m['inputs'])
    require(os.environ.get('HOME') == m['actualHome'], 'actual-home')
    env = m['childEnvironment']
    require(set(env) == ENV_KEYS and env['DOTNET_PROCESSOR_COUNT'] == '1'
            and env['DOTNET_GCHeapHardLimit'] == '0x60000000'
            and env['DOTNET_CLI_TELEMETRY_OPTOUT'] == env['MSBUILDDISABLENODEREUSE'] == '1', 'closed-serial-environment')
    for name, expected in [('dotnet', m['dotnetExecutable']), ('fsgg-sdd', m['sddExecutable'])]:
        import shutil
        selected = shutil.which(name, path=env['PATH'])
        require(selected and Path(selected).resolve() == Path(expected).resolve(), 'selected-executable:' + name)
    root = Path(m['workRoot'])
    require(root.is_absolute() and root == root.resolve() and not root.is_symlink() and Path(m['report']).parent == root, 'output-boundary')
    require(after or not root.exists(), 'original-output-exists-no-replay')
    return m


class Runner:
    """One operation deadline, finite per-command limits, private original output and group cleanup."""
    def __init__(self, base, env, total_seconds=600, deadline=None):
        self.base, self.env = Path(base), env
        self.deadline = time.monotonic() + total_seconds if deadline is None else deadline
        self.sequence = 0

    def run(self, argv, *, cwd=None, capture_output=True, text=True, check=False, timeout=180):
        require(capture_output is True and text in (True, False), 'unsupported-command-port')
        argv = [str(a) for a in argv]
        require(argv and all(isinstance(a, str) and a for a in argv), 'empty-command')
        remaining = self.deadline - time.monotonic()
        require(remaining > 5, 'operation-deadline')
        budget = min(float(timeout), 180, remaining - 4)
        require(budget > 0, 'command-deadline')
        self.sequence += 1
        record = dict(sequence=self.sequence, argv=argv, cwd=str(cwd) if cwd else None, timeoutSeconds=budget,
                      phase='started', stdoutPath=f'{self.sequence:03d}.stdout', stderrPath=f'{self.sequence:03d}.stderr')
        journal = self.base / 'commands.jsonl'
        with journal.open('a') as out:
            out.write(json.dumps(record) + '\n')
        process = None
        stdout, stderr = b'', b''
        try:
            process = subprocess.Popen(argv, cwd=cwd, env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                       start_new_session=True)
            record['pid'] = record['processGroup'] = process.pid
            try:
                stdout, stderr = process.communicate(timeout=budget)
                record['timedOut'] = False
            except subprocess.TimeoutExpired:
                record['timedOut'] = True
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                try:
                    stdout, stderr = process.communicate(timeout=2)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    stdout, stderr = process.communicate(timeout=2)
            record['actualExitCode'] = process.returncode
            # This is an observed group test, not an inference from direct-child exit.
            try:
                os.killpg(process.pid, 0)
                record['groupObservedAbsent'] = False
                os.killpg(process.pid, signal.SIGKILL)
                record['cleanupRequested'] = 'SIGKILL'
                try:
                    os.killpg(process.pid, 0)
                    record['cleanupObservedAbsentAfterRequest'] = False
                except ProcessLookupError:
                    record['cleanupObservedAbsentAfterRequest'] = True
            except ProcessLookupError:
                record['groupObservedAbsent'] = True
            for name, data in [('stdout', stdout), ('stderr', stderr)]:
                (self.base / record[name + 'Path']).write_bytes(data)
                record[name + 'Sha256'] = hashlib.sha256(data).hexdigest()
            record['phase'] = 'terminal'
            require(record['groupObservedAbsent'], 'command-group-cleanup-incomplete')
            require(not record['timedOut'], 'command-timeout')
            result = subprocess.CompletedProcess(argv, process.returncode,
                stdout.decode('utf-8', errors='replace') if text else stdout,
                stderr.decode('utf-8', errors='replace') if text else stderr)
            if check:
                require(result.returncode == 0, 'command-refused:' + str(self.sequence))
            return result
        finally:
            primary = sys.exc_info()[1]
            if primary is not None:
                record['failureType'] = type(primary).__name__
            if process is not None and record['phase'] != 'terminal':
                # Cancellation or an unexpected port failure must not strand the owned group.
                # Preserve the primary exception if cleanup itself refuses or exceeds its cap.
                try:
                    try:
                        os.killpg(process.pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
                    try:
                        stdout, stderr = process.communicate(timeout=2)
                    except subprocess.TimeoutExpired:
                        try:
                            os.killpg(process.pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                        stdout, stderr = process.communicate(timeout=2)
                    record['actualExitCode'] = process.returncode
                    try:
                        os.killpg(process.pid, 0)
                        record['groupObservedAbsent'] = False
                    except ProcessLookupError:
                        record['groupObservedAbsent'] = True
                    for name, data in [('stdout', stdout), ('stderr', stderr)]:
                        (self.base / record[name + 'Path']).write_bytes(data)
                        record[name + 'Sha256'] = hashlib.sha256(data).hexdigest()
                except BaseException as cleanup:
                    record['cleanupFailureType'] = type(cleanup).__name__
            if record['phase'] != 'terminal':
                record['phase'] = 'failed'
            with journal.open('a') as out:
                out.write(json.dumps(record) + '\n')

    def call(self, argv, cwd=None, success=True, timeout=180):
        result = self.run(argv, cwd=cwd, timeout=timeout)
        require((result.returncode == 0) == success, 'unexpected-command-outcome:' + str(self.sequence))
        return result

    def git(self, root, *argv):
        return self.call(['git', '-C', root, *argv]).stdout.strip()

    def knowledge(self, executable, *args, expect_success=True):
        result = self.call([executable, 'knowledge', *args], success=expect_success)
        return (json.loads(result.stdout) if result.stdout.strip() else None) if expect_success else result


@contextmanager
def frozen_probes(source_root, runner):
    """Load exact bound files without bytecode writes; adapt every subprocess command port."""
    old = {name: sys.modules.get(name) for name in ('receiver', 'candidate', 'public21')}
    modules = {}
    try:
        for name in ('receiver', 'candidate', 'public21'):
            path = Path(source_root) / 'tests/knowledge-receiver' / (name + '.py')
            require(sha(path) == SOURCE_HASHES['tests/knowledge-receiver/' + name + '.py'], 'probe-load-drift')
            module = types.ModuleType(name)
            module.__file__ = str(path)
            sys.modules[name] = module
            exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
            module.subprocess = types.SimpleNamespace(run=runner.run)
            modules[name] = module
        yield modules['candidate'], modules['public21']
    finally:
        for name, previous in old.items():
            if previous is None:
                sys.modules.pop(name, None)
            else:
                sys.modules[name] = previous


def wizard_command(m, root):
    return [m['wizard'], str(root), 'ConsoleKnowledge', '--template', 'console', '--lifecycle', 'typed-sdd',
            '--pinned', '--no-governance', '--no-coordination', '--ref', REVISION]


def tool_manifest(root):
    m = read_json(root / '.config/dotnet-tools.json')
    require(type(m['version']) is int and m['version'] == 1 and m['isRoot'] is True and set(m['tools']) == set(TOOL_PINS), 'generated-tool-set')
    for name, version in TOOL_PINS.items():
        require(m['tools'][name]['version'] == version and isinstance(m['tools'][name]['commands'], list), 'generated-tool-pin')
    require('fsgg-sdd' in m['tools']['fs.gg.sdd.cli']['commands'], 'generated-sdd-command')
    return m


def checker_command(root):
    return [sys.executable, str(root / 'scripts/check-project-knowledge.py'), '--root', str(root)]


def checked(runner, root):
    result = json.loads(runner.call(checker_command(root), cwd=root).stdout)
    require(result['sourceCandidateOverride'] is False and result['pinnedVersion'] == '2.2.0'
            and result['limit'] == LIMIT, 'ordinary-pinned-checker')
    return result


def registration(base, expected_version, expected_sha):
    p = base / 'dotnet-home/.templateengine/packages.json'
    metadata = json.loads(p.read_text(encoding='utf-8-sig'))
    rows = [r for r in metadata['Packages'] if r.get('Details', {}).get('PackageId', '').lower() == 'fs.gg.workspace.template']
    require(len(rows) == 1 and rows[0]['Details']['Version'] == expected_version, 'sole-workspace-registration')
    archive = Path(rows[0]['MountPointUri'])
    require(archive.resolve().is_relative_to(base) and sha(archive) == expected_sha, 'registered-public-archive')
    return dict(version=expected_version, archiveSha256=sha(archive))


def initial_commit(runner, root, package_archive):
    require(not runner.git(root, 'rev-list', '--all'), 'background-initial-commit')
    canonical = sorted(p.relative_to(root).as_posix() for p in (root / '.fsgg/knowledge').rglob('*') if p.is_file())
    require('.fsgg/knowledge/schema.json' in canonical and any('/records/' in p for p in canonical), 'missing-canonical-knowledge')
    require((root / '.fsgg/knowledge-guide.md').stat().st_size > 0, 'missing-knowledge-guide')
    with zipfile.ZipFile(package_archive) as archive:
        for relative in ('scripts/check-project-knowledge.py', '.github/workflows/project-knowledge.yml'):
            require((root / relative).read_bytes() == archive.read('content/templates/fs-gg-console/' + relative), 'generated-owner-assets')
    manifest = tool_manifest(root)
    required = canonical + ['.fsgg/knowledge-guide.md', '.config/dotnet-tools.json', 'scripts/check-project-knowledge.py', '.github/workflows/project-knowledge.yml']
    cache = root / '.fsgg/cache/console-fixture.json'
    cache.parent.mkdir(parents=True, exist_ok=True)
    cache.write_text('{}')
    runner.git(root, 'add', '.')
    runner.git(root, '-c', 'user.name=Console receiver', '-c', 'user.email=receiver@example.invalid', 'commit', '-qm', 'Initial console workspace')
    commit = runner.git(root, 'rev-parse', 'HEAD')
    tracked = set(runner.git(root, 'ls-files').splitlines())
    require(set(required) <= tracked and '.fsgg/cache/console-fixture.json' not in tracked, 'caller-initial-inclusion')
    # Bind actual committed blob bytes before the frozen probe mutates anything.
    blobs = {}
    for relative in required:
        blob = runner.run(['git', '-C', root, 'show', commit + ':' + relative], text=False, check=True).stdout
        require(blob == (root / relative).read_bytes(), 'initial-commit-blob')
        blobs[relative] = hashlib.sha256(blob).hexdigest()
    return dict(commit=commit, canonicalFiles=canonical, committedAssets=blobs, toolManifest=manifest)


def post_private_export(runner, executable, root, scratch):
    private = runner.knowledge(executable, 'get', '--store', str(scratch / 'synthetic-private'), '--id', 'synthetic-private')
    require(private['Record']['Id'] == 'synthetic-private', 'private-capture-not-established')
    shared = runner.knowledge(executable, 'search', '--root', str(root))
    ids = sorted(row['Record']['Id'] for row in shared)
    require(ids and 'synthetic-private' not in ids, 'private-record-in-shared-search')
    archive = scratch / 'post-private-all-shared.json'
    runner.knowledge(executable, 'export', '--root', str(root), '--ids', ','.join(ids), '--archive', str(archive))
    exported = read_json(archive)
    require(exported['HistoryIncluded'] is False and 'synthetic-private' not in archive.read_text(), 'post-private-export-leak')
    restored = scratch / 'post-private-all-restored'
    runner.knowledge(executable, 'restore', '--root', str(restored), '--archive', str(archive))
    require(runner.knowledge(executable, 'search', '--root', str(restored)) == shared, 'all-shared-lossless-restore')
    return dict(sharedIds=ids, archiveSha256=sha(archive), privateCapturePrecedesExport=True, allSharedLosslessRestore=True)


def qualify(manifest_path, expected_sha):
    started = time.monotonic()
    operation_deadline = started + 600
    m = validate(manifest_path, expected_sha)
    base = Path(m['workRoot']); base.mkdir(mode=0o700)
    for relative in ('tmp', 'feed'):
        (base / relative).mkdir()
    env = {k: os.environ[k] for k in ('HOME', 'LANG', 'LC_ALL') if k in os.environ}
    env.update(m['childEnvironment'])
    env.update(DOTNET_CLI_HOME=str(base / 'dotnet-home'), NUGET_PACKAGES=str(base / 'packages'),
               NUGET_HTTP_CACHE_PATH=str(base / 'http-cache'), TMPDIR=str(base / 'tmp'))
    # Source/package choices remain genuine. The ancestor config supplies a closed local feed
    # to normal package-id installs, rather than rewriting the immutable provider descriptor.
    import shutil
    for key, (identity, version, _, _) in PACKAGES.items():
        if key != 'wizard':
            shutil.copyfile(m['packages'][key]['archive'], base / 'feed' / (identity + '.' + version + '.nupkg'))
    feed = str(base / 'feed')
    require(not any(c in feed for c in '<>&"'), 'unsafe-feed-config-path')
    config = base / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="admitted-public-originals" value="' + feed + '"/></packageSources></configuration>')
    # One clock includes source validation and feed setup. Reserve five seconds for
    # postchecks/reporting; each command separately reserves four seconds for cleanup.
    runner = Runner(base, env, deadline=operation_deadline - 5)
    report = dict(schema='fsgg.wizard.public-console-result/1', state='started', phase='inputs',
                  manifestSha256=expected_sha, providerRevision=REVISION, sourceHashes=SOURCE_HASHES,
                  packageSha256={k: v[3] for k, v in PACKAGES.items()},
                  setupElapsedSeconds=time.monotonic() - started, outerSeconds=600, reportMarginSeconds=5)
    def save(phase):
        report['phase'] = phase
        Path(m['report']).write_text(json.dumps(report, indent=2) + '\n')
    save('inputs')
    try:
        require(runner.call([m['sddExecutable'], '--version']).stdout.strip() == '2.2.0', 'actual-producer-version')
        runner.call(['dotnet', 'new', 'install', m['packages']['overlay']['archive'], '--nuget-source', base / 'feed'], cwd=base)
        report['preflightRegistration'] = registration(base, '0.18.2', PACKAGES['overlay'][3]); save('preflight-registration')
        root = base / 'console'
        runner.call(wizard_command(m, root), cwd=base)
        report['providerRegistration'] = registration(base, '0.19.0', PACKAGES['workspace'][3]); save('created')
        report['callerInitial'] = initial_commit(runner, root, m['packages']['workspace']['archive']); save('caller-initial-commit')
        runner.call(['dotnet', 'tool', 'restore', '--configfile', config, '--no-cache'], cwd=root)
        # Literal restored producer members join the ordinary local-tool path as well.
        restored_package = base / 'packages/fs.gg.sdd.cli/2.2.0'
        for name, digest in archive_payload(m['packages']['producer']['archive']).items():
            if name.startswith('tools/net10.0/any/'):
                require(sha(restored_package / name) == digest, 'restored-local-producer-payload')
        report['ordinaryChecker'] = checked(runner, root); save('ordinary-checker')
        with frozen_probes(m['sourceRoot'], runner) as (candidate, public):
            report['probe'] = candidate.probe([m['sddExecutable']], root, base / 'probe')
            report['probe']['probeSetupCommit'] = report['probe'].pop('initialCommit')
            require(report['probe']['probeSetupCommit'] != report['callerInitial']['commit'], 'probe-commit-relabeled-as-initial')
            save('capture-recovery-probe')
            report['postPrivateExport'] = post_private_export(runner, m['sddExecutable'], root, base / 'probe'); save('post-private-export')
            report['ordinaryBudget'] = public.ordinary_ci_limits(root); save('exact-and-over-budget')
        report['ordinaryCheckerAfterRestore'] = checked(runner, root)
        require(validate(manifest_path, expected_sha, after=True) == m, 'post-input-drift')
        require(time.monotonic() < operation_deadline, 'operation-deadline-during-postcheck')
        report.update(state='passed', commands=runner.sequence, elapsedSeconds=time.monotonic() - started)
        save('complete')
        return report
    except BaseException as error:
        report.update(state='failed', failureType=type(error).__name__, failure=str(error), commands=runner.sequence)
        save(report['phase'])
        raise


def source_controls():
    """Pure controls only; no production CLI, SDK, Git, network or other child is launched."""
    import tempfile
    from unittest.mock import Mock

    class Controls(unittest.TestCase):
        def test_console_route_is_explicit_and_immutable(self):
            argv = wizard_command({'wizard': '/actual-wizard'}, Path('/fresh'))
            self.assertIn('console', argv); self.assertIn('typed-sdd', argv); self.assertIn(REVISION, argv)
            self.assertNotIn('--profile', argv); self.assertNotIn('--command-json', argv)
            self.assertIn('--pinned', argv); self.assertIn('--no-governance', argv)

        def test_workflow_reaches_only_pure_console_controls(self):
            workflow = Path(__file__).resolve().parents[2] / '.github/workflows/new-sdd-workspace-selftest.yml'
            calls = [line.strip() for line in workflow.read_text().splitlines() if 'knowledge-console-public.py' in line]
            self.assertEqual(calls, ['python3 -B tests/new-sdd-workspace/knowledge-console-public.py --source-controls'])

        def test_manifest_digest_refuses_before_any_runtime(self):
            with tempfile.TemporaryDirectory() as td:
                path = Path(td) / 'manifest'; path.write_text('{}')
                with patch('subprocess.Popen', side_effect=AssertionError('runtime launched')):
                    with self.assertRaisesRegex(ValueError, 'manifest-digest'):
                        qualify(path, '0' * 64)

        def test_closed_manifest_refuses_extra_command(self):
            with tempfile.TemporaryDirectory() as td:
                path = Path(td) / 'manifest'; path.write_text(json.dumps({'schema': 'fsgg.wizard.public-console-knowledge/1', 'commandJson': ['fake']}))
                with self.assertRaisesRegex(ValueError, 'closed-manifest'):
                    validate(path, sha(path))

        def test_duplicate_manifest_key_refuses(self):
            with self.assertRaisesRegex(ValueError, 'duplicate-json-key'):
                json.loads('{"schema":1,"schema":2}', object_pairs_hook=unique)

        def test_tuple_is_literal_and_keeps_both_workspace_versions(self):
            self.assertEqual(PACKAGES['overlay'][1], '0.18.2')
            self.assertEqual(PACKAGES['workspace'][1], '0.19.0')
            with tempfile.TemporaryDirectory() as td:
                path = Path(td) / 'same-version.nupkg'; path.write_bytes(b'private lookalike')
                with self.assertRaisesRegex(ValueError, 'literal-public-package'):
                    package({'archive': str(path)}, {str(path): sha(path)}, PACKAGES['workspace'])

        def test_checker_never_accepts_source_override_or_wrong_pin(self):
            runner = Mock()
            for changed in ({'sourceCandidateOverride': True}, {'pinnedVersion': '2.1.0'}):
                value = dict(sourceCandidateOverride=False, pinnedVersion='2.2.0', limit=LIMIT); value.update(changed)
                runner.call.return_value.stdout = json.dumps(value)
                with self.assertRaisesRegex(ValueError, 'ordinary-pinned-checker'):
                    checked(runner, Path('/receiver'))
            self.assertNotIn('--command-json', checker_command(Path('/receiver')))

        def test_generated_manifest_dependency_closure_is_exact(self):
            with tempfile.TemporaryDirectory() as td:
                root = Path(td); (root / '.config').mkdir()
                tools = {k: dict(version=v, commands=['fsgg-sdd'] if k == 'fs.gg.sdd.cli' else ['fsgg-coord']) for k, v in TOOL_PINS.items()}
                path = root / '.config/dotnet-tools.json'
                path.write_text(json.dumps(dict(version=1, isRoot=True, tools=tools)))
                tool_manifest(root)
                tools['unbound-tool'] = dict(version='1.0.0', commands=['extra'])
                path.write_text(json.dumps(dict(version=1, isRoot=True, tools=tools)))
                with self.assertRaisesRegex(ValueError, 'generated-tool-set'):
                    tool_manifest(root)

        def test_every_port_has_one_command_budget_and_original_exit(self):
            with tempfile.TemporaryDirectory() as td:
                runner = Runner(td, {'PATH': '/bound'})
                process = Mock(pid=123, returncode=7)
                process.communicate.return_value = (b'original out', b'original error')
                with patch('subprocess.Popen', return_value=process) as launch, patch('os.killpg', side_effect=ProcessLookupError):
                    result = runner.run(['git', 'clone', '/from', '/to'], timeout=999)
                self.assertEqual(result.returncode, 7)
                self.assertLessEqual(process.communicate.call_args.kwargs['timeout'], 180)
                self.assertTrue(launch.call_args.kwargs['start_new_session'])
                rows = [json.loads(line) for line in (Path(td) / 'commands.jsonl').read_text().splitlines()]
                self.assertEqual(rows[-1]['actualExitCode'], 7)
                self.assertTrue(rows[-1]['groupObservedAbsent'])

        def test_expired_operation_does_not_launch(self):
            with tempfile.TemporaryDirectory() as td:
                runner = Runner(td, {}, total_seconds=0)
                with patch('subprocess.Popen', side_effect=AssertionError('runtime launched')):
                    with self.assertRaisesRegex(ValueError, 'operation-deadline'):
                        runner.run(['git', 'clone'])

        def test_timeout_retains_primary_failure_and_group_cleanup(self):
            with tempfile.TemporaryDirectory() as td:
                runner = Runner(td, {})
                process = Mock(pid=123, returncode=-15)
                process.communicate.side_effect = [subprocess.TimeoutExpired(['producer'], 180), (b'partial', b'original timeout')]
                with patch('subprocess.Popen', return_value=process), patch('os.killpg', side_effect=[None, ProcessLookupError]):
                    with self.assertRaisesRegex(ValueError, 'command-timeout'):
                        runner.run(['producer'])
                row = json.loads((Path(td) / 'commands.jsonl').read_text().splitlines()[-1])
                self.assertTrue(row['timedOut']); self.assertEqual(row['actualExitCode'], -15)
                self.assertEqual((Path(td) / row['stdoutPath']).read_bytes(), b'partial')

        def test_kill_request_for_unexpected_group_never_establishes_success(self):
            with tempfile.TemporaryDirectory() as td:
                runner = Runner(td, {})
                process = Mock(pid=123, returncode=0)
                process.communicate.return_value = (b'original out', b'')
                with patch('subprocess.Popen', return_value=process), patch('os.killpg', return_value=None):
                    with self.assertRaisesRegex(ValueError, 'command-group-cleanup-incomplete'):
                        runner.run(['producer'])
                row = json.loads((Path(td) / 'commands.jsonl').read_text().splitlines()[-1])
                self.assertFalse(row['groupObservedAbsent'])
                self.assertEqual(row['cleanupRequested'], 'SIGKILL')
                self.assertFalse(row['cleanupObservedAbsentAfterRequest'])

        def test_outer_cancellation_cleans_child_and_preserves_primary_failure(self):
            with tempfile.TemporaryDirectory() as td:
                runner = Runner(td, {})
                process = Mock(pid=123, returncode=-15)
                process.communicate.side_effect = [TimeoutError('outer-operation-cancelled'), (b'original partial', b'original stderr')]
                with patch('subprocess.Popen', return_value=process), patch('os.killpg', side_effect=[None, ProcessLookupError]):
                    with self.assertRaisesRegex(TimeoutError, 'outer-operation-cancelled'):
                        runner.run(['producer'])
                row = json.loads((Path(td) / 'commands.jsonl').read_text().splitlines()[-1])
                self.assertEqual(row['failureType'], 'TimeoutError')
                self.assertTrue(row['groupObservedAbsent'])
                self.assertEqual((Path(td) / row['stdoutPath']).read_bytes(), b'original partial')

        def test_successful_empty_export_output_is_not_json_failure(self):
            with tempfile.TemporaryDirectory() as td:
                runner = Runner(td, {})
                with patch.object(runner, 'call', return_value=subprocess.CompletedProcess([], 0, '', '')):
                    self.assertIsNone(runner.knowledge('/producer', 'export'))

        def test_frozen_probe_all_original_subprocess_ports_are_bounded(self):
            with tempfile.TemporaryDirectory() as td:
                source = Path(td); directory = source / 'tests/knowledge-receiver'; directory.mkdir(parents=True)
                texts = {
                    'receiver': 'import subprocess\ndef call(argv): return subprocess.run(argv, capture_output=True, text=True)\n',
                    'candidate': 'import subprocess\nfrom receiver import call\ndef probe():\n call(["producer", "knowledge", "get"])\n subprocess.run(["git", "clone", "from", "to"], check=True, capture_output=True)\n',
                    'public21': 'import subprocess\nfrom candidate import probe\ndef ordinary_ci_limits(): return subprocess.run(["python", "checker"], timeout=120, capture_output=True, text=True)\n',
                }
                hashes = {}
                for name, text in texts.items():
                    path = directory / (name + '.py'); path.write_text(text)
                    hashes['tests/knowledge-receiver/' + name + '.py'] = sha(path)
                runner = Mock(); runner.run.return_value = subprocess.CompletedProcess([], 0, '{}', '')
                with patch.dict(SOURCE_HASHES, hashes):
                    with frozen_probes(source, runner) as (candidate, public):
                        candidate.probe(); public.ordinary_ci_limits()
                self.assertEqual([call.args[0] for call in runner.run.call_args_list],
                                 [['producer', 'knowledge', 'get'], ['git', 'clone', 'from', 'to'], ['python', 'checker']])
                self.assertEqual(runner.run.call_args_list[-1].kwargs['timeout'], 120)
                self.assertFalse(list(source.rglob('__pycache__')))
                (directory / 'candidate.py').write_text('raise AssertionError("changed source executed")')
                with patch.dict(SOURCE_HASHES, hashes):
                    with self.assertRaisesRegex(ValueError, 'probe-load-drift'):
                        with frozen_probes(source, runner): pass

        def test_caller_initial_assets_are_captured_before_any_probe_mutation(self):
            with tempfile.TemporaryDirectory() as td:
                root = Path(td) / 'console'; root.mkdir()
                files = {'.fsgg/knowledge/schema.json': '{}', '.fsgg/knowledge/records/seed.json': '{"Id":"seed"}',
                         '.fsgg/knowledge-guide.md': 'useful guide', 'scripts/check-project-knowledge.py': 'checker',
                         '.github/workflows/project-knowledge.yml': 'workflow',
                         '.config/dotnet-tools.json': json.dumps(dict(version=1, isRoot=True, tools={k: dict(version=v, commands=['fsgg-sdd']) for k,v in TOOL_PINS.items()}))}
                for name, text in files.items():
                    path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(text)
                archive = Path(td) / 'workspace.nupkg'
                with zipfile.ZipFile(archive, 'w') as z:
                    for name in ('scripts/check-project-knowledge.py', '.github/workflows/project-knowledge.yml'):
                        z.writestr('content/templates/fs-gg-console/' + name, files[name])
                runner = Mock()
                def git(root, *argv):
                    if argv[0] == 'rev-list': return ''
                    if argv[0] == 'rev-parse': return 'caller-commit'
                    if argv[0] == 'ls-files': return '\n'.join(files)
                    return ''
                runner.git.side_effect = git
                runner.run.side_effect = lambda argv, **kwargs: subprocess.CompletedProcess(argv, 0, files[argv[-1].split(':',1)[1]].encode(), b'')
                evidence = initial_commit(runner, root, archive)
                self.assertEqual(evidence['commit'], 'caller-commit')
                self.assertEqual(set(evidence['committedAssets']), set(files))
                for name in files:
                    self.assertEqual(evidence['committedAssets'][name], sha(root / name))
                runner.git.side_effect = lambda *args: 'unwanted background commit'
                with self.assertRaisesRegex(ValueError, 'background-initial-commit'):
                    initial_commit(runner, root, archive)

        def test_qualify_preserves_caller_commit_order_and_later_probe_commit(self):
            from contextlib import contextmanager
            with tempfile.TemporaryDirectory() as td:
                base = Path(td) / 'fresh'; archive = Path(td) / 'package'; archive.write_bytes(b'bound original')
                m = dict(workRoot=str(base), report=str(base / 'report.json'), sourceRoot='/frozen', sddExecutable='/producer', wizard='/wizard',
                         childEnvironment={}, packages={key: dict(archive=str(archive)) for key in PACKAGES})
                events = []
                runner = Mock(sequence=0)
                runner.call.return_value.stdout = '2.2.0'
                @contextmanager
                def probes(source, port):
                    candidate, public = Mock(), Mock()
                    def probe(*args):
                        self.assertIn('caller-initial', events)
                        events.append('probe')
                        return dict(initialCommit='probe-setup-commit')
                    candidate.probe.side_effect = probe
                    public.ordinary_ci_limits.return_value = dict(exactLimit={'bytes': LIMIT}, oneByteOverExit=1)
                    yield candidate, public
                def initial(*args):
                    events.append('caller-initial'); return dict(commit='caller-commit')
                with patch(__name__ + '.validate', return_value=m) as validation, patch(__name__ + '.Runner', return_value=runner) as factory, \
                     patch(__name__ + '.registration', return_value={}), patch(__name__ + '.initial_commit', side_effect=initial), \
                     patch(__name__ + '.checked', return_value={}), patch(__name__ + '.frozen_probes', probes), \
                     patch(__name__ + '.post_private_export', return_value={}), patch(__name__ + '.archive_payload', return_value={}), \
                     patch('time.monotonic', return_value=100) as clock:
                    def validated(*args, **kwargs):
                        clock.return_value = 300  # Two hundred seconds already consumed by input setup.
                        return m
                    validation.side_effect = validated
                    report = qualify('/bound-manifest', 'bound-digest')
                self.assertEqual(factory.call_args.kwargs['deadline'], 695)
                self.assertEqual(report['setupElapsedSeconds'], 200)
                self.assertEqual(events, ['caller-initial', 'probe'])
                self.assertEqual(report['callerInitial']['commit'], 'caller-commit')
                self.assertEqual(report['probe']['probeSetupCommit'], 'probe-setup-commit')
                self.assertNotIn('initialCommit', report['probe'])

        def test_private_capture_must_precede_new_all_shared_export(self):
            with tempfile.TemporaryDirectory() as td:
                root, scratch = Path(td) / 'receiver', Path(td) / 'probe'; scratch.mkdir()
                runner = Mock(); calls = []
                shared = [{'Record': {'Id': 'project-knowledge'}}, {'Record': {'Id': 'decision'}}]
                def knowledge(exe, operation, *args):
                    calls.append((operation, args))
                    if operation == 'get': return {'Record': {'Id': 'synthetic-private'}}
                    if operation == 'search': return shared
                    if operation == 'export':
                        Path(args[-1]).write_text(json.dumps({'HistoryIncluded': False, 'Files': shared})); return None
                    return None
                runner.knowledge.side_effect = knowledge
                result = post_private_export(runner, '/producer', root, scratch)
                self.assertEqual([x[0] for x in calls], ['get', 'search', 'export', 'restore', 'search'])
                self.assertEqual(result['sharedIds'], ['decision', 'project-knowledge'])
                self.assertIn('decision,project-knowledge', calls[2][1])
                self.assertNotEqual(calls[2][1][-1], str(scratch / 'knowledge.json'))
                shared.append({'Record': {'Id': 'synthetic-private'}})
                with self.assertRaisesRegex(ValueError, 'private-record-in-shared-search'):
                    post_private_export(runner, '/producer', root, scratch)

    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Controls))
    require(result.wasSuccessful(), 'source-controls-failed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-controls', action='store_true')
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--manifest-sha256')
    args = parser.parse_args()
    if args.source_controls:
        source_controls()
    else:
        require(args.manifest and args.manifest_sha256, 'digest-bound-manifest-required')
        def cancelled(signum, frame):
            raise TimeoutError('outer-operation-cancelled')
        previous = signal.signal(signal.SIGTERM, cancelled)
        try:
            qualify(args.manifest, args.manifest_sha256)
        finally:
            signal.signal(signal.SIGTERM, previous)


if __name__ == '__main__':
    main()
