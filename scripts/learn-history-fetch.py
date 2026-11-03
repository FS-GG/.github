#!/usr/bin/env python3
"""Bounded READ-ONLY native history fetch for explicitly selected node IDs.

No search, dispatch, mutation or automatic retry. Outputs are private source evidence.
A truncated nested connection is retained as truncated; the evaluator refuses to qualify it.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

FIELDS = '''id number title body createdAt updatedAt lastEditedAt closedAt state
userContentEdits(first:100) { totalCount pageInfo { hasNextPage endCursor }
 nodes { id createdAt editedAt deletedAt diff } }
comments(first:100) { totalCount pageInfo { hasNextPage endCursor }
 nodes { id createdAt updatedAt body } }
timelineItems(first:100,itemTypes:[CLOSED_EVENT,REOPENED_EVENT,CROSS_REFERENCED_EVENT,RENAMED_TITLE_EVENT]) {
 totalCount pageInfo { hasNextPage endCursor } nodes { __typename
 ... on ClosedEvent { id createdAt closer { __typename ... on PullRequest { id url mergedAt } } }
 ... on ReopenedEvent { id createdAt }
 ... on RenamedTitleEvent { id createdAt previousTitle currentTitle }
 ... on CrossReferencedEvent { id createdAt source { __typename
 ... on PullRequest { id number url mergedAt } ... on Issue { id number url } } } } }'''
PR_FIELDS = '''mergedAt commits(first:100) { totalCount pageInfo { hasNextPage endCursor }
 nodes { commit { oid committedDate authoredDate } } }
 reviews(first:100) { totalCount pageInfo { hasNextPage endCursor } nodes { id submittedAt state } }
 closingIssuesReferences(first:100) { totalCount pageInfo { hasNextPage endCursor } nodes { id url } }'''


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--ids', required=True, type=Path, help='private JSON array of explicit node ID strings')
    p.add_argument('--output', required=True, type=Path, help='new private directory; never overwrite evidence')
    p.add_argument('--max-requests', type=int, default=40)
    a = p.parse_args()
    if not 1 <= a.max_requests <= 250:
        raise ValueError('request limit must be 1..250')
    ids = json.loads(a.ids.read_text())
    if not isinstance(ids, list) or any(not isinstance(n, str) for n in ids) or len(ids) > a.max_requests or len(set(ids)) != len(ids):
        raise ValueError('invalid, duplicate or excessive explicit selection')
    if any(not isinstance(n, str) or not n or '/' in n for n in ids):
        raise ValueError('invalid node ID')
    os.umask(0o077)
    a.output.mkdir(parents=True, mode=0o700, exist_ok=False)
    now = lambda: dt.datetime.now(dt.timezone.utc).isoformat()
    m = {'schema': 'fsgg.learn-history-audit/1', 'startedAt': now(),
         'selectionSha256': hashlib.sha256(a.ids.read_bytes()).hexdigest(),
         'maxRequests': a.max_requests, 'queries': []}
    start = time.monotonic()
    total_bytes = 0
    env = dict(os.environ, GOMAXPROCS='1', GOMEMLIMIT='128MiB')
    try:
        for node in ids:
            if time.monotonic() - start >= 900 or total_bytes >= 32 * 1024 * 1024:
                raise ValueError('time or private-output bound reached')
            query = 'query { node(id:' + json.dumps(node) + ') { ... on Issue { ' + FIELDS + ' stateReason } ... on PullRequest { ' + FIELDS + PR_FIELDS + ' } } }'
            r = {'nodeId': node, 'at': now(), 'querySha256': hashlib.sha256(query.encode()).hexdigest()}
            m['queries'].append(r)
            try:
                proc = subprocess.run(['gh', 'api', 'graphql', '-f', 'query=' + query], capture_output=True,
                                      text=True, env=env, timeout=min(120, 900 - (time.monotonic() - start)))
                r['exit'] = proc.returncode
                if proc.returncode:
                    r['error'] = proc.stderr[:500]
                    continue
                data = json.loads(proc.stdout)
                if data.get('errors') or data.get('data', {}).get('node') is None:
                    r['error'] = 'GraphQL errors or missing node'
                    continue
                payload = json.dumps(data).encode()
                if total_bytes + len(payload) > 32 * 1024 * 1024:
                    r['error'] = 'private-output bound'
                    raise ValueError('private-output bound reached')
                (a.output / ('audit-' + node + '.json')).write_bytes(payload)
                total_bytes += len(payload)
                r['sha256'] = hashlib.sha256(payload).hexdigest()
            except subprocess.TimeoutExpired:
                r['exit'] = None
                r['error'] = 'read timeout; no automatic retry'
    finally:
        m['endedAt'] = now()
        m['retainedBytes'] = total_bytes
        (a.output / 'audit-manifest.json').write_text(json.dumps(m, indent=2) + '\n')
    print(json.dumps({'attempted': len(m['queries']), 'succeeded': sum('sha256' in r for r in m['queries']),
                      'requested': len(ids), 'bytes': total_bytes}))


if __name__ == '__main__':
    main()
