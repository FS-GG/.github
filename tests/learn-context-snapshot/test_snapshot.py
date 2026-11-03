"""Synthetic controls only: no native requests, raters, historical outcomes or shared Git writes."""
import copy
import datetime as dt
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
from types import SimpleNamespace
import contextlib
import io

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('context_snapshot', ROOT / 'scripts/learn-context-snapshot.py')
m = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(m)


def row(number, created='2026-02-01T00:00:00Z', parent=None, private=False):
    return {'node_id': 'I' + str(number), 'repository': 'ORG/repo', 'number': number,
            'created_at': created, 'private': private, 'kind': 'issue',
            'parent_issue_url': parent, 'body': 'not a selection input', 'state': 'open'}


def development(item):
    return [{'nodeId': item['node_id'], 'historicalItem': 'ORG/repo#' + str(item['number']),
             'parent': item['parent_issue_url'], 'createdAt': item['created_at']}]


def connection(nodes, complete=True):
    return {'nodes': nodes, 'totalCount': len(nodes), 'pageInfo': {'hasNextPage': not complete, 'endCursor': None}}


def issue():
    return {'id': 'I2', 'number': 2, 'title': 'Later title', 'body': 'Later body',
            'createdAt': '2026-02-01T00:00:00Z', 'lastEditedAt': '2026-02-02T00:00:00Z',
            'repository': {'nameWithOwner': 'ORG/repo'},
            'userContentEdits': connection([
                {'id': 'E1', 'createdAt': '2026-02-01T00:00:00Z', 'editedAt': '2026-02-01T00:00:00Z', 'deletedAt': None, 'diff': 'Original task'},
                {'id': 'E2', 'createdAt': '2026-02-02T00:00:00Z', 'editedAt': '2026-02-02T00:00:00Z', 'deletedAt': None, 'diff': 'Later body'}]),
            'timelineItems': connection([{'id': 'R1', '__typename': 'RenamedTitleEvent',
                                         'createdAt': '2026-02-02T00:00:00Z',
                                         'previousTitle': 'Original title', 'currentTitle': 'Later title'}]),
            'comments': connection([
                {'id': 'C1', 'createdAt': '2026-02-01T00:00:00Z', 'updatedAt': '2026-02-03T00:00:00Z', 'body': 'Later edited answer'},
                {'id': 'C2', 'createdAt': '2026-02-02T00:00:00Z', 'updatedAt': '2026-02-02T00:00:00Z', 'body': 'Future closing answer'}])}


CASE = {'caseId': 'context-001', 'nodeId': 'I2', 'repository': 'ORG/repo', 'number': 2,
        'boundaryKind': 'item-creation', 'cutoffAt': '2026-02-01T00:00:00Z'}


class SelectionTests(unittest.TestCase):
    def test_parent_component_private_and_time_block_exclusions(self):
        first = row(1, '2026-01-05T00:00:00Z', 'https://api.github.com/repos/ORG/repo/issues/10')
        rows = [first, row(10), row(3, parent='https://api.github.com/repos/ORG/repo/issues/10'),
                row(4, private=True), row(5, '2026-01-06T00:00:00Z'), row(6), row(7)]
        selected, coverage = m.choose_cases(rows, development(first), count=2)
        self.assertEqual({r['number'] for r in selected}, {6, 7})
        self.assertEqual(coverage['excluded'], {'development-component': 3, 'private-or-unknown': 1,
                                               'development-utc-iso-week': 1})

    def test_outcomes_text_and_row_order_do_not_select(self):
        first = row(1, '2026-01-05T00:00:00Z')
        rows = [first] + [row(i) for i in range(2, 25)]
        expected = m.choose_cases(rows, development(first), count=16)[0]
        changed = copy.deepcopy(list(reversed(rows)))
        for item in changed:
            item.update(state='closed', state_reason='not_planned', closed_at='2026-04-01T00:00:00Z',
                        body='Hardness High; runtime 9000; later accepted result', title='Changed outcome')
        self.assertEqual(expected, m.choose_cases(changed, development(first), count=16)[0])
        self.assertEqual(len(expected), 16)

    def test_missing_or_insufficient_support_refuses(self):
        first = row(1, '2026-01-05T00:00:00Z')
        with self.assertRaisesRegex(m.Unavailable, 'insufficient'):
            m.choose_cases([first], development(first))
        bad = development(first); bad[0]['createdAt'] = '2026-01-06T00:00:00Z'
        with self.assertRaisesRegex(m.Unavailable, 'creation-time-mismatch'):
            m.choose_cases([first, row(2)], bad, count=1)


