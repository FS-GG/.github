import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('projection', Path(__file__).parents[2] / 'scripts/learn-projection.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class ProjectionTests(unittest.TestCase):
    def envelope(self, payload):
        return dict(payload=payload, sha256=hashlib.sha256(m.dispatch.canonical(payload)).hexdigest(), reference='fixture:immutable-original')

    def cohort(self):
        return dict(schema='fsgg.learn-projection-input/1', complete=True,
            population=[dict(itemId='FS-GG/test#1', nodeId='native-1', state='CLOSED', archived=False, workLevel='Delivery'),
                        dict(itemId='FS-GG/test#2', nodeId='native-2', state='CLOSED', archived=True, workLevel='Delivery')],
            classifications=[], launches=[], observations=[])

    def original(self, body='credential'):
        return m.dispatch.classify(dict(itemId='FS-GG/test#1', body=body, capturedAt='2026-01-01T00:00:00Z',
            sourceRevision='fixture:original-native-body', dispatchBoundary='fixture:before-dispatch', producer='fixture',
            runtime='fixture', model='requested-model'))

    def launch(self, identity='worker-1', role='worker'):
        return self.envelope(dict(itemId='FS-GG/test#1', invocationId=identity,
            brief='fsgg-dispatch: item=FS-GG/test#1 role=' + role + '\nOriginal dispatch text.'))

    def usage(self, identity='worker-1', model='observed-worker', effort=None, response='response-1'):
        return self.envelope(dict(kind='provider-usage-observation/1', identity=response, threadId=identity, model=model, effort=effort))

    def test_category_does_not_confuse_safety_floor_with_hardness(self):
        original = self.original()
        self.assertTrue(original['safetyFloor'])
        self.assertEqual('best', original['chosenProfile']['tier'])
        self.assertEqual('Low', original['hardnessCategory'])
        self.assertEqual('Low', m.category(original['scores']))
        for total, expected in [(6, 'Low'), (7, 'Medium'), (12, 'Medium'), (13, 'High')]:
            scores = dict.fromkeys(m.dispatch.RULES, 0)
            left = total
            for key in ['D1', 'D2', 'D4', 'D6', 'D7', 'D8']:
                scores[key] = min(left, 3)
                left -= scores[key]
            self.assertEqual(expected, m.category(scores))
        self.assertEqual('Unknown', m.category(dict(original['scores'], D8=None)))
        self.assertEqual('High', m.category(dict.fromkeys(m.dispatch.RULES, 2)))

    def test_complete_census_missing_history_remains_unknown_including_archived(self):
        result = m.project(self.cohort())
        self.assertEqual(2, result['coverage']['selectedDeliveries'])
        self.assertEqual(2, result['coverage']['closedDeliveries'])
        self.assertEqual(1, result['coverage']['archivedDeliveries'])
        self.assertEqual(0, result['coverage']['actualModelKnown'])
        self.assertEqual('Unknown', result['coverage']['completeHistoricalExecutionCoverage'])
        self.assertTrue(all(record['hardnessCategory'] == 'Unknown' for record in result['records']))

    def test_multiple_roles_models_and_effort_changes_preserve_execution_identities(self):
        cohort = self.cohort()
        cohort['classifications'] = [self.envelope(self.original())]
        cohort['launches'] = [self.launch(), self.launch('critic-1', 'critic')]
        cohort['observations'] = [self.usage(effort='medium'), self.usage(model='second-model', effort='high', response='response-2'),
                                  self.usage('critic-1', 'observed-critic', 'high', 'response-3')]
        result = m.project(cohort)
        record = result['records'][0]
        self.assertEqual(3, len(record['executions']))
        self.assertEqual({'worker-1', 'critic-1'}, {row['invocationId'] for row in record['executions']})
        self.assertEqual({'worker', 'critic'}, {row['role'] for row in record['executions']})
        self.assertEqual('requested-model', record['classification']['model'])
        self.assertNotIn('requested-model', m.assignment(record, 'https://github.com/FS-GG/test/issues/1#issuecomment-17')['values']['Actual models'])
        self.assertEqual('Low', record['hardnessCategory'])
        self.assertIn('fsgg:learn-completion/v1', m.comment_for(record))

    def test_requested_effort_never_fills_missing_observed_effort(self):
        cohort = self.cohort()
        cohort['launches'] = [self.launch()]
        cohort['classifications'] = [self.envelope(self.original())]
        cohort['observations'] = [self.usage()]
        record = m.project(cohort)['records'][0]
        self.assertEqual('Unknown', record['executions'][0]['effort'])
        self.assertIn('actual-effort-unavailable', record['gaps'])
        requested = self.usage()
        requested['payload']['kind'] = 'runtime-admission'
        cohort['observations'] = [self.envelope(requested['payload'])]
        with self.assertRaisesRegex(ValueError, 'actual provider'):
            m.project(cohort)

    def test_deduplication_conflicts_and_tampering(self):
        cohort = self.cohort()
        launch = self.launch()
        cohort['launches'] = [launch, copy.deepcopy(launch)]
        observation = self.usage()
        cohort['observations'] = [observation, copy.deepcopy(observation)]
        self.assertEqual(1, len(m.project(cohort)['records'][0]['executions']))
        changed = copy.deepcopy(launch['payload'])
        changed['brief'] = changed['brief'].replace('worker', 'critic')
        cohort['launches'].append(self.envelope(changed))
        with self.assertRaisesRegex(ValueError, 'ambiguous execution'):
            m.project(cohort)
        cohort = self.cohort()
        observation['payload']['model'] = 'tampered'
        cohort['observations'] = [observation]
        with self.assertRaisesRegex(ValueError, 'digest'):
            m.project(cohort)

    def test_incomplete_population_or_missing_marker_refused(self):
        cohort = self.cohort()
        cohort['complete'] = False
        with self.assertRaisesRegex(ValueError, 'complete'):
            m.project(cohort)
        cohort = self.cohort()
        launch = self.launch()['payload']
        launch['brief'] = 'Unattributed launch'
        cohort['launches'] = [self.envelope(launch)]
        with self.assertRaisesRegex(ValueError, 'marker'):
            m.project(cohort)

    def test_current_scope_assessment_is_revision_bound_and_original_dispatch_wins(self):
        cohort = self.cohort()
        current = dict(itemId='FS-GG/test#1', body='## Done when\nAtomic concurrency installed adoption\n## Acceptance evidence\nLater text',
            capturedAt='2026-01-02T00:00:00Z', sourceRevision='fixture:current-revision', producer='fixture')
        cohort['currentScopes'] = [self.envelope(current)]
        record = m.project(cohort)['records'][0]
        self.assertEqual('High', record['hardnessCategory'])
        self.assertEqual('current-scope', record['classificationBasis'])
        self.assertEqual('Done when', record['classification']['assessmentInput'])
        self.assertNotIn('dispatchBoundary', record['classification'])
        cohort['classifications'] = [self.envelope(self.original('spelling'))]
        record = m.project(cohort)['records'][0]
        self.assertEqual('Low', record['hardnessCategory'])
        self.assertEqual('pre-dispatch', record['classificationBasis'])
        replacement = dict(current, supersedesClassificationDigest=cohort['classifications'][0]['sha256'])
        cohort['currentScopes'] = [self.envelope(replacement)]
        record = m.project(cohort)['records'][0]
        self.assertEqual('High', record['hardnessCategory'])
        self.assertEqual('current-scope', record['classificationBasis'])
        self.assertEqual('Low', record['originalClassification']['hardnessCategory'])
        cohort['currentScopes'] = [self.envelope(current)]
        cohort['classifications'] = []
        record = m.project(cohort)['records'][0]
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, 'body changed'):
                m.dispatch.prepare_completion(dict(current, body='changed'), record, Path(directory) / 'stale')

    def test_accepted_launch_configuration_is_distinct_from_requested_and_provider_effective(self):
        cohort = self.cohort()
        snapshot = self.original()
        cohort['classifications'] = [self.envelope(snapshot)]
        cohort['launches'] = [self.envelope(dict(agent='/root/owner', runtime='collaboration-spawn-agent',
            requestedModel='configured-model', requestedEffort='medium', observedStatus='running',
            afterVerifiedAppliedReceipt=True, bodySnapshot=hashlib.sha256(m.dispatch.canonical(snapshot)).hexdigest()))]
        result = m.project(cohort)
        execution = result['records'][0]['executions'][0]
        self.assertEqual('Unknown', execution['model'])
        self.assertEqual('Unknown', execution['effort'])
        self.assertEqual('configured-model', execution['launchConfiguration']['model'])
        self.assertEqual('root-recorded-launch-configuration', execution['launchConfiguration']['provenance'])
        self.assertIn('raw-launch-response-unavailable', result['records'][0]['gaps'])
        self.assertEqual(1, result['coverage']['launchConfirmedIdentities'])
        self.assertIn('configured-model [launch-confirmed]', m.assignment(result['records'][0], 'https://github.com/FS-GG/test/issues/1#issuecomment-17')['values']['Actual models'])
        self.assertEqual('Unknown', execution['role'])

    def test_shared_thread_cannot_cross_attribute_deliveries_or_roles(self):
        for same_delivery in [False, True]:
            cohort = self.cohort()
            first = self.launch('invocation-1')['payload']
            second = self.launch('invocation-2', 'critic')['payload']
            if not same_delivery:
                second['itemId'] = 'FS-GG/test#2'
                second['brief'] = second['brief'].replace('test#1', 'test#2')
            first['threadId'] = second['threadId'] = 'reused-thread'
            cohort['launches'] = [self.envelope(first), self.envelope(second)]
            cohort['observations'] = [self.usage('reused-thread', 'model-1', 'medium'), self.usage('reused-thread', 'model-2', 'high', 'response-2')]
            with self.assertRaisesRegex(ValueError, 'ambiguous shared thread'):
                m.project(cohort)

    def test_summary_encodes_provider_separators_and_both_execution_evidence_levels(self):
        row = dict(role='worker', model='model; critic: other', effort='medium',
                   launchConfiguration=dict(model='launch: model; alias', effort='medium'))
        summary = m.dispatch.execution_summary([row], 'model')
        self.assertEqual(['worker: launch: model; alias [launch-confirmed]', 'worker: model; critic: other [provider-observed]'], json.loads(summary))

    def test_recorded_projection_scope_uses_native_comment_and_never_requested_model(self):
        snapshot = self.original()
        template = dict(selectedIssues=[dict(id='native-1', repository='FS-GG/test', number=1, slice='fixture')],
            views=[], initialPlanning=[], learn=dict(schema='fs.gg.board-v2.learn-annotations/1', fields=[dict(name=name, fieldId='field-' + str(index)) for index, name in enumerate(m.NAMES)], assignments=[]))
        scope = m.dispatch.projection_scope(snapshot, 'https://github.com/FS-GG/test/issues/1#issuecomment-17', template)
        self.assertEqual('Unknown', scope['learn']['assignments'][0]['values']['Actual models'])
        self.assertEqual('Unknown', scope['learn']['assignments'][0]['values']['Hardness'])
        self.assertEqual([], template['learn']['assignments'])
        with self.assertRaisesRegex(ValueError, 'owning-issue'):
            m.dispatch.projection_scope(snapshot, 'https://github.com/FS-GG/test/issues/2#issuecomment-17', template)
        with self.assertRaisesRegex(ValueError, 'cannot select'):
            m.dispatch.projection_scope(snapshot, 'https://github.com/FS-GG/test/issues/1#issuecomment-17', dict(template, views=[{}]))

    def template(self):
        return dict(selectedIssues=[dict(id='native-1', repository='FS-GG/test', number=1, slice='fixture')],
            views=[], initialPlanning=[], learn=dict(schema='fs.gg.board-v2.learn-annotations/1',
                fields=[dict(name=name, fieldId='field-' + str(index)) for index, name in enumerate(m.NAMES)], assignments=[]))

    def test_unvalidated_native_policy_retains_original_bytes_and_known_execution(self):
        for body in ['spelling', 'files implement migration render tests', 'atomic concurrency installed']:
            with self.subTest(body=body):
                cohort = self.cohort()
                original = self.original(body)
                # Legacy classifications without the new policy are retained byte for byte.
                original.pop('hardnessProjection', None)
                envelope = self.envelope(original)
                original_bytes = m.dispatch.canonical(original)
                cohort['classifications'] = [envelope]
                cohort['launches'] = [self.launch(role='analyst'), self.launch('report-1', 'report')]
                cohort['observations'] = [self.usage(effort='high'), self.usage('report-1', 'report-model', 'medium', 'r2')]
                result = m.project(cohort)
                record = result['records'][0]
                self.assertEqual(original_bytes, m.dispatch.canonical(record['originalClassification']))
                self.assertEqual(envelope['sha256'], record['classificationSource']['sha256'])
                self.assertEqual(original['hardnessCategory'], record['hardnessCategory'])
                self.assertEqual(0, result['coverage']['projectedNativeHardnessKnown'])
                values = m.assignment(record, 'https://github.com/FS-GG/test/issues/1#issuecomment-17')['values']
                self.assertEqual('Unknown', values['Hardness'])
                self.assertIn('analyst: observed-worker [provider-observed]', values['Actual models'])
                self.assertIn('report: report-model [provider-observed]', values['Actual models'])
                self.assertIn('analyst: high [provider-observed]', values['Actual effort'])
                native = dict(itemId=record['itemId'], body='recording preimage', capturedAt='2026-01-01T00:00:00Z',
                    sourceRevision='fixture', producer='fixture')
                with tempfile.TemporaryDirectory() as directory:
                    out = Path(directory) / 'intent'
                    m.dispatch.prepare_completion(native, record, out)
                    snapshot = json.loads((out / 'snapshot.json').read_bytes())
                    scope = m.dispatch.projection_scope(snapshot, values['Execution provenance'], self.template())
                    self.assertEqual(values, scope['learn']['assignments'][0]['values'])
                    self.assertEqual(m.dispatch.canonical(record), m.dispatch.canonical(snapshot['completion']))

    def test_policy_disposition_is_digest_bound_and_cannot_enable_known_labels(self):
        policy = dict(schema='fsgg.learn-hardness-projection/1', mode='withhold-unvalidated',
            dispositionReference='https://github.com/FS-GG/.github/issues/5273#issuecomment-17')
        cohort = dict(self.cohort(), hardnessProjection=policy)
        record = m.project(cohort)['records'][0]
        self.assertEqual(policy, record['hardnessProjection'])
        self.assertIn(policy['dispositionReference'], m.comment_for(record))
        for bad in [dict(policy, mode='validated'), dict(policy, mode='provisional'),
                    dict(policy, nativeCategory='High'), dict(policy, dispositionReference='fixture:Root'), {}]:
            with self.subTest(policy=bad), self.assertRaisesRegex(ValueError, 'hardness'):
                m.project(dict(cohort, hardnessProjection=bad))
        snapshot = self.original()
        snapshot['hardnessProjection'] = dict(policy, mode='validated')
        with self.assertRaisesRegex(ValueError, 'known native labels'):
            m.dispatch.projection_scope(snapshot, 'https://github.com/FS-GG/test/issues/1#issuecomment-17', self.template())
        for url in ['https://github.com/FS-GG/test/issues/2#issuecomment-17',
                    'https://github.com/FS-GG/other/issues/1#issuecomment-17',
                    'https://github.com/FS-GG/test/issues/1#issuecomment-0']:
            with self.assertRaisesRegex(ValueError, 'owning-issue'):
                m.dispatch.projection_scope(self.original(), url, self.template())

    def test_completion_policy_mismatch_and_snapshot_tampering_refuse(self):
        record = m.project(self.cohort())['records'][0]
        record['hardnessProjection']['dispositionReference'] = 'https://github.com/FS-GG/.github/issues/5273#issuecomment-17'
        snapshot = dict(schema='fsgg.learn-completion/1', itemId=record['itemId'], completion=record)
        with self.assertRaisesRegex(ValueError, 'policy mismatch'):
            m.dispatch.projection_scope(snapshot, 'https://github.com/FS-GG/test/issues/1#issuecomment-17', self.template())
        cohort = self.cohort()
        envelope = self.envelope(self.original())
        envelope['payload']['scores']['D1'] = 3
        cohort['classifications'] = [envelope]
        with self.assertRaisesRegex(ValueError, 'digest'):
            m.project(cohort)

    def test_completion_record_reuses_durable_comment_custody_without_rescoring(self):
        record = m.project(self.cohort())['records'][0]
        native = dict(itemId=record['itemId'], body='Current post-work issue body contains upgrade concurrency',
            capturedAt='2026-01-01T00:00:00Z', sourceRevision='fixture:current-native-body', producer='fixture')
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory) / 'completion'
            m.dispatch.prepare_completion(native, record, out)
            snapshot = json.loads((out / 'snapshot.json').read_text())
            self.assertEqual('Unknown', snapshot['completion']['hardnessCategory'])
            self.assertNotIn('scores', snapshot)
            comment = dict(id=17, body=(out / 'comment.md').read_text(), html_url='fixture:comment')
            args = SimpleNamespace(engine='fixture', config='fixture', producer='fixture', binding_digest='fixture', state_root=Path(directory) / 'custody')
            with patch.object(m.dispatch, 'read_issue', return_value={'body': native['body']}), \
                 patch.object(m.dispatch, 'read_comments', return_value=[comment]), \
                 patch.object(m.dispatch, 'read_comment', return_value=comment), \
                 patch.object(m.dispatch, 'post_comment') as post, \
                 patch.object(m.dispatch, 'submit_telemetry', return_value='receipt') as submit:
                m.dispatch.record(out, args)
                m.dispatch.record(out, args)
                post.assert_not_called()
                self.assertEqual(1, submit.call_count)
