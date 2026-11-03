#!/usr/bin/env python3
"""Read-only historical-context audit; never launches raters or executes repository code.

`select` freezes metadata-only cases. `collect-case` performs bounded authenticated GET/GraphQL
reads, verifies a proven-before-cutoff checkpoint, and exports private exact Git blobs.
No source fetch, native mutation, automatic retry, replacement case or model call exists.
"""
from __future__ import annotations

import argparse
import collections
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import selectors
import subprocess
import sys
import time

SCHEMA = 'fsgg.learn-context-snapshot/1'
SELECTION_SCHEMA = 'fsgg.learn-context-selection/1'
SOURCE_SCHEMA = 'fsgg.learn-context-controller/1'
DEFAULT_SEED = 'learn-context-audit-v1'
SHA = re.compile(r'[0-9a-f]{40}')
REPO = re.compile(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+')
MAX_INPUT_BYTES = 64 * 1024 * 1024


class Unavailable(ValueError):
    """A retained audit case lacks qualifying context; never select a replacement."""


def require(value, reason):
    if not value:
        raise Unavailable(reason)


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True,
                       allow_nan=False) + '\n').encode()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def instant(value):
    require(isinstance(value, str), 'missing-time')
    try:
        result = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
    except ValueError as error:
        raise Unavailable('invalid-time') from error
    require(result.tzinfo is not None, 'timezone-required')
    return result


def utc_now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def read_json(path, limit=MAX_INPUT_BYTES):
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), 'input-not-regular-file')
    require(path.stat().st_size <= limit, 'input-byte-bound')
    return json.loads(path.read_bytes())


def private_parent(path):
    path = Path(path)
    require(path.is_absolute() and path.parent.is_dir(), 'absolute-existing-parent-required')
    require(path.parent.resolve() == path.parent, 'symlink-parent-refused')
    require(path.parent.stat().st_uid == os.getuid() and path.parent.stat().st_mode & 0o077 == 0,
            'private-owned-parent-required')
    return path


def write_new(path, data):
    path = Path(path)
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW
    fd = os.open(path, flags, 0o600)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())


def history_module():
    path = Path(__file__).with_name('learn-history-evaluate.py')
    spec = importlib.util.spec_from_file_location('learn_context_history', path)
    require(spec is not None and spec.loader is not None, 'history-adapter-unavailable')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def issue_ref(repository, number):
    require(isinstance(repository, str) and REPO.fullmatch(repository), 'invalid-repository')
    require(type(number) is int and number > 0, 'invalid-number')
    return repository + '#' + str(number)


def parent_ref(value):
    if value is None:
        return None
    require(isinstance(value, str), 'invalid-parent-reference')
    match = re.fullmatch(r'https://(?:api\.github\.com/repos|github\.com)/([^/]+/[^/]+)/issues/([1-9][0-9]*)', value)
    require(match is not None, 'unrecognized-parent-reference')
    return issue_ref(match[1], int(match[2]))


