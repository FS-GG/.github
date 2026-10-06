#!/usr/bin/env python3
"""Offline executable contract oracle; no engine, store, network or provider calls."""
import copy
import datetime as dt
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import re
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = json.loads((ROOT / 'contracts/process-efficiency/measurement-contract-v1.json').read_text())
FIXTURES = json.loads((ROOT / 'tests/process-efficiency/metric-fixtures-v1.json').read_text())
CORPUS = json.loads((ROOT / 'tests/process-efficiency/labeled-corpus-v1.json').read_text())


def rational(value):
    return Fraction(*value) if isinstance(value, list) else value


def encode(value):
    if isinstance(value, Fraction):
        return value.numerator if value.denominator == 1 else [value.numerator, value.denominator]
    if isinstance(value, dict):
        return {key: encode(val) for key, val in value.items()}
    return value


def union(intervals):
    if not intervals:
        return None
    end = None
    total = 0
    for start, finish in sorted(intervals):
        if finish < start:
            raise ValueError('reversed interval')
        total += finish - max(start, end if end is not None else start) if end is None or finish > end else 0
        end = max(finish, end if end is not None else finish)
    return total


def calculate(fixture):
    """Reference calculations, intentionally independent of fixture expected values."""
    kind = fixture['kind']
    if kind == 'allocation':
        sources = {}
        for row in fixture['sources']:
            if row['id'] in sources and sources[row['id']] != row:
                raise ValueError('conflicting resource identity')
            sources[row['id']] = row
        total = allocated = Fraction(0)
        purposes, items = {}, {}
        for source in sources.values():
            amount = source['amount']
            total += amount
            shares = Fraction(0)
            for share in source['shares']:
                fraction = Fraction(share['numerator'], share['denominator'])
                if fraction < 0:
                    raise ValueError('negative allocation')
                shares += fraction
                contribution = amount * fraction
                purposes[share['purpose']] = purposes.get(share['purpose'], 0) + contribution
                items[share['item']] = items.get(share['item'], 0) + contribution
                allocated += contribution
            if shares > 1:
                raise ValueError('shared resource overallocated')
        return encode(dict(total=total, allocated=allocated, unallocated=total-allocated, byPurpose=purposes, byItem=items))
    if kind == 'time':
        start, end = fixture['window']
        def clip(rows):
            return [(max(a, start), min(b, end)) for a, b in rows if min(b, end) > max(a, start)]
        touch, wait = clip(fixture['touch']), clip(fixture['wait'])
        overlap = [(max(a, c), min(b, d)) for a, b in touch for c, d in wait if min(b, d) > max(a, c)]
        touched = union(touch)
        return encode(dict(elapsed=end-start, observedSpan=sum(b-a for a,b in fixture['touch']), touchUnion=touched, waitUnion=union(wait), touchWaitOverlap=union(overlap) if touch and wait else None, flowRatio=Fraction(touched, end-start) if touched is not None and end > start else None))
    if kind == 'attempts':
        total = retried = count = additional = same = changed = 0
        complete = True
        for operation in fixture['operations']:
            attempts = {}
            for attempt in operation['attempts']:
                if attempt['id'] in attempts and attempts[attempt['id']] != attempt:
                    raise ValueError('conflicting executed attempt')
                attempts[attempt['id']] = attempt
            rows = list(attempts.values())
            total += sum(row['usage'] for row in rows)
            if operation['population'] == 'complete':
                count += 1
                retried += len(rows) > 1
            else:
                complete = False
            for before, after in zip(rows, rows[1:]):
                additional += after['usage']
                if before['inputs'] == after['inputs']:
                    same += after['usage']
                else:
                    changed += after['usage']
        return encode(dict(total=total, operations=count, retriedOperations=retried, additionalUsage=additional, sameInputUsage=same, changedInputUsage=changed, incidence=Fraction(retried,count) if count else None, burden=Fraction(additional,total) if complete and total else None))
    if kind == 'avoidability':
        total = sum(row['amount'] for row in fixture['amounts'])
        supported = lambda row: row['status'] in {'observed','supported-inference'}
        amount = sum(row['amount'] for row in fixture['amounts'] if supported(row) and row['necessity']=='avoidable' and row['purpose']=='avoidable-process')
        classified = sum(row['amount'] for row in fixture['amounts'] if supported(row) and row['purpose'] != 'unknown')
        return encode(dict(total=total, supportedAvoidable=amount, share=Fraction(amount,total) if total else None, supportedClassification=classified))
    if kind == 'accepted':
        total = sum(fixture['costs'])
        count = fixture['accepted']
        return encode(dict(total=total, ratio=Fraction(total,count) if count else None, status='known' if count else 'not-applicable'))
    if kind == 'revision':
        distinct = {json.dumps(row,sort_keys=True) for row in fixture['records']}
        selected = {}
        for row in fixture['records']:
            key = (row['id'],row['revision'])
            if key in selected and selected[key] != row:
                return dict(total=None,outcomes=None,byItem={},auditDistinctRecords=len(distinct),conflict=True)
            selected[key] = row
        current = {}
        for row in selected.values():
            if row['id'] not in current or row['revision'] > current[row['id']]['revision']:
                current[row['id']] = row
        items = {}
        for row in current.values():
            if not row['current']:
                raise ValueError('no canonical current selection')
            items[row['item']] = items.get(row['item'],0)+row['cost']
        return dict(total=sum(items.values()),outcomes=len({row['outcome'] for row in current.values()}),byItem=items,auditDistinctRecords=len(distinct),conflict=False)
    if kind == 'population':
        complete = fixture['expectedInvocations'] > 0 and fixture['expectedInvocations'] == fixture['terminalInvocations'] == fixture['usageInvocations']
        # Fixture counts presume valid canonical lineage and admission, never substitute for it.
        return dict(sourceDeliveries=len(set(fixture['sourceOutcomes'])),nativeCompletions=int(complete),observedUsage=fixture['observedUsage'],wholeItemTotal=fixture['observedUsage'] if complete else None,assessmentScope='native-item' if complete else 'provisional-delivery')
    if kind == 'profile':
        bound = fixture['unknownBound']
        if bound is None:
            return dict(share=None,ceilingBreach=None,severe=None,status='insufficient')
        profile = next(row for row in CONTRACT['policyProfiles'] if row['id']==fixture['profile'])
        share = Fraction(fixture['overhead']+bound,fixture['productive']+fixture['overhead']+bound)
        severe = 'severe' in profile and share > Fraction(**profile['severe'])
        return encode(dict(share=share,ceilingBreach=share>Fraction(**profile['ceiling']),severe=bool(severe),status='bounded' if bound else 'known'))
    if kind == 'first-pass':
        eligible=[row for row in fixture['items'] if row['population']=='complete' and row['state']!='open']
        first=sum(row['acceptedAttempt']==1 and row['state']=='accepted' for row in eligible)
        return encode(dict(eligibleItems=len(eligible),firstPassItems=first,rate=Fraction(first,len(eligible)) if eligible else None,excludedItems=len(fixture['items'])-len(eligible),openItems=sum(row['state']=='open' for row in fixture['items']),abandonedItems=sum(row['state']=='abandoned' for row in fixture['items'])))
    if kind == 'health':
        age=lambda key:fixture['cutoff']-fixture[key] if fixture[key] is not None else None
        return encode(dict(sourceAge=age('producerTime'),ingestionAge=age('ingestionTime'),publicationAge=age('publicationTime'),producerCoverage=Fraction(fixture['observedProducers'],fixture['expectedProducers']) if fixture['expectedProducers'] else None))
    if kind == 'epochs':
        return dict(totalExposure=sum(row['cost'] for row in fixture['outcomes']),currentAccepted=sum(row['status']=='current' for row in fixture['outcomes']),historicalAccepted=sum(row['status']=='historical' for row in fixture['outcomes']))
    raise ValueError('unknown fixture kind')


