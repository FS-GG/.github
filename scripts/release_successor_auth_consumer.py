"""Inert child entry boundary for the retained authentication phase protocol.

The Coordination producer owns process custody, original time, retention and ACK
admission. This module only checks its exact start frame against an independently
held binding before exposing the existing inert domain adapter. It has no native
launcher, credential acquisition or publication route.
"""

from __future__ import annotations

import hashlib
import json

from release_successor_provider import (
    AUTH_PROFILE,
    AUTH_PROTOCOL,
    AuthenticationPhaseAdapter,
    Refused,
    _auth_canonical,
    validate_auth_binding,
)


class InertStartConsumer:
    """One attempted start for one independently supplied inert binding."""

    def __init__(self, expected_binding):
        # Copy canonical bytes now: a later caller mutation cannot change identity.
        canonical = _auth_canonical(expected_binding)
        binding = json.loads(canonical)
        if binding.get("mode") != "inert" or binding.get("profile") != AUTH_PROFILE:
            raise Refused("authentication consumer binding is not inert")
        digest = validate_auth_binding(binding)
        self._binding = binding
        self._start = self._frame({"v": AUTH_PROTOCOL, "k": "start", "b": digest, "p": binding})
        self._used = False

    @staticmethod
    def _frame(value):
        body = _auth_canonical(value)
        raw = len(body).to_bytes(4, "big") + body
        if len(raw) > 2048:
            raise Refused("authentication consumer start frame exceeds bound")
        return raw

    @property
    def binding_digest(self):
        return hashlib.sha256(_auth_canonical(self._binding)).hexdigest()

    def take(self, start_frame, exchange, ticks):
        # Consume even a refused attempt. A retry cannot reinterpret an uncertain
        # original start or turn this helper into an independent admission owner.
        if self._used:
            raise Refused("authentication consumer start already attempted")
        self._used = True
        if type(start_frame) is not bytes or start_frame != self._start:
            raise Refused("authentication consumer start differs")
        return AuthenticationPhaseAdapter(self._binding, exchange, ticks)


def run_inert_authentication(prepared, start_frame, exchange, ticks, opener, report, wall_clock):
    """Run the actual child library flow with a fixed fake JWT and explicit HTTP.

    This source fixture opens no process or default network client. Independent
    prebinding must already exist; no serialized frame can construct it here.
    """
    from release_successor_provider import OriginalTagAuthenticationSession
    if type(prepared) is not InertStartConsumer or opener is None or not callable(wall_clock):
        raise Refused('authentication inert fixture context unavailable')
    adapter = prepared.take(start_frame, exchange, ticks)
    session = OriginalTagAuthenticationSession('inert-jwt', prepared._binding['app']['id'],
        prepared._binding['app']['installation'], phase_adapter=adapter, opener=opener, wall_clock=wall_clock)
    return session.qualify(report)


CALLBACK_PROTOCOL = 'fsgg.execution.phase-callback/1'
CALLBACK_AGGREGATE = 40960
_CALLBACK_KEYS = {'v', 'kind', 'invocation', 'binding', 'sequence', 'ordinal', 'frameDigest'}


class _CallbackStopped(Refused):
    def __init__(self, reason):
        super().__init__('authentication callback stopped')
        self.reason = reason


class InertCallbackClock:
    """Already-live fixture clock; serialized binding text never creates a grant."""

    def __init__(self, domain, frequency, start, normal, work, total, ticks, cancelled=lambda: False):
        values = (frequency, start, normal, work, total)
        if (any(type(value) is not int for value in values) or frequency <= 0
                or not 0 <= start <= normal <= work <= total <= (1 << 63) - 1
                or normal - start > 270 * frequency or work - start > 285 * frequency
                or total - start > 300 * frequency):
            raise Refused('authentication callback clock refused')
        self._description = dict(domain=domain, frequency=str(frequency), start=str(start),
                                 normal=str(normal), work=str(work), total=str(total))
        self._ticks, self._cancelled = ticks, cancelled
        self._last = start
        self._normal, self._work = normal, work

    def checkpoint(self, normal):
        now = self._ticks()
        if type(now) is not int or now < self._last:
            raise _CallbackStopped('internal')
        self._last = now
        if self._cancelled():
            raise _CallbackStopped('cancelled')
        if now >= (self._normal if normal else self._work):
            raise _CallbackStopped('deadline')


