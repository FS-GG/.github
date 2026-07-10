#!/usr/bin/env bash
# Fixture for .github/workflows/touch-set-drift.yml — the advisory touch-set drift gate (ADR-0027 §8).
#
# Proves the gate distinguishes "the verdict is: nothing to check" from "I could not compute a
# verdict" (.github#318). The four real verdicts classify as themselves; a verify-paths that dies
# (non-zero rc) or returns quietly without printing an `FSGG-PATHS *` marker fails the job instead of
# reporting `skip` — which passes green AND deletes the sticky drift comment.
#
# The classifier under test is the workflow's OWN `run:` block, extracted from the YAML rather than
# retyped here: a copy would keep passing after someone edits the shipped gate. `verify-paths` is
# stubbed (STUB_OUT/STUB_RC), so no network and no repo state. Mirrors the other fixtures.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
WORKFLOW="$REPO_ROOT/.github/workflows/touch-set-drift.yml"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/touch-set-drift-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "touch-set-drift fixture — work='$WORK'"

# --- extract the classifier from the shipped workflow -------------------------------------------
# `${{ … }}` is Actions template syntax, not shell; the runner substitutes it before bash ever sees
# it. Do the same, with values the stub ignores anyway.
python3 - "$WORKFLOW" "$WORK/check.sh" <<'PY'
import sys, re, yaml
wf = yaml.safe_load(open(sys.argv[1]).read())
steps = wf["jobs"]["drift"]["steps"]
run = next(s["run"] for s in steps if s.get("id") == "check")
subs = {"github.event.number": "1", "github.repository": "FS-GG/.github"}
run = re.sub(r"\$\{\{\s*(.*?)\s*\}\}", lambda m: subs.get(m.group(1), ""), run)
open(sys.argv[2], "w").write(run)
PY
[ -s "$WORK/check.sh" ] && ok "extracted the check step's run block from the workflow" \
  || { bad "extract check step" "empty"; exit 1; }

# The stub stands in for `bash scripts/fsgg-coord verify-paths …`, which the run block invokes by
# relative path — so the fixture executes from a fake root carrying only that one file.
mkdir -p "$WORK/root/scripts"
cat > "$WORK/root/scripts/fsgg-coord" <<'STUB'
#!/usr/bin/env bash
[ -n "${STUB_OUT:-}" ] && printf '%s\n' "$STUB_OUT"
exit "${STUB_RC:-0}"
STUB

# run_gate <stub-stdout> <stub-rc> -> rc of the gate; VERDICT set from its $GITHUB_OUTPUT
run_gate() {
  local out rc=0
  : > "$WORK/gh_output"; : > "$WORK/gh_summary"
  out="$(cd "$WORK/root" && STUB_OUT="$1" STUB_RC="$2" \
         GITHUB_OUTPUT="$WORK/gh_output" GITHUB_STEP_SUMMARY="$WORK/gh_summary" \
         bash "$WORK/check.sh" 2>&1)" || rc=$?
  GATE_OUT="$out"
  VERDICT="$(sed -n 's/^verdict=//p' "$WORK/gh_output")"
  return "$rc"
}

# expect_verdict <name> <stub-stdout> <stub-rc> <want-verdict>
expect_verdict() {
  local n="$1" want="$4" rc=0
  run_gate "$2" "$3" || rc=$?
  if [ "$rc" -eq 0 ] && [ "$VERDICT" = "$want" ]; then ok "$n"
  else bad "$n" "want rc=0 verdict='$want'; got rc=$rc verdict='$VERDICT'"$'\n'"$GATE_OUT"; fi
}

# expect_closed <name> <stub-stdout> <stub-rc>
# The gate must fail the job, and must NOT emit a verdict — `skip` here would delete the sticky
# comment, and `ok` would stamp ✅ on a PR whose touch-set nobody looked at.
expect_closed() {
  local n="$1" rc=0
  run_gate "$2" "$3" || rc=$?
  if [ "$rc" -eq 0 ]; then bad "$n" "gate passed green; verdict='$VERDICT'"$'\n'"$GATE_OUT"
  elif [ -n "$VERDICT" ]; then bad "$n" "gate failed but still emitted verdict='$VERDICT'"
  else ok "$n"; fi
}

# --- the four real verdicts still classify as themselves -----------------------------------------
expect_verdict "OK classifies as ok"           'FSGG-PATHS OK — PR #1 stays inside the touch-set.'  0 ok
expect_verdict "DRIFT classifies as drift"     'FSGG-PATHS DRIFT — PR #1 touches 2 file(s) outside' 0 drift
expect_verdict "INVALID classifies as invalid" 'FSGG-PATHS INVALID — unmatchable token(s): src/**'  0 invalid
expect_verdict "SKIP classifies as skip"       'FSGG-PATHS SKIP — #1 declares no Paths: touch-set'  0 skip

# INVALID must be matched before the OK/SKIP arms, never buried (.github#273). `--warn` prints the
# grammar hint on a second line, which contains the word "paths" — assert the arm order holds anyway.
expect_verdict "INVALID wins over a trailing grammar hint" \
  'FSGG-PATHS INVALID — unmatchable token(s): src/**
  paths are exact files, directory prefixes, or a trailing /**' 0 invalid

# --- fail closed: verify-paths never reached a verdict (#318) -------------------------------------
# Under --warn every real verdict exits 0, so a non-zero rc means the run died. The old gate `|| true`d
# it away and fell through the `else` to `skip`.
expect_closed "a crash (rc=1, stack trace, no marker) fails the job" \
  'Traceback (most recent call last):
  gh: HTTP 502 Bad Gateway (repos/FS-GG/.github/pulls/1/files)' 1

expect_closed "an rc=2 die() with no marker fails the job" \
  'fsgg-coord: verify-paths: --repo owner/repo required (not inside a GitHub checkout).' 2

# `set -e` inside fsgg-coord can kill it mid-run with nothing on stdout at all.
expect_closed "a silent non-zero exit fails the job" '' 1

# rc=0 but no marker: it "ran" and said nothing. Not a verdict; not a pass.
expect_closed "rc=0 with no FSGG-PATHS marker fails the job" \
  'warning: gh auth token is read-only; skipping' 0

expect_closed "rc=0 with empty output fails the job" '' 0

# --- the diagnostic survives a fail-closed exit ---------------------------------------------------
run_gate 'gh: HTTP 502 Bad Gateway' 1 || true
grep -q 'HTTP 502' "$WORK/gh_summary" \
  && ok "the step summary records verify-paths' output even when the gate fails closed" \
  || bad "summary on fail-closed" "$(cat "$WORK/gh_summary")"

# --- the comment step must never run on a fail-closed check ---------------------------------------
# No `if:` on the comment step means `success()`, so it is skipped when the check fails. An
# `if: always()` there would restore the bug from the other side: `skip`'s branch deletes the sticky
# comment, and VERDICT is empty when the check died — which is not "skip", but does not match "drift"
# or "invalid" either, so it would render the ✅ else-branch on an unchecked PR.
python3 - "$WORKFLOW" <<'PY' && ok "the comment step is guarded by the implied success()" \
  || bad "comment step guard" "it must not carry an if: that runs on failure"
import sys, yaml
wf = yaml.safe_load(open(sys.argv[1]).read())
step = next(s for s in wf["jobs"]["drift"]["steps"] if s.get("name") == "Comment the verdict")
cond = str(step.get("if", "")).lower()
sys.exit(0 if not cond or ("success()" in cond and "always()" not in cond) else 1)
PY

echo "touch-set-drift fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::touch-set-drift fixture FAILED"; exit 1; }
echo "touch-set-drift fixture — OK"