def schema_check(value, schema, document=None):
    """Small offline checker for exactly the JSON Schema vocabulary used here.

    It is a fixture checker, not a replacement for a production Draft202012 validator.
    """
    document = document or schema
    if '$ref' in schema:
        target = document
        for segment in schema['$ref'].removeprefix('#/').split('/'):
            target = target[segment]
        return schema_check(value,target,document)
    if 'const' in schema and value != schema['const']:
        raise ValueError('wrong constant')
    if 'enum' in schema and value not in schema['enum']:
        raise ValueError('unsupported enum')
    if 'type' in schema:
        kinds = schema['type'] if isinstance(schema['type'],list) else [schema['type']]
        types = {'object':lambda v:isinstance(v,dict),'array':lambda v:isinstance(v,list),'string':lambda v:isinstance(v,str),'integer':lambda v:type(v) is int,'null':lambda v:v is None}
        if not any(types[kind](value) for kind in kinds):
            raise ValueError('wrong type')
    if isinstance(value,dict):
        if not set(schema.get('required',[])) <= value.keys():
            raise ValueError('missing required field')
        if schema.get('additionalProperties') is False and value.keys()-schema.get('properties',{}).keys():
            raise ValueError('unknown field')
        for key,val in value.items():
            if key in schema.get('properties',{}):
                schema_check(val,schema['properties'][key],document)
    elif isinstance(value,list):
        if len(value)>schema.get('maxItems',len(value)):
            raise ValueError('too many items')
        for val in value:
            schema_check(val,schema['items'],document)
    elif isinstance(value,str):
        if len(value)<schema.get('minLength',0) or len(value)>schema.get('maxLength',len(value)):
            raise ValueError('text bound')
        if 'pattern' in schema and not re.search(schema['pattern'],value):
            raise ValueError('wrong pattern')
        if schema.get('format')=='date-time':
            parsed=dt.datetime.fromisoformat(value.replace('Z','+00:00'))
            if parsed.tzinfo is None:
                raise ValueError('timestamp without timezone')
    elif type(value) is int and value<schema.get('minimum',value):
        raise ValueError('below minimum')


