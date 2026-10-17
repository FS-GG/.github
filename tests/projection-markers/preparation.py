#!/usr/bin/env python3
"""Inert production-caller regressions for the bounded local preparation route."""
import hashlib
import json
import os
import re
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
            self.assertIn("registry-projections: FAILED", output)
            self.assertIn("skill-manifests: BLOCKED", output)
            self.assertIn("own-registry-rows:", output)
            self.assertEqual(before, snapshot(self.root))
            manifest = self.root / "registry/driver-skill-manifest.json"
            old = manifest.read_bytes()
            written = self.run_mode("--write", expected=1)
            self.assertIn("registry-projections: FAILED", written)
            self.assertNotIn("registry-projections: prepared", written)
            self.assertEqual(old, manifest.read_bytes())
            target.write_text(original)

    def retired_targets(self):
        source = (self.root / "scripts/generate-projections").read_text()
        block = re.search(r"^RETIRED_ORG_TARGETS=\(\n(.*?)^\)", source, re.M | re.S)
        self.assertIsNotNone(block)
        targets = re.findall(r'^  "([^"\n]+)"$', block[1], re.M)
        self.assertEqual(len(targets), 12)
        self.assertEqual(len(set(targets)), 12)
        expected = {
            "wave-policy": "drive-board/references/host-loop.md",
            "filing-rules": "cross-repo-coordination/references/mailbox-and-board.md",
            "board-statuses": "cross-repo-coordination/references/mailbox-and-board.md",
            "reconcile-rules": "check-board/references/mechanical-reconciliation.md",
            "blocker-states": "check-board/references/mechanical-reconciliation.md",
            "snapshot-shape": "check-board/references/mechanical-reconciliation.md",
        }
        self.assertEqual(set(targets), {f"{kind}:{root}/{relative}" for kind, relative in expected.items()
                                       for root in (".agents/skills", ".claude/skills")})
        active = subprocess.check_output(["bash", str(self.root / "scripts/generate-projections"), "--list"], text=True)
        entries = [row.split("\t") for row in active.splitlines()]
        self.assertEqual(len(entries), 27)
        self.assertFalse(set(targets) & {kind + ":" + path for kind, path, _ in entries})
        self.assertIn("wave-policy:.agents/skills/work-board/references/host-loop.md",
                      {kind + ":" + path for kind, path, _ in entries})
        workflow = (ROOT / ".github/workflows/projections.yml").read_text()
        self.assertEqual(int(re.search(r"^          expected=(\d+)$", workflow, re.M)[1]), len(entries))
        return targets

    @staticmethod
    def old_region(kind):
        return (f"<!-- BEGIN GENERATED: fsgg-protocol:{kind} -->\n"
                "Historical organization fixture, not engine facts.\n"
                f"<!-- END GENERATED: fsgg-protocol:{kind} -->\n")

    def test_retired_org_regions_prepare_before_digests_and_preserve_product(self):
        before = {}
        product = {str(path): path.read_bytes() for root in (".agents", ".claude")
                   for path in (self.root / root / "skills/work-board").rglob("*") if path.is_file()}
        for target in self.retired_targets():
            kind, relative = target.split(":", 1)
            path = self.root / relative
            before.setdefault(relative, path.read_text())
            path.write_text(path.read_text() + self.old_region(kind))
        current = snapshot(self.root)
        output = self.run_mode(expected=1)
        self.assertIn("retired organization", output)
        self.assertIn("dependent status BLOCKED", output)
        self.assertEqual(current, snapshot(self.root))
        self.run_mode("--write")
        for relative, original in before.items():
            self.assertEqual((self.root / relative).read_text(), original)
        self.assertEqual(product, {str(path): path.read_bytes() for root in (".agents", ".claude")
                                  for path in (self.root / root / "skills/work-board").rglob("*") if path.is_file()})
        self.run_mode()
        current = snapshot(self.root)
        self.run_mode("--write")
        self.assertEqual(current, snapshot(self.root))

    def test_retired_marker_corruption_blocks_manifest_writes(self):
        kind, relative = self.retired_targets()[0].split(":", 1)
        path = self.root / relative
        original = path.read_text()
        begin = f"<!-- BEGIN GENERATED: fsgg-protocol:{kind} -->"
        end = f"<!-- END GENERATED: fsgg-protocol:{kind} -->"
        region = self.old_region(kind)
        mutations = (None, region.replace(end, "missing END"), region + region,
                     end + "\n" + begin + "\n", region.replace(begin, " " + begin))
        for malformed in mutations:
            with self.subTest(malformed=malformed):
                if malformed is None: path.unlink()
                else: path.write_text(original + malformed)
                manifests = [self.root / "registry" / name for name in
                             ("driver-skill-manifest.json", "coordination-kit-skill-manifest.json")]
                before = [manifest.read_bytes() for manifest in manifests]
                output = self.run_mode("--write", expected=1)
                self.assertIn("registry-projections: FAILED", output)
                self.assertIn("dependent status BLOCKED", output)
                self.assertEqual(before, [manifest.read_bytes() for manifest in manifests])
                path.write_text(original)

    def test_extra_retired_markers_and_overlap_preserve_all_inputs(self):
        groups = {}
        originals = {}
        for target in self.retired_targets():
            kind, relative = target.split(":", 1)
            groups.setdefault(relative, []).append(kind)
            path = self.root / relative
            originals.setdefault(relative, path.read_text())
            path.write_text(path.read_text() + self.old_region(kind))
        # A valid pair in each file precedes a later malformed marker: no earlier
        # target or dependent manifest may change before complete integrity checks.
        kind, relative = self.retired_targets()[-1].split(":", 1)
        path = self.root / relative
        valid = path.read_text()
        begin = f"<!-- BEGIN GENERATED: fsgg-protocol:{kind} -->"
        end = f"<!-- END GENERATED: fsgg-protocol:{kind} -->"
        mutations = (valid + "inline " + begin + "\n", valid + begin + " trailing\n",
                     valid + " " + begin + "\n", valid + "inline " + end + "\n",
                     valid + end + " trailing\n", valid + " " + end + "\n",
                     valid.replace(begin, begin + "\ninline " + begin, 1),
                     valid.replace(end, " " + end + "\n" + end, 1), valid + self.old_region(kind))
        for malformed in mutations:
            with self.subTest(marker=malformed):
                path.write_text(malformed)
                before = snapshot(self.root)
                for mode in ("--check", "--write"):
                    output = self.run_mode(mode, expected=1)
                    self.assertIn("exactly one raw and standalone", output)
                    self.assertIn("dependent status BLOCKED", output)
                    self.assertEqual(before, snapshot(self.root))
        path.write_text(valid)
        # Different retired kinds share files. Both nesting and crossing are corrupt,
        # even with one raw/standalone pair per kind; check and write must both refuse.
        relative, kinds = next((relative, kinds) for relative, kinds in groups.items() if len(kinds) == 2)
        path = self.root / relative
        left, right = kinds
        markers = lambda kind: (f"<!-- BEGIN GENERATED: fsgg-protocol:{kind} -->\n",
                                f"<!-- END GENERATED: fsgg-protocol:{kind} -->\n")
        left_begin, left_end = markers(left)
        right_begin, right_end = markers(right)
        for body in (left_begin + right_begin + right_end + left_end,
                     left_begin + right_begin + left_end + right_end):
            path.write_text(originals[relative] + body)
            before = snapshot(self.root)
            for mode in ("--check", "--write"):
                output = self.run_mode(mode, expected=1)
                self.assertIn("overlapping retired organization regions", output)
                self.assertIn("dependent status BLOCKED", output)
                self.assertEqual(before, snapshot(self.root))

    def test_missing_prerequisite_and_full_mode_guard(self):
        environment = dict(os.environ, FSGG_COORD_ENGINE_BIN=str(self.root / "absent-engine"))
        full = subprocess.run(["bash", str(self.root / "scripts/generate-projections"), "--check"],
                              env=environment, text=True, capture_output=True)
        self.assertEqual(full.returncode, 2)
        self.assertIn("no engine", full.stderr)
        mixed = subprocess.run(["bash", str(self.root / "scripts/generate-projections"), "--check", "--list"],
                               env=environment, text=True, capture_output=True)
        self.assertEqual(mixed.returncode, 2)
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

    def test_manifest_source_paths_are_bounded_before_builders(self):
        roster = self.root / "registry/repos.yml"
        original = roster.read_text()
        source = ".claude/skills/initialize-sdd-workspace"
        with tempfile.TemporaryDirectory(prefix="outside-preparation.") as outside:
            external = Path(outside)
            (external / "SKILL.md").write_text("Outside tree must never become manifest input.\n")
            link = self.root / ".claude/skills/outside-fixture"
            link.symlink_to(external, target_is_directory=True)
            for invalid in (str(external), "../" + external.name, ".", "registry", ".claude/skills/outside-fixture"):
                roster.write_text(original.replace("source: " + source, "source: " + invalid))
                manifests = [self.root / "registry" / name for name in
                             ("driver-skill-manifest.json", "coordination-kit-skill-manifest.json")]
                before = [p.read_bytes() for p in manifests]
                result = self.run_mode("--write", expected=1)
                self.assertIn("skill-manifests: UNAVAILABLE", result)
                self.assertIn("own-registry-rows: UNAVAILABLE", result)
                self.assertNotIn("skill-manifests: prepared", result)
                self.assertEqual(before, [p.read_bytes() for p in manifests])
            roster.write_text(original)
            link.unlink()

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
