#!/usr/bin/env python3
"""Exercise the real workflow's resolved checkout operand and verification shell offline.

The narrow checkout seam uses real local Git objects. Its SHA/ref normalization follows the
pinned actions/checkout input-helper.ts and main.ts: SHA -> empty ref output + commit;
empty input -> default branch. Unknown GitHub properties resolve empty (GitHub context contract).
This is component evidence; native checkout inputs/outputs still require caller qualification.
"""
from __future__ import annotations
import copy
import hashlib
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[2]
PIN = "61be6a913fe9f6b4b7410ba8dbf1020b61971a2a"
RULE = "scripts/check-closing-keywords.py"
DIGEST = "0c71b522472eefd1724d12699cda7e8fce6048732992332601c51a556de96ed4"
FILES = ("kit-materialize.yml", "closing-keywords-receiver.yml")


def resolve(value, context):
    if not isinstance(value, str):
        return ""
    expression = re.fullmatch(r"\$\{\{\s*([\w.-]+)\s*\}\}", value)
    if expression:
        current = context
        for key in expression[1].split("."):
            if not isinstance(current, dict) or key not in current:
                return ""
            current = current[key]
        return str(current)
    return value


class SourceBinding(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.scratch = tempfile.TemporaryDirectory(prefix="closing-source-")
        cls.work = Path(cls.scratch.name)
        cls.hub = cls.work / "fsgg-hub"
        cls.env = {"PATH": os.defpath, "HOME": str(cls.work), "GIT_CONFIG_NOSYSTEM": "1",
                   "GIT_CONFIG_GLOBAL": os.devnull, "PYTHONDONTWRITEBYTECODE": "1"}
        cls.git("clone", "--quiet", "--shared", "--no-checkout", str(ROOT), str(cls.hub), cwd=cls.work)
        cls.git("sparse-checkout", "init", "--no-cone")
        cls.git("sparse-checkout", "set", "--no-cone", "/scripts/")
        cls.git("checkout", "--quiet", "--force", "--detach", PIN)
        cls.original = (cls.hub / RULE).read_bytes()
        assert hashlib.sha256(cls.original).hexdigest() == DIGEST
        # A future default branch with different rule bytes; it is private fixture source only.
        (cls.hub / RULE).write_bytes(cls.original + b"\n# fixture moving-main mutation\n")
        cls.git("add", "--", RULE)
        cls.git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                "commit", "--quiet", "-m", "Fixture moving main")
        cls.moving_main = cls.git("rev-parse", "HEAD").strip()
        cls.jobs = [yaml.safe_load((ROOT / ".github/workflows" / f).read_text())["jobs"]["closing-keywords"]
                    for f in FILES]

    @classmethod
    def git(cls, *args, cwd=None):
        return subprocess.run(["git", *args], cwd=cwd or cls.hub, env=cls.env,
                              check=True, capture_output=True, text=True, timeout=20).stdout

    @classmethod
    def tearDownClass(cls):
        cls.scratch.cleanup()

    def guard(self, job, *, default=None, mutate=False, actual=None, outputs=None):
        fetch = job["steps"][0]
        value = resolve(fetch.get("with", {}).get("ref"), {"env": job.get("env", {}), "github": {}})
        if re.fullmatch(r"[0-9a-f]{40}", value):
            target, ref_output = value, ""
        else:
            target, ref_output = default or PIN, value or "refs/heads/main"
        self.git("checkout", "--quiet", "--force", "--detach", actual or target)
        if mutate:
            (self.hub / RULE).write_bytes(self.original + b"\n# uncommitted rule mutation\n")
        action_outputs = {"ref": ref_output, "commit": target}
        action_outputs.update(outputs or {})
        context = {"env": job.get("env", {}),
                   "steps": {fetch.get("id", ""): {"outputs": action_outputs}}}
        verification = next(s for s in job["steps"] if s.get("id") == "verify-rule-source")
        env = dict(self.env, **{k: resolve(v, context) for k, v in verification["env"].items()})
        return subprocess.run(["bash", "-c", verification["run"]], cwd=self.work, env=env,
                              capture_output=True, text=True, timeout=10)

    def both(self, change=lambda job: None, *, passed=False, **kwargs):
        for filename, original in zip(FILES, self.jobs):
            with self.subTest(workflow=filename):
                job = copy.deepcopy(original)
                change(job)
                result = self.guard(job, **kwargs)
                self.assertEqual(result.returncode == 0, passed, result.stdout + result.stderr)

    def test_pinned_operand_executes_verified_source(self):
        self.both(passed=True)

    def test_missing_ref_refuses_even_when_default_main_is_exact_pin(self):
        self.both(lambda j: j["steps"][0]["with"].pop("ref"))

    def test_unknown_github_context_resolves_empty_and_refuses(self):
        self.both(lambda j: j["steps"][0]["with"].update(ref="${{ github.job_workflow_sha }}"))

    def test_default_main_refuses_even_when_rule_bytes_and_commit_match(self):
        self.both(lambda j: j["steps"][0]["with"].update(ref="main"))

    def test_caller_sha_is_not_rule_source(self):
        self.both(lambda j: j["steps"][0]["with"].update(ref=self.moving_main))

    def test_missing_rule_pin_refuses(self):
        self.both(lambda j: j["env"].pop("CLOSING_RULE_SOURCE"))

    def test_actual_checkout_sha_is_independently_verified(self):
        self.both(actual=self.moving_main)

    def test_mutated_script_refuses_before_lint(self):
        self.both(mutate=True)

    def test_missing_checkout_commit_output_refuses(self):
        self.both(outputs={"commit": ""})

    def test_wrong_checkout_step_identity_refuses(self):
        self.both(lambda j: j["steps"][0].update(id="wrong-source-step"))

    def test_pin_is_independent_of_changed_main_and_its_rule(self):
        self.both(default=self.moving_main, passed=True)

    def test_moving_main_cannot_supply_mutated_rule(self):
        self.both(lambda j: j["steps"][0]["with"].update(ref="main"), default=self.moving_main)

    def test_source_guard_is_mandatory_and_precedes_unchanged_lint(self):
        for filename, job in zip(FILES, self.jobs):
            with self.subTest(workflow=filename):
                self.assertEqual(job["env"], {"CLOSING_RULE_SOURCE": PIN})
                fetch, verification, lint = job["steps"]
                self.assertEqual(fetch["with"]["ref"], "${{ env.CLOSING_RULE_SOURCE }}")
                self.assertEqual(fetch["id"], "hub-rule-source")
                self.assertEqual(fetch["uses"], "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1")
                self.assertEqual(verification["id"], "verify-rule-source")
                self.assertNotIn("continue-on-error", verification)
                self.assertNotIn("if", verification)
                self.assertNotIn("if", lint)
                self.assertNotIn("continue-on-error", lint)
                self.assertEqual(verification["env"], {
                    "RULE_SOURCE_COMMIT": "${{ env.CLOSING_RULE_SOURCE }}",
                    "RULE_CHECKOUT_REF": "${{ steps.hub-rule-source.outputs.ref }}",
                    "RULE_CHECKOUT_COMMIT": "${{ steps.hub-rule-source.outputs.commit }}",
                    "RULE_SHA256": DIGEST})
                old = yaml.safe_load(self.git("show", PIN + ":.github/workflows/" + filename, cwd=ROOT))
                self.assertEqual(lint, old["jobs"]["closing-keywords"]["steps"][-1])
                self.assertEqual(fetch["with"]["repository"], "FS-GG/.github")
                self.assertFalse(fetch["with"]["persist-credentials"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
