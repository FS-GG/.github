#!/usr/bin/env bash
# Fixture for scripts/check-required-contexts.py (.github#549, epic #266).
#
# required-context-coherence.yml is a REUSABLE workflow: it runs in the receivers' repos, not here,
# so nothing in this repo would otherwise ever execute this script. Per epic #266's ratified rule,
# A GATE THAT CANNOT FAIL ON ITS SUBJECT IS NOT A GATE — so the fixture drives it against the shapes
# the org actually has, and asserts BOTH directions.
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
expect "a 403 on protection is exit 3, and names the permission the CALLER must grant" \
  3 "administration: read" "$WF" "$RN" FS-GG/R
expect "...and it explains that a callee cannot request what its caller withheld (#478)" \
  3 "a callee cannot request a permission its caller withheld" "$WF" "$RN" FS-GG/R

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
# 5. The gate's own shipped surface.
# =============================================================================================
if python3 - "$REPO_ROOT/.github/workflows/required-context-coherence.yml" <<'PY'
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
on = d.get("on", d.get(True))
assert "workflow_call" in on, "required-context-coherence.yml is not a reusable workflow"
perms = d.get("permissions") or {}
assert perms.get("administration") == "read", f"the callee must DECLARE administration: read: {perms}"
assert perms.get("contents") == "read", f"the callee must declare contents: read: {perms}"
body = "".join(str(s.get("run", "")) for j in d["jobs"].values() for s in j.get("steps", []))
assert "check-required-contexts.py" in body, "the gate workflow never runs the gate"
for jid, j in d["jobs"].items():
    assert isinstance(j.get("timeout-minutes"), int), f"job {jid} does not bound itself (#541)"
PY
then ok "the shipped required-context-coherence.yml is reusable, declares administration+contents: read, bounds its jobs, and runs the gate"
else bad "the shipped required-context-coherence.yml is not the shape this fixture asserts"
fi

echo
echo "required-contexts fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::required-contexts fixture FAILED"; exit 1; }
echo "required-contexts fixture — OK"
