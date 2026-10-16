"""ADR-0093 codecs and descriptor policy controller.

No concrete admission, storage, cryptographic, credential or deployment adapters.
Injected ports are trusted components; construction is never enrollment evidence.
"""
import base64
import hashlib
import json
import re
import threading
from dataclasses import dataclass

PHASES = ('ready', 'issuance-possible', 'token-owned', 'revocation-possible', 'revoked', 'terminal')
GATES = (1, 2, 4)
U64 = (1 << 64) - 1
I63 = (1 << 63) - 1

@dataclass(frozen=True)
class Field:
    kind: str
    bound: object


def uint(n=U64, minimum=0): return Field('uint', (minimum, n))
def hexdigest(n=64): return Field('hex', n)
def choice(*values): return Field('enum', values)
def signature(): return Field('signature', 64)
def optional(shape): return Field('optional', shape)


def cause_shape():
    return {'phase': choice(*PHASES),
            'code': choice('credential-input', 'readback', 'transport', 'deadline', 'child-loss')}


def maximum(shape):
    if isinstance(shape, Field):
        if shape.kind == 'optional': return maximum(shape.bound)
        if shape.kind == 'uint': return shape.bound[1]
        if shape.kind == 'hex': return 'f' * shape.bound
        if shape.kind == 'signature': return base64.urlsafe_b64encode(b'\xff' * shape.bound).rstrip(b'=').decode()
        return max(shape.bound, key=lambda value: len(canonical(value)))
    if type(shape) is dict: return {k: maximum(v) for k, v in shape.items()}
    if type(shape) is list: return [maximum(v) for v in shape]
    return shape


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode('ascii')


def validate(value, shape):
    if isinstance(shape, Field):
        if shape.kind == 'optional':
            if value is not None: validate(value, shape.bound)
            return
        if shape.kind == 'uint': ok = type(value) is int and shape.bound[0] <= value <= shape.bound[1]
        elif shape.kind == 'hex': ok = type(value) is str and re.fullmatch('[0-9a-f]{%d}' % shape.bound, value) is not None
        elif shape.kind == 'enum': ok = any(type(value) is type(member) and value == member for member in shape.bound)
        else:
            try:
                raw = base64.b64decode(value + '==', altchars=b'-_', validate=True)
                ok = len(raw) == shape.bound and base64.urlsafe_b64encode(raw).rstrip(b'=').decode() == value
            except (ValueError, TypeError): ok = False
        if not ok: raise ValueError('closed field differs')
    elif type(shape) is dict:
        if type(value) is not dict or value.keys() != shape.keys(): raise ValueError('closed keys differ')
        for k in shape: validate(value[k], shape[k])
    elif type(shape) is list:
        if type(value) is not list or len(value) != len(shape): raise ValueError('closed count differs')
        for v, s in zip(value, shape): validate(v, s)
    elif type(value) is not type(shape) or value != shape: raise ValueError('fixed original differs')


def decode(raw, shape):
    def unique(pairs):
        result = {}
        for k, v in pairs:
            if k in result: raise ValueError('duplicate key')
            result[k] = v
        return result
    value = json.loads(raw.decode('ascii'), object_pairs_hook=unique)
    validate(value, shape)
    if canonical(value) != raw: raise ValueError('noncanonical')
    return value


