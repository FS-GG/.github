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
            with self.assertRaises(MODULE.GraphQlReadError):
                MODULE.execute("query{x}", {}, lambda data: data["x"])

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


if __name__ == "__main__": unittest.main()
