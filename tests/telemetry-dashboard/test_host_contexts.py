"""Pure controls for independent historical and prospective store views."""
import copy
import json
import pathlib
import tempfile
import unittest
from unittest import mock
from test_dashboard import D, host_fixture


def rows():
    return [{"key": key, "label": label, "status": "ready", "reason": None, "host": host_fixture()}
            for key, label in D.CONTEXT_LABELS.items()]


class ContextTests(unittest.TestCase):
    def test_independent_authorities_never_enter_cross_store_join(self):
        sources = [{"key": "historical", "storeRoot": "/PRIVATE-HISTORY"}, {"key": "current", "storeRoot": "/PRIVATE-CURRENT"}]
        snapshots = [({"workspaceId": "", "epoch": "same-private-id"}, {"revision": "a"*64}),
                     ({"workspaceId": "PRIVATE-WORKSPACE", "epoch": "same-private-id"}, {"revision": "b"*64})]
        with mock.patch.object(D, '_read_host_snapshot', side_effect=snapshots), mock.patch.object(D, '_project_host_snapshot', side_effect=[host_fixture(), host_fixture()]) as project, mock.patch.object(D, '_join_host_snapshots', side_effect=AssertionError('cross-authority join')):
            value = D.build_host(resolved_config={"engine": "engine", "sources": sources})
        self.assertEqual(project.call_count, 2)
        self.assertEqual([row['key'] for row in value['contexts']], ['historical', 'current'])
        self.assertNotIn('PRIVATE', json.dumps(value))
        self.assertNotIn('totals', value)
        D.validate_host(value)

    def test_source_failure_is_unknown_not_zero_work(self):
        with mock.patch.object(D, '_read_host_snapshot', side_effect=[D.HostSourceError('PRIVATE FAILURE'), ({}, {"revision":"a"*64})]), mock.patch.object(D, '_project_host_snapshot', return_value=host_fixture()):
            value = D.build_host(resolved_config={"engine": "engine", "sources": [{"key": "historical", "storeRoot": "/history"}, {"key": "current", "storeRoot": "/current"}]})
        self.assertIsNone(value['contexts'][0]['host'])
        self.assertEqual(value['contexts'][0]['reason'], 'source-unavailable')
        self.assertEqual(value['contexts'][1]['status'], 'ready')
        self.assertNotIn('PRIVATE FAILURE', json.dumps(value))

    def test_one_shared_byte_budget_with_explicit_withholding(self):
        value = D.project_host_contexts(rows(), max_bytes=1024)
        self.assertLessEqual(len(D.dump(value)), 1024)
        self.assertTrue(all(row['reason']=='public-budget-exceeded' and row['host'] is None for row in value['contexts']))
        D.validate_host(value)

    def test_closed_contexts_reject_private_labels_recursive_hosts_and_false_success(self):
        value = D.project_host_contexts(rows())
        for change in (lambda v: v['contexts'][0].update(label='PRIVATE-WORKSPACE'),
                       lambda v: v['contexts'][0].update(host=None),
                       lambda v: v['contexts'][0].update(host=copy.deepcopy(value)),
                       lambda v: v.update(totals={})): 
            mutated = copy.deepcopy(value); change(mutated)
            mutated.pop('revision'); mutated['revision']=D.hashlib.sha256(json.dumps(mutated, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()).hexdigest()
            with self.assertRaises(ValueError): D.validate_host(mutated)

    def test_shared_deadline_does_not_grant_second_source_another_45_seconds(self):
        sources=[{"key":"historical","storeRoot":"/history"},{"key":"current","storeRoot":"/current"}]
        with mock.patch.object(D.time,'monotonic',side_effect=[100,100,144]), mock.patch.object(D,'_read_host_snapshot',return_value=({}, {"revision":"a"*64})) as read, mock.patch.object(D,'_project_host_snapshot',return_value=host_fixture()):
            D.build_host(resolved_config={"engine":"engine","sources":sources})
        self.assertEqual([call.args[2] for call in read.call_args_list],[45,1])

    def test_publisher_preview_reports_per_context_counts_without_rollup(self):
        from test_publisher_setup import PublisherSetupTests
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); config,_,args=PublisherSetupTests().fixture(root)
            contexts=rows(); contexts[1].update(status='unavailable',reason='source-unavailable',host=None)
            snapshot=D.project_host_contexts(contexts)
            with mock.patch.object(D,'config',return_value=(config,{"engine":"engine"})), mock.patch.object(D.shutil,'which',return_value=D.sys.executable), mock.patch.object(D,'build_host',return_value=snapshot), mock.patch.object(D,'github') as external:
                report=D.publisher_setup(args)
            self.assertNotIn('eligible',report['counts'])
            self.assertIsNone(report['counts']['contexts'][1]['eligible'])
            self.assertIsNone(report['counts']['contexts'][1]['published'])
            external.assert_not_called()

    def test_explicit_config_routes_recurrent_reads_without_native_discovery(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory)/'config.json'
            cfg = {'schema': 'fsgg.telemetry.host-config/3', 'sources': [{'key': 'historical', 'storeRoot': '/history'}, {'key': 'current', 'storeRoot': '/current'}], 'engine': 'fsgg-coord-engine'}
            path.write_text(json.dumps(cfg)); path.chmod(0o600)
            with mock.patch.object(D.subprocess, 'run', side_effect=AssertionError('native discovery')):
                selected, result = D.config(path)
            self.assertEqual(selected, path)
            self.assertEqual(result['sources'], cfg['sources'])
            for change in (lambda v: v['sources'][1].update(storeRoot='/history'), lambda v: v['sources'][0].update(key='PRIVATE'), lambda v: v.update(workspace='PRIVATE')):
                value=copy.deepcopy(cfg); change(value); path.write_text(json.dumps(value))
                with self.assertRaises(D.HostSourceError): D.config(path)


