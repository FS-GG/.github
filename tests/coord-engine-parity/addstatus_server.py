#!/usr/bin/env python3
"""`add` DEFAULTS Status to `Backlog` (.github#1823), served over HTTP for the compiled engine.

`add` used to put an issue on the board and leave `Status` UNSET. A row with no `Status` is invisible to
every scheduler — `Schedulability` says so in as many words: *"no Status on the board: invisible to every
scheduler, and nobody set it."* Fourteen rows were filed that way on 2026-07-28, in three batches, and
EVERY instance was found by accident by a driver reading `batch` output for an unrelated reason. Nothing
reported any of them.

THIS FIXTURE IS A BOARD, NOT A SCRIPT OF ANSWERS, and that is load-bearing rather than tidy. The
`addProjectV2ItemById` mutation really adds the item; `updateProjectV2ItemFieldValue` really moves the
column. So `GET /_state` answers *"what Status does the board hold now?"* — a fact about the board, which
is what the defect was about — and no assertion has to read the words the CLI printed about itself. A
canned server would answer the post-add item lookup with "not on board", the Status write would silently
become a no-op, and the harness would certify the opposite of what it believes.

The board (project #12 "Coordination", every row in FS-GG/.github):

    #901  NOT on the board          `add` boards it, then defaults it to Backlog
    #902  on board, Status UNSET    the fourteen rows' condition — `add` REPAIRS it to Backlog,
                                    and `lint` reports STATUS-UNSET on it (AC5)
    #903  on board, "In progress"   THE IDEMPOTENCE ROW (#861/AC4): `add` must leave it ALONE.
                                    A naive "set Status on add" walks it back to Backlog and destroys
                                    information rather than adding it
    #904  on board, Status UNSET    the explicit-column row: `add --status Ready` writes Ready (AC2)
    #905  on board, "Ready"         STATUS-UNSET's NEGATIVE — a row with a column is silent
    #906  on board, Status UNSET    the no-identity row: with the whole identity ladder unset, `add`
                                    must still BOARD it and degrade the column by name. #1823 ruled out
                                    making `add` refuse, and a default that needs a worker id could
                                    re-introduce exactly that refusal

Env toggles, one per invocation:

    ADDSTATUS_MUTATE=preset-unset   #903 starts with NO Status instead of "In progress".

        THE FIXTURE MUTATION (.github#1808). The AC4 leg asserts an ABSENCE — that no Status write was
        sent for #903 — and an absence is exactly what a probe that has quietly stopped observing also
        reports. So the harness mutates the FIXTURE in the one direction that must flip the verdict: with
        #903's column emptied, the same engine and the same probe must now report a Backlog write. If the
        probe were broken, both runs would agree, and the disagreement is the proof that it fires.

    ADDSTATUS_STATUS_UNREADABLE=903 the `itemStatus` read for #903 FAILS (HTTP 502).

        #266's arm. "I could not read the column" is not "the column is empty", and defaulting over an
        unread column is the same destruction AC4 forbids, reached through a fabricated absence instead of
        a missing check.
"""

import json
import os
import re
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4970}
OWNER_REPO = "FS-GG/.github"
PROJECT = 12
PROJECT_ID = "PVT_coord"
STATUS_FIELD_ID = "PVTSSF_status"

# The board's Status column, name -> option id. `Backlog` is here because the board really has it; an
# engine that wrote a column this board does not offer would be refused at the field, not silently.
OPTIONS = {
    "Backlog": "opt_backlog",
    "Ready": "opt_ready",
    "In progress": "opt_wip",
    "In review": "opt_review",
    "Blocked": "opt_blocked",
    "Done": "opt_done",
}
BY_OPTION_ID = {v: k for k, v in OPTIONS.items()}

MUTATE = os.environ.get("ADDSTATUS_MUTATE", "")
UNREADABLE = {int(n) for n in os.environ.get("ADDSTATUS_STATUS_UNREADABLE", "").split(",") if n.strip()}

