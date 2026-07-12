#!/usr/bin/env bash
# Fixture for scripts/check-required-contexts.py (.github#549, epic #266).
#
# check-required-contexts.py cannot be wired into CI — reading branch protection needs
# `administration: read`, which is not a valid GITHUB_TOKEN `permissions:` scope and which the org's
# dispatch App does not hold either (.github#463). It is therefore an ADMIN-RUN VERIFIER, and a tool
# nothing runs is a tool that rots. Per epic #266's ratified rule, A GATE THAT CANNOT FAIL ON ITS
# SUBJECT IS NOT A GATE — so this fixture drives it against the shapes the org actually has, on every
# PR that touches it, and asserts BOTH directions.
#
# The gate that DOES run in CI, without any credential, is reusable-job-id-coherence.yml
# (tests/reusable-job-ids/): it catches the rename here, on the PR that would cause the outage.
#
# Offline. `gh` is stubbed on PATH and FAILS LIKE THE REAL ONE — a 404 is an answer ("not
# protected"), a 403 is a permission a human must grant, and a rate limit is neither — so the
# fail-closed legs exercise the transport rather than a convenience shim.
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: the .github#266 vacuous-failure defect was a "must fail" test whose non-zero exit
# came from a path guard rather than from the thing under test.
#
# THE HEADLINE LEG is the deadlock itself, built from the REAL files: FS.GG.Audio's actual gate.yml
# caller, this repo's actual lock-range-coherence.yml callee, and Audio's actual required contexts —
# with the callee's `lock-ranges:` job renamed, which is the ordinary-looking refactor that would
# stop every PR in FS.GG.Audio from merging, forever, with no commit in Audio changed.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-required-contexts.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
FIX="$HERE/fixtures"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/required-contexts-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1
unset GITHUB_TOKEN GH_TOKEN || true   # the gate must not fall back to ambient credentials

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# ---------------------------------------------------------------------------------------------
# The `gh` stub. Serves a WORLD directory:
#   $WORLD/protection/<owner>__<repo>__<branch>.json   branch protection payload
#   $WORLD/refs/<ref>/<file>.yml                       a callee at a pinned ref
#   $WORLD/protection/<slug>__<branch>.forbidden       403 — the token may not read protection
#   $WORLD/protection/<slug>__<branch>.unreachable     rate limit / outage — never a verdict
# An absent protection file = 404 = "the branch is not protected", which is an ANSWER.
# ---------------------------------------------------------------------------------------------
STUB="$WORK/stub"; mkdir -p "$STUB"
cat > "$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
path=""
for a in "$@"; do case "$a" in repos/*) path="$a";; esac; done

notfound()  { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
forbidden() { echo "gh: Resource not accessible by integration (HTTP 403)" >&2; exit 1; }
apifail()   { echo "gh: API rate limit exceeded for installation (HTTP 500)" >&2; exit 1; }

rest="${path#repos/}"
case "$rest" in
  */branches/*/protection)
    repo="${rest%%/branches/*}"; branch="${rest#*/branches/}"; branch="${branch%/protection}"
    slug="${repo//\//__}__${branch}"
    [ -e "$WORLD/protection/$slug.forbidden" ]   && forbidden
    [ -e "$WORLD/protection/$slug.unreachable" ] && apifail
    f="$WORLD/protection/$slug.json"
    [ -f "$f" ] || notfound
    cat "$f"
    ;;
  */contents/.github/workflows/*)
    file="${rest##*/contents/.github/workflows/}"
    ref="${file#*\?ref=}"; [ "$ref" = "$file" ] && ref=""
    file="${file%%\?*}"
    f="$WORLD/refs/$ref/$file"
    [ -f "$f" ] || notfound
    cat "$f"
    ;;
  *) notfound ;;
esac
STUB
chmod +x "$STUB/gh"

run() {  # run <world> <root> <repo> [args…]
  local world="$1" root="$2" repo="$3"; shift 3
  PATH="$STUB:$PATH" WORLD="$world" FSGG_CONTEXT_TRIES=1 FSGG_CONTEXT_RETRY_DELAY=0 \
    python3 "$TOOL" --repo "$repo" --root "$root" "$@" 2>&1
}