def _callback_frame(row):
    body = _auth_canonical(row)
    return len(body).to_bytes(4, 'big') + body


def _callback_decode(raw):
    # Bound nesting/property work before the existing canonical decoder recurses.
    if type(raw) is not bytes or not 4 < len(raw) <= 4096:
        raise Refused('authentication callback encoding refused')
    depth = properties = 0
    quoted = escaped = False
    for byte in raw[4:]:
        if quoted:
            if escaped:
                escaped = False
            elif byte == 92:
                escaped = True
            elif byte == 34:
                quoted = False
        elif byte == 34:
            quoted = True
        elif byte in (123, 91):
            depth += 1
            if depth > 12:
                raise Refused('authentication callback encoding refused')
        elif byte in (125, 93):
            depth -= 1
        elif byte == 58:
            properties += 1
            if properties > 512:
                raise Refused('authentication callback encoding refused')
    return __import__('release_successor_provider').decode_auth_frame(raw, maximum=4096)


class InertCallbackOwner:
    """One source-only owner endpoint; validation never persists a child frame.

    The supplied sink is the maintained publisher's persist_authentication_snapshot.
    The caller owns the path/report, live clock and this endpoint throughout any
    blocked sink. A transport failure calls close(); a new endpoint cannot authorize
    replay over an existing destination. No channel, process or credential is opened.
    """

    def __init__(self, expected_binding, result_path, report, sink, clock):
        import threading
        canonical = _auth_canonical(expected_binding)
        binding = json.loads(canonical)
        if binding.get('mode') != 'inert' or binding.get('profile') != AUTH_PROFILE:
            raise Refused('authentication callback binding is not inert')
        digest = validate_auth_binding(binding)
        # Own a third expected copy, separate from every received envelope.
        InertStartConsumer(binding)  # Also bounds the complete producer start frame.
        if (type(clock) is not InertCallbackClock or clock._description != binding['clock']
                or report.get('authenticationSession') is not None
                or result_path.exists() or result_path.is_symlink()):
            raise Refused('authentication callback preparation refused')
        self._bytes, self._binding, self._digest = canonical, binding, digest
        self._path, self._report, self._sink, self._clock = result_path, report, sink, clock
        self._sequence = self._ordinal = self._charged = 0
        self._bound = self._closed = self._terminal = False
        self._pending = self._previous = None
        self._retain_attempted = False
        self._lock = threading.Lock()
        self._failure_lock = threading.Lock()
        self.first_cause = None

    @property
    def charged_bytes(self):
        return self._charged

    @property
    def unresolved_retention(self):
        return self._retain_attempted and self._pending is not None

    def close(self, reason='internal'):
        # Transport overlap and an in-flight callback can fail on different
        # threads. Preserve the first observed reason under its own short lock.
        with self._failure_lock:
            self._closed = True
            if self.first_cause is None:
                self.first_cause = reason if reason in {
                    'binding', 'order', 'domain', 'retention', 'readback', 'deadline', 'cancelled', 'internal'
                } else 'internal'

    def handle(self, raw):
        # Charge even bytes received after closure or during a rejected overlap.
        self._charged += len(raw) if type(raw) is bytes else 0
        # A concurrent request is a protocol failure, never a queued later callback.
        if not self._lock.acquire(blocking=False):
            self.close('order')
            raise Refused('authentication callback refused')
        try:
            return self._handle(raw)
        except Exception:
            self.close()
            raise Refused('authentication callback refused') from None
        finally:
            self._lock.release()

    def _handle(self, raw):
        import base64
        import re
        from release_successor_provider import decode_auth_frame, validate_auth_payload
        if self._closed or self._terminal:
            self.close('order')
            raise Refused('authentication callback refused')
        # Actual received bytes include malformed input. Never trust a declared body
        # length as an allocation request; stream adapters stop at a bounded prefix.
        if self._charged > CALLBACK_AGGREGATE:
            raise Refused('authentication callback bound refused')
        row = _callback_decode(raw)
        kind = row.get('kind')
        addition = {'bind': {'bindingBase64'}, 'validate': {'frameBase64'}, 'retain': set()}.get(kind)
        if (addition is None or set(row) != _CALLBACK_KEYS | addition
                or row['v'] != CALLBACK_PROTOCOL or row['invocation'] != self._binding['invocation']
                or row['binding'] != self._digest or type(row['sequence']) is not int
                or row['sequence'] != self._sequence + 1 or not 1 <= row['sequence'] <= 13
                or type(row['ordinal']) is not int or not 0 <= row['ordinal'] <= 6
                or type(row['frameDigest']) is not str
                or re.fullmatch('[0-9a-f]{64}', row['frameDigest']) is None
                or len(raw) > (512 if kind == 'retain' else 4096)):
            self.close('order')
            raise Refused('authentication callback envelope refused')
        self._sequence = row['sequence']  # Refusal consumes the addressed attempt.
        reason = 'order'
        try:
            phase = None
            frame = None
            if kind != 'retain':
                encoded = row['bindingBase64' if kind == 'bind' else 'frameBase64']
                if type(encoded) is not str or len(encoded) > 2732:
                    raise Refused('callback base64 refused')
                frame = base64.b64decode(encoded, validate=True)
                if base64.b64encode(frame).decode('ascii') != encoded:
                    raise Refused('callback base64 refused')
            if kind == 'bind':
                reason = 'binding'
                if (self._bound or row['sequence'] != 1 or row['ordinal'] != 0
                        or row['frameDigest'] != self._digest or frame != self._bytes):
                    raise Refused('callback binding refused')
            elif kind == 'validate':
                if not self._bound or self._pending is not None or row['ordinal'] != self._ordinal + 1:
                    raise Refused('callback order refused')
                phase = decode_auth_frame(frame)
                if (set(phase) != {'v', 'b', 'n', 'k', 'p'} or phase['v'] != AUTH_PROTOCOL
                        or phase['b'] != self._digest or type(phase['n']) is not int
                        or phase['n'] != row['ordinal'] or hashlib.sha256(frame).hexdigest() != row['frameDigest']):
                    raise Refused('callback identity refused')
            else:
                if (not self._bound or self._pending is None or self._retain_attempted
                        or row['ordinal'] != self._pending[0]['n']
                        or row['frameDigest'] != hashlib.sha256(self._pending[1]).hexdigest()):
                    raise Refused('callback retention order refused')
                phase, frame = self._pending
            terminal = phase is not None and phase['k'] == 'terminal'
            future = (1024 if kind == 'validate' else 0) if terminal else 5632
            if self._charged + 512 + future > CALLBACK_AGGREGATE:
                raise Refused('callback budget refused')
            normal = kind == 'bind' or phase['k'] in {'ready', 'issuance-possible'}
            reason = 'deadline'
            self._clock.checkpoint(normal)  # Actual callback entry, after envelope work.
            if kind == 'bind':
                self._bound = True
                response_kind = 'bound'
            elif kind == 'validate':
                reason = 'domain'
                previous = self._previous['p'].get('facts') if self._previous else None
                validate_auth_payload(phase['k'], phase['p'], self._binding, previous)
                self._pending = (phase, frame)  # Immutable bytes owned until settlement.
                self._retain_attempted = False
                response_kind = 'validated'
            else:
                reason = 'retention'
                self._retain_attempted = True  # Consume before the first actual sink entry.
                receipt = self._sink(self._path, self._report, self._binding, frame)
                reason = 'readback'
                if (type(receipt) is not dict or type(receipt.get('ordinal')) is not int
                        or receipt != {name: row[name] for name in ('binding', 'ordinal', 'frameDigest')}):
                    raise Refused('callback readback refused')
                # Commit before constructing/sending the reply. Reply loss cannot undo it.
                self._ordinal = phase['n']
                self._previous = phase
                self._pending = None
                self._terminal = terminal
                response_kind = 'retained'
            # A late sink may settle a durable write, but supplies no fresh authority.
            reason = 'deadline'
            self._clock.checkpoint(normal)
            if self._closed:
                raise Refused('authentication callback closed')
            reply = {key: value for key, value in row.items() if key in _CALLBACK_KEYS}
            reply['kind'] = response_kind
        except Exception as error:
            self.close(error.reason if isinstance(error, _CallbackStopped) else reason)
            reply = {key: value for key, value in row.items() if key in _CALLBACK_KEYS}
            reply.update(kind='refused', reason=self.first_cause)
        result = _callback_frame(reply)
        if len(result) > 512 or self._charged + len(result) > CALLBACK_AGGREGATE:
            self.close('internal')
            raise Refused('authentication callback response refused')
        self._charged += len(result)  # Entire outgoing record charged before transport.
        return result


