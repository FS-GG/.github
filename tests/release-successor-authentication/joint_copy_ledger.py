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
        if value['quantity_type'] not in ('logical-bytes', 'physical-byte-lower-bound', 'object-count', 'backing-capacity-bytes'):
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
        if value['quantity_type'] in ('physical-byte-lower-bound', 'backing-capacity-bytes') and value['identity_basis'] != 'observed-identity':
            raise ValueError('observed identity required for physical bounds or backing capacity')
        return cls(**dict(value, provenance=provenance, scope=scope))



# Protected source review must add exact complete-profile pins here. Input documents
# cannot populate this registry. No production upper-bound contract is reviewed yet.
REVIEWED_UPPER_BOUND_CONTRACTS = frozenset()
MAX_ACCOUNTING_INTEGER = (1 << 63) - 1
POPULATION_DOMAINS = ('authority', 'managed', 'python', 'backend', 'peer',
                      'transport-kernel-cache', 'diagnostic-export', 'observation')
COMPLETENESS_OBLIGATIONS = ('closed-call-graph', 'all-representations',
    'headers-rounding-storage', 'uncertainty-replacements', 'concurrency-restart',
    'diagnostic-recipient-population', 'transient-exclusions', 'runtime-source-layout')
UPPER_BOUND_METRICS = ('capacity_bytes', 'header_bytes', 'allocator_rounding_bytes',
                       'storage_metadata_bytes', 'transport_cache_bytes')


def profile_digest(profile):
    return hashlib.sha256(json.dumps(profile, sort_keys=True,
        separators=(',', ':'), ensure_ascii=True).encode('ascii')).hexdigest()


def bounded_integer(value):
    if value is not None and (type(value) is not int or not 0 <= value <= MAX_ACCOUNTING_INTEGER):
        raise ValueError('finite accounting integer or explicit null required')
    return value


def unique_labels(values):
    if type(values) is not list or not values:
        raise ValueError('nonempty closed inventory required')
    result = tuple(label(value) for value in values)
    if len(set(result)) != len(result): raise ValueError('duplicate inventory entry')
    return result


