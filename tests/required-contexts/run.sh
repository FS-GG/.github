#!/usr/bin/env bash
# Fixture for scripts/check-required-contexts.py (.github#549, epic #266).
#
# check-required-contexts.py cannot run from a RECEIVER's own CI — reading branch protection needs
# `administration: read`, which is not a valid GITHUB_TOKEN `permissions:` scope, so no repo can read
# its own protection from its own workflow. It is NOT, however, an admin-run-only verifier any more:
# the org dispatch App was granted `administration: read` on 2026-07-17, and .github#1138 wired
# required-context-coherence.yml to sweep the roster nightly with a per-receiver installation token.
# (This header claimed the opposite for as long as that was false — .github#463's finding outlived the
# grant that reversed it. See #1138: fix the premise-rot in the same change that creates it.)
#
# What that sweep cannot do is fail on the PR that BREAKS the script, and a tool nothing exercises is a
# tool that rots. Per epic #266's ratified rule, A GATE THAT CANNOT FAIL ON ITS SUBJECT IS NOT A GATE —
# so this fixture drives it against the shapes the org actually has, on every PR that touches it, and
# asserts BOTH directions.
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
#   $WORLD/protection/<owner>__<repo>__<branch>.json   CLASSIC branch protection payload
#   $WORLD/refs/<ref>/<file>.yml                       a callee at a pinned ref
#   $WORLD/protection/<slug>__<branch>.forbidden       403 — the token may not read protection
#   $WORLD/protection/<slug>__<branch>.unreachable     rate limit / outage — never a verdict
#   $WORLD/rules/<owner>__<repo>__<branch>.json        RULESET rules that apply to the branch
#   $WORLD/rules/<slug>__<branch>.forbidden            403 on the rules endpoint
#   $WORLD/rules/<slug>__<branch>.notfound             404 — no such repo/branch (NOT "no rules")
#
# An absent protection file = 404 = "no CLASSIC protection". That is an answer about that STORE,
# not about the branch — a ruleset may still protect it (#574), so the stub models the two
# endpoints independently, exactly as GitHub does.
# An absent rules file = `[]` = "no rulesets apply", which is how the real endpoint answers.
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
  */rules/branches/*)
    repo="${rest%%/rules/branches/*}"; branch="${rest#*/rules/branches/}"
    slug="${repo//\//__}__${branch}"
    [ -e "$WORLD/rules/$slug.forbidden" ]   && forbidden
    [ -e "$WORLD/rules/$slug.unreachable" ] && apifail
    [ -e "$WORLD/rules/$slug.notfound" ]    && notfound
    f="$WORLD/rules/$slug.json"
    # The real endpoint answers `[]` for a branch no ruleset touches — it does NOT 404.
    [ -f "$f" ] || { echo '[]'; exit 0; }
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

