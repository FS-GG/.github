"""Opt-in closed SOURCE constructor. No production provider is installed.

Private qualification seams are excluded from production selection. Pinned trusted
Python code is the boundary; reflection and a compromised interpreter are not.
"""
import hashlib
import io
import json
import pathlib
import sys
import threading
from enum import Enum

_ROOT = pathlib.Path(__file__).resolve().parents[1]
_HERE = _ROOT / 'tests/release-successor-authentication'
sys.path.insert(0, str(_HERE))
import release_successor_provider as _wire
import release_successor_auth_consumer as _consumer
import inert_phase_slot_sink as _slots
import domain_contract as _domain

class Refused(RuntimeError):
    pass

class Status(Enum):
    COMPLETE = 'complete'
    OBSERVED = 'observed-synthetic'
    REFUSED = 'refused'
    UNKNOWN = 'unknown'

_ROLES = ('programme-acceptance', 'systemadmin-recovery')
_PURPOSES = ('initial-status', 'settlement-status', 'retention-expiry-status')
_TOKEN = object()
_PROFILE_DIGEST = 'ed652bb3fe4f6132a9329f4b791f60f51297bb2ac0507dacb344b0e5191df660'
_SOURCE_PATHS = (
    'scripts/fsgg_feed.py',
    'scripts/release_successor_auth_consumer.py',
    'scripts/release_successor_closed_callback.py',
    'scripts/release_successor_execution.py',
    'scripts/release_successor_provider.py',
    'tests/release-successor-authentication/domain_contract.py',
    'tests/release-successor-authentication/inert_phase_slot_sink.py',
    'tests/release-successor-authentication/domain-max-vectors.json',
    'tests/release-successor-authentication/closed-source-profile.json',
)

def _canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode('ascii')

def _pairs(rows):
    result = {}
    for key, value in rows:
        if key in result:
            raise Refused('closed profile refused')
        result[key] = value
    return result

def validate_profile(raw):
    """Return a nonsecret profile digest; accept only this unavailable profile."""
    if type(raw) is not bytes or len(raw) > 65536:
        raise Refused('closed profile refused')
    try:
        value = json.loads(raw, object_pairs_hook=_pairs)
        expected = (_HERE / 'closed-source-profile.json').read_bytes().rstrip(b'\n')
        if _canonical(value) != raw or raw != expected or hashlib.sha256(raw).hexdigest() != _PROFILE_DIGEST:
            raise Refused('closed profile refused')
        _verify_sources()
        return hashlib.sha256(raw).hexdigest()
    except Exception:
        raise Refused('closed profile refused') from None

def _verify_sources():
    manifest = json.loads((_HERE / 'closed-source-manifest.json').read_bytes(), object_pairs_hook=_pairs)
    if (type(manifest) is not dict or set(manifest) != {'schema', 'files', 'qualification'}
            or manifest['schema'] != 'fsgg.utel-closed-source-manifest/1'
            or type(manifest['files']) is not list
            or [row.get('path') for row in manifest['files']] != list(_SOURCE_PATHS)
            or any(set(row) != {'path', 'sha256'} for row in manifest['files'])):
        raise Refused('closed source refused')
    for row in manifest['files']:
        if hashlib.sha256((_ROOT / row['path']).read_bytes()).hexdigest() != row['sha256']:
            raise Refused('closed source refused')

def prepare(profile):
    """Production ABI: no streams, writer, backend, paths or activation argument.

    Even a rewritten supplier declaration cannot activate this source. A separately
    reviewed provider join is required; no operation/reservation is attempted here.
    """
    validate_profile(profile)
    raise Refused('closed production provider unavailable')

class _Destination:
    def __str__(self): return 'closed-owned-slot'
    def exists(self): return False
    def is_symlink(self): return False

class _Original:
    """Controlled provider ORIGINAL record, never selectable by prepare().

    All opportunities are reserved together with original ownership. Reusing this
    record preserves consumption, including unknown delivery and failed creation.
    No durable implementation or restart-proof physical accounting is claimed.
    """
    def __init__(self, token):
        if token is not _TOKEN: raise Refused('closed constructor refused')
        self.backend = _slots.ControlledSlotBackend()
        self.started = False
        self.lock = threading.Lock()
        self.opportunities = {(role, purpose): None for role in _ROLES for purpose in _PURPOSES}
        self.recipient_slots = {}
        self.extractions = 0
        # Explicitly non-operational, preexisting controlled provider evidence.
        # No signer/credential/clock is called; these are not enrolled ACKs.
        body = json.loads((_HERE / 'domain-max-vectors.json').read_bytes())['envelopes']['read'].encode('ascii')
        self.diagnostic_evidence = len(body).to_bytes(4, 'big') + body
        if len(self.diagnostic_evidence) > 546:
            raise Refused('closed diagnostic bound')
        self.delivery_cut = None
        self.expired = False

