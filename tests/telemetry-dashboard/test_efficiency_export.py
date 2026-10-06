"""Offline tests for the agreed canonical consumer seam."""
import copy
import unittest
from unittest import mock
from test_process_efficiency import D, metric, assessment, labels, WHEN
import json


def export():
    metrics = [metric()]
    selected = {'limit': 32, 'returned': 1, 'omitted': 0, 'complete': True}
    return {'schema': 'fsgg.telemetry.efficiency-export/1', 'snapshotRevision': 'a'*64, 'sourceFingerprint': 'sha256:'+'b'*64,
            'cutoff': WHEN, 'observedAt': WHEN,
            'selection': {'limit': 200, 'returned': 1, 'omitted': 0, 'complete': True},
            'metricSelection': {'limit': 1000, 'returned': 1, 'omitted': 0, 'complete': True},
            'items': [{'itemId': 'PRIVATE-NATIVE', 'originalItemId': 'PRIVATE-ORIGINAL', 'metrics': metrics, 'metricSelection': selected, 'assessment': assessment(),
                       'analysisHealth': {'state': 'ready', 'requestState': 'settled', 'assessmentState': 'ready', 'pendingSince': None, 'lastAttemptAt': WHEN, 'failureCode': None},
                       'freshness': {'sourceObservedAt': WHEN, 'ingestedAt': None}}]}


