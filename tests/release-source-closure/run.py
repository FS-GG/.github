#!/usr/bin/env python3
"""Causal controls for pinned release sources against a moving main (#5107).

Each case builds real Git history in a private fixture repository and runs the actual
closure module, CLI and publisher admission predicates against it:

* main moved outside the release closure            -> accepted
* main moved inside the closure (each derived kind) -> refused
* pinned source is not an ancestor of main          -> refused

The final class derives the closure of the real repository at HEAD and checks it
against the authoritative readers (repos.sh, MSBuild project graph, workflows).
"""

from __future__ import annotations

import contextlib
import io
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

import release_source_closure as closure  # noqa: E402
import release_successor_admission  # noqa: E402
import telemetry_host_successor_admission  # noqa: E402
from release_successor_execution import ordered_effects  # noqa: E402

FIXTURE = {
    ".github/workflows/release-successor-candidate.yml": (
        "jobs:\n  candidate:\n    steps:\n"
        "      - run: python3 tests/release-successor-candidate/run.py\n"
        "      - run: python3 scripts/release-saga.py prepare\n"
        "      - run: dotnet pack src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj\n"
        "  reuse:\n    uses: ./.github/workflows/reusable.yml\n"),
    ".github/workflows/reusable.yml": "jobs: {}\n",
    ".github/workflows/release-successor-publish.yml": "jobs: {}\n# python3 scripts/release-successor-publish.py\n",
    ".github/workflows/release-telemetry-host-successor-candidate.yml": "jobs: {}\n# scripts/telemetry-host-release.py\n",
    ".github/workflows/release-telemetry-host-successor-publish.yml": "jobs: {}\n# scripts/telemetry-host-successor-publish.py\n",
    ".github/workflows/unrelated.yml": "jobs: {}\n",
    "tests/release-successor-candidate/run.py": "print('candidate gate')\n",
    "tests/unrelated/run.py": "print('unrelated')\n",
    "scripts/release-saga.py": ("import pathlib\nimport release_helper\nDATA = 'registry/release-data.json'\n"
                                "ROOT = pathlib.Path(__file__).resolve().parents[1]\n"
                                "BUILT = ROOT / 'contracts' / 'built' / 'schema.json'\n"),
    "contracts/built/schema.json": "{}\n",
    "contracts/unrelated/schema.json": "{}\n",
    "tools/readback.py": "VALUE = 1\n",
    ".github/actions/setup/action.yml": "runs: {}\n",
    "README.md": "readme\n",
    "scripts/release_helper.py": "VALUE = 1\n",
    "scripts/check-release-candidate-uniqueness.py": "VALUE = 1\n",
    "scripts/release-successor-publish.py": "import release_publisher_helper\n",
    "scripts/release_publisher_helper.py": "VALUE = 1\n",
    "scripts/telemetry-host-release.py": "VALUE = 1\n",
    "scripts/telemetry-host-candidate-read-gate.py": "VALUE = 1\n",
    "scripts/telemetry-host-successor-publish.py": "VALUE = 1\n",
    "scripts/unrelated.py": "VALUE = 1\n",
    "scripts/repos.sh": "#!/bin/sh\n",
    "scripts/sync-build-config.sh": "#!/bin/sh\n",
    "registry/release-data.json": "{}\n",
    "registry/repos.yml": (
        "repos: []\n"
        "kit:\n"
        "  # comment rows are allowed\n"
        "  - { id: pnext-item,  kind: skill,  source: .claude/skills/pnext-item }\n"
        "  - { id: fsgg-coord,  kind: client, source: scripts/fsgg-coord }\n"
        "  - { id: args,        kind: config, source: scripts/lib/args.sh, dest: scripts/lib/args.sh }\n"
        "other: {}\n"),
    "registry/repos.lock": "lock\n",
    "registry/driver-skill-manifest.json": json.dumps({"skills": [
        {"id": "work-board", "scope": "driver", "supplied-by": ".claude/skills/work-board"},
        {"id": "drive-board", "scope": "operator", "supplied-by": ".claude/skills/drive-board"}]}),
    "registry/coordination-kit-skill-manifest.json": "{}\n",
    ".claude/skills/pnext-item/SKILL.md": "kit skill\n",
    ".claude/skills/work-board/SKILL.md": "driver skill\n",
    ".claude/skills/drive-board/SKILL.md": "operator skill, never staged\n",
    ".claude/skills/unrelated/SKILL.md": "unrelated skill\n",
    "scripts/fsgg-coord": "#!/bin/sh\n",
    "scripts/lib/args.sh": "#!/bin/sh\n",
    "scripts/lib/unrelated.sh": "#!/bin/sh\n",
    "dist/dotnet/Directory.Build.props": "<Project/>\n",
    "global.json": "{}\n",
    "Directory.Build.props": "<Project/>\n",
    "Directory.Packages.props": "<Project/>\n",
    "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj": (
        "<Project><ItemGroup>"
        "<ProjectReference Include=\"../FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj\" />"
        "</ItemGroup></Project>\n"),
    "src/FS.GG.Coord.Cli/Program.fs": "module Program\n",
    "src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj": (
        "<Project><ItemGroup><Compile Include=\"../Shared/Linked.fs\" Link=\"Linked.fs\" />"
        "</ItemGroup></Project>\n"),
    "src/FS.GG.Coord.Core/Core.fs": "module Core\n",
    "src/Shared/Linked.fs": "module Linked\n",
    "src/Shared/NotLinked.fs": "module NotLinked\n",
    "src/FS.GG.Kit/FS.GG.Kit.csproj": "<Project/>\n",
    "src/FS.GG.Drivers/FS.GG.Drivers.csproj": "<Project/>\n",
    "src/FS.GG.Drivers/workspace-inventory.json": json.dumps({"files": [{"source": ".fsgg/atlas.json", "path": ".fsgg/atlas.json"}]}),
    ".fsgg/atlas.json": "{}\n",
    ".fsgg/unrelated.json": "{}\n",
    "src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj": (
        "<Project><ItemGroup><Compile Include=\"../FS.GG.Coord.Cli/Program.fs\" Link=\"Program.fs\" />"
        "</ItemGroup></Project>\n"),
    "src/FS.GG.Unrelated/FS.GG.Unrelated.fsproj": "<Project/>\n",
    "docs/notes.md": "notes\n",
}


