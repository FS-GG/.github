#!/usr/bin/env python3
"""Pinned HOST gate adapter and allowlisted custody; no qualification authority."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import struct
import subprocess
import sys
import tarfile
import time
import xml.etree.ElementTree as ET

SOURCE = '7d9663421a0e74eeb2531eadb5924974f31304f2'
SOURCE_TREE = '6d7346cd4b7393752c70ab7b97432d8815932400'
NATIVE = '8ad0da67004d670c6803f34755dfe759a7fc84e7'
NATIVE_TREE = 'df72335c0ddd892d23ee5d573c10b737245845eb'
WORKFLOW = '.github/workflows/telemetry-host-package.yml'
WORKFLOW_HASH = '62e2d94391635cbc1c9599c1630da41ae9387887a08c7168d1572e32f4b49e34'
QUINT_HASH = 'f58a238bb0d4ea6a119b5a1f0da37e7ed77135c1067228866e6ede37832395c09b7efeff63fcdc85a75c5d746631998df684cf5d493d1a686340e8400c9b2249'
GATES = [
 ('Qualify the typed HOST binding and private template', '16fccc7360a1eedbd47bb851480a8b3b1894e7e808f059af543204cf2f1f7540', {
 'NUGET_PACKAGES': '${{ runner.temp }}/host-binding-nuget', 'HOSTED_RUNNER_CUSTODY': '${{ runner.environment }}'}),
 ('Qualify the typed HOST attempt producer and canonical lifecycle', '04ec71bdb7dae124e2f1d959b603f0c6c887911d1313b0c75a0759819be3ff7a', {
 'NUGET_PACKAGES': '${{ runner.temp }}/host-attempt-nuget',
 'HOST_ATTEMPT_RECIPE_ROOT': '${{ github.workspace }}/.host-attempt-recipe-8ad',
 'HOST_ATTEMPT_BINDING_DLL': '${{ runner.temp }}/host-attempt-binding/bin/HostBinding/release/HostBinding.dll',
 'HOST_ATTEMPT_PRODUCER_DLL': '${{ runner.temp }}/host-attempt-tests/bin/HostAttempt/release/HostAttempt.dll',
 'HOST_ATTEMPT_MUTATED_PRODUCER_DLL': '${{ runner.temp }}/host-attempt-mutated/bin/HostAttempt/release/HostAttempt.dll',
 'HOST_ATTEMPT_TEST_LOADED_DLL': '${{ runner.temp }}/host-attempt-tests/bin/HostAttempt.Tests/release/HostAttempt.dll',
 'HOST_ATTEMPT_SOURCE_PINS': '${{ runner.temp }}/host-attempt-source-pins.json',
 'HOST_ATTEMPT_ITF_ROOT': '${{ runner.temp }}/host-attempt-itf',
 'HOST_ATTEMPT_ACQUIRED_TRACE_OUTPUT': '${{ runner.temp }}/host-attempt-acquired-state.json',
 'HOST_ATTEMPT_QUINT_ROOT': '${{ runner.temp }}/host-attempt-quint'})]

class Refusal(Exception):
    pass

def require(condition, message):
    if not condition:
        raise Refusal(message)

def digest(path, algorithm='sha256'):
    return hashlib.new(algorithm, Path(path).read_bytes()).hexdigest()

def json_write(path, value):
    Path(path).write_text(json.dumps(value, sort_keys=True, indent=2) + '\n')

def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args], text=True).strip()

def identity(root, revision, tree=None):
    require(git(root, 'rev-parse', 'HEAD') == revision, 'checkout revision mismatch')
    actual = git(root, 'rev-parse', 'HEAD^{tree}')
    require(tree is None or actual == tree, 'checkout tree mismatch')
    require(not git(root, 'status', '--porcelain', '--untracked-files=no'), 'tracked checkout changed')
    return {'revision': revision, 'tree': actual}

def extract(text):
    """Only the hash-verified workflow's literal indentation contract is supported."""
    require(hashlib.sha256(text.encode()).hexdigest() == WORKFLOW_HASH, 'selected workflow hash mismatch')
    result = []
    for name, script_hash, expected_env in GATES:
        marker = '      - name: ' + name + '\n'
        require(text.count(marker) == 1, 'missing or duplicate gate: ' + name)
        block = text.split(marker)[1].split('\n      - ')[0]
        require(block.startswith('        env:\n') and '        run: |\n' in block, 'unsupported gate structure')
        env_text, body = block.split('        run: |\n', 1)
        values = {}
        for line in env_text.splitlines()[1:]:
            match = re.fullmatch(r'          ([A-Z_]+): (.+)', line)
            require(match is not None and match[1] not in values, 'unsupported environment mapping')
            values[match[1]] = match[2]
        require(values == expected_env, 'gate environment mapping changed')
        lines = body.splitlines()
        require(all(not line or line.startswith('          ') for line in lines), 'unsupported run indentation')
        script = '\n'.join(line[10:] if line else '' for line in lines) + '\n'
        require('${{' not in script, 'unsupported expression in script')
        require(hashlib.sha256(script.encode()).hexdigest() == script_hash, 'gate script hash mismatch')
        result.append((name, script, values))
    return result