def shapes(original, binding_digest, ready_digest):
    """Original rows supplied by exact protected publisher AST, never caller trust roots.

    Binding/source/runtime/original-clock bytes are retained inside legacy ready;
    signed descriptor binds their exact digest and that complete ready frame.
    No digest stands in for an unretained original.
    """
    workflow = {'repositoryId': 1269292704, 'path': '.github/workflows/release-successor-authenticate.yml',
                'revision': hexdigest(40), 'run': uint(minimum=1), 'attempt': uint((1 << 32) - 1, minimum=1)}
    enrollment = {'v': 3, 'policy': hexdigest(), 'issuer': hexdigest(), 'ack': hexdigest(),
                  'reporter': hexdigest(), 'custodian': hexdigest(), 'supervisor': hexdigest(), 'storeGeneration': uint()}
    descriptor = {'v': 3, 'binding': binding_digest, 'ready': ready_digest,
        'candidate': original['ORIGINAL_0103_CANDIDATE'],
        'dependencies': original['ORIGINAL_0103_DEPENDENCIES'],
        'compatibleDependencies': original['ORIGINAL_0103_DESCENDANT_DEPENDENCIES'],
        'originalCheckpoint': original['ORIGINAL_0103_TAG_CHECKPOINT'],
        'authentication': workflow, 'enrollment': hexdigest(), 'signature': signature()}
    ack = {'v': 3, 'k': choice('gate', 'diagnostic', 'loss'), 'd': hexdigest(), 'f': hexdigest(),
           'c': hexdigest(), 'q': hexdigest(), 'g': uint(), 'n': uint(6, minimum=1), 'p': choice(*PHASES),
           'l': uint(I63), 'u': uint(I63), 's': signature()}
    checkpoint = {'v': 3, 'descriptor': hexdigest(), 'previous': hexdigest(), 'ordinal': uint(6),
                  'frame': hexdigest(), 'priorPhaseAck': hexdigest(), 'childAck': hexdigest(),
                  'control': hexdigest(), 'signature': signature()}
    control = {'v': 3, 'descriptor': hexdigest(), 'generation': uint(), 'ordinal': uint(6),
        'candidate': hexdigest(), 'priorCheckpoint': hexdigest(), 'gatePossible': uint(63),
        'firstCause': optional(cause_shape()),
        'loss': choice('none', 'job', 'runner', 'vm', 'service'),
        'terminal': choice('open', 'unknown', 'qualified', 'unqualified'),
        'retention': choice('retained', 'RetentionExpired'), 'originalCutoff': uint(I63),
        'expiry': uint(I63), 'uncertain': choice(False, True)}
    peer = {'v': 3, 'descriptor': hexdigest(), 'processBirth': hexdigest(), 'challenge': hexdigest(),
        'pendingPurpose': choice('gate', 'diagnostic', 'loss'), 'ordinal': uint(6),
        'taken': uint(63), 'sent': uint(63), 'consumed': uint(63),
        'unknown': choice(False, True), 'originalUpper': uint(I63)}
    child_ack = {'v': 'fsgg.execution.phase-result/1', 'k': 'ack', 'b': hexdigest(), 'n': uint(6, minimum=1), 'h': hexdigest()}
    # Read response is *only* an exact previously retained envelope, typed by the
    # request. It never signs/restamps a gate. One response buffer reserved below.
    observation = {'v': 3, 'd': hexdigest(), 'q': hexdigest(), 'n': uint(6),
        'k': choice('loss', 'expiry', 'custodian-204'), 'l': uint(I63), 'u': uint(I63),
        'e': hexdigest(), 'clockKnown': choice(False, True),
        'result': choice('observed', 'Unknown', 'RetentionExpired'), 'tokenSlot': 1, 'method': 'DELETE',
        'path': '/installation/token', 'status': 204, 's': signature()}
    custodian = dict(observation, k='custodian-204')
    observation = {k: v for k, v in observation.items() if k not in ('tokenSlot', 'method', 'path', 'status')}
    observation['k'] = choice('loss', 'expiry')
    # A later cause belongs to its append-once observation, never frozen core.
    observation['lateCause'] = optional(cause_shape())
    read = {'v': 3, 'k': 'diagnostic', 'evidence': ack}
    return dict(read=read, custodian=custodian, observation=observation, enrollment=enrollment, descriptor=descriptor, ack=ack, checkpoint=checkpoint,
                control=control, peer=peer, child_ack=child_ack)


def signing_bytes(kind, value):
    domains = {'descriptor': b'FSGG-UTEL-DESCRIPTOR3\0', 'ack': b'FSGG-UTEL-ACK3\0',
               'checkpoint': b'FSGG-UTEL-CHECKPOINT3\0', 'observation': b'FSGG-UTEL-OBSERVATION3\0',
               'custodian': b'FSGG-UTEL-CUSTODIAN2043\0'}
    signature_key = 'signature' if kind in ('descriptor', 'checkpoint') else 's'
    return domains[kind] + canonical({k: v for k, v in value.items() if k != signature_key})


class Refused(ValueError):
    """No issuance is authorized; diagnostics never grant a retry."""


class IssuerAdmission:
    """Trusted port: independently authenticate policy, request and external evidence.

    authenticate returns closed admission bytes, or raises. recheck returns a
    fresh closed clock interval after durable reservation. Neither accepts a
    reporter assertion as proof. See domain-authority-service.md for obligations.
    """
    def authenticate(self, policy, request, evidence):
        raise NotImplementedError

    def recheck(self, policy, request, admission):
        raise NotImplementedError


