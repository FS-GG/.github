#!/usr/bin/env python3
"""Bounded offline V2-EFF evaluator; consumes predictions, never generates or tunes them.

Required actual-run inputs: frozen corpus bytes; separately retained analyst assessment records
with episode/finding joins and assessment digests; independent evidence-binding truth; canonical
metric truth when checking numeric correspondence. Reference checks establish supplied-byte joins,
not receipt authenticity, canonical admission, causal truth or economic success.
"""
from __future__ import annotations
import argparse
import datetime as dt
from fractions import Fraction
import hashlib
import json
import math
from pathlib import Path
import re
import sys
from functools import lru_cache

ROOT = Path(__file__).resolve().parents[1]
MAX_BYTES = 2 * 1024 * 1024
MAX_RECORDS = 200
MAX_METRICS = 1000
MAX_EVIDENCE = 4096
INPUT_SCHEMA = 'fsgg.telemetry.efficiency-evaluation-input/1'
REPORT_SCHEMA = 'fsgg.telemetry.efficiency-evaluation-report/1'
SUPPORTED = {'observed', 'supported-inference'}


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True, allow_nan=False).encode()


def digest(value):
    return 'sha256:' + hashlib.sha256(canonical(value)).hexdigest()


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate JSON key')
        result[key] = value
    return result


def read_json(path):
    with Path(path).open('rb') as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError('input exceeds byte bound')
    value = json.loads(raw, object_pairs_hook=no_duplicates, parse_constant=lambda _: (_ for _ in ()).throw(ValueError('non-finite number')))
    return value, 'sha256:' + hashlib.sha256(raw).hexdigest()


def exact_keys(value, keys):
    if not isinstance(value, dict) or set(value) != set(keys):
        raise ValueError('unexpected or missing fields')


def bounded_rows(value, maximum=MAX_RECORDS):
    if not isinstance(value, list) or len(value) > maximum:
        raise ValueError('record bound or type')
    return value


def ref_key(value):
    if not isinstance(value, dict) or not {'id', 'kind', 'revision'} <= value.keys():
        raise ValueError('malformed source reference')
    if not isinstance(value['id'], str) or not isinstance(value['kind'], str) or type(value['revision']) is not int or value['revision'] < 0:
        raise ValueError('invalid source identity/revision')
    if set(value)-{'id','kind','revision','contentDigest'}:
        raise ValueError('unexpected source-reference field')
    if 'contentDigest' in value and not re.fullmatch(r'sha256:[a-f0-9]{64}',value['contentDigest']):
        raise ValueError('invalid source-reference content digest')
    return value['id'], value['kind'], value['revision']


@lru_cache(maxsize=4)
def compiled_validator(schema_bytes):
    # Reuse the already selected .3/CI jsonschema dependency; never silently fall back.
    try:
        from jsonschema import Draft202012Validator, FormatChecker
    except ImportError as error:
        raise ValueError('Selected jsonschema dependency unavailable; use the qualified .3/CI Python environment.') from error
    schema=json.loads(schema_bytes)
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema,format_checker=FormatChecker())


def check_shape(value, schema):
    validator=compiled_validator(canonical(schema))
    failures=sorted(validator.iter_errors(value),key=lambda error:tuple(str(part) for part in error.path))
    if failures:
        path='.'.join(str(part) for part in failures[0].path)
        raise ValueError('Frozen Draft202012 schema rejected field '+(path or '<root>'))


def rate(numerator, denominator):
    """Wilson 95% binomial interval; small samples keep wide uncertainty explicit."""
    if not denominator:
        return {'numerator':numerator, 'denominator':denominator, 'value':None, 'wilson95':None}
    z = 1.959963984540054
    p = numerator / denominator
    divisor = 1 + z*z/denominator
    center = (p + z*z/(2*denominator))/divisor
    width = z*math.sqrt(p*(1-p)/denominator + z*z/(4*denominator*denominator))/divisor
    return {'numerator':numerator, 'denominator':denominator, 'value':p, 'wilson95':[max(0,center-width),min(1,center+width)]}


