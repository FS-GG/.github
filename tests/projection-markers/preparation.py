#!/usr/bin/env python3
"""Inert production-caller regressions for the bounded local preparation route."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def snapshot(root):
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob("*") if p.is_file()}


class Preparation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="registry-preparation.")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for relative in ("registry", ".agents", ".claude", "docs/atlas"):
            shutil.copytree(ROOT / relative, self.root / relative)
        scripts = self.root / "scripts"
        scripts.mkdir()
        for name in ("prepare-registry", "generate-projections", "generate-driver-manifest", "fsgg-skill-registry-check"):
            shutil.copy2(ROOT / "scripts" / name, scripts / name)
        roster = subprocess.check_output(["bash", str(ROOT / "scripts/generate-projections"), "--list"], text=True)
        for line in roster.splitlines():
            relative = line.split("\t")[1]
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / relative, target)
        # Existing protected own-row drift is also a preparation obligation;
        # establish a genuinely current fixture through the production caller.
        self.run_mode("--write", expected=0)

    def run_mode(self, mode="--check", expected=0, env=None):
        result = subprocess.run([sys.executable, "-B", str(self.root / "scripts/prepare-registry"), mode],
                                text=True, capture_output=True, env=env)
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        self.assertIn("protocol/full qualification NOT RUN", result.stdout)
        return result.stdout + result.stderr

    def test_current_read_only_and_idempotent(self):
        before = snapshot(self.root)
        self.run_mode()
        self.assertEqual(before, snapshot(self.root))
        self.run_mode("--write")
        self.assertEqual(before, snapshot(self.root))

    def test_registry_version_drives_every_declared_region(self):
        path = self.root / "registry/dependencies.yml"
        text = path.read_text()
        start = text.index("  - id: wasm-shared-runtime\n")
        end = text.index("\n  - id:", start + 1)
        path.write_text(text[:start] + text[start:end].replace('"0.4.0"', '"0.5.0"') + text[end:])
        before = snapshot(self.root)
        output = self.run_mode(expected=1)
        for kind in ("release-inventory", "versions", "contract-versions", "component-inventory"):
            self.assertIn("'" + kind + "' region", output)
        for target in (".agents/skills/publishing-and-deployment/SKILL.md", ".claude/skills/publishing-and-deployment/SKILL.md",
                       "docs/architecture.md", "docs/registry/compatibility.md", "docs/components.md", "docs/consumer/versioning-and-updates.md"):
            self.assertIn(target, output)
        self.assertIn("skill-manifests: BLOCKED", output)
        self.assertIn("own-registry-rows: BLOCKED", output)
        self.assertEqual(before, snapshot(self.root))
        self.run_mode("--write")
        self.run_mode()

    def test_public_seed_change_and_projected_skill_report_together(self):
        archive = self.root / "docs/atlas"
        first = json.loads((archive / "catalog.json").read_text())["records"][0]
        record = json.loads((archive / "records" / (first + ".json")).read_text())
        record.update(id="platform/preparation-fixture", summary="Public test fixture for generated delivery seeds.",
                      supersedes=[], conflictsWith=[])
        prepared = self.root / "fixture-record.json"
        prepared.write_text(json.dumps(record))
        captured = subprocess.run([sys.executable, "-B", str(self.root / ".agents/skills/atlas/scripts/atlas.py"),
                                   "--archive", str(archive), "capture", str(prepared)], capture_output=True, text=True)
        self.assertEqual(captured.returncode, 0, captured.stderr)
        for root in (".agents/skills", ".claude/skills"):
            skill = self.root / root / "publishing-and-deployment/SKILL.md"
            skill.write_text(skill.read_text() + "\nFixture authored annotation.\n")
        before = snapshot(self.root)
        output = self.run_mode(expected=1)
        self.assertIn(".agents/skills/atlas/assets/seed", output)
        self.assertIn(".claude/skills/atlas/assets/seed", output)
        self.assertIn("skill-manifests: STALE", output)
        self.assertIn("own-registry-rows: STALE: publishing-and-deployment", output)
        self.assertIn("dependent status BLOCKED", output)
        self.assertEqual(before, snapshot(self.root))
        # Foreign producer bytes remain opaque even when obviously stale.
        registry = self.root / "registry/skills.yml"
        import yaml
        foreign = next(row for row in yaml.safe_load(registry.read_text())["skills"] if row["owner"] != ".github")
        registry.write_text(registry.read_text().replace(str(foreign["sha256"]), "0" * 64))
        self.run_mode("--write")
        after = yaml.safe_load(registry.read_text())
        self.assertEqual(next(row for row in after["skills"] if row["id"] == foreign["id"])["sha256"], 0)
        self.run_mode()

    def test_deleted_target_and_malformed_marker_never_pass_dependencies(self):
        target = self.root / "docs/components.md"
        original = target.read_text()
        for malformed in (None, original.replace("<!-- END GENERATED: fsgg-component-inventory -->", "missing END")):
            if malformed is None:
                target.unlink()
            else:
                target.write_text(malformed)
            before = snapshot(self.root)
            output = self.run_mode(expected=1)
            self.assertIn("registry-projections: STALE", output)
            self.assertIn("skill-manifests: BLOCKED", output)
            self.assertIn("own-registry-rows:", output)
            self.assertEqual(before, snapshot(self.root))
            target.write_text(original)

    def test_missing_prerequisite_and_full_mode_guard(self):
        environment = dict(os.environ, FSGG_COORD_ENGINE_BIN=str(self.root / "absent-engine"))
        full = subprocess.run(["bash", str(self.root / "scripts/generate-projections"), "--check"],
                              env=environment, text=True, capture_output=True)
        self.assertEqual(full.returncode, 2)
        self.assertIn("no engine", full.stderr)
        self.run_mode(env=environment)  # explicit preparation does not call CLR
        environment["PATH"] = str(self.root / "missing-tools")
        output = self.run_mode(expected=1, env=environment)
        self.assertIn("registry-projections: UNAVAILABLE", output)
        self.assertIn("skill-manifests: BLOCKED", output)

    def test_ambiguous_own_identity_is_not_a_foreign_write(self):
        registry = self.root / "registry/skills.yml"
        body = registry.read_text()
        row = next(line for line in body.splitlines() if "id: publishing-and-deployment," in line)
        registry.write_text(body + "\n" + row.replace("owner: .github", "owner: foreign") + "\n")
        before = registry.read_bytes()
        output = self.run_mode("--write", expected=1)
        self.assertIn("own-registry-rows: UNAVAILABLE: ambiguous registry id", output)
        self.assertEqual(before, registry.read_bytes())

    def test_symlink_archive_refuses_and_write_does_not_digest_stale_input(self):
        source = self.root / "docs/atlas/README.md"
        original = source.read_bytes()
        source.unlink()
        source.symlink_to(self.root / "registry/CHANGELOG.md")
        manifest = self.root / "registry/driver-skill-manifest.json"
        before = manifest.read_bytes()
        output = self.run_mode("--write", expected=1)
        self.assertIn("public-atlas-seeds: UNAVAILABLE", output)
        self.assertIn("skill-manifests: BLOCKED", output)
        self.assertEqual(before, manifest.read_bytes())
        source.unlink()
        source.write_bytes(original)


if __name__ == "__main__":
    unittest.main()
