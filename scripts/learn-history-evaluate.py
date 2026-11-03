#!/usr/bin/env python3
"""Offline aggregate historical evidence audit. Never fits an unidentified model or launches work.

Inputs are private retained API envelopes and optional normalized attempt observations.
Outputs contain an aggregate report plus a PRIVATE source hash manifest. No network calls.
"""
import argparse
import collections
import datetime as dt
import hashlib
import importlib.util
import itertools
import json
import os
from pathlib import Path
import re
import statistics

MAX_BYTES = 512 * 1024 * 1024
ANCHOR_FIELDS = ('inputHash', 'startCommit', 'environmentHash', 'contextHash',
                 'qualityContractHash', 'protocolHash', 'role', 'stage')


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def digest(value):
    return hashlib.sha256(value).hexdigest()


def instant(value):
    if not isinstance(value, str):
        raise ValueError('missing timestamp')
    result = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
    if result.tzinfo is None:
        raise ValueError('timestamp lacks timezone')
    return result


def connection(value, filtered=False):
    if not isinstance(value, dict) or value.get('pageInfo', {}).get('hasNextPage') is not False:
        raise ValueError('incomplete or unknown connection')
    nodes = value.get('nodes')
    total = value.get('totalCount')
    if not isinstance(nodes, list) or type(total) is not int or total < len(nodes):
        raise ValueError('malformed connection')
    if not filtered and total != len(nodes):
        raise ValueError('connection count mismatch')
    if any(not isinstance(n, dict) for n in nodes):
        raise ValueError('null or malformed connection node')
    return nodes


def unique_rows(rows, key):
    seen = {}
    for row in rows:
        identity = row.get(key)
        if not isinstance(identity, str) or not identity:
            raise ValueError('missing stable identity')
        if identity in seen and canonical(seen[identity]) != canonical(row):
            raise ValueError('conflicting duplicate identity')
        seen[identity] = row
    return list(seen.values())


def inventory(root):
    manifest = json.loads((root / 'manifest.json').read_text())
    repos = manifest['repositoryConnection']
    if repos['pageInfo']['hasNextPage'] is not False:
        raise ValueError('repository population truncated')
    listed = manifest['repositories']
    if len(listed) != repos['totalCount'] or len({r['name'] for r in listed}) != len(listed):
        raise ValueError('repository population mismatch')
    if {r['name'] for r in listed} != {r['name'] for r in repos['nodes']}:
        raise ValueError('repository identities differ')
    paths = [root / (r['name'].replace('.', '_') + '.jsonl') for r in listed]
    if len(set(paths)) != len(paths):
        raise ValueError('filename collision')
    if sum(p.stat().st_size for p in paths) > MAX_BYTES:
        raise ValueError('inventory exceeds size bound')
    rows, sources = [], []
    for r, path in zip(listed, paths):
        if r.get('complete') is not True:
            raise ValueError('incomplete repository')
        payload = path.read_bytes()
        if digest(payload) != r['fileSha256']:
            raise ValueError('inventory file digest mismatch')
        part = [json.loads(line) for line in payload.splitlines()]
        if len(part) != r['rows']:
            raise ValueError('repository row count mismatch')
        counts = collections.Counter(x['kind'] for x in part)
        if counts['issue'] != r['expectedIssues'] or counts['pull_request'] != r['expectedPRs']:
            raise ValueError('native issue/PR totals do not reconcile')
        for x in part:
            if x['repository'] != 'FS-GG/' + r['name'] or x['private'] != r['private']:
                raise ValueError('repository attribution mismatch')
            if digest((x['body'] or '').encode()) != x['bodySha256']:
                raise ValueError('body digest mismatch')
            if instant(x['created_at']) > instant(manifest['cutoff']):
                raise ValueError('creation beyond population cutoff')
        rows.extend(part)
        sources.append({'path': str(path), 'sha256': digest(payload)})
    if len({x['node_id'] for x in rows}) != len(rows) or len({x['id'] for x in rows}) != len(rows):
        raise ValueError('duplicate inventory identity')
    sources.append({'path': str(root / 'manifest.json'), 'sha256': digest((root / 'manifest.json').read_bytes())})
    return manifest, rows, sources


