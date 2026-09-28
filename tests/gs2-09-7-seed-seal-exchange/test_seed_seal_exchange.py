import hashlib
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest import mock
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "seed_seal_exchange", ROOT / "scripts/gs2-09-7-seed-seal-exchange.py")
exchange = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(exchange)


class FakeRead:
    def __init__(self, rows):
        self.rows = rows
        self.calls = []

    def get(self, route, *, limit=exchange.bridge.MAX_READ):
        self.calls.append(route)
        value = self.rows[route]
        if callable(value):
            value = value()
        return value if isinstance(value, bytes) else json.dumps(value).encode()


def archive(files):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w") as bundle:
        for name, raw in files.items():
            bundle.writestr(name, raw)
    return output.getvalue()


class ExchangeTests(unittest.TestCase):
    def setUp(self):
        self.facts = {"runId": 12345, "runAttempt": 2,
                      "candidateSha": "b" * 40, "workflowSha": "a" * 40,
                      "runNonce": "12345-2-" + "b" * 40,
                      "manifestSha256": "c" * 64}
        self.input_files = {name: (name + "\n").encode() for name in exchange.INPUT_FILES}
        self.input_files["source-manifest.json"] = b"source\n"
        self.facts["manifestSha256"] = exchange.digest(self.input_files["source-manifest.json"])
        self.name = exchange.name("output", self.facts)
        self.route = (f"{exchange.bridge.REPO}/actions/artifacts?name={self.name}&per_page=100")
        self.jobs_route = (f'{exchange.bridge.REPO}/actions/runs/{self.facts["runId"]}'
                           f'/attempts/{self.facts["runAttempt"]}/jobs?per_page=100')
        self.job = {"name": "gs2-09-7-seed-seal", "run_id": 12345,
                    "head_sha": "a" * 40,
                    "status": "completed", "conclusion": "success"}
        self.files = {name: (name + "\n").encode() for name in exchange.PUBLISH_FILES}
        self.zip = archive(self.files)
        self.artifact = {"id": 9876, "name": self.name, "expired": False,
                         "digest": "sha256:" + exchange.digest(self.zip),
                         "workflow_run": {"id": 12345, "head_sha": "a" * 40,
                                          "head_branch": "main", "repository_id": 1269292704,
                                          "head_repository_id": 1269292704}}

    def test_input_allowlist_excludes_token_and_binds_every_byte(self):
        raw = exchange.manifest(self.facts, self.input_files)
        exchange.validate_manifest(raw, self.facts, self.input_files)
        self.assertNotIn(b"installation-token", raw)
        with self.assertRaisesRegex(exchange.Refused, "seal-input-files"):
            exchange.manifest(self.facts, {**self.input_files, "token": b"secret"})
        altered = {**self.input_files, "prestate.json": b"changed"}
        with self.assertRaisesRegex(exchange.Refused, "seal-input-manifest-drift"):
            exchange.validate_manifest(raw, self.facts, altered)

    def test_cross_run_artifact_is_refused(self):
        foreign = {**self.artifact, "workflow_run": {
            **self.artifact["workflow_run"], "id": 99999}}
        port = FakeRead({self.route: {"total_count": 1, "artifacts": [foreign]}})
        with self.assertRaisesRegex(exchange.Refused, "seal-artifact-binding"):
            exchange.artifact(port, "output", self.facts)

    def test_visible_output_waits_for_terminal_sealer_success(self):
        states = iter(({**self.job, "status": "in_progress", "conclusion": None}, self.job))
        port = FakeRead({
            self.route: {"total_count": 1, "artifacts": [self.artifact]},
            self.jobs_route: lambda: {"total_count": 1, "jobs": [next(states)]},
            f'{exchange.bridge.REPO}/actions/artifacts/9876/zip': self.zip})
        files, artifact = exchange.wait(port, "output", self.facts, 30,
                                        clock=lambda: 0, pause=lambda _: None,
                                        require_job=True)
        self.assertEqual(self.files, files)
        self.assertEqual(self.artifact, artifact)
        self.assertEqual(2, port.calls.count(self.jobs_route))

    def test_failed_or_stalled_sealer_never_returns_artifact(self):
        for status, conclusion, reason in (("completed", "failure", "seal-job-failed"),
                                           ("in_progress", None, "seal-wait-timeout")):
            with self.subTest(status=status):
                port = FakeRead({self.route: {"total_count": 1, "artifacts": [self.artifact]},
                                 self.jobs_route: {"total_count": 1, "jobs": [{
                                     **self.job, "status": status, "conclusion": conclusion}]}})
                times = iter((0, 0, 2))
                with self.assertRaisesRegex(exchange.Refused, reason):
                    exchange.wait(port, "output", self.facts, 1,
                                  clock=lambda: next(times), pause=lambda _: None,
                                  require_job=True)
                self.assertFalse(any(route.endswith("/zip") for route in port.calls))

    def test_archive_rejects_extra_member_and_digest_drift(self):
        with self.assertRaisesRegex(exchange.Refused, "seal-archive-members"):
            exchange.safe_archive(archive({**self.files, "token": b"secret"}),
                                  exchange.PUBLISH_FILES)
        port = FakeRead({f'{exchange.bridge.REPO}/actions/artifacts/9876/zip': self.zip})
        with self.assertRaisesRegex(exchange.Refused, "seal-archive-digest"):
            exchange.download(port, {**self.artifact, "digest": "sha256:" + "f" * 64},
                              exchange.PUBLISH_FILES)


if __name__ == "__main__":
    unittest.main()
