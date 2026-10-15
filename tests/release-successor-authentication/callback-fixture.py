#!/usr/bin/env python3
"""Hosted source fixture: callback records only; no authentication child process."""
import base64
import importlib.util
import json
import pathlib
import sys


def audit(event, args):
    if event in {'subprocess.Popen', 'os.system', 'os.posix_spawn', 'socket.__new__',
                 'socket.connect', 'socket.getaddrinfo', 'socket.bind'}:
        raise RuntimeError('callback fixture effect refused')


sys.addaudithook(audit)
ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import release_successor_auth_consumer as consumer
spec = importlib.util.spec_from_file_location('callback_fixture_publisher', ROOT / 'scripts/release-successor-publish.py')
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


def read_exact(stream, count):
    value = bytearray()
    while len(value) < count:
        chunk = stream.read(count - len(value))
        if not chunk:
            raise RuntimeError('callback fixture input incomplete')
        value.extend(chunk)
    return bytes(value)


def main():
    if len(sys.argv) != 4:
        return 2
    # Both expectations are trusted fixture arguments, independent of callback bytes.
    binding = json.loads(base64.b64decode(sys.argv[1], validate=True))
    expected_clock = json.loads(base64.b64decode(sys.argv[2], validate=True))
    clock = consumer.InertCallbackClock(expected_clock['domain'], int(expected_clock['frequency']),
        int(expected_clock['start']), int(expected_clock['normal']), int(expected_clock['work']),
        int(expected_clock['total']), lambda: int(expected_clock['start']) + 1)
    result = pathlib.Path(sys.argv[3])
    owner = consumer.InertCallbackOwner(binding, result, {'schema': 'fsgg.release-successor-result/1'},
                                      publisher.persist_authentication_snapshot, clock)
    decoder = consumer.InertCallbackRequestDecoder(owner)
    while True:
        prefix = read_exact(sys.stdin.buffer, 4)
        decoder.feed(prefix)  # Refuses an oversized prefix before reading/allocating its body.
        body = read_exact(sys.stdin.buffer, int.from_bytes(prefix, 'big'))
        reply = decoder.feed(body)
        if reply is None:
            raise RuntimeError('callback fixture request incomplete')
        sys.stdout.buffer.write(reply)
        sys.stdout.buffer.flush()
        decoder.confirm_response_written(len(reply))
        if owner._terminal:
            decoder.eof()
            return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception:
        # Rejected payload/exception details never enter diagnostics.
        print('callback-fixture-refused', file=sys.stderr)
        raise SystemExit(3) from None
