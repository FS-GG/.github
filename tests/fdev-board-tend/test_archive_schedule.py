"""Offline archive scheduling controls: fake HOME, pinned child/stub, no systemd or native effects (#5170)."""
from __future__ import annotations

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
DEPLOY = ROOT / "deployment/fdev-board-tend"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


A = load("archive_schedule", DEPLOY / "fsgg-board-archive-run.py")
I = load("archive_install", DEPLOY / "install.py")
FIXTURE = load("archive_fixture", ROOT / "tests/coord-board-archive/test_archive.py")
FAKE = '''import json,os,pathlib,sys,time
mode = MODE
p=pathlib.Path(sys.argv[sys.argv.index('--custody-dir')+1]);p.mkdir(mode=0o700)
(p.parent/'child.json').write_text(json.dumps({'argv':sys.argv[1:],'env':dict(os.environ)}))
print('PRIVATE-CARD-IDENTITY',flush=True)
if mode=='timeout': time.sleep(60)
rows=[{'step':'run'},{'step':'intent','itemId':'PRIVATE-ID'}]
if mode=='success': rows += [{'step':'archived','itemId':'PRIVATE-ID'},{'step':'finished'}]
if mode=='unresolved': rows += [{'step':'finished'}]
if mode=='failed': rows += [{'step':'uncertain','itemId':'PRIVATE-ID'}]
(p/'custody.jsonl').write_text(''.join(json.dumps(x)+'\\n' for x in rows))
sys.exit(3 if mode=='failed' else 0)
'''