def numeric_value(metric):
    value = metric['value']
    if value['status'] in {'unknown', 'not-applicable'}:
        return None
    if value['numerator'] is None:
        raise ValueError('known metric lacks numerator')
    if metric['unit'] == 'ratio' and (value['denominator'] is None or value['denominator'] <= 0):
        raise ValueError('known ratio lacks denominator')
    return Fraction(value['numerator'],value['denominator'] or 1)


def evidence_index(truth, episode_ids):
    if truth is None:
        return None, {}, {}, None
    exact_keys(truth, {'schema','records','bindings','snapshots'})
    if truth['schema'] != 'fsgg.telemetry.efficiency-evidence-truth/1':
        raise ValueError('unsupported evidence truth')
    records = {}
    for row in bounded_rows(truth['records'],MAX_EVIDENCE):
        exact_keys(row, {'sourceRef','contentDigest'})
        key = ref_key(row['sourceRef'])
        if not re.fullmatch(r'sha256:[a-f0-9]{64}',row['contentDigest']):
            raise ValueError('invalid evidence content digest')
        if 'contentDigest' in row['sourceRef'] and row['sourceRef']['contentDigest']!=row['contentDigest']:
            raise ValueError('contradictory evidence truth source digest')
        if key in records:
            raise ValueError('duplicate evidence truth identity')
        records[key] = row['contentDigest']
    bindings = {}
    for row in bounded_rows(truth['bindings']):
        exact_keys(row, {'episodeId','episodeEvidenceId','sourceRef','contentDigest'})
        if row['episodeId'] not in episode_ids:
            raise ValueError('evidence binding episode absent from frozen corpus')
        key = ref_key(row['sourceRef'])
        if 'contentDigest' in row['sourceRef'] and row['sourceRef']['contentDigest']!=row['contentDigest']:
            raise ValueError('contradictory evidence binding source digest')
        if records.get(key) != row['contentDigest']:
            raise ValueError('evidence binding digest/reference mismatch')
        join = row['episodeId'],row['episodeEvidenceId']
        if join in bindings:
            raise ValueError('duplicate episode evidence binding')
        bindings[join] = key
    snapshots = {}
    for row in bounded_rows(truth['snapshots']):
        exact_keys(row, {'assessmentId','revision','subject','evidenceDigest'})
        key = row['assessmentId'],row['revision']
        if key in snapshots:raise ValueError('duplicate assessment snapshot truth')
        snapshots[key] = row
    return records, bindings, snapshots, digest(truth)