def quantiles(values):
    if not values:
        return {'n': 0, 'p50': None, 'p90': None, 'p99': None, 'max': None}
    v = sorted(values)
    return {'n': len(v), **{name: v[round(q * (len(v) - 1))] for name, q in
                           [('p50', .5), ('p90', .9), ('p99', .99)]}, 'max': v[-1]}


def census_summary(manifest, rows):
    issues = [x for x in rows if x['kind'] == 'issue']
    prs = [x for x in rows if x['kind'] == 'pull_request']
    days = [(instant(x['closed_at']) - instant(x['created_at'])).total_seconds() / 86400
            for x in issues if x['closed_at']]
    if any(x < 0 for x in days):
        raise ValueError('negative native calendar interval')
    return {
        'repositories': len(manifest['repositories']), 'issues': len(issues), 'pullRequests': len(prs),
        'uniqueNativeRecords': len(rows), 'privateIssues': sum(x['private'] for x in issues),
        'privatePRs': sum(x['private'] for x in prs),
        'issueStates': dict(collections.Counter(x['state'] + ':' + str(x['state_reason']) for x in issues)),
        'prStates': dict(collections.Counter('merged' if x['pullRequest'].get('merged_at') else x['state'] for x in prs)),
        'creationMonths': dict(sorted(collections.Counter(x['created_at'][:7] for x in issues).items())),
        'closedOnlyCalendarDays': quantiles(days),
        'calendarInterpretation': 'creation-to-native-final-close; unsuccessful closures included; open cases excluded from quantiles; not active work',
        'openIssuesRetained': sum(x['state'] == 'open' for x in issues),
        'rowsUpdatedAfterPopulationCutoff': sum(instant(x['updated_at']) > instant(manifest['cutoff']) for x in rows),
        'cutoff': manifest['cutoff'], 'collectionEndedAt': manifest['endedAt'],
        'collectionInvocations': manifest['requests'],
        'collectionFailedInvocations': sum(x['exitCode'] != 0 for x in manifest['queries']),
        'identityAndDigestChecks': 'passed', 'allRepositoryIssueAndPrCountsReconcile': True,
    }


def creation_body(record):
    edits = connection(record['userContentEdits'])
    if not edits:
        if record['lastEditedAt'] is not None:
            raise ValueError('empty edit chain contradicts lastEditedAt')
        return record['body'], 'verified_unedited'
    if any(e.get('deletedAt') is not None or not isinstance(e.get('diff'), str) for e in edits):
        raise ValueError('deleted or missing edit')
    edits = sorted(unique_rows(edits, 'id'), key=lambda e: instant(e['editedAt']))
    if len(edits) != record['userContentEdits']['totalCount']:
        raise ValueError('duplicate edit identity')
    if instant(edits[0]['editedAt']) != instant(record['createdAt']) or edits[-1]['diff'] != record['body']:
        raise ValueError('unqualified native whole-body format')
    if any(e['diff'].startswith(('@@', 'diff --git', '--- ')) for e in edits):
        raise ValueError('patch format requires verified replay adapter')
    if len({e['diff'] for e in edits if instant(e['editedAt']) == instant(edits[0]['editedAt'])}) > 1:
        raise ValueError('ambiguous same-time original revisions')
    return edits[0]['diff'], 'qualified_native_snapshot_format'


def body_at_boundary(record, boundary):
    original, provenance = creation_body(record)
    when = instant(boundary)
    if when < instant(record['createdAt']):
        raise ValueError('boundary precedes native creation')
    edits = record['userContentEdits']['nodes']
    if not edits:
        return original, provenance
    eligible = [e for e in edits if instant(e['editedAt']) <= when]
    if not eligible:
        raise ValueError('no body at boundary')
    latest_time = max(instant(e['editedAt']) for e in eligible)
    tied = [e for e in eligible if instant(e['editedAt']) == latest_time]
    if len({e['diff'] for e in tied}) > 1:
        raise ValueError('ambiguous same-time body revisions')
    return tied[0]['diff'], 'qualified_native_revision_at_boundary'


