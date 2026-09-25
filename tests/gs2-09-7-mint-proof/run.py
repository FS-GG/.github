#!/usr/bin/env python3
"""Secret-free negative controls for the protected sandbox token mint."""

import copy
import datetime
import importlib.util
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "sandbox_mint", ROOT / "scripts/gs2-09-7-mint-sandbox-token.py")
mint = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mint)


def valid_response():
    return {
        "token": "ghs_" + "a" * 40,
        "expires_at": (datetime.datetime.now(datetime.timezone.utc)
                       + datetime.timedelta(minutes=45)).isoformat().replace("+00:00", "Z"),
        "permissions": {**mint.PERMISSIONS, "metadata": "read"},
        "repository_selection": "selected",
        "repositories": [{"id": mint.REPOSITORY_ID,
                          "node_id": mint.REPOSITORY_NODE_ID,
                          "full_name": f"{mint.OWNER}/{mint.REPOSITORY}"}],
    }


def refuse(change):
    response = valid_response()
    change(response)
    try:
        mint.validate_mint_response(response)
    except (ValueError, TypeError):
        return
    raise AssertionError("unsafe mint response was accepted")


proof = mint.validate_mint_response(valid_response())
assert proof["permissions"]["issues"] == "write"
assert proof["permissions"]["organization_projects"] == "write"
assert proof["repository"]["nodeId"] == mint.REPOSITORY_NODE_ID
assert "token" not in proof
assert len(proof["tokenSha256"]) == 64

refuse(lambda r: r["permissions"].update(issues="read"))
refuse(lambda r: r["permissions"].pop("organization_projects"))
refuse(lambda r: r["permissions"].update(workflows="write"))
refuse(lambda r: r.update(repository_selection="all"))
refuse(lambda r: r["repositories"].append(copy.deepcopy(r["repositories"][0])))
refuse(lambda r: r["repositories"][0].update(id=1))
refuse(lambda r: r["repositories"][0].update(node_id="foreign"))
refuse(lambda r: r["repositories"][0].update(full_name="FS-GG/production"))
refuse(lambda r: r.update(expires_at="2000-01-01T00:00:00Z"))
refuse(lambda r: r.update(token="not a token"))

# The Q4 workflow is the installed credential handoff for the later migration
# rehearsal. Keep its token source on the protected revision, not the product
# candidate or an unproven second mint path.
workflow = (ROOT / ".github/workflows/github-substrate-v2-sandbox-qualification.yml").read_text()
steps = re.split(r"(?m)^      - name: ", workflow)[1:]
steps = {step.split("\n", 1)[0]: step for step in steps}
required = [
    "Check out the protected mint implementation",
    "Bind the protected mint revision",
    "Exercise secret-free mint grant controls",
    "Mint and attest the exact sandbox App grant",
    "Authoritatively bind the App and sandbox targets before writes",
    "Execute the signed live operation plan",
    "Compensate and authoritatively verify cleanup",
    "Revoke the scoped token",
]
positions = [workflow.index("      - name: " + name) for name in required]
assert positions == sorted(positions)
assert "ref: ${{ github.sha }}" in steps[required[0]]
assert "git -C host rev-parse HEAD" in steps[required[1]]
assert "python3 host/tests/gs2-09-7-mint-proof/run.py" in steps[required[2]]
assert "python3 host/scripts/gs2-09-7-mint-sandbox-token.py" in steps[required[3]]
assert "id: mint" in steps[required[3]]
assert "steps.mint.outputs.token" in steps[required[4]]
assert "mint-grants.json" in steps[required[4]]
for name in required[5:7]:
    assert "FSGG_SANDBOX_TOKEN: ${{ steps.mint.outputs.token }}" in steps[name]
    assert "FSGG_SANDBOX_MINT_PROOF:" in steps[name]
assert "installation/token" in steps[required[7]]
assert "steps.revoke.outcome" in steps["Preserve execution and cleanup verdicts"]
assert "actions/create-github-app-token@" not in workflow
print("sandbox mint grant controls: 11 passed; protected workflow handoff: passed")
