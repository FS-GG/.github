"""Offline accounting of an explicit, hash-pinned retained helper window.

Never executes programme.fsx or reads transcripts, environment, reference targets,
provider stores or files discovered by glob. Output is sanitized replay data.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path

MAX_FILE = 2 * 1024 * 1024


def sha(data):
    return hashlib.sha256(data).hexdigest()


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate-json-key')
        result[key] = value
    return result


def decode(data):
    return json.loads(data.decode('utf-8'), object_pairs_hook=unique)


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def read_file(path):
    path = Path(path).absolute()
    for part in (path, *path.parents):
        require(not part.is_symlink(), 'linked-input')
    require(path.is_file() and path.stat().st_size <= MAX_FILE, 'input-bound')
    data = path.read_bytes()
    require(len(data) <= MAX_FILE, 'input-grew')
    return data


def collect(root, selection):
    require(set(selection) == {'schema', 'identity', 'helper', 'records'}, 'selection-fields')
    require(selection['schema'] == 'fsgg.programme.context-window-selection/1', 'selection-schema')
    require(1 <= len(selection['records']) <= 16, 'window-bound')
    root = Path(root).absolute()
    rows, all_refs, seen = [], [], set()
    for number, spec in enumerate(selection['records'], 1):
        require(set(spec) == {'name', 'sha256', 'inputSha256', 'resultSha256', 'packetSha256'}, 'record-fields')
        name = spec['name']
        require(Path(name).name == name and name.endswith('.measure.json') and name not in seen, 'record-name')
        seen.add(name)
        raw = read_file(root / name)
        require(sha(raw) == spec['sha256'], 'measurement-drift')
        measure = decode(raw)
        require(measure['schema'] == 'fsgg.programme.measurement/1', 'measurement-schema')
        require(measure['operation'] in ('packet', 'frontier'), 'operation-outside-window')
        stem = name[:-len('.measure.json')]
        require(Path(measure['input']).absolute() == root / (stem + '.input.json'), 'input-location')
        require(Path(measure['artifact']).absolute() == root / (stem + '.json'), 'result-location')
        inp = read_file(root / (stem + '.input.json'))
        out = read_file(root / (stem + '.json'))
        require(sha(inp) == spec['inputSha256'] == measure['inputSha256'] and len(inp) == measure['inputBytes'], 'input-drift')
        require(sha(out) == spec['resultSha256'] and len(out) == measure['artifactBytes'], 'result-drift')
        request, result = decode(inp), decode(out)
        require(measure['nativeTokens'] == 'unknown', 'unsupported-native-usage')
        require(measure['result'] in ('passed', 'refused', 'failed'), 'result-domain')
        row = {'id': f'operation-{number:02}', 'operation': measure['operation'], 'result': measure['result'],
               'inputBytes': len(inp), 'outputBytes': measure['outputBytes'], 'artifactBytes': len(out),
               'elapsedMs': measure['elapsedMs'], 'inputDigest': sha(inp), 'resultDigest': sha(out),
               'measurementDigest': sha(raw)}
        require(all(isinstance(row[k], int) and row[k] >= 0 for k in ('outputBytes', 'elapsedMs')), 'counter-domain')
        if measure['operation'] == 'packet':
            require(result['schema'] == 'fsgg.programme.packet-result/1' and measure['result'] == 'passed', 'packet-outcome')
            require(Path(result['packetPath']).absolute() == root / (stem + '.packet.txt'), 'packet-location')
            packet = read_file(root / (stem + '.packet.txt'))
            require(sha(packet) == spec['packetSha256'] == result['sha256'] and len(packet) == result['bytes'], 'packet-drift')
            selected = result['selected']
            refs = request['references']
            require(len(selected) == len(set(selected)), 'duplicate-selected')
            require(set(selected).isdisjoint(result['omitted']), 'selected-omitted-overlap')
            require(set(selected) | set(result['omitted']) == {r['path'] for r in refs}, 'reference-population')
            require(set(request['mandatoryPaths']) <= set(selected), 'mandatory-omission')
            expected_prefix = f"Objective: {request['objective']}\nStop: {request['stop']}\n".encode()
            require(packet.startswith(expected_prefix), 'packet-prefix-drift')
            cursor = len(expected_prefix)
            chosen = [r for r in sorted(refs, key=lambda r: not r['mandatory']) if r['path'] in selected]
            markers = [f"\n--- {r['trust']}: {r['path']} ({r['sha256']}; {r['reason']}) ---\n".encode() for r in chosen]
            sanitized_refs = []
            for index, (ref, marker) in enumerate(zip(chosen, markers)):
                require(packet[cursor:cursor + len(marker)] == marker, 'packet-header-drift')
                start = cursor + len(marker)
                end = packet.find(markers[index + 1], start) if index + 1 < len(markers) else len(packet)
                require(end >= start and packet[end - 1:end] == b'\n', 'packet-section-drift')
                body = packet[start:end - 1]
                require(ref['startLine'] == ref['endLine'] == 0, 'unsupported-excerpt-window')
                require(sha(body) == ref['sha256'], 'reference-body-drift')
                item = {'digest': ref['sha256'], 'range': [0, 0], 'bytes': len(body), 'trust': ref['trust'], 'mandatory': ref['mandatory']}
                sanitized_refs.append(item)
                all_refs.append(item)
                cursor = end
            require(cursor == len(packet), 'packet-uncovered-bytes')
            row.update(packetBytes=len(packet), packetDigest=sha(packet), references=sanitized_refs, omittedCount=len(result['omitted']))
        else:
            require(spec['packetSha256'] is None, 'unexpected-packet-binding')
            require(request['schema'] == 'fsgg.programme.snapshot/1', 'snapshot-schema')
            row['laneCount'] = len(request['lanes'])
            if measure['result'] == 'passed':
                require(result['schema'] == 'fsgg.programme.frontier/1' and result['inputSha256'] == sha(inp), 'frontier-binding')
                require(result['advisory'] is True, 'effect-authority')
                aliases = {lane['id']: f'lane-{i:02}' for i, lane in enumerate(request['lanes'], 1)}
                require(len(aliases) == len(request['lanes']), 'duplicate-lane')
                row.update(evaluationTime=result['observedAt'], active=[aliases[x] for x in result['active']],
                           actions=[{'lane': aliases[x['lane']], 'action': x['action']} for x in result['actions']])
            else:
                require(result['schema'] == 'fsgg.programme.refusal/1', 'refusal-schema')
                row['refusalDigest'] = sha(result['reason'].encode())
        rows.append(row)
    groups = {}
    repeated = 0
    for ref in all_refs:
        key = (ref['digest'], *ref['range'])
        if key in groups:
            require(groups[key] == ref['bytes'], 'same-reference-size-conflict')
            repeated += ref['bytes']
        groups[key] = ref['bytes']
    totals = {key: sum(row.get(key, 0) for row in rows) for key in ('inputBytes', 'outputBytes', 'artifactBytes', 'packetBytes', 'elapsedMs')}
    return {'schema': 'fsgg.programme.context-baseline/1', 'identity': selection['identity'], 'helper': selection['helper'],
            'selectionDigest': sha(json.dumps(selection, sort_keys=True, separators=(',', ':')).encode()), 'operations': rows, 'totals': totals,
            'packetProjection': {'selectedReferences': len(all_refs), 'uniqueDigestRanges': len(groups), 'repeatedConstructionBytes': repeated,
                                 'interpretation': 'Cross-owner packet construction overlap; actual exposure, parent rereads and justification unknown.'},
            'coverage': {'helperOperations': len(rows), 'observedModelTurns': None, 'nativeUsage': 'unknown', 'compactions': 'not-observed',
                         'actualRetrievals': 'unknown', 'waits': 'unknown', 'workerReturnFailures': 'unknown', 'wholeFamilyCost': 'unknown'},
            'boundary': 'Artifact accounting only; no acceptance, dispatch or effect authority.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root')
    parser.add_argument('selection')
    parser.add_argument('output')
    args = parser.parse_args()
    report = collect(args.root, decode(read_file(args.selection)))
    data = (json.dumps(report, indent=2, sort_keys=True) + '\n').encode()
    # Only a new private regular file; never overwrite retained evidence.
    fd = os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(data)
    print(json.dumps({'operations': len(report['operations']), 'totals': report['totals'], 'sha256': sha(data)}))


if __name__ == '__main__':
    main()
