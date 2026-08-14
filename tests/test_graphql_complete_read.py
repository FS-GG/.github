import importlib.util
import json
import pathlib
import sys
import unittest
from unittest.mock import patch

PATH = pathlib.Path(__file__).parents[1] / "scripts" / "graphql_complete_read.py"
SPEC = importlib.util.spec_from_file_location("graphql_complete_read", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class Reply:
    returncode = 0
    stderr = ""
    def __init__(self, body): self.stdout = json.dumps(body)


class CompleteReadTests(unittest.TestCase):
    def test_mixed_data_and_errors_is_failure(self):
        with patch.object(MODULE.subprocess, "run", return_value=Reply({"data": {"x": 1}, "errors": [{"message": "partial"}]})):
            with self.assertRaises(MODULE.GraphQlReadError) as caught:
                MODULE.execute("query{x}", {}, lambda data: data["x"])
        self.assertEqual(MODULE.RetryClassification.NOT_RETRYABLE, caught.exception.metadata.retry)

    def test_primary_rate_limit_has_typed_retry_metadata(self):
        error = {"message": "API rate limit exceeded", "extensions": {"resetAt": "2026-08-14T13:00:00Z"}}
        with patch.object(MODULE.subprocess, "run", return_value=Reply({"errors": [error]})):
            with self.assertRaises(MODULE.GraphQlReadError) as caught: MODULE.execute("query", {}, lambda d: d)
        self.assertEqual(MODULE.RateLimitKind.PRIMARY, caught.exception.metadata.rate_limit)
        self.assertEqual(MODULE.RetryClassification.RETRYABLE, caught.exception.metadata.retry)
        self.assertEqual("2026-08-14T13:00:00Z", caught.exception.metadata.reset_at)

    def test_secondary_rate_limit_has_typed_retry_after(self):
        error = {"message": "secondary rate limit", "extensions": {"retryAfter": 17}}
        with patch.object(MODULE.subprocess, "run", return_value=Reply({"errors": [error]})):
            with self.assertRaises(MODULE.GraphQlReadError) as caught: MODULE.execute("query", {}, lambda d: d)
        self.assertEqual(MODULE.RateLimitKind.SECONDARY, caught.exception.metadata.rate_limit)
        self.assertEqual(17, caught.exception.metadata.retry_after_seconds)

    def test_repeated_cursor_is_failure(self):
        replies = [
            Reply({"data": {"c": {"totalCount": 2, "nodes": [{"id": "1"}], "pageInfo": {"hasNextPage": True, "endCursor": "same"}}}}),
            Reply({"data": {"c": {"totalCount": 2, "nodes": [{"id": "2"}], "pageInfo": {"hasNextPage": True, "endCursor": "same"}}}}),
        ]
        with patch.object(MODULE.subprocess, "run", side_effect=replies):
            with self.assertRaises(MODULE.GraphQlReadError):
                MODULE.drain("query", {}, lambda d: d["c"], lambda n: n, lambda n: n["id"])

    def test_total_count_mismatch_is_failure(self):
        body = {"data": {"c": {"totalCount": 2, "nodes": [{"id": "1"}], "pageInfo": {"hasNextPage": False, "endCursor": None}}}}
        with patch.object(MODULE.subprocess, "run", return_value=Reply(body)):
            with self.assertRaises(MODULE.GraphQlReadError):
                MODULE.drain("query", {}, lambda d: d["c"], lambda n: n, lambda n: n["id"])

    def test_malformed_nodes_is_failure(self):
        body = {"data": {"c": {"totalCount": 0, "nodes": {}, "pageInfo": {"hasNextPage": False}}}}
        with patch.object(MODULE.subprocess, "run", return_value=Reply(body)):
            with self.assertRaises(MODULE.GraphQlReadError): MODULE.drain("q", {}, lambda d: d["c"], lambda n: n, lambda n: n["id"])

    def test_empty_continuing_page_is_failure(self):
        body = {"data": {"c": {"totalCount": 1, "nodes": [], "pageInfo": {"hasNextPage": True, "endCursor": "x"}}}}
        with patch.object(MODULE.subprocess, "run", return_value=Reply(body)):
            with self.assertRaises(MODULE.GraphQlReadError): MODULE.drain("q", {}, lambda d: d["c"], lambda n: n, lambda n: n["id"])

    def test_changed_total_is_failure(self):
        pages = [Reply({"data": {"c": {"totalCount": 2, "nodes": [{"id":"1"}], "pageInfo": {"hasNextPage": True, "endCursor":"a"}}}}),
                 Reply({"data": {"c": {"totalCount": 3, "nodes": [{"id":"2"}], "pageInfo": {"hasNextPage": False}}}})]
        with patch.object(MODULE.subprocess, "run", side_effect=pages):
            with self.assertRaises(MODULE.GraphQlReadError): MODULE.drain("q", {}, lambda d: d["c"], lambda n: n, lambda n: n["id"])

    def test_page_and_item_limits_are_failures(self):
        continuing = Reply({"data": {"c": {"totalCount": 2, "nodes": [{"id":"1"}], "pageInfo": {"hasNextPage": True, "endCursor":"a"}}}})
        with patch.object(MODULE.subprocess, "run", return_value=continuing):
            with self.assertRaises(MODULE.GraphQlReadError): MODULE.drain("q", {}, lambda d: d["c"], lambda n: n, lambda n: n["id"], max_pages=1)
        ending = Reply({"data": {"c": {"totalCount": 2, "nodes": [{"id":"1"},{"id":"2"}], "pageInfo": {"hasNextPage": False}}}})
        with patch.object(MODULE.subprocess, "run", return_value=ending):
            with self.assertRaises(MODULE.GraphQlReadError): MODULE.drain("q", {}, lambda d: d["c"], lambda n: n, lambda n: n["id"], max_items=1)


if __name__ == "__main__": unittest.main()
