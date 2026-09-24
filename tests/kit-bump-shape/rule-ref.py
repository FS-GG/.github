#!/usr/bin/env python3
"""Execute the reusable workflow's real release-ref step with offline provider replies.

The 0.91.4 manifest fixture is the public release asset at
https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.91.4; its archive digest
is checked below before it is used as a positive control.
"""

import copy
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/kit-materialize.yml"
FIXTURE = Path(__file__).with_name("release-manifest-v0.91.4.json")
SOURCE = "f0b1bb8748cbb7b27aeaf7346b8690c82ed03343"
LEGACY_SOURCE = "9d35d674a9443d922dd86c9ffee725bffee4ef8a"


def step_script() -> str:
    text = WORKFLOW.read_text()
    start = text.index("        id: rule-ref\n")
    start = text.index("        run: |\n", start) + len("        run: |\n")
    end = text.index("      - name: Check out the shape rule", start)
    return "".join(line[10:] if line.startswith("          ") else line
                   for line in text[start:end].splitlines(keepends=True))


def write_nuspec(path: Path, version: str, source: str) -> None:
    path.mkdir()
    (path / "fs.gg.kit.nuspec").write_text(
        f'<package><metadata><id>FS.GG.Kit</id><version>{version}</version>'
        f'<repository type="git" url="https://github.com/FS-GG/.github" commit="{source}" />'
        '</metadata></package>\n'
    )


def rehash(manifest: dict) -> None:
    descriptor = json.dumps(manifest["descriptor"], sort_keys=True, separators=(",", ":"),
                            ensure_ascii=False).encode()
    content_id = "sha256:" + hashlib.sha256(descriptor).hexdigest()
    manifest["contentId"] = content_id
    manifest["state"]["channelPromotion"]["receipt"]["contentId"] = content_id


def run_case(name: str, *, version: str = "0.91.4", kit_sha: str = "",
             coherent_sha: str = SOURCE, nuspec_sha: str = SOURCE,
             mutate=None, curl_fails: bool = False, good: bool = True) -> None:
    with tempfile.TemporaryDirectory(prefix="kit-rule-ref-") as temp:
        root = Path(temp)
        bindir = root / "bin"
        bindir.mkdir()
        (bindir / "git").write_text(
            "#!/usr/bin/env python3\n"
            "import os, sys\n"
            "assert sys.argv[1] == 'ls-remote'\n"
            "version = os.environ['KIT_VERSION']\n"
            "for kind, sha in [('kit', os.getenv('TEST_KIT_SHA', '')), "
            "('coherent-set', os.getenv('TEST_COHERENT_SHA', ''))]:\n"
            "    if sha: print(f'{sha}\\trefs/tags/{kind}/v{version}')\n"
        )
        (bindir / "curl").write_text(
            "#!/usr/bin/env python3\n"
            "import os, pathlib, shutil, sys\n"
            "if os.getenv('TEST_CURL_FAIL') == '1': sys.exit(22)\n"
            "assert '/coherent-set/v' in ' '.join(sys.argv)\n"
            "shutil.copyfile(os.environ['TEST_MANIFEST'], sys.argv[sys.argv.index('--output')+1])\n"
        )
        for executable in bindir.iterdir():
            executable.chmod(0o755)
        manifest = copy.deepcopy(json.loads(FIXTURE.read_text()))
        if mutate:
            mutate(manifest)
        manifest_path = root / "manifest.json"
        manifest_path.write_text(json.dumps(manifest))
        kit_dir = root / "kit"
        write_nuspec(kit_dir, version, nuspec_sha)
        output = root / "output"
        summary = root / "summary"
        env = {**os.environ, "PATH": str(bindir) + os.pathsep + os.environ["PATH"],
               "KIT_VERSION": version, "KIT_DIR": str(kit_dir), "RUNNER_TEMP": str(root),
               "GITHUB_OUTPUT": str(output), "GITHUB_STEP_SUMMARY": str(summary),
               "TEST_KIT_SHA": kit_sha, "TEST_COHERENT_SHA": coherent_sha,
               "TEST_MANIFEST": str(manifest_path), "TEST_CURL_FAIL": str(int(curl_fails))}
        result = subprocess.run(["bash", "-e"], input=step_script(), text=True,
                                capture_output=True, env=env)
        if good:
            assert result.returncode == 0, (name, result.stdout, result.stderr)
            parts = tuple(map(int, version.split(".")))
            chosen = coherent_sha if parts >= (0, 91, 0) else kit_sha
            assert output.read_text() == f"sha={chosen}\n", name
            assert f"@{chosen}" in result.stdout, name
        else:
            assert result.returncode != 0, name
            assert "kit-bump-shape:" in result.stderr, (name, result.stderr)
            assert not output.exists(), name
        print(f"PASS {name}")


def wrong_source(manifest: dict) -> None:
    manifest["descriptor"]["sourceSha"] = "a" * 40
    manifest["state"]["channelPromotion"]["receipt"]["sourceSha"] = "a" * 40
    rehash(manifest)


def wrong_version(manifest: dict) -> None:
    manifest["descriptor"]["version"] = "0.91.5"
    manifest["state"]["channelPromotion"]["receipt"]["version"] = "0.91.5"
    rehash(manifest)


def missing_member(manifest: dict) -> None:
    manifest["descriptor"]["packages"] = [
        row for row in manifest["descriptor"]["packages"] if row["id"] != "FS.GG.Kit"
    ]
    rehash(manifest)


assert hashlib.sha256(FIXTURE.read_bytes()).hexdigest() == (
    "b750c24617e8b9eefa1ea7809c3562c1be719c19bec3cb4dcdbd6ba948c8ee9e"
)
assert re.fullmatch(r"[0-9a-f]{40}", SOURCE)
run_case("published 0.91.4 successor without kit tag")
run_case("stray successor Kit tag cannot bypass manifest", kit_sha="b" * 40)
run_case("legacy 0.90 kit tag", version="0.90.0", kit_sha=LEGACY_SOURCE,
         coherent_sha="3adada5a9738464291088830c47a30a3a8fc9561")
run_case("successor coherent tag absent despite stray Kit tag", coherent_sha="",
         kit_sha="b" * 40, good=False)
run_case("legacy Kit tag absent despite coherent tag", version="0.90.0", good=False)
run_case("missing both tags", coherent_sha="", good=False)
run_case("moved coherent tag", coherent_sha="b" * 40, good=False)
run_case("unreadable release manifest", curl_fails=True, good=False)
run_case("wrong manifest source", mutate=wrong_source, good=False)
run_case("wrong manifest version", mutate=wrong_version, good=False)
run_case("missing Kit member", mutate=missing_member, good=False)
run_case("wrong restored nuspec source", nuspec_sha="c" * 40, good=False)
run_case("malformed version", version="0.91.4.1", good=False)
