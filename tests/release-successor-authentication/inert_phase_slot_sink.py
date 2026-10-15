"""Source-only typed slot model. No filesystem, transport, signer or native backend."""
import copy
import hashlib
import threading
from dataclasses import dataclass
from types import MappingProxyType

import domain_contract as contract
import release_successor_provider as provider

Refused = provider.Refused
REPORT = {'schema': 'fsgg.release-successor-result/1', 'sink': 'inert-phase-slots/1'}


def _closed_report(value):
    # Independent of the exported convenience dict, including caller mutation.
    return (type(value) is dict and len(value) == 2
            and all(type(key) is str and type(item) is str for key, item in value.items())
            and value == {'schema': 'fsgg.release-successor-result/1', 'sink': 'inert-phase-slots/1'})


class BackendCut(OSError):
    def __init__(self, stage):
        super().__init__('controlled ambiguous cut: ' + stage)
        self.stage = stage


ROLES = MappingProxyType({**{f'phase-{n}': 2048 for n in range(1, 7)},
         **{role: 2048 for role in ('old', 'candidate', 'temporary', 'readback')},
         **{f'{kind}-{role}': 512 for kind in ('loss', 'expiry')
            for role in ('candidate', 'file', 'readback')}})
MAXIMUM_BYTES = sum(ROLES.values())


def population():
    return {'roles': dict(ROLES), 'maximumBytes': MAXIMUM_BYTES}


@dataclass(frozen=True)
class Reservation:
    identity: bytes
    generation: int


@dataclass(frozen=True)
class Receipt:
    original: bytes
    generation: int
    slot: str
    ordinal: int
    digest: str
    written: bool
    barrier: bool
    fresh_readback: bool


class ControlledSlotBackend:
    """Explicit byte stores and named failure cuts; never a durable implementation.

    The old/candidate/temporary/readback stores are independent counted bytearrays.
    Aliasing the completed temporary as a stable phase adds no further copy.
    Reservations persist in this object across reconstructed sink instances.
    """
    def __init__(self):
        self.reservation = None
        self.stores = {}
        self.attempts = set()
        self.events = []
        self.cut = None
        self.peak_bytes = 0

    def reserve(self, identity, required_roles, maximum_bytes):
        if (self.reservation is not None or required_roles != ROLES
                or maximum_bytes != MAXIMUM_BYTES):
            raise Refused('reservation/restart refused')
        self.reservation = Reservation(identity, __import__('json').loads(identity)['storeGeneration'])
        return self.reservation

    def _store(self, role, raw):
        if type(raw) is not bytes or len(raw) > ROLES[role]:
            raise Refused('typed slot overflow')
        self.stores[role] = bytearray(raw)
        self.peak_bytes = max(self.peak_bytes, sum(len(v) for v in self.stores.values()))
        if self.peak_bytes > MAXIMUM_BYTES:
            raise AssertionError('reservation exceeded')

    def _stage(self, stage):
        self.events.append(stage)
        if self.cut == stage:
            raise BackendCut(stage)

    def _append(self, reservation, slot, raw, ordinal, observation=False):
        if reservation is not self.reservation or slot in self.attempts:
            raise Refused('one attempt per original slot')
        self.attempts.add(slot)  # Before the first possible mutation.
        if observation:
            candidate, temporary, readback = (slot + '-' + r for r in ('candidate', 'file', 'readback'))
        else:
            candidate, temporary, readback = 'candidate', 'temporary', 'readback'
            old = self.stores.get(f'phase-{ordinal - 1}', b'')
            self._store('old', bytes(old))
        self._store(candidate, raw)
        self._stage('candidate')
        self._store(temporary, raw)
        self._stage('write')
        self._stage('file-barrier')
        if not observation:
            self.stores[slot] = self.stores.pop(temporary)
        self._stage('replace')
        self._stage('directory-barrier')
        actual = bytes(self.stores[temporary if observation else slot])
        self._store(readback, actual)
        self._stage('readback')
        if self.cut == 'mismatch':
            actual = b'!' + actual[1:]
        if actual != raw:
            raise BackendCut('readback')
        receipt = Receipt(reservation.identity, reservation.generation, slot, ordinal,
                          hashlib.sha256(actual).hexdigest(), True, True, True)
        if not observation:
            for role in ('old', 'candidate', 'temporary', 'readback'):
                self.stores.pop(role, None)
        return receipt

    def append_phase(self, reservation, exact_frame):
        phase = provider.decode_auth_frame(exact_frame)
        return self._append(reservation, f"phase-{phase['n']}", exact_frame, phase['n'])

    def observe_loss_or_expiry(self, reservation, exact_envelope):
        import json
        row = json.loads(exact_envelope)
        return self._append(reservation, row['kind'], exact_envelope, row['ordinal'], True)

    def inspect_original(self, identity):
        # Diagnostic data supplies neither an ACK nor permission to retry.
        if self.reservation is None or identity != self.reservation.identity:
            return {'result': 'Unknown', 'attempts': ()}
        return {'result': 'Unknown', 'attempts': tuple(sorted(self.attempts))}