class Repo:
    def __init__(self, path: pathlib.Path):
        self.path = path
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("config", "user.name", "fixture")
        self.git("config", "commit.gpgsign", "false")
        for name, text in FIXTURE.items():
            self.write(name, text)
        (path / "scripts").mkdir(exist_ok=True)
        shutil.copy2(ROOT / closure.SELF, path / closure.SELF)
        self.base = self.commit("base")

    def git(self, *args: str) -> str:
        return subprocess.run(["git", "-C", str(self.path), *args], check=True, capture_output=True,
                              text=True, timeout=60).stdout.strip()

    def write(self, name: str, text: str) -> None:
        target = self.path / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)

    def commit(self, message: str) -> str:
        self.git("add", "-A")
        self.git("commit", "-q", "--allow-empty", "-m", message)
        return self.git("rev-parse", "HEAD")

    def change(self, name: str, text: str = "changed\n", *, delete: bool = False) -> str:
        if delete:
            (self.path / name).unlink()
        else:
            self.write(name, text)
        return self.commit(f"change {name}")


class FixtureTest(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary = tempfile.TemporaryDirectory(prefix="release-closure-")
        self.repo = Repo(pathlib.Path(self._temporary.name))
        closure._ACCEPTED.clear()

    def tearDown(self) -> None:
        closure._ACCEPTED.clear()
        self._temporary.cleanup()

    def local(self, route: str, source: str, live: str) -> dict:
        return closure.verify_local(route, source, live, root=self.repo.path)


class LocalAncestryAndClosureTests(FixtureTest):
    OUTSIDE = ("docs/notes.md", ".github/workflows/unrelated.yml", "tests/unrelated/run.py",
               ".claude/skills/unrelated/SKILL.md", ".claude/skills/drive-board/SKILL.md", "src/Shared/NotLinked.fs",
               "src/FS.GG.Unrelated/FS.GG.Unrelated.fsproj", ".fsgg/unrelated.json", "README.md",
               "contracts/unrelated/schema.json", "docs/new-page.md", ".github/workflows/new-unrelated.yml")
    INSIDE = {
        "packed project source": "src/FS.GG.Coord.Cli/Program.fs",
        "transitive project reference": "src/FS.GG.Coord.Core/Core.fs",
        "linked compile outside the project": "src/Shared/Linked.fs",
        "new ancestor Directory.Build.props": "src/Directory.Build.props",
        "root build configuration": "global.json",
        "candidate workflow": ".github/workflows/release-successor-candidate.yml",
        "reusable workflow named ./.github/...": ".github/workflows/reusable.yml",
        "workflow-invoked qualification unit": "tests/release-successor-candidate/fixture.json",
        "workflow-invoked release script": "scripts/release-saga.py",
        "imported release module": "scripts/release_helper.py",
        "release script literal data path": "registry/release-data.json",
        "release script pathlib-built data path": "contracts/built/schema.json",
        "kit skill member": ".claude/skills/pnext-item/references/new.md",
        "kit client": "scripts/fsgg-coord",
        "kit config": "scripts/lib/args.sh",
        "kit registry": "registry/repos.yml",
        "driver skill member": ".claude/skills/work-board/SKILL.md",
        "driver workspace inventory source": ".fsgg/atlas.json",
        "build-config staging": "dist/dotnet/Directory.Build.props",
        "closure definition itself": closure.SELF,
        "fail-closed: any script": "scripts/unrelated.py",
        "fail-closed: any script library": "scripts/lib/unrelated.sh",
        "fail-closed: shadowing module added to scripts/": "scripts/json.py",
        "fail-closed: any tool": "tools/readback.py",
        "fail-closed: any composite action": ".github/actions/setup/action.yml",
        "fail-closed: a new composite action": ".github/actions/new/action.yml",
        "added root file": "Directory.Build.rsp",
        "added .gitattributes": ".gitattributes",
        "added NuGet.config beside a closure file": "src/NuGet.config",
        "added file beside a closure data file": ".fsgg/new.json",
        "added file in a release data directory": "registry/new.yml",
    }

    def test_main_moved_outside_the_closure_is_accepted(self) -> None:
        for path in self.OUTSIDE:
            with self.subTest(path=path):
                live = self.repo.change(path, "moved outside\n")
                result = self.local("coherent-set", self.repo.base, live)
                self.assertEqual(result["closureChanged"], [])
                self.assertGreater(result["changed"], 0)

    def test_several_unrelated_merges_are_accepted_together(self) -> None:
        for path in self.OUTSIDE[:4]:
            live = self.repo.change(path, "burst\n")
        self.assertEqual(self.local("coherent-set", self.repo.base, live)["changed"], 4)

    def test_uncovered_msbuild_property_path_refuses(self) -> None:
        self.repo.write("src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj", FIXTURE["src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj"]
                        .replace("</ItemGroup>", "<Compile Include=\"$(SharedRoot)/Hidden.fs\" /></ItemGroup>"))
        source = self.repo.commit("property path")
        with self.assertRaisesRegex(closure.ClosureRefused, "MSBuild property path is not covered"):
            self.local("coherent-set", source, self.repo.change("docs/notes.md"))

    def test_escaping_msbuild_glob_is_covered_by_its_static_prefix(self) -> None:
        self.repo.write("src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj", FIXTURE["src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj"]
                        .replace("</ItemGroup>", "<None Include=\"../../contracts/unrelated/**/*.json\" /></ItemGroup>"))
        source = self.repo.commit("escaping glob")
        with self.assertRaisesRegex(closure.ClosureRefused, "contracts/unrelated/schema.json"):
            self.local("coherent-set", source, self.repo.change("contracts/unrelated/schema.json"))

    def test_main_moved_inside_the_closure_is_refused(self) -> None:
        for reason, path in self.INSIDE.items():
            with self.subTest(reason=reason):
                live = self.repo.change(path, "moved inside\n")
                with self.assertRaisesRegex(closure.ClosureRefused, "release closure changed"):
                    self.local("coherent-set", self.repo.base, live)
                self.repo.git("reset", "-q", "--hard", self.repo.base)

    def test_a_deleted_or_renamed_closure_file_is_refused(self) -> None:
        live = self.repo.change("src/FS.GG.Coord.Core/Core.fs", delete=True)
        with self.assertRaisesRegex(closure.ClosureRefused, "Core.fs"):
            self.local("coherent-set", self.repo.base, live)
        self.repo.git("reset", "-q", "--hard", self.repo.base)
        self.repo.git("mv", "src/Shared/Linked.fs", "docs/Linked.fs")
        live = self.repo.commit("rename out of the closure")
        with self.assertRaisesRegex(closure.ClosureRefused, "src/Shared/Linked.fs"):
            self.local("coherent-set", self.repo.base, live)

    def test_an_inside_change_reverted_before_the_check_is_net_unchanged(self) -> None:
        self.repo.change("src/FS.GG.Coord.Cli/Program.fs", "transient\n")
        live = self.repo.change("src/FS.GG.Coord.Cli/Program.fs", FIXTURE["src/FS.GG.Coord.Cli/Program.fs"])
        # Net-identical trees: the diff is empty, so the pinned closure bytes are unchanged.
        self.assertEqual(self.local("coherent-set", self.repo.base, live)["changed"], 0)

    def test_non_ancestor_is_refused(self) -> None:
        outside = self.repo.change("docs/notes.md", "on main\n")
        self.repo.git("checkout", "-q", "-b", "side", self.repo.base)
        side = self.repo.change("docs/side.md", "side branch\n")
        with self.assertRaisesRegex(closure.ClosureRefused, "not an ancestor"):
            self.local("coherent-set", side, outside)
        # A later source is never admitted against an older main either.
        with self.assertRaisesRegex(closure.ClosureRefused, "not an ancestor"):
            self.local("coherent-set", outside, self.repo.base)

    def test_unknown_or_malformed_sources_are_refused(self) -> None:
        with self.assertRaisesRegex(closure.ClosureRefused, "malformed"):
            self.local("coherent-set", "not-a-sha", self.repo.base)
        with self.assertRaisesRegex(closure.ClosureRefused, "Git read failed"):
            self.local("coherent-set", "f" * 40, self.repo.base)
        with self.assertRaisesRegex(closure.ClosureRefused, "malformed"):
            self.local("coherent-set", self.repo.base, "HEAD")

    def test_unrecognized_kit_row_fails_closed(self) -> None:
        self.repo.write("registry/repos.yml", FIXTURE["registry/repos.yml"].replace(
            "other: {}", "  - source: .claude/skills/hidden\nother: {}"))
        source = self.repo.commit("ambiguous kit row")
        with self.assertRaisesRegex(closure.ClosureRefused, "unrecognized kit row"):
            self.local("coherent-set", source, self.repo.change("docs/notes.md"))

    def test_host_route_has_its_own_narrower_closure(self) -> None:
        kit_only = self.repo.change(".claude/skills/pnext-item/SKILL.md", "kit only\n")
        self.assertEqual(self.local("telemetry-host", self.repo.base, kit_only)["closureChanged"], [])
        self.repo.git("reset", "-q", "--hard", self.repo.base)
        linked = self.repo.change("src/FS.GG.Coord.Cli/Program.fs", "linked into Host\n")
        with self.assertRaisesRegex(closure.ClosureRefused, "Program.fs"):
            self.local("telemetry-host", self.repo.base, linked)

    def test_publisher_route_adds_its_controller_workflow(self) -> None:
        live = self.repo.change(".github/workflows/release-successor-publish.yml", "controller fix\n")
        self.assertEqual(self.local("coherent-set", self.repo.base, live)["closureChanged"], [])
        with self.assertRaisesRegex(closure.ClosureRefused, "release-successor-publish.yml"):
            self.local("coherent-set-publisher", self.repo.base, live)


class CommandLineTests(FixtureTest):
    def run_cli(self, *argv: str) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        with mock.patch.object(closure, "ROOT", self.repo.path), \
             contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = closure.main(list(argv))
        return code, out.getvalue(), err.getvalue()

    def test_workflow_command_accepts_outside_and_refuses_inside_and_non_ancestor(self) -> None:
        outside = self.repo.change("docs/notes.md", "outside\n")
        code, out, _ = self.run_cli("check", "--route", "coherent-set", "--source", self.repo.base, "--live", outside)
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(out)["liveMain"], outside)
        inside = self.repo.change("src/FS.GG.Coord.Cli/Program.fs", "inside\n")
        code, _, err = self.run_cli("check", "--route", "coherent-set", "--source", self.repo.base, "--live", inside)
        self.assertEqual(code, 1)
        self.assertIn("release closure changed", err)
        code, _, err = self.run_cli("check", "--route", "coherent-set", "--source", inside, "--live", self.repo.base)
        self.assertEqual(code, 1)
        self.assertIn("not an ancestor", err)

    def test_default_check_fetches_live_main_from_origin(self) -> None:
        with tempfile.TemporaryDirectory(prefix="release-closure-clone-") as clone_root:
            clone = pathlib.Path(clone_root) / "clone"
            subprocess.run(["git", "clone", "-q", str(self.repo.path), str(clone)], check=True, timeout=60)
            # Upstream main moves after the clone (the queued-candidate case).
            outside = self.repo.change("docs/notes.md", "merged while queued\n")
            out = io.StringIO()
            with mock.patch.object(closure, "ROOT", clone), contextlib.redirect_stdout(out):
                self.assertEqual(closure.main(["check", "--route", "coherent-set", "--source", self.repo.base]), 0)
            self.assertEqual(json.loads(out.getvalue())["liveMain"], outside)
            self.repo.change("src/FS.GG.Coord.Cli/Program.fs", "merged inside while queued\n")
            with mock.patch.object(closure, "ROOT", clone), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(closure.main(["check", "--route", "coherent-set", "--source", self.repo.base]), 1)


