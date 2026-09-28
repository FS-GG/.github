#!/usr/bin/env python3

import datetime as dt
import importlib.util
import json
import sys
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


prestate = load("seed_prestate", ROOT / "scripts/gs2-09-7-seed-prestate.py")
entry = load("seed_entry_for_prestate", ROOT / "scripts/gs2-09-7-seed-bootstrap-entry.py")


def raw(value):
    return json.dumps(value, separators=(",", ":")).encode()


def issue(number, marker="", pull=False):
    value = {
        "id": 9000 + number,
        "node_id": f"I_{number}",
        "number": number,
        "title": f"fixture {number}{marker}",
        "body": None,
        "state": "closed" if number % 2 == 0 else "open",
        "updated_at": "2026-09-28T10:00:00Z",
    }
    if pull:
        value["pull_request"] = {"url": "https://api.github.com/pulls/1"}
    return value


def project_identity():
    return {"id": 2, "node_id": prestate.PROJECT_NODE_ID,
            "number": 2, "title": prestate.PROJECT_TITLE, "public": False,
            "state": "open", "closed_at": None, "owner": {"login": "FS-GG"}}


def project_item(item_id="PVTI_1", issue_id="I_1", number=1):
    return {
        "id": 100, "node_id": item_id,
        "content_type": "Issue",
        "content": {
            "id": 9000 + number, "node_id": issue_id,
            "number": number,
            "repository_url": f"{prestate.API}/repos/{prestate.OWNER}/{prestate.REPOSITORY}",
        },
        "project_url": f"{prestate.API}/orgs/FS-GG/projectsV2/2",
    }


class Provider:
    def __init__(self, issue_rows=None, project_nodes=None):
        self.issue_rows = issue_rows if issue_rows is not None else [issue(1)]
        self.project_nodes = project_nodes if project_nodes is not None else []
        self.calls = []
        self.status = None
        self.escape = False
        self.missing = False
        self.changed = False
        self.project_override = None
        self.project_missing = False
        self.project_escape = False
        self.capture = 0

    def __call__(self, method, url, token, body=None):
        self.calls.append((method, url, token, body))
        if self.status is not None:
            return self.status, {}, b"{}"
        if url.endswith("/FS.GG.GitHub.Substrate.Sandbox"):
            self.capture += 1
            value = {"id": prestate.REPOSITORY_ID,
                     "node_id": prestate.REPOSITORY_NODE_ID,
                     "full_name": prestate.REPOSITORY_FULL_NAME,
                     "private": True, "description": prestate.REPOSITORY_DESCRIPTION}
            if self.changed and self.capture == 2:
                value["extra"] = "changed"
            return 200, {}, raw(value)
        if "/issues?" in url:
            rows = self.issue_rows
            headers = {}
            if self.missing:
                rows = [issue(index) for index in range(1, 101)]
            elif len(rows) > 100:
                if "page=2" in url:
                    rows = rows[100:]
                else:
                    rows = rows[:100]
                    headers["Link"] = (
                        f'<https://api.github.com/repositories/{prestate.REPOSITORY_ID}/issues'
                        '?state=all&per_page=100&page=2>; rel="next"')
            if self.escape:
                headers["Link"] = '<https://evil.example/issues?page=2>; rel="next"'
            return 200, headers, raw(rows)
        if url.endswith("/orgs/FS-GG/projectsV2/2"):
            return 200, {}, raw(self.project_override or project_identity())
        if "/orgs/FS-GG/projectsV2/2/items?" in url:
            nodes = self.project_nodes
            headers = {}
            if self.project_missing:
                nodes = [project_item(f"PVTI_{number}", f"I_{number}", number)
                         for number in range(1, 101)]
            elif self.project_escape:
                headers["Link"] = '<https://evil.example/items?after=x>; rel="next"'
            elif len(nodes) > 100:
                if "after=cursor-100" in url:
                    nodes = nodes[100:]
                else:
                    nodes = nodes[:100]
                    headers["Link"] = (
                        '<https://api.github.com/orgs/FS-GG/projectsV2/2/items'
                        '?per_page=100&after=cursor-100>; rel="next"')
            return 200, headers, raw(nodes)
        raise AssertionError(f"unexpected request {method} {url}")