class InertCallbackRequestDecoder:
    """Bounded injected-channel entry. The caller owns all read/write tasks.

    A reply must finish writing before another request byte can enter. Prefixes
    are bounded before body allocation; rejected/partial input remains observed.
    No transport is opened or disposed by this decoder.
    """

    def __init__(self, owner):
        if type(owner) is not InertCallbackOwner:
            raise Refused('authentication callback owner refused')
        import threading
        self._entry = threading.Lock()
        self._counts = threading.Lock()
        self._owner = owner
        self._buffer = bytearray()
        self._expected = 4
        self._reply = None
        self._observed = self._delivered = 0

    @property
    def observed_request_bytes(self):
        with self._counts:
            return self._observed

    def feed(self, data):
        # Reserve entry before parsing or invoking a potentially blocked callback.
        # An overlapping byte is observed and refused; it cannot enter the buffer
        # that the first request will clear after its response.
        with self._counts:
            self._observed += len(data) if type(data) is bytes else 0
        if not self._entry.acquire(blocking=False):
            self._owner.close('order')
            raise Refused('authentication callback overlap refused')
        try:
            return self._feed(data)
        finally:
            self._entry.release()

    def _feed(self, data):
        try:
            if type(data) is not bytes:
                raise Refused('authentication callback transport refused')
            if (self._owner._closed or self._reply is not None
                    or self._observed + self._owner.charged_bytes - self._delivered > CALLBACK_AGGREGATE):
                raise Refused('authentication callback transport refused')
            for index, value in enumerate(data):
                self._buffer.append(value)
                if len(self._buffer) == 4:
                    length = int.from_bytes(self._buffer, 'big')
                    if not 0 < length <= 4092:
                        raise Refused('authentication callback prefix refused')
                    self._expected = length + 4
                if len(self._buffer) == self._expected and len(self._buffer) > 4:
                    # A second request cannot be pipelined behind the first.
                    if index != len(data) - 1:
                        raise Refused('authentication callback pipeline refused')
                    raw = bytes(self._buffer)
                    self._delivered += len(raw)
                    self._reply = self._owner.handle(raw)
                    return self._reply
            return None
        except Exception:
            self._owner.close('order')
            raise Refused('authentication callback transport refused') from None

    def confirm_response_written(self, count):
        if not self._entry.acquire(blocking=False):
            self._owner.close('order')
            raise Refused('authentication callback overlap refused')
        try:
            self._confirm_response_written(count)
        finally:
            self._entry.release()

    def _confirm_response_written(self, count):
        if self._owner._closed or self._reply is None or type(count) is not int or count != len(self._reply):
            self._owner.close('internal')
            raise Refused('authentication callback write refused')
        self._reply = None
        self._buffer.clear()
        self._expected = 4

    def eof(self):
        if not self._entry.acquire(blocking=False):
            self._owner.close('order')
            raise Refused('authentication callback overlap refused')
        try:
            self._eof()
        finally:
            self._entry.release()

    def _eof(self):
        # EOF proves no writer termination. A partial/current reply stays owned.
        if self._buffer or self._reply is not None or not self._owner._terminal:
            self._owner.close('internal')
            raise Refused('authentication callback input incomplete')
