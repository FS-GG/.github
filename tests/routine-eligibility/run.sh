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
require 'test "$observed_base" != "$BASE_SHA"' "$WORKFLOW"
require 'merge-base --is-ancestor "$BASE_SHA" "$observed_base"' "$WORKFLOW"
require 'merge-base --is-ancestor "$HEAD_SHA" "$observed_base"' "$WORKFLOW"
require 'test "$observed_head" = "$HEAD_SHA"' "$WORKFLOW"
require 'show "$observed_base:scripts/check-routine-eligibility-envelope.py"' "$WORKFLOW"
require 'show "$observed_base:scripts/check-claim-generation.py"' "$WORKFLOW"
require 'show "$observed_base:scripts/lib/gate.py"' "$WORKFLOW"
# The event base is only a monotonic floor: no executable may be loaded from it.
if grep -qF 'show "$BASE_SHA:' "$WORKFLOW"; then
  echo "workflow loads executable bytes from the (possibly stale) event base" >&2
  exit 1
fi
require 'refs/pull/${PR_NUMBER}/head:refs/remotes/origin/routine-head' "$WORKFLOW"
# Push parent (#5189): only GitHub's own `synchronize` `before`, only after proven ancestry.
require 'EVENT_BEFORE: ${{ github.event.before }}' "$WORKFLOW"
require 'if test "${EVENT_ACTION:-}" = synchronize' "$WORKFLOW"
require 'merge-base --is-ancestor "$EVENT_BEFORE" refs/remotes/origin/routine-head' "$WORKFLOW"
require 'previous_args=(--previous-head "$previous_head")' "$WORKFLOW"
require 'pull_request:' "$SELFTEST"
require 'name: routine-eligibility-fixture' "$SELFTEST"
require 'bash tests/routine-eligibility/run.sh' "$SELFTEST"

for dangerous in 'actions/checkout' 'bash tests/' 'curl ' 'eval ' 'source '; do
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
assert activation["status"] == "candidate"
rejected = activation["rejectedRepositoryPushRule"]
assert rejected["status"] == "unsupported"
assert rejected["intent"]["refInclude"] == ["refs/heads/routine/**"]
assert rejected["intent"]["restrictedPaths"] == [".github/workflows/**"]
alternative = activation["requiredWorkflowAlternative"]
assert alternative["status"] == "optional-hardening-blocked"
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
threat = activation["acceptedThreatModel"]
assert threat["repositoryWriters"] == "trusted"
assert "does not claim adversarial protection" in threat["nonGuarantee"]
app = activation["existingAppAlternative"]
assert app["status"] == "optional-hardening-not-currently-capable"
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
  echo "candidate executable replacement influenced the base-loaded result: $result" >&2
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
expect_failure base-binding 'is not an ancestor of live base' "${bad[@]}"
bad=("${args[@]}"); bad[17]="$base"
expect_failure head-binding 'fetched head does not equal' "${bad[@]}"
bad=("${args[@]}"); bad[13]=feature/not-routine
expect_failure head-ref 'head ref is not a routine branch' "${bad[@]}"

echo "routine-eligibility: target/ref/SHA boundaries and base-loaded execution fail closed under the trusted-writer model"

