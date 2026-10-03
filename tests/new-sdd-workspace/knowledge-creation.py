#!/usr/bin/env python3
"""Actual Wizard + capable producer + immutable templates, with explicit source-package transport overrides."""
import argparse
from functools import partial
from contextlib import nullcontext
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


COMMAND_JOURNAL = None
CALL_ENV = None


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def payload(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)), 'duplicate-package-entry')
        require(all(not Path(n).is_absolute() and '..' not in Path(n).parts and '\\' not in n for n in names), 'unsafe-package-entry')
        return {n: hashlib.sha256(archive.read(n)).hexdigest() for n in names if n != '.signature.p7s'}


INSUFFICIENT_ARCHIVE_SHA256 = 'b950bf4fc46a09554a51b6b31f920830c8811bb6580b2724c500b8317bcfa9d7'
INSUFFICIENT_SOURCE = '0c26ac591e76d2839177da823b3f6ada5c09a698'


def regular_input(path, inputs):
    path = Path(path)
    require(path.is_absolute() and path == path.resolve() and path.is_file(), 'insufficient-unsafe-input')
    require(str(path) in inputs and sha(path) == inputs[str(path)], 'insufficient-input-pin')
    return path


def validate_insufficient_public(m, expected_archive_sha=INSUFFICIENT_ARCHIVE_SHA256):
    """Literal public archive join; version output cannot authorize this identity."""
    import stat
    import xml.etree.ElementTree as ET
    binding = m.get('insufficientPublic')
    require(isinstance(binding, dict) and set(binding) == {'archive', 'archiveSha256', 'sourceRevision', 'closure', 'apphostTarget', 'literalPayloads'}, 'insufficient-public-binding-required')
    require(binding['sourceRevision'] == INSUFFICIENT_SOURCE and binding['archiveSha256'] == expected_archive_sha, 'insufficient-public-source')
    inputs = m['inputs']
    archive_path = regular_input(binding['archive'], inputs)
    require(archive_path.stat().st_size <= 32 * 1024 * 1024 and sha(archive_path) == expected_archive_sha, 'insufficient-archive-identity')
    closure = Path(binding['closure'])
    require(closure.is_absolute() and closure == closure.resolve() and closure.is_dir(), 'insufficient-unsafe-closure')
    prefix = 'tools/net10.0/any/'
    with zipfile.ZipFile(archive_path) as archive:
        entries = archive.infolist(); names = [entry.filename for entry in entries]
        require(len(entries) <= 512 and len(names) == len(set(names)), 'insufficient-duplicate-members')
        require(sum(entry.file_size for entry in entries) <= 128 * 1024 * 1024, 'insufficient-archive-bound')
        for entry in entries:
            name = entry.filename
            require(name and not name.startswith('/') and '\\' not in name and all(part not in ('', '.', '..') for part in name.rstrip('/').split('/')) and ':' not in name and not stat.S_ISLNK(entry.external_attr >> 16), 'insufficient-unsafe-member')
        nuspecs = [name for name in names if name.endswith('.nuspec')]
        require(len(nuspecs) == 1, 'insufficient-package-metadata')
        metadata = ET.fromstring(archive.read(nuspecs[0]))
        def values(name):
            return [node for node in metadata.iter() if node.tag.split('}')[-1] == name]
        require(len(values('id')) == len(values('version')) == len(values('repository')) == 1 and values('id')[0].text == 'FS.GG.SDD.Cli' and values('version')[0].text == '2.0.3' and values('repository')[0].get('commit') == INSUFFICIENT_SOURCE, 'insufficient-package-source')
        members = {name: hashlib.sha256(archive.read(name)).hexdigest() for name in names if name.startswith(prefix) and not name.endswith('/')}
    require(len(members) == 36 and binding['literalPayloads'] == members, 'insufficient-full-payload')
    required = ['FS.GG.SDD.Cli.dll', 'FS.GG.SDD.Commands.dll', 'FS.GG.SDD.Cli.deps.json', 'FS.GG.SDD.Cli.runtimeconfig.json', 'DotnetToolSettings.xml']
    require(all(prefix + name in members for name in required), 'insufficient-runtime-members')
    expected_paths = {str(closure / name.removeprefix(prefix)) for name in members}
    actual_paths = {str(path) for path in closure.rglob('*') if path.is_file() or path.is_symlink()}
    require(actual_paths == expected_paths and {path for path in inputs if path.startswith(str(closure) + os.sep)} == expected_paths, 'insufficient-exclusive-closure')
    for name, expected in members.items():
        path = regular_input(closure / name.removeprefix(prefix), inputs)
        require(sha(path) == expected, 'insufficient-closure-payload')
    executable = regular_input(m['insufficientExecutable'], inputs)
    require(executable.stat().st_size <= 1024 * 1024, 'insufficient-apphost-bound')
    target = os.path.relpath(closure / 'FS.GG.SDD.Cli.dll', executable.parent)
    raw = executable.read_bytes()
    require(binding['apphostTarget'] == target and raw.startswith(b'\x7fELF') and raw.count(b'FS.GG.SDD.Cli.dll\x00') == 1 and b'\x00' + target.encode() + b'\x00' in raw, 'insufficient-apphost-target')
    return binding


