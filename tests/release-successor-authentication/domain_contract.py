"""Inert ADR-0093 wire/storage construction. No signing, IO backend or native authority."""
import base64
import copy
import hashlib
import json
import re
from dataclasses import dataclass, replace

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
        'firstCause': optional({'phase': choice(*PHASES), 'code': choice('credential-input', 'readback', 'transport', 'deadline', 'child-loss')}),
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
    read = {'v': 3, 'k': 'diagnostic', 'evidence': ack}
    return dict(read=read, custodian=custodian, observation=observation, enrollment=enrollment, descriptor=descriptor, ack=ack, checkpoint=checkpoint,
                control=control, peer=peer, child_ack=child_ack)


def accounting(schemas):
    size = {k: len(canonical(maximum(v))) + (4 if k in ('child_ack', 'ack', 'read') else 0) for k, v in schemas.items()}
    size['frame'] = 2048  # Existing cap including the four-byte header.
    # One mutation at a time per owner. Immutable snapshot/descriptor are admitted
    # once, with no replacement. Count retained + distinct temp + candidate plus
    # peer retained copy. Phase/ACK append has six stable copies on *each* side,
    # plus two possible staging copies on *each* side. Checkpoints are local only.
    counts = {'enrollment': 4, 'descriptor': 4, 'frame': 16, 'ack': 16,
              'checkpoint': 9, 'control': 3, 'peer': 3, 'child_ack': 8,
              'observation': 4, 'custodian': 2}  # immutable stable+candidate each
    rows = {k: {'copies': count, 'bytesEach': size[k], 'bytes': count * size[k]} for k, count in counts.items()}
    rows['diagnosticResponse'] = {'copies': 1, 'bytesEach': size['read'], 'bytes': size['read']}
    return {'rows': rows, 'total': sum(v['bytes'] for v in rows.values()), 'limit': 65536}

@dataclass(frozen=True)
class Model:
    ordinal: int = 0
    pending: bool = False
    uncertain: bool = False
    expired: bool = False
    possible: int = 0
    taken: int = 0
    sent: int = 0
    consumed: int = 0
    stage: str = 'idle'
    first_failure: tuple | None = None

    loss_record: bool = False
    expiry_record: bool = False

    def step(self, event, upper=0, clock_ok=True):
        if event.startswith('failure:'):
            code = event.split(':', 1)[1]
            if code not in ('credential-input', 'readback', 'transport', 'deadline', 'child-loss'):
                raise ValueError('closed failure code')
            if self.first_failure is not None: return self
            if self.uncertain or self.expired: raise ValueError('core is frozen; observation slot only')
            return replace(self, first_failure=(PHASES[min(self.ordinal, 5)], code), uncertain=True)
        if event == 'diagnostic': return self  # No mutation/renewal and no gate.
        if event == 'record-loss':
            if self.loss_record: raise ValueError('append once loss')
            return replace(self, loss_record=True, uncertain=True)
        if event == 'expire':
            if self.expiry_record: raise ValueError('append once expiry')
            return replace(self, expired=True, uncertain=True, expiry_record=True)
        if event in ('loss', 'restart'): return replace(self, uncertain=True)
        if self.uncertain or self.expired: raise ValueError('original hold')
        cutoff = 270 if self.ordinal + 1 <= 2 else 285
        if not clock_ok or type(upper) is not int or not 0 <= upper < cutoff:
            raise ValueError('original clock refuses; no settlement effect')
        if event == 'begin':
            if self.pending or self.ordinal >= 6: raise ValueError('no repeated candidate/seventh phase')
            return replace(self, pending=True, stage='prewrite')
        chain = ('prewrite', 'partialwrite', 'fsync', 'rename', 'dirbarrier', 'readback', 'sign')
        if event in chain[1:]:
            if not self.pending or chain.index(event) != chain.index(self.stage) + 1: raise ValueError('barrier order')
            return replace(self, stage=event)
        n = self.ordinal + 1; bit = 1 << (n - 1)
        if event == 'reserve':
            if self.stage != 'sign' or n not in GATES: raise ValueError('gate readiness')
            return replace(self, possible=self.possible | bit, stage='reserved')
        if event == 'take':
            if self.stage != 'reserved' or self.taken & bit: raise ValueError('TakeAck once')
            return replace(self, taken=self.taken | bit, stage='taken')
        if event == 'full-send':
            if self.stage != 'taken': raise ValueError('ConfirmAckSent requires full send')
            return replace(self, sent=self.sent | bit, stage='sent')
        if event == 'consume':
            if self.stage != 'sent' or self.consumed & bit: raise ValueError('consume once')
            return replace(self, consumed=self.consumed | bit, ordinal=n, pending=False, stage='idle')
        if event == 'commit':
            if self.stage != 'sign' or n in GATES: raise ValueError('gate requires one-use delivery')
            return replace(self, ordinal=n, pending=False, stage='idle')
        raise ValueError('unknown event')