def choose_cases(rows, development, count=16, seed=DEFAULT_SEED):
    """Only identity, public scope, creation time and parent components affect selection."""
    require(type(count) is int and 1 <= count <= 16, 'audit-count-must-be-1-to-16')
    require(isinstance(seed, str) and 1 <= len(seed) <= 100, 'invalid-seed')
    require(isinstance(development, list) and development, 'development-cohort-required')
    issues = [row for row in rows if row['kind'] == 'issue']
    require(len({r['node_id'] for r in issues}) == len(issues), 'duplicate-issue-identity')
    graph = collections.defaultdict(set)
    by_node = {}
    for row in issues:
        ref = issue_ref(row['repository'], row['number'])
        by_node[row['node_id']] = ref
        parent = parent_ref(row.get('parent_issue_url'))
        if parent:
            graph[ref].add(parent)
            graph[parent].add(ref)
    seeds = []
    development_weeks = set()
    for row in development:
        require(isinstance(row, dict) and isinstance(row.get('nodeId'), str), 'invalid-development-identity')
        require(row['nodeId'] in by_node, 'development-identity-outside-inventory')
        require(row.get('historicalItem') == by_node[row['nodeId']], 'development-reference-mismatch')
        native = next(r for r in issues if r['node_id'] == row['nodeId'])
        require(row.get('createdAt') == native['created_at'], 'development-creation-time-mismatch')
        week = instant(row['createdAt']).astimezone(dt.timezone.utc).isocalendar()
        development_weeks.add((week.year, week.week))
        seeds.append(by_node[row['nodeId']])
        if row.get('parent'):
            seeds.append(parent_ref(row['parent']))
    excluded = set()
    while seeds:
        ref = seeds.pop()
        if ref not in excluded:
            excluded.add(ref)
            seeds.extend(graph[ref] - excluded)
    strata = collections.defaultdict(list)
    excluded_counts = collections.Counter()
    for row in issues:
        ref = by_node[row['node_id']]
        if ref in excluded:
            excluded_counts['development-component'] += 1
            continue
        if row['private'] is not False:
            excluded_counts['private-or-unknown'] += 1
            continue
        when = instant(row['created_at']).astimezone(dt.timezone.utc)
        week = when.isocalendar()
        if (week.year, week.week) in development_weeks:
            excluded_counts['development-utc-iso-week'] += 1
            continue
        strata[(row['repository'], when.strftime('%Y-%m'))].append(row)
    rank = lambda value: digest((seed + '/' + value).encode())
    for values in strata.values():
        values.sort(key=lambda row: (rank(row['node_id']), row['node_id']))
    keys = sorted(strata, key=lambda key: (rank('/'.join(key)), key))
    selected = []
    depth = 0
    while len(selected) < count:
        advanced = False
        for key in keys:
            if depth < len(strata[key]):
                selected.append(strata[key][depth])
                advanced = True
                if len(selected) == count:
                    break
        require(advanced, 'insufficient-fixed-audit-candidates')
        depth += 1
    cases = [{'caseId': 'context-' + str(i).zfill(3), 'nodeId': row['node_id'],
              'repository': row['repository'], 'number': row['number'],
              'boundaryKind': 'item-creation', 'cutoffAt': row['created_at']}
             for i, row in enumerate(selected, 1)]
    return cases, {'inventoryIssues': len(issues), 'candidateUpperBound': sum(map(len, strata.values())),
                   'excluded': dict(sorted(excluded_counts.items())),
                   'developmentTimeBlocks': [f'{year}-W{week:02d}' for year, week in sorted(development_weeks)],
                   'parentGraphSha256': digest(canonical({k: sorted(v) for k, v in sorted(graph.items())})),
                   'limits': ['Census-observed native parent graph may omit historical or later relationships.',
                              'No issue-history, code-context or work-clock eligibility is implied.',
                              'Unavailable fixed cases are retained without replacement.']}


def select(args):
    h = history_module()
    manifest, rows, _ = h.inventory(args.inventory)
    development = read_json(args.development_cohort)
    cases, coverage = choose_cases(rows, development, args.count, args.seed)
    result = {'schema': SELECTION_SCHEMA, 'createdAt': utc_now(), 'seed': args.seed,
              'inventorySha256': digest((args.inventory / 'manifest.json').read_bytes()),
              'developmentCohortSha256': digest(args.development_cohort.read_bytes()),
              'cases': cases, 'coverage': coverage, 'selectionFields': [
                  'native identity', 'repository', 'creation month', 'public scope', 'known parent component', 'UTC ISO week'],
              'outcomesUsedForSelection': False, 'replacementAllowed': False}
    result['sha256'] = digest(canonical(result))
    write_new(private_parent(args.output), canonical(result))
    print(json.dumps({'cases': len(cases), 'candidateUpperBound': coverage['candidateUpperBound'],
                      'selectionSha256': result['sha256'], 'nativeRequests': 0}))


