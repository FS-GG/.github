"""Loopback-only integration checks for the private historical capture command."""

import json
import os
import pathlib
import stat
import subprocess
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse


ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / "tools/HistoricalLossCapture/HistoricalLossCapture.fsproj"
ASSEMBLY = ROOT / "tools/HistoricalLossCapture/bin/Debug/net10.0/HistoricalLossCapture.dll"
REPOSITORIES = {
    ".github": (1269292704, "R_kgDOS6feoA"),
    "FS.GG.Audio": (1292226968, "R_kgDOTQXRmA"),
    "FS.GG.Coordination": (1346720714, "R_kgDOUEVTyg"),
    "FS.GG.Game": (1290990429, "R_kgDOTPLzXQ"),
    "FS.GG.Governance": (1273065119, "R_kgDOS-Funw"),
    "FS.GG.Net": (1305845505, "R_kgDOTdWfAQ"),
    "FS.GG.Rendering": (1269292235, "R_kgDOS6fcyw"),
    "FS.GG.SDD": (1274272672, "R_kgDOS_PboA"),
    "FS.GG.Templates": (1281961814, "R_kgDOTGkvVg"),
}


class NativeFixture(BaseHTTPRequestHandler):
    requests = []
    selected_version = "2026-03-10"

    def log_message(self, *_args):
        pass

    def do_GET(self):
        self.requests.append((self.command, self.path, self.headers.get("X-GitHub-Api-Version")))
        parts = urlparse(self.path).path.strip("/").split("/")
        if len(parts) < 3 or parts[:2] != ["repos", "FS-GG"] or parts[2] not in REPOSITORIES:
            self.send_error(404)
            return
        name = parts[2]
        if len(parts) == 3:
            database_id, node_id = REPOSITORIES[name]
            body = json.dumps({"full_name": f"FS-GG/{name}", "id": database_id, "node_id": node_id})
        else:
            body = "[]"
        encoded = body.encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("X-RateLimit-Resource", "core")
        self.send_header("X-GitHub-Api-Version-Selected", self.selected_version)
        self.end_headers()
        self.wfile.write(encoded)

    def do_POST(self):
        self.send_error(405)


class CaptureCliTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        subprocess.run(["dotnet", "build", str(PROJECT), "-v:q"], cwd=ROOT, check=True, capture_output=True)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="historical-capture-cli-")
        self.addCleanup(self.temp.cleanup)
        self.directory = pathlib.Path(self.temp.name)
        self.output = self.directory / "evidence.native"
        NativeFixture.requests = []
        NativeFixture.selected_version = "2026-03-10"
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), NativeFixture)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)

    def invoke(self, *, token="fixture-token", horizon="2026-01-01T00:00:00.0000000Z", output=None):
        env = os.environ.copy()
        env.pop("GITHUB_TOKEN", None)
        env.pop("GH_TOKEN", None)
        env.pop("FSGG_HISTORICAL_CAPTURE_TOKEN", None)
        if token is not None:
            env["FSGG_HISTORICAL_CAPTURE_TOKEN"] = token
        env["FSGG_HISTORICAL_CAPTURE_FIXTURE_API_BASE"] = f"http://127.0.0.1:{self.server.server_port}"
        return subprocess.run(
            ["dotnet", str(ASSEMBLY), "capture", "--horizon", horizon, "--output", str(output or self.output)],
            cwd=ROOT, env=env, capture_output=True, text=True, timeout=45,
        )

    def test_saves_private_two_pass_get_only_capture(self):
        completed = self.invoke()
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(90, len(NativeFixture.requests))
        self.assertTrue(all(method == "GET" and version == "2026-03-10" for method, _, version in NativeFixture.requests))
        requested_paths = {path for _, path, _ in NativeFixture.requests}
        self.assertIn("/repos/FS-GG/FS.GG.Audio", requested_paths)
        self.assertIn("/repos/FS-GG/FS.GG.Templates", requested_paths)
        self.assertNotIn("/repos/FS-GG/Audio", requested_paths)
        self.assertEqual(0o600, stat.S_IMODE(self.output.stat().st_mode))
        self.assertGreater(self.output.stat().st_size, 0)
        self.assertNotIn("fixture-token", completed.stdout + completed.stderr)
        self.assertNotIn("R_kgD", completed.stdout + completed.stderr)
        self.assertNotIn("FS-GG", completed.stdout + completed.stderr)
        again = self.invoke()
        self.assertNotEqual(0, again.returncode)
        self.assertEqual(90, len(NativeFixture.requests))

    def test_refuses_missing_token_and_nonprivate_directory_before_io(self):
        missing = self.invoke(token=None)
        self.assertNotEqual(0, missing.returncode)
        self.assertEqual([], NativeFixture.requests)
        os.chmod(self.directory, 0o755)
        unsafe = self.invoke()
        self.assertNotEqual(0, unsafe.returncode)
        self.assertEqual([], NativeFixture.requests)

    def test_refuses_symlinked_private_directory_before_io(self):
        link = self.directory.parent / f"{self.directory.name}-link"
        link.symlink_to(self.directory, target_is_directory=True)
        self.addCleanup(link.unlink)
        completed = self.invoke(output=link / "evidence.native")
        self.assertNotEqual(0, completed.returncode)
        self.assertEqual([], NativeFixture.requests)

    def test_refuses_wrong_selected_version_without_private_file_or_body_output(self):
        NativeFixture.selected_version = "2022-11-28"
        completed = self.invoke()
        self.assertNotEqual(0, completed.returncode)
        self.assertFalse(self.output.exists())
        self.assertEqual(1, len(NativeFixture.requests))
        self.assertNotIn("FS-GG", completed.stdout + completed.stderr)

    def test_refuses_invalid_horizon_before_io(self):
        completed = self.invoke(horizon="2099-01-01T00:00:00.0000000Z")
        self.assertNotEqual(0, completed.returncode)
        self.assertEqual([], NativeFixture.requests)


if __name__ == "__main__":
    unittest.main()
