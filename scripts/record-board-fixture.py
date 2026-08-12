#!/usr/bin/env python3
"""Capture a real HTTP transcript of one repo's coordination board and check it in as a
`fsgg.coord.board-recording/1` fixture for `tests/coord-engine-replay/` to replay (`.github#2401`).

WHY THIS EXISTS

Every fixture the engine is otherwise tested against (`tests/coord-engine-e2e/`,
`tests/coord-engine-parity/`'s 47 servers) is HAND-AUTHORED: it encodes its author's belief about what
GitHub returns, not what GitHub actually returned. That is the exact blind spot for the
lifecycle/projection defect class (`.github#2384`, `.github#2394`): those bugs are not about the engine
mishandling a response it expected, they are about the engine meeting a board state its author never
pictured. A hand-written fixture can only encode a failure mode someone already imagined; a RECORDING
captures whatever the board actually contained, including the states nobody thought to construct.

MECHANISM

This starts a local HTTP proxy, points the compiled engine's `$FSGG_GITHUB_API_BASE` at it, runs each
of `--commands` (default: reconcile, ready, driver-events) through the REAL engine binary, and forwards
every request the engine makes on to `--upstream`, recording the request/response pair as it goes. The
engine never knows it is being recorded: it makes the exact HTTP calls it would make against the real
GitHub API; the proxy is just also writing them down.

Because the three default commands each independently re-scan the same board, one capture session
naturally re-asks many identical questions — the proxy de-duplicates by the same (method, path,
canonical body) shape `tests/coord-engine-replay/replay_server.py` matches on, so one merged transcript
can answer any of them at replay time regardless of which command a later leg drives.

USAGE

    # against the real board — needs a token with read access to the repo and its Coordination board:
    GITHUB_TOKEN=... scripts/record-board-fixture.py --repo .github \\
        --out tests/coord-engine-replay/fixtures/my-board

    # against ANY GitHub-shaped HTTP fixture — this is how the suite's own self-check leg proves the
    # capture tool works with no live network and no real token at all (see
    # tests/coord-engine-replay/run.sh, which points --upstream at
    # tests/coord-engine-e2e/stateful_server.py):
    scripts/record-board-fixture.py --repo FS.GG.SDD --upstream http://127.0.0.1:PORT \\
        --out /tmp/some-dir --token-env FIXTURE_TOKEN

DETERMINISM

Running this twice against an unchanged board writes byte-identical `transcript.json` and
`expected/*.json` files (`.github#2401` AC1): entries are sorted by content rather than capture order,
rate-limit telemetry (a header on REST, `data.rateLimit` inline on GraphQL) is normalized to a fixed
sentinel because it moves on every live call regardless of whether the board changed, and
`driver --events`'s wall-clock `renderedAt`/`observedAt` fields are stripped from the recorded expected
output. See `tests/coord-engine-replay/fixture_lib.py` for the shared normalization both this script and
the replay server apply.

REDACTION

The proxy never persists a request HEADER at all — only method, path, and body — so the `Authorization`
header (and the token in it) never reaches the transcript by construction. `fixture_lib.redact_body`
additionally scans every captured body for a GitHub-token-shaped string as defense in depth.
"""

import argparse
import http.client
import json
import os
import subprocess
import sys
import tempfile
import threading
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tests" / "coord-engine-replay"))
import fixture_lib as lib  # noqa: E402

LOCK = threading.Lock()
CAPTURED = []
SEEN_KEYS = {}
UPSTREAM = None  # (scheme, netloc), set by start_proxy()


def _forward(method, path, headers, body_bytes):
    scheme, netloc = UPSTREAM
    conn_cls = http.client.HTTPSConnection if scheme == "https" else http.client.HTTPConnection
    conn = conn_cls(netloc, timeout=60)
    # Host/Content-Length are recomputed by http.client for the new target; Accept-Encoding is
    # dropped so the upstream answers uncompressed — this proxy does not implement gzip framing, and a
    # compressed body would otherwise get relayed with headers that no longer describe it.
    fwd_headers = {
        k: v for k, v in headers.items() if k.lower() not in ("host", "content-length", "accept-encoding")
    }
    try:
        conn.request(method, path, body=body_bytes, headers=fwd_headers)
        resp = conn.getresponse()
        resp_body = resp.read()
        resp_headers = dict(resp.getheaders())
        return resp.status, resp_body, resp_headers
    finally:
        conn.close()


class ProxyHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _body(self):
        n = int(self.headers.get("Content-Length", 0))
        return self.rfile.read(n) if n else b""

    def _handle(self, method):
        path = self.path
        raw = self._body()
        status, resp_body, resp_headers = _forward(method, path, dict(self.headers), raw)
        self._record(method, path.split("?", 1)[0], raw, status, resp_body, resp_headers)
        self.send_response(status)
        ctype = resp_headers.get("Content-Type") or resp_headers.get("content-type") or "application/json"
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(resp_body)))
        self.end_headers()
        if resp_body:
            self.wfile.write(resp_body)

    def _record(self, method, path, raw_req, status, raw_resp, resp_headers):
        req_body = None
        if raw_req:
            try:
                req_body = json.loads(raw_req.decode())
            except (json.JSONDecodeError, UnicodeDecodeError):
                req_body = None  # this API surface is JSON-only; an opaque body is not expected

        if req_body is not None and path.rstrip("/") == "/graphql":
            query_text = req_body.get("query", "") if isinstance(req_body, dict) else ""
            if "mutation" in query_text.lower().split()[:1] or query_text.lstrip().lower().startswith("mutation"):
                print(
                    f"record-board-fixture: WARNING — captured a MUTATION ({path}). The commands this "
                    "tool drives are all read-only (writes:never); a mutation here means the board was "
                    "changed by this capture run, which the fixture will now also reflect.",
                    file=sys.stderr,
                )

        resp_json = None
        if raw_resp:
            try:
                resp_json = json.loads(raw_resp.decode())
            except (json.JSONDecodeError, UnicodeDecodeError):
                resp_json = {"__nonJsonBody__": raw_resp.decode("latin1")}
        if resp_json is not None:
            resp_json = lib.normalize_body(resp_json)
            resp_json = lib.redact_body(resp_json)

        key = lib.request_key(method, path, json.dumps(req_body) if req_body is not None else "")
        entry = {
            "method": method,
            "path": path,
            "requestBody": lib.redact_body(req_body) if req_body is not None else None,
            "status": status,
            "responseBody": resp_json,
            "responseHeaders": lib.normalize_response_headers(resp_headers),
        }
        with LOCK:
            if key in SEEN_KEYS:
                prior = CAPTURED[SEEN_KEYS[key]]
                if prior["responseBody"] != entry["responseBody"] or prior["status"] != entry["status"]:
                    print(
                        f"record-board-fixture: WARNING — {method} {path} answered differently on a "
                        "later call within the SAME capture session; keeping the first answer. The "
                        "board may have changed mid-capture.",
                        file=sys.stderr,
                    )
                return
            SEEN_KEYS[key] = len(CAPTURED)
            CAPTURED.append(entry)

    def do_GET(self):
        self._handle("GET")

    def do_POST(self):
        self._handle("POST")

    def do_PATCH(self):
        self._handle("PATCH")

    def do_DELETE(self):
        self._handle("DELETE")

    def do_PUT(self):
        self._handle("PUT")


