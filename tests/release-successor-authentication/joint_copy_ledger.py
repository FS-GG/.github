#!/usr/bin/env python3
"""Offline source-evidence reconciliation; never grants runtime admission."""
from dataclasses import asdict, dataclass
import hashlib
import json
from pathlib import Path
import re

CAP = 65536
HERE = Path(__file__).resolve().parent


def exact(value, keys):
    if type(value) is not dict or set(value) != set(keys):
        raise ValueError('closed ledger shape required')


def label(value):
    if type(value) is not str or not value or len(value) > 512:
        raise ValueError('bounded nonempty literal required')
    return value


@dataclass(frozen=True)
class Provenance:
    source_type: str
    repository: str
    revision: object
    path: str
    sha256: str

    @classmethod
    def read(cls, value):
        exact(value, ('source_type', 'repository', 'revision', 'path', 'sha256'))
        for key in ('source_type', 'repository', 'path', 'sha256'): label(value[key])
        if not re.fullmatch('[a-f0-9]{64}', value['sha256']):
            raise ValueError('source snapshot digest required')
        if value['source_type'] == 'git-blob':
            if type(value['revision']) is not str or not re.fullmatch('[a-f0-9]{40}', value['revision']):
                raise ValueError('immutable Git revision required')
        elif value['source_type'] == 'public-comment':
            if value['revision'] is not None or not re.fullmatch(r'issues/[0-9]+#issuecomment-[0-9]+', value['path']):
                raise ValueError('comment snapshot is not a Git revision')
        else: raise ValueError('unsupported source type')
        return cls(**value)


@dataclass(frozen=True)
class Observation:
    id: str
    provenance: Provenance
    scope: tuple
    cut: str
    role: str
    representation: str
    quantity_type: str
    quantity: object
    ownership: str
    alias_class: object
    identity_basis: str
    uncertainty: str

    @classmethod
    def read(cls, value):
        exact(value, cls.__dataclass_fields__)
        exact(value['scope'], ('topology', 'run', 'process', 'attempt'))
        scope = tuple(label(value['scope'][key]) for key in ('topology', 'run', 'process', 'attempt'))
        provenance = Provenance.read(value['provenance'])
        for key in ('id', 'cut', 'role', 'representation', 'quantity_type', 'ownership', 'identity_basis', 'uncertainty'):
            label(value[key])
        if value['quantity_type'] not in ('logical-bytes', 'physical-byte-lower-bound', 'object-count'):
            raise ValueError('quantity type required')
        quantity = value['quantity']
        if quantity is not None and (type(quantity) is not int or quantity < 0):
            raise ValueError('unknown is null; known quantity is a nonnegative integer')
        alias = value['alias_class']
        if alias is not None: label(alias)
        if value['identity_basis'] not in ('observed-identity', 'modeled-identity', 'population-only'):
            raise ValueError('identity basis required')
        if value['identity_basis'] == 'population-only' and alias is not None:
            raise ValueError('population has no individual object identity')
        if value['quantity_type'] == 'physical-byte-lower-bound' and value['identity_basis'] != 'observed-identity':
            raise ValueError('model roles do not establish physical byte bounds')
        return cls(**dict(value, provenance=provenance, scope=scope))


