"""Portable actual-subprocess coverage; no collector, store or browser acceptance."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('telemetry_local_journey', Path(__file__).with_name('run.py'))
J = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(J)


class HelperEnvironmentTests(unittest.TestCase):
    def fixture(self, directory):
        root = Path(directory)
        probe = root / 'probe.py'
        probe.write_text('import json, os\n' +
            'keys = ' + repr(sorted(J.ASSOCIATION_KEYS)) + '\n' +
            'print(json.dumps({"associationVisible":any(k in os.environ for k in keys) or any(k.startswith("FSGG_TELEMETRY_CREDENTIAL_") for k in os.environ),\n' +
            '"privateXdg":os.environ.get("XDG_CONFIG_HOME") == ' + repr(str(root / 'config')) + '}))\n')
        poisoned = dict(os.environ, XDG_CONFIG_HOME=str(root / 'ambient'))
        poisoned.update({key:'synthetic-unselected-association' for key in J.ASSOCIATION_KEYS})
        poisoned['FSGG_TELEMETRY_CREDENTIAL_FIXTURE'] = 'synthetic-unselected-credential'
        return root, probe, poisoned

    def test_real_helper_child_excludes_sentinels_and_restores_parent(self):
        original = dict(os.environ)
        with tempfile.TemporaryDirectory() as directory:
            root, probe, poisoned = self.fixture(directory)
            with J.process_environment(poisoned):
                selected = J.private_environment(root / 'config')
                with J.process_environment(selected):
                    result = J.D.engine_json(sys.executable, [str(probe)])
                self.assertEqual(result, {'associationVisible':False, 'privateXdg':True})
                self.assertEqual(dict(os.environ), poisoned)
        self.assertEqual(dict(os.environ), original)

    def test_unisolated_helper_child_detects_sentinel_control(self):
        with tempfile.TemporaryDirectory() as directory:
            _, probe, poisoned = self.fixture(directory)
            with J.process_environment(poisoned):
                result = J.D.engine_json(sys.executable, [str(probe)])
            self.assertEqual(result, {'associationVisible':True, 'privateXdg':False})

    def test_failed_actual_helper_child_restores_environment(self):
        with tempfile.TemporaryDirectory() as directory:
            root, _, poisoned = self.fixture(directory)
            failed = root / 'failed.py'
            failed.write_text('import sys\nsys.exit(23)\n')
            with J.process_environment(poisoned):
                with self.assertRaises(J.D.HostSourceError):
                    with J.process_environment(J.private_environment(root / 'config')):
                        J.D.engine_json(sys.executable, [str(failed)])
                self.assertEqual(dict(os.environ), poisoned)


if __name__ == '__main__':
    unittest.main()