def start_proxy(upstream_url):
    global UPSTREAM
    parsed = urllib.parse.urlsplit(upstream_url)
    UPSTREAM = (parsed.scheme, parsed.netloc)
    server = ThreadingHTTPServer(("127.0.0.1", 0), ProxyHandler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server, server.server_address[1]


COMMANDS = {
    "reconcile": lambda repo: ["reconcile", "--repo", repo, "--json"],
    "ready": lambda repo: ["ready", "--repo", repo, "--json"],
    "driver-events": lambda repo: ["driver", "--events", "--repo", repo, "--json"],
}


def run_engine_command(engine, command_args, api_base, owner, project, token_env, cache_dir):
    env = dict(os.environ)
    env["FSGG_GITHUB_API_BASE"] = api_base
    env["FSGG_COORD_OWNER"] = owner
    env["FSGG_COORD_PROJECT"] = project
    env["FSGG_COORD_SCAN_TTL_SEC"] = "0"  # every capture pass must actually hit the proxy
    # ISOLATED, not merely unset. `FSGG_COORD_CACHE` unset falls back to `Cache.root()`'s default —
    # `$XDG_CACHE_HOME/fsgg-coord` (usually `~/.cache/fsgg-coord`) — which is PERSISTENT and SHARED
    # across every invocation on the machine. An operator capturing more than once, or capturing
    # right after running the engine for any other reason, would silently mix a stale cached read
    # into what should be a fresh transcript — the same isolation `tests/coord-engine-e2e/run.sh`
    # gives its own runs a `mktemp -d` for. `--commands` share the one directory this call is handed,
    # matching how one real `fsgg-coord` session behaves.
    env["FSGG_COORD_CACHE"] = cache_dir
    if token_env not in env or not env[token_env].strip():
        print(
            f"record-board-fixture: ${token_env} is not set. The engine requires SOME token to send "
            "even against a hermetic upstream that ignores its value (`scan` refuses without one, "
            ".github#... see tests/coord-engine-e2e/run.sh leg 5).",
            file=sys.stderr,
        )
        sys.exit(1)
    return subprocess.run(
        [engine] + command_args, env=env, capture_output=True, text=True, timeout=180
    )


def main():
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--repo", required=True, help="the repo name as the engine expects it, e.g. .github")
    ap.add_argument("--out", required=True, type=Path, help="fixture directory to write")
    ap.add_argument("--upstream", default="https://api.github.com")
    ap.add_argument("--owner", default=os.environ.get("FSGG_COORD_OWNER", "FS-GG"))
    ap.add_argument("--project", default=os.environ.get("FSGG_COORD_PROJECT", "Coordination"))
    ap.add_argument("--engine-bin", default=None, help="path to the compiled fsgg-coord-engine binary")
    ap.add_argument("--commands", default="reconcile,ready,driver-events")
    ap.add_argument("--token-env", default="GITHUB_TOKEN")
    args = ap.parse_args()

    repo_root = Path(__file__).resolve().parent.parent
    engine = args.engine_bin or str(repo_root / "src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine")
    if not Path(engine).exists():
        print(
            f"record-board-fixture: the engine must be built first: "
            f"dotnet build src/FS.GG.Coord.Cli -c Release (looked at {engine})",
            file=sys.stderr,
        )
        sys.exit(1)

    wanted = [c.strip() for c in args.commands.split(",") if c.strip()]
    unknown = [c for c in wanted if c not in COMMANDS]
    if unknown:
        print(
            f"record-board-fixture: unknown command(s) {unknown}; choose from {sorted(COMMANDS)}",
            file=sys.stderr,
        )
        sys.exit(1)
    if not wanted:
        print("record-board-fixture: --commands named nothing to capture.", file=sys.stderr)
        sys.exit(1)

    server, port = start_proxy(args.upstream)
    api_base = f"http://127.0.0.1:{port}"

    with tempfile.TemporaryDirectory(prefix="record-board-fixture-cache-") as cache_dir:
        outputs = {}
        failures = []
        for name in wanted:
            proc = run_engine_command(
                engine, COMMANDS[name](args.repo), api_base, args.owner, args.project,
                args.token_env, cache_dir,
            )
            if proc.returncode != 0:
                failures.append((name, proc.returncode, proc.stderr.strip()))
                continue
            try:
                parsed = json.loads(proc.stdout)
            except json.JSONDecodeError:
                failures.append((name, proc.returncode, f"non-JSON stdout: {proc.stdout[:200]!r}"))
                continue
            outputs[name] = lib.strip_volatile_output(parsed)

    server.shutdown()

    if failures:
        for name, rc, err in failures:
            print(f"record-board-fixture: `{name}` exited {rc}: {err}", file=sys.stderr)
        print("record-board-fixture: refusing to write a fixture from a failed capture run.", file=sys.stderr)
        sys.exit(1)

    args.out.mkdir(parents=True, exist_ok=True)
    expected_dir = args.out / "expected"
    expected_dir.mkdir(exist_ok=True)
    for name, payload in outputs.items():
        with open(expected_dir / f"{name}.json", "w") as f:
            json.dump(payload, f, indent=2, sort_keys=True)
            f.write("\n")

    entries = lib.canonical_entries(CAPTURED)
    transcript = {
        "schema": lib.SCHEMA,
        "repo": args.repo,
        "owner": args.owner,
        "project": args.project,
        "commands": wanted,
        "entries": entries,
    }
    with open(args.out / "transcript.json", "w") as f:
        json.dump(transcript, f, indent=2, sort_keys=True)
        f.write("\n")

    print(
        f"record-board-fixture: wrote {len(entries)} recorded request(s) and "
        f"{len(outputs)} expected output(s) to {args.out}"
    )


if __name__ == "__main__":
    main()