def schema14_host():
    value=host_fixture(); value['store']['schemaVersion']=14
    value.pop('revision'); value['revision']=D.hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',', ':'),ensure_ascii=True).encode()).hexdigest()
    return value


class CollectorJoinTests(unittest.TestCase):
    def approved(self):
        from test_process_efficiency import labels
        value=labels();value['items']['PRIVATE-ORIGINAL']=value['items'].pop('PRIVATE-NATIVE')
        return value

    def test_schema14_collector_calls_one_exact_revision_export_and_reseals_safe_host(self):
        from test_efficiency_export import export
        base=schema14_host()
        with mock.patch.object(D.time,'monotonic',return_value=100),mock.patch.object(D,'engine_json',return_value=export()) as engine,mock.patch.object(D,'load_labels',return_value=self.approved()):
            value=D._attach_efficiency(base,'/PRIVATE-STORE','engine','a'*64,110,None)
        engine.assert_called_once_with('engine',['telemetry','efficiency-export','--snapshot-revision','a'*64,'--store-root','/PRIVATE-STORE','--max-items','200','--max-metrics','1000'],max_bytes=D.MAX_CANONICAL_SNAPSHOT,timeout_seconds=10)
        self.assertEqual(value['processEfficiency']['source'],'canonical-export')
        self.assertEqual(value['processEfficiency']['items'][0]['metrics'][0]['value']['numerator'],70)
        self.assertEqual(value['usage'],base['usage'])
        self.assertEqual(value['completedItems'],base['completedItems'])
        self.assertNotEqual(value['revision'],base['revision'])
        self.assertNotIn('PRIVATE',json.dumps(value));D.validate_host(value)

    def test_unsupported_endpoint_timeout_or_drift_preserves_base_work(self):
        from test_efficiency_export import export
        base=schema14_host();wrong=export();wrong['snapshotRevision']='b'*64
        for result in (D.HostSourceError('PRIVATE FAILURE'),D.HostSourceError('HOST_EFFICIENCY_DEADLINE'),wrong):
            with mock.patch.object(D,'read_efficiency_export',side_effect=result if isinstance(result,Exception) else None,return_value=result),mock.patch.object(D,'load_labels',return_value=self.approved()):
                value=D._attach_efficiency(base,'/store','engine','a'*64,110,None)
            self.assertIs(value,base)
            self.assertEqual(value['usage']['total'],140)
            self.assertEqual(value['processEfficiency']['status'],'unavailable')
            D.validate_host(value)

    def test_prior_store_has_no_efficiency_call_or_false_canonical_empty_success(self):
        base=host_fixture();base['store']['schemaVersion']=13
        with mock.patch.object(D,'read_efficiency_export') as read:
            value=D._attach_efficiency(base,'/store','engine','a'*64,110,None)
        read.assert_not_called();self.assertIs(value,base)
        self.assertEqual(value['processEfficiency']['source'],'unavailable')

    def test_two_contexts_export_independently_and_failure_does_not_hide_base(self):
        from test_efficiency_export import export
        sources=[{'key':'historical','storeRoot':'/history'},{'key':'current','storeRoot':'/current'}]
        snapshots=[({}, {'revision':'a'*64}),({}, {'revision':'b'*64})]
        with mock.patch.object(D,'_read_host_snapshot',side_effect=snapshots),mock.patch.object(D,'_project_host_snapshot',side_effect=[schema14_host(),schema14_host()]),mock.patch.object(D,'read_efficiency_export',side_effect=[export(),D.HostSourceError('PRIVATE')]) as read,mock.patch.object(D,'load_labels',return_value=self.approved()):
            value=D.build_host(resolved_config={'engine':'engine','sources':sources})
        self.assertEqual([call.args[:3] for call in read.call_args_list],[('/history','engine','a'*64),('/current','engine','b'*64)])
        self.assertEqual(read.call_args_list[0].args[3],read.call_args_list[1].args[3])
        self.assertTrue(all(row['status']=='ready' for row in value['contexts']))
        self.assertEqual(value['contexts'][0]['host']['processEfficiency']['source'],'canonical-export')
        self.assertEqual(value['contexts'][1]['host']['processEfficiency']['source'],'unavailable')
        self.assertEqual(value['contexts'][1]['host']['usage']['total'],140)
        self.assertLessEqual(len(D.dump(value)),D.MAX_JSON);D.validate_host(value)

    def test_projection_receives_remaining_host_budget(self):
        from test_efficiency_export import export
        base=schema14_host()
        with mock.patch.object(D,'read_efficiency_export',return_value=export()),mock.patch.object(D,'load_labels',return_value=self.approved()),mock.patch.object(D,'project_efficiency_exports',wraps=D.project_efficiency_exports) as project:
            value=D._attach_efficiency(base,'/store','engine','a'*64,110,None)
        self.assertLess(project.call_args.kwargs['max_bytes'],D.MAX_JSON)
        self.assertLessEqual(len(D.dump(value)),D.MAX_JSON)


if __name__ == '__main__': unittest.main()
