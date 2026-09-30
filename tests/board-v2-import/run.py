#!/usr/bin/env python3
from __future__ import annotations

import copy
import importlib.util
import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("board_v2_import", ROOT / "tools" / "board-v2-import.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class ManifestTests(unittest.TestCase):
    def setUp(self) -> None:
        self.manifest = json.loads((ROOT / "docs" / "coordination" / "board-v2-import-manifest.json").read_text())

    def test_checked_in_manifest_is_adjudicated_and_bounded(self) -> None:
        result = MODULE.validate(self.manifest)
        self.assertEqual(8, result["candidateCount"])
        self.assertEqual(7, result["approvedImportCount"])
        self.assertEqual(3, result["pilotCount"])
        self.assertEqual(0, result["unresolvedCount"])

    def test_pilot_preserves_source_issue_identities(self) -> None:
        plan = MODULE.pilot_plan(self.manifest)
        self.assertEqual(
            ["FS-GG/.github#2963", "FS-GG/.github#2995", "FS-GG/FS.GG.Coordination#24"],
            [item["issue"] for item in plan["items"]],
        )
        self.assertTrue(all(item["nodeId"].startswith("I_") for item in plan["items"]))

    def test_duplicate_canonical_issue_is_refused(self) -> None:
        invalid = copy.deepcopy(self.manifest)
        invalid["items"].append(copy.deepcopy(invalid["items"][0]))
        with self.assertRaisesRegex(MODULE.InvalidManifest, "duplicate issue identity"):
            MODULE.validate(invalid)

    def test_pending_target_cannot_claim_project_identity(self) -> None:
        invalid = copy.deepcopy(self.manifest)
        invalid["target"]["id"] = "PVT_invented"
        with self.assertRaisesRegex(MODULE.InvalidManifest, "must not invent identities"):
            MODULE.validate(invalid)

    def test_refresh_cannot_own_scheduling_fields(self) -> None:
        invalid = copy.deepcopy(self.manifest)
        next(field for field in invalid["fields"] if field["name"] == "Status")["refreshMayWrite"] = True
        with self.assertRaisesRegex(MODULE.InvalidManifest, "Status refresh ownership differs"):
            MODULE.validate(invalid)


if __name__ == "__main__":
    unittest.main()
