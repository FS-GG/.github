#!/usr/bin/env python3
"""Injected Python stream fixture only; no process, filesystem or native selection."""
import copy
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[1] / 'scripts'))
import inert_phase_slot_sink as slots
import release_successor_auth_consumer as consumer

Refused = slots.Refused


class InertDestination:
    """Owner's destination protocol, with no real path or filesystem access."""
    def __init__(self, identity): self.identity = identity
    def __str__(self): return self.identity
    def exists(self): return False
    def is_symlink(self): return False


class FramedSlotSinkFixture:
    def __init__(self, expected_binding, original_sink_identity, report, original_window,
                 reserved_population, backend, clock, input_stream, output_stream):
        # The independently supplied live clock must match the original window.
        # No callback bytes construct a grant or replace these expected inputs.
        if (type(clock) is not consumer.InertCallbackClock
                or clock._description != original_window
                or not callable(getattr(input_stream, 'read', None))
                or not callable(getattr(output_stream, 'write', None))
                or not callable(getattr(output_stream, 'flush', None))):
            raise Refused('inert framed fixture preparation refused')
        binding = copy.deepcopy(expected_binding)
        self.report = copy.deepcopy(report)
        self.sink = slots.InertPhaseSlotSink.prepare(binding, original_sink_identity,
            self.report, original_window, reserved_population, backend)
        destination = InertDestination(original_sink_identity['path'])
        self.owner = consumer.InertCallbackOwner(binding, destination, self.report, self.sink, clock)
        self.decoder = consumer.InertCallbackRequestDecoder(self.owner)
        self.input, self.output = input_stream, output_stream
        self.closed = self.completed = self.started = False

    def _close(self):
        self.closed = True
        self.owner.close('internal')

    def feed(self, data):
        """One incoming fragment; never confirm partial or unflushed replies."""
        if self.closed or self.completed:
            raise Refused('inert framed fixture closed')
        try:
            reply = self.decoder.feed(data)
            if reply is None:
                return None
            count = self.output.write(reply)
            if type(count) is not int or count != len(reply):
                raise Refused('inert framed fixture partial write')
            self.output.flush()
            self.decoder.confirm_response_written(count)
            return reply
        except Exception:
            self._close()
            raise Refused('inert framed fixture refused') from None

    def eof(self):
        if self.closed or self.completed:
            raise Refused('inert framed fixture closed')
        try:
            self.decoder.eof()
            self.completed = True
        except Exception:
            self._close()
            raise Refused('inert framed fixture incomplete') from None

    def _read_exact(self, count, allow_eof=False):
        result = bytearray()
        while len(result) < count:
            chunk = self.input.read(count - len(result))
            if type(chunk) is not bytes or len(chunk) > count - len(result):
                raise Refused('inert stream read differs')
            if not chunk:
                if not result and allow_eof:
                    return None
                raise Refused('inert stream incomplete')
            result.extend(chunk)
        return bytes(result)

    def run(self):
        """Pull one bounded frame, finish its reply, then read another prefix.

        Prefix refusal precedes reading the declared body. The feed API exposes
        decoder rejection of pipelined chunks; pull reads never over-read a frame.
        """
        if self.started or self.closed or self.completed:
            raise Refused('inert framed fixture already attempted')
        self.started = True
        try:
            while True:
                prefix = self._read_exact(4, allow_eof=True)
                if prefix is None:
                    self.eof()
                    return self.sink.ordinal
                self.feed(prefix)  # Validate the bound before any body read.
                body = self._read_exact(int.from_bytes(prefix, 'big'))
                if self.feed(body) is None:
                    raise Refused('inert framed fixture missing reply')
        except Exception:
            self._close()
            raise Refused('inert framed fixture refused') from None
