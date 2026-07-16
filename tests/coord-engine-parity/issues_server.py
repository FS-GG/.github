#!/usr/bin/env python3
"""Case 13's `issues` short-id command (#446) — served over HTTP for the compiled engine.

`issues` lists a repo's issues over REST with ETag revalidation — the command both coordination skills
advertise as THE way to read issues WITHOUT spending GraphQL (a 304 costs nothing, #418). The corpus
(`tests/fsgg-coord/cases/13-repo-scope-and-lint.sh`, #446) certifies that it resolves its `<repo>` argument
like EVERY other repo-taking command: an `owner/repo` passes through split, a bare short-id maps through
`resolve_repo` to the repo NAME board rows carry.

bash's bug: `issues` was the ONE repo-taking command that took the bare token VERBATIM — so `issues game`
asked GitHub for `repos/FS-GG/game` and 404'd, while `--repo game` resolved everywhere else. That is worse
than a typo-class bug: the natural recovery from the 404 is `gh issue list` — 2 GraphQL points a call, the
exact budget the command exists to save.

This fixture is a pure REST issue list. It records the `owner/repo` (and state/label/If-None-Match) of every
`/repos/*/issues` request, so the harness can re-express the corpus's `issue-list FS-GG/<repo>` `gh`-log
assertion one transport under: the fixture must have been asked for `FS-GG/FS.GG.Game`, NEVER `FS-GG/game`.
It also carries a fixed ETag and answers a matching `If-None-Match` with 304, so "a 304 is free" is provable
as a conditional request served from the engine's cache with no fresh body.

No GraphQL endpoints: `issues` is a pure REST read — it never bootstraps the board — so a fixture that
served GraphQL would be over-specifying a world the command never touches.
"""

import json
import re
import sys
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

# A fixed validator. GitHub's issue-list responses carry a weak ETag; the value is opaque — all that
# matters is that the engine stores it and sends it back, and a match answers 304.
ETAG = 'W/"issues-corpus-v1"'

# The listing content is irrelevant to #446 (the property under test is the RESOLVED PATH, not the body),
# but a non-empty array proves the engine emits the REST body verbatim for the caller to jq. Two issues and
# a PR (777 carries `pull_request`): #641 — `issues` must drop it, so the §4 duplicate-check never reads a
# PR as an already-filed issue. The engine filters it out; only 501/502 (genuine issues) survive.
BODY = [
    {"number": 501, "title": "a real issue", "state": "open"},
    {"number": 502, "title": "another issue", "state": "open"},
    {"number": 777, "title": "a pull request (an issue in REST)", "state": "open",
     "pull_request": {"url": "https://api.github.com/repos/x/y/pulls/777"}},
]

LOCK = threading.Lock()
_REQUESTS = []  # every /repos/*/issues request: {"nwo", "state", "label", "inm"}


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, code, payload, etag=False):
        b = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        if etag:
            self.send_header("ETag", ETAG)
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def _send_304(self):
        # A 304 carries no body, but keeps the validator — exactly as GitHub answers a matching
        # If-None-Match. It is the free read the ETag bought.
        self.send_response(304)
        self.send_header("ETag", ETAG)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        path, _, qs = self.path.partition("?")
        if path == "/_requests":
            with LOCK:
                return self._send(200, list(_REQUESTS))

        m = re.match(r"^/repos/([^/]+)/([^/]+)/issues/?$", path)
        if m:
            nwo = f"{m.group(1)}/{m.group(2)}"
            params = dict(p.split("=", 1) for p in qs.split("&") if "=" in p)
            inm = self.headers.get("If-None-Match")
            with LOCK:
                _REQUESTS.append({"nwo": nwo,
                                  "state": params.get("state", ""),
                                  "label": params.get("labels", ""),
                                  "inm": inm if inm else "none"})
            # The conditional read the ETag makes free: a matching validator answers 304, and the engine
            # serves the body from its own cache.
            if inm == ETAG:
                return self._send_304()
            return self._send(200, BODY, etag=True)

        self._send(404, {"message": f"unhandled GET {path}"})


def main():
    s = HTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)