def resolve_env(values, source, temp, runner):
    substitutions = {'${{ runner.temp }}': str(temp), '${{ github.workspace }}': str(source), '${{ runner.environment }}': runner}
    result = {}
    for key, value in values.items():
        for expression, replacement in substitutions.items():
            value = value.replace(expression, replacement)
        require('${{' not in value, 'unknown environment expression')
        result[key] = value
    return result

def execute_gates(gates, source, temp, evidence, runner, executor):
    statuses = []
    for index, (name, script, values) in enumerate(gates, 1):
        path = evidence / f'gate-{index}.sh'
        path.write_text(script)
        values = resolve_env(values, source, temp, runner)
        json_write(evidence / f'gate-{index}-execution.json', {'name': name, 'cwd': str(source), 'env': values, 'scriptSha256': digest(path)})
        env = dict(os.environ, **values, GITHUB_WORKSPACE=str(source))
        started = time.time()
        code = executor(path, source, env, evidence / f'gate-{index}.log')
        statuses.append({'name': name, 'exitCode': code, 'startedUnix': started, 'endedUnix': time.time()})
        json_write(evidence / 'gate-status.json', statuses)
        require(code == 0, f'gate {index} failed ({code}); no success staging')
    return statuses

def shell_executor(path, source, env, log):
    # Stream exact output while retaining at most 16 MiB. Overflow fails custody.
    size = 0
    with log.open('wb') as output:
        process = subprocess.Popen(['bash', str(path)], cwd=source, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        for chunk in iter(lambda: process.stdout.read(65536), b''):
            size += len(chunk)
            if size > 16 * 1024 * 1024:
                process.kill()
                process.wait()
                raise Refusal('gate log bound exceeded')
            output.write(chunk)
            sys.stdout.buffer.write(chunk)
            sys.stdout.buffer.flush()
        return process.wait()

def counters(path, count):
    node = ET.parse(path).getroot().find('.//{*}Counters')
    require(node is not None, 'missing TRX counters')
    require(all(node.get(key) == str(value) for key, value in {'total': count, 'executed': count, 'passed': count, 'failed': 0, 'notExecuted': 0}.items()), 'wrong TRX counts or skips')

def check_counts(temp, evidence):
    counters(temp / 'host-binding-results/host-binding.trx', 35)
    counters(temp / 'host-attempt-results/host-attempt.trx', 41)
    for filename, count in [('host-binding-python-tests.log', 38), ('host-attempt-transport.log', 17), ('host-attempt-cli.log', 5)]:
        text = (temp / filename).read_text()
        require(re.search(rf'Ran {count} tests\b', text) is not None and re.search(r'^OK\s*$', text, re.M) is not None and 'skipped' not in text, 'wrong Python counts: ' + filename)
    text = (evidence / 'gate-2.log').read_text()
    for count in (12, 8):
        require(re.search(rf'\b{count} passing\b', text) is not None, 'missing positive Quint directed count')
    for fragment in ('7 retained scenarios, 91 retained transitions', '16-step concrete acquired trace matched canonical success ITF', 'actual cancellation guard mutation diverged', '8 scenarios, 23 transitions', 'actual identity guard mutation diverged'):
        require(fragment in text, 'missing canonical correspondence: ' + fragment)
    require(re.search(r'\[ok\] No violation found \(', text) is not None, 'missing actual invariant completion')
    json_write(evidence / 'invariant-run-observation.json', {'maxSamples': 5000, 'maxSteps': 30, 'seed': 20261004, 'authority': 'exact hash-verified original command and zero gate exit', 'completionObserved': True, 'exactCompletedSampleCount': None, 'limitation': 'Quint stdout does not emit an exact completed sample count'})

def safe_file(path, root):
    require(path.is_file() and not path.is_symlink(), 'missing or symlink file: ' + str(path))
    require(path.resolve().is_relative_to(root.resolve()), 'escaping path')
    require(path.stat().st_nlink == 1, 'hardlink refused')

def copy_file(path, root, destination):
    safe_file(path, root)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, destination)