def validate_installed(args, after=False):
    require(args.installed_manifest and args.approved_installed_manifest_sha256, 'installed-root-manifest-required')
    require(sha(args.installed_manifest) == args.approved_installed_manifest_sha256, 'installed-root-manifest-drift')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'duplicate-manifest-key')
            result[key] = value
        return result
    m = json.loads(args.installed_manifest.read_text(), object_pairs_hook=unique)
    require(m['schema'] == 'fsgg.wizard.installed-public-qualification/1', 'installed-manifest-schema')
    validate_insufficient_public(m)
    for path, expected in m['inputs'].items():
        require(Path(path).is_file() and not Path(path).is_symlink() and sha(path) == expected, 'installed-input-drift:' + path)
    for argument, key in [('wizard', 'wizard'), ('sdd_cli', 'sddExecutable'), ('insufficient_cli', 'insufficientExecutable'),
                          ('workspace_package', 'workspaceArchive'), ('rendering_package', 'renderingArchive'), ('providers', 'providers')]:
        require(str(getattr(args, argument).resolve()) == m[key], 'installed-argument:' + argument)
    require(args.provider_revision == m['providerRevision'] == '9161a9d91b3fdd7a80cae7776fdff179e712b32e', 'installed-descriptor-revision')
    require(str(args.report.resolve()) == m['report'] and (after or not args.report.exists()), 'installed-report-scope')
    require(after or not Path(m['workRoot']).exists(), 'installed-work-root-exists')
    require(os.environ.get('HOME') == m['actualHome'], 'installed-home-drift')
    for key in ('workspaceOriginalArchive', 'freshnessReceipt', 'dotnetExecutable', 'insufficientExecutable'):
        require(m[key] in m['inputs'], 'installed-custody-pin:' + key)
    require(Path(m['report']).parent == Path(m['workRoot']), 'installed-output-boundary')
    require(sha(m['workspaceOriginalArchive']) == 'de6a2fdc887d7a595ca6d9d59f465bc762b1a27fdf5518592f304d7c1b3053d1', 'installed-original-templates')
    require(payload(args.workspace_package) == payload(m['workspaceOriginalArchive']), 'installed-dual-feed-payload')
    require(sha(args.rendering_package) == '06ddce126916565595d66896294cc81ba24f59b7e51c5da21acdc58a1dc483ef', 'installed-original-rendering31')
    require(str(args.wizard) in m['inputs'] and str(args.sdd_cli) in m['inputs'], 'installed-executable-pins')
    fresh = json.loads(Path(m['freshnessReceipt']).read_text())
    producer = next(row for row in fresh['packages'] if row['package'] == 'fs.gg.sdd.cli')
    require(producer['sourceRevision'] == '518517f6b90330a6e99f90bbce68faa0a891287f', 'installed-public-producer')
    closure = Path(m['sddClosure'])
    for name, expected in producer['literalPayloads'].items():
        if name.startswith('tools/net10.0/any/') and name.endswith('.dll'):
            path = closure / name.removeprefix('tools/net10.0/any/')
            require(sha(path) == expected and str(path) in m['inputs'], 'installed-closure-payload:' + name)
    require(sha(closure / 'FSharp.Core.dll') == fresh['core']['assets']['lib/netstandard2.1/FSharp.Core.dll'], 'installed-runtime-core')
    return m


