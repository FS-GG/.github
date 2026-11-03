#!/usr/bin/env python3
"""Synthetic adversarial controls; UTC clock and offline evidence only."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('history', ROOT / 'scripts/learn-history-evaluate.py')
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)


def conn(nodes, total=None, next_page=False):
    return {'nodes': nodes, 'totalCount': len(nodes) if total is None else total,
            'pageInfo': {'hasNextPage': next_page}}


def record():
    return {'id': 'issue1', 'createdAt': '2026-01-01T00:00:00Z', 'closedAt': '2026-01-01T04:00:00Z',
            'state': 'CLOSED', 'body': 'original', 'lastEditedAt': None,
            'userContentEdits': conn([]), 'comments': conn([]),
            'timelineItems': conn([{'id': 'close1', '__typename': 'ClosedEvent', 'createdAt': '2026-01-01T04:00:00Z'}]),
            'commits': conn([{'commit': {'oid': 'a', 'authoredDate': '2026-01-01T01:00:00Z', 'committedDate': '2026-01-01T02:00:00Z'}}])}


def observation(task='task1', profile='profile1', invocation='inv1', **extra):
    r = {k: 'frozen' for k in h.ANCHOR_FIELDS}
    r.update(inputHash=task, invocationId=invocation, requestedProfileId=profile, requestedAssignmentVerified=True,
             effectiveProfileId=profile, effectiveConfigurationVerified=True, qualityPass=False,
             qualitySourceId='independent-evidence', priorAttemptInformation=False, independenceEstablished=True)
    r.update(extra)
    return r


class HistoryTests(unittest.TestCase):
    def test_boundary_uses_latest_prior_body(self):
        r=record();r['body']='later';r['lastEditedAt']='2026-01-01T03:00:00Z'
        r['userContentEdits']=conn([{'id':'a','editedAt':r['createdAt'],'diff':'original','deletedAt':None},
            {'id':'b','editedAt':'2026-01-01T03:00:00Z','diff':'later','deletedAt':None}])
        self.assertEqual(h.body_at_boundary(r,'2026-01-01T02:00:00Z')[0],'original')
        self.assertEqual(h.body_at_boundary(r,'2026-01-01T04:00:00Z')[0],'later')

    def test_tied_original_revisions_refuse(self):
        r=record();r['lastEditedAt']=r['createdAt']
        r['userContentEdits']=conn([{'id':'a','editedAt':r['createdAt'],'diff':'other','deletedAt':None},
            {'id':'b','editedAt':r['createdAt'],'diff':'original','deletedAt':None}])
        with self.assertRaises(ValueError):h.creation_body(r)

    def test_tied_lifecycle_preserves_native_order_not_lexical_ids(self):
        r=record();r['state']='OPEN'
        r['timelineItems']=conn([{'id':'zclose','__typename':'ClosedEvent','createdAt':r['closedAt']},
            {'id':'areopen','__typename':'ReopenedEvent','createdAt':r['closedAt']}])
        self.assertEqual(h.lifecycle(r)['reopens'],1)

    def test_timezone_required(self):
        with self.assertRaises(ValueError): h.instant('2026-01-01T00:00:00')

    def test_offset_same_instant(self):
        self.assertEqual(h.instant('2026-01-01T01:00:00+01:00'), h.instant('2026-01-01T00:00:00Z'))

    def test_truncated_connection_refuses(self):
        with self.assertRaises(ValueError): h.connection(conn([{}], next_page=True))

    def test_missing_pagination_refuses(self):
        with self.assertRaises(ValueError): h.connection({'nodes': [], 'totalCount': 0})

    def test_count_mismatch_refuses(self):
        with self.assertRaises(ValueError): h.connection(conn([{}], total=2))

    def test_filtered_timeline_count_is_not_all_event_census(self):
        self.assertEqual(len(h.connection(conn([{}], total=20), filtered=True)), 1)

    def test_null_node_refuses(self):
        with self.assertRaises(ValueError): h.connection(conn([None]))

    def test_unedited_body_qualified(self):
        self.assertEqual(h.creation_body(record()), ('original', 'verified_unedited'))

    def test_edited_body_origin_and_current_verified(self):
        x = record(); x['body'] = 'final'; x['lastEditedAt'] = '2026-01-01T03:00:00Z'
        x['userContentEdits'] = conn([{'id': 'e2', 'editedAt': x['lastEditedAt'], 'deletedAt': None, 'diff': 'final'},
                                     {'id': 'e1', 'editedAt': x['createdAt'], 'deletedAt': None, 'diff': 'original'}])
        self.assertEqual(h.creation_body(x)[0], 'original')
        x['userContentEdits']['nodes'][1]['deletedAt'] = x['lastEditedAt']
        with self.assertRaises(ValueError): h.creation_body(x)

    def test_unknown_edit_format_refuses(self):
        x = record(); x['body'] = '@@ patch'; x['lastEditedAt'] = x['createdAt']
        x['userContentEdits'] = conn([{'id': 'e', 'editedAt': x['createdAt'], 'deletedAt': None, 'diff': x['body']}])
        with self.assertRaises(ValueError): h.creation_body(x)

    def test_missing_original_revision_refuses(self):
        x = record(); x['lastEditedAt'] = '2026-01-01T01:00:00Z'
        x['userContentEdits'] = conn([{'id': 'e', 'editedAt': x['lastEditedAt'], 'deletedAt': None, 'diff': x['body']}])
        with self.assertRaises(ValueError): h.creation_body(x)

    def test_empty_history_edit_contradiction(self):
        x = record(); x['lastEditedAt'] = x['createdAt']
        with self.assertRaises(ValueError): h.creation_body(x)

    def test_reopen_preserves_first_failure(self):
        x = record(); x['timelineItems'] = conn([
            {'id': 'c1', '__typename': 'ClosedEvent', 'createdAt': '2026-01-01T01:00:00Z'},
            {'id': 'r1', '__typename': 'ReopenedEvent', 'createdAt': '2026-01-01T02:00:00Z'},
            {'id': 'c2', '__typename': 'ClosedEvent', 'createdAt': '2026-01-01T04:00:00Z'}])
        got = h.lifecycle(x)
        self.assertEqual((got['closures'], got['reopens'], got['firstToFinalCloseSeconds']), (2, 1, 10800))
        self.assertIsNone(got['activeSeconds'])

    def test_duplicate_transport_event_deduplicated(self):
        x = record(); e = x['timelineItems']['nodes'][0]
        x['timelineItems'] = conn([e, copy.deepcopy(e)])
        self.assertEqual(h.lifecycle(x)['closures'], 1)

    def test_conflicting_duplicate_event_refuses(self):
        x = record(); e = copy.deepcopy(x['timelineItems']['nodes'][0]); e['createdAt'] = '2026-01-01T03:00:00Z'
        x['timelineItems'] = conn(x['timelineItems']['nodes'] + [e])
        with self.assertRaises(ValueError): h.lifecycle(x)

    def test_timeline_state_disagreement_refuses(self):
        x = record(); x['state'] = 'OPEN'
        with self.assertRaises(ValueError): h.lifecycle(x)

    def test_round_zero_and_quoted_marker(self):
        x = record(); body = '<!-- fsgg:independent-review:v1 -->\nround: 0\nverdict: changes-required\n'
        x['comments'] = conn([{'id': 'a', 'body': body}, {'id': 'b', 'body': 'Quoted example:\n' + body}])
        self.assertEqual(h.review_declarations(x), [{'roundIndex': 0, 'verdict': 'changes-required'}])

    def test_commit_silence_never_becomes_active_time(self):
        x = record(); out = h.activity_gaps(x, x['closedAt'])
        self.assertIsNone(out['activeSeconds'])
        self.assertEqual(out['authorCommitTimeDifferences'], 1)
        self.assertEqual(out['authoredDate']['thresholds']['3600']['hypotheticalCappedGapSeconds'], 7200)
        self.assertEqual(out['calendarSeconds'], 14400)

    def test_commit_outside_window_is_reported_not_moved(self):
        x = record(); x['commits']['nodes'][0]['commit']['authoredDate'] = '2025-12-31T00:00:00Z'
        self.assertEqual(h.activity_gaps(x, x['closedAt'])['authoredDate']['outsidePrWindow'], 1)

    def test_truncated_commits_refuse(self):
        x = record(); x['commits']['pageInfo']['hasNextPage'] = True
        with self.assertRaises(ValueError): h.activity_gaps(x, x['closedAt'])

    def test_requested_estimand_survives_unknown_effective(self):
        rows = [observation(effectiveProfileId=None, effectiveConfigurationVerified=False)]
        self.assertEqual(h.support_graph(rows, 'requested_assignment')['qualifiedUniqueEdges'], 1)
        self.assertEqual(h.support_graph(rows, 'provider_effective')['qualifiedUniqueEdges'], 0)

    def test_observed_effective_does_not_invent_requested_assignment(self):
        rows = [observation(requestedProfileId=None, requestedAssignmentVerified=False)]
        self.assertEqual(h.support_graph(rows, 'requested_assignment')['qualifiedUniqueEdges'], 0)
        self.assertEqual(h.support_graph(rows, 'provider_effective')['qualifiedUniqueEdges'], 1)

    def test_disjoint_allocations_not_connected(self):
        rows = [observation(), observation(task='other', profile='p2', invocation='i2')]
        self.assertFalse(h.support_graph(rows, 'provider_effective')['crossProfileConnected'])

    def test_connectivity_not_invariance(self):
        rows = [observation(), observation(profile='p2', invocation='i2')]
        out = h.support_graph(rows, 'provider_effective')
        self.assertTrue(out['crossProfileConnected']); self.assertFalse(out['invarianceEstablished'])

    def test_role_stage_environment_and_context_prevent_false_anchor(self):
        for key in ('role', 'stage', 'environmentHash', 'contextHash', 'qualityContractHash'):
            with self.subTest(key=key):
                rows = [observation(), observation(profile='p2', invocation='i2', **{key: 'changed'})]
                self.assertFalse(h.support_graph(rows, 'provider_effective')['crossProfileConnected'])

    def test_prior_feedback_not_independent_replicate(self):
        rows = [observation(priorAttemptInformation=True)]
        self.assertEqual(h.support_graph(rows, 'provider_effective')['qualifiedUniqueEdges'], 0)

    def test_missing_quality_not_zero_failure(self):
        rows = [observation(qualityPass=None)]
        self.assertEqual(h.support_graph(rows, 'provider_effective')['qualifiedResponses'], 0)

    def test_real_failure_preserved_as_observation(self):
        self.assertEqual(h.support_graph([observation(qualityPass=False)], 'provider_effective')['qualifiedResponses'], 1)

    def test_transport_duplicates_vs_independent_replicates(self):
        r = observation()
        self.assertEqual(h.support_graph([r, copy.deepcopy(r)], 'provider_effective')['qualifiedResponses'], 1)
        out = h.support_graph([r, observation(invocation='i2')], 'provider_effective')
        self.assertEqual((out['qualifiedResponses'], out['qualifiedUniqueEdges']), (2, 1))

    def test_conflicting_invocation_fails_closed(self):
        with self.assertRaises(ValueError):
            h.support_graph([observation(), observation(qualityPass=True)], 'provider_effective')

    def test_inventory_hashes_and_types(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            row = {'node_id': 'node1', 'id': 1, 'kind': 'issue', 'repository': 'FS-GG/test', 'private': False,
                   'body': 'test', 'bodySha256': h.digest(b'test'), 'created_at': '2026-01-01T00:00:00Z'}
            payload = (json.dumps(row) + '\n').encode(); (root/'test.jsonl').write_bytes(payload)
            manifest = {'cutoff': '2026-01-02T00:00:00Z', 'repositoryConnection': {'totalCount': 1, 'nodes': [{'name': 'test'}], 'pageInfo': {'hasNextPage': False}},
                        'repositories': [{'name': 'test', 'private': False, 'complete': True, 'rows': 1, 'expectedIssues': 1, 'expectedPRs': 0, 'fileSha256': h.digest(payload)}]}
            (root/'manifest.json').write_text(json.dumps(manifest)); self.assertEqual(len(h.inventory(root)[1]), 1)
            (root/'test.jsonl').write_bytes(payload + b' ')
            with self.assertRaises(ValueError): h.inventory(root)


if __name__ == '__main__':
    unittest.main()
