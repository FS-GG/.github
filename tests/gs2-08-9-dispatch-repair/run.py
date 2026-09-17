#!/usr/bin/env python3
"""Offline adversarial checks for the GS2-08.9 dispatch/repair retirement."""

from __future__ import annotations

import json
import pathlib
import re
import subprocess
import tempfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / "tests/gs2-08-9-dispatch-repair/disposition.json"
ROUTES = [
    ".github/workflows/dispatch-sender.yml",
    ".github/workflows/fsgg-dispatch-broker.yml",
    ".github/workflows/feed-autofix.yml",
    ".github/workflows/skill-registry-autofix.yml",
    ".github/workflows/skillmirror-redrive.yml",
    ".github/workflows/kit-materialize.yml",
    ".github/workflows/lockfile-sync.yml",
    ".github/workflows/touch-set-drift.yml",
    ".github/workflows/github-substrate-v2-authority-qualification.yml",
]


def active_lines(text: str) -> str:
    return "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("#"))


evidence = json.loads(EVIDENCE.read_text())
assert [row["path"] for row in evidence["routes"]] == ROUTES
assert evidence["productionCalls"] == 0
assert evidence["productionCredentials"] == 0

for relative in ROUTES:
    text = (ROOT / relative).read_text()
    active = active_lines(text)
    assert "GS2-08.9 retirement boundary" in text, relative
    assert not re.search(r"^\s*(contents|issues|pull-requests|checks|actions|administration):\s*write\s*$", active, re.M), relative
    assert "actions/create-github-app-token" not in active, relative
    assert "FSGG_DISPATCH_APP" not in active, relative
    assert "${{ secrets." not in active, relative
    assert not re.search(r"\bgh\s+api\s+-X\s+(POST|PATCH|PUT|DELETE)\b", active), relative
    assert not re.search(r"\bgh\s+pr\s+(create|edit|merge|close)\b", active), relative
    assert not re.search(r"repos/[^\s]+/dispatches", active), relative
    assert not re.search(r"(^|\s)--write(?:\s|$)", active), relative

# No route other than the local bare-repository qualification may even spell a push command.
for relative in ROUTES[:-1]:
    assert not re.search(r"^\s*git\s+push(?:\s|$)", active_lines((ROOT / relative).read_text()), re.M), relative

authority = (ROOT / ROUTES[-1]).read_text()
assert "local-authority-qualification.sh" in authority
assert "github.com" not in active_lines(authority)
assert "GH_TOKEN" not in active_lines(authority)
subprocess.run(
    ["bash", str(ROOT / "tests/gs2-08-9-dispatch-repair/local-authority-qualification.sh")],
    check=True,
)

for reusable in (ROUTES[0], ROUTES[5], ROUTES[6]):
    text = (ROOT / reusable).read_text()
    for secret in ("app-id", "app-private-key"):
        declaration = re.search(
            rf"^\s{{6}}{re.escape(secret)}:\s*$\n(?:^\s{{8}}.*$\n)*?^\s{{8}}required:\s*false\s*$",
            text,
            re.M,
        )
        assert declaration, f"{reusable}: optional compatibility declaration missing for {secret}"

kit = active_lines((ROOT / ROUTES[5]).read_text())
assert "actions/create-github-app-token" not in kit
assert not re.search(r"^\s+- uses:\s+\./", kit, re.M)
assert "tests/gs2-08-9-dispatch-repair" not in kit
assert "  materialize:" not in kit
for job in ("bump-shape", "bump-mechanical", "receiver-validate"):
    assert re.search(rf"^  {job}:\s*$", kit, re.M), job

caller = (ROOT / "tests/gs2-08-9-dispatch-repair/fixtures/net-kit-materialize-caller.yml").read_text()
assert "FS-GG/.github/.github/workflows/kit-materialize.yml@main" in caller
assert "app-id: ${{ secrets.FSGG_DISPATCH_APP_ID }}" in caller
assert "app-private-key: ${{ secrets.FSGG_DISPATCH_APP_PRIVATE_KEY }}" in caller

# The protected mechanical context must consume the real bump-shape verdict and fail on every
# non-mechanical, refused, absent, or failed producer outcome. It may never emit unconditional success.
assert "needs: bump-shape" in kit
assert "VERDICT: ${{ needs.bump-shape.outputs.verdict }}" in kit
assert "if: ${{ !cancelled() && github.event_name == 'pull_request' }}" in kit
assert "kit-bump-mechanical: verdict 1" in kit
assert "kit-bump-mechanical: verdict 3" in kit
assert "kit-bump-mechanical: kit-bump-shape published no verdict" in kit

sender = evidence["routes"][0]["historicalCapability"]
assert sender["calleeRevision"] == "5fed2838f9ed085ffca09f4cc18b4f7bc59c1294"
assert sender["status"] == "requires-administrative-revocation"
assert "FSGG_DISPATCH_APP_PRIVATE_KEY" in sender["action"]

census_path = ROOT / "docs/coordination/v1-writer-census.json"
census = json.loads(census_path.read_text())
census_rows = {row["path"]: row for row in census["sources"]}
assert census_rows[ROUTES[0]]["disposition"] == "read-only"
assert census_rows[ROUTES[1]]["disposition"] == "read-only"
assert census_rows[ROUTES[5]]["disposition"] == "read-only"
assert census_rows[ROUTES[-1]]["disposition"] == "local-only"
for retired in (ROUTES[2], ROUTES[3], ROUTES[4], ROUTES[6], ROUTES[7]):
    assert retired not in census_rows, retired

mutated = json.loads(json.dumps(census))
next(row for row in mutated["sources"] if row["path"] == ROUTES[0])["disposition"] = "conditional-remote-writer"
with tempfile.TemporaryDirectory(prefix="gs2-08-9-census-") as temporary:
    mutation = pathlib.Path(temporary) / "census.json"
    mutation.write_text(json.dumps(mutated))
    result = subprocess.run(
        [
            "python",
            str(ROOT / "scripts/check-v1-writer-census.py"),
            "--root",
            str(ROOT),
            "--census",
            str(mutation),
            "--structural",
        ],
        text=True,
        capture_output=True,
    )
    assert result.returncode != 0
    assert "must remain classified read-only" in result.stderr

print("GS2-08.9 dispatch/repair routes are read-only or local-only; historical SHA capability is explicitly unresolved")