# Execute the actual workflow shell against an inert fixed-origin Git transport.
ROOT="$ROOT" FIXTURE_REPO="$repo" FIXTURE_BASE="$base" FIXTURE_HEAD="$head" python3 - <<'PYFETCH'
import base64, json, os, pathlib, shutil, subprocess, tempfile
root=pathlib.Path(os.environ['ROOT'])
workflow=(root/'.github/workflows/routine-eligibility.yml').read_text()
shell='\n'.join(line[10:] for line in workflow.split('        run: |\n',1)[1].splitlines())+'\n'
with tempfile.TemporaryDirectory() as temporary:
    work=pathlib.Path(temporary); bindir=work/'bin';bindir.mkdir()
    git=bindir/'git'
    git.write_text("""#!/usr/bin/env python3
import base64,json,os,pathlib,subprocess,sys
args=sys.argv[1:];secret=os.environ.get('RECEIVER_FETCH_TOKEN','');header=os.environ.get('ROUTINE_FETCH_HEADER','')
if 'fetch' in args:
 assert args[:5]==['-c','credential.helper=','-c','http.followRedirects=false','--config-env=http.https://github.com/.extraheader=ROUTINE_FETCH_HEADER']
 assert header=='AUTHORIZATION: basic '+base64.b64encode(b'x-access-token:synthetic-fetch-only-credential').decode()
 assert not secret and all('synthetic-fetch-only-credential' not in arg and header not in arg for arg in args)
 assert os.environ.get('GIT_TERMINAL_PROMPT')=='0'
 assert args[-3:]==['origin','+refs/heads/main:refs/remotes/origin/routine-base','+refs/pull/7/head:refs/remotes/origin/routine-head']
 if os.environ['MODE']=='fetch-fail':sys.exit(9)
 target=args[args.index('-C')+1]
 origin=subprocess.check_output([os.environ['REAL_GIT'],'-C',target,'remote','get-url','origin'],text=True).strip()
 assert origin=='https://github.com/'+os.environ['REPOSITORY']+'.git'
 base=os.environ['FIXTURE_BASE'];head=os.environ['FIXTURE_HEAD']
 if os.environ['MODE']=='base-drift':base=head
 if os.environ['MODE']=='head-drift':head=base
 sys.exit(subprocess.call([os.environ['REAL_GIT'],'-C',target,'fetch','--quiet','--no-tags',os.environ['FIXTURE_REPO'],base+':refs/remotes/origin/routine-base',head+':refs/remotes/origin/routine-head']))
if 'fetch' not in args:
 assert not secret and not header, 'credential escaped fetch'
if 'show' in args:
 config=pathlib.Path(args[args.index('-C')+1],'.git/config').read_text()
 assert 'AUTHORIZATION' not in config and 'extraheader' not in config
if 'remote' in args and 'add' in args:assert args[-1]=='https://github.com/'+os.environ['REPOSITORY']+'.git'
sys.exit(subprocess.call([os.environ['REAL_GIT'],*args]))
""")
    git.chmod(0o700)
    synthetic='synthetic-fetch-only-credential'
    for mode in ['good','public','missing-token','fetch-fail','base-drift','head-drift']:
        target=work/mode;target.mkdir()
        env=dict(os.environ,PATH=str(bindir)+os.pathsep+os.environ['PATH'],REAL_GIT=shutil.which('git'),
                 GIT_TRACE2_EVENT=str(target/'forbidden-trace.json'),GIT_TRACE2_ENV_VARS='ROUTINE_FETCH_HEADER,RECEIVER_FETCH_TOKEN',
                 RUNNER_TEMP=str(target),MODE=mode,RECEIVER_FETCH_TOKEN='' if mode=='missing-token' else synthetic,
                 REPOSITORY='FS-GG/public-fixture' if mode=='public' else 'FS-GG/private-fixture',PR_NUMBER='7',BASE_REF='main',DEFAULT_BRANCH='main',
                 BASE_SHA=os.environ['FIXTURE_BASE'],HEAD_SHA=os.environ['FIXTURE_HEAD'],HEAD_REF='routine/example',
                 PR_BODY='$(touch hostile-body-must-not-run)')
        result=subprocess.run(['bash','-c',shell],env=env,text=True,capture_output=True)
        derived=base64.b64encode(('x-access-token:'+synthetic).encode()).decode()
        visible='\n'.join(line for line in result.stdout.splitlines() if line!='::add-mask::'+derived)+result.stderr
        assert synthetic not in visible and derived not in visible, 'credential leaked'
        assert not (target/'forbidden-trace.json').exists(), 'Git trace escaped confinement'
        assert 'CANDIDATE-' not in visible and not (root/'hostile-body-must-not-run').exists()
        assert (result.returncode==0)==(mode in ('good','public')), (mode,result.returncode,visible)
        assert ('BASE-TRUSTED' in visible)==(mode in ('good','public'))
print('routine-eligibility: actual private-fetch shell confines credentials and refuses missing/fetch/drift cases')
PYFETCH

# The moved-base race: real workflow shell, real shallow transport, real envelope and validator.
python3 "$ROOT/tests/routine-eligibility/moved_base.py"

# The push-parent allowance (#5189): stale, forced, rebound, forged and unrebound-merge controls.
python3 "$ROOT/tests/routine-eligibility/push_parent.py"
