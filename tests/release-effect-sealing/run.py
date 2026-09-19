#!/usr/bin/env python3
"""Offline GS2-08.9 checks for the retired release/publication routes."""

from __future__ import annotations

import hashlib
import os
import pathlib
import re
import subprocess
import tempfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
BOUND = {
    ".github/workflows/release-coord-engine.yml": "7973a57d19123afaafab7cc0cdc8e24a5cb3e891a97c00194be4fed9e1cc3ee5",
    ".github/workflows/release-drivers.yml": "b6e33e102ea9d7d30a8400887ab58a9953209c3dd65611eaf22a439de8b2866b",
    ".github/workflows/release-kit.yml": "caaa1f4dfad50930ccc91582be3fce87335d11bb72386ec9255d3ee62264f626",
    ".github/workflows/release-saga-prepare.yml": "704301a1ed74e863d638d016d835be956c95d68af579213cb5c4b21686877664",
    ".github/workflows/release-saga-start.yml": "316e019d8cec6c60cd78059e5b98abb85b925fa1564f6df9f86345cd00b6d912",
}
QUALIFICATION_ONLY = (
    ".github/workflows/kit-auto-publish.yml",
    ".github/workflows/release-new-sdd-workspace.yml",
    ".github/workflows/release-telemetry-host.yml",
)
FORBIDDEN = (
    "create-github-app-token",
    "NuGet/login",
    "actions/upload-artifact",
    "dotnet nuget push",
    "git push",
    "gh release create",
    "gh release upload",
    "gh release edit",
    "gh workflow run",
    "repository_dispatch",
)


def digest(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def executable_text(path: pathlib.Path) -> str:
    return "\n".join(line for line in path.read_text().splitlines() if not re.match(r"^\s*#", line))


for relative, expected in BOUND.items():
    actual = digest(ROOT / relative)
    assert actual == expected, f"0.90-bound workflow drifted: {relative}: {actual}"

# The accepted workflows remain byte-bound to the immutable 0.90 release, while a
# successor source candidate may advance the coherent scalar. The retained
# kit-auto-publish workflow itself still refuses any version other than 0.90.0;
# this test protects its exact bytes and the reachable effect refusals below.

for relative in QUALIFICATION_ONLY:
    text = executable_text(ROOT / relative)
    assert "workflow_dispatch:" in text
    assert not re.search(r"^\s+(push|schedule):", text, re.MULTILINE), relative
    assert "contents: read" in text
    assert not re.search(r"\b(?:contents|packages|actions|id-token|attestations|pull-requests|issues): write\b", text), relative
    for token in FORBIDDEN:
        assert token not in text, f"{relative} retained forbidden effect token {token!r}"

with tempfile.TemporaryDirectory(prefix="gs2-08-9-release-seal.") as temporary:
    work = pathlib.Path(temporary)
    calls = work / "calls"
    fake_bin = work / "bin"
    fake_bin.mkdir()
    credential_names = (
        "GH_TOKEN", "GITHUB_TOKEN", "NUGET_API_KEY", "NUGET_USER",
        "FSGG_DISPATCH_APP_ID", "FSGG_DISPATCH_APP_PRIVATE_KEY",
    )
    inherited = os.environ.copy()
    inherited.update({name: f"real-looking-inherited-{name.lower()}" for name in credential_names})
    env = {key: value for key, value in inherited.items() if key not in credential_names}
    env.update({
        "GH_TOKEN": "fake-gs2-08-9-gh-token",
        "GITHUB_TOKEN": "fake-gs2-08-9-github-token",
        "NUGET_API_KEY": "fake-gs2-08-9-nuget-key",
        "NUGET_USER": "fake-gs2-08-9-nuget-user",
        "FSGG_DISPATCH_APP_ID": "fake-gs2-08-9-app-id",
        "FSGG_DISPATCH_APP_PRIVATE_KEY": "fake-gs2-08-9-private-key",
        "PATH": f"{fake_bin}:{os.environ['PATH']}",
        "FSGG_FAKE_CALLS": str(calls),
    })
    assert all(not env[name].startswith("real-looking-inherited-") for name in credential_names)
    credential_check = (
        '[ "$GH_TOKEN" = fake-gs2-08-9-gh-token ] && '
        '[ "$GITHUB_TOKEN" = fake-gs2-08-9-github-token ] && '
        '[ "$NUGET_API_KEY" = fake-gs2-08-9-nuget-key ] || exit 98\n'
    )
    for command in ("gh", "dotnet", "curl"):
        stub = fake_bin / command
        stub.write_text(f'#!/usr/bin/env bash\n{credential_check}echo {command} "$@" >> "$FSGG_FAKE_CALLS"\nexit 99\n')
        stub.chmod(0o755)
    adapter = ROOT / "scripts/release-saga-ci.sh"
    for version in ("0.90.0", "0.91.0"):
        for command in ("github", "nuget-probe", "nuget-record"):
            result = subprocess.run(
                ["bash", str(adapter), command, "FS.GG.Kit", version, "a" * 40],
                cwd=ROOT, env=env, text=True, capture_output=True, check=False,
            )
            assert result.returncode == 78, (version, command, result.returncode, result.stderr)
            assert "sealed legacy release effect" in result.stderr
            assert not calls.exists() or not calls.read_text().strip(), f"{version} {command} reached an external command"

    gh = fake_bin / "gh"
    gh.write_text(
        "#!/usr/bin/env bash\n"
        + credential_check
        + 'echo gh "$@" >> "$FSGG_FAKE_CALLS"\n'
        + "if [ \"$1 $2\" = \"release view\" ]; then printf '%s\\n' '{\"isDraft\":true,\"isImmutable\":false}'; exit 0; fi\n"
        + "exit 99\n"
    )
    gh.chmod(0o755)
    calls.unlink(missing_ok=True)
    manifest = work / "manifest.json"
    channel = work / "channel.json"
    manifest.write_text("{}")
    channel.write_text("{}")
    for version in ("0.90.0", "0.91.0"):
        calls.unlink(missing_ok=True)
        tag = f"coherent-set/v{version}"
        result = subprocess.run(
            ["bash", str(ROOT / "scripts/release-saga-promote-release.sh"), "FS-GG/.github", tag, str(manifest), str(channel)],
            cwd=ROOT, env=env, text=True, capture_output=True, check=False,
        )
        assert result.returncode == 78, (version, result.returncode, result.stderr)
        assert calls.read_text().splitlines() == [f"gh release view {tag} --repo FS-GG/.github --json isDraft,isImmutable"]

print("GS2-08.9 release/publication sealing: offline qualification passed")
