#!/usr/bin/env python3
"""Prepare fixed native image inputs; never build, launch, or acquire credentials.

The Coord distribution must be publisher supplied. A tool-install apphost and a
locally synthesized content marker do not satisfy that input contract.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import ssl
import stat
import subprocess
import zipfile

ELF = '167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9'
HOST_SOURCE = '0145bd2c852847d00da8b3a0c35d27cd64a87781'
HOST_PINS = {'package.nupkg': '4847c15ab207a33556462873840ad4109cf1c1e16fd7a15d5fffae5a8162f589', 'manifest.json': 'f16546855d4ce60060f762b6f7bed715cc115f9ef97cd7b55dad5294b2ea0816', 'publication-journal.json': 'bce45f3f4c0701031863c6b1eb398a99982b152f585467cb2e7b06c00987c58e'}
COORD_SOURCE = '337b6a1d53571b07ca8e1417e18e52546ad319a7'
COORD_CONTENT = '9b9486a54e014fd5d21b65ed71a00b9021562a56909a1303f89c9646bca4a585'
COORD_ARCHIVES = {'FS.GG.Coord.Cli': '64fdd5311e73e9b5c81c6353138d2bad5fa07df7aed2b3e55b56101ecf972603', 'FS.GG.Drivers': 'd8a24f0cc19421187771b6f1dfc1e97a84a22931b1e0cd5ba262afc4187f8f0b', 'FS.GG.Kit': 'b8e0631330d277c520225fa9107560cada6f67aa804a583cc208c763460499f6'}
SOURCE_ARGS = {'qualify_native.py': 'NATIVE_DRIVER_SHA256', 'native_producer_support.py': 'NATIVE_SUPPORT_SHA256', 'native-operation-v1.json': 'NATIVE_PROFILE_SHA256', 'native-producer-config.toml': 'NATIVE_CONFIG_SHA256'}
MAX_FILE = 384 * 1024 * 1024
MAX_TOTAL = 512 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def bounded(path, directory=False):
    path = Path(path)
    require(path.is_absolute() and '..' not in path.parts, 'absolute bounded path required')
    for component in [path, *path.parents]:
        require(not component.is_symlink(), 'symlink path refused')
    info = path.stat()
    require(info.st_uid == os.getuid(), 'input ownership differs')
    require(stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode), 'input kind differs')
    require(not info.st_mode & 0o022, 'writable shared input refused')
    if not directory:
        require(info.st_size <= MAX_FILE, 'input exceeds bound')
    return path


def digest(path):
    bounded(path)
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def pinned(path, expected):
    require(re.fullmatch('[0-9a-f]{64}', expected or '') is not None, 'invalid digest pin')
    require(digest(path) == expected, 'input digest differs')


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def members(package, prefix):
    selected = {}
    total = 0
    with zipfile.ZipFile(package) as archive:
        require(len(archive.infolist()) <= 2048, 'archive member count exceeds bound')
        seen = set()
        for info in archive.infolist():
            name = PurePosixPath(info.filename)
            require(not name.is_absolute() and '..' not in name.parts and '\\' not in info.filename, 'unsafe archive path')
            require(info.filename not in seen, 'duplicate archive member')
            seen.add(info.filename)
            mode = info.external_attr >> 16
            require(not stat.S_ISLNK(mode), 'archive symlink refused')
            require(info.file_size <= MAX_FILE, 'archive member exceeds bound')
            total += info.file_size
            require(total <= MAX_TOTAL, 'archive expansion exceeds bound')
            if info.filename.startswith(prefix) and not info.is_dir():
                selected[info.filename[len(prefix):]] = archive.read(info)
    require(selected, 'distribution payload missing')
    return selected


def verify_public_artifacts(repository, host, coord, manifest, codex):
    bounded(host, True); bounded(coord, True)
    for name, pin in HOST_PINS.items():
        pinned(host / name, pin)
    host_reader = load_module(repository / 'deployment/telemetry-collector/telemetry_collector.py', 'context_host_reader')
    host_reader.verify_release(repository, host / 'package.nupkg', host / 'manifest.json', host / 'publication-journal.json', '0.2.1', HOST_SOURCE, *HOST_PINS.values())
    bounded(manifest)
    release = json.loads(manifest.read_text())
    saga = load_module(repository / 'scripts/release-saga.py', 'context_release_saga')
    require(release.get('contentId') == 'sha256:' + COORD_CONTENT, 'Coord content identity differs')
    require(hashlib.sha256(saga.canonical(release['descriptor'])).hexdigest() == COORD_CONTENT, 'Coord descriptor differs')
    require(release['descriptor']['sourceSha'] == COORD_SOURCE and release['descriptor']['version'] == '0.94.0', 'Coord source identity differs')
    require(release['state']['phase'] == 'promoted', 'Coord release is not promoted')
    packages = {row['id']: row for row in release['descriptor']['packages']}
    require(set(packages) == set(COORD_ARCHIVES), 'Coord coherent package set differs')
    for name, pin in COORD_ARCHIVES.items():
        row = packages[name]
        require(row['version'] == '0.94.0' and row['artifact']['sha256'] == pin, 'Coord package binding differs')
        archive = coord / (name + '.0.94.0.nupkg')
        pinned(archive, pin)
        require(saga.payload_id(archive) == row['artifact']['payloadSha256'], 'Coord package payload differs')
        for feed in ('github', 'nuget'):
            fact = release['state']['feeds'][feed]['packages'][name]
            require(fact['state'] == 'verified' and fact['externalPayloadSha256'] == row['artifact']['payloadSha256'], 'Coord feed proof incomplete')
    pinned(codex, ELF)
    with codex.open('rb') as executable:
        require(executable.read(4) == b'\x7fELF', 'native executable is not ELF')
    return {'hostVersion': '0.2.1', 'hostSourceSha': HOST_SOURCE, 'coordSourceSha': COORD_SOURCE, 'coordContentSha256': COORD_CONTENT, 'coordManifestSha256': digest(manifest), 'nativeElfSha256': ELF}


def verify_distribution(root, package, executable_pin, provenance, provenance_pin):
    bounded(root, True)
    pinned(provenance, provenance_pin)
    evidence = json.loads(provenance.read_text())
    require(evidence.get('schema') == 'fsgg.coord.publisher-distribution/1' and evidence.get('sourceSha') == COORD_SOURCE and evidence.get('contentSha256') == COORD_CONTENT, 'publisher distribution provenance missing')
    expected = members(package, 'tools/net10.0/any/')
    expected_names = set(expected) | {'fsgg-coord-engine', 'coherent-content.sha256'}
    actual = set()
    for entry in root.rglob('*'):
        bounded(entry, entry.is_dir())
        if entry.is_file():
            actual.add(entry.relative_to(root).as_posix())
    require(actual == expected_names, 'full publisher distribution differs')
    for name, raw in expected.items():
        require((root / name).read_bytes() == raw, 'publisher dependency bytes differ')
    pinned(root / 'fsgg-coord-engine', executable_pin)
    require(evidence.get('executableSha256') == executable_pin, 'publisher executable pin differs')
    marker = root / 'coherent-content.sha256'
    require(marker.read_text() in (COORD_CONTENT, COORD_CONTENT + '\n'), 'publisher coherent marker differs')
    require(evidence.get('markerSha256') == digest(marker), 'publisher marker pin differs')
    return expected_names


def write_private(path, raw, mode=0o600):
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(raw)
    path.chmod(mode)


def prepare(args):
    repository = Path(__file__).absolute().parents[2]
    host, coord, manifest, codex = map(Path, (args.host_served, args.coord_packages, args.coord_manifest, args.codex))
    facts = verify_public_artifacts(repository, host, coord, manifest, codex)
    output = Path(args.output)
    require(output.is_absolute() and '..' not in output.parts and not output.exists(), 'fresh absolute output required')
    if output.parent == Path('/tmp'):
        require(output.parent.stat().st_uid == 0 and output.parent.stat().st_mode & stat.S_ISVTX, 'temporary parent is not protected')
    else:
        bounded(output.parent, True)
    gaps = []
    if not args.coord_distribution:
        gaps.append('publisher-coord-executable-and-coherent-marker-unavailable')
    if not args.receiver_cert:
        gaps.append('caller-public-receiver-trust-certificate-required')
    if not args.native_source:
        gaps.append('reviewed-native-source-input-required')
    if gaps:
        output.mkdir(mode=0o700)
        write_private(output / 'availability.json', (json.dumps(dict(facts, schema='fsgg.native-image-input-availability/1', gaps=gaps, contextPrepared=False), sort_keys=True) + '\n').encode())
        return {'contextPrepared': False, 'gaps': gaps}
    require(args.coord_provenance and args.coord_provenance_sha256 and args.coord_engine_sha256, 'publisher provenance and executable pins required')
    distribution = Path(args.coord_distribution)
    names = verify_distribution(distribution, coord / 'FS.GG.Coord.Cli.0.94.0.nupkg', args.coord_engine_sha256, Path(args.coord_provenance), args.coord_provenance_sha256)
    certificate = Path(args.receiver_cert)
    bounded(certificate)
    raw_cert = certificate.read_text('ascii')
    require(raw_cert.count('-----BEGIN CERTIFICATE-----') == 1 and 'PRIVATE KEY' not in raw_cert and len(raw_cert) <= 65536, 'public certificate input differs')
    ssl.PEM_cert_to_DER_cert(raw_cert)
    source = bounded(Path(args.native_source), True)
    require(args.native_source_pins and re.fullmatch('[0-9a-f]{40}', args.native_source_revision or ''), 'native source revision and pins required')
    pin_file = bounded(Path(args.native_source_pins))
    pins = json.loads(pin_file.read_text())
    require(set(pins) == set(SOURCE_ARGS), 'native source pin set differs')
    for name, pin in pins.items():
        pinned(source / name, pin)
        repository_path = 'deployment/telemetry-collector/' + name
        committed = subprocess.run(['git', '-C', str(source), 'show', args.native_source_revision + ':' + repository_path], check=True, capture_output=True, env={'PATH': '/usr/bin:/bin', 'LANG': 'C', 'GIT_CONFIG_NOSYSTEM': '1', 'GIT_CONFIG_GLOBAL': '/dev/null'}).stdout
        require(hashlib.sha256(committed).hexdigest() == pin, 'native source revision bytes differ')
    output.mkdir(mode=0o700)
    context = output / 'context'
    context.mkdir(mode=0o700)
    write_private(context / 'native/codex', codex.read_bytes(), 0o500)
    for name in SOURCE_ARGS:
        write_private(context / 'native' / name, (source / name).read_bytes())
    for name in sorted(names):
        write_private(context / 'native/fsgg-coord-engine' / name, (distribution / name).read_bytes(), 0o500 if name == 'fsgg-coord-engine' else 0o600)
    write_private(context / 'native/native-receiver.crt', certificate.read_bytes())
    for name, raw in members(host / 'package.nupkg', 'tools/net10.0/linux-x64/').items():
        write_private(context / 'collector/host' / name, raw)
    development = dict((key, pins[name]) for name, key in SOURCE_ARGS.items())
    development.update(NATIVE_ELF_SHA256=ELF, COORD_ENGINE_SHA256=args.coord_engine_sha256, RECEIVER_TRUST_CERT_SHA256=digest(certificate), COORD_SOURCE_SHA=COORD_SOURCE, COORD_PAYLOAD_SHA256=COORD_CONTENT)
    collector = dict(NATIVE_ELF_SHA256=ELF, HOST_VERSION='0.2.1', HOST_SOURCE_SHA=HOST_SOURCE, HOST_PACKAGE_SHA256=HOST_PINS['package.nupkg'])
    inventory = {entry.relative_to(context).as_posix(): digest(entry) for entry in context.rglob('*') if entry.is_file()}
    packet = dict(facts, schema='fsgg.native-image-context/1', contextPrepared=True, nativeSourceRevision=args.native_source_revision, publisherProvenanceSha256=args.coord_provenance_sha256, buildArguments={'native-development': development, 'native-collector': collector}, files=inventory)
    write_private(output / 'manifest.json', (json.dumps(packet, sort_keys=True, indent=2) + '\n').encode())
    return {'contextPrepared': True, 'manifestSha256': digest(output / 'manifest.json')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('host-served', 'coord-packages', 'coord-manifest', 'codex', 'output'):
        parser.add_argument('--' + name, required=True)
    for name in ('coord-distribution', 'coord-provenance', 'coord-provenance-sha256', 'coord-engine-sha256', 'receiver-cert', 'native-source', 'native-source-pins', 'native-source-revision'):
        parser.add_argument('--' + name)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args), sort_keys=True))
    except (ValueError, OSError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        parser.exit(2, 'native context refused: ' + str(error) + '\n')


if __name__ == '__main__':
    main()