class SeedPrestateTests(unittest.TestCase):
    def setUp(self):
        self.nonce = "12345-2-" + "a" * 40
        self.facts = {"runNonce": self.nonce}
        self.token = "installation-token-secret"

    def produce(self, provider):
        with mock.patch.object(prestate, "HTTP_PORT", provider):
            return prestate.produce(self.token, self.facts)

    def test_two_fresh_equal_passes_return_only_typed_summary(self):
        provider = Provider()
        result = self.produce(provider)
        value = json.loads(result["raw"])
        self.assertEqual({"nonceIssueCount": 0, "nonceProjectItemCount": 0},
                         {name: value[name] for name in
                          ("nonceIssueCount", "nonceProjectItemCount")})
        self.assertIs(type(value["nonceIssueCount"]), int)
        self.assertEqual(prestate.REPOSITORY_ID, value["repositoryId"])
        self.assertEqual(64, len(value["snapshotSha256"]))
        self.assertEqual(64, len(result["captureId"]))
        self.assertEqual(8, len(provider.calls))
        self.assertTrue(all(method == "GET" and body is None
                            for method, _, _, body in provider.calls))
        self.assertTrue(all(token == self.token for _, _, token, _ in provider.calls))
        self.assertNotIn(self.token.encode(), result["raw"])
        self.assertNotIn(b"fixture 1", result["raw"])

    def test_nonce_issue_and_exact_project_item_are_counted(self):
        marker = f" [fsgg:gs2-09-7:{self.nonce}]"
        provider = Provider([issue(1, marker)], [project_item()])
        value = json.loads(self.produce(provider)["raw"])
        self.assertEqual(1, value["nonceIssueCount"])
        self.assertEqual(1, value["nonceProjectItemCount"])

    def test_rest_and_graphql_pagination_reach_terminal_pages(self):
        issues = [issue(number) for number in range(1, 102)]
        items = [project_item(f"PVTI_{number}", f"I_{number}", number)
                 for number in range(1, 102)]
        provider = Provider(issues, items)
        value = json.loads(self.produce(provider)["raw"])
        self.assertEqual(0, value["nonceIssueCount"])
        self.assertEqual(12, len(provider.calls))
        project_pages = [url for method, url, _, _ in provider.calls
                         if method == "GET" and "/items?" in url]
        self.assertEqual(4, len(project_pages))
        self.assertEqual(2, sum("after=cursor-100" in url for url in project_pages))

    def test_wrapper_contract_accepts_a_fresh_capture(self):
        provider = Provider()
        with mock.patch.object(prestate, "HTTP_PORT", provider), \
             mock.patch.object(entry, "PRESTATE_PRODUCER_PORT", prestate):
            captured = entry.produce_prestate(self.token, self.facts)
        self.assertEqual(prestate.SCHEMA, json.loads(captured)["schema"])
        self.assertEqual({
            "schema": "fsgg.gs2-09-7.sandbox-seed-prestate-producer/1",
            "repositoryId": 1353050537,
            "projectNodeId": "PVT_kwDOEYAWY84BiESo",
            "source": "fresh-native-issue-and-project-pages",
            "credentialScope": "protected-host-only",
            "candidateCanWrite": False,
        }, prestate.describe())

    def test_401_403_and_404_are_terminal_refusals(self):
        for status in (401, 403, 404):
            provider = Provider()
            provider.status = status
            with self.subTest(status=status), self.assertRaisesRegex(
                    prestate.Refused, f"http-{status}"):
                self.produce(provider)

    def test_missing_and_escaped_rest_pagination_refuse(self):
        missing = Provider()
        missing.missing = True
        with self.assertRaisesRegex(prestate.Refused, "missing-pagination"):
            self.produce(missing)
        escaped = Provider()
        escaped.escape = True
        with self.assertRaisesRegex(prestate.Refused, "escaped-pagination"):
            self.produce(escaped)

    def test_missing_and_escaped_project_pagination_refuse(self):
        issues = [issue(number) for number in range(1, 102)]
        missing = Provider(issues)
        missing.project_missing = True
        with self.assertRaisesRegex(prestate.Refused, "missing-project-pagination"):
            self.produce(missing)
        escaped = Provider()
        escaped.project_escape = True
        with self.assertRaisesRegex(prestate.Refused, "escaped-project-pagination"):
            self.produce(escaped)

    def test_duplicate_and_foreign_content_refuse(self):
        duplicate = Provider([issue(1), issue(1)])
        with self.assertRaisesRegex(prestate.Refused, "duplicate-issue"):
            self.produce(duplicate)
        foreign = project_item()
        foreign["content"]["repository_url"] = "https://api.github.com/repos/FS-GG/foreign"
        with self.assertRaisesRegex(prestate.Refused, "foreign-project-content"):
            self.produce(Provider(project_nodes=[foreign]))

    def test_unknown_ownership_and_non_issue_project_items_refuse(self):
        unknown = Provider(project_nodes=[project_item(issue_id="I_9", number=9)])
        with self.assertRaisesRegex(prestate.Refused, "unknown-project-ownership"):
            self.produce(unknown)
        draft = {"id": 101, "node_id": "PVTI_DRAFT", "content_type": "DraftIssue",
                 "content": {"title": "draft"}}
        with self.assertRaisesRegex(prestate.Refused, "non-issue-project-item"):
            self.produce(Provider(project_nodes=[draft]))

    def test_changed_population_between_passes_refuses(self):
        provider = Provider()
        provider.changed = True
        with self.assertRaisesRegex(prestate.Refused, "changed-population"):
            self.produce(provider)

    def test_foreign_project_identity_refuses(self):
        provider = Provider()
        provider.project_override = project_identity()
        provider.project_override["node_id"] = "PVT_foreign"
        with self.assertRaisesRegex(prestate.Refused, "foreign-project"):
            self.produce(provider)

    def test_boolean_ids_and_invalid_nonce_are_not_typed_integers(self):
        provider = Provider()
        provider.issue_rows = [issue(1)]
        provider.issue_rows[0]["id"] = True
        with self.assertRaisesRegex(prestate.Refused, "issue-shape"):
            self.produce(provider)
        with self.assertRaisesRegex(prestate.Refused, "run-nonce"):
            prestate.produce(self.token, {"runNonce": "foreign"})


if __name__ == "__main__":
    unittest.main()
