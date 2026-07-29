#!/usr/bin/env python3
"""Replay the recorded Actions API transcript for check-gate-finding-history's fetch tests."""
import json, sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

doc = json.loads(Path(sys.argv[1]).read_text())
class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_): pass
    def do_GET(self):
        path, _, query = self.path.partition('?')
        if path == '/repos/FS-GG/fixture': reply = doc['repo']
        elif path == '/repos/FS-GG/fixture/actions/workflows': reply = doc['workflows']
        elif path.endswith('/runs'):
            # The status-filter shape is an API contract: an unfiltered reply here would make every
            # gate look exercised. The harness records every query so its caller can assert it.
            print(query, flush=True); reply = doc['runs']
        else: self.send_error(404); return
        data = json.dumps(reply).encode(); self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
HTTPServer(('127.0.0.1', int(sys.argv[2])), Handler).serve_forever()
