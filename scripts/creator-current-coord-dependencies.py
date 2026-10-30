#!/usr/bin/env python3
"""Current Creator release join; historical staging primitives remain immutable.

No pin is shipped until the producer has genuinely promoted. Source-only builds
never need one. This module imports no command entrypoint and runs no compiler.
"""
from __future__ import annotations
import argparse
import json
import pathlib
import re
import runpy
import stat
import zipfile
import subprocess
import urllib.request

from creator_source_qualification import SOURCE_020, qualification_version
from new_sdd_workspace_successor_execution import CURRENT_020, CURRENT_021, CURRENT_022, CURRENT_023

ROOT = pathlib.Path(__file__).resolve().parents[1]
PIN_PATH = pathlib.Path('scripts/creator-current-coord-dependencies.json')
PIN_021_PATH = pathlib.Path('scripts/creator021-coord-dependencies.json')
PIN_022_PATH = pathlib.Path('scripts/creator022-coord-dependencies.json')
PIN_023_PATH = pathlib.Path('scripts/creator023-coord-dependencies.json')
# Each release binding selects exactly one fixed promoted coherent pin; none falls back to another.
RELEASE_PINS = ((CURRENT_020, PIN_PATH, '0.108.0'), (CURRENT_021, PIN_021_PATH, '0.110.0'),
                (CURRENT_022, PIN_022_PATH, '0.112.0'), (CURRENT_023, PIN_023_PATH, '0.113.0'))
RELEASE_QUALIFIED = (CURRENT_021, CURRENT_022, CURRENT_023)  # explicit current-route release qualification profiles


def release_pin(binding):
    rows = [row for row in RELEASE_PINS if row[0] is binding]
    require(len(rows) == 1, 'unknown Creator dependency binding')
    return rows[0]


def binding_pin_path(binding):
    return release_pin(binding)[1]


old = runpy.run_path(str(ROOT / 'scripts/creator-frozen-coord-dependencies.py'))
require, regular, digest = (old[name] for name in ('require', 'regular', 'digest'))
PREFIX = old['PREFIX']
MODE = 'published-dependency'
SCHEMA = 'fsgg.creator-published-dependencies/1'


def object_pairs(pairs):
    value = {}
    for key, body in pairs:
        require(key not in value, 'duplicate JSON field: ' + key)
        value[key] = body
    return value


def read_json(body):
    require(len(body) <= 4 * 1024 * 1024, 'dependency JSON bound exceeded')
    return json.loads(body, object_pairs_hook=object_pairs)


def validate_pin(pin, *, release_binding=CURRENT_020):
    coherent = release_pin(release_binding)[2]
    require(isinstance(pin, dict) and pin.get('schema') == 'fsgg.creator-current-coord-dependencies/1'
            and pin.get('version') == coherent, 'current dependencies require promoted coherent ' + coherent + ' pin')
    for field, size in (('sourceSha', 40), ('archiveSha256', 64), ('promotedManifestSha256', 64)):
        require(isinstance(pin.get(field), str) and re.fullmatch('[0-9a-f]{' + str(size) + '}', pin[field]),
                'invalid current dependency ' + field)
    for field in ('releaseId', 'assetId', 'promotedManifestAssetId'):
        require(type(pin.get(field)) is int and pin[field] > 0, 'missing promoted dependency ' + field)
    require(pin.get('downloadUrl') == f'https://github.com/FS-GG/.github/releases/download/coherent-set/v{coherent}/FS.GG.Coord.Cli.{coherent}.nupkg',
            'current dependency download identity differs')
    members = pin.get('members')
    require(isinstance(members, dict) and {'FS.GG.Coord.GitHub.dll', 'fsgg-coord-engine.dll',
            'fsgg-coord-engine.deps.json', 'fsgg-coord-engine.runtimeconfig.json'} <= members.keys(),
            'current dependency closure missing required runtime members')
    for name, sha in members.items():
        path = pathlib.PurePosixPath(name)
        require(name and not path.is_absolute() and '..' not in path.parts and '\\' not in name
                and path.as_posix() == name and name != 'DotnetToolSettings.xml'
                and not name.startswith('new-sdd-workspace.')
                and isinstance(sha, str) and re.fullmatch('[0-9a-f]{64}', sha), 'invalid dependency member')
    require(isinstance(pin.get('projects'), dict) and pin['projects'].get(
        'scripts/NewSddWorkspace/NewSddWorkspace.fsproj') == 'new-sdd-workspace', 'missing current source graph')
    require(isinstance(pin.get('sourceLeaves'), list) and pin['sourceLeaves'], 'missing current source census')
    paths = set()
    for leaf in pin['sourceLeaves']:
        require(isinstance(leaf, dict) and set(leaf) == {'path', 'sha256'}, 'invalid dependency source leaf')
        path = pathlib.PurePosixPath(leaf['path'])
        require(not path.is_absolute() and '..' not in path.parts and '\\' not in leaf['path']
                and path.as_posix() == leaf['path'] and leaf['path'] not in paths
                and re.fullmatch('[0-9a-f]{64}', leaf['sha256']), 'invalid dependency source path or digest')
        paths.add(leaf['path'])
    return pin