def closure(directory, assembly, destination):
    require(directory.is_dir() and not directory.is_symlink(), 'missing production directory')
    deps_path = directory / f'{assembly}.deps.json'
    safe_file(deps_path, directory)
    deps = json.loads(deps_path.read_text())
    targets = deps['targets'][deps['runtimeTarget']['name']]
    allowed = {f'{assembly}{suffix}' for suffix in ('.dll', '.pdb', '.deps.json', '.runtimeconfig.json', '')}
    for package in targets.values():
        for kind in ('runtime', 'native', 'runtimeTargets'):
            for asset in package.get(kind, {}):
                name = Path(asset).name
                require(name not in ('', '.', '..') and 'test' not in name.lower() and 'mutat' not in name.lower(), 'test/mutant production dependency')
                allowed.add(name)
                if name.endswith('.dll'):
                    allowed.add(name[:-4] + '.pdb')
    required = allowed - {name for name in allowed if name.endswith('.pdb') and name != f'{assembly}.pdb'}
    present = {p.name for p in directory.iterdir()}
    require(required <= present and present <= allowed, 'production runtime closure missing or unallowlisted files')
    require((directory / assembly).stat().st_mode & 0o111, 'apphost is not executable')
    for path in directory.iterdir():
        copy_file(path, directory, destination / path.name)
    return directory / f'{assembly}.pdb'

def compressed(data, offset):
    first = data[offset]
    if first < 128:
        return first, offset + 1
    if first < 192:
        return ((first & 63) << 8) | data[offset+1], offset + 2
    require(first < 224, 'invalid compressed metadata integer')
    return ((first & 31) << 24) | int.from_bytes(data[offset+1:offset+4], 'big'), offset + 4