def run_bounded(argv, *, seconds, max_bytes, env=None):
    """Bound both streams while acquiring them; no shell, retry or unbounded communicate()."""
    require(seconds > 0, 'wall-bound-exhausted')
    process = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
    selector = selectors.DefaultSelector()
    streams = {'stdout': bytearray(), 'stderr': bytearray()}
    for name, stream in [('stdout', process.stdout), ('stderr', process.stderr)]:
        selector.register(stream, selectors.EVENT_READ, name)
    deadline = time.monotonic() + seconds
    try:
        while selector.get_map():
            remaining = deadline - time.monotonic()
            require(remaining > 0, 'subprocess-wall-bound')
            for key, _ in selector.select(min(remaining, .2)):
                data = os.read(key.fd, 65536)
                if not data:
                    selector.unregister(key.fileobj)
                    continue
                target = streams[key.data]
                require(len(target) + len(data) <= (max_bytes if key.data == 'stdout' else 16384),
                        'subprocess-output-bound')
                target.extend(data)
        returncode = process.wait(timeout=max(.01, deadline - time.monotonic()))
        require(returncode == 0, 'subprocess-read-failed')
        return bytes(streams['stdout'])
    except BaseException:
        if process.poll() is None:
            process.kill()
        process.wait(timeout=5)
        raise
    finally:
        selector.close()
        process.stdout.close()
        process.stderr.close()


class Budget:
    def __init__(self, seconds=120, requests=8):
        require(type(seconds) is int and 1 <= seconds <= 900, 'invalid-wall-bound')
        require(type(requests) is int and 1 <= requests <= 32, 'invalid-request-bound')
        self.deadline = time.monotonic() + seconds
        self.cleanup_reserve = min(5.0, seconds / 10)
        self.maximum = requests
        self.charged = 0

    def remaining(self):
        return max(0, self.deadline - time.monotonic() - self.cleanup_reserve)

    def charge(self):
        require(self.charged < self.maximum and self.remaining() > 0, 'native-request-bound')
        self.charged += 1


def native_read(query, budget, controller):
    budget.charge()
    request = {'at': utc_now(), 'sequence': budget.charged, 'querySha256': digest(query.encode()),
               'automaticRetry': False}
    prefix = controller / ('native-' + str(budget.charged).zfill(3))
    write_new(prefix.with_suffix('.request.json'), canonical(request))
    payload = run_bounded(['gh', 'api', 'graphql', '-f', 'query=' + query],
                          seconds=budget.remaining(), max_bytes=8 * 1024 * 1024,
                          env=dict(os.environ, GOMAXPROCS='1', GOMEMLIMIT='128MiB'))
    write_new(prefix.with_suffix('.response.json'), payload)
    result = json.loads(payload)
    require(not result.get('errors') and isinstance(result.get('data'), dict), 'native-read-incomplete')
    return result['data']


def title_at_boundary(record, cutoff, h):
    title = record['title']
    require(isinstance(title, str), 'missing-title')
    events = h.connection(record['timelineItems'], filtered=True)
    renames = [e for e in events if e.get('__typename') == 'RenamedTitleEvent']
    require(len({e['id'] for e in renames}) == len(renames), 'duplicate-rename-event')
    times = [e['createdAt'] for e in renames]
    require(len(set(times)) == len(times), 'ambiguous-same-time-rename')
    for event in sorted(renames, key=lambda e: instant(e['createdAt']), reverse=True):
        require(instant(event['createdAt']) >= instant(record['createdAt']), 'rename-before-creation')
        if instant(event['createdAt']) >= instant(cutoff):
            require(title == event['currentTitle'], 'conflicting-rename-chain')
            title = event['previousTitle']
    return title


def task_at_boundary(record, case, h):
    require(record['id'] == case['nodeId'] and record['number'] == case['number'], 'issue-identity-drift')
    require(record.get('repository', {}).get('nameWithOwner') == case['repository'], 'issue-repository-drift')
    require(record['createdAt'] == case['cutoffAt'] and case['boundaryKind'] == 'item-creation',
            'v1-only-supports-exact-item-creation')
    body, provenance = h.body_at_boundary(record, case['cutoffAt'])
    title = title_at_boundary(record, case['cutoffAt'], h)
    comments = h.connection(record['comments'])
    kept, gaps, future = [], [], 0
    for comment in comments:
        created, updated, cutoff = instant(comment['createdAt']), instant(comment['updatedAt']), instant(case['cutoffAt'])
        require(updated >= created, 'comment-time-conflict')
        require(created >= instant(record['createdAt']), 'comment-before-issue-creation')
        if created > cutoff:
            future += 1
        elif created == cutoff or updated == cutoff:
            gaps.append({'kind': 'ambiguous-cutoff-comment-timestamp'})
        elif updated > cutoff:
            gaps.append({'kind': 'earlier-comment-revision-unavailable'})
        else:
            require(isinstance(comment.get('body'), str), 'comment-body-unavailable')
            kept.append(comment['body'])
    return {'title': title, 'body': body, 'comments': kept}, {
        'bodyProvenance': provenance, 'rawBodySha256': digest(body.encode()),
        'rawTitleSha256': digest(title.encode()), 'futureCommentsOmitted': future,
        'contextGaps': gaps}


