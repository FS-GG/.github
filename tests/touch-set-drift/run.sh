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
# it. Do the same, with values the stub ignores anyway — and REFUSE an expression we have no value
# for, rather than rewriting it to "": a substitution nobody chose would silently make this fixture
# exercise a different program than the one CI runs.
extract_err="$(python3 - "$WORKFLOW" "$WORK/check.sh" 2>&1 <<'PY'
import sys, re, yaml
wf = yaml.safe_load(open(sys.argv[1]).read())
steps = wf["jobs"]["drift"]["steps"]
run = next((s["run"] for s in steps if s.get("id") == "check"), None)
if run is None:
    sys.exit("no step with `id: check` in jobs.drift.steps — did the step id change?")
subs = {"github.event.number": "1", "github.repository": "FS-GG/.github"}
unknown = [e for e in re.findall(r"\$\{\{\s*(.*?)\s*\}\}", run) if e not in subs]
if unknown:
    sys.exit("run block uses ${{ %s }} — add a fixture value for it" % " }}, ${{ ".join(unknown))
# Strip comments before looking for the flag: this block DISCUSSES `--warn` at length, and a guard
# that a comment can satisfy guards nothing.
code = "\n".join(l for l in run.splitlines() if not l.lstrip().startswith("#"))
if "--warn" not in code:
    sys.exit("the gate no longer passes --warn: verify-paths would exit non-zero on a genuine DRIFT "
             "verdict, and the rc guard would fail the job — making the advisory check a merge "
             "blocker, against ADR-0021.")
run = re.sub(r"\$\{\{\s*(.*?)\s*\}\}", lambda m: subs[m.group(1)], run)
open(sys.argv[2], "w").write(run)
PY
)" && ok "extracted the check step's run block (it still passes --warn)" \
  || { bad "extract the check step's run block" "$extract_err"; exit 1; }

# --- the gate and the real client must agree on the marker vocabulary ---------------------------
# Everything below this line stubs verify-paths, which means nothing below it can notice that the
# REAL client stopped printing the markers the gate greps for. That is the #266 shape one level up:
# a fixture reporting green on a subject it never read. So read it here, in both directions.
#
#   gate ⊄ client — the gate greps a marker nobody emits: that verdict is dead, and its output falls
#                   through to the fail-closed `else`.
#   client ⊄ gate — the client emits a marker the gate does not classify: same fall-through, on a
#                   verdict that was supposed to be handled.
#
# This is what earns `scripts/fsgg-coord-bash` its place in the selftest's `paths:` trigger.
# (ADR-0040 D.2 cut `scripts/fsgg-coord` to the engine shim, which prints no markers; the bash client
# that emits FSGG-PATHS lives at `scripts/fsgg-coord-bash` until D.4. The ENGINE's verify-paths markers
# are held to the gate's vocabulary by the D.1/shell corpus case 23, so this stays covered end to end.)
markers_err="$(python3 - "$WORK/check.sh" "$REPO_ROOT/scripts/fsgg-coord-bash" 2>&1 <<'PY'
import sys, re
gate   = set(re.findall(r"grep -q '(FSGG-PATHS [A-Z]+)'", open(sys.argv[1]).read()))
client = set(re.findall(r"(FSGG-PATHS [A-Z]+)", open(sys.argv[2]).read()))
if not gate:   sys.exit("the gate greps for no FSGG-PATHS markers at all")
if not client: sys.exit("scripts/fsgg-coord-bash prints no FSGG-PATHS markers at all")
if gate != client:
    sys.exit("gate greps %s; client prints %s; only-in-gate=%s only-in-client=%s" % (
        sorted(gate), sorted(client), sorted(gate - client) or "-", sorted(client - gate) or "-"))
print(" ".join(sorted(m.split()[-1] for m in gate)))
PY
)" && ok "the gate greps exactly the markers scripts/fsgg-coord prints ($markers_err)" \
  || bad "gate/client marker vocabulary disagrees" "$markers_err"

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
step = next((s for s in wf["jobs"]["drift"]["steps"] if s.get("name") == "Comment the verdict"), None)
if step is None:
    sys.exit("no step named 'Comment the verdict' — did it get renamed?")
cond = str(step.get("if", "")).lower()
sys.exit(0 if not cond or ("success()" in cond and "always()" not in cond) else 1)
PY

echo "touch-set-drift fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::touch-set-drift fixture FAILED"; exit 1; }
echo "touch-set-drift fixture — OK"
