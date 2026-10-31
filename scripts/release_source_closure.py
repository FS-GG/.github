#!/usr/bin/env python3
"""Admit a release source that `main` has moved past, but only outside its closure.

A release candidate or publisher pins the exact dispatch-time source SHA. Instead of
requiring live `main` to still equal that SHA (so every unrelated merge cancelled a
queued release, #5107), the source is admitted against a later `main` only when:

1. the pinned source is an ancestor of that `main`; and
2. no path in the route's release closure changed between the two commits.

Every other identity check stays with the caller. This module grants no effect and
never changes which source a release binds: a journal intent still names the exact
pinned candidate source.

The closure is FAIL-CLOSED first and derived second:

* every path under scripts/, tools/ and .github/actions/ (any controller, helper or
  composite action a release could load by path), the root build configuration and this
  module are always inside, unless explicitly listed in EXCLUDED (currently empty);
* an ADDED file at the repository root, or in any directory that already holds a closure
  file, is a closure change (a shadowing module, NuGet.config, *.rsp, .gitattributes);
* the rest is derived from the pinned source tree, never restated as a file list: the
  route's workflows and every repository path they reference; the packed projects plus
  their transitive ProjectReference, linked item and Import paths and every ancestor
  Directory.Build.*/Directory.Packages.props; the repository data paths the reachable
  release scripts name literally or build with pathlib; and, for the coherent set, the
  FS.GG.Kit and FS.GG.Drivers staged member sources read from the same registry rows
  their stagers read.

Every input of that derivation is itself inside the closure, so an unchanged closure
derives the same closure at both commits. Refusal is the safe failure: an unreadable
tree, malformed registry row, an MSBuild property path not covered elsewhere, a
truncated comparison or a non-ancestor refuses.
"""

from __future__ import annotations

import argparse
import ast
import json
import pathlib
import posixpath
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SELF = "scripts/release_source_closure.py"
SHA = re.compile(r"[0-9a-f]{40}")
# GitHub's compare API lists at most 300 changed files; a full list cannot be proved beyond that.
COMPARE_FILE_LIMIT = 300

_COHERENT_STAGER_INPUTS = (
    "registry/repos.yml", "registry/repos.lock", "scripts/repos.sh", "scripts/sync-build-config.sh",
    "registry/driver-skill-manifest.json", "registry/coordination-kit-skill-manifest.json", "dist/dotnet/",
)
_BUILD_ROOT = ("global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
               "Directory.Build.rsp", "Directory.Solution.props", "Directory.Solution.targets",
               "NuGet.config", "nuget.config", "NuGet.Config", ".gitattributes", ".gitmodules", ".editorconfig",
               ".config/", "dist/dotnet/")
# Fail-closed roots: anything a release can load by path (#5111 review). Narrowing this needs
# an explicit EXCLUDED entry with its reason, never a cleverer reference parser.
FAIL_CLOSED = ("scripts/", "tools/", ".github/actions/")
# Paths inside FAIL_CLOSED proven unable to reach a release, each with its reason. Empty.
EXCLUDED: dict[str, str] = {}
# Added files here cannot shadow anything: Actions loads a workflow only by its own path,
# and every workflow a route references is already inside by its exact path.
_ADDITION_EXEMPT_DIRECTORIES = frozenset({".github/workflows"})
# MSBuild property paths whose sources are derived elsewhere (the staged Kit/Drivers members).
_COVERED_PROPERTY_ITEMS = frozenset({
    ("src/FS.GG.Kit/FS.GG.Kit.csproj", "$(KitStagingDir)/**/*"),
    ("src/FS.GG.Drivers/FS.GG.Drivers.csproj", "$(DriversStagingDir)/**/*"),
    ("src/FS.GG.Drivers/FS.GG.Drivers.csproj", "$(DriversStagingDir)/workspace/**/*"),
})