def reconcile_upper_bound(profile):
    """Offline reviewed-source accounting only; never authenticates a live runtime."""
    exact(profile, ('schema', 'profile', 'runtime', 'topology', 'source',
                    'inventory', 'components', 'obligations'))
    if profile['schema'] != 'fsgg.copy-upper-bound/1':
        raise ValueError('unsupported upper-bound profile')
    label(profile['profile']); label(profile['topology'])
    exact(profile['runtime'], ('implementation', 'version', 'build', 'architecture', 'allocator', 'storage'))
    for value in profile['runtime'].values(): label(value)
    if profile['source'] is not None:
        source = Provenance.read(profile['source'])
        if source.source_type != 'git-blob': raise ValueError('immutable upper-bound source required')
    exact(profile['inventory'], POPULATION_DOMAINS)
    unresolved, roles = [], set()
    if profile['source'] is None: unresolved.append('profile-source')
    for domain, inventory in profile['inventory'].items():
        if inventory is None:
            unresolved.append('inventory:' + domain)
        else:
            for role in unique_labels(inventory): roles.add(domain + '/' + role)
    if type(profile['components']) is not list or type(profile['obligations']) is not list:
        raise ValueError('explicit upper-bound populations required')
    seen_roles, classes, total = set(), set(), 0
    for component in profile['components']:
        exact(component, ('allocation_class', 'roles', 'profile', 'runtime', 'topology',
                          'source', 'quantity_type', 'maximum_multiplicity', 'metrics',
                          'lifetime', 'alias_basis'))
        allocation = label(component['allocation_class'])
        if allocation in classes: raise ValueError('allocation class must be counted once')
        classes.add(allocation)
        selected_roles = set(unique_labels(component['roles']))
        if selected_roles - roles or seen_roles & selected_roles:
            raise ValueError('unknown or multiply assigned allocation role')
        seen_roles.update(selected_roles)
        if (component['profile'], component['runtime'], component['topology']) != (
                profile['profile'], profile['runtime'], profile['topology']):
            raise ValueError('incompatible profile/runtime/topology')
        evidence = Provenance.read(component['source'])
        if evidence.source_type != 'git-blob': raise ValueError('immutable allocation proof required')
        if component['quantity_type'] != 'physical-allocation-upper-bound-bytes':
            raise ValueError('capacities, logical sizes and lower bounds are not upper bounds')
        basis = 'single-role' if len(selected_roles) == 1 else 'reviewed-shared-allocation'
        if component['alias_basis'] != basis:
            raise ValueError('allocation aliases require reviewed identity proof')
        exact(component['metrics'], UPPER_BOUND_METRICS)
        values = [bounded_integer(component['metrics'][key]) for key in UPPER_BOUND_METRICS]
        multiplicity = bounded_integer(component['maximum_multiplicity'])
        if multiplicity == 0: raise ValueError('a represented allocation needs positive multiplicity')
        exact(component['lifetime'], ('policy', 'ownership', 'reservation_proof'))
        lifetime = component['lifetime']
        if lifetime['policy'] != 'cumulative-through-operation-and-uncertainty':
            raise ValueError('no task/GC/restart release discount is admitted')
        if lifetime['ownership'] not in ('reserved', 'unknown', 'expired'):
            raise ValueError('explicit reservation ownership required')
        if lifetime['reservation_proof'] is not None: label(lifetime['reservation_proof'])
        if (None in values or multiplicity is None or lifetime['ownership'] != 'reserved'
                or lifetime['reservation_proof'] is None):
            unresolved.append('allocation:' + allocation)
        else:
            amount = sum(values) * multiplicity
            if amount > MAX_ACCOUNTING_INTEGER or total + amount > MAX_ACCOUNTING_INTEGER:
                raise ValueError('upper-bound arithmetic overflow')
            total += amount
    unresolved.extend('role:' + role for role in sorted(roles - seen_roles))
    obligation_ids = set()
    for obligation in profile['obligations']:
        exact(obligation, ('id', 'status', 'source', 'proof'))
        identity = label(obligation['id'])
        if identity not in COMPLETENESS_OBLIGATIONS or identity in obligation_ids:
            raise ValueError('unknown or duplicate completeness obligation')
        obligation_ids.add(identity)
        if obligation['status'] not in ('unknown', 'proved'):
            raise ValueError('explicit completeness status required')
        if obligation['source'] is not None:
            if Provenance.read(obligation['source']).source_type != 'git-blob':
                raise ValueError('immutable completeness proof required')
        if obligation['proof'] is not None: label(obligation['proof'])
        if obligation['status'] != 'proved' or obligation['source'] is None or obligation['proof'] is None:
            unresolved.append('obligation:' + identity)
    unresolved.extend('obligation:' + item for item in COMPLETENESS_OBLIGATIONS if item not in obligation_ids)
    # A plausible hash, source citation or "proved" field is not reviewed evidence.
    if (profile['profile'], profile_digest(profile)) not in REVIEWED_UPPER_BOUND_CONTRACTS:
        unresolved.append('authentic-reviewed-contract')
    complete = not unresolved
    return {'schema': 'fsgg.copy-upper-bound-result/1',
            'profile': profile['profile'], 'status': 'finite-complete' if complete else 'unresolved',
            'physical_upper_bound_bytes': total if complete else None,
            'unresolved': unresolved, 'admission': False}


def reconcile(document):
    keys = ('schema', 'observations', 'aliases', 'missing_obligations')
    exact(document, keys + (('upper_bound_profile',) if 'upper_bound_profile' in document else ()))
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
                          'physical_byte_lower_bound': None, 'known_object_backing_capacity_bytes': None, 'fit': 'unproved'}
        groups[key]['observation_ids'].append(row.id)
    for key, group in groups.items():
        selected = [row for (scope, cut, _, _), row in objects.items() if (scope, cut) == key]
        for kind, field in (('logical-bytes', 'known_object_logical_bytes'),
                            ('physical-byte-lower-bound', 'physical_byte_lower_bound'),
                            ('backing-capacity-bytes', 'known_object_backing_capacity_bytes')):
            values = [row.quantity for row in selected if row.quantity_type == kind and row.quantity is not None]
            if values: group[field] = sum(values)
        # Conditional on the supplied bound; this parser does not validate a physical measurement.
        # Logical sums cannot establish even this conditional physical overflow.
        bound = group['physical_byte_lower_bound']
        if bound is not None and bound > CAP: group['fit'] = 'supplied-bound-overflow'
    result = {'schema': 'fsgg.joint-copy-ledger-result/1',
            'observations': [dict(asdict(row), scope=dict(zip(('topology', 'run', 'process', 'attempt'), row.scope))) for row in observations],
            'groups': list(groups.values()),
            'supported_alias_deductions': deductions, 'missing_obligations': list(obligations),
            'fit': 'supplied-bound-overflow' if any(g['fit'] == 'supplied-bound-overflow' for g in groups.values()) else 'unproved',
            'admission': False}
    if 'upper_bound_profile' in document:
        result['upper_bound'] = reconcile_upper_bound(document['upper_bound_profile'])
    return result


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