def lifecycle(record):
    events = unique_rows(connection(record['timelineItems'], filtered=True), 'id')
    transitions = sorted((e for e in events if e['__typename'] in ('ClosedEvent', 'ReopenedEvent')),
                         key=lambda e: instant(e['createdAt']))
    state = 'open'
    for e in transitions:
        next_state = 'closed' if e['__typename'] == 'ClosedEvent' else 'open'
        if next_state == state or instant(e['createdAt']) < instant(record['createdAt']):
            raise ValueError('inconsistent native state transitions')
        state = next_state
    current = 'open' if record['state'] == 'OPEN' else 'closed'
    if state != current:
        raise ValueError('timeline does not explain current state')
    closures = [e for e in transitions if e['__typename'] == 'ClosedEvent']
    return {'closures': len(closures), 'reopens': len(transitions) - len(closures),
            'firstToFinalCloseSeconds': ((instant(closures[-1]['createdAt']) - instant(closures[0]['createdAt'])).total_seconds()
                                         if closures else None), 'activeSeconds': None}


def review_declarations(record):
    comments = unique_rows(connection(record['comments']), 'id')
    observations = []
    for c in comments:
        if not re.match(r'^<!-- fsgg:independent-review(?:-confirmation)?:v1 -->', c['body']):
            continue
        index = re.search(r'^round: (\d+)\s*$', c['body'], re.M)
        verdict = re.search(r'^verdict: ([\w-]+)\s*$', c['body'], re.M)
        if index and verdict:
            observations.append({'roundIndex': int(index[1]), 'verdict': verdict[1]})
    return observations


def activity_gaps(record, cutoff, thresholds=(3600, 28800, 86400)):
    """Visible-PR commit-gap sensitivity; capped gaps are explicitly hypothetical proxies."""
    nodes = connection(record['commits'])
    commits = unique_rows([n['commit'] for n in nodes], 'oid')
    if len(commits) != len(nodes):
        raise ValueError('duplicate commit row')
    start = instant(record['createdAt'])
    end = instant(record['closedAt']) if record['closedAt'] else instant(cutoff)
    if end < start:
        raise ValueError('negative PR calendar span')
    result = {'commits': len(commits), 'calendarSeconds': (end - start).total_seconds(),
              'authorCommitTimeDifferences': sum(instant(c['authoredDate']) != instant(c['committedDate']) for c in commits),
              'activeSeconds': None, 'stopEvidence': 'Unknown: commit silence is a proxy, not a verified stop'}
    for field in ('authoredDate', 'committedDate'):
        dates = [instant(c[field]) for c in commits]
        inside = sorted(set(d for d in dates if start <= d <= end))
        points = sorted(set([start, *inside, end]))
        gaps = [(b - a).total_seconds() for a, b in zip(points, points[1:])]
        result[field] = {
            'outsidePrWindow': sum(not start <= d <= end for d in dates),
            'distinctTimes': len(set(dates)), 'maxObservableGapSeconds': max(gaps, default=0),
            'thresholds': {str(t): {'gapsOverThreshold': sum(g > t for g in gaps),
                                  'hypotheticalCappedGapSeconds': sum(min(g, t) for g in gaps)} for t in thresholds},
        }
    return result