ROUTES = {
    "coherent-set": {
        "workflows": (".github/workflows/release-successor-candidate.yml",),
        "projects": ("src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj", "src/FS.GG.Kit/FS.GG.Kit.csproj",
                     "src/FS.GG.Drivers/FS.GG.Drivers.csproj"),
        "scripts": ("scripts/release-saga.py", "scripts/check-release-candidate-uniqueness.py"),
        "static": FAIL_CLOSED + _BUILD_ROOT + _COHERENT_STAGER_INPUTS,
        "staged": True,
    },
    "telemetry-host": {
        "workflows": (".github/workflows/release-telemetry-host-successor-candidate.yml",),
        "projects": ("src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj",),
        "scripts": ("scripts/telemetry-host-release.py", "scripts/telemetry-host-candidate-read-gate.py"),
        "static": FAIL_CLOSED + _BUILD_ROOT,
        "staged": False,
    },
}
# A running publisher is also bound to its own reviewed controller source.
ROUTES["coherent-set-publisher"] = {
    **ROUTES["coherent-set"],
    "workflows": ROUTES["coherent-set"]["workflows"] + (".github/workflows/release-successor-publish.yml",),
    "scripts": ROUTES["coherent-set"]["scripts"] + ("scripts/release-successor-publish.py",),
}
ROUTES["telemetry-host-publisher"] = {
    **ROUTES["telemetry-host"],
    "workflows": ROUTES["telemetry-host"]["workflows"] + (".github/workflows/release-telemetry-host-successor-publish.yml",),
    "scripts": ROUTES["telemetry-host"]["scripts"] + ("scripts/telemetry-host-successor-publish.py",),
}

_REPO_ROOTS = ("scripts", "tests", "src", "registry", "dist", "tools", "contracts", "deployment", ".github", "policy",
               "docs", "evidence", ".fsgg", ".agents", ".claude")
_PATH_TOKEN = re.compile(r"(?<![\w$./-])((?:%s)/[A-Za-z0-9._/@+-]*[A-Za-z0-9_/-])" % "|".join(re.escape(r) for r in _REPO_ROOTS))
_DOT_SLASH = re.compile(r"(?<![\w.])\./")
_IMPORT = re.compile(r"^\s*(?:from\s+([A-Za-z_][\w]*)[\w.]*\s+import|import\s+([A-Za-z_][\w]*(?:\s*,\s*[A-Za-z_][\w]*)*))", re.M)
_WITH_NAME = re.compile(r"with_name\(\s*[\"']([A-Za-z0-9._-]+)[\"']\s*\)")
# Every path-bearing attribute on any element; item types whose Include names a package,
# namespace or assembly identity rather than a file are the only ones skipped.
_PROJECT_ATTR = re.compile(r"<([A-Za-z_][\w.]*)\b[^>]*?\b(?:Include|Project|Exclude|Update|Remove)=\"([^\"]+)\"")
_NON_PATH_ITEMS = frozenset({"PackageReference", "PackageVersion", "GlobalPackageReference", "PackageDownload",
                             "FrameworkReference", "Using", "InternalsVisibleTo", "AssemblyAttribute"})
_EXISTS = re.compile(r"Exists\(\s*'([^']+)'\s*\)")
_KIT_ROW = re.compile(r"^  - \{\s*id:\s*[A-Za-z0-9._-]+,\s*kind:\s*(skill|client|config),\s*source:\s*([A-Za-z0-9._/-]+)\s*(?:,\s*dest:\s*[A-Za-z0-9._/-]+\s*)?\}\s*(?:#.*)?$")
_PROJECT_SUFFIXES = (".fsproj", ".csproj", ".props", ".targets")
# Repository roots whose paths a reachable release script can turn into released or verified bytes.
_RELEASE_DATA_ROOTS = ("src", "registry", "dist", "contracts", "docs", "evidence", ".fsgg", "policy", "deployment",
                       "scripts", "tools", ".github")


class ClosureRefused(RuntimeError):
    pass


def _require(ok: bool, detail: str) -> None:
    if not ok:
        raise ClosureRefused(detail)


