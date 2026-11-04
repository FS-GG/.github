"""Small exploratory-analysis controls; no historical text or identities."""
import importlib.util
from pathlib import Path
import unittest
import numpy as np
spec=importlib.util.spec_from_file_location('history_explore',Path(__file__).resolve().parents[2]/'scripts/learn-history-explore.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class ExplorationTests(unittest.TestCase):
    def test_zero_variance_is_unknown_not_zero_effect(self):
        rows=[{'baselineDimensions':{'D8':3},'seconds':i} for i in range(20)]
        got=m.pair(rows,'D8','seconds')
        self.assertEqual(got['xUnique'],1);self.assertIsNone(got['spearman'])
    def test_missing_is_not_zero(self):
        rows=[{'H':None,'seconds':100},{'H':1,'seconds':3},{'H':2,'seconds':4},{'H':3,'seconds':5}]
        got=m.pair(rows,'H','seconds');self.assertEqual(got['n'],3);self.assertEqual(got['missing'],1)
    def test_feature_text_transform(self):
        self.assertEqual(m.feature({'baselineTextWords':0},'log1pTextWords'),0)
        self.assertIsNone(m.feature({},'log1pTextWords'))
    def test_ridge_training_center_does_not_use_test_outcome(self):
        train=[{'H':i,'responseEnvelopeUnionSeconds':i*i+1} for i in range(10)]
        test=[{'H':11,'responseEnvelopeUnionSeconds':3}]
        a=m.ridge_predict(train,test,['H'])
        test[0]['responseEnvelopeUnionSeconds']=10**20
        np.testing.assert_array_equal(a,m.ridge_predict(train,test,['H']))
    def test_constant_column_regularized_without_nan(self):
        train=[{'H':2,'responseEnvelopeUnionSeconds':i+1} for i in range(10)]
        self.assertTrue(np.isfinite(m.ridge_predict(train,[{'H':2}],['H'])).all())
    def test_group_evaluation_refuses_tiny_group_count(self):
        rows=[]
        self.assertIn('insufficient',m.grouped_prediction(rows)['status'])
    def test_group_models_include_median_and_influence(self):
        rows=[]
        for g in range(7):
            for i in range(g+1):
                rows.append(dict(H=g+i,baselineDimensions={'D'+str(k):k%4 for k in range(1,9)},baselineTextWords=10+i,
                    baselineTextCharacters=50+i,baselineSafetyFloor=False,baselineBoundary=f'2026-01-{g+1:02d}T00:00:00Z',
                    cluster=str(g),responseEnvelopeUnionSeconds=10+g+i))
        got=m.grouped_prediction(rows)
        self.assertIn('intercept_median',got['models'])
        self.assertEqual(len(got['models']['H']['folds']),7)
        self.assertEqual(got['leaveLargestParentSensitivity']['removedRows'],7)
        self.assertEqual(got['leaveLargestParentSensitivity']['evaluation']['n'],21)

    def test_strata_keep_open_separate(self):
        rows=[{'stratum':'a','state':'closed','calendarSeconds':2},{'stratum':'a','state':'open','calendarSeconds':100}]
        got=m.aggregate_strata(rows,'stratum',minimum=1)[0]
        self.assertEqual(got['closedCalendar']['secondsQuantiles']['max'],2)
        self.assertEqual(got['openAge']['secondsQuantiles']['max'],100)
if __name__=='__main__':unittest.main()