LOCK = threading.Lock()

# number -> {"item": item id or None (not on the board), "status": column name or None (unset)}
BOARD = {
    901: {"item": None, "status": None},
    902: {"item": "PVTI_902", "status": None},
    903: {"item": "PVTI_903", "status": "In progress"},
    904: {"item": "PVTI_904", "status": None},
    905: {"item": "PVTI_905", "status": "Ready"},
    906: {"item": "PVTI_906", "status": None},
}

if MUTATE == "preset-unset":
    BOARD[903]["status"] = None

# Every Status write the engine POSTed: {"item": ..., "option": "Backlog", ...}. `_state` says where the
# board ENDED UP; this says whether anything was written at all — and AC4's whole assertion is that
# NOTHING was, which `_state` alone cannot tell from "wrote the same value back".
WRITES = []

TITLES = {
    901: "a finding filed mid-item, not yet on the board",
    902: "on the board with no Status — invisible to every scheduler",
    903: "already being worked by somebody",
    904: "on the board with no Status, to be given one explicitly",
    905: "on the board and triaged",
    906: "on the board with no Status, for the no-identity leg",
}

# Every body declares a real touch-set and a class, so `lint`'s NO-TOUCH-SET / CLASS-UNSET rules stay
# silent and the STATUS axis is the only thing these rows can be reported for. A row that tripped three
# rules would let the AC5 leg pass on the wrong finding.
BODIES = {
    n: f"A row for .github#1823's fixture.\n\nPaths: src/FS.GG.Coord.Cli/Client.fs\n\nClass: defect"
    for n in BOARD
}


def _issue_node_id(number):
    return f"I_issue{number}"


def _scan_nodes():
    """The board as `Scan` reads it — only rows that are actually ON it."""
    with LOCK:
        rows = sorted((n, dict(s)) for n, s in BOARD.items() if s["item"] is not None)

    return [
        {
            # A row with no column has `status: null` — `NoStatus`, a case and never a default (#437).
            "status": {"name": s["status"]} if s["status"] else None,
            "severity": {"name": "Low"},
            "blockedBy": None,
            "content": {
                "__typename": "Issue",
                "number": n,
                "title": TITLES[n],
                "state": "OPEN",
                "repository": {"nameWithOwner": OWNER_REPO},
            },
        }
        for n, s in rows
    ]


def _number_in(document, variables):
    if variables and "number" in variables:
        return int(variables["number"])
    m = re.search(r"number:\s*(\d+)", document)
    return int(m.group(1)) if m else None