class InertPhaseSlotSink:
    @classmethod
    def prepare(cls, independently_expected_binding, original_sink_identity,
                closed_report_envelope, original_window, reserved_population, slot_backend):
        if type(original_sink_identity) is not dict:
            raise Refused('closed inert preparation refused')
        # Snapshot once before any caller-controlled preparation hooks. Exact
        # scalar types make this shallow copy a stable literal identity.
        identity = original_sink_identity.copy()
        if (len(identity) != 4 or any(type(key) is not str for key in identity)
                or set(identity) != {'path', 'binding', 'invocation', 'storeGeneration'}
                or any(type(identity[key]) is not str for key in ('path', 'binding', 'invocation'))
                or type(identity['storeGeneration']) is not int):
            raise Refused('closed inert preparation refused')
        binding = copy.deepcopy(independently_expected_binding)
        digest = provider.validate_auth_binding(binding)
        if (binding['mode'] != 'inert' or not 0 < len(identity['path']) <= 512
                or identity['binding'] != digest or identity['invocation'] != binding['invocation']
                or not 1 <= identity['storeGeneration'] <= contract.U64
                or not _closed_report(closed_report_envelope)
                or original_window != binding['clock'] or reserved_population != population()
                or type(slot_backend) is not ControlledSlotBackend):
            raise Refused('closed inert preparation refused')
        result = cls()
        result.binding, result.digest = binding, digest
        result.identity = contract.canonical(identity)
        result.path = identity['path']
        result.backend = slot_backend
        result.reservation = slot_backend.reserve(result.identity, ROLES, MAXIMUM_BYTES)
        result.ordinal = 0
        result.previous = None
        result.previous_phase = None
        result.first_cause = None
        result.frozen = False
        result.observations = set()
        result._lock = threading.Lock()
        return result

    def _receipt(self, receipt, slot, ordinal, raw):
        expected = Receipt(self.identity, self.reservation.generation, slot, ordinal,
                           hashlib.sha256(raw).hexdigest(), True, True, True)
        if (type(receipt) is not Receipt or receipt != expected
                or type(receipt.generation) is not int or type(receipt.ordinal) is not int
                or any(value is not True for value in
                       (receipt.written, receipt.barrier, receipt.fresh_readback))):
            raise Refused('typed barrier/readback receipt differs')

    def _freeze(self, phase, code):
        self.frozen = True
        if self.first_cause is None:
            self.first_cause = {'phase': phase, 'code': code}

    def __call__(self, path, report, binding, exact_frame):
        if not self._lock.acquire(blocking=False):
            raise Refused('serial slot entry required')
        try:
            if (self.frozen or str(path) != self.path or not _closed_report(report)
                    or binding != self.binding or type(exact_frame) is not bytes or len(exact_frame) > 2048):
                raise Refused('prepared sink identity/state differs')
            phase = provider.decode_auth_frame(exact_frame)
            ordinal = self.ordinal + 1
            allowed = {None: {'ready'}, 'ready': {'issuance-possible', 'terminal'},
                       'issuance-possible': {'token-owned', 'terminal'},
                       'token-owned': {'revocation-possible'},
                       'revocation-possible': {'revoked', 'terminal'}, 'revoked': {'terminal'},
                       'terminal': set()}
            if (set(phase) != {'v', 'b', 'n', 'k', 'p'} or phase['v'] != provider.AUTH_PROTOCOL
                    or phase['b'] != self.digest or type(phase['n']) is not int
                    or not 1 <= ordinal <= 6 or phase['n'] != ordinal
                    or phase['k'] not in allowed[self.previous_phase]):
                raise Refused('phase identity/order differs')
            provider.validate_auth_payload(phase['k'], phase['p'], self.binding, self.previous)
            try:
                receipt = self.backend.append_phase(self.reservation, exact_frame)
            except Exception as error:
                code = 'readback' if isinstance(error, BackendCut) and error.stage == 'readback' else 'transport'
                self._freeze(phase['k'], code)
                raise Refused('Unknown: original slot attempt consumed') from None
            try:
                self._receipt(receipt, f'phase-{ordinal}', ordinal, exact_frame)
            except Exception:
                self._freeze(phase['k'], 'readback')
                raise Refused('Unknown: original slot attempt consumed') from None
            self.ordinal = ordinal
            self.previous_phase = phase['k']
            self.previous = copy.deepcopy(phase['p'].get('facts'))
            return {'binding': self.digest, 'ordinal': ordinal, 'frameDigest': receipt.digest}
        finally:
            self._lock.release()

    def observe(self, kind, late_cause=None):
        if not self._lock.acquire(blocking=False):
            raise Refused('serial observation entry required')
        try:
            if type(kind) is not str or kind not in ('loss', 'expiry') or kind in self.observations:
                raise Refused('append-once observation refused')
            try:
                contract.validate(late_cause, contract.optional(contract.cause_shape()))
            except ValueError:
                raise Refused('closed late cause differs') from None
            # Freeze before possible mutation. A late cause never rewrites firstCause.
            self.frozen = True
            row = {'v': 1, 'binding': self.digest, 'generation': self.reservation.generation,
                   'kind': kind, 'ordinal': self.ordinal, 'firstCause': self.first_cause,
                   'lateCause': copy.deepcopy(late_cause), 'result': 'Unknown'}
            raw = contract.canonical(row)
            if len(raw) > 512:
                raise Refused('observation slot overflow')
            self.observations.add(kind)
            try:
                receipt = self.backend.observe_loss_or_expiry(self.reservation, raw)
                self._receipt(receipt, kind, self.ordinal, raw)
            except Exception:
                raise Refused('Unknown: original observation attempt consumed') from None
            return copy.deepcopy(row)
        finally:
            self._lock.release()