def load_pin(root=ROOT, *, release_binding=CURRENT_020):
    """Only the fixed protected-source path; no caller-provided provenance path."""
    path = root / binding_pin_path(release_binding)
    require(path.exists(), 'Creator release fenced: genuine promoted dependency pin absent')
    return validate_pin(read_json(regular(path)), release_binding=release_binding)


def protected_pin(source_sha, *, release_binding=CURRENT_020):
    pin = load_pin() if release_binding is CURRENT_020 else load_pin(release_binding=release_binding)
    pin_path = binding_pin_path(release_binding)
    require(re.fullmatch('[0-9a-f]{40}', source_sha) is not None, 'invalid protected creator source')
    original = subprocess.check_output(['git', '-C', str(ROOT), 'show', source_sha + ':' + pin_path.as_posix()], timeout=10)
    protected = subprocess.check_output(['git', '-C', str(ROOT), 'show', 'HEAD:' + pin_path.as_posix()], timeout=10)
    require(read_json(original) == pin == read_json(protected), 'candidate dependency pin differs from protected publisher source')
    return pin


def acquire(destination, pin):
    """Acquire original promoted assets with independent native identity readback."""
    require(not destination.exists(), 'dependency acquisition destination exists')
    def request(url):
        with urllib.request.urlopen(urllib.request.Request(url, headers={'Accept': 'application/vnd.github+json'}), timeout=60) as response:
            body = response.read(134217729)
        require(len(body) <= 134217728, 'dependency acquisition bound exceeded')
        return body
    release = read_json(request('https://api.github.com/repos/FS-GG/.github/releases/' + str(pin['releaseId'])))
    require(release.get('id') == pin['releaseId'] and release.get('tag_name') == 'coherent-set/v' + pin['version']
            and release.get('draft') is False and release.get('prerelease') is False, 'producer release not promoted')
    asset_rows = read_json(request('https://api.github.com/repos/FS-GG/.github/releases/' + str(pin['releaseId']) + '/assets?per_page=100&page=1'))
    require(isinstance(asset_rows, list) and len(asset_rows) < 100
            and len({row['id'] for row in asset_rows}) == len(asset_rows)
            and len({row['name'] for row in asset_rows}) == len(asset_rows), 'incomplete or duplicate producer assets')
    assets = {row['id']: row for row in asset_rows}
    package = assets.get(pin['assetId'], {})
    manifest = assets.get(pin['promotedManifestAssetId'], {})
    require(package.get('browser_download_url') == pin['downloadUrl']
            and package.get('name') == 'FS.GG.Coord.Cli.' + pin['version'] + '.nupkg', 'producer asset identity differs')
    require(manifest.get('name') == 'release-manifest.json', 'promoted manifest asset identity differs')
    manifest_body = request(manifest['browser_download_url'])
    require(digest(manifest_body) == pin['promotedManifestSha256'], 'promoted manifest digest differs')
    promoted = read_json(manifest_body)
    require(promoted.get('descriptor', {}).get('sourceSha') == pin['sourceSha'] and promoted.get('descriptor', {}).get('version') == pin['version'],
            'promoted manifest producer identity differs')
    body = request(pin['downloadUrl'])
    require(digest(body) == pin['archiveSha256'], 'producer archive digest differs')
    with destination.open('xb') as stream:
        stream.write(body)
    old['archive_members'](destination, pin)
    return {'producerArchiveSha256': pin['archiveSha256'], 'promotedManifestSha256': pin['promotedManifestSha256']}


