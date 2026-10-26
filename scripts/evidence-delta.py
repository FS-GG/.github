#!/usr/bin/env python3
"""Read-only byte evidence delta. No Git, execution, settlement or acceptance authority."""
from __future__ import annotations
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import sys

SCHEMA = 'fsgg.evidence-delta/2'
RECEIPT = 'fsgg.evidence-byte-receipt/2'
JSON_LIMIT = 262144
FILE_LIMIT = 8388608
TOTAL_LIMIT = 67108864
SHA = re.compile(r'[0-9a-f]{64}\Z')
GIT = re.compile(r'[0-9a-f]{40}\Z')

class EvidenceError(ValueError):
    pass

def require(ok, message):
    if not ok:
        raise EvidenceError(message)

def canonical(value):
    """JSON identity preserves bool/int/float distinctions and rejects nonfinite numbers."""
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False)

def same(left, right):
    return canonical(left) == canonical(right)

def pairs(rows):
    out = {}
    for key, value in rows:
        require(key not in out, 'duplicate JSON key')
        out[key] = value
    return out

def read_regular(path, limit):
    """Reject symlinks in every component, special files and unstable/oversize reads."""
    path = Path(path).absolute()
    fd = os.open('/', os.O_RDONLY | os.O_DIRECTORY)
    try:
        for part in path.parts[1:-1]:
            next_fd = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd)
            fd = next_fd
        stream = os.open(path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=fd)
        try:
            before = os.fstat(stream)
            require(stat.S_ISREG(before.st_mode) and before.st_size <= limit, 'non-regular or oversized input')
            data = bytearray()
            while True:
                block = os.read(stream, min(65536, limit + 1 - len(data)))
                if not block:
                    break
                data.extend(block)
                require(len(data) <= limit, 'input exceeds byte bound')
            after = os.fstat(stream)
            fields = ('st_dev', 'st_ino', 'st_mode', 'st_size', 'st_mtime_ns', 'st_ctime_ns')
            require(all(getattr(before, k) == getattr(after, k) for k in fields)
                    and len(data) == before.st_size, 'input changed during read')
            # The opened pathname must still identify the same file at read completion.
            final = os.stat(path.name, dir_fd=fd, follow_symlinks=False)
            require(all(getattr(after, k) == getattr(final, k) for k in fields), 'input pathname changed during read')
            return bytes(data)
        finally:
            os.close(stream)
    finally:
        os.close(fd)

def sha(data):
    return hashlib.sha256(data).hexdigest()

def load(path, pin=None):
    raw = read_regular(path, JSON_LIMIT)
    if pin is not None:
        require(isinstance(pin, str) and SHA.fullmatch(pin) and sha(raw) == pin, 'selected input SHA256 mismatch')
    value = json.loads(raw, object_pairs_hook=pairs, parse_constant=lambda _: require(False, 'non-finite JSON'))
    require(isinstance(value, dict), 'JSON input must be an object')
    canonical(value)  # Also reject numeric exponent overflow (e.g. 1e999), beyond parse_constant.
    return value, sha(raw)

def text(value):
    return isinstance(value, str) and 0 < len(value) <= 4096

def records(value, label):
    require(isinstance(value, list) and len(value) <= 512, label + ' must be a bounded array')
    result = {}
    for row in value:
        require(isinstance(row, dict) and text(row.get('id')), label + ' requires identities')
        require(row['id'] not in result, 'duplicate ' + label + ' identity')
        result[row['id']] = row
    return result