def reconcile(document):
    exact(document, ('schema', 'observations', 'aliases', 'missing_obligations'))
    if document['schema'] != 'fsgg.joint-copy-ledger/1':
        raise ValueError('unsupported ledger')
    if any(type(document[key]) is not list for key in ('observations', 'aliases', 'missing_obligations')):
        raise ValueError('ledger populations must be explicit arrays')
    observations = tuple(Observation.read(row) for row in document['observations'])
    by_id = {row.id: row for row in observations}
    if len(by_id) != len(observations): raise ValueError('duplicate observation identity')
    obligations = tuple(label(item) for item in document['missing_obligations'])
    if not observations or not obligations:
        raise ValueError('source-only ledger must preserve missing accounting obligations')
    deductions = []
    for alias in document['aliases']:
        exact(alias, ('left', 'right', 'basis'))
        if alias['basis'] != 'same-scoped-object':
            raise ValueError('equal bytes or role names cannot prove aliasing')
        try: left, right = by_id[alias['left']], by_id[alias['right']]
        except (KeyError, TypeError): raise ValueError('unknown alias endpoint') from None
        if left.id == right.id: raise ValueError('alias requires two roles')
        if (left.scope, left.cut) != (right.scope, right.cut):
            raise ValueError('aliases cannot cross topology/run/process/attempt/cut')
        if left.alias_class is None or left.alias_class != right.alias_class or left.identity_basis != right.identity_basis:
            raise ValueError('same object evidence required')
        if left.provenance != right.provenance or left.representation != right.representation:
            raise ValueError('unreconciled representation or provenance')
        deductions.append({'left': left.id, 'right': right.id, 'identity_basis': left.identity_basis})
    # An alias class is an explicit object witness, never inferred from bytes.
    objects = {}
    for row in observations:
        if row.alias_class is None: continue
        key = (row.scope, row.cut, row.identity_basis, row.alias_class)
        prior = objects.get(key)
        if prior and (prior.quantity_type, prior.quantity, prior.representation, prior.provenance) != (
                row.quantity_type, row.quantity, row.representation, row.provenance):
            raise ValueError('inconsistent same-object observations')
        objects[key] = row
    groups = {}
    group_sources = {}
    for row in observations:
        key = (row.scope, row.cut)
        source_identity = (row.provenance, row.identity_basis)
        if key in group_sources and group_sources[key] != source_identity:
            raise ValueError('a compatible cut needs one established provenance and identity basis')
        group_sources[key] = source_identity
        if key not in groups:
            groups[key] = {'scope': dict(zip(('topology', 'run', 'process', 'attempt'), row.scope)),
                          'cut': row.cut, 'observation_ids': [], 'known_object_logical_bytes': None,
                          'physical_byte_lower_bound': None, 'fit': 'unproved'}
        groups[key]['observation_ids'].append(row.id)
    for key, group in groups.items():
        selected = [row for (scope, cut, _, _), row in objects.items() if (scope, cut) == key]
        for kind, field in (('logical-bytes', 'known_object_logical_bytes'),
                            ('physical-byte-lower-bound', 'physical_byte_lower_bound')):
            values = [row.quantity for row in selected if row.quantity_type == kind and row.quantity is not None]
            if values: group[field] = sum(values)
        # A partial physical lower bound may refute fit; logical sums cannot.
        bound = group['physical_byte_lower_bound']
        if bound is not None and bound > CAP: group['fit'] = 'demonstrated-overflow'
    return {'schema': 'fsgg.joint-copy-ledger-result/1',
            'observations': [dict(asdict(row), scope=dict(zip(('topology', 'run', 'process', 'attempt'), row.scope))) for row in observations],
            'groups': list(groups.values()),
            'supported_alias_deductions': deductions, 'missing_obligations': list(obligations),
            'fit': 'demonstrated-overflow' if any(g['fit'] == 'demonstrated-overflow' for g in groups.values()) else 'unproved',
            'admission': False}


def load(path=HERE / 'joint-copy-ledger-inputs.json'):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise ValueError('duplicate JSON key')
            result[key] = value
        return result
    return json.loads(Path(path).read_bytes(), object_pairs_hook=unique)


def verify_local_sources(document, root=HERE.parents[1]):
    """Check local cited source bytes without network or executing remote evidence."""
    for row in document['observations']:
        source = Provenance.read(row['provenance'])
        if source.repository != 'FS-GG/.github' or source.source_type != 'git-blob': continue
        if source.path.startswith('/') or '..' in Path(source.path).parts:
            raise ValueError('source path escapes checkout')
        if hashlib.sha256((root / source.path).read_bytes()).hexdigest() != source.sha256:
            raise ValueError('local source changed: ' + source.path)


if __name__ == '__main__':
    inputs = load()
    verify_local_sources(inputs)
    print(json.dumps(reconcile(inputs), indent=2))