def checkpoint_candidates(rows, case, maximum=3):
    cutoff = instant(case['cutoffAt'])
    result = []
    for row in rows:
        if row['kind'] != 'pull_request' or row['repository'] != case['repository']:
            continue
        merged = (row.get('pullRequest') or {}).get('merged_at')
        if merged is not None and instant(merged) < cutoff:
            result.append(row)
    result.sort(key=lambda r: (instant(r['pullRequest']['merged_at']), r['node_id']), reverse=True)
    return result[:maximum]


def verify_checkpoint(witness, case, candidate, branch):
    require(witness is not None and witness.get('id') == candidate['node_id'], 'checkpoint-identity')
    require(witness.get('repository', {}).get('nameWithOwner') == case['repository'], 'checkpoint-repository')
    merged = witness.get('mergedAt')
    require(merged is not None and merged == candidate['pullRequest']['merged_at'], 'server-merge-witness-drift')
    require(instant(merged) < instant(case['cutoffAt']), 'checkpoint-not-strictly-before-cutoff')
    require(witness.get('baseRefName') == branch, 'checkpoint-other-branch')
    commit = witness.get('mergeCommit') or {}
    require(SHA.fullmatch(commit.get('oid', '')) and SHA.fullmatch((commit.get('tree') or {}).get('oid', '')),
            'checkpoint-object-identity-unavailable')
    return {'commit': commit['oid'], 'tree': commit['tree']['oid'], 'mergedAt': merged,
            'branch': branch, 'lagSeconds': (instant(case['cutoffAt']) - instant(merged)).total_seconds(),
            'interpretation': 'proven-before-cutoff checkpoint; exact historical branch head is not established'}


def git_read(repository, arguments, budget, maximum):
    env = dict(os.environ, GIT_NO_REPLACE_OBJECTS='1', GIT_NO_LAZY_FETCH='1', GIT_CONFIG_NOSYSTEM='1',
               GIT_CONFIG_GLOBAL='/dev/null', GIT_TERMINAL_PROMPT='0', GIT_PAGER='cat')
    return run_bounded(['git', '--no-optional-locks', '-c', 'core.hooksPath=/dev/null',
                        '-c', 'core.fsmonitor=false', '-C', str(repository), *arguments],
                       seconds=budget.remaining(), max_bytes=maximum, env=env)


def object_digest(kind, payload):
    return hashlib.sha1(kind.encode() + b' ' + str(len(payload)).encode() + b'\0' + payload).hexdigest()


def safe_path(path):
    require(isinstance(path, str) and path and len(path.encode('utf-8')) <= 4096 and '\\' not in path and not any(ord(c) < 32 for c in path),
            'unsafe-tree-path')
    parsed = PurePosixPath(path)
    require(not parsed.is_absolute() and str(parsed) == path and
            all(part not in {'.', '..'} and part.casefold() != '.git' for part in parsed.parts),
            'unsafe-tree-path')
    return parsed