class _Core:
    __slots__ = ('_original', '_sink', '_owner', '_decoder', '_input', '_replies',
                 '_attempted', '_cut', '_fragment', '_status', '_cleanup')
    def __init__(self, token, original, binding, requests, *, cut=None, fragment=4096):
        if token is not _TOKEN or type(original) is not _Original:
            raise Refused('closed constructor refused')
        if type(requests) is not bytes or len(requests) > 40960:
            raise Refused('closed input refused')
        if cut not in (None, 'write', 'short', 'flush', 'confirmation', 'backend') or type(fragment) is not int or not 1 <= fragment <= 4096:
            raise Refused('closed qualification refused')
        with original.lock:
            if original.started: raise Refused('closed original already attempted')
            original.started = True  # Before any possible construction/retention.
        clock = binding['clock']
        identity = {'path': 'closed-owned-slot', 'binding': _wire.validate_auth_binding(binding),
                    'invocation': binding['invocation'], 'storeGeneration': 1}
        owned_sink = _slots.InertPhaseSlotSink.prepare(binding, identity, dict(_slots.REPORT),
            clock, _slots.population(), original.backend)
        self._initialize_owned(token, original, binding, requests, owned_sink, cut=cut, fragment=fragment)
        if cut == 'backend': original.backend.cut = 'write'

    def _initialize_owned(self, token, owned_original, binding, requests, owned_sink, *, cut=None, fragment=4096):
        """Share the one maintained callback engine with an already owned sink.

        Fixed private composition owns original consumption and adapter/source joins
        before sink preparation. No public entry or dynamic provider selection uses
        this seam; run and diagnostic remain the same owned algorithms.
        """
        if (token is not _TOKEN or type(owned_sink) is not _slots.InertPhaseSlotSink
                or hasattr(self, '_attempted')):
            raise Refused('closed constructor refused')
        if type(requests) is not bytes or len(requests) > 40960:
            raise Refused('closed input refused')
        if cut not in (None, 'write', 'short', 'flush', 'confirmation', 'backend') or type(fragment) is not int or not 1 <= fragment <= 4096:
            raise Refused('closed qualification refused')
        self._original, self._cut, self._fragment = owned_original, cut, fragment
        self._input, self._replies = io.BytesIO(requests), io.BytesIO()
        self._attempted, self._status, self._cleanup = False, Status.UNKNOWN, Status.UNKNOWN
        clock = binding['clock']
        owned_clock = _consumer.InertCallbackClock(clock['domain'], int(clock['frequency']),
            int(clock['start']), int(clock['normal']), int(clock['work']), int(clock['total']),
            lambda: int(clock['start']) + 1)
        self._sink = owned_sink
        self._owner = _consumer.InertCallbackOwner(binding, _Destination(), dict(_slots.REPORT), self._sink, owned_clock)
        self._decoder = _consumer.InertCallbackRequestDecoder(self._owner)

    def _read(self, count):
        result = bytearray()
        while len(result) < count:
            piece = self._input.read(min(count - len(result), self._fragment))
            if not piece: raise Refused('closed input incomplete')
            result.extend(piece)
        return bytes(result)

    def run(self):
        if self._attempted: return Status.REFUSED
        self._attempted = True
        try:
            for index in range(13):
                prefix = self._read(4)
                self._decoder.feed(prefix)  # Reject prefix before body allocation.
                reply = self._decoder.feed(self._read(int.from_bytes(prefix, 'big')))
                if type(reply) is not bytes or len(reply) > 512: raise Refused('closed reply refused')
                if self._cut == 'write': raise Refused('closed write failed')
                count = self._replies.write(reply)
                if self._cut == 'short': count -= 1
                if count != len(reply): raise Refused('closed write incomplete')
                if self._cut == 'flush': raise Refused('closed flush failed')
                self._replies.flush()
                if self._cut == 'confirmation': raise Refused('closed confirmation failed')
                self._decoder.confirm_response_written(count)
                if self._owner._terminal:
                    if index != 12 or self._sink.ordinal != 6 or self._owner._closed:
                        raise Refused('closed terminal refused')
                    self._status = Status.COMPLETE
                    break  # No fourteenth read, EOF or caller egress.
            if self._status is not Status.COMPLETE: raise Refused('closed terminal missing')
        except Exception:
            self._owner.close('internal')
            self._status = Status.REFUSED
        finally:
            # Buffers stay owned under the original reservation. No physical
            # release/GC/settlement is inferred from returning a source status.
            self._cleanup = Status.UNKNOWN
        return self._status

    def diagnostic(self, role, purpose):
        """Only fixed completion enums escape; no raw evidence accessor exists.

        Enrollment/original/profile joins in this controlled record are synthetic.
        Production and post-expiry custody remain unavailable.
        """
        original = self._original
        if type(role) is not str or type(purpose) is not str or (role, purpose) not in original.opportunities:
            return Status.REFUSED
        if not original.lock.acquire(blocking=False): return Status.REFUSED
        try:
            key = (role, purpose)
            if original.opportunities[key] is not None: return Status.REFUSED
            # Expiry delivery cannot extract retained bytes, even in this slice.
            if purpose == 'retention-expiry-status' or original.expired:
                original.opportunities[key] = Status.REFUSED
                return Status.REFUSED
            original.opportunities[key] = Status.UNKNOWN  # Before extraction.
            try:
                original.extractions += 1
                evidence = _domain.diagnostic_read(original.diagnostic_evidence)
                if type(evidence.exact_retained_bytes) is not bytes or len(evidence.exact_retained_bytes) > 546:
                    raise Refused('closed diagnostic bound')
                # One owned typed slot; neither bytes nor retained object escapes.
                original.recipient_slots[key] = evidence
                if original.delivery_cut == 'loss': return Status.UNKNOWN
                original.opportunities[key] = Status.OBSERVED
                return Status.OBSERVED
            except Exception:
                return Status.UNKNOWN
        finally:
            original.lock.release()