class TemporalTests(unittest.TestCase):
    def test_later_body_title_comments_do_not_enter_task(self):
        task, provenance = m.task_at_boundary(issue(), CASE, m.history_module())
        self.assertEqual(task, {'title': 'Original title', 'body': 'Original task', 'comments': []})
        self.assertEqual(provenance['futureCommentsOmitted'], 1)
        self.assertEqual(provenance['contextGaps'], [{'kind': 'earlier-comment-revision-unavailable'}])
        self.assertNotIn(b'Later', m.safe_task_text(task))

    def test_truncated_missing_and_conflicting_revisions_refuse(self):
        bad = issue(); bad['userContentEdits']['pageInfo']['hasNextPage'] = True
        with self.assertRaises(ValueError): m.task_at_boundary(bad, CASE, m.history_module())
        bad = issue(); bad['userContentEdits']['nodes'][0]['deletedAt'] = '2026-02-04T00:00:00Z'
        with self.assertRaises(ValueError): m.task_at_boundary(bad, CASE, m.history_module())
        bad = issue(); bad['timelineItems']['nodes'][0]['currentTitle'] = 'Does not match'
        with self.assertRaisesRegex(m.Unavailable, 'conflicting-rename'): m.task_at_boundary(bad, CASE, m.history_module())
        bad = issue(); bad['comments']['pageInfo']['hasNextPage'] = True
        with self.assertRaises(ValueError): m.task_at_boundary(bad, CASE, m.history_module())

    def test_backdated_commit_never_overrides_future_server_visibility(self):
        candidate = {'node_id': 'P1', 'pullRequest': {'merged_at': '2026-02-02T00:00:00Z'}}
        witness = {'id': 'P1', 'repository': {'nameWithOwner': 'ORG/repo'}, 'baseRefName': 'main',
                   'mergedAt': '2026-02-02T00:00:00Z', 'authoredDate': '2020-01-01T00:00:00Z',
                   'mergeCommit': {'oid': 'a' * 40, 'tree': {'oid': 'b' * 40}}}
        with self.assertRaisesRegex(m.Unavailable, 'visible-after-cutoff'):
            m.verify_checkpoint(witness, CASE, candidate, 'main')
        witness['mergedAt'] = candidate['pullRequest']['merged_at'] = '2026-01-30T00:00:00Z'
        result = m.verify_checkpoint(witness, CASE, candidate, 'main')
        self.assertEqual(result['lagSeconds'], 172800)
        self.assertIn('not established', result['interpretation'])
        witness['baseRefName'] = 'other'
        with self.assertRaisesRegex(m.Unavailable, 'other-branch'):
            m.verify_checkpoint(witness, CASE, candidate, 'main')

    def test_only_pre_cutoff_merge_candidates(self):
        rows = [{'kind': 'pull_request', 'repository': 'ORG/repo', 'node_id': 'P1',
                 'pullRequest': {'merged_at': '2026-02-02T00:00:00Z'}},
                {'kind': 'pull_request', 'repository': 'ORG/repo', 'node_id': 'P2',
                 'pullRequest': {'merged_at': '2026-01-31T00:00:00Z'}}]
        self.assertEqual([r['node_id'] for r in m.checkpoint_candidates(rows, CASE)], ['P2'])