def assessment_check(value, metric_ids):
    schema_check(value,json.loads((ROOT/'contracts/process-efficiency/assessment-v1.schema.json').read_text()))
    evidence = {ref['id']:ref for ref in value['evidenceRefs']}
    if len(evidence) != len(value['evidenceRefs']):
        raise ValueError('duplicate evidence reference')
    if not set(value['metricRefs']) <= set(metric_ids):
        raise ValueError('unknown metric')
    fields = CONTRACT['assessmentRules']['idempotencyFields']
    def lookup(path):
        current=value
        for part in path.split('.'):
            current=current[part]
        return current
    key=hashlib.sha256(json.dumps([lookup(path) for path in fields],separators=(',',':'),ensure_ascii=True).encode()).hexdigest()
    if key != value['lifecycle']['idempotencyKey']:
        raise ValueError('wrong idempotency join')
    if value['subject']['outcomeEpoch'] is None and value['lifecycle']['state']=='ready':
        raise ValueError('unknown outcome epoch cannot qualify ready')
    if value['subject']['scope']=='provisional-delivery':
        if value['lifecycle']['itemReviewRef'] is not None or value['lifecycle']['state']=='ready':
            raise ValueError('provisional cannot claim native ready')
    elif value['lifecycle']['state']=='ready':
        review=value['lifecycle']['itemReviewRef']
        if not review or review not in evidence or evidence[review]['kind']!='process-review' or value['coverage']['population']!='complete' or value['coverage']['usage']!='complete':
            raise ValueError('final review needs admitted complete population')
    for finding in value['findings']:
        if not set(finding['evidenceRefs']+finding['recoveryRefs']) <= evidence.keys():
            raise ValueError('broken finding evidence')
        supported=finding['epistemicStatus'] in {'observed','supported-inference'}
        if supported and not finding['evidenceRefs']:
            raise ValueError('supported claim missing evidence')
        if finding['necessity']=='avoidable' and supported and not finding['alternative']:
            raise ValueError('avoidable claim missing feasible alternative')
        if finding['primaryCause']!='unknown' and not supported:
            raise ValueError('unsupported primary cause')