expect() {  # expect <name> <want-rc> <needle> <world> <root> <repo> [args…]
  local name="$1" want="$2" needle="$3"; shift 3
  local out rc=0
  out="$(run "$@")" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

protect() {  # protect <world> <repo> <branch> <context…>  — an Actions-produced required check set
  local world="$1" repo="$2" branch="$3"; shift 3
  mkdir -p "$world/protection"
  local checks=""
  for c in "$@"; do checks="$checks{\"context\":\"$c\",\"app_id\":15368},"; done
  printf '{"required_status_checks":{"strict":false,"checks":[%s]}}' "${checks%,}" \
    > "$world/protection/${repo//\//__}__${branch}.json"
}

# =============================================================================================
# 1. THE DEADLOCK — the real Audio caller, the real callee, the real required contexts.
# =============================================================================================
W="$WORK/w-real"; R="$WORK/r-audio"; mkdir -p "$R/.github/workflows" "$W/refs/main"
cp "$FIX/audio-gate.yml" "$R/.github/workflows/gate.yml"
protect "$W" FS-GG/FS.GG.Audio main "Build + test (locked restore, net10.0, headless)" "lock-ranges / lock-ranges"

# (a) The callee AS IT IS TODAY: the job is still `lock-ranges:`. Audio merges.
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$W/refs/main/lock-range-coherence.yml"
expect "REAL FS.GG.Audio, with this repo's REAL callee: every required context is producible" \
  0 "ok: every required context is producible" "$W" "$R" FS-GG/FS.GG.Audio

# (b) Rename the callee's job id — the ordinary-looking refactor. Audio deadlocks.
python3 - "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" \
           "$W/refs/main/lock-range-coherence.yml" <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8").read()
renamed = re.sub(r"^  lock-ranges:$", "  check-lock-ranges:", text, flags=re.M)
assert renamed != text, "lock-range-coherence.yml has no `lock-ranges:` job — fix the fixture"
open(sys.argv[2], "w", encoding="utf-8").write(renamed)
PY
expect "REGRESSION #549: renaming the callee's job id deadlocks FS.GG.Audio" \
  1 "REQUIRES the status check 'lock-ranges / lock-ranges'" "$W" "$R" FS-GG/FS.GG.Audio
expect "REGRESSION: and it names the consequence — every PR hangs, with no commit in Audio changed" \
  1 "waiting for status to be reported" "$W" "$R" FS-GG/FS.GG.Audio
expect "REGRESSION: and it points at the callee's renamed job, not at Audio" \
  1 "the callee's JOB ID has changed" "$W" "$R" FS-GG/FS.GG.Audio
expect "REGRESSION: and it shows what the job DOES produce now" \
  1 "'lock-ranges / check-lock-ranges'" "$W" "$R" FS-GG/FS.GG.Audio

# (c) A plain TYPO in the protection setting is the same outage, and is caught identically.
WT="$WORK/w-typo"; mkdir -p "$WT/refs/main"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$WT/refs/main/lock-range-coherence.yml"
protect "$WT" FS-GG/FS.GG.Audio main "lock-ranges / lock-range"     # missing trailing 's'
expect "a MISSPELLED required context is the same deadlock, and is caught" \
  1 "REQUIRES the status check 'lock-ranges / lock-range'" "$WT" "$R" FS-GG/FS.GG.Audio

# =============================================================================================
# 2. How a context is NAMED. Get this wrong and the gate lies in both directions.
# =============================================================================================
mkwf() { mkdir -p "$(dirname "$1")"; cat > "$1"; }

RN="$WORK/r-name"; WN="$WORK/w-name"
mkwf "$RN/.github/workflows/g.yml" <<'YML'
name: g
on: { pull_request: }
jobs:
  build-test:
    name: Build + test (locked restore, net10.0, headless)
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
  bare:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
protect "$WN" FS-GG/R main "Build + test (locked restore, net10.0, headless)" "bare"
expect "a job's \`name:\` is its context; a job without one uses its ID" \
  0 "ok: every required context is producible" "$WN" "$RN" FS-GG/R

# The job ID is NOT the context when a `name:` is present — requiring the id deadlocks.
WNB="$WORK/w-name-bad"; protect "$WNB" FS-GG/R main "build-test"
expect "requiring the job ID of a job that has a \`name:\` is a deadlock, and is caught" \
  1 "REQUIRES the status check 'build-test'" "$WNB" "$RN" FS-GG/R

# A matrix job's contexts carry the ` (v1, v2)` suffix, in declaration order.
RM="$WORK/r-matrix"; WM="$WORK/w-matrix"
mkwf "$RM/.github/workflows/g.yml" <<'YML'
name: g
on: { pull_request: }
jobs:
  test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
        tfm: ["net10.0"]
    steps: [{ run: 'true' }]
YML
protect "$WM" FS-GG/R main "test (ubuntu-latest, net10.0)" "test (windows-latest, net10.0)"
expect "a matrix job's contexts are enumerated with their \` (v1, v2)\` suffixes" \
  0 "ok: every required context is producible" "$WM" "$RM" FS-GG/R

WMB="$WORK/w-matrix-bad"; protect "$WMB" FS-GG/R main "test"
expect "requiring a matrix job's BARE name is a deadlock — it never reports under that name" \
  1 "REQUIRES the status check 'test'" "$WMB" "$RM" FS-GG/R

# An `include:`/`exclude:` matrix cannot be enumerated exactly. Guessing a producible set that
# happened to contain the required context would be a VACUOUS GREEN over a deadlocked repo.
RMI="$WORK/r-matrix-include"; WMI="$WORK/w-matrix-include"
mkwf "$RMI/.github/workflows/g.yml" <<'YML'
name: g
on: { pull_request: }
jobs:
  test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        os: [ubuntu-latest]
        include:
          - os: macos-latest
    steps: [{ run: 'true' }]
YML
protect "$WMI" FS-GG/R main "test (ubuntu-latest)"
expect "an \`include:\` matrix is exit 3 — the gate refuses to guess a producible set" \
  3 "cannot be derived exactly" "$WMI" "$RMI" FS-GG/R

# Reusable calls NEST. A callee that itself calls one produces `a / b / c`.
RNEST="$WORK/r-nest"; WNEST="$WORK/w-nest"; mkdir -p "$WNEST/refs/main"
mkwf "$RNEST/.github/workflows/g.yml" <<'YML'
name: g
on: { pull_request: }
jobs:
  outer:
    uses: FS-GG/.github/.github/workflows/mid.yml@main
YML
mkwf "$WNEST/refs/main/mid.yml" <<'YML'
name: mid
on: { workflow_call: }
jobs:
  middle:
    uses: FS-GG/.github/.github/workflows/inner.yml@main
YML
mkwf "$WNEST/refs/main/inner.yml" <<'YML'
name: inner
on: { workflow_call: }
jobs:
  innermost:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
protect "$WNEST" FS-GG/R main "outer / middle / innermost"
expect "nested reusable calls produce a nested context — every level's job id is API" \
  0 "ok: every required context is producible" "$WNEST" "$RNEST" FS-GG/R

# An EXPRESSION in a job's `name:` resolves at run time, from values this gate cannot see. Deriving
# the literal `Build ${{ matrix.os }}` would yield a context that can never match the real one — and
# the gate would then announce, at exit 1, that the repo is DEADLOCKED and every PR will hang. A
# confident, alarming, wrong finding is worse than no verdict.
RE_="$WORK/r-expr"; WE_="$WORK/w-expr"
mkwf "$RE_/.github/workflows/g.yml" <<'YML'
name: g
on: { pull_request: }
jobs:
  build:
    name: Build ${{ matrix.os }}
    runs-on: ubuntu-latest
    strategy:
      matrix:
        os: [ubuntu-latest]
    steps: [{ run: 'true' }]
YML
protect "$WE_" FS-GG/R main "Build ubuntu-latest (ubuntu-latest)"
expect "an EXPRESSION in a job's \`name:\` is exit 3 — never a false 'your repo is deadlocked'" \
  3 "contains an expression" "$WE_" "$RE_" FS-GG/R

# =============================================================================================
# 3. A context produced only on `push` can NEVER report on a PR — the message must say which.
# =============================================================================================
RP="$WORK/r-push"; WP="$WORK/w-push"
mkwf "$RP/.github/workflows/g.yml" <<'YML'
name: g
on:
  push:
    branches: [main]
jobs:
  only-on-push:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
protect "$WP" FS-GG/R main "only-on-push"
expect "a required context produced only on \`push\` never reports on a PR — caught, and named" \
  1 "does not trigger on \`pull_request\`" "$WP" "$RP" FS-GG/R

# =============================================================================================
# 4. Fail closed. "I could not check" is never green, and never a finding either (#266/#320/#335).
# =============================================================================================
# NOT PROTECTED (404) is an ANSWER, not a failure. It requires nothing, so nothing can deadlock.
WU="$WORK/w-unprotected"; mkdir -p "$WU/protection"
expect "an UNPROTECTED branch requires nothing — exit 0, and it says so without claiming the gates are green" \
  0 "requires NO status checks" "$WU" "$RN" FS-GG/R

# 403 is a PERMISSION a human must grant. Retrying will not help, and it must never read as green.
WF="$WORK/w-forbidden"; mkdir -p "$WF/protection"; : > "$WF/protection/FS-GG__R__main.forbidden"
expect "a 403 on protection is exit 3 — the token lacks admin rights" \
  3 "THIS TOKEN DOES NOT HAVE IT" "$WF" "$RN" FS-GG/R
expect "...and it does NOT advise the impossible fix: \`administration\` is not a valid workflow scope" \
  3 "is NOT a valid \`permissions:\` scope" "$WF" "$RN" FS-GG/R

# A rate limit / outage is RETRYABLE — never green, never a finding about somebody's protection.
WR="$WORK/w-unreachable"; mkdir -p "$WR/protection"; : > "$WR/protection/FS-GG__R__main.unreachable"
expect "an unreadable API is exit 2 (RETRYABLE) — not green, not a finding" \
  2 "no verdict" "$WR" "$RN" FS-GG/R

# A callee that 404s at the pinned ref: the call cannot start, so its contexts can never report.
WM404="$WORK/w-callee-404"; mkdir -p "$WM404/refs/main"
protect "$WM404" FS-GG/R main "outer / middle / innermost"
expect "a callee missing at the pinned ref is exit 3 — nothing it would name can ever report" \
  3 "has no .github/workflows/mid.yml at ref main" "$WM404" "$RNEST" FS-GG/R

# A third-party app's context is not derivable from this repo's YAML. Do not cry wolf about it...
WA="$WORK/w-thirdparty"; mkdir -p "$WA/protection"
printf '{"required_status_checks":{"checks":[{"context":"bare","app_id":15368},{"context":"codecov/patch","app_id":254}]}}' \
  > "$WA/protection/FS-GG__R__main.json"
expect "a NON-Actions required context is skipped, not flagged — the gate cannot see that producer" \
  0 "skip codecov/patch" "$WA" "$RN" FS-GG/R

# ...but if EVERY required context is a third party's, this gate audited nothing, and examining
# nothing is a failure to audit, not a clean audit.
WA2="$WORK/w-all-thirdparty"; mkdir -p "$WA2/protection"
printf '{"required_status_checks":{"checks":[{"context":"codecov/patch","app_id":254}]}}' \
  > "$WA2/protection/FS-GG__R__main.json"
expect "if EVERY required context is a third party's, that is exit 3 — never a vacuous green" \
  3 "audited nothing" "$WA2" "$RN" FS-GG/R

# The LEGACY protection shape (`contexts: [str]`, no app attribution) must still be audited, not
# silently skipped — a repo on the old shape is exactly as deadlockable as one on the new.
WL="$WORK/w-legacy"; mkdir -p "$WL/protection"
printf '{"required_status_checks":{"contexts":["ghost"]}}' > "$WL/protection/FS-GG__R__main.json"
expect "the LEGACY \`contexts:\` shape is still audited — it deadlocks identically" \
  1 "REQUIRES the status check 'ghost'" "$WL" "$RN" FS-GG/R

# A workflow that is NOT PR-triggered cannot deadlock a PR, whatever is wrong with it. Deriving its
# contexts is a convenience — it only sharpens a message — so a broken or unfetchable callee THERE
# must never cost the gate its verdict on the contexts that DO matter. Here `release.yml` calls a
# callee that does not exist at its ref; the PR contexts are all fine, and the gate must still say so.
RNPR="$WORK/r-nonpr-broken"; WNPR="$WORK/w-nonpr-broken"; mkdir -p "$WNPR/refs/main"
mkwf "$RNPR/.github/workflows/g.yml" <<'YML'
name: g
on: { pull_request: }
jobs:
  bare:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
mkwf "$RNPR/.github/workflows/release.yml" <<'YML'
name: release
on: { push: { tags: ['v*'] } }
jobs:
  publish:
    uses: FS-GG/.github/.github/workflows/does-not-exist.yml@main
YML
protect "$WNPR" FS-GG/R main "bare"
expect "a broken callee in a NON-PR workflow does not cost the verdict on the PR contexts" \
  0 "ok: every required context is producible" "$WNPR" "$RNPR" FS-GG/R

RBAD="$WORK/r-unparsable"; mkwf "$RBAD/.github/workflows/g.yml" <<'YML'
name: g
on: { pull_request: }
jobs:
  j:
   - broken: [
YML
expect "an unparsable workflow is exit 3 — not a finding about a required context" \
  3 "not parsable as YAML" "$WN" "$RBAD" FS-GG/R

# =============================================================================================
# 5. WHY THIS TOOL IS NOT WIRED TO A WORKFLOW — and the regression guard that keeps it that way.
#
# This script needs to read `branches/<b>/protection`, which requires `administration: read`. That
# is NOT a valid `permissions:` scope for a workflow's GITHUB_TOKEN: declaring it is a workflow
# validation error, and the run dies at STARTUP — producing no check run at all, so it shows as
# neither red nor green (the #478 startup_failure blind spot). #549's first attempt shipped exactly
# that and was caught only by reading the workflow-run list rather than the check-run list.
#
# The org's dispatch App does not hold the scope either — .github#463 learned this when
# coordination-propagate's protection probe 403'd on every receiver and had to be rewritten to ask
# the pull request instead.
#
# So no workflow here may declare it, ever. The preventive gate that CAN run without a credential is
# reusable-job-id-coherence.yml (tests/reusable-job-ids/); this script stays an admin-run verifier.
# =============================================================================================
if python3 - "$REPO_ROOT" <<'PY'
import glob, os, sys, yaml
root = sys.argv[1]
offenders = []
for path in sorted(glob.glob(os.path.join(root, ".github/workflows/*.yml"))):
    doc = yaml.safe_load(open(path, encoding="utf-8")) or {}
    scopes = [doc.get("permissions")] + [j.get("permissions") for j in (doc.get("jobs") or {}).values()
                                         if isinstance(j, dict)]
    for s in scopes:
        if isinstance(s, dict) and "administration" in s:
            offenders.append(os.path.basename(path))
assert not offenders, (
    f"{sorted(set(offenders))} declare `permissions: administration:`, which is not a valid "
    f"GITHUB_TOKEN scope. The workflow will not validate and the run will die at startup — "
    f"producing NO check run, so it reads as neither red nor green."
)
PY
then ok "no workflow declares \`permissions: administration:\` — the invalid scope that startup_failures a run, and cannot be reintroduced"
else bad "a workflow declares \`permissions: administration:\` — it will die at startup and show as neither red nor green"
fi

echo
echo "required-contexts fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::required-contexts fixture FAILED"; exit 1; }
echo "required-contexts fixture — OK"