class GitTree:
    """Read one exact commit's tracked files from local Git objects (never the worktree)."""

    def __init__(self, revision: str, *, root: pathlib.Path | None = None):
        _require(isinstance(revision, str) and bool(SHA.fullmatch(revision)), "closure source SHA is malformed")
        self.revision = revision
        self.root = root if root is not None else ROOT
        listed = self._git("ls-tree", "-r", "-z", "--name-only", revision)
        self.files = frozenset(name for name in listed.decode("utf-8").split("\0") if name)
        _require(bool(self.files), "closure source tree is empty or unreadable")

    def _git(self, *args: str) -> bytes:
        try:
            return subprocess.run(["git", "-C", str(self.root), *args], check=True,
                                  capture_output=True, timeout=120).stdout
        except (OSError, subprocess.SubprocessError) as error:
            raise ClosureRefused(f"closure Git read failed: {' '.join(args[:2])}") from error

    def read(self, path: str) -> str | None:
        if path not in self.files:
            return None
        return self._git("show", f"{self.revision}:{path}").decode("utf-8", errors="replace")

    def under(self, prefix: str) -> list[str]:
        return sorted(name for name in self.files if name.startswith(prefix))

    def is_dir(self, path: str) -> bool:
        prefix = path.rstrip("/") + "/"
        return any(name.startswith(prefix) for name in self.files)


class Closure:
    """Exact paths plus directory prefixes (ending in '/')."""

    def __init__(self) -> None:
        self.paths: set[str] = set()
        self.prefixes: set[str] = set()

    def add(self, path: str) -> None:
        if path.endswith("/"):
            self.prefixes.add(path)
        else:
            self.paths.add(path)

    def covers(self, path: str) -> bool:
        if any(path == key or (key.endswith("/") and path.startswith(key)) for key in EXCLUDED):
            return False
        return path in self.paths or any(path.startswith(prefix) for prefix in self.prefixes)

    def shadows(self, added: str) -> bool:
        """An added file at the root or beside an exact closure file is a closure change."""
        directory = posixpath.dirname(added)
        if directory in _ADDITION_EXEMPT_DIRECTORIES:
            return False
        return directory == "" or any(posixpath.dirname(path) == directory for path in self.paths)

    def as_json(self) -> dict:
        return {"paths": sorted(self.paths), "prefixes": sorted(self.prefixes)}


def _normalize(path: str) -> str | None:
    value = posixpath.normpath(path.replace("\\", "/"))
    if value in {".", ""} or value.startswith("../") or value == ".." or value.startswith("/"):
        return None
    return value


def _tokens(text: str) -> set[str]:
    found = set()
    for match in _PATH_TOKEN.finditer(_DOT_SLASH.sub(" ", text)):
        value = match.group(1).rstrip("/.")
        if "/" in value:
            found.add(value)
    return found


def _kit_sources(tree: GitTree) -> list[str]:
    """Read every kit row from registry/repos.yml, failing closed on any unrecognized row."""
    raw = tree.read("registry/repos.yml")
    _require(raw is not None, "registry/repos.yml is absent at the closure source")
    lines = raw.splitlines()
    try:
        start = lines.index("kit:")
    except ValueError:
        raise ClosureRefused("registry/repos.yml has no top-level kit block") from None
    sources = []
    for line in lines[start + 1:]:
        if line and not line[0].isspace() and not line.startswith("#"):
            break
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        match = _KIT_ROW.match(line)
        _require(match is not None, f"unrecognized kit row in registry/repos.yml: {stripped[:80]}")
        sources.append(match.group(2))
    _require(bool(sources), "registry/repos.yml declares no kit rows")
    return sources


def _driver_sources(tree: GitTree) -> list[str]:
    raw = tree.read("registry/driver-skill-manifest.json")
    _require(raw is not None, "driver skill manifest is absent at the closure source")
    inventory = tree.read("src/FS.GG.Drivers/workspace-inventory.json")
    _require(inventory is not None, "driver workspace inventory is absent at the closure source")
    try:
        skills = json.loads(raw)["skills"]
        rows = json.loads(inventory)["files"]
        sources = [row["supplied-by"] for row in skills if row.get("scope") == "driver"]
        sources += [row["source"] for row in rows]
    except (ValueError, KeyError, TypeError) as error:
        raise ClosureRefused("driver staging inputs are malformed") from error
    _require(bool(sources) and all(isinstance(source, str) and source for source in sources),
             "driver staging inputs are empty or malformed")
    return sources