def pin_digest(pin):
    return digest(json.dumps(pin, sort_keys=True, separators=(',', ':')).encode())


def dependency_metadata(deps, producer, pin):
    """Join metadata to the hash-verified producer, including its internal versions.

    SDK deps names the entry project by assembly name and a project reference by
    project name. This one root translation does not rewrite either document.
    Internal libraries retain their producer identities (currently 1.0.0).
    """
    source_root, reference_root = 'fsgg-coord-engine/' + pin['version'], 'FS.GG.Coord.Cli/' + pin['version']
    source_libraries, libraries = producer.get('libraries'), deps.get('libraries')
    source_targets, targets = producer.get('targets'), deps.get('targets')
    require(all(isinstance(value, dict) for value in (source_libraries, libraries, source_targets, targets))
            and source_targets and set(source_targets) == set(targets), 'published dependency metadata targets differ')
    projects = {pathlib.PurePosixPath(path).stem: assembly for path, assembly in pin['projects'].items()
                if assembly != 'new-sdd-workspace'}
    project_keys = set()
    for project, assembly in projects.items():
        name = 'fsgg-coord-engine' if project == 'FS.GG.Coord.Cli' else project
        found = [key for key, value in source_libraries.items()
                 if key.partition('/')[0] == name and isinstance(value, dict) and value.get('type') == 'project']
        require(len(found) == 1, 'published dependency project metadata missing: ' + project)
        project_keys.add(found[0])
        require(all(isinstance(rows, dict) and isinstance(rows.get(found[0]), dict)
                    and set(rows[found[0]].get('runtime', {})) == {assembly + '.dll'}
                    for rows in source_targets.values()), 'published project runtime metadata differs: ' + project)
    require(source_root in project_keys and project_keys == {
        key for key, value in source_libraries.items() if isinstance(value, dict) and value.get('type') == 'project'
    }, 'published dependency project population differs')
    translated = {key: reference_root if key == source_root else key for key in source_libraries}
    for key, expected in source_libraries.items():
        selected = translated[key]
        require(libraries.get(selected) == expected, 'published dependency library metadata differs: ' + selected)
        require({entry for entry in libraries if entry.partition('/')[0] == selected.partition('/')[0]} == {selected},
                'alternate published dependency metadata identity: ' + selected)
        for framework, rows in source_targets.items():
            require(isinstance(rows, dict) and isinstance(targets[framework], dict)
                    and isinstance(rows.get(key), dict) and isinstance(targets[framework].get(selected), dict),
                    'published dependency target metadata missing: ' + selected)
            actual = targets[framework][selected]
            if key == source_root:
                # The SDK may annotate the referenced current entry assembly's
                # default FileVersion. All dependency edges remain exact.
                require(actual.get('runtime') in ({'fsgg-coord-engine.dll': {}},
                        {'fsgg-coord-engine.dll': {'fileVersion': '0.0.0.0'}}),
                        'published entry runtime metadata differs')
                actual = {**actual, 'runtime': {'fsgg-coord-engine.dll': {}}}
            require(actual == rows[key], 'published dependency target metadata differs: ' + selected)
    for framework, rows in source_targets.items():
        owned_runtime = {pathlib.PurePosixPath(asset).name: translated[key]
                         for key, value in rows.items() for asset in value.get('runtime', {})}
        for key, value in targets[framework].items():
            require(isinstance(value, dict), 'malformed dependency target metadata')
            require(all(owned_runtime.get(pathlib.PurePosixPath(asset).name, key) == key
                        for asset in value.get('runtime', {})), 'published runtime metadata shadow')


