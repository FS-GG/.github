#!/usr/bin/env python3
"""Actual reader controls; synthetic files are never executed as a caller."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("artifact_state", Path(__file__).with_name("build-artifact-state.py"))
state = importlib.util.module_from_spec(spec)
spec.loader.exec_module(state)


class CallerArtifactState(unittest.TestCase):
    def test_unchanged_and_new_artifacts_are_distinguished(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = state.capture(root, None)
            self.assertEqual(first, state.capture(root, None))
            target = root / "src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"
            target.parent.mkdir(parents=True)
            target.write_bytes(b"inert fixture")
            self.assertNotEqual(first, state.capture(root, None))

    def test_same_size_same_time_changed_engine_bytes_are_detected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            target = root / "src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"
            target.parent.mkdir(parents=True)
            target.write_bytes(b"before")
            info = target.stat()
            first = state.capture(root, None)
            target.write_bytes(b"change")
            os.utime(target, ns=(info.st_atime_ns, info.st_mtime_ns))
            self.assertNotEqual(first, state.capture(root, None))

    def test_declared_transitive_and_creator_artifacts_are_preserved(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for project in ("src/FS.GG.Telemetry.Contracts", "scripts/NewSddWorkspace"):
                directory = root / project
                directory.mkdir(parents=True)
                (directory / "fixture.fsproj").write_text("<Project />")
                first = state.capture(root, None)
                target = directory / "obj/project.assets.json"
                target.parent.mkdir()
                target.write_bytes(b"synthetic restore metadata")
                self.assertNotEqual(first, state.capture(root, None))

    def test_selected_caller_change_is_detected_without_execution(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            caller = root / "selected-caller"
            caller.write_bytes(b"inert first")
            first = state.capture(root, caller)
            caller.write_bytes(b"inert other")
            self.assertNotEqual(first, state.capture(root, caller))


if __name__ == "__main__":
    unittest.main()