def manifest(value):
    canonical(value)
    require(value.get('schema') == SCHEMA, 'unsupported delta schema')
    require(set(value) <= {'schema', 'source', 'files', 'runs', 'dependencies', 'evidence', 'acceptance', 'gaps', 'observations', 'reportedMetrics'}, 'unknown manifest field')
    source = value.get('source')
    require(isinstance(source, dict) and set(source) == {'head', 'tree'}
            and all(isinstance(v, str) and GIT.fullmatch(v) for v in source.values()), 'exact source head/tree required')
    acceptance = value.get('acceptance')
    require(isinstance(acceptance, dict) and set(acceptance) == {'scope', 'reference'}
            and all(text(v) for v in acceptance.values()), 'acceptance scope/reference required')
    require(isinstance(value.get('gaps'), list) and len(value['gaps']) <= 128
            and all(text(v) for v in value['gaps']), 'explicit unresolved gaps required')
    rows = records(value.get('files'), 'file')
    paths = set()
    total = 0
    for row in rows.values():
        require(set(row) == {'id', 'kind', 'path', 'sha256', 'bytes', 'gitBlob'}, 'file fields differ')
        path = row['path']
        require(text(path) and not path.startswith('/') and '\\' not in path
                and all(p not in {'', '.', '..'} for p in path.split('/'))
                and str(PurePosixPath(path)) == path, 'unsafe file path')
        require(path not in paths, 'duplicate file path')
        paths.add(path)
        require(row['kind'] in {'source', 'artifact'}, 'unknown file kind')
        require(isinstance(row['sha256'], str) and SHA.fullmatch(row['sha256']), 'invalid file SHA256')
        require(type(row['bytes']) is int and 0 <= row['bytes'] <= FILE_LIMIT, 'file byte bound exceeded')
        require((row['kind'] == 'source' and isinstance(row['gitBlob'], str) and GIT.fullmatch(row['gitBlob']))
                or (row['kind'] == 'artifact' and row['gitBlob'] is None), 'source Git blob identity required')
        total += row['bytes']
    require(total <= TOTAL_LIMIT, 'aggregate file byte bound exceeded')
    runs = records(value.get('runs'), 'run')
    for row in runs.values():
        require(set(row) == {'id', 'attempt', 'outcome', 'clocks', 'limits'}
                and type(row['attempt']) is int and row['attempt'] >= 1
                and row['outcome'] in {'success', 'failure', 'cancelled', 'unknown', 'running'}
                and isinstance(row['clocks'], dict) and bool(row['clocks'])
                and isinstance(row['limits'], dict) and bool(row['limits']), 'original run/attempt/outcome/clocks/limits required')
    records(value.get('dependencies'), 'dependency')
    records(value.get('evidence'), 'evidence')
    require(isinstance(value.get('observations', {}), dict), 'observations must be an object')
    metrics = value.get('reportedMetrics')
    if metrics is not None:
        require(isinstance(metrics, dict) and set(metrics) == {'coordinatorInterventions', 'sourceReadyAt', 'firstNativeAt', 'reference'}
                and text(metrics['reference']), 'reported metrics need an explicit evidence reference')
        count = metrics['coordinatorInterventions']
        require(count is None or (type(count) is int and 0 <= count <= 1000000), 'invalid reported intervention count')
        dates = []
        for key in ('sourceReadyAt', 'firstNativeAt'):
            raw = metrics[key]
            require(raw is None or text(raw), 'invalid reported metric timestamp')
            parsed = datetime.fromisoformat(raw.replace('Z', '+00:00')) if raw else None
            require(parsed is None or parsed.tzinfo is not None, 'reported timestamp needs timezone')
            dates.append(parsed)
        require(not all(dates) or dates[1] >= dates[0], 'first-native timestamp precedes source-ready')
    return rows

def changes(old, new):
    return {'added': sorted(new.keys() - old.keys()), 'removed': sorted(old.keys() - new.keys()),
            'changed': sorted(k for k in old.keys() & new.keys() if not same(old[k], new[k]))}

