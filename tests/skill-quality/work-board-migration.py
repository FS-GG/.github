#!/usr/bin/env python3
"""Exercise actual coordinator packaging and compatibility with inert local file mutations only.

Independent agent consumer journeys separately test the instruction decisions; these controls do not
claim a package publication, installed discovery, CLR qualification or executing-caller adoption.
"""
from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
import json
from pathlib import Path
import shutil
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def load():
    path = ROOT / 'src/FS.GG.Drivers/stage-drivers.py'
    spec = importlib.util.spec_from_file_location('coordinator_stage', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class CoordinatorPackaging(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='work-board-migration-')
        self.addCleanup(self.temp.cleanup)
        self.out = Path(self.temp.name) / 'stage'
        self.stage = load()

    def stage_actual(self):
        with redirect_stdout(io.StringIO()):
            self.assertEqual(0, self.stage.main([str(self.out)]))

    def test_actual_offline_closure_and_discovery_policy(self):
        self.stage_actual()
        for name in ('work-board', 'atlas', 'continue-from-handoff', 'stop-with-handoff',
                     'routine-decisions', 'agent-handoff', 'work-programme'):
            self.assertTrue((self.out / 'skills' / name / 'SKILL.md').is_file(), name)
        self.stage.validate_coordinator_links(str(self.out))
        metadata = (self.out / 'skills/work-board/agents/openai.yaml').read_text()
        self.assertIn('allow_implicit_invocation: true', metadata)
        for name in ('work-board-normal', 'work-board-best'):
            self.assertEqual((ROOT / '.agents/skills' / name / 'agents/openai.yaml').read_bytes(),
                             (self.out / 'skills' / name / 'agents/openai.yaml').read_bytes())
        contract = 'references/organization-v2-inspection.md'
        self.assertEqual((ROOT / '.agents/skills/check-board' / contract).read_bytes(),
                         (self.out / 'skills/work-board' / contract).read_bytes())

    def test_missing_recovery_dependency_refuses_real_stager(self):
        original = json.loads(Path(self.stage.MANIFEST_PATH).read_text())
        original['skills'] = [row for row in original['skills'] if row['id'] != 'stop-with-handoff']
        manifest = Path(self.temp.name) / 'manifest.json'
        manifest.write_text(json.dumps(original))
        self.stage.MANIFEST_PATH = str(manifest)
        error = io.StringIO()
        with redirect_stderr(error), self.assertRaises(SystemExit):
            self.stage.main([str(self.out)])
        self.assertIn('coordinator relative link is not delivered', error.getvalue())
        self.assertIn('stop-with-handoff', error.getvalue())

    def test_consumer_removed_file_and_escaped_link_refuse(self):
        self.stage_actual()
        helper = self.out / 'skills/work-board/references/crash-recovery.md'
        helper.unlink()
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            self.stage.validate_coordinator_links(str(self.out))
        self.stage_actual()
        entry = self.out / 'skills/work-board/SKILL.md'
        entry.write_text(entry.read_text() + '\n[escape](../../outside.md)\n')
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            self.stage.validate_coordinator_links(str(self.out))

    def test_historical_python_path_runs_same_helper(self):
        # No CLR or native execution: the historical shim must project the real pure helper.
        import subprocess
        canonical = ROOT / '.agents/skills/work-board/scripts/context.py'
        historical = ROOT / '.agents/skills/work-programme/scripts/context.py'
        import sys
        first = subprocess.run([sys.executable, '-B', str(canonical), '--help'], capture_output=True)
        old = subprocess.run([sys.executable, '-B', str(historical), '--help'], capture_output=True)
        self.assertEqual(first.returncode, old.returncode)
        self.assertEqual(first.stdout.replace(b'context.py', b'HELPER'), old.stdout.replace(b'context.py', b'HELPER'))
        self.assertEqual(first.stderr, old.stderr)


if __name__ == '__main__':
    unittest.main()