def tree_entries(payload, max_files, max_blob_bytes, max_total_bytes, tree_oids=None):
    result, gaps, seen, total = [], [], set(), 0
    require(not payload or payload.endswith(b'\0'), 'incomplete-tree-output')
    for raw in payload.split(b'\0')[:-1]:
        try:
            header, name = raw.split(b'\t', 1)
            mode, kind, oid, size = header.decode('ascii').split()
            path = name.decode('utf-8')
        except (ValueError, UnicodeError) as error:
            raise Unavailable('unsupported-tree-record') from error
        safe_path(path)
        require(path.casefold() not in seen, 'case-colliding-tree-path')
        seen.add(path.casefold())
        require(len(seen) <= max_files, 'file-count-bound')
        require(SHA.fullmatch(oid), 'invalid-tree-object')
        if mode == '040000' and kind == 'tree':
            require(size == '-' and tree_oids is not None, 'unsupported-tree-directory')
            tree_oids.append(oid)
            continue
        if mode == '160000' and kind == 'commit':
            gaps.append({'kind': 'unresolved-gitlink', 'path': path, 'object': oid})
            continue
        require(kind == 'blob' and mode in {'100644', '100755'}, 'unsupported-link-or-tree-mode')
        require(size.isdigit(), 'blob-size-unavailable')
        count = int(size)
        require(count <= max_blob_bytes, 'per-blob-byte-bound')
        total += count
        require(total <= max_total_bytes, 'total-blob-byte-bound')
        result.append({'path': path, 'mode': mode, 'blob': oid, 'bytes': count})
    require(result, 'empty-exported-context')
    return sorted(result, key=lambda e: e['path']), gaps


def export_tree(repository, checkpoint, destination, budget, *, max_files=20000,
                max_blob_bytes=4 * 1024 * 1024, max_total_bytes=64 * 1024 * 1024):
    require(repository.is_dir() and not repository.is_symlink(), 'local-repository-unavailable')
    commit = git_read(repository, ['cat-file', 'commit', checkpoint['commit']], budget, 4 * 1024 * 1024)
    require(object_digest('commit', commit) == checkpoint['commit'], 'local-commit-hash-mismatch')
    require(commit.startswith(('tree ' + checkpoint['tree'] + '\n').encode()), 'native-local-tree-mismatch')
    tree = git_read(repository, ['ls-tree', '-rltz', '--full-tree', checkpoint['commit']], budget, 8 * 1024 * 1024)
    tree_oids = [checkpoint['tree']]
    entries, gaps = tree_entries(tree, max_files, max_blob_bytes, max_total_bytes, tree_oids)
    for oid in sorted(set(tree_oids)):
        raw_tree = git_read(repository, ['cat-file', 'tree', oid], budget, 8 * 1024 * 1024)
        require(object_digest('tree', raw_tree) == oid, 'tree-object-hash-mismatch')
    destination.mkdir(mode=0o700, exist_ok=False)
    for entry in entries:
        data = git_read(repository, ['cat-file', 'blob', entry['blob']], budget, max_blob_bytes)
        require(len(data) == entry['bytes'] and object_digest('blob', data) == entry['blob'], 'blob-hash-mismatch')
        relative = safe_path(entry['path'])
        target = destination.joinpath(*relative.parts)
        target.parent.mkdir(parents=True, mode=0o700, exist_ok=True)
        require(target.parent.resolve().is_relative_to(destination.resolve()), 'export-parent-escape')
        write_new(target, data)
        entry['sha256'] = digest(data)
        if data.startswith(b'version https://git-lfs.github.com/spec/v1\n'):
            gaps.append({'kind': 'unmaterialized-lfs-object', 'path': entry['path']})
        try:
            data.decode('utf-8')
            entry['contentKind'] = 'binary' if b'\0' in data else 'utf8'
        except UnicodeError:
            entry['contentKind'] = 'binary'
    # Locked files are context bytes, not proof of a reproducible or pre-cutoff dependency environment.
    gaps.append({'kind': 'dependency-environment-not-reconstructed'})
    return entries, gaps


def safe_task_text(task):
    text = '# ' + task['title'] + '\n\n' + task['body']
    if task['comments']:
        text += '\n\n## Available earlier discussion\n\n' + '\n\n'.join(task['comments'])
    text = re.sub(r'https://(?:api\.)?github\.com/\S+', '[historical-reference]', text)
    text = re.sub(r'(?<![A-Za-z0-9_])[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[1-9][0-9]*', '[historical-item]', text)
    return text.encode('utf-8')


