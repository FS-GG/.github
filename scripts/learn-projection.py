#!/usr/bin/env python3
"""Offline LEARN card/completion projection from a complete, explicitly selected cohort.
Evidence envelopes retain canonical original payload bytes, a SHA256 and retrieval reference.
This tool performs no remote reads or writes; the integrator verifies evidence authority.
"""
import argparse
import hashlib
import importlib.util
import json
import re
from pathlib import Path

_spec = importlib.util.spec_from_file_location('learn_dispatch', Path(__file__).with_name('learn-dispatch.py'))
dispatch = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(dispatch)
CATEGORY_VERSION = 'learn-02-category-v1'
NAMES = ['Hardness', 'Actual models', 'Actual effort', 'Execution provenance']


def category(scores):
    if set(scores) != set(dispatch.RULES):
        raise ValueError('all eight dimension scores required')
    if any(value is not None and (type(value) is not int or not 0 <= value <= 3) for value in scores.values()):
        raise ValueError('invalid dimension score')
    if any(value is None for value in scores.values()):
        return 'Unknown'
    total = sum(scores.values())
    return 'High' if total >= 13 or scores['D3'] >= 2 or scores['D5'] >= 2 else 'Medium' if total >= 7 else 'Low'


def evidence(envelope):
    payload = envelope['payload']
    digest = hashlib.sha256(dispatch.canonical(payload)).hexdigest()
    if envelope['sha256'] != digest or not isinstance(envelope.get('reference'), str) or not envelope['reference'].strip():
        raise ValueError('original evidence digest/reference mismatch')
    return payload, dict(sha256=digest, reference=envelope['reference'])


def native_population(native):
    if native.get('schema') != 'fsgg.learn-native-population-read/1' or native.get('complete') is not True:
        raise ValueError('complete qualified native metadata population required')
    if native.get('projectId') != 'PVT_kwDOEYAWY84Bldpa':
        raise ValueError('exact Coordination Project 3 identity required')
    population = []
    coverage = dict(nativeItems=len(native['items']), outcomes=0, unknownWorkLevel=0, nonIssueCards=0,
                    archivedHistoricalMembershipCoverage='Unknown', sourceCapturedAt=native['capturedAt'])
    seen = set()
    for item in native['items']:
        if item['id'] in seen or item['fieldValues']['pageInfo']['hasNextPage'] is not False:
            raise ValueError('duplicate card or incomplete field-value connection')
        seen.add(item['id'])
        issue = item.get('content') or {}
        if issue.get('__typename') != 'Issue':
            coverage['nonIssueCards'] += 1
            continue
        levels = [field.get('name') for field in item['fieldValues']['nodes'] if field.get('field', {}).get('name') == 'Work level']
        if levels == ['Outcome']:
            coverage['outcomes'] += 1
        elif levels != ['Delivery']:
            coverage['unknownWorkLevel'] += 1
        else:
            population.append(dict(itemId=issue['repository']['nameWithOwner'] + '#' + str(issue['number']),
                nodeId=issue['id'], state=issue['state'], archived=item['isArchived'], workLevel='Delivery'))
    return population, coverage


def assess_scope(native):
    """Assess the captured current acceptance, never masquerading as pre-dispatch calibration."""
    body = native['body']
    match = re.search(r'(?ims)^##[ \t]+Done when[ \t]*\n(.*?)(?=^##[ \t]|\Z)', body)
    text = match[1].strip() if match else body.strip()
    if not text:
        raise ValueError('current declared scope unavailable')
    frozen = dict(native, body=text, dispatchBoundary='current-scope-assessment; not a dispatch')
    scored = dispatch.classify(frozen)
    keep = ['itemId', 'rubricVersion', 'capturedAt', 'sourceRevision', 'producer', 'inputDigest',
            'provenance', 'scores', 'evidence', 'hardness', 'categoryVersion', 'hardnessCategory', 'safetyFloor']
    assessment = {key: scored[key] for key in keep}
    assessment.update(schema='fsgg.learn-current-assessment/1', classificationBasis='current-scope',
        assessmentInput='Done when' if match else 'current-declared-body',
        nativeBodyDigest=hashlib.sha256(body.encode()).hexdigest())
    if native.get('supersedesClassificationDigest'):
        assessment['supersedesClassificationDigest'] = native['supersedesClassificationDigest']
    return assessment


