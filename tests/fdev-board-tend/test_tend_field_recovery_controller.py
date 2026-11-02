"""Controller sandbox only: fake runtime/executable, no production locks, credential or provider calls."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("field_recovery_controller", ROOT / "tools/tend-field-validation-settle.py")
C = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(C)


class Controller(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        def write(name, content):
            p = self.root / name; p.write_text(content); p.chmod(0o600); return p
        self.core = write("FS.GG.Coord.Core.dll", "synthetic core")
        self.github = write("FS.GG.Coord.GitHub.dll", "synthetic adapter")
        self.credentials = write("credentials", '{"GH_TOKEN":"FIXTURE-PRIVATE"}')
        self.original = write("original", "synthetic original")
        self.request = write("request", "synthetic reconstructed request")
        self.dotnet = write("dotnet", "#!/usr/bin/python3\nimport json,os,pathlib,sys\np=pathlib.Path(sys.argv[-1]).parent\n(p/'invoked').write_text(json.dumps({'args':sys.argv,'env':dict(os.environ)}))\n")
        self.dotnet.chmod(0o700)
        self.out = self.root / "output"
        sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
        self.args = ["--runtime-directory", str(self.root), "--expect-core-sha256", sha(self.core),
                     "--expect-github-sha256", sha(self.github), "--dotnet", str(self.dotnet), "--expect-dotnet-sha256", sha(self.dotnet),
                     "--credentials", str(self.credentials), "--custody-root", str(self.root / "custody"),
                     "--original-path", str(self.original), "--request-path", str(self.request),
                     "--original-runtime-directory", str(self.root), "--output-directory", str(self.out),
                     "--root-admission-reference", "https://github.com/FS-GG/.github/issues/5206#issuecomment-1"]

    def go(self, *extra):
        output = io.StringIO()
        with contextlib.redirect_stdout(output): result = C.main(self.args + list(extra))
        self.assertNotIn("FIXTURE-PRIVATE", output.getvalue())
        return result

    def test_preview_never_launches_or_creates_output(self):
        with mock.patch.object(C.subprocess, "Popen", side_effect=AssertionError("must not launch")):
            self.assertEqual(self.go(), 0)
        self.assertFalse(self.out.exists())

    def test_changed_runtime_or_public_credentials_refuse_before_launch(self):
        self.github.write_text("changed")
        self.assertEqual(self.go("--admit"), 2)
        self.assertFalse(self.out.exists())
        self.github.write_text("synthetic adapter"); self.credentials.chmod(0o644)
        self.assertEqual(self.go("--admit"), 2)

    def test_explicit_admission_pins_process_and_private_output_and_never_replaces(self):
        with mock.patch.dict(os.environ, {"XDG_RUNTIME_DIR": str(self.root), "GH_TOKEN": "wrong", "GITHUB_TOKEN": "wrong"}):
            self.assertEqual(self.go("--admit"), 0)
            self.assertEqual(self.go("--admit"), 2)
        call = json.loads((self.out / "invoked").read_text())
        self.assertNotIn("GH_TOKEN", call["env"]); self.assertNotIn("GITHUB_TOKEN", call["env"])
        self.assertNotIn("FIXTURE-PRIVATE", json.dumps(call))
        self.assertEqual(call["args"][1:3], ["fsi", "--exec"])
        for path in self.out.iterdir(): self.assertEqual(path.stat().st_mode & 0o777, 0o600)
        self.assertEqual(self.out.stat().st_mode & 0o777, 0o700)

    def test_missing_shared_runtime_refuses_before_process_or_output(self):
        with mock.patch.dict(os.environ, {"XDG_RUNTIME_DIR": "relative"}): self.assertEqual(self.go("--admit"), 2)
        self.assertFalse(self.out.exists())
