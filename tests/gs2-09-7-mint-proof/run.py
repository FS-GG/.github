#!/usr/bin/env python3
"""Secret-free negative controls for the protected sandbox token mint."""

import copy
import datetime
import importlib.util
from pathlib import Path


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
print("sandbox mint grant controls: 11 passed")
