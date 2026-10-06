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
        with mock.patch.object(D, '_read_host_snapshot', side_effect=[D.HostSourceError('PRIVATE FAILURE'), ({}, {})]), mock.patch.object(D, '_project_host_snapshot', return_value=host_fixture()):
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
        with mock.patch.object(D.time,'monotonic',side_effect=[100,100,144]), mock.patch.object(D,'_read_host_snapshot',return_value=({},{})) as read, mock.patch.object(D,'_project_host_snapshot',return_value=host_fixture()):
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


if __name__ == '__main__': unittest.main()
