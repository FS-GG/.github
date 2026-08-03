#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
case_run() {
  local name="$1" expected="$2" facts="$3"
  printf '%s' "$facts" > "$work/$name.json"
  actual="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/$name.json" --json | python3 -c 'import json,sys; print(json.load(sys.stdin)["action"])')"
  [ "$actual" = "$expected" ] || { echo "$name: expected $expected, got $actual" >&2; exit 1; }
}
base='"provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.27.0","tagExists":false'
case_run eligible tag "{\"version\":\"0.27.1\",$base}"
case_run major refuse "{\"version\":\"1.0.0\",$base}"
case_run partial stickyEscalate "{\"version\":\"0.27.1\",${base/\"orgFeed\":\"absent\"/\"orgFeed\":\"present\"},\"nugetFeed\":\"absent\"}"
case_run older-gap refuse '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.28.0","nugetLatest":"0.28.0","tagExists":false}'
case_run frontier-disagree stickyEscalate '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.28.0","tagExists":false}'
case_run unrelated-later-pr refuse '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.0","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.27.0","tagExists":false}'
facts='{ "version":"0.27.1", "provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"}, "orgFeed":"unknown", "nugetFeed":"absent", "orgLatest":"0.27.0", "nugetLatest":"0.27.0", "tagExists":false }'
printf '%s' "$facts" > "$work/escalate.json"
printf '%s' '{"streak":2,"action":"refuse","reason":"feed-observation-unknown","version":"0.27.1","lastRun":"1"}' > "$work/previous.json"
state="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/escalate.json" --previous-escalation "$work/previous.json" --run 2 | jq -c .escalation)"
[ "$(jq -r .streak <<<"$state")" = 2 ] || { echo 'duplicate run did not stay idempotent' >&2; exit 1; }
printf '%s' '{"streak":2,"action":"refuse","reason":"other","version":"0.27.1","lastRun":"1"}' > "$work/previous.json"
state="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/escalate.json" --previous-escalation "$work/previous.json" --run 3 | jq -c .escalation)"
[ "$(jq -r .streak <<<"$state")" = 3 ] || { echo 'transition did not increment streak' >&2; exit 1; }
printf '%s' 'not-json' > "$work/previous.json"
state="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/escalate.json" --previous-escalation "$work/previous.json" --run 4 | jq -c .escalation)"
[ "$(jq -r .valid <<<"$state")" = false ] || { echo 'malformed marker did not fail closed' >&2; exit 1; }
echo 'kit auto-publish state machine: 10 passed'
