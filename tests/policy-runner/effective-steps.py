#!/usr/bin/env python3
"""Negative controls for executable checker/fixture workflow wiring."""

import importlib.util
import json
from pathlib import Path
import tempfile


ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("policy_runner", ROOT / "scripts/policy-runner.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


def check(workflow: str, should_pass: bool, label: str, *, fixture_body: str = "#!/bin/sh\npython3 scripts/check-alpha.py\n") -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        for parent in ("scripts", "tests/alpha", ".github/workflows"):
            (root / parent).mkdir(parents=True)
        (root / "scripts/check-alpha.py").write_text("# line\n" * 5, encoding="utf-8")
        (root / "tests/alpha/run.sh").write_text(fixture_body, encoding="utf-8")
        (root / ".github/workflows/alpha.yml").write_text(workflow, encoding="utf-8")
        inventory = {"large_checker_threshold_lines": 5, "checkers": [{
            "source": "scripts/check-alpha.py", "owner": "maintainers",
            "fixture": "tests/alpha/run.sh", "workflow": ".github/workflows/alpha.yml"}]}
        path = root / "scripts/policy-checkers.json"
        path.write_text(json.dumps(inventory), encoding="utf-8")
        try:
            runner.inventory(root, path)
        except runner.PolicyError:
            if should_pass:
                raise AssertionError(f"{label}: effective wiring rejected")
        else:
            if not should_pass:
                raise AssertionError(f"{label}: inert wiring passed")


def workflow(body: str, *, job_if: str = "", step_if: str = "", continue_on_error: str = "") -> str:
    job_line = f"    if: {job_if}\n" if job_if else ""
    step_line = f"        if: {step_if}\n" if step_if else ""
    continue_line = f"        continue-on-error: {continue_on_error}\n" if continue_on_error else ""
    return ("name: fixture\njobs:\n  gate:\n" + job_line +
            "    runs-on: ubuntu-latest\n    steps:\n      - name: gate\n" +
            step_line + continue_line + "        run: |\n" +
            "".join(f"          {line}\n" for line in body.splitlines()))


direct = "python3 scripts/check-alpha.py\nbash tests/alpha/run.sh"
check(workflow(direct), True, "direct executable commands")
check(workflow("echo harmless") + "# python3 scripts/check-alpha.py; bash tests/alpha/run.sh\n", False,
      "workflow comments")
check(workflow("echo 'python3 scripts/check-alpha.py; bash tests/alpha/run.sh'"), False,
      "inert shell string")
check(workflow("python3 scripts/check-alpha.py\necho harmless # bash tests/alpha/run.sh"), False,
      "fixture in inline shell comment")
check(workflow("bash tests/alpha/run.sh\necho harmless # out=\"$(python scripts/check-alpha.py)\""),
      False, "checker in inline shell comment", fixture_body="#!/bin/sh\necho harmless\n")
check(workflow(direct, job_if="false"), False, "disabled job")
check(workflow(direct, step_if="${{ false }}"), False, "disabled step")
check(workflow(direct, step_if="${{ false && github.event_name == 'push' }}"), False,
      "statically false dynamic step")
check(workflow(direct, continue_on_error="true"), False, "non-gating step")
check(workflow(direct, continue_on_error='"true"'), False, "quoted non-gating step")
check(workflow(direct, continue_on_error="${{ github.event_name == 'push' }}"), False,
      "dynamic non-gating step")
check(workflow("if false; then\n  " + direct.replace("\n", "\n  ") + "\nfi"), False,
      "unreachable shell branch")
check(workflow("cat <<'EOF'\n" + direct + "\nEOF"), False, "inert heredoc")
check(workflow("bash tests/alpha/run.sh"), True, "checker via executable fixture")
check(workflow("bash tests/alpha/run.sh"), False, "checker only in fixture comment",
      fixture_body="#!/bin/sh\necho harmless # scripts/check-alpha.py\n")
check(workflow("bash tests/alpha/run.sh"), False, "checker only in fixture echo",
      fixture_body="#!/bin/sh\necho 'python3 scripts/check-alpha.py'\n")
check(workflow("bash tests/alpha/run.sh"), False, "checker variable never invoked",
      fixture_body="#!/bin/sh\nGATE=\"$ROOT/scripts/check-alpha.py\"\necho \"$GATE\"\n")
check(workflow("bash tests/alpha/run.sh"), False, "checker variable only in dead fixture branch",
      fixture_body="#!/bin/sh\nGATE=\"$ROOT/scripts/check-alpha.py\"\nif false; then\n  python3 \"$GATE\"\nfi\n")
check(workflow("bash tests/alpha/run.sh"), False, "direct checker only in dead fixture branch",
      fixture_body="#!/bin/sh\nif false; then\n  python3 scripts/check-alpha.py\nfi\n")
check(workflow("bash tests/alpha/run.sh"), True, "checker invoked through fixture variable",
      fixture_body="#!/bin/sh\nGATE=\"$ROOT/scripts/check-alpha.py\"\npython3 \"$GATE\"\n")
check(workflow(direct, step_if="${{ github.event_name == 'pull_request' }}"), True,
      "event-scoped executable step")
print("policy effective-step controls: ok")