def derive(route: str, tree: GitTree) -> Closure:
    """Derive the closure at one exact commit.

    Release bytes recurse: packed projects through their project graph, and repository
    scripts through local imports and the repository paths they name. Qualification the
    route's workflows invoke directly (a test directory, a composite action) is consumed
    as one unit without recursion, as are staged kit/driver member sources (copied bytes).
    """
    _require(route in ROUTES, f"unknown release closure route: {route}")
    spec = ROUTES[route]
    closure = Closure()
    closure.add(SELF)
    for path in spec["static"]:
        closure.add(path)
    pending: list[tuple[str, str]] = [(path, "workflow") for path in spec["workflows"]]
    pending += [(path, "release") for path in (*spec["projects"], *spec["scripts"], *spec["static"])]
    if spec["staged"]:
        for source in _kit_sources(tree) + _driver_sources(tree):
            normalized = _normalize(source)
            _require(normalized is not None, f"staged source escapes the repository: {source}")
            pending.append((normalized, "unit"))
    seen: set[tuple[str, str]] = set()
    while pending:
        item, mode = pending.pop()
        if (item, mode) in seen:
            continue
        seen.add((item, mode))
        if not item.endswith("/") and tree.is_dir(item):
            item += "/"
        if item.endswith("/"):
            _require(item.count("/") >= 2 or item in FAIL_CLOSED + _BUILD_ROOT + _COHERENT_STAGER_INPUTS,
                     f"closure directory is too broad: {item}")
            closure.add(item)
            if mode == "release" and item not in FAIL_CLOSED:
                pending.extend((name, "release") for name in tree.under(item)
                               if name.endswith(_PROJECT_SUFFIXES + (".py",)))
            continue
        closure.add(item)
        if mode == "unit":
            continue
        text = tree.read(item)
        if text is None:
            continue  # An absent exact path stays in the closure: adding it later is a change.
        directory = posixpath.dirname(item)
        if mode == "workflow":
            for token in _tokens(text):
                if token not in tree.files and not tree.is_dir(token):
                    continue
                parts = token.split("/")
                if parts[0] == "tests":
                    pending.append(("/".join(parts[:2]) + "/", "unit"))
                elif parts[:2] == [".github", "actions"] and len(parts) >= 3:
                    pending.append(("/".join(parts[:3]) + "/", "unit"))
                elif parts[0] == ".github":
                    pending.append((token, "unit"))
                else:
                    pending.append((token, "release"))
            continue
        if item.endswith(_PROJECT_SUFFIXES):
            if item.endswith(("proj",)):
                closure.add(directory + "/")
            for ancestor in _ancestors(directory):
                for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
                    pending.append((posixpath.join(ancestor, name) if ancestor else name, "release"))
            values = [value for element, value in _PROJECT_ATTR.findall(text) if element not in _NON_PATH_ITEMS]
            for value in values + _EXISTS.findall(text):
                for raw in value.split(";"):
                    resolved = _project_path(item, directory, raw.strip())
                    if resolved is not None:
                        pending.append((resolved, "release"))
            continue
        if item.endswith(".py") and item != SELF:  # This module's strings are route definitions, not reads.
            for candidate in _python_references(text, directory, tree):
                pending.append((candidate, "release"))
    return closure


def _project_path(project: str, directory: str, value: str) -> str | None:
    """Resolve one MSBuild item/import path; refuse a property path not covered elsewhere."""
    if not value or value.startswith(("@(", "%(")):
        return None  # Item and metadata references name items declared elsewhere in this file.
    original = value
    value = value.replace("$(MSBuildThisFileDirectory)", "").replace("$(MSBuildProjectDirectory)/", "")
    value = value.replace("$(MSBuildProjectDirectory)", ".")
    if "$(" in value:
        _require((project, original) in _COVERED_PROPERTY_ITEMS,
                 f"MSBuild property path is not covered by the release closure: {project}: {original}")
        return None
    if "*" in value or "?" in value:
        static = []
        for segment in value.replace("\\", "/").split("/"):
            if "*" in segment or "?" in segment:
                break
            static.append(segment)
        value = "/".join(static) + "/"
    resolved = _normalize(posixpath.join(directory, value))
    _require(resolved is not None, f"MSBuild path escapes the repository: {project}: {original}")
    if resolved == directory or resolved.startswith(directory + "/"):
        return None  # Inside the project directory, which is already a closure prefix.
    return resolved + "/" if value.endswith("/") else resolved