def pdb_documents(path, source, temp, output):
    """Read Portable PDB Document checksums and SourceLink without executing .NET."""
    data = path.read_bytes()
    require(data[:4] == b'BSJB', 'portable PDB required')
    version_length = struct.unpack_from('<I', data, 12)[0]
    offset = (16 + version_length + 3) & ~3
    count = struct.unpack_from('<H', data, offset + 2)[0]
    offset += 4
    streams = {}
    for _ in range(count):
        start, size = struct.unpack_from('<II', data, offset)
        offset += 8
        end = data.index(b'\0', offset)
        name = data[offset:end].decode()
        offset = (end + 4) & ~3
        streams[name] = data[start:start+size]
    require({'#~', '#Blob', '#GUID', '#Pdb'} <= streams.keys(), 'incomplete portable PDB streams')
    tables, blobs, guids = streams['#~'], streams['#Blob'], streams['#GUID']
    flags = tables[6]
    blob_width = 4 if flags & 4 else 2
    guid_width = 4 if flags & 2 else 2
    valid = struct.unpack_from('<Q', tables, 8)[0]
    row_counts = {}
    pos = 24
    for table in range(64):
        if valid & (1 << table):
            row_counts[table] = struct.unpack_from('<I', tables, pos)[0]
            pos += 4
    require(not any(table < 48 for table in row_counts), 'unsupported portable PDB table')
    def integer(buffer, at, width):
        return int.from_bytes(buffer[at:at+width], 'little')
    def blob(index):
        size, at = compressed(blobs, index)
        return blobs[at:at+size]
    documents = []
    for _ in range(row_counts.get(48, 0)):
        name_index = integer(tables, pos, blob_width); pos += blob_width
        algorithm_index = integer(tables, pos, guid_width); pos += guid_width
        checksum_index = integer(tables, pos, blob_width); pos += blob_width
        pos += guid_width  # language
        name = blob(name_index)
        require(name and name[0] in (47, 92), 'unsupported PDB document separator')
        components = []
        at = 1
        while at < len(name):
            index, at = compressed(name, at)
            components.append(blob(index).decode())
        document = Path(chr(name[0]).join(components))
        require(document.is_absolute() and (document.is_relative_to(source) or document.is_relative_to(temp)), 'PDB document cannot join selected source/build root')
        safe_file(document, source if document.is_relative_to(source) else temp)
        algorithm = guids[(algorithm_index-1)*16:algorithm_index*16].hex()
        algorithms = {'ec1618ff5eaa104d87f76f4963833460': 'sha1', '0fd02988b8111342878b770e8597ac16': 'sha256'}
        require(algorithm in algorithms, 'unknown PDB document checksum algorithm')
        require(digest(document, algorithms[algorithm]) == blob(checksum_index).hex(), 'PDB source checksum mismatch')
        relative = str(document.relative_to(source)) if document.is_relative_to(source) else None
        tracked = relative is not None and bool(git(source, 'ls-files', '--', relative))
        if tracked:
            require(hashlib.sha256(subprocess.check_output(['git','-C',str(source),'show',SOURCE+':'+relative])).hexdigest() == digest(document), 'PDB document differs from selected Git source')
        else:
            copy_file(document, source if document.is_relative_to(source) else temp, output / ('generated-' + digest(document) + document.suffix))
        documents.append({'document': str(document), 'algorithm': algorithms[algorithm], 'checksum': blob(checksum_index).hex(), 'gitPath': relative if tracked else None, 'generated': not tracked})
    require(documents and any(not d['generated'] for d in documents), 'PDB has no selected Git document join')
    # Decode SourceLink CustomDebugInformation; preserve original PDB alongside documents.
    source_link_guid = bytes.fromhex('560511cc91a0384d9fec25ab9a351a6a')
    external = streams['#Pdb']
    require(len(external) >= 32, 'truncated PDB identity stream')
    external_mask = struct.unpack_from('<Q', external, 24)[0]
    external_pos = 32
    all_counts = dict(row_counts)
    for table in range(64):
        if external_mask & (1 << table):
            all_counts[table] = struct.unpack_from('<I', external, external_pos)[0]
            external_pos += 4
    def width(table):
        return 4 if all_counts.get(table, 0) >= 65536 else 2
    string_width = 4 if flags & 1 else 2
    sizes = {49: width(48) + blob_width, 50: width(6) + width(53) + width(51) + width(52) + 8,
             51: 4 + string_width, 52: string_width + blob_width, 53: width(53) + blob_width,
             54: 2 * width(6)}
    for table in range(49, 55):
        pos += row_counts.get(table, 0) * sizes[table]
    parent_tables = [6,4,1,2,8,9,10,0,14,23,20,17,26,27,32,35,38,39,40,42,44,43,48,50,51,52,53]
    parent_width = 4 if max((all_counts.get(table, 0) for table in parent_tables), default=0) >= 2048 else 2
    source_link = None
    for _ in range(row_counts.get(55, 0)):
        parent = integer(tables, pos, parent_width); pos += parent_width
        kind = integer(tables, pos, guid_width); pos += guid_width
        value = integer(tables, pos, blob_width); pos += blob_width
        if guids[(kind-1)*16:kind*16] == source_link_guid:
            require(source_link is None and parent == 7 + (1 << 5), 'invalid or duplicate SourceLink metadata')
            source_link = json.loads(blob(value))
            require(type(source_link) is dict and type(source_link.get('documents')) is dict, 'invalid SourceLink documents')
    require(pos == len(tables) or not any(tables[pos:]), 'unsupported PDB table bytes')
    return {'sha256': digest(path), 'portablePdbId': external[:20].hex(), 'documents': documents, 'sourceLink': source_link if source_link is not None else 'absent'}