def legacy_bounds(binding_shape):
    """Structural super-set of provider payloads, including infeasible combinations.

    Each enum independently takes its longest canonical value and every nullable
    number takes its largest legal width. Semantic correlations only reduce bytes.
    This bounds *all* legal values, rather than sampling a successful transcript.
    """
    status = choice('unknown', 'verified', 'failed')
    boolean = choice(None, False, True)
    nullable_id = uint()  # 20 decimal bytes dominates null's four.
    facts = {
        'app': {'status': status, 'id': nullable_id},
        'installation': {'status': status, 'id': nullable_id, 'appId': nullable_id,
            'active': boolean, 'selection': choice(None, 'selected', 'all'),
            'repositoryAssociation': status, 'requiredGrants': status},
        'issuance': {'state': choice('not-started', 'possible', 'response-owned', 'unknown'), 'tokenSlot': None},
        'permissions': {'status': status, 'contents': choice('absent', 'read', 'write', 'unknown'),
            'workflows': choice('absent', 'read', 'write', 'unknown'),
            'metadata': choice('absent', 'read', 'write', 'unknown'), 'extra': boolean},
        'expiry': {'status': status, 'unix': uint(I63), 'observedUnix': uint(I63)},
        'census': {'status': status, 'total': uint(), 'repositoryId': uint(), 'nameMatches': boolean},
        'revocation': {'state': choice('not-started', 'possible', 'verified', 'unknown'), 'tokenSlot': None, 'status': None},
        'counters': {'getsStarted': uint(8), 'issuanceReservations': uint(1), 'revocationReservations': uint(1)},
        'firstCause': {'stage': choice(*PHASES, 'metadata', 'issuance', 'permissions', 'expiry', 'census', 'cleanup', 'protocol'),
            'code': choice('identity', 'grant', 'scope', 'expiry', 'transport', 'response', 'deadline', 'cancelled',
                           'credential-input', 'protocol', 'reporting', 'child-loss')}}
    # None above is a width bound, not a semantic rule on actual tokenSlot/status.
    result = {}
    for phase in PHASES:
        payload = {'binding': binding_shape} if phase == 'ready' else {'facts': facts}
        if phase == 'issuance-possible':
            payload['request'] = {'method': 'POST', 'path': '/app/installations/' + str(U64) + '/access_tokens',
                                  'repositoryId': 1269292704, 'contents': 'write', 'workflows': 'write'}
        if phase == 'revocation-possible': payload['request'] = {'method': 'DELETE', 'path': '/installation/token', 'tokenSlot': 1}
        if phase == 'terminal': payload.update(publication={k: 0 for k in ('tag', 'journal', 'feed', 'release', 'asset', 'channel')},
            publicationComplete=False, qualified=False, credentialReferencesDropped=False)
        result[phase] = {'v': 'fsgg.execution.phase-result/1', 'b': 'f' * 64, 'n': 6, 'k': phase, 'p': maximum(payload)}
    return result


def retained_accounting(schemas, frame_bounds):
    result = accounting(schemas)
    # Same original ABI. The closed owner schema's derived bound is stricter;
    # an adapter must reject non-owner payloads before retaining them as facts.
    frame_max = max(4 + len(canonical(v)) for v in frame_bounds.values())
    row = result['rows']['frame']; row.update(bytesEach=frame_max, bytes=row['copies'] * frame_max)
    # Start is retained separately from the ready snapshot, even though both
    # contain the complete binding. Do not hide duplicate binding bytes.
    binding = frame_bounds['ready']['p']['binding']
    start = {'v': 'fsgg.execution.phase-result/1', 'b': 'f' * 64, 'k': 'start', 'p': binding}
    result['rows']['start'] = {'copies': 1, 'bytesEach': 4 + len(canonical(start)), 'bytes': 4 + len(canonical(start))}
    result['total'] = sum(v['bytes'] for v in result['rows'].values())
    result['headroom'] = result['limit'] - result['total']
    return result


@dataclass(frozen=True)
class DiagnosticEvidence:
    exact_retained_bytes: bytes


def diagnostic_read(exact_retained_bytes):
    # Caller receives exact evidence in an observational role; no fresh signature,
    # timestamp, delivery right, typed readback or state transition is generated.
    return DiagnosticEvidence(exact_retained_bytes)


def receipt_matches(receipt, expected, *, authenticated, clock_authenticated, original_upper):
    """Pure admission predicate only; never an authenticator implementation.

    Actual verification against independently enrolled identities must establish
    authenticated. Tests cannot set that flag to prove native/crypto authority.
    """
    if isinstance(receipt, DiagnosticEvidence): return False
    keys = ('d', 'f', 'c', 'q', 'g', 'n', 'p', 'k')
    if not authenticated or not clock_authenticated or any(receipt.get(k) != expected[k] for k in keys): return False
    if receipt['k'] != 'gate' or receipt['n'] not in GATES or receipt['p'] != PHASES[receipt['n'] - 1]: return False
    cutoff = 270 if receipt['n'] in (1, 2) else 285
    return (type(original_upper) is int and 0 <= original_upper < cutoff
            and type(receipt['l']) is int and type(receipt['u']) is int
            and 0 <= receipt['l'] <= receipt['u'] <= original_upper)


def signing_bytes(kind, value):
    domains = {'descriptor': b'FSGG-UTEL-DESCRIPTOR3\0', 'ack': b'FSGG-UTEL-ACK3\0',
               'checkpoint': b'FSGG-UTEL-CHECKPOINT3\0', 'observation': b'FSGG-UTEL-OBSERVATION3\0',
               'custodian': b'FSGG-UTEL-CUSTODIAN2043\0'}
    signature_key = 'signature' if kind in ('descriptor', 'checkpoint') else 's'
    return domains[kind] + canonical({k: v for k, v in value.items() if k != signature_key})
