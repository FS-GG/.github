#!/usr/bin/env python3
"""Summarize private read-only telemetry metadata; never infer matched task trials.
The SQL output and manifest are immutable inputs. No database/network access occurs here.
"""
import argparse
import collections
import hashlib
import json
import os
from pathlib import Path

SECTIONS = {'controls', 'invocations', 'boundaries', 'snapshots', 'population'}
KINDS = {'runtime-start', 'runtime-terminal', 'activity-span', 'runtime-gap'}

def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate JSON key')
        result[key] = value
    return result

def decode(text):
    return json.loads(text, object_pairs_hook=unique_object)

def integer(value):
    if type(value) is not int or value < 0:
        raise ValueError('expected nonnegative integer')
    return value

def parse_sections(text):
    decoder = json.JSONDecoder(object_pairs_hook=unique_object)
    sections = {}
    remaining = text.strip()
    while remaining:
        batch, end = decoder.raw_decode(remaining)
        if not isinstance(batch, list):
            raise ValueError('expected sqlite JSON row array')
        for row in batch:
            if not isinstance(row, dict) or set(row) != {'section', 'payload'}:
                raise ValueError('invalid section envelope')
            key = row['section']
            if key not in SECTIONS or key in sections:
                raise ValueError('unexpected or duplicate section')
            sections[key] = decode(row['payload'])
        remaining = remaining[end:].lstrip()
    if set(sections) != SECTIONS:
        raise ValueError('missing query sections')
    return sections

def graph(rows):
    adjacency = collections.defaultdict(set)
    by_item = collections.defaultdict(set)
    for row, profile in rows:
        if profile is None:
            continue
        item, person = ('item', row['item']), ('profile', profile)
        adjacency[item].add(person)
        adjacency[person].add(item)
        by_item[item].add(person)
    unseen, components = set(adjacency), []
    while unseen:
        seen, stack = set(), [next(iter(unseen))]
        while stack:
            node = stack.pop()
            if node not in seen:
                seen.add(node)
                stack.extend(adjacency[node] - seen)
        unseen -= seen
        components.append({'items': sum(n[0] == 'item' for n in seen),
                           'profiles': sum(n[0] == 'profile' for n in seen)})
    return {'components': sorted(components, key=lambda c: (c['profiles'], c['items'])),
            'multiProfileItems': sum(len(v) > 1 for v in by_item.values()),
            'isMatchedAnchorGraph': False}