def support_graph(rows, target):
    if target not in ('requested_assignment', 'provider_effective'):
        raise ValueError('unknown profile estimand')
    unique = unique_rows(rows, 'invocationId')
    edges = set()
    rejected, missing = collections.Counter(), collections.Counter()
    eligible_responses = 0
    for r in unique:
        reasons = []
        for k in ANCHOR_FIELDS:
            if not r.get(k):
                missing[k] += 1
                reasons.append('incomplete_frozen_task')
        if r.get('priorAttemptInformation') is not False or r.get('independenceEstablished') is not True:
            reasons.append('not_independent_or_unknown')
        if target == 'requested_assignment':
            profile = r.get('requestedProfileId')
            if not profile or r.get('requestedAssignmentVerified') is not True:
                reasons.append('unverified_requested_assignment')
        else:
            profile = r.get('effectiveProfileId')
            if not profile or r.get('effectiveConfigurationVerified') is not True:
                reasons.append('unverified_effective_configuration')
        if type(r.get('qualityPass')) is not bool or not r.get('qualitySourceId'):
            reasons.append('unqualified_quality_outcome')
        if reasons:
            rejected.update(set(reasons))
            continue
        anchor = digest(canonical([r[k] for k in ANCHOR_FIELDS]))
        edges.add((anchor, profile))
        eligible_responses += 1
    adjacency = collections.defaultdict(set)
    by_anchor = collections.defaultdict(set)
    for a, p in edges:
        adjacency['a:' + a].add('p:' + p)
        adjacency['p:' + p].add('a:' + a)
        by_anchor[a].add(p)
    remaining = set(adjacency)
    components = []
    while remaining:
        seen, pending = set(), [next(iter(remaining))]
        while pending:
            n = pending.pop()
            if n not in seen:
                seen.add(n)
                pending.extend(adjacency[n] - seen)
        remaining -= seen
        components.append({'anchors': sum(n.startswith('a:') for n in seen), 'profiles': sum(n.startswith('p:') for n in seen)})
    pairs = collections.Counter(pair for ps in by_anchor.values() for pair in itertools.combinations(sorted(ps), 2))
    profiles = len({p for _, p in edges})
    return {'estimand': target, 'candidateRows': len(rows), 'uniqueInvocations': len(unique),
            'qualifiedResponses': eligible_responses, 'qualifiedUniqueEdges': len(edges),
            'qualifiedAnchors': len(by_anchor), 'qualifiedProfiles': profiles,
            'components': sorted(components, key=lambda c: (c['profiles'], c['anchors'])),
            'profilePairAnchorCounts': sorted(pairs.values()), 'missingFrozenTaskFields': dict(missing),
            'rejectedReasonsNonexclusive': dict(rejected),
            'crossProfileConnected': len(components) == 1 and profiles > 1,
            'designMatrixRank': None, 'invarianceEstablished': False,
            'decision': 'inconclusive: structural support audit only; no quality/IRT/invariance fit performed'}


