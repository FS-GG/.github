#!/usr/bin/env python3
"""Replay the recorded Actions API transcript for check-gate-finding-history's fetch tests."""
import json, os, sys, base64
from urllib.parse import parse_qs
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

doc = json.loads(Path(sys.argv[1]).read_text())
class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_): pass
    def do_GET(self):
        path, _, query = self.path.partition('?')
        if os.environ.get('GFH_RATE_LIMIT'):
            self.send_response(429); self.send_header('Retry-After', '0'); self.end_headers(); return
        if path == '/repos/FS-GG/fixture': reply = doc['repo']
        elif path == '/repos/FS-GG/fixture/actions/workflows':
            reply = dict(doc['workflows'])
            if os.environ.get('GFH_TRUNCATED'): reply['total_count'] = 101
        elif path == '/repos/FS-GG/fixture/contents/.github/workflows/fixture.yml':
            reply = {'content': base64.b64encode(b'on: workflow_call\n').decode()}
        elif path.endswith('/runs'):
            # The status-filter shape is an API contract: an unfiltered reply here would make every
            # gate look exercised. The harness records every query so its caller can assert it.
            print(query, flush=True)
            params = parse_qs(query)
            status = params.get('status', [''])[0]
            reply = dict(doc['failureRuns'] if status == 'failure' else
                         {'total_count': doc['runs']['total_count'] - doc['failureRuns']['total_count'], 'workflow_runs': []} if status == 'success' else
                         {'total_count': 0, 'workflow_runs': []} if status == 'timed_out' else
                         doc['runs'])
            if os.environ.get('GFH_ZERO_RUNS'):
                reply = {'total_count': 0, 'workflow_runs': []}
        elif '/actions/runs/' in path and path.endswith('/jobs'):
            run_id = path.split('/actions/runs/', 1)[1].split('/', 1)[0]
            reply = doc['jobs'][run_id]
        elif '/check-runs/' in path and path.endswith('/annotations'):
            check_id = path.split('/check-runs/', 1)[1].split('/', 1)[0]
            if os.environ.get('GFH_EXPIRED') and check_id == '1002':
                self.send_error(410); return
            if os.environ.get('GFH_UNREAD_ANNOTATIONS') and check_id == '1002':
                self.send_error(500); return
            reply = doc['annotations'][check_id]
        else: self.send_error(404); return
        data = json.dumps(reply).encode(); self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
HTTPServer(('127.0.0.1', int(sys.argv[2])), Handler).serve_forever()