def descriptor_content(content, name, installed):
    if name != 'rendering.providers.yml':
        return content
    require(content.count('source: FS.GG.UI.Template::0.31.0') == 1, 'rendering-protected-pin')
    return content if installed else content.replace('source: FS.GG.UI.Template::0.31.0', 'source: FS.GG.UI.Template::0.32.0')


def checker_command(root, executable, installed):
    argv = ['python3', root / 'scripts/check-project-knowledge.py', '--root', root]
    return argv if installed else argv + ['--command-json', json.dumps([str(executable.resolve())])]


def installed_environment(m, base, port):
    env = {key: os.environ[key] for key in ('HOME', 'LANG', 'LC_ALL') if key in os.environ}
    env.update(m['childEnvironment'])
    require(env['HOME'] == m['actualHome'], 'installed-home-override')
    require(not any('TOKEN' in key or 'PASSWORD' in key or 'SECRET' in key for key in env), 'installed-credential-environment')
    env.update(DOTNET_CLI_HOME=str(base / 'dotnet-home'), NUGET_PACKAGES=str(base / 'packages'),
               NUGET_HTTP_CACHE_PATH=str(base / 'http-cache'), TMPDIR=str(base / 'tmp'),
               FSGG_TEMPLATES_RAW_BASE=f'http://127.0.0.1:{port}')
    return env


def prepare_offline_tools(m, base):
    feed = base / 'tool-feed'
    feed.mkdir()
    for item in m['ordinaryToolPackages']:
        require(sha(item['path']) == m['inputs'][item['path']], 'ordinary-tool-package-drift')
        require(Path(item['name']).name == item['name'] and item['name'].endswith('.nupkg'), 'ordinary-tool-package-name')
        shutil.copyfile(item['path'], feed / item['name'])
    config = base / 'ordinary-NuGet.config'
    # Owner-generated path contains no XML-special characters.
    require(not any(c in str(feed) for c in '<>&\"'), 'offline-feed-path')
    config.write_text('<configuration><packageSources><clear/><add key="admitted-local" value="' + str(feed) + '"/></packageSources></configuration>')
    return config


def call(argv, *, env=None, cwd=None, succeeds=True):
    argv = [str(x) for x in argv]
    entry = dict(argv=argv, cwd=str(cwd) if cwd else None, timeoutSeconds=180)
    try:
        result = subprocess.run(argv, env=env if env is not None else CALL_ENV, cwd=cwd, capture_output=True, text=True, timeout=180)
        entry.update(actualExitCode=result.returncode, stdout=result.stdout, stderr=result.stderr)
    except subprocess.TimeoutExpired:
        entry.update(actualExitCode=None, timedOut=True)
        raise
    finally:
        if COMMAND_JOURNAL is not None:
            with COMMAND_JOURNAL.open('a') as out:
                out.write(json.dumps(entry) + '\n')
    if succeeds and result.returncode:
        raise AssertionError(f"Command refused ({result.returncode}): {argv}\n{result.stdout[-10000:]}\n{result.stderr[-10000:]}")
    return result