def scoring_module(path=None):
    spec = importlib.util.spec_from_file_location('learn_dispatch', path or Path(__file__).with_name('learn-dispatch.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def score_body(module, item, text, created):
    return module.classify({'itemId': item, 'body': text, 'capturedAt': created, 'sourceRevision': digest(text.encode()),
                            'dispatchBoundary': created, 'producer': 'offline-history-audit'})


def audit_summary(root, census, cutoff, scorer_path=None):
    manifest = json.loads((root / 'audit-manifest.json').read_text())
    indexed = {r['node_id']: r for r in census}
    result = collections.Counter()
    missing = collections.Counter()
    activities, sources = [], [{'path': str(root / 'audit-manifest.json'), 'sha256': digest((root / 'audit-manifest.json').read_bytes())}]
    if len({q['nodeId'] for q in manifest['queries']}) != len(manifest['queries']):
        raise ValueError('duplicate audit query identity')
    scorer = scoring_module(scorer_path)
    for query in manifest['queries']:
        if 'sha256' not in query:
            result['failedHistoryReads'] += 1
            continue
        node_id = query['nodeId']
        path = root / ('audit-' + node_id + '.json')
        payload = path.read_bytes()
        if digest(payload) != query['sha256']:
            raise ValueError('audit source digest mismatch')
        envelope = json.loads(payload)
        if envelope.get('errors') or envelope.get('data', {}).get('node') is None:
            raise ValueError('audit envelope failed')
        x = envelope['data']['node']
        if x['id'] != node_id or node_id not in indexed:
            raise ValueError('audit identity mismatch')
        sources.append({'path': str(path), 'sha256': digest(payload)})
        result['records'] += 1
        is_pr = 'commits' in x
        result['prs' if is_pr else 'issues'] += 1
        for name in ('comments', 'userContentEdits', 'timelineItems'):
            values = connection(x[name], filtered=name == 'timelineItems')
            result[name + 'Returned'] += len(values)
            result[name + 'ReportedTotal'] += x[name]['totalCount']
        native = lifecycle(x)
        result['closeEvents'] += native['closures']
        result['reopenEvents'] += native['reopens']
        result['recordsWithReopen'] += native['reopens'] > 0
        declarations = review_declarations(x)
        result['reviewDeclarations'] += len(declarations)
        result['changesRequiredDeclarations'] += sum(r['verdict'] == 'changes-required' for r in declarations)
        result['roundZeroDeclarations'] += sum(r['roundIndex'] == 0 for r in declarations)
        try:
            body, provenance = creation_body(x)
            result['qualifiedCreationBodies'] += 1
            result[provenance] += 1
            if x['userContentEdits']['totalCount']:
                item = indexed[node_id]['repository'] + '#' + str(x['number'])
                a = score_body(scorer, item, body, x['createdAt'])
                b = score_body(scorer, item, x['body'], x['createdAt'])
                result['editedBodiesCompared'] += 1
                result['changedHardnessTotals'] += a['hardness'] != b['hardness']
                result['changedRoutes'] += a['recommendedProfile'] != b['recommendedProfile']
        except ValueError as exc:
            missing[str(exc)] += 1
        if is_pr:
            reviews = connection(x['reviews'])
            result['nativeReviewObjects'] += len(reviews)
            result['prsWithDeclarationsButNoReviewObjects'] += bool(declarations) and not reviews
            activities.append(activity_gaps(x, cutoff))
    activity = {'prs': len(activities), 'commits': sum(r['commits'] for r in activities),
                'authorCommitTimeDifferences': sum(r['authorCommitTimeDifferences'] for r in activities),
                'calendarSecondsTotal': sum(r['calendarSeconds'] for r in activities), 'activeSeconds': None,
                'inference': 'selected PR metadata only; hypothetical capped-gap sensitivity is NOT active work'}
    for field in ('authoredDate', 'committedDate'):
        activity[field] = {'outsidePrWindow': sum(r[field]['outsidePrWindow'] for r in activities),
                           'maxObservableGapSeconds': max((r[field]['maxObservableGapSeconds'] for r in activities), default=None),
                           'thresholds': {str(t): {k: sum(r[field]['thresholds'][str(t)][k] for r in activities)
                                                  for k in ('gapsOverThreshold', 'hypotheticalCappedGapSeconds')}
                                          for t in (3600, 28800, 86400)}}
    return {'counts': dict(result), 'featureQualificationUnknowns': dict(missing),
            'commitInactivitySensitivity': activity, 'validityInference': 'audit controls and source feasibility; not predictive validity'}, sources


def dispatch_observations(root):
    rows, requested, sources = [], set(), []
    for folder in sorted(root.glob('*-first')):
        ip, ap = folder / 'input.json', folder / 'actual-dispatch-observation.json'
        x, a = json.loads(ip.read_text()), json.loads(ap.read_text())
        # This is evidence of a retained requested launch observation, not provider resolution.
        profile = [a.get('runtime'), a.get('requestedModel'), a.get('requestedEffort')]
        if not all(isinstance(v, str) and v for v in profile):
            raise ValueError('missing retained requested profile')
        requested.add(tuple(profile))
        rows.append({'invocationId': 'retained-observation:' + folder.name,
                     'inputHash': digest(x['body'].encode()), 'requestedProfileId': digest(canonical(profile)),
                     'requestedAssignmentVerified': True, 'effectiveProfileId': None,
                     'qualityPass': None, 'priorAttemptInformation': None, 'independenceEstablished': None})
        sources.extend({'path': str(p), 'sha256': digest(p.read_bytes())} for p in (ip, ap))
    return rows, {'retainedLaunchObservations': len(rows), 'distinctRequestedProfiles': len(requested),
                  'distinctBodySnapshots': len({r['inputHash'] for r in rows}),
                  'effectiveProfileCoverage': 'Unknown; not inferred from requested configuration'}, sources


def legacy_support(root):
    result, sources = [], []
    for name in ('checked-join.json', 'telemetry-all.json', 'telemetry-dispatches.json', 'telemetry-anchors.json'):
        path = root / name
        x = json.loads(path.read_text())
        rows = x['rows'] if isinstance(x, dict) else x
        if rows is None:
            raise ValueError('ambiguous legacy telemetry join')
        keys = sorted({k for r in rows for k in r})
        result.append({'sourceKind': name, 'rows': len(rows), 'fields': keys,
                       'explicitProfileFieldsPresent': bool(set(keys) & {'model', 'effort', 'runtime', 'requestedModel', 'requestedEffort'})})
        sources.append({'path': str(path), 'sha256': digest(path.read_bytes())})
    return result, sources


def content_probes(scorer_path):
    module = scoring_module(scorer_path)
    path = Path(__file__).resolve().parents[1] / 'tests/learn-history/content-probes.json'
    cases = json.loads(path.read_text())['cases']
    results = []
    for case in cases:
        got = score_body(module, 'fixture/task#1', case['body'], '2026-01-01T00:00:00Z')
        category = got.get('hardnessCategory')
        results.append({'name': case['name'], 'hardness': got['hardness'], 'category': category,
                        'safetyFloor': got['safetyFloor'],
                        'literalPolicyConformance': got['hardness'] == case['expectedHardness'] and
                            got['safetyFloor'] == case['expectedSafetyFloor'] and
                            (category == case['expectedCategory'] if category is not None else None)})
    return {'kind': 'synthetic illustrative probes; not population prevalence or independent human ground truth',
            'scorerSha256': digest(scorer_path.read_bytes()), 'rubricVersion': module.RUBRIC,
            'cases': results, 'categoryAvailable': all(r['category'] is not None for r in results),
            'interpretation': 'literal implementation conformance and construct validity are distinct; excluded/quoted words alter scores'}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--inventory-dir', required=True, type=Path)
    p.add_argument('--audit-dir', required=True, type=Path)
    p.add_argument('--dispatch-dir', required=True, type=Path)
    p.add_argument('--legacy-telemetry-dir', required=True, type=Path)
    p.add_argument('--scorer', type=Path, default=Path(__file__).with_name('learn-dispatch.py'))
    p.add_argument('--responses', type=Path, help='optional private normalized attempts; never inferred from transcript quotations')
    p.add_argument('--output', required=True, type=Path, help='new PRIVATE output directory')
    a = p.parse_args()
    os.umask(0o077)
    if a.output.exists():
        raise ValueError('output directory already exists; preserve prior run')
    manifest, rows, sources = inventory(a.inventory_dir)
    audit, extra = audit_summary(a.audit_dir, rows, manifest['cutoff'], a.scorer)
    sources.extend(extra)
    observations, profiles, extra = dispatch_observations(a.dispatch_dir)
    sources.extend(extra)
    legacy, extra = legacy_support(a.legacy_telemetry_dir)
    sources.extend(extra)
    if a.responses:
        observations.extend(json.loads(a.responses.read_text()))
        sources.append({'path': str(a.responses), 'sha256': digest(a.responses.read_bytes())})
    report = {'schema': 'fsgg.learn-history-evaluation/1',
              'primaryConclusion': 'inconclusive: cross-profile hardness validity not established',
              'census': census_summary(manifest, rows), 'sourceAudit': audit, 'retainedDispatchProfiles': profiles,
              'legacyTelemetrySourceSupport': legacy,
              'requestedAssignmentSupport': support_graph(observations, 'requested_assignment'),
              'providerEffectiveSupport': support_graph(observations, 'provider_effective'),
              'modelFit': 'not performed: support/quality qualification precedes fitting',
              'contentAndCategoryProbes': content_probes(a.scorer),
              'routingActivation': False, 'sourceManifestDigest': digest(canonical(sources))}
    sources.append({'path': str(a.scorer), 'sha256': digest(a.scorer.read_bytes())})
    sources.append({'path': str(Path(__file__)), 'sha256': digest(Path(__file__).read_bytes())})
    report['sourceManifestDigest'] = digest(canonical(sources))
    a.output.mkdir(parents=True, mode=0o700)
    (a.output / 'aggregate.json').write_text(json.dumps(report, indent=2) + '\n')
    (a.output / 'PRIVATE-source-manifest.json').write_text(json.dumps({'sources': sources}, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
