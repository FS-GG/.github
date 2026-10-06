"""Projection tests only: measurements are hand-labeled .1 fixture outputs."""
import copy
import hashlib
import json
import pathlib
import unittest
from test_dashboard import D, host_fixture

E = D.EFF
WHEN = '2026-10-06T12:00:00Z'


def metric(subject='PRIVATE-NATIVE', name='observed-resource', numerator=70, denominator=None, status='known'):
    # late-usage-completes in .1 metric-fixtures-v1: observed whole item=70.
    return {'schema': 'fsgg.telemetry.efficiency-metric/1', 'metricId': 'PRIVATE-METRIC', 'metric': name,
            'calculationVersion': 'efficiency-calculation/1', 'purpose': None, 'healthDimension': None, 'unit': 'tokens-total',
            'population': {'itemIds': [subject], 'repository': 'PRIVATE-REPO', 'workType': 'PRIVATE-TYPE', 'acceptanceScope': 'PRIVATE-SCOPE', 'windowStart': WHEN, 'windowEnd': WHEN, 'cutoff': WHEN, 'excludedItems': [], 'openItems': 0, 'abandonedItems': 0},
            'sourceRefs': [{'id': 'PRIVATE-EVIDENCE', 'kind': 'usage', 'revision': 1}],
            'coverage': {axis: 'complete' for axis in E.AXES},
            'value': {'status': status, 'numerator': numerator, 'denominator': denominator, 'unknownAmount': None, 'reason': 'PRIVATE REASON'},
            'policyProfile': None, 'price': {'kind': 'not-applicable', 'currency': None, 'version': None}, 'eventTime': WHEN, 'observedAt': WHEN, 'projectedAt': WHEN}


def assessment(subject='PRIVATE-NATIVE', scope='native-item'):
    return {'schema': 'fsgg.telemetry.efficiency-assessment/1', 'subject': {'itemId': subject, 'outcomeEpoch': 1, 'scope': scope}, 'metricRefs': ['PRIVATE-METRIC'],
            'lifecycle': {'state': 'ready', 'generatedAt': WHEN}, 'provenance': {'validationResult': 'accepted'}, 'publication': {'visibility': 'private', 'policyVersion': None},
            'outcomeSynopsis': 'PRIVATE SYNOPSIS <script>alert(1)</script>', 'wentWell': ['PRIVATE SUCCESS'],
            'findings': [{'primaryCause': 'telemetry-loss', 'necessity': 'uncertain', 'epistemicStatus': 'hypothesis', 'summary': 'PRIVATE FINDING', 'evidenceRefs': ['PRIVATE-EVIDENCE']}],
            'improvements': [{'mechanism': 'PRIVATE IMPROVEMENT'}], 'evidenceRefs': [{'id': 'PRIVATE-EVIDENCE', 'kind': 'usage', 'revision': 1}]}


def labels():
    return {'items': {name: {'key': key, 'label': label, 'url': f'https://github.com/FS-GG/.github/pull/{number}'} for name, key, label, number in [('PRIVATE-NATIVE', 'native-example', 'Synthetic native example', 1), ('PRIVATE-MISSING', 'missing-example', 'Synthetic missing runtime', 2)]}}


def fixture():
    complete = metric()
    missing = metric('PRIVATE-MISSING', numerator=None, status='unknown')
    missing['coverage'].update(population='partial', usage='unknown')
    provisional = assessment('PRIVATE-MISSING', 'provisional-delivery')
    provisional['subject']['outcomeEpoch'] = None
    provisional['lifecycle']['state'] = 'partial'
    return E.project([complete, missing], [assessment(), provisional], labels(), {'PRIVATE-EVIDENCE': 'https://github.com/FS-GG/.github/pull/1'})


