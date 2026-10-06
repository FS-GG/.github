#!/usr/bin/env python3
"""Validate/project a closed immutable managed lease source contract, without fetching."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tempfile
import xml.etree.ElementTree as ET

PROJECT = 'src/FS.GG.Coordination.Orchestration.Execution'
SOURCE_PATHS = tuple(f'{PROJECT}/CustodyProcessLease.{ext}' for ext in ('fsi', 'fs'))
PRODUCER_MANIFEST = f'{PROJECT}/Custody/lease-source-manifest.json'
CLIENT = 'src/FS.GG.Telemetry.Client'
PROJECTION = f'{CLIENT}/CustodyProjection'
LOCK = f'{CLIENT}/custody-source.lock.json'
NAMESPACE = 'FS.GG.Coordination.Orchestration.Execution'
NAMES = ('CustodyProcessLease.fsi', 'CustodyProcessLease.fs', 'custody-source.manifest.json')
CAP = 16384


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encode(value):
    return (json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(',', ':')) + '\n').encode('ascii')


def duplicate_free(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate property')
        result[key] = value
    return result


def decode(data):
    if len(data) > CAP:
        raise ValueError('JSON size')
    return json.loads(data.decode('utf-8'), object_pairs_hook=duplicate_free)


def closed(value, keys):
    if not isinstance(value, dict) or set(value) != set(keys):
        raise ValueError('closed object fields')


def sha(value):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value)


def validate_manifest(value):
    closed(value, ('schema', 'producerRepository', 'namespace', 'targetFramework', 'files'))
    if (value['schema'], value['producerRepository'], value['namespace'], value['targetFramework']) != ('fsgg.custody-lease-source/1', 'FS-GG/FS.GG.Coordination', NAMESPACE, 'net10.0'):
        raise ValueError('source contract identity')
    if not isinstance(value['files'], list) or len(value['files']) != 2:
        raise ValueError('source roster')
    for row, path in zip(value['files'], SOURCE_PATHS):
        closed(row, ('path', 'bytes', 'sha256'))
        if row['path'] != path or type(row['bytes']) is not int or not 0 < row['bytes'] <= CAP or not sha(row['sha256']):
            raise ValueError('source file contract')
    if sum(row['bytes'] for row in value['files']) > CAP:
        raise ValueError('aggregate size')
    return value


def read_regular(root, relative):
    root = Path(root).absolute()
    path = root / relative
    if root.is_symlink() or any(parent.is_symlink() for parent in path.parents if parent != root and root in parent.parents):
        raise ValueError('symlink path')
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    try:
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_mode & 0o111 or before.st_size > CAP:
            raise ValueError('file mode or size')
        data = os.read(fd, CAP + 1)
        after = os.fstat(fd)
        named = os.stat(path, follow_symlinks=False)
        identity = lambda s: (s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
        if len(data) != before.st_size or identity(before) != identity(after) or identity(after) != identity(named):
            raise ValueError('file changed')
        return data
    finally:
        os.close(fd)


def load_lock(root):
    lock = decode(read_regular(root, LOCK))
    closed(lock, ('schema', 'producerCommit', 'manifestSha256', 'manifest'))
    if lock['schema'] != 'fsgg.custody-lease-source-lock/1' or not isinstance(lock['producerCommit'], str) or not re.fullmatch('[0-9a-f]{40}', lock['producerCommit']) or not sha(lock['manifestSha256']):
        raise ValueError('source lock identity')
    validate_manifest(lock['manifest'])
    return lock


def git(root, *args):
    result = subprocess.run(['git', '-C', str(root), '--no-replace-objects', *args], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=5, check=True, env={**os.environ, 'GIT_NO_LAZY_FETCH': '1', 'GIT_OPTIONAL_LOCKS': '0'})
    return result.stdout


def blob(root, commit, path):
    entry = git(root, 'ls-tree', commit, '--', path).decode('ascii').strip().split('\t')
    if len(entry) != 2 or entry[1] != path:
        raise ValueError('missing Git source')
    mode, kind, oid = entry[0].split(' ')
    if mode != '100644' or kind != 'blob':
        raise ValueError('Git source mode')
    size = int(git(root, 'cat-file', '-s', oid))
    if not 0 < size <= CAP:
        raise ValueError('Git source size')
    data = git(root, 'cat-file', 'blob', oid)
    if len(data) != size:
        raise ValueError('Git source changed')
    return data


def validate_bytes(lock, manifest_bytes, sources):
    if digest(manifest_bytes) != lock['manifestSha256'] or validate_manifest(decode(manifest_bytes)) != lock['manifest']:
        raise ValueError('manifest commitment')
    for row, data in zip(lock['manifest']['files'], sources):
        if len(data) != row['bytes'] or digest(data) != row['sha256']:
            raise ValueError('source commitment')
        if not data.decode('utf-8').startswith(f'namespace {NAMESPACE}\n'):
            raise ValueError('source namespace')


def expected_from_git(root, lock):
    if git(root, 'cat-file', '-t', lock['producerCommit']).strip() != b'commit':
        raise ValueError('producer revision')
    manifest_bytes = blob(root, lock['producerCommit'], PRODUCER_MANIFEST)
    sources = [blob(root, lock['producerCommit'], path) for path in SOURCE_PATHS]
    validate_bytes(lock, manifest_bytes, sources)
    return dict(zip(NAMES, (*sources, manifest_bytes)))


def check_project(root):
    project = ET.fromstring(read_regular(root, f'{CLIENT}/FS.GG.Telemetry.Client.fsproj'))
    if project.findtext('.//TargetFramework') != 'net10.0' or project.findtext('.//IsPackable') != 'false':
        raise ValueError('Client target or package')
    includes = [node.attrib.get('Include') for node in project.findall('.//Compile')]
    expected = ['CustodyProjection/CustodyProcessLease.fsi', 'CustodyProjection/CustodyProcessLease.fs',
                'DirectResponses.fsi', 'DirectResponses.fs', 'ObservedCodexProcess.fsi']
    if any(includes.count(name) != 1 for name in expected) or includes[:len(expected)] != expected:
        raise ValueError('Client compilation order')
    for node in project.findall('.//ProjectReference') + project.findall('.//PackageReference'):
        if 'Coordination' in node.attrib.get('Include', '') or 'Akka' in node.attrib.get('Include', ''):
            raise ValueError('Coordination CLR dependency')


def local_bytes(root, lock):
    directory = root / PROJECTION
    if directory.is_symlink() or not directory.is_dir() or set(os.listdir(directory)) != set(NAMES):
        raise ValueError('projection roster')
    content = {name: read_regular(root, f'{PROJECTION}/{name}') for name in NAMES}
    validate_bytes(lock, content[NAMES[2]], [content[name] for name in NAMES[:2]])
    return content


def synchronize(root, producer_root=None, check=False):
    root = Path(root).absolute()
    lock = load_lock(root)
    check_project(root)
    expected = expected_from_git(producer_root, lock) if producer_root is not None else None
    directory = root / PROJECTION
    if directory.exists() or directory.is_symlink():
        actual = local_bytes(root, lock)
        if expected is not None and actual != expected:
            raise ValueError('producer/local mismatch')
    elif check:
        raise ValueError('missing projection')
    elif expected is None:
        raise ValueError('generation requires producer root')
    else:
        # Validate everything before staging; one new directory rename commits the roster.
        stage = Path(tempfile.mkdtemp(prefix='.custody-projection-', dir=directory.parent))
        try:
            for name, data in expected.items():
                with (stage / name).open('xb') as stream:
                    stream.write(data)
                    stream.flush()
                    os.fsync(stream.fileno())
            stage.chmod(0o755)
            # Never merge/overwrite a competing or dirty destination.
            if directory.exists() or directory.is_symlink():
                raise ValueError('projection appeared during staging')
            os.rename(stage, directory)
        finally:
            if stage.exists():
                shutil.rmtree(stage)
    return lock['producerCommit']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--producer-root', type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    try:
        synchronize(args.root, args.producer_root, args.check)
    except (OSError, ValueError, UnicodeError, ET.ParseError, subprocess.SubprocessError) as error:
        parser.exit(1, f'custody source projection refused: {error}\n')


if __name__ == '__main__':
    main()
