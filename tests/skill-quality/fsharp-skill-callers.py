#!/usr/bin/env python3
"""Regression checks for callers switched to the packaged F# skill commands."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


DASHBOARD = load("fsharp_skill_dashboard", ROOT / "tools/telemetry-dashboard.py")


class FsharpSkillCallerTests(unittest.TestCase):
    def test_retired_python_helpers_are_absent_from_live_roots_and_manifest(self):
        retired = (
            "work-roadmap/scripts/fsgg_telemetry_defaults.py",
            "work-roadmap/scripts/native_collaboration_usage.py",
            "work-roadmap/scripts/roadmap-telemetry.py",
            "pipeline-preflight/scripts/preflight.py",
        )
        for relative in retired:
            for root in (ROOT / ".agents/skills", ROOT / ".claude/skills"):
                self.assertFalse((root / relative).exists(), f"retired helper returned: {root / relative}")
        manifest = json.loads((ROOT / "registry/driver-skill-manifest.json").read_text(encoding="utf-8"))
        paths = {row["path"] for skill in manifest["skills"] for row in skill.get("files", [])}
        for path in ("scripts/fsgg_telemetry_defaults.py", "scripts/native_collaboration_usage.py",
                     "scripts/roadmap-telemetry.py", "scripts/preflight.py"):
            self.assertNotIn(path, paths)
        self.assertFalse((ROOT / "tools/roadmap-telemetry.py").exists())

    def test_dashboard_uses_bounded_compiled_discovery_projection(self):
        projection = {
            "schema": "fsgg.telemetry.config-discovery/1", "status": "configured",
            "configPath": "/private/config.json", "storeRoot": "/private/store",
            "engine": "configured-engine", "repository": None, "workspace": False,
        }
        completed = subprocess.CompletedProcess([], 0, json.dumps(projection) + "\n", "")
        with mock.patch.object(DASHBOARD.subprocess, "run", return_value=completed) as invoked:
            path, found = DASHBOARD.config(Path("/private/config.json"))
        self.assertEqual(path, Path("/private/config.json"))
        self.assertEqual(found, {"storeRoot": "/private/store", "engine": "configured-engine"})
        self.assertEqual(
            invoked.call_args.args[0],
            ["fsgg-coord-engine", "skill", "telemetry-config", "discover",
             "--config", "/private/config.json"],
        )

    def test_dashboard_refuses_invalid_compiled_output_without_python_fallback(self):
        completed = subprocess.CompletedProcess([], 1, "", "synthetic-secret")
        with mock.patch.object(DASHBOARD.subprocess, "run", return_value=completed):
            with self.assertRaisesRegex(DASHBOARD.HostSourceError, "HOST_CONFIG_HELPER_UNAVAILABLE"):
                DASHBOARD.config()

    def test_dashboard_refuses_malformed_compiled_projection(self):
        projection = {
            "schema": "fsgg.telemetry.config-discovery/1", "status": "configured",
            "configPath": "/private/config.json", "storeRoot": "/private/store",
            "engine": "configured-engine", "repository": None, "workspace": "false",
        }
        completed = subprocess.CompletedProcess([], 0, json.dumps(projection) + "\n", "")
        with mock.patch.object(DASHBOARD.subprocess, "run", return_value=completed):
            with self.assertRaisesRegex(DASHBOARD.HostSourceError, "HOST_CONFIG_HELPER_UNAVAILABLE"):
                DASHBOARD.config()

    def test_tracked_skill_roots_select_compiled_helpers(self):
        for relative in (
            "skills/work-roadmap/SKILL.md",
            "skills/work-board/SKILL.md",
            "skills/work-unified-roadmap/SKILL.md",
            "skills/pipeline-preflight/SKILL.md",
            "skills/pipeline-preflight/references/examples.md",
        ):
            agents = (ROOT / ".agents" / relative).read_bytes()
            claude = (ROOT / ".claude" / relative).read_bytes()
            self.assertEqual(agents, claude, relative)
        selected = "fsgg-coord-engine skill roadmap-telemetry"
        for skill in ("work-roadmap", "work-board", "work-unified-roadmap"):
            text = (ROOT / ".agents/skills" / skill / "SKILL.md").read_text(encoding="utf-8")
            self.assertIn(selected, text)
            self.assertNotIn("work-roadmap/scripts/roadmap-telemetry.py", text)
        preflight = (ROOT / ".agents/skills/pipeline-preflight/SKILL.md").read_text(encoding="utf-8")
        examples = (ROOT / ".agents/skills/pipeline-preflight/references/examples.md").read_text(encoding="utf-8")
        self.assertIn("fsgg-coord-engine skill preflight assess", preflight)
        self.assertIn("fsgg-coord-engine skill preflight graph", examples)
        self.assertNotIn("scripts/preflight.py", preflight + examples)

    def test_driver_workspace_no_longer_copies_python_configuration_helper(self):
        inventory = json.loads((ROOT / "src/FS.GG.Drivers/workspace-inventory.json").read_text(encoding="utf-8"))
        sources = {row["source"] for row in inventory["files"]}
        self.assertNotIn(".claude/skills/work-roadmap/scripts/fsgg_telemetry_defaults.py", sources)


if __name__ == "__main__":
    unittest.main()