class CompareAPI:
    """Native compare responses derived from the fixture's real history."""

    def __init__(self, repo: Repo, *, status=None, files=None, base=None):
        self.repo, self.status, self.files, self.base = repo, status, files, base
        self.paths: list[str] = []

    def get(self, path: str) -> dict:
        self.paths.append(path)
        base, head = path.rsplit("/", 1)[1].split("...")
        ancestor = subprocess.run(["git", "-C", str(self.repo.path), "merge-base", "--is-ancestor", base, head]).returncode == 0
        rows = [line.split("\t") for line in self.repo.git("diff", "--name-status", "--no-renames", base, head).splitlines()]
        status = {"A": "added", "M": "modified", "D": "removed"}
        return {"status": self.status or ("ahead" if ancestor else "diverged"),
                "merge_base_commit": {"sha": self.base or (base if ancestor else "0" * 40)},
                "files": self.files if self.files is not None else [{"filename": name, "status": status[code]} for code, name in rows]}


class CompareTests(FixtureTest):
    def api(self, route, live, **kwargs):
        return closure.verify_api(CompareAPI(self.repo, **kwargs), "FS-GG/.github", route, self.repo.base, live,
                                  root=self.repo.path)

    def test_compare_outside_accepted_inside_and_non_ancestor_refused(self) -> None:
        outside = self.repo.change("docs/notes.md", "outside\n")
        self.assertEqual(self.api("coherent-set", outside)["closureChanged"], [])
        inside = self.repo.change(".claude/skills/pnext-item/SKILL.md", "inside\n")
        with self.assertRaisesRegex(closure.ClosureRefused, "release closure changed"):
            self.api("coherent-set", inside)
        for status in ("behind", "diverged", "identical"):
            with self.subTest(status=status), self.assertRaisesRegex(closure.ClosureRefused, "not an ancestor"):
                self.api("coherent-set", outside, status=status)
        with self.assertRaisesRegex(closure.ClosureRefused, "not an ancestor"):
            self.api("coherent-set", outside, base="1" * 40)

    def test_compare_added_files_shadowing_the_closure_are_refused(self) -> None:
        live = self.repo.change("docs/notes.md", "outside\n")
        for added in ("NuGet.config", "Directory.Build.rsp", "src/NuGet.config", ".fsgg/new.json"):
            with self.subTest(added=added), self.assertRaisesRegex(closure.ClosureRefused, added):
                self.api("coherent-set", live, files=[{"filename": added, "status": "added"}])
        # The same paths merely modified (they are not closure files) and a new workflow pass.
        self.api("coherent-set", live, files=[{"filename": ".fsgg/unrelated.json", "status": "modified"},
                                             {"filename": ".github/workflows/new.yml", "status": "added"}])
        with self.assertRaisesRegex(closure.ClosureRefused, "row is malformed"):
            self.api("coherent-set", live, files=[{"filename": "docs/notes.md"}])

    def test_rename_out_of_the_closure_and_truncated_lists_are_refused(self) -> None:
        live = self.repo.change("docs/notes.md", "outside\n")
        rename = [{"filename": "docs/Linked.fs", "previous_filename": "src/Shared/Linked.fs", "status": "renamed"}]
        with self.assertRaisesRegex(closure.ClosureRefused, "src/Shared/Linked.fs"):
            self.api("coherent-set", live, files=rename)
        truncated = [{"filename": f"docs/{index}.md", "status": "added"} for index in range(closure.COMPARE_FILE_LIMIT)]
        with self.assertRaisesRegex(closure.ClosureRefused, "truncated"):
            self.api("coherent-set", live, files=truncated)
        with self.assertRaisesRegex(closure.ClosureRefused, "absent or may be truncated"):
            self.api("coherent-set", live, files="not-a-list")