def package_members(package, pin, *, source_binding=None, release_binding=None):
    """Recompute every producer member and reject alternate DLL shadowing."""
    require(release_binding is None or (any(release_binding is item for item in RELEASE_QUALIFIED) and source_binding is None),
            'unknown Creator release qualification binding')
    version = release_binding.version if release_binding is not None else qualification_version(source_binding)
    validate_pin(pin, release_binding=release_binding if release_binding is not None else CURRENT_020)
    with zipfile.ZipFile(package) as archive:
        entries = archive.infolist()
        names = [row.filename for row in entries]
        require(len(names) == len(set(names)) and sum(row.file_size for row in entries) <= 134217728,
                'duplicate or oversized creator package')
        for row in entries:
            path = pathlib.PurePosixPath(row.filename)
            require(not path.is_absolute() and '..' not in path.parts and '\\' not in row.filename
                    and not stat.S_ISLNK(row.external_attr >> 16), 'unsafe creator member')
        for name, expected in pin['members'].items():
            require(PREFIX + name in names and digest(archive.read(PREFIX + name)) == expected,
                    'creator package changed published dependency: ' + name)
        shared_dlls = {pathlib.PurePosixPath(name).name.casefold() for name in pin['members'] if name.endswith('.dll')}
        for name in names:
            if name.endswith('.dll') and pathlib.PurePosixPath(name).name.casefold() in shared_dlls:
                require(name.startswith(PREFIX) and name[len(PREFIX):] in pin['members'], 'alternate dependency DLL shadow')
        deps = read_json(archive.read(PREFIX + 'new-sdd-workspace.deps.json'))
        cli = 'FS.GG.Coord.Cli/' + pin['version']
        require(deps.get('libraries', {}).get(cli, {}).get('type') == 'project'
                and deps.get('targets') and all(
                    target.get('new-sdd-workspace/' + version, {}).get('dependencies', {}).get('FS.GG.Coord.Cli') == pin['version']
                    and 'fsgg-coord-engine.dll' in target.get(cli, {}).get('runtime', {})
                    for target in deps['targets'].values()), 'Creator 0.19 dependency metadata differs')
        # This member already passed the complete original-byte comparison above.
        producer = read_json(archive.read(PREFIX + 'fsgg-coord-engine.deps.json'))
        dependency_metadata(deps, producer, pin)
    return dict(pin['members'])


def evidence(package, source_sha, pin, *, fixtures_passed, source_binding=None, release_binding=None):
    require(fixtures_passed is True, 'published dependency compiled fixtures not accepted')
    require(release_binding is None or (any(release_binding is item for item in RELEASE_QUALIFIED) and source_binding is None),
            'unknown Creator release qualification binding')
    version = release_binding.version if release_binding is not None else qualification_version(source_binding)
    if source_binding is SOURCE_020 or release_binding is not None:
        checker = runpy.run_path(str(ROOT / 'scripts/new-sdd-workspace-release.py'))
        require(checker['package_identity'](package) == (release_binding.package if release_binding is not None else SOURCE_020.package, version, source_sha),
                'Creator source/release qualification package/source identity differs')
    members = package_members(package, pin, source_binding=source_binding, release_binding=release_binding)
    result = {'schema': SCHEMA, 'mode': MODE, 'version': version, 'sourceSha': source_sha,
            'publishedDependencySourceSha': pin['sourceSha'], 'coherentVersion': pin['version'],
            'dependencyPinSha256': pin_digest(pin), 'producerArchiveSha256': pin['archiveSha256'],
            'promotedManifestSha256': pin['promotedManifestSha256'],
            'archiveSha256': digest(regular(package)), 'dependencyMembers': members,
            'fixturesPassed': True, 'installedAdoptionAccepted': False}
    if source_binding is SOURCE_020:
        result.update(sourceQualificationOnly=True, releaseEligible=False)
    return result


