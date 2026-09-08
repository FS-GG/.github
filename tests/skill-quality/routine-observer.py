#!/usr/bin/env python3
"""Exercise v2 joins, independent assessments, and bounded observer inputs."""
import json, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/routine-observer.py"
HEAD, OTHER, MERGE = "a" * 40, "c" * 40, "b" * 40

def invoke(root, delivery_value, usage_value=None, economics_value=None):
    delivery_path, output = root / "delivery.json", root / "report.json"
    delivery_path.write_text(json.dumps(delivery_value))
    args = [str(TOOL), "--unit", "UTEL-01", "--delivery", str(delivery_path)]
    for flag, name, value in (("--usage", "usage.json", usage_value), ("--economics", "economics.json", economics_value)):
        if value is not None:
            path = root / name; path.write_text(json.dumps(value)); args += [flag, str(path)]
    result = subprocess.run(args + ["--output", str(output)], text=True, capture_output=True)
    assert result.returncode == 0, result.stderr
    return json.loads(output.read_text())

def delivery(**changes):
    value = {"schema":"fsgg.routine-delivery/v1", "repo":"FS-GG/.github", "pr":7,
             "expectedHead":HEAD, "observedHead":HEAD, "outcome":"delivered", "codeDelivery":"delivered",
             "publication":"not-required", "mergeCommit":MERGE, "attempts":1, "reason":None}
    value.update(changes); return value

USAGE = {"schema":"fsgg.routine-usage/1", "unit":"UTEL-01", "scope":"whole-unit",
         "freshInputTokens":40, "cachedInputTokens":40, "outputTokens":20, "reasoningTokens":5,
         "productiveTokens":80, "overheadTokens":10, "unclassifiedTokens":10}
ECONOMICS = {"schema":"fsgg.coordination.qualification-cadence-report/2", "dataStatus":"available",
             "completeness":{"complete":True, "attemptsExpected":2, "attemptsObserved":2}}

with tempfile.TemporaryDirectory(prefix="fsgg-routine-observer-") as scratch:
    root = Path(scratch)
    report = invoke(root, delivery(), USAGE, ECONOMICS)
    assert report["schema"] == "fsgg.routine-unit-economics/2"
    assert report["delivery"]["status"] == "delivered"
    assert report["joinIntegrity"]["status"] == "matched"
    assert report["recordValidity"]["status"] == "valid"
    assert report["populationCoverage"]["status"] == "unknown"
    assert report["qualification"] == {"status":"not-evaluated", "code":"population-coverage-unknown"}
    assert report["usage"]["conservativeOverheadRatio"] == .2
    assert report["usage"]["withinTenPercentCeiling"] is False
    mismatch = invoke(root, delivery(observedHead=OTHER))
    assert mismatch["joinIntegrity"] == {"status":"invalid", "code":"head-mismatch"}
    assert mismatch["delivery"]["status"] == "delivered"
    assert invoke(root, delivery(observedHead=None))["joinIntegrity"]["code"] == "observed-head-absent"
    assert invoke(root, delivery(schema="unsupported/1"))["recordValidity"]["code"] == "delivery-schema-unsupported"
    assert invoke(root, delivery(pr=True))["recordValidity"]["code"] == "delivery-fields-invalid"
    legacy_value = delivery(); legacy_value["head"] = legacy_value.pop("observedHead"); legacy_value.pop("expectedHead")
    legacy = invoke(root, legacy_value)
    assert legacy["joinIntegrity"] == {"status":"unproven", "code":"expected-head-absent"}
    assert legacy["delivery"]["status"] == "delivered"
    conflict = invoke(root, delivery(head=OTHER))
    assert conflict["recordValidity"]["code"] == "delivery-head-conflict" and conflict["joinIntegrity"]["status"] == "invalid"
    assert invoke(root, delivery(codeDelivery="unknown", outcome="indeterminate"))["delivery"]["status"] == "unknown"
    disputed = delivery(outcome="delivered-disputed", validationDisposition="reused", coherentValidation="disputed")
    disputed_report = invoke(root, disputed)
    assert disputed_report["delivery"]["status"] == "delivered"
    assert disputed_report["recordValidity"]["status"] == "valid"
    assert invoke(root, delivery(validationDisposition="reused", coherentValidation="bogus"))["recordValidity"]["code"] == "delivery-validation-fields-invalid"
    cases = [
        (dict(USAGE, reasoningTokens=21), "usage-accounting-invalid"),
        (dict(USAGE, unit="other"), "usage-identity-invalid"),
        (dict(USAGE, outputTokens=True), "usage-count-invalid"),
        (dict(USAGE, overheadTokens=-1), "usage-count-invalid"),
        (dict(USAGE, productiveTokens=79), "usage-accounting-invalid"),
    ]
    for value, code in cases: assert invoke(root, delivery(), value)["usage"]["code"] == code
    bad = dict(ECONOMICS, completeness={"complete":True, "attemptsExpected":2, "attemptsObserved":1})
    assert invoke(root, delivery(), USAGE, bad)["economics"]["code"] == "economics-completeness-invalid"
    malformed_path, malformed_output = root / "malformed.json", root / "malformed-report.json"
    malformed_path.write_text("not-json")
    malformed_run = subprocess.run([str(TOOL), "--unit", "UTEL-01", "--delivery", str(malformed_path),
                                    "--output", str(malformed_output)], capture_output=True)
    assert malformed_run.returncode == 0
    assert json.loads(malformed_output.read_text())["recordValidity"]["code"] == "input-json-invalid"
print("routine-observer: v2 identity joins and four independent assessments pass")
