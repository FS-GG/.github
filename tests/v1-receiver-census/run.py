#!/usr/bin/env python3
"""Independent mutations for the offline v1 receiver census validator."""

from __future__ import annotations

import base64
import copy
import gzip
import importlib.util
import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CHECKER = ROOT / "scripts/check-v1-receiver-census.py"
COLLECTOR = ROOT / "scripts/collect-v1-receiver-census.py"
CENSUS = ROOT / "docs/coordination/v1-writer-receiver-census.json"
EVIDENCE = ROOT / "tests/v1-receiver-census/evidence/source-manifests.json.gz"
BLOBS = ROOT / "tests/v1-receiver-census/evidence/source-blobs.json.gz"
EXPECTED = json.loads((Path(__file__).parent / "fixtures/expected-summary.json").read_text())
spec = importlib.util.spec_from_file_location("receiver_census", CHECKER)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)
census = json.loads(CENSUS.read_text())
with gzip.open(EVIDENCE, "rt", encoding="utf-8") as stream:
    evidence = json.load(stream)
with gzip.open(BLOBS, "rt", encoding="utf-8") as stream:
    blobs = json.load(stream)
passed = 0
failed = 0


def ok(label: str) -> None:
    global passed
    passed += 1
    print(f"PASS  {label}")


def bad(label: str, detail: str = "") -> None:
    global failed
    failed += 1
    print(f"FAIL  {label}: {detail}", file=sys.stderr)


def refuses(label: str, mutate) -> None:
    candidate = copy.deepcopy(census)
    candidate_evidence = copy.deepcopy(evidence)
    candidate_blobs = copy.deepcopy(blobs)
    mutate(candidate, candidate_evidence, candidate_blobs)
    try:
        module.validate(candidate, candidate_evidence, candidate_blobs)
    except module.CensusError:
        ok(label)
    else:
        bad(label, "mutation survived")


baseline = subprocess.run([sys.executable, str(CHECKER)], cwd=ROOT, text=True, capture_output=True, check=False)
if baseline.returncode == 0 and "receivers=7 sources=555 routes=615" in baseline.stdout:
    ok("offline retained baseline qualifies")
else:
    bad("offline retained baseline qualifies", baseline.stderr.strip())

summary = {
    "schema": census["schema"], "receivers": len(census["receivers"]),
    "sources": len(census["sourceIdentities"]), "deduplicatedBlobs": len(census["sourceBlobDigests"]),
    "routes": len(census["writerRoutes"]), "dependencies": len(census["callableDependencies"]),
    "effects": {name: sum(row["effectClass"] == name for row in census["writerRoutes"]) for name in EXPECTED["effects"]},
}
ok("independent population summary agrees") if summary == EXPECTED else bad("independent population summary agrees", repr(summary))

refuses("missing receiver reds", lambda c, e, b: c["receivers"].pop())
refuses("duplicate receiver reds", lambda c, e, b: c["receivers"].append(copy.deepcopy(c["receivers"][0])))
refuses("reordered receiver reds", lambda c, e, b: c["receivers"].reverse())
refuses("missing source reds", lambda c, e, b: c["sourceIdentities"].pop())
refuses("reordered source reds", lambda c, e, b: c["sourceIdentities"].reverse())
refuses("truncated full manifest reds", lambda c, e, b: e["receivers"][0]["manifest"].pop())
refuses("wrong repository reds", lambda c, e, b: c["receivers"][0].update(repository="FS-GG/wrong"))
refuses("wrong revision reds", lambda c, e, b: c["receivers"][0].update(revision="0" * 40))
refuses("wrong tree reds", lambda c, e, b: c["receivers"][0].update(tree="0" * 40))

def alter_blob(c, e, b):
    b["blobs"][0]["bytesBase64"] = base64.b64encode(b"changed").decode()
refuses("changed retained bytes red", alter_blob)
refuses("removed helper route reds", lambda c, e, b: c["writerRoutes"].pop())

def hide_delegate(c, e, b):
    row = next(row for row in c["writerRoutes"] if row["receiver"] == "sdd" and "kit-materialize" in row["entrypoint"] and row["effectClass"] == "conditional")
    row["effectClass"] = "read-only"
refuses("read-only caller cannot hide delegated writer", hide_delegate)

def remove_dispatch(c, e, b):
    index = next(index for index, row in enumerate(c["callableDependencies"]) if row["receiver"] == "rendering" and row["reference"] == "5fed2838f9ed085ffca09f4cc18b4f7bc59c1294")
    c["callableDependencies"].pop(index)
refuses("missing historical dispatch target reds", remove_dispatch)

refuses("current metadata cannot replace legacy source", lambda c, e, b: c["installedTools"].__setitem__(0, copy.deepcopy(c["installedTools"][2])))

def launder_admin(c, e, b):
    next(row for row in c["writerRoutes"] if row["effectClass"] == "protected-admin")["effectClass"] = "inert"
refuses("merge publish or admin cannot be laundered", launder_admin)
refuses("source snapshot cannot claim installed", lambda c, e, b: c["claims"].update(installed=True))

def unresolved_mutable(c, e, b):
    next(row for row in c["callableDependencies"] if row["resolution"] == "observed-mutable")["resolution"] = "mutable-source-reference"
refuses("unresolved mutable FS-GG dependency reds", unresolved_mutable)
refuses("telemetry remains local-only without source read", lambda c, e, b: c["telemetryBoundary"].update(sourceRead=True))

checker_source = CHECKER.read_text(encoding="utf-8")
if not any(token in checker_source for token in ("import subprocess", "import urllib", "import requests", "gh api")):
    ok("offline validator has no network or regeneration path")
else:
    bad("offline validator has no network or regeneration path", "network-capable token present")

collector_source = COLLECTOR.read_text(encoding="utf-8")
if '["gh", "api", "--method", "GET", endpoint]' in collector_source and 'add_argument("--endpoint"' not in collector_source and 'add_argument("--method"' not in collector_source:
    ok("collector exposes only reviewed fixed GET routes")
else:
    bad("collector exposes only reviewed fixed GET routes", "method or endpoint surface widened")
arbitrary = subprocess.run([sys.executable, str(COLLECTOR), "--output", "/tmp/not-written.json", "--endpoint", "repos/other"], cwd=ROOT, text=True, capture_output=True, check=False)
if arbitrary.returncode != 0 and "unrecognized arguments" in arbitrary.stderr:
    ok("arbitrary dependency expansion is refused before collection")
else:
    bad("arbitrary dependency expansion is refused before collection", arbitrary.stderr.strip())

print(f"\nv1 receiver census fixture: {passed} passed, {failed} failed")
raise SystemExit(0 if failed == 0 else 1)