def assembly_pdb_join(assembly, pdb_identity):
    """Join Portable PDB ID to the production PE's actual CodeView debug record."""
    data = assembly.read_bytes()
    require(data[:2] == b'MZ', 'production assembly is not PE')
    pe = struct.unpack_from('<I', data, 60)[0]
    require(data[pe:pe+4] == b'PE\0\0', 'invalid PE signature')
    sections = struct.unpack_from('<H', data, pe+6)[0]
    optional_size = struct.unpack_from('<H', data, pe+20)[0]
    optional = pe + 24
    magic = struct.unpack_from('<H', data, optional)[0]
    require(magic in (0x10b, 0x20b), 'unsupported PE optional header')
    directory = optional + (96 if magic == 0x10b else 112) + 6*8
    debug_rva, debug_size = struct.unpack_from('<II', data, directory)
    debug_offset = None
    for index in range(sections):
        offset = optional + optional_size + index*40
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from('<IIII', data, offset+8)
        if virtual_address <= debug_rva < virtual_address + max(virtual_size, raw_size):
            debug_offset = raw_pointer + debug_rva - virtual_address
    require(debug_offset is not None and debug_size and debug_size % 28 == 0, 'missing PE debug directory')
    observed = []
    for offset in range(debug_offset, debug_offset + debug_size, 28):
        stamp, kind, size, pointer = [struct.unpack_from('<I', data, offset+part)[0] for part in (4, 12, 16, 24)]
        if kind == 2:
            record = data[pointer:pointer+size]
            require(record[:4] == b'RSDS' and len(record) >= 25, 'unsupported CodeView record')
            require(struct.unpack_from('<I', record, 20)[0] == 1, 'unsupported portable PDB age')
            actual = record[4:20].hex() + struct.pack('<I', stamp).hex()
            require(actual == pdb_identity, 'production assembly/PDB identity mismatch')
            observed.append({'codeViewPdbPath': record[24:].rstrip(b'\0').decode(), 'portablePdbId': actual})
    require(len(observed) == 1, 'missing or duplicate CodeView PDB join')
    return observed[0]

def selected_loaded(temp):
    pairs = [
     ('caller-test', 'host-attempt-tests/bin/HostAttempt/release/HostAttempt.dll', 'host-attempt-tests/bin/HostAttempt.Tests/release/HostAttempt.dll'),
     ('caller-correspondence', 'host-attempt-tests/bin/HostAttempt/release/HostAttempt.dll', 'host-attempt-correspondence/bin/HostAttempt.Correspondence/release/HostAttempt.dll'),
     ('receiver-correspondence', 'host-attempt-binding/bin/HostBinding/release/HostBinding.dll', 'host-attempt-correspondence/bin/HostAttempt.Correspondence/release/HostBinding.dll')]
    result = []
    for role, selected, loaded in pairs:
        safe_file(temp / selected, temp); safe_file(temp / loaded, temp)
        a, b = digest(temp / selected), digest(temp / loaded)
        require(a == b, 'selected/loaded assembly mismatch: ' + role)
        result.append({'role': role, 'selectedPath': selected, 'loadedPath': loaded, 'selectedSha256': a, 'loadedSha256': b})
    mutant = temp / 'host-attempt-mutated/bin/HostAttempt/release/HostAttempt.dll'
    safe_file(mutant, temp)
    require(digest(mutant) != result[0]['selectedSha256'], 'mutant equals selected caller')
    result.append({'role': 'evidence-only-mutant', 'sha256': digest(mutant)})
    return result

