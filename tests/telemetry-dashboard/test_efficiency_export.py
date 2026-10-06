"""Offline tests for the agreed inactive canonical consumer seam."""
import copy
import unittest
from unittest import mock
from test_process_efficiency import D, metric, assessment, WHEN


def export():
    metrics = [metric()]
    selected = {'limit': 32, 'returned': 1, 'omitted': 0, 'complete': True}
    return {'schema': 'fsgg.telemetry.efficiency-export/1', 'snapshotRevision': 'a'*64, 'sourceFingerprint': 'sha256:'+'b'*64,
            'cutoff': WHEN, 'observedAt': WHEN,
            'selection': {'limit': 200, 'returned': 1, 'omitted': 0, 'complete': True},
            'metricSelection': {'limit': 1000, 'returned': 1, 'omitted': 0, 'complete': True},
            'items': [{'itemId': 'PRIVATE-NATIVE', 'originalItemId': 'PRIVATE-ORIGINAL', 'metrics': metrics, 'metricSelection': selected, 'assessment': assessment(),
                       'analysisHealth': {'state': 'ready', 'pendingSince': None, 'lastAttemptAt': WHEN, 'failureCode': None},
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

    def test_reader_is_inactive_and_never_replaces_source_deliveries(self):
        self.assertEqual(D.EFF.unavailable()['status'], 'unavailable')
        import inspect
        self.assertNotIn('read_efficiency_export(', inspect.getsource(D.build_host))


if __name__ == '__main__': unittest.main()
