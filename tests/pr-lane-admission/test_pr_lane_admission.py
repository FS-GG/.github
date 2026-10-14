import json
import argparse
import importlib.util
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/pr-lane-admission.py"
FAKE_GH = ROOT / "tests/pr-lane-admission/fake_gh.py"
EXPECTED_SHA = "a" * 40


def managed_body(campaign: str, chain: str) -> str:
    return (
        "Existing PR body\n\n"
        f"<!-- fsgg-pr-lane-admission:v1 campaign={campaign} chain={chain} -->\n"
    )


class PullRequestLaneAdmissionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        def inert(event, args):
            if event == "socket.connect":
                raise RuntimeError("offline qualification refuses network")
            if event == "subprocess.Popen":
                argv = args[1]
                executable = pathlib.Path(argv[0]).name
                if executable != "git" and not (argv[0] == sys.executable and str(TOOL) in argv) and argv[0] != str(FAKE_GH):
                    raise RuntimeError("offline qualification refuses nonfixture child")
        sys.addaudithook(inert)

    def invoke(self, state, body="First line\nSecond line\n", branch="feature/combined", head=EXPECTED_SHA):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        directory = pathlib.Path(temporary.name)
        state_path = directory / "state.json"
        log_path = directory / "calls.jsonl"
        body_path = directory / "body.md"
        state_path.write_text(json.dumps(state), encoding="utf-8")
        body_path.write_text(body, encoding="utf-8")
        environment = dict(os.environ)
        environment.update(FAKE_GH_STATE=str(state_path), FAKE_GH_LOG=str(log_path))
        completed = subprocess.run(
            [
                sys.executable,
                "-I", "-S", "-B",
                str(TOOL),
                "--repo",
                "FS-GG/example",
                "--campaign-id",
                "campaign-7",
                "--dependency-chain-id",
                "chain-3",
                "--head",
                branch,
                "--head-sha",
                head,
                "--base",
                "main",
                "--title",
                "Deliver coherent feature",
                "--body-file",
                str(body_path),
                "--gh",
                str(FAKE_GH),
            ],
            env=environment,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        calls = []
        if log_path.exists():
            calls = [json.loads(line) for line in log_path.read_text().splitlines()]
        return completed, json.loads(completed.stdout), calls

    def test_refuses_when_same_dependency_chain_is_open_across_paginated_results(self):
        state = {
            "head_sha": EXPECTED_SHA,
            "open_pages": [
                [{"number": 1, "html_url": "https://example.test/pull/1", "body": "unmanaged"}],
                [{"number": 2, "html_url": "https://example.test/pull/2", "body": managed_body("campaign-7", "chain-3")}],
            ],
        }
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode, completed.stderr)
        self.assertEqual("same-chain-open", result["reason"])
        self.assertEqual([2], [pull["number"] for pull in result["matchingPullRequests"]])
        self.assertEqual(1, len(calls))
        self.assertIn("--paginate", calls[0]["arguments"])

    def test_refuses_third_managed_pr_across_different_campaigns_in_repo(self):
        state = {
            "head_sha": EXPECTED_SHA,
            "open_pages": [[
                {"number": 8, "html_url": "https://example.test/pull/8", "body": managed_body("campaign-1", "chain-1")},
                {"number": 9, "html_url": "https://example.test/pull/9", "body": managed_body("campaign-2", "chain-2")},
            ]],
        }
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode, completed.stderr)
        self.assertEqual("repository-open-pr-cap", result["reason"])
        self.assertEqual(2, result["openManagedPullRequestCount"])
        self.assertEqual(1, len(calls))

    def test_same_chain_id_in_a_different_campaign_does_not_collide(self):
        state = {
            "head_sha": EXPECTED_SHA,
            "open_pages": [[
                {"number": 10, "html_url": "https://example.test/pull/10", "body": managed_body("campaign-6", "chain-3")},
            ]],
        }
        completed, result, calls = self.invoke(state)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("created", result["action"])
        self.assertEqual(1, result["openManagedPullRequestCount"])
        self.assertEqual("POST", calls[-1]["arguments"][2])

    def test_stale_routine_head_does_not_release_campaign_chain_reservation(self):
        body = managed_body('campaign-7', 'chain-3') + (
            '<!-- fsgg:routine-development/v1 head=' + 'b' * 40 + ' operation=source-change -->\n')
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': body,
            'head': {'ref': 'routine/source', 'sha': EXPECTED_SHA}}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual('same-chain-open', result['reason'])
        self.assertEqual(1, result['openManagedPullRequestCount'])
        self.assertEqual(1, result['openNativePullRequestCount'])
        self.assertEqual(EXPECTED_SHA, result['nativeOpenPullRequests'][0]['headSha'])
        self.assertEqual(1, len(calls))

    def test_erased_lane_marker_on_routine_branch_cannot_manufacture_third_slot(self):
        routine = '<!-- fsgg:routine-development/v1 head=' + EXPECTED_SHA + ' operation=source-change -->'
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': routine,
            'head': {'ref': 'routine/source', 'sha': EXPECTED_SHA}},
            {'number': 12, 'body': managed_body('other', 'other')}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual('repository-open-pr-cap', result['reason'])
        self.assertEqual(2, result['openManagedPullRequestCount'])
        self.assertEqual(2, result['openNativePullRequestCount'])
        self.assertEqual([11, 12], [p['number'] for p in result['matchingPullRequests']])
        self.assertEqual(1, len(calls))

    def test_missing_or_edited_routine_identity_blocks_without_guessing_chain(self):
        bodies = [None, '', 'Rewritten prose', '<!-- fsgg-pr-lane-admission:v1 campaign=campaign-7 -->',
                  managed_body('campaign-7', 'chain-3') * 2,
                  managed_body('campaign-7', 'chain-3') + managed_body('other', 'other')]
        for body in bodies:
            state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': body,
                'head': {'ref': 'routine/source', 'sha': EXPECTED_SHA}}]]}
            with self.subTest(body=body):
                completed, result, calls = self.invoke(state)
                self.assertEqual(3, completed.returncode)
                self.assertEqual('managed-reservation-identity-unknown', result['reason'])
                self.assertEqual(1, result['openManagedPullRequestCount'])
                self.assertEqual(1, result['openNativePullRequestCount'])
                self.assertEqual(1, len(calls))

    def test_damaged_lane_footprint_blocks_even_on_generic_feature_branch(self):
        for body in ('<!-- fsgg-pr-lane-admission', '<!-- fsgg-pr-lane-admission edited metadata -->',
                     managed_body('one', 'one') + managed_body('two', 'two')):
            state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': body,
                'head': {'ref': 'feature/source', 'sha': EXPECTED_SHA}}]]}
            with self.subTest(body=body):
                completed, result, calls = self.invoke(state)
                self.assertEqual(3, completed.returncode)
                self.assertEqual('managed-reservation-identity-unknown', result['reason'])
                self.assertEqual(1, len(calls))

    def test_native_population_reports_genuine_unmanaged_prs_without_expanding_cap(self):
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': '',
            'head': {'ref': 'feature/unmanaged', 'sha': EXPECTED_SHA}}],
            [{'number': 12, 'body': 'Discuss fsgg-pr-lane-admission without a reservation marker',
              'head': {'ref': 'feature/other', 'sha': 'b' * 40}}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(0, completed.returncode)
        self.assertEqual(0, result['openManagedPullRequestCount'])
        self.assertEqual(2, result['openNativePullRequestCount'])
        self.assertEqual([11, 12], [p['number'] for p in result['nativeOpenPullRequests']])
        self.assertEqual('POST', calls[-1]['arguments'][2])

    def test_valid_lane_identity_remains_reserved_when_routine_body_is_malformed(self):
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11,
            'body': managed_body('campaign-7', 'chain-3') + '<!-- fsgg:routine-development/v1 broken',
            'head': {'ref': 'routine/source', 'sha': 'b' * 40}}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual('same-chain-open', result['reason'])
        self.assertEqual(1, len(calls))

    def test_refuses_when_head_moved_and_does_not_post(self):
        state = {"head_sha": "b" * 40, "open_pages": [[]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode, completed.stderr)
        self.assertEqual("head-moved", result["reason"])
        self.assertEqual("b" * 40, result["observedHeadSha"])
        self.assertEqual(["GET", "GET"], [call["arguments"][2] for call in calls])

    def test_creates_with_json_input_and_preserves_body_before_marker(self):
        original_body = "Heading\n\nParagraph with `code` and ünicode.\n"
        state = {"head_sha": EXPECTED_SHA, "open_pages": [[], []]}
        completed, result, calls = self.invoke(state, body=original_body)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("created", result["action"])
        self.assertEqual(42, result["number"])
        self.assertEqual(3, len(calls))
        post = calls[-1]
        self.assertEqual("POST", post["arguments"][2])
        payload = json.loads(post["stdin"])
        self.assertTrue(payload["body"].startswith(original_body))
        self.assertEqual(
            original_body
            + "\n<!-- fsgg-pr-lane-admission:v1 campaign=campaign-7 chain=chain-3 -->\n",
            payload["body"],
        )
        self.assertEqual("feature/combined", payload["head"])

    def test_routine_creation_real_gate_and_idempotent_preparation(self):
        spec = importlib.util.spec_from_file_location("canonical_lane", TOOL)
        lane = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(lane)
        with tempfile.TemporaryDirectory() as directory:
            def git(*args):
                return subprocess.check_output(["git", "-C", directory, *args], text=True).strip()
            git("init", "-q")
            git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-qm", "base")
            base = git("rev-parse", "HEAD")
            pathlib.Path(directory, "fixture.txt").write_text("nonempty source delta\n")
            git("add", "fixture.txt")
            git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "-qm", "candidate")
            head = git("rev-parse", "HEAD")
            completed, result, calls = self.invoke({"head_sha": head, "open_pages": [[]]}, branch="routine/fixture", head=head)
            self.assertEqual(0, completed.returncode, completed.stderr)
            body = json.loads(calls[-1]["stdin"])["body"]
            self.assertEqual(1, body.count("fsgg:routine-development/v1"))
            self.assertEqual(body, lane.prepare_body(body, "routine/fixture", head, "campaign-7", "chain-3"))
            gate = lane.routine_validator()
            args = argparse.Namespace(base_sha=base, head_sha=head, head_ref="routine/fixture",
                routine_policy=str(ROOT / ".fsgg/routine-development.json"), routine_policy_ref=None)
            previous = os.getcwd()
            try:
                os.chdir(directory)
                self.assertIsNone(gate.evaluate_routine(args, body)[0])
                self.assertEqual("routine-missing", gate.evaluate_routine(args, "Original missing marker")[0])
                self.assertEqual("routine-changed-head", gate.evaluate_routine(args, body.replace(head, "f" * 40))[0])
                args.base_sha = head
                self.assertEqual("routine-empty", gate.evaluate_routine(args, body)[0])
            finally:
                os.chdir(previous)

    def test_routine_invalid_metadata_refuses_without_any_remote_call(self):
        valid = f"<!-- fsgg:routine-development/v1 head={EXPECTED_SHA} operation=source-change -->"
        cases = [(valid + valid, "routine/fixture", EXPECTED_SHA),
                 (valid.replace("operation=source-change", "operation=publish"), "routine/fixture", EXPECTED_SHA),
                 (valid.replace("head=", "head=" + "b" * 40 + " head="), "routine/fixture", EXPECTED_SHA),
                 (valid.replace(EXPECTED_SHA, "b" * 40), "routine/fixture", EXPECTED_SHA),
                 ("<!-- fsgg:routine-development/v1", "routine/fixture", EXPECTED_SHA),
                 ("", "routine/INVALID", EXPECTED_SHA), ("", "routine/fixture", "a" * 64)]
        for body, branch, head in cases:
            with self.subTest(body=body, branch=branch):
                completed, result, calls = self.invoke({"head_sha": head, "open_pages": [[]]}, body, branch, head)
                self.assertNotEqual(0, completed.returncode)
                self.assertEqual([], calls)

    def test_existing_docs_marker_is_preserved_and_lane_conflicts_refuse(self):
        body = f"Prose\n<!-- fsgg:routine-development/v1 head={EXPECTED_SHA} operation=internal-docs extra=value -->\n"
        completed, _, calls = self.invoke({"head_sha": EXPECTED_SHA, "open_pages": [[]]}, body, "routine/docs")
        self.assertEqual(0, completed.returncode)
        self.assertTrue(json.loads(calls[-1]["stdin"])["body"].startswith(body))
        conflict = body + managed_body("other", "chain")
        completed, _, calls = self.invoke({"head_sha": EXPECTED_SHA, "open_pages": [[]]}, conflict, "routine/docs")
        self.assertNotEqual(0, completed.returncode)
        self.assertEqual([], calls)
        completed, _, calls = self.invoke({'head_sha':'a' * 64,'open_pages':[[]]}, 'Generic prose\n', 'feature/generic', 'a' * 64)
        self.assertEqual(0, completed.returncode)
        self.assertNotIn('fsgg:routine-development',json.loads(calls[-1]['stdin'])['body'])


if __name__ == "__main__":
    unittest.main()