def inventory(root):
    result = []
    for path in sorted(root.rglob('*')):
        require(not path.is_symlink(), 'symlink refused in retention')
        if path.is_file():
            safe_file(path, root)
            info = path.stat()
            result.append({'path': str(path.relative_to(root)), 'sha256': digest(path), 'length': info.st_size, 'mode': stat.S_IMODE(info.st_mode), 'uid': info.st_uid, 'gid': info.st_gid, 'linkTarget': None})
        else:
            require(path.is_dir(), 'special file refused')
    return result

def retain(source, temp, stage):
    evidence = stage / 'evidence'
    check_counts(temp, evidence)
    joins = selected_loaded(temp)
    for role, path, root in [('receiver-test-role', source / 'deployment/telemetry-collector/host-binding/tests/bin/Release/net10.0/HostBinding.dll', source), ('receiver-test-assembly', source / 'deployment/telemetry-collector/host-binding/tests/bin/Release/net10.0/HostBinding.Tests.dll', source), ('caller-test-assembly', temp / 'host-attempt-tests/bin/HostAttempt.Tests/release/HostAttempt.Tests.dll', temp)]:
        safe_file(path, root)
        joins.append({'role': 'evidence-only-' + role, 'path': str(path), 'sha256': digest(path)})
    require(joins[-3]['sha256'] != joins[2]['selectedSha256'], 'test-role receiver substituted for production receiver')
    json_write(evidence / 'assembly-joins.json', joins)
    provenance = {}
    for assembly, directory in [('HostAttempt', 'host-attempt-tests'), ('HostBinding', 'host-attempt-binding')]:
        pdb = closure(temp / directory / 'bin' / assembly / 'release', assembly, stage / 'production' / assembly)
        provenance[assembly] = pdb_documents(pdb, source, temp, evidence / 'pdb-generated-documents')
        provenance[assembly]['assemblyJoin'] = assembly_pdb_join(pdb.with_suffix('.dll'), provenance[assembly]['portablePdbId'])
    json_write(evidence / 'pdb-provenance.json', provenance)
    base = source / 'deployment/telemetry-collector/host-attempt'
    copy_file(base / 'host_attempt_transport.py', source, stage / 'production/transport/host_attempt_transport.py')
    for filename in ['host-binding-results/host-binding.trx', 'host-attempt-results/host-attempt.trx', 'host-binding-python-tests.log', 'host-binding-pyyaml-version.txt', 'host-attempt-transport.log', 'host-attempt-cli.log', 'host-attempt-acquired-state.json', 'host-attempt-source-pins.json']:
        copy_file(temp / filename, temp, evidence / filename)
    itfs = list((temp / 'host-attempt-itf').glob('*.itf.json'))
    require(len(itfs) == 15 and len(list((temp / 'host-attempt-itf').iterdir())) == 15, 'canonical ITF closure must contain exactly 15 files')
    expected = {'success','ambiguous','lost','sticky','deadline','refused-owned-cancel','false-ownership','gatedRelease','lostRelease','cancelBefore','deadlineBefore','identityFailure','cancelAfter','failureAfter','settled'}
    require({re.sub(r'-\d+\.itf\.json$', '', p.name) for p in itfs} == expected, 'ITF scenario names mismatch')
    for path in itfs:
        copy_file(path, temp, evidence / 'host-attempt-itf' / path.name)
    for relative in ['deployment/telemetry-collector/host-attempt/HostAttempt.qnt', 'deployment/telemetry-collector/host-attempt/HostAttempt_test.qnt', 'docs/quint/toolchain.json']:
        copy_file(source / relative, source, evidence / 'selected-inputs' / relative)
    for name in ('qualify_native.py', 'native_producer_support.py', 'native-operation-v1.json', 'native-producer-config.toml'):
        path = source / '.host-attempt-recipe-8ad/deployment/telemetry-collector' / name
        copy_file(path, source, evidence / 'fixed-native-recipe' / name)
    archive = temp / 'host-attempt-quint/archives/quint-0.32.0.tgz'
    safe_file(archive, temp)
    require(digest(archive, 'sha512') == QUINT_HASH, 'Quint archive identity mismatch')
    copy_file(archive, temp, evidence / archive.name)
    packages = list((temp / 'host-attempt-quint/npm/_npx').glob('*/node_modules/**/package.json'))
    require(packages, 'missing resolved npm dependency inventory')
    dependencies = []
    for path in packages:
        safe_file(path, temp)
        value = json.loads(path.read_text())
        dependencies.append({'path': str(path.relative_to(temp)), 'name': value.get('name'), 'version': value.get('version'), 'sha256': digest(path)})
    json_write(evidence / 'quint-resolved-dependencies.json', dependencies)
    inputs = []
    for relative in git(source, 'ls-files').splitlines():
        if Path(relative).suffix in ('.fs', '.fsi', '.fsproj', '.props', '.targets', '.qnt') or Path(relative).name in ('packages.lock.json', 'global.json', 'nuget.config', 'toolchain.json'):
            inputs.append({'path': relative, 'sha256': digest(source / relative)})
    json_write(evidence / 'selected-build-input-hashes.json', inputs)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--recipe', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    source, recipe, output = args.source.resolve(), args.recipe.resolve(), args.output.resolve()
    require(os.environ.get('GITHUB_ACTIONS') == 'true' and os.environ.get('CUSTODY_RUNNER_ENVIRONMENT') == 'github-hosted', 'only genuine hosted Actions runner permitted')
    require(os.environ.get('GITHUB_REPOSITORY') == 'FS-GG/.github', 'repository mismatch')
    require(os.environ.get('GITHUB_EVENT_NAME') in ('push', 'pull_request'), 'unsupported event')
    if os.environ['GITHUB_EVENT_NAME'] == 'push':
        require(os.environ.get('GITHUB_REF') == 'refs/heads/main', 'protected recipe requires main push')
    required_env = ['GITHUB_WORKFLOW_SHA', 'GITHUB_SHA', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_WORKFLOW_REF', 'GITHUB_JOB', 'RUNNER_TEMP', 'ImageOS', 'ImageVersion']
    require(all(os.environ.get(key) for key in required_env), 'missing Actions/run/image identity')
    require(os.environ['GITHUB_WORKFLOW_SHA'] == os.environ['GITHUB_SHA'], 'recipe workflow/head identity mismatch')
    for tool in ('bash', 'git', 'dotnet', 'python3', 'jq', 'curl', 'node', 'npm', 'sudo', 'sha512sum'):
        require(shutil.which(tool) is not None, 'missing tool: ' + tool)
    subprocess.check_call(['python3', '-c', 'import yaml'])
    source_identity = identity(source, SOURCE, SOURCE_TREE)
    native_identity = identity(source / '.host-attempt-recipe-8ad', NATIVE, NATIVE_TREE)
    recipe_identity = identity(recipe, os.environ['GITHUB_WORKFLOW_SHA'])
    gates = extract((source / WORKFLOW).read_text())
    require(not output.exists(), 'output already exists')
    output.mkdir(parents=True)
    stage = output / 'payload'
    evidence = stage / 'evidence'
    evidence.mkdir(parents=True)
    temp = Path(os.environ['RUNNER_TEMP']).resolve()
    require(not source.is_relative_to(temp) and not output.is_relative_to(source), 'source/output layout refused')
    for name in ('host-binding-nuget', 'host-binding-results', 'host-attempt-nuget', 'host-attempt-binding', 'host-attempt-tests', 'host-attempt-mutated', 'host-attempt-correspondence', 'host-attempt-results', 'host-attempt-itf', 'host-attempt-quint', 'host-attempt-acquired-state.json', 'host-attempt-source-pins.json'):
        require(not (temp / name).exists(), 'existing gate output/cache refused: ' + name)
    started = time.time()
    execute_gates(gates, source, temp, evidence, os.environ['CUSTODY_RUNNER_ENVIRONMENT'], shell_executor)
    identity(source, SOURCE, SOURCE_TREE)
    identity(source / '.host-attempt-recipe-8ad', NATIVE, NATIVE_TREE)
    identity(recipe, os.environ['GITHUB_WORKFLOW_SHA'])
    retain(source, temp, stage)
    tool_info = {}
    for name, command in [('dotnet', ['/usr/bin/dotnet', '--info']), ('runtimes', ['/usr/bin/dotnet', '--list-runtimes']), ('node', ['node', '--version']), ('npm', ['npm', '--version'])]:
        tool_info[name] = subprocess.check_output(command, text=True)
    tool_info['compilerAndHostHashes'] = {str(path): digest(path) for path in [Path('/usr/share/dotnet/dotnet'), Path('/usr/share/dotnet/sdk/10.0.401/FSharp/fsc.dll'), Path('/usr/share/dotnet/sdk/10.0.401/FSharp/FSharp.Compiler.Service.dll')]}
    tool_info['runtimeFiles'] = [{'path': str(path), 'sha256': digest(path)} for name in ('libcoreclr.so', 'System.Private.CoreLib.dll', 'libhostfxr.so') for path in Path('/usr/share/dotnet').glob('**/' + name)]
    require(tool_info['runtimeFiles'], 'missing hosted runtime file identities')
    json_write(evidence / 'runtime-toolchain.json', tool_info)
    json_write(evidence / 'acquired-trace-role.json', {'role': 'synthetic-unit-test-only', 'nativeAcceptanceEstablished': False, 'useTimeRuntimeCustodyRequired': True})
    identities = {'compiledSource': source_identity, 'fixedNativeRecipe': native_identity, 'constructionRecipe': recipe_identity}
    recipe_hashes = {path: digest(recipe / path) for path in ['scripts/host-protected-artifact-custody.py', '.github/workflows/host-protected-artifact-custody.yml']}
    json_write(evidence / 'construction.json', {'identities': identities, 'recipeHashes': recipe_hashes, 'actions': {key: os.environ[key] for key in required_env + ['GITHUB_EVENT_NAME', 'GITHUB_REPOSITORY', 'GITHUB_REF']}, 'outerWorkspace': os.environ['GITHUB_WORKSPACE'], 'runnerEnvironment': os.environ['CUSTODY_RUNNER_ENVIRONMENT'], 'startedUnix': started, 'endedUnix': time.time()})
    entries = inventory(stage)
    json_write(output / 'inventory.json', entries)
    with tarfile.open(output / 'payload.tar', 'w') as archive:
        archive.add(stage, arcname='payload', recursive=True)
    json_write(output / 'manifest.json', {'schema': 'fsgg.host-protected-artifact-custody/1', 'scope': 'exact-selected-HOST-gates-only', 'status': 'constructed-gates-passed-root-authentication-pending', 'identities': identities, 'runId': os.environ['GITHUB_RUN_ID'], 'runAttempt': os.environ['GITHUB_RUN_ATTEMPT'], 'designation': 'protected' if os.environ['GITHUB_EVENT_NAME'] == 'push' else 'candidate', 'tarSha256': digest(output / 'payload.tar'), 'inventorySha256': digest(output / 'inventory.json'), 'nativeAcceptanceEstablished': False, 'rootAuthenticationRequired': True})
    shutil.rmtree(stage)

if __name__ == '__main__':
    try:
        main()
    except (Refusal, OSError, ValueError, KeyError, IndexError, struct.error, ET.ParseError, subprocess.CalledProcessError) as error:
        print('HOST custody refused: ' + str(error), file=sys.stderr)
        sys.exit(1)
