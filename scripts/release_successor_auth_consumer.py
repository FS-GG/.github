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