def collect_case(args):
    budget = Budget(args.wall_seconds, args.max_requests)
    selection = read_json(args.selection)
    require(selection.get('schema') == SELECTION_SCHEMA, 'unsupported-selection-schema')
    expected = dict(selection)
    claimed = expected.pop('sha256', None)
    require(claimed == digest(canonical(expected)), 'selection-digest-mismatch')
    h = history_module()
    _, rows, _ = h.inventory(args.inventory)
    require(selection['inventorySha256'] == digest((args.inventory / 'manifest.json').read_bytes()), 'inventory-drift')
    require(selection['developmentCohortSha256'] == digest(args.development_cohort.read_bytes()), 'development-cohort-drift')
    cases, coverage = choose_cases(rows, read_json(args.development_cohort), len(selection['cases']), selection['seed'])
    require(cases == selection['cases'] and coverage == selection['coverage'], 'selection-reconstruction-mismatch')
    matches = [case for case in cases if case['caseId'] == args.case_id]
    require(len(matches) == 1, 'case-not-in-frozen-selection')
    case = matches[0]
    output = private_parent(args.output)
    output.mkdir(mode=0o700, exist_ok=False)
    controller = output / 'controller'
    controller.mkdir(mode=0o700)
    outcome = {'schema': SOURCE_SCHEMA, 'caseId': case['caseId'], 'startedAt': utc_now(),
               'selectionSha256': claimed, 'case': case, 'state': 'unavailable',
               'sourceHashes': {name: digest(Path(__file__).with_name(name).read_bytes()) for name in
                                ['learn-context-snapshot.py', 'learn-history-evaluate.py', 'learn-history-fetch.py']},
               'ratingLaunchAllowed': False, 'nativeMutations': 0, 'automaticRetry': False}
    try:
        fetch_spec = importlib.util.spec_from_file_location('learn_context_fetch', Path(__file__).with_name('learn-history-fetch.py'))
        fetch = importlib.util.module_from_spec(fetch_spec)
        fetch_spec.loader.exec_module(fetch)
        query = 'query { node(id:' + json.dumps(case['nodeId']) + ') { ... on Issue { ' + fetch.FIELDS + ' repository { nameWithOwner } } } }'
        record = native_read(query, budget, controller).get('node')
        require(isinstance(record, dict), 'native-issue-unavailable')
        task, task_provenance = task_at_boundary(record, case, h)
        tied_merges = sum(1 for row in rows if row['kind'] == 'pull_request' and
                          row['repository'] == case['repository'] and
                          (row.get('pullRequest') or {}).get('merged_at') is not None and
                          instant(row['pullRequest']['merged_at']) == instant(case['cutoffAt']))
        outcome['sameTimestampMergeCandidatesOmitted'] = tied_merges
        candidates = checkpoint_candidates(rows, case)
        require(candidates, 'no-pre-cutoff-checkpoint-candidate')
        checkpoint = None
        for candidate in candidates:
            query = 'query { node(id:' + json.dumps(candidate['node_id']) + ') { ... on PullRequest { id mergedAt baseRefName mergeCommit { oid tree { oid } } repository { nameWithOwner } } } }'
            witness = native_read(query, budget, controller).get('node')
            # A different branch is a supported candidate rejection, not an automatic failed-read retry.
            if isinstance(witness, dict) and witness.get('baseRefName') != args.branch:
                continue
            checkpoint = verify_checkpoint(witness, case, candidate, args.branch)
            break
        require(checkpoint is not None, 'no-selected-branch-checkpoint-in-bounded-candidates')
        outcome['checkpoint'] = checkpoint
        outcome['taskProvenance'] = task_provenance
        rater = output / 'rater'
        rater.mkdir(mode=0o700)
        entries, gaps = export_tree(args.repository, checkpoint, rater / 'source', budget,
                                   max_files=args.max_files, max_blob_bytes=args.max_blob_bytes,
                                   max_total_bytes=args.max_total_bytes)
        if tied_merges:
            gaps.append({'kind': 'ambiguous-cutoff-merge-omitted'})
        task_bytes = safe_task_text(task)
        write_new(rater / 'task.md', task_bytes)
        safe = {'schema': SCHEMA, 'caseId': case['caseId'], 'boundaryKind': case['boundaryKind'],
                'checkpointKind': 'proven-before-cutoff', 'checkpointLagSeconds': checkpoint['lagSeconds'],
                'exactHistoricalHead': False,
                'taskSha256': digest(task_bytes), 'taskRedactionVersion': 'github-identity-links/1', 'files': entries,
                'contextGaps': task_provenance['contextGaps'] + gaps + [
                    {'kind': 'creator-private-context-unavailable'},
                    {'kind': 'related-discussion-and-external-research-not-reconstructed'},
                    {'kind': 'creation-before-first-work-not-established'},
                    {'kind': 'exact-branch-state-not-established'}],
                'identityBlinding': 'partial-source-identifiability', 'ratingLaunchAllowed': False,
                'isolation': 'Not enforced by this collector; controller directory and host/network must be excluded by a separately qualified runner.'}
        require(budget.remaining() > 0, 'wall-bound-exhausted-before-manifest')
        write_new(rater / 'manifest.json', canonical(safe))
        outcome.update(state='context-built-with-declared-gaps', safeManifestSha256=digest(canonical(safe)),
                       contextFiles=len(entries), contextBytes=sum(e['bytes'] for e in entries))
    except (Unavailable, ValueError, OSError, KeyError, TypeError, IndexError, AttributeError, subprocess.SubprocessError) as error:
        outcome['reason'] = str(error) if isinstance(error, Unavailable) else type(error).__name__
    finally:
        outcome.update(endedAt=utc_now(), nativeRequestsCharged=budget.charged)
        write_new(controller / 'result.json', canonical(outcome))
    print(json.dumps({'caseId': case['caseId'], 'state': outcome['state'],
                      'nativeRequestsCharged': budget.charged, 'ratingLaunchAllowed': False}))
    return 0 if outcome['state'] == 'context-built-with-declared-gaps' else 3