def verify(args):
    candidate, candidate_hash = load(args.manifest)
    files = manifest(candidate)
    old = {}
    baseline = None
    baseline_hash = receipt_hash = None
    options = (args.baseline, args.baseline_sha256, args.baseline_receipt, args.baseline_receipt_sha256)
    require(all(options) or not any(options), 'baseline requires both externally selected SHA256 pins and files')
    if all(options):
        baseline, baseline_hash = load(args.baseline, args.baseline_sha256)
        old = manifest(baseline)
        receipt, receipt_hash = load(args.baseline_receipt, args.baseline_receipt_sha256)
        require(receipt.get('schema') == RECEIPT and receipt.get('manifestSha256') == baseline_hash
                and receipt.get('byteVerification') == 'verified'
                and same(receipt.get('proofs'), list(old.values())), 'baseline receipt does not prove selected manifest inventory')
        old_runs = records(baseline['runs'], 'run')
        runs = records(candidate['runs'], 'run')
        require(all(same(runs.get(k), v) for k, v in old_runs.items()), 'original runs cannot be removed or rewritten; append later observations separately')
    reused = [k for k, row in files.items() if same(old.get(k), row)]
    current = [k for k in files if k not in reused]
    read_bytes = 0
    for k in current:
        row = files[k]
        raw = read_regular(Path(args.root) / row['path'], row['bytes'])
        require(len(raw) == row['bytes'] and sha(raw) == row['sha256'], 'payload bytes/hash mismatch: ' + k)
        read_bytes += len(raw)
    delta = {'files': changes(old, files)}
    for key in ('dependencies', 'evidence', 'runs'):
        delta[key] = changes(records(baseline[key], key) if baseline else {}, records(candidate[key], key))
    delta['sourceChanged'] = baseline is None or not same(baseline['source'], candidate['source'])
    delta['acceptanceChanged'] = baseline is None or not same(baseline['acceptance'], candidate['acceptance'])
    delta['gapsChanged'] = baseline is None or not same(baseline['gaps'], candidate['gaps'])
    metrics = candidate.get('reportedMetrics') or {}
    elapsed = None
    if metrics.get('sourceReadyAt') and metrics.get('firstNativeAt'):
        elapsed = (datetime.fromisoformat(metrics['firstNativeAt'].replace('Z', '+00:00')) -
                   datetime.fromisoformat(metrics['sourceReadyAt'].replace('Z', '+00:00'))).total_seconds()
    return {'schema': RECEIPT, 'manifestSha256': candidate_hash, 'baselineManifestSha256': baseline_hash,
            'baselineReceiptSha256': receipt_hash, 'byteVerification': 'verified', 'proofs': list(files.values()),
            'delta': delta, 'currentPayloadReads': current, 'historicalByteProofReuse': reused,
            'measurements': {'payloadFilesRead': len(current), 'payloadBytesRead': read_bytes,
                             'historicalProofsReused': len(reused), 'reportedCoordinatorInterventions': metrics.get('coordinatorInterventions'),
                             'reportedSourceReadyToFirstNativeSeconds': elapsed,
                             'reportedMetricsReference': metrics.get('reference'), 'tokenSavings': None},
            'source': candidate['source'], 'acceptance': candidate['acceptance'], 'gaps': candidate['gaps'],
            'runs': candidate['runs'], 'dependencies': candidate['dependencies'], 'evidence': candidate['evidence'],
            'laterObservations': candidate.get('observations', {}),
            'sourceGitCorrespondence': 'unknown', 'currentFilesystemForReusedProofs': 'not-read',
            'processSettlement': 'unknown', 'writerSettlement': 'unknown', 'cgroupSettlement': 'unknown',
            'namespaceSettlement': 'unknown', 'cleanupSettlement': 'unknown', 'nativeAcceptance': 'unknown'}

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest')
    parser.add_argument('--root', required=True)
    parser.add_argument('--baseline')
    parser.add_argument('--baseline-sha256')
    parser.add_argument('--baseline-receipt')
    parser.add_argument('--baseline-receipt-sha256')
    args = parser.parse_args(argv)
    try:
        print(json.dumps(verify(args), sort_keys=True, separators=(',', ':')))
        return 0
    except (ValueError, OSError, RecursionError) as error:
        print('evidence delta: ERROR: ' + str(error), file=sys.stderr)
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