class ExportTests(unittest.TestCase):
    def test_paths_symlinks_collisions_and_bounds(self):
        for path in ['/absolute', '../outside', 'nested/../outside', '.git/config', 'nested/.GIT/objects', 'bad\\path', 'line\nname']:
            with self.subTest(path=path), self.assertRaises(m.Unavailable): m.safe_path(path)
        def entry(path='a', mode='100644', size='1', kind='blob'):
            return f'{mode} {kind} {"a" * 40} {size}\t{path}\0'.encode()
        for payload in [entry(mode='120000'), entry('a') + entry('A'), entry(size='11')]:
            with self.assertRaises(m.Unavailable): m.tree_entries(payload, 10, 10, 10)
        with self.assertRaisesRegex(m.Unavailable, 'file-count-bound'):
            m.tree_entries(entry('a') + entry('b'), 1, 10, 10)
        with self.assertRaisesRegex(m.Unavailable, 'total-blob-byte-bound'):
            m.tree_entries(entry('a', size='6') + entry('b', size='6'), 10, 10, 10)

    def test_future_gitlink_is_opaque_not_resolved(self):
        payload = f'100644 blob {"a" * 40} 1\ta\0'.encode() + f'160000 commit {"f" * 40} -\tdependency\0'.encode()
        entries, gaps = m.tree_entries(payload, 10, 10, 10)
        self.assertEqual([r['path'] for r in entries], ['a'])
        self.assertEqual(gaps, [{'kind': 'unresolved-gitlink', 'path': 'dependency', 'object': 'f' * 40}])

    def test_real_git_export_excludes_future_objects_and_preserves_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); repo = root / 'repo'; repo.mkdir()
            env = dict(os.environ, GIT_AUTHOR_NAME='Fixture', GIT_AUTHOR_EMAIL='fixture@example.invalid',
                       GIT_COMMITTER_NAME='Fixture', GIT_COMMITTER_EMAIL='fixture@example.invalid',
                       GIT_CONFIG_GLOBAL='/dev/null', GIT_CONFIG_NOSYSTEM='1')
            def git(*arguments):
                return subprocess.check_output(['git', '-C', str(repo), *arguments], env=env, stderr=subprocess.DEVNULL).decode().strip()
            git('init', '-q'); (repo / 'task.txt').write_bytes(b'Original source\n'); (repo / 'image.bin').write_bytes(b'\0\xff')
            (repo / 'asset.lfs').write_bytes(b'version https://git-lfs.github.com/spec/v1\noid sha256:' + b'f' * 64 + b'\nsize 999999999\n')
            git('add', '.'); git('commit', '-qm', 'Historical fixture')
            commit, tree = git('rev-parse', 'HEAD'), git('rev-parse', 'HEAD^{tree}')
            (repo / 'task.txt').write_text('Future accepted fix'); (repo / 'answer.txt').write_text('Future answer')
            git('add', '.'); git('commit', '-qm', 'Future fixture')
            entries, gaps = m.export_tree(repo, {'commit': commit, 'tree': tree}, root / 'export', m.Budget())
            self.assertEqual((root / 'export/task.txt').read_bytes(), b'Original source\n')
            self.assertFalse((root / 'export/answer.txt').exists())
            self.assertFalse((root / 'export/.git').exists())
            self.assertEqual(next(r for r in entries if r['path'] == 'image.bin')['contentKind'], 'binary')
            self.assertIn({'kind': 'dependency-environment-not-reconstructed'}, gaps)
            self.assertIn({'kind': 'unmaterialized-lfs-object', 'path': 'asset.lfs'}, gaps)
            with self.assertRaisesRegex(m.Unavailable, 'native-local-tree-mismatch'):
                m.export_tree(repo, {'commit': commit, 'tree': '0' * 40}, root / 'bad', m.Budget())

    def test_corrupt_blob_refuses_before_manifest_can_be_sealed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            blob = m.object_digest('blob', b'original')
            raw_tree = b'100644 task.txt\0' + bytes.fromhex(blob)
            tree = m.object_digest('tree', raw_tree)
            commit = b'tree ' + tree.encode() + b'\n\nfixture\n'
            checkpoint = {'commit': m.object_digest('commit', commit), 'tree': tree}
            listing = f'100644 blob {blob} 8\ttask.txt\0'.encode()
            with mock.patch.object(m, 'git_read', side_effect=[commit, listing, raw_tree, b'corrupte']):
                with self.assertRaisesRegex(m.Unavailable, 'blob-hash-mismatch'):
                    m.export_tree(root, checkpoint, root / 'output', m.Budget())
            self.assertFalse((root / 'output/task.txt').exists())
            with mock.patch.object(m, 'git_read', side_effect=[commit, listing, b'corrupt tree']):
                with self.assertRaisesRegex(m.Unavailable, 'tree-object-hash-mismatch'):
                    m.export_tree(root, checkpoint, root / 'bad-tree', m.Budget())
            self.assertFalse((root / 'bad-tree').exists())