def project(cohort):
    if cohort.get('schema') != 'fsgg.learn-projection-input/1' or cohort.get('complete') is not True:
        raise ValueError('qualified complete selected population required')
    population = cohort['population']
    identities = [row['itemId'] for row in population]
    if not population or len(set(identities)) != len(identities) or len({row['nodeId'] for row in population}) != len(population):
        raise ValueError('nonempty unique selected population required')
    if any(not re.fullmatch(r'FS-GG/[\w.-]+#[1-9][0-9]*', row['itemId']) or row['state'] not in ('OPEN', 'CLOSED')
           or type(row['archived']) is not bool or row['workLevel'] != 'Delivery' for row in population):
        raise ValueError('explicit native Delivery identity/state/archive facts required')
    snapshots = {}
    assessments = {}
    launches = {}
    observations = {}
    for envelope in cohort['classifications']:
        snapshot, source = evidence(envelope)
        item = snapshot['itemId']
        if item not in identities or item in snapshots:
            raise ValueError('ambiguous or unselected classification')
        if snapshot['rubricVersion'] != dispatch.RUBRIC or snapshot['provenance'] != 'rule':
            raise ValueError('unsupported classification provenance')
        # Only an original retained dispatch snapshot can supply historical dimensions.
        if snapshot['schema'] != 'fsgg.learn-dispatch/1' or not snapshot.get('dispatchBoundary') or not snapshot.get('inputDigest'):
            raise ValueError('original pre-dispatch snapshot required')
        snapshots[item] = snapshot, source
    for envelope in cohort.get('currentScopes', []):
        native, source = evidence(envelope)
        assessment = assess_scope(native)
        item = assessment['itemId']
        if item not in identities or item in assessments:
            raise ValueError('ambiguous or unselected current scope')
        assessments[item] = assessment, source
    for envelope in cohort.get('assessments', []):
        assessment, source = evidence(envelope)
        item = assessment['itemId']
        if item not in identities or item in assessments:
            raise ValueError('ambiguous or unselected current assessment')
        if assessment.get('schema') != 'fsgg.learn-current-assessment/1' or assessment.get('classificationBasis') != 'current-scope' or assessment.get('rubricVersion') != dispatch.RUBRIC:
            raise ValueError('current assessment basis/version required')
        if not all(assessment.get(key) for key in ('capturedAt', 'sourceRevision', 'inputDigest', 'nativeBodyDigest', 'assessmentInput')):
            raise ValueError('revision-bound current assessment evidence required')
        category(assessment['scores'])
        assessments[item] = assessment, source
    for envelope in cohort['launches']:
        launch, source = evidence(envelope)
        configured = None
        if launch.get('runtime') == 'collaboration-spawn-agent' and launch.get('observedStatus') in ('running', 'completed'):
            # Retained original coordinator observation after an accepted native spawn. A routing
            # snapshot alone cannot enter this branch or supply a confirmed launch identity.
            matches = [(item, snapshot) for item, (snapshot, _) in snapshots.items()
                       if hashlib.sha256(dispatch.canonical(snapshot)).hexdigest() == launch.get('bodySnapshot')]
            if len(matches) != 1 or not re.fullmatch(r'/root/[a-zA-Z0-9_/-]+', launch.get('agent', '')):
                raise ValueError('accepted launch requires unique original snapshot and canonical returned agent identity')
            item, snapshot = matches[0]
            if not launch.get('afterVerifiedAppliedReceipt') or not launch.get('requestedModel') or not launch.get('requestedEffort'):
                raise ValueError('accepted launch configuration is incomplete')
            identity, role = launch['agent'], 'Unknown'
            configured = dict(model=launch['requestedModel'], effort=launch['requestedEffort'],
                provenance='root-recorded-launch-configuration', rawReturnedToolResponse='Unknown')
        else:
            match = re.fullmatch(r'fsgg-dispatch: item=(FS-GG/[\w.-]+#[1-9][0-9]*) role=(worker|critic|analyst|report)', launch['brief'].splitlines()[0])
            if not match or launch['itemId'] != match[1] or match[1] not in identities:
                raise ValueError('explicit original dispatch marker required')
            item, identity, role = match[1], launch['invocationId'], match[2]
        value = (item, role, source, configured, launch.get('threadId', identity))
        if identity in launches and launches[identity] != value:
            raise ValueError('ambiguous execution identity attribution')
        launches[identity] = value
    threads = [launch[4] for launch in launches.values()]
    if len(set(threads)) != len(threads):
        raise ValueError('ambiguous shared thread attribution across invocation identities or roles')
    for envelope in cohort['observations']:
        observation, source = evidence(envelope)
        if observation['kind'] != 'provider-usage-observation/1':
            raise ValueError('actual provider observation required; requested routing is not execution evidence')
        identity = observation['identity']
        if identity in observations and observations[identity] != (observation, source):
            raise ValueError('conflicting provider observation identity')
        observations[identity] = observation, source
    records = []
    for native in population:
        item = native['itemId']
        original = snapshots.get(item)
        current = assessments.get(item)
        supersedes = current[0].get('supersedesClassificationDigest') if current else None
        if supersedes and (not original or supersedes != hashlib.sha256(dispatch.canonical(original[0])).hexdigest()):
            raise ValueError('explicit current scope replacement must bind the exact retained original classification')
        retained = current if supersedes else original or current
        executions = []
        for identity, (assigned, role, launch_source, configured, thread_id) in sorted(launches.items()):
            if assigned != item:
                continue
            facts = [(value, source) for value, source in observations.values() if value['threadId'] == thread_id]
            # Preserve every observed model/effort pair, including changes within one invocation.
            pairs = {(value.get('model') or 'Unknown', value.get('effort') or 'Unknown') for value, _ in facts} or {('Unknown', 'Unknown')}
            for model, effort in sorted(pairs):
                if not all(isinstance(value, str) and value.strip() and len(value) <= 256 and not any(ord(c) < 32 for c in value) for value in (model, effort)):
                    raise ValueError('invalid observed model/effort')
                executions.append(dict(invocationId=identity, role=role, model=model, effort=effort,
                    evidenceClass='provider-observed' if facts else 'unavailable', launchConfiguration=configured,
                    evidence=[launch_source] + [source for value, source in facts if (value.get('model') or 'Unknown', value.get('effort') or 'Unknown') == (model, effort)]))
        hardness = category(retained[0]['scores']) if retained else 'Unknown'
        gaps = []
        if not original:
            gaps.append('original-dispatch-snapshot-unavailable')
        if not executions:
            gaps.append('attributed-execution-unavailable')
        if any(value.get('launchConfiguration', {}).get('rawReturnedToolResponse') == 'Unknown' for value in executions if value.get('launchConfiguration')):
            gaps.append('raw-launch-response-unavailable')
        if any(value['role'] == 'Unknown' for value in executions):
            gaps.append('execution-role-unavailable')
        if not executions or any(value['model'] == 'Unknown' for value in executions):
            gaps.append('actual-model-unavailable')
        if not executions or any(value['effort'] == 'Unknown' for value in executions):
            gaps.append('actual-effort-unavailable')
        records.append(dict(schema='fsgg.learn-completion/1', itemId=item, nodeId=native['nodeId'],
            categoryVersion=CATEGORY_VERSION, hardnessCategory=hardness,
            classificationBasis='current-scope' if retained is current and current else 'pre-dispatch' if original else 'Unknown',
            originalClassification=original[0] if original else None,
            classification=retained[0] if retained else None, classificationSource=retained[1] if retained else None,
            executions=executions, gaps=gaps, state=native['state'], archived=native['archived'],
            populationCoverage='complete-selected', executionCoverage='partial' if gaps else 'observed-only'))
    # A selected population census does not prove full historical runtime capture.
    summary = dict(selectedDeliveries=len(records), closedDeliveries=sum(row['state'] == 'CLOSED' for row in records),
        archivedDeliveries=sum(row['archived'] for row in records),
        knownHardness=sum(row['hardnessCategory'] != 'Unknown' for row in records),
        originalHardnessKnown=sum(row['originalClassification'] is not None and category(row['originalClassification']['scores']) != 'Unknown' for row in records),
        currentScopeHardnessKnown=sum(row['classificationBasis'] == 'current-scope' and row['hardnessCategory'] != 'Unknown' for row in records),
        observedExecutionIdentities=len(launches), actualModelKnown=sum('actual-model-unavailable' not in row['gaps'] for row in records),
        actualEffortKnown=sum('actual-effort-unavailable' not in row['gaps'] for row in records),
        launchConfirmedIdentities=sum(value[3] is not None for value in launches.values()),
        unattributedObservations=sum(value['threadId'] not in {launch[4] for launch in launches.values()} for value, _ in observations.values()),
        completeHistoricalExecutionCoverage='Unknown')
    return dict(schema='fsgg.learn-projection/1', records=records, coverage=summary, nativePopulationCoverage=cohort.get('nativePopulationCoverage'))