ruleset() {  # ruleset <world> <repo> <branch> <context…>  — the SAME requirement, via a RULESET
  # The shape GitHub really answers on `rules/branches/<b>`: a flat list of rules, the status-check
  # one carrying `parameters.required_status_checks[]`, each naming its app as `integration_id`
  # (not `app_id` — the two stores spell it differently). Non-status rules are present and must be
  # ignored, exactly as FS.GG.Governance's real payload carries deletion/non_fast_forward/pull_request.
  local world="$1" repo="$2" branch="$3"; shift 3
  mkdir -p "$world/rules"
  local checks=""
  for c in "$@"; do checks="$checks{\"context\":\"$c\",\"integration_id\":15368},"; done
  printf '[{"type":"deletion"},{"type":"pull_request","parameters":{}},{"type":"required_status_checks","parameters":{"required_status_checks":[%s]}}]' \
    "${checks%,}" > "$world/rules/${repo//\//__}__${branch}.json"
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
# 1a2. THE SECOND KIT-BUMP CONTEXT IS PRODUCIBLE (.github#1815, AC 2)
#
# `materialize / kit-bump-shape` is green on `mechanical`, on `mechanical + repair` AND on `abstain`,
# so a required context built on it cannot express #1587's "automerge the mechanical class only" — a
# check run carries one boolean and that job reaches five verdicts. #1815 adds a SECOND context,
# `materialize / kit-bump-mechanical`, green iff the pinned rule exits 0.
#
# AC 2 is "it is producible, proved in the fixture form BEFORE any --apply", and this is that proof.
# It is worth more than a dry run: nothing is armed anywhere, and the derivation asserted here is the
# same one the nightly required-context-coherence sweep runs.
#
# BOTH REAL FILES. The caller is FS.GG.Net's ACTUAL .github/workflows/kit-materialize.yml (fetched
# 2026-07-28; its job id `materialize` is the left half of both context strings), and the callee is
# THIS WORKING TREE's kit-materialize.yml — so a rename, a `paths:` filter, or a deleted job in the
# producer fails here, in the pull request that did it. That is why the selftest workflow's `paths:`
# names kit-materialize.yml, exactly as it already names lock-range-coherence.yml.
#
# THE PROTECTION PAYLOAD USES `required_status_checks.checks`, VIA `protect` — which is what
# check-required-contexts.py reads. The legacy `contexts:` list is a fallback it only consults when
# `checks` is absent, so a payload that named the new context ONLY in `contexts` would silently audit
# one context fewer and pass for the wrong reason.
WK="$WORK/w-kit"; RK="$WORK/r-kitrecv"; mkdir -p "$RK/.github/workflows" "$WK/refs/main"
cp "$FIX/net-kit-materialize.yml" "$RK/.github/workflows/kit-materialize.yml"
cp "$REPO_ROOT/.github/workflows/kit-materialize.yml" "$WK/refs/main/kit-materialize.yml"
protect "$WK" FS-GG/FS.GG.Net main "materialize / kit-bump-shape" "materialize / kit-bump-mechanical"
expect "#1815: BOTH kit-bump contexts are producible on every PR, from the REAL reusable workflow" \
  0 "ok: every required context is producible" "$WK" "$RK" FS-GG/FS.GG.Net

# MUTATION — and it is the leg that proves the one above can fail. Delete the `bump-mechanical` job
# from the callee and the derivation must name the context that can no longer report. Without this,
# a fixture that asserted producibility of a context nothing produces would pass just as loudly.
WKG="$WORK/w-kit-gone"
protect "$WKG" FS-GG/FS.GG.Net main "materialize / kit-bump-shape" "materialize / kit-bump-mechanical"
python3 - "$REPO_ROOT/.github/workflows/kit-materialize.yml" \
           "$WKG/refs/main/kit-materialize.yml" <<'PY'
import os, re, sys
text = open(sys.argv[1], encoding="utf-8").read()
# The job block, from its `  bump-mechanical:` key to the next top-level job key.
cut = re.sub(r"^  bump-mechanical:\n(?:.*\n)*?(?=^  [a-z][a-z0-9-]*:$)", "", text, flags=re.M)
assert cut != text, "kit-materialize.yml has no `bump-mechanical:` job — fix the fixture, not this"
os.makedirs(os.path.dirname(sys.argv[2]), exist_ok=True)
open(sys.argv[2], "w", encoding="utf-8").write(cut)
PY
expect "#1815 MUTATION: deleting the bump-mechanical job makes the new context unproducible, and is caught" \
  1 "REQUIRES the status check 'materialize / kit-bump-mechanical'" \
  "$WKG" "$RK" FS-GG/FS.GG.Net
# ...and the SURVIVING context is still fine, so the finding is about the deleted job and not about
# the pair. A mutation that reddened both would not have located anything.
expect "#1815 MUTATION: and kit-bump-shape is untouched by it — the finding names one context, not two" \
  1 "1 required context(s)" "$WKG" "$RK" FS-GG/FS.GG.Net

# =============================================================================================
# 1b. THE REF THE PRODUCER CROSSES (.github#1783). The same real files, mutated one token.
#
# A deadlocked context is loud: nothing merges. THIS defect is silent — the context reports, the PR
# merges, and the verdict is a statement about when it ran. `FS.GG.SDD#724` passed on merged SHA
# 0376309 at 08:15Z and failed on byte-identical content at 08:21Z (.github#1584), which is what
# ADR-0067 §2 forbids and what nothing here detected until now.
#
# #1783 DECIDED that `@main` is accepted for these calls and is not pinned; the reasoning and its
# measurements are in docs/coordination/reusable-workflow-contract.md. What is NOT accepted is any
# other moving ref — the debugging branch left in a `uses:` line, which reads exactly like `@main` to
# every reviewer and to every other gate in this repo.
#
# The subject is FS.GG.Audio's REAL gate.yml and this repo's REAL lock-range-coherence.yml, against
# Audio's REAL required context — the same three files as leg 1, so a green here is a green over the
# fleet's actual shape and not over a hand-drawn one.
# =============================================================================================
usesref() {  # usesref <dest> <ref>  — Audio's real gate.yml with its hub call moved to <ref>
  python3 - "$FIX/audio-gate.yml" "$1" "$2" <<'PY'
import sys
src, dest, ref = sys.argv[1], sys.argv[2], sys.argv[3]
text = open(src, encoding="utf-8").read()
old = "lock-range-coherence.yml@main"
assert old in text, "the Audio fixture no longer calls lock-range-coherence.yml@main — fix the fixture"
open(dest, "w", encoding="utf-8").write(text.replace(old, f"lock-range-coherence.yml@{ref}"))
PY
}

# (a) CONTROL — `@main`, untouched. This is the fleet as it stands today, and it must be GREEN, or
#     the check would be reporting the decision itself as a defect in all seven receivers.
#     (Leg 1(b) renamed the callee's job in $W; put the real one back, or this leg would measure that
#     mutation instead of this one.)
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$W/refs/main/lock-range-coherence.yml"
expect "the DECIDED ref: Audio's real \`@main\` call carries a required verdict, and is accepted" \
  0 "ok: every required context is producible" "$W" "$R" FS-GG/FS.GG.Audio

# (b) THE MUTATION — one token, `main` -> `wip`. Nothing else in either repo changes. The context
#     still reports (so leg 1's assertion still passes over this tree), and the verdict is now a
#     function of a branch nobody gates.
RB="$WORK/r-audio-branch"; WB="$WORK/w-audio-branch"
mkdir -p "$RB/.github/workflows" "$WB/refs/wip"
usesref "$RB/.github/workflows/gate.yml" wip
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$WB/refs/wip/lock-range-coherence.yml"
protect "$WB" FS-GG/FS.GG.Audio main "Build + test (locked restore, net10.0, headless)" "lock-ranges / lock-ranges"
expect "MUTATION #1783: \`@main\` -> \`@wip\` in a receiver's caller is caught" \
  1 "REQUIRES the status check 'lock-ranges / lock-ranges'" "$WB" "$RB" FS-GG/FS.GG.Audio
expect "MUTATION: and it names the ref, not merely 'something is wrong'" \
  1 "tracks the ref 'wip'" "$WB" "$RB" FS-GG/FS.GG.Audio
expect "MUTATION: and it says WHY that is a defect — the verdict stops being about the tree" \
  1 "byte-identical content" "$WB" "$RB" FS-GG/FS.GG.Audio
expect "MUTATION: and it summarises it as its own defect, not as the deadlock" \
  1 "report a verdict that is not a function of the tree under test" "$WB" "$RB" FS-GG/FS.GG.Audio

# ...and it must NOT say the repo is deadlocked. It is not: every context reports. Sending the reader
# to branch protection for a defect that lives in one `uses:` line is how the wrong thing gets
# "fixed" — the reason this summary is built from the two counts rather than written once for both.
if grep -qF "this repo is deadlocked" <<<"$(run "$WB" "$RB" FS-GG/FS.GG.Audio || true)"; then
  bad "MUTATION: the summary wrongly calls a reporting repo deadlocked"
else
  ok "MUTATION: and it does NOT call the repo deadlocked — every context here reports"
fi

# (c) A TAG is a moving ref too — mutable, and unchecked after publication (.github#1784). It looks
#     pinned, which is the whole reason it may not pass for one.
RT="$WORK/r-audio-tag"; WTG="$WORK/w-audio-tag"
mkdir -p "$RT/.github/workflows" "$WTG/refs/v1"
usesref "$RT/.github/workflows/gate.yml" v1
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$WTG/refs/v1/lock-range-coherence.yml"
protect "$WTG" FS-GG/FS.GG.Audio main "Build + test (locked restore, net10.0, headless)" "lock-ranges / lock-ranges"
expect "a TAG is not a pin — mutable, unchecked after publish, and refused for a required verdict" \
  1 "tracks the ref 'v1'" "$WTG" "$RT" FS-GG/FS.GG.Audio

# (d) A 40-hex COMMIT is what ADR-0067 §2 asks for, and stays legal. #1783 declined to REQUIRE
#     pinning; it did not forbid it, and a receiver that pins must not be red for doing so.
SHA=0123456789abcdef0123456789abcdef01234567
RS="$WORK/r-audio-sha"; WS="$WORK/w-audio-sha"
mkdir -p "$RS/.github/workflows" "$WS/refs/$SHA"
usesref "$RS/.github/workflows/gate.yml" "$SHA"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$WS/refs/$SHA/lock-range-coherence.yml"
protect "$WS" FS-GG/FS.GG.Audio main "Build + test (locked restore, net10.0, headless)" "lock-ranges / lock-ranges"
expect "a 40-hex COMMIT pin is accepted — #1783 declined to require pinning, it did not forbid it" \
  0 "ok: every required context is producible" "$WS" "$RS" FS-GG/FS.GG.Audio

# (e) The subject is what is REQUIRED. A context on a strange ref that nothing requires deadlocks
#     nobody and moves no merge verdict, so it is not this gate's business — asserting over it would
#     be the gate crying wolf about a report.
WNR="$WORK/w-audio-notreq"; mkdir -p "$WNR/refs/wip"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$WNR/refs/wip/lock-range-coherence.yml"
protect "$WNR" FS-GG/FS.GG.Audio main "Build + test (locked restore, net10.0, headless)"
expect "a NON-required context on the same odd ref is not a finding — only required verdicts move merges" \
  0 "ok: every required context is producible" "$WNR" "$RB" FS-GG/FS.GG.Audio

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
# 3b. A `paths:`-FILTERED `pull_request` workflow does not report on an ARBITRARY PR (.github#1508).
#
# THE HOLE THIS CLOSES. "Triggers on `pull_request`" is a strictly WIDER claim than "reports on every
# pull request", and only the SECOND is what a required context needs. A path-filtered workflow
# satisfies the first and fails the second, so it scored GREEN here — in the gate whose entire purpose
# is "this required context can never report, and the repo is deadlocked".
#
# GitHub does NOT synthesise a neutral or skipped check run for a workflow its `paths:` filter
# excluded; it never creates the check run at all. Branch protection then holds the PR at "Expected —
# waiting for status to be reported" forever. So requiring a path-filtered context blocks every PR
# that touches none of those paths — normally MOST of them.
#
# Found while wiring the skill-union receiver caller for FS.GG.Governance#326. The documented caller in
# docs/coordination/skill-union-assertion.md was `paths:`-filtered to the three ADR-0011 skill roots
# AND told the receiver to make the resulting context required, and seven rostered receivers were
# queued behind that instruction. Per #1508: Governance reached the wiring step first and caught it,
# and SDD declined to arm the context although it held the admin rights to do so. Both were human
# judgement — nothing mechanical would have stopped the other five, which is why this leg exists.
# =============================================================================================
# THE REGRESSION, in the exact shape the doc used to prescribe: the skill-union receiver caller.
RFP="$WORK/r-filtered"; WFP="$WORK/w-filtered"
mkwf "$RFP/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths:
      - ".claude/skills/**"
      - ".codex/skills/**"
      - ".agents/skills/**"
      - ".github/workflows/skill-union.yml"
  push:
    branches: [main]
  workflow_dispatch:
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
protect "$WFP" FS-GG/R main "skill-union"
expect "REGRESSION #1508: a required context whose only producer is \`paths:\`-filtered is a FINDING" \
  1 "REQUIRES the status check 'skill-union'" "$WFP" "$RFP" FS-GG/R
expect "REGRESSION #1508: ...and it blames the FILTER, not a missing or misnamed workflow" \
  1 "PATH-FILTERED" "$WFP" "$RFP" FS-GG/R
# The needle is the DERIVED `on.<event>.<key>:` locator, not the workflow filename — the filename also
# appears inside the filter's own entry list here, so grepping for it would pass even if the message
# never named the producer at all. (A vacuous needle is the trap this file's header warns about.)
expect "REGRESSION #1508: ...and it names the filtered workflow, the EVENT and the KEY" \
  1 "skill-union.yml \`on.pull_request.paths:\`" "$WFP" "$RFP" FS-GG/R
expect "REGRESSION #1508: ...and it states the GitHub mechanic — a filtered required check is never created, not skipped" \
  1 "GitHub does not skip a filtered required check" "$WFP" "$RFP" FS-GG/R
expect "REGRESSION #1508: ...and it names the consequence: the PR waits for a status that never arrives" \
  1 "waiting for status to be reported" "$WFP" "$RFP" FS-GG/R

# THE FIX THE DOC NOW PRESCRIBES — the same caller with the `pull_request` filter dropped. This is the
# other half of the regression: the gate must not merely fail louder, it must PASS the repaired shape.
RFU="$WORK/r-unfiltered"
mkwf "$RFU/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
  push:
    branches: [main]
    paths:
      - ".claude/skills/**"
  workflow_dispatch:
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "the REPAIRED caller — \`pull_request\` unfiltered — is green, and a \`push\` filter is irrelevant to a PR" \
  0 "ok: every required context is producible" "$WFP" "$RFU" FS-GG/R

# `paths-ignore:` is the same defect wearing the other key. A PR touching only ignored paths gets no
# check run, so the context still cannot be required.
RFI="$WORK/r-paths-ignore"
mkwf "$RFI/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths-ignore:
      - "docs/**"
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "a \`paths-ignore:\` filter is the same deadlock — a docs-only PR never gets the check run" \
  1 "PATH-FILTERED" "$WFP" "$RFI" FS-GG/R
# ...and the message must name the OPPOSITE excluded set. `paths:` withholds the check from a PR that
# touches NONE of the paths; `paths-ignore:` withholds it from one that touches ONLY them. One message
# written for both tells half its readers the exact inverse of which PRs deadlock.
expect "...and it names the set \`paths-ignore:\` excludes, which is the INVERSE of \`paths:\`'s" \
  1 "a pull request that touches only those paths" "$WFP" "$RFI" FS-GG/R
expect "...while the \`paths:\` leg names its own, opposite, excluded set" \
  1 "a pull request that touches none of those paths" "$WFP" "$RFP" FS-GG/R

# A filtered producer must NOT condemn a context that some OTHER workflow reports on every PR. The
# question is whether the context always reports, not whether some producer of it is filtered — and
# crying wolf here would be a confident, wrong "your repo is deadlocked".
RFB="$WORK/r-filtered-plus-always"
mkwf "$RFB/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: [".claude/skills/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
mkwf "$RFB/.github/workflows/always.yml" <<'YML'
name: always
on: { pull_request: }
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "a context ALSO produced by an unfiltered workflow is fine — a filtered twin does not condemn it" \
  0 "ok: every required context is producible" "$WFP" "$RFB" FS-GG/R

# A `paths:` filter that matches EVERY changed file excludes no pull request, so it is not this defect.
# Reporting it would be the gate crying wolf — the same "confident, alarming, WRONG finding" the
# expression and matrix legs above refuse to produce.
RFW="$WORK/r-paths-universal"
mkwf "$RFW/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: ["**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "a \`paths: [\"**\"]\` filter excludes no PR, so it is NOT reported — the gate does not cry wolf" \
  0 "ok: every required context is producible" "$WFP" "$RFW" FS-GG/R

# ...but a universal entry that also SUBTRACTS is not universal. GitHub supports `!`-negated entries,
# and this is precisely the shape a receiver reaches for when told to widen its filter — so blessing
# it would score the #1508 deadlock green in the most likely way to arrive at it.
RFNEG="$WORK/r-paths-negated"
mkwf "$RFNEG/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths:
      - "**"
      - "!docs/**"
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "a \`!\`-negated entry SUBTRACTS, so \`paths: [\"**\", \"!docs/**\"]\` is still a deadlock for a docs-only PR" \
  1 "REQUIRES the status check 'skill-union'" "$WFP" "$RFNEG" FS-GG/R

# `**/*` is NOT universal: `*` does not span `/`, so it requires at least one slash and misses a
# root-level file. Treating it as universal would leave a README-only PR with no check run.
RFSS="$WORK/r-paths-starslash"
mkwf "$RFSS/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: ["**/*"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "\`paths: [\"**/*\"]\` is NOT universal — it misses a root-level file, so it is still a finding" \
  1 "REQUIRES the status check 'skill-union'" "$WFP" "$RFSS" FS-GG/R

# GITHUB'S OWN DOCUMENTED REMEDY ("Handling skipped but required checks") must be green. A real job
# filtered `paths: P` beside a no-op job reporting the SAME context filtered `paths-ignore: P` covers
# every pull request between them — no single producer is unfiltered, and the context always reports.
# Calling that a deadlock would be the confident, alarming, WRONG finding this file refuses elsewhere.
RFC="$WORK/r-complementary"
mkwf "$RFC/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: [".claude/skills/**", ".codex/skills/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'bash scripts/skill-union-assert.sh' }]
YML
mkwf "$RFC/.github/workflows/skill-union-noop.yml" <<'YML'
name: skill-union (no-op)
on:
  pull_request:
    paths-ignore: [".claude/skills/**", ".codex/skills/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'echo "no skill change; required check satisfied"' }]
YML
expect "GitHub's documented complementary twin (\`paths: P\` + \`paths-ignore: P\`) covers every PR — green" \
  0 "ok: every required context is producible" "$WFP" "$RFC" FS-GG/R

# ...and the complement is matched as a SET, so declaration order must not change the verdict.
RFC2="$WORK/r-complementary-reordered"
mkwf "$RFC2/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: [".codex/skills/**", ".claude/skills/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
mkwf "$RFC2/.github/workflows/skill-union-noop.yml" <<'YML'
name: skill-union (no-op)
on:
  pull_request:
    paths-ignore: [".claude/skills/**", ".codex/skills/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "...and the complement is a SET comparison — reordering the twin's entries is still green" \
  0 "ok: every required context is producible" "$WFP" "$RFC2" FS-GG/R

# Two filtered producers that are NOT complements: whether they jointly cover every PR is glob-set
# coverage this gate cannot compute. Exit 3 — a no-verdict, not a guess in either direction.
RFU2="$WORK/r-two-filtered"
mkwf "$RFU2/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: [".claude/skills/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
mkwf "$RFU2/.github/workflows/skill-union-other.yml" <<'YML'
name: skill-union (other)
on:
  pull_request:
    paths: ["src/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "two filtered producers that are not complements is exit 3 — joint coverage is not computable" \
  3 "no verdict" "$WFP" "$RFU2" FS-GG/R
expect "...and it says WHY it refused rather than guessing green or red" \
  3 "glob-set coverage" "$WFP" "$RFU2" FS-GG/R

# A filter shape the gate cannot read is a no-verdict too, never a silent green. `repos-audit.sh`
# reads an empty `paths:` as matching NOTHING; guessing the opposite here would be a vacuous pass.
RFE="$WORK/r-paths-empty"
mkwf "$RFE/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: []
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "an EMPTY \`paths:\` is a shape the gate cannot model — exit 3, never a silent green" \
  3 "a shape this gate cannot model" "$WFP" "$RFE" FS-GG/R

# `pull_request_target` puts a check run on a PR exactly as `pull_request` does, so a filter on it
# starves a required context identically — and the message must name the event it actually found.
RFPT="$WORK/r-pr-target"
mkwf "$RFPT/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request_target:
    paths: [".claude/skills/**"]
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "a filtered \`pull_request_target\` is the same deadlock, and the message names THAT event" \
  1 "\`on.pull_request_target.paths:\`" "$WFP" "$RFPT" FS-GG/R

# Declaring BOTH PR events, with only one filtered, reports on every PR — the unfiltered one suffices.
RFBOTH="$WORK/r-both-events"
mkwf "$RFBOTH/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: [".claude/skills/**"]
  pull_request_target:
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
expect "one unfiltered PR event alongside a filtered one is enough — not a finding" \
  0 "ok: every required context is producible" "$WFP" "$RFBOTH" FS-GG/R

# The filtered context is still DERIVED correctly — including through a reusable call, which is the
# shape every skill-union receiver actually has (`skill-union / skill-union`).
RFN="$WORK/r-filtered-nested"; WFN="$WORK/w-filtered-nested"; mkdir -p "$WFN/refs/main"
mkwf "$RFN/.github/workflows/skill-union.yml" <<'YML'
name: skill-union
on:
  pull_request:
    paths: [".claude/skills/**"]
jobs:
  skill-union:
    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main
YML
mkwf "$WFN/refs/main/skill-union-assert.yml" <<'YML'
name: skill-union-assert
on: { workflow_call: }
jobs:
  skill-union:
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
protect "$WFN" FS-GG/R main "skill-union / skill-union"
expect "the nested receiver context \`skill-union / skill-union\` is derived AND caught when filtered" \
  1 "REQUIRES the status check 'skill-union / skill-union'" "$WFN" "$RFN" FS-GG/R

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
# 5. RULESETS — the OTHER store, and the vacuous green that reading one store produced (#574).
#
# GitHub keeps required status checks in TWO places. `branches/<b>/protection` (classic) does not
# report ruleset rules; `rules/branches/<b>` (rulesets) does not report classic protection. A branch
# may be governed by either, both, or neither, and GitHub enforces BOTH.
#
# This gate read classic protection alone and took its 404 to mean "not protected, requires
# nothing". FS.GG.Governance is protected by a repository RULESET requiring five status checks and
# answers 404 on the classic endpoint — so the gate printed `requires NO status checks` and exited
# 0 over a fully-protected, deadlockable repo, holding an ADMIN token. Reading the wrong store, not
# a missing credential. Exactly the #266 fail-open the gate exists to prevent, inside the gate.
# =============================================================================================
# THE REGRESSION. A deadlocking context, required by a ruleset, with NO classic protection at all.
# Before the fix this was exit 0 and "requires NO status checks".
RRS="$WORK/r-ruleset"; WRS="$WORK/w-ruleset"
mkwf "$RRS/.github/workflows/g.yml" <<'YML'
name: g
on: [pull_request]
jobs:
  build:
    name: Deterministic gate
    runs-on: ubuntu-latest
    steps: [{ run: 'true' }]
YML
ruleset "$WRS" FS-GG/R main "Deterministic gate" "lock-ranges / renamed-away"
expect "a RULESET-required context that can never report is a FINDING — not a vacuous green (#574)" \
  1 "REQUIRES the status check 'lock-ranges / renamed-away'" "$WRS" "$RRS" FS-GG/R

# ...and the same world with the ruleset SATISFIED is green — the fix must not merely fail louder.
WRS2="$WORK/w-ruleset-ok"
ruleset "$WRS2" FS-GG/R main "Deterministic gate"
expect "a RULESET whose every required context IS producible is green" \
  0 "every required context is producible" "$WRS2" "$RRS" FS-GG/R

# BOTH STORES AT ONCE. They stack, so the required set is their UNION — a repo can be deadlocked by
# either one, and honouring only the store that happens to be non-empty would still be half-blind.
WBOTH="$WORK/w-both"
protect "$WBOTH" FS-GG/R main "Deterministic gate"
ruleset "$WBOTH" FS-GG/R main "gone-from-the-ruleset"
expect "classic and ruleset requirements are UNIONED — a ruleset-only deadlock is caught behind a satisfiable classic set" \
  1 "REQUIRES the status check 'gone-from-the-ruleset'" "$WBOTH" "$RRS" FS-GG/R

WBOTH2="$WORK/w-both-2"
protect "$WBOTH2" FS-GG/R main "gone-from-the-classic-set"
ruleset "$WBOTH2" FS-GG/R main "Deterministic gate"
expect "...and symmetrically: a CLASSIC-only deadlock is caught behind a satisfiable ruleset" \
  1 "REQUIRES the status check 'gone-from-the-classic-set'" "$WBOTH2" "$RRS" FS-GG/R

# TRULY unprotected: BOTH stores say nothing. Only then is "requires nothing" the truth — and it
# must say it checked both, so the next reader cannot mistake it for the old half-read.
WNONE="$WORK/w-neither"; mkdir -p "$WNONE/protection" "$WNONE/rules"
expect "a branch neither store protects requires nothing — and it says it checked BOTH" \
  0 "Checked BOTH stores" "$WNONE" "$RRS" FS-GG/R

# FAIL CLOSED on the rules endpoint, exactly as on the classic one. A 403 here means we can see only
# half of what protects the branch, and a half-read is not a verdict.
WRF="$WORK/w-rules-403"; mkdir -p "$WRF/protection" "$WRF/rules"
: > "$WRF/rules/FS-GG__R__main.forbidden"
expect "a 403 on the RULES endpoint is exit 3 — a half-read of what protects the branch is not a verdict" \
  3 "half-read is not a verdict" "$WRF" "$RRS" FS-GG/R

# A 404 on the rules endpoint is NOT "no rules" — the real endpoint answers `[]` for that. It means
# no such repo/branch, and inferring "unprotected" from it would rebuild the very fail-open above.
WRN="$WORK/w-rules-404"; mkdir -p "$WRN/protection" "$WRN/rules"
: > "$WRN/rules/FS-GG__R__main.notfound"
expect "a 404 on the RULES endpoint is exit 3 — it means 'no such branch', never 'no rules'" \
  3 "Refusing to infer that the branch is unprotected" "$WRN" "$RRS" FS-GG/R

# A rate limit on the rules endpoint is RETRYABLE — never green, never a finding.
WRU="$WORK/w-rules-500"; mkdir -p "$WRU/protection" "$WRU/rules"
: > "$WRU/rules/FS-GG__R__main.unreachable"
expect "an unreachable RULES endpoint is exit 2 (RETRYABLE) — not green, not a finding" \
  2 "no verdict" "$WRU" "$RRS" FS-GG/R

# A 403 on CLASSIC is exit 3 even when the repo is RULESET-protected and the rules ARE readable.
# `rules/branches/<b>` needs only `metadata: read`, so it is tempting to think a non-admin token can
# audit a ruleset repo. It cannot: a 403 does not say "there is no classic protection", it says "I
# cannot see whether there is" — and the required set is the UNION, so the union is unknowable.
WRO="$WORK/w-rules-only-403"; mkdir -p "$WRO/protection" "$WRO/rules"
: > "$WRO/protection/FS-GG__R__main.forbidden"
ruleset "$WRO" FS-GG/R main "Deterministic gate"
expect "a readable RULESET does not rescue an unreadable CLASSIC store — the union is unknowable, so exit 3" \
  3 "a half-read is not a verdict" "$WRO" "$RRS" FS-GG/R

# The same context required by BOTH stores is ONE requirement, not two. A ruleset routinely omits
# `integration_id` (all five of Governance's real checks do) while classic sets `app_id`, so keying
# the dedup on the pair would let one context survive twice — and be reported as two deadlocks.
WDUP="$WORK/w-dup"
protect "$WDUP" FS-GG/R main "Deterministic gate"
ruleset "$WDUP" FS-GG/R main "Deterministic gate"          # same context, unattributed
expect "a context required by BOTH stores is audited ONCE, not twice" \
  0 "1 audited" "$WDUP" "$RRS" FS-GG/R

WDUP2="$WORK/w-dup-bad"
protect "$WDUP2" FS-GG/R main "vanished"
ruleset "$WDUP2" FS-GG/R main "vanished"
# The summary counts the two findings separately since #1783, so this asserts the whole sentence:
# ONE deadlock finding, ONE audited context, and no ref finding invented alongside it.
expect "...and an unproducible context required by both is ONE finding, not two" \
  1 "1 required context(s) can never report — this repo is deadlocked, or will be on its next pull request. Of 1 audited." \
  "$WDUP2" "$RRS" FS-GG/R

# A ruleset check with no readable `context` is NO VERDICT — never a confident `REQUIRES ''`.
WNC="$WORK/w-nameless"; mkdir -p "$WNC/protection" "$WNC/rules"
printf '[{"type":"required_status_checks","parameters":{"required_status_checks":[{"integration_id":15368}]}}]' \
  > "$WNC/rules/FS-GG__R__main.json"
expect "a required check with no readable name is exit 3 — the gate does not guess at what a repo requires" \
  3 "Refusing to guess at a required check's name" "$WNC" "$RRS" FS-GG/R

# The rules payload must be a LIST. Anything else and we do not know what protects the branch.
WBADJ="$WORK/w-rules-notlist"; mkdir -p "$WBADJ/protection" "$WBADJ/rules"
printf '{"message":"Not Found"}' > "$WBADJ/rules/FS-GG__R__main.json"
expect "a rules payload that is not a list is exit 3 — never 'no rules'" \
  3 "Refusing to guess what protects this branch" "$WBADJ" "$RRS" FS-GG/R

# =============================================================================================
# 5b. THE SAVED-PAYLOAD FLAGS. A saved payload for ONE store is a HALF-WORLD — and handing the gate
# half a world is exactly how it came to report green over a protected repo. Both, or neither.
# =============================================================================================
SP="$WORK/saved"; mkdir -p "$SP"
printf '{"required_status_checks":{"strict":false,"checks":[{"context":"Deterministic gate","app_id":15368}]}}' > "$SP/prot.json"
printf '[{"type":"required_status_checks","parameters":{"required_status_checks":[{"context":"gone"}]}}]' > "$SP/rules.json"
printf '[]' > "$SP/norules.json"

expect "--protection WITHOUT --rules is refused — a saved half-world is not a verdict" \
  3 "describes half a world" "$WNONE" "$RRS" FS-GG/R --protection "$SP/prot.json"
expect "--rules WITHOUT --protection is refused too — the refusal is symmetric" \
  3 "describes half a world" "$WNONE" "$RRS" FS-GG/R --rules "$SP/rules.json"

# Both saved: a complete offline world, and NO API call is made (the stub would 404 the rules read
# if it were reached, since this world has no rules/ dir — so a pass here proves it was not).
expect "BOTH saved payloads audit fully offline, unioning the two stores" \
  1 "REQUIRES the status check 'gone'" "$WNONE" "$RRS" FS-GG/R \
  --protection "$SP/prot.json" --rules "$SP/rules.json"
expect "...and both saved with an EMPTY ruleset list is the classic-only world, still audited" \
  0 "1 audited" "$WNONE" "$RRS" FS-GG/R --protection "$SP/prot.json" --rules "$SP/norules.json"

expect "an unreadable saved rules payload is exit 3, not a crash" \
  3 "cannot read the saved rules payload" "$WNONE" "$RRS" FS-GG/R \
  --protection "$SP/prot.json" --rules "$SP/nonexistent.json"

# =============================================================================================
# 6. WHY THIS TOOL IS NOT WIRED TO A WORKFLOW — and the regression guard that keeps it that way.
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
