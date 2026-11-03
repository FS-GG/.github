"""Synthetic duration and dependence controls; use pinned statistics environment."""
import importlib.util
from pathlib import Path
import unittest
spec=importlib.util.spec_from_file_location('history_stats',Path(__file__).resolve().parents[2]/'scripts/learn-history-statistics.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class StatisticsTests(unittest.TestCase):
    def test_union_counts_concurrency_once(self):
        self.assertEqual(m.union_seconds([(0,10),(5,20),(30,40)]),30)
    def test_negative_interval_refused(self):
        with self.assertRaises(ValueError):m.union_seconds([(3,2)])
    def test_gap_cap_keeps_late_timestamp_and_deduplicates(self):
        times=['2026-01-01T00:00:00Z','2026-01-01T00:00:00Z','2026-01-01T04:00:00Z']
        self.assertEqual(m.union_seconds(m.intervals(times)),14400)
        self.assertEqual(m.union_seconds(m.intervals(times,3600)),3600)
    def test_single_timestamp_does_not_invent_duration(self):
        self.assertEqual(m.union_seconds(m.intervals(['2026-01-01T00:00:00Z'])),0)
    def test_constant_hardness_not_zero_correlation(self):
        self.assertIsNone(m.coefficient([1,1,1],[1,2,3],'spearman'))
    def test_tail_retained(self):
        self.assertEqual(m.describe([1,2,3,10**8])['secondsQuantiles']['max'],10**8)
    def test_cluster_guard_not_response_count(self):
        rows=[dict(H=i,seconds=i,cluster='same') for i in range(100)]
        got=m.association(rows,'seconds',1,100)
        self.assertEqual(got['clusters'],1)
        self.assertIn('not estimated',got['clusterBootstrap']['status'])
    def test_cluster_bootstrap_reproducible(self):
        rows=[dict(H=i,seconds=i*i,cluster=str(i)) for i in range(12)]
        self.assertEqual(m.association(rows,'seconds',1,100),m.association(rows,'seconds',1,100))
    def test_disjoint_hardness_support_refuses_interaction(self):
        rows=[dict(H=i,workerEnvelopeUnionSeconds=i+1,workerProfile='a' if i<5 else 'b',cluster=str(i)) for i in range(10)]
        self.assertIn('not estimable',m.profile_interaction(rows,1,100)['status'])
if __name__=='__main__':unittest.main()
