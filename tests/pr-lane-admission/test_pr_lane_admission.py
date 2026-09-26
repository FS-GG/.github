import json
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
    def invoke(self, state, body="First line\nSecond line\n"):
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
                str(TOOL),
                "--repo",
                "FS-GG/example",
                "--campaign-id",
                "campaign-7",
                "--dependency-chain-id",
                "chain-3",
                "--head",
                "feature/combined",
                "--head-sha",
                EXPECTED_SHA,
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

    def test_refuses_third_managed_pr_in_campaign(self):
        state = {
            "head_sha": EXPECTED_SHA,
            "open_pages": [[
                {"number": 8, "html_url": "https://example.test/pull/8", "body": managed_body("campaign-7", "chain-1")},
                {"number": 9, "html_url": "https://example.test/pull/9", "body": managed_body("campaign-7", "chain-2")},
            ]],
        }
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode, completed.stderr)
        self.assertEqual("campaign-open-pr-cap", result["reason"])
        self.assertEqual(2, result["openManagedPullRequestCount"])
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


if __name__ == "__main__":
    unittest.main()
