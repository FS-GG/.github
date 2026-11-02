"""Fixture tests for deployment/fdev-board-tend (board-reconciler design section 8, #5136 PR 8).

Offline, credential-free, stdlib only. The installer runs against a fake $HOME; the wrapper runs a fake engine
that records its argv and environment, so the tests prove what the real engine would be given: the pinned
FSGG_KIT_ROOT, the fixed working directory, no --apply in plan-only, private outputs, and the post-merge marker
rules (never swallowed, cleared only by a run that started after it was written). Nothing here touches systemd.
"""
from __future__ import annotations

import contextlib
import ast
import importlib.util
import io
import json
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
DEPLOY = ROOT / "deployment" / "fdev-board-tend"


def load(name: str, path: pathlib.Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


INSTALL = load("fdev_board_tend_install", DEPLOY / "install.py")
RUN = load("fdev_board_tend_run", DEPLOY / "fsgg-board-tend-run.py")

CONTRACT = "# Lifecycle contract\n\nBody.\n\n<!-- fsgg:board-tend-contract/v1 -->\n"
RELEASE = "0.114.2"

FAKE_ENGINE = """#!/usr/bin/python3
import json, os, pathlib, sys
here = pathlib.Path(__file__).resolve().parent
if sys.argv[1:] == ["--version"]:
    print("0.114.2.0")
    raise SystemExit(0)
config = json.loads((here / "config.json").read_text())
calls = here / "calls.jsonl"
with open(calls, "a") as handle:
    handle.write(json.dumps({"argv": sys.argv[1:], "env": dict(os.environ), "cwd": os.getcwd()}) + "\\n")
count = sum(1 for _ in open(calls))
plan = pathlib.Path(sys.argv[sys.argv.index("--plan-file") + 1])
plans = config.get("plans") or [config.get("plan", {})]
plan.write_text(json.dumps(plans[min(count, len(plans)) - 1]))
rewrite = config.get("rewrite_marker")
if rewrite and count <= config.get("rewrite_times", 1):
    tmp = rewrite + ".new"
    open(tmp, "w").write("again\\n")
    os.replace(tmp, rewrite)
print("board-v2 tend: plan-only; cards=3 apply=0 wouldApply=1 close=0 wouldClose=1 deferred=0 propose=0 refuse=0; written=0 noop=0 refused=0 uncertain=0; requests=12 graphqlPoints=12 rest=0; seconds=1.0")
raise SystemExit(config.get("exit", 0))
"""


def plan_with(close=(), apply_=(), t1i=(10, 1)):
    return {
        "schema": "fsgg.board-tend-plan/1", "ruleset": "fsgg:board-tend-rules/v1", "observedAt": "2026-11-01T10:00:00Z",
        "apply": [], "close": [],
        "wouldApply": [{"id": f"{rule}:{i}", "item": "PVTI_x", "ref": "FS-GG/.github#1", "field": "status", "preimage": "Ready",
                        "target": "In progress", "rules": [rule], "dependsOn": []} for rule, i in apply_],
        "wouldClose": [{"id": f"C1:{ref}", "item": "PVTI_y", "ref": ref, "mergedPrs": ["FS-GG/.github#9"]} for ref in close],
        "deferred": [], "propose": [{"id": "P:1", "ref": "FS-GG/.github#2", "rule": "P-LINK", "reason": "x", "suggested": None}],
        "refuse": [{"id": "S5:1", "ref": "FS-GG/.github#3", "rule": "S5", "reason": "timeline-incomplete"}],
        "t1iComparison": {"pairs": t1i[0], "disagreements": t1i[1]},
    }


class FakeHome(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.home = pathlib.Path(tmp.name).resolve()
        patch = mock.patch.dict(os.environ, {"HOME": str(self.home), "XDG_CONFIG_HOME": str(self.home / ".config")})
        patch.start()
        self.addCleanup(patch.stop)
        self.tools = self.home / ".local/share/fs-gg/tools" / f"coord-{RELEASE}"
        self.tools.mkdir(parents=True)
        self.engine = self.tools / "fsgg-coord-engine"
        self.engine.write_text(FAKE_ENGINE, encoding="utf-8")
        self.engine.chmod(0o755)
        self.kit = self.home / ".local/share/fs-gg/board-tend" / f"kit-{RELEASE}"
        for mirror in INSTALL.MIRRORS:
            (self.kit / mirror).parent.mkdir(parents=True)
            (self.kit / mirror).write_text(CONTRACT, encoding="utf-8")
        self.workdir = self.home / ".local/share/fs-gg/board-tend"
        self.unit_dir = self.home / ".config/systemd/user"
        self.libexec = self.home / ".local/libexec"
        self.credentials = self.home / ".config/fdev/credentials.json"
        self.credentials.parent.mkdir(parents=True)
        self.credentials.write_text(json.dumps({"GH_TOKEN": "fixture-token", "GITHUB_TOKEN": "other"}), encoding="utf-8")
        self.credentials.chmod(0o600)
        self.enable = self.home / ".config/fsgg/board-tend.json"

    def tree(self) -> set[str]:
        return {str(p.relative_to(self.home)) for p in self.home.rglob("*")}


class UnitInstallTests(FakeHome):
    def install(self, *extra, expect=0):
        argv = ["units", "--kit-root", str(self.kit), "--engine-path", str(self.engine), "--expect-kit-sha256", INSTALL.digest(CONTRACT.encode()), *extra]
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = INSTALL.main(argv)
        self.assertEqual(code, expect, err.getvalue())
        return out.getvalue(), err.getvalue()

    def unit(self, name):
        return (self.unit_dir / name).read_text(encoding="utf-8")

    def directives(self, name):
        """The unit text without its comment lines, which explain what is deliberately absent."""
        return "\n".join(l for l in self.unit(name).splitlines() if not l.lstrip().startswith("#")) + "\n"

    def test_service_pins_the_kit_root_workdir_runtime_cap_and_lock_directory(self):
        self.install()
        service = self.directives("fsgg-board-tend.service")
        self.assertIn(f"Environment=FSGG_KIT_ROOT={self.kit}\n", service)
        self.assertIn(f"WorkingDirectory={self.workdir}\n", service)
        self.assertIn("RuntimeMaxSec=900\n", service)
        self.assertIn("Type=exec\n", service, "RuntimeMaxSec has no effect on a oneshot service")
        # The writer lock: the runtime directory is pinned, created 0700 and kept between runs.
        self.assertIn("Environment=XDG_RUNTIME_DIR=%t\n", service)
        self.assertIn("RuntimeDirectory=fsgg\n", service)
        self.assertIn("RuntimeDirectoryMode=0700\n", service)
        self.assertIn("RuntimeDirectoryPreserve=yes\n", service)
        self.assertIn("board-writer-PVT_kwDOEYAWY84Bldpa.lock", self.unit("fsgg-board-tend.service"))
        # Private outputs and the engine is the versioned path, not the moving link.
        self.assertIn("UMask=0077\n", service)
        self.assertIn(f"--engine-path {self.engine}", service)
        self.assertNotIn(".local/bin", service)
        # The legacy /tmp lock must stay shared with the other writers.
        self.assertNotIn("PrivateTmp", service)
        self.assertEqual(len(re.findall(r"^Environment=FSGG_KIT_ROOT=", service, re.M)), 1)
        self.assertNotIn("@", service.replace("@MODE@", ""), "no unrendered placeholder")

    def test_default_is_plan_only_and_never_apply(self):
        self.install()
        exec_start = next(l for l in self.directives("fsgg-board-tend.service").splitlines() if l.startswith("ExecStart="))
        self.assertIn(" run --mode plan-only ", exec_start)
        self.assertNotIn("apply", exec_start)
        self.assertNotIn("--apply", self.directives("fsgg-board-tend.service"))
        self.assertIn("plan-only", self.unit("fsgg-board-tend.service").splitlines()[3])  # the Description

    def test_timer_is_hourly_at_minute_seven_persistent_and_randomised(self):
        self.install()
        timer = self.unit("fsgg-board-tend.timer")
        for line in ("OnCalendar=*-*-* *:07:00", "Persistent=true", "RandomizedDelaySec=120", "Unit=fsgg-board-tend.service", "WantedBy=timers.target"):
            self.assertIn(line + "\n", timer)
        daily = self.unit("fsgg-board-tend-daily.timer")
        self.assertIn("OnCalendar=*-*-* 00:30:00 UTC\n", daily)
        # The daily record is local: it summarises and posts nothing.
        service = self.directives("fsgg-board-tend-daily.service")
        self.assertIn(" summarize --date yesterday", service)
        self.assertNotIn("--credentials", service)
        self.assertNotIn("FSGG_KIT_ROOT", service)

    def test_dry_run_writes_nothing_and_lists_every_file(self):
        before = self.tree()
        out, _ = self.install("--dry-run", "--enable")
        self.assertEqual(self.tree(), before)
        self.assertEqual(out.count("would write"), 5)
        self.assertIn("would run  systemctl --user daemon-reload", out)

    def test_install_writes_only_the_four_units_and_the_wrapper(self):
        before = self.tree()
        out, _ = self.install()
        created = self.tree() - before
        self.assertEqual({p for p in created if (self.home / p).is_file()}, {
            ".config/systemd/user/fsgg-board-tend.service", ".config/systemd/user/fsgg-board-tend.timer",
            ".config/systemd/user/fsgg-board-tend-daily.service", ".config/systemd/user/fsgg-board-tend-daily.timer",
            ".local/libexec/fsgg-board-tend-run.py"})
        self.assertEqual(stat.S_IMODE((self.libexec / "fsgg-board-tend-run.py").stat().st_mode), 0o755)
        self.assertEqual(stat.S_IMODE((self.unit_dir / "fsgg-board-tend.service").stat().st_mode), 0o644)
        self.assertTrue(self.workdir.is_dir())
        self.assertFalse(self.enable.exists(), "the installer never writes the enable file")
        self.assertIn("systemd not touched", out)

    def test_install_is_idempotent_and_refuses_to_replace_a_different_file_without_force(self):
        self.install()
        out, _ = self.install()
        self.assertEqual(out.count("unchanged"), 5)
        (self.unit_dir / "fsgg-board-tend.service").write_text("edited\n", encoding="utf-8")
        _, err = self.install(expect=2)
        self.assertIn("--force", err)
        self.install("--force")
        self.assertIn("FSGG_KIT_ROOT", self.unit("fsgg-board-tend.service"))

    def test_enable_runs_systemctl_for_both_timers_only(self):
        with mock.patch.object(INSTALL.subprocess, "run") as run:
            self.install("--enable")
        self.assertEqual([c.args[0] for c in run.call_args_list], [
            ["systemctl", "--user", "daemon-reload"],
            ["systemctl", "--user", "enable", "--now", "fsgg-board-tend.timer", "fsgg-board-tend-daily.timer"]])

    def test_refusals(self):
        moving = self.home / ".local/bin/fsgg-coord-engine"
        moving.parent.mkdir(parents=True)
        moving.symlink_to(self.engine)
        base = {"--kit-root": str(self.kit), "--engine-path": str(self.engine), "--expect-kit-sha256": INSTALL.digest(CONTRACT.encode())}
        cases = {
            "the moving link, not a versioned engine": {"--engine-path": str(moving)},
            "a kit root of another release": {"--kit-root": str(self.kit.parent / "kit-0.114.1")},
            "a path outside HOME": {"--workdir": "/opt/fsgg-board-tend"},
            "a path that needs unit escaping": {"--workdir": str(self.home / "a b")},
            "apply without an enable file": {"--mode": "apply"},
        }
        for name, overrides in cases.items():
            with self.subTest(name):
                before = self.tree()
                argv = ["units"] + [part for pair in {**base, **overrides}.items() for part in pair]
                with contextlib.redirect_stderr(io.StringIO()):
                    self.assertEqual(INSTALL.main(argv), 2)
                self.assertEqual(self.tree(), before, "a refusal writes nothing")

    def test_refuses_a_kit_root_without_a_contract_and_a_git_workdir(self):
        empty = self.kit.parent / f"kit-{RELEASE}-empty"
        shutil.copytree(self.kit, empty)
        for mirror in INSTALL.MIRRORS:
            (empty / mirror).unlink()
        with contextlib.redirect_stderr(io.StringIO()) as err:
            self.assertEqual(INSTALL.main(["units", "--kit-root", str(empty), "--engine-path", str(self.engine), "--expect-kit-sha256", INSTALL.digest(CONTRACT.encode())]), 2)
        (self.workdir / ".git").mkdir(parents=True)
        with contextlib.redirect_stderr(io.StringIO()) as err:
            self.assertEqual(INSTALL.main(["units", "--kit-root", str(self.kit), "--engine-path", str(self.engine), "--expect-kit-sha256", INSTALL.digest(CONTRACT.encode())]), 2)
        self.assertIn("git checkouts", err.getvalue())

    def test_apply_mode_needs_an_enable_file_that_names_a_rule_and_renders_apply(self):
        self.enable.parent.mkdir(parents=True)
        self.enable.write_text('{"apply": ["W1h"]}', encoding="utf-8")
        self.enable.chmod(0o644)
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(INSTALL.main(["units", "--kit-root", str(self.kit), "--engine-path", str(self.engine), "--expect-kit-sha256", INSTALL.digest(CONTRACT.encode()), "--mode", "apply"]), 2)
        self.enable.chmod(0o600)
        self.install("--mode", "apply")
        self.assertIn(" run --mode apply ", self.directives("fsgg-board-tend.service"))

    def test_refuses_to_run_as_root(self):
        with mock.patch.object(INSTALL.os, "geteuid", return_value=0), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(INSTALL.main(["units", "--kit-root", str(self.kit), "--engine-path", str(self.engine), "--expect-kit-sha256", INSTALL.digest(CONTRACT.encode())]), 2)

    @unittest.skipUnless(shutil.which("systemd-analyze"), "systemd-analyze is not available")
    def test_systemd_accepts_the_rendered_units(self):
        self.install()
        files = [str(self.unit_dir / n) for n in INSTALL.UNIT_TEMPLATES]
        done = subprocess.run(["systemd-analyze", "--user", "verify", *files], capture_output=True, text=True, timeout=60, check=False)
        problems = [l for l in (done.stdout + done.stderr).splitlines() if "fsgg-board-tend" in l and not l.startswith("Could not")]
        self.assertEqual(problems, [], done.stderr)

    def test_custom_xdg_homes_are_identical_in_installer_units_wrapper_and_gate(self):
        config, state = self.home / "config-custom", self.home / "state-custom"
        self.install("--config-home", str(config), "--state-home", str(state))
        for name in ("fsgg-board-tend.service", "fsgg-board-tend-daily.service"):
            text = self.directives(name)
            self.assertIn(f"Environment=XDG_CONFIG_HOME={config}\n", text)
            self.assertIn(f"Environment=XDG_STATE_HOME={state}\n", text)
            self.assertIn(f"--config-home {config} --state-home {state}", text)
        # Execute the actual gate's path resolver against the same explicitly pinned environment.
        tree = ast.parse((ROOT / "tools/routine-delivery.py").read_text())
        function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "default_board_tend_state_root")
        ns = {"os": os, "pathlib": pathlib}
        exec(compile(ast.Module(body=[function], type_ignores=[]), "<actual-gate-resolver>", "exec"), ns)
        with mock.patch.dict(os.environ, {"XDG_STATE_HOME": str(state), "XDG_CONFIG_HOME": str(config)}):
            self.assertEqual(ns["default_board_tend_state_root"](), state / "fsgg/board-tend")


class TmpCleanupInstallTests(FakeHome):
    """The hourly dead-run /tmp cleanup timer and the chromium wrapper, installed by their own effects (#5240)."""

    def run_install(self, *argv, expect=0):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = INSTALL.main(list(argv))
        self.assertEqual(code, expect, err.getvalue())
        return out.getvalue(), err.getvalue()

    def test_writes_only_the_two_units_and_the_script_and_leaves_the_board_tend_units_alone(self):
        before = self.tree()
        self.run_install("tmp-cleanup")
        self.assertEqual(self.tree() - before, {
            ".config/systemd", ".config/systemd/user", ".local/libexec",
            ".config/systemd/user/fsgg-tmp-cleanup.service", ".config/systemd/user/fsgg-tmp-cleanup.timer",
            ".local/libexec/fdev-tmp-cleanup.py"} - before)
        script = self.libexec / "fdev-tmp-cleanup.py"
        self.assertEqual(script.read_bytes(), (ROOT / "scripts/fdev-tmp-cleanup.py").read_bytes())
        self.assertEqual(stat.S_IMODE(script.stat().st_mode), 0o755)

    def test_the_service_runs_the_installed_script_with_apply_and_cannot_see_a_private_tmp(self):
        self.run_install("tmp-cleanup")
        service = (self.unit_dir / "fsgg-tmp-cleanup.service").read_text(encoding="utf-8")
        directives = [l for l in service.splitlines() if l.strip() and not l.lstrip().startswith("#")]
        self.assertIn("Type=oneshot", directives)
        self.assertIn(f"ExecStart=/usr/bin/python3 -I {self.libexec}/fdev-tmp-cleanup.py --apply", directives)
        self.assertFalse([l for l in directives if l.startswith(("PrivateTmp", "ProtectHome", "ProtectSystem"))], directives)
        self.assertNotIn("@", "\n".join(directives))
        self.assertIn("Nice=15", directives)
        self.assertIn("IOSchedulingClass=idle", directives)

    def test_the_timer_is_hourly_at_minute_37_persistent_and_randomised(self):
        self.run_install("tmp-cleanup")
        timer = (self.unit_dir / "fsgg-tmp-cleanup.timer").read_text(encoding="utf-8")
        for line in ("OnCalendar=*-*-* *:37:00", "Persistent=true", "RandomizedDelaySec=120", "Unit=fsgg-tmp-cleanup.service", "WantedBy=timers.target"):
            self.assertIn(line, timer.splitlines())
        # minute 37 collides with no other fdev timer: board tend :07, observer :23, dispatch :53, daily 00:30
        self.assertNotIn(":07:00", timer)

    def test_dry_run_writes_nothing_is_idempotent_and_refuses_a_different_file_without_force(self):
        before = self.tree()
        out, _ = self.run_install("tmp-cleanup", "--dry-run")
        self.assertEqual(self.tree(), before)
        self.assertIn("would write", out)
        self.run_install("tmp-cleanup")
        again, _ = self.run_install("tmp-cleanup")
        self.assertIn("unchanged", again)
        (self.unit_dir / "fsgg-tmp-cleanup.timer").write_text("edited", encoding="utf-8")
        _, err = self.run_install("tmp-cleanup", expect=2)
        self.assertIn("--force", err)
        self.run_install("tmp-cleanup", "--force")
        self.assertIn("OnCalendar", (self.unit_dir / "fsgg-tmp-cleanup.timer").read_text(encoding="utf-8"))

    def test_enable_runs_systemctl_for_the_cleanup_timer_only(self):
        calls = []
        with mock.patch.object(INSTALL.subprocess, "run", side_effect=lambda cmd, **kw: calls.append(cmd)):
            self.run_install("tmp-cleanup", "--enable")
        self.assertEqual(calls, [["systemctl", "--user", "daemon-reload"], ["systemctl", "--user", "enable", "--now", "fsgg-tmp-cleanup.timer"]])
        out, _ = self.run_install("tmp-cleanup")
        self.assertIn("systemd not touched", out)

    def test_refuses_to_run_as_root_and_paths_outside_home(self):
        with mock.patch.object(INSTALL.os, "geteuid", return_value=0):
            self.run_install("tmp-cleanup", expect=2)
            self.run_install("chromium-env", expect=2)
        self.run_install("tmp-cleanup", "--unit-dir", "/etc/systemd/system", expect=2)
        self.run_install("chromium-env", "--dest", "/usr/local/bin/fdev-chromium-env", expect=2)

    def test_the_rendered_script_runs_as_a_dry_run_against_an_empty_root(self):
        self.run_install("tmp-cleanup")
        root = self.home / "sweep"
        root.mkdir()
        done = subprocess.run([sys.executable, "-I", str(self.libexec / "fdev-tmp-cleanup.py"), "--root", str(root)], capture_output=True, text=True, check=False)
        self.assertEqual(done.returncode, 0, done.stderr)
        self.assertEqual(json.loads(done.stdout)["mode"], "dry-run")

    @unittest.skipUnless(shutil.which("systemd-analyze"), "systemd-analyze is not available")
    def test_systemd_accepts_the_rendered_units(self):
        self.run_install("tmp-cleanup")
        files = [str(self.unit_dir / n) for n in ("fsgg-tmp-cleanup.service", "fsgg-tmp-cleanup.timer")]
        done = subprocess.run(["systemd-analyze", "--user", "verify", *files], capture_output=True, text=True, timeout=60, check=False)
        problems = [l for l in (done.stdout + done.stderr).splitlines() if "fsgg-tmp-cleanup" in l and not l.startswith("Could not")]
        self.assertEqual(problems, [], done.stderr)

    def test_chromium_env_installs_the_repo_source_executable_and_refuses_to_replace_without_force(self):
        dest = self.home / ".local/bin/fdev-chromium-env"
        self.run_install("chromium-env", "--dry-run")
        self.assertFalse(dest.exists())
        self.run_install("chromium-env")
        self.assertEqual(dest.read_bytes(), (ROOT / "deployment/fdev-chromium/fdev-chromium-env").read_bytes())
        self.assertEqual(stat.S_IMODE(dest.stat().st_mode), 0o755)
        dest.write_text("#!/bin/sh\n# the old installed copy\n", encoding="utf-8")
        _, err = self.run_install("chromium-env", expect=2)
        self.assertIn("--force", err)
        self.run_install("chromium-env", "--force")
        self.assertIn("TMPDIR", dest.read_text(encoding="utf-8"))


class KitRootTests(FakeHome):
    def repo(self, *texts):
        repo = self.home / "repo"
        repo.mkdir()
        env = {"GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@x", "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@x", "PATH": os.environ["PATH"], "HOME": str(self.home)}
        subprocess.run(["git", "-c", "init.defaultBranch=main", "init", "-q", str(repo)], check=True, env=env)
        for mirror, text in zip(INSTALL.MIRRORS, texts):
            (repo / mirror).parent.mkdir(parents=True, exist_ok=True)
            (repo / mirror).write_text(text, encoding="utf-8")
        subprocess.run(["git", "-C", str(repo), "add", "-A"], check=True, env=env)
        subprocess.run(["git", "-C", str(repo), "commit", "-q", "-m", "x"], check=True, env=env)
        return repo

    def run_kit(self, repo, dest, sha, *extra):
        argv = ["kit-root", "--git-dir", str(repo), "--ref", "HEAD", "--expect-sha256", sha, "--dest", str(dest), *extra]
        err = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
            return INSTALL.main(argv), err.getvalue()

    def test_writes_both_mirrors_read_only_when_the_release_digest_matches(self):
        repo = self.repo(CONTRACT, CONTRACT)
        sha = INSTALL.digest(CONTRACT.encode())
        dest = self.home / "kits" / "kit-0.114.2"
        before = self.tree()
        self.assertEqual(self.run_kit(repo, dest, sha, "--dry-run")[0], 0)
        self.assertEqual(self.tree(), before, "dry run writes nothing")
        self.assertEqual(self.run_kit(repo, dest, sha)[0], 0)
        for mirror in INSTALL.MIRRORS:
            self.assertEqual((dest / mirror).read_text(encoding="utf-8"), CONTRACT)
            self.assertEqual(stat.S_IMODE((dest / mirror).stat().st_mode), 0o444)

    def test_refuses_a_stale_a_wrong_digest_a_doubled_or_a_quoted_marker(self):
        quoted = "# C\n\n```\n<!-- fsgg:board-tend-contract/v1 -->\n```\n"
        doubled = CONTRACT + "<!-- fsgg:board-tend-contract/v1 -->\n"
        v2 = CONTRACT.replace("v1", "v2")
        for name, text in {"stale (no marker)": "# C\n", "quoted": quoted, "doubled": doubled, "other version": v2}.items():
            with self.subTest(name):
                dest = self.home / name.replace(" ", "_").replace("(", "").replace(")", "") / "kit-0.114.2"
                repo = self.home / "repo"
                shutil.rmtree(repo, ignore_errors=True)
                repo = self.repo(text, text)
                code, err = self.run_kit(repo, dest, INSTALL.digest(text.encode()))
                self.assertEqual(code, 2, err)
                self.assertFalse(dest.exists())
        shutil.rmtree(self.home / "repo")
        repo = self.repo(CONTRACT, CONTRACT)
        code, err = self.run_kit(repo, self.home / "k" / "kit-0.114.2", "0" * 64)
        self.assertEqual(code, 2)
        self.assertIn("expected release contract", err)

    def test_markers_ignore_fenced_examples(self):
        self.assertEqual(INSTALL.markers(CONTRACT), ["v1"])
        self.assertEqual(INSTALL.markers("```\n<!-- fsgg:board-tend-contract/v1 -->\n```\n"), [])
        self.assertEqual(INSTALL.markers("see `<!-- fsgg:board-tend-contract/v1 -->` inline\n"), [])


class EnableFileTests(FakeHome):
    def run_enable(self, *extra):
        err = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
            return INSTALL.main(["enable-file", "--dest", str(self.enable), *extra]), err.getvalue()

    def test_the_shipped_enable_file_is_empty_and_inside_the_engine_schema(self):
        shipped = json.loads((DEPLOY / "board-tend.json").read_text(encoding="utf-8"))
        self.assertEqual(shipped["apply"], [])
        self.assertIs(shipped["propose"], False)
        source = (ROOT / "src/FS.GG.Coord.GitHub/V2TendWrite.fs").read_text(encoding="utf-8")
        match = re.search(r'set \[ ("apply"[^\]]*)\]\)\.Contains k', source)
        self.assertIsNotNone(match, "the engine's key list moved; update this fixture")
        allowed = set(re.findall(r'"(\w+)"', match.group(1)))
        self.assertTrue(set(shipped) <= allowed, (set(shipped), allowed))
        gated = set(re.findall(r'"(\w+)"', re.search(r"let contractGated = set \[([^\]]*)\]", source).group(1)))
        self.assertTrue(gated and not (set(shipped["apply"]) & gated))

    def test_writes_mode_0600_once_and_never_replaces(self):
        self.assertEqual(self.run_enable("--dry-run")[0], 0)
        self.assertFalse(self.enable.exists())
        self.assertEqual(self.run_enable()[0], 0)
        self.assertEqual(stat.S_IMODE(self.enable.stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(self.enable.parent.stat().st_mode), 0o700)
        self.assertEqual(json.loads(self.enable.read_text(encoding="utf-8"))["apply"], [])
        self.enable.write_text('{"apply": ["W1h"]}', encoding="utf-8")
        code, err = self.run_enable()
        self.assertEqual(code, 2)
        self.assertIn('"W1h"', self.enable.read_text(encoding="utf-8"), "Root's edit survives")


class WrapperTests(FakeHome):
    def setUp(self):
        super().setUp()
        self.run_dir = self.home / "run"
        self.run_dir.mkdir(mode=0o700)
        self.state_home = self.home / "state"
        self.state = self.state_home / "fsgg/board-tend"
        self.out = self.state_home / "fsgg/board-tend-out"
        self.workdir.mkdir(parents=True, exist_ok=True)
        self.config = {"plan": plan_with(close=["FS-GG/.github#10"])}
        self.write_config()
        self.env = {"HOME": str(self.home), "PATH": "/usr/bin", "XDG_RUNTIME_DIR": str(self.run_dir), "XDG_STATE_HOME": str(self.state_home),
                    "XDG_CONFIG_HOME": str(self.home / ".config"), "FSGG_KIT_ROOT": str(self.kit),
                    "GITHUB_TOKEN": "parent-token", "TMPDIR": "/somewhere", "UNRELATED": "x"}

    def write_config(self):
        (self.tools / "config.json").write_text(json.dumps(self.config), encoding="utf-8")

    def args(self, *extra):
        return RUN.parser().parse_args(["run", "--engine-path", str(self.engine), "--expect-kit-sha256", INSTALL.digest(CONTRACT.encode()), "--credentials", str(self.credentials),
                                        "--workdir", str(self.workdir), "--config-home", self.env["XDG_CONFIG_HOME"],
                                        "--state-home", self.env["XDG_STATE_HOME"], "--min-interval", "0", *extra])

    def go(self, *extra, env=None, cwd=None):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()) as err:
            code = RUN.command_run(self.args(*extra), env=env or self.env, cwd=cwd or self.workdir)
        self.err = err.getvalue()
        return code, out.getvalue()

    def calls(self):
        path = self.tools / "calls.jsonl"
        return [json.loads(l) for l in path.read_text().splitlines()] if path.exists() else []

    def write_marker(self):
        self.state.mkdir(parents=True, exist_ok=True, mode=0o700)
        tmp = self.state / ".m"
        tmp.write_text("merge\n")
        os.replace(tmp, self.state / RUN.MARKER)

    def index(self):
        return [json.loads(l) for l in (self.out / "index.jsonl").read_text().splitlines()]

    # ---- the pin ----

    def test_the_engine_runs_with_the_pinned_kit_root_and_a_scrubbed_environment(self):
        self.env["FSGG_KIT_ROOT"] = str(self.kit)
        code, _ = self.go()
        self.assertEqual(code, 0)
        call = self.calls()[0]
        self.assertEqual(call["env"]["FSGG_KIT_ROOT"], str(self.kit))
        self.assertEqual(call["env"]["GH_TOKEN"], "fixture-token")
        for leaked in ("GITHUB_TOKEN", "TMPDIR", "UNRELATED"):
            self.assertNotIn(leaked, call["env"])
        self.assertEqual(call["env"]["XDG_STATE_HOME"], str(self.state_home))
        self.assertEqual(call["argv"][:2], ["board-v2", "tend"])

    def test_refuses_without_running_the_engine_when_the_pin_does_not_hold(self):
        other = self.home / "some-checkout"
        other.mkdir()
        bare = self.home / "bare"
        bare.mkdir()
        cases = {
            "unset": ({k: v for k, v in self.env.items() if k != "FSGG_KIT_ROOT"}, None),
            "relative": (dict(self.env, FSGG_KIT_ROOT="kit"), None),
            "missing directory": (dict(self.env, FSGG_KIT_ROOT=str(self.home / "nope")), None),
            "not a kit root": (dict(self.env, FSGG_KIT_ROOT=str(bare)), None),
            "started in another directory": (self.env, other),
            "no runtime directory": ({k: v for k, v in self.env.items() if k != "XDG_RUNTIME_DIR"}, None),
        }
        for name, (env, cwd) in cases.items():
            with self.subTest(name):
                code, _ = self.go(env=env, cwd=cwd)
                self.assertEqual(code, 2, self.err)
        self.assertEqual(self.calls(), [], "the engine was never started")

    def test_refuses_a_credentials_file_that_is_not_private_or_has_no_token(self):
        self.credentials.chmod(0o644)
        self.assertEqual(self.go()[0], 2)
        self.credentials.chmod(0o600)
        self.credentials.write_text("{}", encoding="utf-8")
        self.assertEqual(self.go()[0], 2)
        self.assertNotIn("fixture-token", self.err)
        self.assertEqual(self.calls(), [])

    def test_plan_only_never_passes_apply_and_apply_mode_does(self):
        self.go()
        self.assertNotIn("--apply", self.calls()[0]["argv"])
        self.go("--mode", "apply")
        self.assertIn("--apply", self.calls()[1]["argv"])

    # ---- private output ----

    def test_outputs_are_private_and_the_run_is_indexed(self):
        code, out = self.go()
        self.assertEqual(code, 0)
        for directory in (self.out, self.out / "runs", *(self.out / "runs").iterdir()):
            self.assertEqual(stat.S_IMODE(directory.stat().st_mode), 0o700, directory)
        record = json.loads((self.out / "last-run.json").read_text())
        run_dir = pathlib.Path(record["runDir"])
        self.assertEqual(run_dir.parent, self.out / "runs")
        self.assertEqual(record["plan"], str(run_dir / "plan.json"))
        for name in ("plan.json", "engine.out", "engine.err", "meta.json"):
            self.assertEqual(stat.S_IMODE((run_dir / name).stat().st_mode), 0o600, name)
        self.assertEqual(record["mode"], "plan-only")
        self.assertEqual(record["writerLock"], str(self.run_dir / "fsgg/board-writer-PVT_kwDOEYAWY84Bldpa.lock"))
        self.assertEqual(record["engineVersion"], "0.114.2.0")
        self.assertIn("board-v2 tend: plan-only", out)
        self.assertNotIn("fixture-token", json.dumps(record) + out)

    def test_refuses_a_group_readable_output_directory(self):
        self.out.mkdir(parents=True)
        self.out.chmod(0o750)
        self.assertEqual(self.go()[0], 2)
        self.assertEqual(self.calls(), [])

    def test_old_run_directories_are_pruned_to_the_newest(self):
        for i in range(5):
            self.go("--keep", "3")
            runs = sorted((self.out / "runs").iterdir())
            os.utime(runs[-1], (1000 + i, 1000 + i))
            # distinct names: the fake clock is the wall clock, so rename to keep them ordered
            runs[-1].rename(runs[-1].with_name(f"2026010{i + 1}T000000Z-00"))
        self.assertEqual(len(list((self.out / "runs").iterdir())), 3)
        self.assertEqual(sorted(p.name for p in (self.out / "runs").iterdir())[0], "20260103T000000Z-00")

    # ---- the marker ----

    def test_a_timer_run_inside_the_minimum_interval_is_skipped_but_a_pending_marker_runs(self):
        self.assertEqual(self.go("--min-interval", "300")[0], 0)
        self.assertEqual(len(self.calls()), 1)
        code, out = self.go("--min-interval", "300")
        self.assertEqual(len(self.calls()), 1, "no marker, inside the interval: skipped")
        self.assertIn("skipped", out)
        self.assertEqual(self.index()[-1]["event"], "skipped")
        self.write_marker()
        self.assertEqual(self.go("--min-interval", "300")[0], 0)
        self.assertEqual(len(self.calls()), 2, "a post-merge marker ignores the minimum interval")
        self.assertEqual(self.index()[-1]["trigger"], "post-merge")

    def test_a_run_that_started_after_the_marker_clears_it(self):
        self.write_marker()
        self.assertEqual(self.go()[0], 0)
        self.assertFalse((self.state / RUN.MARKER).exists())
        self.assertEqual(self.index()[-1]["marker"], "cleared")

    def test_a_marker_written_during_a_run_is_not_cleared_by_it_and_runs_again(self):
        self.write_marker()
        self.config.update(rewrite_marker=str(self.state / RUN.MARKER), rewrite_times=1)
        self.write_config()
        self.assertEqual(self.go()[0], 0)
        self.assertEqual(len(self.calls()), 2, "the newer marker caused one more run")
        self.assertEqual([e["marker"] for e in self.index()], ["newer", "cleared"])
        self.assertFalse((self.state / RUN.MARKER).exists())

    def test_a_marker_that_keeps_arriving_is_kept_after_the_pass_limit(self):
        self.write_marker()
        self.config.update(rewrite_marker=str(self.state / RUN.MARKER), rewrite_times=99)
        self.write_config()
        self.assertEqual(self.go("--max-passes", "2")[0], 0)
        self.assertEqual(len(self.calls()), 2)
        self.assertTrue((self.state / RUN.MARKER).exists(), "left for the next run, never swallowed")

    def test_a_failed_run_keeps_the_marker_and_propagates_its_exit_code(self):
        self.write_marker()
        self.config["exit"] = 3
        self.write_config()
        self.assertEqual(self.go()[0], 3)
        self.assertTrue((self.state / RUN.CLAIMED_MARKER).exists())
        self.assertFalse((self.state / RUN.MARKER).exists())
        self.assertEqual(self.index()[-1]["marker"], "kept-failed")

    def test_a_second_wrapper_while_one_holds_the_run_lock_does_nothing_and_keeps_the_marker(self):
        self.out.mkdir(parents=True, mode=0o700)
        import fcntl
        handle = os.open(self.out / "run.lock", os.O_RDWR | os.O_CREAT, 0o600)
        self.addCleanup(os.close, handle)
        fcntl.flock(handle, fcntl.LOCK_EX)
        self.write_marker()
        self.assertEqual(self.go()[0], 0)
        self.assertEqual(self.calls(), [])
        self.assertTrue((self.state / RUN.MARKER).exists())

    def test_two_runs_in_one_second_get_distinct_private_directories(self):
        moment = RUN.now_utc()
        runs = RUN.private_directory(self.out / "runs")
        first, second = RUN.new_run_directory(runs, moment, 0), RUN.new_run_directory(runs, moment, 0)
        self.assertNotEqual(first, second)
        self.assertEqual(stat.S_IMODE(second.stat().st_mode), 0o700)

    def test_the_marker_name_agrees_with_the_merge_gate(self):
        gate = (ROOT / "tools/routine-delivery.py").read_text(encoding="utf-8")
        self.assertIn(f'BOARD_TEND_MARKER = "{RUN.MARKER}"', gate)
        self.assertIn('BOARD_TEND_UNIT = "fsgg-board-tend.service"', gate)
        self.assertIn(f"board-writer-{RUN.PROJECT_ID}.lock", gate)
        engine = (ROOT / "src/FS.GG.Coord.GitHub/V2WriterLock.fs").read_text(encoding="utf-8")
        self.assertIn('"board-writer-" + projectId + ".lock"', engine)
        self.assertIn(RUN.PROJECT_ID, (ROOT / "src/FS.GG.Coord.Cli/BoardV2Application.fs").read_text(encoding="utf-8"))

    # ---- the daily record ----

    def test_daily_record_summarises_would_apply_would_close_and_t1i_privately(self):
        self.config["plans"] = [
            plan_with(close=["FS-GG/.github#10", "FS-GG/Other#7"], apply_=[("O1", 1), ("S3", 2)], t1i=(222, 73)),
            plan_with(close=["FS-GG/.github#10"], apply_=[("O1", 1)], t1i=(222, 11)),
        ]
        self.write_config()
        self.go()
        self.go()
        code = io.StringIO()
        today = RUN.now_utc().date().isoformat()
        with contextlib.redirect_stdout(code):
            self.assertEqual(RUN.command_summarize(RUN.parser().parse_args(["summarize", "--date", today, "--out-dir", str(self.out), "--config-home", self.env["XDG_CONFIG_HOME"], "--state-home", self.env["XDG_STATE_HOME"]]), env=self.env), 0)
        daily = json.loads((self.out / "daily" / f"{today}.json").read_text())
        self.assertEqual((daily["runs"], daily["plansRead"], daily["applyModeRuns"], daily["plansWithApply"]), (2, 2, 0, 0))
        self.assertEqual(daily["wouldApplyByRule"]["O1"]["stableAllDay"], 1)
        self.assertEqual(daily["wouldApplyByRule"]["S3"]["stableAllDay"], 0)
        self.assertEqual(daily["wouldClose"]["distinct"], 2)
        self.assertEqual(daily["wouldClose"]["stableAllDay"], ["C1:FS-GG/.github#10"])
        self.assertEqual((daily["t1i"]["lastRate"], daily["t1i"]["maxRate"]), (round(11 / 222, 4), round(73 / 222, 4)))
        for path in ((self.out / "daily"), (self.out / "daily" / f"{today}.json"), (self.out / "daily" / f"{today}.md")):
            self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o700 if path.is_dir() else 0o600)
        body = (self.out / "daily" / f"{today}.md").read_text()
        self.assertIn("FS-GG/.github#10", body)
        self.assertNotIn("FS-GG/Other#7", body, "a comment names only FS-GG/.github references")
        self.assertIn("+1 in other repositories", body)
        self.assertIn(f"<!-- fsgg:board-tend-observation/v1 date={today} -->", body)

    def test_a_day_without_runs_is_recorded_as_zero(self):
        self.out.mkdir(parents=True, mode=0o700)
        daily = RUN.summarize(self.out, "2026-01-01")
        self.assertEqual((daily["runs"], daily["plansRead"]), (0, 0))
        self.assertIn("Runs: 0", RUN.render_comment(daily))

    def test_runtime_rejects_changed_missing_or_checkout_kit_mirrors(self):
        mirror = self.kit / INSTALL.MIRRORS[1]
        original = mirror.read_text()
        mirror.write_text(original + "changed")
        self.assertEqual(self.go()[0], 2)
        mirror.unlink()
        self.assertEqual(self.go()[0], 2)
        mirror.write_text(original)
        (self.kit / ".git").write_text("gitdir: elsewhere")
        self.assertEqual(self.go()[0], 2)
        self.assertEqual(self.calls(), [])

    def test_runtime_rejects_wrong_real_engine_version_before_board_call(self):
        self.engine.write_text(FAKE_ENGINE.replace('print("0.114.2.0")', 'print("0.114.1.0")'))
        self.assertEqual(self.go()[0], 2)
        self.assertEqual(self.calls(), [])

    def test_marker_is_claimed_before_work_and_concurrent_pending_is_never_unlinked(self):
        self.write_marker()
        run_once = RUN.run_once
        def work(*args):
            self.assertFalse((self.state / RUN.MARKER).exists(), "pending must have been renamed before work")
            self.assertTrue((self.state / (RUN.MARKER + ".claimed")).exists())
            return run_once(*args)
        with mock.patch.object(RUN, "run_once", side_effect=work):
            self.assertEqual(self.go()[0], 0)

    def test_trigger_arriving_at_claim_deletion_survives_and_runs_next_time(self):
        self.write_marker()
        unlink = RUN.os.unlink
        def concurrent(path, *args, **kwargs):
            if pathlib.Path(path).name == RUN.CLAIMED_MARKER:
                self.write_marker()
            return unlink(path, *args, **kwargs)
        with mock.patch.object(RUN.os, "unlink", side_effect=concurrent):
            self.assertEqual(self.go()[0], 0)
        self.assertTrue((self.state / RUN.MARKER).exists())
        self.assertFalse((self.state / RUN.CLAIMED_MARKER).exists())
        self.assertEqual(len(self.calls()), 1)
        self.assertEqual(self.go()[0], 0)
        self.assertEqual(len(self.calls()), 2)
        self.assertFalse((self.state / RUN.MARKER).exists())

    def test_abandoned_claim_survives_failed_run_and_is_recovered(self):
        self.state.mkdir(parents=True, mode=0o700)
        (self.state / RUN.CLAIMED_MARKER).write_text("interrupted prior wrapper")
        self.config["exit"] = 3; self.write_config()
        self.assertEqual(self.go()[0], 3)
        self.assertTrue((self.state / RUN.CLAIMED_MARKER).exists())
        self.config["exit"] = 0; self.write_config()
        self.assertEqual(self.go("--min-interval", "300")[0], 0)
        self.assertEqual(len(self.calls()), 2)
        self.assertFalse((self.state / RUN.CLAIMED_MARKER).exists())

    def test_missing_workdir_and_changed_xdg_homes_refuse_before_board_work(self):
        args = ["run", "--engine-path", str(self.engine), "--credentials", str(self.credentials),
                "--expect-kit-sha256", INSTALL.digest(CONTRACT.encode()),
                "--config-home", self.env["XDG_CONFIG_HOME"], "--state-home", self.env["XDG_STATE_HOME"]]
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
            RUN.parser().parse_args(args)
        self.assertEqual(error.exception.code, 2)
        for key in ("XDG_CONFIG_HOME", "XDG_STATE_HOME"):
            with self.subTest(key=key):
                self.assertEqual(self.go(env=dict(self.env, **{key: str(self.home / "other")}))[0], 2)
        self.assertEqual(self.calls(), [])

    def test_wrong_mode_state_directory_is_loud_and_never_runs_engine(self):
        self.state.mkdir(parents=True, mode=0o755)
        self.state.chmod(0o755)
        self.assertEqual(self.go()[0], 2)
        self.assertIn("private", self.err)
        self.assertEqual(self.calls(), [])

    def test_daily_summary_counts_json_valid_malformed_plans_as_unreadable(self):
        self.go()
        latest = pathlib.Path(json.loads((self.out / "last-run.json").read_text())["plan"])
        for broken in [{"wouldApply": [None]}, {"wouldClose": [{}]}, {"propose": 42},
                       {"refuse": ["bad"]}, {"t1iComparison": {"pairs": "bad", "disagreements": 1}}]:
            with self.subTest(broken=broken):
                latest.write_text(json.dumps(dict(plan_with(), **broken)))
                daily = RUN.summarize(self.out, RUN.now_utc().date().isoformat())
                self.assertEqual((daily["plansRead"], daily["plansUnreadable"]), (0, 1))


if __name__ == "__main__":
    unittest.main()