def source_controls():
    """Pure transport/command guards; no real CLI or build is invoked."""
    import unittest
    from unittest.mock import patch
    class Controls(unittest.TestCase):
        def test_public_descriptor_keeps_original(self):
            text = '    source: FS.GG.UI.Template::0.31.0\n'
            self.assertEqual(descriptor_content(text, 'rendering.providers.yml', True), text)
        def test_historical_descriptor_override_remains(self):
            self.assertIn('::0.32.0', descriptor_content('source: FS.GG.UI.Template::0.31.0', 'rendering.providers.yml', False))
        def test_wrong_public_descriptor_refuses(self):
            with self.assertRaises(ValueError): descriptor_content('source: FS.GG.UI.Template::0.32.0', 'rendering.providers.yml', True)
        def test_duplicate_public_descriptor_refuses(self):
            with self.assertRaises(ValueError): descriptor_content('source: FS.GG.UI.Template::0.31.0\n' * 2, 'rendering.providers.yml', True)
        def test_unrelated_descriptor_unchanged(self):
            self.assertEqual(descriptor_content('original', 'console.providers.yml', True), 'original')
        def test_public_checker_has_no_override(self):
            self.assertNotIn('--command-json', checker_command(Path('/fixture'), Path('/producer'), True))
        def test_historical_checker_override_remains(self):
            self.assertIn('--command-json', checker_command(Path('/fixture'), Path('/producer'), False))
        def test_allowlist_excludes_credentials(self):
            with patch.dict(os.environ, {'HOME': '/owner', 'GH_TOKEN': 'synthetic-control'}):
                env = installed_environment(dict(actualHome='/owner', childEnvironment=dict(PATH='/usr/bin')), Path('/fixture'), 1234)
                self.assertNotIn('GH_TOKEN', env)
                self.assertEqual(env['HOME'], '/owner')
                self.assertEqual(env['DOTNET_CLI_HOME'], '/fixture/dotnet-home')
        def test_home_override_refuses(self):
            with patch.dict(os.environ, {'HOME': '/owner'}):
                with self.assertRaises(ValueError): installed_environment(dict(actualHome='/owner', childEnvironment=dict(HOME='/other')), Path('/fixture'), 1234)
        def test_credential_override_refuses(self):
            with patch.dict(os.environ, {'HOME': '/owner'}):
                with self.assertRaises(ValueError): installed_environment(dict(actualHome='/owner', childEnvironment=dict(GITHUB_TOKEN='synthetic')), Path('/fixture'), 1234)
        def test_package_payload_signature_only_exception(self):
            with tempfile.TemporaryDirectory() as folder:
                one, two = Path(folder) / 'one.zip', Path(folder) / 'two.zip'
                for path, signed in [(one, False), (two, True)]:
                    with zipfile.ZipFile(path, 'w') as z:
                        z.writestr('nested/.signature.p7s', 'retained')
                        if signed: z.writestr('.signature.p7s', 'root signature')
                self.assertEqual(payload(one), payload(two))
                self.assertIn('nested/.signature.p7s', payload(two))
        def test_package_traversal_refuses(self):
            with tempfile.TemporaryDirectory() as folder:
                path = Path(folder) / 'bad.zip'
                with zipfile.ZipFile(path, 'w') as z: z.writestr('../bad', 'bad')
                with self.assertRaises(ValueError): payload(path)
        def test_command_timeout_and_exit_remain_actual(self):
            with patch('subprocess.run') as run:
                run.return_value = subprocess.CompletedProcess(['pure-control'], 7, 'control', 'control')
                self.assertEqual(call(['pure-control'], succeeds=False).returncode, 7)
                self.assertEqual(run.call_args.kwargs['timeout'], 180)
        def test_missing_root_manifest_refuses(self):
            with self.assertRaises(ValueError): validate_installed(argparse.Namespace(installed_manifest=None, approved_installed_manifest_sha256=None))
    import runpy
    custody = runpy.run_path(str(Path(__file__).with_name('test-insufficient-public-custody.py')))
    suite = unittest.TestSuite([unittest.defaultTestLoader.loadTestsFromTestCase(Controls),
                                unittest.defaultTestLoader.loadTestsFromTestCase(custody['Custody'])])
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    require(result.wasSuccessful(), 'source-controls-failed')


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
    parser.add_argument('--qualification-mode', choices=('source-candidate', 'installed-public'), default='source-candidate')
    parser.add_argument('--installed-manifest', type=Path)
    parser.add_argument('--approved-installed-manifest-sha256')
    parser.add_argument('--source-controls', action='store_true', help='Pure controls only; no producer or Wizard invocation')
    args = parser.parse_args()
    if args.source_controls:
        source_controls()
        return
    installed = args.qualification_mode == 'installed-public'
    m = validate_installed(args) if installed else None
    if installed:
        global CALL_ENV
        global COMMAND_JOURNAL
        Path(m['workRoot']).mkdir(mode=0o700)
        (Path(m['workRoot']) / 'tmp').mkdir()
        COMMAND_JOURNAL = Path(m['workRoot']) / 'commands.jsonl'
        CALL_ENV = installed_environment(m, Path(m['workRoot']), 0)
    version = call([args.sdd_cli, "--version"]).stdout.strip()
    assert version == "2.1.0", f"This source window requires a genuine 2.1.0 candidate; got {version!r}"
    assert call([args.insufficient_cli, "--version"]).stdout.strip() == "2.0.3"
    dotnet = shutil.which("dotnet")
    assert dotnet
    if installed:
        require(str(Path(dotnet).resolve()) == m['dotnetExecutable'], 'installed-sdk-executable')
    wizard_command = [dotnet, args.wizard] if args.wizard.suffix == ".dll" else [args.wizard]
    assert all(path.is_file() for path in (args.wizard, args.sdd_cli, args.insufficient_cli, args.workspace_package, args.rendering_package))
    with zipfile.ZipFile(args.workspace_package) as package:
        candidates = [name for name in package.namelist() if "/fs-gg-project-knowledge/" in name and name.endswith("/scripts/check-project-knowledge.py")]
        assert len(candidates) == 1, candidates
        checker_bytes = package.read(candidates[0])
        workflow_bytes = package.read(candidates[0].replace("scripts/check-project-knowledge.py", ".github/workflows/project-knowledge.yml"))
    closure = Path(m['sddClosure']) if installed else args.sdd_cli.parent
    report = {"qualification": "installed-public-packages-private-wizard" if installed else "source-candidate-only", "sddVersion": version,
              "sddClosureSha256": {name: sha(closure / name) for name in ("FS.GG.SDD.Cli.dll", "FS.GG.SDD.Commands.dll", "FS.GG.SDD.Knowledge.dll")},
              "workspaceArchiveSha256": hashlib.sha256(args.workspace_package.read_bytes()).hexdigest(),
              "renderingArchiveSha256": hashlib.sha256(args.rendering_package.read_bytes()).hexdigest(),
              "providerSourceRevision": args.provider_revision or "working-source-candidate", "routes": []}
    if installed:
        report.update(sddExecutableSha256=sha(args.sdd_cli), installedManifestSha256=sha(args.installed_manifest),
                      wizardSha256=sha(args.wizard), publicWizard=False, adoptionReceiptEmitted=False,
                      workspaceOriginalArchiveSha256=sha(m['workspaceOriginalArchive']), renderingProtectedPin='0.31.0')
    else:
        report['sddClosureSha256'][args.sdd_cli.name] = sha(args.sdd_cli)
    with (nullcontext(m['workRoot']) if installed else tempfile.TemporaryDirectory(prefix="wizard-knowledge-creation-")) as temporary:
        base = Path(temporary)
        server_root = base / "http" / "fixture" / "providers"
        server_root.mkdir(parents=True)
        for provider in args.providers.glob("*.providers.yml"):
            content = (call(["git", "show", f"{args.provider_revision}:providers/{provider.name}"], cwd=args.providers.parent).stdout
                       if args.provider_revision else provider.read_text())
            if provider.name == "rendering.providers.yml" and not installed:
                assert content.count("source: FS.GG.UI.Template::0.31.0") == 1
                report["renderingDescriptorTransportOverride"] = {"protectedPin": "0.31.0", "candidatePin": "0.32.0", "onlySourceLineChanged": True}
            content = descriptor_content(content, provider.name, installed)
            if installed:
                require(hashlib.sha256(content.encode()).hexdigest() == m['descriptorSha256'][provider.name], 'checked-descriptor-bytes')
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
        ordinary_config = None
        if installed:
            env = installed_environment(m, base, server.server_port)
            ordinary_config = prepare_offline_tools(m, base)
        shim = base / "bin"
        shim.mkdir()
        sdd_path = shim / "fsgg-sdd"
        def select_cli(executable):
            sdd_path.write_text("#!/usr/bin/env python3\nimport os,sys\nos.execv(" + repr(str(executable.resolve())) + ", [" + repr(str(executable.resolve())) + "] + sys.argv[1:])\n")
            sdd_path.chmod(0o755)
        replacements = {"FS.GG.Workspace.Template::0.18.0": str(args.workspace_package.resolve()),
                        "FS.GG.UI.Template::" + ('0.31.0' if installed else '0.32.0'): str(args.rendering_package.resolve())}
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
                if installed:
                    call(['dotnet', 'tool', 'restore', '--configfile', ordinary_config, '--no-cache'], env=env, cwd=root)
                check_command = checker_command(root, args.sdd_cli, installed)
                ci = json.loads(call(check_command, env=env if installed else None).stdout)
                assert ci['limit'] == 10485760
                if installed:
                    assert ci['sourceCandidateOverride'] is False and ci['pinnedVersion'] == '2.1.0'
                record = next((root / ".fsgg/knowledge/records").glob("*.json"))
                original_record = record.read_bytes()
                try:
                    # Valid JSON trailing whitespace bypasses the write API and exceeds
                    # the full canonical population cap; invoke the owner's actual CI entry.
                    record.write_bytes(original_record + b" " * 10485761)
                    refused_budget = call(check_command, env=env if installed else None, succeeds=False)
                    assert refused_budget.returncode != 0 and "Producer knowledge check refused" in refused_budget.stderr
                finally:
                    record.write_bytes(original_record)
                clone = base / ("clone-" + profile)
                call(["git", "clone", "-q", root, clone])
                assert not (clone / ".fsgg/cache").exists()
                assert json.loads(call([args.sdd_cli, "knowledge", "check", "--root", clone]).stdout)["Limit"] == 10485760
                if installed:
                    call(['dotnet', 'tool', 'restore', '--configfile', ordinary_config, '--no-cache'], env=env, cwd=clone)
                    clone_ci = json.loads(call(checker_command(clone, args.sdd_cli, True), env=env).stdout)
                    assert clone_ci['sourceCandidateOverride'] is False and clone_ci['limit'] == 10485760
                report["routes"].append({"profile": profile, "initialCommit": git(root, "rev-parse", "HEAD"), "canonicalFiles": len(canonical), "normalForegroundCallerCommit": True})
            cache_metadata = base / "dotnet-home/.templateengine/packages.json"
            registrations = json.loads(cache_metadata.read_text(encoding="utf-8-sig"))["Packages"]
            overlays = [item for item in registrations if item.get("Details", {}).get("PackageId") == "FS.GG.Workspace.Template"]
            assert len(overlays) == 1, "Repeated creation duplicated package registration"
            cached_archive = Path(overlays[0]["MountPointUri"])
            if installed:
                require(cached_archive.resolve().is_relative_to(base.resolve()), 'mutable-cache-outside-owned-root')
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
            report["insufficientProducerQualificationScope"] = "public-archive-literal-closure" if installed else "private-source-candidate"
            if installed:
                report["insufficientPublicBinding"] = m["insufficientPublic"]
        finally:
            server.shutdown()
    if installed:
        require(validate_installed(args, after=True) == m, 'installed-post-input-drift')
        report['inputPinsUnchanged'] = True
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
