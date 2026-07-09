#!/usr/bin/env python3
"""Cover the LIVE feed reader in scripts/check-feed-coherence.py, with a stubbed transport.

The fixture in run.sh drives the gate through `--fixture`, which bypasses `feed_versions()`
entirely — so the HTTP error handling, which IS the fail-closed logic epic #266 is about, would
otherwise ship untested. (Discovered by mutation-testing the fixture: replacing the "zero
versions" raise with a silent default survived every test.)

Every case here asserts the gate turns an HTTP condition into a GateError with the right reason,
never into an empty list or a skip. Pure stdlib, no network.

Usage: tests/feed-coherence/feed_reader_cases.py <path-to-check-feed-coherence.py>
"""
from __future__ import annotations

import base64
import importlib.util
import io
import json
import sys
import urllib.error
import urllib.request

gate_path = sys.argv[1] if len(sys.argv) > 1 else "scripts/check-feed-coherence.py"
spec = importlib.util.spec_from_file_location("gate", gate_path)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

passed = failed = 0


def ok(label: str) -> None:
    global passed
    passed += 1
    print(f"PASS  {label}")


def bad(label: str, detail: str = "") -> None:
    global failed
    failed += 1
    print(f"FAIL  {label}")
    if detail:
        print(f"    | {detail}")


class _Resp(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False


def stub(fn):
    """Point urlopen at `fn(req)`; returns a restore callable."""
    real = urllib.request.urlopen
    urllib.request.urlopen = lambda req, timeout=None: fn(req)
    return lambda: setattr(urllib.request, "urlopen", real)


def http_error(code: str | int):
    def _f(req):
        raise urllib.error.HTTPError(req.full_url, int(code), "boom", {}, None)
    return _f


def expect_gate_error(label: str, transport, needle: str) -> None:
    restore = stub(transport)
    try:
        got = gate.feed_versions("FS.GG.Contracts", "tok")
    except gate.GateError as e:
        if needle.lower() in str(e).lower():
            ok(label)
        else:
            bad(label, f"GateError, but not for the stated reason /{needle}/: {e}")
    except Exception as e:  # noqa: BLE001 — any other escape is itself the bug
        bad(label, f"raised {type(e).__name__} instead of GateError: {e}")
    else:
        bad(label, f"returned {got!r} instead of failing closed")
    finally:
        restore()


print("--- the live feed reader fails CLOSED on every unreadable condition ---")
expect_gate_error("401 -> the token cannot read the feed", http_error(401), "read:packages")
expect_gate_error("403 -> the token cannot read the feed", http_error(403), "read:packages")
expect_gate_error("404 -> the package is not on the feed", http_error(404), "not on the org feed")
expect_gate_error("500 -> the feed read failed", http_error(500), "HTTP 500")
expect_gate_error(
    "a network error -> the feed is unreachable",
    lambda req: (_ for _ in ()).throw(urllib.error.URLError("dns")),
    "unreachable",
)
expect_gate_error(
    "an empty version list -> zero versions, not 'coherent'",
    lambda req: _Resp(b'{"versions": []}'),
    "zero versions",
)
expect_gate_error(
    "unparsable JSON -> a failure, not 'coherent'",
    lambda req: _Resp(b"<html>502 bad gateway</html>"),
    "unparsable JSON",
)
# If the feed's response shape ever changes, the gate must notice rather than read the absence
# of a `versions` key as "no versions differ from the registry".
expect_gate_error(
    "an unrecognised response shape -> a failure, not 'coherent'",
    lambda req: _Resp(b'{"data": ["1.0.0"]}'),
    "no `versions` list",
)

print()
print("--- and reads the feed correctly when it IS readable ---")

restore = stub(lambda req: _Resp(json.dumps({"versions": ["1.2.0", "1.4.0"]}).encode()))
try:
    got = gate.feed_versions("FS.GG.Contracts", "tok")
    if got == ["1.2.0", "1.4.0"] and gate.newest(got) == "1.4.0":
        ok("the versions list is returned, newest by version order")
    else:
        bad("the versions list is returned, newest by version order", repr(got))
finally:
    restore()

# The request must carry Basic auth and hit the lowercase flat-container path; a request without
# credentials 401s against this feed rather than reading it anonymously.
captured: dict[str, str] = {}
seen_url: list[str] = []


def capture(req):
    captured.update(req.headers)
    seen_url.append(req.full_url)
    return _Resp(json.dumps({"versions": ["1.0.0"]}).encode())


restore = stub(capture)
try:
    gate.feed_versions("FS.GG.Contracts", "s3cret")
    hdrs = {k.lower(): v for k, v in captured.items()}
    expect_auth = "Basic " + base64.b64encode(b"x:s3cret").decode()
    if hdrs.get("authorization") == expect_auth:
        ok("the token is sent as HTTP Basic auth")
    else:
        bad("the token is sent as HTTP Basic auth", repr(captured))
    if seen_url and seen_url[0].endswith("/download/fs.gg.contracts/index.json"):
        ok("the flat-container path is lowercased")
    else:
        bad("the flat-container path is lowercased", repr(seen_url))
finally:
    restore()

print()
print(f"{passed} passed, {failed} failed.")
sys.exit(1 if failed else 0)
