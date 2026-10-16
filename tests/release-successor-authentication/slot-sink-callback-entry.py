#!/usr/bin/env python3
"""Controlled inert slot callback fixture; test evidence, never an operation receipt."""
import base64
import hashlib
import importlib.util
import json
import pathlib
import sys


def audit(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise RuntimeError('inert slot callback effect refused')


sys.addaudithook(audit)
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[1] / 'scripts'))
import release_successor_provider as provider
import release_successor_auth_consumer as consumer
import inert_phase_slot_sink as slots
spec = importlib.util.spec_from_file_location('slot_callback_framed_fixture', HERE / 'slot-sink-fixture.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
PROFILE = 'inert-slot-callback-pair/2'
EVIDENCE_MAXIMUM = 32768


def execute(binding, expected_clock, input_stream, output_stream, backend=None):
    """Return bounded synthetic evidence after terminal response confirmation.

    The caller supplies independent inert expectations. No observed EOF or
    trailing-input check is claimed: the managed caller closes stdin after exit.
    """
    clock = consumer.InertCallbackClock(expected_clock['domain'], int(expected_clock['frequency']),
        int(expected_clock['start']), int(expected_clock['normal']), int(expected_clock['work']),
        int(expected_clock['total']), lambda: int(expected_clock['start']) + 1)
    identity = {'path': 'inert-slot-callback-test-evidence',
                'binding': provider.validate_auth_binding(binding),
                'invocation': binding['invocation'], 'storeGeneration': 1}
    backend = slots.ControlledSlotBackend() if backend is None else backend
    subject = fixture.FramedSlotSinkFixture(binding, identity, dict(slots.REPORT), expected_clock,
        slots.population(), backend, clock, input_stream, output_stream)
    try:
        # Use feed rather than run: run awaits EOF, which this caller cannot supply
        # until after child exit. Each feed completes write/flush/confirmation.
        for _ in range(13):
            prefix = subject._read_exact(4)
            subject.feed(prefix)  # Refuse an oversized length before its body.
            body = subject._read_exact(int.from_bytes(prefix, 'big'))
            reply = subject.feed(body)
            if reply is None:
                raise slots.Refused('inert slot callback missing response')
            row = provider.decode_auth_frame(reply, maximum=512)
            if subject.owner._terminal:
                if (row['kind'] != 'retained' or subject.owner._closed
                        or subject.decoder._reply is not None
                        or subject.sink.previous_phase != 'terminal' or subject.sink.ordinal != 6):
                    raise slots.Refused('inert slot callback terminal confirmation differs')
                records = []
                for ordinal in range(1, 7):
                    raw = bytes(backend.stores[f'phase-{ordinal}'])
                    records.append({'ordinal': ordinal, 'bytes': raw.hex(),
                                    'sha256': hashlib.sha256(raw).hexdigest()})
                evidence = {'schema': 'fsgg.inert-slot-callback-test-evidence/1', 'profile': PROFILE,
                    'binding': identity['binding'], 'ordinal': 6, 'slots': records,
                    'terminalResponseConfirmed': True, 'inputEofObserved': False,
                    'trailingInputChecked': False, 'operationalReceipt': False,
                    'nativeCampaign': None, 'nativeAttempt': None}
                if len(encode_evidence(evidence)) > EVIDENCE_MAXIMUM:
                    raise slots.Refused('inert slot callback evidence bound')
                return evidence
        raise slots.Refused('inert slot callback terminal missing')
    except Exception:
        subject._close()
        raise slots.Refused('inert slot callback refused') from None


def encode_evidence(evidence):
    return json.dumps(evidence, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode('ascii')


def main():
    try:
        if len(sys.argv) != 4 or any(len(value) > 8192 for value in sys.argv[1:3]):
            raise slots.Refused('inert slot callback arguments')
        binding = json.loads(base64.b64decode(sys.argv[1], validate=True))
        expected_clock = json.loads(base64.b64decode(sys.argv[2], validate=True))
        result = pathlib.Path(sys.argv[3])
        evidence = execute(binding, expected_clock, sys.stdin.buffer, sys.stdout.buffer)
        raw = encode_evidence(evidence)
        # Exclusive evidence creation, with no operational receipt or retry path.
        with result.open('xb') as output:
            count = output.write(raw)
            if type(count) is not int or count != len(raw):
                raise slots.Refused('inert slot evidence partial write')
            output.flush()
        return 0
    except Exception:
        print('slot-sink-callback-refused', file=sys.stderr)
        return 3


if __name__ == '__main__':
    raise SystemExit(main())
