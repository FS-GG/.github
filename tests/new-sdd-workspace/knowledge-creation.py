#!/usr/bin/env python3
"""Actual Wizard + capable producer + immutable templates, with explicit source-package transport overrides."""
import argparse
from functools import partial
import hashlib
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import threading
import zipfile


def call(argv, *, env=None, cwd=None, succeeds=True):
    result = subprocess.run([str(x) for x in argv], env=env, cwd=cwd, capture_output=True, text=True, timeout=180)
    if succeeds and result.returncode:
        raise AssertionError(f"Command refused ({result.returncode}): {argv}\n{result.stdout[-10000:]}\n{result.stderr[-10000:]}")
    return result


def git(root, *args):
    return call(["git", "-C", root, *args]).stdout.strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--wizard", type=Path, required=True)
    parser.add_argument("--sdd-cli", type=Path, required=True)
    parser.add_argument("--insufficient-cli", type=Path, required=True)
    parser.add_argument("--providers", type=Path, required=True)
    parser.add_argument("--provider-revision", help="Explicit immutable provider source revision for source-candidate qualification")
    parser.add_argument("--workspace-package", type=Path, required=True)
    parser.add_argument("--rendering-package", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    version = call([args.sdd_cli, "--version"]).stdout.strip()
    assert version == "2.1.0", f"This source window requires a genuine 2.1.0 candidate; got {version!r}"
    assert call([args.insufficient_cli, "--version"]).stdout.strip() == "2.0.3"
    dotnet = shutil.which("dotnet")
    assert dotnet
    wizard_command = [dotnet, args.wizard] if args.wizard.suffix == ".dll" else [args.wizard]
    assert all(path.is_file() for path in (args.wizard, args.sdd_cli, args.insufficient_cli, args.workspace_package, args.rendering_package))
    with zipfile.ZipFile(args.workspace_package) as package:
        candidates = [name for name in package.namelist() if "/fs-gg-project-knowledge/" in name and name.endswith("/scripts/check-project-knowledge.py")]
        assert len(candidates) == 1, candidates
        checker_bytes = package.read(candidates[0])
        workflow_bytes = package.read(candidates[0].replace("scripts/check-project-knowledge.py", ".github/workflows/project-knowledge.yml"))
    report = {"qualification": "source-candidate-only", "sddVersion": version,
              "sddClosureSha256": {name: hashlib.sha256((args.sdd_cli.parent / name).read_bytes()).hexdigest()
                                    for name in (args.sdd_cli.name, "FS.GG.SDD.Cli.dll", "FS.GG.SDD.Commands.dll", "FS.GG.SDD.Knowledge.dll")},
              "workspaceArchiveSha256": hashlib.sha256(args.workspace_package.read_bytes()).hexdigest(),
              "renderingArchiveSha256": hashlib.sha256(args.rendering_package.read_bytes()).hexdigest(),
              "providerSourceRevision": args.provider_revision or "working-source-candidate", "routes": []}
    with tempfile.TemporaryDirectory(prefix="wizard-knowledge-creation-") as temporary:
        base = Path(temporary)
        server_root = base / "http" / "fixture" / "providers"
        server_root.mkdir(parents=True)
        for provider in args.providers.glob("*.providers.yml"):
            content = (call(["git", "show", f"{args.provider_revision}:providers/{provider.name}"], cwd=args.providers.parent).stdout
                       if args.provider_revision else provider.read_text())
            if provider.name == "rendering.providers.yml":
                assert content.count("source: FS.GG.UI.Template::0.31.0") == 1
                content = content.replace("source: FS.GG.UI.Template::0.31.0", "source: FS.GG.UI.Template::0.32.0")
                report["renderingDescriptorTransportOverride"] = {"protectedPin": "0.31.0", "candidatePin": "0.32.0", "onlySourceLineChanged": True}
            (server_root / provider.name).write_text(content)
        server = ThreadingHTTPServer(("127.0.0.1", 0), partial(SimpleHTTPRequestHandler, directory=str(base / "http")))
        threading.Thread(target=server.serve_forever, daemon=True).start()
        env = os.environ.copy()
        # Avoid copying credentials into source fixtures, logs or generated products.
        for name in ("GH_TOKEN", "GITHUB_TOKEN", "FSGG_PACKAGES_TOKEN"):
            env.pop(name, None)
        env.update(DOTNET_CLI_HOME=str(base / "dotnet-home"), NUGET_PACKAGES=str(base / "packages"),
                   NUGET_HTTP_CACHE_PATH=str(base / "http-cache"),
                   FSGG_TEMPLATES_RAW_BASE=f"http://127.0.0.1:{server.server_port}")
        shim = base / "bin"
        shim.mkdir()
        sdd_path = shim / "fsgg-sdd"
        def select_cli(executable):
            sdd_path.write_text("#!/usr/bin/env python3\nimport os,sys\nos.execv(" + repr(str(executable.resolve())) + ", [" + repr(str(executable.resolve())) + "] + sys.argv[1:])\n")
            sdd_path.chmod(0o755)
        replacements = {"FS.GG.Workspace.Template::0.18.0": str(args.workspace_package.resolve()),
                        "FS.GG.UI.Template::0.32.0": str(args.rendering_package.resolve())}
        dotnet_shim = shim / "dotnet"
        dotnet_shim.write_text("#!/usr/bin/env python3\nimport os,sys\nargs=sys.argv[1:]\nreplacements=" + repr(replacements) + "\nif args[:2]==['new','install'] and len(args)>2 and args[2] in replacements: args[2]=replacements[args[2]]\nimport subprocess\nresult=subprocess.run([" + repr(dotnet) + "]+args,capture_output=True,text=True)\nif result.returncode: open(" + repr(str(args.report.with_suffix(".dotnet-failure.log"))) + ",'w').write(repr(args)+'\\n'+result.stdout+result.stderr)\nsys.stdout.write(result.stdout);sys.stderr.write(result.stderr);sys.exit(result.returncode)\n")
        dotnet_shim.chmod(0o755)
        env["PATH"] = str(shim) + os.pathsep + env["PATH"]
        common = ["--pinned", "--ref", "fixture", "--no-governance", "--no-coordination"]
        select_cli(args.insufficient_cli)
        refused_root = base / "refused"
        refused = call([*wizard_command, refused_root, "Refused", "--lifecycle", "typed-sdd", *common], env=env, succeeds=False)
        assert refused.returncode == 1 and "2.1.0" in refused.stdout, refused.stdout + refused.stderr
        assert not (refused_root / ".fsgg/knowledge").exists() and not (refused_root / ".git").exists()
        select_cli(args.sdd_cli)
        try:
            for profile in ("app", "game"):
                root = base / ("typed-" + profile)
                call([*wizard_command, root, "Knowledge" + profile.capitalize(), "--lifecycle", "typed-sdd", "--profile", profile, *common], env=env)
                assert (root / "scripts/check-project-knowledge.py").read_bytes() == checker_bytes
                assert (root / ".github/workflows/project-knowledge.yml").read_bytes() == workflow_bytes
                manifest = json.loads((root / ".config/dotnet-tools.json").read_text())
                assert manifest["isRoot"] and manifest["tools"]["fs.gg.sdd.cli"]["version"] == "2.1.0"
                assert not git(root, "rev-list", "--all"), "Wizard introduced a background initial commit"
                canonical = [path.relative_to(root).as_posix() for path in (root / ".fsgg/knowledge").rglob("*") if path.is_file()]
                assert canonical and any("/records/" in path for path in canonical)
                cache = root / ".fsgg/cache/fixture.json"
                cache.parent.mkdir(parents=True, exist_ok=True)
                cache.write_text("{}")
                git(root, "add", ".")
                git(root, "-c", "user.name=Knowledge fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "Initial workspace")
                tracked = set(git(root, "ls-files").splitlines())
                assert set(canonical + [".fsgg/knowledge-guide.md", ".config/dotnet-tools.json", ".github/workflows/project-knowledge.yml", "scripts/check-project-knowledge.py"]) <= tracked
                assert ".fsgg/cache/fixture.json" not in tracked
                checked = call([args.sdd_cli, "knowledge", "check", "--root", root]).stdout
                assert json.loads(checked)["Limit"] == 10485760
                check_command = ["python3", root / "scripts/check-project-knowledge.py", "--root", root,
                                 "--command-json", json.dumps([str(args.sdd_cli.resolve())])]
                assert json.loads(call(check_command).stdout)["limit"] == 10485760
                record = next((root / ".fsgg/knowledge/records").glob("*.json"))
                original_record = record.read_bytes()
                try:
                    # Valid JSON trailing whitespace bypasses the write API and exceeds
                    # the full canonical population cap; invoke the owner's actual CI entry.
                    record.write_bytes(original_record + b" " * 10485761)
                    refused_budget = call(check_command, succeeds=False)
                    assert refused_budget.returncode != 0 and "Producer knowledge check refused" in refused_budget.stderr
                finally:
                    record.write_bytes(original_record)
                clone = base / ("clone-" + profile)
                call(["git", "clone", "-q", root, clone])
                assert not (clone / ".fsgg/cache").exists()
                assert json.loads(call([args.sdd_cli, "knowledge", "check", "--root", clone]).stdout)["Limit"] == 10485760
                report["routes"].append({"profile": profile, "initialCommit": git(root, "rev-parse", "HEAD"), "canonicalFiles": len(canonical), "normalForegroundCallerCommit": True})
            cache_metadata = base / "dotnet-home/.templateengine/packages.json"
            registrations = json.loads(cache_metadata.read_text(encoding="utf-8-sig"))["Packages"]
            overlays = [item for item in registrations if item.get("Details", {}).get("PackageId") == "FS.GG.Workspace.Template"]
            assert len(overlays) == 1, "Repeated creation duplicated package registration"
            cached_archive = Path(overlays[0]["MountPointUri"])
            original_archive = cached_archive.read_bytes()
            with zipfile.ZipFile(cached_archive) as archive:
                entries = [(item, archive.read(item)) for item in archive.infolist()]
            try:
                with zipfile.ZipFile(cached_archive, "w") as archive:
                    for item, data in entries:
                        if item.filename.endswith("/fs-gg-project-knowledge/scripts/check-project-knowledge.py"):
                            data += b"\n# same version, unqualified owner bytes\n"
                        archive.writestr(item, data)
                wrong_root = base / "wrong-bytes"
                wrong = call([*wizard_command, wrong_root, "WrongBytes", "--lifecycle", "typed-sdd", "--profile", "app", *common], env=env, succeeds=False)
                assert wrong.returncode == 1
                assert not (wrong_root / ".github/workflows/project-knowledge.yml").exists()
                assert not (wrong_root / "scripts/check-project-knowledge.py").exists()
                report["sameVersionWrongOverlayBytesRefused"] = True
            finally:
                cached_archive.write_bytes(original_archive)
            report["repeatedCacheSingleQualifiedRegistration"] = True
            standard = base / "standard"
            call([*wizard_command, standard, "Standard", "--lifecycle", "sdd", "--profile", "app", *common], env=env)
            assert not (standard / ".fsgg/knowledge").exists()
            assert not (standard / ".github/workflows/project-knowledge.yml").exists()
            report["standardSddUnchanged"] = True
            report["insufficientProducerRefusedBeforeScaffold"] = True
        finally:
            server.shutdown()
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