def summarize(sections):
    controls, rows = sections['controls'], sections['invocations']
    required = {'rawAssignments', 'assignments', 'invocations', 'items', 'ambiguousInvocations', 'runtimeMismatches'}
    if set(controls) != required or not isinstance(rows, list):
        raise ValueError('invalid controls or rows')
    for value in controls.values():
        integer(value)
    if len({(r['item'], r['invocation']) for r in rows}) != len(rows):
        raise ValueError('duplicate invocation/item output row')
    owners = collections.defaultdict(set)
    for row in rows:
        owners[row['invocation']].add(row['item'])
    derived = {'assignments': len(rows), 'invocations': len(owners),
               'items': len({r['item'] for r in rows}),
               'ambiguousInvocations': sum(len(v) > 1 for v in owners.values())}
    if any(controls[k] != v for k, v in derived.items()) or controls['rawAssignments'] < len(rows):
        raise ValueError('attribution counts disagree with rows')
    if controls['ambiguousInvocations'] or controls['runtimeMismatches']:
        raise ValueError('ambiguous invocation or runtime attribution')
    qualified, profiles, roles, unknown = [], collections.Counter(), collections.Counter(), collections.Counter()
    nulls = {'model': 0, 'effort': 0}
    for row in rows:
        count = integer(row['responses'])
        values, reasons = [], []
        for label, array, null_key in [('model', 'models', 'nullModelResponses'), ('effort', 'efforts', 'nullEffortResponses')]:
            missing = integer(row[null_key]); nulls[label] += missing
            observed = row[array]
            if missing > count or not isinstance(observed, list) or any(v is not None and (not isinstance(v, str) or not v.strip()) for v in observed):
                raise ValueError('invalid profile observations')
            known = {v for v in observed if v is not None}
            if bool(known) != (count > missing) or (count > 0 and (None in observed) != (missing > 0)):
                raise ValueError('profile null counts disagree with observed values')
            if missing: reasons.append(label + '_missing_in_responses')
            if len(known) != 1: reasons.append(label + '_nonunique_or_absent')
            values.append(next(iter(known)) if len(known) == 1 else None)
        if count == 0: reasons.append('no_observed_responses')
        stream, runtime = row.get('producerStream'), row.get('runtime')
        if not isinstance(runtime, str) or not runtime.strip(): reasons.append('unknown_runtime')
        profile = (runtime, *values) if not reasons else None
        role = stream[len(runtime) + 1:] if isinstance(stream, str) and isinstance(runtime, str) and stream.startswith(runtime + ':') else 'unknown'
        if role not in {'worker', 'critic', 'analyst', 'report'}: role = 'unknown'
        row = dict(row, role=role)
        qualified.append((row, profile)); roles[role] += 1
        if profile is not None: profiles[profile] += 1
        else: unknown.update(reasons)
    boundaries = sections['boundaries']
    if not isinstance(boundaries, list) or len(boundaries) != len(KINDS) or {r['kind'] for r in boundaries} != KINDS:
        raise ValueError('incomplete boundary inventory')
    for row in boundaries: integer(row['attributedFacts'])
    snapshots = sections['snapshots']
    if set(snapshots) != {'facts', 'items'}: raise ValueError('invalid snapshot inventory')
    for value in snapshots.values(): integer(value)
    population = sections['population']
    for value in population.values(): integer(value)
    needs_review = snapshots['facts'] > 0 or any(r['attributedFacts'] > 0 for r in boundaries)
    return {'schema': 'fsgg.learn-telemetry-support/2', 'attribution': controls,
            'attributedResponses': sum(r['responses'] for r in rows), 'nullProfileResponses': nulls,
            'observedProfiles': [{'runtime': p[0], 'model': p[1], 'effort': p[2], 'invocations': n} for p, n in sorted(profiles.items())],
            'unknownProfileInvocations': sum(p is None for _, p in qualified), 'unknownProfileReasons': dict(unknown),
            'roles': dict(roles), 'storedRequestedEffortKnown': sum(r.get('storedRequestedEffort') is not None for r in rows),
            'launchResolvedModelMatchesObserved': sum(p is not None and r.get('launchResolvedModel') == p[1] for r, p in qualified),
            'naiveItemProfileGraph': graph(qualified),
            'workerOnlyItemProfileGraph': graph([(r, p) for r, p in qualified if r['role'] == 'worker']),
            'boundaryFacts': boundaries, 'registeredSnapshots': snapshots, 'population': population,
            'newBoundaryFactsRequireReview': needs_review, 'qualifiedMatchedAnchors': 0,
            'matchedAnchorStatus': 'Unknown; metadata projection does not establish equal input/start/context/environment/quality',
            'invarianceDecision': 'inconclusive',
            'profileProvenance': {'launchResolvedModel': 'Claude launch result; historical requested_model column is not original requested setting',
                                  'observedModel': 'assistant.message.model', 'observedEffort': 'assistant root.effort'}}

def audit(query_path, input_path, manifest_path):
    manifest = decode(Path(manifest_path).read_text())
    if manifest.get('schema') != 'fsgg.learn-telemetry-read/2' or type(manifest.get('exitCode')) is not int or manifest['exitCode'] != 0:
        raise ValueError('failed or unsupported collection manifest')
    if any(Path(p).stat().st_size > 32 * 1024 * 1024 for p in [query_path, input_path]): raise ValueError('input exceeds metadata bound')
    query, output = Path(query_path).read_bytes(), Path(input_path).read_bytes()
    for key, data in [('querySha256', query), ('outputSha256', output)]:
        if manifest.get(key) != hashlib.sha256(data).hexdigest(): raise ValueError('evidence hash mismatch: ' + key)
    result = summarize(parse_sections(output.decode('utf-8')))
    result.update(observedAt=manifest['observedAt'], sourceScope=manifest['sourceScope'],
                  sourceQuerySha256=manifest['querySha256'], sourceOutputSha256=manifest['outputSha256'])
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['query', 'input', 'manifest', 'output']: parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    result = audit(args.query, args.input, args.manifest)
    os.umask(0o077)
    if Path(args.output).exists(): raise ValueError('output exists; preserve original evidence')
    Path(args.output).write_text(json.dumps(result, indent=2, sort_keys=True) + '\n')
    Path(args.output).chmod(0o600)
