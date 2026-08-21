#!/usr/bin/env bash
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
ARTIFACT="${1:-$ROOT/work/2797-review-escalation-claim-turnover/artifacts/2797-reviewwait.xml}"
EXPECTED_SHA="713860372ba9ae1a3dfb4cda5f40e12562ea0cc3db7c63edee7c29475ab6cecc"

if [ ! -f "$ARTIFACT" ]; then
  echo "reviewwait evidence: missing artifact: $ARTIFACT" >&2
  exit 1
fi

ACTUAL_SHA="$(sha256sum "$ARTIFACT" | awk '{print $1}')"
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
  echo "reviewwait evidence: digest mismatch: expected $EXPECTED_SHA, got $ACTUAL_SHA" >&2
  exit 1
fi

python3 - "$ARTIFACT" <<'PY'
import sys
import xml.etree.ElementTree as ET

artifact = sys.argv[1]
root = ET.parse(artifact).getroot()
counters = next((node for node in root.iter() if node.tag.endswith("Counters")), None)
if counters is None:
    raise SystemExit("reviewwait evidence: missing ResultSummary/Counters")

expected = {"total": "19", "executed": "19", "passed": "19", "failed": "0", "error": "0"}
actual = {key: counters.attrib.get(key) for key in expected}
if actual != expected:
    raise SystemExit(f"reviewwait evidence: unexpected counters: expected {expected}, got {actual}")

results = [node for node in root.iter() if node.tag.endswith("UnitTestResult")]
if len(results) != 19 or any(node.attrib.get("outcome") != "Passed" for node in results):
    raise SystemExit("reviewwait evidence: expected exactly 19 passing UnitTestResult entries")

print("reviewwait evidence: immutable receipt verified (19 passed, 0 failed)")
PY