class PublisherAdmissionTests(FixtureTest):
    """The actual coherent and Host admission predicates over a moving fixture main."""

    def native(self, workflow: str, publisher: str, live: str):
        repo = self.repo

        class API(CompareAPI):
            def get(inner, path):
                if path == "repos/FS-GG/.github":
                    return {"id": 1269292704, "full_name": "FS-GG/.github"}
                if path.endswith("/actions/runs/7"):
                    return {"repository": {"id": 1269292704}, "path": workflow, "event": "workflow_dispatch",
                            "head_branch": "main", "head_sha": publisher, "run_attempt": 1,
                            "actor": {"login": "EHotwagner"}, "status": "in_progress"}
                if path.endswith("/git/ref/heads/main"):
                    return {"object": {"type": "commit", "sha": inner.live}}
                return CompareAPI.get(inner, path)
        api = API(repo)
        api.live = live
        return api

    def test_coherent_publisher_context_survives_unrelated_merge_only(self) -> None:
        authorize = release_successor_admission.publisher_context_authorized
        workflow = release_successor_admission.WORKFLOW
        api = self.native(workflow, self.repo.base, self.repo.base)
        with mock.patch.object(closure, "ROOT", self.repo.path):
            self.assertTrue(authorize(api, self.repo.base, 7))
            api.live = self.repo.change("docs/notes.md", "unrelated merge\n")
            self.assertTrue(authorize(api, self.repo.base, 7))
            # The hosted installer keeps the exact-head rule.
            self.assertFalse(authorize(api, self.repo.base, 7, hosted_install=True))
            outside = api.live
            api.live = self.repo.change("scripts/release_publisher_helper.py", "controller change\n")
            self.assertFalse(authorize(api, self.repo.base, 7))
            self.repo.git("checkout", "-q", "-b", "side", self.repo.base)
            side = self.repo.change("docs/side.md", "side\n")
            diverged = self.native(workflow, side, outside)
            self.assertFalse(authorize(diverged, side, 7))

    def test_host_admission_survives_unrelated_merge_only(self) -> None:
        manifest_path = ROOT / "tests/telemetry-host-successor/run.py"
        self.assertTrue(manifest_path.exists())
        spec = telemetry_host_successor_admission
        manifest = _host_manifest()
        api = self.native(spec.WORKFLOW, self.repo.base, self.repo.base)
        admission = spec.HostAdmission(api, manifest, self.repo.base, 7, "EHotwagner", "refs/heads/main")
        effect = next(iter(admission.requests.items()))
        with mock.patch.object(closure, "ROOT", self.repo.path):
            self.assertTrue(admission.authorize(admission.content_id, effect[0], "intent", effect[1]))
            api.live = self.repo.change(".claude/skills/pnext-item/SKILL.md", "kit-only merge\n")
            self.assertTrue(admission.authorize(admission.content_id, effect[0], "dispatch", effect[1]))
            api.live = self.repo.change("src/FS.GG.Coord.Cli/Program.fs", "linked Host source\n")
            self.assertFalse(admission.authorize(admission.content_id, effect[0], "settle", effect[1]))

    def test_coherent_fresh_publication_binds_candidate_but_admits_descendant_publisher(self) -> None:
        import importlib.util
        spec = importlib.util.spec_from_file_location("closure_publisher", ROOT / "scripts/release-successor-publish.py")
        publisher = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(publisher)
        with mock.patch.object(closure, "ROOT", self.repo.path), contextlib.redirect_stderr(io.StringIO()):
            later = self.repo.change("docs/notes.md", "unrelated\n")
            self.assertTrue(publisher.candidate_closure_admitted(self.repo.base, later))
            inside = self.repo.change("scripts/release_helper.py", "release helper\n")
            self.assertFalse(publisher.candidate_closure_admitted(self.repo.base, inside))
            self.assertFalse(publisher.candidate_closure_admitted(inside, self.repo.base))