def evaluate(corpus, corpus_digest, predictions, *, split, evidence_truth=None, metric_truth=None, assessment_schema=None, metric_schema=None):
    if split not in {'development','held-out'}:
        raise ValueError('explicit development or held-out split required')
    if corpus.get('schema') != 'fsgg.telemetry.efficiency-corpus/1':
        raise ValueError('unsupported frozen corpus')
    all_episodes = bounded_rows(corpus.get('episodes'))
    episodes = {row['id']:row for row in all_episodes}
    if len(episodes) != len(all_episodes):
        raise ValueError('duplicate corpus episode')
    selected = {key:row for key,row in episodes.items() if row['split'] == split}
    exact_keys(predictions, {'schema','origin','corpusSha256','records','metrics'})
    if predictions['schema'] != INPUT_SCHEMA or predictions['origin'] not in {'actual-analyst','synthetic-test'}:
        raise ValueError('unsupported predictions or missing explicit provenance')
    if predictions['corpusSha256'] != corpus_digest:
        raise ValueError('frozen corpus byte digest mismatch')
    if assessment_schema is None:
        assessment_schema = json.loads((ROOT/'contracts/process-efficiency/assessment-v1.schema.json').read_text())
    if metric_schema is None:
        metric_schema = json.loads((ROOT/'contracts/process-efficiency/metric-v1.schema.json').read_text())
    source_records, bindings, snapshots, evidence_digest = evidence_index(evidence_truth,episodes)
    for (episode_id,fact_id) in bindings:
        if fact_id not in {fact['id'] for fact in episodes[episode_id]['facts']}:
            raise ValueError('binding is not a frozen episode fact')
    metrics = {}
    for metric in bounded_rows(predictions['metrics'],MAX_METRICS):
        check_shape(metric,metric_schema)
        if metric['metricId'] in metrics:
            raise ValueError('duplicate observed metric identity')
        numeric_value(metric)
        metrics[metric['metricId']] = metric
    truths = None
    if metric_truth is not None:
        exact_keys(metric_truth, {'schema','metrics'})
        if metric_truth['schema'] != 'fsgg.telemetry.efficiency-metric-truth/1':
            raise ValueError('unsupported numeric truth')
        truths = {}
        for metric in bounded_rows(metric_truth['metrics'],MAX_METRICS):
            check_shape(metric,metric_schema); numeric_value(metric)
            if metric['metricId'] in truths:
                raise ValueError('duplicate numeric truth identity')
            truths[metric['metricId']] = metric
    records = {}
    replays = 0
    for record in bounded_rows(predictions['records']):
        exact_keys(record, {'episodeId','assessment','assessmentSha256','findingId','sourceRefs'})
        episode_id = record['episodeId']
        if episode_id not in episodes:
            raise ValueError('prediction episode absent from frozen corpus')
        if episode_id in records:
            if canonical(records[episode_id]) != canonical(record):
                raise ValueError('conflicting predictions for one episode; choose an explicit revision')
            replays += 1
        records[episode_id] = record
    family = {name:{'expected':0,'submitted':0,'scored':0,'supportedCauseClaims':0,'abstentions':0} for name in sorted({row['family'] for row in selected.values()})}
    for row in selected.values():family[row['family']]['expected'] += 1
    confusion = {}
    rejected = []
    scored = supported_claims = correct_causes = cause_expected = abstentions = 0
    avoid_tp = avoid_fp = avoid_fn = avoid_tn = 0
    reference_checked = reference_valid = 0
    numeric_checked = numeric_correct = numeric_unknown = numeric_unresolved = 0
    metric_seen = set()
    for episode_id, record in records.items():
        if episode_id not in selected:
            continue
        episode = selected[episode_id]
        stats = family[episode['family']]; stats['submitted'] += 1
        try:
            assessment = record['assessment']
            if digest(assessment) != record['assessmentSha256']:
                raise ValueError('assessment digest mismatch')
            check_shape(assessment,assessment_schema)
            if assessment['provenance']['validationResult'] != 'accepted':
                raise ValueError('analyst output not validated accepted')
            if assessment['subject']['outcomeEpoch'] is None and assessment['lifecycle']['state'] not in {'pending','partial'}:
                raise ValueError('unknown epoch cannot claim final assessment')
            if assessment['subject']['scope']=='provisional-delivery' and (assessment['lifecycle']['itemReviewRef'] is not None or assessment['lifecycle']['state']=='ready'):
                raise ValueError('provisional output claims native review')
            if source_records is not None:
                snapshot=snapshots.get((assessment['assessmentId'],assessment['revision']))
                if snapshot is None or snapshot['subject']!=assessment['subject'] or snapshot['evidenceDigest']!=assessment['evidenceDigest']:
                    raise ValueError('assessment subject/evidence snapshot join mismatch')
            findings = [row for row in assessment['findings'] if row['id'] == record['findingId']]
            if len(findings) != 1:
                raise ValueError('finding join missing or ambiguous')
            finding = findings[0]
            supported = finding['epistemicStatus'] in SUPPORTED
            if finding['primaryCause'] != 'unknown' and not supported:
                raise ValueError('unsupported primary-cause assertion')
            if supported and not finding['evidenceRefs']:
                raise ValueError('supported finding missing evidence')
            if supported and finding['necessity']=='avoidable' and not finding['alternative']:
                raise ValueError('supported avoidability missing feasible alternative')
            commitments={}
            for commitment in bounded_rows(record['sourceRefs']):
                exact_keys(commitment,{'id','kind','revision','contentDigest'})
                key=ref_key(commitment)
                if key in commitments:
                    raise ValueError('ambiguous packet source commitment')
                commitments[key]=commitment['contentDigest']
            refs = {}
            for ref in assessment['evidenceRefs']:
                if ref['id'] in refs:
                    raise ValueError('ambiguous evidence ID')
                refs[ref['id']] = ref
            expected_keys = {bindings[(episode_id,fact)] for fact in episode['expected']['evidenceRefs'] if (episode_id,fact) in bindings}
            reference_ok = True
            for name in finding['evidenceRefs'] + finding['recoveryRefs']:
                if source_records is not None:
                    reference_checked += 1
                ref = refs.get(name)
                if ref is None:
                    reference_ok = False
                    continue
                key = ref_key(ref)
                if source_records is not None:
                    valid = key in source_records and commitments.get(key)==source_records[key] and (name in finding['recoveryRefs'] or key in expected_keys)
                    reference_valid += valid
                    reference_ok &= valid
            if source_records is not None and not reference_ok:
                raise ValueError('broken exact episode evidence reference')
            # Numeric correspondence uses independent canonical metric truth, not model prose parsing.
            for metric_id in assessment['metricRefs']:
                if metric_id in metric_seen:
                    continue
                metric_seen.add(metric_id)
                observed = metrics.get(metric_id)
                truth = truths.get(metric_id) if truths is not None else None
                if truths is None:
                    numeric_unknown += 1
                    continue
                if observed is None or truth is None:
                    numeric_checked += 1; numeric_unresolved += 1
                    continue
                expected_quantity = numeric_value(truth); quantity = numeric_value(observed)
                if expected_quantity is None and quantity is not None:
                    numeric_checked += 1
                    continue
                if expected_quantity is None or quantity is None:
                    numeric_unknown += 1
                    continue
                numeric_checked += 1
                # Population/cutoff/source revision and unit must agree as well as rational quantity.
                joins = ['metric','unit','purpose','healthDimension','calculationVersion','population','sourceRefs','policyProfile','price','coverage']
                numeric_correct += quantity==expected_quantity and all(observed[key]==truth[key] for key in joins) and observed['value']['status']==truth['value']['status'] and observed['value']['unknownAmount']==truth['value']['unknownAmount']
            scored += 1; stats['scored'] += 1
            expected_cause = episode['expected']['primaryCause']
            predicted_cause = finding['primaryCause'] if supported else 'unknown'
            confusion.setdefault(expected_cause,{}).setdefault(predicted_cause,0)
            confusion[expected_cause][predicted_cause] += 1
            cause_expected += expected_cause != 'unknown'
            if predicted_cause == 'unknown':
                abstentions += 1; stats['abstentions'] += 1
            else:
                supported_claims += 1; stats['supportedCauseClaims'] += 1
                correct_causes += predicted_cause == expected_cause
            predicted_avoidable = supported and finding['necessity']=='avoidable' and finding['purpose']=='avoidable-process' and bool(finding['alternative'])
            expected_avoidable = episode['expected']['measuredAvoidable']
            avoid_tp += predicted_avoidable and expected_avoidable
            avoid_fp += predicted_avoidable and not expected_avoidable
            avoid_fn += not predicted_avoidable and expected_avoidable
            avoid_tn += not predicted_avoidable and not expected_avoidable
        except (ValueError,KeyError,TypeError) as error:
            rejected.append({'episodeId':episode_id,'reason':str(error)[:256]})
    for stats in family.values():
        stats['coverage'] = rate(stats['scored'],stats['expected'])
    return {'schema':REPORT_SCHEMA,'split':split,'predictionOrigin':predictions['origin'],'corpusSha256':corpus_digest,'predictionsSha256':digest(predictions),'evidenceTruthSha256':evidence_digest,'metricTruthSha256':digest(metric_truth) if metric_truth is not None else None,
        'population':{'expectedEpisodes':len(selected),'submittedEpisodes':sum(row['submitted'] for row in family.values()),'scoredEpisodes':scored,'missingEpisodes':len(selected)-sum(row['submitted'] for row in family.values()),'rejectedEpisodes':len(rejected),'otherSplitEpisodes':sum(key not in selected for key in records),'duplicateObservationReplays':replays},
        'cause':{'expectedSupportedEpisodes':sum(row['expected']['primaryCause']!='unknown' for row in selected.values()),'positivePopulationCoverage':rate(cause_expected,sum(row['expected']['primaryCause']!='unknown' for row in selected.values())),'confusion':confusion,'supportedPrecision':rate(correct_causes,supported_claims),'supportedRecall':rate(correct_causes,cause_expected),'abstention':rate(abstentions,scored)},
        'metricPopulation':'Rates describe valid submitted outputs in the selected split; missing/rejected output is not invented abstention. Positive-population coverage and family coverage disclose selection.',
        'avoidability':{'expectedPositiveEpisodes':sum(row['expected']['measuredAvoidable'] for row in selected.values()),'positivePopulationCoverage':rate(avoid_tp+avoid_fn,sum(row['expected']['measuredAvoidable'] for row in selected.values())),'truePositive':avoid_tp,'falsePositive':avoid_fp,'falseNegative':avoid_fn,'trueNegative':avoid_tn,'precision':rate(avoid_tp,avoid_tp+avoid_fp),'recall':rate(avoid_tp,avoid_tp+avoid_fn)},
        'familyCoverage':family,'references':{'status':'checked' if source_records is not None else 'unknown-independent-truth-missing','validity':rate(reference_valid,reference_checked)},
        'numericAccuracy':{'status':'checked' if truths is not None else 'unknown-independent-truth-missing','exactCorrespondence':rate(numeric_correct,numeric_checked),'unknownQuantities':numeric_unknown,'unresolvedReferences':numeric_unresolved,'semantics':'metric-reference quantity/unit/population/source correspondence only; unstructured prose numeric assertions are not scored as verified facts'},
        'validator':{'dialect':'Draft202012','implementation':'selected jsonschema dependency with FormatChecker','canonicalAdmission':'not established by schema validation'},
        'rejections':rejected[:32],'rejectionsOmitted':max(0,len(rejected)-32),'limits':{'maxRecords':MAX_RECORDS,'maxMetrics':MAX_METRICS,'maxEvidenceRecords':MAX_EVIDENCE,'maxInputBytes':MAX_BYTES},
        'qualification':{'verdict':'not-established','reason':'Offline evaluator behavior only; synthetic predictions are plumbing tests. Actual held-out calibration needs real admitted outputs, independently adjudicated labels and disclosed uncertainty. Economics and installed acceptance are separate.'},
        'uncertainty':'Wilson 95% binomial intervals are descriptive; authored synthetic labels, sparse families, missing outputs and unverified source authority prevent causal or general performance claims.'}


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--corpus',default=str(ROOT/'tests/process-efficiency/labeled-corpus-v1.json'))
    parser.add_argument('--predictions',required=True,help='Separately supplied actual analyst output envelope; this command never generates predictions.')
    parser.add_argument('--split',choices=['development','held-out'],required=True)
    parser.add_argument('--evidence-truth',help='Independent exact episode-to-canonical-reference/digest bindings.')
    parser.add_argument('--metric-truth',help='Independent canonical metric records for numeric correspondence.')
    parser.add_argument('--output',help='Optional report file; otherwise writes JSON to stdout.')
    args=parser.parse_args(argv)
    try:
        corpus,corpus_digest=read_json(args.corpus)
        predictions,_=read_json(args.predictions)
        evidence,_=read_json(args.evidence_truth) if args.evidence_truth else (None,None)
        metrics,_=read_json(args.metric_truth) if args.metric_truth else (None,None)
        report=evaluate(corpus,corpus_digest,predictions,split=args.split,evidence_truth=evidence,metric_truth=metrics)
        payload=json.dumps(report,sort_keys=True,indent=2,allow_nan=False)+'\n'
        if args.output:
            with Path(args.output).open('x',encoding='utf-8') as stream:stream.write(payload)
        else:sys.stdout.write(payload)
        return 0
    except (ValueError,OSError,KeyError,TypeError,json.JSONDecodeError) as error:
        sys.stderr.write('process-efficiency-evaluate: '+str(error)[:512]+'\n')
        return 2

if __name__=='__main__':raise SystemExit(main())
