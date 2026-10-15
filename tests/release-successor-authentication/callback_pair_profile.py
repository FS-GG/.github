"""Closed, immutable inert callback pairing; no factory or native admission."""
import hashlib
import json

PROFILE = 'inert-callback-pair'
BUNDLE = '5720eff0de13dae5396dcff68235c399e8fffea3ba1f14422cb09440ab8a1c07'
OWNER = '90cbcbf8c3dcddbb938d0e7e66bbe28eea67d718'
PRODUCER = '5c6e7073209f4aecf2a0733f9cd2eededfa397c5'
GITHUB_ROLES = (
    'scripts/release-successor-publish.py',
    'scripts/release_successor_auth_consumer.py',
    'scripts/release_successor_provider.py',
    'tests/release-successor-authentication/callback-contract.json',
    'tests/release-successor-authentication/callback-fixture.py',
    'tests/release-successor-authentication/wire-contract.json',
)
COORDINATION_ROLES = (
    'src/FS.GG.Coordination.Orchestration.Execution/FiniteCustodyComposition.fs',
    'src/FS.GG.Coordination.Orchestration.Execution/FiniteCustodySessionAdapter.fs',
    'src/FS.GG.Coordination.Orchestration.Execution/PhaseCallbackProtocol.fs',
    'src/FS.GG.Coordination.Orchestration.Execution/PhaseResultProtocol.fs',
    'src/FS.GG.Coordination.Orchestration.Runner.Client/FiniteCustodyInertAdapter.fs',
    'src/FS.GG.Coordination.Orchestration.Runner.Client/Program.fs',
)


def closed_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate callback profile key')
        result[key] = value
    return result


def select(root, profile):
    if profile != PROFILE:
        raise ValueError('unknown callback pairing profile')
    directory = root / 'tests/release-successor-authentication'
    manifest = json.loads((directory / 'callback-pair-manifest.json').read_bytes(),
                          object_pairs_hook=closed_object)
    if (set(manifest) != {'schema', 'qualification', 'ownerBase', 'producerRevision',
                          'digestRecipe', 'files', 'bundleDigest'} or
            manifest['schema'] != 'fsgg.authentication-callable-source-bundle/1' or
            manifest['ownerBase'] != OWNER or manifest['producerRevision'] != PRODUCER):
        raise ValueError('wrong callback pairing manifest')
    expected = [('FS-GG/.github', path) for path in GITHUB_ROLES]
    expected += [('FS-GG/FS.GG.Coordination', path) for path in COORDINATION_ROLES]
    rows = manifest['files']
    if (not isinstance(rows, list) or
            any(set(row) != {'repository', 'path', 'sha256'} for row in rows) or
            [(row['repository'], row['path']) for row in rows] != expected):
        raise ValueError('callback pairing requires exact ordered twelve roles')
    encoded = ''.join(row['sha256'] + '  ' + row['repository'] + '/' + row['path'] + '\n'
                      for row in rows).encode('ascii')
    if manifest['bundleDigest'] != BUNDLE or hashlib.sha256(encoded).hexdigest() != BUNDLE:
        raise ValueError('callback pairing bundle mismatch')
    for row in rows[:len(GITHUB_ROLES)]:
        if hashlib.sha256((root / row['path']).read_bytes()).hexdigest() != row['sha256']:
            raise ValueError('callback pairing source mismatch: ' + row['path'])
    return manifest, directory / 'callback-pair-vectors.json'
