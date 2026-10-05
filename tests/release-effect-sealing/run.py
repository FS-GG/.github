#!/usr/bin/env python3
"""Offline GS2-08.9 checks for the retired release/publication routes."""

from __future__ import annotations

import hashlib
import ast
import json
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
    ".github/workflows/release-telemetry-host.yml",
)
WIZARD_SUCCESSOR = ".github/workflows/release-new-sdd-workspace.yml"
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

# SVG-D5 0.12.0 explicitly supersedes only the wizard filename's no-effect seal.
# Its old OIDC policy remains bound to this filename, but its new effect boundary
# is the exact candidate, live-main and protected-journal successor adapter.
wizard = executable_text(ROOT / WIZARD_SUCCESSOR)
assert "workflow_dispatch:" in wizard and not re.search(r"^\s+(push|schedule):", wizard, re.MULTILINE)
assert "github.ref == 'refs/heads/main' && github.actor == 'EHotwagner'" in wizard
assert "environment: release-successor" in wizard
assert "candidate_archive_sha256:" in wizard and "publish:" in wizard
assert "verify_nuget_login:" in wizard and "uses: NuGet/login@v1" in wizard
assert "scripts/new-sdd-workspace-successor-publish.py" in wizard
assert "--preflight-only" in wizard and "--publish" in wizard
for token in ("dotnet nuget push", "gh release create", "gh release upload", "gh release edit", "git push"):
    assert token not in wizard, f"wizard workflow bypasses the successor adapter: {token}"
admission = (ROOT / "scripts/new_sdd_workspace_successor_admission.py").read_text()
assert f'WORKFLOW = "{WIZARD_SUCCESSOR}"' in admission

# COORD-BOARD-V2-01.5 source successor: keep the accepted 0.13 route
# as history and bind current bytes only after proving the retained boundaries.
report = json.loads((ROOT / "docs/reports/gs2-08-9-release-route-dispositions.json").read_text())
route = next(row for row in report["routes"] if row["path"] == WIZARD_SUCCESSOR)
assert route["sha256"] == digest(ROOT / WIZARD_SUCCESSOR)
assert route["disposition"] == "exact-source-protected-journal-successor-only"
amendment = route["successorAmendment"]
assert amendment["unit"] == "COORD-BOARD-V2-01.5" and amendment["version"] == "0.15.0"
assert amendment["operationAcceptance"] == "candidate, journal initialization, protected preflight and 0.15 publication not performed"
history = next(row for row in report["historicalRouteAttestations"]
               if row["acceptedSource"] == amendment["previousAcceptedSource"])
assert history["status"] == "non-current-history"
assert history["route"]["sha256"] == amendment["previousSuccessorWorkflowSha256"]
for key in ("forwardRecoveryAmendment", "capabilityLoss", "historicalCapabilityLoss", "historicalDisposition"):
    assert route[key] == history["route"][key], f"retained recovery/retirement boundary changed: {key}"
proof = route["bindingIsolationProof"]
for name, expected in proof["historicalRecoveryJobSha256"].items():
    raw_workflow = (ROOT / WIZARD_SUCCESSOR).read_text()
    start = raw_workflow.index("  " + name + ":\n")
    end = raw_workflow.find("\n  recovery-", start + 1)
    assert hashlib.sha256(raw_workflow[start:end if end != -1 else None].encode()).hexdigest() == expected
assert "format('Wizard 0.13 recovery {0} {1} {2}', inputs.promotion_recovery, inputs.recovery_correlation, inputs.recovery_binding_sha256)" in wizard
assert "default: false" in wizard and "default: 'off'" in wizard
recovery_source = (ROOT / "scripts/new_sdd_workspace_promote_recovery.py").read_text()
def verify_recovery_source_spans(source, expected_proof):
    """Verify exact production spans; metadata does not exempt amended guards."""
    parsed = ast.parse(source)
    assignments = {",".join(target.id for target in node.targets): node
                   for node in parsed.body if isinstance(node, ast.Assign)
                   and all(isinstance(target, ast.Name) for target in node.targets)}
    functions = {node.name: node for node in parsed.body if isinstance(node, ast.FunctionDef)}
    methods = {owner.name + "." + node.name: node
               for owner in parsed.body if isinstance(owner, ast.ClassDef)
               for node in owner.body if isinstance(node, ast.FunctionDef)}
    # AST locates exact bytes; interpreter-dependent serialization is not evidence.
    for spans, expected_spans in ((assignments, expected_proof["historicalRecoveryAssignmentSourceSha256"]),
                                  (functions, expected_proof["historicalGuardFunctionSourceSha256"]),
                                  (methods, expected_proof["pagedAncestryMethodSourceSha256"])):
        for name, expected in expected_spans.items():
            source_span = ast.get_source_segment(source, spans[name])
            assert source_span is not None
            assert hashlib.sha256(source_span.encode("utf-8")).hexdigest() == expected, name
    return parsed

