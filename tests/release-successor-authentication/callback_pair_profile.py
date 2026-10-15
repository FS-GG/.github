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
    if profile == SLOT_PROFILE:
        return select_slot(root)
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


SLOT_PROFILE = 'inert-slot-callback-pair/1'
SLOT_BUNDLE = 'b73ce545011cc1156bbcf49f2bb98f174e33b970d221b32c0bad6e83b21b5862'
SLOT_OWNER = 'fca016e1db30a279a5489223d19408f086eb18b5'
SLOT_PRODUCER = '16c28c0419612a358aa572ca5cbc1e3540bdd8d0'
SLOT_GITHUB_ROLES = (
    'scripts/fsgg_feed.py',
    'scripts/release_successor_auth_consumer.py',
    'scripts/release_successor_execution.py',
    'scripts/release_successor_provider.py',
    'tests/release-successor-authentication/callback-contract.json',
    'tests/release-successor-authentication/domain_contract.py',
    'tests/release-successor-authentication/inert_phase_slot_sink.py',
    'tests/release-successor-authentication/slot-sink-callback-entry.py',
    'tests/release-successor-authentication/slot-sink-fixture.py',
    'tests/release-successor-authentication/wire-contract.json',
)


def select_slot(root):
    directory = root / 'tests/release-successor-authentication'
    manifest = json.loads((directory / 'slot-callback-pair-manifest.json').read_bytes(),
                          object_pairs_hook=closed_object)
    if (set(manifest) != {'schema', 'qualification', 'ownerBase', 'producerRevision',
                          'digestRecipe', 'files', 'bundleDigest'} or
            manifest['schema'] != 'fsgg.authentication-callable-source-bundle/1' or
            manifest['ownerBase'] != SLOT_OWNER or manifest['producerRevision'] != SLOT_PRODUCER):
        raise ValueError('wrong slot callback pairing manifest')
    expected = [('FS-GG/.github', path) for path in SLOT_GITHUB_ROLES]
    expected += [('FS-GG/FS.GG.Coordination', path) for path in COORDINATION_ROLES]
    rows = manifest['files']
    if (not isinstance(rows, list) or
            any(set(row) != {'repository', 'path', 'sha256'} for row in rows) or
            [(row['repository'], row['path']) for row in rows] != expected):
        raise ValueError('slot callback pairing requires exact ordered sixteen roles')
    raw = ''.join(row['sha256'] + '  ' + row['repository'] + '/' + row['path'] + '\n'
                  for row in rows).encode('ascii')
    if manifest['bundleDigest'] != SLOT_BUNDLE or hashlib.sha256(raw).hexdigest() != SLOT_BUNDLE:
        raise ValueError('slot callback pairing bundle mismatch')
    for row in rows[:len(SLOT_GITHUB_ROLES)]:
        if hashlib.sha256((root / row['path']).read_bytes()).hexdigest() != row['sha256']:
            raise ValueError('slot callback pairing source mismatch: ' + row['path'])
    return manifest, directory / 'slot-callback-pair-vectors.json'