def comment_for(record):
    digest = hashlib.sha256(dispatch.canonical(record)).hexdigest()
    return '<!-- fsgg:learn-completion/v1 digest=' + digest + ' -->\n```json\n' + dispatch.canonical(record).decode() + '```\n'


def assignment(record, comment_url):
    # Values are summaries; exact identities, roles, dimensions and requested profiles stay on the issue.
    def values(key):
        return dispatch.execution_summary(record['executions'], key)
    return dict(issueId=record['nodeId'], evidence=comment_url, values={
        'Hardness': record['hardnessCategory'], 'Actual models': values('model'), 'Actual effort': values('effort'),
        'Execution provenance': comment_url})


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--native-population', type=Path, help='qualified current native metadata census; preserves excluded-card coverage')
    args = parser.parse_args()
    try:
        cohort = json.loads(args.input.read_text())
        if args.native_population:
            cohort['population'], cohort['nativePopulationCoverage'] = native_population(json.loads(args.native_population.read_text()))
        result = project(cohort)
        args.output.mkdir(mode=0o700, parents=True, exist_ok=False)
        (args.output / 'projection.json').write_bytes(dispatch.canonical(result))
        for index, record in enumerate(result['records']):
            (args.output / (str(index) + '.comment.md')).write_text(comment_for(record))
        print(json.dumps(dict(**result['coverage'], effects=0), sort_keys=True))
    except (KeyError, TypeError, ValueError, OSError) as error:
        parser.exit(2, str(error) + '\n')