def verify_evidence(body, package, manifest, pin, *, source_binding=None):
    require(isinstance(body, dict), 'published dependency evidence body missing')
    expected = evidence(package, manifest['sourceSha'], pin, fixtures_passed=True, source_binding=source_binding)
    require(body == expected, 'published dependency evidence body differs or is source-only')
    require(manifest.get('version') == qualification_version(source_binding) and manifest.get('archiveSha256') == expected['archiveSha256'],
            'Creator manifest/package dependency join differs')
    return expected


def release_evidence(package, source_sha, pin, *, fixtures_passed, release_binding=CURRENT_020):
    """Recompute a closed020, 021, 022 or 023 qualification, never upgrade an old receipt.

    The successful original candidate workflow supplies the compiled fixture verdict;
    its authenticated run/archive is checked independently by the artifact reader.
    Neither this evidence nor its eligibility field grants publication authority.
    """
    require(release_binding is CURRENT_020 or any(release_binding is item for item in RELEASE_QUALIFIED),
            'unknown Creator release qualification binding')
    result = evidence(package, source_sha, pin, fixtures_passed=fixtures_passed,
                      **({'release_binding': release_binding} if release_binding is not CURRENT_020 else {'source_binding': SOURCE_020}))
    schema = 'fsgg.creator0' + release_binding.version.split('.')[1] + '-release-qualification/1'
    return {**result, 'schema': schema,
            'releaseBinding': release_binding.tag, 'sourceQualificationOnly': False, 'releaseEligible': True}


def verify_release_evidence(body, package, manifest, pin, *, release_binding=CURRENT_020):
    require(isinstance(body, dict), 'Creator release qualification body missing')
    expected = release_evidence(package, manifest['sourceSha'], pin, fixtures_passed=True,
                                release_binding=release_binding)
    require(body == expected, 'Creator release qualification differs or is source-only')
    require(manifest.get('packageId') == release_binding.package and manifest.get('version') == release_binding.version
            and manifest.get('tag') == release_binding.tag and manifest.get('archiveSha256') == expected['archiveSha256'],
            'Creator release manifest/package join differs')
    return expected


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('preflight', 'acquire', 'verify-source', 'stage', 'verify-layout', 'verify-package'))
    parser.add_argument('--producer-source', type=pathlib.Path)
    parser.add_argument('--dependencies', type=pathlib.Path)
    parser.add_argument('--package', type=pathlib.Path)
    parser.add_argument('--source-sha')
    args = parser.parse_args()
    pin = load_pin(release_binding=CURRENT_023)
    if args.command == 'preflight':
        print(json.dumps({'mode': MODE, 'dependencyPinSha256': pin_digest(pin)}))
    elif args.command == 'acquire':
        print(json.dumps(acquire(args.package, pin), sort_keys=True))
    elif args.command == 'verify-package':
        print(json.dumps(release_evidence(args.package, args.source_sha, pin, fixtures_passed=True, release_binding=CURRENT_023), sort_keys=True))
    else:
        require(args.producer_source is not None, 'isolated producer source required')
        if args.command == 'verify-source':
            result = old['source_projects'](ROOT, pin, args.producer_source)
        elif args.command == 'stage':
            result = old['stage'](ROOT, args.dependencies, args.package, pin, args.producer_source)
        else:
            result = old['layout'](ROOT, args.dependencies, pin, args.producer_source)
        print(json.dumps(result, sort_keys=True))