def _host_manifest() -> dict:
    import importlib.util
    spec = importlib.util.spec_from_file_location("host_successor_tests", ROOT / "tests/telemetry-host-successor/run.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.manifest()


class RepositoryClosureCompletenessTests(unittest.TestCase):
    """Derive the real closure at HEAD and join it to the authoritative readers."""

    @classmethod
    def setUpClass(cls) -> None:
        cls.head = subprocess.run(["git", "-C", str(ROOT), "rev-parse", "HEAD"], check=True, capture_output=True,
                                  text=True).stdout.strip()
        tree = closure.GitTree(cls.head)
        if closure.SELF not in tree.files:
            raise unittest.SkipTest("closure module is not committed at HEAD")
        cls.tree = tree
        cls.coherent = closure.derive("coherent-set", tree)
        cls.host = closure.derive("telemetry-host", tree)

    def test_kit_parser_matches_the_registry_reader(self) -> None:
        if shutil.which("yq") is None and subprocess.run([sys.executable, "-c", "import yaml"]).returncode != 0:
            self.skipTest("repos.sh YAML reader unavailable")
        expected = []
        for kind in ("skill", "client", "config"):
            expected += subprocess.run(["bash", "scripts/repos.sh", "kit", "--field", "source", "--kind", kind,
                                        "--registry", "registry/repos.yml"], cwd=ROOT, check=True,
                                       capture_output=True, text=True).stdout.split()
        self.assertEqual(sorted(closure._kit_sources(self.tree)), sorted(expected))
        for source in expected:
            probe = source + "/SKILL.md" if (ROOT / source).is_dir() else source
            self.assertTrue(self.coherent.covers(probe), source)

    def test_project_graph_and_linked_sources_are_covered(self) -> None:
        import re
        for route, result in (("coherent-set", self.coherent), ("telemetry-host", self.host)):
            pending = list(closure.ROUTES[route]["projects"])
            seen = set()
            while pending:
                project = pending.pop()
                if project in seen:
                    continue
                seen.add(project)
                self.assertTrue(result.covers(project), (route, project))
                directory = os.path.dirname(project)
                self.assertTrue(result.covers(directory + "/any-new-file.fs"), (route, directory))
                text = (ROOT / project).read_text()
                for value in re.findall(r"(?:Include|Project)=\"([^\"$*]+)\"", text):
                    target = os.path.normpath(os.path.join(directory, value))
                    if target.startswith(directory + "/"):
                        continue
                    self.assertTrue(result.covers(target), (route, project, target))
                    if target.endswith("proj"):
                        pending.append(target)

    def test_workflow_invoked_scripts_and_packaging_inputs_are_covered(self) -> None:
        import re
        for route, result in (("coherent-set", self.coherent), ("telemetry-host", self.host)):
            for workflow in closure.ROUTES[route]["workflows"]:
                self.assertTrue(result.covers(workflow))
                for script in re.findall(r"python3 (scripts/[A-Za-z0-9_.-]+\.py)", (ROOT / workflow).read_text()):
                    self.assertTrue(result.covers(script), (route, script))
            self.assertTrue(result.covers(closure.SELF))
            for name in ("global.json", "Directory.Build.props", "Directory.Packages.props"):
                self.assertTrue(result.covers(name), (route, name))
        for path in ("registry/repos.yml", "registry/driver-skill-manifest.json", "scripts/repos.sh",
                     "src/FS.GG.Kit/stage-kit.sh", "src/FS.GG.Drivers/stage-drivers.py",
                     "src/FS.GG.Drivers/workspace-inventory.json", "src/FS.GG.Telemetry.Dashboard/Assets/x"):
            self.assertTrue(self.coherent.covers(path), path)
        for row in json.loads((ROOT / "registry/driver-skill-manifest.json").read_text())["skills"]:
            if row["scope"] == "driver":
                self.assertTrue(self.coherent.covers(row["supplied-by"] + "/SKILL.md"), row["id"])

    def test_every_route_workflow_and_action_reference_is_covered(self) -> None:
        """Any repository path a route workflow or the composite actions it uses names (./ form,
        $GITHUB_ACTION_PATH-relative or bare) is inside, not just `python3 scripts/*.py`."""
        import re
        token = re.compile(r"(?:\./)?((?:\.github|scripts|tools|src|tests|registry|dist|contracts)/[A-Za-z0-9._/-]*[A-Za-z0-9_-])")
        for route in closure.ROUTES:
            result = closure.derive(route, self.tree)
            texts = []
            for workflow in closure.ROUTES[route]["workflows"]:
                texts.append(("", (ROOT / workflow).read_text()))
            for _, text in list(texts):
                for action in re.findall(r"uses:\s*\./(\.github/actions/[A-Za-z0-9._-]+)", text):
                    self.assertTrue(result.covers(action + "/action.yml"), (route, action))
                    texts.append((action, (ROOT / action / "action.yml").read_text()))
            for action_dir, text in texts:
                for path in token.findall(text):
                    if (ROOT / path).exists():
                        self.assertTrue(result.covers(path if (ROOT / path).is_file() else path + "/x"), (route, path))
                for relative in re.findall(r"\$\{?GITHUB_ACTION_PATH\}?/([A-Za-z0-9._/-]+)", text):
                    path = os.path.normpath(os.path.join(action_dir, relative))
                    self.assertTrue(result.covers(path), (route, action_dir, path))

    def test_reviewer_probes_for_path_loaded_controllers_and_actions(self) -> None:
        host = closure.derive("telemetry-host-publisher", self.tree)
        coherent = closure.derive("coherent-set-publisher", self.tree)
        # telemetry-host-release.py loads release-saga.py by path for producerPayloadSha256.
        self.assertIn("release-saga.py", (ROOT / "scripts/telemetry-host-release.py").read_text())
        for result in (self.host, host):
            self.assertTrue(result.covers("scripts/release-saga.py"))
        for path in ("tools/v2-ci-ordinary-app-readback.py", ".github/actions/setup-policy-python/action.yml",
                     ".github/actions/publication-writer-lock/action.yml", "scripts/publication_writer_lock.py",
                     "scripts/a-new-helper.py", "tools/a-new-tool.py", ".github/actions/a-new-action/action.yml"):
            self.assertTrue(coherent.covers(path), path)
            self.assertTrue(host.covers(path), path)
        for result in (self.coherent, self.host, coherent, host):
            for added in ("NuGet.config", "Directory.Build.rsp", ".gitattributes", "src/NuGet.config"):
                self.assertTrue(result.covers(added) or result.shadows(added), added)
        self.assertEqual(closure.EXCLUDED, {})

    def test_ordinary_traffic_stays_outside(self) -> None:
        for path in ("docs/reports/progress.md", "README.md", ".agents/skills/work-board/SKILL.md",
                     ".github/workflows/routine-eligibility-selftest.yml", "registry/projects.yml"):
            self.assertFalse(self.coherent.covers(path), path)
            self.assertFalse(self.host.covers(path), path)
        self.assertFalse(self.host.covers(".claude/skills/pnext-item/SKILL.md"))


if __name__ == "__main__":
    unittest.main()