class EfficiencyTests(unittest.TestCase):
    def test_vertical_slice_native_and_missing_runtime_never_zero(self):
        value = fixture(); E.validate(value)
        self.assertEqual(value['source'], 'fixtures')
        rows = {r['key']: r for r in value['items']}
        self.assertEqual(rows['native-example']['metrics'][0]['value']['numerator'], 70)
        missing = rows['missing-example']
        self.assertIsNone(missing['metrics'][0]['value']['numerator'])
        self.assertEqual(missing['analysisState'], 'partial')
        self.assertEqual(missing['summary'], 'delivery-accounting-incomplete')
        self.assertEqual(missing['improvements'], ['collect-missing-evidence'])
        raw = json.dumps(value)
        for secret in ('PRIVATE', '<script>', 'alert(', 'outcomeSynopsis', 'mechanism', 'metricId', 'itemId'):
            self.assertNotIn(secret, raw)

    def test_ratios_copy_hand_calculated_values_without_calculation(self):
        # retries-and-replay .1: two retried logical operations / three operations.
        record = metric(name='retry-incidence', numerator=2, denominator=3)
        record['unit'] = 'ratio'
        result = E.project([record], [], labels())
        self.assertEqual(result['items'][0]['summary'], 'accounting-unestablished')
        self.assertEqual(result['items'][0]['scope'], 'unestablished')
        self.assertEqual(result['items'][0]['metrics'][0]['value'], {'status': 'known', 'numerator': 2, 'denominator': 3, 'unknownAmount': None})

    def test_work_mix_uses_admitted_purpose_dimension_and_keeps_repair_separate(self):
        records = []
        for purpose, amount in [('direct-product', 60), ('useful-assurance', 30), ('necessary-coordination', 10), ('process-improvement', 0), ('avoidable-process', 0), ('unknown', 0)]:
            record = metric(name='work-mix', numerator=amount); record['purpose'] = purpose; record['metricId'] = 'PRIVATE-METRIC-' + purpose
            records.append(record)
        result = E.project(records, [], labels())
        self.assertEqual([m['purpose'] for m in result['items'][0]['metrics']], [r['purpose'] for r in records])
        bad = metric(name='work-mix')
        with self.assertRaises(ValueError): E.project([bad], [], labels())
        bad = metric(); bad['purpose'] = 'direct-product'
        with self.assertRaises(ValueError): E.project([bad], [], labels())

    def test_health_dimension_is_explicit_and_ages_are_independent(self):
        records = []
        for dimension, age in [('source-age', 100), ('ingestion-age', 10), ('publication-age', 2)]:
            record = metric(name='data-health', numerator=age); record['unit'] = 'seconds'; record['healthDimension'] = dimension; record['metricId'] += dimension
            records.append(record)
        result = E.project(records, [], labels())
        self.assertEqual([(m['healthDimension'], m['value']['numerator']) for m in result['items'][0]['metrics']], [('source-age', 100), ('ingestion-age', 10), ('publication-age', 2)])
        with self.assertRaises(ValueError): E.project([metric(name='data-health')], [], labels())

    def test_no_labels_no_public_identity_or_metrics(self):
        result = E.project([metric()], [assessment()], {'items': {}})
        self.assertEqual(result['items'], [])
        self.assertEqual(result['coverage']['unmapped'], 1)

    def test_evidence_map_required_and_unsafe_links_refuse(self):
        result = E.project([metric()], [assessment()], labels())
        self.assertEqual(result['items'][0]['problems'][0]['evidenceUrls'], [])
        for url in ('javascript:alert(1)', 'https://evil.example/', 'https://github.com/FS-GG/.github/pull/1?private=x'):
            with self.assertRaises(ValueError): E.project([metric()], [], labels(), {'PRIVATE-EVIDENCE': url})

    def test_unvalidated_findings_withheld_and_missing_refs_withhold_subject(self):
        record = assessment(); record['provenance']['validationResult'] = 'rejected'
        result = E.project([metric()], [record], labels())
        self.assertEqual(result['items'][0]['problems'], [])
        self.assertEqual(result['items'][0]['analysisState'], 'failed')
        record['metricRefs'] = ['PRIVATE-MISSING-REF']
        result = E.project([metric()], [record], labels())
        self.assertEqual(result['items'], [])
        self.assertEqual(result['coverage']['withheld'], 1)

    def test_unknown_amount_zero_denominator_and_private_nested_fields_refuse(self):
        for mutate in (lambda v: v['items'][0].update(privateNotes='PRIVATE'), lambda v: v['items'][0]['metrics'][0]['value'].update(numerator=True), lambda v: v['items'][0]['metrics'][0]['value'].update(denominator=0), lambda v: v['items'][0]['metrics'][0].update(policyProfile='PRIVATE-PROFILE'), lambda v: v['items'][0]['timeline'][0].update(at='bad'), lambda v: v['items'][0]['problems'][0].update(primaryCause='PRIVATE-CAUSE')):
            value = fixture(); mutate(value)
            with self.assertRaises(ValueError): E.validate(value)

    def test_multiitem_population_is_explicit_and_unmapped_subjects_private(self):
        record = metric(); record['population']['itemIds'].append('PRIVATE-UNMAPPED')
        public = E.project([record], [], labels())
        m = public['items'][0]['metrics'][0]
        self.assertEqual(m['populationItems'], ['native-example'])
        self.assertEqual(m['unmappedPopulationItems'], 1)
        self.assertNotIn('PRIVATE-UNMAPPED', json.dumps(public))

    def test_ambiguous_revisions_and_bounds_withhold_without_arbitrary_selection(self):
        record = assessment()
        self.assertEqual(E.project([metric()], [record, copy.deepcopy(record)], labels())['coverage']['withheld'], 1)
        self.assertEqual(E.project([metric() for _ in range(33)], [], labels())['coverage']['withheld'], 1)
        with self.assertRaises(ValueError): E.project([metric()] * 1001, [], labels())

    def test_host5_unavailable_default_and_host4_backwards_read(self):
        value = host_fixture(); D.validate_host(value)
        self.assertEqual(value['schema'], 'fsgg.telemetry.dashboard-host/5')
        self.assertEqual(value['processEfficiency'], E.unavailable())
        old = copy.deepcopy(value); old['schema'] = 'fsgg.telemetry.dashboard-host/4'; old.pop('processEfficiency'); old.pop('revision')
        old['revision'] = hashlib.sha256(json.dumps(old, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()).hexdigest()
        D.validate_host(old)

    def test_committed_browser_fixture_matches_projection(self):
        path = pathlib.Path(__file__).with_name('fixtures')/'process-efficiency-public-v1.json'
        self.assertEqual(json.loads(path.read_text()), fixture())

    def test_static_accessibility_pagination_and_last_valid_feed_path(self):
        root = pathlib.Path(__file__).resolve().parents[2]
        html = (root/'telemetry-dashboard/index.html').read_text()
        script = (root/'telemetry-dashboard/efficiency.js').read_text()
        app = (root/'telemetry-dashboard/app.js').read_text()
        self.assertLess(html.index('src="efficiency.js"'), html.index('src="app.js"'))
        for control in ('efficiency-search', 'efficiency-scope', 'efficiency-view', 'efficiency-prev', 'efficiency-next'):
            self.assertIn(f'id="{control}"', html)
        self.assertIn('slice(page*10,page*10+10)', script)
        self.assertIn('cell.scope="col"', script)
        self.assertIn('node("caption",title)', script)
        self.assertNotIn('innerHTML', script)
        self.assertIn('validateHost(data.host)', app)
        self.assertIn('data.host.schema === "fsgg.telemetry.dashboard-host/5" ? data.host.processEfficiency : null', app)
        self.assertIn('showing last good data', app)


if __name__ == '__main__': unittest.main()