def parser():
    root = argparse.ArgumentParser(description=__doc__)
    commands = root.add_subparsers(dest='command', required=True)
    choose = commands.add_parser('select', help='metadata-only fixed audit selection; no native requests')
    choose.add_argument('--inventory', type=Path, required=True)
    choose.add_argument('--development-cohort', type=Path, required=True)
    choose.add_argument('--output', type=Path, required=True)
    choose.add_argument('--count', type=int, default=16)
    choose.add_argument('--seed', default=DEFAULT_SEED)
    collect = commands.add_parser('collect-case', help='bounded reads and private context export; never launches agents')
    collect.add_argument('--inventory', type=Path, required=True)
    collect.add_argument('--development-cohort', type=Path, required=True)
    collect.add_argument('--selection', type=Path, required=True)
    collect.add_argument('--case-id', required=True)
    collect.add_argument('--repository', type=Path, required=True)
    collect.add_argument('--branch', required=True, help='explicit branch witness; no default-branch inference')
    collect.add_argument('--output', type=Path, required=True)
    collect.add_argument('--wall-seconds', type=int, default=120)
    collect.add_argument('--max-requests', type=int, default=8)
    collect.add_argument('--max-files', type=int, default=20000)
    collect.add_argument('--max-blob-bytes', type=int, default=4 * 1024 * 1024)
    collect.add_argument('--max-total-bytes', type=int, default=64 * 1024 * 1024)
    return root


def main():
    os.umask(0o077)
    args = parser().parse_args()
    try:
        if args.command == 'select':
            select(args)
            return 0
        require(isinstance(args.branch, str) and 0 < len(args.branch) <= 200, 'invalid-branch')
        require(1 <= args.max_files <= 20000 and 1 <= args.max_blob_bytes <= 4 * 1024 * 1024 and
                args.max_blob_bytes <= args.max_total_bytes <= 64 * 1024 * 1024, 'invalid-export-bounds')
        return collect_case(args)
    except (Unavailable, ValueError, OSError) as error:
        print(json.dumps({'state': 'refused', 'reason': str(error) if isinstance(error, Unavailable) else type(error).__name__,
                          'ratingLaunchAllowed': False}), file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