def metric_check(value):
    schema_check(value,json.loads((ROOT/'contracts/process-efficiency/metric-v1.schema.json').read_text()))
    amount=value['value']
    if value['unit']=='ratio' and amount['status']=='known' and (amount['denominator'] is None or amount['denominator']==0):
        raise ValueError('known ratio requires positive denominator')
    if amount['status']=='unknown' and (amount['numerator'] is not None or not amount['reason']):
        raise ValueError('unknown numeric claim or missing reason')
    if value['metric']=='cost-per-accepted' and amount['denominator']==0 and (amount['status']!='not-applicable' or not amount['reason']):
        raise ValueError('zero acceptance must remain not-applicable')
    if value['price']['kind']=='estimate' and (not value['price']['currency'] or not value['price']['version']):
        raise ValueError('estimated currency lacks price provenance')
    if value['population']['windowEnd'] < value['population']['windowStart']:
        raise ValueError('reversed cohort window')


class ContractTests(unittest.TestCase):
    def test_hand_calculations(self):
        for fixture in FIXTURES['fixtures']:
            with self.subTest(fixture=fixture['id']):
                self.assertEqual(calculate(fixture),fixture['expected'])
                self.assertTrue(fixture['handCalculation'])

    def test_allocation_rejects_duplication_and_conserves_replay(self):
        fixture=copy.deepcopy(FIXTURES['fixtures'][1])
        fixture['sources'].append(copy.deepcopy(fixture['sources'][0]))
        self.assertEqual(calculate(fixture),fixture['expected'])
        fixture['sources'][1]['amount']+=1
        with self.assertRaises(ValueError):calculate(fixture)
        fixture=copy.deepcopy(FIXTURES['fixtures'][1])
        fixture['sources'][0]['shares'][0]['numerator']=2
        with self.assertRaises(ValueError):calculate(fixture)

    def test_heldout_and_label_boundaries(self):
        episodes=CORPUS['episodes']
        self.assertEqual(len({row['id'] for row in episodes}),60)
        for split in ['development','held-out']:
            self.assertEqual(sum(row['split']==split for row in episodes),30)
        families={row['family'] for row in episodes}
        self.assertEqual(len(families),10)
        for family in families:self.assertGreaterEqual(sum(row['family']==family for row in episodes),5)
        for row in episodes:
            expected=row['expected']
            for axis in ['activity','purpose','trigger','necessity','epistemicStatus']:
                self.assertIn(expected[axis],CONTRACT['axes'][axis])
            self.assertIn(expected['primaryCause'],CONTRACT['axes']['cause'])
            self.assertLessEqual(set(expected['evidenceRefs']),{fact['id'] for fact in row['facts']})
            if expected['measuredAvoidable']:
                self.assertEqual(expected['necessity'],'avoidable')
                self.assertIn(expected['epistemicStatus'],['observed','supported-inference'])
                self.assertTrue(expected['alternative'])
            if row['family'] in ['useful-failure','changed-input-rerun','required-duplicate']:
                self.assertFalse(expected['measuredAvoidable'])

    def test_source_pin_and_legacy_categories(self):
        inspected=CONTRACT['sourceInspection']
        self.assertEqual(inspected['storeSchemaVersion'],13)
        for file,digest in inspected['fileSha256'].items():
            pinned=subprocess.run(['git','show',f"{inspected['revision']}:{file}"],cwd=ROOT,check=True,capture_output=True,timeout=5).stdout
            self.assertEqual(hashlib.sha256(pinned).hexdigest(),digest)
        mapping=CONTRACT['canonicalMappings']['ActivitySpan.Category']
        self.assertEqual(set(mapping),{'planning','implementation','review','validation','delivery','repair','operations','other','unclassified'})
        self.assertEqual(mapping['unclassified'],'unknown')

    def test_schema_samples_and_semantics(self):
        metric_ids={row['metricId'] for row in FIXTURES['metricSamples']}
        for sample in FIXTURES['metricSamples']:
            metric_check(sample)
        for sample in FIXTURES['assessmentSamples']:
            assessment_check(sample,metric_ids)

    def test_reject_fabricated_cost_unknown_evidence_and_provisional_ready(self):
        samples=FIXTURES['assessmentSamples'];ids={'metric-observed-a'}
        for mutate in [lambda x:x.update(measuredCost=0),lambda x:x['metricRefs'].append('not-measured'),lambda x:x['coverage'].update(population='partial'),lambda x:x['lifecycle'].update(idempotencyKey='0'*64),lambda x:x['provenance'].update(taxonomyVersion='efficiency-taxonomy/99')]:
            bad=copy.deepcopy(samples[0]);mutate(bad)
            with self.assertRaises(ValueError):assessment_check(bad,ids)
        bad=copy.deepcopy(samples[1]);bad['lifecycle']['state']='ready'
        with self.assertRaises(ValueError):assessment_check(bad,ids)

    def test_supported_avoidable_claim_requires_evidence_and_alternative(self):
        sample=copy.deepcopy(FIXTURES['assessmentSamples'][0])
        episode=next(row for row in CORPUS['episodes'] if row['family']=='avoidable-replay')
        expected=episode['expected']
        finding={key:expected[key] for key in ['activity','purpose','trigger','primaryCause','contributingCauses','necessity','epistemicStatus','alternative']}
        finding.update(id='finding-1',severity='minor',summary='Synthetic redundant proof.',evidenceRefs=['source-a'],affectedObjects=[],recoveryRefs=[],uncertainty='')
        sample['findings']=[finding]
        assessment_check(sample,{'metric-observed-a'})
        for mutate in [lambda x:x.update(alternative=None),lambda x:x.update(evidenceRefs=[]),lambda x:x.update(evidenceRefs=['missing']),lambda x:x.update(epistemicStatus='hypothesis')]:
            bad=copy.deepcopy(sample);mutate(bad['findings'][0])
            with self.assertRaises(ValueError):assessment_check(bad,{'metric-observed-a'})

    def test_metric_unknown_denominators_and_pricing(self):
        sample=copy.deepcopy(FIXTURES['metricSamples'][0])
        metric_check(sample)
        for mutate in [lambda x:x.update(unit='ratio'),lambda x:x['value'].update(status='unknown'),lambda x:x['price'].update(kind='estimate'),lambda x:x['value'].update(numerator=-1)]:
            bad=copy.deepcopy(sample);mutate(bad)
            with self.assertRaises(ValueError):metric_check(bad)
        sample.update(metric='avoidable-share',unit='ratio')
        sample['value'].update(status='unknown',numerator=None,denominator=None,unknownAmount=None,reason='Expected usage population unknown.')
        metric_check(sample)

    def test_revision_order_and_correction_conservation(self):
        fixture=next(row for row in FIXTURES['fixtures'] if row['id']=='cross-item-correction')
        reversed_fixture=copy.deepcopy(fixture);reversed_fixture['records'].reverse()
        self.assertEqual(calculate(reversed_fixture),fixture['expected'])
        before=144
        self.assertEqual(calculate(fixture)['total'],before)
        self.assertEqual(calculate(fixture)['outcomes'],1)

if __name__=='__main__':unittest.main(verbosity=2)