class ExportTests(unittest.TestCase):
    def test_closed_revision_and_explicit_null_clocks(self):
        value = export(); D.validate_efficiency_export(value, 'a'*64)
        self.assertIsNone(value['items'][0]['freshness']['ingestedAt'])
        for mutation in (lambda v: v.update(snapshotRevision='c'*64), lambda v: v.update(sourceFingerprint='b'*64), lambda v: v.update(privatePacket=[]), lambda v: v['items'][0].update(extra=[]), lambda v: v['items'][0]['analysisHealth'].update(failureCode='PRIVATE REASON')):
            value = export(); mutation(value)
            with self.assertRaises(ValueError): D.validate_efficiency_export(value, 'a'*64)

    def test_partial_metric_population_is_not_complete_or_zero(self):
        value = export(); value['items'][0]['metrics'] = []
        value['items'][0]['metricSelection'].update(returned=0, omitted=1, complete=False)
        value['metricSelection'].update(returned=0, omitted=1, complete=False)
        D.validate_efficiency_export(value, 'a'*64)
        value['metricSelection']['complete'] = True
        with self.assertRaises(ValueError): D.validate_efficiency_export(value, 'a'*64)

    def test_new_pending_request_does_not_hide_older_ready_assessment(self):
        for request, display in [('pending', 'pending'), ('claimed', 'running')]:
            value = export(); health = value['items'][0]['analysisHealth']
            health.update(requestState=request, state=display, assessmentState='ready', pendingSince=WHEN)
            D.validate_efficiency_export(value, 'a'*64)
            health['state'] = 'ready'
            with self.assertRaises(ValueError): D.validate_efficiency_export(value, 'a'*64)
        value = export(); value['items'][0]['analysisHealth']['requestState'] = 'running'
        with self.assertRaises(ValueError): D.validate_efficiency_export(value, 'a'*64)

    def test_subjects_duplicates_and_global_counts_refuse(self):
        for mutation in (lambda v: v['items'][0]['metrics'][0]['population'].update(itemIds=['OTHER']), lambda v: v['items'][0]['assessment']['subject'].update(itemId='OTHER'), lambda v: v['items'].append(copy.deepcopy(v['items'][0])), lambda v: v['metricSelection'].update(returned=2), lambda v: v['items'][0]['metricSelection'].update(limit=33)):
            value = export(); mutation(value)
            with self.assertRaises(ValueError): D.validate_efficiency_export(value, 'a'*64)

    def test_one_batch_and_remaining_caller_deadline_no_per_item_spawn(self):
        with mock.patch.object(D.time, 'monotonic', return_value=100), mock.patch.object(D, 'engine_json', return_value=export()) as engine:
            value = D.read_efficiency_export('/private/store', 'engine', 'a'*64, 110, '/private/config.json', 'FS-GG/example')
        self.assertEqual(value['snapshotRevision'], 'a'*64)
        self.assertEqual(engine.call_count, 1)
        self.assertEqual(engine.call_args.kwargs, {'max_bytes': D.MAX_CANONICAL_SNAPSHOT, 'timeout_seconds': 10})
        self.assertEqual(engine.call_args.args[1], ['telemetry', 'efficiency-export', '--snapshot-revision', 'a'*64, '--store-root', '/private/store', '--max-items', '200', '--max-metrics', '1000', '--config', '/private/config.json', '--repository', 'FS-GG/example'])
        with mock.patch.object(D.time, 'monotonic', return_value=100), mock.patch.object(D, 'engine_json') as engine:
            with self.assertRaises(D.HostSourceError): D.read_efficiency_export('/private/store', 'engine', 'a'*64, 100)
            engine.assert_not_called()
        for deadline in (float('nan'), float('inf'), True, None):
            with mock.patch.object(D, 'engine_json') as engine:
                with self.assertRaises(D.HostSourceError): D.read_efficiency_export('/private/store', 'engine', 'a'*64, deadline)
                engine.assert_not_called()

    def test_canonical_join_uses_original_approved_identity_and_exact_bindings(self):
        value = export(); approved = labels(); approved['items']['PRIVATE-ORIGINAL'] = approved['items'].pop('PRIVATE-NATIVE')
        result = D.project_efficiency_exports([(value, 'a'*64)], approved)
        self.assertEqual(result['source'], 'canonical-export')
        self.assertEqual(result['exports'][0]['sourceFingerprint'], value['sourceFingerprint'])
        self.assertEqual(result['exports'][0]['baseSnapshotRevision'], value['snapshotRevision'])
        item = result['items'][0]
        self.assertEqual(item['key'], 'native-example')
        self.assertEqual(item['metrics'][0]['value']['numerator'], 70)
        self.assertIsNone(item['exportHealth']['ingestedAt'])
        self.assertEqual(item['exportHealth']['sourceKey'], 's0')
        for secret in ('PRIVATE', 'metricId', 'itemId', 'summaryText', 'reason'):
            self.assertNotIn(secret, json.dumps(result))

    def test_health_only_rows_and_older_assessment_preserve_new_queue(self):
        for request, state in [('pending', 'pending'), ('claimed', 'running')]:
            value = export(); value['items'][0]['originalItemId'] = 'PRIVATE-NATIVE'
            value['items'][0]['analysisHealth'].update(requestState=request, state=state, pendingSince=WHEN)
            result = D.project_efficiency_exports([(value, 'a'*64)], labels())
            self.assertEqual(result['items'][0]['analysisState'], state)
            self.assertEqual(result['items'][0]['exportHealth']['assessmentState'], 'ready')
        value = export(); item = value['items'][0]
        item.update(originalItemId='PRIVATE-NATIVE', metrics=[], assessment=None)
        item['metricSelection'].update(returned=0, omitted=4, complete=False)
        value['metricSelection'].update(returned=0, omitted=4, complete=False)
        value['selection'].update(omitted=3, complete=False)
        item['analysisHealth'].update(state='pending', requestState='pending', assessmentState=None, pendingSince=WHEN)
        result = D.project_efficiency_exports([(value, 'a'*64)], labels())
        row = result['items'][0]
        self.assertEqual(row['summary'], 'accounting-unestablished')
        self.assertEqual(row['metrics'], [])
        self.assertEqual(row['analysisState'], 'pending')
        self.assertEqual(row['exportHealth']['metricSelection']['omitted'], 4)
        self.assertEqual(result['exports'][0]['selection']['omitted'], 3)
        self.assertIsNone(row['exportHealth']['assessmentState'])

    def test_two_store_duplicate_identity_is_withheld_without_aggregation(self):
        value = export(); value['items'][0]['originalItemId'] = 'PRIVATE-NATIVE'
        second = copy.deepcopy(value); second.update(snapshotRevision='c'*64, sourceFingerprint='sha256:'+'d'*64)
        result = D.project_efficiency_exports([(value, 'a'*64), (second, 'c'*64)], labels())
        self.assertEqual(result['items'], [])
        self.assertEqual(result['coverage']['withheld'], 2)
        self.assertEqual([s['key'] for s in result['exports']], ['s0', 's1'])
        with self.assertRaises(ValueError): D.project_efficiency_exports([(value, 'a'*64)]*3, labels())

    def test_public_queue_binding_clocks_and_omissions_remain_closed(self):
        value = export(); value['items'][0]['originalItemId'] = 'PRIVATE-NATIVE'
        for mutation in (lambda r: r['exports'][0].update(sourceFingerprint='sha256:private'), lambda r: r['items'][0]['exportHealth'].update(sourceKey='private'), lambda r: r['items'][0]['exportHealth'].update(ingestedAt=0), lambda r: r['items'][0]['exportHealth']['metricSelection'].update(omitted=1), lambda r: r['items'][0]['exportHealth'].update(requestState='claimed')):
            result = D.project_efficiency_exports([(value, 'a'*64)], labels()); mutation(result)
            with self.assertRaises(ValueError): D.validate_process_efficiency(result)

    def test_public_byte_budget_withholds_rows_but_retains_exact_source_counts(self):
        value = export(); value['items'][0]['originalItemId'] = 'PRIVATE-NATIVE'
        result = D.project_efficiency_exports([(value, 'a'*64)], labels(), max_bytes=1024)
        self.assertLessEqual(len(json.dumps(result, ensure_ascii=True, separators=(',', ':')).encode()), 1024)
        self.assertEqual(result['items'], [])
        self.assertEqual(result['coverage']['withheld'], 1)
        self.assertEqual(result['exports'][0]['selection']['returned'], 1)
        for budget in (0, True, 1_048_577):
            with self.assertRaises(ValueError): D.project_efficiency_exports([(value, 'a'*64)], labels(), max_bytes=budget)

    def test_existing_approved_repository_item_url_does_not_approve_metric_links(self):
        value = export(); value['items'][0]['originalItemId'] = 'PRIVATE-NATIVE'
        approved = labels(); approved['items']['PRIVATE-NATIVE']['url'] = 'https://github.com/FS-GG/.github'
        result = D.project_efficiency_exports([(value, 'a'*64)], approved)
        self.assertEqual(result['items'][0]['url'], 'https://github.com/FS-GG/.github')
        self.assertEqual(result['items'][0]['metrics'][0]['sourceEvidence'], [])
        with self.assertRaises(ValueError): D.project_efficiency_exports([(value, 'a'*64)], approved, {'PRIVATE-EVIDENCE': 'https://github.com/FS-GG/.github'})

    def test_reader_is_inactive_and_never_replaces_source_deliveries(self):
        self.assertEqual(D.EFF.unavailable()['status'], 'unavailable')
        import inspect
        self.assertNotIn('read_efficiency_export(', inspect.getsource(D.build_host))


if __name__ == '__main__': unittest.main()
