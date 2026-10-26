#!/usr/bin/env python3
"""Optional bounded GET-only issue-comment observer. No dispatch or native authority."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import selectors
import signal
import stat
import subprocess
import sys
import time
from urllib.parse import urlencode

_spec = importlib.util.spec_from_file_location('evidence_delta', Path(__file__).parents[1] / 'scripts/evidence-delta.py')
assert _spec and _spec.loader
safe = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(safe)
SCHEMA = 'fsgg.issue-comment-cursor/1'
EVENT = 'fsgg.issue-comment-observation/1'
READ_SECONDS = 5.0
CLEANUP_SECONDS = 1.0
MAX_BYTES = 262144
MAX_PAGES = 4
MAX_EMIT = 65536

class ObservationError(RuntimeError):
    def __init__(self, reason, *, partial_bytes=0, cleanup='not-started', primary_error=None, cleanup_error=None):
        super().__init__(reason)
        self.partial_bytes = partial_bytes
        self.cleanup = cleanup
        self.primary_error = primary_error
        self.cleanup_error = cleanup_error

class Deadline(ObservationError):
    pass

def capture(command, deadline, *, clock=time.monotonic):
    """Drain bounded stdout, with a full read and cleanup reserve before spawning."""
    if deadline - clock() < READ_SECONDS + CLEANUP_SECONDS:
        raise Deadline('insufficient original query and cleanup reserve')
    data = bytearray()
    selector = None
    cleanup = 'unknown'
    child = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                             stderr=subprocess.DEVNULL, start_new_session=True)
    # Own the child before selector construction, clock reads or descriptor setup can fail.
    try:
        cutoff = min(deadline - CLEANUP_SECONDS, clock() + READ_SECONDS)
        selector = selectors.DefaultSelector()
        assert child.stdout
        os.set_blocking(child.stdout.fileno(), False)
        selector.register(child.stdout, selectors.EVENT_READ)
        while selector.get_map():
            remaining = cutoff - clock()
            if remaining <= 0:
                raise ObservationError('comment read timed out', partial_bytes=len(data))
            for key, _ in selector.select(remaining):
                block = os.read(key.fd, min(65536, MAX_BYTES + 1 - len(data)))
                if not block:
                    selector.unregister(key.fileobj)
                    continue
                data.extend(block)
                if len(data) > MAX_BYTES:
                    raise ObservationError('comment read exceeded byte bound', partial_bytes=len(data))
        code = child.wait(timeout=max(0.001, cutoff - clock()))
        if code:
            raise ObservationError('comment GET failed', partial_bytes=len(data))
        return bytes(data)
    except BaseException as error:
        # Preserve the primary failure even if cancellation/setup/cleanup fails too.
        # Reaping this direct child does not establish settlement of escaped descendants.
        cleanup_error = None
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        except BaseException as failure:
            cleanup_error = type(failure).__name__
        try:
            child.wait(timeout=CLEANUP_SECONDS)
            cleanup = 'direct-child-reaped; descendants-unverified'
        except BaseException as failure:
            cleanup_error = cleanup_error or type(failure).__name__
        reason = (str(error) if isinstance(error, ObservationError) else
                  'comment reader cancelled' if isinstance(error, KeyboardInterrupt) else
                  'comment reader failed')
        raise ObservationError(reason, partial_bytes=len(data), cleanup=cleanup,
                               primary_error=type(error).__name__, cleanup_error=cleanup_error) from error
    finally:
        # Descriptor cleanup must not mask the first failure or interrupt process cleanup.
        for resource in (selector, child.stdout):
            if resource is not None:
                try:
                    resource.close()
                except BaseException:
                    pass

def normalize_since(value):
    if value is None:
        return None
    # Finite datetime precision avoids silently collapsing distinct submicrosecond windows.
    safe.require(isinstance(value, str) and re.fullmatch(
        r'[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt](?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](?:\.[0-9]{1,6})?(?:[Zz]|[+-](?:[01][0-9]|2[0-3]):[0-5][0-9])', value),
        'since must be RFC3339 with timezone and at most six fractional digits')
    parsed = datetime.fromisoformat(value.upper().replace('Z', '+00:00'))
    try:
        return parsed.astimezone(timezone.utc).isoformat().replace('+00:00', 'Z')
    except OverflowError as error:
        raise ValueError('since lies outside the supported UTC date range') from error

def get_page(repo, issue, page, deadline, *, since=None):
    # Both command and projection are fixed. No caller command, URL, body or credentials are emitted.
    query = {'per_page': 100, 'page': page}
    since = normalize_since(since)
    if since is not None:
        query['since'] = since
    command = ['gh', 'api', '--hostname', 'github.com', '--method', 'GET',
               f'repos/{repo}/issues/{issue}/comments?{urlencode(query)}',
               '--jq', '[.[] | {id,updated_at,body}]']
    raw = capture(command, deadline)
    try:
        rows = json.loads(raw, object_pairs_hook=safe.pairs)
        safe.require(isinstance(rows, list) and len(rows) <= 100, 'invalid comment page')
        output = []
        for row in rows:
            safe.require(isinstance(row, dict) and set(row) == {'id', 'updated_at', 'body'}
                         and type(row['id']) is int and row['id'] > 0
                         and isinstance(row['updated_at'], str) and 0 < len(row['updated_at']) < 64
                         and isinstance(row['body'], str), 'malformed comment')
            output.append({'id': row['id'], 'updatedAt': row['updated_at'],
                           'bodySha256': hashlib.sha256(row['body'].encode()).hexdigest()})
        return output
    except (ValueError, UnicodeError) as error:
        raise ObservationError('malformed comment JSON', partial_bytes=len(raw), cleanup='direct-child-reaped; descendants-unverified') from error

def snapshot(repo, issues, request, deadline, *, since=None, read=get_page, clock=time.monotonic):
    since = normalize_since(since)
    comments = {}
    complete_pages = 0
    try:
        for issue in issues:
            rows = []
            for page in range(1, MAX_PAGES + 1):
                if deadline - clock() < READ_SECONDS + CLEANUP_SECONDS:
                    raise Deadline('insufficient original query and cleanup reserve')
                batch = (read(repo, issue, page, deadline) if since is None else
                         read(repo, issue, page, deadline, since=since))
                complete_pages += 1
                rows.extend(batch)
                safe.require(len({row['id'] for row in rows}) == len(rows), 'duplicate comment identity')
                if len(batch) < 100:
                    break
            else:
                raise ObservationError('comment pagination bound reached')
            comments[str(issue)] = sorted(rows, key=lambda r: r['id'])
        cursor = {'schema': SCHEMA, 'repository': repo, 'issues': issues, 'originalRequest': request,
                  'successful': True, 'since': since, 'comments': comments}
        safe.require(len(json.dumps(cursor).encode()) <= 48000, 'cursor population exceeds output budget')
        return cursor, complete_pages
    except (ObservationError, ValueError) as error:
        if not isinstance(error, ObservationError):
            error = ObservationError('malformed complete comment population')
        error.complete_pages = complete_pages
        raise error

def validate_cursor(cursor, repo, issues, request, *, since=None):
    safe.require(len(json.dumps(cursor).encode()) <= 48000, 'baseline cursor exceeds output budget')
    safe.require(cursor.get('schema') == SCHEMA and cursor.get('repository') == repo
                 and safe.same(cursor.get('issues'), issues) and cursor.get('successful') is True
                 and safe.text(cursor.get('originalRequest')) and cursor['originalRequest'] == request
                 and cursor.get('since') == normalize_since(since)
                 and isinstance(cursor.get('comments'), dict)
                 and set(cursor['comments']) == {str(i) for i in issues}, 'baseline successful cursor identity differs')
    for rows in cursor['comments'].values():
        safe.require(isinstance(rows, list) and len(rows) < MAX_PAGES * 100, 'cursor population exceeds bound')
        seen = set()
        for row in rows:
            safe.require(isinstance(row, dict) and set(row) == {'id', 'updatedAt', 'bodySha256'}
                         and type(row['id']) is int and row['id'] > 0 and row['id'] not in seen
                         and isinstance(row['updatedAt'], str) and 0 < len(row['updatedAt']) < 64
                         and isinstance(row['bodySha256'], str) and safe.SHA.fullmatch(row['bodySha256']), 'malformed baseline comment identity')
            seen.add(row['id'])

def observe(repo, issues, request, seconds, baseline, *, since=None, read=get_page, clock=time.monotonic, sleep=time.sleep):
    deadline = clock() + seconds
    previous = baseline
    while True:
        try:
            current, pages = snapshot(repo, issues, request, deadline, since=since, read=read, clock=clock)
        except ObservationError as error:
            return {'event': 'deadline' if isinstance(error, Deadline) else 'observation-failure',
                    'reason': str(error), 'partialBytes': error.partial_bytes,
                    'completePagesInFailedSample': getattr(error, 'complete_pages', 0),
                    'readerCleanup': error.cleanup, 'readerPrimaryErrorType': error.primary_error,
                    'readerCleanupErrorType': error.cleanup_error, 'lastCompleteCursor': previous}
        if clock() >= deadline - CLEANUP_SECONDS:
            return {'event': 'deadline', 'reason': 'sample completed after observation reserve',
                    'lastCompleteCursor': previous}
        if not safe.same(current['comments'], previous['comments']):
            delta = {}
            for issue in map(str, issues):
                old = {str(r['id']): r for r in previous['comments'][issue]}
                new = {str(r['id']): r for r in current['comments'][issue]}
                delta[issue] = safe.changes(old, new)
            return {'event': 'change', 'delta': delta, 'completePages': pages, 'lastCompleteCursor': current}
        previous = current
        sleep(min(2, max(0, deadline - CLEANUP_SECONDS - clock())))

def lock_operation(directory, repo, issues, request):
    """One cooperating observer for this exact caller-selected operation, held through return."""
    root = Path(directory)
    root.mkdir(mode=0o700, parents=False, exist_ok=True)
    info = root.lstat()
    safe.require(stat.S_ISDIR(info.st_mode) and not stat.S_ISLNK(info.st_mode)
                 and info.st_uid == os.getuid() and info.st_mode & 0o077 == 0, 'private custody directory required')
    key = hashlib.sha256(json.dumps([repo, issues, request], separators=(',', ':')).encode()).hexdigest()
    fd = os.open(root / (key + '.lock'), os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600)
    try:
        info = os.fstat(fd)
        safe.require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == os.getuid()
                     and info.st_mode & 0o077 == 0, 'invalid cooperative lock file')
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return fd
    except BaseException:
        os.close(fd)
        raise

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['snapshot', 'watch'])
    parser.add_argument('--repo', required=True)
    parser.add_argument('--issue', type=int, action='append', required=True)
    parser.add_argument('--original-request', required=True)
    parser.add_argument('--seconds', type=int, required=True)
    parser.add_argument('--custody-dir', required=True)
    parser.add_argument('--baseline')
    parser.add_argument('--baseline-sha256')
    parser.add_argument('--since', help='fixed RFC3339 updated-comment coverage boundary; default unfiltered')
    args = parser.parse_args(argv)
    lock = None
    baseline = None
    since = None
    try:
        since = normalize_since(args.since)
        safe.require(re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', args.repo)
                     and all(v not in {'.', '..'} for v in args.repo.split('/')), 'invalid repository')
        safe.require(1 <= len(args.issue) <= 2 and len(set(args.issue)) == len(args.issue)
                     and all(0 < i <= 2147483647 for i in args.issue), 'select one or two distinct issues')
        safe.require(safe.text(args.original_request) and 1 <= args.seconds <= 600, 'finite window and original request required')
        issues = sorted(args.issue)
        baseline = None
        if args.action == 'watch':
            safe.require(args.baseline and args.baseline_sha256, 'explicit successful baseline cursor and SHA256 pin required')
            selected, _ = safe.load(args.baseline, args.baseline_sha256)
            validate_cursor(selected, args.repo, issues, args.original_request, since=since)
            baseline = selected
        else:
            safe.require(not args.baseline and not args.baseline_sha256, 'snapshot cannot replace an existing baseline')
        lock = lock_operation(args.custody_dir, args.repo, issues, args.original_request)
        if args.action == 'snapshot':
            cursor, _ = snapshot(args.repo, issues, args.original_request, time.monotonic() + args.seconds, since=since)
            result = cursor
        else:
            result = {'schema': EVENT, 'repository': args.repo, 'issues': issues,
                      'originalRequest': args.original_request, 'since': since,
                      **observe(args.repo, issues, args.original_request, args.seconds, baseline, since=since),
                      'nativeAcceptance': 'unknown', 'dispatchAuthorized': False}
        raw = json.dumps(result, sort_keys=True, separators=(',', ':'))
        safe.require(len(raw.encode()) <= MAX_EMIT, 'observer output exceeds bound')
        print(raw)
        return 0 if args.action == 'snapshot' or result.get('event') == 'change' else 3
    except (ValueError, OSError, ObservationError, KeyboardInterrupt) as error:
        # Never include response bodies, argv, environment, stderr or credentials.
        print(json.dumps({'schema': EVENT, 'event': 'observation-failure', 'since': since, 'reason':
                          str(error) if isinstance(error, (ValueError, ObservationError)) else 'observer interrupted or unavailable',
                          'lastCompleteCursor': baseline, 'partialBytes': getattr(error, 'partial_bytes', 0),
                          'completePagesInFailedSample': getattr(error, 'complete_pages', 0),
                          'readerCleanup': getattr(error, 'cleanup', 'not-started'),
                          'readerPrimaryErrorType': getattr(error, 'primary_error', type(error).__name__),
                          'readerCleanupErrorType': getattr(error, 'cleanup_error', None),
                          'nativeAcceptance': 'unknown', 'dispatchAuthorized': False}))
        return 3
    finally:
        if lock is not None:
            os.close(lock)

if __name__ == '__main__':
    raise SystemExit(main())
