import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('telemetry_audit', Path(__file__).resolve().parents[2] / 'scripts/learn-history-telemetry.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def row(item='i1', invocation='v1', model='m1', role='worker'):
    return dict(item=item, invocation=invocation, runtime='claude-code-subagent', firstExpectedAt='2026-11-01T00:00:00Z',
                launchResolvedModel=model, storedRequestedEffort=None, producerStream='claude-code-subagent:' + role,
                responses=2, nullModelResponses=0, nullEffortResponses=0, models=[model], efforts=['high'])

def fixture(rows=None):
    rows = rows or [row()]
    owners = {}
    for r in rows: owners.setdefault(r['invocation'], set()).add(r['item'])
    return dict(controls=dict(rawAssignments=len(rows), assignments=len(rows), invocations=len(owners),
                             items=len({r['item'] for r in rows}), ambiguousInvocations=sum(len(x)>1 for x in owners.values()), runtimeMismatches=0),
                invocations=rows, boundaries=[dict(kind=k, attributedFacts=0) for k in sorted(m.KINDS)],
                snapshots=dict(facts=0, items=0), population=dict(passiveResponses=2, nullModelResponses=0, nullEffortResponses=0,
                unknownTerminalResponses=2, partialPopulationResponses=2))

class TelemetryAuditTests(unittest.TestCase):
    def test_duplicate_lineage_does_not_multiply(self):
        data = fixture(); data['controls']['rawAssignments'] = 5
        self.assertEqual(m.summarize(data)['attribution']['invocations'], 1)
    def test_duplicate_output_row_refused(self):
        with self.assertRaises(ValueError): m.summarize(fixture([row(), row()]))
    def test_conflicting_attribution_refused(self):
        with self.assertRaises(ValueError): m.summarize(fixture([row(), row(item='i2')]))
    def test_control_counts_reconciled(self):
        data = fixture(); data['controls']['items'] = 2
        with self.assertRaises(ValueError): m.summarize(data)
    def test_null_profile_not_hidden_by_count_distinct(self):
        data = fixture(); data['invocations'][0].update(nullEffortResponses=1, efforts=['high', None])
        result = m.summarize(data)
        self.assertEqual(result['unknownProfileInvocations'], 1)
        self.assertEqual(result['observedProfiles'], [])
        self.assertEqual(result['nullProfileResponses']['effort'], 1)
    def test_multiple_observed_models_are_unknown(self):
        data = fixture(); data['invocations'][0]['models'] = ['m1', 'm2']
        self.assertEqual(m.summarize(data)['unknownProfileInvocations'], 1)
    def test_mismatched_null_counts_refused(self):
        data = fixture(); data['invocations'][0]['nullModelResponses'] = 1
        with self.assertRaises(ValueError): m.summarize(data)
    def test_mixed_roles_are_not_worker_overlap(self):
        result = m.summarize(fixture([row(), row(invocation='v2', model='m2', role='critic')]))
        self.assertEqual(result['naiveItemProfileGraph']['multiProfileItems'], 1)
        self.assertEqual(result['workerOnlyItemProfileGraph']['multiProfileItems'], 0)
        self.assertEqual(result['qualifiedMatchedAnchors'], 0)
        self.assertEqual(result['invarianceDecision'], 'inconclusive')
    def test_same_worker_issue_is_still_not_matched_anchor(self):
        result = m.summarize(fixture([row(), row(invocation='v2', model='m2')]))
        self.assertEqual(result['workerOnlyItemProfileGraph']['multiProfileItems'], 1)
        self.assertEqual(result['qualifiedMatchedAnchors'], 0)
    def test_new_boundary_facts_require_review_not_rejection(self):
        data = fixture(); data['snapshots'] = dict(facts=2, items=1); data['boundaries'][0]['attributedFacts'] = 3
        result = m.summarize(data)
        self.assertTrue(result['newBoundaryFactsRequireReview'])
        self.assertEqual(result['qualifiedMatchedAnchors'], 0)
    def test_missing_or_duplicate_sections_refused(self):
        with self.assertRaises(ValueError): m.parse_sections('[]')
        envelope = json.dumps([dict(section='controls', payload='{}')])
        with self.assertRaises(ValueError): m.parse_sections(envelope + envelope)
    def test_query_and_output_hashes_verified(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name); q = root/'q.sql'; i = root/'i.jsonl'; manifest = root/'manifest.json'
            q.write_text('SELECT 1;')
            i.write_text('\n'.join(json.dumps([dict(section=k, payload=json.dumps(v))]) for k,v in fixture().items()))
            meta=dict(schema='fsgg.learn-telemetry-read/2', exitCode=0, observedAt='2026-11-01T00:00:00Z',sourceScope='synthetic',
                      querySha256=hashlib.sha256(q.read_bytes()).hexdigest(), outputSha256=hashlib.sha256(i.read_bytes()).hexdigest())
            manifest.write_text(json.dumps(meta)); self.assertEqual(m.audit(q,i,manifest)['attribution']['items'],1)
            q.write_text('SELECT 2;')
            with self.assertRaises(ValueError): m.audit(q,i,manifest)
            q.write_text('SELECT 1;'); i.write_text(i.read_text()+' ')
            with self.assertRaises(ValueError): m.audit(q,i,manifest)

if __name__ == '__main__': unittest.main()
