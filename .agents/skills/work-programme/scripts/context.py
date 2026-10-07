#!/usr/bin/env python3
"""Bounded, dependency-free context projections. No dispatch or workload execution."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import time
import uuid

LIMIT = 2 * 1024 * 1024


class Refusal(ValueError):
    pass


def require(ok, reason):
    if not ok:
        raise Refusal(reason)


def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate-json-key')
        result[key] = value
    return result


def decode(body):
    return json.loads(body, object_pairs_hook=unique,
                      parse_constant=lambda _: (_ for _ in ()).throw(Refusal('nonfinite-json')))


def encode(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()


def shape(value, keys):
    require(isinstance(value, dict) and set(value) == set(keys.split()), 'invalid-fields')


def no_links(path):
    path = Path(path)
    require(path.is_absolute(), 'absolute-path-required')
    for part in [path, *path.parents]:
        require(not part.is_symlink(), 'linked-path')
    return path


def read(path, limit=LIMIT):
    path = no_links(path)
    with path.open('rb') as stream:
        before = os.fstat(stream.fileno())
        require(stat.S_ISREG(before.st_mode) and before.st_size <= limit, 'file-bound')
        body = stream.read(limit + 1)
        after = os.fstat(stream.fileno())
    def stable(value):
        return (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
    require(len(body) <= limit and stable(before) == stable(after) == stable(path.stat()), 'file-changed-or-grown')
    return body


def pin_body(pin, limit=LIMIT):
    shape(pin, 'path bytes sha256')
    require(type(pin['bytes']) is int and 0 <= pin['bytes'] <= limit and
            isinstance(pin['sha256'], str) and re.fullmatch('[0-9a-f]{64}', pin['sha256']), 'invalid-pin')
    body = read(pin['path'], limit)
    require(len(body) == pin['bytes'] and hashlib.sha256(body).hexdigest() == pin['sha256'], 'pin-drift')
    return body


def bound(value):
    require(type(value) is int and 1 <= value <= 65536, 'invalid-exposure-bound')
    return value


def pointer(value, path):
    require(isinstance(path, str) and path.startswith('/') and len(path) <= 1024, 'invalid-pointer')
    for key in path[1:].split('/'):
        require(not re.search(r'~(?![01])', key), 'invalid-pointer-escape')
        key = key.replace('~1', '/').replace('~0', '~')
        if isinstance(value, list):
            require(re.fullmatch('0|[1-9][0-9]*', key) is not None, 'invalid-array-index')
            value = value[int(key)]
        else:
            require(isinstance(value, dict), 'pointer-not-found')
            value = value[key]
    return value


def view(spec):
    shape(spec, 'schema artifact trust access obligationsComplete reason selection maximumBytes')
    require(spec['schema'] == 'fsgg.programme.context-view-input/1', 'invalid-view-schema')
    require(spec['access'] == 'allowed', 'access-unestablished')
    require(spec['trust'] in ('instruction', 'plan', 'data'), 'invalid-trust')
    require(type(spec['obligationsComplete']) is bool and isinstance(spec['reason'], str) and
            0 < len(spec['reason']) <= 1024, 'missing-obligation-or-reason')
    maximum = bound(spec['maximumBytes'])
    selection = spec['selection']
    require(isinstance(selection, dict) and len(selection) == 1, 'invalid-selection')
    if spec['trust'] == 'instruction':
        require(selection == {'whole': True} and spec['obligationsComplete'], 'instruction-must-be-complete-whole-file')
    body = pin_body(spec['artifact'])
    if set(selection) == {'whole'}:
        require(selection['whole'] is True, 'invalid-whole-selection')
        selected = body.decode('utf-8')
    elif set(selection) == {'lines'}:
        span = selection['lines']
        require(isinstance(span, list) and len(span) == 2 and all(type(v) is int for v in span), 'invalid-lines')
        lines = body.decode('utf-8').split('\n')
        require(1 <= span[0] <= span[1] <= len(lines), 'invalid-lines')
        selected = '\n'.join(lines[span[0]-1:span[1]])
    elif set(selection) == {'pointers'}:
        paths = selection['pointers']
        require(isinstance(paths, list) and 1 <= len(paths) <= 32 and
                all(isinstance(v, str) for v in paths) and len(paths) == len(set(paths)), 'invalid-pointers')
        value = decode(body)
        selected = {path: pointer(value, path) for path in paths}
    else:
        raise Refusal('unsupported-selection')
    require(len(encode(selected)) <= maximum, 'selection-oversize')
    return dict(schema='fsgg.programme.context-view/1', artifact=spec['artifact'], selection=selection,
                trust=spec['trust'], reason=spec['reason'], selected=selected, identityVerified=True,
                authority='none; equality and declared selection only')


def review(spec):
    shape(spec, 'schema sourceRevision obligations views unchanged unknowns maximumBytes')
    require(spec['schema'] == 'fsgg.programme.context-review-input/1' and
            re.fullmatch('[0-9a-f]{40}', spec['sourceRevision']), 'invalid-review-identity')
    require(isinstance(spec['views'], list) and 1 <= len(spec['views']) <= 16, 'review-view-bound')
    selected = {}
    for row in spec['views']:
        shape(row, 'id input')
        require(isinstance(row['id'], str) and 0 < len(row['id']) <= 128 and row['id'] not in selected, 'duplicate-review-view')
        selected[row['id']] = view(row['input'])
    require(isinstance(spec['obligations'], list) and 1 <= len(spec['obligations']) <= 32, 'review-obligation-bound')
    ids = set()
    for row in spec['obligations']:
        shape(row, 'id question viewIds')
        require(isinstance(row['id'], str) and row['id'] not in ids and 0 < len(row['id']) <= 128 and
                isinstance(row['question'], str) and 0 < len(row['question']) <= 1024 and
                isinstance(row['viewIds'], list) and 1 <= len(row['viewIds']) <= 16 and
                all(isinstance(v, str) and v in selected for v in row['viewIds']), 'uncovered-review-obligation')
        ids.add(row['id'])
    require({v for row in spec['obligations'] for v in row['viewIds']} == set(selected), 'unexplained-review-view')
    require(isinstance(spec['unchanged'], list) and len(spec['unchanged']) <= 1024, 'unchanged-closure-bound')
    require(len({p['path'] for p in spec['unchanged']}) == len(spec['unchanged']), 'duplicate-unchanged-pin')
    for pin in spec['unchanged']:
        pin_body(pin, 64 * 1024 * 1024)
    require(isinstance(spec['unknowns'], list) and len(spec['unknowns']) <= 32 and
            all(isinstance(v, str) and 0 < len(v) <= 1024 for v in spec['unknowns']), 'unknowns-bound')
    result = dict(schema='fsgg.programme.context-review/1', sourceRevision=spec['sourceRevision'],
                  obligations=spec['obligations'], views=selected, unknowns=spec['unknowns'],
                  unchangedCount=len(spec['unchanged']), unchangedClosureSha256=hashlib.sha256(encode(spec['unchanged'])).hexdigest(),
                  completeness='caller-declared; expand missing proof obligations',
                  authority='none; root semantic review and native admission remain required')
    require(len(encode(result)) <= bound(spec['maximumBytes']), 'review-oversize')
    return result


def instant(value):
    require(isinstance(value, str), 'invalid-time')
    parsed = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
    require(parsed.utcoffset() == dt.timedelta(0), 'utc-required')
    return parsed


def usage(spec):
    shape(spec, 'schema start end sessions expectedSessionIds helperDirectories')
    require(spec['schema'] == 'fsgg.programme.context-report-input/1', 'invalid-report-schema')
    start, end = instant(spec['start']), instant(spec['end'])
    require(start < end, 'invalid-report-window')
    require(isinstance(spec['sessions'], list) and 1 <= len(spec['sessions']) <= 32 and
            isinstance(spec['expectedSessionIds'], list) and 1 <= len(spec['expectedSessionIds']) <= 32 and
            all(isinstance(v, str) and v for v in spec['expectedSessionIds']) and
            len(set(spec['expectedSessionIds'])) == len(spec['expectedSessionIds']), 'session-bound')
    sessions = []; found = set()
    for row in spec['sessions']:
        shape(row, 'sessionId artifact')
        require(row['sessionId'] in spec['expectedSessionIds'] and row['sessionId'] not in found, 'unexpected-or-duplicate-session')
        body = pin_body(row['artifact'], 64 * 1024 * 1024)
        records = []; incomplete = 0
        lines = body.splitlines(keepends=True)
        for index, line in enumerate(lines):
            try:
                record = decode(line)
                require(isinstance(record, dict), 'invalid-session-record')
                records.append(record)
            except json.JSONDecodeError:
                # Complete malformed records (including duplicate keys) never become tail gaps.
                require(index == len(lines)-1 and not line.endswith(b'\n') and
                        not line.rstrip().endswith((b'}', b']')), 'malformed-session-record')
                incomplete += 1
        metas = [d['payload'] for d in records if d.get('type') == 'session_meta']
        require(len(metas) == 1 and metas[0].get('id') == row['sessionId'], 'session-identity-mismatch')
        found.add(row['sessionId']); inputs = []; last_totals = None; counters = []; output_bytes = 0; unmeasured_blocks = 0; tool_calls = 0; compactions = []
        for d in records:
            stamp = instant(d['timestamp'])
            p = d.get('payload', {})
            if d.get('type') == 'event_msg' and p.get('type') == 'token_count' and p.get('info'):
                totals = p['info'].get('total_token_usage')
                if stamp < start:
                    last_totals = totals
                elif stamp <= end:
                    if totals is not None:
                        counters.append(totals)
                    value = p['info'].get('last_token_usage', {}).get('input_tokens')
                    if value is not None:
                        require(type(value) is int and value >= 0, 'invalid-native-input')
                        if value > 0:
                            inputs.append(value)
            if not start <= stamp <= end:
                continue
            if d.get('type') == 'compacted':
                compactions.append(d['timestamp'])
            if d.get('type') == 'response_item':
                if p.get('type') in ('function_call', 'custom_tool_call'):
                    tool_calls += 1
                if p.get('type') in ('function_call_output', 'custom_tool_call_output'):
                    output = p.get('output', '')
                    if isinstance(output, str):
                        output_bytes += len(output.encode())
                    elif isinstance(output, list):
                        for block in output:
                            if isinstance(block, dict) and isinstance(block.get('text'), str):
                                output_bytes += len(block['text'].encode())
                            else:
                                unmeasured_blocks += 1
                    else:
                        unmeasured_blocks += 1
        fields = ('input_tokens', 'cached_input_tokens', 'output_tokens', 'reasoning_output_tokens')
        known = bool(counters) and last_totals is not None
        for counter in ([last_totals] if last_totals is not None else []) + counters:
            require(isinstance(counter, dict) and all(type(counter.get(k)) is int and counter[k] >= 0 for k in fields), 'invalid-native-counter')
        if known:
            previous = last_totals
            for counter in counters:
                if any(counter[k] < previous[k] for k in fields):
                    known = False
                previous = counter
        delta = {k: counters[-1][k]-last_totals[k] for k in fields} if known else None
        sessions.append(dict(sessionId=row['sessionId'], agent=metas[0].get('agent_path'),
                             parentSessionId=metas[0].get('parent_thread_id'), compactions=compactions,
                             inputTokensMin=min(inputs) if inputs else None, inputTokensMax=max(inputs) if inputs else None,
                             tokenCounterDelta=delta, tokenDeltaCoverage='observed-baseline' if known else 'missing-baseline-or-counter-reset',
                             toolOutputTextBytes=output_bytes, unmeasuredOutputBlocks=unmeasured_blocks,
                             toolCalls=tool_calls, incompleteTailRecords=incomplete,
                             artifact=row['artifact']))
    directories = spec['helperDirectories']
    require(isinstance(directories, list) and len(directories) <= 64 and
            all(isinstance(v, str) for v in directories) and len(set(directories)) == len(directories), 'helper-directory-bound')
    helper = dict(operations=0, inputBytes=0, outputBytes=0, artifactBytes=0)
    seen = set()
    for directory in directories:
        root = no_links(directory)
        for path in sorted(root.glob('*.measure.json')):
            require(str(path) not in seen and len(seen) < 10000, 'helper-population-bound')
            seen.add(str(path))
            match = re.match(r'(\d{8})T(\d{6})(\d{0,7})-', path.name)
            require(match is not None, 'helper-time-unestablished')
            stamp = dt.datetime.strptime(match[1]+match[2], '%Y%m%d%H%M%S').replace(
                tzinfo=dt.timezone.utc, microsecond=int((match[3]+'000000')[:6]))
            if not start <= stamp <= end:
                continue
            measure = decode(read(path))
            require(measure.get('schema') == 'fsgg.programme.measurement/1', 'invalid-helper-measurement')
            helper['operations'] += 1
            for key in ('inputBytes', 'outputBytes', 'artifactBytes'):
                require(type(measure.get(key)) is int and measure[key] >= 0, 'invalid-helper-counter')
                helper[key] += measure[key]
    return dict(schema='fsgg.programme.context-report/1', start=spec['start'], end=spec['end'], sessions=sessions,
                missingSessionIds=sorted(set(spec['expectedSessionIds'])-found), helper=helper,
                coverage='explicit pinned sessions and named helper directories only; no inferred zero for missing participants',
                attribution='session diagnostics; original-item, dispatch-token and whole-family economic joins unestablished',
                authority='none; no usage reconciliation or acceptance minted')


def command(spec):
    shape(spec, 'schema python launcher operations window runtime binding bindingSha256 index receipt')
    require(spec['schema'] == 'fsgg.programme.guard-command-input/1', 'invalid-command-schema')
    for name in ('python', 'launcher', 'operations', 'window', 'runtime', 'binding'):
        pin_body(spec[name], 64 * 1024 * 1024)
    require(spec['bindingSha256'] == spec['binding']['sha256'], 'binding-digest-mismatch')
    operations = decode(pin_body(spec['operations']))
    binding = decode(pin_body(spec['binding']))
    window = decode(pin_body(spec['window']))
    now = time.monotonic()
    require(window['issuedMonotonic'] <= now < window['ownerWorkEndMonotonic'] <
            window['ownerCleanupEndMonotonic'], 'expired-or-invalid-window')
    require(type(spec['index']) is int and 0 <= spec['index'] < len(operations['operationPins']), 'invalid-operation-index')
    require(binding['operationIndex'] == spec['index'] and binding['operationsPin'] == spec['operations'] and
            binding['recipePin'] == {k: operations['operationPins'][spec['index']][k] for k in ('path', 'bytes', 'sha256')}, 'command-binding-join')
    require(window['attempt'] == binding['expectedAttempt'] and window['cpu'] == binding['cpu'], 'command-window-join')
    receipt = no_links(spec['receipt'])
    require(not receipt.exists(), 'receipt-already-exists')
    argv = [spec['python']['path'], spec['launcher']['path'], '--operations', spec['operations']['path'],
            '--deadlines', spec['window']['path'], '--index', str(spec['index']), '--receipt', str(receipt),
            '--runtime-pins', spec['runtime']['path'], '--entry-binding', spec['binding']['path'],
            '--entry-binding-sha256', spec['bindingSha256']]
    return dict(schema='fsgg.programme.guard-command/1', argv=argv, authority='none; existing guard and fresh root admission required',
                windowRenewed=False, executed=False, completeness='launcher support, runtime and source closure remain root prerequisites')


def retain(root, value):
    root = no_links(root)
    root.mkdir(mode=0o700, parents=True, exist_ok=True)
    require(stat.S_IMODE(root.stat().st_mode) & 0o077 == 0, 'private-root-permissions')
    name = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S%f') + '-' + uuid.uuid4().hex
    path = root / (name + '.context.json')
    body = encode(value)
    require(len(body) <= LIMIT, 'result-bound')
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(body)
    return dict(path=str(path), bytes=len(body), sha256=hashlib.sha256(body).hexdigest())


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('view', 'review', 'report', 'command'))
    parser.add_argument('input')
    parser.add_argument('private_root')
    parser.add_argument('--input-sha256', required=True)
    parser.add_argument('--stdout-bytes', type=int, default=8192)
    args = parser.parse_args(argv)
    started = time.monotonic()
    try:
        require(1024 <= args.stdout_bytes <= 65536, 'invalid-stdout-bound')
        body = read(args.input, 262144)
        require(hashlib.sha256(body).hexdigest() == args.input_sha256, 'input-pin-drift')
        value = dict(view=view, review=review, report=usage, command=command)[args.command](decode(body))
        value['elapsedMs'] = int((time.monotonic()-started)*1000)
        artifact = retain(args.private_root, value)
        envelope = dict(status='passed', artifact=artifact, result=value)
        if len(encode(envelope)) > args.stdout_bytes:
            envelope = dict(status='retained', artifact=artifact, reason='stdout-bound; retrieve selected obligations through view')
        require(len(encode(envelope)) <= args.stdout_bytes, 'stdout-envelope-oversize')
        print(encode(envelope).decode())
        return 0
    except (ValueError, OSError, KeyError, IndexError, TypeError, RecursionError, OverflowError) as error:
        # Do not echo source content, native records, credentials or arbitrary exception text.
        reason = str(error) if isinstance(error, Refusal) else type(error).__name__
        print(encode(dict(status='refused', reason=reason, authority='none')).decode())
        return 3


if __name__ == '__main__':
    sys.exit(main())