class IssuanceStore:
    """Trusted atomic durable one-attempt store, shared across every issuer process.

    reserve_once consumes the stable original key before returning the exact
    commitment. Existing/uncertain/unreadable state must raise. Failed reservation
    attempts remain consumed on recovery. complete retains the exact descriptor;
    failure never clears reservation. observe has no issuance/repair side effects.
    """
    def reserve_once(self, original_key, commitment):
        raise NotImplementedError

    def complete(self, original_key, commitment, descriptor):
        raise NotImplementedError

    def observe(self, original_key):
        raise NotImplementedError


class DescriptorSigner:
    """Trusted descriptor-only port with enrolled issuer key and original cutoff.

    Independently enforce reservation, current policy/clock and exact descriptor
    domain at dispatch. Return a canonical 64-byte signature string; never retry
    an uncertain signing request. This is not a reporter-facing sign-bytes API.
    """
    def issue_descriptor(self, descriptor_signing_bytes, commitment, admission):
        raise NotImplementedError


def _document(raw):
    # Immutable byte input avoids caller hooks, subclass equality and copy hooks.
    if type(raw) is not bytes or len(raw) > 65536:
        raise Refused('bounded immutable canonical bytes required')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise Refused('duplicate key')
            result[key] = value
        return result
    try:
        value = json.loads(raw.decode('ascii'), object_pairs_hook=unique)
        if type(value) is not dict or canonical(value) != raw:
            raise Refused('canonical object required')
        return value
    except (ValueError, UnicodeError, TypeError, RecursionError) as error:
        raise Refused('invalid canonical document') from error


def _keys(value, names):
    if type(value) is not dict or set(value) != set(names.split()):
        raise Refused('closed document keys differ')


def _digest(raw):
    return hashlib.sha256(raw).hexdigest()


_ADMISSION = {'v': 1, 'policy': hexdigest(), 'request': hexdigest(),
              'identityEvidence': hexdigest(), 'permissionEvidence': hexdigest(),
              'enrollmentEvidence': hexdigest(), 'populationEvidence': hexdigest(),
              'clockEvidence': hexdigest(), 'domain': hexdigest(),
              'lower': uint(I63), 'upper': uint(I63)}