def _python_references(text: str, directory: str, tree: GitTree) -> set[str]:
    """Local modules plus every repository path a script names literally or joins with pathlib."""
    found: set[str] = set()
    for module in _python_imports(text):
        for candidate in (f"scripts/{module}.py", posixpath.join(directory, f"{module}.py")):
            if candidate in tree.files:
                found.add(candidate)
    strings: list[str] = list(_WITH_NAME.findall(text)) + [token for token in _tokens(text)]
    try:
        module = ast.parse(text)
    except SyntaxError:
        raise ClosureRefused(f"release script is not parseable Python in {directory or '.'}") from None
    for node in ast.walk(module):
        if isinstance(node, ast.Constant) and isinstance(node.value, str) and len(node.value) < 300:
            strings.append(node.value)
        elif isinstance(node, ast.BinOp) and isinstance(node.op, ast.Div):
            parts = []
            for operand in _division_operands(node):
                if isinstance(operand, ast.Constant) and isinstance(operand.value, str):
                    parts.append(operand.value)
                elif parts:
                    parts = []  # A non-literal segment restarts the literal suffix.
            if parts:
                strings.append("/".join(parts))
    for value in strings:
        value = value.strip()
        value = value[2:] if value.startswith("./") else value
        if not value or "\n" in value or " " in value:
            continue
        for base in ("", directory, "scripts"):
            candidate = _normalize(posixpath.join(base, value)) if base else _normalize(value)
            if candidate is None or candidate.split("/")[0] not in _RELEASE_DATA_ROOTS:
                continue
            if candidate in tree.files:
                found.add(candidate)
                break
            if tree.is_dir(candidate) and candidate.count("/") >= 1:
                found.add(candidate + "/")
                break
    return found


def _division_operands(node: ast.AST) -> list[ast.AST]:
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Div):
        return _division_operands(node.left) + _division_operands(node.right)
    return [node]


def _ancestors(directory: str) -> list[str]:
    parts = directory.split("/") if directory else []
    return ["/".join(parts[:index]) for index in range(len(parts), -1, -1)]


def _python_imports(text: str) -> set[str]:
    modules = set()
    for left, right in _IMPORT.findall(text):
        if left:
            modules.add(left)
        if right:
            modules.update(name.strip() for name in right.split(","))
    return modules


def _closure_changes(route: str, tree: GitTree, changed: list[str], added: list[str]) -> list[str]:
    closure = derive(route, tree)
    touched = {path for path in changed if closure.covers(path)}
    touched |= {path for path in added if closure.shadows(path)}
    return sorted(touched)


def verify_local(route: str, source: str, live: str, *, root: pathlib.Path | None = None) -> dict:
    """Prove ancestry and an unchanged closure from local Git objects."""
    _require(isinstance(live, str) and bool(SHA.fullmatch(live)), "live main SHA is malformed")
    root = root if root is not None else ROOT
    tree = GitTree(source, root=root)
    if source == live:
        return {"route": route, "source": source, "liveMain": live, "changed": 0, "closureChanged": []}
    ancestry = subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor", source, live],
                              capture_output=True, timeout=120)
    _require(ancestry.returncode != 1, "pinned source is not an ancestor of live main")
    _require(ancestry.returncode == 0, "pinned source ancestry is unreadable")
    raw = tree._git("diff", "--name-status", "--no-renames", "-z", source, live).decode("utf-8").split("\0")
    rows = list(zip(raw[0::2], raw[1::2]))
    _require(len(raw) % 2 == 1 and raw[-1] == "" and all(status and name for status, name in rows),
             "release closure diff is malformed")
    changed = [name for _, name in rows]
    added = [name for status, name in rows if status == "A"]
    touched = _closure_changes(route, tree, changed, added)
    _require(not touched, "release closure changed after the pinned source: " + ", ".join(touched[:20]))
    return {"route": route, "source": source, "liveMain": live, "changed": len(changed), "closureChanged": []}


