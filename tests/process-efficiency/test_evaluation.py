#!/usr/bin/env python3
"""Synthetic predictions exercise evaluator math, never establish live calibration."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode=True

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('eff_evaluate',ROOT/'tools/process-efficiency-evaluate.py')
E=importlib.util.module_from_spec(spec);spec.loader.exec_module(E)
CORPUS_BYTES=(ROOT/'tests/process-efficiency/labeled-corpus-v1.json').read_bytes()
CORPUS=json.loads(CORPUS_BYTES)
CORPUS_DIGEST='sha256:'+E.hashlib.sha256(CORPUS_BYTES).hexdigest()
FIXTURES=json.loads((ROOT/'tests/process-efficiency/metric-fixtures-v1.json').read_text())
EPISODES={row['id']:row for row in CORPUS['episodes']}


def inputs(ids):
    """Explicit test-only predictions, independent synthetic truth bindings and metric truth."""
    predictions={'schema':E.INPUT_SCHEMA,'origin':'synthetic-test','corpusSha256':CORPUS_DIGEST,'records':[],'metrics':[copy.deepcopy(FIXTURES['metricSamples'][0])]}
    evidence={'schema':'fsgg.telemetry.efficiency-evidence-truth/1','records':[],'bindings':[],'snapshots':[]}
    for episode_id in ids:
        episode=EPISODES[episode_id]
        assessment=copy.deepcopy(FIXTURES['assessmentSamples'][1])
        assessment.update(assessmentId='test-'+episode_id,evidenceDigest='sha256:'+'a'*64)
        assessment['provenance']['producer']='explicit-synthetic-test'
        ref={'id':episode['facts'][0]['id'],'kind':'complication','revision':episode['facts'][0]['revision']}
        assessment['evidenceRefs']=[ref]
        expected=episode['expected']
        finding={key:copy.deepcopy(expected[key]) for key in ['activity','purpose','trigger','primaryCause','contributingCauses','necessity','epistemicStatus','alternative']}
        finding.update(id=episode_id,summary='Explicit synthetic test prediction.',severity='minor',evidenceRefs=[ref['id']],affectedObjects=[],recoveryRefs=[],uncertainty='')
        assessment['findings']=[finding]
        predictions['records'].append({'episodeId':episode_id,'assessment':assessment,'assessmentSha256':E.digest(assessment),'findingId':episode_id,'sourceRefs':[{**ref,'contentDigest':'sha256:'+'b'*64}]})
        content='sha256:'+'b'*64
        evidence['records'].append({'sourceRef':copy.deepcopy(ref),'contentDigest':content})
        evidence['bindings'].append({'episodeId':episode_id,'episodeEvidenceId':episode['facts'][0]['id'],'sourceRef':copy.deepcopy(ref),'contentDigest':content})
        evidence['snapshots'].append({key:copy.deepcopy(assessment[key]) for key in ['assessmentId','revision','subject','evidenceDigest']})
    numeric={'schema':'fsgg.telemetry.efficiency-metric-truth/1','metrics':copy.deepcopy(predictions['metrics'])}
    return predictions,evidence,numeric


def refresh(record):record['assessmentSha256']=E.digest(record['assessment'])


def report(predictions,evidence=None,numeric=None,split='development'):
    return E.evaluate(CORPUS,CORPUS_DIGEST,predictions,split=split,evidence_truth=evidence,metric_truth=numeric)


class EvaluationTests(unittest.TestCase):
    def test_missing_predictions_never_create_zero_accuracy_or_abstentions(self):
        predictions,evidence,numeric=inputs([])
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['population']['expectedEpisodes'],30)
        self.assertEqual(result['population']['missingEpisodes'],30)
        self.assertEqual(result['cause']['supportedPrecision']['denominator'],0)
        self.assertIsNone(result['cause']['supportedPrecision']['value'])
        self.assertIsNone(result['cause']['abstention']['value'])
        self.assertEqual(result['qualification']['verdict'],'not-established')

    def test_known_classification_counts_denominators_and_wilson_uncertainty(self):
        predictions,evidence,numeric=inputs(['clean-delivery-01','useful-failure-01','avoidable-replay-01','unknown-retry-01'])
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['population']['scoredEpisodes'],4)
        self.assertEqual(result['cause']['supportedPrecision']['numerator'],2)
        self.assertEqual(result['cause']['supportedPrecision']['denominator'],2)
        self.assertEqual(result['cause']['abstention']['value'],0.5)
        self.assertEqual(result['avoidability']['precision']['value'],1)
        self.assertEqual(result['avoidability']['recall']['value'],1)
        self.assertEqual(result['references']['validity']['value'],1)
        self.assertEqual(result['numericAccuracy']['exactCorrespondence']['denominator'],1)
        self.assertEqual(result['numericAccuracy']['exactCorrespondence']['value'],1)
        interval=result['cause']['supportedPrecision']['wilson95']
        self.assertLess(interval[0],0.5)
        self.assertLess(result['avoidability']['positivePopulationCoverage']['value'],1)
        self.assertEqual(result['familyCoverage']['useful-failure']['coverage']['denominator'],3)

    def test_unsupported_cause_and_false_avoidable_claim_are_false_positives(self):
        predictions,evidence,numeric=inputs(['changed-input-rerun-01','useful-failure-01'])
        first=predictions['records'][0]['assessment']['findings'][0];first['primaryCause']='requirements'
        second=predictions['records'][1]['assessment']['findings'][0]
        second.update(purpose='avoidable-process',necessity='avoidable',alternative='Synthetic proposed alternative.')
        for record in predictions['records']:refresh(record)
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['cause']['confusion']['unknown']['requirements'],1)
        self.assertEqual(result['cause']['supportedPrecision']['value'],0.5)
        self.assertEqual(result['avoidability']['falsePositive'],1)
        self.assertEqual(result['avoidability']['precision']['value'],0)

    def test_hypothesis_cannot_claim_supported_primary_cause(self):
        predictions,evidence,numeric=inputs(['useful-failure-01'])
        record=predictions['records'][0];record['assessment']['findings'][0]['epistemicStatus']='hypothesis';refresh(record)
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['population']['rejectedEpisodes'],1)
        self.assertIsNone(result['cause']['supportedPrecision']['value'])

    def test_assessment_bytes_and_snapshot_subject_digest_joins_fail_closed(self):
        predictions,evidence,numeric=inputs(['clean-delivery-01'])
        predictions['records'][0]['assessment']['outcomeSynopsis']='Tampered.'
        self.assertEqual(report(predictions,evidence,numeric)['population']['rejectedEpisodes'],1)
        refresh(predictions['records'][0])
        evidence['snapshots'][0]['evidenceDigest']='sha256:'+'c'*64
        result=report(predictions,evidence,numeric)
        self.assertIn('snapshot join',result['rejections'][0]['reason'])

    def test_exact_reference_revision_and_episode_binding_required(self):
        predictions,evidence,numeric=inputs(['useful-failure-01'])
        record=predictions['records'][0];record['assessment']['evidenceRefs'][0]['revision']+=1;refresh(record)
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['references']['validity']['numerator'],0)
        self.assertEqual(result['references']['validity']['denominator'],1)
        self.assertEqual(result['population']['rejectedEpisodes'],1)
        evidence['bindings'][0]['contentDigest']='sha256:'+'c'*64
        with self.assertRaises(ValueError):report(predictions,evidence,numeric)

    def test_exact_packet_reference_digest_evidence_and_recovery(self):
        predictions,evidence,numeric=inputs(['useful-failure-01'])
        record=predictions['records'][0]
        record['sourceRefs'][0]['contentDigest']='sha256:'+'c'*64
        # Same identity/revision and rehashed output still cannot authenticate tampered packet bytes.
        refresh(record)
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['references']['validity']['value'],0)
        self.assertEqual(result['population']['rejectedEpisodes'],1)
        record['sourceRefs'][0]['contentDigest']='sha256:'+'b'*64
        recovery={'id':'recovery-a','kind':'activity','revision':0}
        record['assessment']['evidenceRefs'].append(recovery)
        record['assessment']['findings'][0]['recoveryRefs']=['recovery-a']
        record['sourceRefs'].append({**recovery,'contentDigest':'sha256:'+'c'*64})
        evidence['records'].append({'sourceRef':recovery,'contentDigest':'sha256:'+'d'*64})
        refresh(record)
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['references']['validity']['numerator'],1)
        self.assertEqual(result['references']['validity']['denominator'],2)
        self.assertEqual(result['population']['rejectedEpisodes'],1)
        # No independent truth means unverified, even with a supplied commitment.
        result=report(predictions)
        self.assertEqual(result['references']['status'],'unknown-independent-truth-missing')
        self.assertIsNone(result['references']['validity']['value'])
        record['sourceRefs']=[]
        result=report(predictions)
        self.assertIsNone(result['references']['validity']['value'])

    def test_truth_embedded_source_digest_must_match_declared_digest(self):
        predictions,evidence,numeric=inputs(['useful-failure-01'])
        for group in ['records','bindings']:
            bad=copy.deepcopy(evidence)
            bad[group][0]['sourceRef']['contentDigest']='sha256:'+'c'*64
            with self.assertRaises(ValueError):report(predictions,bad,numeric)
        evidence['records'][0]['sourceRef']['contentDigest']=evidence['records'][0]['contentDigest']
        evidence['bindings'][0]['sourceRef']['contentDigest']=evidence['bindings'][0]['contentDigest']
        self.assertEqual(report(predictions,evidence,numeric)['references']['validity']['value'],1)

    def test_date_time_checker_rejects_malformed_and_invalid_calendar_values(self):
        schema={'type':'string','format':'date-time'}
        E.check_shape('2026-10-06T12:00:00Z',schema)
        for value in ['not-a-date','2026-02-30T12:00:00Z']:
            with self.subTest(value=value), self.assertRaises(ValueError):
                E.check_shape(value,schema)

    def test_missing_required_date_time_checker_refuses_validation(self):
        from jsonschema import FormatChecker
        E.compiled_validator.cache_clear()
        try:
            with patch.dict(FormatChecker.checkers,clear=True):
                with self.assertRaisesRegex(ValueError,'Required date-time format checker unavailable'):
                    E.check_shape('2026-10-06T12:00:00Z',{'type':'string','format':'date-time'})
        finally:
            E.compiled_validator.cache_clear()

    def test_full_draft_validator_enforces_composed_schema_keywords(self):
        # A future schema keyword must be enforced rather than ignored by a fixture subset.
        schema={'$schema':'https://json-schema.org/draft/2020-12/schema','type':'integer','not':{'const':2}}
        E.check_shape(1,schema)
        with self.assertRaises(ValueError):E.check_shape(2,schema)

    def test_numeric_quantity_scope_and_unknown_truth_accuracy(self):
        predictions,evidence,numeric=inputs(['clean-delivery-01'])
        predictions['metrics'][0]['value']['numerator']=101
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['numericAccuracy']['exactCorrespondence']['value'],0)
        predictions['metrics'][0]['value']['numerator']=100
        predictions['metrics'][0]['population']['cutoff']='2026-10-06T00:30:00Z'
        self.assertEqual(report(predictions,evidence,numeric)['numericAccuracy']['exactCorrespondence']['value'],0)
        predictions['metrics'][0]['population']['cutoff']=numeric['metrics'][0]['population']['cutoff']
        predictions['metrics'][0]['value']['unknownAmount']=50
        self.assertEqual(report(predictions,evidence,numeric)['numericAccuracy']['exactCorrespondence']['value'],0)
        numeric['metrics'][0]['value'].update(status='unknown',numerator=None,unknownAmount=None,reason='Missing authoritative counters.')
        self.assertEqual(report(predictions,evidence,numeric)['numericAccuracy']['exactCorrespondence']['value'],0)

    def test_equivalent_rational_numbers_match_without_rounding(self):
        predictions,evidence,numeric=inputs(['clean-delivery-01'])
        for metric in [predictions['metrics'][0],numeric['metrics'][0]]:
            metric.update(metric='avoidable-share',unit='ratio')
        predictions['metrics'][0]['value'].update(numerator=2,denominator=6)
        numeric['metrics'][0]['value'].update(numerator=1,denominator=3)
        self.assertEqual(report(predictions,evidence,numeric)['numericAccuracy']['exactCorrespondence']['value'],1)
        predictions['records'][0]['assessment']['metricRefs']=['not-in-canonical-truth'];refresh(predictions['records'][0])
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['numericAccuracy']['exactCorrespondence']['value'],0)
        self.assertEqual(result['numericAccuracy']['unresolvedReferences'],1)

    def test_missing_truth_is_unknown_not_verified_reference_or_numeric_success(self):
        predictions,_,_=inputs(['useful-failure-01'])
        result=report(predictions)
        self.assertEqual(result['references']['status'],'unknown-independent-truth-missing')
        self.assertIsNone(result['references']['validity']['value'])
        self.assertIsNone(result['numericAccuracy']['exactCorrespondence']['value'])
        self.assertEqual(result['numericAccuracy']['unknownQuantities'],1)

    def test_duplicate_observation_replay_not_extra_prediction_and_conflict_refuses(self):
        predictions,evidence,numeric=inputs(['useful-failure-01'])
        predictions['records'].append(copy.deepcopy(predictions['records'][0]))
        result=report(predictions,evidence,numeric)
        self.assertEqual(result['population']['scoredEpisodes'],1)
        self.assertEqual(result['population']['duplicateObservationReplays'],1)
        predictions['records'][1]['findingId']='conflicting'
        with self.assertRaises(ValueError):report(predictions,evidence,numeric)

    def test_split_is_explicit_and_heldout_predictions_are_separately_supplied(self):
        predictions,evidence,numeric=inputs(['useful-failure-01','useful-failure-04'])
        result=report(predictions,evidence,numeric,split='held-out')
        self.assertEqual(result['population']['scoredEpisodes'],1)
        self.assertEqual(result['population']['otherSplitEpisodes'],1)
        self.assertEqual(result['predictionOrigin'],'synthetic-test')
        with self.assertRaises(ValueError):report(predictions,split='all')
        predictions['corpusSha256']='sha256:'+'0'*64
        with self.assertRaises(ValueError):report(predictions,split='held-out')

    def test_input_bounds_duplicate_keys_unknown_episode_and_nonfinite_numbers(self):
        predictions,evidence,numeric=inputs(['useful-failure-01'])
        predictions['records'][0]['episodeId']='not-in-frozen-corpus'
        with self.assertRaises(ValueError):report(predictions,evidence,numeric)
        with self.assertRaises(ValueError):E.bounded_rows([None]*201)
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'input.json'
            for payload in ['{"key":1,"key":2}','{"value":NaN}']:
                path.write_text(payload)
                with self.assertRaises(ValueError):E.read_json(path)
            path.write_bytes(b' '*(E.MAX_BYTES+1))
            with self.assertRaises(ValueError):E.read_json(path)

    def test_cli_uses_only_supplied_files_and_never_overwrites_report(self):
        predictions,evidence,numeric=inputs(['useful-failure-01'])
        with tempfile.TemporaryDirectory() as directory:
            directory=Path(directory)
            for name,value in [('predictions',predictions),('evidence',evidence),('metrics',numeric)]:
                (directory/(name+'.json')).write_text(json.dumps(value))
            output=directory/'report.json'
            command=[sys.executable,str(ROOT/'tools/process-efficiency-evaluate.py'),'--predictions',str(directory/'predictions.json'),'--evidence-truth',str(directory/'evidence.json'),'--metric-truth',str(directory/'metrics.json'),'--split','development','--output',str(output)]
            completed=subprocess.run(command,capture_output=True,text=True,timeout=10)
            self.assertEqual(completed.returncode,0,completed.stderr)
            result=json.loads(output.read_text());self.assertEqual(result['population']['scoredEpisodes'],1)
            self.assertEqual(subprocess.run(command,capture_output=True,text=True,timeout=10).returncode,2)

if __name__=='__main__':unittest.main(verbosity=2)