class BoundTests(unittest.TestCase):
    def test_request_and_output_bounds(self):
        budget = m.Budget(requests=1); budget.charge()
        with self.assertRaisesRegex(m.Unavailable, 'request-bound'): budget.charge()
        with self.assertRaisesRegex(m.Unavailable, 'output-bound'):
            m.run_bounded([sys.executable, '-c', 'print("a"*20000)'], seconds=2, max_bytes=100)

    def test_launch_is_not_a_supported_command(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            m.parser().parse_args(['launch'])


class BundleTests(unittest.TestCase):
    def test_complete_mocked_acquisition_separates_future_controller_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); inventory = root / 'inventory'; inventory.mkdir()
            (inventory / 'manifest.json').write_text('{}')
            first = row(1, '2026-01-05T00:00:00Z'); rows = [first, row(2)]
            candidate = {'kind': 'pull_request', 'node_id': 'P1', 'repository': 'ORG/repo',
                         'pullRequest': {'merged_at': '2026-01-30T00:00:00Z'}}
            rows.append(candidate)
            devpath = root / 'development.json'; devpath.write_text(json.dumps(development(first)))
            cases, coverage = m.choose_cases(rows, development(first), count=1)
            selection = {'schema': m.SELECTION_SCHEMA, 'seed': m.DEFAULT_SEED,
                         'inventorySha256': m.digest((inventory / 'manifest.json').read_bytes()),
                         'developmentCohortSha256': m.digest(devpath.read_bytes()),
                         'cases': cases, 'coverage': coverage}
            selection['sha256'] = m.digest(m.canonical(selection))
            selectionpath = root / 'selection.json'; selectionpath.write_bytes(m.canonical(selection))
            env = dict(os.environ, GIT_AUTHOR_NAME='Fixture', GIT_AUTHOR_EMAIL='fixture@example.invalid',
                       GIT_COMMITTER_NAME='Fixture', GIT_COMMITTER_EMAIL='fixture@example.invalid',
                       GIT_CONFIG_GLOBAL='/dev/null', GIT_CONFIG_NOSYSTEM='1')
            repo = root / 'repo'; repo.mkdir()
            def git(*arguments):
                return subprocess.check_output(['git', '-C', str(repo), *arguments], env=env,
                                               stderr=subprocess.DEVNULL).decode().strip()
            git('init', '-q'); (repo / 'task.txt').write_text('Original source')
            git('add', '.'); git('commit', '-qm', 'Historical fixture')
            commit, tree = git('rev-parse', 'HEAD'), git('rev-parse', 'HEAD^{tree}')
            witness = {'id': 'P1', 'repository': {'nameWithOwner': 'ORG/repo'}, 'baseRefName': 'main',
                       'mergedAt': '2026-01-30T00:00:00Z',
                       'mergeCommit': {'oid': commit, 'tree': {'oid': tree}}}
            args = SimpleNamespace(selection=selectionpath, inventory=inventory, development_cohort=devpath,
                                   case_id='context-001', repository=repo, branch='main', output=root / 'result',
                                   wall_seconds=120, max_requests=2, max_files=10,
                                   max_blob_bytes=1024, max_total_bytes=4096)
            h = m.history_module()
            responses = iter([{'node': issue()}, {'node': witness}])
            def native(query, budget, controller):
                budget.charge(); response = next(responses)
                m.write_new(controller / f'mocked-native-{budget.charged}.json', m.canonical(response))
                return response
            with mock.patch.object(h, 'inventory', return_value=({}, rows, [])), \
                 mock.patch.object(m, 'history_module', return_value=h), \
                 mock.patch.object(m, 'native_read', side_effect=native), \
                 contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(m.collect_case(args), 0)
            safe = json.loads((args.output / 'rater/manifest.json').read_text())
            controller = json.loads((args.output / 'controller/result.json').read_text())
            self.assertFalse(safe['ratingLaunchAllowed'])
            self.assertFalse(safe['exactHistoricalHead'])
            self.assertEqual(safe['checkpointLagSeconds'], 172800)
            self.assertEqual(safe['taskRedactionVersion'], 'github-identity-links/1')
            self.assertEqual(controller['nativeRequestsCharged'], 2)
            self.assertEqual((args.output / 'rater/task.md').read_text(), '# Original title\n\nOriginal task')
            self.assertNotIn('Future closing answer', ''.join(p.read_text() for p in (args.output / 'rater').rglob('*') if p.is_file()))
            self.assertIn('Future closing answer', (args.output / 'controller/mocked-native-1.json').read_text())
            self.assertFalse((args.output / 'rater/source/.git').exists())
            # An existing original output cannot be overwritten or replayed as a fresh case.
            with mock.patch.object(h, 'inventory', return_value=({}, rows, [])), \
                 mock.patch.object(m, 'history_module', return_value=h), \
                 mock.patch.object(m, 'native_read', side_effect=AssertionError('must not send')):
                with self.assertRaises(FileExistsError): m.collect_case(args)


if __name__ == '__main__':
    unittest.main()