def verify_api(api, repository: str, route: str, source: str, live: str, *, root: pathlib.Path | None = None) -> dict:
    """Prove ancestry and an unchanged closure through the native compare API.

    `source` must be available in local Git objects (the caller's checkout); `live` need not be.
    """
    _require(isinstance(live, str) and bool(SHA.fullmatch(live)), "live main SHA is malformed")
    tree = GitTree(source, root=root)
    if source == live:
        return {"route": route, "source": source, "liveMain": live, "changed": 0, "closureChanged": []}
    comparison = api.get(f"repos/{repository}/compare/{source}...{live}")
    _require(isinstance(comparison, dict), "release closure comparison is malformed")
    _require(comparison.get("status") == "ahead"
             and (comparison.get("merge_base_commit") or {}).get("sha") == source,
             "pinned source is not an ancestor of live main")
    files = comparison.get("files")
    _require(isinstance(files, list) and len(files) < COMPARE_FILE_LIMIT,
             "release closure comparison file list is absent or may be truncated")
    changed, added = [], []
    for row in files:
        _require(isinstance(row, dict) and isinstance(row.get("filename"), str)
                 and isinstance(row.get("status"), str), "release closure comparison row is malformed")
        changed.append(row["filename"])
        if row["status"] in {"added", "renamed", "copied"}:
            added.append(row["filename"])  # A rename or copy also creates a new path.
        if isinstance(row.get("previous_filename"), str):
            changed.append(row["previous_filename"])
    touched = _closure_changes(route, tree, changed, added)
    _require(not touched, "release closure changed after the pinned source: " + ", ".join(touched[:20]))
    return {"route": route, "source": source, "liveMain": live, "changed": len(changed), "closureChanged": []}


# Accepted verdicts only. (route, source, live) names immutable commits, so a cached
# acceptance cannot go stale; the caller still rereads the live ref on every authorization.
_ACCEPTED: set[tuple[str, str, str, str, str]] = set()


def admits(api, repository: str, route: str, source: str, live: str, *, root: pathlib.Path | None = None) -> bool:
    """Return False on a closure refusal; read failures propagate to the caller's own retry/deny."""
    key = (str(root if root is not None else ROOT), repository, route, source, live)
    if key in _ACCEPTED:
        return True
    try:
        verify_api(api, repository, route, source, live, root=root)
    except ClosureRefused:
        return False
    _ACCEPTED.add(key)
    return True


def _fetch_live_main(root: pathlib.Path) -> str:
    ref = "refs/fsgg/release-closure/live-main"
    try:
        subprocess.run(["git", "-C", str(root), "fetch", "--no-tags", "--quiet", "origin", f"+refs/heads/main:{ref}"],
                       check=True, capture_output=True, timeout=300)
        live = subprocess.run(["git", "-C", str(root), "rev-parse", "--verify", f"{ref}^{{commit}}"],
                              check=True, capture_output=True, timeout=60).stdout.decode().strip()
    except (OSError, subprocess.SubprocessError) as error:
        raise ClosureRefused("live main could not be fetched") from error
    _require(bool(SHA.fullmatch(live)), "live main SHA is malformed")
    return live


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)
    check = commands.add_parser("check", help="fetch live main and admit the pinned source against it")
    check.add_argument("--route", required=True, choices=sorted(ROUTES))
    check.add_argument("--source", required=True)
    check.add_argument("--live", help="explicit live main SHA (tests); default fetches origin main")
    show = commands.add_parser("closure", help="print the closure derived at one commit")
    show.add_argument("--route", required=True, choices=sorted(ROUTES))
    show.add_argument("--source", required=True)
    args = parser.parse_args(argv)
    try:
        if args.command == "closure":
            print(json.dumps(derive(args.route, GitTree(args.source)).as_json(), indent=1, sort_keys=True))
            return 0
        live = args.live or _fetch_live_main(ROOT)
        result = verify_local(args.route, args.source, live)
    except ClosureRefused as error:
        print(f"release source closure refused: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
