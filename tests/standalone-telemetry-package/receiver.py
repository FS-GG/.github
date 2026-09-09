#!/usr/bin/env python3
"""Small stateful HTTPS receiver for the source-free package qualification."""
import argparse
import hashlib
import http.server
import json
import os
import pathlib
import ssl


def compact(value):
    return json.dumps(value, separators=(",", ":"), sort_keys=True).encode()


class Receiver(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, _format, *_args):
        pass

    def event(self, kind, **values):
        with self.server.log.open("a", encoding="utf-8") as output:
            output.write(json.dumps({"kind": kind, **values}, separators=(",", ":")) + "\n")

    def reply(self, status, body):
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(body)

    def error(self, status, code):
        self.reply(status, compact({"schema": "fsgg.telemetry.error/1", "code": code}))

    def authorized(self):
        if self.headers.get("Authorization") == "Bearer " + self.server.token:
            return True
        self.event("auth-refused")
        self.error(401, "unauthorized-scope")
        return False

    def receipt(self, batch, digest):
        workspace, producer, stream = self.server.scope
        return compact({"schema": "fsgg.telemetry.receipt/1", "workspaceId": workspace,
                        "producerId": producer, "streamId": stream, "batchId": batch,
                        "digest": digest, "status": "applied", "code": None})

    def load(self):
        if not self.server.state.exists():
            return {}
        return json.loads(self.server.state.read_text(encoding="utf-8"))

    def save(self, state):
        temporary = self.server.state.with_suffix(".tmp")
        temporary.write_text(json.dumps(state, separators=(",", ":")), encoding="utf-8")
        temporary.replace(self.server.state)

    def do_GET(self):
        if not self.authorized():
            return
        prefix = "/v1/receipts/"
        if not self.path.startswith(prefix):
            self.error(404, "receipt-unavailable")
            return
        batch = self.path[len(prefix):]
        state = self.load()
        if batch not in state:
            self.event("lookup-miss", batch=batch)
            self.error(404, "receipt-unavailable")
            return
        self.event("lookup-hit", batch=batch)
        self.reply(200, self.receipt(batch, state[batch]))

    def do_POST(self):
        if not self.authorized():
            return
        if self.path != "/v1/batches":
            self.error(404, "invalid-request")
            return
        try:
            length = int(self.headers.get("Content-Length", "-1"))
            if length < 0 or length > 73728:
                raise ValueError("invalid length")
            body = self.rfile.read(length)
            envelope = json.loads(body)
            workspace, producer, stream = self.server.scope
            if (envelope.get("workspaceId"), envelope.get("producerId"), envelope.get("streamId")) != (workspace, producer, stream):
                self.error(403, "unauthorized-scope")
                return
            batch = envelope["batchId"]
            digest = hashlib.sha256(body).hexdigest()
        except (ValueError, KeyError, json.JSONDecodeError):
            self.error(400, "invalid-request")
            return
        mode = self.server.mode.read_text(encoding="utf-8").strip()
        if mode == "mismatch":
            self.event("mismatch", batch=batch)
            self.reply(202, self.receipt(batch, "0" * 64))
            return
        if mode == "oversized":
            self.event("oversized", batch=batch)
            self.reply(202, b"{" + b"x" * 5000 + b"}")
            return
        state = self.load()
        prior = state.get(batch)
        if prior is not None and prior != digest:
            self.error(409, "identity-conflict")
            return
        state[batch] = digest
        self.save(state)
        self.event("accepted", batch=batch, replay=prior is not None)
        marker = self.server.state.with_name("dropped-" + batch)
        if mode in ("drop-once", "drop-exit") and not marker.exists():
            marker.touch()
            self.event("response-dropped", batch=batch)
            self.close_connection = True
            if mode == "drop-exit":
                os._exit(0)
            return
        self.reply(202, self.receipt(batch, digest))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--cert", required=True)
    parser.add_argument("--key", required=True)
    parser.add_argument("--state", required=True)
    parser.add_argument("--mode", required=True)
    parser.add_argument("--log", required=True)
    parser.add_argument("--token", required=True)
    parser.add_argument("--scope", nargs=3, required=True)
    args = parser.parse_args()
    server = http.server.ThreadingHTTPServer(("127.0.0.1", args.port), Receiver)
    server.state = pathlib.Path(args.state)
    server.mode = pathlib.Path(args.mode)
    server.log = pathlib.Path(args.log)
    server.token = args.token
    server.scope = tuple(args.scope)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(args.cert, args.key)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
