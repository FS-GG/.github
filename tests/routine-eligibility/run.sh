#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/routine-eligibility.yml"
SELFTEST="$ROOT/.github/workflows/routine-eligibility-selftest.yml"
ENVELOPE="$ROOT/scripts/check-routine-eligibility-envelope.py"
ACTIVATION="$ROOT/.fsgg/routine-eligibility-activation.json"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/routine-eligibility.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

require() {
  grep -qF "$1" "$2" || { echo "missing security invariant in $2: $1" >&2; exit 1; }
}

require 'pull_request_target:' "$WORKFLOW"
require 'contents: read' "$WORKFLOW"
require 'name: routine-eligibility' "$WORKFLOW"
require 'if: github.event.pull_request.base.ref == github.event.repository.default_branch' "$WORKFLOW"
require 'test "$BASE_REF" = "$DEFAULT_BRANCH"' "$WORKFLOW"
require 'test "$observed_base" = "$BASE_SHA"' "$WORKFLOW"
require 'test "$observed_head" = "$HEAD_SHA"' "$WORKFLOW"
require 'show "$BASE_SHA:scripts/check-routine-eligibility-envelope.py"' "$WORKFLOW"
require 'show "$BASE_SHA:scripts/check-claim-generation.py"' "$WORKFLOW"
require 'refs/pull/${PR_NUMBER}/head:refs/remotes/origin/routine-head' "$WORKFLOW"
require 'pull_request:' "$SELFTEST"
require 'name: routine-eligibility-fixture' "$SELFTEST"
require 'bash tests/routine-eligibility/run.sh' "$SELFTEST"

for dangerous in 'actions/checkout' 'GITHUB_TOKEN' 'bash tests/' 'curl ' 'eval ' 'source '; do
  if grep -qF "$dangerous" "$WORKFLOW"; then
    echo "trusted workflow contains forbidden executable pattern: $dangerous" >&2
    exit 1
  fi
done
if grep -qE '^ *name: routine-eligibility$' "$SELFTEST"; then
  echo "ordinary pull_request workflow must not emit the authority context" >&2
  exit 1
fi

python3 - "$ACTIVATION" <<'PY'
import json
import sys

activation = json.load(open(sys.argv[1], encoding="utf-8"))
assert activation["status"] == "inactive"
rejected = activation["rejectedRepositoryPushRule"]
assert rejected["status"] == "unsupported"
assert rejected["intent"]["refInclude"] == ["refs/heads/routine/**"]
assert rejected["intent"]["restrictedPaths"] == [".github/workflows/**"]
alternative = activation["requiredWorkflowAlternative"]
assert alternative["status"] == "planned-blocked"
request = alternative["request"]
assert request["bypass_actors"] == []
assert request["conditions"]["repository_id_and_ref_name"]["repository_id"]["repository_ids"] == [1269292704]
assert request["conditions"]["repository_id_and_ref_name"]["ref_name"]["include"] == ["refs/heads/main"]
workflow = request["rules"][0]["parameters"]["workflows"][0]
assert workflow["path"] == ".github/workflows/routine-eligibility.yml"
assert workflow["ref"] == "refs/heads/main"
assert workflow["repository_id"] == 1269292704
assert alternative["authorizationEvidence"]["missingScope"] == "admin:org"
assert activation["organization"]["plan"] == "free"
app = activation["existingAppAlternative"]
assert app["status"] == "not-currently-capable"
assert app["appSlug"] == "fs-gg-cross-repo-dispatch"
assert app["installation"]["missingRequiredPermissions"] == ["checks:write", "statuses:write"]
assert "checks" not in app["installation"]["permissions"]
assert "statuses" not in app["installation"]["permissions"]
PY

repo="$TMP/repository"
mkdir -p "$repo/scripts/lib" "$repo/.github/workflows" "$repo/.fsgg"
git -C "$repo" init -q
git -C "$repo" config user.email fixture@example.invalid
git -C "$repo" config user.name fixture
cp "$ENVELOPE" "$repo/scripts/check-routine-eligibility-envelope.py"
printf '%s\n' '#!/usr/bin/env python3' 'print("BASE-TRUSTED")' > "$repo/scripts/check-claim-generation.py"
printf '%s\n' '# base dependency' > "$repo/scripts/lib/gate.py"
printf '%s\n' '{}' > "$repo/.fsgg/routine-development.json"
cp "$WORKFLOW" "$repo/.github/workflows/routine-eligibility.yml"
git -C "$repo" add .
git -C "$repo" commit -qm base
base="$(git -C "$repo" rev-parse HEAD)"

printf '%s\n' '#!/usr/bin/env python3' 'print("CANDIDATE-CONTROLLED")' > "$repo/scripts/check-claim-generation.py"
printf '%s\n' '#!/usr/bin/env python3' 'print("CANDIDATE-ENVELOPE")' > "$repo/scripts/check-routine-eligibility-envelope.py"
git -C "$repo" rm -q .github/workflows/routine-eligibility.yml
mkdir -p "$repo/.github/workflows"
printf '%s\n' 'name: spoof' 'on: pull_request' 'jobs:' '  spoof:' '    name: routine-eligibility' > "$repo/.github/workflows/spoof.yml"
git -C "$repo" add .
git -C "$repo" commit -qm hostile-candidate
head="$(git -C "$repo" rev-parse HEAD)"

mkdir -p "$TMP/trusted/lib"
git -C "$repo" show "$base:scripts/check-routine-eligibility-envelope.py" > "$TMP/trusted/check-routine-eligibility-envelope.py"
git -C "$repo" show "$base:scripts/check-claim-generation.py" > "$TMP/trusted/check-claim-generation.py"
git -C "$repo" show "$base:scripts/lib/gate.py" > "$TMP/trusted/lib/gate.py"
printf '%s' '<!-- marker -->' > "$TMP/body.md"

args=(
  --git-dir "$repo" --repository FS-GG/.github
  --default-branch main --base-ref main --base-revision "$base" --base-sha "$base"
  --head-ref routine/example --head-revision "$head" --head-sha "$head"
  --body "$TMP/body.md" --validator "$TMP/trusted/check-claim-generation.py"
)

result="$(python3 "$TMP/trusted/check-routine-eligibility-envelope.py" "${args[@]}")"
test "$result" = BASE-TRUSTED || {
  echo "candidate workflow/context/executable replacement influenced trusted result: $result" >&2
  exit 1
}

expect_failure() {
  local name="$1" needle="$2"; shift 2
  local output rc=0
  output="$(python3 "$TMP/trusted/check-routine-eligibility-envelope.py" "$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ] || ! grep -qF "$needle" <<<"$output"; then
    echo "$name did not fail closed for $needle (exit $rc): $output" >&2
    exit 1
  fi
}

bad=("${args[@]}"); bad[7]=other
expect_failure target-ref 'is not default branch' "${bad[@]}"
bad=("${args[@]}"); bad[11]=not-a-sha
expect_failure base-shape 'base SHA is not exact' "${bad[@]}"
bad=("${args[@]}"); bad[11]="$head"
expect_failure base-binding 'fetched base does not equal' "${bad[@]}"
bad=("${args[@]}"); bad[17]="$base"
expect_failure head-binding 'fetched head does not equal' "${bad[@]}"
bad=("${args[@]}"); bad[13]=feature/not-routine
expect_failure head-ref 'head ref is not a routine branch' "${bad[@]}"

echo "routine-eligibility: target/ref/SHA boundaries and candidate workflow/context spoof fail closed"