class ArchiveSchedule(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        self.env = mock.patch.dict(os.environ, {"HOME": str(self.home), "XDG_RUNTIME_DIR": str(self.home / "runtime"),
                                               "GH_TOKEN": "WRONG", "GITHUB_TOKEN": "WRONG", "TMPDIR": "WRONG",
                                               "COORD_ARCHIVE_GH": "/untrusted/gh"})
        self.env.start(); self.addCleanup(self.env.stop)
        self.cwd = Path.cwd(); os.chdir(self.home); self.addCleanup(os.chdir, self.cwd)
        self.config = self.home / "config.json"
        self.credentials = self.write("credentials.json", {"GH_TOKEN": "FIXTURE-TOKEN"})
        self.script = self.write("archive.py", FAKE.replace("MODE", repr("success")))
        self.gh = self.write("gh", "fixture")
        self.binding = self.write("binding.json", {"selectedIssues": {"fixture-node": {}}})
        self.commitment = self.write("commitment.json", {"commitmentSha256": "fixture"})
        self.reader = self.write("reader", "qualified fixture reader")
        self.receipt = self.home / "receipt.json"
        self.c = {"schema": A.SCHEMA, "enabled": True, "maxArchive": 3, "readerVersion": "0.114.1",
                  "script": self.pin(self.script), "gh": self.pin(self.gh), "binding": self.pin(self.binding),
                  "commitment": self.pin(self.commitment), "readerFiles": [self.pin(self.reader)],
                  "stateDir": str(self.home / "state"), "outDir": str(self.home / "out"), "workdir": str(self.home)}
        self.rebind()

    def write(self, name, value):
        path = self.home / name
        path.write_text(value if isinstance(value, str) else json.dumps(value))
        path.chmod(0o600)
        return path

    def pin(self, path):
        return {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

    def rebind(self):
        self.c["script"] = self.pin(self.script)
        inputs = {"projectId": A.PROJECT, "keep": 200, "maxArchive": self.c["maxArchive"],
                  "readerVersion": self.c["readerVersion"], "readerFiles": self.c["readerFiles"],
                  "scriptSha256": self.c["script"]["sha256"], "bindingSha256": self.c["binding"]["sha256"],
                  "commitmentSha256": self.c["commitment"]["sha256"]}
        self.write("receipt.json", {"schema": "fsgg.board-archive-accepted/1", "accepted": True, "inputs": inputs,
                                    "evidenceUrl": "https://github.com/FS-GG/.github/issues/5170#issuecomment-1"})
        self.c["acceptedReceipt"] = self.pin(self.receipt)
        self.save()

    def save(self):
        self.write("config.json", self.c)

    def invoke(self, timeout=5):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = A.run(self.config, self.credentials, timeout)
        self.assertNotIn("PRIVATE", out.getvalue() + err.getvalue())
        self.assertNotIn("FIXTURE-TOKEN", out.getvalue() + err.getvalue())
        return code

    def test_success_pins_policy_environment_and_private_outputs(self):
        self.assertEqual(self.invoke(), 0)
        self.assertFalse((self.home / "state/in-progress.json").exists())
        run = next((self.home / "out").iterdir())
        child = json.loads((run / "child.json").read_text())
        args, env = child["argv"], child["env"]
        for flag, value in (("--keep", "200"), ("--max-archive", "3"), ("--project", "3"),
                            ("--expect-project-id", A.PROJECT), ("--enrollment-binding", str(self.binding)),
                            ("--reader-version", "0.114.1")):
            self.assertEqual(args[args.index(flag) + 1], value)
        self.assertIn("--execute", args)
        self.assertEqual(env["GH_TOKEN"], "FIXTURE-TOKEN")
        self.assertEqual(env["COORD_ARCHIVE_GH"], str(self.gh))
        self.assertNotIn("GITHUB_TOKEN", env); self.assertNotIn("TMPDIR", env)
        self.assertIn("PRIVATE-CARD-IDENTITY", (run / "stdout").read_text())
        for path in (run, self.home / "state", self.home / "out"):
            self.assertEqual(path.stat().st_mode & 0o777, 0o700)
        for path in run.rglob("*"):
            if path.is_file(): self.assertEqual(path.stat().st_mode & 0o777, 0o600)
        self.assertEqual(self.invoke(), 0)

    def test_disabled_missing_receipt_and_stale_acceptance_refuse_before_child(self):
        self.c["enabled"] = False; self.save(); self.assertEqual(self.invoke(), 2)
        self.c["enabled"] = True; self.c["maxArchive"] = 4; self.save(); self.assertEqual(self.invoke(), 2)
        self.receipt.unlink(); self.assertEqual(self.invoke(), 2)
        self.assertFalse((self.home / "out").exists())

    def test_each_changed_identity_refuses_before_child(self):
        for path in (self.binding, self.commitment, self.reader, self.script, self.gh):
            with self.subTest(path=path.name):
                before = path.read_bytes(); path.write_bytes(before + b" ")
                self.assertEqual(self.invoke(), 2)
                path.write_bytes(before)
        self.assertFalse((self.home / "out").exists())

    def test_unaccepted_receipt_and_private_input_symlink_refuse(self):
        receipt = json.loads(self.receipt.read_text()); receipt["accepted"] = False
        self.receipt.write_text(json.dumps(receipt)); self.c["acceptedReceipt"] = self.pin(self.receipt); self.save()
        self.assertEqual(self.invoke(), 2)
        self.rebind()
        link = self.home / "binding-link"; link.symlink_to(self.binding)
        self.c["binding"]["path"] = str(link); self.save()
        self.assertEqual(self.invoke(), 2)
        self.assertFalse((self.home / "out").exists())

    def test_completed_empty_plan_releases_latch(self):
        self.script.write_text("print('nothing to archive.')\n"); self.rebind()
        self.assertEqual(self.invoke(), 0)
        self.assertFalse((self.home / "state/in-progress.json").exists())

    def test_malformed_custody_keeps_latch(self):
        self.script.write_text(FAKE.replace("MODE", repr("success"))
                               .replace("sys.exit(3 if mode=='failed' else 0)",
                                        "(p/'custody.jsonl').write_text('[}');sys.exit(0)"))
        self.rebind()
        self.assertEqual(self.invoke(), 2)
        self.assertTrue((self.home / "state/in-progress.json").exists())
        self.assertEqual(self.invoke(), 2)

    def test_bad_credentials_modes_version_and_runtime_refuse(self):
        self.credentials.chmod(0o644); self.assertEqual(self.invoke(), 2)
        self.credentials.chmod(0o600)
        self.c["readerVersion"] = "0.114.0"; self.rebind(); self.assertEqual(self.invoke(), 2)
        self.c["readerVersion"] = "0.114.1"; self.rebind()
        with mock.patch.dict(os.environ, {"XDG_RUNTIME_DIR": "relative"}): self.assertEqual(self.invoke(), 2)

    def test_failed_zero_exit_unresolved_and_timeout_block_next_invocation(self):
        for mode in ("failed", "unresolved", "timeout"):
            with self.subTest(mode=mode):
                self.script.write_text(FAKE.replace("MODE", repr(mode))); self.rebind()
                self.assertEqual(self.invoke(timeout=0.1), 3)
                latch = self.home / "state/in-progress.json"
                self.assertTrue(latch.exists())
                runs = list((self.home / "out").iterdir())
                self.assertEqual(self.invoke(), 2)
                self.assertEqual(list((self.home / "out").iterdir()), runs)
                # Only fixture cleanup: production requires Root's resolved readback.
                latch.unlink()

    def test_simulated_crash_latch_blocks_even_with_current_inputs(self):
        state = self.home / "state"; state.mkdir(mode=0o700)
        A.exclusive_json(state / "in-progress.json", {"run": "interrupted fixture"})
        self.assertEqual(self.invoke(), 2)
        self.assertFalse((self.home / "out").exists())

    def test_actual_archiver_shared_lock_busy_sends_no_mutation(self):
        import fcntl
        rows = FIXTURE.done_history(203)
        stub = FIXTURE.Stub(self, scan=[FIXTURE.scan_page(rows[:100], total=203, board=203, has_next=True),
                                       FIXTURE.scan_page(rows[100:200], total=203, board=203, has_next=True, cursor="c2"),
                                       FIXTURE.scan_page(rows[200:], total=203, board=203)])
        self.script.write_bytes((ROOT / "scripts/coord-board-archive.py").read_bytes())
        self.c["gh"] = self.pin(stub.path); self.rebind()
        shared = self.home / "runtime/fsgg"; shared.mkdir(parents=True, mode=0o700)
        with (shared / f"board-writer-{A.PROJECT}.lock").open("w") as lock:
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertEqual(self.invoke(), 3)
        self.assertEqual(stub.calls("mutation"), [])
        self.assertEqual(stub.calls("item"), [])
        self.assertEqual(self.invoke(), 2)

    def test_installer_dry_run_and_disabled_staging_leave_tend_untouched(self):
        release = self.home / "release-source-pin"
        args = ["archive", "--release-dir", str(release)]
        with mock.patch.object(I.subprocess, "run", side_effect=AssertionError("systemd must not run")):
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(I.main(args + ["--dry-run"]), 0)
                self.assertFalse(release.exists())
                self.assertEqual(I.main(args), 0)
        units = self.home / ".config/systemd/user"
        self.assertEqual({x.name for x in units.iterdir()}, {"fsgg-board-archive.service", "fsgg-board-archive.timer"})
        service = (units / "fsgg-board-archive.service").read_text()
        self.assertIn("Type=exec", service); self.assertIn("RuntimeMaxSec=900", service)
        self.assertIn("Environment=XDG_RUNTIME_DIR=%t", service)
        self.assertIn("CPUQuota=100%", service); self.assertIn("MemoryMax=1G", service)
        self.assertNotIn("PrivateTmp=", service); self.assertNotIn("Restart=", service)
        self.assertEqual((release / "coord-board-archive.py").stat().st_mode & 0o777, 0o400)
        (release / "coord-board-archive.py").chmod(0o600)
        (release / "coord-board-archive.py").write_text("changed")
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(I.main(args + ["--force"]), 2)


if __name__ == "__main__":
    unittest.main()