ancestry_amendment = proof["pagedAncestryAmendment"]
assert ancestry_amendment["unit"] == "TSDD-KNOWLEDGE-01.4"
assert ancestry_amendment["originalItem"] == "TEMPLATES-H2"
assert ancestry_amendment["sourceCandidate"] == "44667c54a81ed9fdc63ab5bc4174b8448bb49f32"
assert ancestry_amendment["acceptedReviewSha256"] == "05928c08a8bc9ab59c76d65b0d2a5cac25f3458e37f43769bf38f2ada2e298e3"
assert ancestry_amendment["previousOriginalRunsSourceSha256"] == "0a5cbcb4ec4143998834aee2018ca303d87b00f37da757fdc1089badb35385c6"
assert ancestry_amendment["currentOriginalRunsSourceSha256"] == proof["historicalGuardFunctionSourceSha256"]["original_runs"]
assert {"ancestry_url", "validate_ancestry", "original_runs"} <= set(proof["historicalGuardFunctionSourceSha256"])
assert set(proof["pagedAncestryMethodSourceSha256"]) == {"FiniteAPI.request", "Redirect.redirect_request"}
recovery = verify_recovery_source_spans(recovery_source, proof)

# Causal offline negatives mutate production bytes, not fabricated native receipts.
# The same span verifier must refuse an accidental weakening of the new role and
# drift of a retained source/effect guard, even though metadata names an amendment.
mutations = (
    ("total>=2", "total>=1"),
    ("?per_page=1&page=2", "?per_page=1&page=1"),
    ('response["behind_by"]==0', 'response["behind_by"]>=0'),
    ("headers is None", "True"),
    ("body is None and headers", "True and headers"),
    ('row["status"]==200', 'row["status"]==201'),
    ('stream.write(data)', 'stream.write(data[:1])'),
    ('"/compare" not in urllib.parse.unquote', '"/unrelated" not in urllib.parse.unquote'),
    ('CANDIDATE_SOURCE="f891b5b0723070c67e08d1a87b7d12b0b4d8bebe"', 'CANDIDATE_SOURCE="' + "a" * 40 + '"'),
    ('original.get("conclusion")=="failure"', 'original.get("conclusion")=="success"'),
)
for original, changed in mutations:
    assert original in recovery_source, f"negative control no longer selects production bytes: {original}"
    mutated = recovery_source.replace(original, changed, 1)
    try:
        verify_recovery_source_spans(mutated, proof)
    except AssertionError:
        pass
    else:
        raise AssertionError(f"unexpected source/effect seal weakening accepted: {original}")
print(f"Wizard paged ancestry source seals: {len(mutations)} mutated production guards refused")
calls = [node for node in ast.walk(recovery) if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)]
for function, keyword, count in (("effects", "binding", 2), ("WizardAdmission", "release_binding", 1)):
    selected = [call for call in calls if call.func.id == function]
    assert len(selected) == count
    assert all(any(arg.arg == keyword and isinstance(arg.value, ast.Name) and arg.value.id == "HISTORICAL_013"
                   for arg in call.keywords) for call in selected)
assert "self.release_binding is not CURRENT_015" in admission
assert "self.release_binding is not HISTORICAL_013" in admission
execution = (ROOT / "scripts/new_sdd_workspace_successor_execution.py").read_text()
assert 'CURRENT_014 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.14.0", "new-sdd-workspace/v0.14.0")' in execution
assert 'CURRENT_015 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.15.0", "new-sdd-workspace/v0.15.0")' in execution
assert 'HISTORICAL_013 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.13.0", "new-sdd-workspace/v0.13.0")' in execution
assert "binding is not CURRENT_015 and binding is not HISTORICAL_013" in execution

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