def graphql(q, variables):
    if "projectsV2" in q:
        return {
            "data": {"organization": {"projectsV2": {"nodes": [
                {"number": PROJECT, "title": "Coordination", "id": PROJECT_ID}]}}},
            "rateLimit": RATE,
        }

    if "fields(first" in q:
        return {
            "data": {"organization": {"projectV2": {"fields": {"nodes": [
                {"id": STATUS_FIELD_ID, "name": "Status", "dataType": "SINGLE_SELECT",
                 "options": [{"id": i, "name": n} for n, i in OPTIONS.items()]},
                {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}},
            "rateLimit": RATE,
        }

    if "items(first" in q:
        return {
            "data": {"organization": {"projectV2": {"items": {
                "pageInfo": {"hasNextPage": False, "endCursor": None},
                "nodes": _scan_nodes()}}}},
            "rateLimit": RATE,
        }

    # `Board.itemStatus` and `Board.itemId` both select `projectItems`; only the first asks for
    # `fieldValueByName`, so that is what tells them apart. A fixture keying on `projectItems` alone
    # answers one query with the other's shape.
    if "fieldValueByName" in q:
        n = _number_in(q, variables)
        if n in UNREADABLE:
            return {"data": None, "errors": [{"message": "stub: the Status column could not be read"}]}
        with LOCK:
            row = BOARD.get(n)
        if not row or row["item"] is None:
            return {"data": {"repository": {"issue": {"projectItems": {"nodes": []}}}}, "rateLimit": RATE}
        value = {"name": row["status"]} if row["status"] else None
        return {
            "data": {"repository": {"issue": {"projectItems": {"nodes": [
                {"project": {"number": PROJECT}, "fieldValueByName": value}]}}}},
            "rateLimit": RATE,
        }

    if "projectItems" in q:
        n = _number_in(q, variables)
        with LOCK:
            row = BOARD.get(n)
        nodes = [] if not row or row["item"] is None else [
            {"id": row["item"], "project": {"number": PROJECT}}]
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": nodes}}}}, "rateLimit": RATE}

    if "addProjectV2ItemById" in q:
        content = (variables or {}).get("contentId", "")
        m = re.search(r"I_issue(\d+)", str(content))
        n = int(m.group(1)) if m else None
        with LOCK:
            row = BOARD.get(n)
            if row is None:
                return {"errors": [{"message": f"stub: no issue {n}"}]}
            if row["item"] is None:
                # A NEW project item carries NO field values. That is the whole reason `add`'s
                # freshly-boarded arm may skip the column read.
                row["item"] = f"PVTI_{n}"
                row["status"] = None
            item = row["item"]
        return {"data": {"addProjectV2ItemById": {"item": {"id": item}}}, "rateLimit": RATE}

    if "issue(number: $number) { id }" in q or ("issue(number:" in q and "{ id }" in q and "projectItems" not in q):
        n = _number_in(q, variables)
        return {"data": {"repository": {"issue": {"id": _issue_node_id(n)}}}, "rateLimit": RATE}

    if "updateProjectV2ItemFieldValue" in q:
        v = variables or {}
        item, field, option = v.get("itemId"), v.get("fieldId"), v.get("optionId")
        if field != STATUS_FIELD_ID:
            return {"errors": [{"message": f"stub: this fixture only serves the Status field, got {field}"}]}
        name = BY_OPTION_ID.get(option)
        if name is None:
            return {"errors": [{"message": f"stub: '{option}' is not an option on the Status field"}]}
        with LOCK:
            WRITES.append({"item": item, "option": name})
            for row in BOARD.values():
                if row["item"] == item:
                    row["status"] = name
        return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}, "rateLimit": RATE}

    return None


class H(BaseHTTPRequestHandler):
    # Keep-alive, so the server does not close after every response: HTTP/1.0's close-per-response
    # races the engine's pooling HttpClient and RSTs away a written response (#761). Pairs with
    # ThreadingHTTPServer below — a kept-alive connection would pin a single-threaded server.
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _send(self, code, payload):
        b = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def do_POST(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        p = self.path.split("?", 1)[0]
        if p.rstrip("/") == "/graphql":
            try:
                payload = json.loads(raw)
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "bad body"}]})
            q = payload.get("query", "")
            a = graphql(q, payload.get("variables") or {})
            return self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:80]}"}]})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]

        # THE ANCHOR THE HARNESS READS: the board's own answer, not the CLI's account of it.
        if p.rstrip("/") == "/_state":
            with LOCK:
                return self._send(200, {str(n): s["status"] for n, s in sorted(BOARD.items())})

        # ...and whether anything was WRITTEN at all. AC4 asserts an absence, which `_state` cannot tell
        # from a write of the value that was already there.
        if p.rstrip("/") == "/_writes":
            with LOCK:
                return self._send(200, list(WRITES))

        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p):
            return self._send(200, [])

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if n in BODIES:
                return self._send(200, {"number": n, "body": BODIES[n], "state": "open"})
            return self._send(404, {"message": "Not Found"})

        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4970, "limit": 5000}}})

        self._send(500, {"message": f"unhandled GET {p}"})


def main():
    s = ThreadingHTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)