class DescriptorAuthority:
    """One original descriptor attempt, with no native/default construction path.

    policy is independently provisioned immutable canonical bytes. Passing bytes
    or injected ports does not authenticate them. Only a qualified trusted
    admission adapter can supply the admission result used here.
    """
    def __init__(self, policy=None, *, admission=None, store=None, signer=None):
        if (not isinstance(admission, IssuerAdmission) or not isinstance(store, IssuanceStore)
                or not isinstance(signer, DescriptorSigner)):
            raise Refused('qualified trusted adapters required; native adapters unavailable')
        value = _document(policy)
        _keys(value, 'v original binding ready authentication enrollment population')
        if type(value['v']) is not int or value['v'] != 1:
            raise Refused('policy version differs')
        original = value['original']
        _keys(original, 'ORIGINAL_0103_CANDIDATE ORIGINAL_0103_DEPENDENCIES '
              'ORIGINAL_0103_DESCENDANT_DEPENDENCIES ORIGINAL_0103_TAG_CHECKPOINT')
        if type(original['ORIGINAL_0103_DEPENDENCIES']) is not dict or len(original['ORIGINAL_0103_DEPENDENCIES']) != 9:
            raise Refused('all nine protected dependencies required')
        # Reuse the exact maintained legacy ABI validator without selecting its
        # deliberately unavailable native adapter or changing its implementation.
        from release_successor_provider import validate_auth_binding, encode_auth_frame
        binding = value['binding']
        digest = validate_auth_binding(binding)
        if binding['mode'] != 'native': raise Refused('original native identity required')
        clock = binding['clock']; start = int(clock['start']); frequency = int(clock['frequency'])
        if any(int(clock[name]) != start + seconds * frequency
               for name, seconds in (('normal', 270), ('work', 285), ('total', 300))):
            raise Refused('original cutoffs cannot be renewed')
        ready = encode_auth_frame(binding, 1, 'ready', {'binding': binding})
        if type(value['ready']) is not str or ready.hex() != value['ready']:
            raise Refused('exact retained ready differs')
        schemas = shapes(original, digest, _digest(ready))
        validate(value['authentication'], schemas['descriptor']['authentication'])
        if (value['authentication']['run'], value['authentication']['attempt']) == (binding['workflow']['run'], binding['workflow']['attempt']):
            raise Refused('executing authentication must be distinct from original publisher')
        validate(value['enrollment'], schemas['enrollment'])
        roles = value['enrollment']
        if roles['issuer'] in (roles['ack'], roles['reporter'], roles['custodian']):
            raise Refused('issuer custody must be distinct')
        population = value['population']
        validate(population, {'v': 1, 'scope': 'complete-physical-population',
                             'evidence': hexdigest(), 'bytes': uint(65536, minimum=1), 'limit': 65536})
        descriptor = maximum(schemas['descriptor'])
        descriptor['authentication'] = value['authentication']
        descriptor['enrollment'] = _digest(canonical(roles))
        validate(descriptor, schemas['descriptor'])
        self._policy = policy
        self._expected = canonical({key: value[key] for key in ('binding', 'ready', 'authentication')})
        self._descriptor = canonical(descriptor)
        self._shape = schemas['descriptor']
        # Stable across policy, enrollment, execution and clock revisions: none
        # can mint another attempt for the same original candidate/checkpoint.
        self._key = _digest(canonical({'operation': binding['operation'], 'generation': binding['originalGeneration'],
            'candidate': original['ORIGINAL_0103_CANDIDATE'], 'checkpoint': original['ORIGINAL_0103_TAG_CHECKPOINT']}))
        self._domain = _digest(canonical(clock))
        self._population = population['evidence']
        self._start = start
        self._normal = int(clock['normal'])
        self._admission, self._store, self._signer = admission, store, signer
        self._lock = threading.Lock()
        self._state, self._first_failure = 'Unissued', None

    def _check_admission(self, raw, request):
        value = _document(raw)
        validate(value, _ADMISSION)
        if (value['policy'] != _digest(self._policy) or value['request'] != _digest(request)
                or value['domain'] != self._domain or value['populationEvidence'] != self._population
                or not self._start <= value['lower'] <= value['upper'] < self._normal):
            raise Refused('authenticated original admission differs or expired')
        return value

    def issue(self, request, evidence):
        # Reentrant callbacks and competing threads refuse rather than waiting
        # into a later clock window or requesting another signature.
        if not self._lock.acquire(blocking=False): raise Refused('issuance already active')
        try:
            if self._state != 'Unissued': raise Refused('original attempt consumed')
            _document(request)
            if request != self._expected or type(evidence) is not bytes:
                raise Refused('original identity/ready/authentication differs')
            admission = self._admission.authenticate(self._policy, request, evidence)
            initial = self._check_admission(admission, request)
            descriptor = _document(self._descriptor)
            to_sign = signing_bytes('descriptor', descriptor)
            commitment = canonical({'policy': _digest(self._policy), 'request': _digest(request),
                                    'descriptor': _digest(to_sign), 'original': self._key})
            # Consume locally BEFORE the durable port: even a lost reservation
            # result cannot authorize a local retry. The port owns cold recovery.
            self._state = 'Unknown'
            stage = 'reservation'
            try:
                reserved = self._store.reserve_once(self._key, commitment)
                if type(reserved) is not bytes or reserved != commitment:
                    raise Refused('durable reservation readback differs')
                stage = 'clock'
                fresh = self._admission.recheck(self._policy, request, admission)
                current = self._check_admission(fresh, request)
                if (current['lower'] < initial['lower'] or current['upper'] < initial['upper']
                        or any(current[k] != initial[k] for k in initial if k not in ('lower', 'upper', 'clockEvidence'))):
                    raise Refused('clock rollback or admission substitution')
                stage = 'signer'
                signed = self._signer.issue_descriptor(to_sign, commitment, fresh)
                if type(signed) is not str: raise Refused('signature encoding differs')
                validate(signed, signature())
                descriptor['signature'] = signed
                validate(descriptor, self._shape)
                raw = canonical(descriptor)
                stage = 'completion'
                retained = self._store.complete(self._key, commitment, raw)
                if type(retained) is not bytes or retained != raw:
                    raise Refused('descriptor completion readback differs')
                self._state = 'Issued'
                return raw
            except BaseException:
                # No exception details: a signer or transport may include secrets.
                if self._first_failure is None: self._first_failure = stage
                raise Refused('issuance uncertain; original attempt permanently held') from None
        finally:
            self._lock.release()

    def diagnostic(self):
        """Read only local status: no store recovery, fresh signature or retry right."""
        return {'state': self._state, 'firstFailure': self._first_failure}
